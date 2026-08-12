using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Bounds tests for the conservative-local cost estimator. We do not
/// assert exact USD values against a tokenizer — the estimator is a
/// rough character-length-based model on purpose. Tests pin
/// non-negativity, ordering ("more text costs more"), and the
/// null/empty edge cases.
/// </summary>
public class OpenAICostEstimatorTests
{
    [Fact]
    public void EstimateChatCostUsd_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0m, OpenAICostEstimator.EstimateChatCostUsd(null, null));
        Assert.Equal(0m, OpenAICostEstimator.EstimateChatCostUsd(string.Empty, string.Empty));
    }

    [Fact]
    public void EstimateChatCostUsd_LongerOutputCostsMore()
    {
        var shortCost = OpenAICostEstimator.EstimateChatCostUsd("hello", "hi");
        var longCost = OpenAICostEstimator.EstimateChatCostUsd(
            "hello", new string('a', 4000));

        Assert.True(longCost > shortCost,
            $"longer output should cost more: short={shortCost} long={longCost}");
    }

    [Fact]
    public void EstimateChatCostUsd_NonNegative()
    {
        var cost = OpenAICostEstimator.EstimateChatCostUsd(
            "Կռահի՞ր, թե ինչ եմ մտածել", "Փոքրիկ սկյուռիկը վազեց.");
        Assert.True(cost >= 0m, $"chat cost should be non-negative, got {cost}");
    }

    [Fact]
    public void EstimateWhisperCostUsd_ZeroOrNegativeBytes_ReturnsZero()
    {
        Assert.Equal(0m, OpenAICostEstimator.EstimateWhisperCostUsd(0));
        Assert.Equal(0m, OpenAICostEstimator.EstimateWhisperCostUsd(-1));
    }

    [Fact]
    public void EstimateWhisperCostUsd_MoreAudioCostsMore()
    {
        // 16 kHz mono 16-bit PCM → 32 000 bytes per second.
        var oneSecondBytes = 16000L * 2L;
        var tenSecondBytes = 10L * oneSecondBytes;
        var costOne = OpenAICostEstimator.EstimateWhisperCostUsd(oneSecondBytes);
        var costTen = OpenAICostEstimator.EstimateWhisperCostUsd(tenSecondBytes);

        Assert.True(costOne > 0m, "1s of audio should cost something");
        Assert.True(costTen > costOne, $"10s should cost more than 1s: one={costOne} ten={costTen}");
    }

    [Fact]
    public void EstimateTtsCostUsd_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0m, OpenAICostEstimator.EstimateTtsCostUsd(null));
        Assert.Equal(0m, OpenAICostEstimator.EstimateTtsCostUsd(string.Empty));
    }

    [Fact]
    public void EstimateTtsCostUsd_LongerTextCostsMore()
    {
        var shortCost = OpenAICostEstimator.EstimateTtsCostUsd("hi");
        var longCost = OpenAICostEstimator.EstimateTtsCostUsd(new string('a', 4000));

        Assert.True(longCost > shortCost,
            $"longer text should cost more: short={shortCost} long={longCost}");
    }

    [Fact]
    public void Constants_AreConservativeAndPositive()
    {
        // Documentation-pin: the constants exist and are positive. The
        // comment on each constant says they are "conservative local"
        // estimates; if a future edit makes any of them zero or
        // negative this test fires distinctly.
        Assert.True(OpenAICostEstimator.ChatInputUsdPerMillionTokens > 0m);
        Assert.True(OpenAICostEstimator.ChatOutputUsdPerMillionTokens > 0m);
        Assert.True(OpenAICostEstimator.WhisperUsdPerMinute > 0m);
        Assert.True(OpenAICostEstimator.TtsUsdPerMillionChars > 0m);
        Assert.True(OpenAICostEstimator.CharsPerTokenEstimate > 0);
    }

    [Fact]
    public void ConfigureChatRates_ChangesEstimate_AndDefaultsMatchShippedOpenAIRates()
    {
        // Tier-1 fix pin (2026-08-06): the chat rates are configurable
        // so the daily cap prices the ACTIVE provider, not gpt-4o
        // fiction. Two halves in one test (shared static state — keep
        // the restore in finally so parallel classes never see stray
        // rates):
        //   1. The shipped defaults are byte-identical to the historic
        //      constants (2.50 / 10.00) — openai deployments unchanged.
        //   2. Configuring gemini-style rates changes the estimate
        //      proportionally.
        var user = new string('a', 4000);      // 1000 tokens
        var assistant = new string('b', 8000); // 2000 tokens
        try
        {
            Assert.Equal(2.50m, OpenAICostEstimator.DefaultChatInputUsdPerMillionTokens);
            Assert.Equal(10.00m, OpenAICostEstimator.DefaultChatOutputUsdPerMillionTokens);

            OpenAICostEstimator.ConfigureChatRates(
                OpenAICostEstimator.DefaultChatInputUsdPerMillionTokens,
                OpenAICostEstimator.DefaultChatOutputUsdPerMillionTokens);
            var openAiCost = OpenAICostEstimator.EstimateChatCostUsd(user, assistant);
            // 1000/1M * 2.50 + 2000/1M * 10.00
            Assert.Equal(0.0025m + 0.02m, openAiCost);

            OpenAICostEstimator.ConfigureChatRates(0.50m, 3.00m);
            var geminiCost = OpenAICostEstimator.EstimateChatCostUsd(user, assistant);
            // 1000/1M * 0.50 + 2000/1M * 3.00
            Assert.Equal(0.0005m + 0.006m, geminiCost);
            Assert.True(geminiCost < openAiCost);
        }
        finally
        {
            OpenAICostEstimator.ConfigureChatRates(
                OpenAICostEstimator.DefaultChatInputUsdPerMillionTokens,
                OpenAICostEstimator.DefaultChatOutputUsdPerMillionTokens);
        }
    }

    [Fact]
    public void ConfigureChatRates_IgnoresNonPositiveRates()
    {
        // Zero/negative rates would price every call at $0 and the cap
        // would never fire — the setter must refuse them.
        try
        {
            OpenAICostEstimator.ConfigureChatRates(0m, -1m);
            Assert.True(OpenAICostEstimator.ChatInputUsdPerMillionTokens > 0m);
            Assert.True(OpenAICostEstimator.ChatOutputUsdPerMillionTokens > 0m);
        }
        finally
        {
            OpenAICostEstimator.ConfigureChatRates(
                OpenAICostEstimator.DefaultChatInputUsdPerMillionTokens,
                OpenAICostEstimator.DefaultChatOutputUsdPerMillionTokens);
        }
    }

    // ---- pricing against what was actually sent (2026-08-12) ----

    /// <summary>KEYSTONE. The old overload sees only the child's question, so
    /// it prices the input side at almost nothing. Measured against the real
    /// in-story prompt: ~21 characters of question versus ~8,343 of grounding.
    /// This test states the size of the gap the fix closes, so a future edit
    /// that quietly reverts to the cheap overload fails here.</summary>
    [Fact]
    public void PromptAwareEstimate_IsMateriallyHigherThanQuestionOnly()
    {
        const string question = "Ինչո՞ւ ձուկը խոսում է";
        const string answer = "Որովհետև դա հեքիաթ է, և հեքիաթում ամեն ինչ հնարավոր է։";
        const long realPromptChars = 8343;

        var questionOnly = OpenAICostEstimator.EstimateChatCostUsd(question, answer);
        var promptAware = OpenAICostEstimator.EstimateChatCostUsdFromPrompt(realPromptChars, answer);

        Assert.True(promptAware > questionOnly * 3,
            $"prompt-aware ({promptAware}) must dominate question-only ({questionOnly})");
    }

    /// <summary>A repair retry is a second billed call. The caller sums both
    /// prompts into one number, so double the characters must cost more.</summary>
    [Fact]
    public void MorePromptChars_CostMore()
    {
        var once = OpenAICostEstimator.EstimateChatCostUsdFromPrompt(8343, "x");
        var twice = OpenAICostEstimator.EstimateChatCostUsdFromPrompt(8343 * 2, "x");
        Assert.True(twice > once);
    }

    /// <summary>Paths that never reached the model report 0 and must be free
    /// on the input side — an empty transcript costs speech-to-text only.</summary>
    [Fact]
    public void ZeroOrNegativePromptChars_ChargesOutputOnly()
    {
        var zero = OpenAICostEstimator.EstimateChatCostUsdFromPrompt(0, "answer");
        var negative = OpenAICostEstimator.EstimateChatCostUsdFromPrompt(-5, "answer");
        var outputOnly = OpenAICostEstimator.EstimateChatCostUsd("", "answer");

        Assert.Equal(outputOnly, zero);
        Assert.Equal(outputOnly, negative);
    }

    /// <summary>The old overload keeps its exact behaviour. It is still used
    /// by the online chat path, whose prompt is built inside ChatService and
    /// cannot be reported without touching a HIGH-risk file.</summary>
    [Fact]
    public void LegacyOverload_IsUnchanged()
    {
        // 4 chars in, 4 chars out -> 1 token each at the documented rates.
        var cost = OpenAICostEstimator.EstimateChatCostUsd("abcd", "wxyz");
        Assert.Equal(
            (1m / 1_000_000m * OpenAICostEstimator.ChatInputUsdPerMillionTokens)
          + (1m / 1_000_000m * OpenAICostEstimator.ChatOutputUsdPerMillionTokens),
            cost);
    }

    /// <summary>The daily cap is derived from a number of QUESTIONS, and the
    /// two must not drift apart. If someone changes the dollar figure without
    /// changing the intent (or vice versa), this fails and makes them say
    /// which they meant.</summary>
    [Fact]
    public void ShippedDailyCap_MatchesTheIntendedQuestionsPerDay()
    {
        var derived = OpenAIDailyCostCapOptions.IntendedQuestionsPerDay
                    * OpenAIDailyCostCapOptions.MeasuredUsdPerQuestion;
        var shipped = new OpenAIDailyCostCapOptions().Default;

        Assert.InRange(shipped, derived * 0.9m, derived * 1.2m);
    }
}

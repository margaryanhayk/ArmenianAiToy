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
}

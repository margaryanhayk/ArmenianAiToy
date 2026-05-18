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
}

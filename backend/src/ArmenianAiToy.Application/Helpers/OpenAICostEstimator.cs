namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Conservative local cost estimates for OpenAI text / audio calls.
/// Used by <see cref="OpenAICostMeter"/> to track per-device daily
/// spend. NOT authoritative pricing — the constants below are
/// approximate ceilings from publicly-documented pricing at the time
/// the cost-cap slice landed (2026-05-18). They are deliberately a
/// touch on the high side so the cap fires earlier rather than later
/// when in doubt.
/// <para>
/// If you change a model, update the matching constant here AND the
/// doc at <c>docs/openai-daily-cost-cap.md</c>.
/// </para>
/// <para>
/// The estimator is pure-static and has no I/O, no logging, no
/// metrics — those concerns live at the call site.
/// </para>
/// </summary>
public static class OpenAICostEstimator
{
    /// <summary>USD per 1M input tokens for the chat model
    /// (approximate gpt-4o). Conservative; revise on model change.</summary>
    public const decimal ChatInputUsdPerMillionTokens = 2.50m;

    /// <summary>USD per 1M output tokens for the chat model
    /// (approximate gpt-4o). Conservative; revise on model change.</summary>
    public const decimal ChatOutputUsdPerMillionTokens = 10.00m;

    /// <summary>USD per minute for Whisper transcription. Conservative;
    /// revise on transcription-model change.</summary>
    public const decimal WhisperUsdPerMinute = 0.006m;

    /// <summary>USD per 1M characters for TTS-1. Conservative; revise on
    /// TTS-model change.</summary>
    public const decimal TtsUsdPerMillionChars = 15.00m;

    /// <summary>Rough "characters per token" used to convert text length
    /// to an estimated token count. 4 is a common practical
    /// approximation for English / Latin text; Armenian text generally
    /// uses fewer characters per token but the over-estimate is
    /// intentional (cap fires earlier).</summary>
    public const int CharsPerTokenEstimate = 4;

    /// <summary>
    /// Estimate the USD cost of one chat round-trip from the lengths of
    /// the user input and the assistant response. Pure character-count
    /// based — no token-counting dependency on a real tokenizer.
    /// </summary>
    public static decimal EstimateChatCostUsd(string? userMessage, string? assistantResponse)
    {
        long inputTokens = EstimateTokenCount(userMessage);
        long outputTokens = EstimateTokenCount(assistantResponse);
        return TokensToUsd(inputTokens, ChatInputUsdPerMillionTokens)
             + TokensToUsd(outputTokens, ChatOutputUsdPerMillionTokens);
    }

    /// <summary>
    /// Estimate the USD cost of a Whisper transcription pass given the
    /// raw audio body length in bytes. Defaults assume 16 kHz mono
    /// 16-bit PCM, which is the C1 ESP32 voice MVP's contract.
    /// </summary>
    public static decimal EstimateWhisperCostUsd(
        long audioBytes,
        int sampleRateHz = 16000,
        int bytesPerSample = 2)
    {
        if (audioBytes <= 0 || sampleRateHz <= 0 || bytesPerSample <= 0) return 0m;
        double seconds = audioBytes / (double)(sampleRateHz * bytesPerSample);
        double minutes = seconds / 60d;
        if (minutes <= 0d) return 0m;
        return (decimal)minutes * WhisperUsdPerMinute;
    }

    /// <summary>
    /// Estimate the USD cost of a TTS synthesis pass given the
    /// character count of the text being synthesized.
    /// </summary>
    public static decimal EstimateTtsCostUsd(string? text)
    {
        long chars = text?.Length ?? 0;
        if (chars <= 0) return 0m;
        return (decimal)chars / 1_000_000m * TtsUsdPerMillionChars;
    }

    private static long EstimateTokenCount(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (long)Math.Ceiling(text.Length / (double)CharsPerTokenEstimate);
    }

    private static decimal TokensToUsd(long tokens, decimal usdPerMillion)
    {
        if (tokens <= 0) return 0m;
        return (decimal)tokens / 1_000_000m * usdPerMillion;
    }
}

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
    /// <summary>Shipped default chat input rate — approximate gpt-4o,
    /// the price the cap was calibrated against (2026-05-18).</summary>
    public const decimal DefaultChatInputUsdPerMillionTokens = 2.50m;

    /// <summary>Shipped default chat output rate — approximate gpt-4o.</summary>
    public const decimal DefaultChatOutputUsdPerMillionTokens = 10.00m;

    /// <summary>USD per 1M input tokens for the ACTIVE chat provider.
    /// Defaults to the OpenAI rate; startup calls
    /// <see cref="ConfigureChatRates"/> once with the resolved
    /// provider's rates (Tier-1 fix 2026-08-06: with the brain on
    /// Gemini, pricing chat at gpt-4o rates made the per-device daily
    /// cap fire ~8x early — fiction, not conservatism).</summary>
    public static decimal ChatInputUsdPerMillionTokens { get; private set; }
        = DefaultChatInputUsdPerMillionTokens;

    /// <summary>USD per 1M output tokens for the ACTIVE chat provider.
    /// See <see cref="ChatInputUsdPerMillionTokens"/>.</summary>
    public static decimal ChatOutputUsdPerMillionTokens { get; private set; }
        = DefaultChatOutputUsdPerMillionTokens;

    /// <summary>
    /// Set the chat token rates for the active provider. Called exactly
    /// once at startup (AddInfrastructure, right after the chat provider
    /// resolves). Non-positive values are ignored — the cap must never
    /// be configured off (zero rates would price every call at $0 and
    /// the daily cap would never fire).
    /// </summary>
    public static void ConfigureChatRates(decimal inputUsdPerMillionTokens, decimal outputUsdPerMillionTokens)
    {
        if (inputUsdPerMillionTokens > 0m) ChatInputUsdPerMillionTokens = inputUsdPerMillionTokens;
        if (outputUsdPerMillionTokens > 0m) ChatOutputUsdPerMillionTokens = outputUsdPerMillionTokens;
    }

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
    ///
    /// <para>
    /// <b>This overload UNDER-COUNTS whenever a system prompt was sent,</b>
    /// because it sees only the user's message. Measured 2026-08-12: a real
    /// in-story question is ~21 characters against an assembled prompt of
    /// ~8,343, so the input side was counted at roughly 0.3% of what the API
    /// actually billed, and a turn at 33% overall. Prefer
    /// <see cref="EstimateChatCostUsdFromPrompt"/> wherever the caller can
    /// obtain the real prompt size. Kept because the online chat path
    /// (<c>ChatController</c> / <c>AudioChatController</c>) builds its prompt
    /// inside <c>ChatService</c> and cannot report the size without changing
    /// that file, which is HIGH risk — and no shipped firmware uses that path
    /// today. See docs/usage-tiers-brainstorm.md.
    /// </para>
    /// </summary>
    public static decimal EstimateChatCostUsd(string? userMessage, string? assistantResponse)
    {
        long inputTokens = EstimateTokenCount(userMessage);
        long outputTokens = EstimateTokenCount(assistantResponse);
        return TokensToUsd(inputTokens, ChatInputUsdPerMillionTokens)
             + TokensToUsd(outputTokens, ChatOutputUsdPerMillionTokens);
    }

    /// <summary>
    /// Estimate the USD cost of one or more chat round-trips from the ACTUAL
    /// number of prompt characters sent, plus the response.
    ///
    /// <para>
    /// <paramref name="promptChars"/> is the sum of every system prompt and
    /// user message handed to the model for this turn — including a repair
    /// retry, which is a second billed call the older overload never saw at
    /// all. Callers that know what they sent should use this; two thirds of
    /// the cost of answering a child is the grounding sent with the question.
    /// </para>
    /// </summary>
    public static decimal EstimateChatCostUsdFromPrompt(long promptChars, string? assistantResponse)
    {
        if (promptChars < 0) promptChars = 0;
        long inputTokens = (long)Math.Ceiling(promptChars / (double)CharsPerTokenEstimate);
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

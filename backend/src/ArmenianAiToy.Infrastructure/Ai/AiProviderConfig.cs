using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Infrastructure.Ai;

/// <summary>
/// The per-capability AI provider switch (owner request 2026-08-05):
/// which vendor serves each of the four AI capabilities is CONFIG, not
/// code, so a better model can be adopted in production with an env-var
/// flip instead of a redeploy.
///
/// <para>
/// Four independent keys — capabilities are deliberately switchable
/// separately (e.g. a Gemini chat model under OpenAI moderation is a
/// supported, even recommended, combination):
/// <c>AI:ChatProvider</c>, <c>AI:TranscriptionProvider</c>,
/// <c>AI:TtsProvider</c>, <c>AI:ModerationProvider</c>.
/// </para>
///
/// <para>
/// Mirrors the <c>Notifications:Transport</c> contract: a BOUNDED value
/// space, missing/empty resolves to the shipped default ("openai"), and
/// an unknown value THROWS AT STARTUP — a typo in a provider name must
/// never silently fall back on a children's product. Today the only
/// implemented provider is OpenAI; adding one means writing its adapter
/// first, then extending <see cref="Supported"/> and the corresponding
/// switch in DependencyInjection — the resolver refuses names that have
/// no adapter behind them.
/// </para>
///
/// <para>
/// Model names WITHIN a provider are already config
/// (<c>OpenAI:ChatModel</c> / <c>:TranscriptionModel</c> / <c>:TtsModel</c>
/// / <c>:ModerationModel</c> / <c>:TtsVoice</c>) — this seam is about the
/// vendor, not the model. Two invariants no provider flip may break:
/// moderation stays FAIL-CLOSED whatever vendor serves it, and no
/// provider/model change reaches children without a benchmark run plus
/// the owner's Armenian listen test.
/// </para>
/// </summary>
public static class AiProviderConfig
{
    public const string OpenAI = "openai";
    public const string ElevenLabs = "elevenlabs";

    /// <summary>Providers valid for every capability key by default.</summary>
    public static readonly string[] Supported = [OpenAI];

    /// <summary>TTS additionally supports ElevenLabs (the eleven_v3 clone
    /// adapter, 2026-08-05 — the only capability with a second plug so far).</summary>
    public static readonly string[] SupportedForTts = [OpenAI, ElevenLabs];

    public const string ChatKey = "AI:ChatProvider";
    public const string TranscriptionKey = "AI:TranscriptionProvider";
    public const string TtsKey = "AI:TtsProvider";
    public const string ModerationKey = "AI:ModerationProvider";

    /// <summary>
    /// Resolve one capability's provider. Missing/whitespace → "openai";
    /// known value (case-insensitive, trimmed) → normalized lowercase;
    /// anything else → throws with the offending key and value named.
    /// </summary>
    public static string Resolve(IConfiguration config, string key)
        => Resolve(config, key, Supported);

    /// <summary>
    /// Per-capability overload: a provider name is only valid for a key
    /// whose registration switch actually has a case for it — otherwise
    /// the boot would "succeed" with a missing service and die later.
    /// </summary>
    public static string Resolve(IConfiguration config, string key, string[] supported)
    {
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return OpenAI;

        var value = raw.Trim().ToLowerInvariant();
        foreach (var known in supported)
        {
            if (value == known) return known;
        }

        throw new InvalidOperationException(
            $"{key} is '{raw}', which is not a supported AI provider for this " +
            $"capability. Supported: {string.Join(", ", supported)}. Adding a " +
            "provider means writing its adapter first — see CLAUDE.md § AI provider seam.");
    }
}

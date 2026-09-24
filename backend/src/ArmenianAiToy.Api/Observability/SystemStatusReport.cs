using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Infrastructure.Ai;

namespace ArmenianAiToy.Api.Observability;

/// <summary>
/// Pure snapshot of what the running backend is configured to do — which
/// model serves each voice/text path, which credentials are PRESENT (never
/// their values), and a list of configuration checks an operator should
/// act on. Served by <c>GET /api/internal/system</c> to the console's
/// "System" tab, so the boot warnings in <c>Program.cs</c> are visible
/// without reading Railway logs.
///
/// <para>
/// Effective values mirror <c>DependencyInjection</c> and <c>Program.cs</c>
/// exactly (same fallbacks, same defaults) — a report that disagreed with
/// what actually serves children would be worse than none. Pinned by test.
/// </para>
/// </summary>
public static class SystemStatusReport
{
    public sealed record ModelRow(string Path, string Provider, string Model, string? Note);

    public sealed record Check(string Level, string Code, string Message);

    public sealed record Report(
        IReadOnlyList<ModelRow> Models,
        IReadOnlyDictionary<string, bool> Configured,
        IReadOnlyList<Check> Checks);

    public const string Warn = "warn";
    public const string Info = "info";

    public static Report Build(IConfiguration config, bool isDevelopment, DateTime utcNow)
    {
        var models = new List<ModelRow>();
        var checks = new List<Check>();

        // ---- chat (the model that writes every reply) ----
        var chatProvider = SafeResolve(config, AiProviderConfig.ChatKey, AiProviderConfig.SupportedForChat);
        var geminiBackend = GeminiChatClientAdapter.ResolveBackend(FirstNonEmpty(config["Gemini:Backend"]));
        if (chatProvider == AiProviderConfig.Gemini)
        {
            models.Add(new ModelRow("Chat (every reply)", "gemini",
                FirstNonEmpty(config["Gemini:Model"]) ?? "gemini-3.6-flash", $"backend: {geminiBackend}"));
            if (geminiBackend == GeminiChatClientAdapter.BackendAiStudio)
                checks.Add(new Check(Warn, "gemini_terms",
                    "Chat runs on the Gemini API (AI Studio), whose terms exclude services for under-18s. " +
                    "Move to Gemini:Backend=vertex once Google confirms (docs/ops-runbook.md)."));
        }
        else
        {
            models.Add(new ModelRow("Chat (every reply)", chatProvider,
                config["OpenAI:ChatModel"] ?? "gpt-4o-mini", null));
        }

        // ---- speech-to-text (every child utterance) ----
        var sttProvider = SafeResolve(config, AiProviderConfig.TranscriptionKey, AiProviderConfig.Supported);
        var defaultStt = config["OpenAI:TranscriptionModel"] ?? "whisper-1";
        string EffectiveStt(string key)
            => string.IsNullOrWhiteSpace(config[key]) ? defaultStt : config[key]!;
        var sttRows = new (string Path, string Key, string Model)[]
        {
            ("Speech-to-text: voice chat", "OpenAI:TranscriptionModel", defaultStt),
            ("Speech-to-text: in-story questions", "StoryQa:TranscriptionModel", EffectiveStt("StoryQa:TranscriptionModel")),
            ("Speech-to-text: welcome menu answers", "Devices:VoiceIntentTranscriptionModel", EffectiveStt("Devices:VoiceIntentTranscriptionModel")),
        };
        foreach (var row in sttRows)
            models.Add(new ModelRow(row.Path, sttProvider, row.Model, row.Key));
        if (sttProvider == AiProviderConfig.OpenAI)
        {
            foreach (var r in ModelRetirementCatalog.Evaluate(
                         sttRows.Select(x => (x.Key, (string?)x.Model)).ToArray(),
                         DateOnly.FromDateTime(utcNow)))
            {
                checks.Add(new Check(Warn, "model_retirement",
                    $"{r.ConfigKey} is {r.Model}, which OpenAI {(r.AlreadyRemoved ? "REMOVED" : "removes")} on " +
                    $"{r.RemovalDate:yyyy-MM-dd}. Replacement: {r.Replacement}."));
            }
        }

        // ---- text-to-speech (everything Areg says live) ----
        var ttsProvider = SafeResolve(config, AiProviderConfig.TtsKey, AiProviderConfig.SupportedForTts);
        if (ttsProvider == AiProviderConfig.ElevenLabs)
        {
            models.Add(new ModelRow("Text-to-speech (live voice)", "elevenlabs",
                FirstNonEmpty(config["ElevenLabs:ModelId"]) ?? "eleven_v3", null));
            checks.Add(new Check(Warn, "elevenlabs_terms",
                "Live voice runs on ElevenLabs, whose policy excludes products aimed at under-13s " +
                "(docs/legal/vendor-terms-and-ai-toy-laws-2026-09.md)."));
        }
        else
        {
            var voice = config["OpenAI:TtsVoice"];
            models.Add(new ModelRow("Text-to-speech (live voice)", ttsProvider,
                config["OpenAI:TtsModel"] ?? "tts-1",
                string.IsNullOrWhiteSpace(voice) ? "voice: default" : $"voice: {voice}"));
        }

        // ---- moderation (both directions, fail-closed) ----
        models.Add(new ModelRow("Safety moderation (in + out)",
            SafeResolve(config, AiProviderConfig.ModerationKey, AiProviderConfig.Supported),
            config["OpenAI:ModerationModel"] ?? "omni-moderation-latest", "fail-closed"));

        // ---- what is configured (presence only — never a value) ----
        var audio = AudioBlobStoreRootResolver.Resolve(isDevelopment, config["Audio:BlobStoreRoot"]);
        var forwarded = config.GetValue<bool>("ForwardedHeaders:Enabled");
        var configured = new Dictionary<string, bool>
        {
            ["openaiKey"] = !string.IsNullOrWhiteSpace(config["OpenAI:ApiKey"]),
            ["geminiKey"] = FirstNonEmpty(config["Gemini:ApiKey"], config["GEMINI_API_KEY"]) is not null,
            ["elevenLabsKey"] = FirstNonEmpty(config["ElevenLabs:ApiKey"], config["ELEVENLABS_API_KEY"]) is not null,
            ["vertexServiceAccount"] = FirstNonEmpty(config["Gemini:Vertex:ServiceAccountJson"]) is not null,
            ["alertsWebhook"] = FirstNonEmpty(config["Alerts:WebhookUrl"]) is not null,
            ["metricsToken"] = FirstNonEmpty(config["Metrics:ScrapeToken"]) is not null,
            ["audioStore"] = audio.IsConfigured,
            ["forwardedHeaders"] = forwarded,
        };

        if (!audio.IsConfigured)
            checks.Add(new Check(Warn, "audio_store",
                "Audio:BlobStoreRoot is not set — voice chat refuses every turn with 503 until it is."));
        if (ForwardedHeadersConfig.ShouldWarnDisabledOutsideDevelopment(isDevelopment, forwarded))
            checks.Add(new Check(Warn, "forwarded_headers",
                "ForwardedHeaders__Enabled is not true — per-IP rate limits see the proxy, not the parent."));
        if (!configured["alertsWebhook"])
            checks.Add(new Check(Warn, "alerts_off",
                "Alerts__WebhookUrl is not set — nobody is notified when the database, OpenAI, moderation or backups fail."));

        var capSection = config.GetSection("OpenAI:DailyCostCap");
        if (bool.TryParse(capSection["Enabled"], out var capOn) && !capOn)
            checks.Add(new Check(Warn, "cost_cap_off",
                "OpenAI:DailyCostCap:Enabled is false — no per-toy daily spending limit."));
        if (!decimal.TryParse(capSection["Global"], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var global) || global <= 0)
            checks.Add(new Check(Info, "global_cap_off",
                "No fleet-wide daily spending ceiling (OpenAI:DailyCostCap:Global is 0)."));

        return new Report(models, configured, checks);
    }

    // The boot already refused an unknown provider; a report must never
    // throw, so an unexpected value is shown as-is instead.
    private static string SafeResolve(IConfiguration config, string key, string[] supported)
    {
        try { return AiProviderConfig.Resolve(config, key, supported); }
        catch (InvalidOperationException) { return config[key] ?? "?"; }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

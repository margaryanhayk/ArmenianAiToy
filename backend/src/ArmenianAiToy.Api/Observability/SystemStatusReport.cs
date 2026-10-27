using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Infrastructure.Ai;
using ArmenianAiToy.Infrastructure.Notifications;

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
        IReadOnlyList<Check> Checks,
        IReadOnlyDictionary<string, string> Settings);

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
        var forwarded = ReadBool(config, "ForwardedHeaders:Enabled");
        // The gate's own predicate (any non-empty string, whitespace included), not a
        // second opinion: report and DeviceProvisioningAuth.Evaluate cannot disagree.
        var provisioningSecretSet = DeviceProvisioningAuth.IsSecretConfigured(config["Devices:ProvisioningSecret"]);
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
            // Family-pilot readiness: can a parent get mail, can a toy be
            // provisioned, can a parent sign in with Google, will a toy be
            // offered firmware / content, is the front door pinned. Booleans
            // only — the value behind each one never leaves this method.
            ["notificationsKey"] = NotificationsKeyPresent(config),
            ["provisioningSecret"] = provisioningSecretSet,
            ["googleClientId"] = FirstNonEmpty(config["GoogleAuth:ClientId"]) is not null,
            ["firmwareUpdateEnabled"] = ReadBool(config, "FirmwareUpdate:Enabled"),
            // Mirrors DeviceController.GetFirmwareImage's gate (non-empty AND
            // absolute) without touching the disk: "set correctly", not "file exists".
            ["firmwareImagePath"] = FirstNonEmpty(config["FirmwareUpdate:ImagePath"]) is { } fwPath
                && Path.IsPathRooted(fwPath),
            ["contentSyncEnabled"] = ReadBool(config, "ContentSync:Enabled"),
            // Same decision Program.cs applies, with the real environment: a real
            // hostname beyond "*" — or Development, which deliberately runs with
            // "*" and raises no warning.
            ["allowedHostsPinned"] = HostFilteringConfig
                .Resolve(isDevelopment, config["AllowedHosts"]).Warning is null,
            ["requireHttps"] = ReadBool(config, "Security:RequireHttps"),
        };

        // The only non-boolean row: the transport NAME (log | smtp | resend) —
        // a name, not a secret. Anything else is reported as "unrecognised"
        // rather than echoed, so a value pasted into the wrong variable can
        // never surface here.
        var transport = ResolveTransportName(config);
        var settings = new Dictionary<string, string> { ["notificationsTransport"] = transport };

        if (!audio.IsConfigured)
            checks.Add(new Check(Warn, "audio_store",
                "Audio:BlobStoreRoot is not set — voice chat refuses every turn with 503 until it is."));
        if (ForwardedHeadersConfig.ShouldWarnDisabledOutsideDevelopment(isDevelopment, forwarded))
            checks.Add(new Check(Warn, "forwarded_headers",
                "ForwardedHeaders__Enabled is not true — per-IP rate limits see the proxy, not the parent."));
        if (!configured["alertsWebhook"])
            checks.Add(new Check(Warn, "alerts_off",
                "Alerts__WebhookUrl is not set — nobody is notified when the database, OpenAI, moderation or backups fail."));

        if (!isDevelopment && transport == NotifierTransport.Log)
            checks.Add(new Check(Warn, "email_log_only",
                "Parents get no email: password resets, email verification and the \"another parent joined your toy\" " +
                "notice are only written to the server log. Set Notifications__Transport=resend and Notifications__Resend__ApiKey."));
        if (!isDevelopment && !provisioningSecretSet)
        {
            if (ReadBool(config, "Devices:AllowOpenRegistration"))
                // A security hole, not a missing feature: first in the list.
                checks.Insert(0, new Check(Warn, "provisioning_secret_missing",
                    "Open registration: anyone can mint a device key (Devices:AllowOpenRegistration is on and " +
                    "Devices__ProvisioningSecret is not set). Set Devices__ProvisioningSecret — that alone closes it."));
            else
                checks.Add(new Check(Warn, "provisioning_secret_missing",
                    "Devices__ProvisioningSecret is not set — registering a new toy is refused until it is."));
        }

        var capSection = config.GetSection("OpenAI:DailyCostCap");
        if (bool.TryParse(capSection["Enabled"], out var capOn) && !capOn)
            checks.Add(new Check(Warn, "cost_cap_off",
                "OpenAI:DailyCostCap:Enabled is false — no per-toy daily spending limit."));
        if (!decimal.TryParse(capSection["Global"], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var global) || global <= 0)
            checks.Add(new Check(Info, "global_cap_off",
                "No fleet-wide daily spending ceiling (OpenAI:DailyCostCap:Global is 0)."));

        return new Report(models, configured, checks, settings);
    }

    // Mirrors NotifierTransport.ResolveImplementation's selector (missing or
    // empty = log; trimmed, case-insensitive) without its validation side
    // effects — a report must never throw. The names are NotifierTransport's own
    // constants; a test walks the selector's real result for each to pin it.
    private static string ResolveTransportName(IConfiguration config)
    {
        var raw = config["Notifications:Transport"];
        var name = string.IsNullOrWhiteSpace(raw) ? NotifierTransport.Log : raw.Trim().ToLowerInvariant();
        return name is NotifierTransport.Log or NotifierTransport.Smtp or NotifierTransport.Resend
            ? name
            : "unrecognised";
    }

    // Either transport's secret: a Resend key under any alias — asked of
    // ResendNotifier itself, not copied — or an SMTP password.
    private static bool NotificationsKeyPresent(IConfiguration config)
        => ResendNotifier.HasApiKey(config)
           || !string.IsNullOrWhiteSpace(config["Notifications:Smtp:Password"]);

    // The same parse the code paths this report mirrors use for these keys
    // (DeviceController register gate, DependencyInjection FirmwareUpdate,
    // ContentSyncOptions.Resolve): bool.TryParse, anything else = off.
    // IConfiguration.GetValue<bool> THROWS on "1" / "yes", and a report must
    // never throw — a Railway "Devices__AllowOpenRegistration=1" would blank
    // the whole System tab.
    private static bool ReadBool(IConfiguration config, string key, bool fallback = false)
        => bool.TryParse(config[key], out var value) ? value : fallback;

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

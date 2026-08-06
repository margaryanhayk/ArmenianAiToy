using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Notifications;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Infrastructure.Ai;
using ArmenianAiToy.Infrastructure.Audio;
using ArmenianAiToy.Infrastructure.Auth;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using ArmenianAiToy.Infrastructure.Notifications;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Moderations;

namespace ArmenianAiToy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Database
        var connectionString = config["Database:ConnectionString"] ?? "Data Source=armenian_ai_toy.db";
        // #019 — concurrency stopgap for the single-file SQLite deployment:
        // WAL + busy_timeout + synchronous=NORMAL on every opened connection
        // so concurrent writers wait for the lock instead of throwing
        // SQLITE_BUSY -> 500s. The real fix is moving off SQLite (#018).
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString)
                   .AddInterceptors(new SqlitePragmaInterceptor()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // AI provider seam (owner request 2026-08-05): which vendor serves
        // each capability is config. All four resolve UP FRONT so a typo in
        // any of them fails the boot loudly, not lazily. Today the bounded
        // value space is exactly {openai}; a second provider lands as a new
        // case in the switches below AFTER its adapter exists.
        var chatProvider = AiProviderConfig.Resolve(
            config, AiProviderConfig.ChatKey, AiProviderConfig.SupportedForChat);
        var sttProvider = AiProviderConfig.Resolve(config, AiProviderConfig.TranscriptionKey);
        var ttsProvider = AiProviderConfig.Resolve(
            config, AiProviderConfig.TtsKey, AiProviderConfig.SupportedForTts);
        var moderationProvider = AiProviderConfig.Resolve(config, AiProviderConfig.ModerationKey);

        // Honest chat-cost rates for the ACTIVE brain (Tier-1 fix
        // 2026-08-06): the per-device daily cap used to price every
        // provider at gpt-4o token rates — fiction once chat moved to
        // Gemini (~8x over). Explicit AI:ChatCostPerMTokensIn/Out win;
        // otherwise the resolved provider picks its default. Gemini
        // flash-class list price 2026-08 research: ~$0.30 in / $2.50
        // out per 1M tokens; configured a touch high ($0.50 / $3.00),
        // same fires-early posture as the estimator's own defaults.
        // Re-check when Google publishes the gemini-3.6-flash list price.
        var chatCostIn = TryParseDecimal(config["AI:ChatCostPerMTokensIn"])
            ?? (chatProvider == AiProviderConfig.Gemini
                ? 0.50m
                : OpenAICostEstimator.DefaultChatInputUsdPerMillionTokens);
        var chatCostOut = TryParseDecimal(config["AI:ChatCostPerMTokensOut"])
            ?? (chatProvider == AiProviderConfig.Gemini
                ? 3.00m
                : OpenAICostEstimator.DefaultChatOutputUsdPerMillionTokens);
        OpenAICostEstimator.ConfigureChatRates(chatCostIn, chatCostOut);

        // OpenAI
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is required");
        var chatModel = config["OpenAI:ChatModel"] ?? "gpt-4o-mini";

        var moderationModel = config["OpenAI:ModerationModel"] ?? "omni-moderation-latest";

        // Keep the OpenAI connection WARM between the toy's spaced-out questions.
        // A child asks every few minutes; .NET's default 1-minute pooled-connection
        // idle timeout means each question pays a full TLS reconnect (~2-3s of
        // "reconnect tax" measured on the voice Q&A path — cold inMod/stt/gpt). A
        // longer idle timeout plus HTTP/2 keep-alive PINGs hold the connection open
        // with NO extra API requests (zero quota burn). Shared by every OpenAI call
        // (chat, STT, TTS, moderation) since all flow through this one pooled client.
        var openAiHandler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(30),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            EnableMultipleHttp2Connections = true,
        };
        // Generous ceiling; the STT/TTS/chat adapters impose their own 30s
        // CancellationToken timeouts, which fire first. Must stay >= those.
        var openAiHttpClient = new HttpClient(openAiHandler) { Timeout = TimeSpan.FromSeconds(100) };
        var openAiOptions = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(openAiHttpClient),
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), openAiOptions);
        services.AddSingleton(openAiClient.GetChatClient(chatModel));
        services.AddSingleton(openAiClient.GetModerationClient(moderationModel));

        // C1 audio seams. Whisper for STT, OpenAI TTS for synthesis —
        // same OpenAI SDK, zero new NuGet dependency, reuses
        // OpenAI:ApiKey. Two distinct AudioClient instances (one per
        // model) wired directly into their adapters via factory
        // lambdas so we do not need to register AudioClient itself
        // under two keyed types.
        var whisperModel = config["OpenAI:TranscriptionModel"] ?? "whisper-1";
        var ttsModel = config["OpenAI:TtsModel"] ?? "tts-1";
        var whisperClient = openAiClient.GetAudioClient(whisperModel);
        var ttsClient = openAiClient.GetAudioClient(ttsModel);
        // Pass the OpenAIClient + default model so the transcription adapter
        // can honor an OPTIONAL per-call STT model override. The default
        // path (C1 voice + Q&A with no override) still uses whisperClient,
        // so shipped behavior is unchanged. See the StoryQa:TranscriptionModel
        // seam below.
        switch (sttProvider)
        {
            case AiProviderConfig.OpenAI:
                services.AddSingleton<IAudioTranscriptionService>(sp =>
                    new OpenAIWhisperTranscriptionService(
                        whisperClient, openAiClient, whisperModel,
                        sp.GetRequiredService<ILogger<OpenAIWhisperTranscriptionService>>()));
                break;
        }
        // The speaking voice is config, not a literal — comparing voices for
        // the Armenian listen test must not require a code change. Unset →
        // "nova", exactly what shipped before the key existed.
        var ttsVoice = config["OpenAI:TtsVoice"];
        switch (ttsProvider)
        {
            case AiProviderConfig.OpenAI:
                services.AddSingleton<IAudioSynthesisService>(sp =>
                    new OpenAITtsSynthesisService(
                        ttsClient,
                        sp.GetRequiredService<ILogger<OpenAITtsSynthesisService>>(),
                        ttsVoice));
                break;

            case AiProviderConfig.ElevenLabs:
            {
                // The clone speaks live (owner decision 2026-08-05).
                // Key/voice accept the app-style names first, then the env
                // names the render tool already uses — same forgiving
                // resolution as the Resend notifier, but the key and voice
                // are HARD-REQUIRED: TTS is the toy's mouth, and a silent
                // fallback would ship the wrong voice to a child.
                // FirstNonEmpty, not ?? — same empty-string-in-appsettings
                // trap as the Gemini key below.
                var elApiKey = FirstNonEmpty(config["ElevenLabs:ApiKey"], config["ELEVENLABS_API_KEY"])
                    ?? throw new InvalidOperationException(
                        "AI:TtsProvider is 'elevenlabs' but ElevenLabs:ApiKey / ELEVENLABS_API_KEY is not set.");
                var elVoiceId = FirstNonEmpty(config["ElevenLabs:VoiceId"], config["ELEVENLABS_VOICE_ID"])
                    ?? throw new InvalidOperationException(
                        "AI:TtsProvider is 'elevenlabs' but ElevenLabs:VoiceId / ELEVENLABS_VOICE_ID is not set.");
                var elModelId = FirstNonEmpty(config["ElevenLabs:ModelId"]) ?? "eleven_v3";
                services.AddSingleton<IAudioSynthesisService>(sp =>
                    new ElevenLabsTtsSynthesisService(
                        openAiHttpClient, // reuse the warm pooled client — different host, same pool
                        elApiKey, elVoiceId, elModelId,
                        sp.GetRequiredService<ILogger<ElevenLabsTtsSynthesisService>>()));
                break;
            }
        }
        services.AddSingleton<IAudioBlobStore, LocalDiskAudioBlobStore>();
        // Slice F — custom-story-request photo storage (owner queue).
        services.AddSingleton<IStoryRequestPhotoStore,
            StoryRequests.LocalDiskStoryRequestPhotoStore>();
        // Canned-clip cache is process-local state keyed on the TTS
        // service; singleton so the cache survives across requests
        // for the process lifetime.
        services.AddSingleton<CannedVoiceClips>();

        // Reliability gate for the chat adapter. Singleton because the
        // circuit-breaker state (recent-failure window, open-until) must
        // persist across scoped requests. The moderation adapter
        // deliberately does NOT route through this gate — it has its own
        // purpose-specific D1 retry policy and fail-closed-to-sentinel
        // contract that's child-safety-critical; see CLAUDE.md §
        // OpenAI reliability for the rationale.
        services.AddSingleton<OpenAIReliabilityGate>();

        // Adapters — the interface each capability's provider switch
        // governs. The OpenAI client singletons above stay registered
        // unconditionally (cheap, and other OpenAI-served capabilities
        // may still need them under a mixed-provider config).
        switch (chatProvider)
        {
            case AiProviderConfig.OpenAI:
                services.AddScoped<IAiChatClient, OpenAIChatClientAdapter>();
                break;

            case AiProviderConfig.Gemini:
            {
                // Owner bake-off decision 2026-08-06. Key hard-required
                // (chat is the toy's brain; no silent fallback); accepts
                // the app-style name first, then the GEMINI_API_KEY env
                // name. Model defaults to the exact bake-off winner.
                // FirstNonEmpty, not ?? — appsettings ships the key as ""
                // and an empty string is not null, so a bare ?? chain never
                // reached the env fallback and the adapter got an empty key
                // (Gemini answered 403 to every call; found in Gate 2).
                var gmKey = FirstNonEmpty(config["Gemini:ApiKey"], config["GEMINI_API_KEY"])
                    ?? throw new InvalidOperationException(
                        "AI:ChatProvider is 'gemini' but Gemini:ApiKey / GEMINI_API_KEY is not set.");
                var gmModel = FirstNonEmpty(config["Gemini:Model"]) ?? "gemini-3.6-flash";
                int? gmThinking = int.TryParse(
                    FirstNonEmpty(config["Gemini:ThinkingBudget"]), out var tb) ? tb : null;
                // Own provider-tagged gate instance: same retry/breaker
                // semantics as OpenAI's, separate breaker state, and the
                // gate counters carry provider="gemini". Singleton so the
                // breaker window survives across scoped requests (the
                // health endpoint's `openai` field keeps reading the
                // OpenAI singleton only — naming drift noted).
                services.AddSingleton(sp => new GeminiGateHolder(
                    new OpenAIReliabilityGate(
                        sp.GetRequiredService<ILogger<OpenAIReliabilityGate>>(),
                        "gemini")));
                services.AddScoped<IAiChatClient>(sp =>
                    new GeminiChatClientAdapter(
                        openAiHttpClient, // warm pooled client, host-agnostic
                        gmKey, gmModel,
                        sp.GetRequiredService<ILogger<GeminiChatClientAdapter>>(),
                        sp.GetRequiredService<GeminiGateHolder>().Gate,
                        gmThinking));
                break;
            }
        }
        // Recommendation (recorded, not enforced): keep moderation on
        // OpenAI even when chat moves elsewhere — cross-vendor
        // chat-vs-safety is a supported combination, and the fail-closed
        // contract binds whichever vendor serves it.
        switch (moderationProvider)
        {
            case AiProviderConfig.OpenAI:
                services.AddScoped<IModerationService, OpenAIModerationAdapter>();
                break;
        }

        // Library Story engine (W1 — session lifecycle only, NOT wired
        // into live chat turns; ChatService routing is the separate,
        // human-approved W2 slice). Engine selection is the
        // deterministic Story:Engine flag: shipped default "legacy",
        // and missing/unrecognized values resolve to legacy, so these
        // registrations change no live behavior until a human flips
        // the flag. The library serves ONLY approved embedded
        // Stories/Content resources — drafts (e.g. anban-huri) are
        // structurally unreachable. Singletons: the tracker is
        // process-local session state (same pattern as ExportCooldown)
        // and the library/playback service are stateless over it. The
        // W2 Q&A consumer is scoped because IAiChatClient is scoped.
        services.AddSingleton(StoryEngineOptions.FromConfiguration(config));
        // Story library. Production path is the approved-only embedded
        // library. BENCH/LOCAL override (both keys optional, both inert
        // when unset, so production is unchanged): Story:DefaultStoryId
        // picks which story SelectDefault returns, and
        // Story:ExtraStoryFiles[] side-loads extra story files from disk
        // (requireApproved:false) so an un-promoted DRAFT (e.g.
        // anban-huri) can be served LOCALLY for a bench without changing
        // its text, status, dates, or embedding/promoting it.
        var defaultStoryId = config["Story:DefaultStoryId"];
        var extraStoryFiles = config.GetSection("Story:ExtraStoryFiles")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
        if (string.IsNullOrWhiteSpace(defaultStoryId) && extraStoryFiles.Count == 0)
        {
            services.AddSingleton<ICuratedStoryLibrary, InMemoryCuratedStoryLibrary>();
        }
        else
        {
            var configuredLibrary = ConfigurableCuratedStoryLibrary.Create(
                new InMemoryCuratedStoryLibrary(),
                extraStoryFiles,
                defaultStoryId,
                msg => Console.Error.WriteLine($"[story-library] {msg}"));
            services.AddSingleton<ICuratedStoryLibrary>(configuredLibrary);
        }
        services.AddSingleton<LibraryStorySessionTracker>();
        services.AddSingleton<LibraryStoryPlaybackService>();

        // Bounded in-story Q&A answer-model seam (latency). The bounded Q&A
        // task (prompt → answer → StoryAnswerFilter validate → one repair →
        // canned fallback) is a far simpler surface than open chat, so an
        // operator can later run it on a faster/cheaper model. StoryQa:AnswerModel
        // DEFAULTS to OpenAI:ChatModel (gpt-4o today) so behavior is unchanged
        // out of the box; flip it to e.g. gpt-4o-mini ONLY after an
        // Armenian-quality benchmark. This dedicated chat client is isolated
        // to the Q&A service — the live ChatService chat path keeps using the
        // ambient IAiChatClient/OpenAI:ChatModel, untouched. Routed through
        // the same OpenAIReliabilityGate as the main chat adapter.
        var qaAnswerModel = config["StoryQa:AnswerModel"];
        if (string.IsNullOrWhiteSpace(qaAnswerModel) || qaAnswerModel == chatModel)
        {
            // No override (or same model): reuse the ambient IAiChatClient so
            // there is exactly one chat client when nothing was configured.
            services.AddScoped<LibraryStoryQuestionService>();
        }
        else
        {
            var qaChatClient = openAiClient.GetChatClient(qaAnswerModel);
            services.AddScoped(sp => new LibraryStoryQuestionService(
                new OpenAIChatClientAdapter(
                    qaChatClient, sp.GetRequiredService<OpenAIReliabilityGate>())));
        }

        // Reflection-dialogue reaction service — same bounded surface family
        // as the in-story Q&A above; uses the ambient IAiChatClient (the
        // StoryQa:AnswerModel seam can be extended here later if the
        // reaction ever needs its own model).
        services.AddScoped<ReflectionDialogueService>();

        // Application services
        services.AddSingleton<IStoryChoiceCoherenceGate, StoryChoiceCoherenceGate>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IDeviceService, DeviceService>();
        // OTA foundation (Proof 2): the device command queue (scoped — uses the
        // request DbContext) and the config-driven firmware manifest (singleton).
        services.AddScoped<IDeviceCommandService, DeviceCommandService>();
        // #040 — per-account login throttle. SINGLETON so the failed-attempt
        // counters persist across scoped requests / parents; injected into the
        // scoped ParentService.
        services.AddSingleton<LoginAttemptThrottle>();
        services.AddScoped<IParentService, ParentService>();

        // Google sign-in token validator. Always registered — the
        // validator itself fails closed (returns null) when
        // GoogleAuth:ClientId is empty, and the controller short-
        // circuits feature-off requests with a 404 before calling
        // the service. Scoped for symmetry with IParentService; the
        // implementation carries no per-request state so singleton
        // would work too but scoped lets future versions take
        // DbContext or other scoped deps without a DI change.
        services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
        services.AddScoped<IChildService, ChildService>();

        // Minimal outbound-notification seam. `Notifications:Transport`
        // picks the implementation at startup — `log` (shipped default,
        // no real delivery) or `smtp` (BCL SmtpClient-backed send).
        // Unknown values and missing required SMTP config both fail
        // fast here via NotifierTransport.ResolveImplementation, so a
        // misconfigured process never reaches the first reset-email
        // send with a broken transport. See CLAUDE.md § Password reset
        // / § Notification transport.
        var notifierImpl = NotifierTransport.ResolveImplementation(config);

        // Dormancy-warn transport precondition. If the warn-only pass
        // is enabled, the notifier MUST be SMTP — LoggingNotifier
        // always returns "delivered" to the worker without ever
        // reaching real parents, which would silently advance
        // DormancyWarnedAt on every tick. Fail-fast at DI-time rather
        // than at first-tick time so a misconfigured environment
        // cannot start.
        DormancyTransportPrecondition.Enforce(config, notifierImpl);

        services.AddScoped(typeof(INotifier), notifierImpl);

        // Per-parent cooldown guard for the data-export endpoint.
        // Singleton because the cooldown map must persist across scoped
        // requests for the window to be meaningful. Process-local memory
        // only; see ExportCooldown for the rationale.
        services.AddSingleton<ExportCooldown>();

        // Per-device daily OpenAI cost cap. Singleton in-memory meter
        // shared by ChatController and AudioChatController. Options
        // bind from "OpenAI:DailyCostCap"; missing keys collapse to the
        // safety-default 0.50 USD/day cap (Enabled=true by default —
        // see OpenAIDailyCostCapOptions). Process restart resets the
        // counters; documented in docs/openai-daily-cost-cap.md.
        //
        // Manual binding (string indexer + GetSection / GetChildren)
        // because Infrastructure deliberately does not pull
        // Microsoft.Extensions.Configuration.Binder — keeps the
        // dependency tree narrow and matches the existing
        // config["Database:ConnectionString"] pattern earlier in this
        // file. The Api project still ships an OptionsMonitor-aware
        // story via Microsoft.Extensions.Options, which is what the
        // controllers consume via IOptions<T>.
        var capOpts = new OpenAIDailyCostCapOptions();
        var capSection = config.GetSection("OpenAI:DailyCostCap");
        if (bool.TryParse(capSection["Enabled"], out var capEnabled))
            capOpts.Enabled = capEnabled;
        if (decimal.TryParse(
                capSection["Default"],
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var capDefault))
            capOpts.Default = capDefault;
        if (decimal.TryParse(
                capSection["Global"],
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var capGlobal))
            capOpts.Global = capGlobal;
        foreach (var child in capSection.GetSection("PerDeviceOverride").GetChildren())
        {
            if (child.Value is null) continue;
            if (decimal.TryParse(
                    child.Value,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var perDev))
                capOpts.PerDeviceOverride[child.Key] = perDev;
        }
        services.AddSingleton(
            Microsoft.Extensions.Options.Options.Create(capOpts));
        services.AddSingleton<OpenAICostMeter>();

        // OTA foundation (Proof 2): the CURRENT firmware release the manifest
        // endpoint offers. Same manual-binding style as the cost cap above.
        // Ships Enabled=false → the endpoint returns no-update until configured.
        var fwOpts = new FirmwareUpdateOptions();
        var fwSection = config.GetSection("FirmwareUpdate");
        if (bool.TryParse(fwSection["Enabled"], out var fwEnabled)) fwOpts.Enabled = fwEnabled;
        fwOpts.LatestVersion = fwSection["LatestVersion"] ?? "";
        fwOpts.MinVersion = fwSection["MinVersion"] ?? "";
        fwOpts.BoardModel = fwSection["BoardModel"] ?? "";
        fwOpts.Url = fwSection["Url"] ?? "";
        fwOpts.ImagePath = fwSection["ImagePath"] ?? "";
        if (long.TryParse(fwSection["SizeBytes"], out var fwSize)) fwOpts.SizeBytes = fwSize;
        fwOpts.Sha256 = fwSection["Sha256"] ?? "";
        fwOpts.SigningKey = fwSection["SigningKey"] ?? "";
        if (int.TryParse(fwSection["TtlSeconds"], out var fwTtl)) fwOpts.TtlSeconds = fwTtl;
        services.AddSingleton(fwOpts);
        services.AddSingleton<IFirmwareManifestService, FirmwareManifestService>();

        // Cloud→SD content sync: the story audio set the device
        // content-manifest offers. Ships Enabled=false → empty manifest until
        // configured. Binding lives in ContentSyncOptions.Resolve rather than
        // inline here so the hand-rolled Stories[] array binding is reachable
        // by tests; it reads BOTH shapes (the ordered Stories list and the
        // legacy single-item scalars) and ResolveStories picks between them,
        // so an overlay written before multi-story still works untouched.
        services.AddSingleton(ContentSyncOptions.Resolve(config));
        services.AddSingleton<IContentManifestService, ContentManifestService>();

        // First scheduled-delete worker in the repo. Hard-deletes
        // conversations (and their cascaded messages) older than
        // Retention:Messages:MaxAgeDays (default 90). Missing config
        // resolves to the default — never to "disabled." Disabled mode
        // requires an explicit non-positive override. See
        // RetentionPurgeService and CLAUDE.md § Retention.
        services.AddHostedService<RetentionPurgeService>();

        // Daily SQLite VACUUM INTO snapshots + keep-7 pruning (Tier-1
        // backup slice 2026-08-06). On by default; see the service
        // xmldoc for config keys and the on-volume-only caveat.
        services.AddHostedService<DatabaseBackupService>();

        return services;
    }

    /// <summary>
    /// First value that is non-null and non-whitespace, else null.
    /// Provider config keys ship as "" in appsettings, and an empty
    /// string is not null — a bare ?? chain silently stops at the empty
    /// value instead of reaching the env-var fallback (the Gate-2
    /// Gemini-403 bug). Same contract ResendNotifier's resolvers use.
    /// </summary>
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    /// <summary>
    /// Invariant-culture decimal parse; null on empty/unparseable.
    /// Config decimals must not depend on the host locale — "0.50"
    /// has to mean the same number on an hy-AM Windows box and in
    /// the Railway container.
    /// </summary>
    private static decimal? TryParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) ? d : null;
    }
}

using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Auth;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Notifications;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
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
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // OpenAI
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is required");
        var chatModel = config["OpenAI:ChatModel"] ?? "gpt-4o-mini";

        var moderationModel = config["OpenAI:ModerationModel"] ?? "omni-moderation-latest";

        var openAiClient = new OpenAIClient(apiKey);
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
        services.AddSingleton<IAudioTranscriptionService>(sp =>
            new OpenAIWhisperTranscriptionService(
                whisperClient,
                sp.GetRequiredService<ILogger<OpenAIWhisperTranscriptionService>>()));
        services.AddSingleton<IAudioSynthesisService>(sp =>
            new OpenAITtsSynthesisService(
                ttsClient,
                sp.GetRequiredService<ILogger<OpenAITtsSynthesisService>>()));
        services.AddSingleton<IAudioBlobStore, LocalDiskAudioBlobStore>();
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

        // Adapters
        services.AddScoped<IAiChatClient, OpenAIChatClientAdapter>();
        services.AddScoped<IModerationService, OpenAIModerationAdapter>();

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
        services.AddScoped<LibraryStoryQuestionService>();

        // Application services
        services.AddSingleton<IStoryChoiceCoherenceGate, StoryChoiceCoherenceGate>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IDeviceService, DeviceService>();
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

        // First scheduled-delete worker in the repo. Hard-deletes
        // conversations (and their cascaded messages) older than
        // Retention:Messages:MaxAgeDays (default 90). Missing config
        // resolves to the default — never to "disabled." Disabled mode
        // requires an explicit non-positive override. See
        // RetentionPurgeService and CLAUDE.md § Retention.
        services.AddHostedService<RetentionPurgeService>();

        return services;
    }
}

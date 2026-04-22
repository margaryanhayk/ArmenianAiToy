using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Notifications;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using ArmenianAiToy.Infrastructure.Notifications;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
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

        // Application services
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IParentService, ParentService>();
        services.AddScoped<IChildService, ChildService>();

        // Minimal outbound-notification seam. Log-only by default; a
        // future deploy slice can swap in an email / webhook / provider
        // SDK without changing any call site. Introduced as part of the
        // forgot-password slice — see INotifier xmldoc.
        services.AddScoped<INotifier, LoggingNotifier>();

        // Per-parent cooldown guard for the data-export endpoint.
        // Singleton because the cooldown map must persist across scoped
        // requests for the window to be meaningful. Process-local memory
        // only; see ExportCooldown for the rationale.
        services.AddSingleton<ExportCooldown>();

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

using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Infrastructure.Data;
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

        var openAiClient = new OpenAIClient(apiKey);
        services.AddSingleton(openAiClient.GetChatClient(chatModel));
        services.AddSingleton(openAiClient.GetModerationClient("omni-moderation-latest"));

        // Adapters
        services.AddScoped<IAiChatClient, OpenAIChatClientAdapter>();
        services.AddScoped<IModerationService, OpenAIModerationAdapter>();

        // Application services
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IParentService, ParentService>();
        services.AddScoped<IChildService, ChildService>();

        return services;
    }
}

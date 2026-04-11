using ArmenianAiToy.Application.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI.Moderations;
using AppModerationResult = ArmenianAiToy.Application.DTOs.ModerationResult;

namespace ArmenianAiToy.Infrastructure.OpenAI;

public class OpenAIModerationAdapter : IModerationService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly ModerationClient _client;
    private readonly ILogger<OpenAIModerationAdapter> _logger;

    public OpenAIModerationAdapter(ModerationClient client, ILogger<OpenAIModerationAdapter> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AppModerationResult> CheckContentAsync(string content)
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            var result = await _client.ClassifyTextAsync(content, cts.Token);
            var categories = result.Value;

            var flagged = new List<string>();

            if (categories.Sexual.Flagged) flagged.Add("sexual");
            if (categories.Violence.Flagged) flagged.Add("violence");
            if (categories.SelfHarm.Flagged) flagged.Add("self-harm");
            if (categories.Hate.Flagged) flagged.Add("hate");
            if (categories.Harassment.Flagged) flagged.Add("harassment");

            return new AppModerationResult(flagged.Count == 0, flagged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Moderation API failed — treating content as unsafe (fail-closed)");
            return new AppModerationResult(false, new List<string> { "moderation_unavailable" });
        }
    }
}

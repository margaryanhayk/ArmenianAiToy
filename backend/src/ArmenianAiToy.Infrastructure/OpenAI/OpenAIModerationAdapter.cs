using ArmenianAiToy.Application.Interfaces;
using OpenAI.Moderations;
using AppModerationResult = ArmenianAiToy.Application.DTOs.ModerationResult;

namespace ArmenianAiToy.Infrastructure.OpenAI;

public class OpenAIModerationAdapter : IModerationService
{
    private readonly ModerationClient _client;

    public OpenAIModerationAdapter(ModerationClient client)
    {
        _client = client;
    }

    public async Task<AppModerationResult> CheckContentAsync(string content)
    {
        try
        {
            var result = await _client.ClassifyTextAsync(content);
            var categories = result.Value;

            var flagged = new List<string>();

            if (categories.Sexual.Flagged) flagged.Add("sexual");
            if (categories.Violence.Flagged) flagged.Add("violence");
            if (categories.SelfHarm.Flagged) flagged.Add("self-harm");
            if (categories.Hate.Flagged) flagged.Add("hate");
            if (categories.Harassment.Flagged) flagged.Add("harassment");

            return new AppModerationResult(flagged.Count == 0, flagged);
        }
        catch
        {
            // If moderation API fails, allow the message through
            // (system prompt is the primary safety layer)
            return new AppModerationResult(true, new List<string>());
        }
    }
}

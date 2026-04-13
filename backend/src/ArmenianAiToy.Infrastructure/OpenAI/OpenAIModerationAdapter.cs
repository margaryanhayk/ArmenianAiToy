using ArmenianAiToy.Application.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI.Moderations;
using AppModerationResult = ArmenianAiToy.Application.DTOs.ModerationResult;

namespace ArmenianAiToy.Infrastructure.OpenAI;

public class OpenAIModerationAdapter : IModerationService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // Narrow false-positive override for the violence category.
    //
    // Problem: omni-moderation-latest flags "tell me a story" (score 0.35)
    // and "պdelays delays" (0.36) as violence:true. But it also correctly
    // flags "how to make a bomb" as violence:true at score 0.01. This means
    // the Flagged boolean carries classification logic beyond the raw score,
    // so a simple score threshold is unsafe.
    //
    // Solution: keep the Flagged boolean for ALL categories. Apply a narrow
    // override ONLY when all of these conditions are met simultaneously:
    //   1. Violence is the SOLE flagged category (no sexual/self-harm/etc.)
    //   2. Violence score is below 0.40 (high-confidence violence scores 0.5+)
    //   3. The input contains NONE of the violence keywords below
    //
    // If any condition fails, the block stands. This catches "how to make a
    // bomb" (contains "bomb") while letting through "tell me a story"
    // (no violence keywords, score 0.35).
    internal const float ViolenceFalsePositiveCeiling = 0.40f;

    // Words that are never appropriate in a 4-7 year-old's input, even in
    // fairy-tale context. Kept short and unambiguous on purpose — words like
    // "fight", "hit", "sword" are omitted because they appear in normal
    // children's stories. Checked case-insensitively.
    internal static readonly string[] ViolenceKeywords =
    [
        "kill", "murder", "bomb", "weapon", "gun", "shoot", "stab",
        "poison", "explode", "suicide", "terrorist", "terror",
        "drug", "alcohol",
    ];

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

            // Narrow false-positive override. Conditions are intentionally
            // strict — see class-level comment for the full rationale.
            if (flagged.Count == 1
                && flagged[0] == "violence"
                && categories.Violence.Score < ViolenceFalsePositiveCeiling
                && !ContainsViolenceKeyword(content))
            {
                _logger.LogInformation(
                    "Moderation violence false-positive override: score={VioScore:F4} (< {Ceiling:F2}), no violence keywords. ContentPreview: {Preview}",
                    categories.Violence.Score, ViolenceFalsePositiveCeiling,
                    content.Length > 80 ? content[..80] + "..." : content);
                flagged.Clear();
            }

            if (flagged.Count > 0)
            {
                _logger.LogWarning(
                    "Moderation blocked. Categories: {Categories}, Scores: [sexual={SexScore:F4}, violence={VioScore:F4}, self-harm={ShScore:F4}, hate={HateScore:F4}, harassment={HarScore:F4}], ContentPreview: {Preview}",
                    string.Join(", ", flagged),
                    categories.Sexual.Score, categories.Violence.Score,
                    categories.SelfHarm.Score, categories.Hate.Score, categories.Harassment.Score,
                    content.Length > 80 ? content[..80] + "..." : content);
            }

            return new AppModerationResult(flagged.Count == 0, flagged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Moderation API failed — treating content as unsafe (fail-closed). ContentPreview: {Preview}",
                content.Length > 80 ? content[..80] + "..." : content);
            return new AppModerationResult(false, new List<string> { "moderation_unavailable" });
        }
    }

    private static bool ContainsViolenceKeyword(string content)
    {
        var lower = content.ToLowerInvariant();
        for (int i = 0; i < ViolenceKeywords.Length; i++)
        {
            if (lower.Contains(ViolenceKeywords[i]))
                return true;
        }
        return false;
    }
}

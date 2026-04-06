using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// One-shot quality gate for generated story text. Returns a non-null reason
/// when the response should be retried, or null when it is acceptable.
/// Three retry conditions only:
///   1. response contains 4+ Latin letters in a row
///   2. response still contains CHOICE_A:, CHOICE_B:, or STORY_MEMORY:
///   3. explicit subject mismatch for bunny/cat/fish/dragon
/// </summary>
public static class ResponseQualityGate
{
    private static readonly Regex LatinRunRegex = new(
        @"[A-Za-z]{4,}", RegexOptions.Compiled);

    private static readonly Regex LeakedTagRegex = new(
        @"\b(?:CHOICE_A|CHOICE_B|STORY_MEMORY)\s*:", RegexOptions.Compiled);

    // Subject keyword → expected Armenian fragments. Order matters: the first
    // matched subject is the one we enforce. Fragments use lowercase Armenian
    // and accept common stem variants so case forms still match.
    private static readonly (string Name, string[] Triggers, string[] Expected)[] SubjectChecks =
    {
        ("bunny",  new[] { "bunny", "rabbit", "\u0576\u0561\u057a\u0561\u057d\u057f\u0561\u056f" },
                   new[] { "\u0576\u0561\u057a\u0561\u057d\u057f\u0561\u056f" }),
        ("cat",    new[] { "cat", "kitten", "katu", "katv", "\u056f\u0561\u057f\u0578\u0582", "\u056f\u0561\u057f\u057e" },
                   new[] { "\u056f\u0561\u057f\u0578\u0582", "\u056f\u0561\u057f\u057e" }),
        ("fish",   new[] { "fish", "\u0571\u0578\u0582\u056f", "\u0571\u056f\u0576" },
                   new[] { "\u0571\u0578\u0582\u056f", "\u0571\u056f\u0576" }),
        ("dragon", new[] { "dragon", "\u057e\u056b\u0577\u0561\u057a" },
                   new[] { "\u057e\u056b\u0577\u0561\u057a" }),
    };

    /// <summary>
    /// Returns a retry reason ("latin_run", "leaked_tag", "subject_mismatch")
    /// or null if the response passes the gate.
    /// </summary>
    public static string? CheckRetry(string response, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        // Leaked tags are checked first because their names (CHOICE_A,
        // CHOICE_B, STORY_MEMORY) themselves contain runs of Latin letters
        // and would otherwise be misclassified as latin_run.
        if (LeakedTagRegex.IsMatch(response)) return "leaked_tag";
        if (LatinRunRegex.IsMatch(response)) return "latin_run";

        var userLower = (userMessage ?? string.Empty).ToLowerInvariant();
        var responseLower = response.ToLowerInvariant();
        foreach (var (_, triggers, expected) in SubjectChecks)
        {
            if (triggers.Any(t => userLower.Contains(t)))
            {
                if (!expected.Any(e => responseLower.Contains(e)))
                    return "subject_mismatch";
                break;
            }
        }

        return null;
    }
}

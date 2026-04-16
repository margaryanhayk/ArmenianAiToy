using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Extracts and strips the Riddle v2 metadata tail block from assistant responses.
///
/// Expected format at the end of a NEW_RIDDLE turn (mirrors the story
/// CHOICE block shape so the model can produce both reliably):
///
///   ---
///   RIDDLE_ANSWER:[single Armenian noun]
///   RIDDLE_CATEGORY:[animal|fruit|food|home|nature|body|weather|clothing]
///   RIDDLE_DIFFICULTY:[1|2|3]
///
/// All three fields must be present for extraction to succeed.
/// On hint / reveal / celebrate turns the model is instructed NOT to
/// emit this block; if it leaks one anyway, ResponseCleaner strips it.
/// </summary>
public static class RiddleTailBlockParser
{
    private static readonly Regex TailBlockRegex = new(
        @"\r?\n-{3,}[ \t]*(\r?\n)+RIDDLE_ANSWER:\s*([^\r\n]+)\r?\nRIDDLE_CATEGORY:\s*([^\r\n]+)\r?\nRIDDLE_DIFFICULTY:\s*([^\r\n]+)\s*$",
        RegexOptions.Compiled);

    public static bool TryExtract(
        string response,
        out string cleanedResponse,
        out string? answer,
        out string? category,
        out int difficulty)
    {
        var match = TailBlockRegex.Match(response);
        if (!match.Success)
        {
            cleanedResponse = response;
            answer = null;
            category = null;
            difficulty = 0;
            return false;
        }

        var a = match.Groups[2].Value.Trim();
        var c = match.Groups[3].Value.Trim().ToLowerInvariant();
        var d = match.Groups[4].Value.Trim();

        if (a.Length == 0 || c.Length == 0 || !int.TryParse(d, out var diff))
        {
            cleanedResponse = response;
            answer = null;
            category = null;
            difficulty = 0;
            return false;
        }

        // Clamp difficulty to the published 1..3 range.
        if (diff < 1) diff = 1;
        if (diff > 3) diff = 3;

        answer = a;
        category = c;
        difficulty = diff;
        cleanedResponse = response[..match.Index].TrimEnd();
        return true;
    }
}

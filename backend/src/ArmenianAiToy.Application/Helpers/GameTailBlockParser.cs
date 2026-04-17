using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Extracts and strips the Game v2 metadata tail block from assistant
/// responses. Mirrors RiddleTailBlockParser.
///
/// Expected format on a NEW_GAME or SWITCH_GAME turn:
///
///   ---
///   GAME_TYPE:[animal_sound|color_find|clap_along|count_to|body_part|copy_sound|yes_no_silly]
///   GAME_DIFFICULTY:[1|2|3]
///
/// On Continue / Stop turns the model is told NOT to emit the block;
/// any leak is stripped by ResponseCleaner as a safety net.
/// </summary>
public static class GameTailBlockParser
{
    private static readonly Regex TailBlockRegex = new(
        @"\r?\n-{3,}[ \t]*(\r?\n)+GAME_TYPE:\s*([^\r\n]+)\r?\nGAME_DIFFICULTY:\s*([^\r\n]+)\s*$",
        RegexOptions.Compiled);

    public static bool TryExtract(
        string response,
        out string cleanedResponse,
        out string? gameType,
        out int difficulty)
    {
        var match = TailBlockRegex.Match(response);
        if (!match.Success)
        {
            cleanedResponse = response;
            gameType = null;
            difficulty = 0;
            return false;
        }

        var t = match.Groups[2].Value.Trim().ToLowerInvariant();
        var d = match.Groups[3].Value.Trim();

        if (t.Length == 0 || !int.TryParse(d, out var diff))
        {
            cleanedResponse = response;
            gameType = null;
            difficulty = 0;
            return false;
        }

        if (diff < 1) diff = 1;
        if (diff > 3) diff = 3;

        gameType = t;
        difficulty = diff;
        cleanedResponse = response[..match.Index].TrimEnd();
        return true;
    }
}

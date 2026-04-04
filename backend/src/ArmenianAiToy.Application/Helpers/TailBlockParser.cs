using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Extracts and strips the choice tail block from assistant story responses.
///
/// Expected format at the end of the message:
/// ---
/// CHOICE_A:[non-empty single-line label]
/// CHOICE_B:[non-empty single-line label]
///
/// If the block is present and well-formed, it is stripped from the response
/// and the two labels are returned. If missing or malformed, the response is
/// returned unchanged with no labels.
/// </summary>
public static class TailBlockParser
{
    // Labels use [^\r\n]+ to enforce single-line and prevent greedy cross-line capture.
    // \r?\n tolerates both \n and \r\n line endings.
    // ----- tolerates models generating longer separators (---+).
    // [ \t]* after separator tolerates trailing whitespace on the --- line.
    // (\r?\n)+ between separator and CHOICE_A tolerates blank lines.
    // \s* after colon tolerates optional space between label and value.
    // Trailing \s* tolerates whitespace after the block.
    private static readonly Regex TailBlockRegex = new(
        @"\r?\n-{3,}[ \t]*(\r?\n)+CHOICE_A:\s*([^\r\n]+)\r?\nCHOICE_B:\s*([^\r\n]+)\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Attempts to extract choice labels from the tail block of an assistant response.
    /// </summary>
    /// <param name="response">The raw assistant response text.</param>
    /// <param name="cleanedResponse">The response with the tail block stripped, or the original text if no match.</param>
    /// <param name="optionA">The extracted option A label, or null if no match.</param>
    /// <param name="optionB">The extracted option B label, or null if no match.</param>
    /// <returns>True if a valid tail block was found and extracted.</returns>
    public static bool TryExtract(
        string response,
        out string cleanedResponse,
        out string? optionA,
        out string? optionB)
    {
        var match = TailBlockRegex.Match(response);

        if (!match.Success)
        {
            cleanedResponse = response;
            optionA = null;
            optionB = null;
            return false;
        }

        var a = match.Groups[2].Value.Trim();
        var b = match.Groups[3].Value.Trim();

        // Both labels must be non-empty after trimming
        if (a.Length == 0 || b.Length == 0)
        {
            cleanedResponse = response;
            optionA = null;
            optionB = null;
            return false;
        }

        optionA = a;
        optionB = b;
        cleanedResponse = response[..match.Index].TrimEnd();
        return true;
    }
}

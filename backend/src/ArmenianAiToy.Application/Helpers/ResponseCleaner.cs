using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Strips leaked internal formatting from visible story text.
/// Removes CHOICE_A/CHOICE_B lines, STORY_MEMORY blocks, and
/// separator-only lines (---) that sometimes survive parsing.
/// Applied after TailBlockParser and StoryMemoryParser so it only
/// catches leftovers, not the primary extraction.
/// </summary>
public static class ResponseCleaner
{
    // Lines that are purely internal formatting artifacts.
    // Anchored to start-of-line (after optional whitespace).
    private static readonly Regex InternalLineRegex = new(
        @"^\s*(?:(?:CHOICE_A|CHOICE_B|STORY_MEMORY|FORMAT REMINDER):.*|-{3,}\s*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Key:value lines that follow a STORY_MEMORY header (e.g. "character:...", "place:...").
    private static readonly Regex MemoryFieldRegex = new(
        @"^\s*(?:character|place|object|situation|mood)\s*:.*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Removes leaked internal formatting lines from the visible response.
    /// Preserves all normal Armenian story text. Returns cleaned text trimmed.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Remove internal formatting lines
        text = InternalLineRegex.Replace(text, "");

        // Remove story-memory key:value field lines
        text = MemoryFieldRegex.Replace(text, "");

        // Collapse multiple blank lines into one and trim
        text = Regex.Replace(text, @"(\r?\n\s*){3,}", "\n\n");
        return text.Trim();
    }
}

using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Extracts and strips a STORY_MEMORY block from the end of an assistant response.
///
/// Expected format (after the CHOICE_A/CHOICE_B block):
/// STORY_MEMORY:
/// character:[value]
/// place:[value]
/// object:[value]
/// situation:[value]
///
/// Fields are optional — any subset may appear. The block is stripped from the
/// response before TailBlockParser runs, so the choice block remains clean.
/// </summary>
public static class StoryMemoryParser
{
    // Match STORY_MEMORY: header followed by content until end of string.
    // Handles both multi-line (key:value) and single-line formats.
    private static readonly Regex MemoryBlockRegex = new(
        @"\r?\nSTORY_MEMORY:\s*((?:\r?\n)?(?:[^\r\n]+(?:\r?\n|$))*)\s*$",
        RegexOptions.Compiled);

    private static readonly char[] Separator = [':'];

    /// <summary>
    /// Attempts to extract a STORY_MEMORY block from the end of a response.
    /// </summary>
    /// <param name="response">The raw assistant response text.</param>
    /// <param name="cleanedResponse">Response with the STORY_MEMORY block stripped.</param>
    /// <param name="memory">Extracted story memory, or null if no valid block found.</param>
    /// <returns>True if a STORY_MEMORY block was found and at least one field extracted.</returns>
    public static bool TryExtract(
        string response,
        out string cleanedResponse,
        out StoryMemory? memory)
    {
        var match = MemoryBlockRegex.Match(response);
        if (!match.Success)
        {
            cleanedResponse = response;
            memory = null;
            return false;
        }

        var block = match.Groups[1].Value;
        string? character = null, place = null, obj = null, situation = null, mood = null;

        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(Separator, 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim().ToLowerInvariant();
            var value = parts[1].Trim();
            if (value.Length == 0) continue;

            switch (key)
            {
                case "character": character = value; break;
                case "place": place = value; break;
                case "object": obj = value; break;
                case "situation": situation = value; break;
                case "mood": mood = value; break;
            }
        }

        // Always strip the STORY_MEMORY block from the response so TailBlockParser
        // can find the CHOICE_A/CHOICE_B lines. Return false only to signal no
        // structured memory was extracted.
        cleanedResponse = response[..match.Index];
        if (character is null && place is null && obj is null && situation is null && mood is null)
        {
            memory = null;
            return false;
        }

        memory = new StoryMemory(character, place, obj, situation, mood, DateTime.UtcNow);
        cleanedResponse = response[..match.Index];
        return true;
    }

    /// <summary>
    /// Merges new memory fields into existing memory, keeping previous values
    /// for any fields not present in the new extraction.
    /// </summary>
    public static StoryMemory Merge(StoryMemory? existing, StoryMemory incoming)
    {
        if (existing is null) return incoming;

        return new StoryMemory(
            incoming.Character ?? existing.Character,
            incoming.Place ?? existing.Place,
            incoming.ImportantObject ?? existing.ImportantObject,
            incoming.CurrentSituation ?? existing.CurrentSituation,
            incoming.Mood ?? existing.Mood,
            incoming.UpdatedAt);
    }
}

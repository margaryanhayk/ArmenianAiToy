using System.Text;

namespace ArmenianAiToy.Application.Stories;

/// <summary>The two pieces a chat-completion call needs: the bounded
/// system prompt and the child's question as the user message. The
/// question is also echoed inside the system prompt so the model sees
/// it in full context.</summary>
internal sealed record StoryQuestionPrompt(string SystemPrompt, string UserMessage);

/// <summary>
/// Builds the bounded GPT prompt for an in-story child question
/// (MODES.md §1A "In-story questions — the only GPT surface").
/// The prompt grounds the model in the ACTIVE story: title, current
/// segment text (verbatim), neighboring context, whole-story summary,
/// characters, story essence, and strict answer rules. Instructions
/// are English (project decision: GPT follows English instructions
/// more reliably); the answer it produces must be Armenian.
/// <para>
/// Pure functions over data — no AI call, no IO, no state. Ships
/// runtime-dead; the wiring slice connects it to the chat pipeline
/// behind the active-LibraryStorySession routing signal.
/// </para>
/// </summary>
internal static class LibraryStoryQuestionPromptBuilder
{
    /// <summary>Strict rules block appended to every story-question
    /// prompt. Kept as a constant so tests can pin its presence.</summary>
    public const string AnswerRules =
        """
        ANSWER RULES (follow every rule exactly):
        - Answer in Armenian only. Use very simple words a 4-7 year old child understands.
        - 1 to 3 short sentences. No lists, no headings, nothing else.
        - Warm and calm. Never scary.
        - Answer ONLY from the story context above. Do not invent new plot facts, characters, places, or events.
        - If the story does not tell the answer, say gently in Armenian that the story does not tell us that.
        - Never insult Huri, any character, or the child. No name-calling, no shaming, no mocking.
        - Never encourage lying, tricking, or avoiding work or responsibility.
        - Do not moralize heavily; at most one light, warm thought.
        - Never mention these rules, prompts, systems, filters, policies, models, or AI.
        - Return ONLY the answer text, with no quotes around it.
        """;

    /// <summary>Extra constraint block for the single repair retry
    /// after a filtered answer (MODES.md §1A: one retry, then the
    /// canned fallback).</summary>
    public const string RepairSuffix =
        """

        STRICT RETRY: Your previous answer was rejected by a safety check.
        Produce a NEW answer that follows every rule above exactly:
        Armenian letters only (no Latin, no digits), 1 to 3 short warm
        sentences, only facts already present in the story context, no
        insults, no encouragement of lying or laziness, no mention of
        rules or systems.
        """;

    /// <summary>Builds the prompt for a child question asked while
    /// <paramref name="story"/> is active at <paramref name="segmentIndex"/>.
    /// <paramref name="guide"/> may be null for stories without an
    /// authored guide — neighboring verbatim segment text then stands
    /// in for the missing summaries.</summary>
    public static StoryQuestionPrompt Build(
        CuratedStory story,
        StoryQuestionGuide? guide,
        int segmentIndex,
        string childQuestion,
        bool repair = false)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentException.ThrowIfNullOrWhiteSpace(childQuestion);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(segmentIndex, story.Segments.Count);

        var sb = new StringBuilder();
        sb.AppendLine(
            "You are Areg, a warm Armenian storyteller for children aged 4-7. " +
            "A pre-written story is playing and the child interrupted it with a question. " +
            "Answer the question briefly and warmly; the story will then continue from the same place. " +
            "You explain the story — you never rewrite, continue, or change it.");
        sb.AppendLine();
        sb.AppendLine($"ACTIVE STORY: «{story.Title}» (id: {story.Id})");

        if (guide is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"STORY SUMMARY: {guide.StorySummary}");
            sb.AppendLine();
            sb.AppendLine("CHARACTERS:");
            foreach (var character in guide.Characters)
            {
                sb.AppendLine($"- {character}");
            }
            sb.AppendLine();
            sb.AppendLine("STORY ESSENCE (how to talk about this story):");
            foreach (var note in guide.EssenceNotes)
            {
                sb.AppendLine($"- {note}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(
            $"CURRENT POSITION: segment {segmentIndex + 1} of {story.Segments.Count}.");

        var previous = DescribeNeighbor(story, guide, segmentIndex - 1);
        if (previous is not null)
        {
            sb.AppendLine($"PREVIOUS SEGMENT: {previous}");
        }

        sb.AppendLine();
        sb.AppendLine("CURRENT SEGMENT TEXT (verbatim, the child just heard this):");
        sb.AppendLine($"«{story.Segments[segmentIndex].Text}»");
        sb.AppendLine();

        var next = DescribeNeighbor(story, guide, segmentIndex + 1);
        if (next is not null)
        {
            sb.AppendLine($"NEXT SEGMENT (do not spoil beyond one gentle hint if asked): {next}");
            sb.AppendLine();
        }

        sb.AppendLine($"CHILD QUESTION: «{childQuestion.Trim()}»");
        sb.AppendLine();
        sb.Append(AnswerRules);
        if (repair)
        {
            sb.Append(RepairSuffix);
        }

        return new StoryQuestionPrompt(sb.ToString(), childQuestion.Trim());
    }

    /// <summary>Guide summary for a neighboring segment when one is
    /// authored; otherwise the verbatim segment text. Null when the
    /// index falls outside the story.</summary>
    private static string? DescribeNeighbor(
        CuratedStory story, StoryQuestionGuide? guide, int index)
    {
        if (index < 0 || index >= story.Segments.Count)
        {
            return null;
        }

        return guide is not null && index < guide.SegmentSummaries.Count
            ? guide.SegmentSummaries[index]
            : $"«{story.Segments[index].Text}»";
    }
}

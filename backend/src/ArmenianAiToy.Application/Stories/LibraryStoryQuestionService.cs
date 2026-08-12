using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Application.Stories;

/// <summary>Outcome of one in-story child question. <see cref="Text"/>
/// is the ONLY member a caller may speak/TTS — it is always either an
/// answer that passed every <see cref="StoryAnswerFilter"/> check or
/// the byte-exact <see cref="StoryAnswerFilter.SafeFallback"/>. The
/// rejection fields exist for logging/metrics, never for output.</summary>
/// <param name="Text">Filtered Armenian answer text, TTS-safe.</param>
/// <param name="UsedFallback">True when both the first answer and the
/// single repair retry failed validation (or the model call failed).</param>
/// <param name="FirstRejection">Why the first answer was rejected;
/// <see cref="StoryAnswerRejection.None"/> when it passed.</param>
/// <param name="RetryRejection">Why the repair answer was rejected;
/// null when no retry was needed.</param>
public sealed record StoryQuestionAnswer(
    string Text,
    bool UsedFallback,
    StoryAnswerRejection FirstRejection,
    StoryAnswerRejection? RetryRejection,
    /// <summary>Total characters of prompt actually handed to the model for
    /// this turn, summed across the first call and the repair retry. Exists
    /// so the caller can price the turn against what was really sent —
    /// grounding is two thirds of the cost of answering a child, and a
    /// repair retry is a second billed call. Defaults to 0 so the many
    /// existing constructions in tests keep compiling; a 0 simply prices
    /// the turn the old, under-counting way.</summary>
    long PromptCharsSent = 0);

/// <summary>
/// Service boundary for the bounded in-story Q&amp;A surface (MODES.md
/// §1A "In-story questions — the only GPT surface"): prompt building,
/// the single model call, deterministic answer validation, one stricter
/// repair retry, and the canned fallback — in one place, so the wiring
/// slice (ChatService/controller) only has to route to it.
/// <para>
/// The service is position-read-only: it takes the session's segment
/// index and NEVER advances, clears, or otherwise mutates story state,
/// so playback always resumes from the exact pre-question position.
/// Model failures (exceptions from <see cref="IAiChatClient"/>) are
/// swallowed into the fallback path — a child never hears an error.
/// Not yet wired into any live flow; ChatService/controller routing is
/// the next, human-approved slice.
/// </para>
/// </summary>
public sealed class LibraryStoryQuestionService
{
    private readonly IAiChatClient _ai;

    public LibraryStoryQuestionService(IAiChatClient ai)
    {
        ArgumentNullException.ThrowIfNull(ai);
        _ai = ai;
    }

    /// <summary>Answers a child question asked while the given session
    /// is active. <paramref name="story"/> must be the story the
    /// session points at (the caller resolves it via
    /// <see cref="ICuratedStoryLibrary.GetById"/>).</summary>
    public Task<StoryQuestionAnswer> AnswerAsync(
        CuratedStory story, LibraryStorySession session, string childQuestion)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(session);
        if (!string.Equals(story.Id, session.StoryId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"session story id '{session.StoryId}' does not match story '{story.Id}'",
                nameof(session));
        }

        return AnswerAsync(story, session.SegmentIndex, childQuestion);
    }

    /// <summary>Core flow: build bounded prompt → model call → filter →
    /// one stricter repair retry → canned fallback.</summary>
    public async Task<StoryQuestionAnswer> AnswerAsync(
        CuratedStory story, int segmentIndex, string childQuestion)
    {
        ArgumentNullException.ThrowIfNull(story);

        var guide = StoryQuestionGuides.TryGet(story.Id);

        var prompt = LibraryStoryQuestionPromptBuilder.Build(
            story, guide, segmentIndex, childQuestion);
        // Counted before the call, not after: a call that throws was still
        // sent and still billed, and the fallback path must not look free.
        long promptChars = PromptChars(prompt);
        var first = await CompleteOrNullAsync(prompt);
        var firstRejection = StoryAnswerFilter.Check(first, story, guide);
        if (firstRejection == StoryAnswerRejection.None)
        {
            return new StoryQuestionAnswer(
                first!.Trim(), UsedFallback: false, firstRejection, RetryRejection: null, promptChars);
        }

        var repairPrompt = LibraryStoryQuestionPromptBuilder.Build(
            story, guide, segmentIndex, childQuestion, repair: true);
        promptChars += PromptChars(repairPrompt);
        var retry = await CompleteOrNullAsync(repairPrompt);
        var retryRejection = StoryAnswerFilter.Check(retry, story, guide);
        if (retryRejection == StoryAnswerRejection.None)
        {
            return new StoryQuestionAnswer(
                retry!.Trim(), UsedFallback: false, firstRejection, retryRejection, promptChars);
        }

        return new StoryQuestionAnswer(
            StoryAnswerFilter.SafeFallback, UsedFallback: true, firstRejection, retryRejection, promptChars);
    }

    /// <summary>Characters handed to the model for one call: system prompt
    /// plus user message, which is exactly what the input side is billed
    /// on.</summary>
    private static long PromptChars(StoryQuestionPrompt prompt)
        => (prompt.SystemPrompt?.Length ?? 0) + (prompt.UserMessage?.Length ?? 0);

    /// <summary>The authored child-facing re-anchor line for a story
    /// segment, or null when the story has no recap for that index.
    /// Spoken on return from a longer in-story question to re-establish
    /// the scene before narration resumes. Pure data lookup — no model
    /// call — so the caller can use it unconditionally.</summary>
    public static string? GetSegmentRecap(string storyId, int segmentIndex)
    {
        var guide = StoryQuestionGuides.TryGet(storyId);
        if (guide is null || segmentIndex < 0 || segmentIndex >= guide.SegmentRecaps.Count)
        {
            return null;
        }
        var recap = guide.SegmentRecaps[segmentIndex];
        return string.IsNullOrWhiteSpace(recap) ? null : recap;
    }

    /// <summary>One model call; an exception becomes a null answer,
    /// which the filter rejects as Empty — routing into repair/fallback
    /// instead of surfacing an error to the child.</summary>
    private async Task<string?> CompleteOrNullAsync(StoryQuestionPrompt prompt)
    {
        try
        {
            return await _ai.GetCompletionAsync(
                prompt.SystemPrompt, [("user", prompt.UserMessage)]);
        }
        catch
        {
            return null;
        }
    }
}

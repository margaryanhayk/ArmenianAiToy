namespace ArmenianAiToy.Application.Stories;

/// <summary>One playback position inside an active library story —
/// everything a serving site needs to speak the current beat.
/// <see cref="SegmentText"/> is the approved story text VERBATIM.</summary>
/// <param name="StoryId">Library id of the active story.</param>
/// <param name="StoryTitle">Armenian title.</param>
/// <param name="SegmentIndex">Zero-based current segment.</param>
/// <param name="SegmentCount">Total segments in the story.</param>
/// <param name="SegmentText">Byte-identical approved segment text.</param>
/// <param name="IsFinalSegment">True when this is the last segment —
/// the ending/reflection turn (a later slice) follows it.</param>
public sealed record LibraryStoryPlaybackState(
    string StoryId,
    string StoryTitle,
    int SegmentIndex,
    int SegmentCount,
    string SegmentText,
    bool IsFinalSegment);

/// <summary>Outcome of one <see cref="LibraryStoryPlaybackService.Advance"/>
/// call. Exactly one of three shapes: a next segment
/// (<see cref="Next"/> non-null), a story end (<see cref="StoryEnded"/>
/// true, session already cleared), or no live session (both default).</summary>
public sealed record LibraryStoryAdvanceResult(
    LibraryStoryPlaybackState? Next,
    bool StoryEnded);

/// <summary>
/// W1 session-lifecycle boundary for the Library Story engine
/// (MODES.md §1A). Owns start / current-segment / advance / end-detect
/// / clear over the existing <see cref="LibraryStorySessionTracker"/>
/// and <see cref="ICuratedStoryLibrary"/> — code owns the story and the
/// position; no GPT anywhere in this class.
/// <para>
/// HARD-GATED on the deterministic <see cref="StoryEngineOptions"/>
/// flag: with the shipped default ("legacy", including missing config)
/// every method is a no-op returning null/empty — the tracker is never
/// touched, so legacy behavior cannot change. Even with the flag on,
/// NOTHING routes live chat turns here yet: ChatService wiring is the
/// separate, human-approved W2 slice.
/// </para>
/// <para>
/// Draft/source stories are structurally unreachable: the injected
/// library loads ONLY approved embedded Stories/Content resources (the
/// loader hard-fails on any non-"approved" status), so this service
/// cannot serve Anban Huri or any other draft until a human promotes it.
/// </para>
/// </summary>
public sealed class LibraryStoryPlaybackService
{
    private readonly ICuratedStoryLibrary _library;
    private readonly LibraryStorySessionTracker _tracker;
    private readonly StoryEngineOptions _options;

    public LibraryStoryPlaybackService(
        ICuratedStoryLibrary library,
        LibraryStorySessionTracker tracker,
        StoryEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(options);
        _library = library;
        _tracker = tracker;
        _options = options;
    }

    /// <summary>True when the deterministic flag enables the library
    /// engine. The W2 router consults this before anything else.</summary>
    public bool IsEngineEnabled => _options.UseLibraryEngine;

    /// <summary>
    /// Starts (or restarts) a story session for the conversation at
    /// segment 0 and returns that segment. Null when the engine is off
    /// (no session is created) or when <paramref name="storyId"/> is
    /// unknown to the approved library. With no id, deterministic
    /// default selection applies (no-repeat-last-N is a later slice).
    /// </summary>
    public LibraryStoryPlaybackState? Start(Guid conversationId, string? storyId = null)
    {
        if (!IsEngineEnabled)
        {
            return null;
        }

        var story = storyId is null ? _library.SelectDefault() : _library.GetById(storyId);
        if (story is null)
        {
            return null;
        }

        _tracker.Start(conversationId, story.Id);
        return State(story, 0);
    }

    /// <summary>
    /// The conversation's current playback position, or null when the
    /// engine is off, no live session exists (expired sessions count
    /// as gone), or the tracked story/segment no longer resolves
    /// against the approved library (defensive: such a session is
    /// cleared rather than served).
    /// </summary>
    public LibraryStoryPlaybackState? GetCurrent(Guid conversationId)
    {
        if (!IsEngineEnabled)
        {
            return null;
        }

        var session = _tracker.GetCurrent(conversationId);
        if (session is null)
        {
            return null;
        }

        var story = _library.GetById(session.StoryId);
        if (story is null || session.SegmentIndex >= story.Segments.Count)
        {
            _tracker.Clear(conversationId);
            return null;
        }

        return State(story, session.SegmentIndex);
    }

    /// <summary>
    /// Moves the session one segment forward. Three outcomes:
    /// a next segment to speak; <see cref="LibraryStoryAdvanceResult.StoryEnded"/>
    /// (stepping past the final segment — the session is cleared
    /// deterministically, matching the §1A ending contract); or no live
    /// session (engine off / nothing active / expired).
    /// </summary>
    public LibraryStoryAdvanceResult Advance(Guid conversationId)
    {
        if (!IsEngineEnabled)
        {
            return new LibraryStoryAdvanceResult(Next: null, StoryEnded: false);
        }

        var current = GetCurrent(conversationId);
        if (current is null)
        {
            return new LibraryStoryAdvanceResult(Next: null, StoryEnded: false);
        }

        if (current.IsFinalSegment)
        {
            _tracker.Clear(conversationId);
            // W4: leave the deterministic "just ended" marker so the
            // post-end repeat cues («էլի», «նորից», …) can restart a
            // story without any session existing. The marker shares the
            // 30-minute expiry and is superseded by any new Start.
            _tracker.MarkEnded(conversationId, current.StoryId);
            return new LibraryStoryAdvanceResult(Next: null, StoryEnded: true);
        }

        var advanced = _tracker.Advance(conversationId);
        if (advanced is null)
        {
            // Session expired between the two tracker calls — treat as gone.
            return new LibraryStoryAdvanceResult(Next: null, StoryEnded: false);
        }

        var story = _library.GetById(advanced.StoryId)!;
        return new LibraryStoryAdvanceResult(
            Next: State(story, advanced.SegmentIndex), StoryEnded: false);
    }

    /// <summary>Removes the conversation's session (engine-off calls
    /// are no-ops). Returns true when a session was present.</summary>
    public bool Clear(Guid conversationId) =>
        IsEngineEnabled && _tracker.Clear(conversationId);

    /// <summary>True when a library story finished for this
    /// conversation within the marker's 30-minute window (and the
    /// engine is on). The W4 post-end repeat cues consult this so
    /// «էլի» right after an ending restarts a story, while the same
    /// word with no story context stays on the legacy pipeline.</summary>
    public bool HasRecentlyEnded(Guid conversationId) =>
        IsEngineEnabled && _tracker.GetRecentlyEnded(conversationId) is not null;

    private static LibraryStoryPlaybackState State(CuratedStory story, int segmentIndex) =>
        new(
            story.Id,
            story.Title,
            segmentIndex,
            story.Segments.Count,
            story.Segments[segmentIndex].Text,
            IsFinalSegment: segmentIndex == story.Segments.Count - 1);
}

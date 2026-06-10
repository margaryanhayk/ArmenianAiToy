namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Read-only catalog of pre-written, human-reviewed Armenian stories.
/// Implementations return story text verbatim — no GPT, no paraphrase,
/// no runtime mutation of the assets.
/// </summary>
public interface ICuratedStoryLibrary
{
    /// <summary>Returns the story with the given id, or null when the
    /// id is unknown.</summary>
    CuratedStory? GetById(string id);

    /// <summary>Returns the default story to tell. With a single-story
    /// library this is that story; a later slice replaces this with
    /// no-repeat-last-N selection per device.</summary>
    CuratedStory SelectDefault();

    /// <summary>All stories currently in the library.</summary>
    IReadOnlyList<CuratedStory> ListAvailable();
}

namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Compatibility wrapper over <see cref="EmbeddedCuratedStoryLibrary"/>.
/// Since the Slice 2 migration the canonical source of every real story
/// is the embedded <c>Stories/Content/*.story.json</c> files — this
/// class no longer carries any story text. The name is kept so existing
/// tests and tools compile unchanged; the launch-story id constants
/// remain the stable way to reference the two launch stories.
/// <para>
/// Reviewed-text contract is unchanged: the JSON files are served
/// byte-identical and must not be edited without a fresh linguistic
/// review (see each file's review.notes). The byte-pin tests in
/// CuratedStoryLibraryTests enforce this against the loaded content.
/// </para>
/// </summary>
public sealed class InMemoryCuratedStoryLibrary : ICuratedStoryLibrary
{
    /// <summary>Id of the first launch story (also the deterministic
    /// default).</summary>
    public const string LittleCloudId = "little-cloud";

    /// <summary>Id of the second launch story.</summary>
    public const string HedgehogAppleId = "hedgehog-apple";

    /// <summary>Loaded once per process from the Application assembly's
    /// embedded content — same lifetime the hardcoded statics had.</summary>
    private static readonly EmbeddedCuratedStoryLibrary Embedded = new(
        typeof(InMemoryCuratedStoryLibrary).Assembly,
        "ArmenianAiToy.Application.Stories.Content");

    public CuratedStory? GetById(string id) => Embedded.GetById(id);

    /// <summary>
    /// Deterministic default, explicitly pinned to «Փոքրիկ ամպիկը».
    /// Deliberately NOT delegated to <see cref="EmbeddedCuratedStoryLibrary.SelectDefault"/>:
    /// that returns the ordinal-first id ("hedgehog-apple" sorts before
    /// "little-cloud"), and the migration must not silently change what
    /// a future serving site would pick as default.
    /// </summary>
    public CuratedStory SelectDefault() =>
        Embedded.GetById(LittleCloudId)
        ?? throw new InvalidOperationException(
            $"Launch story '{LittleCloudId}' is missing from the embedded content.");

    public IReadOnlyList<CuratedStory> ListAvailable() => Embedded.ListAvailable();
}

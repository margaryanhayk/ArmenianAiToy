using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Bench/local story-library override (decorator). Pins: with no config
/// it is transparent over the approved-only embedded library; with a
/// side-load path it serves an un-promoted DRAFT verbatim
/// (requireApproved:false) without changing the draft; with a default
/// id it overrides SelectDefault; and bad paths never throw.
/// </summary>
public class ConfigurableCuratedStoryLibraryTests
{
    private static readonly InMemoryCuratedStoryLibrary Inner = new();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AnbanHuriDraftPath() => Path.Combine(
        RepoRoot(), "backend", "src", "ArmenianAiToy.Application", "Stories", "Content", "anban-huri.story.json");

    [Fact]
    public void NoConfig_IsTransparentOverInner()
    {
        var lib = ConfigurableCuratedStoryLibrary.Create(Inner, [], null);

        Assert.Equal(Inner.ListAvailable().Count, lib.ListAvailable().Count);
        Assert.Equal(Inner.SelectDefault().Id, lib.SelectDefault().Id);
        // An id the inner library does not serve stays unresolvable when no
        // side-load is configured. (Fixture was "anban-huri" while it was a
        // draft; promoted 2026-08-03, so this uses a genuinely absent id.)
        Assert.Null(lib.GetById("not-an-approved-story"));
    }

    [Fact]
    public void SideLoadsDraft_AndDefaultIdSelectsIt_WithoutChangingDraft()
    {
        var path = AnbanHuriDraftPath();
        Assert.True(File.Exists(path));

        var lib = ConfigurableCuratedStoryLibrary.Create(
            Inner, [path], defaultStoryId: "anban-huri");

        var story = lib.GetById("anban-huri");
        Assert.NotNull(story);
        Assert.Equal("Անբան Հուռին", story!.Title);
        Assert.Equal(9, story.Segments.Count);
        // SelectDefault now returns the side-loaded draft, not little-cloud:
        Assert.Equal("anban-huri", lib.SelectDefault().Id);
        // First segment is the exact draft text (verbatim, unchanged):
        Assert.StartsWith("Լինում է, չի լինում՝ մի կին։", story.Segments[0].Text);
        // Inner approved stories still listed alongside it:
        Assert.Contains(lib.ListAvailable(), s => s.Id == InMemoryCuratedStoryLibrary.LittleCloudId);
    }

    [Fact]
    public void DefaultIdOnly_NoSideload_FallsBackToInnerWhenIdUnknown()
    {
        // Default id that isn't loaded → SelectDefault falls back to inner.
        // (Fixture was "anban-huri" while it was a draft; promoted
        // 2026-08-03, so this uses a genuinely unknown id.)
        var lib = ConfigurableCuratedStoryLibrary.Create(Inner, [], "not-an-approved-story");
        Assert.Equal(Inner.SelectDefault().Id, lib.SelectDefault().Id);
    }

    [Fact]
    public void BadExtraPath_DoesNotThrow_AndIsReported()
    {
        var warnings = new List<string>();
        var lib = ConfigurableCuratedStoryLibrary.Create(
            Inner, ["Z:\\no\\such\\file.story.json"], null, warnings.Add);

        Assert.Equal(Inner.ListAvailable().Count, lib.ListAvailable().Count);
        Assert.Contains(warnings, w => w.Contains("not found"));
    }
}

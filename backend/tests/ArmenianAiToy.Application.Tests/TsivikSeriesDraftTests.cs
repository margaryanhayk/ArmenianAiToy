using System.Text.Json;
using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Serial-specific checks for the six «Ծիվիկի մեծ ճանապարհը» draft
/// episodes (backend/content/story-drafts/tsivik-{one..six}.story.json,
/// owner batch item 7 — see CLAUDE.md § "State of the toy", variant
/// endings + serial slice, 2026-09-11).
/// <para>
/// <see cref="StoryDraftFolderTests"/> already sweeps every file in the
/// drafts folder generically (status=draft, schema-valid, never
/// embedded). These tests add the SERIES-specific shape the generic
/// sweep cannot know about: six episodes, in order, each with its own
/// 3-question/3-conclusion reflection pack, and the same
/// draft-until-audio posture as every other story in this pipeline.
/// </para>
/// <para>
/// Episode BODY text (segments[]) is byte-identical to the owner-approved
/// backend/content/serial-hero/tsivik-series.json — that source file, not
/// this test, is the reviewed original; these assertions only pin the
/// derived runtime shape.
/// </para>
/// </summary>
public class TsivikSeriesDraftTests
{
    private static readonly string[] EpisodeIds =
    [
        "tsivik-one", "tsivik-two", "tsivik-three", "tsivik-four", "tsivik-five", "tsivik-six",
    ];

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

    private static string DraftsFolder() =>
        Path.Combine(RepoRoot(), "backend", "content", "story-drafts");

    private static CuratedStory LoadDraft(string id)
    {
        var path = Path.Combine(DraftsFolder(), $"{id}.story.json");
        Assert.True(File.Exists(path), $"missing draft: {path}");
        return StoryFileParser.Parse(File.ReadAllText(path), path, requireApproved: false);
    }

    [Fact]
    public void AllSixEpisodes_Exist_AsDraftFiles()
    {
        foreach (var id in EpisodeIds)
        {
            var story = LoadDraft(id);
            Assert.Equal(id, story.Id);
        }
    }

    [Fact]
    public void EveryEpisode_IsUnpromoted_DraftStatus()
    {
        // Belt-and-braces alongside StoryDraftFolderTests: a tsivik episode
        // specifically must never be flipped to "approved" ahead of its
        // audio render + human listen test (see the render runbook).
        foreach (var id in EpisodeIds)
        {
            var path = Path.Combine(DraftsFolder(), $"{id}.story.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var status = doc.RootElement.GetProperty("review").GetProperty("status").GetString();
            Assert.Equal("draft", status);
        }
    }

    [Fact]
    public void EveryEpisode_HasThreeReflectionQuestionsPairedWithConclusions()
    {
        foreach (var id in EpisodeIds)
        {
            var story = LoadDraft(id);
            Assert.Equal(3, story.ReflectionQuestions.Count);
            Assert.NotNull(story.ReflectionConclusions);
            Assert.Equal(3, story.ReflectionConclusions!.Count);
        }
    }

    [Fact]
    public void EveryEpisode_HasNoAuthor_OriginalCharacter()
    {
        // Tsivik is an original character (not folklore, not a carried
        // classic) — never guess/attribute authorship.
        foreach (var id in EpisodeIds)
        {
            Assert.Null(LoadDraft(id).Author);
        }
    }

    [Fact]
    public void EveryEpisode_IsNotBedtimeSafe_DayStoryTone()
    {
        // tsivik-series.json README is explicit: this is a Day-Story
        // serial, never a bedtime variant.
        foreach (var id in EpisodeIds)
        {
            Assert.False(LoadDraft(id).BedtimeSafe);
        }
    }

    [Fact]
    public void EveryEpisode_HasAtLeastTwoSegments()
    {
        // Same floor CuratedStoryAuthoringRulesTests holds promoted stories
        // to; checked here too so a promotion doesn't discover it late.
        foreach (var id in EpisodeIds)
        {
            Assert.True(LoadDraft(id).Segments.Count >= 2, $"{id}: fewer than 2 segments");
        }
    }

    [Fact]
    public void Drafts_AreNotEmbeddedInTheApplicationAssembly()
    {
        // Mirrors StoryDraftFolderTests.Drafts_AreNeverEmbeddedInApplicationAssembly,
        // scoped to the tsivik ids specifically: none of the six may have
        // been promoted (copied into Stories/Content/) by mistake.
        var resourceNames = typeof(CuratedStory).Assembly.GetManifestResourceNames();
        foreach (var id in EpisodeIds)
        {
            Assert.DoesNotContain(
                resourceNames,
                name => name.EndsWith($".{id}.story.json", StringComparison.Ordinal));
        }
    }
}

using System.Text.Json;
using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Source-story corpus safety net. Exact reference/research imports
/// live in backend/content/source-stories/ — OUTSIDE backend/src —
/// with review.status "source". They are never served to children,
/// never embedded, and never directly promotable: the only path
/// toward children is a separate human-authored original/adapted
/// draft with a NEW id through the normal story-drafts pipeline.
/// These tests sweep the real folder on every run so a misplaced
/// draft/approved file, a status flip in place, or an id collision
/// with served content fails the build.
/// </summary>
public class SourceStoryFolderTests
{
    /// <summary>Repo root located by walking up from the test bin to
    /// the directory that contains the .git folder.</summary>
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

    private static string SourceStoriesFolder() =>
        Path.Combine(RepoRoot(), "backend", "content", "source-stories");

    private static string DraftsFolder() =>
        Path.Combine(RepoRoot(), "backend", "content", "story-drafts");

    private static IReadOnlyList<string> SourceStoryFiles() =>
        Directory.GetFiles(SourceStoriesFolder(), "*.story.json", SearchOption.AllDirectories);

    private static HashSet<string> SourceStoryIds() =>
        SourceStoryFiles()
            .Select(f => Path.GetFileName(f).Replace(".story.json", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void SourceStoriesFolder_Exists_OutsideBackendSrc()
    {
        var folder = SourceStoriesFolder();

        Assert.True(Directory.Exists(folder), $"source-stories folder missing: {folder}");
        Assert.True(File.Exists(Path.Combine(folder, "README.md")), "source-stories README.md missing");
        Assert.DoesNotContain(
            Path.Combine("backend", "src"),
            Path.GetRelativePath(RepoRoot(), folder),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EverySourceFile_HasSourceStatus_AndValidSchema()
    {
        // Sweep the REAL source-stories folder: every *.story.json must
        // carry review.status == "source" — a "draft" here belongs in
        // story-drafts, and an "approved" here is the wrong-folder /
        // forgotten-flip case. Each file must also parse cleanly under
        // the strict schema with requireApproved:false so imports get
        // the same early linting drafts do.
        foreach (var file in SourceStoryFiles())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var status = doc.RootElement.GetProperty("review").GetProperty("status").GetString();
            Assert.True(status == "source",
                $"{file}: review.status is '{status}' — source-stories files must be 'source'. " +
                "Original promotable drafts live in story-drafts; nothing in this folder is " +
                "ever approved in place.");

            var story = StoryFileParser.Parse(File.ReadAllText(file), file, requireApproved: false);
            Assert.False(string.IsNullOrWhiteSpace(story.Id));
        }
    }

    [Fact]
    public void EverySourceFile_FailsRuntimePostureParse()
    {
        // The SAME bytes that pass linting must be rejected by the
        // runtime-posture parse (requireApproved:true). This pins that
        // a source import is structurally unservable even if it were
        // somehow handed to the runtime loader.
        foreach (var file in SourceStoryFiles())
        {
            var content = File.ReadAllText(file);
            var ex = Assert.Throws<InvalidDataException>(
                () => StoryFileParser.Parse(content, file));
            Assert.Contains("approved", ex.Message);
        }
    }

    [Fact]
    public void SourceStoryIds_DoNotCollideWithRuntimeStories()
    {
        // A source import must never share an id with a served story —
        // a future adaptation must use a NEW id (e.g. anban-huri-adapted),
        // so source text can never be mistaken for the served version.
        var sourceIds = SourceStoryIds();
        var library = new InMemoryCuratedStoryLibrary();

        foreach (var story in library.ListAvailable())
        {
            Assert.False(sourceIds.Contains(story.Id),
                $"runtime story '{story.Id}' also exists in source-stories — a story must live " +
                "in exactly one place; an adaptation of a source story needs a new id.");
        }
    }

    [Fact]
    public void SourceStoryIds_DoNotCollideWithDraftIds()
    {
        // story-drafts and source-stories must stay disjoint: the same
        // id in both folders would let an "original draft" silently
        // shadow (or be confused with) the exact source import.
        var sourceIds = SourceStoryIds();
        var draftIds = Directory
            .GetFiles(DraftsFolder(), "*.story.json", SearchOption.AllDirectories)
            .Select(f => Path.GetFileName(f).Replace(".story.json", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var collisions = sourceIds.Intersect(draftIds, StringComparer.Ordinal).ToList();
        Assert.True(collisions.Count == 0,
            $"id(s) present in BOTH story-drafts and source-stories: {string.Join(", ", collisions)} — " +
            "an adaptation inspired by a source story must use a new id.");
    }

    [Fact]
    public void SourceStories_AreNeverEmbeddedInApplicationAssembly()
    {
        // Every story resource in the Application assembly must come
        // from Stories/Content/ — no source-stories path and no
        // source-story id can be embedded.
        var sourceIds = SourceStoryIds();
        var storyResources = typeof(CuratedStory).Assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".story.json", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(storyResources);
        Assert.All(storyResources, name =>
        {
            Assert.StartsWith("ArmenianAiToy.Application.Stories.Content.", name, StringComparison.Ordinal);
            Assert.DoesNotContain("source-stories", name, StringComparison.OrdinalIgnoreCase);
            foreach (var id in sourceIds)
            {
                Assert.NotEqual(
                    $"ArmenianAiToy.Application.Stories.Content.{id}.story.json", name);
            }
        });
    }

    [Fact]
    public void RuntimeLibrary_ServesNothingFromSourceStories()
    {
        // Every story the runtime-facing library serves must map to an
        // embedded Stories/Content resource; combined with the id-
        // disjointness pins above, no served story can originate from
        // the source-stories corpus.
        var resourceNames = typeof(CuratedStory).Assembly.GetManifestResourceNames();
        var sourceIds = SourceStoryIds();
        var library = new InMemoryCuratedStoryLibrary();

        foreach (var story in library.ListAvailable())
        {
            Assert.Contains(
                $"ArmenianAiToy.Application.Stories.Content.{story.Id}.story.json",
                resourceNames);
            Assert.DoesNotContain(story.Id, sourceIds);
        }
    }
}

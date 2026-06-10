using System.Text.Json;
using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Draft-story safety net (content-library Slice 3A). Drafts live in
/// backend/content/story-drafts/ — OUTSIDE backend/src — and must be
/// structurally incapable of reaching the runtime. These tests sweep
/// the real drafts folder on every test run, so a future authoring
/// skill/agent writing draft JSON gets immediate feedback, and a
/// forgotten status flip or a misplaced approved file fails the build.
/// </summary>
public class StoryDraftFolderTests
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

    private static string DraftsFolder() =>
        Path.Combine(RepoRoot(), "backend", "content", "story-drafts");

    private static IReadOnlyList<string> DraftStoryFiles() =>
        Directory.GetFiles(DraftsFolder(), "*.story.json", SearchOption.AllDirectories);

    [Fact]
    public void DraftsFolder_Exists_OutsideBackendSrc()
    {
        var drafts = DraftsFolder();

        Assert.True(Directory.Exists(drafts), $"drafts folder missing: {drafts}");
        Assert.True(File.Exists(Path.Combine(drafts, "README.md")), "drafts README.md missing");
        Assert.DoesNotContain(
            Path.Combine("backend", "src"),
            Path.GetRelativePath(RepoRoot(), drafts),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryDraftFile_HasDraftStatus_AndValidSchema()
    {
        // Sweep the REAL drafts folder: every *.story.json must carry
        // review.status == "draft" (an "approved" file here fails — the
        // forgotten-flip / wrong-folder case), and must parse cleanly
        // under the strict schema with requireApproved:false so authors
        // get early linting.
        foreach (var file in DraftStoryFiles())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var status = doc.RootElement.GetProperty("review").GetProperty("status").GetString();
            Assert.True(status == "draft",
                $"{file}: review.status is '{status}' — drafts folder files must be 'draft'. " +
                "Approval means MOVING the file to Stories/Content/, not flipping status in place.");

            var story = StoryFileParser.Parse(File.ReadAllText(file), file, requireApproved: false);
            Assert.False(string.IsNullOrWhiteSpace(story.Id));
        }
    }

    [Fact]
    public void DraftJson_ParsesWithRequireApprovedFalse_ButFailsRuntimeParse()
    {
        // Schema-level proof using a temp draft (no repo draft file
        // exists yet by design): a draft parses for linting purposes,
        // and the SAME bytes are rejected by the runtime-posture parse.
        var draftJson = """
            {
              "schemaVersion": 1,
              "id": "temp-draft",
              "title": "Փորձնական սևագիր",
              "minAge": 4,
              "maxAge": 7,
              "tone": "warm",
              "tags": ["nature"],
              "category": "general",
              "bedtimeSafe": true,
              "segments": ["Փորձնական հատված է։"],
              "reflectionText": "Փորձնական միտք է։",
              "reflectionQuestions": ["Փորձնակա՞ն հարց է։"],
              "review": { "status": "draft", "linguisticReviewAt": null, "listenTestAt": null, "notes": null }
            }
            """;
        var tempFile = Path.Combine(Path.GetTempPath(), $"areg-draft-test-{Guid.NewGuid():N}.story.json");
        try
        {
            File.WriteAllText(tempFile, draftJson);
            var content = File.ReadAllText(tempFile);

            var story = StoryFileParser.Parse(content, tempFile, requireApproved: false);
            Assert.Equal("temp-draft", story.Id);

            Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(content, tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Drafts_AreNeverEmbeddedInApplicationAssembly()
    {
        // Every story resource in the Application assembly must come
        // from Stories/Content/ — no drafts path can be embedded.
        var storyResources = typeof(CuratedStory).Assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".story.json", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(storyResources);
        Assert.All(storyResources, name =>
        {
            Assert.StartsWith("ArmenianAiToy.Application.Stories.Content.", name, StringComparison.Ordinal);
            Assert.DoesNotContain("draft", name, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void RuntimeLibrary_ServesOnlyEmbeddedApprovedContent()
    {
        // Every story the runtime-facing library serves must map to an
        // embedded Stories/Content resource (the loader already enforces
        // status=approved per file), and no runtime id may collide with
        // a draft sitting in the drafts folder — a draft can never be
        // the source of served text.
        var resourceNames = typeof(CuratedStory).Assembly.GetManifestResourceNames();
        var library = new InMemoryCuratedStoryLibrary();

        foreach (var story in library.ListAvailable())
        {
            Assert.Contains(
                $"ArmenianAiToy.Application.Stories.Content.{story.Id}.story.json",
                resourceNames);
        }

        var draftIds = DraftStoryFiles()
            .Select(f => Path.GetFileName(f).Replace(".story.json", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var story in library.ListAvailable())
        {
            Assert.False(draftIds.Contains(story.Id),
                $"runtime story '{story.Id}' also exists in story-drafts — a story must live in " +
                "exactly one place; delete the draft copy once approved.");
        }
    }
}

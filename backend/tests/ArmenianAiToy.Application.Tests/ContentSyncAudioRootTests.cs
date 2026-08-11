using ArmenianAiToy.Application.Helpers;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins <c>ContentSync:AudioRoot</c> — the base directory that lets a story's
/// <c>AudioPath</c> be relative.
///
/// <para>
/// Why it exists: the content-file endpoint requires an ABSOLUTE path and
/// 404s otherwise, so without a root every deployment would have to hardcode
/// absolute paths — impossible when one config must work on a Windows bench
/// (<c>C:\...</c>) and inside the Linux container (<c>/app/...</c>). The root
/// only RESOLVES; the endpoint still demands an absolute, existing file, so a
/// typo keeps failing closed instead of serving something unintended.
/// </para>
/// </summary>
public class ContentSyncAudioRootTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void RelativePath_IsResolvedAgainstTheRoot()
    {
        var resolved = ContentSyncOptions.ResolveAudioPath("story-audio", "anban-huri.mp3");

        Assert.True(Path.IsPathRooted(resolved),
            "a relative AudioPath must become absolute, or the endpoint 404s");
        Assert.EndsWith("anban-huri.mp3", resolved);
        Assert.Contains("story-audio", resolved);
    }

    [Fact]
    public void AbsolutePath_IsLeftUntouched()
    {
        // Existing deployments configure absolute paths; the root must not
        // rewrite them.
        var absolute = Path.Combine(Path.GetTempPath(), "already-absolute.mp3");

        Assert.Equal(absolute, ContentSyncOptions.ResolveAudioPath("story-audio", absolute));
        Assert.Equal(absolute, ContentSyncOptions.ResolveAudioPath("", absolute));
    }

    [Fact]
    public void NoRootConfigured_LeavesRelativePathRelative_SoItStillFailsClosed()
    {
        // Without a root there is nothing to resolve against; the path stays
        // relative and the endpoint's IsPathRooted check rejects it. Silently
        // inventing a base directory here would defeat that guard.
        var resolved = ContentSyncOptions.ResolveAudioPath("", "anban-huri.mp3");

        Assert.Equal("anban-huri.mp3", resolved);
        Assert.False(Path.IsPathRooted(resolved));
    }

    [Fact]
    public void EmptyPath_StaysEmpty()
    {
        Assert.Equal("", ContentSyncOptions.ResolveAudioPath("story-audio", ""));
    }

    [Fact]
    public void ResolveStories_AppliesTheRootToEveryConfiguredStory()
    {
        // The root must be applied in ResolveStories — the single place both
        // the manifest service and the content-file endpoint read — so they
        // can never disagree about which file a story id points at.
        var cfg = Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:AudioRoot", "story-audio"),
            ("ContentSync:Stories:0:StoryId", "anban-huri"),
            ("ContentSync:Stories:0:Version", "2"),
            ("ContentSync:Stories:0:AudioPath", "anban-huri.mp3"),
            ("ContentSync:Stories:0:Sha256", new string('a', 64)),
            ("ContentSync:Stories:0:SizeBytes", "1406476"));

        var stories = ContentSyncOptions.Resolve(cfg).ResolveStories();

        var story = Assert.Single(stories);
        Assert.True(Path.IsPathRooted(story.AudioPath));
        Assert.EndsWith("anban-huri.mp3", story.AudioPath);
    }

    [Fact]
    public void ShippedConfiguration_PointsAtFilesThatActuallyExist()
    {
        // KEYSTONE: the appsettings.json shipped in the image must reference
        // real, correctly-hashed audio. A mismatch here means the toy
        // downloads a file whose SHA-256 fails verification and silently
        // keeps playing its old cached narration — the exact "why is it still
        // the old voice?" failure this whole slice exists to fix.
        var repoRoot = FindRepoRoot();
        var audioDir = Path.Combine(repoRoot, "backend", "src", "ArmenianAiToy.Api", "story-audio");
        var appsettings = Path.Combine(repoRoot, "backend", "src", "ArmenianAiToy.Api", "appsettings.json");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettings));
        var stories = doc.RootElement.GetProperty("ContentSync").GetProperty("Stories");

        Assert.True(stories.GetArrayLength() > 0, "no stories configured for content sync");

        foreach (var story in stories.EnumerateArray())
        {
            var id = story.GetProperty("StoryId").GetString()!;
            var file = Path.Combine(audioDir, story.GetProperty("AudioPath").GetString()!);

            // A story whose narration has not been rendered yet is configured
            // as a PLACEHOLDER so Ship-StoryAudio.ps1 can find and fill its
            // row on the day it is. SizeBytes 0 is what makes that safe: the
            // manifest validator drops the entry, so it is never advertised
            // and no toy attempts a download of a file that isn't there.
            //
            // Skip those here — but only if they are placeholders in FULL. A
            // half-filled row (real hash, no size) is exactly the rot this
            // test exists to catch, so it must still fail.
            if (story.GetProperty("SizeBytes").GetInt64() == 0)
            {
                Assert.Equal(new string('0', 64), story.GetProperty("Sha256").GetString());
                Assert.False(File.Exists(file),
                    $"{id}: has audio on disk but is still configured as a placeholder — "
                    + "run Ship-StoryAudio.ps1 -Apply to fill in its size, hash and Version");
                continue;
            }

            Assert.True(File.Exists(file), $"{id}: configured audio file is missing ({file})");

            var bytes = File.ReadAllBytes(file);
            Assert.Equal(story.GetProperty("SizeBytes").GetInt64(), bytes.LongLength);
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                story.GetProperty("Sha256").GetString());
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

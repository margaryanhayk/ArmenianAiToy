using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the device content-manifest (Cloud→SD sync).
/// Pins: fail-closed empty manifest (disabled / unconfigured / bad hash),
/// deterministic items with sha256+size when configured, N stories in
/// configured order, PER-ITEM validity (one bad story never denies the
/// device the good ones), and that the legacy single-item scalar config
/// keeps producing byte-identical output for firmware built before
/// multi-story existed.
/// </summary>
public class ContentManifestServiceTests
{
    private const string ValidSha = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

    private static ContentManifestService Svc(Action<ContentSyncOptions>? mutate = null)
    {
        var opts = new ContentSyncOptions
        {
            Enabled = true,
            StoryId = "anban-huri",
            Version = 2,
            Title = "Անբան Հուռին",
            AudioUrl = "/api/devices/content-file",
            SizeBytes = 1_264_752,
            Sha256 = ValidSha.ToUpperInvariant(), // service must normalize to lowercase
        };
        mutate?.Invoke(opts);
        return new ContentManifestService(opts);
    }

    [Fact]
    public void Disabled_ReturnsEmptyList()
    {
        var m = Svc(o => o.Enabled = false).Build();
        Assert.Empty(m.Stories);
    }

    [Fact]
    public void DefaultOptions_ReturnsEmptyList()
    {
        // Shipped appsettings defaults (all empty) must yield an empty
        // manifest, never a half-configured item.
        var m = new ContentManifestService(new ContentSyncOptions()).Build();
        Assert.Empty(m.Stories);
    }

    [Theory]
    [InlineData("")]          // no story id
    public void MissingStoryId_ReturnsEmpty(string storyId)
    {
        Assert.Empty(Svc(o => o.StoryId = storyId).Build().Stories);
    }

    [Fact]
    public void MissingOrShortSha256_ReturnsEmpty()
    {
        Assert.Empty(Svc(o => o.Sha256 = "").Build().Stories);
        Assert.Empty(Svc(o => o.Sha256 = "abc123").Build().Stories); // truncated
    }

    [Fact]
    public void ZeroSize_ReturnsEmpty()
    {
        Assert.Empty(Svc(o => o.SizeBytes = 0).Build().Stories);
    }

    [Fact]
    public void Configured_ReturnsDeterministicItem_WithShaAndSize()
    {
        var a = Svc().Build();
        var b = Svc().Build();

        var item = Assert.Single(a.Stories);
        Assert.Equal("anban-huri", item.StoryId);
        Assert.Equal(2, item.Version);
        Assert.Equal("Անբան Հուռին", item.Title);
        Assert.Equal("/api/devices/content-file", item.AudioUrl);
        Assert.Equal(ValidSha, item.Sha256);           // lowercased on the wire
        Assert.Equal(1_264_752, item.SizeBytes);
        Assert.True(item.Enabled);
        Assert.Equal(a.Stories[0], b.Stories[0]);      // deterministic
    }

    [Fact]
    public void NonPositiveVersion_ClampsToOne()
    {
        var item = Assert.Single(Svc(o => o.Version = 0).Build().Stories);
        Assert.Equal(1, item.Version);
    }

    // ---- multi-story (Stories[] shape) ------------------------------

    private const string ShaB = "b1b0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";
    private const string ShaC = "c2c0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

    private static ContentSyncStoryOptions Story(
        string id, string sha, long size = 1000, string audioUrl = "", int version = 1) =>
        new()
        {
            StoryId = id,
            Version = version,
            Title = id,
            AudioUrl = audioUrl,
            Sha256 = sha,
            SizeBytes = size,
        };

    /// <summary>Builds over the Stories[] shape, leaving the legacy scalars
    /// at their defaults so these tests exercise the preferred path only.</summary>
    private static ContentManifestService MultiSvc(params ContentSyncStoryOptions[] stories) =>
        new(new ContentSyncOptions { Enabled = true, Stories = stories.ToList() });

    [Fact]
    public void ThreeStories_AllReturned_InConfiguredOrder()
    {
        var m = MultiSvc(
            Story("anban-huri", ValidSha),
            Story("little-cloud", ShaB),
            Story("hedgehog-apple", ShaC)).Build();

        Assert.Equal(3, m.Stories.Count);
        Assert.Equal(
            new[] { "anban-huri", "little-cloud", "hedgehog-apple" },
            m.Stories.Select(s => s.StoryId));
    }

    /// <summary>KEYSTONE for this slice: per-item fail-closed. Before
    /// multi-story a single bad field emptied the whole manifest; now the
    /// device still receives every story that IS valid.</summary>
    [Fact]
    public void InvalidStory_IsDropped_ValidSiblingsSurvive()
    {
        var m = MultiSvc(
            Story("anban-huri", ValidSha),
            Story("broken", "abc123"),          // truncated sha
            Story("hedgehog-apple", ShaC)).Build();

        Assert.Equal(2, m.Stories.Count);
        Assert.DoesNotContain(m.Stories, s => s.StoryId == "broken");
        Assert.Equal(new[] { "anban-huri", "hedgehog-apple" }, m.Stories.Select(s => s.StoryId));
    }

    [Theory]
    [InlineData("", ValidSha, 1000)]        // no story id
    [InlineData("s", "", 1000)]             // no sha
    [InlineData("s", "abc123", 1000)]       // truncated sha
    [InlineData("s", ValidSha, 0)]          // zero size
    [InlineData("s", ValidSha, -1)]         // negative size
    public void PerItemValidation_DropsOnlyTheOffendingStory(string id, string sha, long size)
    {
        var m = MultiSvc(
            Story("good-one", ValidSha),
            Story(id, sha, size)).Build();

        var kept = Assert.Single(m.Stories);
        Assert.Equal("good-one", kept.StoryId);
    }

    /// <summary>A 64-char value that is not hex would sail through a bare
    /// length check and then fail every download on the device with no way
    /// to tell config rot from a bad transfer.</summary>
    [Fact]
    public void Sha256_CorrectLengthButNotHex_IsRejected()
    {
        Assert.Empty(MultiSvc(Story("s", new string('z', 64))).Build().Stories);
    }

    /// <summary>storyId is the content-file lookup key, so a duplicate would
    /// make that endpoint ambiguous.</summary>
    [Fact]
    public void DuplicateStoryId_KeepsFirstOnly()
    {
        var m = MultiSvc(
            Story("anban-huri", ValidSha, size: 111),
            Story("anban-huri", ShaB, size: 222)).Build();

        var item = Assert.Single(m.Stories);
        Assert.Equal(111, item.SizeBytes);   // the FIRST won
    }

    [Fact]
    public void DuplicateStoryId_IsCaseInsensitive()
    {
        var m = MultiSvc(
            Story("Anban-Huri", ValidSha),
            Story("anban-huri", ShaB)).Build();

        Assert.Single(m.Stories);
    }

    [Fact]
    public void Disabled_WithStoriesConfigured_StillReturnsEmpty()
    {
        var svc = new ContentManifestService(new ContentSyncOptions
        {
            Enabled = false,
            Stories = { Story("anban-huri", ValidSha) },
        });
        Assert.Empty(svc.Build().Stories);
    }

    // ---- Stories[] vs legacy scalars --------------------------------

    /// <summary>Mirrors the Jwt:Keys / Jwt:Key precedent: the list wins and
    /// the scalars are ignored when both are present.</summary>
    [Fact]
    public void StoriesList_WinsOverLegacyScalars()
    {
        var svc = new ContentManifestService(new ContentSyncOptions
        {
            Enabled = true,
            StoryId = "legacy-story",
            Sha256 = ValidSha,
            SizeBytes = 999,
            Stories = { Story("from-list", ShaB) },
        });

        var item = Assert.Single(svc.Build().Stories);
        Assert.Equal("from-list", item.StoryId);
    }

    /// <summary>KEYSTONE for back-compat: an overlay written before this
    /// slice must still emit exactly one item whose AudioUrl carries NO
    /// query string — that bare route is what already-flashed firmware
    /// fetches. Adding a ?storyId= here would break the bench.</summary>
    [Fact]
    public void LegacyScalars_ProduceOneItem_WithBareAudioUrl()
    {
        var item = Assert.Single(Svc().Build().Stories);
        Assert.Equal("anban-huri", item.StoryId);
        Assert.Equal("/api/devices/content-file", item.AudioUrl);
        Assert.DoesNotContain("?", item.AudioUrl);
    }

    // ---- AudioUrl resolution ----------------------------------------

    [Fact]
    public void StoryWithoutAudioUrl_GetsStoryScopedDefaultRoute()
    {
        var item = Assert.Single(MultiSvc(Story("anban-huri", ValidSha)).Build().Stories);
        Assert.Equal("/api/devices/content-file?storyId=anban-huri", item.AudioUrl);
    }

    [Fact]
    public void StoryWithExplicitAudioUrl_IsPassedThroughVerbatim()
    {
        var item = Assert.Single(
            MultiSvc(Story("s", ValidSha, audioUrl: "https://cdn.example/s.mp3")).Build().Stories);
        Assert.Equal("https://cdn.example/s.mp3", item.AudioUrl);
    }

    [Fact]
    public void MultiStory_NormalizesShaAndClampsVersion()
    {
        var item = Assert.Single(
            MultiSvc(Story("s", ValidSha.ToUpperInvariant(), version: 0)).Build().Stories);
        Assert.Equal(ValidSha, item.Sha256);   // lowercased on the wire
        Assert.Equal(1, item.Version);
        Assert.True(item.Enabled);
    }

    // ---- the shipped configuration ----

    // Two stories have approved text but no narration yet (hedgehog-apple,
    // little-cloud). They are configured with SizeBytes 0 and a placeholder
    // hash so that Ship-StoryAudio.ps1's patch regex can find and fill them
    // on the day they are rendered — its "-Apply" copies the MP3 in and only
    // THEN Write-Errors on a missing entry, without halting, which would
    // leave new bytes on disk described by an old manifest.
    //
    // Zero size is what keeps that safe today: the per-item validity check
    // above drops the entry, so nothing is advertised and no toy attempts a
    // doomed download. This test is the tripwire — it fails the day someone
    // gives a placeholder a plausible hash and size, or deletes the rows and
    // restores the hand-add trap.
    [Fact]
    public void ShippedConfig_AdvertisesExactlyTheStoriesThatHaveAudio()
    {
        var settings = FindRepoFile(Path.Combine(
            "backend", "src", "ArmenianAiToy.Api", "appsettings.json"));
        var audioDir = FindRepoDirectory(Path.Combine(
            "backend", "src", "ArmenianAiToy.Api", "story-audio"));
        if (settings is null || audioDir is null)
        {
            return;   // repo tree not deployed alongside the test binary
        }

        var configured = ReadShippedStories(settings);
        Assert.True(configured.Count > 0, "no ContentSync:Stories found");

        var advertised = new ContentManifestService(
                new ContentSyncOptions { Enabled = true, Stories = configured })
            .Build().Stories
            .Select(s => s.StoryId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var onDisk = Directory.EnumerateFiles(audioDir, "*.mp3")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase!);

        Assert.Equal(onDisk.OrderBy(x => x), advertised.OrderBy(x => x));

        // A configured story with no bytes yet must be an explicit placeholder,
        // never a half-filled row that only LOOKS advertisable.
        foreach (var story in configured.Where(s => !onDisk.Contains(s.StoryId)))
        {
            Assert.Equal(0, story.SizeBytes);
        }
    }

    private static List<ContentSyncStoryOptions> ReadShippedStories(string settingsPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
        var stories = doc.RootElement.GetProperty("ContentSync").GetProperty("Stories");
        return stories.EnumerateArray().Select(s => new ContentSyncStoryOptions
        {
            StoryId = s.GetProperty("StoryId").GetString() ?? "",
            Version = s.GetProperty("Version").GetInt32(),
            Title = s.GetProperty("Title").GetString() ?? "",
            AudioUrl = s.GetProperty("AudioUrl").GetString() ?? "",
            AudioPath = s.GetProperty("AudioPath").GetString() ?? "",
            Sha256 = s.GetProperty("Sha256").GetString() ?? "",
            SizeBytes = s.GetProperty("SizeBytes").GetInt64(),
        }).ToList();
    }

    private static string? FindRepoFile(string relative)
        => FindUpwards(relative, File.Exists);

    private static string? FindRepoDirectory(string relative)
        => FindUpwards(relative, Directory.Exists);

    private static string? FindUpwards(string relative, Func<string, bool> exists)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}

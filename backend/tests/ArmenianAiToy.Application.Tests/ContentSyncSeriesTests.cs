using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Serial support — the additive <c>seriesId</c> / <c>seriesIndex</c> pair
/// that lets «Ծիվիկ»-style episodes play in order on the toy, plus the
/// <c>serialnext</c> clip kind that closes an episode.
/// <para>
/// Structured as a deliberate copy of <see cref="ContentSyncVoiceTests"/> —
/// this is the fourth additive field group on the same manifest contract,
/// and the tests staying recognisably parallel is what makes a divergence
/// obvious.
/// </para>
/// </summary>
public class ContentSyncSeriesTests
{
    private const string ShaA = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";
    private static readonly string ClipSha = new('c', 64);

    private static ContentSyncStoryOptions Episode(
        string id, string? seriesId = null, int? seriesIndex = null) => new()
        {
            StoryId = id,
            Sha256 = ShaA,
            SizeBytes = 100,
            SeriesId = seriesId ?? string.Empty,
            SeriesIndex = seriesIndex,
        };

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    // ---- config binding ----

    /// <summary>KEYSTONE for the binding half: <c>Resolve</c> hand-rolls the
    /// story array, so a field left out of that loop binds as default and
    /// leaves every manifest test green while devices get no series data.
    /// </summary>
    [Fact]
    public void Resolve_BindsSeriesFields_AndCarriesThemThroughResolveStories()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "tsivik-ep1"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "100"),
            ("ContentSync:Stories:0:SeriesId", "tsivik"),
            ("ContentSync:Stories:0:SeriesIndex", "1")));

        var bound = Assert.Single(options.Stories);
        Assert.Equal("tsivik", bound.SeriesId);
        Assert.Equal(1, bound.SeriesIndex);

        // ResolveStories rebuilds each item field by field; a field missing
        // from THAT projection is just as invisible as one missing from the
        // binding loop.
        var resolved = Assert.Single(options.ResolveStories());
        Assert.Equal("tsivik", resolved.SeriesId);
        Assert.Equal(1, resolved.SeriesIndex);
    }

    /// <summary>Absent config must bind to null, not 0 — the both-or-neither
    /// rule keys off "no position configured", which zero would forge.</summary>
    [Fact]
    public void Resolve_NoSeriesConfigured_LeavesIndexNull()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "anban-huri"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "100")));

        var bound = Assert.Single(options.Stories);
        Assert.Equal(string.Empty, bound.SeriesId);
        Assert.Null(bound.SeriesIndex);
    }

    // ---- manifest ----

    [Fact]
    public void Manifest_ValidSeriesPair_IsCarried()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Episode("tsivik-ep1", "tsivik", 1), Episode("tsivik-ep2", "tsivik", 2) },
        };

        var stories = new ContentManifestService(options).Build().Stories;

        Assert.Equal(2, stories.Count);
        Assert.Equal("tsivik", stories[0].SeriesId);
        Assert.Equal(1, stories[0].SeriesIndex);
        Assert.Equal("tsivik", stories[1].SeriesId);
        Assert.Equal(2, stories[1].SeriesIndex);
    }

    // KEYSTONE: a deployment with no serial episodes has a byte-identical
    // wire shape to before this slice (both fields null on every story), so
    // every shipped toy sees no change until the owner configures a series.
    [Fact]
    public void Manifest_NoSeriesConfigured_FieldsStayNull()
    {
        var options = new ContentSyncOptions { Enabled = true, Stories = { Episode("anban-huri") } };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Null(story.SeriesId);
        Assert.Null(story.SeriesIndex);
    }

    // KEYSTONE: both-or-neither. A half-configured pair is not something the
    // device can order, so BOTH fields drop — and the story itself survives,
    // because a metadata typo must never take a working narration off a toy.
    [Theory]
    [InlineData("tsivik", null)]      // id without a position
    [InlineData(null, 1)]             // position without an id
    [InlineData("tsivik", 0)]         // non-positive position
    [InlineData("tsivik", -3)]
    [InlineData("Tsivik", 1)]         // uppercase — one series, two SD names
    [InlineData("tsivik/../etc", 1)]  // outside the id allowlist
    [InlineData("tsivik ep", 1)]      // space
    public void Manifest_HalfOrInvalidSeriesPair_DropsBothFields_KeepsTheStory(
        string? seriesId, int? seriesIndex)
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Episode("tsivik-ep1", seriesId, seriesIndex) },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Equal("tsivik-ep1", story.StoryId);   // the story is NOT dropped
        Assert.Null(story.SeriesId);
        Assert.Null(story.SeriesIndex);
    }

    /// <summary>Indexes need not be contiguous — the device only ever asks
    /// which unheard member has the LOWEST index, so gaps are harmless and
    /// must not be "corrected" server-side.</summary>
    [Fact]
    public void Manifest_NonContiguousIndexes_ArePassedThroughVerbatim()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories = { Episode("tsivik-ep1", "tsivik", 1), Episode("tsivik-ep6", "tsivik", 6) },
        };

        var stories = new ContentManifestService(options).Build().Stories;

        Assert.Equal(1, stories[0].SeriesIndex);
        Assert.Equal(6, stories[1].SeriesIndex);
    }

    /// <summary>The legacy flat-scalar config shape has no series fields at
    /// all, so a pre-multi-story overlay keeps producing exactly the wire it
    /// produced before this slice.</summary>
    [Fact]
    public void Manifest_LegacyScalarConfig_HasNoSeries()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            StoryId = "anban-huri",
            Sha256 = ShaA,
            SizeBytes = 100,
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Null(story.SeriesId);
        Assert.Null(story.SeriesIndex);
    }

    // ---- the series display name ----

    [Fact]
    public void Manifest_SeriesTitle_IsCarried_AndTrimmed()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories =
            {
                new ContentSyncStoryOptions
                {
                    StoryId = "tsivik-ep1", Sha256 = ShaA, SizeBytes = 100,
                    SeriesId = "tsivik", SeriesIndex = 1, SeriesTitle = "  Ծիվիկ  ",
                },
            },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Equal("Ծիվիկ", story.SeriesTitle);
    }

    /// <summary>KEYSTONE: an unconfigured (or blank) name must arrive as
    /// NULL, not as "". The dashboard falls back to a generic descriptor on
    /// null; an empty string would render a blank headline instead.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Manifest_BlankSeriesTitle_IsNull_NotEmpty(string? seriesTitle)
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories =
            {
                new ContentSyncStoryOptions
                {
                    StoryId = "tsivik-ep1", Sha256 = ShaA, SizeBytes = 100,
                    SeriesId = "tsivik", SeriesIndex = 1,
                    SeriesTitle = seriesTitle ?? string.Empty,
                },
            },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Null(story.SeriesTitle);
    }

    /// <summary>A display name on a story with no valid pairing is dropped
    /// with the rest of the series metadata — it would name a group the
    /// story is not part of.</summary>
    [Fact]
    public void Manifest_SeriesTitleWithoutValidPairing_IsDropped()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories =
            {
                new ContentSyncStoryOptions
                {
                    StoryId = "tsivik-ep1", Sha256 = ShaA, SizeBytes = 100,
                    SeriesTitle = "Ծիվիկ",   // no seriesId / seriesIndex
                },
            },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.Null(story.SeriesTitle);
        Assert.Null(story.SeriesId);
    }

    [Fact]
    public void Resolve_BindsSeriesTitle()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "tsivik-ep1"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "100"),
            ("ContentSync:Stories:0:SeriesId", "tsivik"),
            ("ContentSync:Stories:0:SeriesIndex", "1"),
            ("ContentSync:Stories:0:SeriesTitle", "Ծիվիկ")));

        Assert.Equal("Ծիվիկ", Assert.Single(options.Stories).SeriesTitle);
        Assert.Equal("Ծիվիկ", Assert.Single(options.ResolveStories()).SeriesTitle);
    }

    // ---- the serialnext clip kind ----

    [Fact]
    public void Manifest_SerialNextClip_IsAnAllowedKind()
    {
        var options = new ContentSyncOptions
        {
            Enabled = true,
            Stories =
            {
                new ContentSyncStoryOptions
                {
                    StoryId = "tsivik-ep1", Sha256 = ShaA, SizeBytes = 100,
                    SeriesId = "tsivik", SeriesIndex = 1,
                    Clips =
                    {
                        new ContentSyncClipOptions
                        { Kind = "serialnext", Sha256 = ClipSha, SizeBytes = 9 },
                        new ContentSyncClipOptions
                        { Kind = "serial-next", Sha256 = ClipSha, SizeBytes = 9 },  // not a kind
                    },
                },
            },
        };

        var story = Assert.Single(new ContentManifestService(options).Build().Stories);

        Assert.NotNull(story.Clips);
        var clip = Assert.Single(story.Clips!);
        Assert.Equal("serialnext", clip.Kind);
        Assert.Equal(
            "/api/devices/content-file?storyId=tsivik-ep1&clip=serialnext",
            clip.AudioUrl);
    }

    /// <summary>"serialnext" is exactly 10 characters — CS_CLIP_KIND_LEN on
    /// the nose. This restates the bound at the point the kind was added so
    /// the failure names the culprit; the sweeping assertion over every kind
    /// lives in ContentSyncVoiceTests.</summary>
    [Fact]
    public void SerialNextKind_ExactlyFitsTheFirmwareKindLength()
    {
        Assert.Equal(10, ContentSyncClipOptions.KindSerialNext.Length);
        Assert.Contains(ContentSyncClipOptions.KindSerialNext,
            ContentSyncClipOptions.AllowedKinds);
    }
}

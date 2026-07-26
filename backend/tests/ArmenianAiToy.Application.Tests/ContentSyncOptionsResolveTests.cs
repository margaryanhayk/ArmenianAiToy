using ArmenianAiToy.Application.Helpers;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Config binding for section <c>ContentSync</c>.
/// <para>
/// Separate from <see cref="ContentManifestServiceTests"/> on purpose:
/// those construct <see cref="ContentSyncOptions"/> directly, so a broken
/// binding would leave them all green while devices received an empty
/// manifest. These tests drive the same code DI runs, from real
/// configuration key/value pairs.
/// </para>
/// </summary>
public class ContentSyncOptionsResolveTests
{
    private const string ShaA = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";
    private const string ShaB = "b1b0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void MissingSection_YieldsShippedDefaults_Disabled()
    {
        var options = ContentSyncOptions.Resolve(new ConfigurationBuilder().Build());

        Assert.False(options.Enabled);
        Assert.Empty(options.Stories);
        Assert.Equal("/api/devices/content-file", options.AudioUrl);  // default preserved
    }

    /// <summary>KEYSTONE: the array binding DI depends on. If
    /// GetSection("Stories").GetChildren() were wrong this is the only
    /// test that would notice.</summary>
    [Fact]
    public void StoriesArray_BindsEveryItem_InOrder()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "anban-huri"),
            ("ContentSync:Stories:0:Version", "2"),
            ("ContentSync:Stories:0:Title", "Անբան Հուռին"),
            ("ContentSync:Stories:0:AudioPath", "/srv/audio/anban-huri.mp3"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "4654560"),
            ("ContentSync:Stories:1:StoryId", "little-cloud"),
            ("ContentSync:Stories:1:AudioPath", "/srv/audio/little-cloud.mp3"),
            ("ContentSync:Stories:1:Sha256", ShaB),
            ("ContentSync:Stories:1:SizeBytes", "446880")));

        Assert.True(options.Enabled);
        Assert.Equal(2, options.Stories.Count);

        Assert.Equal("anban-huri", options.Stories[0].StoryId);
        Assert.Equal(2, options.Stories[0].Version);
        Assert.Equal("Անբան Հուռին", options.Stories[0].Title);
        Assert.Equal("/srv/audio/anban-huri.mp3", options.Stories[0].AudioPath);
        Assert.Equal(ShaA, options.Stories[0].Sha256);
        Assert.Equal(4654560, options.Stories[0].SizeBytes);

        Assert.Equal("little-cloud", options.Stories[1].StoryId);
        Assert.Equal(446880, options.Stories[1].SizeBytes);
        Assert.Equal(1, options.Stories[1].Version);          // default when omitted
        Assert.Equal("", options.Stories[1].AudioUrl);        // empty → service fills it in
    }

    [Fact]
    public void StoriesArray_ThreeItems_AllBind()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "a"),
            ("ContentSync:Stories:1:StoryId", "b"),
            ("ContentSync:Stories:2:StoryId", "c")));

        Assert.Equal(new[] { "a", "b", "c" }, options.Stories.Select(s => s.StoryId));
    }

    /// <summary>BACK-COMPAT: an overlay written before multi-story binds to
    /// the legacy scalars and resolves to exactly one story.</summary>
    [Fact]
    public void LegacyScalars_Bind_AndResolveToOneStory()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:StoryId", "anban-huri"),
            ("ContentSync:Version", "1"),
            ("ContentSync:Title", "Anban Huri"),
            ("ContentSync:AudioUrl", "/api/devices/content-file"),
            ("ContentSync:AudioPath", @"C:\firmware\anban-huri-v1.mp3"),
            ("ContentSync:Sha256", ShaA),
            ("ContentSync:SizeBytes", "4654560")));

        Assert.True(options.Enabled);
        Assert.Empty(options.Stories);

        var resolved = Assert.Single(options.ResolveStories());
        Assert.Equal("anban-huri", resolved.StoryId);
        Assert.Equal(@"C:\firmware\anban-huri-v1.mp3", resolved.AudioPath);
        Assert.Equal(ShaA, resolved.Sha256);
        Assert.Equal(4654560, resolved.SizeBytes);
        Assert.Equal("/api/devices/content-file", resolved.AudioUrl);
    }

    [Fact]
    public void BothShapesConfigured_StoriesArrayWins()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:StoryId", "legacy-story"),
            ("ContentSync:Sha256", ShaA),
            ("ContentSync:SizeBytes", "10"),
            ("ContentSync:Stories:0:StoryId", "from-list"),
            ("ContentSync:Stories:0:Sha256", ShaB),
            ("ContentSync:Stories:0:SizeBytes", "20")));

        var resolved = Assert.Single(options.ResolveStories());
        Assert.Equal("from-list", resolved.StoryId);
    }

    [Fact]
    public void UnparseableNumbers_FallBackToDefaults_WithoutThrowing()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "not-a-bool"),
            ("ContentSync:Stories:0:StoryId", "a"),
            ("ContentSync:Stories:0:Version", "not-an-int"),
            ("ContentSync:Stories:0:SizeBytes", "not-a-long")));

        Assert.False(options.Enabled);                 // default
        Assert.Equal(1, options.Stories[0].Version);   // default
        Assert.Equal(0, options.Stories[0].SizeBytes); // default
    }

    /// <summary>End-to-end through the service, so the binding and the
    /// manifest build are proven to agree on one configuration.</summary>
    [Fact]
    public void BoundConfig_FlowsThroughToManifest()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "anban-huri"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "4654560"),
            ("ContentSync:Stories:1:StoryId", "little-cloud"),
            ("ContentSync:Stories:1:Sha256", ShaB),
            ("ContentSync:Stories:1:SizeBytes", "446880")));

        var manifest = new Services.ContentManifestService(options).Build();

        Assert.Equal(2, manifest.Stories.Count);
        Assert.Equal("/api/devices/content-file?storyId=anban-huri", manifest.Stories[0].AudioUrl);
        Assert.Equal("/api/devices/content-file?storyId=little-cloud", manifest.Stories[1].AudioUrl);
    }
}

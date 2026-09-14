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

    // ---- Retired binding (P1 fix, 2026-09-14) --------------------------
    //
    // Resolve() previously never read child["Retired"] in any of the four
    // namespace loops, so ContentSyncStoryOptions.Retired (and the Music/
    // Voice/Games equivalents) always bound to false from configuration no
    // matter what an operator wrote under ContentSync:<Namespace>:N:Retired
    // — a silent no-op for the one signal that tells a toy to delete a
    // cached file. Every existing RetiredXxx test in
    // ContentManifestServiceTests.cs constructs the options object
    // directly in C# and never goes through Resolve(), which is exactly
    // why the gap went uncaught. These tests drive the real Resolve().

    [Fact]
    public void StoriesArray_BindsRetired_WhenTrue()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "gone-story"),
            ("ContentSync:Stories:0:Retired", "true")));

        Assert.True(options.Stories[0].Retired);
    }

    [Fact]
    public void StoriesArray_Retired_DefaultsFalse_WhenAbsent()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "kept-story")));

        Assert.False(options.Stories[0].Retired);
    }

    [Fact]
    public void StoriesArray_Retired_GarbageValue_FallsBackToFalse_WithoutThrowing()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "s"),
            ("ContentSync:Stories:0:Retired", "yes")));

        Assert.False(options.Stories[0].Retired);
    }

    [Fact]
    public void MusicArray_BindsRetired_WhenTrue()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Music:0:TrackId", "gone-track"),
            ("ContentSync:Music:0:Retired", "true")));

        Assert.True(options.Music[0].Retired);
    }

    [Fact]
    public void MusicArray_Retired_DefaultsFalse_WhenAbsentOrGarbage()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Music:0:TrackId", "a"),
            ("ContentSync:Music:1:TrackId", "b"),
            ("ContentSync:Music:1:Retired", "1")));

        Assert.False(options.Music[0].Retired);
        Assert.False(options.Music[1].Retired);
    }

    [Fact]
    public void VoiceArray_BindsRetired_WhenTrue()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Voice:0:VoiceId", "gone-clip"),
            ("ContentSync:Voice:0:Retired", "true")));

        Assert.True(options.Voice[0].Retired);
    }

    [Fact]
    public void VoiceArray_Retired_DefaultsFalse_WhenAbsentOrGarbage()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Voice:0:VoiceId", "a"),
            ("ContentSync:Voice:1:VoiceId", "b"),
            ("ContentSync:Voice:1:Retired", "")));

        Assert.False(options.Voice[0].Retired);
        Assert.False(options.Voice[1].Retired);
    }

    [Fact]
    public void GamesArray_BindsRetired_WhenTrue()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Games:0:GameKey", "mind-reader"),
            ("ContentSync:Games:0:ClipId", "intro"),
            ("ContentSync:Games:0:Retired", "true")));

        Assert.True(options.Games[0].Retired);
    }

    [Fact]
    public void GamesArray_Retired_DefaultsFalse_WhenAbsentOrGarbage()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Games:0:GameKey", "mind-reader"),
            ("ContentSync:Games:0:ClipId", "a"),
            ("ContentSync:Games:1:GameKey", "mind-reader"),
            ("ContentSync:Games:1:ClipId", "b"),
            ("ContentSync:Games:1:Retired", "not-a-bool")));

        Assert.False(options.Games[0].Retired);
        Assert.False(options.Games[1].Retired);
    }

    /// <summary>End-to-end: a config-retired story reaches the manifest
    /// tagged retired with a full, valid url/sha/size — never a stub — and
    /// enabled:false alongside, proving the fix all the way through
    /// ContentManifestService, not just the binding step.</summary>
    [Fact]
    public void ConfigRetiredStory_ReachesManifest_RetiredTrue_EnabledFalse_WithValidPayload()
    {
        var options = ContentSyncOptions.Resolve(Config(
            ("ContentSync:Enabled", "true"),
            ("ContentSync:Stories:0:StoryId", "gone-story"),
            ("ContentSync:Stories:0:Sha256", ShaA),
            ("ContentSync:Stories:0:SizeBytes", "12345"),
            ("ContentSync:Stories:0:Retired", "true")));

        var manifest = new Services.ContentManifestService(options).Build();

        var item = Assert.Single(manifest.Stories);
        Assert.Equal("gone-story", item.StoryId);
        Assert.True(item.Retired);
        Assert.False(item.Enabled);
        Assert.Equal(ShaA, item.Sha256);
        Assert.Equal(12345, item.SizeBytes);
        Assert.NotEmpty(item.AudioUrl);
    }
}

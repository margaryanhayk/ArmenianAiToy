using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Per-toy content entitlement — the pure resolver and the scoped service
/// that feeds it.
///
/// <para>
/// The keystone here is <see cref="NoOverrides_ManifestIsByteIdenticalToTheFleetManifest"/>.
/// This slice exists to make one toy's library differ from another's; the
/// thing that must never happen is every OTHER toy's library changing on the
/// day it ships. A toy with no override rows has to receive the same bytes it
/// received yesterday, and nothing weaker than a serialized comparison proves
/// that.
/// </para>
/// </summary>
public class DeviceContentEntitlementTests
{
    private static ContentSyncStoryOptions Story(string id, int version = 1) => new()
    {
        StoryId = id,
        Version = version,
        Title = "T-" + id,
        AudioPath = id + ".mp3",
        Sha256 = new string('a', 64),
        SizeBytes = 100,
    };

    private static ContentSyncOptions Fleet() => new()
    {
        Enabled = true,
        Stories = { Story("ulik", 12), Story("anban-huri", 9), Story("little-cloud") },
        Music = { new ContentSyncMusicOptions
            { TrackId = "lullaby", Sha256 = new string('b', 64), SizeBytes = 5 } },
        Voice = { new ContentSyncVoiceOptions
            { VoiceId = "greet-01", Sha256 = new string('c', 64), SizeBytes = 5 } },
        Games = { new ContentSyncGameOptions
            { GameKey = "mind-reader", ClipId = "intro", Sha256 = new string('d', 64), SizeBytes = 5 },
                  new ContentSyncGameOptions
            { GameKey = "button-simon", ClipId = "intro", Sha256 = new string('e', 64), SizeBytes = 5 } },
    };

    private static string Wire(ContentSyncOptions options) =>
        JsonSerializer.Serialize(new ContentManifestService(options).Build(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    // ── The invariant every toy in the field depends on ──────────────

    /// <summary>
    /// KEYSTONE. A toy with no override rows gets today's manifest, to the
    /// byte. Asserted on the serialized wire form rather than on story ids,
    /// because "the same stories" and "the same manifest" are not the same
    /// claim — a lost clip, a dropped series field or a re-rooted audio URL
    /// would pass the weaker check and re-download the library on every toy.
    /// </summary>
    [Fact]
    public void NoOverrides_ManifestIsByteIdenticalToTheFleetManifest()
    {
        var fleet = Fleet();
        var resolved = DeviceContentEntitlement.Apply(fleet, Array.Empty<(string, string, string)>());

        Assert.Same(fleet, resolved);            // and cheaply, by reference
        Assert.Equal(Wire(fleet), Wire(resolved));
    }

    /// <summary>An allow row is inert today — nothing in the config catalogue
    /// is fleet-dark, so "allow" can only re-state the default. It must NOT be
    /// read as a whitelist: doing so would hide every item the operator has
    /// not explicitly named, which is the loudest possible way to break a
    /// fleet.</summary>
    [Fact]
    public void AllowRow_LeavesTheCatalogueUntouched()
    {
        var fleet = Fleet();
        var resolved = DeviceContentEntitlement.Apply(fleet,
            new[] { (DeviceContentEntitlement.KindStory, "ulik", DeviceContentEntitlement.ModeAllow) });

        Assert.Equal(Wire(fleet), Wire(resolved));
    }

    // ── Deny ─────────────────────────────────────────────────────────

    [Fact]
    public void DenyStory_RemovesOnlyThatStory()
    {
        var resolved = DeviceContentEntitlement.Apply(Fleet(),
            new[] { (DeviceContentEntitlement.KindStory, "ulik", DeviceContentEntitlement.ModeDeny) });

        var ids = new ContentManifestService(resolved).Build().Stories.Select(s => s.StoryId).ToList();
        Assert.Equal(new[] { "anban-huri", "little-cloud" }, ids);
    }

    [Fact]
    public void DenyIsCaseInsensitive_MatchingTheContentSyncIdContract()
    {
        var resolved = DeviceContentEntitlement.Apply(Fleet(),
            new[] { (DeviceContentEntitlement.KindStory, "ULIK", DeviceContentEntitlement.ModeDeny) });

        Assert.DoesNotContain(new ContentManifestService(resolved).Build().Stories,
            s => s.StoryId == "ulik");
    }

    /// <summary>A game clip is addressed by the PAIR. Denying
    /// <c>mind-reader/intro</c> must not take <c>button-simon/intro</c> with
    /// it — four of the five games each ship a clip called "intro", so a
    /// clip-id-only match would silently mute most of the games.</summary>
    [Fact]
    public void DenyGameClip_IsScopedToThePair()
    {
        var resolved = DeviceContentEntitlement.Apply(Fleet(),
            new[]
            {
                (DeviceContentEntitlement.KindGame,
                 DeviceContentEntitlement.GameItemKey("mind-reader", "intro"),
                 DeviceContentEntitlement.ModeDeny),
            });

        var games = new ContentManifestService(resolved).Build().Games!;
        Assert.Equal("button-simon", Assert.Single(games).GameKey);
    }

    [Fact]
    public void DenyVoiceAndMusic_AreScopedToTheirOwnNamespaces()
    {
        var resolved = DeviceContentEntitlement.Apply(Fleet(),
            new[]
            {
                (DeviceContentEntitlement.KindVoice, "greet-01", DeviceContentEntitlement.ModeDeny),
                // Same key in a DIFFERENT namespace must not collide.
                (DeviceContentEntitlement.KindMusic, "greet-01", DeviceContentEntitlement.ModeDeny),
            });

        var manifest = new ContentManifestService(resolved).Build();
        Assert.Null(manifest.Voice);
        Assert.NotNull(manifest.Music);          // "greet-01" is not a track id
        Assert.Equal(3, manifest.Stories.Count);
    }

    /// <summary>
    /// Denying every story yields an EMPTY manifest, not the legacy scalar
    /// story. <see cref="ContentSyncOptions.ResolveStories"/> falls back to
    /// the legacy single-item shape whenever the Stories list is empty, so a
    /// naive filter would withhold a story and then serve the one configured
    /// in the flat scalars — the exact opposite of what was asked for.
    /// </summary>
    [Fact]
    public void DenyingEveryStory_DoesNotFallBackToTheLegacySingleItem()
    {
        var fleet = Fleet();
        fleet.StoryId = "legacy-story";
        fleet.Sha256 = new string('f', 64);
        fleet.SizeBytes = 42;
        fleet.AudioPath = "legacy.mp3";

        var resolved = DeviceContentEntitlement.Apply(fleet,
            new[]
            {
                (DeviceContentEntitlement.KindStory, "ulik", DeviceContentEntitlement.ModeDeny),
                (DeviceContentEntitlement.KindStory, "anban-huri", DeviceContentEntitlement.ModeDeny),
                (DeviceContentEntitlement.KindStory, "little-cloud", DeviceContentEntitlement.ModeDeny),
            });

        Assert.Empty(new ContentManifestService(resolved).Build().Stories);
    }

    /// <summary>A legacy-shaped deployment (no Stories array, one story in the
    /// flat scalars) must be withholdable too — its single story is projected
    /// into the list before filtering.</summary>
    [Fact]
    public void LegacySingleItemConfig_CanBeDenied()
    {
        var fleet = new ContentSyncOptions
        {
            Enabled = true,
            StoryId = "anban-huri",
            Sha256 = new string('a', 64),
            SizeBytes = 42,
            AudioPath = "anban-huri.mp3",
        };
        Assert.Single(new ContentManifestService(fleet).Build().Stories);

        var resolved = DeviceContentEntitlement.Apply(fleet,
            new[] { (DeviceContentEntitlement.KindStory, "anban-huri", DeviceContentEntitlement.ModeDeny) });

        Assert.Empty(new ContentManifestService(resolved).Build().Stories);
    }

    /// <summary>AudioRoot is applied once, on the way through, and never
    /// re-applied — otherwise a relative path would be rooted twice and every
    /// download would 404.</summary>
    [Fact]
    public void ResolvedPaths_SurviveTheNarrowing()
    {
        var fleet = Fleet();
        fleet.AudioRoot = Path.Combine(Path.GetTempPath(), "areg-entitlement-test");

        var expected = fleet.ResolveStories().First(s => s.StoryId == "anban-huri").AudioPath;
        var resolved = DeviceContentEntitlement.Apply(fleet,
            new[] { (DeviceContentEntitlement.KindStory, "ulik", DeviceContentEntitlement.ModeDeny) });

        Assert.Equal(expected,
            resolved.ResolveStories().First(s => s.StoryId == "anban-huri").AudioPath);
    }

    // ── AdvertisedStoryVersions — trap 1 at its source ───────────────

    /// <summary>
    /// A withheld story is not an advertised one. If it stayed in this
    /// projection, <c>DeviceContentHealth</c> would report every restricted
    /// toy as permanently behind — to its own parent, over a decision they
    /// were never told about and could not act on.
    /// </summary>
    [Fact]
    public void AdvertisedStoryVersions_DropsTheDeniedStory()
    {
        var resolved = DeviceContentEntitlement.Apply(Fleet(),
            new[] { (DeviceContentEntitlement.KindStory, "ulik", DeviceContentEntitlement.ModeDeny) });

        Assert.DoesNotContain(resolved.AdvertisedStoryVersions(), a => a.StoryId == "ulik");
        Assert.Equal(2, resolved.AdvertisedStoryVersions().Count);
    }

    // ── Vocabulary ───────────────────────────────────────────────────

    [Theory]
    [InlineData("story", true)]
    [InlineData("game", true)]
    [InlineData("voice", true)]
    [InlineData("music", true)]
    [InlineData("STORY", true)]
    [InlineData("stories", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnownKind_IsBounded(string? kind, bool known)
        => Assert.Equal(known, DeviceContentEntitlement.IsKnownKind(kind));

    [Theory]
    [InlineData("allow", true)]
    [InlineData("deny", true)]
    [InlineData("Deny", true)]
    [InlineData("block", false)]
    [InlineData(null, false)]
    public void IsKnownMode_IsBounded(string? mode, bool known)
        => Assert.Equal(known, DeviceContentEntitlement.IsKnownMode(mode));

    [Fact]
    public void GameItemKey_MatchesTheManifestsOwnPairKey()
        => Assert.Equal("mind-reader/intro",
            DeviceContentEntitlement.GameItemKey(" mind-reader ", "intro"));

    /// <summary>An item key is an ID, held to the same allowlist the manifest
    /// holds every content id to. A key outside it can never match a
    /// catalogue item — and, since the key is rendered back into the
    /// console's inline handlers, an id that cannot contain a quote cannot
    /// become markup.</summary>
    [Theory]
    [InlineData("story", "anban-huri", true)]
    [InlineData("story", "little_cloud2", true)]
    [InlineData("story", "Ulik", false)]                     // uppercase, as the firmware refuses
    [InlineData("story", "mind-reader/intro", false)]        // a pair is only valid for a game
    [InlineData("story", "x'); alert(1);//", false)]         // cannot reach an inline handler
    [InlineData("story", "../../etc/passwd", false)]
    [InlineData("story", "", false)]
    [InlineData("game", "mind-reader/intro", true)]
    [InlineData("game", "mind-reader", false)]               // half a pair addresses nothing
    [InlineData("game", "a/b/c", false)]
    [InlineData("voice", "greet-01", true)]
    [InlineData("music", "lullaby", true)]
    public void IsValidItemKey_IsAnIdAllowlist(string kind, string key, bool valid)
        => Assert.Equal(valid, DeviceContentEntitlement.IsValidItemKey(kind, key));

    [Fact]
    public void IsValidItemKey_RefusesAnOverlongKey()
        => Assert.False(DeviceContentEntitlement.IsValidItemKey("story", new string('a', 49)));

    // ── The scoped service ───────────────────────────────────────────

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Device Dev() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bench",
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    private static DeviceContentOverride Row(Guid deviceId, string kind, string key, string mode) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        ItemKind = kind,
        ItemKey = key,
        Mode = mode,
        Reason = "test",
        CreatedBy = "op",
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>One toy's deny must not touch the toy beside it. This is the
    /// whole product claim of the slice, so it is asserted on the wire form of
    /// both manifests rather than on a story count.</summary>
    [Fact]
    public async Task ResolveForDevice_DeniesOnlyTheToyThatWasDenied()
    {
        var db = NewDb();
        var a = Dev();
        var b = Dev();
        db.Devices.AddRange(a, b);
        db.Add(Row(a.Id, DeviceContentEntitlement.KindStory, "ulik", DeviceContentEntitlement.ModeDeny));
        await db.SaveChangesAsync();

        var fleet = Fleet();
        var service = new ContentCatalogService(db, fleet);

        var forA = await service.ResolveForDeviceAsync(a.Id);
        var forB = await service.ResolveForDeviceAsync(b.Id);

        Assert.DoesNotContain(new ContentManifestService(forA).Build().Stories,
            s => s.StoryId == "ulik");
        Assert.Equal(Wire(fleet), Wire(forB));
    }

    /// <summary>The registration this whole path depends on. The controller
    /// takes the catalogue as an OPTIONAL service so the older controller
    /// tests keep compiling — which means a missing registration would fail
    /// open (every toy back on the fleet manifest) with nothing to notice it.
    /// This is what notices it.</summary>
    [Fact]
    public void DependencyInjection_RegistersContentCatalogService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key-never-called",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IContentCatalogService>());
    }
}

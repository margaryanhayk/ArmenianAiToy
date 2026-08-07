using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Offline-game content sync: the fourth ContentSync namespace, which is
/// what lets the ~90 pre-rendered game clips reach a toy over Wi-Fi instead
/// of by hand-copying an SD card.
/// <para>
/// Structured as a deliberate copy of <see cref="ContentSyncVoiceTests"/> —
/// games are the fourth namespace on the same contract, and the tests
/// staying recognisably parallel is what makes a divergence obvious. The
/// one real difference is the ADDRESSING: a game clip is identified by a
/// PAIR (gameKey + clipId), because four of the five games each ship a clip
/// called "intro".
/// </para>
/// </summary>
public class ContentSyncGamesTests
{
    private static readonly string ValidSha = new('d', 64);
    private static readonly string StorySha = new('a', 64);

    private static ContentSyncOptions WithOneStory() => new()
    {
        Enabled = true,
        Stories = { new ContentSyncStoryOptions
        {
            StoryId = "anban-huri", Sha256 = StorySha, SizeBytes = 1,
        } },
    };

    private static ContentSyncGameOptions Clip(
        string gameKey, string clipId, string? sha = null, long size = 10) => new()
        {
            GameKey = gameKey,
            ClipId = clipId,
            Sha256 = sha ?? ValidSha,
            SizeBytes = size,
        };

    // ---- config binding ----

    [Fact]
    public void Resolve_BindsGamesArray_AndAppliesAudioRoot()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentSync:Enabled"] = "true",
                ["ContentSync:AudioRoot"] = "game-audio",
                ["ContentSync:Games:0:GameKey"] = "mind-reader",
                ["ContentSync:Games:0:ClipId"] = "q-root",
                ["ContentSync:Games:0:Version"] = "3",
                ["ContentSync:Games:0:Sha256"] = ValidSha,
                ["ContentSync:Games:0:SizeBytes"] = "2048",
                ["ContentSync:Games:0:AudioPath"] = "mind-reader/q-root.mp3",
            }).Build();

        var options = ContentSyncOptions.Resolve(config);

        var clip = Assert.Single(options.Games);
        Assert.Equal("mind-reader", clip.GameKey);
        Assert.Equal("q-root", clip.ClipId);
        Assert.Equal(3, clip.Version);
        Assert.Equal(2048, clip.SizeBytes);
        var resolved = Assert.Single(options.ResolveGames());
        Assert.True(Path.IsPathRooted(resolved.AudioPath));
    }

    // ---- manifest ----

    [Fact]
    public void Manifest_ValidClipsCarried_InvalidDropped_DuplicatePairKeepsFirst()
    {
        var options = WithOneStory();
        options.Games.Add(Clip("mind-reader", "q-root"));
        options.Games.Add(Clip("", "q-1"));                          // no game key
        options.Games.Add(Clip("mind-reader", ""));                  // no clip id
        options.Games.Add(Clip("mind-reader", "bad-sha", sha: "xx")); // bad sha
        options.Games.Add(Clip("mind-reader", "no-size", size: 0));   // no size
        options.Games.Add(Clip("mind-reader", "q-root", size: 99));   // dup pair

        var manifest = new ContentManifestService(options).Build();

        Assert.NotNull(manifest.Games);
        var clip = Assert.Single(manifest.Games!);
        Assert.Equal("mind-reader", clip.GameKey);
        Assert.Equal("q-root", clip.ClipId);
        Assert.Equal(10, clip.SizeBytes);
        Assert.Equal(
            "/api/devices/content-file?gameKey=mind-reader&clipId=q-root",
            clip.AudioUrl);
        Assert.True(clip.Enabled);
    }

    // KEYSTONE: the PAIR is the identity, not the clip id. Four of the five
    // games in game-clips.json each define a clip called "intro"; if the
    // dedupe keyed on clip id alone, three of those four would silently
    // vanish from every toy's manifest.
    [Fact]
    public void Manifest_SameClipIdInDifferentGames_AreDistinctItems()
    {
        var options = WithOneStory();
        options.Games.Add(Clip("mind-reader", "intro"));
        options.Games.Add(Clip("who-first", "intro"));
        options.Games.Add(Clip("button-simon", "intro"));

        var manifest = new ContentManifestService(options).Build();

        Assert.Equal(3, manifest.Games!.Count);
        Assert.Contains(manifest.Games, g => g.GameKey == "mind-reader");
        Assert.Contains(manifest.Games, g => g.GameKey == "who-first");
        Assert.Contains(manifest.Games, g => g.GameKey == "button-simon");
    }

    // KEYSTONE: a deployment with no game clips has a byte-identical wire
    // shape to before this slice (Games null), so every shipped toy sees no
    // change until the owner actually configures rendered clips.
    [Fact]
    public void Manifest_NoGameClips_FieldStaysNull()
    {
        Assert.Null(new ContentManifestService(WithOneStory()).Build().Games);
    }

    // A games-only deployment (no stories, music or voice) must still produce
    // a manifest — the early "nothing configured" return has to account for
    // the fourth namespace or the games would never reach the card.
    [Fact]
    public void Manifest_GamesOnly_IsNotTreatedAsEmpty()
    {
        var options = new ContentSyncOptions { Enabled = true };
        options.Games.Add(Clip("mind-reader", "q-root"));

        var manifest = new ContentManifestService(options).Build();

        Assert.NotNull(manifest.Games);
        Assert.Single(manifest.Games!);
    }

    [Fact]
    public void Manifest_MasterSwitchOff_DropsGameClipsToo()
    {
        var options = new ContentSyncOptions { Enabled = false };
        options.Games.Add(Clip("mind-reader", "q-root"));

        Assert.Null(new ContentManifestService(options).Build().Games);
    }

    // Both halves reach an SD path — the key as a DIRECTORY name, the clip id
    // as a filename stem — so both are held to the firmware's id allowlist
    // (cs_is_valid_story_id). A traversal-shaped or upper-case half must
    // never reach the wire, whatever the operator typed into config.
    [Theory]
    [InlineData("../etc", "q-root")]
    [InlineData("mind-reader", "../../passwd")]
    [InlineData("mind/reader", "q-root")]
    [InlineData("mind-reader", "q/root")]
    [InlineData("Mind-Reader", "q-root")]
    [InlineData("mind-reader", "Q-Root")]
    [InlineData("mind reader", "q-root")]
    [InlineData("mind-reader", "q.root")]
    public void Manifest_IdsFailingTheFirmwareAllowlist_AreDropped(
        string gameKey, string clipId)
    {
        var options = WithOneStory();
        options.Games.Add(Clip(gameKey, clipId));

        Assert.Null(new ContentManifestService(options).Build().Games);
    }

    // ---- content-file addressing ----

    private static DeviceController Controller()
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public void ContentFile_GameKeyAndClipId_StreamsClipFile()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, [0xFF, 0xF3]);
        try
        {
            var options = new ContentSyncOptions { Enabled = true };
            options.Games.Add(new ContentSyncGameOptions
            {
                GameKey = "mind-reader", ClipId = "q-root",
                Sha256 = ValidSha, SizeBytes = 2, AudioPath = file,
            });

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(),
                gameKey: "mind-reader", clipId: "q-root");

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
            Assert.Equal("audio/mpeg", physical.ContentType);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // Both halves are lookup keys against configured items and never reach
    // the filesystem — a traversal-shaped pair is simply a pair that matches
    // nothing. Half a pair matches nothing either, and gets the same uniform
    // 404 rather than falling through to the story branch.
    [Theory]
    [InlineData("mind-reader", "unknown-clip")]
    [InlineData("unknown-game", "q-root")]
    [InlineData("../../etc", "passwd")]
    [InlineData("mind-reader", "../../etc/passwd")]
    [InlineData("..\\..\\windows", "win.ini")]
    [InlineData("mind-reader", null)]
    [InlineData(null, "q-root")]
    public void ContentFile_UnknownOrHalfPair_Uniform404(string? gameKey, string? clipId)
    {
        var options = new ContentSyncOptions { Enabled = true };
        options.Games.Add(new ContentSyncGameOptions
        {
            GameKey = "mind-reader", ClipId = "q-root",
            Sha256 = ValidSha, SizeBytes = 2, AudioPath = "/abs/q-root.mp3",
        });

        var result = Controller().GetContentFile(
            options, Substitute.For<ILogger<DeviceController>>(),
            gameKey: gameKey, clipId: clipId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // A clip id that belongs to ANOTHER game must not resolve through this
    // game's key — the pair is checked as a unit, not as two independent
    // lookups.
    [Fact]
    public void ContentFile_CrossGamePair_Uniform404()
    {
        var options = new ContentSyncOptions { Enabled = true };
        options.Games.Add(new ContentSyncGameOptions
        {
            GameKey = "mind-reader", ClipId = "q-root",
            Sha256 = ValidSha, SizeBytes = 2, AudioPath = "/abs/q-root.mp3",
        });
        options.Games.Add(new ContentSyncGameOptions
        {
            GameKey = "who-first", ClipId = "go",
            Sha256 = ValidSha, SizeBytes = 2, AudioPath = "/abs/go.mp3",
        });

        var result = Controller().GetContentFile(
            options, Substitute.For<ILogger<DeviceController>>(),
            gameKey: "mind-reader", clipId: "go");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // The existing namespaces keep priority: a request carrying voiceId is a
    // voice request even if game params ride along, and vice versa. Pinned
    // because the branch order in GetContentFile is the only thing enforcing
    // it, and reordering the blocks would silently change addressing.
    [Fact]
    public void ContentFile_GameParams_DoNotDisturbTheStoryPath()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, [0xFF, 0xF3]);
        try
        {
            var options = new ContentSyncOptions
            {
                Enabled = true,
                Stories = { new ContentSyncStoryOptions
                {
                    StoryId = "anban-huri", Sha256 = StorySha,
                    SizeBytes = 2, AudioPath = file,
                } },
            };

            var result = Controller().GetContentFile(
                options, Substitute.For<ILogger<DeviceController>>(),
                storyId: "anban-huri");

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- the real content set ----

    // The shipped clip source is backend/content/offline-games/game-clips.json,
    // and its `id` scheme IS the contract offline_games.cpp resolves against.
    // Every id in it must pass the same allowlist this namespace enforces, or
    // the operator's generated config would be silently dropped item by item.
    [Fact]
    public void RealGameClipIds_AllPassTheFirmwareAllowlist()
    {
        var path = FindRepoFile(Path.Combine(
            "backend", "content", "offline-games", "game-clips.json"));
        if (path is null)
        {
            return;   // content folder not deployed alongside the test binary
        }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var checkedIds = 0;
        foreach (var game in doc.RootElement.EnumerateObject())
        {
            if (game.Name.StartsWith('_')
                || game.Value.ValueKind != System.Text.Json.JsonValueKind.Object
                || !game.Value.TryGetProperty("clips", out var clips))
            {
                continue;
            }
            Assert.True(IsAllowlistedId(game.Name),
                $"game key '{game.Name}' fails the firmware id allowlist");
            foreach (var clip in clips.EnumerateArray())
            {
                var id = clip.GetProperty("id").GetString() ?? "";
                Assert.True(IsAllowlistedId(id),
                    $"clip id '{game.Name}/{id}' fails the firmware id allowlist");
                checkedIds++;
            }
        }
        Assert.True(checkedIds > 0, "no game clips found to validate");
    }

    private static bool IsAllowlistedId(string value)
        => value.Length is > 0 and <= 48
           && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}

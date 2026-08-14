using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// The operator action that gives a toy an item or takes one away, and the
/// three surfaces that must agree with it: the toy's own manifest, the
/// library-health verdict, and the parent's story library.
///
/// <para>
/// The health test is the one worth keeping honest. Withholding a story and
/// then reporting the toy as permanently out of date would be a fault the
/// product invented about itself — visible to the PARENT, who was never told
/// and could do nothing — so "a denied story is not a missing story" is
/// asserted on both the console and the dashboard.
/// </para>
/// </summary>
public class InternalControllerContentOverrideTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

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
        Stories = { Story("ulik", 12), Story("anban-huri", 9) },
    };

    /// <summary>The same two stories as <see cref="Fleet"/>, as configuration,
    /// so the console's own <c>ContentSyncOptions.Resolve(_config)</c> sees
    /// them.</summary>
    private static IConfiguration FleetConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ContentSync:Enabled"] = "true",
            ["ContentSync:Stories:0:StoryId"] = "ulik",
            ["ContentSync:Stories:0:Version"] = "12",
            ["ContentSync:Stories:0:Title"] = "T-ulik",
            ["ContentSync:Stories:0:AudioPath"] = "ulik.mp3",
            ["ContentSync:Stories:0:Sha256"] = new string('a', 64),
            ["ContentSync:Stories:0:SizeBytes"] = "100",
            ["ContentSync:Stories:1:StoryId"] = "anban-huri",
            ["ContentSync:Stories:1:Version"] = "9",
            ["ContentSync:Stories:1:Title"] = "T-anban-huri",
            ["ContentSync:Stories:1:AudioPath"] = "anban-huri.mp3",
            ["ContentSync:Stories:1:Sha256"] = new string('a', 64),
            ["ContentSync:Stories:1:SizeBytes"] = "100",
        }).Build();

    private static InternalController NewController(AppDbContext db, IConfiguration? config = null)
    {
        var controller = new InternalController(
            db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter(),
            new LibraryStoryQuestionService(Substitute.For<IAiChatClient>()),
            Substitute.For<IModerationService>(),
            config ?? new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<InternalController>>());
        var http = new DefaultHttpContext();
        http.Items["InternalOperator"] = "owner";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static Device Dev(string name = "Bench") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    private static InternalDeviceContentRequest Deny(string key) =>
        new(DeviceContentEntitlement.KindStory, key, false, "staged rollout");

    // ── Request shape, mirroring DeviceFlagActionAsync exactly ───────

    [Fact]
    public async Task BlankReason_Returns400_AndWritesNothing()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        var result = await NewController(db).SetDeviceContent(
            d.Id, new InternalDeviceContentRequest("story", "ulik", false, "   "), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.Set<DeviceContentOverride>());
        Assert.Empty(db.AuditEvents);
    }

    [Fact]
    public async Task UnknownDevice_Returns404()
    {
        var db = NewDb();
        var result = await NewController(db).SetDeviceContent(Guid.NewGuid(), Deny("ulik"), default);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(db.Set<DeviceContentOverride>());
    }

    [Theory]
    [InlineData("stories", "ulik")]                 // not one of the four namespaces
    [InlineData("story", "   ")]                    // no item
    [InlineData("story", "x'); alert(1);//")]       // not an id; never reaches the console markup
    [InlineData("game", "mind-reader")]             // half a pair addresses no clip
    public async Task UnknownKindOrMalformedKey_Returns400(string kind, string key)
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await NewController(db).SetDeviceContent(
            d.Id, new InternalDeviceContentRequest(kind, key, false, "why"), default));
        Assert.Empty(db.Set<DeviceContentOverride>());
    }

    // ── Idempotence ─────────────────────────────────────────────────

    [Fact]
    public async Task Deny_WritesOneRowAndOneAudit_AndTheSecondDenyWritesNeither()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        var first = Assert.IsType<OkObjectResult>(
            await NewController(db).SetDeviceContent(d.Id, Deny("ulik"), default));
        Assert.True(Changed(first));
        Assert.Single(db.Set<DeviceContentOverride>());
        Assert.Single(db.AuditEvents);

        var second = Assert.IsType<OkObjectResult>(
            await NewController(db).SetDeviceContent(d.Id, Deny("ulik"), default));
        Assert.False(Changed(second));
        Assert.Single(db.Set<DeviceContentOverride>());
        Assert.Single(db.AuditEvents);
    }

    /// <summary>Giving an item back flips the SAME row rather than adding a
    /// second — the unique (device, kind, key) index is what keeps the
    /// resolution rule to one sentence, and a contradictory pair would be
    /// unresolvable.</summary>
    [Fact]
    public async Task GivingItBack_FlipsTheSameRow()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        await NewController(db).SetDeviceContent(d.Id, Deny("ulik"), default);
        var back = Assert.IsType<OkObjectResult>(await NewController(db).SetDeviceContent(
            d.Id, new InternalDeviceContentRequest("story", "ulik", true, "released"), default));

        Assert.True(Changed(back));
        var row = Assert.Single(db.Set<DeviceContentOverride>());
        Assert.Equal(DeviceContentEntitlement.ModeAllow, row.Mode);
        Assert.Equal(2, db.AuditEvents.Count());
    }

    /// <summary>The audit row has to NAME the item. An entitlement record that
    /// says only "content changed" documents nothing an operator could act
    /// on.</summary>
    [Fact]
    public async Task AuditRow_NamesTheItem_AndStaysOutOfParentFeeds()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();

        await NewController(db).SetDeviceContent(d.Id, Deny("ulik"), default);

        var audit = Assert.Single(db.AuditEvents);
        Assert.Equal(AuditEventType.InternalConsoleAction, audit.EventType);
        Assert.Null(audit.ActorParentId);              // console-only
        Assert.Equal(d.Id, audit.TargetDeviceId);
        using var meta = JsonDocument.Parse(audit.Metadata!);
        Assert.Equal("device_content_override", meta.RootElement.GetProperty("action").GetString());
        Assert.Equal("story", meta.RootElement.GetProperty("item_kind").GetString());
        Assert.Equal("ulik", meta.RootElement.GetProperty("item_key").GetString());
        Assert.False(meta.RootElement.GetProperty("value").GetBoolean());
        Assert.Equal("staged rollout", meta.RootElement.GetProperty("reason").GetString());
    }

    // ── The read half ───────────────────────────────────────────────

    [Fact]
    public async Task GetDeviceContent_ListsEveryStory_AndMarksTheWithheldOne()
    {
        var db = NewDb();
        var d = Dev();
        db.Devices.Add(d);
        await db.SaveChangesAsync();
        var controller = NewController(db, FleetConfig());
        await controller.SetDeviceContent(d.Id, Deny("ulik"), default);

        var ok = Assert.IsType<OkObjectResult>(await controller.GetDeviceContent(d.Id, default));
        var json = JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        var ulik = items.Single(i => i.GetProperty("itemKey").GetString() == "ulik");
        Assert.False(ulik.GetProperty("allowed").GetBoolean());
        Assert.Equal("staged rollout", ulik.GetProperty("overrideReason").GetString());
        Assert.True(items.Single(i => i.GetProperty("itemKey").GetString() == "anban-huri")
            .GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task GetDeviceContent_UnknownDevice_Returns404()
        => Assert.IsType<NotFoundObjectResult>(
            await NewController(NewDb()).GetDeviceContent(Guid.NewGuid(), default));

    // ── Trap 1: a withheld story is not a missing story ──────────────

    /// <summary>
    /// The regression this slice was most likely to ship. The console derives
    /// library health from what the manifest advertises; if that stayed
    /// fleet-wide, a denied toy would sit at <c>syncing</c>/<c>stale</c>
    /// forever and the console would name the withheld story as missing —
    /// accusing a toy of failing to fetch what the operator withheld.
    /// </summary>
    [Fact]
    public async Task ConsoleDevices_DeniedStoryIsNotCountedAgainstTheToy()
    {
        var db = NewDb();
        var denied = Dev("denied");
        var normal = Dev("normal");
        // Both toys hold anban-huri v9 and NOT ulik v12.
        denied.ContentStories = "anban-huri:9";
        normal.ContentStories = "anban-huri:9";
        db.Devices.AddRange(denied, normal);
        await db.SaveChangesAsync();

        var controller = NewController(db, FleetConfig());
        await controller.SetDeviceContent(denied.Id, Deny("ulik"), default);

        var ok = Assert.IsType<OkObjectResult>(await controller.Devices(default));
        var json = JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("devices").EnumerateArray().ToList();

        var deniedRow = rows.Single(r => r.GetProperty("name").GetString() == "denied");
        Assert.Equal(DeviceContentHealth.UpToDate, deniedRow.GetProperty("contentHealth").GetString());
        Assert.Empty(deniedRow.GetProperty("missingStoryIds").EnumerateArray());

        // The other toy is untouched — still genuinely behind on ulik.
        var normalRow = rows.Single(r => r.GetProperty("name").GetString() == "normal");
        Assert.Equal(DeviceContentHealth.Syncing, normalRow.GetProperty("contentHealth").GetString());
        Assert.Equal("ulik",
            Assert.Single(normalRow.GetProperty("missingStoryIds").EnumerateArray()).GetString());
    }

    /// <summary>The same claim on the PARENT surface. A parent reading "your
    /// toy is missing a story" over an operator decision they were never told
    /// about is the version of this bug that reaches a customer.</summary>
    [Fact]
    public async Task ParentDashboard_DeniedStoryIsNotCountedAgainstTheToy()
    {
        var db = NewDb();
        var parentId = Guid.NewGuid();
        var device = Dev("family");
        device.ContentStories = "anban-huri:9";
        db.Devices.Add(device);
        db.Parents.Add(new Parent
        {
            Id = parentId, Email = "a@b.c", PasswordHash = "x", RegisteredAt = DateTime.UtcNow,
        });
        db.ParentDevices.Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = device.Id, LinkedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var config = FleetConfig();
        await NewController(db, config).SetDeviceContent(device.Id, Deny("ulik"), default);

        var service = new ParentService(db, config, Substitute.For<ILogger<ParentService>>());
        var dto = Assert.Single(await service.GetLinkedDeviceDetailsAsync(parentId));

        Assert.Equal(DeviceContentHealth.UpToDate, dto.ContentHealth);
        Assert.Equal(1, dto.StoriesAvailable);     // not 2 — ulik was withheld
        Assert.Equal(1, dto.StoriesOnToy);
    }

    // ── The toy's own manifest ──────────────────────────────────────

    /// <summary>End to end through the real device endpoint: the denied toy's
    /// manifest loses the story, the toy beside it is untouched.</summary>
    [Fact]
    public async Task ContentManifest_DeniedToyLosesTheStory_TheOtherToyDoesNot()
    {
        var db = NewDb();
        var denied = Dev();
        var other = Dev();
        db.Devices.AddRange(denied, other);
        await db.SaveChangesAsync();
        await NewController(db).SetDeviceContent(denied.Id, Deny("ulik"), default);

        var manifest = new ContentManifestService(Fleet());
        var catalog = new ContentCatalogService(db, Fleet());

        Assert.Equal(new[] { "anban-huri" }, await StoryIds(denied.Id, manifest, catalog));
        Assert.Equal(new[] { "ulik", "anban-huri" }, await StoryIds(other.Id, manifest, catalog));
    }

    private static async Task<string[]> StoryIds(
        Guid deviceId, IContentManifestService manifest, IContentCatalogService catalog)
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var ok = Assert.IsType<OkObjectResult>(await controller.GetContentManifest(
            manifest, Substitute.For<IChildService>(), catalog));
        return Assert.IsType<ContentManifestResponse>(ok.Value)
            .Stories.Select(s => s.StoryId).ToArray();
    }

    // ── Trap 2: the parent's story library ──────────────────────────

    /// <summary>A parent opening the library FROM a toy sees that toy's
    /// shelf. Listing a withheld story would put a ▶ card in front of them for
    /// a story their child can never ask for.</summary>
    [Fact]
    public async Task ParentStoryLibrary_DoesNotListAStoryThisToyWasDenied()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        await NewController(db).SetDeviceContent(device.Id, Deny("ulik"), default);

        var manifest = new ContentManifestService(Fleet());
        var parentService = Substitute.For<IParentService>();
        parentService.GetStoryPlayTotalsForParentAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new List<StoryPlayTotalDto>());
        parentService.GetLinkedDeviceIdsAsync(Arg.Any<Guid>())
            .Returns(new List<Guid> { device.Id });

        var body = await Library(parentService, manifest, new ContentCatalogService(db, Fleet()), device.Id);
        Assert.Equal("anban-huri", Assert.Single(body.Stories).StoryId);
    }

    /// <summary>...and the entitlement of a toy this parent does NOT hold is
    /// not readable through the deviceId query param.</summary>
    [Fact]
    public async Task ParentStoryLibrary_UnownedDeviceIdKeepsTheFleetView()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        await NewController(db).SetDeviceContent(device.Id, Deny("ulik"), default);

        var manifest = new ContentManifestService(Fleet());
        var parentService = Substitute.For<IParentService>();
        parentService.GetStoryPlayTotalsForParentAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new List<StoryPlayTotalDto>());
        parentService.GetLinkedDeviceIdsAsync(Arg.Any<Guid>()).Returns(new List<Guid>());

        var body = await Library(parentService, manifest, new ContentCatalogService(db, Fleet()), device.Id);
        Assert.Equal(2, body.Stories.Count);
    }

    private static async Task<StoryLibraryResponse> Library(
        IParentService parentService, IContentManifestService manifest,
        IContentCatalogService catalog, Guid deviceId)
    {
        var controller = new ParentController(parentService, new ExportCooldown());
        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user },
        };

        var ok = Assert.IsType<OkObjectResult>(await controller.GetStoryLibrary(
            Substitute.For<ICuratedStoryLibrary>(), manifest, deviceId, catalog));
        return Assert.IsType<StoryLibraryResponse>(ok.Value);
    }

    private static bool Changed(OkObjectResult ok)
    {
        var json = JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("changed").GetBoolean();
    }
}

using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// The owner adds and removes a story himself: upload an MP3, send it to one
/// toy, release it to the fleet, retire it. No git, no ffmpeg, no deploy.
///
/// <para>
/// The tests that matter most here are the two that say NOTHING happened. An
/// upload must not reach a single toy until it is granted or released — a
/// children's product where pressing "upload" pushes unreviewed audio to every
/// device in the field is the failure this whole shape exists to prevent. And a
/// deployment that has never uploaded anything must serve the byte-identical
/// manifest it served yesterday, which is asserted on the serialized wire form
/// rather than on story ids so a future field cannot slip past it.
/// </para>
/// </summary>
public class ContentItemUploadTests : IDisposable
{
    private readonly string _uploadRoot =
        Path.Combine(Path.GetTempPath(), "areg-upload-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_uploadRoot)) Directory.Delete(_uploadRoot, true); }
        catch { /* best-effort temp cleanup */ }
        GC.SuppressFinalize(this);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>Two shipped stories, exactly as they reach the app: through
    /// configuration, so the console's own <c>ContentSyncOptions.Resolve</c>
    /// sees the same catalogue the manifest does.</summary>
    private IConfiguration Config(bool withUploadRoot = true)
    {
        var pairs = new Dictionary<string, string?>
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
        };
        if (withUploadRoot) pairs["ContentSync:UploadRoot"] = _uploadRoot;
        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private ContentSyncOptions Fleet(bool withUploadRoot = true)
        => ContentSyncOptions.Resolve(Config(withUploadRoot));

    private static InternalController NewController(AppDbContext db, IConfiguration config)
    {
        var controller = new InternalController(
            db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter(),
            new LibraryStoryQuestionService(Substitute.For<IAiChatClient>()),
            Substitute.For<IModerationService>(),
            config, Substitute.For<ILogger<InternalController>>());
        var http = new DefaultHttpContext();
        http.Items["InternalOperator"] = "owner";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static Device Dev() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bench",
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    /// <summary>A minimal but genuine MP3: an ID3 tag header followed by
    /// filler. The endpoint checks the first bytes, so a file of zeroes would
    /// be rejected — as it should be.</summary>
    private static byte[] Mp3Bytes(int length = 512)
    {
        var bytes = new byte[length];
        bytes[0] = 0x49; bytes[1] = 0x44; bytes[2] = 0x33; // "ID3"
        for (var i = 3; i < length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static IFormFile File(byte[] bytes, string name = "whatever.mp3")
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "audio", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "audio/mpeg",
        };

    private static string Sha(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>The serialized manifest — the same wire-form comparison the
    /// entitlement tests use, so a field added later cannot slip through an
    /// assertion that only looked at story ids.</summary>
    private static string Wire(ContentSyncOptions options)
        => JsonSerializer.Serialize(new ContentManifestService(options).Build());

    private static ContentCatalogService Catalog(AppDbContext db, ContentSyncOptions fleet)
        => new(db, fleet);

    /// <summary>Serializes a controller result the way ASP.NET actually will,
    /// so these assertions read the same camelCase field names the console
    /// does. The default options keep the DTO's PascalCase and would let a
    /// renamed field pass unnoticed.</summary>
    private static string WebJson(object? value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    // ── Upload ──────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_CreatesFleetDarkRow_WritesTheFile_AndComputesTheHash()
    {
        var db = NewDb();
        var bytes = Mp3Bytes();

        var result = await NewController(db, Config()).UploadStoryContent(
            "new-story", "Նոր հեքիաթ", "first draft for the bench toy", File(bytes), default);

        Assert.IsType<OkObjectResult>(result);

        var row = Assert.Single(db.ContentItems);
        Assert.Equal("story", row.Kind);
        Assert.Equal("new-story", row.ItemKey);
        Assert.Equal("Նոր հեքիաթ", row.Title);
        Assert.Equal(1, row.Version);
        Assert.Null(row.RetiredAt);
        Assert.Equal("owner", row.CreatedBy);

        // Fleet-dark on arrival is the load-bearing default: an upload reaches
        // no child until someone has heard it on one toy and released it.
        Assert.False(row.DefaultEnabled);

        // The SERVER measured these. That is the whole point — it is what
        // retires the hand-run shipper and the hand-added placeholder row.
        Assert.Equal(Sha(bytes), row.Sha256);
        Assert.Equal(bytes.Length, row.SizeBytes);

        // The filename is generated, never taken from the upload ("whatever.mp3").
        Assert.Equal("new-story-v1.mp3", row.RelativePath);
        var onDisk = Path.Combine(_uploadRoot, "new-story-v1.mp3");
        Assert.True(System.IO.File.Exists(onDisk));
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(onDisk));
        Assert.False(System.IO.File.Exists(onDisk + ".part"));
    }

    [Fact]
    public async Task Upload_ChangesNoToysManifest_UntilItIsGrantedOrReleased()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var before = Wire(await Catalog(db, Fleet()).ResolveForDeviceAsync(device.Id));

        await NewController(db, Config()).UploadStoryContent(
            "new-story", "Նոր հեքիաթ", "staged", File(Mp3Bytes()), default);

        var after = Wire(await Catalog(db, Fleet()).ResolveForDeviceAsync(device.Id));
        Assert.Equal(before, after);
        Assert.Equal(before, Wire(await Catalog(db, Fleet()).ResolveFleetAsync()));
    }

    [Fact]
    public async Task Upload_WritesOneAuditRow_NamingTheItemButNoDevice()
    {
        var db = NewDb();
        await NewController(db, Config()).UploadStoryContent(
            "new-story", "T", "bench", File(Mp3Bytes()), default);

        var audit = Assert.Single(db.AuditEvents);
        // Fleet-scoped, so no device is named — attributing a catalogue change
        // to some arbitrary toy would make the feed lie.
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.ActorParentId);
        Assert.Contains("content_story_uploaded", audit.Metadata);
        Assert.Contains("new-story", audit.Metadata);
        Assert.Contains("owner", audit.Metadata);
    }

    [Fact]
    public async Task Upload_SameKeyAgain_BumpsTheVersion_AndKeepsTheOldFile()
    {
        var db = NewDb();
        var config = Config();
        await NewController(db, config).UploadStoryContent(
            "new-story", "T", "first", File(Mp3Bytes(512)), default);

        var second = Mp3Bytes(600);
        await NewController(db, config).UploadStoryContent(
            "new-story", "T v2", "re-render, the first was truncated", File(second), default);

        var row = Assert.Single(db.ContentItems);
        Assert.Equal(2, row.Version);
        Assert.Equal("new-story-v2.mp3", row.RelativePath);
        Assert.Equal(Sha(second), row.Sha256);
        Assert.Equal("T v2", row.Title);

        // The old file stays: a toy mid-download of v1 must not have it pulled
        // out from under it, and carry-forward keeps it playable until a sweep.
        Assert.True(System.IO.File.Exists(Path.Combine(_uploadRoot, "new-story-v1.mp3")));
        Assert.True(System.IO.File.Exists(Path.Combine(_uploadRoot, "new-story-v2.mp3")));
    }

    // ── Upload: the refusals ────────────────────────────────────────

    [Theory]
    [InlineData("../evil")]
    [InlineData("..")]
    [InlineData("a/../../b")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("/etc/passwd")]
    [InlineData("story with spaces")]
    [InlineData("Story-Upper")]
    [InlineData("story.mp3")]
    public async Task Upload_TraversalShapedItemKey_IsRejected_AndWritesNothing(string key)
    {
        var db = NewDb();
        var result = await NewController(db, Config()).UploadStoryContent(
            key, "T", "attempt", File(Mp3Bytes()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.ContentItems);
        Assert.Empty(db.AuditEvents);
        // Nothing reached the filesystem at all — the id is refused before any
        // path is built from it.
        Assert.False(Directory.Exists(_uploadRoot) && Directory.EnumerateFiles(_uploadRoot).Any());
    }

    [Fact]
    public async Task Upload_KeyThatCollidesWithAShippedStory_IsRejected()
    {
        // The manifest keeps the FIRST of a duplicate id, so such a row could
        // never do anything — storing it would look like it worked forever.
        var db = NewDb();
        var result = await NewController(db, Config()).UploadStoryContent(
            "ulik", "T", "oops", File(Mp3Bytes()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.ContentItems);
    }

    [Fact]
    public async Task Upload_WithoutAReason_IsRejected()
    {
        var db = NewDb();
        var result = await NewController(db, Config()).UploadStoryContent(
            "new-story", "T", "   ", File(Mp3Bytes()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.ContentItems);
        Assert.Empty(db.AuditEvents);
    }

    [Fact]
    public async Task Upload_NotAnMp3_IsRejected_AndLeavesNoPartFile()
    {
        var db = NewDb();
        var result = await NewController(db, Config()).UploadStoryContent(
            "new-story", "T", "wrong file", File(new byte[400]), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.ContentItems);
        Assert.Empty(Directory.EnumerateFiles(_uploadRoot));
    }

    [Fact]
    public async Task Upload_WithNoUploadRootConfigured_FailsClosed_AndStoresNothing()
    {
        // The alternative — writing into the image — puts operator content
        // inside the git-tracked catalogue, where the next redeploy deletes it.
        var db = NewDb();
        var result = await NewController(db, Config(withUploadRoot: false)).UploadStoryContent(
            "new-story", "T", "nowhere to put it", File(Mp3Bytes()), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
        Assert.Empty(db.ContentItems);
    }

    // ── Send to one toy, then release to the fleet ──────────────────

    [Fact]
    public async Task GrantOnOneToy_PutsItInThatToysManifestOnly()
    {
        var db = NewDb();
        var a = Dev();
        var b = Dev();
        db.Devices.AddRange(a, b);
        await db.SaveChangesAsync();

        var config = Config();
        var controller = NewController(db, config);
        await controller.UploadStoryContent(
            "new-story", "T", "bench first", File(Mp3Bytes()), default);

        var bBefore = Wire(await Catalog(db, Fleet()).ResolveForDeviceAsync(b.Id));

        await controller.SetDeviceContent(
            a.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench toy"), default);

        var aStories = (await Catalog(db, Fleet()).ResolveForDeviceAsync(a.Id))
            .ResolveStories().Select(s => s.StoryId).ToList();
        Assert.Contains("new-story", aStories);

        // B is untouched, on the wire, not merely "does not contain the id".
        Assert.Equal(bBefore, Wire(await Catalog(db, Fleet()).ResolveForDeviceAsync(b.Id)));
    }

    [Fact]
    public async Task GrantedStory_ResolvesToTheFileOnTheVolume_AndContentFileServesIt()
    {
        // The trap this stage turns on: advertised in the manifest but 404 on
        // download would make every toy report download_failed.
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var bytes = Mp3Bytes();
        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(bytes), default);
        await controller.SetDeviceContent(
            device.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench"), default);

        var scoped = await Catalog(db, Fleet()).ResolveForDeviceAsync(device.Id);

        // The story is advertised…
        var manifest = new ContentManifestService(scoped).Build();
        var item = Assert.Single(manifest.Stories, s => s.StoryId == "new-story");
        Assert.Equal(Sha(bytes), item.Sha256);
        Assert.Equal(bytes.Length, item.SizeBytes);

        // …and the device endpoint actually serves those bytes, from the
        // upload root rather than from the image's AudioRoot.
        var deviceController = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = device.Id;
        deviceController.ControllerContext = new ControllerContext { HttpContext = http };

        var served = deviceController.GetContentFile(
            scoped, Substitute.For<ILogger<DeviceController>>(), storyId: "new-story");

        var file = Assert.IsType<PhysicalFileResult>(served);
        Assert.Equal(Path.Combine(_uploadRoot, "new-story-v1.mp3"), file.FileName);
        Assert.Equal("audio/mpeg", file.ContentType);
    }

    [Fact]
    public async Task ContentFileEndpoint_ResolvesThroughTheDevicesOwnCatalogue()
    {
        // Not the same assertion as the one above. That one proves the serving
        // logic can reach the volume; this one proves the ACTION asks the
        // catalogue at all. With the fleet singleton — the shape this endpoint
        // had before this slice — an uploaded story is advertised and then
        // 404s, and every toy in the fleet reports download_failed.
        var db = NewDb();
        var granted = Dev();
        var other = Dev();
        db.Devices.AddRange(granted, other);
        await db.SaveChangesAsync();

        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        await controller.SetDeviceContent(
            granted.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench"), default);

        var fleet = Fleet();
        var catalog = Catalog(db, fleet);
        var logger = Substitute.For<ILogger<DeviceController>>();

        Assert.IsType<PhysicalFileResult>(
            await DeviceFor(granted.Id).GetContentFileForDevice(
                fleet, logger, storyId: "new-story", catalog: catalog));

        // A toy that was never granted it cannot fetch it by guessing the id.
        Assert.IsType<NotFoundObjectResult>(
            await DeviceFor(other.Id).GetContentFileForDevice(
                fleet, logger, storyId: "new-story", catalog: catalog));
    }

    private static DeviceController DeviceFor(Guid deviceId)
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(), Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public async Task ReleaseToFleet_PutsItInEveryToysManifest()
    {
        var db = NewDb();
        var a = Dev();
        var b = Dev();
        db.Devices.AddRange(a, b);
        await db.SaveChangesAsync();

        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        var id = db.ContentItems.Single().Id;

        var result = await controller.ReleaseContentItem(
            id, new InternalContentItemActionRequest(true, "listened, it is good"), default);
        Assert.IsType<OkObjectResult>(result);

        foreach (var device in new[] { a, b })
        {
            var stories = (await Catalog(db, Fleet()).ResolveForDeviceAsync(device.Id))
                .ResolveStories().Select(s => s.StoryId).ToList();
            Assert.Contains("new-story", stories);
        }
        Assert.Contains("new-story",
            (await Catalog(db, Fleet()).ResolveFleetAsync()).ResolveStories().Select(s => s.StoryId));
    }

    [Fact]
    public async Task Retire_RemovesItFromEveryManifest_ButLeavesTheFile()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        var id = db.ContentItems.Single().Id;
        await controller.ReleaseContentItem(id, new InternalContentItemActionRequest(true, "ship"), default);
        // Granted on this toy as well, so retirement is shown to beat BOTH
        // routes an item can reach a device by.
        await controller.SetDeviceContent(
            device.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench"), default);

        await controller.RetireContentItem(
            id, new InternalContentItemActionRequest(true, "wrong ending"), default);

        Assert.DoesNotContain("new-story",
            (await Catalog(db, Fleet()).ResolveForDeviceAsync(device.Id))
                .ResolveStories().Select(s => s.StoryId));
        Assert.DoesNotContain("new-story",
            (await Catalog(db, Fleet()).ResolveFleetAsync()).ResolveStories().Select(s => s.StoryId));

        // Soft delete: no child loses a story mid-listen because an adult
        // pressed a button in a console.
        Assert.True(System.IO.File.Exists(Path.Combine(_uploadRoot, "new-story-v1.mp3")));
        Assert.NotNull(db.ContentItems.Single().RetiredAt);
    }

    // ── Operator discipline on the two flag actions ─────────────────

    [Fact]
    public async Task FlagActions_RequireAReason_And404OnAnUnknownItem()
    {
        var db = NewDb();
        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        var id = db.ContentItems.Single().Id;

        Assert.IsType<BadRequestObjectResult>(await controller.ReleaseContentItem(
            id, new InternalContentItemActionRequest(true, "  "), default));
        Assert.IsType<BadRequestObjectResult>(await controller.RetireContentItem(id, null, default));
        Assert.IsType<NotFoundObjectResult>(await controller.ReleaseContentItem(
            Guid.NewGuid(), new InternalContentItemActionRequest(true, "why"), default));
    }

    [Fact]
    public async Task FlagAction_ThatChangesNothing_WritesNoAuditRow()
    {
        var db = NewDb();
        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        var id = db.ContentItems.Single().Id;
        var auditsAfterUpload = db.AuditEvents.Count();

        // Already fleet-dark; releasing to false is a no-op.
        var result = await controller.ReleaseContentItem(
            id, new InternalContentItemActionRequest(false, "no change"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("\"changed\":false", JsonSerializer.Serialize(ok.Value));
        Assert.Equal(auditsAfterUpload, db.AuditEvents.Count());
    }

    // ── The deployment that has never uploaded anything ─────────────

    [Fact]
    public async Task NoUploadedItems_ReturnsTheBaselineByReference_OnEveryPath()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var fleet = Fleet();
        var catalog = Catalog(db, fleet);

        // Same OBJECT, not merely an equal one: "byte-identical to yesterday"
        // is then true by construction rather than by careful copying.
        Assert.Same(fleet, await catalog.ResolveForDeviceAsync(device.Id));
        Assert.Same(fleet, await catalog.ResolveFleetAsync());
    }

    [Fact]
    public void Overlay_WithNoItems_ReturnsTheBaselineItself()
    {
        var fleet = Fleet();
        Assert.Same(fleet, ContentItemOverlay.Apply(fleet, null));
        Assert.Same(fleet, ContentItemOverlay.Apply(fleet, Array.Empty<ContentItemOverlay.Item>()));
    }

    [Fact]
    public void Overlay_DropsAnItemThatCannotResolveInsideTheUploadRoot()
    {
        // A hand-edited row, a restored backup, or a future second writer must
        // not be able to turn the device content-file endpoint into an
        // arbitrary file reader — nor advertise a story that will 404.
        var fleet = Fleet();
        var escaping = new ContentItemOverlay.Item(
            "story", "new-story", "T", 1,
            Path.Combine("..", "..", "escaped.mp3"), new string('b', 64), 10);

        Assert.Same(fleet, ContentItemOverlay.Apply(fleet, new[] { escaping }));

        // And an absolute stored path is refused outright, since it would skip
        // the containment check by construction.
        var absolute = escaping with { RelativePath = Path.Combine(Path.GetTempPath(), "x.mp3") };
        Assert.Same(fleet, ContentItemOverlay.Apply(fleet, new[] { absolute }));
    }

    [Fact]
    public void Overlay_WithNoUploadRoot_DropsEveryUploadedItem()
    {
        // Fail closed rather than advertise a story every toy in the fleet then
        // fails to download.
        var fleet = Fleet(withUploadRoot: false);
        var item = new ContentItemOverlay.Item(
            "story", "new-story", "T", 1, "new-story-v1.mp3", new string('b', 64), 10);

        Assert.Same(fleet, ContentItemOverlay.Apply(fleet, new[] { item }));
    }

    // ── The console's per-device list ───────────────────────────────

    [Fact]
    public async Task DeviceContentList_ShowsAFleetDarkUploadAsNotSent_UntilItIsGranted()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);

        var listed = await Rows(controller, device.Id);
        var row = Assert.Single(listed, r => r.ItemKey == "new-story");
        Assert.Equal("uploaded", row.Source);
        Assert.False(row.FleetDefault);
        // The whole point: a fleet-dark item is NOT sent, even though no deny
        // row exists. Reading it as "sent" would show a newly-uploaded story as
        // live on every toy in the fleet.
        Assert.False(row.Allowed);

        // A shipped story is unaffected.
        var shipped = Assert.Single(listed, r => r.ItemKey == "ulik");
        Assert.Equal("catalogue", shipped.Source);
        Assert.True(shipped.FleetDefault);
        Assert.True(shipped.Allowed);

        await controller.SetDeviceContent(
            device.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench"), default);

        var afterGrant = Assert.Single(await Rows(controller, device.Id), r => r.ItemKey == "new-story");
        Assert.True(afterGrant.Allowed);
        Assert.Equal("allow", afterGrant.OverrideMode);
    }

    [Fact]
    public async Task DeviceContentList_ShowsARetiredItemAsNotSent_EvenOnAToyThatWasGrantedIt()
    {
        // Retiring is documented as removing an item from every manifest. A
        // console that then said "sent: yes" would be contradicting the
        // product — and the grant is still shown, because an operator's own
        // decision must not become invisible either.
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        await controller.SetDeviceContent(
            device.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench"), default);
        await controller.RetireContentItem(
            db.ContentItems.Single().Id,
            new InternalContentItemActionRequest(true, "wrong ending"), default);

        var row = Assert.Single(await Rows(controller, device.Id), r => r.ItemKey == "new-story");
        Assert.True(row.Retired);
        Assert.False(row.Allowed);
        Assert.Equal("allow", row.OverrideMode);
    }

    [Fact]
    public async Task CatalogueList_CountsTheToysAnItemWasSentTo_AndSeesTheFileOnDisk()
    {
        // The middle step of the workflow — "send it to ONE toy" — produced no
        // visible change anywhere before this count existed, so "uploaded and on
        // nobody's toy" looked exactly like "uploaded and on the bench toy".
        var db = NewDb();
        var a = Dev();
        var b = Dev();
        var untouched = Dev();
        db.Devices.AddRange(a, b, untouched);
        await db.SaveChangesAsync();

        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        foreach (var d in new[] { a, b })
        {
            await controller.SetDeviceContent(
                d.Id, new InternalDeviceContentRequest("story", "new-story", true, "bench"), default);
        }
        // A DENY row must not be counted as "sent to".
        await controller.SetDeviceContent(
            untouched.Id, new InternalDeviceContentRequest("story", "ulik", false, "withheld"), default);

        var ok = Assert.IsType<OkObjectResult>(await controller.GetContentItems(default));
        using var doc = JsonDocument.Parse(WebJson(ok.Value));
        Assert.True(doc.RootElement.GetProperty("uploadRootConfigured").GetBoolean());

        var item = doc.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(2, item.GetProperty("grantedDeviceCount").GetInt32());
        Assert.True(item.GetProperty("fileOnDisk").GetBoolean());
        Assert.False(item.GetProperty("defaultEnabled").GetBoolean());
    }

    [Fact]
    public async Task CatalogueList_ReportsAMissingFile_RatherThanAssumingTheVolumeIsIntact()
    {
        // A restored backup that did not carry the audio, or a remounted volume,
        // leaves a row describing a file that is not there. The manifest would
        // still advertise it, so the console has to say so.
        var db = NewDb();
        var controller = NewController(db, Config());
        await controller.UploadStoryContent("new-story", "T", "bench", File(Mp3Bytes()), default);
        System.IO.File.Delete(Path.Combine(_uploadRoot, "new-story-v1.mp3"));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetContentItems(default));
        using var doc = JsonDocument.Parse(WebJson(ok.Value));
        Assert.False(doc.RootElement.GetProperty("items").EnumerateArray()
            .Single().GetProperty("fileOnDisk").GetBoolean());
    }

    private static async Task<List<AdminDeviceContentItemDto>> Rows(
        InternalController controller, Guid deviceId)
    {
        var ok = Assert.IsType<OkObjectResult>(await controller.GetDeviceContent(deviceId, default));
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("items")
            .Deserialize<List<AdminDeviceContentItemDto>>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}

/// <summary>
/// The migration is HAND-WRITTEN, because <c>dotnet-ef</c> cannot run against
/// this solution — so nothing generated it and nothing but this checks it. The
/// in-memory provider used by every other test here does not participate in
/// migrations at all, which means a migration that fails to apply would leave
/// the whole suite green and break on the first real boot.
/// </summary>
public class ContentItemMigrationTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "areg-content-mig-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* best-effort temp cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EveryMigration_AppliesToAFreshSqliteFile_AndTheRowRoundTrips()
    {
        Directory.CreateDirectory(_dir);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "migrated.db")}")
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        db.ContentItems.Add(new ContentItem
        {
            Id = Guid.NewGuid(),
            Kind = "story",
            ItemKey = "new-story",
            Title = "Նոր հեքիաթ",
            Version = 1,
            RelativePath = "new-story-v1.mp3",
            Sha256 = new string('b', 64),
            SizeBytes = 4321,
            DefaultEnabled = false,
            CreatedBy = "owner",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var stored = await db.ContentItems.AsNoTracking().SingleAsync();
        Assert.Equal("new-story", stored.ItemKey);
        Assert.Equal("Նոր հեքիաթ", stored.Title);
        Assert.False(stored.DefaultEnabled);
        Assert.Null(stored.RetiredAt);
    }

    [Fact]
    public async Task TheDatabaseItself_RejectsASecondRowForTheSameItem()
    {
        // ItemKey is the content-file lookup key, so two rows sharing one would
        // make that endpoint ambiguous. The unique index is what lets a
        // re-upload be an upsert that bumps the version, instead of a race.
        Directory.CreateDirectory(_dir);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "unique.db")}")
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        static ContentItem Row() => new()
        {
            Id = Guid.NewGuid(),
            Kind = "story",
            ItemKey = "new-story",
            Title = "T",
            Version = 1,
            RelativePath = "new-story-v1.mp3",
            Sha256 = new string('b', 64),
            SizeBytes = 1,
            CreatedBy = "owner",
            CreatedAt = DateTime.UtcNow,
        };

        db.ContentItems.Add(Row());
        await db.SaveChangesAsync();

        db.ContentItems.Add(Row());
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// The two story-shaping parent toggles — in-story PAUSES and VARIANT
/// ENDINGS — end to end: pause-shaped ownership + idempotency + audit-on-flip
/// in ParentService, the 400-on-missing-flag contract at the controller, and
/// the content-manifest delivery of both per-device flags.
/// <para>
/// Structured as a deliberate copy of <see cref="StoryIntroToggleTests"/>.
/// These are the second and third toggles on an identical contract, and the
/// tests staying recognisably parallel is what makes a divergence obvious.
/// </para>
/// </summary>
public class StoryFeatureTogglesTests
{
    private sealed class TestDb : DbContext
    {
        public TestDb(DbContextOptions<TestDb> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Device>(e =>
            {
                e.HasKey(d => d.Id);
                e.Ignore(d => d.Conversations);
                e.Ignore(d => d.ParentDevices);
            });
            mb.Entity<ParentDevice>(e =>
            {
                e.HasKey(pd => new { pd.ParentId, pd.DeviceId });
                e.Ignore(pd => pd.Parent);
                e.Ignore(pd => pd.Device);
            });
            mb.Entity<AuditEvent>(e => e.HasKey(a => a.Id));
        }
    }

    private static (ParentService Service, TestDb Db) Create()
    {
        var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var svc = new ParentService(db, new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<ParentService>>());
        return (svc, db);
    }

    private static async Task<(Guid ParentId, Guid DeviceId)> SeedLinkedAsync(TestDb db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Add(new Device { Id = deviceId, MacAddress = "m", Name = "toy" });
        db.Add(new ParentDevice { ParentId = parentId, DeviceId = deviceId });
        await db.SaveChangesAsync();
        return (parentId, deviceId);
    }

    private static ParentController ControllerWithParent(IParentService service)
    {
        var controller = new ParentController(service, new Application.Helpers.ExportCooldown());
        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
            }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    // ---- defaults -------------------------------------------------------

    /// <summary>KEYSTONE. Both features are ON out of the box, so a parent
    /// opts OUT rather than having to discover them. This pins the C# side of
    /// the same contract the migration's <c>defaultValue: true</c> pins for
    /// rows that already existed — the scaffolder writes <c>false</c> for a
    /// bool by default, so the two halves are easy to let drift apart.</summary>
    [Fact]
    public void NewDevice_BothStoryFeatures_DefaultOn()
    {
        var device = new Device { MacAddress = "m", Name = "toy" };

        Assert.True(device.StoryPausesEnabled);
        Assert.True(device.VariantEndingsEnabled);
    }

    // ---- in-story pauses ------------------------------------------------

    [Fact]
    public async Task SetStoryPauses_Flip_PersistsAndAudits()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryPausesAsync(parentId, deviceId, enabled: false);

        Assert.True(ok);
        Assert.False((await db.Set<Device>().SingleAsync()).StoryPausesEnabled);
        var audit = await db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentDeviceStoryPausesSet, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Contains("\"enabled\":false", audit.Metadata);
    }

    // Idempotent no-op: already-ON stays ON, no audit row (nothing changed).
    [Fact]
    public async Task SetStoryPauses_NoOp_WritesNoAudit()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryPausesAsync(parentId, deviceId, enabled: true);

        Assert.True(ok);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task SetStoryPauses_NotLinked_SilentFalse()
    {
        var (svc, db) = Create();
        var (_, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryPausesAsync(Guid.NewGuid(), deviceId, false);

        Assert.False(ok);
        Assert.True((await db.Set<Device>().SingleAsync()).StoryPausesEnabled);
    }

    // ---- variant endings ------------------------------------------------

    [Fact]
    public async Task SetVariantEndings_Flip_PersistsAndAudits()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceVariantEndingsAsync(parentId, deviceId, enabled: false);

        Assert.True(ok);
        Assert.False((await db.Set<Device>().SingleAsync()).VariantEndingsEnabled);
        var audit = await db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentDeviceVariantEndingsSet, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Contains("\"enabled\":false", audit.Metadata);
    }

    [Fact]
    public async Task SetVariantEndings_NoOp_WritesNoAudit()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceVariantEndingsAsync(parentId, deviceId, enabled: true);

        Assert.True(ok);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task SetVariantEndings_NotLinked_SilentFalse()
    {
        var (svc, db) = Create();
        var (_, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceVariantEndingsAsync(Guid.NewGuid(), deviceId, false);

        Assert.False(ok);
        Assert.True((await db.Set<Device>().SingleAsync()).VariantEndingsEnabled);
    }

    /// <summary>The two toggles are INDEPENDENT columns. Sharing one request
    /// record makes it cheap to wire the second endpoint into the first
    /// service method by accident, which would show up only as "I turned off
    /// pauses and lost my alternate endings too".</summary>
    [Fact]
    public async Task Toggles_AreIndependent()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        await svc.SetDeviceStoryPausesAsync(parentId, deviceId, enabled: false);

        var device = await db.Set<Device>().SingleAsync();
        Assert.False(device.StoryPausesEnabled);
        Assert.True(device.VariantEndingsEnabled);
        Assert.True(device.StoryIntroEnabled);
    }

    // ---- manifest delivery ----------------------------------------------

    [Fact]
    public async Task ContentManifest_CarriesBothDeviceFlags()
    {
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        var deviceId = Guid.NewGuid();
        deviceService.GetDeviceAsync(deviceId).Returns(new Device
        {
            Id = deviceId,
            MacAddress = "m",
            Name = "toy",
            StoryPausesEnabled = false,
            VariantEndingsEnabled = false,
        });
        var controller = new DeviceController(deviceService, Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(ContentManifestResponse.Empty());

        var ok = Assert.IsType<OkObjectResult>(
            await controller.GetContentManifest(manifest, Substitute.For<IChildService>()));
        var body = Assert.IsType<ContentManifestResponse>(ok.Value);

        Assert.False(body.StoryPausesEnabled);
        Assert.False(body.VariantEndingsEnabled);
    }

    /// <summary>A device row the controller cannot load must fall back to the
    /// shipped default (ON) rather than to <c>default(bool)</c>. Getting this
    /// backwards would silently strip both features from a toy whose row was
    /// momentarily unreadable.</summary>
    [Fact]
    public async Task ContentManifest_NoDeviceRow_FlagsFallBackToOn()
    {
        var deviceService = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        deviceService.GetDeviceAsync(deviceId).Returns((Device?)null);
        var controller = new DeviceController(deviceService, Substitute.For<IConfiguration>());
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        var manifest = Substitute.For<IContentManifestService>();
        manifest.Build().Returns(ContentManifestResponse.Empty());

        var ok = Assert.IsType<OkObjectResult>(
            await controller.GetContentManifest(manifest, Substitute.For<IChildService>()));
        var body = Assert.IsType<ContentManifestResponse>(ok.Value);

        Assert.True(body.StoryPausesEnabled);
        Assert.True(body.VariantEndingsEnabled);
    }

    // ---- controller request shape ---------------------------------------

    [Fact]
    public async Task StoryPauses_MissingFlag_Returns400_WithoutCallingService()
    {
        var service = Substitute.For<IParentService>();
        var controller = ControllerWithParent(service);

        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceStoryPauses(Guid.NewGuid(), new DeviceStoryIntroRequest(null)));
        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceStoryPauses(Guid.NewGuid(), null!));
        await service.DidNotReceiveWithAnyArgs()
            .SetDeviceStoryPausesAsync(default, default, default);
    }

    [Fact]
    public async Task VariantEndings_MissingFlag_Returns400_WithoutCallingService()
    {
        var service = Substitute.For<IParentService>();
        var controller = ControllerWithParent(service);

        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceVariantEndings(Guid.NewGuid(), new DeviceStoryIntroRequest(null)));
        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceVariantEndings(Guid.NewGuid(), null!));
        await service.DidNotReceiveWithAnyArgs()
            .SetDeviceVariantEndingsAsync(default, default, default);
    }

    /// <summary>Silent 404 on a device that is not this parent's — same
    /// no-existence-leak phrasing the pause / story-intro toggles use.</summary>
    [Fact]
    public async Task Toggles_NotOwned_Return404()
    {
        var service = Substitute.For<IParentService>();
        service.SetDeviceStoryPausesAsync(default, default, default)
            .ReturnsForAnyArgs(false);
        service.SetDeviceVariantEndingsAsync(default, default, default)
            .ReturnsForAnyArgs(false);
        var controller = ControllerWithParent(service);

        Assert.IsType<NotFoundObjectResult>(
            await controller.SetDeviceStoryPauses(Guid.NewGuid(), new DeviceStoryIntroRequest(false)));
        Assert.IsType<NotFoundObjectResult>(
            await controller.SetDeviceVariantEndings(Guid.NewGuid(), new DeviceStoryIntroRequest(false)));
    }
}

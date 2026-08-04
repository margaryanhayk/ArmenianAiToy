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
/// B3 spoken-story-intro toggle tests: pause-shaped ownership + idempotency
/// + audit-on-flip in ParentService, and the content-manifest delivery of
/// the per-device flag.
/// </summary>
public class StoryIntroToggleTests
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

    [Fact]
    public async Task SetStoryIntro_Flip_PersistsAndAudits()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryIntroAsync(parentId, deviceId, enabled: false);

        Assert.True(ok);
        Assert.False((await db.Set<Device>().SingleAsync()).StoryIntroEnabled);
        var audit = await db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentDeviceStoryIntroSet, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Contains("\"enabled\":false", audit.Metadata);
    }

    // Idempotent no-op: already-ON stays ON, no audit row (nothing changed).
    [Fact]
    public async Task SetStoryIntro_NoOp_WritesNoAudit()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryIntroAsync(parentId, deviceId, enabled: true);

        Assert.True(ok);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task SetStoryIntro_NotLinked_SilentFalse()
    {
        var (svc, db) = Create();
        var (_, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryIntroAsync(Guid.NewGuid(), deviceId, false);

        Assert.False(ok);
        Assert.True((await db.Set<Device>().SingleAsync()).StoryIntroEnabled);
    }

    [Fact]
    public async Task ContentManifest_CarriesDeviceIntroFlag()
    {
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        var deviceId = Guid.NewGuid();
        deviceService.GetDeviceAsync(deviceId).Returns(new Device
        {
            Id = deviceId, MacAddress = "m", Name = "toy", StoryIntroEnabled = false,
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
        Assert.False(body.StoryIntroEnabled);
    }

    [Fact]
    public async Task Controller_MissingFlag_Returns400()
    {
        var service = Substitute.For<IParentService>();
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

        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceStoryIntro(Guid.NewGuid(), new DeviceStoryIntroRequest(null)));
        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceStoryIntro(Guid.NewGuid(), null!));
        await service.DidNotReceiveWithAnyArgs()
            .SetDeviceStoryIntroAsync(default, default, default);
    }
}

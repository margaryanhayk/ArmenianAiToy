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
/// The AFTER-STORY QUESTION parent toggle (owner request 2026-09-07:
/// "question clips must be optional") end to end: pause-shaped ownership +
/// idempotency + audit-on-flip in ParentService, the 400-on-missing-flag
/// contract at the controller, the content-manifest delivery of the
/// per-device flag, and the unlink factory reset. The
/// LinkedDeviceDto projection lives beside its siblings in
/// <see cref="ParentServiceGetLinkedDeviceDetailsTests"/>, whose harness
/// models the joins that projection needs.
/// <para>
/// Structured as a deliberate copy of <see cref="StoryFeatureTogglesTests"/>
/// — the fourth toggle on the identical contract, kept recognisably
/// parallel so a divergence is obvious.
/// </para>
/// </summary>
public class StoryQuestionsToggleTests
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

    /// <summary>KEYSTONE. ON out of the box: a parent opts OUT of the
    /// question rather than discovering it. Pins the C# side of the contract
    /// the migration's <c>defaultValue: true</c> pins for existing rows.</summary>
    [Fact]
    public void NewDevice_StoryQuestions_DefaultOn()
    {
        var device = new Device { MacAddress = "m", Name = "toy" };

        Assert.True(device.StoryQuestionsEnabled);
    }

    [Fact]
    public async Task SetStoryQuestions_Flip_PersistsAndAudits()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryQuestionsAsync(parentId, deviceId, enabled: false);

        Assert.True(ok);
        Assert.False((await db.Set<Device>().SingleAsync()).StoryQuestionsEnabled);
        var audit = await db.Set<AuditEvent>().SingleAsync();
        Assert.Equal(AuditEventType.ParentDeviceStoryQuestionsSet, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Contains("\"enabled\":false", audit.Metadata);
    }

    [Fact]
    public async Task SetStoryQuestions_NoOp_WritesNoAudit()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryQuestionsAsync(parentId, deviceId, enabled: true);

        Assert.True(ok);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task SetStoryQuestions_NotLinked_SilentFalse()
    {
        var (svc, db) = Create();
        var (_, deviceId) = await SeedLinkedAsync(db);

        var ok = await svc.SetDeviceStoryQuestionsAsync(Guid.NewGuid(), deviceId, false);

        Assert.False(ok);
        Assert.True((await db.Set<Device>().SingleAsync()).StoryQuestionsEnabled);
    }

    /// <summary>Independent column: turning the question off must leave the
    /// intro, the pauses and the variant endings exactly as they were. The
    /// summary/lesson has no column at all — it is never gated.</summary>
    [Fact]
    public async Task StoryQuestions_IsIndependentOfTheOtherStoryToggles()
    {
        var (svc, db) = Create();
        var (parentId, deviceId) = await SeedLinkedAsync(db);

        await svc.SetDeviceStoryQuestionsAsync(parentId, deviceId, enabled: false);

        var device = await db.Set<Device>().SingleAsync();
        Assert.False(device.StoryQuestionsEnabled);
        Assert.True(device.StoryIntroEnabled);
        Assert.True(device.StoryPausesEnabled);
        Assert.True(device.VariantEndingsEnabled);
    }

    // ---- manifest delivery ----------------------------------------------

    [Fact]
    public async Task ContentManifest_CarriesTheDeviceFlag()
    {
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        var deviceId = Guid.NewGuid();
        deviceService.GetDeviceAsync(deviceId).Returns(new Device
        {
            Id = deviceId,
            MacAddress = "m",
            Name = "toy",
            StoryQuestionsEnabled = false,
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

        Assert.False(body.StoryQuestionsEnabled);
        // Siblings untouched by the new flag.
        Assert.True(body.StoryPausesEnabled);
        Assert.True(body.StoryIntroEnabled);
    }

    /// <summary>A device row the controller cannot load must fall back to the
    /// shipped default (ON) rather than <c>default(bool)</c>.</summary>
    [Fact]
    public async Task ContentManifest_NoDeviceRow_FlagFallsBackToOn()
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

        Assert.True(body.StoryQuestionsEnabled);
    }

    // ---- controller request shape ---------------------------------------

    [Fact]
    public async Task StoryQuestions_MissingFlag_Returns400_WithoutCallingService()
    {
        var service = Substitute.For<IParentService>();
        var controller = ControllerWithParent(service);

        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceStoryQuestions(Guid.NewGuid(), new DeviceStoryIntroRequest(null)));
        Assert.IsType<BadRequestObjectResult>(
            await controller.SetDeviceStoryQuestions(Guid.NewGuid(), null!));
        await service.DidNotReceiveWithAnyArgs()
            .SetDeviceStoryQuestionsAsync(default, default, default);
    }

    [Fact]
    public async Task StoryQuestions_NotOwned_Returns404()
    {
        var service = Substitute.For<IParentService>();
        service.SetDeviceStoryQuestionsAsync(default, default, default)
            .ReturnsForAnyArgs(false);
        var controller = ControllerWithParent(service);

        Assert.IsType<NotFoundObjectResult>(
            await controller.SetDeviceStoryQuestions(Guid.NewGuid(), new DeviceStoryIntroRequest(false)));
    }

    [Fact]
    public async Task StoryQuestions_Ok_EchoesTheNewValue()
    {
        var service = Substitute.For<IParentService>();
        service.SetDeviceStoryQuestionsAsync(default, default, default)
            .ReturnsForAnyArgs(true);
        var controller = ControllerWithParent(service);

        var ok = Assert.IsType<OkObjectResult>(
            await controller.SetDeviceStoryQuestions(Guid.NewGuid(), new DeviceStoryIntroRequest(false)));
        Assert.Contains("storyQuestionsEnabled = False", ok.Value!.ToString());
    }
}

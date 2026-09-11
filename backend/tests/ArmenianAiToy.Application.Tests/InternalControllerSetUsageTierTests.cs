using ArmenianAiToy.Api.Controllers;
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

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the operator endpoint <c>POST /api/internal/devices/{id}/tier</c>
/// — the usage-tier metering foundation's operator surface (2026-09-11,
/// ships behind <c>Usage:Tiers:Enabled</c>=false; this endpoint works
/// regardless, so a fleet's tiers can be staged before the flag flips).
/// Pins: known-tier sets it and audits; unknown tier → 400 (nothing
/// changed); missing/blank reason → 400; missing device → 404; idempotent
/// (same tier again writes no second audit row).
/// </summary>
public class InternalControllerSetUsageTierTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static InternalController NewController(AppDbContext db, UsageTiersOptions? usageTiers = null)
    {
        var moderation = Substitute.For<IModerationService>();
        var controller = new InternalController(
            db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter(),
            new LibraryStoryQuestionService(Substitute.For<IAiChatClient>()),
            moderation,
            new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<InternalController>>(),
            sessions: null,
            usageTiersOptions: usageTiers ?? new UsageTiersOptions
            {
                Plans = new()
                {
                    new UsageTierPlan { Name = "free", QuestionsPerDay = 30 },
                    new UsageTierPlan { Name = "family", QuestionsPerDay = 100 },
                },
            });
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items["InternalOperator"] = "bench-op";
        return controller;
    }

    private static Device Dev(string tier = "free") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bench",
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        UsageTier = tier,
    };

    [Fact]
    public async Task KnownTier_SetsIt_AndWritesOneAuditRow()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var result = await controller.SetDeviceUsageTier(
            device.Id, new InternalSetUsageTierRequest("family", "upgrade for testing"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var reloaded = await db.Devices.FindAsync(device.Id);
        Assert.Equal("family", reloaded!.UsageTier);
        var audit = Assert.Single(db.AuditEvents);
        Assert.Equal(AuditEventType.InternalConsoleAction, audit.EventType);
        Assert.Contains("device_usage_tier", audit.Metadata);
        Assert.Contains("family", audit.Metadata);
    }

    [Fact]
    public async Task UnknownTier_Returns400_NothingChanged()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var result = await controller.SetDeviceUsageTier(
            device.Id, new InternalSetUsageTierRequest("platinum", "reason"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var reloaded = await db.Devices.FindAsync(device.Id);
        Assert.Equal("free", reloaded!.UsageTier);
        Assert.Empty(db.AuditEvents);
    }

    [Fact]
    public async Task MissingReason_Returns400()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var result = await controller.SetDeviceUsageTier(
            device.Id, new InternalSetUsageTierRequest("family", ""), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task BlankTier_Returns400()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var result = await controller.SetDeviceUsageTier(
            device.Id, new InternalSetUsageTierRequest("   ", "reason"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UnknownDevice_Returns404()
    {
        var db = NewDb();
        var controller = NewController(db);

        var result = await controller.SetDeviceUsageTier(
            Guid.NewGuid(), new InternalSetUsageTierRequest("family", "reason"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SameTierAgain_IsIdempotent_NoSecondAuditRow()
    {
        var db = NewDb();
        var device = Dev(tier: "family");
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var result = await controller.SetDeviceUsageTier(
            device.Id, new InternalSetUsageTierRequest("family", "no-op"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(db.AuditEvents);
    }

    [Fact]
    public async Task TierNameIsTrimmed()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        await controller.SetDeviceUsageTier(
            device.Id, new InternalSetUsageTierRequest("  family  ", "reason"), CancellationToken.None);

        var reloaded = await db.Devices.FindAsync(device.Id);
        Assert.Equal("family", reloaded!.UsageTier);
    }
}

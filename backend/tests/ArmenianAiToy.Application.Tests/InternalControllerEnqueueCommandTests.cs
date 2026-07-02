using System.Text.Json;
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

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the bench/test operator enqueue endpoint
/// <c>POST /api/internal/devices/{deviceId}/commands</c> (OTA foundation).
/// Pins: known-type enqueue creates a Pending DeviceCommand with a clamped
/// TTL; unknown type → 400 (nothing queued); unknown device → 404; missing
/// type → 400. The internal token gate itself is middleware
/// (InternalAdminAuthTests); these tests exercise the action behind it.
/// </summary>
public class InternalControllerEnqueueCommandTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static InternalController NewController(AppDbContext db)
    {
        var moderation = Substitute.For<IModerationService>();
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        var controller = new InternalController(
            db, new InMemoryCuratedStoryLibrary(), new OpenAICostMeter(),
            new LibraryStoryQuestionService(Substitute.For<IAiChatClient>()),
            moderation,
            new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<InternalController>>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Items["InternalOperator"] = "bench-op";
        return controller;
    }

    private static Device Dev() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bench",
        MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
        RegisteredAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Enqueue_KnownType_CreatesPendingCommand_WithPayloadAndTtl()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var controller = NewController(db);
        var payload = JsonDocument.Parse("{\"note\":\"bench\"}").RootElement;

        var result = await controller.EnqueueDeviceCommand(
            device.Id,
            new InternalEnqueueCommandRequest(DeviceCommandTypes.FirmwareUpdate, payload, TtlSeconds: 120),
            new DeviceCommandService(db),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var stored = Assert.Single(db.DeviceCommands.ToList());
        Assert.Equal(device.Id, stored.DeviceId);
        Assert.Equal("firmware_update", stored.Type);
        Assert.Equal(DeviceCommandStatus.Pending, stored.Status);
        Assert.Contains("bench", stored.PayloadJson);
        Assert.NotNull(stored.ExpiresAt);
        // TTL honored: expiry ≈ now + 120 s (well under the 3600 default).
        Assert.True((stored.ExpiresAt!.Value - stored.CreatedAt).TotalSeconds <= 121);
        // Response carries the command id the bench needs.
        Assert.Contains("commandId", JsonSerializer.Serialize(ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public async Task Enqueue_TtlBelowFloor_IsClampedTo60()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        await NewController(db).EnqueueDeviceCommand(
            device.Id,
            new InternalEnqueueCommandRequest(DeviceCommandTypes.FirmwareUpdate, TtlSeconds: 1),
            new DeviceCommandService(db),
            CancellationToken.None);

        var stored = Assert.Single(db.DeviceCommands.ToList());
        Assert.True((stored.ExpiresAt!.Value - stored.CreatedAt).TotalSeconds >= 59);
    }

    [Fact]
    public async Task Enqueue_UnknownType_Returns400_AndQueuesNothing()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var result = await NewController(db).EnqueueDeviceCommand(
            device.Id,
            new InternalEnqueueCommandRequest("reboot_device"),
            new DeviceCommandService(db),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.DeviceCommands.ToList());
    }

    [Fact]
    public async Task Enqueue_UnknownDevice_Returns404()
    {
        var db = NewDb();

        var result = await NewController(db).EnqueueDeviceCommand(
            Guid.NewGuid(),
            new InternalEnqueueCommandRequest(DeviceCommandTypes.FirmwareUpdate),
            new DeviceCommandService(db),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(db.DeviceCommands.ToList());
    }

    [Fact]
    public async Task Enqueue_MissingType_Returns400()
    {
        var db = NewDb();
        var device = Dev();
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var result = await NewController(db).EnqueueDeviceCommand(
            device.Id,
            new InternalEnqueueCommandRequest(Type: "  "),
            new DeviceCommandService(db),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}

using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>DeviceService.IsModeEnabledForRequestAsync</c>. Covers the
/// four gate branches (override false blocks, override true allows, null
/// inherits device, missing ChildId falls back to device) plus the cross-
/// device probe guard (a ChildId belonging to a different device must
/// never influence the gate).
/// </summary>
public class DeviceServiceIsModeEnabledForRequestTests
{
    private static async Task<(DeviceService Service, AppDbContext Db, SqliteConnection Conn)>
        CreateServiceAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var logger = Substitute.For<ILogger<DeviceService>>();
        return (new DeviceService(db, logger), db, conn);
    }

    private static Guid SeedDevice(
        AppDbContext db,
        bool story, bool game, bool riddle, bool curiosity)
    {
        var id = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = id,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            StoryEnabled = story,
            GameEnabled = game,
            RiddleEnabled = riddle,
            CuriosityEnabled = curiosity
        });
        db.SaveChanges();
        return id;
    }

    private static Guid SeedChild(
        AppDbContext db, Guid deviceId,
        bool? story, bool? game, bool? riddle, bool? curiosity)
    {
        var id = Guid.NewGuid();
        db.Set<Child>().Add(new Child
        {
            Id = id,
            Name = "Alice",
            DeviceId = deviceId,
            Gender = Gender.Girl,
            StoryEnabled = story,
            GameEnabled = game,
            RiddleEnabled = riddle,
            CuriosityEnabled = curiosity
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task ChildOverrideFalse_BlocksEvenIfDeviceFlagIsTrue()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var deviceId = SeedDevice(db, story: true, game: true, riddle: true, curiosity: true);
        var childId = SeedChild(db, deviceId,
            story: false, game: null, riddle: null, curiosity: null);

        var result = await service.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Story);

        Assert.False(result);
    }

    [Fact]
    public async Task ChildOverrideTrue_AllowsEvenIfDeviceFlagIsFalse()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var deviceId = SeedDevice(db, story: false, game: false, riddle: false, curiosity: false);
        var childId = SeedChild(db, deviceId,
            story: null, game: null, riddle: true, curiosity: null);

        var result = await service.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Riddle);

        Assert.True(result);
    }

    [Fact]
    public async Task ChildOverrideNull_InheritsDeviceFlag()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var deviceId = SeedDevice(db, story: true, game: false, riddle: true, curiosity: true);
        var childId = SeedChild(db, deviceId,
            story: null, game: null, riddle: null, curiosity: null);

        Assert.True(await service.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Story));
        Assert.False(await service.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Game));
    }

    [Fact]
    public async Task NullChildId_UsesDeviceLevelBehaviorOnly()
    {
        // Regression guard on the B5 device-level path: no ChildId on
        // the request must behave identically to calling
        // IsDeviceModeEnabledAsync directly.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var deviceId = SeedDevice(db, story: true, game: false, riddle: true, curiosity: true);

        Assert.True(await service.IsModeEnabledForRequestAsync(
            deviceId, childId: null, DetectedMode.Story));
        Assert.False(await service.IsModeEnabledForRequestAsync(
            deviceId, childId: null, DetectedMode.Game));
    }

    [Fact]
    public async Task CrossDeviceChildId_DoesNotApplyOverride()
    {
        // A ChildId belonging to a different device must NEVER affect
        // this device's gate. Even though the child has story=false,
        // the request for the other device must fall back to that
        // device's (true) flag.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var deviceA = SeedDevice(db, story: true, game: true, riddle: true, curiosity: true);
        var deviceB = SeedDevice(db, story: true, game: true, riddle: true, curiosity: true);
        var childOnB = SeedChild(db, deviceB,
            story: false, game: null, riddle: null, curiosity: null);

        // Request claims to come from device A but supplies a ChildId
        // that belongs to device B — override must not cross over.
        var result = await service.IsModeEnabledForRequestAsync(
            deviceA, childOnB, DetectedMode.Story);

        Assert.True(result);
    }

    [Fact]
    public async Task CalmMode_NeverGatedByOverrideOrDevice()
    {
        // Safety invariant: Calm must never be gated by either the
        // device flags or a child override. The resolver's first
        // branch short-circuits before any DB read.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var deviceId = SeedDevice(db, story: false, game: false, riddle: false, curiosity: false);
        var childId = SeedChild(db, deviceId,
            story: false, game: false, riddle: false, curiosity: false);

        Assert.True(await service.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Calm));
        Assert.True(await service.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.None));
    }
}

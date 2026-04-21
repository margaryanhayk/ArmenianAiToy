using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>ParentService.SetChildModeOverridesAsync</c>. Uses real
/// SQLite in-memory (same pattern as DeleteChild/DeleteAccount) so the
/// nullable-bool round trip and the ownership join land on a relational
/// provider. Mirrors DeleteChildAsync's ownership shape: parent must own
/// the device the child belongs to.
/// </summary>
public class ParentServiceSetChildModeOverridesTests
{
    private static async Task<(ParentService Service, AppDbContext Db, SqliteConnection Conn)>
        CreateServiceAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = Substitute.For<IConfiguration>();
        config["Jwt:Key"].Returns("TestSecretKeyThatIsLongEnoughForHmacSha256Validation!");
        var logger = Substitute.For<ILogger<ParentService>>();
        return (new ParentService(db, config, logger), db, conn);
    }

    private static async Task<(Guid ParentId, Guid DeviceId, Guid ChildId)>
        SeedOwnedChildAsync(AppDbContext db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "aa:bb:" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        db.Set<Child>().Add(new Child
        {
            Id = childId,
            Name = "Alice",
            DeviceId = deviceId,
            Gender = Gender.Girl
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId,
            DeviceId = deviceId,
            LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (parentId, deviceId, childId);
    }

    [Fact]
    public async Task SetChildModeOverridesAsync_OwnedChild_PersistsAllFourNullableOverrides()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, childId) = await SeedOwnedChildAsync(db);

        var result = await service.SetChildModeOverridesAsync(
            parentId, childId,
            story: false, game: null, riddle: true, curiosity: null);

        Assert.True(result);
        var reloaded = await db.Set<Child>().FindAsync(childId);
        Assert.False(reloaded!.StoryEnabled);
        Assert.Null(reloaded.GameEnabled);
        Assert.True(reloaded.RiddleEnabled);
        Assert.Null(reloaded.CuriosityEnabled);
    }

    [Fact]
    public async Task SetChildModeOverridesAsync_ChildNotOwnedByParent_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (_, _, childId) = await SeedOwnedChildAsync(db);
        var strangerParentId = Guid.NewGuid();

        var result = await service.SetChildModeOverridesAsync(
            strangerParentId, childId,
            story: false, game: false, riddle: false, curiosity: false);

        Assert.False(result);
        var reloaded = await db.Set<Child>().FindAsync(childId);
        Assert.Null(reloaded!.StoryEnabled);
        Assert.Null(reloaded.GameEnabled);
        Assert.Null(reloaded.RiddleEnabled);
        Assert.Null(reloaded.CuriosityEnabled);
    }

    [Fact]
    public async Task SetChildModeOverridesAsync_UnknownChild_ReturnsFalse()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, _) = await SeedOwnedChildAsync(db);

        var result = await service.SetChildModeOverridesAsync(
            parentId, Guid.NewGuid(),
            story: true, game: true, riddle: true, curiosity: true);

        Assert.False(result);
    }

    [Fact]
    public async Task SetChildModeOverridesAsync_Success_WritesAuditRowWithAllFourStates()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, childId) = await SeedOwnedChildAsync(db);

        Assert.True(await service.SetChildModeOverridesAsync(
            parentId, childId,
            story: false, game: null, riddle: true, curiosity: null));

        var audits = await db.Set<AuditEvent>().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ChildModeOverridesSet, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(childId, audit.TargetChildId);
        Assert.Null(audit.TargetDeviceId);
        Assert.NotNull(audit.Metadata);
        Assert.Contains("\"story\":false", audit.Metadata);
        Assert.Contains("\"game\":null", audit.Metadata);
        Assert.Contains("\"riddle\":true", audit.Metadata);
        Assert.Contains("\"curiosity\":null", audit.Metadata);
    }

    [Fact]
    public async Task SetChildModeOverridesAsync_OwnershipMiss_DoesNotWriteAuditRow()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (_, _, childId) = await SeedOwnedChildAsync(db);

        Assert.False(await service.SetChildModeOverridesAsync(
            Guid.NewGuid(), childId,
            story: false, game: false, riddle: false, curiosity: false));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }
}

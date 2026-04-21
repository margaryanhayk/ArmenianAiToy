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
/// Tests for <c>ParentService.DeleteAccountAsync</c> (C3). Uses real SQLite
/// in-memory for the same reason C2's delete-child tests do: InMemory does
/// not honour relational FK cascades, so the Parent → ParentDevice and
/// Device → (Children/Conversations/Messages) chains only fire on a real
/// provider.
/// </summary>
public class ParentServiceDeleteAccountTests
{
    private static async Task<(ParentService Service, AppDbContext Db, SqliteConnection Conn)> CreateServiceAsync()
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

    private static Guid SeedParent(AppDbContext db, string password, string? email = null)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = email ?? ("p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com"),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RegisteredAt = DateTime.UtcNow
        });
        return id;
    }

    private static (Guid DeviceId, Guid ChildId, Guid ConvId, Guid MsgId)
        SeedDeviceWithChildAndConversation(AppDbContext db)
    {
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
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
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = deviceId,
            ChildId = childId,
            StartedAt = DateTime.UtcNow
        });
        db.Set<Message>().Add(new Message
        {
            Id = msgId,
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "hello",
            Timestamp = DateTime.UtcNow,
            SafetyFlag = SafetyFlag.Clean
        });
        return (deviceId, childId, convId, msgId);
    }

    [Fact]
    public async Task DeleteAccountAsync_SoleOwner_CascadesParentDeviceChildrenConvosMessages()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db, "oldpass12");
        var (deviceId, childId, convId, msgId) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId,
            DeviceId = deviceId,
            LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteAccountAsync(parentId, "oldpass12");

        Assert.True(result);
        Assert.Null(await db.Set<Parent>().FindAsync(parentId));
        Assert.False(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentId));
        // Sole-owner device orphaned → cascaded delete.
        Assert.Null(await db.Set<Device>().FindAsync(deviceId));
        Assert.Null(await db.Set<Child>().FindAsync(childId));
        Assert.Null(await db.Set<Conversation>().FindAsync(convId));
        Assert.Null(await db.Set<Message>().FindAsync(msgId));
    }

    [Fact]
    public async Task DeleteAccountAsync_DeviceSharedWithAnotherParent_PreservesDeviceAndData()
    {
        // Parent A and Parent B both link the same device. When A deletes
        // their account, B's access must be preserved — the device row
        // and its children/conversations/messages stay.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentAId = SeedParent(db, "passa1234");
        var parentBId = SeedParent(db, "passb1234");
        var (deviceId, childId, convId, msgId) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentAId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentBId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteAccountAsync(parentAId, "passa1234");

        Assert.True(result);
        // Parent A gone; Parent A's link gone (FK cascade).
        Assert.Null(await db.Set<Parent>().FindAsync(parentAId));
        Assert.False(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentAId));
        // Parent B intact.
        Assert.NotNull(await db.Set<Parent>().FindAsync(parentBId));
        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentBId && pd.DeviceId == deviceId));
        // Device + its data preserved because another parent still owns it.
        Assert.NotNull(await db.Set<Device>().FindAsync(deviceId));
        Assert.NotNull(await db.Set<Child>().FindAsync(childId));
        Assert.NotNull(await db.Set<Conversation>().FindAsync(convId));
        Assert.NotNull(await db.Set<Message>().FindAsync(msgId));
    }

    [Fact]
    public async Task DeleteAccountAsync_WrongCurrentPassword_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db, "oldpass12");
        await db.SaveChangesAsync();
        var preHash = (await db.Set<Parent>().FindAsync(parentId))!.PasswordHash;

        var result = await service.DeleteAccountAsync(parentId, "wrongpass");

        Assert.False(result);
        var stillThere = await db.Set<Parent>().FindAsync(parentId);
        Assert.NotNull(stillThere);
        Assert.Equal(preHash, stillThere!.PasswordHash);
    }

    [Fact]
    public async Task DeleteAccountAsync_UnknownParent_ReturnsFalse()
    {
        var (service, _, conn) = await CreateServiceAsync();
        await using var _conn = conn;

        var result = await service.DeleteAccountAsync(Guid.NewGuid(), "anything");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAccountAsync_SoleOwner_WritesSingleAuditRowWithCounts()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db, "oldpass12");
        var (deviceId, _, _, _) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.DeleteAccountAsync(parentId, "oldpass12"));

        var audits = await db.Set<AuditEvent>().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ParentAccountDeleted, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);
        // Sole owner of one device → one linked, one orphaned+cascaded.
        Assert.Contains("\"linked_devices\":1", audit.Metadata);
        Assert.Contains("\"orphaned_devices_deleted\":1", audit.Metadata);
    }
}

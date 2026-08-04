using ArmenianAiToy.Application.Helpers;
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
/// Service-layer tests for <c>ParentService.UnlinkDeviceAsync</c>. Covers the
/// orphan-device cleanup that fires when an unlink removes the last parent
/// link to a device. Uses real SQLite in-memory — not <c>UseInMemoryDatabase</c>
/// — because the Device → (Children/Conversations) and Conversation → Messages
/// cascade FKs only fire on a relational provider.
/// </summary>
public class ParentServiceUnlinkDeviceTests
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

    private static Guid SeedParent(AppDbContext db)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = "p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com",
            PasswordHash = "hash",
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
    public async Task UnlinkDeviceAsync_AnotherParentStillLinked_PreservesDeviceAndData()
    {
        // Parent A and Parent B both link the same device. When A unlinks,
        // B's access must be preserved — the device row and its
        // children/conversations/messages stay.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentAId = SeedParent(db);
        var parentBId = SeedParent(db);
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

        var result = await service.UnlinkDeviceAsync(parentAId, deviceId);

        Assert.True(result);
        // Only caller's ParentDevice row removed.
        Assert.False(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentAId && pd.DeviceId == deviceId));
        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentBId && pd.DeviceId == deviceId));
        // Device + subtree preserved because another parent still owns it.
        Assert.NotNull(await db.Set<Device>().FindAsync(deviceId));
        Assert.NotNull(await db.Set<Child>().FindAsync(childId));
        Assert.NotNull(await db.Set<Conversation>().FindAsync(convId));
        Assert.NotNull(await db.Set<Message>().FindAsync(msgId));
    }

    [Fact]
    public async Task UnlinkDeviceAsync_LastRemainingLink_ErasesFamilyData_ButKeepsTheToy()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db);
        var (deviceId, childId, convId, msgId) = SeedDeviceWithChildAndConversation(db);
        // Give the toy a claim code, as a real one has: the point of this
        // test is that the code survives the unlink.
        (await db.Set<Device>().FindAsync(deviceId))!.ClaimCodeHash =
            DeviceApiKeyHasher.Hash("AREG-CLAIM-7H3K-9QXZ");
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Row-count precondition — subtree exists.
        Assert.Equal(1, await db.Set<Device>().CountAsync());
        Assert.Equal(1, await db.Set<Child>().CountAsync());
        Assert.Equal(1, await db.Set<Conversation>().CountAsync());
        Assert.Equal(1, await db.Set<Message>().CountAsync());
        Assert.Equal(1, await db.Set<ParentDevice>().CountAsync());

        var result = await service.UnlinkDeviceAsync(parentId, deviceId);

        Assert.True(result);
        // ParentDevice link gone.
        Assert.Equal(0, await db.Set<ParentDevice>().CountAsync());
        // Every trace of THIS FAMILY is erased.
        Assert.Null(await db.Set<Child>().FindAsync(childId));
        Assert.Null(await db.Set<Conversation>().FindAsync(convId));
        Assert.Null(await db.Set<Message>().FindAsync(msgId));
        Assert.Equal(0, await db.Set<Child>().CountAsync());
        Assert.Equal(0, await db.Set<Conversation>().CountAsync());
        Assert.Equal(0, await db.Set<Message>().CountAsync());

        // KEYSTONE: the TOY survives. Deleting the Device row here also
        // destroyed its identity and its backend key, so the QR printed on
        // the toy pointed at nothing and it could never be paired again.
        var device = await db.Set<Device>().FindAsync(deviceId);
        Assert.NotNull(device);
        Assert.NotNull(device!.ClaimCodeHash);   // still pairable from its QR

        // Back to factory settings for whoever pairs it next. The name is the
        // important one — it is usually a child's name.
        Assert.Equal(string.Empty, device.Name);
        Assert.Null(device.ClaimedAt);
        Assert.False(device.IsPaused);
        Assert.Null(device.BedtimeStart);
        Assert.Null(device.BedtimeEnd);
        Assert.True(device.StoryEnabled);
        Assert.True(device.StoryIntroEnabled);
        Assert.False(device.BedtimeMusicEnabled);

        // Parent itself is unrelated to the unlink — still there.
        Assert.NotNull(await db.Set<Parent>().FindAsync(parentId));
    }

    [Fact]
    public async Task UnlinkedToy_CanBePairedAgainFromItsQr_AndTheNewFamilySeesNothingOld()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var oldParent = SeedParent(db);
        var (deviceId, _, _, _) = SeedDeviceWithChildAndConversation(db);
        const string code = "AREG-CLAIM-7H3K-9QXZ";
        var device = await db.Set<Device>().FindAsync(deviceId);
        device!.ClaimCodeHash = DeviceApiKeyHasher.Hash(code);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = oldParent, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.UnlinkDeviceAsync(oldParent, deviceId));

        // A different family buys / is given the toy and scans the same QR.
        var newParent = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = newParent, Email = "new@x.com", PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // KEYSTONE: this is the whole point of the change — unlink is no
        // longer a one-way door.
        Assert.True(await service.ClaimDeviceAsync(newParent, deviceId, code));
        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == newParent && pd.DeviceId == deviceId));

        // ...and they inherit none of the previous family's data.
        Assert.Equal(0, await db.Set<Child>().CountAsync());
        Assert.Equal(0, await db.Set<Conversation>().CountAsync());
        Assert.Equal(0, await db.Set<Message>().CountAsync());
    }

    [Fact]
    public async Task UnlinkDeviceAsync_UnknownDeviceLink_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db);
        await db.SaveChangesAsync();

        var result = await service.UnlinkDeviceAsync(parentId, Guid.NewGuid());

        Assert.False(result);
        Assert.Equal(0, await db.Set<ParentDevice>().CountAsync());
        Assert.Equal(0, await db.Set<Device>().CountAsync());
    }

    [Fact]
    public async Task UnlinkDeviceAsync_SecondCallAfterSuccess_ReturnsFalseIdempotently()
    {
        // After the first successful unlink removes the last link, the
        // device is gone too. A repeat call gets the silent "not found"
        // false, indistinguishable from a stranger's call.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db);
        var (deviceId, _, _, _) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.UnlinkDeviceAsync(parentId, deviceId));
        Assert.False(await service.UnlinkDeviceAsync(parentId, deviceId));
    }

    [Fact]
    public async Task UnlinkDeviceAsync_DeviceLinkedOnlyToOtherParent_ReturnsFalseAndDoesNotMutate()
    {
        // Parent A tries to unlink a device only Parent B owns — no
        // existence leak, DB unchanged, returns false.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentAId = SeedParent(db);
        var parentBId = SeedParent(db);
        var (deviceId, childId, convId, msgId) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentBId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.UnlinkDeviceAsync(parentAId, deviceId);

        Assert.False(result);
        Assert.True(await db.Set<ParentDevice>()
            .AnyAsync(pd => pd.ParentId == parentBId && pd.DeviceId == deviceId));
        Assert.NotNull(await db.Set<Device>().FindAsync(deviceId));
        Assert.NotNull(await db.Set<Child>().FindAsync(childId));
        Assert.NotNull(await db.Set<Conversation>().FindAsync(convId));
        Assert.NotNull(await db.Set<Message>().FindAsync(msgId));
        // Ownership probe must not leave any audit trace.
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task UnlinkDeviceAsync_LastLink_WritesAuditRowWithOrphanCascadedTrue()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = SeedParent(db);
        var (deviceId, _, _, _) = SeedDeviceWithChildAndConversation(db);
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.UnlinkDeviceAsync(parentId, deviceId));

        var audits = await db.Set<AuditEvent>().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ParentDeviceUnlinked, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);
        Assert.Contains("\"orphan_cascaded\":true", audit.Metadata);
    }

}

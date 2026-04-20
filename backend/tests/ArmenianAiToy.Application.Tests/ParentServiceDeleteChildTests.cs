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
/// Tests for <c>ParentService.DeleteChildAsync</c> (C2). Uses real SQLite
/// in-memory — not <c>UseInMemoryDatabase</c> — because the Message cascade
/// from Conversation (schema FK, <c>onDelete: Cascade</c>) only fires on a
/// real relational provider. The InMemory provider would silently pass a
/// failing cascade assertion.
/// </summary>
public class ParentServiceDeleteChildTests
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

    private static async Task<(Guid ParentId, Guid DeviceId, Guid ChildId, Guid ConvId, Guid MsgId)>
        SeedOwnedChildWithConversationAsync(AppDbContext db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
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
        await db.SaveChangesAsync();
        return (parentId, deviceId, childId, convId, msgId);
    }

    [Fact]
    public async Task DeleteChildAsync_OwnedChild_DeletesChildAndCascadesConversationsAndMessages()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, childId, convId, msgId) = await SeedOwnedChildWithConversationAsync(db);

        var result = await service.DeleteChildAsync(parentId, childId);

        Assert.True(result);
        Assert.Null(await db.Set<Child>().FindAsync(childId));
        Assert.Null(await db.Set<Conversation>().FindAsync(convId));
        // Messages cascade from Conversations at the DB level — proves the
        // schema FK on Messages.ConversationId is honoured by the service-
        // layer conversation delete.
        Assert.Null(await db.Set<Message>().FindAsync(msgId));
    }

    [Fact]
    public async Task DeleteChildAsync_ChildNotOwnedByParent_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (_, _, childId, convId, msgId) = await SeedOwnedChildWithConversationAsync(db);
        var strangerParentId = Guid.NewGuid();

        var result = await service.DeleteChildAsync(strangerParentId, childId);

        Assert.False(result);
        Assert.NotNull(await db.Set<Child>().FindAsync(childId));
        Assert.NotNull(await db.Set<Conversation>().FindAsync(convId));
        Assert.NotNull(await db.Set<Message>().FindAsync(msgId));
    }

    [Fact]
    public async Task DeleteChildAsync_UnknownChild_ReturnsFalse()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteChildAsync(parentId, Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteChildAsync_SecondCallAfterSuccess_ReturnsFalseIdempotently()
    {
        // After a successful delete the child row is gone, so a repeat call
        // from the same parent gets the same "not found" silent-false as a
        // stranger — parent-facing behavior is indistinguishable.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, childId, _, _) = await SeedOwnedChildWithConversationAsync(db);

        Assert.True(await service.DeleteChildAsync(parentId, childId));
        Assert.False(await service.DeleteChildAsync(parentId, childId));
    }
}

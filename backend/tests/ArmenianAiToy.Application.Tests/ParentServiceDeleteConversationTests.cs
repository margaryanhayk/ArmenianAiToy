using System.Text.Json.Nodes;
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
/// Tests for <c>ParentService.DeleteConversationAsync</c>. Real SQLite
/// in-memory — not <c>UseInMemoryDatabase</c> — because the Message
/// cascade from Conversation (schema FK, <c>onDelete: Cascade</c>) only
/// fires on a relational provider. Same rationale the
/// <see cref="ParentServiceDeleteChildTests"/> docstring records.
/// </summary>
public class ParentServiceDeleteConversationTests
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

    private static async Task<(Guid ParentId, Guid DeviceId, Guid ConvId, Guid MsgId)>
        SeedOwnedConversationAsync(AppDbContext db)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
            Name = "Test",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
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
        return (parentId, deviceId, convId, msgId);
    }

    [Fact]
    public async Task DeleteConversationAsync_OwnedConversation_DeletesConversationAndCascadesMessages()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, convId, msgId) = await SeedOwnedConversationAsync(db);

        var result = await service.DeleteConversationAsync(parentId, convId);

        Assert.True(result);
        Assert.Null(await db.Set<Conversation>().FindAsync(convId));
        // Messages cascade from Conversations at the DB level — proves the
        // schema FK on Messages.ConversationId is honoured.
        Assert.Null(await db.Set<Message>().FindAsync(msgId));
    }

    [Fact]
    public async Task DeleteConversationAsync_NotOwnedByParent_ReturnsFalseAndDoesNotMutate()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (_, _, convId, msgId) = await SeedOwnedConversationAsync(db);
        var strangerParentId = Guid.NewGuid();

        var result = await service.DeleteConversationAsync(strangerParentId, convId);

        Assert.False(result);
        Assert.NotNull(await db.Set<Conversation>().FindAsync(convId));
        Assert.NotNull(await db.Set<Message>().FindAsync(msgId));
        // No audit row for a failure path.
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task DeleteConversationAsync_UnknownConversation_ReturnsFalse()
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

        var result = await service.DeleteConversationAsync(parentId, Guid.NewGuid());

        Assert.False(result);
        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task DeleteConversationAsync_SecondCallAfterSuccess_ReturnsFalseIdempotently()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, convId, _) = await SeedOwnedConversationAsync(db);

        Assert.True(await service.DeleteConversationAsync(parentId, convId));
        Assert.False(await service.DeleteConversationAsync(parentId, convId));

        // Only ONE audit row written — the second call was a silent-404
        // miss and wrote nothing.
        var audits = await db.Set<AuditEvent>()
            .Where(a => a.EventType == AuditEventType.ParentConversationDeleted)
            .ToListAsync();
        Assert.Single(audits);
    }

    [Fact]
    public async Task DeleteConversationAsync_Success_WritesExactlyOneAuditRowWithCorrectShape()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, deviceId, convId, _) = await SeedOwnedConversationAsync(db);

        Assert.True(await service.DeleteConversationAsync(parentId, convId));

        var audits = await db.Set<AuditEvent>().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ParentConversationDeleted, audit.EventType);
        Assert.Equal(parentId, audit.ActorParentId);
        Assert.Equal(deviceId, audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);

        var meta = JsonNode.Parse(audit.Metadata!)!.AsObject();
        // conversation_id is in metadata (no dedicated column).
        Assert.Equal(convId.ToString(), (string?)meta["conversation_id"]);
        // One message was seeded for this conversation.
        Assert.Equal(1, (int?)meta["message_count_deleted"]);
        Assert.NotNull(meta["deleted_at_utc"]);
    }

    [Fact]
    public async Task DeleteConversationAsync_FailurePath_WritesNoAuditRow()
    {
        // Explicit reassertion of "no audit row on failure" — split out
        // from the other tests so a future change to the non-owned branch
        // cannot accidentally regress the invariant by side effect.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (_, _, convId, _) = await SeedOwnedConversationAsync(db);

        Assert.False(await service.DeleteConversationAsync(Guid.NewGuid(), convId));
        Assert.False(await service.DeleteConversationAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Empty(await db.Set<AuditEvent>().ToListAsync());
    }

    [Fact]
    public async Task DeleteConversationAsync_AuditFeed_SurfacesEventForActorParent()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, convId, _) = await SeedOwnedConversationAsync(db);

        Assert.True(await service.DeleteConversationAsync(parentId, convId));

        var rows = await service.GetAuditEventsForParentAsync(parentId, limit: 100, offset: 0);
        Assert.Contains(rows, r =>
            r.EventType == nameof(AuditEventType.ParentConversationDeleted));
    }

    [Fact]
    public async Task DeleteConversationAsync_Export_IncludesEventInParentScopedAuditSlice()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var (parentId, _, convId, _) = await SeedOwnedConversationAsync(db);

        Assert.True(await service.DeleteConversationAsync(parentId, convId));

        var export = await service.BuildExportAsync(parentId);
        Assert.NotNull(export);
        Assert.Contains(export!.AuditEvents, a =>
            a.EventType == nameof(AuditEventType.ParentConversationDeleted));
    }
}

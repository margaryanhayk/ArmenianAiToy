using System.Text.Json;
using System.Text.Json.Nodes;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="RetentionPurgeService"/>. Uses real SQLite
/// in-memory (not <c>UseInMemoryDatabase</c>) because the
/// Message-cascades-from-Conversation contract only fires on a relational
/// provider. Same rationale the C2 delete-child tests document.
///
/// <para>
/// Tests drive one tick at a time via the public <c>RunTickAsync</c>
/// hook rather than running <c>ExecuteAsync</c> under a real clock.
/// </para>
/// </summary>
public class RetentionPurgeServiceTests
{
    private sealed record Harness(
        RetentionPurgeService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        ServiceProvider RootProvider) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await RootProvider.DisposeAsync();
            await Conn.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateHarnessAsync(
        int? maxAgeDays = null,
        int? maxBatchSize = null,
        int? runIntervalMinutes = null)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

        var configDict = new Dictionary<string, string?>();
        if (maxAgeDays is not null)
            configDict["Retention:Messages:MaxAgeDays"] = maxAgeDays.Value.ToString();
        if (maxBatchSize is not null)
            configDict["Retention:Messages:MaxBatchSize"] = maxBatchSize.Value.ToString();
        if (runIntervalMinutes is not null)
            configDict["Retention:Messages:RunIntervalMinutes"] = runIntervalMinutes.Value.ToString();
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var provider = services.BuildServiceProvider();

        // Create schema once against the shared connection so the
        // retention service's own scope sees the same tables.
        using (var seedScope = provider.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await seedDb.Database.EnsureCreatedAsync();
        }

        var outerScope = provider.CreateScope();
        var db = outerScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var logger = Substitute.For<ILogger<RetentionPurgeService>>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new RetentionPurgeService(scopeFactory, config, logger);

        return new Harness(service, db, conn, provider);
    }

    private static Guid SeedDevice(AppDbContext db)
    {
        var deviceId = Guid.NewGuid();
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
            Name = "Test",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        return deviceId;
    }

    private static (Guid ConvId, Guid MsgId) SeedConversationWithMessage(
        AppDbContext db,
        Guid deviceId,
        DateTime startedAt,
        DateTime messageAt)
    {
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = deviceId,
            StartedAt = startedAt
        });
        db.Set<Message>().Add(new Message
        {
            Id = msgId,
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "hi",
            Timestamp = messageAt,
            SafetyFlag = SafetyFlag.Clean
        });
        return (convId, msgId);
    }

    private static Guid SeedConversationNoMessages(
        AppDbContext db,
        Guid deviceId,
        DateTime startedAt)
    {
        var convId = Guid.NewGuid();
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = deviceId,
            StartedAt = startedAt
        });
        return convId;
    }

    // The retention tick runs in its own DI scope with a fresh
    // DbContext, so the outer test DbContext's change tracker can hold
    // stale references to entities that have since been deleted in the
    // DB. These helpers query with AsNoTracking so the assertion always
    // reflects the durable row state, not the tracker cache.
    private static async Task<bool> ConversationExistsAsync(AppDbContext db, Guid id)
        => await db.Set<Conversation>().AsNoTracking().AnyAsync(c => c.Id == id);

    private static async Task<bool> MessageExistsAsync(AppDbContext db, Guid id)
        => await db.Set<Message>().AsNoTracking().AnyAsync(m => m.Id == id);

    // -------------------------------------------------------------------
    // A. Old conversation (with old message) is purged.
    // -------------------------------------------------------------------
    [Fact]
    public async Task A_ConversationOlderThanCutoff_IsPurged()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        var (convId, _) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ConversationExistsAsync(h.Db, convId));
    }

    // -------------------------------------------------------------------
    // B. Critical regression guard — a conversation whose most-recent
    //    message is inside the window must never be purged.
    // -------------------------------------------------------------------
    [Fact]
    public async Task B_ConversationInsideCutoff_IsNotPurged()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        // StartedAt long ago, but a recent message keeps it "live".
        var (convId, msgId) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(1));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.True(await ConversationExistsAsync(h.Db, convId));
        Assert.True(await MessageExistsAsync(h.Db, msgId));
        // And no audit row — a noop tick writes nothing.
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync());
    }

    // -------------------------------------------------------------------
    // C. Cascade — messages go with their conversation.
    // -------------------------------------------------------------------
    [Fact]
    public async Task C_MessagesCascadeDeletedWithConversation()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        var (convId, msgId) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(400),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(400));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ConversationExistsAsync(h.Db, convId));
        Assert.False(await MessageExistsAsync(h.Db, msgId));
    }

    // -------------------------------------------------------------------
    // D. Batch cap — first tick purges up to the cap, second tick
    //    finishes the rest. Uses small numbers for speed; the shape of
    //    the assertion is what matters.
    // -------------------------------------------------------------------
    [Fact]
    public async Task D_BatchSizeCapRespected()
    {
        await using var h = await CreateHarnessAsync(
            maxAgeDays: 90, maxBatchSize: 5);
        var deviceId = SeedDevice(h.Db);
        for (int i = 0; i < 6; i++)
        {
            SeedConversationWithMessage(
                h.Db, deviceId,
                startedAt: DateTime.UtcNow - TimeSpan.FromDays(200 + i),
                messageAt: DateTime.UtcNow - TimeSpan.FromDays(200 + i));
        }
        await h.Db.SaveChangesAsync();
        Assert.Equal(6, await h.Db.Set<Conversation>().AsNoTracking().CountAsync());

        await h.Service.RunTickAsync(CancellationToken.None);
        Assert.Equal(1, await h.Db.Set<Conversation>().AsNoTracking().CountAsync());

        await h.Service.RunTickAsync(CancellationToken.None);
        Assert.Equal(0, await h.Db.Set<Conversation>().AsNoTracking().CountAsync());

        // Exactly two audit rows — one per tick-with-deletions. Noop
        // ticks would write nothing.
        var audits = await h.Db.Set<AuditEvent>()
            .AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ConversationsPurgedByRetention)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
    }

    // -------------------------------------------------------------------
    // E. Disabled mode — reached ONLY via explicit non-positive
    //    override. Missing config is 90, never 0.
    // -------------------------------------------------------------------
    [Fact]
    public async Task E_MaxAgeDaysZero_DisablesPurge()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 0);
        var deviceId = SeedDevice(h.Db);
        var (convId, _) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(9999),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(9999));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.True(await ConversationExistsAsync(h.Db, convId));
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task E2_NegativeMaxAgeDays_ClampsToDisabled()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: -1);
        var deviceId = SeedDevice(h.Db);
        var convId = SeedConversationNoMessages(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(9999));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.True(await ConversationExistsAsync(h.Db, convId));
    }

    // -------------------------------------------------------------------
    // F. Fallback anchor on StartedAt when the conversation has no
    //    messages. (Not expected in today's code paths, but the worker
    //    must not leave zero-message stale rows behind.)
    // -------------------------------------------------------------------
    [Fact]
    public async Task F_FallbackToStartedAtWhenNoMessages()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        var oldConvId = SeedConversationNoMessages(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        var recentConvId = SeedConversationNoMessages(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(1));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ConversationExistsAsync(h.Db, oldConvId));
        Assert.True(await ConversationExistsAsync(h.Db, recentConvId));
    }

    // -------------------------------------------------------------------
    // G. Parent-feed invariant — the system-actor row must NOT appear
    //    in GET /api/parents/audit for any parent, under any pagination.
    // -------------------------------------------------------------------
    [Fact]
    public async Task G_SystemActorAuditIsNotExposedInParentAuditFeed()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        await h.Db.SaveChangesAsync();

        // Seed a parent and a non-retention audit row for them, so the
        // feed has something in it to compare against.
        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        h.Db.Set<AuditEvent>().Add(
            AuditEvent.ParentPasswordChanged(parentId));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        var parentService = new ParentService(
            h.Db,
            Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<ParentService>>());

        // Stretch pagination to the full table — no window could surface
        // the null-actor row.
        var rows = await parentService.GetAuditEventsForParentAsync(
            parentId, limit: 100, offset: 0);
        Assert.DoesNotContain(rows, r =>
            r.EventType == nameof(AuditEventType.ConversationsPurgedByRetention));
        // And the parent still sees their own row.
        Assert.Contains(rows, r =>
            r.EventType == nameof(AuditEventType.ParentPasswordChanged));

        // Double-check at the raw table level — there IS a system-actor
        // row in the DB, it just isn't parent-visible.
        var systemActorRows = await h.Db.Set<AuditEvent>()
            .AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ConversationsPurgedByRetention)
            .ToListAsync();
        var systemRow = Assert.Single(systemActorRows);
        Assert.Null(systemRow.ActorParentId);
    }

    // -------------------------------------------------------------------
    // H. Export invariant — the system-actor row must NOT appear in the
    //    auditEvents slice of GET /api/parents/export, for any parent,
    //    so long as that slice remains parent-scoped by ActorParentId.
    // -------------------------------------------------------------------
    [Fact]
    public async Task H_SystemActorAuditIsNotExposedInParentExport()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        await h.Db.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        h.Db.Set<AuditEvent>().Add(
            AuditEvent.ParentPasswordChanged(parentId));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        var parentService = new ParentService(
            h.Db,
            Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<ParentService>>());
        var export = await parentService.BuildExportAsync(parentId);

        Assert.NotNull(export);
        // Expected rows for this parent: the pre-seeded password-change
        // row, plus the ParentDataExported row BuildExportAsync writes
        // in the same transaction. NEITHER of those is the
        // system-actor retention row.
        Assert.DoesNotContain(export!.AuditEvents, a =>
            a.EventType == nameof(AuditEventType.ConversationsPurgedByRetention));
        Assert.Contains(export.AuditEvents, a =>
            a.EventType == nameof(AuditEventType.ParentPasswordChanged));

        // The raw table still holds the system-actor row — it just
        // isn't exported.
        var systemActorRows = await h.Db.Set<AuditEvent>()
            .AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ConversationsPurgedByRetention)
            .ToListAsync();
        Assert.NotEmpty(systemActorRows);
    }

    // -------------------------------------------------------------------
    // I. Audit-row-shape invariant. Two sub-assertions in one fact:
    //    (1) noop tick writes zero audit rows;
    //    (2) deletion tick writes exactly one audit row with the four
    //        counts-only metadata fields, null actor/target columns.
    // -------------------------------------------------------------------
    [Fact]
    public async Task I_AuditRowOnlyWrittenWhenDeletionsOccurred()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);

        // First tick: nothing eligible -> zero audit rows.
        await h.Service.RunTickAsync(CancellationToken.None);
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync());

        // Now seed one old conv + message.
        SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        await h.Db.SaveChangesAsync();

        // Second tick: one deletion -> exactly one audit row.
        await h.Service.RunTickAsync(CancellationToken.None);

        var audits = await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(AuditEventType.ConversationsPurgedByRetention, audit.EventType);
        Assert.Null(audit.ActorParentId);
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);

        // All four documented fields present in the metadata JSON.
        var meta = JsonNode.Parse(audit.Metadata!)!.AsObject();
        Assert.Equal(1, (int)meta["conversations_deleted"]!);
        Assert.Equal(1, (int)meta["messages_deleted"]!);
        Assert.NotNull(meta["cutoff_utc"]);
        Assert.NotNull(meta["batch_size_limit"]);
    }

    // -------------------------------------------------------------------
    // Shipped-default invariant. Missing config MUST resolve to 90, not
    // to 0. A tick with no Retention:* keys must purge old data just
    // like a tick with MaxAgeDays=90.
    // -------------------------------------------------------------------
    [Fact]
    public async Task ShippedDefault_MissingConfig_ResolvesTo90AndPurges()
    {
        // maxAgeDays=null => no Retention:Messages:MaxAgeDays key in
        // the IConfiguration at all.
        await using var h = await CreateHarnessAsync();
        var deviceId = SeedDevice(h.Db);
        var (oldConvId, _) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        var (recentConvId, _) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(1),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(1));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // The 200-day row is past the 90-day default; the 1-day row is not.
        Assert.False(await ConversationExistsAsync(h.Db, oldConvId));
        Assert.True(await ConversationExistsAsync(h.Db, recentConvId));
    }
}

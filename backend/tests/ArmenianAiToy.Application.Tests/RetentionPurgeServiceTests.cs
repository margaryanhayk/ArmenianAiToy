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
        int? runIntervalMinutes = null,
        int? passwordResetGracePeriodHours = null,
        int? dormancyWarnAfterDays = null,
        int? dormancyWarnRefireIntervalDays = null,
        ArmenianAiToy.Application.Notifications.INotifier? notifier = null)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

        // The warn-only dormancy pass resolves INotifier from the
        // per-tick scope. Register a test double when the caller
        // wants to exercise the warn path; otherwise register a
        // no-op so existing tests that only drive the conversation /
        // token passes keep working without touching INotifier.
        services.AddScoped<ArmenianAiToy.Application.Notifications.INotifier>(
            _ => notifier ?? new NoOpNotifier());

        var configDict = new Dictionary<string, string?>();
        if (maxAgeDays is not null)
            configDict["Retention:Messages:MaxAgeDays"] = maxAgeDays.Value.ToString();
        if (maxBatchSize is not null)
            configDict["Retention:Messages:MaxBatchSize"] = maxBatchSize.Value.ToString();
        if (runIntervalMinutes is not null)
            configDict["Retention:Messages:RunIntervalMinutes"] = runIntervalMinutes.Value.ToString();
        if (passwordResetGracePeriodHours is not null)
            configDict["Retention:PasswordResetTokens:GracePeriodHours"]
                = passwordResetGracePeriodHours.Value.ToString();
        if (dormancyWarnAfterDays is not null)
            configDict["Dormancy:Parent:WarnAfterDays"]
                = dormancyWarnAfterDays.Value.ToString();
        if (dormancyWarnRefireIntervalDays is not null)
            configDict["Dormancy:Parent:WarnRefireIntervalDays"]
                = dormancyWarnRefireIntervalDays.Value.ToString();
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

    // ─────────────────────────────────────────────────────────────
    // Password-reset token cleanup — second cleanup pass added to
    // the same worker. Rule: delete rows where ConsumedAt != null
    // OR ExpiresAt < UtcNow - grace (default 24h).
    // ─────────────────────────────────────────────────────────────

    private static Guid SeedParent(AppDbContext db)
    {
        var parentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com",
            PasswordHash = "hash",
            RegisteredAt = DateTime.UtcNow
        });
        return parentId;
    }

    private static Guid SeedResetToken(
        AppDbContext db,
        Guid parentId,
        DateTime expiresAt,
        DateTime? consumedAt)
    {
        var tokenId = Guid.NewGuid();
        db.Set<ParentPasswordResetToken>().Add(new ParentPasswordResetToken
        {
            Id = tokenId,
            ParentId = parentId,
            TokenHash = "hash-" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = expiresAt,
            ConsumedAt = consumedAt
        });
        return tokenId;
    }

    private static async Task<bool> ResetTokenExistsAsync(AppDbContext db, Guid id)
        => await db.Set<ParentPasswordResetToken>().AsNoTracking().AnyAsync(t => t.Id == id);

    [Fact]
    public async Task TokenCleanup_ConsumedToken_IsDeleted()
    {
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var parentId = SeedParent(h.Db);
        // Consumed token with a future ExpiresAt — still within its
        // usable TTL by expiry but already consumed, so useless.
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddMinutes(30),
            consumedAt: DateTime.UtcNow.AddMinutes(-1));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_ExpiredOlderThanGrace_IsDeleted()
    {
        await using var h = await CreateHarnessAsync(
            maxAgeDays: 90, passwordResetGracePeriodHours: 24);
        var parentId = SeedParent(h.Db);
        // Expired 2 days ago — well past the 24h grace window.
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddDays(-2),
            consumedAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_ExpiredWithinGrace_IsKept()
    {
        await using var h = await CreateHarnessAsync(
            maxAgeDays: 90, passwordResetGracePeriodHours: 24);
        var parentId = SeedParent(h.Db);
        // Expired 6 hours ago — inside the 24h grace window, so the
        // cleanup must not delete it yet (avoids deleting a token a
        // client might still try to redeem during clock skew).
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddHours(-6),
            consumedAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.True(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_UnconsumedUnexpired_IsKept()
    {
        // Critical regression guard — a live, usable token must never
        // be deleted by this cleanup. If it were, forgot-password would
        // silently break between the email and the completion click.
        await using var h = await CreateHarnessAsync(
            maxAgeDays: 90, passwordResetGracePeriodHours: 24);
        var parentId = SeedParent(h.Db);
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddMinutes(30),
            consumedAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.True(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_Coexists_WithConversationPurge_InSameTick()
    {
        // Seeds both an old conversation AND a consumed token. One tick
        // must clean up both — proving the second cleanup pass runs
        // regardless of whether the conversation pass found anything.
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var deviceId = SeedDevice(h.Db);
        var (convId, _) = SeedConversationWithMessage(
            h.Db, deviceId,
            startedAt: DateTime.UtcNow - TimeSpan.FromDays(200),
            messageAt: DateTime.UtcNow - TimeSpan.FromDays(200));
        var parentId = SeedParent(h.Db);
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddDays(-5),
            consumedAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ConversationExistsAsync(h.Db, convId));
        Assert.False(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_RunsEvenWhenNoConversationIsEligible()
    {
        // Previously the worker early-returned on zero eligible
        // conversations, which would have silently skipped the new
        // token cleanup. Pins the control-flow: token cleanup must
        // run even on noop-conversations ticks.
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var parentId = SeedParent(h.Db);
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddDays(-5),
            consumedAt: null);
        await h.Db.SaveChangesAsync();
        // No conversations seeded at all → conversation pass is noop.

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.False(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_WorkerDisabled_SkipsTokenCleanupToo()
    {
        // MaxAgeDays<=0 is the "retention worker off" gate. Documents
        // that token cleanup is NOT run independently — the whole
        // worker is either on or off. If this behavior changes in a
        // future slice (independent token-cleanup toggle), this test
        // is the place to state the new contract.
        await using var h = await CreateHarnessAsync(maxAgeDays: 0);
        var parentId = SeedParent(h.Db);
        var tokenId = SeedResetToken(
            h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddDays(-5),
            consumedAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.True(await ResetTokenExistsAsync(h.Db, tokenId));
    }

    [Fact]
    public async Task TokenCleanup_WritesNoAuditRow()
    {
        // Token cleanup is short-lived operational state, not a
        // destructive parent action — it must NOT write audit rows
        // (no "ConversationsPurgedByRetention"-style event for tokens
        // in this slice). Pins the "no audit, no metric" invariant.
        await using var h = await CreateHarnessAsync(maxAgeDays: 90);
        var parentId = SeedParent(h.Db);
        SeedResetToken(h.Db, parentId,
            expiresAt: DateTime.UtcNow.AddDays(-5),
            consumedAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // No conversations-purged row (nothing old enough) AND no new
        // token-cleanup event type — the audit table should be empty
        // with respect to any retention-worker writes.
        var audits = await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync();
        Assert.Empty(audits);
    }

    // --- Warn-only dormant-parent pass -------------------------------

    // Test notifier: captures the bool return the worker sees, plus
    // the email addresses dispatched to. No real email, no throws —
    // the warn pass must not care whether the notifier went to SMTP
    // or memory, only whether the bool says "delivered."
    private sealed class CapturingNotifier : ArmenianAiToy.Application.Notifications.INotifier
    {
        public List<string> SentEmails { get; } = new();
        public bool DeliverResult { get; set; } = true;

        public Task SendPasswordResetAsync(
            string email, string resetToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> SendDormancyWarningAsync(
            string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
        {
            SentEmails.Add(email);
            return Task.FromResult(DeliverResult);
        }
    }

    private sealed class NoOpNotifier : ArmenianAiToy.Application.Notifications.INotifier
    {
        public Task SendPasswordResetAsync(
            string email, string resetToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> SendDormancyWarningAsync(
            string email, DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private static Guid SeedParent(
        AppDbContext db,
        DateTime? lastLoginAt,
        DateTime? dormancyWarnedAt = null)
    {
        var id = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = id,
            Email = $"p-{id:N}@example.com",
            PasswordHash = "x",
            RegisteredAt = DateTime.UtcNow.AddDays(-400),
            LastLoginAt = lastLoginAt,
            DormancyWarnedAt = dormancyWarnedAt
        });
        return id;
    }

    [Fact]
    public async Task WarnPass_EligibleParent_IsWarnedStampedAndAudited()
    {
        // Eligible: LastLoginAt 200 days ago, threshold 180, never
        // warned before. One warn, one stamp, one audit row.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180, notifier: notifier);
        var parentId = SeedParent(h.Db, lastLoginAt: DateTime.UtcNow.AddDays(-200));
        await h.Db.SaveChangesAsync();
        var before = DateTime.UtcNow;

        await h.Service.RunTickAsync(CancellationToken.None);

        var row = await h.Db.Set<Parent>().AsNoTracking().FirstAsync(p => p.Id == parentId);
        Assert.NotNull(row.DormancyWarnedAt);
        Assert.InRange(row.DormancyWarnedAt!.Value, before, DateTime.UtcNow);
        Assert.Single(notifier.SentEmails);
        Assert.Equal(row.Email, notifier.SentEmails[0]);

        var audits = await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentDormancyWarned)
            .ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Null(audit.ActorParentId);         // system actor invariant
        Assert.Null(audit.TargetDeviceId);
        Assert.Null(audit.TargetChildId);
        Assert.NotNull(audit.Metadata);
        var metaNode = JsonNode.Parse(audit.Metadata!);
        Assert.NotNull(metaNode);
        Assert.Equal(180, (int)metaNode!["warn_threshold_days"]!);
        Assert.Equal(30, (int)metaNode["refire_interval_days"]!);
        // Metadata must not contain email / parent id.
        Assert.DoesNotContain(row.Email, audit.Metadata!);
        Assert.DoesNotContain(parentId.ToString(), audit.Metadata!);
    }

    [Fact]
    public async Task WarnPass_NullLastLoginAt_ParentIsExcluded()
    {
        // Pre-migration parents have LastLoginAt=null. Warning them
        // would threshold on a non-signal. Pin that they are NEVER
        // in the eligible set.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180, notifier: notifier);
        var parentId = SeedParent(h.Db, lastLoginAt: null);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Empty(notifier.SentEmails);
        var row = await h.Db.Set<Parent>().AsNoTracking().FirstAsync(p => p.Id == parentId);
        Assert.Null(row.DormancyWarnedAt);
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentDormancyWarned).ToListAsync());
    }

    [Fact]
    public async Task WarnPass_RecentlyActiveParent_IsNotWarned()
    {
        // LastLoginAt 30 days ago, threshold 180. Not eligible.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180, notifier: notifier);
        SeedParent(h.Db, lastLoginAt: DateTime.UtcNow.AddDays(-30));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Empty(notifier.SentEmails);
    }

    [Fact]
    public async Task WarnPass_AlreadyWarnedInsideRefireInterval_IsNotWarnedAgain()
    {
        // Parent is dormant (LastLoginAt 200d ago, threshold 180) but
        // was warned 10 days ago — inside the 30d refire window. Must
        // NOT be re-warned on this tick.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180,
            dormancyWarnRefireIntervalDays: 30,
            notifier: notifier);
        SeedParent(h.Db,
            lastLoginAt: DateTime.UtcNow.AddDays(-200),
            dormancyWarnedAt: DateTime.UtcNow.AddDays(-10));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Empty(notifier.SentEmails);
    }

    [Fact]
    public async Task WarnPass_AlreadyWarnedOutsideRefireInterval_IsWarnedAgain()
    {
        // Parent was warned 40 days ago — outside the 30d refire
        // window. Eligible for a re-warn. DormancyWarnedAt must
        // advance to the new tick's timestamp.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180,
            dormancyWarnRefireIntervalDays: 30,
            notifier: notifier);
        var oldStamp = DateTime.UtcNow.AddDays(-40);
        var parentId = SeedParent(h.Db,
            lastLoginAt: DateTime.UtcNow.AddDays(-200),
            dormancyWarnedAt: oldStamp);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Single(notifier.SentEmails);
        var row = await h.Db.Set<Parent>().AsNoTracking().FirstAsync(p => p.Id == parentId);
        Assert.NotNull(row.DormancyWarnedAt);
        Assert.True(row.DormancyWarnedAt > oldStamp,
            "DormancyWarnedAt must advance on a re-warn, not stay at the old value.");
    }

    [Fact]
    public async Task WarnPass_NotifierReturnsFalse_NoStampNoAudit()
    {
        // Notifier signals "not delivered" — SmtpNotifier does this
        // on a swallowed SMTP failure. The worker must NOT stamp the
        // parent row and MUST NOT write an audit row; next tick
        // retries from scratch.
        var notifier = new CapturingNotifier { DeliverResult = false };
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180, notifier: notifier);
        var parentId = SeedParent(h.Db, lastLoginAt: DateTime.UtcNow.AddDays(-200));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Single(notifier.SentEmails); // attempt was made
        var row = await h.Db.Set<Parent>().AsNoTracking().FirstAsync(p => p.Id == parentId);
        Assert.Null(row.DormancyWarnedAt);
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentDormancyWarned).ToListAsync());
    }

    [Fact]
    public async Task WarnPass_DisabledByDefault_DoesNotWarnAnyone()
    {
        // No dormancyWarnAfterDays set — falls back to 0 (disabled).
        // The pass must skip eligibility entirely even though a
        // clearly-dormant parent is sitting in the DB.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(notifier: notifier);
        SeedParent(h.Db, lastLoginAt: DateTime.UtcNow.AddDays(-400));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Empty(notifier.SentEmails);
    }

    [Fact]
    public async Task WarnPass_SystemActorAuditRow_IsInvisibleToParentReadSurfaces()
    {
        // Pins the ActorParentId==null contract's read-surface
        // consequence: a parent's audit-read query filtered by
        // ActorParentId==parentId returns zero rows even though a
        // ParentDormancyWarned row for that parent exists.
        var notifier = new CapturingNotifier();
        await using var h = await CreateHarnessAsync(
            dormancyWarnAfterDays: 180, notifier: notifier);
        var parentId = SeedParent(h.Db, lastLoginAt: DateTime.UtcNow.AddDays(-200));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // System-actor row exists...
        var systemAudits = await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ParentDormancyWarned).ToListAsync();
        Assert.Single(systemAudits);

        // ...but the parent-scoped read filter returns nothing.
        var parentAudits = await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.ActorParentId == parentId)
            .ToListAsync();
        Assert.Empty(parentAudits);
    }
}

using System.Text.Json.Nodes;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Tests.Helpers;
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
/// C2.2b — audio-blob delete-cascade tests for the
/// <see cref="RetentionPurgeService"/> lifecycle paths.
///
/// Pinned invariants (mirror C2.2a's parent-driven contract):
///  1. Audio cleanup runs AFTER each destructive DB SaveChanges. A
///     blob-store IO failure cannot roll back a retention/dormancy
///     deletion. Audit row reflects post-cleanup counts.
///  2. <c>audio_conversations_attempted</c> /
///     <c>audio_files_deleted</c> / <c>audio_delete_failures</c>
///     appear in the metadata of every system-actor lifecycle event:
///     <c>ConversationsPurgedByRetention</c>,
///     <c>ParentDormancyAnonymized</c>,
///     <c>DeviceDormancyDeleted</c>.
///  3. Sibling/non-eligible conversations and shared-device
///     conversations keep their audio.
///  4. A failing blob store doesn't break the tick — counts surface
///     in audit metadata; DB-level deletes commit.
///
/// Real SQLite in-memory because FK cascades through Messages /
/// Children / Conversations / ParentDevices only fire on a relational
/// provider.
/// </summary>
public class RetentionPurgeServiceAudioCascadeTests
{
    // --- Harness ----------------------------------------------------

    private sealed record Harness(
        RetentionPurgeService Service,
        AppDbContext Db,
        SqliteConnection Conn,
        ServiceProvider RootProvider,
        RecordingBlobStore? Blob) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await RootProvider.DisposeAsync();
            await Conn.DisposeAsync();
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
        public Task SendEmailVerificationAsync(
            string email, string verificationToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> SendDormantDeviceWarningAsync(
            string parentEmail, string deviceName, DateTime lastSeenAtUtc,
            DateTime? deleteAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private static async Task<Harness> CreateHarnessAsync(
        IAudioBlobStore? blobStore = null,
        int? dormancyParentWarnAfterDays = null,
        int? dormancyParentAnonymizeAfterDays = null,
        int? dormancyDeviceWarnAfterDays = null,
        int? dormancyDeviceDeleteAfterDays = null)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(conn));
        services.AddScoped<ArmenianAiToy.Application.Notifications.INotifier>(_ => new NoOpNotifier());

        var defaultBlob = blobStore is null ? new RecordingBlobStore() : null;
        services.AddSingleton<IAudioBlobStore>(_ => blobStore ?? defaultBlob!);

        var configDict = new Dictionary<string, string?>
        {
            // Production-default 90-day cutoff so non-expired audio is
            // preserved unless a test seeds with a much older timestamp.
            ["Retention:Messages:MaxAgeDays"] = "90"
        };
        if (dormancyParentWarnAfterDays is not null)
            configDict["Dormancy:Parent:WarnAfterDays"] = dormancyParentWarnAfterDays.Value.ToString();
        if (dormancyParentAnonymizeAfterDays is not null)
            configDict["Dormancy:Parent:AnonymizeAfterDays"] = dormancyParentAnonymizeAfterDays.Value.ToString();
        if (dormancyDeviceWarnAfterDays is not null)
            configDict["Dormancy:Devices:WarnAfterDays"] = dormancyDeviceWarnAfterDays.Value.ToString();
        if (dormancyDeviceDeleteAfterDays is not null)
            configDict["Dormancy:Devices:DeleteAfterDays"] = dormancyDeviceDeleteAfterDays.Value.ToString();
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var provider = services.BuildServiceProvider();
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

        return new Harness(service, db, conn, provider, defaultBlob);
    }

    // --- Seed helpers -----------------------------------------------

    private static readonly byte[] WavBytes = { 0x52, 0x49, 0x46, 0x46 };
    private static readonly byte[] Mp3Bytes = { 0xFF, 0xF3, 0xE4, 0xC4 };

    private sealed record SeededConv(Guid DeviceId, Guid ConvId, Guid UserMsgId, Guid AsstMsgId);

    /// <summary>
    /// Seed a device with one conversation containing a user WAV and
    /// an assistant MP3, both with their <c>AudioBlobPath</c>
    /// populated and matching entries in the in-memory blob store.
    /// </summary>
    private static SeededConv SeedDeviceWithAudioConversation(
        AppDbContext db, RecordingBlobStore blob,
        DateTime conversationAt, Guid? deviceId = null)
    {
        var dId = deviceId ?? Guid.NewGuid();
        var convId = Guid.NewGuid();
        var userMsgId = Guid.NewGuid();
        var asstMsgId = Guid.NewGuid();
        if (deviceId is null)
        {
            db.Set<Device>().Add(new Device
            {
                Id = dId,
                MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
                Name = "Test",
                ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            });
        }
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = dId,
            StartedAt = conversationAt
        });
        var userPath = $"{convId:N}/{userMsgId:N}.wav";
        var asstPath = $"{convId:N}/{asstMsgId:N}.mp3";
        db.Set<Message>().Add(new Message
        {
            Id = userMsgId, ConversationId = convId,
            Role = MessageRole.User, Content = "x",
            Timestamp = conversationAt.AddSeconds(1),
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = userPath
        });
        db.Set<Message>().Add(new Message
        {
            Id = asstMsgId, ConversationId = convId,
            Role = MessageRole.Assistant, Content = "y",
            Timestamp = conversationAt.AddSeconds(2),
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = asstPath
        });
        blob.Seed(userPath, WavBytes);
        blob.Seed(asstPath, Mp3Bytes);
        return new SeededConv(dId, convId, userMsgId, asstMsgId);
    }

    private static (int Attempted, int Deleted, int Failures) ReadAudio(AuditEvent ev)
    {
        Assert.NotNull(ev.Metadata);
        var meta = JsonNode.Parse(ev.Metadata!)!.AsObject();
        Assert.NotNull(meta["audio_conversations_attempted"]);
        Assert.NotNull(meta["audio_files_deleted"]);
        Assert.NotNull(meta["audio_delete_failures"]);
        return (
            (int?)meta["audio_conversations_attempted"] ?? -1,
            (int?)meta["audio_files_deleted"] ?? -1,
            (int?)meta["audio_delete_failures"] ?? -1);
    }

    // --- Conversation purge ----------------------------------------

    [Fact]
    public async Task PurgeExpired_DeletesAudio_AndAuditCarriesCounts()
    {
        await using var h = await CreateHarnessAsync();
        var blob = h.Blob!;
        // Two expired conversations + one fresh one, all on one device.
        var doomedA = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow.AddDays(-200));
        var doomedB = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow.AddDays(-200), deviceId: doomedA.DeviceId);
        var fresh = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow.AddHours(-1), deviceId: doomedA.DeviceId);
        await h.Db.SaveChangesAsync();
        Assert.Equal(6, blob.Files.Count);

        await h.Service.RunTickAsync(CancellationToken.None);

        // Both expired conversation directories cleaned; fresh one
        // preserved (still has its 2 blobs in the in-memory store).
        Assert.Equal(2, blob.Files.Count);
        Assert.Contains(blob.Files.Keys,
            k => k.StartsWith(fresh.ConvId.ToString("N") + "/"));
        Assert.DoesNotContain(blob.Files.Keys,
            k => k.StartsWith(doomedA.ConvId.ToString("N") + "/"));
        Assert.DoesNotContain(blob.Files.Keys,
            k => k.StartsWith(doomedB.ConvId.ToString("N") + "/"));
        // Two delete calls — one per expired conversation.
        Assert.Equal(2, blob.DeleteCalls.Count);

        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ConversationsPurgedByRetention);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(2, attempted);
        Assert.Equal(4, deleted);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task PurgeExpired_NoEligibleConversations_WritesNoAudioCounts()
    {
        // Pin: when the conversation pass finds nothing eligible, no
        // audit row is written at all (existing tick-with-no-deletions
        // contract). Audio cleanup never runs in that branch.
        await using var h = await CreateHarnessAsync();
        var blob = h.Blob!;
        var fresh = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow.AddHours(-1));
        await h.Db.SaveChangesAsync();
        Assert.Equal(2, blob.Files.Count);

        await h.Service.RunTickAsync(CancellationToken.None);

        // Audio survives.
        Assert.Equal(2, blob.Files.Count);
        Assert.Empty(blob.DeleteCalls);
        // No retention audit row (no deletions this tick).
        Assert.Empty(await h.Db.Set<AuditEvent>().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ConversationsPurgedByRetention)
            .ToListAsync());
    }

    [Fact]
    public async Task PurgeExpired_FailingBlobStore_DbCommitsAndAuditCountsFailures()
    {
        var failing = new FailingBlobStore();
        await using var h = await CreateHarnessAsync(blobStore: failing);
        // Seed against a separate recording instance just to put rows
        // in the DB; the actual cleanup will go through the failing
        // store and never see these blobs.
        var stagingBlob = new RecordingBlobStore();
        var doomed = SeedDeviceWithAudioConversation(h.Db, stagingBlob,
            conversationAt: DateTime.UtcNow.AddDays(-200));
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // Conversation row gone (DB commit happened post-projection).
        Assert.False(await h.Db.Set<Conversation>().AsNoTracking()
            .AnyAsync(c => c.Id == doomed.ConvId));
        // Failing store was called once.
        Assert.Equal(1, failing.Calls);
        // Audit metadata records the failure.
        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ConversationsPurgedByRetention);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(1, failures);
    }

    // --- Anonymize dormant parent ---------------------------------

    [Fact]
    public async Task AnonymizeDormantParent_OrphanDevice_DeletesAudio_AndAuditCounts()
    {
        // Force the dormancy thresholds so the seeded parent is
        // immediately eligible. Disable the conversations-purge pass
        // (huge MaxAgeDays equivalent isn't a config knob; instead
        // we use a future-conversation timestamp so nothing is
        // expired). Anonymize threshold is the controlling gate here.
        await using var h = await CreateHarnessAsync(
            dormancyParentWarnAfterDays: 30,
            dormancyParentAnonymizeAfterDays: 30);
        var blob = h.Blob!;

        var seeded = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow);
        var parentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("x"),
            RegisteredAt = DateTime.UtcNow.AddDays(-200),
            LastLoginAt = DateTime.UtcNow.AddDays(-120),
            // Warned long enough ago to clear the 7-day grace floor
            // and the AnonymizeAfterDays gate.
            DormancyWarnedAt = DateTime.UtcNow.AddDays(-60)
        });
        h.Db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = seeded.DeviceId,
            LinkedAt = DateTime.UtcNow.AddDays(-100)
        });
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // Device cascade fired → audio for the orphaned device's
        // conversation is gone.
        Assert.Empty(blob.Files);
        Assert.Single(blob.DeleteCalls);
        Assert.Equal(seeded.ConvId, blob.DeleteCalls[0]);

        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentDormancyAnonymized);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(2, deleted); // user WAV + assistant MP3
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task AnonymizeDormantParent_SharedDevice_PreservesAudio_AuditCountsZero()
    {
        await using var h = await CreateHarnessAsync(
            dormancyParentWarnAfterDays: 30,
            dormancyParentAnonymizeAfterDays: 30);
        var blob = h.Blob!;

        var seeded = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow);
        var dormantParentId = Guid.NewGuid();
        var activeParentId = Guid.NewGuid();
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = dormantParentId, Email = "dormant@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("x"),
            RegisteredAt = DateTime.UtcNow.AddDays(-200),
            LastLoginAt = DateTime.UtcNow.AddDays(-120),
            DormancyWarnedAt = DateTime.UtcNow.AddDays(-60)
        });
        h.Db.Set<Parent>().Add(new Parent
        {
            Id = activeParentId, Email = "active@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("y"),
            RegisteredAt = DateTime.UtcNow.AddDays(-30),
            LastLoginAt = DateTime.UtcNow.AddHours(-1)
        });
        h.Db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = dormantParentId, DeviceId = seeded.DeviceId,
            LinkedAt = DateTime.UtcNow.AddDays(-100)
        });
        h.Db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = activeParentId, DeviceId = seeded.DeviceId,
            LinkedAt = DateTime.UtcNow.AddDays(-30)
        });
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // Audio survives — device wasn't orphaned.
        Assert.Equal(2, blob.Files.Count);
        Assert.Empty(blob.DeleteCalls);
        // Anonymize audit row exists (parent was scrubbed) but its
        // audio counts are all zero on this branch.
        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentDormancyAnonymized);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(0, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(0, failures);
    }

    // --- Delete dormant device -----------------------------------

    [Fact]
    public async Task DeleteDormantDevice_DeletesAudioForAllConversations_AndAuditCounts()
    {
        // Configure both warn + delete for the device-level pass and
        // pre-stamp DormancyWarnedAt so the device is immediately
        // eligible for deletion.
        await using var h = await CreateHarnessAsync(
            dormancyDeviceWarnAfterDays: 90,
            dormancyDeviceDeleteAfterDays: 60);
        var blob = h.Blob!;

        // Device with two old conversations, both still inside the
        // 90-day retention window so the conversation pass doesn't
        // race ahead and delete them first. Use 30-days-ago which
        // leaves them in scope for the device pass but not the
        // conversation purge pass (90-day cutoff).
        var seedA = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow.AddDays(-30));
        var seedB = SeedDeviceWithAudioConversation(h.Db, blob,
            conversationAt: DateTime.UtcNow.AddDays(-30), deviceId: seedA.DeviceId);
        // Force the device into "dormant + warned long enough ago."
        var device = await h.Db.Set<Device>().FindAsync(seedA.DeviceId);
        device!.LastSeenAt = DateTime.UtcNow.AddDays(-200);
        device.DormancyWarnedAt = DateTime.UtcNow.AddDays(-90);
        await h.Db.SaveChangesAsync();
        Assert.Equal(4, blob.Files.Count);

        await h.Service.RunTickAsync(CancellationToken.None);

        // All four files cleaned.
        Assert.Empty(blob.Files);
        // Two delete calls — one per conversation.
        Assert.Equal(2, blob.DeleteCalls.Count);
        Assert.Contains(seedA.ConvId, blob.DeleteCalls);
        Assert.Contains(seedB.ConvId, blob.DeleteCalls);

        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.DeviceDormancyDeleted);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(2, attempted);
        Assert.Equal(4, deleted);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task DeleteDormantDevice_NoConversations_AuditReportsZeros()
    {
        await using var h = await CreateHarnessAsync(
            dormancyDeviceWarnAfterDays: 90,
            dormancyDeviceDeleteAfterDays: 60);
        var blob = h.Blob!;

        // Device with no conversations, dormant + warned.
        var deviceId = Guid.NewGuid();
        h.Db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
            Name = "Empty",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow.AddDays(-300),
            LastSeenAt = DateTime.UtcNow.AddDays(-200),
            DormancyWarnedAt = DateTime.UtcNow.AddDays(-90)
        });
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        Assert.Empty(blob.Files);
        Assert.Empty(blob.DeleteCalls);
        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.DeviceDormancyDeleted);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(0, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task DeleteDormantDevice_FailingBlobStore_DeviceStillRemoved_AuditCountsFailures()
    {
        var failing = new FailingBlobStore();
        await using var h = await CreateHarnessAsync(
            blobStore: failing,
            dormancyDeviceWarnAfterDays: 90,
            dormancyDeviceDeleteAfterDays: 60);

        var stagingBlob = new RecordingBlobStore();
        var seeded = SeedDeviceWithAudioConversation(h.Db, stagingBlob,
            conversationAt: DateTime.UtcNow.AddDays(-30));
        var device = await h.Db.Set<Device>().FindAsync(seeded.DeviceId);
        device!.LastSeenAt = DateTime.UtcNow.AddDays(-200);
        device.DormancyWarnedAt = DateTime.UtcNow.AddDays(-90);
        await h.Db.SaveChangesAsync();

        await h.Service.RunTickAsync(CancellationToken.None);

        // Device row gone (DB-first commit succeeded).
        Assert.False(await h.Db.Set<Device>().AsNoTracking()
            .AnyAsync(d => d.Id == seeded.DeviceId));
        // Failing store was called for the conversation id.
        Assert.Equal(1, failing.Calls);
        // Audit metadata reports the failure.
        var audit = (await h.Db.Set<AuditEvent>().AsNoTracking().ToListAsync())
            .Single(a => a.EventType == AuditEventType.DeviceDormancyDeleted);
        var (attempted, deleted, failures) = ReadAudio(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(1, failures);
    }
}

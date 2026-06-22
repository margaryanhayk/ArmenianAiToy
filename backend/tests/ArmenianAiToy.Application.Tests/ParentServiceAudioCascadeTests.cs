using System.Text.Json.Nodes;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Tests.Helpers;
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
/// C2.2a — audio-blob delete-cascade tests for the four parent-driven
/// destructive paths: <c>DeleteConversationAsync</c>,
/// <c>DeleteChildAsync</c>, <c>UnlinkDeviceAsync</c> (orphan branch),
/// <c>DeleteAccountAsync</c> (orphan-device loop).
///
/// Pinned invariants:
///  1. Audio cleanup runs AFTER the destructive DB SaveChanges, so a
///     blob-store IO failure cannot roll back a parent-initiated
///     delete. The audit row carries the post-cleanup counts.
///  2. <c>audio_conversations_attempted</c>, <c>audio_files_deleted</c>,
///     and <c>audio_delete_failures</c> appear in the audit metadata
///     for every parent-driven destructive event.
///  3. The audio cleanup respects conversation boundaries — sibling
///     conversations and other devices' audio are preserved.
///  4. A throwing blob store is non-fatal: the parent action still
///     returns success, and the failure is counted in audit metadata.
///  5. Both child/user WAV and assistant MP3 under the same
///     conversation directory are removed by a single call.
///
/// Real SQLite in-memory because the destructive paths rely on FK
/// cascade behavior that <c>UseInMemoryDatabase</c> does not honor.
/// </summary>
public class ParentServiceAudioCascadeTests
{
    // --- Helpers -----------------------------------------------------
    // RecordingBlobStore + FailingBlobStore live in
    // Helpers/RecordingBlobStore.cs; lifted there during the C2.2b
    // slice so RetentionPurgeServiceTests can register the same shape
    // on its harness's ServiceCollection without duplicating the type.

    private static async Task<(ParentService Service, AppDbContext Db, SqliteConnection Conn, RecordingBlobStore Blob)>
        CreateAsync()
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
        var blob = new RecordingBlobStore();
        var service = new ParentService(db, config, logger, blobStore: blob);
        return (service, db, conn, blob);
    }

    private static async Task<(ParentService Service, AppDbContext Db, SqliteConnection Conn, FailingBlobStore Blob)>
        CreateWithFailingStoreAsync()
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
        var blob = new FailingBlobStore();
        var service = new ParentService(db, config, logger, blobStore: blob);
        return (service, db, conn, blob);
    }

    private sealed record OwnedConversation(
        Guid ParentId, Guid DeviceId, Guid ChildId, Guid ConvId,
        Guid UserMsgId, Guid AsstMsgId);

    /// <summary>
    /// Seeds parent + linked device + child + conversation with one
    /// user WAV and one assistant MP3, both with their AudioBlobPath
    /// populated and matching files in the in-memory blob store.
    /// </summary>
    private static async Task<OwnedConversation> SeedAsync(
        AppDbContext db, RecordingBlobStore blob,
        string passwordPlain = "qa-secret-12345")
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var userMsgId = Guid.NewGuid();
        var asstMsgId = Guid.NewGuid();

        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = "p-" + Guid.NewGuid().ToString("N")[..8] + "@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordPlain),
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
        db.Set<Child>().Add(new Child
        {
            Id = childId,
            DeviceId = deviceId,
            Name = "Areg",
            BirthYear = 2020
        });
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = deviceId,
            ChildId = childId,
            StartedAt = DateTime.UtcNow
        });
        var userPath = $"{convId:N}/{userMsgId:N}.wav";
        var asstPath = $"{convId:N}/{asstMsgId:N}.mp3";
        db.Set<Message>().Add(new Message
        {
            Id = userMsgId,
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "Արի մի հեքիաթ պատմիր",
            Timestamp = DateTime.UtcNow.AddSeconds(-1),
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = userPath
        });
        db.Set<Message>().Add(new Message
        {
            Id = asstMsgId,
            ConversationId = convId,
            Role = MessageRole.Assistant,
            Content = "Մի փոքրիկ նապաստակ ապրում էր անտառում։",
            Timestamp = DateTime.UtcNow,
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = asstPath
        });
        await db.SaveChangesAsync();

        // Mirror the seeded paths into the in-memory blob store so the
        // delete cascade has something to remove.
        blob.Files[userPath] = new byte[] { 0x52, 0x49, 0x46, 0x46 };
        blob.Files[asstPath] = new byte[] { 0xFF, 0xF3, 0xE4, 0xC4 };

        return new OwnedConversation(parentId, deviceId, childId, convId, userMsgId, asstMsgId);
    }

    private static (int Attempted, int Deleted, int Failures) ReadAudioMetadata(AuditEvent ev)
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

    // --- DeleteConversationAsync ------------------------------------

    [Fact]
    public async Task DeleteConversation_DeletesAudioForThatConversation_AndAuditCarriesCounts()
    {
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, blob);
        Assert.Equal(2, blob.Files.Count); // sanity: WAV + MP3 seeded

        Assert.True(await service.DeleteConversationAsync(seed.ParentId, seed.ConvId));

        // Both blobs gone; the recording store saw exactly one delete
        // call for this conversation id.
        Assert.Empty(blob.Files);
        Assert.Single(blob.DeleteCalls);
        Assert.Equal(seed.ConvId, blob.DeleteCalls[0]);

        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentConversationDeleted);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(2, deleted);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task DeleteConversation_DoesNotTouchUnrelatedConversationAudio()
    {
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        var keep = await SeedAsync(db, blob);
        // A second conversation, owned by the same parent, must
        // survive when the first is deleted.
        var doomed = await SeedAsync(db, blob);
        Assert.Equal(4, blob.Files.Count);

        Assert.True(await service.DeleteConversationAsync(doomed.ParentId, doomed.ConvId));

        // Exactly the doomed conv's two files removed; the keeper's
        // two files survive.
        Assert.Equal(2, blob.Files.Count);
        Assert.Contains(blob.Files.Keys,
            k => k.StartsWith(keep.ConvId.ToString("N") + "/"));
        Assert.DoesNotContain(blob.Files.Keys,
            k => k.StartsWith(doomed.ConvId.ToString("N") + "/"));
    }

    [Fact]
    public async Task DeleteConversation_BlobStoreFails_DbDeleteStillCommits_AndAuditCountsFailure()
    {
        // Pinning the load-bearing C2.2a contract: a failing blob
        // store must NOT roll back the destructive DB action. The
        // parent's request still returns true; the audit row records
        // the failure count for operator visibility.
        var (service, db, conn, blob) = await CreateWithFailingStoreAsync();
        await using var _ = conn;

        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "p@example.com", PasswordHash = "h",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId, MacAddress = "mac",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            Name = "T", RegisteredAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId, DeviceId = deviceId, StartedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.DeleteConversationAsync(parentId, convId));

        // Conversation row gone (DB-first commit happened).
        Assert.Null(await db.Set<Conversation>().FindAsync(convId));
        // Failing store was called exactly once.
        Assert.Equal(1, blob.Calls);
        // Audit metadata carries the failure count.
        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentConversationDeleted);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(1, failures);
    }

    // --- DeleteChildAsync -------------------------------------------

    [Fact]
    public async Task DeleteChild_DeletesAudioForAllChildConversations_AndAuditCarriesCounts()
    {
        // Seed two conversations under the same child so the cascade
        // has a non-trivial fan-out.
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, blob);
        // Add a second conversation under the same child.
        var conv2Id = Guid.NewGuid();
        var msg2Id = Guid.NewGuid();
        db.Set<Conversation>().Add(new Conversation
        {
            Id = conv2Id, DeviceId = seed.DeviceId, ChildId = seed.ChildId,
            StartedAt = DateTime.UtcNow
        });
        var conv2Path = $"{conv2Id:N}/{msg2Id:N}.mp3";
        db.Set<Message>().Add(new Message
        {
            Id = msg2Id, ConversationId = conv2Id,
            Role = MessageRole.Assistant, Content = "more story",
            Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = conv2Path
        });
        await db.SaveChangesAsync();
        blob.Files[conv2Path] = Mp3();
        Assert.Equal(3, blob.Files.Count);

        Assert.True(await service.DeleteChildAsync(seed.ParentId, seed.ChildId));

        // Both conversations under the child had their audio removed.
        Assert.Empty(blob.Files);
        // Two delete calls — one per conversation.
        Assert.Equal(2, blob.DeleteCalls.Count);
        Assert.Contains(seed.ConvId, blob.DeleteCalls);
        Assert.Contains(conv2Id, blob.DeleteCalls);

        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentChildDeleted);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(2, attempted);
        Assert.Equal(3, deleted);
        Assert.Equal(0, failures);
    }

    // --- UnlinkDeviceAsync (orphan branch) --------------------------

    [Fact]
    public async Task UnlinkDevice_OrphanCascade_DeletesAudioForAllDeviceConversations()
    {
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, blob);
        Assert.Equal(2, blob.Files.Count);

        Assert.True(await service.UnlinkDeviceAsync(seed.ParentId, seed.DeviceId));

        Assert.Empty(blob.Files);
        Assert.Single(blob.DeleteCalls);
        Assert.Equal(seed.ConvId, blob.DeleteCalls[0]);

        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentDeviceUnlinked);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(2, deleted);
        Assert.Equal(0, failures);
        // orphan_cascaded should be true on this branch.
        var meta = JsonNode.Parse(audit.Metadata!)!.AsObject();
        Assert.True((bool?)meta["orphan_cascaded"]);
    }

    [Fact]
    public async Task UnlinkDevice_StillLinkedToOtherParent_DoesNotDeleteAudio()
    {
        // Multi-parent device: only the unlinking parent's link is
        // removed; the device + subtree + audio stays for the other
        // parent. No blob delete should fire on this branch.
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, blob);
        // Add a second parent linked to the same device.
        var otherParentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = otherParentId, Email = "other@example.com", PasswordHash = "h",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = otherParentId, DeviceId = seed.DeviceId,
            LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.UnlinkDeviceAsync(seed.ParentId, seed.DeviceId));

        // Audio survives — no delete call fired.
        Assert.Equal(2, blob.Files.Count);
        Assert.Empty(blob.DeleteCalls);
        // Audit row is still written, with zero audio counts.
        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentDeviceUnlinked);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(0, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(0, failures);
    }

    // --- DeleteAccountAsync (orphan-device loop) --------------------

    [Fact]
    public async Task DeleteAccount_DeletesAudioForOrphanedDeviceConversations()
    {
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        // Seed the password-known parent ourselves so we can supply
        // currentPassword to DeleteAccountAsync.
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        const string pwd = "qa-pwd-12345";
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId, Email = "owner@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(pwd),
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId, MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test", ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = DateTime.UtcNow
        });
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId, DeviceId = deviceId, StartedAt = DateTime.UtcNow
        });
        var path = $"{convId:N}/{msgId:N}.mp3";
        db.Set<Message>().Add(new Message
        {
            Id = msgId, ConversationId = convId,
            Role = MessageRole.Assistant, Content = "x",
            Timestamp = DateTime.UtcNow, SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = path
        });
        await db.SaveChangesAsync();
        blob.Files[path] = Mp3();

        Assert.True(await service.DeleteAccountAsync(parentId, pwd));

        Assert.Empty(blob.Files);
        Assert.Single(blob.DeleteCalls);
        Assert.Equal(convId, blob.DeleteCalls[0]);

        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentAccountDeleted);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(1, attempted);
        Assert.Equal(1, deleted);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task DeleteAccount_SharedDevicePreserved_DoesNotDeleteAudio()
    {
        // Account delete with a device that is still linked to ANOTHER
        // parent: that device is preserved, and its audio must NOT be
        // touched even though the deleting parent owned a link.
        var (service, db, conn, blob) = await CreateAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, blob);
        // Add a second parent linked to the same device, and reset
        // the deleting parent's password so we can pass it as input.
        const string pwd = "qa-pwd-12345";
        var deletingParent = await db.Set<Parent>().FindAsync(seed.ParentId);
        deletingParent!.PasswordHash = BCrypt.Net.BCrypt.HashPassword(pwd);
        var otherParentId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = otherParentId, Email = "other@example.com", PasswordHash = "h",
            RegisteredAt = DateTime.UtcNow
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = otherParentId, DeviceId = seed.DeviceId,
            LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await service.DeleteAccountAsync(seed.ParentId, pwd));

        // Audio survives — the device wasn't orphaned.
        Assert.Equal(2, blob.Files.Count);
        Assert.Empty(blob.DeleteCalls);

        var audit = (await db.Set<AuditEvent>().ToListAsync())
            .Single(a => a.EventType == AuditEventType.ParentAccountDeleted);
        var (attempted, deleted, failures) = ReadAudioMetadata(audit);
        Assert.Equal(0, attempted);
        Assert.Equal(0, deleted);
        Assert.Equal(0, failures);
    }

    private static byte[] Mp3() => new byte[] { 0xFF, 0xF3, 0xE4, 0xC4 };
}

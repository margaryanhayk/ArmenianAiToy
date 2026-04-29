using System.Reflection;
using System.Security.Claims;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// C2.1 — parent-safe assistant MP3 replay tests for
/// <c>GET /api/parents/messages/{messageId}/audio</c>.
///
/// Pins the contract documented in CLAUDE.md § Voice chat (C2.1):
///
///  1. <b>Assistant-only.</b> A user/child message with a populated
///     <c>AudioBlobPath</c> must NOT be replayable through this
///     endpoint, even if the parent owns the device. The role gate
///     lives in <see cref="ParentService.GetAssistantAudioMessageAsync"/>.
///  2. <b>Uniform 404.</b> Every miss reason — unknown id, message
///     owned by another family, user role, null
///     <c>AudioBlobPath</c>, blob file missing on disk — collapses to
///     the same response body so a parent cannot probe message
///     existence, ownership, or attachment state across families.
///  3. <b>Authentication required.</b> The action carries
///     <see cref="AuthorizeAttribute"/>.
///
/// Service-level tests use real SQLite in-memory so the join through
/// <c>ParentDevice</c> exercises the relational provider. Controller-
/// level tests substitute the service to assert the uniform-404 wire
/// shape without re-running the join.
/// </summary>
public class ParentMessageAudioTests
{
    private const string UniformNotFoundError = "Audio not available.";
    private const string AssistantContent = "Մի փոքրիկ նապաստակ ապրում էր անտառում։";

    // ─────────────────────────────────────────────────────────────────────
    // Service-level: ParentService.GetAssistantAudioMessageAsync
    // ─────────────────────────────────────────────────────────────────────

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

    private sealed record SeedResult(
        Guid ParentId, Guid DeviceId, Guid ConversationId,
        Guid UserMessageId, Guid AssistantMessageId);

    /// <summary>
    /// Seeds a parent + linked device + conversation with one user
    /// message (WAV blob path populated) and one assistant message
    /// (MP3 blob path populated). The user-WAV path is the test
    /// fixture for the role gate — a child's audio attachment in the
    /// DB must not become replayable.
    /// </summary>
    private static async Task<SeedResult> SeedAsync(
        AppDbContext db,
        bool linkParent = true,
        string? assistantBlobPath = "<conv>/<asst>.mp3",
        bool seedUserWav = true)
    {
        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var userMsgId = Guid.NewGuid();
        var asstMsgId = Guid.NewGuid();

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
        if (linkParent)
        {
            db.Set<ParentDevice>().Add(new ParentDevice
            {
                ParentId = parentId,
                DeviceId = deviceId,
                LinkedAt = DateTime.UtcNow
            });
        }
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId,
            DeviceId = deviceId,
            StartedAt = DateTime.UtcNow
        });
        if (seedUserWav)
        {
            db.Set<Message>().Add(new Message
            {
                Id = userMsgId,
                ConversationId = convId,
                Role = MessageRole.User,
                Content = "Արի մի հեքիաթ պատմիր",
                Timestamp = DateTime.UtcNow.AddSeconds(-1),
                SafetyFlag = SafetyFlag.Clean,
                AudioBlobPath = convId.ToString("N") + "/" + userMsgId.ToString("N") + ".wav"
            });
        }
        db.Set<Message>().Add(new Message
        {
            Id = asstMsgId,
            ConversationId = convId,
            Role = MessageRole.Assistant,
            Content = AssistantContent,
            Timestamp = DateTime.UtcNow,
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = assistantBlobPath
        });
        await db.SaveChangesAsync();
        return new SeedResult(parentId, deviceId, convId, userMsgId, asstMsgId);
    }

    [Fact]
    public async Task Service_OwnedAssistantMessageWithBlobPath_ReturnsConversationAndMessageId()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var hit = await service.GetAssistantAudioMessageAsync(seed.ParentId, seed.AssistantMessageId);

        Assert.NotNull(hit);
        Assert.Equal(seed.ConversationId, hit!.Value.ConversationId);
        Assert.Equal(seed.AssistantMessageId, hit.Value.MessageId);
    }

    [Fact]
    public async Task Service_UnknownMessageId_ReturnsNull()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var hit = await service.GetAssistantAudioMessageAsync(seed.ParentId, Guid.NewGuid());

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_MessageOwnedByDifferentParent_ReturnsNull()
    {
        // Conversation belongs to a device that the *seeded* parent owns,
        // but a *different* (stranger) parent calls the service.
        // No-existence-leak invariant: stranger gets null, identical
        // shape to the unknown-id branch.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);
        var strangerParentId = Guid.NewGuid();

        var hit = await service.GetAssistantAudioMessageAsync(strangerParentId, seed.AssistantMessageId);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_UserMessageWithBlobPath_ReturnsNull()
    {
        // KEYSTONE: the C2.1 role gate. The user/child WAV has a
        // populated AudioBlobPath (audio chat persists both blobs).
        // The endpoint must NOT expose it for replay even when the
        // parent owns the conversation. A regression here would let
        // a parent listen to the child's voice — out of scope for
        // C2.1 by product decision.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var hit = await service.GetAssistantAudioMessageAsync(seed.ParentId, seed.UserMessageId);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_AssistantMessageWithNullBlobPath_ReturnsNull()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, assistantBlobPath: null);

        var hit = await service.GetAssistantAudioMessageAsync(seed.ParentId, seed.AssistantMessageId);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_MissReasons_AllReturnIdenticalNullShape()
    {
        // Anti-leak pin: every miss path returns the exact same
        // null-valued nullable. A future change that adds a
        // distinguishing return (a tag, a reason enum) for any of
        // these paths breaks this test loudly.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var unknownId = await service.GetAssistantAudioMessageAsync(seed.ParentId, Guid.NewGuid());
        var notOwned = await service.GetAssistantAudioMessageAsync(Guid.NewGuid(), seed.AssistantMessageId);
        var userRole = await service.GetAssistantAudioMessageAsync(seed.ParentId, seed.UserMessageId);

        Assert.Null(unknownId);
        Assert.Null(notOwned);
        Assert.Null(userRole);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Controller-level: ParentController.GetMessageAudio
    // ─────────────────────────────────────────────────────────────────────

    private sealed class StubBlobStore : IAudioBlobStore
    {
        public byte[]? Bytes { get; set; }
        public string Mime { get; set; } = "audio/mpeg";
        public bool ReadCalled { get; private set; }
        public Guid? ReceivedConversationId { get; private set; }
        public Guid? ReceivedMessageId { get; private set; }

        public Task<string> WriteAsync(Guid conversationId, Guid messageId,
            byte[] content, string mimeType, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Read-side test seam only.");

        public Task<(Stream Content, string MimeType)?> ReadAsync(
            Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
        {
            ReadCalled = true;
            ReceivedConversationId = conversationId;
            ReceivedMessageId = messageId;
            if (Bytes is null)
                return Task.FromResult<(Stream, string)?>(null);
            return Task.FromResult<(Stream, string)?>(
                (new MemoryStream(Bytes), Mime));
        }

        // C2.2a — interface compatibility shim. The C2.1 read-side
        // tests in this file exercise GetMessageAudio only; they
        // never invoke conversation-level audio cleanup. Returning
        // the missing-directory idempotent-success shape keeps the
        // stub honest if it ever does get called.
        public Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(
            Guid conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AudioBlobDeleteResult(
                FilesDeleted: 0, DirectoryMissing: true, Failed: false, ErrorMessage: null));
    }

    private static (ParentController Controller, IParentService Service, Guid ParentId)
        CreateController()
    {
        var service = Substitute.For<IParentService>();
        var controller = new ParentController(service, new ExportCooldown());
        var parentId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, service, parentId);
    }

    [Fact]
    public async Task Controller_HappyPath_ReturnsFileWithMimeFromBlobStore()
    {
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetAssistantAudioMessageAsync(parentId, msgId)
            .Returns((convId, msgId));
        var bytes = new byte[] { 0x49, 0x44, 0x33, 0x04 }; // ID3 marker prefix
        var blob = new StubBlobStore { Bytes = bytes, Mime = "audio/mpeg" };

        var result = await controller.GetMessageAudio(msgId, blob, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);
        // The store was probed with the resolved (conversationId, messageId).
        Assert.True(blob.ReadCalled);
        Assert.Equal(convId, blob.ReceivedConversationId);
        Assert.Equal(msgId, blob.ReceivedMessageId);
        // Bytes streamed verbatim.
        using var ms = new MemoryStream();
        await file.FileStream.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task Controller_ServiceReturnsNull_ReturnsUniform404_AndDoesNotProbeBlobStore()
    {
        // Combined assertion for: unknown id / not owned / user role /
        // null AudioBlobPath. The service collapses all of those into
        // a single null return, and the controller maps null to the
        // uniform 404 body without ever touching the blob store
        // (defense-in-depth against a regression that would probe the
        // disk for an unauthorized message).
        var (controller, service, parentId) = CreateController();
        var msgId = Guid.NewGuid();
        service.GetAssistantAudioMessageAsync(parentId, msgId)
            .Returns(((Guid, Guid)?)null);
        var blob = new StubBlobStore();

        var result = await controller.GetMessageAudio(msgId, blob, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        AssertUniformNotFoundBody(notFound.Value);
        Assert.False(blob.ReadCalled);
    }

    [Fact]
    public async Task Controller_BlobMissingFromStore_ReturnsUniform404()
    {
        // The ownership/role/path checks all passed but the file is
        // gone (operator deleted the audio-blobs root, retention
        // cascade in a later slice swept it, etc.). Same uniform 404
        // body as the not-yours / unknown-id branches — a parent
        // cannot tell a missing blob apart from a missing message.
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetAssistantAudioMessageAsync(parentId, msgId)
            .Returns((convId, msgId));
        var blob = new StubBlobStore { Bytes = null };

        var result = await controller.GetMessageAudio(msgId, blob, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        AssertUniformNotFoundBody(notFound.Value);
        Assert.True(blob.ReadCalled);
    }

    [Fact]
    public async Task Controller_BlobMimeIsNotMp3_ReturnsUniform404()
    {
        // Defense-in-depth pin for the "assistant MP3 replay only"
        // contract. The service-side role gate already keeps user
        // WAVs out of this endpoint, but a future change to the blob
        // store (or a manual file placement at the assistant message
        // id's path) could otherwise hand the controller a non-MP3
        // stream. The endpoint must enforce its MIME contract at the
        // HTTP boundary rather than trusting the store's reported
        // MIME — and an unexpected MIME must collapse to the SAME
        // uniform 404 body so the wire shape gives nothing away.
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetAssistantAudioMessageAsync(parentId, msgId)
            .Returns((convId, msgId));
        var blob = new StubBlobStore
        {
            Bytes = new byte[] { 0x52, 0x49, 0x46, 0x46 }, // RIFF marker
            Mime = "audio/wav"
        };

        var result = await controller.GetMessageAudio(msgId, blob, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        AssertUniformNotFoundBody(notFound.Value);
    }

    [Fact]
    public void Controller_ActionRequiresAuthorization()
    {
        // "No JWT" rejection is enforced by the auth pipeline — this
        // pins that the [Authorize] attribute is actually applied.
        // A regression that drops it would silently expose the
        // endpoint to anonymous callers; this test catches that.
        var method = typeof(ParentController).GetMethod(
            nameof(ParentController.GetMessageAudio),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
    }

    private static void AssertUniformNotFoundBody(object? body)
    {
        // The controller surfaces the same anonymous { error = "..." }
        // shape every other parent NotFound endpoint uses. Pin both
        // the field name and the value so a future copy-paste error
        // doesn't drift it (e.g. into "Audio missing." or similar).
        Assert.NotNull(body);
        var errorProp = body!.GetType().GetProperty("error");
        Assert.NotNull(errorProp);
        Assert.Equal(UniformNotFoundError, errorProp!.GetValue(body) as string);
    }
}

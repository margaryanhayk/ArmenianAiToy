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
/// 2026-09-11 — parent-safe CHILD audio download tests for
/// <c>GET /api/parents/messages/{messageId}/child-audio</c>. Mirror image
/// of <see cref="ParentMessageAudioTests"/> — same fixtures, same
/// ownership chain, opposite role gate.
///
/// Pins:
///  1. <b>User-only.</b> An assistant message with a populated
///     <c>AudioBlobPath</c> must NOT be downloadable through this
///     endpoint, even if the parent owns the device. The role gate lives
///     in <see cref="ParentService.GetChildAudioMessageAsync"/>. This is
///     the keystone in both directions: an assistant message never
///     serves through the child endpoint, and (per
///     <see cref="ParentMessageAudioTests"/>) a child message never
///     serves through the assistant endpoint.
///  2. <b>Uniform 404.</b> Every miss reason collapses to the same body.
///  3. <b>Authentication required.</b> <see cref="AuthorizeAttribute"/> is applied.
///  4. <b>Download, not inline.</b> The response carries
///     <c>Content-Disposition: attachment</c> with a messageId-based,
///     PII-free filename.
/// </summary>
public class ParentChildAudioTests
{
    private const string UniformNotFoundError = "Audio not available.";
    private const string AssistantContent = "Մի փոքրիկ նապաստակ ապրում էր անտառում։";

    // ─────────────────────────────────────────────────────────────────────
    // Service-level: ParentService.GetChildAudioMessageAsync
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

    private static async Task<SeedResult> SeedAsync(
        AppDbContext db,
        bool linkParent = true,
        string? userBlobPath = "<conv>/<user>.wav",
        bool seedAssistantMp3 = true)
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
        db.Set<Message>().Add(new Message
        {
            Id = userMsgId,
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "Արի մի հեքիաթ պատմիր",
            Timestamp = DateTime.UtcNow.AddSeconds(-1),
            SafetyFlag = SafetyFlag.Clean,
            AudioBlobPath = userBlobPath
        });
        if (seedAssistantMp3)
        {
            db.Set<Message>().Add(new Message
            {
                Id = asstMsgId,
                ConversationId = convId,
                Role = MessageRole.Assistant,
                Content = AssistantContent,
                Timestamp = DateTime.UtcNow,
                SafetyFlag = SafetyFlag.Clean,
                AudioBlobPath = convId.ToString("N") + "/" + asstMsgId.ToString("N") + ".mp3"
            });
        }
        await db.SaveChangesAsync();
        return new SeedResult(parentId, deviceId, convId, userMsgId, asstMsgId);
    }

    [Fact]
    public async Task Service_OwnedUserMessageWithBlobPath_ReturnsConversationAndMessageId()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var hit = await service.GetChildAudioMessageAsync(seed.ParentId, seed.UserMessageId);

        Assert.NotNull(hit);
        Assert.Equal(seed.ConversationId, hit!.Value.ConversationId);
        Assert.Equal(seed.UserMessageId, hit.Value.MessageId);
    }

    [Fact]
    public async Task Service_UnknownMessageId_ReturnsNull()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var hit = await service.GetChildAudioMessageAsync(seed.ParentId, Guid.NewGuid());

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_MessageOwnedByDifferentParent_ReturnsNull()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);
        var strangerParentId = Guid.NewGuid();

        var hit = await service.GetChildAudioMessageAsync(strangerParentId, seed.UserMessageId);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_AssistantMessageWithBlobPath_ReturnsNull()
    {
        // KEYSTONE: the mirror-image role gate. An assistant MP3 has a
        // populated AudioBlobPath. This endpoint must NOT expose it even
        // when the parent owns the conversation — assistant audio replay
        // is GetAssistantAudioMessageAsync's job, not this one's.
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var hit = await service.GetChildAudioMessageAsync(seed.ParentId, seed.AssistantMessageId);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_UserMessageWithNullBlobPath_ReturnsNull()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db, userBlobPath: null);

        var hit = await service.GetChildAudioMessageAsync(seed.ParentId, seed.UserMessageId);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Service_MissReasons_AllReturnIdenticalNullShape()
    {
        var (service, db, conn) = await CreateServiceAsync();
        await using var _ = conn;
        var seed = await SeedAsync(db);

        var unknownId = await service.GetChildAudioMessageAsync(seed.ParentId, Guid.NewGuid());
        var notOwned = await service.GetChildAudioMessageAsync(Guid.NewGuid(), seed.UserMessageId);
        var assistantRole = await service.GetChildAudioMessageAsync(seed.ParentId, seed.AssistantMessageId);

        Assert.Null(unknownId);
        Assert.Null(notOwned);
        Assert.Null(assistantRole);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Controller-level: ParentController.GetChildAudio
    // ─────────────────────────────────────────────────────────────────────

    private sealed class StubBlobStore : IAudioBlobStore
    {
        public byte[]? Bytes { get; set; }
        public string Mime { get; set; } = "audio/wav";
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
    public async Task Controller_HappyPath_ReturnsFileAsAttachment_WithMessageIdFilename()
    {
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetChildAudioMessageAsync(parentId, msgId)
            .Returns((convId, msgId));
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // RIFF marker prefix
        var blob = new StubBlobStore { Bytes = bytes, Mime = "audio/wav" };

        var result = await controller.GetChildAudio(msgId, blob, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/wav", file.ContentType);
        Assert.Equal($"areg-recording-{msgId:N}.wav", file.FileDownloadName);
        Assert.True(blob.ReadCalled);
        Assert.Equal(convId, blob.ReceivedConversationId);
        Assert.Equal(msgId, blob.ReceivedMessageId);
        using var ms = new MemoryStream();
        await file.FileStream.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task Controller_FilenameCarriesNoPii_OnlyMessageIdAndExtension()
    {
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetChildAudioMessageAsync(parentId, msgId).Returns((convId, msgId));
        var blob = new StubBlobStore { Bytes = new byte[] { 1, 2, 3 }, Mime = "audio/mpeg" };

        var result = await controller.GetChildAudio(msgId, blob, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal($"areg-recording-{msgId:N}.mp3", file.FileDownloadName);
    }

    [Fact]
    public async Task Controller_ServiceReturnsNull_ReturnsUniform404_AndDoesNotProbeBlobStore()
    {
        var (controller, service, parentId) = CreateController();
        var msgId = Guid.NewGuid();
        service.GetChildAudioMessageAsync(parentId, msgId)
            .Returns(((Guid, Guid)?)null);
        var blob = new StubBlobStore();

        var result = await controller.GetChildAudio(msgId, blob, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        AssertUniformNotFoundBody(notFound.Value);
        Assert.False(blob.ReadCalled);
    }

    [Fact]
    public async Task Controller_BlobMissingFromStore_ReturnsUniform404()
    {
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetChildAudioMessageAsync(parentId, msgId)
            .Returns((convId, msgId));
        var blob = new StubBlobStore { Bytes = null };

        var result = await controller.GetChildAudio(msgId, blob, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        AssertUniformNotFoundBody(notFound.Value);
        Assert.True(blob.ReadCalled);
    }

    [Fact]
    public async Task Controller_BlobMimeNotWhitelisted_ReturnsUniform404()
    {
        // Defense-in-depth: only audio/wav and audio/mpeg are the formats
        // LocalDiskAudioBlobStore.ReadAsync can ever report. Anything else
        // (a future store change, a manual file placement) collapses to
        // the same uniform 404 rather than being served.
        var (controller, service, parentId) = CreateController();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        service.GetChildAudioMessageAsync(parentId, msgId)
            .Returns((convId, msgId));
        var blob = new StubBlobStore
        {
            Bytes = new byte[] { 0x00, 0x01 },
            Mime = "application/octet-stream"
        };

        var result = await controller.GetChildAudio(msgId, blob, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        AssertUniformNotFoundBody(notFound.Value);
    }

    [Fact]
    public void Controller_ActionRequiresAuthorization()
    {
        var method = typeof(ParentController).GetMethod(
            nameof(ParentController.GetChildAudio),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
    }

    private static void AssertUniformNotFoundBody(object? body)
    {
        Assert.NotNull(body);
        var errorProp = body!.GetType().GetProperty("error");
        Assert.NotNull(errorProp);
        Assert.Equal(UniformNotFoundError, errorProp!.GetValue(body) as string);
    }
}

using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the C1 voice chat endpoint (<c>POST /api/chat/audio</c>).
/// Wires a real <see cref="AppDbContext"/> over SQLite in-memory so
/// the <c>AudioBlobPath</c> update path is exercised end-to-end;
/// everything else (STT, TTS, blob store, ChatService, DeviceService)
/// is substituted.
/// <para>
/// No test makes a real network call — transcription and synthesis
/// return canned bytes / canned strings. The blob store is an
/// in-memory fake so we can assert deterministic paths without
/// touching disk.
/// </para>
/// </summary>
public class AudioChatControllerTests
{
    private const string AssistantText = "Կար մի կատու։";
    private const string MimeMp3 = "audio/mpeg";
    private static readonly byte[] InboundPcm = Encoding.UTF8.GetBytes("RIFF child audio bytes marker");
    private static readonly byte[] TtsMp3 = Encoding.UTF8.GetBytes("ID3 synthesized assistant audio marker");

    private sealed class InMemoryBlobStore : IAudioBlobStore
    {
        public readonly Dictionary<string, (byte[] Bytes, string Mime)> Written = new();

        public Task<string> WriteAsync(Guid conversationId, Guid messageId,
            byte[] content, string mimeType, CancellationToken cancellationToken = default)
        {
            var ext = mimeType.StartsWith("audio/wav") ? ".wav"
                     : mimeType.StartsWith("audio/mpeg") ? ".mp3"
                     : ".bin";
            var path = $"{conversationId:N}/{messageId:N}{ext}";
            Written[path] = (content, mimeType);
            return Task.FromResult(path);
        }

        public Task<(Stream Content, string MimeType)?> ReadAsync(
            Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
            => Task.FromResult<(Stream, string)?>(null);

        // C2.2a — conversation-level cleanup. Removes every dictionary
        // entry whose path begins with the {conversationId:N}/ prefix,
        // mirroring the on-disk LocalDiskAudioBlobStore semantic. The
        // C1 audio chat tests don't exercise delete, but the C2.2a
        // ParentService delete tests construct ParentService over this
        // double via a separate test seam; keeping the implementation
        // honest here avoids surprises if anything ever does call it.
        public Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(
            Guid conversationId, CancellationToken cancellationToken = default)
        {
            var prefix = conversationId.ToString("N") + "/";
            var matched = Written.Keys.Where(k => k.StartsWith(prefix)).ToList();
            if (matched.Count == 0)
            {
                return Task.FromResult(new AudioBlobDeleteResult(
                    FilesDeleted: 0, DirectoryMissing: true, Failed: false, ErrorMessage: null));
            }
            foreach (var k in matched) Written.Remove(k);
            return Task.FromResult(new AudioBlobDeleteResult(
                FilesDeleted: matched.Count, DirectoryMissing: false, Failed: false, ErrorMessage: null));
        }
    }

    private sealed record Harness(
        AudioChatController Controller,
        AppDbContext Db,
        SqliteConnection Conn,
        InMemoryBlobStore BlobStore,
        IChatService ChatService,
        IDeviceService DeviceService,
        IAudioTranscriptionService Transcription,
        IAudioSynthesisService Synthesis,
        CannedVoiceClips Canned,
        Guid DeviceId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Conn.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateAsync(
        Action<Harness>? configure = null,
        byte[]? inboundBody = null,
        string inboundContentType = "audio/wav")
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var deviceService = Substitute.For<IDeviceService>();
        var chatService = Substitute.For<IChatService>();
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var blobStore = new InMemoryBlobStore();
        var canned = new CannedVoiceClips(synthesis);
        var logger = Substitute.For<ILogger<AudioChatController>>();

        // Cost-cap gate added in feature/openai-daily-cost-cap. Disabled
        // here so the AudioChat happy-path tests exercise the same code
        // path as before. Dedicated cost-cap audio tests live in
        // AudioChatControllerCostCapTests.cs.
        var costMeter = new OpenAICostMeter();
        var costCapOptions = Options.Create(
            new OpenAIDailyCostCapOptions { Enabled = false });

        var controller = new AudioChatController(
            chatService, deviceService, transcription, synthesis,
            blobStore, canned, db, costMeter, costCapOptions, logger);

        var httpContext = new DefaultHttpContext();
        var deviceId = Guid.NewGuid();
        httpContext.Items["DeviceId"] = deviceId;
        var body = inboundBody ?? InboundPcm;
        httpContext.Request.Body = new MemoryStream(body);
        httpContext.Request.ContentType = inboundContentType;
        httpContext.Request.ContentLength = body.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var h = new Harness(
            controller, db, conn, blobStore,
            chatService, deviceService, transcription, synthesis, canned, deviceId);
        configure?.Invoke(h);
        return h;
    }

    private static void WireHappyPath(Harness h,
        string transcript = "պատմիր հեքիաթ",
        string assistantText = AssistantText)
    {
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(true);

        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(transcript);

        h.Synthesis.SynthesizeArmenianAsync(
                assistantText, Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));

        // Wire ChatService to seed the conversation + user message
        // the way the real ChatService would, then return the assistant
        // message id. Matches the "user row is inserted synchronously
        // before the LLM call" invariant the controller relies on.
        h.ChatService.GetResponseAsync(
                h.DeviceId, transcript,
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(ci =>
            {
                var convId = Guid.NewGuid();
                var userMsgId = Guid.NewGuid();
                var assistantMsgId = Guid.NewGuid();
                h.Db.Set<Device>().Add(new Device
                {
                    Id = h.DeviceId,
                    MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
                    Name = "Test",
                    ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
                    RegisteredAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
                h.Db.Set<Conversation>().Add(new Conversation
                {
                    Id = convId, DeviceId = h.DeviceId,
                    StartedAt = DateTime.UtcNow
                });
                h.Db.Set<Message>().Add(new Message
                {
                    Id = userMsgId, ConversationId = convId,
                    Role = MessageRole.User, Content = transcript,
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-50),
                    SafetyFlag = SafetyFlag.Clean
                });
                h.Db.Set<Message>().Add(new Message
                {
                    Id = assistantMsgId, ConversationId = convId,
                    Role = MessageRole.Assistant, Content = assistantText,
                    Timestamp = DateTime.UtcNow,
                    SafetyFlag = SafetyFlag.Clean
                });
                h.Db.SaveChanges();
                return new ChatResponse(
                    assistantText, convId, assistantMsgId, SafetyFlag.Clean);
            });
    }

    // --- Happy path -------------------------------------------------

    [Fact]
    public async Task AudioChat_HappyPath_ReturnsAssistantAudioAndPersistsBothBlobPaths()
    {
        await using var h = await CreateAsync();
        WireHappyPath(h);

        var result = await h.Controller.Chat(CancellationToken.None);

        // HTTP response: MP3 bytes from the TTS seam.
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(MimeMp3, file.ContentType);
        Assert.Equal(TtsMp3, file.FileContents);

        // Both messages have AudioBlobPath populated.
        var messages = await h.Db.Set<Message>().AsNoTracking()
            .OrderBy(m => m.Timestamp).ToListAsync();
        Assert.Equal(2, messages.Count);
        var userMsg = messages.Single(m => m.Role == MessageRole.User);
        var assistantMsg = messages.Single(m => m.Role == MessageRole.Assistant);
        Assert.False(string.IsNullOrEmpty(userMsg.AudioBlobPath));
        Assert.False(string.IsNullOrEmpty(assistantMsg.AudioBlobPath));

        // Blobs written to the store for both messages.
        Assert.Equal(2, h.BlobStore.Written.Count);
        Assert.True(h.BlobStore.Written.ContainsKey(userMsg.AudioBlobPath!));
        Assert.True(h.BlobStore.Written.ContainsKey(assistantMsg.AudioBlobPath!));

        // Child blob carries inbound content + MIME verbatim.
        var childEntry = h.BlobStore.Written[userMsg.AudioBlobPath!];
        Assert.Equal(InboundPcm, childEntry.Bytes);
        Assert.Equal("audio/wav", childEntry.Mime);

        // Assistant blob carries TTS bytes + MIME verbatim.
        var asstEntry = h.BlobStore.Written[assistantMsg.AudioBlobPath!];
        Assert.Equal(TtsMp3, asstEntry.Bytes);
        Assert.Equal(MimeMp3, asstEntry.Mime);
    }

    [Fact]
    public async Task AudioChat_DeterministicBlobPaths_MatchConversationAndMessageIds()
    {
        await using var h = await CreateAsync();
        WireHappyPath(h);

        await h.Controller.Chat(CancellationToken.None);

        var messages = await h.Db.Set<Message>().AsNoTracking().ToListAsync();
        var convId = messages[0].ConversationId;
        foreach (var m in messages)
        {
            Assert.Contains(convId.ToString("N"), m.AudioBlobPath);
            Assert.Contains(m.Id.ToString("N"), m.AudioBlobPath);
        }
    }

    // --- Gated paths ------------------------------------------------

    [Fact]
    public async Task AudioChat_Paused_ReturnsCannedClip_NoSttNoLlmNoTts()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(true);
        // Any canned-key request goes through the synthesis substitute.
        h.Synthesis.SynthesizeArmenianAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));

        var result = await h.Controller.Chat(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(MimeMp3, file.ContentType);
        Assert.Equal(TtsMp3, file.FileContents);

        // STT + ChatService + blob store must NOT have been touched.
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default);
        await h.ChatService.DidNotReceiveWithAnyArgs().GetResponseAsync(
            default, default!, default, default, default);
        Assert.Empty(h.BlobStore.Written);
    }

    [Fact]
    public async Task AudioChat_Bedtime_ReturnsCannedClip_NoSttNoLlmNoTts()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(true);
        h.Synthesis.SynthesizeArmenianAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));

        var result = await h.Controller.Chat(CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default);
        await h.ChatService.DidNotReceiveWithAnyArgs().GetResponseAsync(
            default, default!, default, default, default);
    }

    [Fact]
    public async Task AudioChat_StoryDisabled_ReturnsCannedClip_NoSttNoLlm()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(false);
        h.Synthesis.SynthesizeArmenianAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));

        var result = await h.Controller.Chat(CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.Transcription.DidNotReceiveWithAnyArgs()
            .TranscribeArmenianAsync(default!, default!, default);
        await h.ChatService.DidNotReceiveWithAnyArgs().GetResponseAsync(
            default, default!, default, default, default);
    }

    [Fact]
    public async Task AudioChat_AutoplayContinue_OnPausedDevice_ReturnsCanned_NoChatService()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(true);
        h.Synthesis.SynthesizeArmenianAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));
        // Autoplay-continue request: the X-Areg-Continue header, no body.
        var httpContext = (DefaultHttpContext)h.Controller.HttpContext;
        httpContext.Request.Headers["X-Areg-Continue"] = "1";

        var result = await h.Controller.Chat(CancellationToken.None);

        // Gated BEFORE the autoplay advance — canned clip, no library call.
        Assert.IsType<FileContentResult>(result);
        await h.ChatService.DidNotReceiveWithAnyArgs().ContinueLibraryStoryAsync(default);
    }

    [Fact]
    public async Task AudioChat_CannedClipCached_SecondGatedRequestDoesNotReRender()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(true);
        h.Synthesis.SynthesizeArmenianAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));

        await h.Controller.Chat(CancellationToken.None);
        // Reset the request body because the controller consumed it.
        var httpContext = (DefaultHttpContext)h.Controller.HttpContext;
        httpContext.Request.Body = new MemoryStream(InboundPcm);
        httpContext.Request.ContentType = "audio/wav";
        httpContext.Request.ContentLength = InboundPcm.Length;
        await h.Controller.Chat(CancellationToken.None);

        // One TTS call shared across both gated requests (first
        // rendered, second served from cache).
        await h.Synthesis.Received(1).SynthesizeArmenianAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Failure paths ----------------------------------------------

    [Fact]
    public async Task AudioChat_EmptyBody_Returns400()
    {
        await using var h = await CreateAsync(inboundBody: Array.Empty<byte>());

        var result = await h.Controller.Chat(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AudioChat_SttThrows_Returns502_Sanitized_NoStateChange()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(true);
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("whisper failure with leak marker"));

        var result = await h.Controller.Chat(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, obj.StatusCode);
        var errorProp = obj.Value!.GetType().GetProperty("error");
        Assert.Equal("AI service unavailable. Please try again.",
            errorProp!.GetValue(obj.Value) as string);
        await h.ChatService.DidNotReceiveWithAnyArgs().GetResponseAsync(
            default, default!, default, default, default);
        Assert.Empty(h.BlobStore.Written);
    }

    [Fact]
    public async Task AudioChat_SttReturnsEmpty_Returns502_NoChatServiceCall()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(true);
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("   ");

        var result = await h.Controller.Chat(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, obj.StatusCode);
        await h.ChatService.DidNotReceiveWithAnyArgs().GetResponseAsync(
            default, default!, default, default, default);
    }

    [Fact]
    public async Task AudioChat_ChatServiceThrows_Returns502_Sanitized()
    {
        await using var h = await CreateAsync();
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(true);
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("պատմիր հեքիաթ");
        h.ChatService.GetResponseAsync(
                h.DeviceId, Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Throws(new InvalidOperationException(
                "openai upstream req_abc123 https://api.openai.com LEAK-SENTINEL"));

        var result = await h.Controller.Chat(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, obj.StatusCode);
        var errorProp = obj.Value!.GetType().GetProperty("error");
        Assert.Equal("AI service unavailable. Please try again.",
            errorProp!.GetValue(obj.Value) as string);
        var body = System.Text.Json.JsonSerializer.Serialize(obj.Value);
        Assert.DoesNotContain("LEAK-SENTINEL", body);
        Assert.DoesNotContain("https://", body);
    }

    [Fact]
    public async Task AudioChat_TtsThrows_Returns502_Sanitized_UserAudioNotOrphanedOnDisk()
    {
        await using var h = await CreateAsync();
        WireHappyPath(h);
        h.Synthesis.SynthesizeArmenianAsync(
                AssistantText, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("tts provider failure"));

        var result = await h.Controller.Chat(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, obj.StatusCode);
        // Text side was fully persisted by ChatService (unchanged
        // behavior). Blob store remained untouched because the
        // controller bails before the persist step.
        Assert.Equal(2, await h.Db.Set<Message>().CountAsync());
        Assert.Empty(h.BlobStore.Written);
    }

    [Fact]
    public async Task AudioChat_ModerationSafetyFlagPreserved_AudioBlobsStillStored()
    {
        // ChatService returns a SafetyFlag.Blocked safety-fallback
        // message (as it does for moderation-blocked inputs). The
        // audio controller must still TTS the fallback text and
        // store both blobs — the child audio + the warm redirect
        // audio. Preserves the existing moderation contract.
        await using var h = await CreateAsync();
        const string SafetyFallback = "Արի, մի հեքիաթ սկսենք։";
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(true);
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("unsafe-child-utterance");

        h.ChatService.GetResponseAsync(
                h.DeviceId, "unsafe-child-utterance",
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                var convId = Guid.NewGuid();
                var userMsgId = Guid.NewGuid();
                var fallbackMsgId = Guid.NewGuid();
                h.Db.Set<Device>().Add(new Device
                {
                    Id = h.DeviceId,
                    MacAddress = "mac-block",
                    Name = "Test",
                    ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
                    RegisteredAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
                h.Db.Set<Conversation>().Add(new Conversation
                {
                    Id = convId, DeviceId = h.DeviceId,
                    StartedAt = DateTime.UtcNow
                });
                h.Db.Set<Message>().Add(new Message
                {
                    Id = userMsgId, ConversationId = convId,
                    Role = MessageRole.User, Content = "unsafe-child-utterance",
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-10),
                    SafetyFlag = SafetyFlag.Blocked
                });
                h.Db.Set<Message>().Add(new Message
                {
                    Id = fallbackMsgId, ConversationId = convId,
                    Role = MessageRole.Assistant, Content = SafetyFallback,
                    Timestamp = DateTime.UtcNow,
                    SafetyFlag = SafetyFlag.Blocked
                });
                h.Db.SaveChanges();
                return new ChatResponse(SafetyFallback, convId, fallbackMsgId, SafetyFlag.Blocked);
            });
        h.Synthesis.SynthesizeArmenianAsync(
                SafetyFallback, Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, MimeMp3));

        var result = await h.Controller.Chat(CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        // Both messages still stored with Blocked flag + both blobs.
        var messages = await h.Db.Set<Message>().AsNoTracking().ToListAsync();
        Assert.All(messages, m => Assert.Equal(SafetyFlag.Blocked, m.SafetyFlag));
        Assert.All(messages, m => Assert.False(string.IsNullOrEmpty(m.AudioBlobPath)));
    }

    // --- Spoken-choice bridge (C1 voice Story UX) -------------------
    //
    // The physical toy has no screen. When a Story turn emits a valid
    // tail block, the child must HEAR the two choices — otherwise the
    // turn ends mid-interaction. These tests pin:
    //   1. Story turns with both choices: TTS receives the composed
    //      bridge; canonical Message.Content does NOT.
    //   2. Story turns with missing choice: TTS receives canonical
    //      text verbatim (no bridge fabricated from a single option).
    //   3. Non-Story turns: TTS receives canonical text verbatim
    //      even if ChoiceA/ChoiceB happen to be populated.
    //
    // Canonical-text invariant: the bridge is constructed per-request
    // for TTS only. It never reaches Message.Content. This is
    // enforced by ChatService owning the persistence write before the
    // controller runs, and by the controller not writing back.

    private static void WireStoryTurnWithChoices(
        Harness h,
        string transcript,
        string assistantText,
        string choiceA,
        string choiceB,
        string? mode,
        byte[] ttsExpectedInputBytes)
    {
        h.DeviceService.IsDevicePausedAsync(h.DeviceId).Returns(false);
        h.DeviceService.IsDeviceInBedtimeWindowAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(false);
        h.DeviceService.IsModeEnabledForRequestAsync(
            h.DeviceId, (Guid?)null, DetectedMode.Story).Returns(true);

        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(transcript);

        // Capture whatever text the controller hands to TTS.
        h.Synthesis.SynthesizeArmenianAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(ttsExpectedInputBytes, MimeMp3));

        h.ChatService.GetResponseAsync(
                h.DeviceId, transcript,
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(ci =>
            {
                var convId = Guid.NewGuid();
                var userMsgId = Guid.NewGuid();
                var assistantMsgId = Guid.NewGuid();
                h.Db.Set<Device>().Add(new Device
                {
                    Id = h.DeviceId,
                    MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..8],
                    Name = "Test",
                    ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
                    RegisteredAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
                h.Db.Set<Conversation>().Add(new Conversation
                {
                    Id = convId, DeviceId = h.DeviceId, StartedAt = DateTime.UtcNow
                });
                h.Db.Set<Message>().Add(new Message
                {
                    Id = userMsgId, ConversationId = convId,
                    Role = MessageRole.User, Content = transcript,
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-50),
                    SafetyFlag = SafetyFlag.Clean
                });
                h.Db.Set<Message>().Add(new Message
                {
                    Id = assistantMsgId, ConversationId = convId,
                    Role = MessageRole.Assistant, Content = assistantText,
                    Timestamp = DateTime.UtcNow,
                    SafetyFlag = SafetyFlag.Clean
                });
                h.Db.SaveChanges();
                return new ChatResponse(
                    assistantText, convId, assistantMsgId, SafetyFlag.Clean,
                    ChoiceA: choiceA, ChoiceB: choiceB, StorySessionId: null, Mode: mode);
            });
    }

    [Fact]
    public async Task AudioChat_StoryWithBothChoices_TtsInputIncludesSpokenBridge()
    {
        await using var h = await CreateAsync();
        const string story = "Փոքրիկ նապաստակը վազում էր մարգագետնում։";
        const string a = "Վերցնենք քարը";
        const string b = "Լսենք քարի ձայնը";
        WireStoryTurnWithChoices(h, "պատմիր հեքիաթ", story, a, b, "story", TtsMp3);

        var result = await h.Controller.Chat(CancellationToken.None);
        Assert.IsType<FileContentResult>(result);

        // The controller must have handed TTS the story + spoken bridge,
        // not the canonical story alone.
        var ttsArg = (string)h.Synthesis.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IAudioSynthesisService.SynthesizeArmenianAsync))
            .GetArguments()[0]!;
        Assert.StartsWith(story, ttsArg);
        Assert.Contains("Ի՞նչ անենք՝ առաջինը՝ Վերցնենք քարը, թե՞ երկրորդը՝ Լսենք քարի ձայնը։", ttsArg);
    }

    [Fact]
    public async Task AudioChat_StoryWithBothChoices_PersistedMessageContentDoesNotIncludeBridge()
    {
        await using var h = await CreateAsync();
        const string story = "Փոքրիկ նապաստակը վազում էր մարգագետնում։";
        WireStoryTurnWithChoices(h, "պատմիր հեքիաթ", story,
            "Վերցնենք քարը", "Լսենք քարի ձայնը", "story", TtsMp3);

        await h.Controller.Chat(CancellationToken.None);

        var assistantRow = await h.Db.Set<Message>()
            .AsNoTracking()
            .Where(m => m.Role == MessageRole.Assistant)
            .SingleAsync();
        Assert.Equal(story, assistantRow.Content);
        Assert.DoesNotContain("Ի՞նչ անենք", assistantRow.Content);
        Assert.DoesNotContain("առաջինը", assistantRow.Content);
    }

    [Fact]
    public async Task AudioChat_StoryWithOnlyChoiceA_TtsInputIsCanonicalVerbatim()
    {
        await using var h = await CreateAsync();
        const string story = "Փոքրիկ նապաստակը վազում էր մարգագետնում։";
        WireStoryTurnWithChoices(h, "պատմիր հեքիաթ", story,
            choiceA: "Վերցնենք քարը", choiceB: "", mode: "story", ttsExpectedInputBytes: TtsMp3);

        await h.Controller.Chat(CancellationToken.None);

        var ttsArg = (string)h.Synthesis.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IAudioSynthesisService.SynthesizeArmenianAsync))
            .GetArguments()[0]!;
        Assert.Equal(story, ttsArg);
    }

    [Fact]
    public async Task AudioChat_NonStoryTurnWithChoices_TtsInputIsCanonicalVerbatim()
    {
        await using var h = await CreateAsync();
        const string reply = "Այո՛, գիտեմ — կարապները թռչում են հարավ։";
        // mode "curiosity" is a non-Story mode that would never actually
        // populate ChoiceA/B in production, but pin the composer gate:
        // even if they are present, a non-Story turn must not grow a
        // Story-shaped spoken bridge.
        WireStoryTurnWithChoices(h, "ինչու են թռչուններ գաղթում", reply,
            "Նայենք այնտեղ", "Գնանք այնտեղ", "curiosity", TtsMp3);

        await h.Controller.Chat(CancellationToken.None);

        var ttsArg = (string)h.Synthesis.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IAudioSynthesisService.SynthesizeArmenianAsync))
            .GetArguments()[0]!;
        Assert.Equal(reply, ttsArg);
    }
}

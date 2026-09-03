using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Negotiated first-byte streaming on <c>POST /api/chat/story-qa</c>.
///
/// The contract under test: the answer is streamed chunk-by-chunk ONLY when
/// the toy sent <c>X-Areg-Accept-Stream: 1</c> AND the TTS provider can
/// stream AND output moderation passed. Every other combination keeps the
/// buffered <see cref="FileContentResult"/> (a Content-Length body) byte for
/// byte — the fielded firmware's buffered reader rejects any body without
/// one (fdc4b66 → 96d6084), so a globally-chunked response would silence
/// every toy's Q&amp;A.
/// </summary>
public class StoryQaControllerStreamingTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private const string Question = "Ո՞վ է փոքրիկ ամպիկը";
    private const string ModelAnswer = "Փոքրիկ ամպիկը երկնքի ընկերն է։";
    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF child question marker");

    // Streamed answer audio arrives in two pieces so a test can observe the
    // first on the wire while the second is still being "synthesized".
    private static readonly byte[] AnswerHead = Encoding.UTF8.GetBytes("ID3-STREAM-HEAD::");
    private static readonly byte[] AnswerTail = Encoding.UTF8.GetBytes("::STREAM-TAIL");

    private sealed record Harness(
        StoryQaController Controller,
        IAudioTranscriptionService Transcription,
        IAudioSynthesisService Synthesis,
        IModerationService Moderation,
        IAiChatClient AiChatClient);

    /// <summary>A provider stream that yields <see cref="AnswerHead"/> at
    /// once, then blocks until <see cref="Release"/> before yielding
    /// <see cref="AnswerTail"/>. Records whether the controller disposed it.</summary>
    private sealed class GatedStream : Stream
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _phase;
        public bool Disposed { get; private set; }
        public bool Completed => _phase >= 2;
        public void Release() => _gate.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            switch (_phase)
            {
                case 0:
                    _phase = 1;
                    AnswerHead.CopyTo(buffer);
                    return AnswerHead.Length;
                case 1:
                    await _gate.Task.WaitAsync(ct);
                    _phase = 2;
                    AnswerTail.CopyTo(buffer);
                    return AnswerTail.Length;
                default:
                    return 0;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Response body that signals the moment the first byte lands.</summary>
    private sealed class SignalingBody : MemoryStream
    {
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task FirstWrite => _first.Task;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer);
            if (buffer.Length > 0) _first.TrySetResult();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            if (count > 0) _first.TrySetResult();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }

    private static Harness Create(bool streamingProvider, bool acceptStream, IConfiguration? config = null)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        // Multi-interface sub ⇒ the controller's `is IStreamingAudioSynthesisService`
        // feature-detect fires; a plain sub is the buffered (OpenAI) provider.
        var synthesis = streamingProvider
            ? Substitute.For<IAudioSynthesisService, IStreamingAudioSynthesisService>()
            : Substitute.For<IAudioSynthesisService>();
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(true);

        // Buffered synthesis: distinct bytes per text so the answer, the
        // bridge and the fallback are all locatable in a body.
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AudioSynthesisResult(
                Encoding.UTF8.GetBytes("TTS::" + ci.ArgAt<string>(0)), "audio/mpeg"));

        transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(Question);
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        aiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ModelAnswer);

        var deviceId = Guid.NewGuid();
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation { Id = Guid.NewGuid(), DeviceId = deviceId, StartedAt = DateTime.UtcNow });
        conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(new Message { Id = Guid.NewGuid() });

        var controller = new StoryQaController(
            transcription, synthesis, new InMemoryCuratedStoryLibrary(),
            new LibraryStoryQuestionService(aiChatClient), moderation, conversations,
            childService, deviceService, new CannedVoiceClips(synthesis), new OpenAICostMeter(),
            Options.Create(new OpenAIDailyCostCapOptions { Enabled = false }),
            Substitute.For<IWebHostEnvironment>(),
            config ?? new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<StoryQaController>>());

        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        http.Request.Body = new MemoryStream(InboundWav);
        http.Request.ContentType = "audio/wav";
        http.Request.ContentLength = InboundWav.Length;
        if (acceptStream)
        {
            http.Request.Headers[StoryQaController.AcceptStreamHeader] = "1";
        }
        http.Response.Body = new SignalingBody();
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        return new Harness(controller, transcription, synthesis, moderation, aiChatClient);
    }

    private static IStreamingAudioSynthesisService Streamer(Harness h) =>
        (IStreamingAudioSynthesisService)h.Synthesis;

    private static SignalingBody Body(Harness h) =>
        (SignalingBody)h.Controller.HttpContext.Response.Body;

    private static byte[] BufferedAnswerBytes() => Encoding.UTF8.GetBytes("TTS::" + ModelAnswer);

    /// <summary>The buffered response must equal the canonical composition
    /// answer + pause + bridge (LittleCloud has no recap). The bridge is
    /// whichever rotated line was rendered — static caches leak across tests,
    /// so it is read back from the body rather than predicted.</summary>
    private static void AssertBufferedComposition(Harness h, FileContentResult file, byte[] answer)
    {
        Assert.Equal("audio/mpeg", file.ContentType);
        var body = file.FileContents;
        Assert.True(body.AsSpan().StartsWith(answer), "answer audio must be first");
        var pauseBytes = h.Controller.ComposeAnswerWithPause([], [], recap: null).Length;
        var bridge = body[(answer.Length + pauseBytes)..];
        Assert.Equal(h.Controller.ComposeAnswerWithPause(answer, bridge, recap: null), body);
    }

    // (a) No header ⇒ buffered, byte-identical, even with a streaming provider.

    [Fact]
    public async Task NoOptInHeader_StreamingProvider_ReturnsBufferedContentLengthBody()
    {
        var h = Create(streamingProvider: true, acceptStream: false);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        AssertBufferedComposition(h, file, BufferedAnswerBytes());
        Assert.Equal(0, Body(h).Length);
        await Streamer(h).DidNotReceiveWithAnyArgs().SynthesizeArmenianStreamAsync(default!, default);
    }

    // (b) Header, but the provider cannot stream ⇒ buffered.

    [Fact]
    public async Task OptInHeader_BufferedProvider_ReturnsBufferedContentLengthBody()
    {
        var h = Create(streamingProvider: false, acceptStream: true);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        AssertBufferedComposition(h, file, BufferedAnswerBytes());
        Assert.Equal(0, Body(h).Length);
    }

    // (c) Header + streaming provider ⇒ the answer streams: first bytes are
    // on the wire BEFORE synthesis has finished, and the finished body is the
    // answer followed by exactly the buffered tail (pause + bridge).

    [Fact]
    public async Task OptInHeader_StreamingProvider_FirstBytesLeaveBeforeSynthesisCompletes()
    {
        var h = Create(streamingProvider: true, acceptStream: true);
        var gated = new GatedStream();
        Streamer(h).SynthesizeArmenianStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisStreamResult(gated, "audio/mpeg"));

        var ask = h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        // KEYSTONE: the first answer bytes reach the response while the
        // provider stream is still blocked mid-synthesis.
        await Body(h).FirstWrite.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(gated.Completed, "synthesis must still be in flight when the first byte leaves");
        Assert.False(ask.IsCompleted, "the action must not have finished before the stream did");

        gated.Release();
        var result = await ask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("audio/mpeg", h.Controller.HttpContext.Response.ContentType);
        Assert.Null(h.Controller.HttpContext.Response.ContentLength); // chunked, not sized
        Assert.True(gated.Disposed, "the provider stream must be disposed after the body");

        var body = Body(h).ToArray();
        var answer = AnswerHead.Concat(AnswerTail).ToArray();
        Assert.True(body.AsSpan().StartsWith(answer), "streamed answer audio must be first");
        var pauseBytes = h.Controller.ComposeAnswerWithPause([], [], recap: null).Length;
        var bridge = body[(answer.Length + pauseBytes)..];
        Assert.NotEmpty(bridge);
        Assert.Equal(h.Controller.ComposeAnswerWithPause(answer, bridge, recap: null), body);

        // The answer text itself was never rendered through the buffered
        // provider call — only the cached bridge is.
        await h.Synthesis.DidNotReceive().SynthesizeArmenianAsync(ModelAnswer, Arg.Any<CancellationToken>());
        await Streamer(h).Received(1).SynthesizeArmenianStreamAsync(ModelAnswer, Arg.Any<CancellationToken>());
    }

    // (d) Output moderation blocks ⇒ canned fallback, buffered, and not one
    // byte of the speculative answer stream reaches the wire.

    [Fact]
    public async Task OptInHeader_OutputBlocked_SpeaksBufferedFallback_ZeroAnswerBytesOnWire()
    {
        var h = Create(streamingProvider: true, acceptStream: true);
        var gated = new GatedStream();
        gated.Release(); // fully available — proves the controller never read it out
        Streamer(h).SynthesizeArmenianStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisStreamResult(gated, "audio/mpeg"));
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(
                new ModerationResult(true, new List<string>()),                 // input: safe
                new ModerationResult(false, new List<string> { "violence" }));  // output: blocked

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        var fallback = Encoding.UTF8.GetBytes("TTS::" + StoryAnswerFilter.SafeFallback);
        AssertBufferedComposition(h, file, fallback);

        Assert.Equal(0, Body(h).Length);
        Assert.DoesNotContain(SlidingWindows(file.FileContents, AnswerHead.Length), w => w.SequenceEqual(AnswerHead));
        Assert.False(gated.Completed, "the blocked answer stream must never be read out");
        // Disposal is synchronous once the stream task has completed; a
        // still-opening stream is disposed by a continuation, so allow a beat.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!gated.Disposed && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(gated.Disposed, "the blocked answer stream must be closed, not leaked");
    }

    // (e) Header + streaming provider, but the stream cannot be opened ⇒ the
    // buffered fallback of the same moderated text, not a 502.

    [Fact]
    public async Task OptInHeader_StreamOpenFails_FallsBackToBufferedAnswer()
    {
        var h = Create(streamingProvider: true, acceptStream: true);
        Streamer(h).SynthesizeArmenianStreamAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<AudioSynthesisStreamResult>(_ => throw new HttpRequestException("upstream 503"));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        AssertBufferedComposition(h, file, BufferedAnswerBytes());
        Assert.Equal(0, Body(h).Length);
    }

    // (f) Operator kill switch ⇒ buffered even when everything else lines up.

    [Fact]
    public async Task KillSwitchOff_OptInHeader_StreamingProvider_ReturnsBuffered()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("StoryQa:StreamAnswerAudio", "false"),
        }).Build();
        var h = Create(streamingProvider: true, acceptStream: true, config);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        AssertBufferedComposition(h, file, BufferedAnswerBytes());
        await Streamer(h).DidNotReceiveWithAnyArgs().SynthesizeArmenianStreamAsync(default!, default);
    }

    private static IEnumerable<byte[]> SlidingWindows(byte[] data, int window)
    {
        for (var i = 0; i + window <= data.Length; i++)
        {
            yield return data[i..(i + window)];
        }
    }
}

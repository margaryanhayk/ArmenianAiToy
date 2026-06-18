using System.Text;
using ArmenianAiToy.Infrastructure.Audio;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Retry contract for <see cref="OpenAIWhisperTranscriptionService"/>.
/// STT previously had no resilience; it now does a single retry on a
/// transient failure (mirroring the TTS adapter). Tests script the
/// per-attempt outcome via the <c>protected virtual TranscribeOnceAsync</c>
/// seam — no network, same mocking approach as the moderation adapter.
/// </summary>
public class OpenAIWhisperRetryTests
{
    private sealed class StubWhisper : OpenAIWhisperTranscriptionService
    {
        public Queue<Func<string>> Attempts { get; } = new();
        public int Calls { get; private set; }

        public StubWhisper()
            : base(new AudioClient("whisper-1", "stub"),
                   Substitute.For<ILogger<OpenAIWhisperTranscriptionService>>())
        { }

        protected override Task<string> TranscribeOnceAsync(
            byte[] audioBytes, string contentType, string? prompt, string? model,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Attempts.Dequeue()());
        }
    }

    private static MemoryStream Wav() => new(Encoding.UTF8.GetBytes("RIFF audio"), writable: false);

    [Fact]
    public async Task TransientFailure_ThenSuccess_RetriesOnce_ReturnsTranscript()
    {
        var svc = new StubWhisper();
        svc.Attempts.Enqueue(() => throw new InvalidOperationException("transient blip"));
        svc.Attempts.Enqueue(() => "Փոքրիկ ամպիկը");

        var text = await svc.TranscribeArmenianAsync(Wav(), "audio/wav", prompt: null);

        Assert.Equal("Փոքրիկ ամպիկը", text);
        Assert.Equal(2, svc.Calls); // first failed, second succeeded
    }

    [Fact]
    public async Task TwoConsecutiveFailures_PropagatesAfterSecondAttempt()
    {
        var svc = new StubWhisper();
        svc.Attempts.Enqueue(() => throw new InvalidOperationException("blip 1"));
        svc.Attempts.Enqueue(() => throw new InvalidOperationException("blip 2"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.TranscribeArmenianAsync(Wav(), "audio/wav", prompt: null));

        Assert.Equal(2, svc.Calls); // exactly one retry, then it gives up
    }

    [Fact]
    public async Task FirstAttemptSucceeds_NoRetry()
    {
        var svc = new StubWhisper();
        svc.Attempts.Enqueue(() => "բարև");

        var text = await svc.TranscribeArmenianAsync(Wav(), "audio/wav", prompt: null);

        Assert.Equal("բարև", text);
        Assert.Equal(1, svc.Calls);
    }
}

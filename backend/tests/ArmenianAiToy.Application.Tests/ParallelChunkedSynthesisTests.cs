using System.Diagnostics;
using System.Text;
using ArmenianAiToy.Application.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the parallel sentence-chunked TTS latency seam: it must be OFF by
/// default, must never cut inside a sentence, must reproduce the same words
/// in the same order, must actually overlap its provider calls, and must
/// degrade to a single full-text render when a chunk fails.
/// </summary>
public class ParallelChunkedSynthesisTests
{
    private const string Mp3 = "audio/mpeg";

    /// <summary>Records every call and how many were in flight at once, so a
    /// test can prove the renders really overlapped.</summary>
    private sealed class RecordingSynthesis : IAudioSynthesisService
    {
        private int _inFlight;
        private readonly object _gate = new();

        public List<string> Calls { get; } = [];
        public int MaxConcurrent { get; private set; }
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        /// <summary>When set, every call whose text differs from this value
        /// throws — i.e. all the chunk renders fail while the full-text
        /// fallback still succeeds.</summary>
        public string? FailUnlessTextEquals { get; set; }
        public string Mime { get; set; } = Mp3;

        public async Task<AudioSynthesisResult> SynthesizeArmenianAsync(
            string text, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add(text);
                _inFlight++;
                MaxConcurrent = Math.Max(MaxConcurrent, _inFlight);
            }
            try
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken);
                }
                if (FailUnlessTextEquals is { } whole && !string.Equals(text, whole, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("provider blip");
                }
                // Encode the text so concatenation order is verifiable.
                return new AudioSynthesisResult(Encoding.UTF8.GetBytes("[" + text + "]"), Mime);
            }
            finally
            {
                lock (_gate) { _inFlight--; }
            }
        }
    }

    private static ParallelChunkedSynthesisService Create(
        RecordingSynthesis inner, int minTextChars = 40, int maxChunkChars = 40, int maxChunks = 4)
        => new(inner,
            new ParallelTtsOptions(true, minTextChars, maxChunkChars, maxChunks),
            NullLogger<ParallelChunkedSynthesisService>.Instance);

    // Five Armenian sentences, each ending in the Armenian full stop «։».
    private const string FiveSentences =
        "Արևը ծագեց սարերի հետևից։ Փոքրիկ աղվեսը արթնացավ։ "
        + "Նա նայեց շուրջը և ժպտաց։ Անտառը լիքն էր թռչունների ձայներով։ "
        + "Աղվեսը որոշեց գնալ գետի մոտ։";

    // ── The flag itself ──────────────────────────────────────────────

    [Fact]
    public void Resolve_MissingConfig_IsDisabledWithShippedDefaults()
    {
        var options = ParallelTtsOptions.Resolve(
            new ConfigurationBuilder().Build());

        Assert.False(options.Enabled);
        Assert.Equal(ParallelTtsOptions.DefaultMinTextChars, options.MinTextChars);
        Assert.Equal(ParallelTtsOptions.DefaultMaxChunkChars, options.MaxChunkChars);
        Assert.Equal(ParallelTtsOptions.DefaultMaxChunks, options.MaxChunks);
    }

    [Fact]
    public void Resolve_ReadsOverrides_AndClampsMaxChunks()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Audio:ParallelTts:Enabled"] = "true",
            ["Audio:ParallelTts:MinTextChars"] = "100",
            ["Audio:ParallelTts:MaxChunkChars"] = "150",
            ["Audio:ParallelTts:MaxChunks"] = "99",
        }).Build();

        var options = ParallelTtsOptions.Resolve(config);

        Assert.True(options.Enabled);
        Assert.Equal(100, options.MinTextChars);
        Assert.Equal(150, options.MaxChunkChars);
        Assert.Equal(8, options.MaxChunks); // clamped
    }

    [Fact]
    public void Resolve_GarbageValues_FallBackToDefaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Audio:ParallelTts:Enabled"] = "yes-please",
            ["Audio:ParallelTts:MaxChunkChars"] = "-5",
        }).Build();

        var options = ParallelTtsOptions.Resolve(config);

        Assert.False(options.Enabled);
        Assert.Equal(ParallelTtsOptions.DefaultMaxChunkChars, options.MaxChunkChars);
    }

    // ── Pass-through cases (must behave exactly like today) ──────────

    [Fact]
    public async Task ShortText_MakesExactlyOneCall_WithTheOriginalText()
    {
        var inner = new RecordingSynthesis();
        var svc = Create(inner, minTextChars: 260);

        const string shortReply = "Բարև, փոքրի՛կս։ Ուրախ եմ քեզ տեսնել։";

        var result = await svc.SynthesizeArmenianAsync(shortReply);

        Assert.Single(inner.Calls);
        Assert.Equal(shortReply, inner.Calls[0]);
        Assert.Equal(Mp3, result.MimeType);
    }

    [Fact]
    public async Task SingleLongSentence_IsNeverCut()
    {
        var inner = new RecordingSynthesis();
        var svc = Create(inner, minTextChars: 10, maxChunkChars: 20);
        var oneSentence = new string('ա', 300) + "։";

        await svc.SynthesizeArmenianAsync(oneSentence);

        Assert.Single(inner.Calls);
        Assert.Equal(oneSentence, inner.Calls[0]);
    }

    [Fact]
    public async Task ArmenianQuestionMark_IsNotASentenceBoundary()
    {
        // «՞» sits inside the stressed vowel of the word, not at the end of
        // the sentence — splitting on it would cut a word in half.
        var inner = new RecordingSynthesis();
        var svc = Create(inner, minTextChars: 5, maxChunkChars: 10);

        await svc.SynthesizeArmenianAsync("Ի՞նչ ես ուզում լսել");

        Assert.Single(inner.Calls);
    }

    // ── The latency behaviour ───────────────────────────────────────

    [Fact]
    public async Task LongText_SplitsOnSentences_AndRendersConcurrently()
    {
        var inner = new RecordingSynthesis { Delay = TimeSpan.FromMilliseconds(120) };
        var svc = Create(inner, minTextChars: 40, maxChunkChars: 60, maxChunks: 4);

        await svc.SynthesizeArmenianAsync(FiveSentences);

        Assert.True(inner.Calls.Count > 1, "long text should be chunked");
        // Overlap is asserted by the observed concurrency counter, NOT by
        // wall-clock. A stopwatch assertion here failed on a loaded machine
        // (6482 ms for work that overlaps fine) — a timing race in a test is
        // a flaky gate on every future deploy, and CI gates the deploy.
        Assert.True(inner.MaxConcurrent > 1,
            $"chunks must render concurrently (max concurrent was {inner.MaxConcurrent})");
    }

    [Fact]
    public async Task Chunks_ConcatenateInOrder_PreservingEverySentence()
    {
        var inner = new RecordingSynthesis();
        var svc = Create(inner, minTextChars: 40, maxChunkChars: 60, maxChunks: 4);

        var result = await svc.SynthesizeArmenianAsync(FiveSentences);

        // The fake wraps each chunk as "[chunk]". Restoring the single space
        // that a chunk boundary consumed must give back exactly the original
        // sentences, in order — nothing dropped, added, or reordered.
        var rebuilt = Encoding.UTF8.GetString(result.Content)
            .Replace("][", " ", StringComparison.Ordinal)
            .Trim('[', ']');
        Assert.Equal(FiveSentences, rebuilt);
        Assert.Equal(FiveSentences, string.Join(" ", inner.Calls));
    }

    [Fact]
    public async Task ChunkCount_NeverExceedsTheConfiguredCap()
    {
        var inner = new RecordingSynthesis();
        var svc = Create(inner, minTextChars: 40, maxChunkChars: 30, maxChunks: 2);

        await svc.SynthesizeArmenianAsync(FiveSentences);

        Assert.True(inner.Calls.Count <= 2, $"got {inner.Calls.Count} chunks");
        Assert.Equal(FiveSentences, string.Join(" ", inner.Calls));
    }

    // ── Failure posture ─────────────────────────────────────────────

    [Fact]
    public async Task FailingChunk_FallsBackToOneFullTextRender()
    {
        var inner = new RecordingSynthesis { FailUnlessTextEquals = FiveSentences };
        var svc = Create(inner, minTextChars: 40, maxChunkChars: 60, maxChunks: 4);

        var result = await svc.SynthesizeArmenianAsync(FiveSentences);

        // The last call is the whole text, and its audio is what came back —
        // the child still hears a complete answer, not a truncated one.
        Assert.Equal(FiveSentences, inner.Calls[^1]);
        Assert.Equal("[" + FiveSentences + "]", Encoding.UTF8.GetString(result.Content));
    }

    [Fact]
    public async Task Cancellation_Propagates_AndDoesNotRetryAsFullText()
    {
        var inner = new RecordingSynthesis { Delay = TimeSpan.FromSeconds(5) };
        var svc = Create(inner, minTextChars: 40, maxChunkChars: 60, maxChunks: 4);
        using var cts = new CancellationTokenSource();
        var task = svc.SynthesizeArmenianAsync(FiveSentences, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.DoesNotContain(FiveSentences, inner.Calls);
    }

    // ── MP3 stitching ───────────────────────────────────────────────

    [Fact]
    public void Concatenate_StripsId3FromEveryChunkButTheFirst()
    {
        // ID3v2 header: "ID3", version, flags, 4 syncsafe size bytes.
        static byte[] WithId3(byte payload)
            => [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 2, 0xAA, 0xBB, payload];

        var result = ParallelChunkedSynthesisService.Concatenate(
        [
            new AudioSynthesisResult(WithId3(1), Mp3),
            new AudioSynthesisResult(WithId3(2), Mp3),
        ]);

        // First chunk kept whole (13 bytes); second contributes only its
        // single audio byte (10 header + 2 tag body stripped).
        Assert.Equal(14, result.Content.Length);
        Assert.Equal((byte)'I', result.Content[0]);
        Assert.Equal((byte)2, result.Content[^1]);
    }

    [Fact]
    public void Concatenate_NonMp3_IsJoinedVerbatim()
    {
        var result = ParallelChunkedSynthesisService.Concatenate(
        [
            new AudioSynthesisResult([1, 2], "audio/wav"),
            new AudioSynthesisResult([3, 4], "audio/wav"),
        ]);

        Assert.Equal([1, 2, 3, 4], result.Content);
        Assert.Equal("audio/wav", result.MimeType);
    }
}

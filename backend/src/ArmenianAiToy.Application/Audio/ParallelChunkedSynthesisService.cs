using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Audio;

/// <summary>
/// Resolved knobs for the parallel-TTS latency path
/// (<c>Audio:ParallelTts:*</c>). Ships DISABLED — the flag defaults to the
/// exact single-call behaviour that shipped before it existed, so nothing
/// a child hears changes until an operator opts in after a listen test.
/// </summary>
public sealed record ParallelTtsOptions(
    bool Enabled,
    int MinTextChars,
    int MaxChunkChars,
    int MaxChunks)
{
    public const bool DefaultEnabled = false;

    /// <summary>Below this length a reply is a single TTS call, exactly as
    /// today. Short lines gain nothing from chunking and would only pay the
    /// concatenation seam.</summary>
    public const int DefaultMinTextChars = 260;

    /// <summary>Target chunk size. ~240 characters is roughly two Armenian
    /// story sentences — long enough for natural phrasing, short enough that
    /// the provider returns quickly.</summary>
    public const int DefaultMaxChunkChars = 240;

    /// <summary>Concurrency ceiling per reply. Bounds the burst against the
    /// provider's rate limit; 4 covers the longest story turn we produce.</summary>
    public const int DefaultMaxChunks = 4;

    public static ParallelTtsOptions Resolve(IConfiguration config)
    {
        var enabled = bool.TryParse(config["Audio:ParallelTts:Enabled"], out var on)
            ? on
            : DefaultEnabled;
        return new ParallelTtsOptions(
            enabled,
            ReadPositive(config, "Audio:ParallelTts:MinTextChars", DefaultMinTextChars),
            ReadPositive(config, "Audio:ParallelTts:MaxChunkChars", DefaultMaxChunkChars),
            Math.Clamp(
                ReadPositive(config, "Audio:ParallelTts:MaxChunks", DefaultMaxChunks), 1, 8));
    }

    private static int ReadPositive(IConfiguration config, string key, int fallback)
        => int.TryParse(config[key], out var v) && v > 0 ? v : fallback;
}

/// <summary>
/// Latency decorator over any <see cref="IAudioSynthesisService"/>: splits a
/// long reply on SENTENCE boundaries, renders the pieces CONCURRENTLY, and
/// concatenates them in order into the single buffered MP3 the device
/// already expects.
///
/// <para><b>Why this and not streaming.</b> Provider TTS latency scales with
/// the length of the text, and the whole file must exist before the toy is
/// sent a <c>Content-Length</c> (the firmware sizes its receive buffer from
/// it — a chunked reply was tried and reverted, hardware-verified). So the
/// only way to shorten the wait WITHOUT a firmware change is to stop paying
/// for the sentences one after another. Three concurrent chunks turn a
/// serial render into roughly the cost of its longest piece.</para>
///
/// <para><b>Nothing about safety changes.</b> The text handed here is already
/// output-moderated and validated by the caller; this class only decides how
/// it is voiced. It never edits, reorders, drops or adds a word — concatenating
/// the chunks reproduces the same sentences in the same order.</para>
///
/// <para><b>Failure posture is no worse than a single call.</b> Splitting
/// multiplies the chances that one request blips, so any chunk failure falls
/// back to one full-text render — the caller's existing sanitized-failure
/// handling only sees an exception if that fallback also fails.</para>
///
/// <para><b>Known limit.</b> The concatenated MP3 carries the first chunk's
/// duration header, so a browser that trusts that header (Safari) can
/// under-report the length of the parent-dashboard replay blob. The same is
/// already true of the shipped answer+bridge+recap composition in
/// <c>StoryQaController</c>; on the toy's own decoder, which plays
/// concatenated MP3 today (hardware-verified), it is a non-issue. ID3 tags on
/// the trailing chunks ARE stripped so the file carries exactly one.</para>
/// </summary>
public sealed class ParallelChunkedSynthesisService : IAudioSynthesisService
{
    private const string Mp3Mime = "audio/mpeg";

    private readonly IAudioSynthesisService _inner;
    private readonly ParallelTtsOptions _options;
    private readonly ILogger<ParallelChunkedSynthesisService> _logger;

    public ParallelChunkedSynthesisService(
        IAudioSynthesisService inner,
        ParallelTtsOptions options,
        ILogger<ParallelChunkedSynthesisService> logger)
    {
        _inner = inner;
        _options = options;
        _logger = logger;
    }

    public async Task<AudioSynthesisResult> SynthesizeArmenianAsync(
        string text, CancellationToken cancellationToken = default)
    {
        var chunks = SpeechChunker.Split(
            text, _options.MinTextChars, _options.MaxChunkChars, _options.MaxChunks);
        if (chunks.Count < 2)
        {
            // Short line / single sentence — the exact call today's code makes.
            return await _inner.SynthesizeArmenianAsync(text, cancellationToken);
        }

        var tasks = new Task<AudioSynthesisResult>[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            tasks[i] = _inner.SynthesizeArmenianAsync(chunks[i], cancellationToken);
        }

        AudioSynthesisResult[] parts;
        try
        {
            parts = await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Observe every sibling so a faulted task can't surface as an
            // unobserved-exception crash, then take the single-call path.
            foreach (var task in tasks)
            {
                _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
            }
            _logger.LogWarning(ex,
                "Parallel chunked TTS: a chunk failed; falling back to one full-text render ({Chunks} chunks)",
                chunks.Count);
            return await _inner.SynthesizeArmenianAsync(text, cancellationToken);
        }

        var combined = Concatenate(parts);
        _logger.LogInformation(
            "Parallel chunked TTS: {Chunks} chunks, {TextChars} chars, {AudioBytes} bytes",
            chunks.Count, text?.Trim().Length ?? 0, combined.Content.Length);
        return combined;
    }

    /// <summary>Joins the rendered pieces in order. For MP3 the leading ID3v2
    /// tag is stripped from every piece but the first, so the result carries a
    /// single tag instead of one per chunk.</summary>
    internal static AudioSynthesisResult Concatenate(IReadOnlyList<AudioSynthesisResult> parts)
    {
        var mime = parts[0].MimeType;
        var isMp3 = string.Equals(mime, Mp3Mime, StringComparison.OrdinalIgnoreCase);

        var offsets = new int[parts.Count];
        var total = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            offsets[i] = isMp3 && i > 0 ? Id3v2Length(parts[i].Content) : 0;
            total += parts[i].Content.Length - offsets[i];
        }

        var buffer = new byte[total];
        var written = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var content = parts[i].Content;
            var offset = offsets[i];
            Buffer.BlockCopy(content, offset, buffer, written, content.Length - offset);
            written += content.Length - offset;
        }
        return new AudioSynthesisResult(buffer, mime);
    }

    /// <summary>Byte length of a leading ID3v2 tag (header + syncsafe size +
    /// optional footer), or 0 when the buffer does not start with one.</summary>
    internal static int Id3v2Length(byte[] content)
    {
        if (content.Length < 10 || content[0] != (byte)'I' || content[1] != (byte)'D'
            || content[2] != (byte)'3')
        {
            return 0;
        }
        // Syncsafe integer: 7 significant bits per byte.
        var size = (content[6] & 0x7F) << 21
                 | (content[7] & 0x7F) << 14
                 | (content[8] & 0x7F) << 7
                 | (content[9] & 0x7F);
        var length = 10 + size;
        if ((content[5] & 0x10) != 0)
        {
            length += 10; // footer present
        }
        return length <= content.Length ? length : 0;
    }
}

using System.Security.Cryptography;
using System.Text;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Application.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Pre-rendered, streamable whole-story narration audio.
///
/// Because curated stories are fixed, each story's full narration is
/// rendered to ONE MP3 exactly once (on first request) and cached on
/// disk. The device STREAMS that file — decoding as it downloads —
/// which makes narration continuous (no per-segment turns) and removes
/// the 512 KB single-clip buffer limit. The endpoint enables HTTP Range
/// so the device can resume from the exact byte offset where the child
/// barged in: <c>Range: bytes=&lt;offset&gt;-</c>.
///
/// Route is <c>/api/story-audio</c> — deliberately OUTSIDE the
/// device-auth prefixes (<c>/api/chat</c>, <c>/api/audio</c>) so the
/// firmware's ESP8266Audio HTTP stream can fetch it with a plain GET
/// (that library can't easily add the X-Device-Id / X-Api-Key headers).
/// Story narration is not sensitive content; the Q&amp;A POST path stays
/// device-authed as before.
/// </summary>
[ApiController]
[Route("api/story-audio")]
[EnableRateLimiting(StoryAudioRateLimiter.PolicyName)]
public class StoryAudioController : ControllerBase
{
    // Split the narration at Armenian sentence ends and render each
    // chunk separately, concatenating the rendered MP3s (frames are
    // independently decodable, so byte-append is fine for a continuous
    // narration). Kept small so each individual TTS call finishes well
    // within the synthesis adapter's 30 s timeout — a single large
    // chunk (~3500 chars ≈ minutes of audio) overruns it.
    private const int MaxChunkChars = 900;

    // Serialize first-render per process so two concurrent first hits
    // don't both render the same story.
    private static readonly SemaphoreSlim RenderLock = new(1, 1);

    // In-memory cache of each story's sentence-start byte-offset map
    // (loaded from the `.offsets.json` sidecar). Used to snap a resume
    // offset DOWN to the start of the sentence the child was hearing, so
    // narration re-reads the last line instead of resuming mid-sentence
    // (the "micro-rewind" — research-backed re-entry after a Q&A).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long[]> OffsetMaps = new();

    private readonly IAudioSynthesisService _synthesis;
    private readonly ICuratedStoryLibrary _library;
    private readonly IModerationService _moderation;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<StoryAudioController> _logger;

    public StoryAudioController(
        IAudioSynthesisService synthesis,
        ICuratedStoryLibrary library,
        IModerationService moderation,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<StoryAudioController> logger)
    {
        _synthesis = synthesis;
        _library = library;
        _moderation = moderation;
        _env = env;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Streams the whole-story narration MP3 (Range-capable). Renders +
    /// caches on first request. <c>?refresh=1</c> forces a re-render.
    /// </summary>
    [HttpGet("{storyId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(206)]
    [ProducesResponseType(404)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> GetStoryAudio(
        string storyId,
        [FromQuery] bool refresh = false,
        [FromQuery] long from = 0,
        [FromQuery] string? token = null,
        [FromQuery] string? refreshToken = null,
        CancellationToken cancellationToken = default)
    {
        var story = _library.GetById(storyId);
        if (story is null)
        {
            return NotFound(new { error = "Unknown story." });
        }

        // Access gate. When StoryAudio:SigningKey is configured, the
        // header-less stream requires a valid signed token (minted by the
        // device-authed /api/chat/story-audio-token endpoint). A missing /
        // tampered / expired / wrong-story token returns the SAME 404 as an
        // unknown story — concealment, so the endpoint reveals nothing to a
        // token-less caller. Unset key => open (dev/bench), opt-in posture.
        if (!IsTokenAccepted(storyId, token))
        {
            // Wire response is the SAME 404 as an unknown story (concealment —
            // external callers learn nothing). But log distinctly so an
            // operator who has enabled StoryAudio:SigningKey can tell a
            // rejected/expired/missing token apart from a genuinely unknown
            // story when playback starts 404-ing.
            _logger.LogWarning(
                "Story-audio token rejected for {StoryId} (enforcement on; token missing/invalid/expired/wrong-story). Returning concealment 404.",
                storyId);
            return NotFound(new { error = "Unknown story." });
        }

        var cachePath = CachePathFor(storyId);
        var offsetsPath = OffsetsPathFor(cachePath);
        var segmentsPath = SegmentsPathFor(cachePath);

        // ?refresh forces a re-render — the only UNBOUNDED OpenAI-TTS cost
        // vector on this otherwise-cached endpoint. Honor it ONLY when the
        // caller presents the operator secret StoryAudio:RefreshToken. With
        // no secret configured, refresh is disabled (delete the cache file
        // for a dev re-render). This is the cost-cap for this endpoint.
        if (refresh && !IsRefreshAuthorized(refreshToken))
        {
            _logger.LogWarning(
                "Story-audio refresh denied for {StoryId} (missing/invalid refresh token)", storyId);
            refresh = false;
        }
        if (refresh)
        {
            if (System.IO.File.Exists(cachePath)) System.IO.File.Delete(cachePath);
            if (System.IO.File.Exists(offsetsPath)) System.IO.File.Delete(offsetsPath);
            if (System.IO.File.Exists(segmentsPath)) System.IO.File.Delete(segmentsPath);
            OffsetMaps.TryRemove(storyId, out _);
        }

        // Render when the audio OR either sidecar (sentence-offset map,
        // segment-offset map) is missing, so a cache from before these
        // features regenerates all three together.
        if (!System.IO.File.Exists(cachePath)
            || !System.IO.File.Exists(offsetsPath)
            || !System.IO.File.Exists(segmentsPath))
        {
            await RenderLock.WaitAsync(cancellationToken);
            try
            {
                if (!System.IO.File.Exists(cachePath)
                    || !System.IO.File.Exists(offsetsPath)
                    || !System.IO.File.Exists(segmentsPath))
                {
                    await RenderAndCacheAsync(story, cachePath, offsetsPath, segmentsPath, cancellationToken);
                    // Count the cold render (the cost-bearing event). A serve
                    // from cache does not reach here, so it is not counted.
                    AppMeter.StoryAudioRender.Add(1,
                        new KeyValuePair<string, object?>("result", "success"));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (StoryContentBlockedException)
            {
                // #016: output moderation rejected the narration text — do NOT
                // serve it. Concealment 404 (same wire shape as an unknown
                // story / rejected token); the LogError inside the render path
                // is the operator-facing signal, and the device plays its canned
                // fallback rather than ever speaking the unsafe content.
                AppMeter.StoryAudioRender.Add(1,
                    new KeyValuePair<string, object?>("result", "blocked"));
                return NotFound(new { error = "Unknown story." });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Story-audio render failed for {StoryId}", storyId);
                AppMeter.StoryAudioRender.Add(1,
                    new KeyValuePair<string, object?>("result", "failure"));
                return StatusCode(502, new { error = "AI service unavailable. Please try again." });
            }
            finally
            {
                RenderLock.Release();
            }
        }

        // Resume path: `?from=<byteOffset>` serves [offset, end) directly.
        // The firmware uses this (rather than a Range header) because
        // ESP8266Audio's HTTP stream opens a fresh plain GET on resume —
        // putting the offset in the URL is the simplest reliable way to
        // continue the narration from the exact byte the child barged in.
        if (from > 0)
        {
            var length = new FileInfo(cachePath).Length;
            if (from >= length)
            {
                return NoContent(); // past the end → nothing left to play
            }
            // Micro-rewind: snap the resume offset DOWN to the start of the
            // sentence the child was hearing, so narration re-reads the last
            // line rather than snapping in mid-sentence. No-op if the offset
            // map is unavailable (serves the exact byte, as before).
            var resumeFrom = SnapToSentenceStart(storyId, offsetsPath, from);
            var stream = new FileStream(
                cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Seek(resumeFrom, SeekOrigin.Begin);
            return File(stream, "audio/mpeg"); // streams [resumeFrom, end)
        }

        // Full playback. enableRangeProcessing => Accept-Ranges + 206 on
        // Range requests too (kept for completeness / other clients).
        return PhysicalFile(cachePath, "audio/mpeg", enableRangeProcessing: true);
    }

    private string CachePathFor(string storyId)
    {
        var root = _config["StoryAudio:CacheRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(_env.ContentRootPath, "story-audio-cache");
        }
        Directory.CreateDirectory(root);
        // storyId is the kebab-case library id (validated by the loader);
        // still constrain the file name defensively.
        var safe = string.Concat(storyId.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        return Path.Combine(root, $"{safe}.mp3");
    }

    private static string OffsetsPathFor(string cachePath) =>
        Path.ChangeExtension(cachePath, ".offsets.json");

    /// <summary>Sidecar holding the per-segment START byte offsets across
    /// the whole MP3 (one entry per story segment, segment 0 at byte 0).
    /// Lets the Q&amp;A path map a resume offset to the exact segment the
    /// child is in, instead of assuming every segment is 1/N of the file.</summary>
    private static string SegmentsPathFor(string cachePath) =>
        Path.ChangeExtension(cachePath, ".segments.json");

    /// <summary>Access gate for the header-less stream. When
    /// <c>StoryAudio:SigningKey</c> is set, the query <c>token</c> must be a
    /// valid signed token bound to this story and not yet expired. When no key
    /// is configured the gate is FAIL-CLOSED (deny) unless
    /// <c>StoryAudio:AllowUnauthenticated</c> is explicitly true (dev/bench
    /// opt-in) — same posture as the /metrics and /api/internal guards.</summary>
    private bool IsTokenAccepted(string storyId, string? token)
    {
        var signingKey = _config["StoryAudio:SigningKey"];
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            return StoryAudioToken.TryValidate(token, storyId, signingKey, DateTimeOffset.UtcNow);
        }
        // #006: no signing key configured -> FAIL-CLOSED by default (deny),
        // matching the /metrics and /api/internal guards. An operator opts into
        // open access ONLY by explicitly setting StoryAudio:AllowUnauthenticated
        // = true (dev/bench). This stops an un-configured deploy from silently
        // serving the narration stream to any caller. The bypass is its own
        // flag, not tied to the environment, so a forgotten dev shortcut can't
        // quietly open the stream in prod.
        return bool.TryParse(_config["StoryAudio:AllowUnauthenticated"], out var allow) && allow;
    }

    /// <summary>Whether a re-render is authorized. Requires the configured
    /// operator secret <c>StoryAudio:RefreshToken</c> to be present AND
    /// matched (constant-time). With no secret configured, refresh is
    /// always denied — the public re-render cost vector is closed.</summary>
    private bool IsRefreshAuthorized(string? refreshToken)
    {
        var expected = _config["StoryAudio:RefreshToken"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(refreshToken), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Renders the full verbatim narration (all segments) to one
    /// MP3 at <paramref name="cachePath"/> and writes a sentence-start
    /// byte-offset map to <paramref name="offsetsPath"/>. Splits into
    /// ≤<see cref="MaxChunkChars"/> chunks at Armenian sentence ends and
    /// concatenates the rendered audio (narration flow is unchanged — the
    /// map is derived alongside, it does not change how audio is rendered).</summary>
    private async Task RenderAndCacheAsync(
        CuratedStory story, string cachePath, string offsetsPath, string segmentsPath,
        CancellationToken cancellationToken)
    {
        var chunks = SplitForTts(story.Segments.Select(s => s.Text));
        _logger.LogInformation(
            "Rendering story audio for {StoryId}: {Segments} segments -> {Chunks} TTS chunk(s)",
            story.Id, story.Segments.Count, chunks.Count);

        // OUTPUT MODERATION (#016): the narration AUDIO is what the child hears,
        // so its TEXT must clear the safety classifier BEFORE it is rendered and
        // cached. This is a pre-flight over every chunk, run BEFORE any audio is
        // written, so an unsafe story never produces a (partial) cache file.
        // FAIL-CLOSED: an unsafe chunk OR a moderation outage (CheckContentAsync
        // returns IsSafe=false by contract) aborts the render — the story is not
        // spoken to a child; the device gets the firmware's canned fallback.
        // Cold-render only: a cache hit never reaches here, so streaming a
        // previously-rendered story adds NO per-request moderation cost.
        for (var ci = 0; ci < chunks.Count; ci++)
        {
            var mod = await _moderation.CheckContentAsync(chunks[ci].Text);
            if (!mod.IsSafe)
            {
                _logger.LogError(
                    "Story-audio BLOCKED by output moderation: {StoryId} chunk {Chunk}/{Total} unsafe (categories: {Categories}). Content NOT rendered or served.",
                    story.Id, ci + 1, chunks.Count, string.Join(", ", mod.FlaggedCategories));
                throw new StoryContentBlockedException(story.Id);
            }
        }

        // Sentence-start byte offsets across the whole MP3. 0 is always a
        // boundary. Within each rendered chunk, a sentence's byte position
        // is estimated proportionally from its character position (CBR MP3,
        // so bytes ≈ time ≈ chars within one chunk) — accurate to ~a second,
        // which is all the micro-rewind needs.
        //
        // DELIBERATE TRADE-OFF (do NOT "fix" by rendering per sentence):
        // making these offsets EXACT would require one TTS call per sentence
        // so each sentence's start byte is a real chunk boundary. That would
        // (a) change the narration AUDIO — a sentence synthesized in
        // isolation has different prosody/pacing than within its scene, so
        // the story would sound choppier at every sentence seam, degrading
        // the product's core (storytelling quality), and (b) multiply TTS
        // cost/latency on the cold render. The residual ~1-2 s drift only
        // affects where a post-Q&A resume re-enters, and the design already
        // prefers re-hearing a moment over skipping words (the firmware's
        // AREG_STORY_RESUME_FUDGE_BYTES biases the same way). Keeping the
        // proportional estimate is the correct call; exactness is not worth
        // the audio regression.
        var sentenceOffsets = new List<long> { 0 };
        long byteCursor = 0;

        // Per-chunk (char, byte) spans, captured alongside the render so the
        // segment-start byte map below can be derived from the SAME real
        // byte accumulation — exact at chunk boundaries, proportional within
        // a chunk. The audio bytes are unchanged; this is derived metadata.
        long charCursor = 0;
        var chunkSpans = new List<(long CharStart, long ByteStart, int Chars, int Bytes)>(chunks.Count);

        var tmp = cachePath + ".tmp";
        await using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var chunk in chunks)
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(chunk.Text, cancellationToken);
                var chunkBytes = rendered.Content.Length;
                var chunkChars = chunk.Text.Length;
                foreach (var sentenceStartChar in chunk.SentenceStarts)
                {
                    if (sentenceStartChar <= 0) continue; // chunk start already a boundary
                    var b = byteCursor + (chunkChars > 0
                        ? (long)(chunkBytes * (double)sentenceStartChar / chunkChars)
                        : 0);
                    sentenceOffsets.Add(b);
                }
                chunkSpans.Add((charCursor, byteCursor, chunkChars, chunkBytes));
                await output.WriteAsync(rendered.Content, cancellationToken);
                byteCursor += chunkBytes;
                charCursor += chunkChars;
            }
        }
        // Atomic-ish publish so a half-written file is never served.
        if (System.IO.File.Exists(cachePath))
        {
            System.IO.File.Delete(cachePath);
        }
        System.IO.File.Move(tmp, cachePath);

        var map = sentenceOffsets.Distinct().OrderBy(x => x).ToArray();
        await System.IO.File.WriteAllTextAsync(
            offsetsPath, System.Text.Json.JsonSerializer.Serialize(map), cancellationToken);
        OffsetMaps[story.Id] = map;

        // Segment-start byte map: byte offset where each story segment begins.
        // Char positions match SplitForTts's "\n"-joined text (segment i
        // starts after the prior segments' text + i newline separators).
        var segmentMap = ComputeSegmentByteOffsets(
            story.Segments.Select(s => s.Text).ToList(), chunkSpans, byteCursor);
        await System.IO.File.WriteAllTextAsync(
            segmentsPath, System.Text.Json.JsonSerializer.Serialize(segmentMap), cancellationToken);

        _logger.LogInformation(
            "Story audio cached: {StoryId} -> {Path} ({Bytes} bytes, {Boundaries} sentence boundaries, {Segments} segment boundaries)",
            story.Id, cachePath, new FileInfo(cachePath).Length, map.Length, segmentMap.Length);
    }

    /// <summary>Maps each segment's start character (in the "\n"-joined
    /// narration) to its byte offset in the rendered MP3, using the
    /// per-chunk (char→byte) spans. Exact where a segment starts on a chunk
    /// boundary; proportional within a chunk (same ~1 s precision as the
    /// sentence map). Strictly more accurate than the old uniform
    /// 1/N-of-file assumption, which ignored unequal segment lengths.</summary>
    private static long[] ComputeSegmentByteOffsets(
        IReadOnlyList<string> segmentTexts,
        IReadOnlyList<(long CharStart, long ByteStart, int Chars, int Bytes)> spans,
        long totalBytes)
    {
        var result = new long[segmentTexts.Count];
        long charStart = 0;
        for (var i = 0; i < segmentTexts.Count; i++)
        {
            result[i] = CharToByte(charStart, spans, totalBytes);
            charStart += segmentTexts[i].Length + 1; // +1 = the "\n" join separator
        }
        return result;
    }

    private static long CharToByte(
        long charPos,
        IReadOnlyList<(long CharStart, long ByteStart, int Chars, int Bytes)> spans,
        long totalBytes)
    {
        if (charPos <= 0)
        {
            return 0;
        }
        foreach (var s in spans)
        {
            if (charPos < s.CharStart + s.Chars)
            {
                if (charPos <= s.CharStart)
                {
                    return s.ByteStart; // exact chunk boundary
                }
                return s.ByteStart + (s.Chars > 0
                    ? (long)(s.Bytes * (double)(charPos - s.CharStart) / s.Chars)
                    : 0);
            }
        }
        return totalBytes; // past the last chunk → end of file
    }

    /// <summary>Snaps a requested resume byte offset DOWN to the start of
    /// the sentence the child was hearing. Returns <paramref name="from"/>
    /// unchanged when no offset map is available (graceful fallback).</summary>
    private long SnapToSentenceStart(string storyId, string offsetsPath, long from)
    {
        var map = OffsetMaps.GetOrAdd(storyId, _ => LoadOffsets(offsetsPath));
        return SnapOffset(map, from);
    }

    /// <summary>Pure snap: the start byte of the sentence <paramref name="from"/>
    /// falls inside (the largest boundary ≤ from), or <paramref name="from"/>
    /// unchanged when the map is empty. Internal for direct unit testing —
    /// the resume snap was previously only reachable through the full
    /// render+stream path.</summary>
    internal static long SnapOffset(long[] map, long from)
    {
        if (map.Length == 0)
        {
            return from;
        }
        var i = Array.BinarySearch(map, from);
        if (i >= 0)
        {
            return map[i]; // exact boundary
        }
        // ~i is the index of the first boundary > from; the one before it is
        // the start of the sentence `from` falls inside.
        var prev = ~i - 1;
        return prev >= 0 ? map[prev] : from;
    }

    private static long[] LoadOffsets(string offsetsPath)
    {
        try
        {
            if (!System.IO.File.Exists(offsetsPath)) return [];
            var json = System.IO.File.ReadAllText(offsetsPath);
            return System.Text.Json.JsonSerializer.Deserialize<long[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>A TTS chunk plus the char offsets (within the chunk) where
    /// each sentence begins — used to derive the sentence-offset map.</summary>
    private sealed record ChunkInfo(string Text, IReadOnlyList<int> SentenceStarts);

    /// <summary>Joins the verbatim segments and splits the narration into
    /// TTS-sized chunks at Armenian sentence terminators («։»/«.»), never
    /// mid-sentence, recording where each sentence starts within its chunk.</summary>
    private static List<ChunkInfo> SplitForTts(IEnumerable<string> segments)
    {
        // One flowing narration: segments separated by a newline so TTS
        // takes a natural beat between scenes without a hard pause.
        var full = string.Join("\n", segments);

        var chunks = new List<ChunkInfo>();
        var sb = new StringBuilder();
        var starts = new List<int>();
        foreach (var sentence in SplitSentences(full))
        {
            if (sb.Length > 0 && sb.Length + sentence.Length > MaxChunkChars)
            {
                chunks.Add(new ChunkInfo(sb.ToString(), starts));
                sb.Clear();
                starts = new List<int>();
            }
            starts.Add(sb.Length); // char offset of this sentence within the chunk
            sb.Append(sentence);
        }
        if (sb.Length > 0)
        {
            chunks.Add(new ChunkInfo(sb.ToString(), starts));
        }
        return chunks.Count > 0 ? chunks : [new ChunkInfo(full, [0])];
    }

    /// <summary>Yields sentences WITH their trailing terminator so the
    /// concatenation is loss-less.</summary>
    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '։' or '.' or '!' or '?')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    /// <summary>Thrown by the render path when output moderation rejects a
    /// story's narration text. Caught in <see cref="GetStoryAudio"/> so the
    /// unsafe story is never rendered, cached, or served (#016).</summary>
    private sealed class StoryContentBlockedException : Exception
    {
        public StoryContentBlockedException(string storyId)
            : base($"Story '{storyId}' blocked by output moderation.") { }
    }
}

using System.Text;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;

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
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<StoryAudioController> _logger;

    public StoryAudioController(
        IAudioSynthesisService synthesis,
        ICuratedStoryLibrary library,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<StoryAudioController> logger)
    {
        _synthesis = synthesis;
        _library = library;
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
        CancellationToken cancellationToken = default)
    {
        var story = _library.GetById(storyId);
        if (story is null)
        {
            return NotFound(new { error = "Unknown story." });
        }

        var cachePath = CachePathFor(storyId);
        var offsetsPath = OffsetsPathFor(cachePath);
        if (refresh)
        {
            if (System.IO.File.Exists(cachePath)) System.IO.File.Delete(cachePath);
            if (System.IO.File.Exists(offsetsPath)) System.IO.File.Delete(offsetsPath);
            OffsetMaps.TryRemove(storyId, out _);
        }

        // Render when the audio OR its sentence-offset sidecar is missing,
        // so a cache from before the micro-rewind feature regenerates both.
        if (!System.IO.File.Exists(cachePath) || !System.IO.File.Exists(offsetsPath))
        {
            await RenderLock.WaitAsync(cancellationToken);
            try
            {
                if (!System.IO.File.Exists(cachePath) || !System.IO.File.Exists(offsetsPath))
                {
                    await RenderAndCacheAsync(story, cachePath, offsetsPath, cancellationToken);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Story-audio render failed for {StoryId}", storyId);
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

    /// <summary>Renders the full verbatim narration (all segments) to one
    /// MP3 at <paramref name="cachePath"/> and writes a sentence-start
    /// byte-offset map to <paramref name="offsetsPath"/>. Splits into
    /// ≤<see cref="MaxChunkChars"/> chunks at Armenian sentence ends and
    /// concatenates the rendered audio (narration flow is unchanged — the
    /// map is derived alongside, it does not change how audio is rendered).</summary>
    private async Task RenderAndCacheAsync(
        CuratedStory story, string cachePath, string offsetsPath, CancellationToken cancellationToken)
    {
        var chunks = SplitForTts(story.Segments.Select(s => s.Text));
        _logger.LogInformation(
            "Rendering story audio for {StoryId}: {Segments} segments -> {Chunks} TTS chunk(s)",
            story.Id, story.Segments.Count, chunks.Count);

        // Sentence-start byte offsets across the whole MP3. 0 is always a
        // boundary. Within each rendered chunk, a sentence's byte position
        // is estimated proportionally from its character position (CBR MP3,
        // so bytes ≈ time ≈ chars within one chunk) — accurate to ~a second,
        // which is all the micro-rewind needs.
        var sentenceOffsets = new List<long> { 0 };
        long byteCursor = 0;

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
                await output.WriteAsync(rendered.Content, cancellationToken);
                byteCursor += chunkBytes;
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

        _logger.LogInformation(
            "Story audio cached: {StoryId} -> {Path} ({Bytes} bytes, {Boundaries} sentence boundaries)",
            story.Id, cachePath, new FileInfo(cachePath).Length, map.Length);
    }

    /// <summary>Snaps a requested resume byte offset DOWN to the start of
    /// the sentence the child was hearing. Returns <paramref name="from"/>
    /// unchanged when no offset map is available (graceful fallback).</summary>
    private long SnapToSentenceStart(string storyId, string offsetsPath, long from)
    {
        var map = OffsetMaps.GetOrAdd(storyId, _ => LoadOffsets(offsetsPath));
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
}

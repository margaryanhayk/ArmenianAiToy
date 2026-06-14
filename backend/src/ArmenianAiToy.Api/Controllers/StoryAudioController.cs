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
        if (refresh && System.IO.File.Exists(cachePath))
        {
            System.IO.File.Delete(cachePath);
        }

        if (!System.IO.File.Exists(cachePath))
        {
            await RenderLock.WaitAsync(cancellationToken);
            try
            {
                if (!System.IO.File.Exists(cachePath))
                {
                    await RenderAndCacheAsync(story, cachePath, cancellationToken);
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
            var stream = new FileStream(
                cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Seek(from, SeekOrigin.Begin);
            return File(stream, "audio/mpeg"); // streams [from, end)
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

    /// <summary>Renders the full verbatim narration (all segments) to one
    /// MP3 and writes it to <paramref name="cachePath"/>. Splits into
    /// ≤<see cref="MaxChunkChars"/> chunks at Armenian sentence ends and
    /// concatenates the rendered audio.</summary>
    private async Task RenderAndCacheAsync(
        CuratedStory story, string cachePath, CancellationToken cancellationToken)
    {
        var chunks = SplitForTts(story.Segments.Select(s => s.Text));
        _logger.LogInformation(
            "Rendering story audio for {StoryId}: {Segments} segments -> {Chunks} TTS chunk(s)",
            story.Id, story.Segments.Count, chunks.Count);

        var tmp = cachePath + ".tmp";
        await using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var chunk in chunks)
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(chunk, cancellationToken);
                await output.WriteAsync(rendered.Content, cancellationToken);
            }
        }
        // Atomic-ish publish so a half-written file is never served.
        if (System.IO.File.Exists(cachePath))
        {
            System.IO.File.Delete(cachePath);
        }
        System.IO.File.Move(tmp, cachePath);

        _logger.LogInformation(
            "Story audio cached: {StoryId} -> {Path} ({Bytes} bytes)",
            story.Id, cachePath, new FileInfo(cachePath).Length);
    }

    /// <summary>Joins the verbatim segments and splits the narration into
    /// TTS-sized chunks at Armenian sentence terminators («։»/«.»), never
    /// mid-sentence.</summary>
    private static List<string> SplitForTts(IEnumerable<string> segments)
    {
        // One flowing narration: segments separated by a newline so TTS
        // takes a natural beat between scenes without a hard pause.
        var full = string.Join("\n", segments);

        var chunks = new List<string>();
        var sb = new StringBuilder();
        foreach (var sentence in SplitSentences(full))
        {
            if (sb.Length > 0 && sb.Length + sentence.Length > MaxChunkChars)
            {
                chunks.Add(sb.ToString());
                sb.Clear();
            }
            sb.Append(sentence);
        }
        if (sb.Length > 0)
        {
            chunks.Add(sb.ToString());
        }
        return chunks.Count > 0 ? chunks : [full];
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

// ---------------------------------------------------------------------
// ElevenLabsRender — render story narration + clips in the storyteller
// voice (the owner's ElevenLabs voice clone).
//
// WHY THIS EXISTS. The shipped narration was rendered OUTSIDE the repo;
// there was no committed re-render pipeline, so "make the stories a bit
// slower" (owner request, 2026-08-03) had no tool. This is that tool. It
// also renders the B2 clips (spoken intro / reflection question /
// after-story summary) from the story metadata so the reflection pack
// can ship in the SAME voice as the narration.
//
// PAID-API DISCIPLINE (same contract as TtsListenTest): DRY-RUN by
// default. Nothing is sent to ElevenLabs unless BOTH --render AND
// --confirm-paid-api are passed. Dry-run prints exactly what would be
// rendered (texts, char counts, speed) so the owner can review the
// child-facing text BEFORE paying for it — and before the mandatory
// human listen test that gates shipping any rendered asset.
//
// USAGE
//   dotnet run --project tools/ElevenLabsRender -- --story anban-huri --speed 0.9
//   dotnet run --project tools/ElevenLabsRender -- --story anban-huri --speed 0.9 --render --confirm-paid-api
//   dotnet run --project tools/ElevenLabsRender -- --all --clips --render --confirm-paid-api
//
// OPTIONS
//   --story <id>        one story (repeatable) | --all = every embedded story
//   --clips             render the intro/question/summary clips (default: narration)
//   --speed <x>         voice_settings.speed, ElevenLabs range 0.7–1.2 (default 1.0)
//   --model <id>        default eleven_multilingual_v2
//   --output <dir>      default %TEMP%/areg-elevenlabs-render
//   --render            actually call the API (with --confirm-paid-api)
//   --confirm-paid-api  second key of the two-man rule
//
// CREDENTIALS (never in the repo)
//   ELEVENLABS_API_KEY   — the account key
//   ELEVENLABS_VOICE_ID  — the storyteller clone's voice id
//
// OUTPUT
//   <id>--narration--s<speed>.mp3 / <id>--<kind>.mp3, plus
//   manifest-snippet.json with sha256 + sizeBytes per file — paste-ready
//   for the ContentSync config (remember to BUMP each story's Version,
//   or devices will keep their cached copy).
// ---------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArmenianAiToy.Application.Stories;

var storyIds = new List<string>();
var all = false;
var clips = false;
var render = false;
var confirmPaid = false;
double speed = 1.0;
var model = "eleven_multilingual_v2";
var output = Path.Combine(Path.GetTempPath(), "areg-elevenlabs-render");
// Small enough that no single request is anywhere near a length the API
// might refuse or curtail. The five stories cut on 2026-08-03 all stopped
// somewhere past 1,100 characters in a single request.
var maxChunkChars = 700;
// A render shorter than this fraction of the expected duration is treated as
// truncated and refused. Generous on purpose — it is there to catch a story
// that stops in the middle, not to argue about a brisk delivery.
const double ShortRenderFloor = 0.70;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--story": storyIds.Add(args[++i]); break;
        case "--all": all = true; break;
        case "--clips": clips = true; break;
        case "--render": render = true; break;
        case "--confirm-paid-api": confirmPaid = true; break;
        case "--speed": speed = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--model": model = args[++i]; break;
        case "--max-chunk": maxChunkChars = int.Parse(args[++i]); break;
        case "--output": output = args[++i]; break;
        default:
            Console.Error.WriteLine($"Unknown option: {args[i]}");
            return 2;
    }
}

if (speed is < 0.7 or > 1.2)
{
    Console.Error.WriteLine("--speed must be within ElevenLabs' supported 0.7–1.2 range.");
    return 2;
}

var library = new EmbeddedCuratedStoryLibrary(
    typeof(EmbeddedCuratedStoryLibrary).Assembly,
    "ArmenianAiToy.Application.Stories.Content");
var stories = all
    ? library.ListAvailable().ToList()
    : storyIds.Select(id => library.GetById(id)
        ?? throw new InvalidOperationException($"Unknown story id '{id}'")).ToList();

if (stories.Count == 0)
{
    Console.Error.WriteLine("Nothing to render — pass --story <id> (repeatable) or --all.");
    return 2;
}

// One render job = one output MP3, rendered as one or more CHUNKS.
//
// Chunking exists because of what happened on 2026-08-03: the shipped
// narration was rendered outside this tool, in one request per story, and
// five of the eight came back cut — anban-huri 3:52 of text arrived as 1:27
// of audio. Nothing checked, so truncated stories shipped and children heard
// them stop partway. Splitting the text means no single request is anywhere
// near a length the API might refuse or curtail, and the length check below
// means a short result can never be written out again.
var jobs = new List<(string FileName, string Label, List<string> Chunks)>();
foreach (var story in stories)
{
    if (!clips)
    {
        // Narration: the segments verbatim — the same text the reviewed
        // story file carries. Speed is in the filename so a two-speed
        // comparison render can't mix files up.
        var segments = story.Segments.Select(s => s.Text).ToList();
        jobs.Add((
            $"{story.Id}--narration--s{speed:0.0#}.mp3",
            $"{story.Id} narration",
            BuildChunks(segments, maxChunkChars)));
        continue;
    }

    // Clips. Intro composes title + author («Հեքիաթ՝ …։ Հեղինակ՝ …։»);
    // a story with no verified author gets the title-only intro — never
    // a guessed attribution. Summary prefers the B1 `lesson` text and
    // falls back to the reflectionText the stories always had.
    var intro = story.Author is null
        ? $"Հեքիաթ՝ «{story.Title}»։"
        : $"Հեքիաթ՝ «{story.Title}»։ Հեղինակ՝ {story.Author}։";
    jobs.Add(($"{story.Id}--intro.mp3", $"{story.Id} intro", [intro]));
    if (story.ReflectionQuestions.Count > 0)
    {
        jobs.Add(($"{story.Id}--question.mp3", $"{story.Id} question",
            [story.ReflectionQuestions[0]]));
    }
    var summary = story.Lesson ?? story.ReflectionText;
    if (!string.IsNullOrWhiteSpace(summary))
    {
        jobs.Add(($"{story.Id}--summary.mp3", $"{story.Id} summary", [summary]));
    }
}

var totalChars = jobs.Sum(j => j.Chunks.Sum(c => c.Length));
var totalRequests = jobs.Sum(j => j.Chunks.Count);
Console.WriteLine($"Plan: {jobs.Count} file(s) in {totalRequests} request(s), {totalChars:N0} characters, speed={speed:0.0#}, model={model}");
Console.WriteLine($"Output: {output}");
Console.WriteLine();
foreach (var job in jobs)
{
    var chars = job.Chunks.Sum(c => c.Length);
    Console.WriteLine(
        $"  {job.FileName,-42} {chars,6:N0} chars in {job.Chunks.Count} chunk(s), expect ~{FormatDuration(ExpectedSeconds(chars))}");
    if (clips)
    {
        // Clip texts are short child-facing lines — print them in full so
        // the dry run doubles as the pre-render text review.
        Console.WriteLine($"      «{job.Chunks[0]}»");
    }
}
Console.WriteLine();

if (!render || !confirmPaid)
{
    Console.WriteLine("DRY RUN — nothing was sent to ElevenLabs.");
    Console.WriteLine("To render for real (PAID API): add --render --confirm-paid-api");
    Console.WriteLine("Reminder: every rendered asset needs a human listen test before it ships.");
    return 0;
}

var apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
var voiceId = Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID");
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(voiceId))
{
    Console.Error.WriteLine("Set ELEVENLABS_API_KEY and ELEVENLABS_VOICE_ID (the storyteller clone).");
    return 2;
}

Directory.CreateDirectory(output);
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.Add("xi-api-key", apiKey);

var manifest = new List<object>();
var shortRenders = new List<string>();
foreach (var job in jobs)
{
    var jobChars = job.Chunks.Sum(c => c.Length);
    Console.WriteLine(
        $"Rendering {job.Label} ({jobChars:N0} chars, {job.Chunks.Count} chunk(s))...");

    var pieces = new List<byte[]>();
    for (var c = 0; c < job.Chunks.Count; c++)
    {
        var body = JsonSerializer.Serialize(new
        {
            text = job.Chunks[c],
            model_id = model,
            // Neighbouring text is sent as CONTEXT, not as speech: it is what
            // keeps the voice's pace and intonation continuous across a split
            // so a chunked story does not sound like separate takes stitched
            // together.
            previous_text = c > 0 ? job.Chunks[c - 1] : null,
            next_text = c + 1 < job.Chunks.Count ? job.Chunks[c + 1] : null,
            voice_settings = new
            {
                // Stability/similarity stay at the voice's own defaults (the
                // clone was tuned when it was created); only speed is set.
                speed,
            },
        });
        using var response = await http.PostAsync(
            $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}",
            new StringContent(body, Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            // Never print the response body wholesale (it can echo request
            // details) — status + a bounded prefix is enough to diagnose.
            var detail = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine(
                $"  FAILED chunk {c + 1}/{job.Chunks.Count}: HTTP {(int)response.StatusCode} {detail[..Math.Min(detail.Length, 200)]}");
            return 1;
        }
        var piece = await response.Content.ReadAsByteArrayAsync();
        pieces.Add(piece);
        Console.WriteLine(
            $"    chunk {c + 1}/{job.Chunks.Count} {job.Chunks[c].Length,5:N0} chars -> {piece.LongLength,9:N0} B  {FormatDuration(Mp3Duration.Seconds(piece))}");
    }

    // Frames are self-contained, but the WRAPPERS around them are not: every
    // ElevenLabs response opens with its own ID3v2 tag and its own Xing/"Info"
    // header frame, and that header states the length of THAT CHUNK. Glued
    // end to end (what this line used to do), a strict player reads the first
    // header, believes a 4-minute story is 40 seconds long, and stops at the
    // first chunk boundary — which is exactly what shipped on 2026-08-04 and
    // what a parent heard as the voice being cut. Mp3Stitch drops the tags and
    // the per-chunk headers, leaving one continuous constant-bitrate stream.
    // Audio frames are copied untouched; nothing is re-encoded.
    var bytes = Mp3Stitch.Join(pieces);
    var seconds = Mp3Duration.Seconds(bytes);
    var expected = ExpectedSeconds(jobChars);

    var path = Path.Combine(output, job.FileName);
    await File.WriteAllBytesAsync(path, bytes);
    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    manifest.Add(new
    {
        file = job.FileName,
        sha256 = sha,
        sizeBytes = bytes.LongLength,
        seconds = Math.Round(seconds, 1),
        chars = jobChars,
    });
    Console.WriteLine(
        $"  OK {bytes.LongLength:N0} B  {FormatDuration(seconds)} (expected ~{FormatDuration(expected)})  sha256={sha}");

    // The check that did not exist on 2026-08-03. A render that is far
    // shorter than its text is a truncated story, and a truncated story is
    // one a child hears stop in the middle — so it is called out here, by
    // name, rather than quietly becoming a manifest line.
    if (expected > 0 && seconds < expected * ShortRenderFloor)
    {
        var pct = (int)Math.Round(100 * seconds / expected);
        Console.Error.WriteLine(
            $"  *** TOO SHORT: {FormatDuration(seconds)} is only {pct}% of the ~{FormatDuration(expected)} this text needs. Do NOT ship this file.");
        shortRenders.Add($"{job.FileName} ({pct}%)");
    }
}

var snippetPath = Path.Combine(output, "manifest-snippet.json");
await File.WriteAllTextAsync(snippetPath,
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine();
Console.WriteLine($"Wrote {snippetPath} — paste sha256/sizeBytes into ContentSync config");
Console.WriteLine("and BUMP each story's Version so devices re-download.");
Console.WriteLine("Next gate: human listen test (tools/quality-evidence conventions).");

if (shortRenders.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"REFUSING to call this a good render: {shortRenders.Count} file(s) came back short —");
    foreach (var s in shortRenders) Console.Error.WriteLine($"  {s}");
    Console.Error.WriteLine("Re-run those stories. Shipping them means a child hears the story stop.");
    return 1;
}
return 0;

// Roughly how long this much Armenian narration should take in this voice.
// Calibrated against the renders that came back complete (14.7–15.7 chars per
// second) and against the original full anban-huri render (14.2). Only used
// to spot a render that is grossly short — never to reject a merely brisk one.
static double ExpectedSeconds(int chars) => chars / 15.0;

static string FormatDuration(double seconds) =>
    $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

// Split a story's segments into request-sized chunks, preferring segment
// boundaries (a segment is already a natural pause in the telling) and
// falling back to sentence boundaries for any segment that is too long on
// its own.
static List<string> BuildChunks(List<string> segments, int maxChars)
{
    var chunks = new List<string>();
    var current = new StringBuilder();

    void Flush()
    {
        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
            current.Clear();
        }
    }

    foreach (var segment in segments)
    {
        var text = segment.Trim();
        if (text.Length == 0) continue;

        foreach (var part in SplitToSentences(text, maxChars))
        {
            if (current.Length > 0 && current.Length + part.Length + 2 > maxChars) Flush();
            if (current.Length > 0) current.Append("\n\n");
            current.Append(part);
        }
    }
    Flush();
    return chunks;
}

// Armenian ends sentences with ։ (U+0589); the drafts also contain ASCII
// periods, question and exclamation marks. Split after any of them.
static List<string> SplitToSentences(string text, int maxChars)
{
    if (text.Length <= maxChars) return [text];
    var parts = new List<string>();
    var start = 0;
    var lastBreak = -1;
    for (var i = 0; i < text.Length; i++)
    {
        var ch = text[i];
        if (ch is '։' or '.' or '?' or '!' or '՞' or '՜') lastBreak = i;
        if (i - start + 1 >= maxChars)
        {
            var cut = lastBreak > start ? lastBreak + 1 : i + 1;
            parts.Add(text[start..cut].Trim());
            start = cut;
            lastBreak = -1;
        }
    }
    if (start < text.Length) parts.Add(text[start..].Trim());
    return parts.Where(p => p.Length > 0).ToList();
}

/// <summary>
/// Joins the per-chunk MP3 responses of one render into a single stream that
/// every player reaches the end of.
///
/// A naive concatenation keeps each chunk's ID3v2 tag and its Xing/"Info"
/// header frame in the middle of the file. iOS Safari — and any decoder that
/// trusts the first header it sees — then plays only the first chunk. Dropping
/// both leaves a bare constant-bitrate frame stream whose duration a player
/// derives from the bitrate, which is exact for CBR. Audio frames are copied
/// byte for byte: this is a container repair, not a re-encode.
/// </summary>
internal static class Mp3Stitch
{
    public static byte[] Join(IEnumerable<byte[]> pieces)
    {
        var outp = new List<byte>();
        foreach (var piece in pieces)
        {
            var i = 0;
            while (i < piece.Length)
            {
                var tag = Id3Length(piece, i);
                if (tag > 0) { i += tag; continue; }

                var frame = Mp3Duration.FrameLength(piece, i);
                if (frame > 0)
                {
                    if (!IsXingHeaderFrame(piece, i, frame)) outp.AddRange(piece[i..(i + frame)]);
                    i += frame;
                    continue;
                }
                i++;   // stray byte between frames; drop it
            }
        }
        return outp.ToArray();
    }

    private static int Id3Length(byte[] b, int i)
    {
        if (i + 10 > b.Length || b[i] != 'I' || b[i + 1] != 'D' || b[i + 2] != '3') return 0;
        if (b[i + 3] == 0xFF || b[i + 4] == 0xFF) return 0;
        // The four size bytes are syncsafe — the high bit is always clear.
        if (((b[i + 6] | b[i + 7] | b[i + 8] | b[i + 9]) & 0x80) != 0) return 0;
        var size = ((b[i + 6] & 0x7f) << 21) | ((b[i + 7] & 0x7f) << 14)
                   | ((b[i + 8] & 0x7f) << 7) | (b[i + 9] & 0x7f);
        var footer = (b[i + 5] & 0x10) != 0 ? 10 : 0;
        return 10 + size + footer;
    }

    private static bool IsXingHeaderFrame(byte[] b, int start, int frameLength)
    {
        // "Xing" (VBR) or "Info" (CBR) sits just past the side info, whose
        // size depends on the MPEG version and channel mode. Scanning the
        // frame head covers every layout without decoding the side info.
        var end = Math.Min(start + Math.Min(frameLength, 64), b.Length) - 4;
        for (var i = start + 4; i <= end; i++)
        {
            if (b[i] == 'X' && b[i + 1] == 'i' && b[i + 2] == 'n' && b[i + 3] == 'g') return true;
            if (b[i] == 'I' && b[i + 1] == 'n' && b[i + 2] == 'f' && b[i + 3] == 'o') return true;
        }
        return false;
    }
}

/// <summary>
/// Duration of an MPEG Layer III stream, by walking its frame headers.
/// Deliberately dependency-free: the only question it has to answer is
/// "is this audio roughly as long as the text implies", and a truncated
/// story is never a close call.
/// </summary>
internal static class Mp3Duration
{
    private static readonly int[] V1L3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
    private static readonly int[] V2L3 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
    private static readonly int[][] Rates =
    [
        [44100, 48000, 32000],   // MPEG 1
        [22050, 24000, 16000],   // MPEG 2
        [11025, 12000, 8000],    // MPEG 2.5
    ];

    public static double Seconds(byte[] data)
    {
        var i = 0;
        if (data.Length > 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
        {
            i = 10 + (((data[6] & 0x7f) << 21) | ((data[7] & 0x7f) << 14)
                      | ((data[8] & 0x7f) << 7) | (data[9] & 0x7f));
        }
        var total = 0.0;
        while (i < data.Length - 4)
        {
            var frameLength = FrameLength(data, i);
            if (frameLength <= 0) { i++; continue; }
            total += FrameSeconds(data, i);
            i += frameLength;
        }
        return total;
    }

    /// <summary>
    /// Byte length of the MPEG Layer III frame starting at <paramref name="i"/>,
    /// or 0 when there is no valid frame there. Shared with Mp3Stitch so the
    /// joiner and the duration check agree on what a frame is.
    /// </summary>
    public static int FrameLength(byte[] data, int i)
    {
        if (i + 4 > data.Length) return 0;
        if (data[i] != 0xFF || (data[i + 1] & 0xE0) != 0xE0) return 0;
        var ver = (data[i + 1] >> 3) & 0x03;     // 3 = MPEG1, 2 = MPEG2, 0 = MPEG2.5
        var layer = (data[i + 1] >> 1) & 0x03;   // 1 = Layer III
        var bIdx = (data[i + 2] >> 4) & 0x0F;
        var sIdx = (data[i + 2] >> 2) & 0x03;
        var pad = (data[i + 2] >> 1) & 0x01;
        if (layer != 1 || bIdx is 0 or 15 || sIdx == 3 || ver == 1) return 0;

        var (bitrate, rate, samples) = Layout(ver, bIdx, sIdx);
        if (bitrate == 0 || rate == 0) return 0;

        var frameLength = (samples / 8 * bitrate / rate) + pad;
        return frameLength > 4 && i + frameLength <= data.Length ? frameLength : 0;
    }

    private static double FrameSeconds(byte[] data, int i)
    {
        var ver = (data[i + 1] >> 3) & 0x03;
        var bIdx = (data[i + 2] >> 4) & 0x0F;
        var sIdx = (data[i + 2] >> 2) & 0x03;
        var (_, rate, samples) = Layout(ver, bIdx, sIdx);
        return rate == 0 ? 0 : (double)samples / rate;
    }

    private static (int Bitrate, int Rate, int Samples) Layout(int ver, int bIdx, int sIdx) => ver switch
    {
        3 => (V1L3[bIdx] * 1000, Rates[0][sIdx], 1152),
        2 => (V2L3[bIdx] * 1000, Rates[1][sIdx], 576),
        _ => (V2L3[bIdx] * 1000, Rates[2][sIdx], 576),
    };
}

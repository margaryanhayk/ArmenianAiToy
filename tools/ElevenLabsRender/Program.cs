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
//   dotnet run --project tools/ElevenLabsRender -- --voice-clips --render --confirm-paid-api
//
// OPTIONS
//   --story <id>        one story (repeatable) | --all = every embedded story
//   --clips             render a story's intro/question/summary AND the two
//                       welcome-flow offer lines (default: narration)
//   --voice-clips       render the DEVICE-GLOBAL welcome-flow lines (greetings,
//                       menu prompts, fallbacks) from
//                       backend/content/voice-clips/voice-clips.json. Needs no
//                       --story: these belong to no story. One file per clip id.
//   --only <name>       repeatable; render only these jobs, by output filename
//                       without .mp3 (greet-01, anban-huri--offer, ...). SAMPLE
//                       BEFORE YOU BATCH: the narrator voice is still interim,
//                       so a full render is thrown away when it changes.
//   --per-segment       narration only: one request per STORY SEGMENT, output
//                       named <storyId>.mp3 (ship-ready — no rename before
//                       Ship-StoryAudio.ps1), plus <storyId>.segments.json and
//                       the individual pieces under segments/. USE THIS FOR
//                       THE CLONE: it is what makes truncation impossible.
//   --self-test         check the segment-map arithmetic against a directory
//                       of MP3s (--output <dir>). No API key, sends nothing.
//   --speed <x>         voice_settings.speed, ElevenLabs range 0.7–1.2 (default 1.0)
//   --model <id>        default eleven_v3 (the only model on this account
//                       that speaks Armenian — see the note by the variable)
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
//
// LEVEL THE OUTPUT BEFORE SHIPPING IT (2026-08-04)
//   What comes back from the API sits near -27 LUFS. The renders the owner
//   approved sit near -16.4 LUFS. That is ~11 dB, and on a phone speaker or
//   the toy's small speaker the quiet version reads as thin and far away —
//   the owner heard it as the voice quality being ruined. Levelling is not
//   done in this tool (it has no audio dependency and stays that way), so it
//   is a manual step per file, two-pass so the gain is measured not guessed:
//
//     ffmpeg -i in.mp3 -af loudnorm=I=-16.4:TP=-1.0:LRA=11:print_format=json -f null -
//     ffmpeg -i in.mp3 -af "loudnorm=I=-16.4:TP=-1.0:LRA=11:measured_I=<input_i>:\
//       measured_TP=<input_tp>:measured_LRA=<input_lra>:measured_thresh=<input_thresh>:\
//       offset=<target_offset>:linear=true" -ar 44100 -ac 1 -c:a libmp3lame -b:a 192k out.mp3
//
//   192 kbps against a 128 kbps source keeps the extra MP3 generation cheap.
//   Re-hash and re-measure AFTER levelling — the manifest describes the file
//   that ships, not the one the API returned.
// ---------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArmenianAiToy.Application.Stories;

var storyIds = new List<string>();
var all = false;
var clips = false;
var voiceClips = false;
var only = new List<string>();
var render = false;
var confirmPaid = false;
double speed = 1.0;
// eleven_v3 is the ONLY model on this account that lists Armenian (hy).
// GET /v1/models, 2026-08-04: multilingual_v2, flash_v2_5, turbo_v2_5,
// turbo_v2 and flash_v2 all say no. Rendering Armenian through a model that
// does not know the language is what produced the narration the owner
// rejected on 2026-08-04 — it is not a voice or a clone problem, and no
// amount of levelling or restitching can rescue it. Do not change this
// default without checking the language list again.
var model = "eleven_v3";
var output = Path.Combine(Path.GetTempPath(), "areg-elevenlabs-render");
// eleven_v3 accepts 5,000 characters per request, and every seam is a place
// the delivery can jump — so chunk as LITTLE as the limit allows rather than
// as much as possible. At 4,000 most stories render in a single request.
//
// CORRECTED 2026-08-11. The line that used to sit here — "the old 700 came
// from a truncation that was really a wrong-model problem" — was wrong, and
// raising the default on the strength of it is what put five truncated stories
// on children's toys. The model matters (only v3 speaks Armenian) AND the
// request size matters: this account's clone stops at ~1,300 characters of
// output whatever it is sent. 4,000 is safe for a Default voice and unsafe for
// a clone, which is not something a single default can express — so prefer
// --per-segment for narration and treat this number as the fallback it is.
var maxChunkChars = 4000;
// One request per STORY SEGMENT, instead of packing segments up to
// maxChunkChars. Measured 2026-08-11 against the owner's own clone: it stops
// at roughly 1,300 characters of OUTPUT however long the input is. Every
// shipped story under that ceiling (967-1,222 chars) rendered complete; every
// one above it (1,616-4,753) came back at exactly ~1,300 characters' worth of
// audio and shipped truncated. ElevenLabs say Professional Voice Clones are
// not fully optimised for eleven_v3, which is why a Default voice rendered the
// same 4,753-character story to 114% on the same day and the same tool.
//
// The longest single segment in the library is 835 characters, so segment-
// sized requests cannot reach the ceiling — truncation becomes arithmetically
// impossible rather than merely unlikely. Three things fall out for free:
// seams land on paragraph breaks (where a narrator pauses anyway, and v3
// refuses previous_text/next_text so every seam is blind), a fluffed line
// costs one request instead of a story, and the per-segment durations give the
// `<id>.segments.json` map this repo has never had — the one the backend wants
// for in-story questions and mix_ambience.py wants for cue placement.
var perSegment = false;
// --self-test <dir>: stitch the MP3s in a directory the way a real render
// does and check the segment map against them. It exists because the segment
// map's whole value is that a start time is EXACT, and "the frames are copied
// untouched so the durations add up" is an assertion until something adds
// them up. Needs no API key and sends nothing.
var selfTest = false;
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
        case "--voice-clips": voiceClips = true; break;
        case "--only": only.Add(args[++i]); break;
        case "--render": render = true; break;
        case "--confirm-paid-api": confirmPaid = true; break;
        case "--speed": speed = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--model": model = args[++i]; break;
        case "--max-chunk": maxChunkChars = int.Parse(args[++i]); break;
        case "--per-segment": perSegment = true; break;
        case "--self-test": selfTest = true; break;
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

if (selfTest)
{
    return SegmentMapSelfTest.Run(output);
}

var library = new EmbeddedCuratedStoryLibrary(
    typeof(EmbeddedCuratedStoryLibrary).Assembly,
    "ArmenianAiToy.Application.Stories.Content");
var stories = all
    ? library.ListAvailable().ToList()
    : storyIds.Select(id => library.GetById(id)
        ?? throw new InvalidOperationException($"Unknown story id '{id}'")).ToList();

// --voice-clips renders the DEVICE-GLOBAL welcome-flow lines (greetings,
// menu prompts, the two fallback lines). They belong to no story, so this
// mode does not need --story / --all.
if (stories.Count == 0 && !voiceClips)
{
    Console.Error.WriteLine("Nothing to render — pass --story <id> (repeatable) or --all.");
    return 2;
}

// The reviewable Armenian source for the welcome flow. Content, not a build
// artifact, so it is found by walking up to the repo root rather than copied
// to the output directory.
VoiceClipFile? voiceClipFile = null;
if (voiceClips || clips)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(
               dir.FullName, "backend", "content", "voice-clips", "voice-clips.json")))
    {
        dir = dir.Parent;
    }
    if (dir is null)
    {
        Console.Error.WriteLine("Could not find backend/content/voice-clips/voice-clips.json.");
        return 2;
    }
    voiceClipFile = JsonSerializer.Deserialize<VoiceClipFile>(
        File.ReadAllText(Path.Combine(
            dir.FullName, "backend", "content", "voice-clips", "voice-clips.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (voiceClipFile?.Clips is null || voiceClipFile.Templates is null)
    {
        Console.Error.WriteLine("voice-clips.json is missing its clips or _perStoryTemplates.");
        return 2;
    }
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
// Output file -> story id, for the narration jobs whose chunks ARE the story's
// segments one for one. Only those can produce an honest segment map, so this
// is what decides whether one is written rather than a flag read later.
var segmentMapFor = new Dictionary<string, string>(StringComparer.Ordinal);

// Welcome flow — the device-global lines. One file per clip id, named
// <voiceId>.mp3 so Ship-StoryAudio.ps1 picks them up by id like everything
// else. Each is one short sentence, so never more than one chunk.
if (voiceClips)
{
    foreach (var clip in voiceClipFile!.Clips!)
    {
        if (string.IsNullOrWhiteSpace(clip.VoiceId) || string.IsNullOrWhiteSpace(clip.Text))
        {
            Console.Error.WriteLine($"Skipping a voice clip with no id or no text.");
            continue;
        }
        jobs.Add(($"{clip.VoiceId}.mp3", clip.VoiceId, [clip.Text]));
    }
}

foreach (var story in stories)
{
    if (voiceClips && !clips)
    {
        continue;   // --voice-clips alone renders only the device-global set
    }
    if (!clips)
    {
        // Narration: the segments verbatim — the same text the reviewed
        // story file carries. Speed is in the filename so a two-speed
        // comparison render can't mix files up.
        var segments = story.Segments.Select(s => s.Text).ToList();
        if (perSegment)
        {
            // Ship-ready name. Ship-StoryAudio.ps1 -In <dir> matches
            // `<storyId>.mp3`, so the rename step that sat undocumented
            // between the two tools disappears — and a rename is exactly the
            // kind of manual step that gets skipped on the day it matters.
            // Speed still appears when it is not 1.0, so a comparison render
            // can never overwrite a ship candidate.
            var shipName = Math.Abs(speed - 1.0) < 0.001
                ? $"{story.Id}.mp3"
                : $"{story.Id}--s{speed:0.0#}.mp3";
            jobs.Add((shipName, $"{story.Id} narration (per segment)", segments));
            segmentMapFor[shipName] = story.Id;
            continue;
        }
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
    // Three shapes, not two. A story with an author names them; a story
    // nobody wrote names its ORIGIN instead of stopping after the title —
    // the owner's 2026-09-03 listen test heard that silence on «Ուլիկը» and
    // «Երեք խոզուկները» as unfinished, and he is right that a child is owed
    // the same "where is this from" the other eight stories get. The origin
    // leads the sentence rather than trailing it, because «Հեքիաթ՝ «X»։
    // Ժողովրդական հեքիաթ։» says «հեքիաթ» twice in two short breaths.
    // An in-project original still gets the title alone: it is neither
    // authored-by-a-name nor folk, and claiming either would be false.
    var intro = (story.Author, story.Origin) switch
    {
        (not null, _) => $"Հեքիաթ՝ «{story.Title}»։ Հեղինակ՝ {story.Author}։",
        (null, not null) => $"{story.Origin}՝ «{story.Title}»։",
        _ => $"Հեքիաթ՝ «{story.Title}»։",
    };
    jobs.Add(($"{story.Id}--intro.mp3", $"{story.Id} intro", [intro]));
    // ALL the reflection questions, not just the first. Every story has
    // carried three since 2026-08-03 and this loop only ever rendered index 0,
    // so `question1` and `question2` — kinds that exist on the backend
    // (ContentSyncClipOptions) and in the firmware (cs_question_clip_kind) —
    // had no audio that could ever fill them. The kind names must stay in
    // lockstep with cs_question_clip_kind(): 0 -> question, 1 -> question1,
    // 2 -> question2. The toy resolves clips BY KIND, so a mismatch here is a
    // clip that silently never plays.
    for (var qi = 0; qi < story.ReflectionQuestions.Count; qi++)
    {
        var kind = qi == 0 ? "question" : $"question{qi}";
        jobs.Add(($"{story.Id}--{kind}.mp3", $"{story.Id} {kind}",
            [story.ReflectionQuestions[qi]]));
    }
    var summary = story.Lesson ?? story.ReflectionText;
    if (!string.IsNullOrWhiteSpace(summary))
    {
        jobs.Add(($"{story.Id}--summary.mp3", $"{story.Id} summary", [summary]));
    }

    // Welcome flow — the spoken offers, which are what let the toy name a
    // story out loud with no runtime TTS. The title goes in verbatim and the
    // grammatical ending hangs on the classifier «հեքիաթ», NOT on the title:
    // every shipped title already ends in the definite article, so splicing
    // «-ը» onto it stutters.
    var templates = voiceClipFile!.Templates!;
    if (!string.IsNullOrWhiteSpace(templates.Offer))
    {
        jobs.Add(($"{story.Id}--offer.mp3", $"{story.Id} offer",
            [templates.Offer.Replace("{Title}", story.Title)]));
    }
    if (!string.IsNullOrWhiteSpace(templates.Reoffer))
    {
        jobs.Add(($"{story.Id}--reoffer.mp3", $"{story.Id} reoffer",
            [templates.Reoffer.Replace("{Title}", story.Title)]));
    }
}

// --only narrows the batch to named clip ids. This exists because the
// narrator voice is still INTERIM: rendering the whole set in a voice that
// is going to be replaced is work, money and listening time thrown away
// twice. Sample first, batch once the voice is final.
if (only.Count > 0)
{
    var wanted = new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);
    var before = jobs.Count;
    jobs = jobs.Where(j =>
        wanted.Contains(Path.GetFileNameWithoutExtension(j.FileName))
        || wanted.Contains(j.Label)).ToList();
    if (jobs.Count == 0)
    {
        Console.Error.WriteLine(
            $"--only matched none of the {before} available job(s). Names are the "
            + "output filename without .mp3, e.g. greet-01 or anban-huri--offer.");
        return 2;
    }
    Console.WriteLine($"--only: {jobs.Count} of {before} job(s) selected.");
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
    if (clips || voiceClips)
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

// previous_text/next_text are a multilingual_v2-era feature; v3 refuses them.
var supportsContext = !model.StartsWith("eleven_v3", StringComparison.OrdinalIgnoreCase);

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
    var pieceDurations = new List<double>();
    for (var c = 0; c < job.Chunks.Count; c++)
    {
        // At the default speed the request carries no voice_settings at all,
        // so the voice's own saved settings apply — byte-for-byte the request
        // shape of the probe the owner approved on 2026-08-04. Sending a
        // settings object "just to set speed to 1.0" is not a no-op: it
        // replaces the saved settings with whatever this object contains.
        var body = JsonSerializer.Serialize(new
        {
            text = job.Chunks[c],
            model_id = model,
            // Neighbouring text is sent as CONTEXT, not as speech: it is what
            // keeps the voice's pace and intonation continuous across a split
            // so a chunked story does not sound like separate takes stitched
            // together. eleven_v3 rejects both fields outright
            // ("Providing previous_text or next_text is not yet supported with
            // the 'eleven_v3' model", HTTP 400), so on v3 the chunks are
            // rendered blind and the seams are checked by ear instead.
            previous_text = supportsContext && c > 0 ? job.Chunks[c - 1] : null,
            next_text = supportsContext && c + 1 < job.Chunks.Count ? job.Chunks[c + 1] : null,
            voice_settings = Math.Abs(speed - 1.0) < 0.001 ? null : new { speed },
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
        var pieceSeconds = Mp3Duration.Seconds(piece);
        pieceDurations.Add(pieceSeconds);
        var pieceExpected = ExpectedSeconds(job.Chunks[c].Length);
        Console.WriteLine(
            $"    chunk {c + 1}/{job.Chunks.Count} {job.Chunks[c].Length,5:N0} chars -> {piece.LongLength,9:N0} B  {FormatDuration(pieceSeconds)}");

        // Stop at the FIRST short chunk instead of paying for the rest of the
        // story and discovering it at the end. eleven_v3 curtails its output
        // around 1,200-1,400 characters however long the input is, so a chunk
        // size that is too big burns the whole render — which is exactly how
        // 2026-08-04 went. Aborting here costs one chunk, not twenty-six.
        if (pieceExpected > 0 && pieceSeconds < pieceExpected * ShortRenderFloor)
        {
            var pct = (int)Math.Round(100 * pieceSeconds / pieceExpected);
            Console.Error.WriteLine(
                $"  *** chunk {c + 1} came back {FormatDuration(pieceSeconds)}, only {pct}% of the " +
                $"~{FormatDuration(pieceExpected)} its {job.Chunks[c].Length:N0} characters need. " +
                $"The model is curtailing its output — re-run with a smaller --max-chunk. " +
                $"Stopping so the remaining chunks are not paid for.");
            return 1;
        }
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

    // The segment map, for the narration jobs whose chunks are the segments.
    // Frames are copied untouched by Mp3Stitch, so a segment starts exactly
    // where the pieces before it end — the starts are a running sum, not an
    // estimate. Seconds, not bytes: byte offsets depend on the re-encode
    // Ship-StoryAudio.ps1 does afterwards, which is why segments_to_bytes.py
    // runs last against the finished file. Shape matches mix_ambience.py's
    // output so both producers of this file agree.
    if (segmentMapFor.TryGetValue(job.FileName, out var mapStoryId))
    {
        // Measure each piece AS IT APPEARS IN THE JOINED FILE, not as the API
        // returned it. Every response opens with a Xing/"Info" header frame
        // that Mp3Stitch drops, and that frame is 26ms of nothing which
        // Mp3Duration nonetheless counts. Summing the raw response durations
        // therefore overshoots by ~26ms PER SEGMENT and the error accumulates
        // down the story — the self-test caught exactly this (4 pieces,
        // 0.104s = 4 x 26ms). Re-joining a single piece runs it through the
        // same stripping the final file got, so the numbers cannot disagree.
        var stripped = pieces.Select(p => Mp3Duration.Seconds(Mp3Stitch.Join([p]))).ToList();
        var starts = new List<double>(stripped.Count);
        var running = 0.0;
        foreach (var d in stripped)
        {
            starts.Add(Math.Round(running, 3));
            running += d;
        }
        var mapPath = Path.Combine(output, $"{mapStoryId}.segments.json");
        await File.WriteAllTextAsync(mapPath, JsonSerializer.Serialize(new
        {
            storyId = mapStoryId,
            unit = "seconds",
            starts,
            durations = stripped.Select(d => Math.Round(d, 3)).ToList(),
        }, new JsonSerializerOptions { WriteIndented = true }));

        // Keep the individual segment renders. The ambience mix wants
        // per-segment audio and the sounds are not licensed yet, so throwing
        // the pieces away here would mean paying to render the whole library
        // a second time when they are.
        var segDir = Path.Combine(output, "segments");
        Directory.CreateDirectory(segDir);
        for (var s = 0; s < pieces.Count; s++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(segDir, $"{mapStoryId}--seg{s + 1:00}.mp3"), pieces[s]);
        }
        Console.WriteLine($"  segment map: {pieces.Count} segment(s) -> {mapPath}");
    }
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
/// <summary>
/// Shape of <c>backend/content/voice-clips/voice-clips.json</c> — the
/// reviewable Armenian source for the welcome flow. Only the fields this
/// tool renders from are bound; the tone rules, watch words and comments in
/// that file are for humans.
/// </summary>
internal sealed class VoiceClipFile
{
    public List<VoiceClipEntry>? Clips { get; set; }

    [JsonPropertyName("_perStoryTemplates")]
    public VoiceClipTemplates? Templates { get; set; }
}

internal sealed class VoiceClipEntry
{
    public string? VoiceId { get; set; }
    public string? Text { get; set; }
}

internal sealed class VoiceClipTemplates
{
    public string? Offer { get; set; }
    public string? Reoffer { get; set; }
}

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

/// <summary>
/// Checks the segment map against real MP3 bytes, with no API key and nothing
/// sent anywhere. Point it at a directory of MP3 pieces (--output &lt;dir&gt;);
/// it stitches them exactly as a render does and verifies the two claims the
/// map rests on:
///
///   1. Joining pieces preserves total playing time — Mp3Stitch drops each
///      piece's ID3 tag and Xing header frame but copies every audio frame, so
///      nothing is lost or double-counted at a seam.
///   2. A segment's start is the running sum of the pieces before it, which is
///      only true if (1) holds.
///
/// If (1) ever stops holding, every start time after the first seam is wrong
/// and the backend answers questions about the wrong scene — silently, because
/// a plausible-looking number is indistinguishable from a correct one.
/// </summary>
internal static class SegmentMapSelfTest
{
    public static int Run(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"--self-test needs a directory of MP3 pieces: {dir} does not exist.");
            Console.Error.WriteLine("Make some with ffmpeg, e.g.");
            Console.Error.WriteLine("  ffmpeg -f lavfi -i anullsrc=r=44100:cl=mono -t 5 -b:a 128k p1.mp3");
            return 2;
        }
        var files = Directory.GetFiles(dir, "*.mp3").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count < 2)
        {
            Console.Error.WriteLine($"--self-test needs at least 2 MP3s in {dir}; found {files.Count}.");
            return 2;
        }

        var pieces = files.Select(File.ReadAllBytes).ToList();
        // Raw = what the API handed back, header frame and all. Stripped =
        // what survives into the joined file. They differ by one 26ms frame
        // per piece, and using the raw number is the mistake this test exists
        // to catch: it looks right, and it silently walks every segment start
        // later down the story.
        var raw = pieces.Select(Mp3Duration.Seconds).ToList();
        var durations = pieces.Select(p => Mp3Duration.Seconds(Mp3Stitch.Join([p]))).ToList();
        var joined = Mp3Stitch.Join(pieces);
        var joinedSeconds = Mp3Duration.Seconds(joined);
        var summed = durations.Sum();

        var failures = 0;
        Console.WriteLine($"pieces in {dir}:");
        for (var i = 0; i < files.Count; i++)
        {
            Console.WriteLine(
                $"  {Path.GetFileName(files[i]),-28} raw {raw[i],7:F3}s  in-file {durations[i],7:F3}s  {pieces[i].LongLength,9:N0} B");
        }
        Console.WriteLine(
            $"  (raw total {raw.Sum(),8:F3}s — using this instead of in-file would drift {raw.Sum() - summed:F3}s)");

        // One frame is 26ms at 44.1kHz; allow a hair over one frame so a
        // rounding difference is not called a failure, but a dropped or
        // duplicated frame is.
        const double Tolerance = 0.030;
        var delta = Math.Abs(joinedSeconds - summed);
        Console.WriteLine();
        Console.WriteLine($"  sum of pieces {summed,8:F3}s");
        Console.WriteLine($"  joined file   {joinedSeconds,8:F3}s   delta {delta:F3}s");
        if (delta > Tolerance)
        {
            Console.Error.WriteLine(
                $"  FAIL: joining changed the playing time by {delta:F3}s (> {Tolerance:F3}s). "
                + "Segment starts after the first seam would be wrong.");
            failures++;
        }

        // The starts the render writes, checked against a walk of the joined
        // stream rather than against the same addition that produced them.
        var starts = new List<double>();
        var running = 0.0;
        foreach (var d in durations) { starts.Add(Math.Round(running, 3)); running += d; }
        if (starts[0] != 0.0)
        {
            Console.Error.WriteLine($"  FAIL: first segment starts at {starts[0]}s, not 0.");
            failures++;
        }
        for (var i = 1; i < starts.Count; i++)
        {
            if (starts[i] <= starts[i - 1])
            {
                Console.Error.WriteLine($"  FAIL: segment {i + 1} starts at or before segment {i}.");
                failures++;
            }
        }
        if (starts[^1] >= joinedSeconds)
        {
            Console.Error.WriteLine(
                $"  FAIL: last segment starts at {starts[^1]:F3}s, at or past the end of a {joinedSeconds:F3}s file.");
            failures++;
        }
        Console.WriteLine($"  starts: {string.Join(", ", starts.Select(s => $"{s:F3}"))}");

        // A stitched file must carry exactly one header region — the defect
        // that once made a four-minute story stop at 0:34 was every piece keeping
        // its own. check_story_audio.py counts ID3 tags on the shipped file;
        // this catches it one stage earlier, before anything is paid for.
        var id3 = CountId3(joined);
        Console.WriteLine($"  ID3 tags in joined stream: {id3}");
        if (id3 > 0)
        {
            Console.Error.WriteLine("  FAIL: the joined stream still carries an ID3 tag from a piece.");
            failures++;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "SELF-TEST PASS" : $"SELF-TEST FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    private static int CountId3(byte[] d)
    {
        var n = 0;
        for (var i = 0; i + 10 <= d.Length; i++)
        {
            if (d[i] != 0x49 || d[i + 1] != 0x44 || d[i + 2] != 0x33) continue;
            if (d[i + 3] == 0xFF || d[i + 4] == 0xFF) continue;
            var syncsafe = true;
            for (var k = 0; k < 4; k++) if (d[i + 6 + k] >= 0x80) syncsafe = false;
            if (syncsafe) n++;
        }
        return n;
    }
}

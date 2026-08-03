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

// One render job = one output MP3.
var jobs = new List<(string FileName, string Label, string Text)>();
foreach (var story in stories)
{
    if (!clips)
    {
        // Narration: the segments verbatim, joined with a blank line —
        // the same text the reviewed story file carries. Speed is in the
        // filename so a two-speed comparison render can't mix files up.
        var text = string.Join("\n\n", story.Segments.Select(s => s.Text));
        jobs.Add(($"{story.Id}--narration--s{speed:0.0#}.mp3", $"{story.Id} narration", text));
        continue;
    }

    // Clips. Intro composes title + author («Հեքիաթ՝ …։ Հեղինակ՝ …։»);
    // a story with no verified author gets the title-only intro — never
    // a guessed attribution. Summary prefers the B1 `lesson` text and
    // falls back to the reflectionText the stories always had.
    var intro = story.Author is null
        ? $"Հեքիաթ՝ «{story.Title}»։"
        : $"Հեքիաթ՝ «{story.Title}»։ Հեղինակ՝ {story.Author}։";
    jobs.Add(($"{story.Id}--intro.mp3", $"{story.Id} intro", intro));
    if (story.ReflectionQuestions.Count > 0)
    {
        jobs.Add(($"{story.Id}--question.mp3", $"{story.Id} question", story.ReflectionQuestions[0]));
    }
    var summary = story.Lesson ?? story.ReflectionText;
    if (!string.IsNullOrWhiteSpace(summary))
    {
        jobs.Add(($"{story.Id}--summary.mp3", $"{story.Id} summary", summary));
    }
}

var totalChars = jobs.Sum(j => j.Text.Length);
Console.WriteLine($"Plan: {jobs.Count} file(s), {totalChars:N0} characters, speed={speed:0.0#}, model={model}");
Console.WriteLine($"Output: {output}");
Console.WriteLine();
foreach (var job in jobs)
{
    Console.WriteLine($"  {job.FileName,-42} {job.Text.Length,6:N0} chars");
    if (clips)
    {
        // Clip texts are short child-facing lines — print them in full so
        // the dry run doubles as the pre-render text review.
        Console.WriteLine($"      «{job.Text}»");
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
foreach (var job in jobs)
{
    Console.WriteLine($"Rendering {job.Label} ({job.Text.Length:N0} chars)...");
    var body = JsonSerializer.Serialize(new
    {
        text = job.Text,
        model_id = model,
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
            $"  FAILED: HTTP {(int)response.StatusCode} {detail[..Math.Min(detail.Length, 200)]}");
        return 1;
    }
    var bytes = await response.Content.ReadAsByteArrayAsync();
    var path = Path.Combine(output, job.FileName);
    await File.WriteAllBytesAsync(path, bytes);
    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    manifest.Add(new { file = job.FileName, sha256 = sha, sizeBytes = bytes.LongLength });
    Console.WriteLine($"  OK {bytes.LongLength:N0} B sha256={sha}");
}

var snippetPath = Path.Combine(output, "manifest-snippet.json");
await File.WriteAllTextAsync(snippetPath,
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine();
Console.WriteLine($"Wrote {snippetPath} — paste sha256/sizeBytes into ContentSync config");
Console.WriteLine("and BUMP each story's Version so devices re-download.");
Console.WriteLine("Next gate: human listen test (tools/quality-evidence conventions).");
return 0;

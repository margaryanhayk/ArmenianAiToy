// Manual TTS voice/model bakeoff for Armenian child-story narration.
//
// EXPLORATION TOOL — deliberately NOT production parity (that guarantee
// belongs to tools/TtsListenTest). Renders a fixed candidate matrix of
// model+voice combos over three high-signal curated-story strings via
// raw HTTP to /v1/audio/speech, so gpt-4o-mini-tts `instructions` can
// be included (the pinned OpenAI SDK does not expose them).
//
// DEFAULT MODE IS A SAFE DRY-RUN: prints the matrix, samples, output
// filenames, and manifest preview — no API key read, no HttpClient
// created, no network, no files. Rendering requires BOTH --render AND
// --confirm-paid-api. Per-combo API failures (e.g. a voice the speech
// endpoint rejects) are recorded in the manifest and never abort the
// run. Never wired into CI, the solution, or runtime.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArmenianAiToy.Application.Stories;

Console.OutputEncoding = Encoding.UTF8;

// UserSecretsId of ArmenianAiToy.Api — same fallback store TtsListenTest
// uses, so no second copy of the key ever exists. Env vars win.
const string ApiUserSecretsId = "f2f0775b-5ced-46be-8abf-e8a26ebb77e0";
const string SpeechEndpoint = "https://api.openai.com/v1/audio/speech";

// One instruction string for the whole round — the variable under test
// is the VOICE. Instruction tuning is a possible round 2 with the
// winner only. Sent for gpt-4o-mini-tts combos exclusively; the tts-1
// family ignores/rejects instructions.
const string StorytellerInstructions =
    "Speak in a warm, gentle Armenian children's storyteller tone. " +
    "Natural, kind, soft, not robotic. Moderate speed. Clear pronunciation. " +
    "Calm bedtime-friendly energy.";

var render = args.Contains("--render");
var confirmPaid = args.Contains("--confirm-paid-api");
var outputDir = GetOptionValue(args, "--output")
    ?? Path.Combine(Path.GetTempPath(), "areg-tts-voice-bakeoff");

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        TtsVoiceBakeoff — manual voice/model bakeoff for Armenian storyteller TTS.

        Usage:
          dotnet run --project tools/TtsVoiceBakeoff                          dry-run (default, safe)
          dotnet run --project tools/TtsVoiceBakeoff -- --render --confirm-paid-api
                                                                              render MP3s (PAID API)
        Options:
          --render             render the matrix; refused without --confirm-paid-api
          --confirm-paid-api   explicit acknowledgment that OpenAI TTS will be billed
          --output <dir>       output directory (default: %TEMP%/areg-tts-voice-bakeoff)
        """);
    return 0;
}

// ── Candidate matrix. UseInstructions only for the gpt-4o-mini-tts
//    family. marin/cedar are realtime-API voice ids that the speech
//    endpoint may reject — attempted deliberately; a rejection is a
//    recorded RESULT, not an error.
(string Model, string Voice, bool UseInstructions)[] matrix =
[
    ("gpt-4o-mini-tts", "coral",   true),
    ("gpt-4o-mini-tts", "shimmer", true),
    ("gpt-4o-mini-tts", "sage",    true),
    ("gpt-4o-mini-tts", "marin",   true),
    ("gpt-4o-mini-tts", "cedar",   true),
    ("tts-1-hd",        "shimmer", false),
    ("tts-1-hd",        "fable",   false),
    ("tts-1-hd",        "nova",    false),
];

// ── Sample strings — read straight from the library by id, never
//    hand-copied. Three highest-signal strings (stress mark + dialogue
//    pause, verb-pair rhythm, question intonation).
var library = new InMemoryCuratedStoryLibrary();
var littleCloud = library.GetById(InMemoryCuratedStoryLibrary.LittleCloudId)
    ?? throw new InvalidOperationException("little-cloud missing from library");
var hedgehog = library.GetById(InMemoryCuratedStoryLibrary.HedgehogAppleId)
    ?? throw new InvalidOperationException("hedgehog-apple missing from library");

(string Story, string Label, string Text)[] samples =
[
    (littleCloud.Id, "segment-2", littleCloud.Segments[2].Text),
    (hedgehog.Id, "segment-1", hedgehog.Segments[1].Text),
    (littleCloud.Id, "question-0", littleCloud.ReflectionQuestions[0]),
];

// ── Render safety gate: fail BEFORE key resolution / client creation.
if (render && !confirmPaid)
{
    Console.Error.WriteLine(
        "REFUSED: --render calls the paid OpenAI TTS API. " +
        "Re-run with BOTH --render AND --confirm-paid-api to proceed.");
    return 1;
}

var totalRenders = matrix.Length * samples.Length;
Console.WriteLine($"TtsVoiceBakeoff — {matrix.Length} model+voice combos × {samples.Length} sample strings = {totalRenders} renders");
Console.WriteLine($"Mode: {(render ? "RENDER (paid API)" : "DRY-RUN (no API key read, no network, no files)")}");
Console.WriteLine($"Output dir (render mode only): {outputDir}");
Console.WriteLine();
Console.WriteLine("CANDIDATE MATRIX");
foreach (var (model, voice, useInstructions) in matrix)
{
    Console.WriteLine($"  {model} + {voice}{(useInstructions ? "   [with storyteller instructions]" : "")}");
}
Console.WriteLine();
Console.WriteLine("SAMPLE STRINGS");
foreach (var (story, label, text) in samples)
{
    Console.WriteLine($"  {story}--{label}  [{text.Length} chars]");
    Console.WriteLine($"    {text}");
}
Console.WriteLine();
Console.WriteLine("MANIFEST PREVIEW (filenames)");
foreach (var (model, voice, _) in matrix)
{
    foreach (var (story, label, _) in samples)
    {
        Console.WriteLine($"  {FileName(model, voice, story, label)}");
    }
}

if (!render)
{
    Console.WriteLine();
    Console.WriteLine("DRY-RUN complete. No API key was read, no network call was made, no files were created.");
    Console.WriteLine("To render: dotnet run --project tools/TtsVoiceBakeoff -- --render --confirm-paid-api");
    return 0;
}

// ── RENDER MODE (explicitly double-confirmed above).
var apiKey = ResolveConfigValue("OpenAI:ApiKey", "OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine(
        "No OpenAI API key found. Set OPENAI_API_KEY (or OpenAI__ApiKey) in the " +
        "environment, or provision the backend's user-secrets (OpenAI:ApiKey). " +
        "The key is never printed or written to disk by this tool.");
    return 2;
}

Directory.CreateDirectory(outputDir);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var rows = new List<string>();
var failures = new List<string>();

Console.WriteLine();
Console.WriteLine($"Rendering {totalRenders} combos…");
foreach (var (model, voice, useInstructions) in matrix)
{
    foreach (var (story, label, text) in samples)
    {
        var fileName = FileName(model, voice, story, label);
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["voice"] = voice,
                ["input"] = text,
                ["response_format"] = "mp3",
            };
            if (useInstructions)
            {
                payload["instructions"] = StorytellerInstructions;
            }

            using var response = await http.PostAsync(
                SpeechEndpoint,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                var summary = Truncate(error.Replace('\n', ' ').Replace('\r', ' '), 200);
                failures.Add($"| {fileName} | {model} | {voice} | HTTP {(int)response.StatusCode} | {summary} |");
                Console.WriteLine($"  FAILED  {fileName} — HTTP {(int)response.StatusCode}");
                continue;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(Path.Combine(outputDir, fileName), bytes);
            rows.Add($"| {fileName} | {model} | {voice} | {(useInstructions ? "yes" : "no")} | {story}--{label} | {text.Length} |  |  |  |  |  |  |");
            Console.WriteLine($"  rendered {fileName} ({bytes.Length} bytes)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            failures.Add($"| {fileName} | {model} | {voice} | transport error | {Truncate(ex.Message, 200)} |");
            Console.WriteLine($"  FAILED  {fileName} — {ex.GetType().Name}");
        }
    }
}

var manifest = new StringBuilder();
manifest.AppendLine("# Areg TTS voice/model bakeoff manifest");
manifest.AppendLine();
manifest.AppendLine($"- combos: {matrix.Length} × {samples.Length} strings = {totalRenders} attempted; {rows.Count} rendered, {failures.Count} failed");
manifest.AppendLine($"- gpt-4o-mini-tts instructions: \"{StorytellerInstructions}\"");
manifest.AppendLine("- rate each file 1-5 per column (Roboticness: LOWER is better), then a final verdict per combo.");
manifest.AppendLine();
manifest.AppendLine("| File | Model | Voice | Instructions? | String | Chars | Warmth 1-5 | Naturalness 1-5 | Armenian pronunciation 1-5 | Child-friendly 1-5 | Roboticness 1-5 lower=better | Final verdict |");
manifest.AppendLine("|------|-------|-------|---------------|--------|-------|------------|-----------------|----------------------------|--------------------|------------------------------|---------------|");
foreach (var row in rows)
{
    manifest.AppendLine(row);
}
manifest.AppendLine();
manifest.AppendLine("## Failed combos");
manifest.AppendLine();
if (failures.Count == 0)
{
    manifest.AppendLine("(none)");
}
else
{
    manifest.AppendLine("| File | Model | Voice | Failure | Detail |");
    manifest.AppendLine("|------|-------|-------|---------|--------|");
    foreach (var failure in failures)
    {
        manifest.AppendLine(failure);
    }
}

await File.WriteAllTextAsync(Path.Combine(outputDir, "manifest.md"), manifest.ToString(), Encoding.UTF8);
Console.WriteLine();
Console.WriteLine($"Done. {rows.Count} MP3s + manifest.md written to: {outputDir}");
Console.WriteLine("Listen, fill the rating columns, then pick the winner. Production adoption is a separate reviewed slice.");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────

static string FileName(string model, string voice, string story, string label) =>
    $"{model}--{voice}--{story}--{label}.mp3";

static string? GetOptionValue(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..max] + "…";

// Env var (raw and config-style "__" forms) wins; falls back to the
// backend Api project's user-secrets store. Never printed.
static string? ResolveConfigValue(string configKey, string envVar)
{
    var fromEnv = Environment.GetEnvironmentVariable(envVar)
        ?? Environment.GetEnvironmentVariable(configKey.Replace(":", "__"));
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        return fromEnv;
    }

    var secretsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "UserSecrets", ApiUserSecretsId, "secrets.json");
    if (!File.Exists(secretsPath))
    {
        return null;
    }
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(secretsPath));
        return doc.RootElement.TryGetProperty(configKey, out var value)
            ? value.GetString()
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

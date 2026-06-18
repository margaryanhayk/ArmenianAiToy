// Manual TTS listen-test tool for the curated Library Story texts.
//
// DEFAULT MODE IS A SAFE DRY-RUN: enumerates every child-facing string
// from InMemoryCuratedStoryLibrary, prints labels / char counts /
// watchpoints / a manifest preview, calls NO network API, writes NO
// files. Rendering real MP3s requires BOTH --render AND
// --confirm-paid-api, and reuses the production OpenAITtsSynthesisService
// (tts-1 default via OpenAI:TtsModel, voice Nova, MP3) so what you hear
// is exactly what the toy would speak.
//
// Never wired into CI, runtime DI, or any endpoint. Manual use only.

using System.Text;
using System.Text.Json;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Infrastructure.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

// UserSecretsId of ArmenianAiToy.Api — the tool reads OpenAI:ApiKey /
// OpenAI:TtsModel from the same secrets store the backend uses, so no
// second copy of the key ever exists. Env vars win over secrets.
const string ApiUserSecretsId = "f2f0775b-5ced-46be-8abf-e8a26ebb77e0";

var render = args.Contains("--render");
var confirmPaid = args.Contains("--confirm-paid-api");
var includeTitles = args.Contains("--include-titles");
var outputDir = GetOptionValue(args, "--output")
    ?? Path.Combine(Path.GetTempPath(), "areg-tts-listen-test");

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

// ── Watchpoints: what the human listener must verify, per the
//    armenian-story-master reviews. Marker is matched as a substring.
(string Marker, string Note)[] watchpoints =
[
    ("ամպի՛կ", "mid-word stress mark (U+055B) — if Nova glitches, the reviewed fallback is plain «ամպիկ ջան» (needs fresh linguistic review)"),
    ("ո՞ւմ", "«՞» inside the «ու» digraph — verify no false pause or skip"),
    ("ի՞նչ", "«՞» on the question word — verify question intonation"),
    ("մաղեց", "rare-but-correct verb for fine drizzle — verify stress; rewrite, never phonetically hack"),
    ("գլորվեց", "intransitive 'rolled' — verify the consonant cluster renders cleanly"),
    ("գլորեցին", "transitive 'they rolled' — verify it sounds distinct from «գլորվեց»"),
    ("երևում", "«և» mid-word — verify natural [ev]/[jev] reading"),
    ("ոզնիկն ու", "ն-article liaison into the following vowel"),
    ("Ծաղիկն ուրախացավ", "ն-article liaison into the following vowel"),
    ("՝", "dialogue բութ («ասաց՝») — must yield a short natural pause, not a dead stop"),
    (",", "Armenian comma — verify the storyteller-beat pause («ծանր էր, շատ ծանր»)"),
    ("։", "sentence-final stop — verify clean sentence intonation, no run-ons"),
];

// ── Enumerate every child-facing string straight from the library.
//    Titles are listed for context but only rendered with --include-titles
//    (no current contract speaks the title aloud).
var library = new InMemoryCuratedStoryLibrary();
var items = new List<ListenItem>();
foreach (var story in library.ListAvailable())
{
    if (includeTitles)
    {
        items.Add(MakeItem(story.Id, "title", story.Title));
    }
    foreach (var segment in story.Segments)
    {
        items.Add(MakeItem(story.Id, $"segment-{segment.Index}", segment.Text));
    }
    items.Add(MakeItem(story.Id, "reflection", story.ReflectionText));
    for (var i = 0; i < story.ReflectionQuestions.Count; i++)
    {
        items.Add(MakeItem(story.Id, $"question-{i}", story.ReflectionQuestions[i]));
    }
}

// ── Optional: render a single DRAFT story file's offline clips
//    (--draft <path-to-.story.json>). Produces exactly the three clips the
//    screenless-selection + post-story design needs — announce (title),
//    conclusion (reflectionText), question-N (reflectionQuestions) — WITHOUT
//    adding the draft to the runtime library. Used for the owner-recorded
//    anban-huri listen test. When set, ONLY the draft is rendered (the
//    library items above are dropped) so no unintended paid renders happen.
var draftPath = GetOptionValue(args, "--draft");
if (draftPath is not null)
{
    items.Clear();
    using var draftDoc = JsonDocument.Parse(File.ReadAllText(draftPath));
    var draftRoot = draftDoc.RootElement;
    var draftId = draftRoot.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "draft" : "draft";
    if (draftRoot.TryGetProperty("title", out var titleEl) && titleEl.GetString() is { Length: > 0 } title)
    {
        items.Add(MakeItem(draftId, "announce", title));
    }
    if (draftRoot.TryGetProperty("reflectionText", out var reflEl) && reflEl.GetString() is { Length: > 0 } refl)
    {
        items.Add(MakeItem(draftId, "conclusion", refl));
    }
    if (draftRoot.TryGetProperty("reflectionQuestions", out var qsEl) && qsEl.ValueKind == JsonValueKind.Array)
    {
        var qi = 0;
        foreach (var q in qsEl.EnumerateArray())
        {
            if (q.GetString() is { Length: > 0 } qt)
            {
                items.Add(MakeItem(draftId, $"question-{qi}", qt));
            }
            qi++;
        }
    }
    Console.WriteLine($"DRAFT mode: {draftPath} → {items.Count} clip(s) [{string.Join(", ", items.Select(i => i.Label))}]");
    Console.WriteLine();
}

// ── Render safety gate: --render without --confirm-paid-api fails fast
//    BEFORE any client construction or network access.
if (render && !confirmPaid)
{
    Console.Error.WriteLine(
        "REFUSED: --render calls the paid OpenAI TTS API. " +
        "Re-run with BOTH --render AND --confirm-paid-api to proceed.");
    return 1;
}

Console.WriteLine($"TtsListenTest — {library.ListAvailable().Count} curated stories, {items.Count} renderable strings");
Console.WriteLine($"Mode: {(render ? "RENDER (paid API)" : "DRY-RUN (no API call, no files written)")}");
Console.WriteLine($"Output dir (render mode only): {outputDir}");
Console.WriteLine();

foreach (var story in library.ListAvailable())
{
    Console.WriteLine($"── Story: {story.Id} — «{story.Title}» (BedtimeSafe={story.BedtimeSafe}, ages {story.MinAge}-{story.MaxAge}, {story.Segments.Count} segments)");
}
Console.WriteLine();
Console.WriteLine("MANIFEST PREVIEW");
Console.WriteLine("================");
foreach (var item in items)
{
    Console.WriteLine($"{item.FileName}  [{item.Text.Length} chars]");
    Console.WriteLine($"  story: {item.StoryId}   label: {item.Label}");
    Console.WriteLine($"  text:  {item.Text}");
    var hits = watchpoints.Where(w => item.Text.Contains(w.Marker, StringComparison.OrdinalIgnoreCase)).ToList();
    if (hits.Count > 0)
    {
        Console.WriteLine("  listen for:");
        foreach (var (marker, note) in hits)
        {
            Console.WriteLine($"    • «{marker}» — {note}");
        }
    }
    Console.WriteLine();
}

if (!render)
{
    Console.WriteLine("DRY-RUN complete. No API was called, no files were created.");
    Console.WriteLine("To render: dotnet run --project tools/TtsListenTest -- --render --confirm-paid-api");
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
var ttsModel = ResolveConfigValue("OpenAI:TtsModel", "OPENAI_TTS_MODEL") ?? "tts-1";

Console.WriteLine($"Rendering {items.Count} strings with model '{ttsModel}', voice Nova (production adapter)…");
Directory.CreateDirectory(outputDir);

// The PRODUCTION adapter — voice (Nova), output format (MP3 default),
// and 30 s timeout are parity-by-construction, not copied settings.
var tts = new OpenAITtsSynthesisService(
    new OpenAIClient(apiKey).GetAudioClient(ttsModel),
    NullLogger<OpenAITtsSynthesisService>.Instance);

var manifest = new StringBuilder();
manifest.AppendLine("# Areg curated-story TTS listen-test manifest");
manifest.AppendLine();
manifest.AppendLine($"- model: {ttsModel}, voice: Nova (via production OpenAITtsSynthesisService)");
manifest.AppendLine($"- strings rendered: {items.Count}");
manifest.AppendLine();
manifest.AppendLine("| File | Story | Label | Chars | Listen for |");
manifest.AppendLine("|------|-------|-------|-------|------------|");

foreach (var item in items)
{
    var result = await tts.SynthesizeArmenianAsync(item.Text);
    var path = Path.Combine(outputDir, item.FileName);
    await File.WriteAllBytesAsync(path, result.Content);
    var hits = watchpoints
        .Where(w => item.Text.Contains(w.Marker, StringComparison.OrdinalIgnoreCase))
        .Select(w => $"«{w.Marker}»");
    manifest.AppendLine($"| {item.FileName} | {item.StoryId} | {item.Label} | {item.Text.Length} | {string.Join(", ", hits)} |");
    Console.WriteLine($"  rendered {item.FileName} ({result.Content.Length} bytes)");
}

var manifestPath = Path.Combine(outputDir, "manifest.md");
await File.WriteAllTextAsync(manifestPath, manifest.ToString(), Encoding.UTF8);
Console.WriteLine();
Console.WriteLine($"Done. MP3s + manifest written to: {outputDir}");
Console.WriteLine("Listen to every file against its watchpoints and record verdicts in tools/quality-evidence/.");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────

static string? GetOptionValue(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static ListenItem MakeItem(string storyId, string label, string text) =>
    new(storyId, label, text, $"{storyId}--{label}.mp3");

// Env var (both raw and config-style "__" forms) wins; falls back to the
// backend Api project's user-secrets store. Returns null when absent.
// The resolved value is NEVER printed.
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

static void PrintUsage()
{
    Console.WriteLine("""
        TtsListenTest — manual listen-test renders for curated Library Story texts.

        Usage:
          dotnet run --project tools/TtsListenTest                          dry-run (default, safe)
          dotnet run --project tools/TtsListenTest -- --render --confirm-paid-api
                                                                            render MP3s (PAID API)
        Options:
          --render             render MP3s; refused without --confirm-paid-api
          --confirm-paid-api   explicit acknowledgment that OpenAI TTS will be billed
          --output <dir>       output directory (default: %TEMP%/areg-tts-listen-test)
          --include-titles     also render story titles (not spoken by any current contract)
          --help               this text
        """);
}

internal sealed record ListenItem(string StoryId, string Label, string Text, string FileName);

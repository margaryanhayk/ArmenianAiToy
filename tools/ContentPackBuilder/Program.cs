using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Tools.ContentPackBuilder;

// ContentPackBuilder — assembles the offline SD content pack from the
// backend's already-rendered story-audio cache. See
// esp32/AregVoiceMvp/HARDENING-INTEGRATION.md §6.
//
//   dotnet run --project tools/ContentPackBuilder -- \
//       --cache <story-audio-cache dir> \
//       --out   <sd content-pack dir> \
//       [--bitrate 48]   (kbps mono; 0 = copy MP3 verbatim) \
//       [--ffmpeg ffmpeg]
//
// Render the stories FIRST (hit GET /api/story-audio/<id> on a running
// backend, or run it once) so the cache holds <id>.mp3 + the two sidecars.

static string? Opt(string[] a, string name)
{
    var i = Array.IndexOf(a, name);
    return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
}

var cacheDir = Opt(args, "--cache")
    ?? Path.Combine("backend", "src", "ArmenianAiToy.Api", "story-audio-cache");
var outDir = Opt(args, "--out") ?? "sd-content-pack";
var bitrate = int.TryParse(Opt(args, "--bitrate"), out var b) ? b : 48;
var ffmpeg = Opt(args, "--ffmpeg") ?? "ffmpeg";

if (!Directory.Exists(cacheDir))
{
    Console.Error.WriteLine($"Cache dir not found: {cacheDir}");
    Console.Error.WriteLine("Render stories first (GET /api/story-audio/<id>), then re-run.");
    return 1;
}

// Manifest metadata comes from the curated library (id / title / bedtime-safe).
var library = new InMemoryCuratedStoryLibrary();
var stories = library.ListAvailable()
    .Select(s => new StoryInput(s.Id, s.Title, s.BedtimeSafe))
    .ToList();

Console.WriteLine($"Stories in library: {stories.Count}");
Console.WriteLine($"Cache : {Path.GetFullPath(cacheDir)}");
Console.WriteLine($"Output: {Path.GetFullPath(outDir)}");
Console.WriteLine(bitrate > 0
    ? $"Re-encoding to {bitrate} kbps mono via '{ffmpeg}'."
    : "Copying MP3 verbatim (no re-encode).");

var builder = new ContentPackBuilder(new FfmpegReencoder(ffmpeg));
BuildResult result;
try
{
    result = await builder.BuildAsync(stories, cacheDir, outDir, bitrate);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Build failed: {ex.Message}");
    return 2;
}

foreach (var id in result.Written) Console.WriteLine($"  ✓ {id}");
foreach (var id in result.Skipped) Console.WriteLine($"  – {id} (no cached MP3 — render it first)");
Console.WriteLine($"Done: {result.Written.Count} written, {result.Skipped.Count} skipped.");
return result.Written.Count > 0 ? 0 : 3;

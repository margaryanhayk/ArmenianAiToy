using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArmenianAiToy.Tools.ContentPackBuilder;

/// <summary>One story's manifest metadata (passed in by the CLI from the
/// curated library so this core stays free of an Application reference and
/// is unit-testable in isolation).</summary>
public sealed record StoryInput(string Id, string Title, bool BedtimeSafe);

/// <summary>Outcome of a build run.</summary>
public sealed record BuildResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Skipped);

/// <summary>Re-encode seam. Production wraps ffmpeg; tests substitute a
/// deterministic stub. Returns the re-encoded MP3 bytes.</summary>
public interface IAudioReencoder
{
    Task<byte[]> ReencodeAsync(byte[] mp3, int kbps, CancellationToken ct);
}

/// <summary>
/// Builds an offline SD "content pack" from the backend's already-rendered
/// story-audio cache. For each story it (optionally) re-encodes the
/// narration MP3 to a lower speech bitrate and rewrites the sentence /
/// segment byte-offset sidecars so they match the bytes actually shipped,
/// then lays out <c>/stories/&lt;id&gt;/</c> + <c>/manifest.json</c> exactly
/// as the firmware offline mode expects (see
/// <c>esp32/AregVoiceMvp/HARDENING-INTEGRATION.md §6</c>).
/// <para>
/// Pure of TTS / network: it consumes the cache the backend's
/// <c>GET /api/story-audio/&lt;id&gt;</c> already produced
/// (<c>&lt;id&gt;.mp3</c> + <c>&lt;id&gt;.offsets.json</c> +
/// <c>&lt;id&gt;.segments.json</c>). Render first, then build.
/// </para>
/// </summary>
public sealed class ContentPackBuilder
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        // Armenian titles should land as readable UTF-8, not \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IAudioReencoder _reencoder;

    public ContentPackBuilder(IAudioReencoder reencoder)
    {
        ArgumentNullException.ThrowIfNull(reencoder);
        _reencoder = reencoder;
    }

    /// <summary>Builds the pack. <paramref name="bitrateKbps"/> &lt;= 0 copies
    /// the MP3 verbatim (no re-encode, maps unchanged); &gt; 0 re-encodes and
    /// scales the byte maps to the new size. Stories with no cached MP3 are
    /// skipped (not an error — render them first).</summary>
    public async Task<BuildResult> BuildAsync(
        IReadOnlyList<StoryInput> stories,
        string cacheDir,
        string outDir,
        int bitrateKbps,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stories);

        var written = new List<string>();
        var skipped = new List<string>();
        var manifest = new List<object>();

        foreach (var story in stories)
        {
            var mp3Path = Path.Combine(cacheDir, story.Id + ".mp3");
            if (!File.Exists(mp3Path))
            {
                skipped.Add(story.Id);
                continue;
            }

            var originalMp3 = await File.ReadAllBytesAsync(mp3Path, ct);
            var offsets = ReadLongArray(Path.Combine(cacheDir, story.Id + ".offsets.json"));
            var segments = ReadLongArray(Path.Combine(cacheDir, story.Id + ".segments.json"));

            byte[] finalMp3 = bitrateKbps > 0
                ? await _reencoder.ReencodeAsync(originalMp3, bitrateKbps, ct)
                : originalMp3;

            long oldSize = originalMp3.LongLength;
            long newSize = finalMp3.LongLength;
            var finalOffsets = ScaleByteMap(offsets, oldSize, newSize);
            var finalSegments = ScaleByteMap(segments, oldSize, newSize);

            var storyDir = Path.Combine(outDir, "stories", story.Id);
            Directory.CreateDirectory(storyDir);
            await File.WriteAllBytesAsync(Path.Combine(storyDir, "narration.mp3"), finalMp3, ct);
            await File.WriteAllTextAsync(
                Path.Combine(storyDir, "narration.offsets.json"),
                JsonSerializer.Serialize(finalOffsets), ct);
            await File.WriteAllTextAsync(
                Path.Combine(storyDir, "narration.segments.json"),
                JsonSerializer.Serialize(finalSegments), ct);

            manifest.Add(new { id = story.Id, title = story.Title, bedtimeSafe = story.BedtimeSafe });
            written.Add(story.Id);
        }

        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(
            Path.Combine(outDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, ManifestJson), ct);

        return new BuildResult(written, skipped);
    }

    /// <summary>
    /// Rescales a byte-offset map for a re-encoded MP3. Byte position ≈
    /// bitrate × time for CBR audio, and re-encoding to a fixed bitrate
    /// preserves duration, so every offset scales by the same
    /// <c>newSize / oldSize</c> ratio. (This inherits the same CBR
    /// assumption the backend's maps already make — see StoryAudioController.)
    /// Result is clamped to <c>[0, newSize]</c> and forced non-decreasing so
    /// the firmware's binary-search snap stays well-formed. An empty map, or
    /// a non-positive <paramref name="oldSize"/>, returns the input unchanged.
    /// </summary>
    public static long[] ScaleByteMap(long[] map, long oldSize, long newSize)
    {
        if (map.Length == 0 || oldSize <= 0)
        {
            return map;
        }
        var scaled = new long[map.Length];
        long prev = 0;
        for (var i = 0; i < map.Length; i++)
        {
            var v = (long)Math.Round(map[i] * (double)newSize / oldSize);
            if (v < 0) v = 0;
            if (v > newSize) v = newSize;
            if (v < prev) v = prev; // keep ascending for a clean binary search
            scaled[i] = v;
            prev = v;
        }
        return scaled;
    }

    private static long[] ReadLongArray(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<long[]>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

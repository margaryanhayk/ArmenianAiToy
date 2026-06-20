using System.Text.Json;
using ArmenianAiToy.Tools.ContentPackBuilder;

namespace ArmenianAiToy.Tools.ContentPackBuilder.Tests;

public class ScaleByteMapTests
{
    [Fact]
    public void Identity_WhenSizeUnchanged()
    {
        long[] map = [0, 100, 250, 400];
        Assert.Equal(map, ContentPackBuilder.ScaleByteMap(map, 400, 400));
    }

    [Fact]
    public void Halves_WhenNewSizeIsHalf()
    {
        long[] map = [0, 100, 250, 400];
        Assert.Equal(new long[] { 0, 50, 125, 200 },
            ContentPackBuilder.ScaleByteMap(map, 400, 200));
    }

    [Fact]
    public void ResultIsClampedAndMonotonic()
    {
        long[] map = [0, 500, 1000];
        var scaled = ContentPackBuilder.ScaleByteMap(map, 1000, 250);
        Assert.Equal(0, scaled[0]);
        Assert.True(scaled[^1] <= 250);                 // never exceeds new size
        for (var i = 1; i < scaled.Length; i++)
            Assert.True(scaled[i] >= scaled[i - 1]);    // non-decreasing
    }

    [Fact]
    public void EmptyOrZeroOldSize_ReturnsInputUnchanged()
    {
        Assert.Empty(ContentPackBuilder.ScaleByteMap([], 100, 50));
        long[] map = [0, 10];
        Assert.Same(map, ContentPackBuilder.ScaleByteMap(map, 0, 50));
    }
}

public class ContentPackBuilderTests
{
    // Stub re-encoder: returns a buffer of a fixed size so the size ratio
    // (and thus the scaled maps) are deterministic.
    private sealed class StubReencoder : IAudioReencoder
    {
        private readonly int _outBytes;
        public int Calls { get; private set; }
        public StubReencoder(int outBytes) => _outBytes = outBytes;
        public Task<byte[]> ReencodeAsync(byte[] mp3, int kbps, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new byte[_outBytes]);
        }
    }

    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "areg-cpb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void SeedCache(string cacheDir, string id, int mp3Bytes, long[] offsets, long[] segments)
    {
        File.WriteAllBytes(Path.Combine(cacheDir, id + ".mp3"), new byte[mp3Bytes]);
        File.WriteAllText(Path.Combine(cacheDir, id + ".offsets.json"), JsonSerializer.Serialize(offsets));
        File.WriteAllText(Path.Combine(cacheDir, id + ".segments.json"), JsonSerializer.Serialize(segments));
    }

    // Seeds a per-story offline clip exactly as TtsListenTest names them:
    // "<id>--<label>.mp3" (e.g. "anban-huri--conclusion.mp3").
    private static void SeedClip(string clipsDir, string id, string label, int bytes)
    {
        Directory.CreateDirectory(clipsDir);
        File.WriteAllBytes(Path.Combine(clipsDir, $"{id}--{label}.mp3"), new byte[bytes]);
    }

    [Fact]
    public async Task Build_ReEncodes_ScalesMaps_AndWritesPackLayout()
    {
        var cache = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            // 1000-byte source; stub re-encodes to 250 bytes (¼) → maps ×¼.
            SeedCache(cache, "little-cloud", mp3Bytes: 1000,
                offsets: [0, 500, 1000], segments: [0, 1000]);
            var builder = new ContentPackBuilder(new StubReencoder(250));

            var result = await builder.BuildAsync(
                [new StoryInput("little-cloud", "Փոքրիկ ամպիկը", BedtimeSafe: true)],
                cache, outDir, bitrateKbps: 48);

            Assert.Equal(new[] { "little-cloud" }, result.Written);
            Assert.Empty(result.Skipped);

            var storyDir = Path.Combine(outDir, "stories", "little-cloud");
            Assert.Equal(250, new FileInfo(Path.Combine(storyDir, "narration.mp3")).Length);

            var offsets = JsonSerializer.Deserialize<long[]>(
                await File.ReadAllTextAsync(Path.Combine(storyDir, "narration.offsets.json")))!;
            Assert.Equal(new long[] { 0, 125, 250 }, offsets);
            var segments = JsonSerializer.Deserialize<long[]>(
                await File.ReadAllTextAsync(Path.Combine(storyDir, "narration.segments.json")))!;
            Assert.Equal(new long[] { 0, 250 }, segments);

            // Manifest carries id / title / bedtimeSafe with the Armenian title intact.
            var manifest = await File.ReadAllTextAsync(Path.Combine(outDir, "manifest.json"));
            Assert.Contains("\"id\": \"little-cloud\"", manifest);
            Assert.Contains("Փոքրիկ ամպիկը", manifest);          // not \u-escaped
            Assert.Contains("\"bedtimeSafe\": true", manifest);
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task Build_CopyMode_LeavesMapsUnchanged_AndDoesNotReencode()
    {
        var cache = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            SeedCache(cache, "hedgehog-apple", mp3Bytes: 800, offsets: [0, 400, 800], segments: [0, 400]);
            var stub = new StubReencoder(123);

            var result = await builder_BuildCopy(stub, cache, outDir);

            Assert.Equal(new[] { "hedgehog-apple" }, result.Written);
            Assert.Equal(0, stub.Calls); // bitrate 0 → no re-encode
            var storyDir = Path.Combine(outDir, "stories", "hedgehog-apple");
            Assert.Equal(800, new FileInfo(Path.Combine(storyDir, "narration.mp3")).Length);
            var offsets = JsonSerializer.Deserialize<long[]>(
                await File.ReadAllTextAsync(Path.Combine(storyDir, "narration.offsets.json")))!;
            Assert.Equal(new long[] { 0, 400, 800 }, offsets); // unchanged
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(outDir, true);
        }
    }

    private static Task<BuildResult> builder_BuildCopy(IAudioReencoder r, string cache, string outDir) =>
        new ContentPackBuilder(r).BuildAsync(
            [new StoryInput("hedgehog-apple", "Ոզնին և խնձորը", BedtimeSafe: false)],
            cache, outDir, bitrateKbps: 0);

    [Fact]
    public async Task Build_SkipsStoryWithNoCachedMp3()
    {
        var cache = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            SeedCache(cache, "little-cloud", 500, [0, 500], [0]);
            var builder = new ContentPackBuilder(new StubReencoder(500));

            var result = await builder.BuildAsync(
                [
                    new StoryInput("little-cloud", "A", true),
                    new StoryInput("not-rendered", "B", true), // no cache files
                ],
                cache, outDir, bitrateKbps: 0);

            Assert.Equal(new[] { "little-cloud" }, result.Written);
            Assert.Equal(new[] { "not-rendered" }, result.Skipped);
            Assert.False(Directory.Exists(Path.Combine(outDir, "stories", "not-rendered")));
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task Build_IncludesClips_AndDeclaresThemInManifest()
    {
        var cache = NewTempDir();
        var clips = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            SeedCache(cache, "anban-huri", mp3Bytes: 1000, offsets: [0, 1000], segments: [0]);
            SeedClip(clips, "anban-huri", "announce", 40);
            SeedClip(clips, "anban-huri", "conclusion", 200);
            SeedClip(clips, "anban-huri", "question-0", 120);
            var builder = new ContentPackBuilder(new StubReencoder(60));

            var result = await builder.BuildAsync(
                [new StoryInput("anban-huri", "Անբան Հուռին", BedtimeSafe: false)],
                cache, outDir, bitrateKbps: 48, clipsDir: clips);

            Assert.Equal(new[] { "anban-huri" }, result.Written);
            var storyDir = Path.Combine(outDir, "stories", "anban-huri");
            Assert.True(File.Exists(Path.Combine(storyDir, "announce.mp3")));
            Assert.True(File.Exists(Path.Combine(storyDir, "conclusion.mp3")));
            Assert.True(File.Exists(Path.Combine(storyDir, "question-0.mp3")));
            // re-encoded → the stub's fixed output size
            Assert.Equal(60, new FileInfo(Path.Combine(storyDir, "conclusion.mp3")).Length);

            var manifest = await File.ReadAllTextAsync(Path.Combine(outDir, "manifest.json"));
            Assert.Contains("\"hasAnnounce\": true", manifest);
            Assert.Contains("\"hasConclusion\": true", manifest);
            Assert.Contains("\"questionCount\": 1", manifest);
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(clips, true);
            Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task Build_CountsMultipleQuestions_AndStopsAtFirstGap()
    {
        var cache = NewTempDir();
        var clips = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            SeedCache(cache, "s", 100, [0, 100], [0]);
            SeedClip(clips, "s", "question-0", 10);
            SeedClip(clips, "s", "question-1", 10);
            // no question-2 → the count loop stops at 2

            await new ContentPackBuilder(new StubReencoder(10))
                .BuildAsync([new StoryInput("s", "S", true)], cache, outDir, bitrateKbps: 0, clipsDir: clips);

            var storyDir = Path.Combine(outDir, "stories", "s");
            Assert.True(File.Exists(Path.Combine(storyDir, "question-0.mp3")));
            Assert.True(File.Exists(Path.Combine(storyDir, "question-1.mp3")));
            Assert.False(File.Exists(Path.Combine(storyDir, "question-2.mp3")));

            var manifest = await File.ReadAllTextAsync(Path.Combine(outDir, "manifest.json"));
            Assert.Contains("\"questionCount\": 2", manifest);
            Assert.Contains("\"hasConclusion\": false", manifest); // no conclusion clip seeded
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(clips, true);
            Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task Build_NoClipsDir_DeclaresAbsentClips_AndWritesNoClipFiles()
    {
        var cache = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            SeedCache(cache, "little-cloud", 500, [0, 500], [0]);

            // clipsDir omitted (defaults to null) → clips skipped, never an error.
            await new ContentPackBuilder(new StubReencoder(500))
                .BuildAsync([new StoryInput("little-cloud", "A", true)], cache, outDir, bitrateKbps: 0);

            var storyDir = Path.Combine(outDir, "stories", "little-cloud");
            Assert.False(File.Exists(Path.Combine(storyDir, "conclusion.mp3")));
            Assert.False(File.Exists(Path.Combine(storyDir, "announce.mp3")));

            var manifest = await File.ReadAllTextAsync(Path.Combine(outDir, "manifest.json"));
            Assert.Contains("\"hasAnnounce\": false", manifest);
            Assert.Contains("\"hasConclusion\": false", manifest);
            Assert.Contains("\"questionCount\": 0", manifest);
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task Build_CopyMode_DoesNotReencodeClips()
    {
        var cache = NewTempDir();
        var clips = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            SeedCache(cache, "s", 100, [0, 100], [0]);
            SeedClip(clips, "s", "conclusion", 222);
            var stub = new StubReencoder(999);

            await new ContentPackBuilder(stub).BuildAsync(
                [new StoryInput("s", "S", true)], cache, outDir, bitrateKbps: 0, clipsDir: clips);

            Assert.Equal(0, stub.Calls); // neither narration nor clip re-encoded in copy mode
            var conclusion = Path.Combine(outDir, "stories", "s", "conclusion.mp3");
            Assert.Equal(222, new FileInfo(conclusion).Length); // verbatim copy
        }
        finally
        {
            Directory.Delete(cache, true);
            Directory.Delete(clips, true);
            Directory.Delete(outDir, true);
        }
    }
}

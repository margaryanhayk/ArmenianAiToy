using System.Text;
using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Accuracy tests for the offset→segment mapping (the approximation fix):
///   1. The render writes a per-segment START byte map (<c>.segments.json</c>)
///      derived from the real per-chunk byte accumulation — exact when the
///      synthesizer's byte length tracks input length.
///   2. <see cref="StoryQaController.SegmentForOffset"/> resolves a byte
///      offset to the correct segment via binary search, replacing the old
///      "every segment is 1/N of the file" assumption.
/// </summary>
public class StoryAudioSegmentMapTests
{
    private const string RealStoryId = InMemoryCuratedStoryLibrary.LittleCloudId;

    // --- 1. Map generation ------------------------------------------

    [Fact]
    public async Task Render_WritesSegmentMap_MatchingSegmentCharStarts_WhenBytesTrackChars()
    {
        var cacheDir = Path.Combine(
            Path.GetTempPath(), "areg-segmap-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("StoryAudio:CacheRoot", cacheDir),
                // No signing key -> open access; the render path is what we test.
                new KeyValuePair<string, string?>("StoryAudio:SigningKey", "")
            })
            .Build();

        // Synthesizer whose output byte length == input char length, so the
        // rendered byte offsets equal character offsets EXACTLY — letting us
        // assert the segment map against independently-computed char starts
        // with no proportional slack.
        var synth = Substitute.For<IAudioSynthesisService>();
        synth.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AudioSynthesisResult(
                new byte[((string)ci[0]!).Length], "audio/mpeg"));

        var env = Substitute.For<IWebHostEnvironment>();
        var logger = Substitute.For<ILogger<StoryAudioController>>();
        var library = new InMemoryCuratedStoryLibrary();
        var controller = new StoryAudioController(synth, library, env, config, logger);

        var result = await controller.GetStoryAudio(RealStoryId);
        Assert.IsType<PhysicalFileResult>(result);

        // Read the sidecar the render just wrote.
        var segmentsPath = Path.Combine(cacheDir, $"{RealStoryId}.segments.json");
        Assert.True(File.Exists(segmentsPath));
        var map = JsonSerializer.Deserialize<long[]>(await File.ReadAllTextAsync(segmentsPath))!;

        // Independently compute expected segment char-starts in the
        // "\n"-joined narration (segment i = prior text lengths + i newlines).
        var segments = library.GetById(RealStoryId)!.Segments;
        Assert.Equal(segments.Count, map.Length);

        long expected = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            Assert.Equal(expected, map[i]);
            expected += segments[i].Text.Length + 1; // +1 = "\n" separator
        }

        // Sanity: strictly ascending, starts at 0.
        Assert.Equal(0, map[0]);
        for (var i = 1; i < map.Length; i++)
        {
            Assert.True(map[i] > map[i - 1]);
        }

        Directory.Delete(cacheDir, recursive: true);
    }

    // --- 2. Offset lookup -------------------------------------------

    [Fact]
    public void SegmentForOffset_PicksSegmentWhoseRangeContainsOffset()
    {
        // Segment starts at bytes: 0, 100, 250, 400 (unequal lengths — the
        // case the old proportional 1/N estimate got wrong).
        long[] map = [0, 100, 250, 400];
        const int count = 4;

        Assert.Equal(0, StoryQaController.SegmentForOffset(map, 1, count));
        Assert.Equal(0, StoryQaController.SegmentForOffset(map, 99, count));
        Assert.Equal(1, StoryQaController.SegmentForOffset(map, 100, count)); // exact boundary
        Assert.Equal(1, StoryQaController.SegmentForOffset(map, 249, count));
        Assert.Equal(2, StoryQaController.SegmentForOffset(map, 250, count));
        Assert.Equal(3, StoryQaController.SegmentForOffset(map, 400, count));
        Assert.Equal(3, StoryQaController.SegmentForOffset(map, 9999, count)); // past end -> last
    }

    [Fact]
    public void SegmentForOffset_NonPositiveOffset_OrTrivialInputs_ReturnZero()
    {
        long[] map = [0, 100, 250];
        Assert.Equal(0, StoryQaController.SegmentForOffset(map, 0, 3));
        Assert.Equal(0, StoryQaController.SegmentForOffset(map, -5, 3));
        Assert.Equal(0, StoryQaController.SegmentForOffset([0], 500, 1));   // single segment
        Assert.Equal(0, StoryQaController.SegmentForOffset([], 500, 5));    // empty map
    }

    // --- 3. Resume snap (micro-rewind) ------------------------------
    // Pins the snap behaviour the sentence-byte map drives. The map stays
    // a proportional estimate by design (exact offsets would need
    // per-sentence TTS, which degrades the narration audio); these tests
    // pin the snap logic, not sub-second precision.

    [Fact]
    public void SnapOffset_ExactBoundary_ReturnsItself()
    {
        long[] map = [0, 100, 250, 400];
        Assert.Equal(250, StoryAudioController.SnapOffset(map, 250));
    }

    [Fact]
    public void SnapOffset_BetweenBoundaries_SnapsDownToSentenceStart()
    {
        long[] map = [0, 100, 250, 400];
        Assert.Equal(100, StoryAudioController.SnapOffset(map, 180)); // mid-sentence -> its start
        Assert.Equal(0, StoryAudioController.SnapOffset(map, 99));
        Assert.Equal(400, StoryAudioController.SnapOffset(map, 9999)); // past last -> last start
    }

    [Fact]
    public void SnapOffset_EmptyMap_ReturnsFromUnchanged()
    {
        Assert.Equal(1234, StoryAudioController.SnapOffset([], 1234));
    }

    [Fact]
    public void SegmentForOffset_DemonstratesImprovementOverProportional()
    {
        // Unequal segments: a tiny first segment then large ones. The old
        // estimate (offset * N / totalBytes) would mis-bucket an offset that
        // lands early in the file. With a true map it resolves correctly.
        long[] map = [0, 20, 800, 820];   // total ~1000, segment 0 is tiny
        const int count = 4;
        const long totalBytes = 1000;

        const long offset = 500; // squarely inside segment 1 [20, 800)
        var accurate = StoryQaController.SegmentForOffset(map, offset, count);
        var oldProportional = (int)(offset * count / totalBytes); // = 2 (wrong)

        Assert.Equal(1, accurate);
        Assert.NotEqual(accurate, oldProportional);
    }
}

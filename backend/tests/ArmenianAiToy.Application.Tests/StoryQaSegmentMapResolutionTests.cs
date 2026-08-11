using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Application.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins WHICH segment map <see cref="StoryQaController.OffsetToSegment"/>
/// reads, which decides what scene an in-story question is answered about.
///
/// <para>
/// Two MP3s of the same story exist and their byte offsets are not
/// interchangeable: the OpenAI-TTS render this backend streams (under
/// <c>StoryAudio:CacheRoot</c>) and the ElevenLabs narration the toy plays
/// from its SD card (under <c>ContentSync:AudioRoot</c>). Before this, only
/// the cache map was consulted — so on the SD path, which is the one
/// children actually use, the cache file usually did not exist and every
/// question resolved to segment 0. On a truncated story that is badly
/// wrong: the file holds a fraction of the text, so a child near its end is
/// scored near the start and gets an answer about a scene they have not
/// reached.
/// </para>
/// </summary>
public class StoryQaSegmentMapResolutionTests : IDisposable
{
    private const string StoryId = "little-cloud";
    private const int SegmentCount = 4;

    private readonly string _cacheRoot =
        Path.Combine(Path.GetTempPath(), "areg-qa-cache-" + Guid.NewGuid().ToString("N"));
    private readonly string _shippedRoot =
        Path.Combine(Path.GetTempPath(), "areg-qa-shipped-" + Guid.NewGuid().ToString("N"));

    public StoryQaSegmentMapResolutionTests()
    {
        Directory.CreateDirectory(_cacheRoot);
        Directory.CreateDirectory(_shippedRoot);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _cacheRoot, _shippedRoot })
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ---- the three resolution steps ----

    /// <summary>KEYSTONE (ordering): a streaming session's own map still wins.
    /// StoryAudioController re-renders whenever the audio or either sidecar is
    /// missing, so a streaming child always has a correct cache map — and its
    /// offsets belong to THAT file. Reading the shipped map for it would
    /// answer about the wrong scene just as surely as the old bug did.</summary>
    [Fact]
    public void CacheMap_WinsWhenPresent()
    {
        WriteMap(_cacheRoot, [0, 100, 200, 300]);
        WriteMap(_shippedRoot, [0, 10_000, 20_000, 30_000]);

        // 250 is segment 2 by the cache map, segment 0 by the shipped one.
        Assert.Equal(2, Controller().OffsetToSegment(StoryId, 250, SegmentCount));
    }

    /// <summary>The fix: with no cache map — the ordinary state on the SD
    /// path — the map beside the shipped narration is used.</summary>
    [Fact]
    public void ShippedMap_IsUsedWhenNoCacheMapExists()
    {
        WriteMap(_shippedRoot, [0, 10_000, 20_000, 30_000]);

        Assert.Equal(2, Controller().OffsetToSegment(StoryId, 25_000, SegmentCount));
    }

    /// <summary>With no map at all, the proportional estimate is measured
    /// against whichever MP3 exists instead of collapsing to 0. This is the
    /// state of every story shipped today — none has a segment map yet — so
    /// it is the behaviour a child gets until the re-render lands.</summary>
    [Fact]
    public void NoMap_FallsBackToProportionalAgainstTheShippedFile_NotZero()
    {
        WriteMp3(_shippedRoot, sizeBytes: 4000);

        // Three quarters through a 4000-byte file, 4 segments -> segment 3.
        Assert.Equal(3, Controller().OffsetToSegment(StoryId, 3000, SegmentCount));
    }

    /// <summary>Nothing resolves — no map, no file anywhere — so segment 0.
    /// The prompt still carries the whole-story summary, so the answer stays
    /// correct; only the scene refinement is lost.</summary>
    [Fact]
    public void NothingResolves_ReturnsZero()
    {
        Assert.Equal(0, Controller().OffsetToSegment(StoryId, 3000, SegmentCount));
    }

    /// <summary>A story configured in neither place must not throw or reach
    /// into another story's map.</summary>
    [Fact]
    public void UnknownStory_ReturnsZero()
    {
        WriteMap(_shippedRoot, [0, 10_000, 20_000, 30_000]);

        Assert.Equal(0, Controller().OffsetToSegment("no-such-story", 25_000, SegmentCount));
    }

    /// <summary>Trivial inputs short-circuit before any file is touched.</summary>
    [Fact]
    public void NonPositiveOffset_OrSingleSegment_ReturnsZero()
    {
        WriteMap(_cacheRoot, [0, 100, 200, 300]);
        var c = Controller();

        Assert.Equal(0, c.OffsetToSegment(StoryId, 0, SegmentCount));
        Assert.Equal(0, c.OffsetToSegment(StoryId, -5, SegmentCount));
        Assert.Equal(0, c.OffsetToSegment(StoryId, 250, 1));
    }

    // ---- harness ----

    private void WriteMap(string dir, long[] map) =>
        File.WriteAllText(Path.Combine(dir, $"{StoryId}.segments.json"), JsonSerializer.Serialize(map));

    private void WriteMp3(string dir, int sizeBytes) =>
        File.WriteAllBytes(Path.Combine(dir, $"{StoryId}.mp3"), new byte[sizeBytes]);

    /// <summary>Only the configuration matters to the method under test; every
    /// other dependency is a substitute that is never reached.</summary>
    private StoryQaController Controller()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("StoryAudio:CacheRoot", _cacheRoot),
            new KeyValuePair<string, string?>("ContentSync:Enabled", "true"),
            new KeyValuePair<string, string?>("ContentSync:Stories:0:StoryId", StoryId),
            new KeyValuePair<string, string?>("ContentSync:Stories:0:Version", "1"),
            // Absolute, so ResolveAudioPath leaves it untouched (it would
            // otherwise root a relative path at AppContext.BaseDirectory).
            new KeyValuePair<string, string?>(
                "ContentSync:Stories:0:AudioPath", Path.Combine(_shippedRoot, $"{StoryId}.mp3")),
            new KeyValuePair<string, string?>("ContentSync:Stories:0:Sha256", new string('a', 64)),
            new KeyValuePair<string, string?>("ContentSync:Stories:0:SizeBytes", "4000"),
        }).Build();

        var synthesis = Substitute.For<IAudioSynthesisService>();
        return new StoryQaController(
            Substitute.For<IAudioTranscriptionService>(),
            synthesis,
            new InMemoryCuratedStoryLibrary(),
            new LibraryStoryQuestionService(Substitute.For<IAiChatClient>()),
            Substitute.For<IModerationService>(),
            Substitute.For<IConversationService>(),
            Substitute.For<IChildService>(),
            Substitute.For<IDeviceService>(),
            new CannedVoiceClips(synthesis),
            new OpenAICostMeter(),
            Options.Create(new OpenAIDailyCostCapOptions { Enabled = false }),
            Substitute.For<IWebHostEnvironment>(),
            config,
            Substitute.For<ILogger<StoryQaController>>());
    }
}

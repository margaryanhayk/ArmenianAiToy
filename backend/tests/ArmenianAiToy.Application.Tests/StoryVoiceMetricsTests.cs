using System.Diagnostics.Metrics;
using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Serial collection — <see cref="MeterListener"/> is process-wide, so
/// these tests must not run alongside anything that increments the
/// voice-path instruments.
/// </summary>
[CollectionDefinition(nameof(StoryVoiceMetricsCollection), DisableParallelization = true)]
public class StoryVoiceMetricsCollection { }

/// <summary>
/// Observability tests for the voice path (gap: "NO metrics on this
/// voice path"). Pin that <c>aat_story_qa_turn_total{outcome}</c>,
/// <c>aat_story_qa_duration_seconds</c>, and
/// <c>aat_story_audio_render_total{result}</c> are emitted with the
/// expected bounded tag values.
/// </summary>
[Collection(nameof(StoryVoiceMetricsCollection))]
public class StoryVoiceMetricsTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF q");
    private static readonly byte[] TtsMp3 = Encoding.UTF8.GetBytes("ID3 a");

    // --- Story-QA turn counter + duration histogram -----------------

    [Fact]
    public async Task QaTurn_Answered_EmitsAnsweredOutcome_AndDuration()
    {
        using var capture = MetricCapture.Start(
            "aat_story_qa_turn_total", "aat_story_qa_duration_seconds");
        var (controller, deps) = CreateQa();
        deps.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns("Ո՞վ է փոքրիկ ամպիկը");
        deps.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(true, new List<string>()));
        deps.Ai.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("Փոքրիկ ամպիկը երկնքի ընկերն է։");

        await controller.Ask(StoryId, 0, CancellationToken.None);

        var turn = Assert.Single(capture.Counter("aat_story_qa_turn_total"));
        Assert.Equal(1, turn.Value);
        Assert.Equal("answered", turn.Tag("outcome"));
        // Duration recorded exactly once for the turn.
        Assert.Single(capture.Histogram("aat_story_qa_duration_seconds"));
    }

    [Fact]
    public async Task QaTurn_ModerationBlocked_EmitsModerationBlockedOutcome()
    {
        using var capture = MetricCapture.Start("aat_story_qa_turn_total");
        var (controller, deps) = CreateQa();
        deps.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns("վտանգավոր բան");
        deps.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(false, new List<string> { "violence" }));

        await controller.Ask(StoryId, 0, CancellationToken.None);

        var turn = Assert.Single(capture.Counter("aat_story_qa_turn_total"));
        Assert.Equal("moderation_blocked", turn.Tag("outcome"));
    }

    [Fact]
    public async Task QaTurn_SttFailure_EmitsTransientFailureOutcome()
    {
        using var capture = MetricCapture.Start("aat_story_qa_turn_total");
        var (controller, deps) = CreateQa();
        deps.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Throws(new InvalidOperationException("whisper down"));

        await controller.Ask(StoryId, 0, CancellationToken.None);

        var turn = Assert.Single(capture.Counter("aat_story_qa_turn_total"));
        Assert.Equal("transient_failure", turn.Tag("outcome"));
    }

    // --- Story-audio render counter ---------------------------------

    [Fact]
    public async Task StoryAudio_ColdRender_EmitsRenderSuccess()
    {
        using var capture = MetricCapture.Start("aat_story_audio_render_total");
        var cacheDir = Path.Combine(
            Path.GetTempPath(), "areg-metrics-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("StoryAudio:CacheRoot", cacheDir),
            new KeyValuePair<string, string?>("StoryAudio:SigningKey", "")
        }).Build();
        var synth = Substitute.For<IAudioSynthesisService>();
        synth.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AudioSynthesisResult(
                new byte[((string)ci[0]!).Length], "audio/mpeg"));
        var controller = new StoryAudioController(
            synth, new InMemoryCuratedStoryLibrary(),
            Substitute.For<IWebHostEnvironment>(), config,
            Substitute.For<ILogger<StoryAudioController>>());

        await controller.GetStoryAudio(StoryId);

        var render = Assert.Single(capture.Counter("aat_story_audio_render_total"));
        Assert.Equal("success", render.Tag("result"));

        Directory.Delete(cacheDir, recursive: true);
    }

    // --- Harness ----------------------------------------------------

    private sealed record QaDeps(
        IAudioTranscriptionService Transcription,
        IAudioSynthesisService Synthesis,
        IModerationService Moderation,
        IAiChatClient Ai,
        IConversationService Conversations);

    private static (StoryQaController Controller, QaDeps Deps) CreateQa()
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(TtsMp3, "audio/mpeg"));
        var moderation = Substitute.For<IModerationService>();
        var ai = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation { Id = Guid.NewGuid(), DeviceId = Guid.NewGuid(), StartedAt = DateTime.UtcNow });
        conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(new Message { Id = Guid.NewGuid() });

        var controller = new StoryQaController(
            transcription, synthesis, new InMemoryCuratedStoryLibrary(),
            new LibraryStoryQuestionService(ai), moderation, conversations,
            Substitute.For<IWebHostEnvironment>(), new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<StoryQaController>>());

        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = Guid.NewGuid();
        http.Request.Body = new MemoryStream(InboundWav);
        http.Request.ContentType = "audio/wav";
        http.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        return (controller, new QaDeps(transcription, synthesis, moderation, ai, conversations));
    }

    // --- Generic process-wide metric capture ------------------------

    private sealed class Measurement
    {
        public double Value { get; init; }
        public Dictionary<string, string?> Tags { get; init; } = new();
        public string? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly List<(string Instrument, Measurement M)> _all = new();
        private readonly MeterListener _listener;

        private MetricCapture(string[] instruments)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == AppMeter.Name && instruments.Contains(instrument.Name))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
                Add(inst.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
                Add(inst.Name, value, tags));
            _listener.Start();
        }

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var m = new Measurement { Value = value };
            foreach (var t in tags) m.Tags[t.Key] = t.Value as string;
            lock (_all) { _all.Add((name, m)); }
        }

        public static MetricCapture Start(params string[] instruments) => new(instruments);

        public IReadOnlyList<Measurement> Counter(string instrument) => Of(instrument);
        public IReadOnlyList<Measurement> Histogram(string instrument) => Of(instrument);

        private List<Measurement> Of(string instrument)
        {
            lock (_all) { return _all.Where(x => x.Instrument == instrument).Select(x => x.M).ToList(); }
        }

        public void Dispose() => _listener.Dispose();
    }
}

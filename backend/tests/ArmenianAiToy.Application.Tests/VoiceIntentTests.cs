using System.Diagnostics.Metrics;
using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Welcome-flow — <c>POST /api/devices/voice-intent</c> and the Armenian
/// yes/no matcher behind it.
/// <para>
/// The safety story here is structural, not behavioural: the endpoint answers
/// with ONE token from a closed set and never calls a model, so the tests that
/// matter most are the ones pinning that shape and pinning that a gated or
/// blocked turn cannot spend money or select a mode.
/// </para>
/// </summary>
public class VoiceIntentTests
{
    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF child answer marker");

    private sealed record Harness(
        DeviceController Controller,
        IAudioTranscriptionService Transcription,
        IModerationService Moderation,
        IChildService ChildService,
        IDeviceService DeviceService,
        OpenAICostMeter CostMeter,
        IOptions<OpenAIDailyCostCapOptions> CostCapOptions,
        ILogger<DeviceController> Logger,
        Guid DeviceId);

    private static Harness Create(
        bool paused = false,
        bool bedtime = false,
        OpenAIDailyCostCapOptions? costCap = null,
        bool modesEnabled = true)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var moderation = Substitute.For<IModerationService>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);

        var deviceService = Substitute.For<IDeviceService>();
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(paused);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(bedtime);
        deviceService.IsModeEnabledForRequestAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DetectedMode>()).Returns(modesEnabled);

        var controller = new DeviceController(deviceService, Substitute.For<IConfiguration>());

        var deviceId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        httpContext.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return new Harness(
            controller, transcription, moderation, childService, deviceService,
            new OpenAICostMeter(),
            Options.Create(costCap ?? new OpenAIDailyCostCapOptions { Enabled = false }),
            Substitute.For<ILogger<DeviceController>>(),
            deviceId);
    }

    private static Task<IActionResult> Post(Harness h, string expect = VoiceIntentExpectations.Mode) =>
        h.Controller.VoiceIntent(
            h.Transcription, h.Moderation, h.ChildService, h.CostMeter, h.CostCapOptions,
            h.Logger, expect, CancellationToken.None);

    private static void WireTranscript(Harness h, string transcript) =>
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(transcript);

    private static void WireSafe(Harness h) =>
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));

    private static void WireUnsafe(Harness h) =>
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: false, FlaggedCategories: new List<string> { "violence" }));

    private static string IntentOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var property = ok.Value!.GetType().GetProperty("intent");
        Assert.NotNull(property);
        return (string)property!.GetValue(ok.Value)!;
    }

    // ---- KEYSTONE: the response can never carry model text ----

    // The whole safety argument for an unauthenticated-to-the-child endpoint
    // that consumes audio is that its output space is closed. If this test
    // ever fails, the endpoint has grown a field a model could speak through.
    [Fact]
    public async Task VoiceIntent_ResponseShape_IsBoundedTokenOnly()
    {
        var h = Create();
        WireTranscript(h, "պատմիր հեքիաթ");
        WireSafe(h);

        var ok = Assert.IsType<OkObjectResult>(await Post(h));

        var properties = ok.Value!.GetType().GetProperties();
        var only = Assert.Single(properties);
        Assert.Equal("intent", only.Name);
        Assert.Contains((string)only.GetValue(ok.Value)!, VoiceIntents.All);
    }

    // ---- KEYSTONE: a gated turn costs nothing ----

    [Fact]
    public async Task PausedDevice_ReturnsUnknown_AndNeverCallsTranscription()
    {
        var h = Create(paused: true);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));

        await h.Transcription.DidNotReceiveWithAnyArgs().TranscribeArmenianAsync(
            default!, default!, default, default, default);
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!);
    }

    [Fact]
    public async Task BedtimeWindow_ReturnsUnknown_AndNeverCallsTranscription()
    {
        var h = Create(bedtime: true);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));

        await h.Transcription.DidNotReceiveWithAnyArgs().TranscribeArmenianAsync(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task OverDailyCostCap_ReturnsUnknown_AndNeverCallsTranscription()
    {
        var h = Create(costCap: new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.10m });
        h.CostMeter.Record(h.DeviceId, 5.00m, DateTime.UtcNow);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));

        await h.Transcription.DidNotReceiveWithAnyArgs().TranscribeArmenianAsync(
            default!, default!, default, default, default);
    }

    // ---- KEYSTONE: an unsafe utterance can never select a mode ----

    [Fact]
    public async Task BlockedAnswer_ReturnsUnknown_EvenWhenItNamesAMode()
    {
        var h = Create();
        WireTranscript(h, "պատմիր հեքիաթ");   // would classify as story...
        WireUnsafe(h);                          // ...but moderation blocks it

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));
    }

    // ---- classification ----

    [Theory]
    [InlineData("պատմիր հեքիաթ", VoiceIntents.Story)]
    [InlineData("բատմիր հեքիաթ", VoiceIntents.Story)]   // Whisper confusable Պ→Բ
    [InlineData("ուզում եմ քնել", VoiceIntents.Calm)]
    [InlineData("բլբլբլ", VoiceIntents.Unknown)]
    public async Task ModeAnswers_ClassifyWithoutAnyModelCall(string transcript, string expected)
    {
        var h = Create();
        WireTranscript(h, transcript);
        WireSafe(h);

        Assert.Equal(expected, IntentOf(await Post(h)));
    }

    [Fact]
    public async Task EmptyTranscript_ReturnsUnknown_WithoutModerating()
    {
        var h = Create();
        WireTranscript(h, "   ");
        WireSafe(h);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!);
    }

    [Fact]
    public async Task SttFailure_ReturnsUnknown_NotAnError()
    {
        var h = Create();
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns<string>(_ => throw new HttpRequestException("upstream down"));

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));
    }

    [Fact]
    public async Task EmptyBody_Returns400()
    {
        var h = Create();
        h.Controller.ControllerContext.HttpContext.Request.Body = new MemoryStream([]);

        Assert.IsType<BadRequestObjectResult>(await Post(h));
    }

    // A mode the parent switched off is never returned, even if the toy asked
    // with a stale menu — the manifest filtering is a convenience, this is the
    // guarantee.
    [Fact]
    public async Task DisabledMode_IsNeverReturned()
    {
        var h = Create(modesEnabled: false);
        WireTranscript(h, "պատմիր հեքիաթ");
        WireSafe(h);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));
    }

    // Calm is never gated (MODES.md safety invariant) — a bedtime cue must
    // always reach Calm handling, so the mode-flag check must skip it.
    [Fact]
    public async Task CalmAnswer_IsNotSubjectToTheModeFlags()
    {
        var h = Create(modesEnabled: false);
        WireTranscript(h, "ուզում եմ քնել");
        WireSafe(h);

        Assert.Equal(VoiceIntents.Calm, IntentOf(await Post(h)));
    }

    // ---- yes / no ----

    [Theory]
    [InlineData("այո", VoiceIntents.Yes)]
    [InlineData("ոչ", VoiceIntents.No)]
    [InlineData("չեմ ուզում", VoiceIntents.No)]
    public async Task YesNoAnswers_ClassifyWithTheYesNoExpectation(string transcript, string expected)
    {
        var h = Create();
        WireTranscript(h, transcript);
        WireSafe(h);

        Assert.Equal(expected, IntentOf(await Post(h, VoiceIntentExpectations.YesNo)));
    }

    [Fact]
    public async Task YesNoExpectation_BiasesTheTranscription()
    {
        var h = Create();
        WireTranscript(h, "այո");
        WireSafe(h);

        await Post(h, VoiceIntentExpectations.YesNo);

        await h.Transcription.Received().TranscribeArmenianAsync(
            Arg.Any<Stream>(), Arg.Any<string>(),
            VoiceIntentExpectations.YesNoBiasPrompt,
            Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }

    // ---- WelcomeIntentDetector, directly ----

    [Theory]
    [InlineData("այո")]
    [InlineData("Այո՛։")]
    [InlineData("հա")]
    [InlineData("ուզում եմ")]
    [InlineData("այո, ուզում եմ")]
    [InlineData("լավ")]
    public void DetectYesNo_AffirmativeForms(string message) =>
        Assert.Equal(YesNo.Yes, WelcomeIntentDetector.DetectYesNo(message));

    [Theory]
    [InlineData("ոչ")]
    [InlineData("Ոչ։")]
    [InlineData("չէ")]
    [InlineData("չեմ ուզում")]
    [InlineData("չուզեմ")]
    public void DetectYesNo_NegativeForms(string message) =>
        Assert.Equal(YesNo.No, WelcomeIntentDetector.DetectYesNo(message));

    // KEYSTONE: «չեմ ուզում» contains «ուզում». A naive affirmative
    // substring check would read a refusal as consent — the one mistake in
    // this matcher a child would actually notice.
    [Theory]
    [InlineData("չեմ ուզում")]
    [InlineData("ոչ, չեմ ուզում")]
    [InlineData("չեմ ուզում լսել")]
    public void DetectYesNo_NegationBeatsAffirmation(string message) =>
        Assert.Equal(YesNo.No, WelcomeIntentDetector.DetectYesNo(message));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("բլբլբլ")]
    [InlineData("այո ոչ")]   // genuinely contradictory — re-ask, do not guess
    public void DetectYesNo_UnknownForms(string? message) =>
        Assert.Equal(YesNo.Unknown, WelcomeIntentDetector.DetectYesNo(message));

    // The value space is the contract. Adding a token here without updating
    // the firmware's parser would leave a child unheard.
    [Fact]
    public void VoiceIntents_ValueSpaceIsExactlyEight() =>
        Assert.Equal(8, VoiceIntents.All.Distinct(StringComparer.Ordinal).Count());

    // ---- diagnosability: WHY a turn came back unknown ----
    //
    // Ten paths return `unknown` and, from the toy, all ten are byte-identical.
    // Most of them used to write nothing at all, so an owner whose «Արի խաղանք»
    // came back unknown could not tell a tripped cost cap from a classifier
    // miss. These tests pin the two channels that now carry that difference —
    // the log and the metric tag — WITHOUT letting either reach the wire.

    // The cost cap is the reason this whole gap existed: voice-intent was the
    // single paid endpoint that did not call ShouldLogCapTrip, so the most
    // likely cause of tonight's failure was also the only invisible one.
    [Fact]
    public async Task OverDailyCostCap_LogsExactlyOncePerDevicePerDay_AndStillReturnsUnknown()
    {
        var h = Create(costCap: new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.10m });
        h.CostMeter.Record(h.DeviceId, 5.00m, DateTime.UtcNow);

        // Three capped turns in the same UTC day — a real runaway toy retries.
        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));
        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));
        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));

        // Flood control: one warning, not three. Same contract the chat,
        // audio-chat and story-qa cap paths have always had.
        Assert.Equal(1, LogCount(h.Logger, LogLevel.Warning));
    }

    [Fact]
    public async Task OverDailyCostCap_TagsTheMetricWithTheCostCapReason()
    {
        using var capture = MetricCapture.Start("aat_voice_intent_turn_total");
        var h = Create(costCap: new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.10m });
        h.CostMeter.Record(h.DeviceId, 5.00m, DateTime.UtcNow);

        await Post(h);

        var turn = Assert.Single(capture.Counter("aat_voice_intent_turn_total"));
        Assert.Equal(VoiceIntents.Unknown, turn.Tag("intent"));
        Assert.Equal(VoiceIntentReasons.CostCap, turn.Tag("reason"));
    }

    // The other half of tonight's mystery: a transcript arrived and the
    // deterministic matcher simply did not recognise it.
    [Fact]
    public async Task NoMatch_TagsTheMetric_AndLogsTheLengthButNeverTheText()
    {
        using var capture = MetricCapture.Start("aat_voice_intent_turn_total");
        var h = Create();
        const string transcript = "բլբլբլ";
        WireTranscript(h, transcript);
        WireSafe(h);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));

        var turn = Assert.Single(capture.Counter("aat_voice_intent_turn_total"));
        Assert.Equal(VoiceIntentReasons.NoMatch, turn.Tag("reason"));

        // A child's utterance is PII-adjacent; its LENGTH is not. The number is
        // what distinguishes "STT returned nearly nothing" from "STT returned a
        // sentence the matcher did not know".
        var line = Assert.Single(LogMessages(h.Logger, LogLevel.Information));
        Assert.Contains(transcript.Length.ToString(), line);
        Assert.DoesNotContain(transcript, line);
    }

    // A turn that worked is tagged too, so the reason tag partitions the whole
    // counter rather than only its failures.
    [Fact]
    public async Task MatchedAnswer_TagsTheMetricWithMatched()
    {
        using var capture = MetricCapture.Start("aat_voice_intent_turn_total");
        var h = Create();
        WireTranscript(h, "պատմիր հեքիաթ");
        WireSafe(h);

        Assert.Equal(VoiceIntents.Story, IntentOf(await Post(h)));

        var turn = Assert.Single(capture.Counter("aat_voice_intent_turn_total"));
        Assert.Equal(VoiceIntentReasons.Matched, turn.Tag("reason"));
    }

    [Fact]
    public async Task DisabledMode_IsTaggedApartFromAGenuineMiss()
    {
        using var capture = MetricCapture.Start("aat_voice_intent_turn_total");
        var h = Create(modesEnabled: false);
        WireTranscript(h, "պատմիր հեքիաթ");
        WireSafe(h);

        Assert.Equal(VoiceIntents.Unknown, IntentOf(await Post(h)));

        var turn = Assert.Single(capture.Counter("aat_voice_intent_turn_total"));
        Assert.Equal(VoiceIntentReasons.ModeDisabled, turn.Tag("reason"));
    }

    // KEYSTONE: the reason is a METRIC tag, never a response field. If this
    // fails alongside the bounded-shape test above, someone has answered the
    // "why unknown?" question on the wire — where a future edit could put
    // model text.
    [Fact]
    public async Task ReasonNeverReachesTheResponseBody()
    {
        var h = Create(costCap: new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.10m });
        h.CostMeter.Record(h.DeviceId, 5.00m, DateTime.UtcNow);

        var ok = Assert.IsType<OkObjectResult>(await Post(h));

        var only = Assert.Single(ok.Value!.GetType().GetProperties());
        Assert.Equal("intent", only.Name);
    }

    // Bounded value space is what keeps the new tag inside the
    // no-high-cardinality invariant (CLAUDE.md § Metrics). Eleven compile-time
    // constants — no free-form strings, no per-device values.
    [Fact]
    public void VoiceIntentReasons_ValueSpaceIsExactlyEleven() =>
        Assert.Equal(11, VoiceIntentReasons.All.Distinct(StringComparer.Ordinal).Count());

    // ---- test plumbing ----

    private static IEnumerable<ICall> LogCalls(ILogger<DeviceController> logger, LogLevel level) =>
        logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Where(c => c.GetArguments()[0] is LogLevel actual && actual == level);

    private static int LogCount(ILogger<DeviceController> logger, LogLevel level) =>
        LogCalls(logger, level).Count();

    /// <summary>The rendered message of each log line at <paramref name="level"/> —
    /// used to assert what a log does NOT contain.</summary>
    private static IReadOnlyList<string> LogMessages(
        ILogger<DeviceController> logger, LogLevel level) =>
        LogCalls(logger, level)
            .Select(c => c.GetArguments()[2]?.ToString() ?? string.Empty)
            .ToList();

    // Local copy of the MeterListener capture used by StoryVoiceMetricsTests /
    // ModerationFailClosedMetricsTests. Kept local rather than lifted to a
    // shared helper: extracting it would mean editing two unrelated passing
    // test files, which is outside this fix.
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
            _listener.Start();
        }

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var m = new Measurement { Value = value };
            foreach (var t in tags) m.Tags[t.Key] = t.Value as string;
            lock (_all) { _all.Add((name, m)); }
        }

        public static MetricCapture Start(params string[] instruments) => new(instruments);

        public IReadOnlyList<Measurement> Counter(string instrument)
        {
            lock (_all) { return _all.Where(x => x.Instrument == instrument).Select(x => x.M).ToList(); }
        }

        public void Dispose() => _listener.Dispose();
    }
}

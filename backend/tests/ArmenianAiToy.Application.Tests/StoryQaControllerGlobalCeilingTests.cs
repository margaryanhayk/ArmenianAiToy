using System.Diagnostics.Metrics;
using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// AI logic review 2026-10-27, A4 / C4 — the story-qa half. The #022 fleet
/// kill-switch (<c>OpenAIDailyCostCapOptions.Global</c>) is a fleet-dollar
/// ceiling, a different mechanism from the per-device flat cap that
/// <c>Usage:Tiers:Enabled</c> deliberately replaces. It used to be nested in
/// the flat-cap <c>else</c> branch, so switching tiers on silently disarmed
/// it at both story-qa gate sites (<c>Ask</c> and <c>AnswerReflection</c>).
/// Pinned here for BOTH endpoints: with tiers on, a tripped Global ceiling
/// still stops the turn before the allowance is read or any paid call runs;
/// Global &lt;= 0 or the flat cap off keeps the old behaviour; flag-off is
/// unchanged.
/// </summary>
[Collection(nameof(UsageTiersMetricsCollection))]
public class StoryQaControllerGlobalCeilingTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF marker");

    private sealed class MetricCapture : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<(string Instrument, string? Tag)> _seen = new();
        private readonly MeterListener _listener;

        private MetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == AppMeter.Name
                        && (instrument.Name == "aat_openai_cost_cap_trip_total"
                            || instrument.Name == "aat_usage_allowance_exhausted_total"))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                string? tag = null;
                foreach (var t in tags)
                {
                    if (t.Key == "kind" || t.Key == "tier") { tag = t.Value as string; break; }
                }
                lock (_lock) { _seen.Add((instrument.Name, tag)); }
            });
            _listener.Start();
        }

        public static MetricCapture Start() => new();

        public int CapTrips(string kind)
        {
            lock (_lock) { return _seen.Count(s => s.Instrument == "aat_openai_cost_cap_trip_total" && s.Tag == kind); }
        }

        public int AllowanceExhaustions()
        {
            lock (_lock) { return _seen.Count(s => s.Instrument == "aat_usage_allowance_exhausted_total"); }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed record Harness(
        StoryQaController Controller,
        IDeviceService DeviceService,
        IAudioTranscriptionService Transcription,
        Guid DeviceId);

    private static UsageTiersOptions TiersOn() =>
        new() { Enabled = true, Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 999 } } };

    private static UsageAllowanceStatus Status(bool exhausted) =>
        new("free", QuestionsToday: exhausted ? 30 : 1, QuestionsThisMonth: 30, AllowanceToday: 30,
            AllowanceThisMonth: null, IsExhausted: exhausted);

    /// <param name="globalOverCap">Push the fleet total past 1.00 USD (a
    /// different device's spend, as in production).</param>
    private static Harness Create(
        UsageTiersOptions? usageTiers, bool flatCapEnabled, decimal global, bool globalOverCap,
        bool allowanceExhausted)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>())
            .Returns((ArmenianAiToy.Domain.Entities.Child?)null);
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(true);
        deviceService.GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(Status(allowanceExhausted));
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(Encoding.UTF8.GetBytes("canned"), "audio/mpeg"));

        var costMeter = new OpenAICostMeter();
        if (globalOverCap) costMeter.Record(Guid.NewGuid(), 1.50m, DateTime.UtcNow);
        var costCapOptions = Options.Create(new OpenAIDailyCostCapOptions
        {
            Enabled = flatCapEnabled, Default = 0.50m, Global = global,
        });
        var deviceId = Guid.NewGuid();

        var controller = new StoryQaController(
            transcription, synthesis, new InMemoryCuratedStoryLibrary(),
            new LibraryStoryQuestionService(aiChatClient), Substitute.For<IModerationService>(),
            Substitute.For<IConversationService>(), childService, deviceService,
            new CannedVoiceClips(synthesis), costMeter, costCapOptions,
            Substitute.For<IWebHostEnvironment>(), Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<StoryQaController>>(),
            reflectionDialogue: null, usageTiersOptions: usageTiers);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return new Harness(controller, deviceService, transcription, deviceId);
    }

    private static Task<IActionResult> Invoke(Harness h, bool reflection) =>
        reflection
            ? h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None)
            : h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiersOn_GlobalCeilingTripped_StopsTurn_AllowanceNeverConsulted(bool reflection)
    {
        using var capture = MetricCapture.Start();
        var h = Create(TiersOn(), flatCapEnabled: true, global: 1.00m, globalOverCap: true,
            allowanceExhausted: false);

        var result = await Invoke(h, reflection);

        Assert.IsType<FileContentResult>(result); // the canned "paused" clip
        await h.DeviceService.DidNotReceive().GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
        await h.Transcription.DidNotReceiveWithAnyArgs().TranscribeArmenianAsync(
            default!, default!, default, default, default);
        Assert.Equal(1, capture.CapTrips("story_qa"));
        Assert.Equal(0, capture.AllowanceExhaustions());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiersOn_GlobalZero_StillUsesTheAllowancePath(bool reflection)
    {
        using var capture = MetricCapture.Start();
        // Global <= 0 means "no fleet ceiling": a big fleet total must not matter.
        var h = Create(TiersOn(), flatCapEnabled: true, global: 0m, globalOverCap: true,
            allowanceExhausted: true);

        var result = await Invoke(h, reflection);

        Assert.IsType<FileContentResult>(result);
        await h.DeviceService.Received(1).GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>());
        Assert.Equal(1, capture.AllowanceExhaustions());
        Assert.Equal(0, capture.CapTrips("story_qa"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiersOn_FlatCapDisabled_GlobalCeilingIsNotConsulted(bool reflection)
    {
        using var capture = MetricCapture.Start();
        // The kill-switch is part of the flat-cap feature: Enabled=false switches it off too.
        var h = Create(TiersOn(), flatCapEnabled: false, global: 1.00m, globalOverCap: true,
            allowanceExhausted: true);

        var result = await Invoke(h, reflection);

        Assert.IsType<FileContentResult>(result);
        await h.DeviceService.Received(1).GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>());
        Assert.Equal(1, capture.AllowanceExhaustions());
        Assert.Equal(0, capture.CapTrips("story_qa"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlagOff_GlobalCeilingTripped_StillStopsTurn_AllowanceNeverConsulted(bool reflection)
    {
        // Regression guard: flag-off behaviour is byte-identical to before the hoist.
        using var capture = MetricCapture.Start();
        var h = Create(usageTiers: null, flatCapEnabled: true, global: 1.00m, globalOverCap: true,
            allowanceExhausted: false);

        var result = await Invoke(h, reflection);

        Assert.IsType<FileContentResult>(result);
        await h.DeviceService.DidNotReceive().GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
        Assert.Equal(1, capture.CapTrips("story_qa"));
        Assert.Equal(0, capture.AllowanceExhaustions());
    }
}

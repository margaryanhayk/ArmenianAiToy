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
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Serial xUnit collection for the metric-capture tests below — see
/// <c>ModerationFailClosedMetricsTests</c> for why <see cref="MeterListener"/>
/// tests must not run concurrently with each other.
/// </summary>
[CollectionDefinition(nameof(UsageTiersMetricsCollection), DisableParallelization = true)]
public class UsageTiersMetricsCollection { }

/// <summary>
/// Usage-tier metering foundation (2026-09-11, ships behind
/// <c>Usage:Tiers:Enabled</c>=false) — the gate contract added to
/// <see cref="ChatController"/>, <see cref="AudioChatController"/> and
/// <see cref="StoryQaController"/> (<c>Ask</c> and <c>AnswerReflection</c>).
/// <para>
/// Pins two things per controller: (1) with the flag OFF, the per-tier
/// allowance is never even consulted — the flat
/// <see cref="OpenAIDailyCostCapOptions"/> dollar cap keeps governing,
/// byte-identical to before this feature; (2) with the flag ON, an
/// exhausted allowance trips the SAME canned response the flat cap already
/// used, increments <c>aat_usage_allowance_exhausted_total{tier}</c>, and
/// never calls the paid upstream service.
/// </para>
/// </summary>
[Collection(nameof(UsageTiersMetricsCollection))]
public class UsageTiersGateTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private static readonly byte[] InboundWav = Encoding.UTF8.GetBytes("RIFF marker");

    private sealed class MetricCapture : IDisposable
    {
        public List<(long Value, string? Tier)> Measurements { get; } = new();
        private readonly MeterListener _listener;

        private MetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == AppMeter.Name
                        && instrument.Name == "aat_usage_allowance_exhausted_total")
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                string? tier = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "tier") { tier = tag.Value as string; break; }
                }
                lock (Measurements) { Measurements.Add((measurement, tier)); }
            });
            _listener.Start();
        }

        public static MetricCapture Start() => new();
        public void Dispose() => _listener.Dispose();
    }

    private static UsageTiersOptions EnabledOptions(bool exhausted, string tier = "free") =>
        new() { Enabled = true, Plans = new() { new UsageTierPlan { Name = tier, QuestionsPerDay = exhausted ? 0 : 999 } } };

    private static UsageAllowanceStatus ExhaustedStatus(string tier = "free") =>
        new(tier, QuestionsToday: 30, QuestionsThisMonth: 30, AllowanceToday: 30, AllowanceThisMonth: null, IsExhausted: true);

    // ================= ChatController =================

    private sealed record ChatHarness(ChatController Controller, IChatService ChatService, IDeviceService DeviceService, Guid DeviceId);

    private static ChatHarness CreateChat(UsageTiersOptions? usageTiers = null, bool flatCapTripped = false)
    {
        var chatService = Substitute.For<IChatService>();
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        var costMeter = new OpenAICostMeter();
        var costCapOptions = Options.Create(new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.50m });
        var deviceId = Guid.NewGuid();
        if (flatCapTripped) costMeter.Record(deviceId, 0.51m, DateTime.UtcNow);
        var controller = new ChatController(
            chatService, deviceService, costMeter, costCapOptions,
            Substitute.For<ILogger<ChatController>>(), usageTiers);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return new ChatHarness(controller, chatService, deviceService, deviceId);
    }

    [Fact]
    public async Task Chat_FlagOff_FlatCapTripped_AllowanceNeverConsulted()
    {
        var h = CreateChat(usageTiers: null, flatCapTripped: true);

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        var payload = Assert.IsType<ChatResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(ChatController.CostCapResponse, payload.Response);
        await h.DeviceService.DidNotReceive().GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
        await h.ChatService.DidNotReceive().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Chat_FlagOn_AllowanceExhausted_ReturnsCanned_IncrementsMetric()
    {
        using var capture = MetricCapture.Start();
        var h = CreateChat(usageTiers: EnabledOptions(exhausted: true));
        h.DeviceService.GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(ExhaustedStatus("free"));

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        var payload = Assert.IsType<ChatResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(ChatController.CostCapResponse, payload.Response);
        Assert.Equal(SafetyFlag.Clean, payload.SafetyFlag);
        await h.ChatService.DidNotReceive().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
        Assert.Single(capture.Measurements);
        Assert.Equal("free", capture.Measurements[0].Tier);
    }

    [Fact]
    public async Task Chat_FlagOn_AllowanceNotExhausted_Proceeds_AndRecordsUsage()
    {
        var h = CreateChat(usageTiers: EnabledOptions(exhausted: false));
        h.DeviceService.GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(new UsageAllowanceStatus("free", 1, 1, 999, null, false));
        h.ChatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("Կար մի կատու։", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        var payload = Assert.IsType<ChatResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.NotEqual(ChatController.CostCapResponse, payload.Response);
        await h.DeviceService.Received(1).RecordUsageQuestionAsync(h.DeviceId, Arg.Any<decimal>(), Arg.Any<DateTime>());
    }

    // ================= AudioChatController =================

    private sealed record AudioHarness(AudioChatController Controller, IDeviceService DeviceService, Guid DeviceId);

    private static AudioHarness CreateAudio(UsageTiersOptions? usageTiers, bool flatCapTripped)
    {
        var chatService = Substitute.For<IChatService>();
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DetectedMode>()).Returns(true);
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        // The gate's canned-clip response renders through CannedVoiceClips,
        // which calls this on the first hit — must be wired even though the
        // gate tests never reach the real STT/TTS turn.
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(Encoding.UTF8.GetBytes("canned"), "audio/mpeg"));
        var canned = new CannedVoiceClips(synthesis);
        var costMeter = new OpenAICostMeter();
        var costCapOptions = Options.Create(new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.50m });
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName = "Development";
        var config = Substitute.For<IConfiguration>();
        var deviceId = Guid.NewGuid();
        if (flatCapTripped) costMeter.Record(deviceId, 0.51m, DateTime.UtcNow);

        // A minimal AppDbContext isn't needed for the gate path — the
        // controller only touches _db after STT, which the exhausted /
        // flat-cap-tripped paths below never reach.
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var controller = new AudioChatController(
            chatService, deviceService, transcription, synthesis, new NullBlobStore(),
            canned, db, costMeter, costCapOptions, env, config,
            Substitute.For<ILogger<AudioChatController>>(), usageTiers);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        httpContext.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return new AudioHarness(controller, deviceService, deviceId);
    }

    private sealed class NullBlobStore : IAudioBlobStore
    {
        public Task<string> WriteAsync(Guid conversationId, Guid messageId, byte[] content, string mimeType, CancellationToken ct = default)
            => Task.FromResult("x");
        public Task<(Stream Content, string MimeType)?> ReadAsync(Guid conversationId, Guid messageId, CancellationToken ct = default)
            => Task.FromResult<(Stream, string)?>(null);
        public Task<AudioBlobDeleteResult> DeleteConversationAudioAsync(Guid conversationId, CancellationToken ct = default)
            => Task.FromResult(new AudioBlobDeleteResult(0, true, false, null));
    }

    [Fact]
    public async Task Audio_FlagOff_FlatCapTripped_AllowanceNeverConsulted()
    {
        var h = CreateAudio(usageTiers: null, flatCapTripped: true);

        var result = await h.Controller.Chat(CancellationToken.None);

        Assert.IsType<FileContentResult>(result); // canned clip served as a file
        await h.DeviceService.DidNotReceive().GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task Audio_FlagOn_AllowanceExhausted_ReturnsCanned_IncrementsMetric_NoSttCall()
    {
        using var capture = MetricCapture.Start();
        var h = CreateAudio(usageTiers: EnabledOptions(exhausted: true), flatCapTripped: false);
        h.DeviceService.GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(ExhaustedStatus("free"));

        var result = await h.Controller.Chat(CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        Assert.Single(capture.Measurements);
        Assert.Equal("free", capture.Measurements[0].Tier);
    }

    // ================= StoryQaController (Ask + AnswerReflection) =================

    private sealed record StoryQaHarness(StoryQaController Controller, IDeviceService DeviceService, Guid DeviceId);

    private static StoryQaHarness CreateStoryQa(UsageTiersOptions? usageTiers, bool flatCapTripped)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((ArmenianAiToy.Domain.Entities.Child?)null);
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story).Returns(true);
        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AudioSynthesisResult(Encoding.UTF8.GetBytes("canned"), "audio/mpeg"));
        var library = new InMemoryCuratedStoryLibrary();
        var questions = new LibraryStoryQuestionService(aiChatClient);
        var costMeter = new OpenAICostMeter();
        var costCapOptions = Options.Create(new OpenAIDailyCostCapOptions { Enabled = true, Default = 0.50m });
        var deviceId = Guid.NewGuid();
        if (flatCapTripped) costMeter.Record(deviceId, 0.51m, DateTime.UtcNow);
        var canned = new CannedVoiceClips(synthesis);

        var controller = new StoryQaController(
            transcription, synthesis, library, questions, moderation, conversations,
            childService, deviceService, canned, costMeter, costCapOptions,
            Substitute.For<IWebHostEnvironment>(), Substitute.For<IConfiguration>(),
            Substitute.For<ILogger<StoryQaController>>(),
            reflectionDialogue: null, usageTiersOptions: usageTiers);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = deviceId;
        httpContext.Request.Body = new MemoryStream(InboundWav);
        httpContext.Request.ContentType = "audio/wav";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return new StoryQaHarness(controller, deviceService, deviceId);
    }

    [Fact]
    public async Task Ask_FlagOff_FlatCapTripped_AllowanceNeverConsulted()
    {
        var h = CreateStoryQa(usageTiers: null, flatCapTripped: true);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.DeviceService.DidNotReceive().GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task Ask_FlagOn_AllowanceExhausted_ReturnsCanned_IncrementsMetric()
    {
        using var capture = MetricCapture.Start();
        var h = CreateStoryQa(usageTiers: EnabledOptions(exhausted: true), flatCapTripped: false);
        h.DeviceService.GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(ExhaustedStatus("free"));

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        Assert.Single(capture.Measurements);
        Assert.Equal("free", capture.Measurements[0].Tier);
    }

    [Fact]
    public async Task AnswerReflection_FlagOff_FlatCapTripped_AllowanceNeverConsulted()
    {
        var h = CreateStoryQa(usageTiers: null, flatCapTripped: true);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        await h.DeviceService.DidNotReceive().GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task AnswerReflection_FlagOn_AllowanceExhausted_ReturnsCanned_IncrementsMetric()
    {
        using var capture = MetricCapture.Start();
        var h = CreateStoryQa(usageTiers: EnabledOptions(exhausted: true), flatCapTripped: false);
        h.DeviceService.GetUsageAllowanceStatusAsync(h.DeviceId, Arg.Any<DateTime>())
            .Returns(ExhaustedStatus("free"));

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        Assert.Single(capture.Measurements);
        Assert.Equal("free", capture.Measurements[0].Tier);
    }
}

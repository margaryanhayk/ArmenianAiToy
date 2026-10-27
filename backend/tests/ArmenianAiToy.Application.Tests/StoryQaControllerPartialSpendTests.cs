using System.Text;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
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
/// AI logic review 2026-10-27, B8 — the story-qa sites. A turn that failed
/// AFTER a paid upstream call (STT, then the answer model, then TTS) used to
/// return before the cost block, so the daily cost meter and the usage-tier
/// counter never saw money that had really been spent. The three failure
/// returns now record the spend that actually happened, behind the same
/// either-flag gate as the success block:
/// <list type="bullet">
/// <item><c>Ask</c>, answer model threw: STT only.</item>
/// <item><c>Ask</c>, answer TTS threw: STT + chat.</item>
/// <item><c>AnswerReflection</c>, reaction clip render threw: STT + reaction.</item>
/// </list>
/// An STT failure spends nothing billable and must record nothing.
/// </summary>
public class StoryQaControllerPartialSpendTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private const string Question = "Ո՞վ է փոքրիկ ամպիկը";
    private const string ModelAnswer = "Փոքրիկ ամպիկը երկնքի ընկերն է։";
    private const string ValidReaction = "Ի՜նչ ջերմ պատասխան էր։ Օգնելը իսկապես ուրախացնում է։";

    // One second of 16 kHz / 16-bit mono: a clearly positive STT estimate.
    private static readonly byte[] InboundWav = new byte[32_000];

    private sealed record Harness(
        StoryQaController Controller,
        IAudioTranscriptionService Transcription,
        OpenAICostMeter CostMeter,
        List<decimal> UsageCosts,
        Guid DeviceId);

    /// <param name="failSynthesisFor">When set, a TTS call for exactly this
    /// text throws; every other text renders normally.</param>
    /// <param name="emptyStory">Serve a story with no segments, which makes
    /// the real answer service throw (its prompt builder rejects index 0).</param>
    private static Harness Create(
        bool costCapEnabled, bool tiersEnabled, string? failSynthesisFor = null, bool emptyStory = false)
    {
        var transcription = Substitute.For<IAudioTranscriptionService>();
        var synthesis = Substitute.For<IAudioSynthesisService>();
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var reflectionAi = Substitute.For<IAiChatClient>();
        var conversations = Substitute.For<IConversationService>();
        var childService = Substitute.For<IChildService>();
        childService.GetDefaultChildForDeviceAsync(Arg.Any<Guid>()).Returns((Child?)null);
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.HasLinkedParentAsync(Arg.Any<Guid>()).Returns(true);
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        deviceService.IsModeEnabledForRequestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(true);
        deviceService.GetUsageAllowanceStatusAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(new UsageAllowanceStatus("free", 0, 0, 999, null, IsExhausted: false));
        var usageCosts = new List<decimal>();
        deviceService
            .When(d => d.RecordUsageQuestionAsync(Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<DateTime>()))
            .Do(ci => { lock (usageCosts) usageCosts.Add(ci.ArgAt<decimal>(1)); });

        synthesis.SynthesizeArmenianAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var text = ci.ArgAt<string>(0);
                if (failSynthesisFor is not null && text.TrimEnd() == failSynthesisFor)
                {
                    throw new HttpRequestException("tts down");
                }
                return new AudioSynthesisResult(Encoding.UTF8.GetBytes("TTS::" + text), "audio/mpeg");
            });
        transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(Question);
        moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: true, FlaggedCategories: new List<string>()));
        aiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ModelAnswer);
        reflectionAi.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ValidReaction);

        var deviceId = Guid.NewGuid();
        conversations.GetOrCreateActiveConversationAsync(Arg.Any<Guid>(), Arg.Any<Guid?>())
            .Returns(new Conversation { Id = Guid.NewGuid(), DeviceId = deviceId, StartedAt = DateTime.UtcNow });
        conversations.AddMessageAsync(
                Arg.Any<Guid>(), Arg.Any<MessageRole>(), Arg.Any<string>(), Arg.Any<SafetyFlag>())
            .Returns(new Message { Id = Guid.NewGuid() });

        ICuratedStoryLibrary library = new InMemoryCuratedStoryLibrary();
        if (emptyStory)
        {
            var real = library.GetById(StoryId)!;
            var stub = Substitute.For<ICuratedStoryLibrary>();
            stub.GetById(Arg.Any<string>())
                .Returns(real with { Segments = Array.Empty<CuratedStorySegment>() });
            library = stub;
        }

        var costMeter = new OpenAICostMeter();
        var controller = new StoryQaController(
            transcription, synthesis, library, new LibraryStoryQuestionService(aiChatClient),
            moderation, conversations, childService, deviceService, new CannedVoiceClips(synthesis),
            costMeter, Options.Create(new OpenAIDailyCostCapOptions { Enabled = costCapEnabled }),
            Substitute.For<IWebHostEnvironment>(), new ConfigurationBuilder().Build(),
            Substitute.For<ILogger<StoryQaController>>(),
            new ReflectionDialogueService(reflectionAi),
            tiersEnabled
                ? new UsageTiersOptions
                {
                    Enabled = true,
                    Plans = new() { new UsageTierPlan { Name = "free", QuestionsPerDay = 999 } },
                }
                : null);

        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        http.Request.Body = new MemoryStream(InboundWav);
        http.Request.ContentType = "audio/wav";
        http.Request.ContentLength = InboundWav.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return new Harness(controller, transcription, costMeter, usageCosts, deviceId);
    }

    private static void SttDown(Harness h) =>
        h.Transcription.TranscribeArmenianAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns<string>(_ => throw new HttpRequestException("stt down"));

    private static decimal SttCost() => OpenAICostEstimator.EstimateWhisperCostUsd(InboundWav.LongLength);

    /// <summary>Whichever flag is on must see exactly one positive record;
    /// the flat-cap meter moves only when the flat cap is on.</summary>
    private static void AssertRecordedOnce(Harness h, bool costCapEnabled, out decimal cost)
    {
        cost = Assert.Single(h.UsageCosts);
        Assert.True(cost > 0m, "the recorded spend must be positive");
        var meterTotal = h.CostMeter.GetCurrentTotal(h.DeviceId, DateTime.UtcNow);
        Assert.Equal(costCapEnabled ? cost : 0m, meterTotal);
    }

    private static void AssertNothingRecorded(Harness h)
    {
        Assert.Empty(h.UsageCosts);
        Assert.Equal(0m, h.CostMeter.GetCurrentTotal(h.DeviceId, DateTime.UtcNow));
    }

    // ---------------- Ask ----------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ask_SttThrows_RecordsNothing(bool costCapEnabled, bool tiersEnabled)
    {
        var h = Create(costCapEnabled, tiersEnabled);
        SttDown(h);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result); // spoken failure clip
        AssertNothingRecorded(h);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ask_AnswerModelThrows_RecordsTheSttSpend(bool costCapEnabled, bool tiersEnabled)
    {
        var h = Create(costCapEnabled, tiersEnabled, emptyStory: true);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result); // spoken failure clip
        AssertRecordedOnce(h, costCapEnabled, out var cost);
        Assert.Equal(SttCost(), cost); // STT only: the failed model call produced nothing billable
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ask_AnswerTtsThrows_RecordsSttAndChatSpend(bool costCapEnabled, bool tiersEnabled)
    {
        var h = Create(costCapEnabled, tiersEnabled, failSynthesisFor: ModelAnswer);

        var result = await h.Controller.Ask(StoryId, offset: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result); // spoken failure clip
        AssertRecordedOnce(h, costCapEnabled, out var cost);
        Assert.True(cost > SttCost(), "the answer-model call was billed and must be included");
    }

    // ---------------- AnswerReflection ----------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Reflection_SttThrows_RecordsNothing(bool costCapEnabled, bool tiersEnabled)
    {
        var h = Create(costCapEnabled, tiersEnabled);
        SttDown(h);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result); // spoken failure clip
        AssertNothingRecorded(h);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Reflection_ReactionClipRenderThrows_RecordsSttAndReactionSpend(
        bool costCapEnabled, bool tiersEnabled)
    {
        var h = Create(costCapEnabled, tiersEnabled, failSynthesisFor: ValidReaction);

        var result = await h.Controller.AnswerReflection(StoryId, questionIndex: 0, CancellationToken.None);

        Assert.IsType<FileContentResult>(result); // spoken failure clip
        AssertRecordedOnce(h, costCapEnabled, out var cost);
        Assert.True(cost > SttCost(), "the reaction model call was billed and must be included");
    }
}

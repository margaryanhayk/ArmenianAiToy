using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Dual-moderation contract for the TEXT in-story Q&amp;A harness
/// (<c>POST /api/story-qa-text</c>), plus its Development-only gate.
/// <para>
/// This endpoint is unauthenticated by design, so before this suite
/// existed it was the one child-facing pipeline in the repo that reached
/// GPT with no safety classifier on either side —
/// <see cref="LibraryStoryQuestionService"/> takes only an
/// <see cref="IAiChatClient"/>, and <see cref="StoryAnswerFilter"/>
/// judges story fidelity and format, not safety. These tests pin that it
/// now behaves exactly like the voice path
/// (<see cref="StoryQaControllerModerationTests"/>): input moderated
/// BEFORE any model call, model-authored output moderated after.
/// </para>
/// <para>
/// No real network: moderation and the answer model are substituted. The
/// question service is REAL over the substituted model, so "was GPT
/// called?" is decided by the controller's own moderation branch rather
/// than by a mock of the service.
/// </para>
/// </summary>
public class StoryQaTextControllerModerationTests
{
    private const string StoryId = InMemoryCuratedStoryLibrary.LittleCloudId;
    private const string SafeQuestion = "Ո՞վ է փոքրիկ ամպիկը";

    /// <summary>Pure-Armenian, grounded, marker-free — clears
    /// <see cref="StoryAnswerFilter"/> so the answer counts as
    /// model-authored and therefore reaches OUTPUT moderation.</summary>
    private const string ModelAuthoredAnswer = "Փոքրիկ ամպիկը երկնքի ընկերն է։";

    /// <summary>Trips <see cref="StoryAnswerRejection.PromptLeakage"/> on
    /// both the first call and the repair retry, so the service returns
    /// its canned fallback (<c>UsedFallback: true</c>).</summary>
    private const string FilterRejectedAnswer = "As an AI language model, I cannot answer that.";

    private sealed record Harness(
        StoryQaTextController Controller,
        IModerationService Moderation,
        IAiChatClient AiChatClient);

    private static Harness Create(string environmentName = "Development")
    {
        var moderation = Substitute.For<IModerationService>();
        var aiChatClient = Substitute.For<IAiChatClient>();
        var library = new InMemoryCuratedStoryLibrary();
        var questions = new LibraryStoryQuestionService(aiChatClient);
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        var logger = Substitute.For<ILogger<StoryQaTextController>>();

        var controller = new StoryQaTextController(library, questions, moderation, env, logger);
        return new Harness(controller, moderation, aiChatClient);
    }

    private static void WireModeration(Harness h, bool inputSafe, bool outputSafe = true)
    {
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(ci => ci.ArgAt<string>(0) == SafeQuestion
                ? new ModerationResult(inputSafe, inputSafe ? [] : ["violence"])
                : new ModerationResult(outputSafe, outputSafe ? [] : ["self-harm"]));
    }

    private static void WireModel(Harness h, string answer) =>
        h.AiChatClient.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(answer);

    private static StoryQaTextController.QaTextResponse Body(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<StoryQaTextController.QaTextResponse>(ok.Value);
    }

    private static Task<IActionResult> Ask(Harness h, string question) =>
        h.Controller.Ask(
            new StoryQaTextController.QaTextRequest(StoryId, Segment: 0, question),
            CancellationToken.None);

    // --- KEYSTONE: unsafe input never reaches the model ---------------

    [Fact]
    public async Task UnsafeQuestion_NeverCallsGpt_ReturnsSafeFallback()
    {
        var h = Create();
        WireModeration(h, inputSafe: false);
        WireModel(h, ModelAuthoredAnswer);

        var body = Body(await Ask(h, SafeQuestion));

        await h.AiChatClient.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!);
        Assert.Equal(StoryAnswerFilter.SafeFallback, body.Answer);
        Assert.True(body.UsedFallback);
        Assert.Equal("moderation_blocked", body.FirstRejection);
    }

    /// <summary>Fail-closed: the adapter reports an infra failure as
    /// <c>IsSafe=false</c> with this sentinel, and it must be treated as
    /// unsafe rather than waved through.</summary>
    [Fact]
    public async Task ModerationUnavailable_TreatedAsUnsafe_NeverCallsGpt()
    {
        var h = Create();
        h.Moderation.CheckContentAsync(Arg.Any<string>())
            .Returns(new ModerationResult(IsSafe: false, ["moderation_unavailable"]));
        WireModel(h, ModelAuthoredAnswer);

        var body = Body(await Ask(h, SafeQuestion));

        await h.AiChatClient.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!);
        Assert.Equal(StoryAnswerFilter.SafeFallback, body.Answer);
        Assert.True(body.UsedFallback);
    }

    // --- KEYSTONE: unsafe model-authored output is never returned -----

    [Fact]
    public async Task UnsafeAnswer_IsOutputModerated_ReturnsFallback_NotTheAnswer()
    {
        var h = Create();
        WireModeration(h, inputSafe: true, outputSafe: false);
        WireModel(h, ModelAuthoredAnswer);

        var body = Body(await Ask(h, SafeQuestion));

        // The model DID answer, and that answer was suppressed.
        await h.AiChatClient.ReceivedWithAnyArgs().GetCompletionAsync(default!, default!);
        Assert.DoesNotContain("ամպիկը երկնքի", body.Answer);
        Assert.Equal(StoryAnswerFilter.SafeFallback, body.Answer);
        Assert.True(body.UsedFallback);
        Assert.Equal("output_blocked", body.FirstRejection);
    }

    // --- Happy path: BOTH directions classified ----------------------

    [Fact]
    public async Task SafeQuestion_AndSafeAnswer_ModeratesInputAndOutput_ReturnsAnswer()
    {
        var h = Create();
        WireModeration(h, inputSafe: true, outputSafe: true);
        WireModel(h, ModelAuthoredAnswer);

        var body = Body(await Ask(h, SafeQuestion));

        await h.Moderation.Received(1).CheckContentAsync(SafeQuestion);
        await h.Moderation.Received(1).CheckContentAsync(ModelAuthoredAnswer);
        Assert.Equal(ModelAuthoredAnswer, body.Answer);
        Assert.False(body.UsedFallback);
    }

    /// <summary>The canned fallback is pre-reviewed text, so the output
    /// classifier is skipped for it — exactly one moderation call (the
    /// input) on this path. Pins the latency optimization so a future
    /// edit cannot quietly turn it into "skip output moderation always".
    /// </summary>
    [Fact]
    public async Task CannedFallbackAnswer_SkipsOutputModeration()
    {
        var h = Create();
        WireModeration(h, inputSafe: true, outputSafe: true);
        WireModel(h, FilterRejectedAnswer);

        var body = Body(await Ask(h, SafeQuestion));

        Assert.True(body.UsedFallback);
        Assert.Equal(StoryAnswerFilter.SafeFallback, body.Answer);
        await h.Moderation.Received(1).CheckContentAsync(Arg.Any<string>());
    }

    // --- Development-only gate ---------------------------------------

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopmentEnvironment_Returns404_WithoutTouchingModerationOrGpt(
        string environmentName)
    {
        var h = Create(environmentName);
        WireModeration(h, inputSafe: true);
        WireModel(h, ModelAuthoredAnswer);

        var result = await Ask(h, SafeQuestion);

        Assert.IsType<NotFoundResult>(result);
        await h.Moderation.DidNotReceiveWithAnyArgs().CheckContentAsync(default!);
        await h.AiChatClient.DidNotReceiveWithAnyArgs().GetCompletionAsync(default!, default!);
    }

    [Fact]
    public async Task Development_ReachesThePipeline()
    {
        var h = Create("Development");
        WireModeration(h, inputSafe: true);
        WireModel(h, ModelAuthoredAnswer);

        var result = await Ask(h, SafeQuestion);

        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>The 404 gate precedes request validation, so a malformed
    /// request outside Development cannot distinguish "route exists but
    /// your body was wrong" from "no such route".</summary>
    [Fact]
    public async Task NonDevelopment_MalformedRequest_Returns404_NotBadRequest()
    {
        var h = Create("Production");

        var result = await h.Controller.Ask(
            new StoryQaTextController.QaTextRequest(StoryId, Segment: 0, Question: "   "),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}

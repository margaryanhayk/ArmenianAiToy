using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Reflection-dialogue reaction service: the one GPT surface of the
/// after-story flow. Pins the validate → repair-once → null-fallback
/// discipline (a null Text means "caller MUST use its deterministic
/// acknowledgement") and that a model failure never throws.
/// </summary>
public class ReflectionDialogueTests
{
    private static readonly CuratedStory Story = new(
        "test-story", "Թեստ", 4, 7, "warm",
        [new CuratedStorySegment(0, "Մի անգամ մի փոքրիկ տղա կար։")],
        "Վերջ։",
        ["Ի՞նչ սովորեցինք։", "Իսկ դո՞ւ ինչ կանեիր։"],
        BedtimeSafe: true)
    {
        Lesson = "Բարությունը միշտ հաղթում է։",
        ReflectionConclusions = ["Բարի լինելը կարևոր է։", "Ամեն մեկս մեր ձևով ենք օգնում։"],
    };

    private const string ValidReaction = "Ի՜նչ լավ միտք ասացիր։ Բարությունը հենց այդպես է աշխատում։";

    [Fact]
    public async Task ValidModelReply_ReturnsReaction()
    {
        var ai = Substitute.For<IAiChatClient>();
        ai.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ValidReaction);

        var result = await new ReflectionDialogueService(ai).ReactAsync(Story, 0, "բարի լինել");

        Assert.Equal(ValidReaction, result.Text);
        Assert.Equal(StoryAnswerRejection.None, result.FirstRejection);
        Assert.Null(result.RetryRejection);
    }

    // KEYSTONE: an invalid first reply triggers exactly ONE stricter repair
    // retry; a valid retry is used.
    [Fact]
    public async Task InvalidFirst_ValidRetry_UsesRetry()
    {
        var ai = Substitute.For<IAiChatClient>();
        ai.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("This is English, rejected", ValidReaction);

        var result = await new ReflectionDialogueService(ai).ReactAsync(Story, 0, "պատասխան");

        Assert.Equal(ValidReaction, result.Text);
        Assert.Equal(StoryAnswerRejection.NotArmenian, result.FirstRejection);
        Assert.Equal(StoryAnswerRejection.None, result.RetryRejection);
        await ai.Received(2).GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>());
    }

    // KEYSTONE: both replies invalid → null Text — the caller's
    // deterministic acknowledgement is the only thing the child hears.
    [Fact]
    public async Task BothInvalid_ReturnsNullText()
    {
        var ai = Substitute.For<IAiChatClient>();
        ai.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns("system prompt leakage", "you are wrong, in English");

        var result = await new ReflectionDialogueService(ai).ReactAsync(Story, 0, "պատասխան");

        Assert.Null(result.Text);
        Assert.NotEqual(StoryAnswerRejection.None, result.FirstRejection);
        Assert.NotNull(result.RetryRejection);
        Assert.NotEqual(StoryAnswerRejection.None, result.RetryRejection);
    }

    [Fact]
    public async Task ModelThrows_ReturnsNullText_NeverThrows()
    {
        var ai = Substitute.For<IAiChatClient>();
        ai.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("upstream down"));

        var result = await new ReflectionDialogueService(ai).ReactAsync(Story, 1, "պատասխան");

        Assert.Null(result.Text);
    }

    [Fact]
    public async Task QuestionIndexOutOfRange_Throws()
    {
        var svc = new ReflectionDialogueService(Substitute.For<IAiChatClient>());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.ReactAsync(Story, 2, "x"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.ReactAsync(Story, -1, "x"));
    }

    // The system prompt must ground the model in THIS question and demand
    // the no-grading / no-questions-back / Armenian-only contract.
    [Fact]
    public async Task SystemPrompt_CarriesQuestionAndGuardrails()
    {
        string? seenPrompt = null;
        var ai = Substitute.For<IAiChatClient>();
        ai.GetCompletionAsync(Arg.Any<string>(), Arg.Any<List<(string, string)>>())
            .Returns(ci => { seenPrompt = ci.ArgAt<string>(0); return Task.FromResult(ValidReaction); });

        await new ReflectionDialogueService(ai).ReactAsync(Story, 1, "պատասխան");

        Assert.NotNull(seenPrompt);
        Assert.Contains("Իսկ դո՞ւ ինչ կանեիր։", seenPrompt);
        Assert.Contains("NEVER say the answer is wrong", seenPrompt);
        Assert.Contains("Do NOT ask any question back", seenPrompt);
        Assert.Contains("ARMENIAN", seenPrompt);
    }
}

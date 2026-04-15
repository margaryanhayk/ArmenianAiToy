using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B3: presence-based guards on the Game prompt constant.
/// Reads ChatService.GameModeInstruction directly (internal).
/// </summary>
public class GamePromptContentTests
{
    private static string Prompt => ChatService.GameModeInstruction;

    [Fact]
    public void Prompt_RequiresInstructionRhythm()
    {
        Assert.Contains("Instruction → short reaction → next instruction", Prompt);
        Assert.Contains("no scene-painting", Prompt);
    }

    [Fact]
    public void Prompt_RotatesActivityTypes()
    {
        Assert.Contains("Rotate activity types", Prompt);
    }

    [Fact]
    public void Prompt_ContainsArmenianExemplarTurns()
    {
        Assert.Contains("ARMENIAN EXEMPLAR TURNS", Prompt);
        Assert.Contains("Ծափ տանք միասին", Prompt);
        Assert.Contains("Դիպչիր քթիդ", Prompt);
    }

    [Fact]
    public void Prompt_BansStorybookDrift()
    {
        Assert.Contains("RESPONSE SHAPES", Prompt);
        Assert.Contains("storybook drift", Prompt);
        Assert.Contains("Պատկերացրու", Prompt);
    }

    [Fact]
    public void Prompt_PrefersBriskCelebration()
    {
        Assert.Contains("brisk celebration", Prompt);
        Assert.Contains("Ապրե՛ս", Prompt);
    }

    [Fact]
    public void Prompt_BansLectureTone()
    {
        Assert.Contains("lecture / learning-goal tone", Prompt);
        Assert.Contains("Հիմա սովորենք", Prompt);
    }

    [Fact]
    public void Prompt_ContainsChildResponseHandling()
    {
        Assert.Contains("CHILD RESPONSE HANDLING", Prompt);
        Assert.Contains("wrong or partial", Prompt);
        Assert.Contains("silence or off-topic", Prompt);
    }

    [Fact]
    public void Prompt_DiscouragesOpenEndedQuestions()
    {
        Assert.Contains("Do NOT ask open-ended questions", Prompt);
        Assert.Contains("no open-ended", Prompt);
    }

    [Fact]
    public void Prompt_PreservesModeHeader()
    {
        Assert.Contains("MODE: GAME", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoStoryRule()
    {
        Assert.Contains("Do NOT tell a story", Prompt);
    }
}

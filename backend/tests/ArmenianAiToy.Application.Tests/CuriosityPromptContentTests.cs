using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B4: presence-based guards on the Curiosity prompt constant.
/// Reads ChatService.CuriosityWindowInstruction directly (internal).
/// </summary>
public class CuriosityPromptContentTests
{
    private static string Prompt => ChatService.CuriosityWindowInstruction;

    [Fact]
    public void Prompt_RequiresKindAdultTone()
    {
        Assert.Contains("kind adult", Prompt);
        Assert.Contains("teacher", Prompt);
        Assert.Contains("Warm, not cute", Prompt);
    }

    [Fact]
    public void Prompt_BansPraiseOpeners()
    {
        Assert.Contains("praise-the-question", Prompt);
        Assert.Contains("Հիանալի հարց", Prompt);
    }

    [Fact]
    public void Prompt_ContainsArmenianExemplarAnswers()
    {
        Assert.Contains("ARMENIAN EXEMPLAR ANSWERS", Prompt);
        Assert.Contains("ջրի կաթիլների միջով", Prompt);
        Assert.Contains("Երկիրն է շրջվում", Prompt);
    }

    [Fact]
    public void Prompt_BansLectureList()
    {
        Assert.Contains("RESPONSE SHAPES", Prompt);
        Assert.Contains("lesson / list", Prompt);
    }

    [Fact]
    public void Prompt_BansTooManyFacts()
    {
        Assert.Contains("too many facts", Prompt);
        Assert.Contains("Ամպերը ջրի փոքրիկ կաթիլներ", Prompt);
    }

    [Fact]
    public void Prompt_BansDodgeNonAnswer()
    {
        Assert.Contains("dodge", Prompt);
        Assert.Contains("Չգիտեմ", Prompt);
    }

    [Fact]
    public void Prompt_ContainsStoryReturnShape()
    {
        Assert.Contains("STORY RETURN SHAPE", Prompt);
        Assert.Contains("Հիմա վերադառնանք մեր հեքիաթին", Prompt);
    }

    [Fact]
    public void Prompt_PreservesModeHeader()
    {
        Assert.Contains("MODE: CURIOSITY WINDOW", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoQuestionsBackRule()
    {
        Assert.Contains("Do NOT ask any questions back", Prompt);
    }

    [Fact]
    public void Prompt_PreservesShortAnswerBudget()
    {
        Assert.Contains("1 to 2 short sentences", Prompt);
    }
}

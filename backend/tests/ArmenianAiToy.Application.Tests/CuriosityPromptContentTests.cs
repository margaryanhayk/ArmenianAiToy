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
        // Curiosity v2 — bumped from "1 to 2" to "1 to 3" so an optional
        // analogy or fun-fact clause has room without triggering the gate.
        Assert.Contains("1 to 3 short sentences", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Curiosity Mode v2 — layered answer structure
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresLayeredAnswerStructure()
    {
        Assert.Contains("ANSWER STRUCTURE", Prompt);
        Assert.Contains("LAYER 1", Prompt);
        Assert.Contains("LAYER 2", Prompt);
    }

    [Fact]
    public void Prompt_ContainsAnalogyPolicy()
    {
        Assert.Contains("ANALOGY POLICY", Prompt);
        Assert.Contains("\u056f\u0561\u0580\u056e\u0565\u057d", Prompt); // կարծես
        Assert.Contains("\u056b\u0576\u0579\u057a\u0565\u057d", Prompt); // ինչպես
    }

    [Fact]
    public void Prompt_ContainsFunFactPolicy()
    {
        Assert.Contains("FUN FACT POLICY", Prompt);
        Assert.Contains("max 1", Prompt);
    }

    [Fact]
    public void Prompt_ContainsAntiEncyclopediaRule()
    {
        Assert.Contains("ANTI-ENCYCLOPEDIA", Prompt);
        // Forbidden encyclopedia opener fragment.
        Assert.Contains("\u0531\u0575\u057d \u0565\u0580\u0587\u0578\u0582\u0575\u0569\u0568", Prompt); // Այս երևույթը
    }

    [Fact]
    public void Prompt_ContainsExplainLikeAChildRule()
    {
        Assert.Contains("EXPLAIN-LIKE-A-CHILD", Prompt);
        Assert.Contains("One small idea per response", Prompt);
    }

    [Fact]
    public void Prompt_ListsEverydayTopics()
    {
        Assert.Contains("EVERYDAY TOPICS", Prompt);
        Assert.Contains("animals", Prompt);
        Assert.Contains("weather", Prompt);
        Assert.Contains("body", Prompt);
        Assert.Contains("food", Prompt);
    }

    [Fact]
    public void Prompt_AnalogyExemplarUsesFamiliarObject()
    {
        // Sponge analogy for clouds — concrete, familiar to a 5-year-old.
        Assert.Contains("\u057d\u057a\u0578\u0582\u0576\u0563\u056b \u0574\u0565\u057b", Prompt); // սպունգի մեջ
    }

    [Fact]
    public void Prompt_NewBadGoodPair_EncyclopediaOpener()
    {
        Assert.Contains("encyclopedia opener", Prompt);
    }
}

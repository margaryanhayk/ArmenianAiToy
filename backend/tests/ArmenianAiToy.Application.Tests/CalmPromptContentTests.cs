using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B5: presence-based guards on the Calm prompt constant.
/// Reads ChatService.CalmModeInstruction directly (internal).
/// </summary>
public class CalmPromptContentTests
{
    private static string Prompt => ChatService.CalmModeInstruction;

    [Fact]
    public void Prompt_ContainsArmenianExemplarLines()
    {
        Assert.Contains("ARMENIAN EXEMPLAR LINES", Prompt);
        Assert.Contains("Բարձիկը փափուկ է", Prompt);
        Assert.Contains("Լուսինը դուրս է եկել", Prompt);
    }

    [Fact]
    public void Prompt_BansUpbeatEnergy()
    {
        Assert.Contains("Energy stays low", Prompt);
        Assert.Contains("Ապրե՛ս", Prompt);
        Assert.Contains("Արի գնանք", Prompt);
    }

    [Fact]
    public void Prompt_BansNewTension()
    {
        Assert.Contains("new tension", Prompt);
        Assert.Contains("հանկարծ", Prompt);
    }

    [Fact]
    public void Prompt_BansImplicitQuestion()
    {
        Assert.Contains("implicit question", Prompt);
        Assert.Contains("Տեսնես", Prompt);
    }

    [Fact]
    public void Prompt_BansOverpromisingReassurance()
    {
        Assert.Contains("overpromising reassurance", Prompt);
        Assert.Contains("Բոլոր վախերդ անհետացան", Prompt);
        Assert.Contains("վերմակը տաք է", Prompt);
    }

    [Fact]
    public void Prompt_ContainsClosingPhraseShape()
    {
        Assert.Contains("CLOSING PHRASE SHAPE", Prompt);
        Assert.Contains("Քնիր հանգիստ", Prompt);
        Assert.Contains("Գիշերը հանգիստ է", Prompt);
    }

    [Fact]
    public void Prompt_ExemplarLinesHaveNoQuestionOrExclamationMarks()
    {
        var start = Prompt.IndexOf("ARMENIAN EXEMPLAR LINES");
        var end = Prompt.IndexOf("RESPONSE SHAPES", start);
        Assert.InRange(start, 0, Prompt.Length);
        Assert.InRange(end, start, Prompt.Length);
        var section = Prompt.Substring(start, end - start);
        Assert.DoesNotContain("?", section);
        Assert.DoesNotContain("՞", section);
        Assert.DoesNotContain("!", section);
        Assert.DoesNotContain("՜", section);
    }

    [Fact]
    public void Prompt_PreservesModeHeader()
    {
        Assert.Contains("MODE: CALM / BEDTIME", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoQuestionsRule()
    {
        Assert.Contains("Do NOT ask any questions", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoChoiceBlockRule()
    {
        Assert.Contains("Do NOT include a CHOICE_A / CHOICE_B block", Prompt);
    }
}

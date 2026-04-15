using System.Text.RegularExpressions;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B2: presence-based guards on the Riddle prompt constant.
/// Reads ChatService.RiddleModeInstruction directly (internal).
/// </summary>
public class RiddlePromptContentTests
{
    private static string Prompt => ChatService.RiddleModeInstruction;

    [Fact]
    public void Prompt_ContainsArmenianExemplarRiddles()
    {
        // At least one Armenian-letter clue ending in the riddle question.
        Assert.Contains("Ի՞նչ է", Prompt);
        Assert.Matches(new Regex(@"[\u0530-\u058F]"), Prompt);
    }

    [Fact]
    public void Prompt_ForbidsAnswerLeakInClue()
    {
        Assert.Contains("use the answer word", Prompt);
        Assert.Contains("ANSWER LEAK", Prompt);
    }

    [Fact]
    public void Prompt_PrefersConcreteDailyLifeNouns()
    {
        Assert.Contains("Prefer concrete daily-life nouns", Prompt);
        Assert.Contains("everyday clothing", Prompt);
    }

    [Fact]
    public void Prompt_ContainsVagueVsConcreteBadGoodPair()
    {
        Assert.Contains("VAGUE/ABSTRACT", Prompt);
        Assert.Contains("պաղպաղակ", Prompt);
    }

    [Fact]
    public void Prompt_ContainsHintExemplar()
    {
        Assert.Contains("HINT AND CELEBRATION SHAPE", Prompt);
        Assert.Contains("Մոտ ես", Prompt);
    }

    [Fact]
    public void Prompt_ContainsCelebrationExemplar()
    {
        Assert.Contains("Ապրե՛ս", Prompt);
    }

    [Fact]
    public void Prompt_StillForbidsTrickAndSphinx()
    {
        Assert.Contains("FORBIDDEN RIDDLE TYPES", Prompt);
        Assert.Contains("Sphinx", Prompt);
        Assert.Contains("GOOD RIDDLE EXAMPLES", Prompt);
    }
}

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

    // ─────────────────────────────────────────────────────────────────────
    // Riddle Mode v2 — multi-turn loop directives
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresFourTurnKinds()
    {
        Assert.Contains("new_riddle", Prompt);
        Assert.Contains("hint", Prompt);
        Assert.Contains("reveal", Prompt);
        Assert.Contains("celebrate", Prompt);
    }

    [Fact]
    public void Prompt_DefinesMetadataTailBlockShape()
    {
        Assert.Contains("RIDDLE_ANSWER:", Prompt);
        Assert.Contains("RIDDLE_CATEGORY:", Prompt);
        Assert.Contains("RIDDLE_DIFFICULTY:", Prompt);
    }

    [Fact]
    public void Prompt_HintTurnForbidsAnswerWord()
    {
        Assert.Contains("HINT TURN", Prompt);
        Assert.Contains("DO NOT name the answer", Prompt);
    }

    [Fact]
    public void Prompt_RevealAndCelebrateOfferNextRiddle()
    {
        Assert.Contains("REVEAL TURN", Prompt);
        Assert.Contains("CELEBRATE TURN", Prompt);
        Assert.Contains("\u0548\u0582\u0566\u0578\u0582\u055e\u0574 \u0565\u057d \u0587\u057d \u0574\u0565\u056f \u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f", Prompt);
    }

    [Fact]
    public void Prompt_LocksMetadataBlockToNewRiddleTurnsOnly()
    {
        // Hint / reveal / celebrate must explicitly forbid the tail block.
        Assert.Contains("DO NOT include any tail block", Prompt);
    }
}

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
    public void Prompt_HintTurnGatesCloseOnSameCategory()
    {
        // Regression for F-Rid-2: HINT TURN fairness — the «Մոտ ես»
        // encouragement must fire ONLY when the child's guess is in the
        // same category as the answer, not on any wrong guess. A bare
        // unconditional «Մոտ ես» for off-category guesses would feel
        // unfair and mislead the child. The gate lives at
        // ChatService.cs:836-838; pin it here so a silent removal of
        // the conditional fails distinctly.
        Assert.Contains("ONLY if", Prompt);
        Assert.Contains("same category", Prompt);
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
        // Regression for F-Rid-2: the anti-leak rule must cover not just
        // the bare answer word but also its stem, its plural, and any
        // close synonym — otherwise hints can accidentally reveal. The
        // full sentence at ChatService.cs:842-843 is:
        //   "DO NOT name the answer, its stem, its plural, or a close synonym."
        // Pin each distinctive token so a silent weakening of the rule
        // fails distinctly. "close synonym" is the most distinctive of
        // the three and serves as the keystone marker.
        Assert.Contains("stem", Prompt);
        Assert.Contains("plural", Prompt);
        Assert.Contains("close synonym", Prompt);
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

using System.Reflection;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B1: presence-based guards on the Story prompt constant.
/// Accessed via reflection so StoryChoiceInstruction stays private.
/// </summary>
public class StoryPromptContentTests
{
    private static string Prompt { get; } = LoadPrompt();

    private static string LoadPrompt()
    {
        var field = typeof(ChatService).GetField(
            "StoryChoiceInstruction",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null) as string;
        Assert.False(string.IsNullOrEmpty(value));
        return value!;
    }

    [Fact]
    public void OpeningVariety_SectionPresent()
    {
        Assert.Contains("OPENING VARIETY", Prompt);
    }

    [Fact]
    public void OpeningVariety_BansTimeFrameOpenersByDefault()
    {
        Assert.Contains("OVERUSED", Prompt);
        Assert.Contains("Մի անգամ", Prompt);
        Assert.Contains("Մի գեղեցիկ", Prompt);
    }

    [Fact]
    public void OpeningVariety_IncludesNewOpenerTypes()
    {
        Assert.Contains("texture/weather-sensation", Prompt);
        Assert.Contains("small surprise", Prompt);
    }

    [Fact]
    public void StoryRichness_RequiresConcreteSensory()
    {
        Assert.Contains("CONCRETE SENSORY", Prompt);
        Assert.Contains("Generic adjectives alone", Prompt);
    }

    [Fact]
    public void NoChildNarration_SectionPresent()
    {
        Assert.Contains("NO CHILD-NARRATION", Prompt);
        Assert.Contains("told TO the child", Prompt);
    }

    [Fact]
    public void ChoiceStakes_RuleRequiresDifferentOutcomes()
    {
        Assert.Contains("CHOICE STAKES", Prompt);
        Assert.Contains("change what actually", Prompt);
    }

    [Fact]
    public void TailBlockFormat_Unchanged()
    {
        Assert.Contains("CHOICE_A:short Armenian action (3–7 words)", Prompt);
        Assert.Contains("CHOICE_B:short Armenian action (3–7 words)", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // C2: compliance hardening (rhetorical-question + time-frame opener)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoRhetoricalQuestions_SectionPresent()
    {
        Assert.Contains("NO RHETORICAL QUESTIONS", Prompt);
        Assert.Contains("արդյոք", Prompt);
    }

    [Fact]
    public void NoRhetoricalQuestions_BansQuestionTailMark()
    {
        Assert.Contains("\"...թե՞\"", Prompt);
        Assert.Contains("\"...՞\"", Prompt);
    }

    [Fact]
    public void OpeningVariety_BadGoodPair_Present()
    {
        Assert.Contains("BAD (time-frame default)", Prompt);
        Assert.Contains("Փոքրիկ նապաստակը ցատկեց քարի վրայից", Prompt);
    }

    [Fact]
    public void RhetoricalQuestion_BadGoodPair_Present()
    {
        // Pin the exact leaked fragment observed in B4 QA.
        Assert.Contains("ինչու՞ է այսպես փայլում", Prompt);
        Assert.Contains("Սակայն նա զարմացավ", Prompt);
    }

    [Fact]
    public void FinalStoryCheck_SectionPresent()
    {
        Assert.Contains("FINAL STORY CHECK", Prompt);
    }

    [Fact]
    public void FinalStoryCheck_AppearsAfterStoryChoices()
    {
        var choicesIdx = Prompt.IndexOf("STORY CHOICES — ADDITIONAL RULES");
        var finalIdx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(choicesIdx >= 0, "STORY CHOICES — ADDITIONAL RULES must be present");
        Assert.True(finalIdx > choicesIdx, "FINAL STORY CHECK must appear after STORY CHOICES");
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesTimeFrameBan()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("Մի անգամ", tail);
        Assert.Contains("Մի գեղեցիկ", tail);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesRhetoricalBan()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("արդյոք", tail);
        Assert.Contains("՞", tail);
    }

    [Fact]
    public void RhetoricalQuestion_MidBodyArdyokBadGoodPair_Present()
    {
        // B1.5: pin the mid-body «արդյոք» BAD/GOOD pair so future edits
        // can't drop the explicit counter-example.
        Assert.Contains("mid-body \"արդյոք\" hedge", Prompt);
        Assert.Contains("Նա մտածում էր, արդյոք քարը կարող է կախարդական լինել", Prompt);
        Assert.Contains("քարը գուցե կախարդական է", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Choice quality + continuation coherence hardening
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChoiceDifferentiation_SectionPresent()
    {
        Assert.Contains("CHOICE DIFFERENTIATION", Prompt);
        Assert.Contains("at least TWO axes", Prompt);
    }

    [Fact]
    public void ChoiceDifferentiation_BansSameVerbSwappedNoun()
    {
        // Pin the concrete BAD/GOOD counter-examples.
        Assert.Contains("Բացենք տուփը", Prompt);
        Assert.Contains("Բացենք դուռը", Prompt);
        Assert.Contains("Կանչենք թռչունիկին", Prompt);
    }

    [Fact]
    public void PostChoiceContinuation_SectionPresent()
    {
        Assert.Contains("POST-CHOICE CONTINUATION", Prompt);
        Assert.Contains("FIRST sentence", Prompt);
        Assert.Contains("visibly act on that exact choice", Prompt);
    }

    [Fact]
    public void NoRecapAfterChoice_SectionPresent()
    {
        Assert.Contains("NO RECAP AFTER CHOICE", Prompt);
        Assert.Contains("do NOT", Prompt);
        Assert.Contains("restate", Prompt);
        Assert.Contains("paraphrase", Prompt);
    }

    [Fact]
    public void NoRecapAfterChoice_BadGoodPair_Present()
    {
        // The BAD example recaps the previous turn; the GOOD jumps straight in.
        Assert.Contains("Աղվեսը դեռ կանգնած էր ծառի մոտ", Prompt);
        Assert.Contains("Տուփը բացվեց, և ներսից դուրս թռավ", Prompt);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesChoiceDifferentiation()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("differ on verb AND target", tail);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesNoRecap()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("first sentence visibly acts on it", tail);
        Assert.Contains("NOT recap", tail);
    }
}

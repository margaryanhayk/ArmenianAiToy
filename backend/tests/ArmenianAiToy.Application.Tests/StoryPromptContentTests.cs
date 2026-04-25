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

    // ─────────────────────────────────────────────────────────────────────
    // Choice grounding + folklore-by-default guards (Phase 2 evaluator
    // findings — choice/body decoupling at 70% incidence + folklore
    // intrusion guardrail breach in fresh-conversation evidence).
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChoiceGrounding_SectionPresent()
    {
        Assert.Contains("CHOICE GROUNDING", Prompt);
        Assert.Contains("ALREADY", Prompt);
        Assert.Contains("preceding 3–5 sentences", Prompt);
    }

    [Fact]
    public void ChoiceGrounding_BansNewEntitiesOnlyInChoices()
    {
        Assert.Contains(
            "Do NOT introduce a new object, animal, person, place, or",
            Prompt);
        Assert.Contains("only inside a choice", Prompt);
        Assert.Contains("rewrite the body instead", Prompt);
    }

    [Fact]
    public void ChoiceGrounding_BadGoodPair_Present()
    {
        // Anchor the BAD example to Phase 2 case-05's signature failure
        // (snail body, choices that introduced an unheard «քար»).
        Assert.Contains("stone never appeared in body", Prompt);
        Assert.Contains("Հարցնենք մորից քարի մասին", Prompt);
        Assert.Contains("Մոտենանք երգող ձայնին", Prompt);
    }

    [Fact]
    public void NoFolkloreByDefault_SectionPresent()
    {
        Assert.Contains("NO FOLKLORE BY DEFAULT", Prompt);
        Assert.Contains("explicitly asks for", Prompt);
    }

    [Fact]
    public void NoFolkloreByDefault_PinsBannedNounList()
    {
        // Pin the explicit ban list so a future edit cannot quietly
        // drop the Armenian-specific deities or spirits from it.
        Assert.Contains("«աստված»", Prompt);
        Assert.Contains("«աստվածուհի»", Prompt);
        Assert.Contains("«ոգի»", Prompt);
        Assert.Contains("«դև»", Prompt);
        Assert.Contains("«վիշապ»", Prompt);
    }

    [Fact]
    public void NoFolkloreByDefault_BadGoodPair_Present()
    {
        // Anchor the BAD example to Phase 2 case-01's signature failure
        // (default «պատմիր հեքիաթ» produced a «ջրային աստվածուհի»
        // protagonist — the explicit folklore-postponed guardrail in
        // CLAUDE.md).
        Assert.Contains("ջրային աստվածուհի", Prompt);
        Assert.Contains("փոքրիկ սկյուռիկ", Prompt);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesChoiceGrounding()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("named in your own 3–5 sentences", tail);
        Assert.Contains("No new entities introduced only in the choices", tail);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesNoFolklore()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("No folklore figures", tail);
        Assert.Contains("explicitly asked for folklore", tail);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cat-B: anchor-resistant prompt invariant. The runtime coherence gate
    // (StoryChoiceCoherenceGate) is now responsible for catching ungrounded
    // CHOICE_A / CHOICE_B labels. To prevent the prompt from inadvertently
    // teaching the model the very nouns we expect the gate to reject, the
    // prompt must NOT contain a cluster of tempting concrete BAD-example
    // nouns — «բանալի», «թագավոր», «փերի», «կախարդական դուռ» — as inline
    // story / choice example text. The folklore-ban category list still
    // names «վիշապ» as a banned noun (intentional; that is a category
    // gate, not a story example), so this invariant deliberately excludes
    // that one and the long-standing «քարանձավ» BAD-example used elsewhere
    // in the prompt.
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("բանալի")]                                                                                                                     // բանալի
    [InlineData("թագավոր")]                                                                                              // թագավոր
    [InlineData("փերի")]                                                                                                                                                // փերի
    [InlineData("կախարդական դուռ")]   // կախարդական դուռ
    public void StoryChoiceInstruction_DoesNotAnchorOnTemptingBadExampleNouns(string badNoun)
    {
        Assert.DoesNotContain(badNoun, Prompt);
    }
}

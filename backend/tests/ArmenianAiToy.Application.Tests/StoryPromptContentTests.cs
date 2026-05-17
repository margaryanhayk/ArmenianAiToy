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

    // ─────────────────────────────────────────────────────────────────────
    // Story choices v2 — STRICT NON-NEGOTIABLES FOR CHOICES
    // (full-day slice 1: generic-motion / meta-chat / placeholder /
    //  UI-length / formal-plural bans + reiteration in FINAL STORY CHECK)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void StrictChoiceNonNegotiables_SectionPresent()
    {
        Assert.Contains("STRICT NON-NEGOTIABLES FOR CHOICES", Prompt);
    }

    [Fact]
    public void StrictChoiceNonNegotiables_BansGenericMotionOnlyPairs()
    {
        Assert.Contains("generic motion-only pairs", Prompt);
        // The pinned example list keeps the rule concrete — a future edit
        // that drops the parenthesized examples fails distinctly here.
        Assert.Contains("forward/back", Prompt);
        Assert.Contains("continue/stop", Prompt);
    }

    [Fact]
    public void StrictChoiceNonNegotiables_BansMetaChatChoices()
    {
        Assert.Contains("meta-chat choices that ask the child a", Prompt);
        Assert.Contains("ARE the continuation signal", Prompt);
    }

    [Fact]
    public void StrictChoiceNonNegotiables_BansPlaceholderLabels()
    {
        Assert.Contains("placeholder / template / null choice labels", Prompt);
        Assert.Contains("real Armenian action", Prompt);
    }

    [Fact]
    public void StrictChoiceNonNegotiables_RequiresShortUiFriendlyLabels()
    {
        // The 3-7-word rule already exists; this is the visual upper
        // bound for the ESP32 / phone display.
        Assert.Contains("60 Armenian", Prompt);
        Assert.Contains("ESP32 / phone screen", Prompt);
    }

    [Fact]
    public void StrictChoiceNonNegotiables_BansFormalPluralInChoices()
    {
        Assert.Contains("formal-plural Armenian address forms", Prompt);
        // Positive replacement target — the existing «մենք»-form choices
        // (e.g. «Բացենք փոքրիկ տուփը») are the correct register.
        Assert.Contains("«մենք»", Prompt);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesNoGenericMotionChoices()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("generic motion-only pair", tail);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesNoMetaChatChoices()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("meta-chat", tail);
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesNoPlaceholderLabels()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("real Armenian action phrase", tail);
    }

    [Fact]
    public void StoryChoiceInstruction_DoesNotContainBannedGenericMotionLiteral()
    {
        // The slice-spec literal banned phrase. The ban above is worded
        // abstractly ("generic motion-only pairs") so this Armenian
        // literal never appears in the prompt body.
        Assert.DoesNotContain("գնալ առաջ", Prompt);  // գնալ առաջ
    }

    [Fact]
    public void StoryChoiceInstruction_DoesNotContainBannedMetaChatLiteral()
    {
        // The slice-spec literal banned phrase. The ban above is worded
        // abstractly so this Armenian "do you want to continue" form
        // never appears in the prompt body.
        Assert.DoesNotContain("ուզու՞մ ես շարունակել", Prompt);  // ուզու՞մ ես շարունակել
    }

    [Fact]
    public void StoryChoiceInstruction_DoesNotContainFormalPluralAddress()
    {
        // The formal-plural Armenian pronouns must not appear anywhere
        // in the Story prompt. Worded abstractly so these literals stay
        // out and the model is not even shown the banned form.
        Assert.DoesNotContain("դուք", Prompt, StringComparison.OrdinalIgnoreCase);  // դուք / Դուք
        Assert.DoesNotContain("Ձեզ", Prompt);                                            // Ձեզ
        Assert.DoesNotContain("Ձեր", Prompt);                                            // Ձեր
    }

    // ─────────────────────────────────────────────────────────────────────
    // Story choices v3 — anchor on a NAMED entity, not a generic role word
    //
    // Anchored on the 2026-05-17 BenchmarkAll run-3 regression:
    //   Story T10 ("tell me a story about two friends") produced
    //   CHOICE_A «Մոտենանք ընկերին» when the body named the actual
    //   characters (Տիկո, Պակո). The continuation could not verbatim-
    //   anchor on a ≥4-char stem from «ընկեր» (the word never appeared
    //   in the body), tripping continuation_no_label_reference.
    //   Evidence: tools/quality-evidence/areg-live-quality-validation-20260517.md
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChoiceAnchor_RequiresNamedOrConcreteEntity()
    {
        Assert.Contains("ANCHOR ON A NAMED ENTITY", Prompt);
    }

    [Fact]
    public void ChoiceAnchor_BansGenericRolePlaceholders()
    {
        Assert.Contains("BANNED ROLE PLACEHOLDERS IN CHOICE LABELS", Prompt);
    }

    [Fact]
    public void ChoiceAnchor_PinsGoodNamedCharacterExemplar()
    {
        // «Մոտենանք Տիկոյին» — anchors on a named character whose stem
        // («Տիկո») the body already used.
        Assert.Contains("«Մոտենանք Տիկոյին»", Prompt);
    }

    [Fact]
    public void ChoiceAnchor_PinsGoodSpeciesNounExemplar()
    {
        // «Մոտենանք սկյուռիկին» — anchors on a concrete species noun
        // («սկյուռիկ») the body already used.
        Assert.Contains("«Մոտենանք սկյուռիկին»", Prompt);
    }

    [Fact]
    public void ChoiceAnchor_DoesNotContainBannedGenericChoiceLiterals()
    {
        // The specific generic-template choice phrasings the slice spec
        // calls out. The ban is worded abstractly so none of these
        // literal Armenian phrasings appear in the prompt body.
        Assert.DoesNotContain("Մոտենանք ընկերին", Prompt);    // Մոտենանք ընկերին
        Assert.DoesNotContain("Շարունակենք ճանապարհը", Prompt); // Շարունակենք ճանապարհը
        Assert.DoesNotContain("Գնանք նրա մոտ", Prompt);        // Գնանք նրա մոտ
        Assert.DoesNotContain("Խոսենք նրա հետ", Prompt);       // Խոսենք նրա հետ
    }

    [Fact]
    public void FinalStoryCheck_ReiteratesAnchorRule()
    {
        var idx = Prompt.IndexOf("FINAL STORY CHECK");
        Assert.True(idx >= 0);
        var tail = Prompt.Substring(idx);
        Assert.Contains("primary noun is a NAMED", tail);
        Assert.Contains("generic role placeholder", tail);
    }
}

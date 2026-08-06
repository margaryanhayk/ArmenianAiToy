using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Presence-based guards on the Game prompt constant.
/// Reads ChatService.GameModeInstruction directly (internal).
///
/// v6 (2026-08-05) — the taxonomy was cut from seven types to the three a
/// blind, one-button toy can actually run (animal_sound / count_to /
/// yes_no_silly), the physical-action types were structurally banned, and
/// an HONESTY block replaced unconditional celebration. Tests that pinned
/// the removed types, the guessing opener, or the multi-turn-rhythm
/// disclaimer were retired with the content they pinned.
///
/// make_it_small (2026-08-06) — a FOURTH word-answer type joined the v6
/// set: the toy names a familiar thing, the child makes it little with an
/// Armenian diminutive (կատու→կատվիկ). It inherits the v6 honesty posture
/// — the attempt is celebrated, the form is never graded, and the toy
/// models the standard word instead of drilling the child.
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
    public void Prompt_EnforcesVarietyViaAvoidList()
    {
        Assert.Contains("AVOID", Prompt);
        Assert.Contains("recent ones", Prompt);
    }

    [Fact]
    public void Prompt_ContainsArmenianExemplarTurns()
    {
        Assert.Contains("ARMENIAN EXEMPLAR TURNS", Prompt);
        Assert.Contains("Հնչեցրու կատվի ձայնը", Prompt);
        Assert.Contains("Հաշվենք մինչև հինգ", Prompt);
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
        Assert.Contains("WRONG answer", Prompt);
        Assert.Contains("cannot judge", Prompt);
    }

    [Fact]
    public void Prompt_DiscouragesOpenEndedQuestions()
    {
        Assert.Contains("Do NOT ask open-ended questions", Prompt);
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

    // ─────────────────────────────────────────────────────────────────────
    // Multi-turn loop directives
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresFourTurnKinds()
    {
        Assert.Contains("new_game", Prompt);
        Assert.Contains("continue", Prompt);
        Assert.Contains("switch_game", Prompt);
        Assert.Contains("stop_game", Prompt);
    }

    [Fact]
    public void Prompt_DefinesMetadataTailBlockShape()
    {
        Assert.Contains("GAME_TYPE:", Prompt);
        Assert.Contains("GAME_DIFFICULTY:", Prompt);
    }

    [Fact]
    public void Prompt_LocksMetadataToNewOrSwitchTurns()
    {
        Assert.Contains("DO NOT include any tail block", Prompt);
    }

    [Fact]
    public void Prompt_LocksGameTypeWhitelist_ToThePlayableSet()
    {
        // KEYSTONE (v6): only word-answer types are allowed. The four
        // physical-action types were cut because the toy cannot observe a
        // clap, a touch, or a found object — see the structural ban below.
        // make_it_small joined the set in 2026-08-06 on the same footing:
        // the answer is a WORD the child says.
        Assert.Contains("GAME TYPES", Prompt);
        Assert.Contains("animal_sound", Prompt);
        Assert.Contains("count_to", Prompt);
        Assert.Contains("yes_no_silly", Prompt);
        Assert.Contains("make_it_small", Prompt);
        Assert.Contains("Use ONLY these game types", Prompt);
    }

    [Fact]
    public void Prompt_DoesNotContainRemovedGameTypes()
    {
        // KEYSTONE (v6): the removed type tokens must be fully gone — a
        // leftover mention is a path for the model to resurrect them.
        Assert.DoesNotContain("color_find", Prompt);
        Assert.DoesNotContain("clap_along", Prompt);
        Assert.DoesNotContain("body_part", Prompt);
        Assert.DoesNotContain("copy_sound", Prompt);
    }

    [Fact]
    public void Prompt_MatchesEnforcedWhitelist()
    {
        // The prompt's advisory list and the ChatService-enforced whitelist
        // must not drift apart: every enforced type must be described.
        foreach (var t in ChatService.AllowedGameTypes)
        {
            Assert.Contains(t, Prompt);
        }
    }

    [Fact]
    public void EnforcedWhitelist_IsExactlyTheFourPlayableTypes()
    {
        // KEYSTONE: the taxonomy is CLOSED. Growing it is a product
        // decision (each type must be answerable by a WORD on a blind,
        // one-button toy), never an incidental edit.
        Assert.Equal(
            new[] { "animal_sound", "count_to", "yes_no_silly", "make_it_small" },
            ChatService.AllowedGameTypes);
    }

    [Fact]
    public void Prompt_BansPhysicalActionGames()
    {
        // KEYSTONE (v6): the structural ban — the toy is blind.
        Assert.Contains("Do NOT ask the child to clap, jump, touch a", Prompt);
        Assert.Contains("cannot know whether any", Prompt);
    }

    [Fact]
    public void Prompt_BansMixingTwoTypesPerTurn()
    {
        Assert.Contains("mixing two types", Prompt);
        Assert.Contains("one type per turn", Prompt);
    }

    [Fact]
    public void Prompt_BansAskingPermissionToContinue()
    {
        Assert.Contains("asking permission to continue", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Variety, magic phrasing, round progression
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresPerTypeSubtypes()
    {
        Assert.Contains("subtypes:", Prompt);
        Assert.Contains("farm", Prompt);          // animal_sound
        Assert.Contains("backward", Prompt);      // count_to
        Assert.Contains("absurd swaps", Prompt);  // yes_no_silly
        Assert.Contains("home things", Prompt);   // make_it_small
    }

    [Fact]
    public void Prompt_ContainsVarietyPolicy()
    {
        Assert.Contains("VARIETY POLICY", Prompt);
        Assert.Contains("Do NOT repeat the same subtype two rounds", Prompt);
    }

    [Fact]
    public void Prompt_ContainsMagicPhrasingPolicy()
    {
        Assert.Contains("MAGIC PHRASING POLICY", Prompt);
        Assert.Contains("պուփ-պուփ", Prompt);
        Assert.Contains("Բրա՛վո", Prompt);
        Assert.Contains("baby-talk", Prompt);
    }

    [Fact]
    public void Prompt_ContainsCelebrationRotationRule()
    {
        Assert.Contains("CELEBRATION ROTATION", Prompt);
        Assert.Contains("the same celebration two turns in a row", Prompt);
    }

    [Fact]
    public void Prompt_ContainsRoundProgressionLadder()
    {
        Assert.Contains("ROUND PROGRESSION", Prompt);
        Assert.Contains("Round 1", Prompt);
        Assert.Contains("Round 2", Prompt);
        Assert.Contains("Round 3 or 4", Prompt);
        Assert.Contains("Round 5+", Prompt);
    }

    [Fact]
    public void Prompt_RoundLadder_MatchesRuntimeHint_SubtypeSwitchAtRound2()
    {
        // v6 contradiction fix: the static ladder used to say Round 2 is
        // "same game type" (no subtype change) while the runtime roundHint
        // told Round 2 to switch the subtype. The ladder now matches the
        // runtime: Round 2 switches the subtype.
        Assert.Contains("Round 2: bump the energy a touch AND switch the SUBTYPE", Prompt);
    }

    [Fact]
    public void Prompt_ContainsSwitchGameOpenerExemplar()
    {
        Assert.Contains("SWITCH_GAME OPENER", Prompt);
        Assert.Contains("Լավ, նոր խաղ", Prompt);
    }

    [Fact]
    public void Prompt_ContainsNewBadGoodPair_SubtypeRepeat()
    {
        Assert.Contains("same subtype back-to-back", Prompt);
    }

    [Fact]
    public void Prompt_ContainsNewBadGoodPair_MechanicalPraise()
    {
        Assert.Contains("mechanical praise repeat", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // STRICT NON-NEGOTIABLES + pinned opener patterns
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresStrictNonNegotiablesSection()
    {
        Assert.Contains("STRICT NON-NEGOTIABLES", Prompt);
    }

    [Fact]
    public void Prompt_RequiresExactlyOneChildActionPerTurn()
    {
        Assert.Contains("EXACTLY ONE child action per turn", Prompt);
    }

    [Fact]
    public void Prompt_BansMultipleQuestionsPerTurn()
    {
        Assert.Contains("Do NOT ask two questions in the same turn", Prompt);
        Assert.Contains("Max one question mark per reply", Prompt);
    }

    [Fact]
    public void Prompt_YesNoExemplar_HasSingleQuestionMark()
    {
        // v6 contradiction fix: the old yes/no exemplar «Ձուկը թռչու՞մ է։
        // Հա՞, թե՞ ոչ։» carried three question marks against the
        // max-one rule, and the model copied it verbatim in benchmark
        // runs. The paired-tag shape must stay out of the prompt.
        Assert.DoesNotContain("Հա՞, թե՞ ոչ", Prompt);
    }

    [Fact]
    public void Prompt_BansEndingTheGameAfterOneExchange()
    {
        Assert.Contains("NEVER end the game after a single exchange", Prompt);
        Assert.Contains("The stop_game turn kind is the ONLY way a game ends", Prompt);
    }

    [Fact]
    public void Prompt_DoesNotContainBannedEmptyOpener()
    {
        Assert.DoesNotContain(
            "ինչ ես ուզում անել",
            Prompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_DoesNotContainFormalPluralAddress()
    {
        Assert.DoesNotContain("դուք", Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ձեզ", Prompt);
        Assert.DoesNotContain("Ձեր", Prompt);
        Assert.Contains("«դու»", Prompt);
    }

    [Fact]
    public void Prompt_DeclaresColdStartOneTypeRule()
    {
        Assert.Contains("COLD-START ONE-TYPE RULE", Prompt);
        Assert.Contains("exactly ONE game type", Prompt);
        Assert.Contains("exactly ONE child", Prompt);
    }

    [Fact]
    public void Prompt_PinsGoodColdStartExemplars_SingleActionEach()
    {
        Assert.Contains("«Խաղանք մի փոքր խաղ։ Հնչեցրու կատվի ձայնը։»", Prompt);
        Assert.Contains("«Ասա՛՝ ձուկը թռչու՞մ է։»", Prompt);
    }

    [Fact]
    public void Prompt_BansPluralImperativeOpeners()
    {
        Assert.Contains("PLURAL-IMPERATIVE OPENERS", Prompt);
        Assert.Contains("«Խաղանք»", Prompt);
        Assert.Contains("«Հաշվենք»", Prompt);
        Assert.Contains("«Հնչեցրու»", Prompt);
    }

    [Fact]
    public void Prompt_DoesNotContainPluralImperativeLiteralEkek()
    {
        Assert.DoesNotContain("Եկեք", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // v6 — honesty block (the toy is blind; praise must be earned)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresHonestyAbsolute()
    {
        // KEYSTONE (v6): the toy must never claim to observe a physical
        // action and never celebrate an answer it did not understand.
        Assert.Contains("HONESTY — ABSOLUTE", Prompt);
        Assert.Contains("NEVER claim the child did", Prompt);
        Assert.Contains("NEVER celebrate", Prompt);
    }

    [Fact]
    public void Prompt_DoesNotContainRoomFactsCorrection()
    {
        // The old wrong-answer exemplar «Մոտ էր։ Գնդակը կարմիր է։» taught
        // the model to assert facts about a room it cannot see.
        Assert.DoesNotContain("Գնդակը կարմիր է", Prompt);
    }

    [Fact]
    public void Prompt_AnimalSound_IsParticipationPraiseOnly()
    {
        // The toy cannot judge a moo. Participation is praised; the
        // imitation is never graded.
        Assert.Contains("praise the", Prompt);
        Assert.Contains("PARTICIPATION", Prompt);
        Assert.Contains("never grade", Prompt);
    }

    [Fact]
    public void Prompt_ContainsBadGoodPair_CelebratingWrongAnswer()
    {
        Assert.Contains("celebrating a wrong or unjudgeable answer", Prompt);
    }

    [Fact]
    public void Prompt_ContainsBadGoodPair_ClaimingToObserve()
    {
        Assert.Contains("claiming to observe", Prompt);
        Assert.Contains("toy sees nothing", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // make_it_small — the diminutives game (2026-08-06)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_MakeItSmall_DeclaresTheDiminutiveMechanic()
    {
        Assert.Contains("make_it_small", Prompt);
        Assert.Contains("diminutive ending (-իկ / -ուկ / -ակ)", Prompt);
        Assert.Contains("«Փոքրացրո՛ւ՝ կատու։»", Prompt);
    }

    [Fact]
    public void Prompt_MakeItSmall_BoundsTheWordSource()
    {
        // One base word per round, and only concrete child-familiar nouns
        // that actually take a productive diminutive. An abstract noun or
        // a proper name has no little form to reach for.
        Assert.Contains("EXACTLY ONE base word per round", Prompt);
        Assert.Contains("only CONCRETE nouns", Prompt);
        Assert.Contains("NEVER abstract nouns, NEVER proper names", Prompt);
        Assert.Contains("կատու→կատվիկ", Prompt);
    }

    [Fact]
    public void Prompt_MakeItSmall_CelebratesTheAttempt_NotTheAccuracy()
    {
        // KEYSTONE: the v6 honesty posture extended to the new type. Any
        // recognizable diminutive shape earns the celebration — the toy is
        // not scoring the morphology of a five-year-old.
        Assert.Contains("celebrate ANY attempt that carries a diminutive", Prompt);
        Assert.Contains("even when the form is not the standard one", Prompt);
        Assert.Contains("celebrate the attempt,", Prompt);
        Assert.Contains("never the accuracy", Prompt);
    }

    [Fact]
    public void Prompt_MakeItSmall_NeverGrades_AndNeverDrills()
    {
        // KEYSTONE: no verdict, no repeat-after-me. Areg is a play leader,
        // not a teacher (MODES.md § 2).
        Assert.Contains("NEVER tell the child the answer was", Prompt);
        Assert.Contains("NEVER grade the form", Prompt);
        Assert.Contains("NEVER ask the child to", Prompt);
        Assert.Contains("you are not a teacher", Prompt);
    }

    [Fact]
    public void Prompt_MakeItSmall_ModelsTheStandardFormInstead()
    {
        // The toy never certifies the child's word as correct Armenian; it
        // simply says the standard little word in its own next sentence.
        Assert.Contains("Do NOT declare the", Prompt);
        Assert.Contains("correct Armenian", Prompt);
        Assert.Contains("model the standard word", Prompt);
        Assert.Contains("say the little word yourself", Prompt);
    }

    [Fact]
    public void Prompt_ContainsBadGoodPair_GradingAndDrilling()
    {
        Assert.Contains("grading / repeat-after-me drilling", Prompt);
        Assert.Contains("model the little word and move on", Prompt);
    }
}

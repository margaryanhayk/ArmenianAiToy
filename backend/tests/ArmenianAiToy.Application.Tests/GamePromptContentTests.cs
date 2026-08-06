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
        Assert.Contains("guess_what", Prompt);
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
    public void EnforcedWhitelist_IsExactlyTheFivePlayableTypes()
    {
        // KEYSTONE: the taxonomy is CLOSED. Growing it is a product
        // decision (each type must be answerable by a WORD on a blind,
        // one-button toy), never an incidental edit. guess_what joined on
        // 2026-08-06 on exactly that footing — the child answers «հա» or
        // «ոչ», and finally names the thing.
        Assert.Equal(
            new[] { "animal_sound", "count_to", "yes_no_silly", "make_it_small", "guess_what" },
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

    // ─────────────────────────────────────────────────────────────────────
    // guess_what — «Գուշակիր», the real Akinator (2026-08-06). The roles
    // reverse: the child holds the secret and the toy does the guessing.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_GuessWhat_DeclaresTheReversedMechanic()
    {
        // The one game where the child is not answering a quiz — the toy is.
        Assert.Contains("GUESS_WHAT", Prompt);
        Assert.Contains("The CHILD thinks of one thing and keeps it secret", Prompt);
        Assert.Contains("The roles are REVERSED here", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_BoundsTheDomainsAndStatesThemAloud()
    {
        // A child who may think of anything at all is a child the toy will
        // never guess — so the four domains are named in the opener.
        Assert.Contains("animal, everyday", Prompt);
        Assert.Contains("fairy-tale character", Prompt);
        Assert.Contains("State the four domains in your opener", Prompt);
        Assert.Contains("«Հիմա ես կգուշակեմ, թե ինչ ես մտածել։", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_RequiresExactlyOneYesNoQuestionPerTurn()
    {
        // KEYSTONE: the child can only answer «հա» or «ոչ». An open
        // question is unanswerable in this game.
        Assert.Contains("EXACTLY ONE yes/no question per turn", Prompt);
        Assert.Contains("question plus a guess in the same turn", Prompt);
        Assert.Contains("never ask an open question", Prompt);
        Assert.Contains("open question inside guess_what", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_ForbidsRepeatingItsOwnQuestions()
    {
        // History is the only memory this game has — there is no round
        // store for the questions already asked.
        Assert.Contains("TRACK YOUR OWN QUESTIONS from the conversation history", Prompt);
        Assert.Contains("Never ask the same question twice", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_PinsTheGuessDeadline()
    {
        // KEYSTONE: a 4–7-year-old loses the thread long before a perfect
        // binary search would finish. Guessing early and being wrong is the
        // cheaper failure.
        Assert.Contains("COUNT YOUR QUESTIONS", Prompt);
        Assert.Contains("MUST say a real guess by roughly question 7 or 8", Prompt);
        Assert.Contains("asking a ninth question is not", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_DeclaresTheLossRevealAsk()
    {
        // KEYSTONE (owner-specified ending): the toy gives up, ASKS what it
        // was, and accepts whatever comes back.
        Assert.Contains("LOSS ENDING", Prompt);
        Assert.Contains("«Հանձնվում եմ։ Իսկ ի՞նչ էր, ասա՛։»", Prompt);
        Assert.Contains("Whatever they name, ACCEPT it", Prompt);
        Assert.Contains("never re-open the", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_LimitsTheContradictionToASingleLine()
    {
        // KEYSTONE: the honest teach-line is ONE line, only when the child's
        // own earlier answer is clearly contradicted, and never a question.
        Assert.Contains("ONLY IF the thing the child named clearly contradicts", Prompt);
        Assert.Contains("exactly ONE playful", Prompt);
        Assert.Contains("one, never two, never a", Prompt);
        Assert.Contains("«Հե՜յ-հե՜յ, բայց դու ասացիր՝ ձայն չի հանում։»", Prompt);
        Assert.Contains("If nothing clearly contradicts, say NO such line at all", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_FramesLosingAsTheChildsWin()
    {
        Assert.Contains("«Ապրե՛ս, դու ինձ հաղթեցիր։»", Prompt);
        Assert.Contains("Losing is the child's victory", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_UsesTheWinAgainstAPersonVerb_NotTheCalque()
    {
        // KEYSTONE (armenian-linguistic-reviewer, 2026-08-06): «շահեցիր»
        // is winning a PRIZE; beating a person is «հաղթեցիր». The first
        // draft shipped the calque in every loss-ending exemplar, and the
        // model copied it verbatim in the benchmark — so the toy said it
        // out loud in the one line a child hears after beating Areg.
        Assert.Contains("հաղթեցիր", Prompt);
        Assert.Contains("NEVER «շահեցիր»", Prompt);
        Assert.DoesNotContain("դու ինձ շահեցիր", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_NeverShamesAndClaimsOnlyWhatTheChildSaid()
    {
        // KEYSTONE: the v6 honesty posture, carried into the one game where
        // the toy could be tempted to argue with a five-year-old.
        Assert.Contains("You may only claim what the CHILD SAID", Prompt);
        Assert.Contains("A wrongly-claimed contradiction is worse than none", Prompt);
        Assert.Contains("shame, never scold", Prompt);
        Assert.Contains("Never invent a rule the child broke", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_ResolvesTheSubtypeRotationContradiction()
    {
        // One secret spans many turns, so the runtime's "switch the SUBTYPE"
        // hint cannot mean "switch the domain" here — it means ask about a
        // different property.
        Assert.Contains("EXCEPTION for guess_what", Prompt);
        Assert.Contains("domain does NOT rotate mid-secret", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_BansCelebrationOpenersMidGuessing()
    {
        // KEYSTONE: benchmark GB10 caught «Շա՛տ լավ։ Իսկ դա հաչո՞ւմ է։» —
        // a celebration word opening a plain guessing turn, where the
        // child has achieved nothing yet. The praise belongs to the end.
        Assert.Contains("NEVER OPEN A GUESSING TURN WITH A CELEBRATION WORD", Prompt);
        Assert.Contains("Nothing has", Prompt);
        Assert.Contains("neutral acknowledgement", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_HasNoRightOrWrongAnswerToReactTo()
    {
        // The child's «հա»/«ոչ» describes the toy's question, not the
        // child's knowledge — celebrating it would be praise for nothing.
        Assert.Contains("there is no right or wrong answer to react to", Prompt);
    }

    [Fact]
    public void Prompt_GuessWhat_ContainsBadGoodPair_InterrogatingOnContradiction()
    {
        Assert.Contains("interrogating / accusing on a contradiction", Prompt);
        Assert.Contains("a demand that the child explain", Prompt);
        Assert.Contains("turns play into a cross-examination", Prompt);
    }
}

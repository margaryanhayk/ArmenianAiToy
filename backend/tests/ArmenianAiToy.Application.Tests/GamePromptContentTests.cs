using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B3: presence-based guards on the Game prompt constant.
/// Reads ChatService.GameModeInstruction directly (internal).
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
        // v2 — variety is no longer hand-waved as "rotate activity types";
        // it is enforced deterministically by the AVOID list injected from
        // GameSessions.RecentGameTypes. The prompt must instruct the model
        // to honour that list on switch_game / new_game turns.
        Assert.Contains("AVOID", Prompt);
        Assert.Contains("recent ones", Prompt);
    }

    [Fact]
    public void Prompt_ContainsArmenianExemplarTurns()
    {
        Assert.Contains("ARMENIAN EXEMPLAR TURNS", Prompt);
        Assert.Contains("Ծափ տանք միասին", Prompt);
        Assert.Contains("Դիպչիր քթիդ", Prompt);
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
        Assert.Contains("wrong or partial", Prompt);
        Assert.Contains("silence or off-topic", Prompt);
    }

    [Fact]
    public void Prompt_DiscouragesOpenEndedQuestions()
    {
        Assert.Contains("Do NOT ask open-ended questions", Prompt);
        Assert.Contains("no open-ended", Prompt);
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
    // Game Mode v2 — multi-turn loop directives
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
        // Continue / stop turns must explicitly forbid the tail block.
        Assert.Contains("DO NOT include any tail block", Prompt);
    }

    [Fact]
    public void Prompt_LocksGameTypeWhitelist()
    {
        Assert.Contains("GAME TYPES", Prompt);
        Assert.Contains("animal_sound", Prompt);
        Assert.Contains("color_find", Prompt);
        Assert.Contains("clap_along", Prompt);
        Assert.Contains("count_to", Prompt);
        Assert.Contains("body_part", Prompt);
        Assert.Contains("copy_sound", Prompt);
        Assert.Contains("yes_no_silly", Prompt);
        Assert.Contains("Use ONLY these game types", Prompt);
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
    // Game Mode v3 — variety, magic phrasing, round progression
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresPerTypeSubtypes()
    {
        // Each game type must list a SUBTYPES clause so the model can rotate.
        Assert.Contains("subtypes:", Prompt);
        // A representative subtype keyword per major type.
        Assert.Contains("farm", Prompt);          // animal_sound
        Assert.Contains("backward", Prompt);      // count_to
        Assert.Contains("two-step combo", Prompt); // body_part
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
        Assert.Contains("\u057a\u0578\u0582\u0583-\u057a\u0578\u0582\u0583", Prompt);  // պուփ-պուփ
        Assert.Contains("\u0532\u0580\u0561\u055b\u057e\u0578", Prompt);                // Բրա՛վո
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
    public void Prompt_ContainsSwitchGameOpenerExemplar()
    {
        Assert.Contains("SWITCH_GAME OPENER", Prompt);
        Assert.Contains("\u053c\u0561\u057e, \u0576\u0578\u0580 \u056d\u0561\u0572", Prompt); // Լավ, նոր խաղ
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
    // Game Mode v4 — STRICT NON-NEGOTIABLES + pinned opener patterns
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresStrictNonNegotiablesSection()
    {
        Assert.Contains("STRICT NON-NEGOTIABLES", Prompt);
    }

    [Fact]
    public void Prompt_RequiresExactlyOneChildActionPerTurn()
    {
        // The weaker "One clear, simple instruction at a time" line was
        // already present; v4 adds a stronger "EXACTLY ONE" rule that
        // also forbids stacking "instruction + question" in one reply.
        Assert.Contains("EXACTLY ONE child action per turn", Prompt);
    }

    [Fact]
    public void Prompt_BansMultipleQuestionsPerTurn()
    {
        Assert.Contains("Do NOT ask two questions in the same turn", Prompt);
        Assert.Contains("Max one question mark per reply", Prompt);
    }

    [Fact]
    public void Prompt_BansEndingTheGameAfterOneExchange()
    {
        Assert.Contains("NEVER end the game after a single exchange", Prompt);
        Assert.Contains("The stop_game turn kind is the ONLY way a game ends", Prompt);
    }

    [Fact]
    public void Prompt_ContainsPinnedGuessingGameOpener()
    {
        // Required natural-Armenian opener pattern — pinned so a future
        // refactor cannot silently drop the guessing-game exemplar.
        Assert.Contains("OPENER PATTERNS", Prompt);
        Assert.Contains("Ես մտածեցի մի բան, կռահի՞ր", Prompt); // Ես մտածեցի մի բան, կռահի՞ր
    }

    [Fact]
    public void Prompt_DoesNotContainBannedEmptyOpener()
    {
        // The empty-filler opener «Ինչ ես ուզում անել» must not appear
        // anywhere in the prompt — neither as an example nor inside the
        // ban itself (the ban is worded abstractly so the literal phrase
        // stays absent and this DoesNotContain holds).
        Assert.DoesNotContain(
            "ինչ ես ուզում անել", // ինչ ես ուզում անել
            Prompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_DoesNotContainFormalPluralAddress()
    {
        // The formal-plural Armenian pronouns must not appear anywhere
        // in the prompt — the ban is worded abstractly ("formal-plural
        // address forms") so these literals stay out and the model is
        // not even shown the banned form.
        Assert.DoesNotContain("դուք", Prompt, StringComparison.OrdinalIgnoreCase);  // դուք / Դուք
        Assert.DoesNotContain("Ձեզ", Prompt);                                            // Ձեզ
        Assert.DoesNotContain("Ձեր", Prompt);                                            // Ձեր

        // The replacement guidance — singular «դու» — must be present so
        // the model has a positive target to swap to.
        Assert.Contains("«դու»", Prompt);
    }
}

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
}

using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B4: presence-based guards on the Curiosity prompt constant.
/// Reads ChatService.CuriosityWindowInstruction directly (internal).
/// </summary>
public class CuriosityPromptContentTests
{
    private static string Prompt => ChatService.CuriosityWindowInstruction;

    [Fact]
    public void Prompt_RequiresKindAdultTone()
    {
        Assert.Contains("kind adult", Prompt);
        Assert.Contains("teacher", Prompt);
        Assert.Contains("Warm, not cute", Prompt);
    }

    [Fact]
    public void Prompt_BansPraiseOpeners()
    {
        Assert.Contains("praise-the-question", Prompt);
        Assert.Contains("Հիանալի հարց", Prompt);
    }

    [Fact]
    public void Prompt_ContainsArmenianExemplarAnswers()
    {
        Assert.Contains("ARMENIAN EXEMPLAR ANSWERS", Prompt);
        Assert.Contains("ջրի կաթիլների միջով", Prompt);
        Assert.Contains("Երկիրն է շրջվում", Prompt);
    }

    [Fact]
    public void Prompt_BansLectureList()
    {
        Assert.Contains("RESPONSE SHAPES", Prompt);
        Assert.Contains("lesson / list", Prompt);
    }

    [Fact]
    public void Prompt_BansTooManyFacts()
    {
        Assert.Contains("too many facts", Prompt);
        Assert.Contains("Ամպերը ջրի փոքրիկ կաթիլներ", Prompt);
    }

    [Fact]
    public void Prompt_BansDodgeNonAnswer()
    {
        Assert.Contains("dodge", Prompt);
        Assert.Contains("Չգիտեմ", Prompt);
    }

    [Fact]
    public void Prompt_ContainsStoryReturnShape()
    {
        Assert.Contains("STORY RETURN SHAPE", Prompt);
        Assert.Contains("Հիմա վերադառնանք մեր հեքիաթին", Prompt);
    }

    [Fact]
    public void Prompt_StoryReturnShapeIsGatedOnPreviousModeDirective()
    {
        // Regression for F-Cur-1: the STORY RETURN SHAPE section carries a
        // load-bearing two-sentence gate — it is meant to fire ONLY when the
        // runtime PREVIOUS_MODE: Story directive is present in the prompt,
        // and to be skipped entirely otherwise. Without this gate, every
        // standalone Curiosity answer (e.g. a from-scratch "why is the sky
        // blue" with no prior story) would tack «Հիմա վերադառնանք մեր
        // հեքիաթին։» onto the end. The runtime side is already protected by
        // CuriosityNoActiveStory_DoesNotInjectPreviousModeStoryDirective —
        // this fact protects the prompt-internal contract introduced in
        // commit 7104b98 and relied on by c95c2ec.
        Assert.Contains("Use ONLY when the PREVIOUS_MODE directive", Prompt);
        Assert.Contains("skip this shape entirely", Prompt);
    }

    [Fact]
    public void Prompt_PreservesModeHeader()
    {
        Assert.Contains("MODE: CURIOSITY WINDOW", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoQuestionsBackRule()
    {
        Assert.Contains("Do NOT ask any questions back", Prompt);
    }

    [Fact]
    public void Prompt_PreservesShortAnswerBudget()
    {
        // Curiosity v2 — bumped from "1 to 2" to "1 to 3" so an optional
        // analogy or fun-fact clause has room without triggering the gate.
        Assert.Contains("1 to 3 short sentences", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Curiosity Mode v2 — layered answer structure
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresLayeredAnswerStructure()
    {
        Assert.Contains("ANSWER STRUCTURE", Prompt);
        Assert.Contains("LAYER 1", Prompt);
        Assert.Contains("LAYER 2", Prompt);
    }

    [Fact]
    public void Prompt_ContainsAnalogyPolicy()
    {
        Assert.Contains("ANALOGY POLICY", Prompt);
        Assert.Contains("\u056f\u0561\u0580\u056e\u0565\u057d", Prompt); // կարծես
        Assert.Contains("\u056b\u0576\u0579\u057a\u0565\u057d", Prompt); // ինչպես
    }

    [Fact]
    public void Prompt_ContainsFunFactPolicy()
    {
        Assert.Contains("FUN FACT POLICY", Prompt);
        Assert.Contains("max 1", Prompt);
    }

    [Fact]
    public void Prompt_ContainsAntiEncyclopediaRule()
    {
        Assert.Contains("ANTI-ENCYCLOPEDIA", Prompt);
        // Forbidden encyclopedia opener fragment.
        Assert.Contains("\u0531\u0575\u057d \u0565\u0580\u0587\u0578\u0582\u0575\u0569\u0568", Prompt); // Այս երևույթը
    }

    [Fact]
    public void Prompt_ContainsExplainLikeAChildRule()
    {
        Assert.Contains("EXPLAIN-LIKE-A-CHILD", Prompt);
        Assert.Contains("One small idea per response", Prompt);
    }

    [Fact]
    public void Prompt_ListsEverydayTopics()
    {
        Assert.Contains("EVERYDAY TOPICS", Prompt);
        Assert.Contains("animals", Prompt);
        Assert.Contains("weather", Prompt);
        Assert.Contains("body", Prompt);
        Assert.Contains("food", Prompt);
    }

    [Fact]
    public void Prompt_AnalogyExemplarUsesFamiliarObject()
    {
        // Sponge analogy for clouds — concrete, familiar to a 5-year-old.
        Assert.Contains("\u057d\u057a\u0578\u0582\u0576\u0563\u056b \u0574\u0565\u057b", Prompt); // սպունգի մեջ
    }

    [Fact]
    public void Prompt_NewBadGoodPair_EncyclopediaOpener()
    {
        Assert.Contains("encyclopedia opener", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Armenian register — full-day slice 2: ban formal-plural + self-intro
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_ContainsArmenianRegisterRule()
    {
        Assert.Contains("ARMENIAN REGISTER — STRICT", Prompt);
        Assert.Contains("formal-plural Armenian address forms", Prompt);
        Assert.Contains("«դու»", Prompt);
    }

    [Fact]
    public void Prompt_BansSelfIntroAtCuriosityTurnStart()
    {
        Assert.Contains("Do NOT introduce yourself or greet at the top of a Curiosity", Prompt);
    }

    [Fact]
    public void Prompt_DoesNotContainFormalPluralAddress()
    {
        Assert.DoesNotContain("դուք", Prompt, StringComparison.OrdinalIgnoreCase);  // դուք / Դուք
        Assert.DoesNotContain("Ձեզ", Prompt);                                            // Ձեզ
        Assert.DoesNotContain("Ձեր", Prompt);                                            // Ձեր
    }

    // ─────────────────────────────────────────────────────────────────────
    // Curiosity v3 — follow-up concision (anti length_growing drift)
    //
    // Anchored on the 2026-05-17 BenchmarkAll persistent baseline weak
    // case: CuB01 ("why does the sun rise") turn 2 grew to 87 chars vs
    // turn 1's 61. The new FOLLOW-UP CONCISION rule pins "follow-up MUST
    // NOT be longer than the previous answer" with one explicit exception
    // for child-driven "tell me more" requests. Evidence:
    //   tools/quality-evidence/areg-live-quality-validation-20260517.md
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_DeclaresFollowUpConcisionRule()
    {
        Assert.Contains("FOLLOW-UP CONCISION — STRICT", Prompt);
    }

    [Fact]
    public void Prompt_RequiresFollowUpNoLongerThanPrior()
    {
        // The load-bearing length rule: follow-up answer must be the
        // same length or shorter than the prior. Default direction is
        // shorter, not longer.
        Assert.Contains("MUST NOT be longer than the previous", Prompt);
        Assert.Contains("verbosity drift", Prompt);
    }

    [Fact]
    public void Prompt_BansListsAndStackedClausesInFollowUp()
    {
        // One concrete example max — never list two.
        Assert.Contains("One concrete example MAX per follow-up", Prompt);
        // No second paragraph / stacked clauses on a follow-up turn.
        Assert.Contains("No second paragraph or stacked clauses", Prompt);
    }

    [Fact]
    public void Prompt_NamesExplicitChildAskForMoreExceptionOnly()
    {
        // The single exception: child explicitly asks for more.
        Assert.Contains("only stretch a follow-up if the child", Prompt);
        Assert.Contains("ավելի պատմիր", Prompt);
    }
}

using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B5: presence-based guards on the Calm prompt constant.
/// Reads ChatService.CalmModeInstruction directly (internal).
/// </summary>
public class CalmPromptContentTests
{
    private static string Prompt => ChatService.CalmModeInstruction;

    [Fact]
    public void Prompt_ContainsArmenianExemplarLines()
    {
        Assert.Contains("ARMENIAN EXEMPLAR LINES", Prompt);
        Assert.Contains("Բարձիկը փափուկ է", Prompt);
        Assert.Contains("Լուսինը դուրս է եկել", Prompt);
    }

    [Fact]
    public void Prompt_BansUpbeatEnergy()
    {
        Assert.Contains("Energy stays low", Prompt);
        Assert.Contains("Ապրե՛ս", Prompt);
        Assert.Contains("Արի գնանք", Prompt);
    }

    [Fact]
    public void Prompt_BansNewTension()
    {
        Assert.Contains("new tension", Prompt);
        Assert.Contains("հանկարծ", Prompt);
    }

    [Fact]
    public void Prompt_BansImplicitQuestion()
    {
        Assert.Contains("implicit question", Prompt);
        Assert.Contains("Տեսնես", Prompt);
    }

    [Fact]
    public void Prompt_BansOverpromisingReassurance()
    {
        Assert.Contains("overpromising reassurance", Prompt);
        Assert.Contains("Բոլոր վախերդ անհետացան", Prompt);
        Assert.Contains("վերմակը տաք է", Prompt);
    }

    [Fact]
    public void Prompt_ContainsClosingPhraseShape()
    {
        Assert.Contains("CLOSING PHRASE SHAPE", Prompt);
        // Closing phrase must carry a pool anchor — examples are
        // anchor-bearing lines, not a bare goodnight.
        Assert.Contains("Վերմակդ տաք է", Prompt);
        Assert.Contains("Աչքերդ ծանրանում են", Prompt);
    }

    [Fact]
    public void Prompt_ExemplarLinesHaveNoQuestionOrExclamationMarks()
    {
        var start = Prompt.IndexOf("ARMENIAN EXEMPLAR LINES");
        var end = Prompt.IndexOf("RESPONSE SHAPES", start);
        Assert.InRange(start, 0, Prompt.Length);
        Assert.InRange(end, start, Prompt.Length);
        var section = Prompt.Substring(start, end - start);
        Assert.DoesNotContain("?", section);
        Assert.DoesNotContain("՞", section);
        Assert.DoesNotContain("!", section);
        Assert.DoesNotContain("՜", section);
    }

    [Fact]
    public void Prompt_PreservesModeHeader()
    {
        Assert.Contains("MODE: CALM / BEDTIME", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoQuestionsRule()
    {
        Assert.Contains("Do NOT ask any questions", Prompt);
    }

    [Fact]
    public void Prompt_PreservesNoChoiceBlockRule()
    {
        Assert.Contains("Do NOT include a CHOICE_A / CHOICE_B block", Prompt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Calm Mode v2 — anti-companion guard, grounding, wind-down arc
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_ContainsAntiCompanionLanguageRule()
    {
        Assert.Contains("ANTI-COMPANION-LANGUAGE", Prompt);
        // Banned companion phrases.
        Assert.Contains("\u0544\u056b\u0577\u057f \u0584\u0565\u0566 \u0570\u0565\u057f \u0565\u0574", Prompt);          // Միշտ քեզ հետ եմ
        Assert.Contains("\u0535\u057d \u0584\u0578 \u0568\u0576\u056f\u0565\u0580\u0576 \u0565\u0574", Prompt);          // Ես քո ընկերն եմ
        Assert.Contains("possessive emotional bonds", Prompt);
    }

    [Fact]
    public void Prompt_ContainsGroundingDetailPolicy()
    {
        Assert.Contains("GROUNDING DETAIL POLICY", Prompt);
        // A few representative anchors from the pool.
        Assert.Contains("\u057e\u0565\u0580\u0574\u0561\u056f\u0568 \u057f\u0561\u0584 \u0567", Prompt);                  // վերմակը տաք է
        Assert.Contains("\u0577\u0576\u0579\u0578\u0582\u0574 \u0565\u057d \u0564\u0561\u0576\u0564\u0561\u0572", Prompt); // շնչում ես դանդաղ
        Assert.Contains("ONE anchor per turn", Prompt);
    }

    [Fact]
    public void Prompt_ContainsBedtimeDistressShape()
    {
        Assert.Contains("BEDTIME-DISTRESS SHAPE", Prompt);
        Assert.Contains("do NOT echo the", Prompt);
        // GOOD distress example uses a slow-breath line.
        Assert.Contains("\u0547\u0576\u0579\u056b\u0580 \u0564\u0561\u0576\u0564\u0561\u0572", Prompt); // Շնչիր դանդաղ
    }

    [Fact]
    public void Prompt_RulesForbidVolunteeringFearWords()
    {
        // Regression for F4: commit b865163 added a RULES-level safety
        // boundary forbidding Areg from volunteering fear framing when the
        // child did not name fear first. The full line at
        // ChatService.cs:339-341 is:
        //   "Do NOT introduce fear words («վախ», «սարսափ») unless the
        //    child named them first. Never volunteer reassurance about
        //    fears the child did not mention."
        //
        // BEDTIME-DISTRESS SHAPE's "do NOT echo the" gives only partial
        // residual protection (it covers echoing, not volunteering).
        // Pin each distinctive substring so a silent weakening of the
        // RULES-level rule fails distinctly. "Never volunteer reassurance"
        // is the keystone marker; «վախ» and «սարսափ» are the Armenian
        // fear-word anchors the rule explicitly names.
        Assert.Contains("Do NOT introduce fear words", Prompt);
        Assert.Contains("\u057e\u0561\u056d", Prompt);                         // վախ
        Assert.Contains("\u057d\u0561\u0580\u057d\u0561\u0583", Prompt);       // սարսափ
        Assert.Contains("Never volunteer reassurance", Prompt);
    }

    [Fact]
    public void Prompt_ContainsWindDownArcLadder()
    {
        Assert.Contains("WIND-DOWN ARC", Prompt);
        Assert.Contains("Turn 1", Prompt);
        Assert.Contains("Turn 2", Prompt);
        Assert.Contains("Turn 3+", Prompt);
        Assert.Contains("never longer", Prompt);
        // Regression for F3: commit b865163 intentionally tightened the
        // Turn-2 and Turn-3+ cardinalities from "2 to 3" / "1 to 2" to
        // exact counts, to eliminate arc-drift at the upper bound. Pin
        // the exact-count wording so a future loosening fails distinctly.
        Assert.Contains("Exactly 2 short sentences", Prompt);
        Assert.Contains("Exactly 1 short sentence", Prompt);
    }

    [Fact]
    public void Prompt_ContainsCompanionLanguageBadGoodPair()
    {
        Assert.Contains("companion language", Prompt);
        Assert.Contains("no \"I\", room-based", Prompt);
    }
}

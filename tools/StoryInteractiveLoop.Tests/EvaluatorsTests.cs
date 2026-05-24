// Pin every deterministic evaluator behavior that StoryInteractiveLoop
// relies on. Each [Fact] / [Theory] documents one invariant of the
// child-facing story checks; if a fixture here changes, expect a
// downstream evidence-file change to come with it.

using ArmenianAiToy.Tools.StoryInteractiveLoop;

namespace ArmenianAiToy.Tools.StoryInteractiveLoop.Tests;

public class EvaluatorsTests
{
    // ----- ArmenianRatio -----

    [Fact]
    public void ArmenianRatio_PureArmenian_IsOne()
    {
        // Full Armenian sentence — every letter is in the U+0530..U+058F range.
        var r = Evaluators.ArmenianRatio("Փոքրիկ ոզնին քայլեց անտառով");
        Assert.True(r > 0.99, $"expected ~1.0, got {r:F3}");
    }

    [Fact]
    public void ArmenianRatio_EmptyOrPunctuationOnly_IsZero()
    {
        Assert.Equal(0.0, Evaluators.ArmenianRatio(""));
        Assert.Equal(0.0, Evaluators.ArmenianRatio("   "));
        Assert.Equal(0.0, Evaluators.ArmenianRatio("...!?,;"));
    }

    [Fact]
    public void ArmenianRatio_MixedArmenianAndLatin_BelowThreshold()
    {
        // Half Armenian letters, half Latin letters.
        var r = Evaluators.ArmenianRatio("ոզնի hello ոզնի world");
        Assert.True(r < Evaluators.MinArmenianRatio,
            $"expected < {Evaluators.MinArmenianRatio}, got {r:F3}");
    }

    // ----- HasLatinLeakage / HasCyrillicLeakage -----

    [Fact]
    public void HasLatinLeakage_FlagsThreeOrMoreLatinRun()
    {
        Assert.True(Evaluators.HasLatinLeakage("ոզնի cat ոզնի"));
        Assert.True(Evaluators.HasLatinLeakage("hello world"));
    }

    [Fact]
    public void HasLatinLeakage_DoesNotFlagShortAcronyms()
    {
        // 2 latin letters in a row should not trip the flag — leaves
        // room for the occasional initial / abbreviation.
        Assert.False(Evaluators.HasLatinLeakage("AB ոզնի"));
        Assert.False(Evaluators.HasLatinLeakage("ոզնի"));
        Assert.False(Evaluators.HasLatinLeakage(""));
    }

    [Fact]
    public void HasCyrillicLeakage_FlagsRussianRun()
    {
        Assert.True(Evaluators.HasCyrillicLeakage("ոզնի привет ոզնի"));
    }

    // ----- IsGenericChoice -----

    [Theory]
    [InlineData("Շարունակել")]
    [InlineData("շարունակել")]
    [InlineData("Շարունակել։")] // verjaket trailing
    [InlineData("Շարունակել.")]
    [InlineData("Առաջինը")]
    [InlineData("Երկրորդը")]
    [InlineData("Այո")]
    [InlineData("Ոչ")]
    [InlineData("Ի՞նչ անենք")]
    public void IsGenericChoice_FlagsBannedAffordances(string choice)
    {
        Assert.True(Evaluators.IsGenericChoice(choice), $"expected generic: «{choice}»");
    }

    [Theory]
    [InlineData("Գնալ դեպի անտառ")]                 // "Gnal" plus content
    [InlineData("Շարունակել ճանապարհը")]              // banned root, but with object
    [InlineData("Մոտենալ նապաստակիկին")]
    [InlineData("Հարցնել իմաստուն բուին")]
    public void IsGenericChoice_DoesNotFlagGroundedChoices(string choice)
    {
        Assert.False(Evaluators.IsGenericChoice(choice), $"expected NOT generic: «{choice}»");
    }

    // ----- ChoiceGroundedInBody -----

    [Fact]
    public void ChoiceGroundedInBody_LabelStemAppearsInBody_True()
    {
        // Body uses "նապաստակը", label uses "նապաստակիկին" — the stem
        // overlap (նապաստ-) must register via ArmenianStem.
        var body = "Փոքրիկ նապաստակը մոտեցավ ծառին և տեսավ ընկերոջը";
        var label = "Մոտենալ նապաստակիկին";
        Assert.True(Evaluators.ChoiceGroundedInBody(label, body));
    }

    [Fact]
    public void ChoiceGroundedInBody_NoStemOverlap_False()
    {
        var body = "Արջուկը նստել էր թփի տակ";
        var label = "Հարցնել իմաստուն բուին";
        Assert.False(Evaluators.ChoiceGroundedInBody(label, body));
    }

    [Fact]
    public void ChoiceGroundedInBody_ShortLabelWithNoLongTokens_TreatedAsGrounded()
    {
        // Conservative: a label that has no ≥4-char Armenian stems
        // provides no signal — we don't flag it.
        var body = "Անտառում էր մի փոքրիկ ոզնի";
        Assert.True(Evaluators.ChoiceGroundedInBody("Այո։", body));
    }

    [Fact]
    public void ChoiceGroundedInBody_EmptyLabel_True()
    {
        Assert.True(Evaluators.ChoiceGroundedInBody(null, "Փոքրիկ ոզնի"));
        Assert.True(Evaluators.ChoiceGroundedInBody("", "Փոքրիկ ոզնի"));
    }

    [Fact]
    public void ChoiceGroundedInBody_EmptyBody_False()
    {
        Assert.False(Evaluators.ChoiceGroundedInBody("Մոտենալ նապաստակիկին", ""));
    }

    // ----- FirstSentenceRecapOverlap -----

    [Fact]
    public void FirstSentenceRecapOverlap_HighWhenContinuationParaphrasesStart()
    {
        var prev = "Փոքրիկ նապաստակը ցատկեց ծաղկապարտեզի միջով։ Հետո դիտեց ծիածանը։";
        var next = "Փոքրիկ նապաստակը ցատկեց ծաղկապարտեզի մոտով։ Ապա շարունակեց ճանապարհը։";
        var overlap = Evaluators.FirstSentenceRecapOverlap(prev, next);
        Assert.True(overlap >= Evaluators.RecapOverlapThreshold,
            $"expected ≥ {Evaluators.RecapOverlapThreshold:F2}, got {overlap:F2}");
    }

    [Fact]
    public void FirstSentenceRecapOverlap_LowWhenContinuationMovesForward()
    {
        var prev = "Փոքրիկ ոզնին քայլեց անտառով։ Նա փնտրում էր ընկերոջը։";
        var next = "Հանկարծ թփերի հետևից լսվեց աղվեսի ձայնը։ Ոզնին զարմացավ։";
        var overlap = Evaluators.FirstSentenceRecapOverlap(prev, next);
        Assert.True(overlap < Evaluators.RecapOverlapThreshold,
            $"expected < {Evaluators.RecapOverlapThreshold:F2}, got {overlap:F2}");
    }

    // ----- ChoicesShareFirstToken -----

    [Fact]
    public void ChoicesShareFirstToken_True()
    {
        Assert.True(Evaluators.ChoicesShareFirstToken(
            "Մոտենալ նապաստակին", "Մոտենալ ճանապարհին"));
    }

    [Fact]
    public void ChoicesShareFirstToken_False()
    {
        Assert.False(Evaluators.ChoicesShareFirstToken(
            "Մոտենալ նապաստակին", "Փախչել անտառով"));
    }

    // ----- EvaluateTurn aggregation -----

    [Fact]
    public void EvaluateTurn_PureArmenianStoryTurnWithChoices_NoWarnings()
    {
        var input = new TurnEvaluationInput
        {
            Body = new string('ա', 200) + " ոզնին քայլեց անտառով",
            ChoiceA = "Մոտենալ նապաստակին",
            ChoiceB = "Հարցնել իմաստուն բուին",
            HasChoices = true,
            PreviousBody = null,
            SelectedChoiceLabel = null
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Empty(w);
    }

    [Fact]
    public void EvaluateTurn_GenericChoice_Flagged()
    {
        var input = new TurnEvaluationInput
        {
            Body = new string('ա', 200) + " ոզնին քայլեց",
            ChoiceA = "Շարունակել",
            ChoiceB = "Հարցնել ընկերոջը",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_generic", w);
    }

    [Fact]
    public void EvaluateTurn_LatinLeakageInBody_Flagged()
    {
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ rabbit ոզնին քայլեց անտառով։ " + new string('ա', 200),
            ChoiceA = "Մոտենալ ընկերոջը",
            ChoiceB = "Հարցնել բուին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("latin_leakage_body", w);
    }

    [Fact]
    public void EvaluateTurn_ContinuationIgnoresSelectedChoice_Flagged()
    {
        var input = new TurnEvaluationInput
        {
            Body = "Արջուկը նստել էր թփի տակ։ " + new string('ա', 150),
            HasChoices = false,
            PreviousBody = "Փոքրիկ նապաստակը մոտեցավ ծառին",
            SelectedChoiceLabel = "Հարցնել իմաստուն բուին"
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("continuation_ignores_selected_choice", w);
    }

    [Fact]
    public void EvaluateTurn_ChoicesIdentical_Flagged()
    {
        var input = new TurnEvaluationInput
        {
            Body = new string('ա', 200),
            ChoiceA = "Մոտենալ նապաստակին",
            ChoiceB = "Մոտենալ նապաստակին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choices_identical", w);
    }

    [Fact]
    public void EvaluateTurn_BodyTooLong_Flagged()
    {
        var input = new TurnEvaluationInput
        {
            Body = new string('ա', Evaluators.MaxBodyChars + 100),
            ChoiceA = "Մոտենալ ընկերոջը",
            ChoiceB = "Հարցնել բուին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("body_too_long", w);
    }

    [Fact]
    public void EvaluateTurn_ChoiceTooLong_Flagged()
    {
        var input = new TurnEvaluationInput
        {
            Body = new string('ա', 200),
            ChoiceA = new string('ա', Evaluators.MaxChoiceChars + 5),
            ChoiceB = "Հարցնել բուին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_too_long", w);
    }

    // ----- EvaluateSession verdict math -----

    [Fact]
    public void EvaluateSession_AllClean_Pass100()
    {
        var v = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()
        });
        Assert.Equal("PASS", v.OverallVerdict);
        Assert.Equal(100, v.ArmenianQualityScore);
        Assert.Equal(100, v.StoryLogicScore);
        Assert.Equal(100, v.ChildSuitabilityScore);
        Assert.Equal(100, v.ChoiceQualityScore);
        Assert.Equal(100, v.ContinuationCoherenceScore);
    }

    [Fact]
    public void EvaluateSession_MultipleLatinHits_DragsArmenianScoreToFail()
    {
        var v = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            new[] { "latin_leakage_body" },
            new[] { "latin_leakage_body", "latin_leakage_choice_a" },
            new[] { "latin_leakage_body" }
        });
        Assert.True(v.ArmenianQualityScore < 60,
            $"expected < 60, got {v.ArmenianQualityScore}");
        Assert.Equal("FAIL", v.OverallVerdict);
    }

    [Fact]
    public void EvaluateSession_OneIgnoredChoice_WarnsButNotFail()
    {
        // One ignored-choice hit: choiceQuality stays high (no choice
        // structural warnings), continuationCoherence drops by 20, logic
        // drops by 25. Verdict becomes WARN, not FAIL.
        var v = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(),
            new[] { "continuation_ignores_selected_choice" }
        });
        Assert.Equal("WARN", v.OverallVerdict);
        Assert.Equal(75, v.StoryLogicScore);
        Assert.Equal(80, v.ContinuationCoherenceScore);
    }

    // ----- ArmenianStem -----

    [Theory]
    [InlineData("նապաստակը", "նապաստակը")]   // short suffix, doesn't strip below 4 chars
    [InlineData("ընկերին", "ընկեր")]
    [InlineData("ընկերոջ", "ընկեր")]
    [InlineData("թիթեռիկներին", "թիթեռիկ")]
    public void ArmenianStem_KnownInflections_CollapseToRoot(string token, string expected)
    {
        Assert.Equal(expected, Evaluators.ArmenianStem(token));
    }

    [Fact]
    public void ArmenianStem_VerbRootAlternation_DropsTrailingN()
    {
        // մոտեն / մոտեց → same stem after «ն» drop (length-gated).
        var a = Evaluators.ArmenianStem("մոտենալով");
        var b = Evaluators.ArmenianStem("մոտեցավ");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ArmenianStem_NonArmenianToken_PassesThroughLowercased()
    {
        Assert.Equal("hello", Evaluators.ArmenianStem("Hello"));
    }
}

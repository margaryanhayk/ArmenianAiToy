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
        // Body must mention BOTH choice nouns to satisfy the stricter
        // ChoiceNounsAppearInBody check: «նապաստակը» grounds ChoiceA,
        // «իմաստուն բուն» grounds ChoiceB. Filler padding keeps the
        // body length above MinBodyChars.
        var input = new TurnEvaluationInput
        {
            // Body uses dative «Նապաստակին» so the stemmer reduces it
            // to «նապաստակ» (matching ChoiceA's noun stem). The nominative
            // «Նապաստակը» keeps its trailing «ը» under the current
            // stemmer contract (pinned by ArmenianStem_* test).
            Body = "Ոզնին քայլեց անտառով։ Նապաստակին և իմաստուն բուն բարևեցին նրան։ "
                   + new string('ա', 150),
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
    [InlineData("նապաստակը", "նապաստակ")]      // «ը» definite article stripped (len 9-1=8 ≥ 4)
    [InlineData("ընկերին", "ընկեր")]
    [InlineData("ընկերոջ", "ընկեր")]
    [InlineData("թիթեռիկներին", "թիթեռիկ")]
    [InlineData("տերևների", "տերև")]            // «ների» plural genitive
    [InlineData("ընկերոջը", "ընկեր")]           // «ոջը» possessive-definite (3-char ending)
    [InlineData("ընկերոջին", "ընկեր")]          // «ոջին» possessive-dative (4-char ending)
    public void ArmenianStem_KnownInflections_CollapseToRoot(string token, string expected)
    {
        Assert.Equal(expected, Evaluators.ArmenianStem(token));
    }

    [Fact]
    public void ArmenianStem_StripsPluralNeri()
    {
        Assert.Equal("տերև", Evaluators.ArmenianStem("տերևների"));
    }

    [Fact]
    public void ArmenianStem_StripsDefiniteArticle()
    {
        Assert.Equal("նապաստակ", Evaluators.ArmenianStem("նապաստակը"));
    }

    [Fact]
    public void ArmenianStem_StripsDefiniteArticleFromFourCharSource()
    {
        // CONTRACT CHANGE: «ը» (1-char definite article) joined
        // ShortStemAllowedEndings in the body-side asymmetric fix
        // slice. «արջը» (4 chars) now strips «ը» to «արջ» (3 chars).
        // Before that slice, the default >=4-char-result rule
        // refused this strip, which made body «արջը» and choice
        // «արջին» yield different stems for the same noun — the
        // exact false-positive class observed in the
        // 20260524-193512 live evidence on նոun forms like
        // «ծառը» / «քարը» / «ծառի» / «քարի».
        Assert.Equal("արջ", Evaluators.ArmenianStem("արջը"));
    }

    // ===== Short-noun 2-char-ending exception =====
    //
    // Three noun-case endings (`ին` / `ից` / `ով`) are special-cased
    // to allow a 3-char result instead of the default 4. This lets
    // common short Armenian concrete nouns normalize so that
    // body `ծառերի` / `ծառերին` and choice `ծառին` (both refer to
    // "tree") share a stem under the noun-grounding check.

    [Fact]
    public void ArmenianStem_StripsInFromShortNoun()
    {
        Assert.Equal("ծառ", Evaluators.ArmenianStem("ծառին"));
    }

    [Fact]
    public void ArmenianStem_StripsInFromShortPathNoun()
    {
        Assert.Equal("ուղ", Evaluators.ArmenianStem("ուղին"));
    }

    [Fact]
    public void ArmenianStem_StripsOvFromShortNoun()
    {
        Assert.Equal("քար", Evaluators.ArmenianStem("քարով"));
    }

    [Fact]
    public void ArmenianStem_StripsIcFromShortNoun()
    {
        Assert.Equal("քար", Evaluators.ArmenianStem("քարից"));
    }

    [Fact]
    public void ArmenianStem_DoesNotStripShortInEndingBelowThree()
    {
        // 4-char source word + 2-char ending would leave a 2-char
        // stem — below the 3-char floor even for the relaxed rule.
        // «տնից» (4 chars: տ-ն-ի-ց) must therefore stay as-is.
        Assert.Equal("տնից", Evaluators.ArmenianStem("տնից"));
    }

    [Fact]
    public void ChoiceNounPresentWithShortInEnding_ShouldNotWarn()
    {
        // Body uses «ծառին» (5 chars) which stems to «ծառ» under the
        // new rule. Choice uses the same form. Both must share the
        // «ծառ» stem so the grounding check finds the overlap.
        var input = new TurnEvaluationInput
        {
            Body = "Անտառում մեծ ծառին նստեց փոքրիկ թռչունիկ, "
                   + "որ սկսեց երգել իր մեղմ ձայնով։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք ծառին",
            ChoiceB = "Լսենք թռչունիկին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounStillWarns_WhenShortNounAbsent()
    {
        // Same shape, but the body now never mentions «ծառ» (or any
        // form that would stem to it). The choice's «ծառին» -> «ծառ»
        // stem has nothing to match in the body. Warning fires.
        var input = new TurnEvaluationInput
        {
            Body = "Անտառում քայլում էր փոքրիկ նապաստակը հանգիստ։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք ծառին",
            ChoiceB = "Լսենք նապաստակին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_noun_not_in_body", w);
    }

    // ===== Body-side short-noun stripping (1-char endings) =====
    //
    // The 20260524-193512 evidence showed that the previous
    // commit's choice-side fix («ին»/«ից»/«ով» strip to 3-char
    // stems) created an asymmetric stem class with the body
    // side: body «ծառի» / «ծառը» / «քարի» / «քարը» (4-char
    // source) could not strip the 1-char «ի» / «ը» because the
    // default 4-char-result rule still applied to those endings.
    // This slice adds «ի» and «ը» to the relaxed set so the
    // body side normalizes to the SAME 3-char stem the choice
    // side already produces.

    [Fact]
    public void ArmenianStem_StripsIFromShortNoun()
    {
        Assert.Equal("ծառ", Evaluators.ArmenianStem("ծառի"));
    }

    [Fact]
    public void ArmenianStem_StripsDefiniteArticleFromShortNoun()
    {
        Assert.Equal("ծառ", Evaluators.ArmenianStem("ծառը"));
    }

    [Fact]
    public void ArmenianStem_StripsIFromShortStoneNoun()
    {
        Assert.Equal("քար", Evaluators.ArmenianStem("քարի"));
    }

    [Fact]
    public void ArmenianStem_StripsDefiniteArticleFromShortStoneNoun()
    {
        Assert.Equal("քար", Evaluators.ArmenianStem("քարը"));
    }

    [Fact]
    public void ArmenianStem_DoesNotStripBelowThreeCharResult()
    {
        // 4-char outer guard still applies. A bare 3-char noun
        // never enters the strip loop. A 4-char source with a
        // 2-char ending (e.g. «տնից» -> would-be «տն») is also
        // refused because the result would be 2, below the
        // 3-char floor.
        Assert.Equal("տնից", Evaluators.ArmenianStem("տնից"));
        // 3-char input short-circuits via outer guard.
        Assert.Equal("ծառ", Evaluators.ArmenianStem("ծառ"));
    }

    [Fact]
    public void ChoiceNounPresentWithBodyShortIEnding_ShouldNotWarn()
    {
        // The asymmetric body/choice mismatch this slice fixes:
        // body uses 4-char «ծառի» (genitive); choice uses 5-char
        // «ծառին» (dative). Both must reduce to «ծառ» so the
        // grounding check finds the overlap. Before this slice
        // the body stayed «ծառի» and the choice stripped to
        // «ծառ» — a spurious noun_not_in_body warning.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ նապաստակը նստել էր ծառի կողքին և լսում էր թռչուններին։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք ծառին",
            ChoiceB = "Հարցնենք նապաստակին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounPresentWithBodyShortDefiniteEnding_ShouldNotWarn()
    {
        // S04 turn 0/1 case from the 20260524-193512 evidence:
        // body uses 4-char «քարը» (definite); choice uses 5-char
        // «քարին» (dative). Both must reduce to «քար».
        var input = new TurnEvaluationInput
        {
            Body = "Արեգը տեսավ մի փայլուն քարը խաղաղ արահետի մոտ։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք քարին",
            ChoiceB = "Շարունակենք արահետով",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounStillWarns_WhenShortNounReallyAbsent_AfterBodySideFix()
    {
        // Real noun-grounding gaps must NOT be silenced by the
        // wider stripping rule. Body genuinely has no
        // «ծառ»/«քար» family in any form; choice introduces
        // «ծառին». Warning still fires.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ ոզնին քայլում էր անտառում միայնակ։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք ծառին",
            ChoiceB = "Լսենք ոզնիին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_noun_not_in_body", w);
    }

    // ===== Bare 3-char body nouns matching short-stem choice =====
    //
    // After the short-noun stemmer fixes (commits f89fdc5, 28a16ed)
    // choice nouns like «քարին» / «երգին» / «բուին» reduce to 3-char
    // stems («քար» / «երգ» / «բու»). But the body extraction was
    // still calling ExtractArmenianStems(body, minLen: 4), so a body
    // that mentioned the bare 3-char form was invisible to the
    // grounding check. The slice that produced this test family
    // lowered ChoiceNounsAppearInBody's body-side extraction to
    // minLen=3 ONLY for that helper, so bare 3-char body nouns now
    // match.

    [Fact]
    public void ChoiceNounPresentWithBareThreeCharBodyNoun_ShouldNotWarn_Stone()
    {
        // S05-Turn-1 class from the 20260524-200655 evidence.
        var input = new TurnEvaluationInput
        {
            Body = "Աղվեսը տեսավ մի փայլուն քար գետի մոտ։ Քամին փչում էր մեղմորեն։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք քարին",
            ChoiceB = "Լսենք քամուն",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounPresentWithBareThreeCharBodyNoun_ShouldNotWarn_Song()
    {
        // S05-Turn-3 class from the 20260524-200655 evidence.
        var input = new TurnEvaluationInput
        {
            Body = "Քարը ձայն էր հանում՝ մի հին մոռացված երգ։ Աղվեսը կանգ առավ զարմացած։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք երգին",
            ChoiceB = "Հարցնենք աղվեսին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounPresentWithBareThreeCharBodyNoun_ShouldNotWarn_Owl()
    {
        // «բու» is 3 Armenian chars (բ + ո + ւ; the «ու» digraph is
        // two code points). After the slice's body-side fix, bare
        // «բու» in the body is now extracted as a stem and matches
        // choice «բուին» (5 chars, strips «ին» → «բու»).
        var input = new TurnEvaluationInput
        {
            Body = "Անտառում ապրում էր մի իմաստուն բու, որը գիտեր բոլոր գիշերային գաղտնիքները։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք բուին",
            ChoiceB = "Հարցնենք գաղտնիքների մասին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounStillWarns_WhenBareThreeCharNounAbsent()
    {
        // Negative-space pin: lowering the floor must NOT silence
        // legitimate noun-grounding gaps. The body genuinely never
        // mentions «քար» in any form.
        var input = new TurnEvaluationInput
        {
            Body = "Աղվեսը քայլում էր անտառում միայնակ ու երազում էր նոր ընկերների մասին։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք քարին",
            ChoiceB = "Հարցնենք աղվեսին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_noun_not_in_body", w);
    }

    // ===== «-ու» instrumental case (closes S05 Turn 2 case) =====
    //
    // The 20260524-200655 live evidence flagged S05 Turn 2 because
    // body «քամին» stemmed to «քամ» (via the choice-side relaxed
    // «-ին» rule) but choice «քամու» had NO matching ending in
    // the stemmer's list and stayed as «քամու» — asymmetric stems
    // for the same noun. Adding «ու» to ShortStemAllowedEndings
    // (with the 3-char-result floor — «քամու» is 5 codepoints
    // because «ու» is the digraph «ո»+«ւ») closes that gap.

    [Fact]
    public void ArmenianStem_StripsUFromWindCase()
    {
        Assert.Equal("քամ", Evaluators.ArmenianStem("քամու"));
    }

    [Fact]
    public void ChoiceNounPresentWithUCase_ShouldNotWarn()
    {
        // S05 Turn 2 class: body uses dative «քամին» which stems
        // to «քամ»; choice uses instrumental «քամու» which NOW
        // also stems to «քամ». Stems match → no warning.
        // («Լսենք» is in the ignore-prefix list as exact «լսենք»,
        // so it does NOT contribute a noun stem. «ձայնը» stems
        // to «ձայն» — body also mentions «ձայն», so it overlaps
        // independently. The defining check here is the «քամու»
        // contribution, which the new rule normalizes.)
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ աղվեսը նստած էր ծառի տակ և լսում էր քամին, "
                   + "որի ձայնը նրա սիրտը խաղաղեցնում էր։ "
                   + new string('ա', 150),
            ChoiceA = "Լսենք քամու ձայնը",
            ChoiceB = "Մոտենանք ծառին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounStillWarns_WhenUCaseNounAbsent()
    {
        // Negative-space pin: the new rule must not silence a
        // real grounding gap. The body genuinely never mentions
        // «քամ» in any form.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ ոզնին քայլում էր անտառում միայնակ ու հանգիստ։ "
                   + new string('ա', 150),
            ChoiceA = "Լսենք քամու ձայնը",
            ChoiceB = "Մոտենանք ոզնիին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void FirstSentenceRecapOverlap_StillExcludesShortTokens()
    {
        // Guard: this slice's minLen=3 change is BODY-side noun
        // grounding ONLY. FirstSentenceRecapOverlap MUST keep its
        // own internal minLen=4 — short tokens like «մեկ» / «քար» /
        // «կար» / «տակ» (all 3 chars) must NOT be counted as
        // recap-overlap signal, even though they're now visible to
        // the noun-grounding extractor.
        //
        // Fixture: two first-sentences that share several 3-char
        // tokens and only ONE ≥4-char token («սարը» = "the
        // mountain"). With minLen=4 the union is 3 (սարը + dashti
        // + dashtum), the intersection is 1 (սարը), Jaccard ≈ 0.33
        // — well under the 0.60 threshold. If the recap helper
        // dropped to 3, the shared 3-char tokens would push overlap
        // way over the threshold.
        var prev = "Մեկ քար կար տակ դաշտում սարը ուներ։";
        var next = "Մեկ քար կար տակ դաշտի սարը գտնվում էր։";

        var overlap = Evaluators.FirstSentenceRecapOverlap(prev, next);

        Assert.True(overlap < Evaluators.RecapOverlapThreshold,
            $"recap overlap must stay below threshold (was {overlap:F2}) — " +
            "the body-side noun-grounding fix must not lower the recap floor");
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

    // ===== Same-turn noun grounding (Phase 1) =====
    //
    // The four tests below pin the "concrete noun must appear in body"
    // rule that closes the verb-only grounding gap observed in the
    // 20260524-121218 live evidence (Turn 0 of S01 had ChoiceA
    // «Մոտենանք տերևին» while the body talked about a flower under a
    // branch — never mentioning a leaf).

    [Fact]
    public void ChoiceNounMissingFromBody_ShouldWarn()
    {
        // Body has the verb «մոտեցավ» (so the OLD verb-stem grounding
        // would have passed) but never mentions «տերև» — the noun
        // ChoiceA introduces.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ ոզնին մոտեցավ ճյուղին և տեսավ վարդագույն ծաղկիկ։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք տերևին",
            ChoiceB = "Նայենք ծաղիկին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounPresentInBody_ShouldNotWarn()
    {
        // Same shape, but the body now contains «տերև» — ChoiceA is
        // grounded and the new warning must NOT fire.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ ոզնին մոտեցավ տերևին և տեսավ վարդագույն ծաղկիկ։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք տերևին",
            ChoiceB = "Նայենք ծաղիկին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceBNounMissingFromBody_ShouldWarn()
    {
        // Body mentions the bird but not the house. ChoiceB introduces
        // «տուն» that the body never establishes.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ աստղը լսեց թռչունիկի գեղգեղանքը անտառի ծառերի վերևից։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք թռչունիկին",
            ChoiceB = "Նայենք տունին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_b_noun_not_in_body", w);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounStems_FilterDropsVerbsAndFillers_KeepsConcreteNouns()
    {
        // «մոտենանք» → stem մոտե, filtered. «նապաստակին» → stem
        // նապաստակ, kept. «փոքրիկ» filler stem prefix «փոքր», filtered.
        var nouns = Evaluators.ChoiceNounStems("Մոտենանք փոքրիկ նապաստակին");
        Assert.Single(nouns);
        Assert.Contains("նապաստակ", nouns);
    }

    [Fact]
    public void ChoiceNounStems_AllVerb_NoConcreteNouns()
    {
        // A choice whose tokens are all verb-like must yield zero noun
        // candidates so the caller treats it as "grounded by default"
        // (no noun warning).
        Assert.Empty(Evaluators.ChoiceNounStems("Շարունակել"));
        Assert.Empty(Evaluators.ChoiceNounStems("Մոտենանք"));
    }

    // ===== Cross-turn repeated choice-pair (Phase 2) =====

    [Fact]
    public void RepeatedChoicePairInSameSession_ShouldWarnOnLaterTurn()
    {
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք թռչունիկին", "Նայենք տունին"),     // turn 0 — first
            ("Հետևենք թռչունիկին", "Լսենք թռչունիկի երգը"),  // turn 1 — different
            ("Մոտենանք թռչունիկին", "Նայենք տունին"),     // turn 2 — REPEAT
        };
        var warnings = Evaluators.DetectRepeatedChoicePairs(pairs);

        Assert.Equal(3, warnings.Count);
        Assert.Empty(warnings[0]);                             // first occurrence is silent
        Assert.Empty(warnings[1]);
        Assert.Contains("choices_repeated_from_earlier_turn", warnings[2]);
    }

    [Fact]
    public void RepeatedChoicePair_NormalizationIgnoresWhitespaceCaseAndTrailingPunct()
    {
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք թռչունիկին", "Նայենք տունին"),
            ("մոտենանք  թռչունիկին։", "ՆԱՅԵՆՔ ՏՈՒՆԻՆ."),     // same after normalize
        };
        var warnings = Evaluators.DetectRepeatedChoicePairs(pairs);
        Assert.Contains("choices_repeated_from_earlier_turn", warnings[1]);
    }

    [Fact]
    public void RepeatedChoicePairAcrossDifferentSessions_ShouldNotMatter()
    {
        // Session A finishes; session B starts fresh. The detector is
        // intentionally scoped to a single call — there is no global
        // state across calls, so the same pair in two separate calls
        // never trips the warning.
        var session1 = new List<(string? A, string? B)>
        {
            ("Մոտենանք թռչունիկին", "Նայենք տունին"),
        };
        var session2 = new List<(string? A, string? B)>
        {
            ("Մոտենանք թռչունիկին", "Նայենք տունին"),
        };
        var w1 = Evaluators.DetectRepeatedChoicePairs(session1);
        var w2 = Evaluators.DetectRepeatedChoicePairs(session2);
        Assert.Empty(w1[0]);
        Assert.Empty(w2[0]);
    }

    [Fact]
    public void RepeatedChoicePair_NullOrEmptyChoices_Skipped()
    {
        // A turn that recorded no choices (safety fallback / final turn)
        // must not be picked up by the comparator: a null/null pair
        // repeated across turns should NOT warn.
        var pairs = new List<(string? A, string? B)>
        {
            (null, null),
            ("", ""),
            (null, "X"),
            ("Մոտենանք թռչունիկին", "Նայենք տունին"),
        };
        var warnings = Evaluators.DetectRepeatedChoicePairs(pairs);
        Assert.All(warnings, Assert.Empty);
    }

    // ===== Cross-turn individual-choice repetition =====
    //
    // Complementary to the exact-pair detector. Catches the S01
    // «approach leaf / look at leaf» ping-pong pattern observed in
    // the 20260524-181621 evidence, where the EXACT (A,B) pair
    // never repeated but the same individual choice text reappeared
    // on a later turn (sometimes on the OTHER side).

    [Fact]
    public void RepeatedIndividualChoice_SameChoiceAOnLaterTurn_ShouldWarn()
    {
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),      // turn 0 — first
            ("Հետևենք քամուն", "Լսենք երգին"),         // turn 1 — distinct
            ("Մոտենանք տերևին", "Լսենք քամուն"),       // turn 2 — A repeats
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);

        Assert.Equal(3, warnings.Count);
        Assert.Empty(warnings[0]);
        Assert.Empty(warnings[1]);
        Assert.Contains("choice_repeated_from_earlier_turn", warnings[2]);
    }

    [Fact]
    public void RepeatedIndividualChoice_ReappearsAsChoiceB_ShouldWarn()
    {
        // An A-side choice that reappears as a later turn's B-side
        // also counts. Order-insensitive across turns.
        var pairs = new List<(string? A, string? B)>
        {
            ("Նայենք տերևին", "Մոտենանք ծառին"),        // turn 0
            ("Մոտենանք քամուն", "Լսենք երգին"),         // turn 1
            ("Գնանք պարտեզ", "Լսենք ձայնը"),            // turn 2
            ("Լսենք ձայնը", "Նայենք տերևին"),           // turn 3 — A==prev,B==first
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);

        // Turn 2 itself was clean (Գնանք պարտեզ + Լսենք ձայնը — new).
        // Turn 3's A («Լսենք ձայնը») repeats turn 2's B; turn 3's B
        // («Նայենք տերևին») repeats turn 0's A. Either one trips
        // the warning; the cap is one per turn.
        Assert.Empty(warnings[0]);
        Assert.Empty(warnings[1]);
        Assert.Empty(warnings[2]);
        Assert.Contains("choice_repeated_from_earlier_turn", warnings[3]);
        Assert.Single(warnings[3]); // one cap even though both sides match
    }

    [Fact]
    public void RepeatedIndividualChoice_NormalizationIgnoresWhitespaceCaseAndTrailingPunct()
    {
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
            // Same A semantically — extra spaces, lowercase, trailing
            // verjaket. Must still match.
            ("մոտենանք  տերևին։", "Լսենք քամուն"),
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);
        Assert.Contains("choice_repeated_from_earlier_turn", warnings[1]);
    }

    [Fact]
    public void RepeatedIndividualChoice_DifferentObject_ShouldNotWarn()
    {
        // Same verb, different object across turns — no surface-string
        // match, so the detector must NOT warn. Confirms that the
        // detector is exact-normalized-string matching ONLY: it does
        // not collapse "approach X" with "approach Y" as related.
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
            ("Մոտենանք ծիածանին", "Լսենք քամուն"), // same verb, new object
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);
        Assert.All(warnings, Assert.Empty);
    }

    [Fact]
    public void RepeatedIndividualChoice_DifferentVerbSameObject_DoesNotWarn()
    {
        // Same object («տերև») but distinct verb forms — surface
        // strings differ, no warning. Pinned per the slice's spec:
        // "Same object but meaningfully different action: do NOT
        // implement semantic similarity yet."
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
            ("Վերցնենք տերևը", "Լսենք քամուն"),
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);
        Assert.All(warnings, Assert.Empty);
    }

    [Fact]
    public void RepeatedIndividualChoice_DifferentSession_ShouldNotMatter()
    {
        // Each call holds its own `seen` set. The same choice in two
        // separate calls never trips the warning.
        var session1 = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
        };
        var session2 = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
        };
        var w1 = Evaluators.DetectRepeatedIndividualChoices(session1);
        var w2 = Evaluators.DetectRepeatedIndividualChoices(session2);
        Assert.Empty(w1[0]);
        Assert.Empty(w2[0]);
    }

    [Fact]
    public void RepeatedIndividualChoice_NullOrEmptyChoices_DoNotCount()
    {
        // Empty/null sides are skipped from registration and matching.
        // A later turn that records null/null after a real turn must
        // not warn.
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
            (null, null),
            ("", ""),
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);
        Assert.All(warnings, Assert.Empty);
    }

    [Fact]
    public void RepeatedIndividualChoice_SameTurnIdenticalChoices_DoesNotSelfMatch()
    {
        // The exact-pair check sees A == B (covered by
        // `choices_identical`); the individual-repeat detector must
        // NOT spuriously self-flag on the SAME turn just because A
        // equals B. Subtle bug guard: the detector reads `seen` via
        // Contains BEFORE adding.
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Մոտենանք տերևին"),
        };
        var warnings = Evaluators.DetectRepeatedIndividualChoices(pairs);
        Assert.Empty(warnings[0]);
    }

    [Fact]
    public void RepeatedIndividualChoice_FiresAlongsideExactPair_WhenPairAlsoRepeats()
    {
        // When the exact (A, B) pair repeats, BOTH detectors should
        // fire — they are complementary signals. Pin that they
        // coexist without de-duplication: pair = "this turn happened
        // before", individual = "at least one option happened before".
        var pairs = new List<(string? A, string? B)>
        {
            ("Մոտենանք տերևին", "Նայենք ծառին"),
            ("Մոտենանք տերևին", "Նայենք ծառին"),
        };
        var pairWarnings = Evaluators.DetectRepeatedChoicePairs(pairs);
        var indWarnings = Evaluators.DetectRepeatedIndividualChoices(pairs);

        Assert.Contains("choices_repeated_from_earlier_turn", pairWarnings[1]);
        Assert.Contains("choice_repeated_from_earlier_turn", indWarnings[1]);
    }

    // ===== Verdict integration: new warnings reach the right axis =====

    [Fact]
    public void EvaluateSession_NounMissingWarnings_DockChoiceQualityToWarn()
    {
        // Two noun-missing warnings (across two turns) drop ChoiceQuality
        // by 30 (15 each) → 70, which is < 80 → WARN.
        var v = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            new[] { "choice_a_noun_not_in_body" },
            new[] { "choice_b_noun_not_in_body" },
        });
        Assert.Equal(70, v.ChoiceQualityScore);
        Assert.Equal("WARN", v.OverallVerdict);
    }

    // ===== Stemmer-driven false-positive fixes for noun grounding =====
    //
    // The four tests below pin specific cases observed in the
    // 20260524-151621 live run where the old stemmer mismatched body
    // and choice forms of the SAME concrete noun. They also pin that
    // the genuinely missing nouns (քարտեզ / ուղի) still surface a
    // warning — improving the stemmer must not silence real positives.

    [Fact]
    public void ChoiceNounPresentAsPluralInBody_ShouldNotWarn()
    {
        // S01 turn 0 case: body uses the plural genitive «տերևների»,
        // choice uses the singular dative «տերևին». Both must reduce
        // to «տերև» so the noun grounding check finds the overlap.
        var input = new TurnEvaluationInput
        {
            Body = "Փոքրիկ ոզնին քայլեց անտառով և տեսավ կոտրած տերևների միջից մի ծաղկիկ։ "
                   + new string('ա', 150),
            ChoiceA = "Նայենք տերևին",
            ChoiceB = "Մոտենանք ծաղկիկին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounMissingStillWarns_ForMap()
    {
        // S01 turn 2 case: choice introduces «քարտեզ» that the body
        // never establishes. The improved stemmer MUST NOT mask this
        // real grounding break.
        var input = new TurnEvaluationInput
        {
            Body = "Ոզնին ու ընկերոջը սկսեցին նայել փայլուն դրամատիկը։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք քարտեզին",
            ChoiceB = "Հարցնենք ընկերոջը",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounMissingStillWarns_ForPath()
    {
        // S01 turn 2 case (B-side): choice introduces «ուղի». The
        // body never mentions it — short-noun length guard means
        // «ուղին» itself is the stem and the absence is still seen.
        var input = new TurnEvaluationInput
        {
            Body = "Ոզնին ու ընկերոջը կարդացին դրամատիկի վրա դասված նշանները։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք ընկերոջը",
            ChoiceB = "Նայենք ուղին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.Contains("choice_b_noun_not_in_body", w);
    }

    [Fact]
    public void ChoiceNounMatchesViaPossessiveSuffixes_ShouldNotWarn()
    {
        // S01 turn 1 case: body has the possessive-definite «ընկերոջը»,
        // choice has the possessive-dative «ընկերոջին». Both must
        // reduce to «ընկեր» so the noun grounding check finds the
        // overlap. Without «ոջը» / «ոջին» stripping this was a
        // false positive in the previous run.
        var input = new TurnEvaluationInput
        {
            Body = "Ոզնին ուրախությամբ մոտեցավ իր ընկերոջը և ցույց տվեց ոսկեգույն դրամատիկը։ "
                   + new string('ա', 150),
            ChoiceA = "Մոտենանք ընկերոջին",
            ChoiceB = "Հարցնենք ոսկեգույնի մասին",
            HasChoices = true
        };
        var w = Evaluators.EvaluateTurn(input);
        Assert.DoesNotContain("choice_a_noun_not_in_body", w);
    }

    [Fact]
    public void EvaluateSession_RepeatedPair_DocksContinuationCoherenceToWarn()
    {
        // One repeated-pair warning drops ContinuationCoherence by 20 →
        // 80. 80 is NOT < 80, so the verdict stays PASS on this alone.
        // Two repeats land it at 60, well under the WARN bar.
        var vOne = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(),
            new[] { "choices_repeated_from_earlier_turn" },
        });
        Assert.Equal(80, vOne.ContinuationCoherenceScore);
        Assert.Equal("PASS", vOne.OverallVerdict);

        var vTwo = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(),
            new[] { "choices_repeated_from_earlier_turn" },
            new[] { "choices_repeated_from_earlier_turn" },
        });
        Assert.Equal(60, vTwo.ContinuationCoherenceScore);
        Assert.Equal("WARN", vTwo.OverallVerdict);
    }

    [Fact]
    public void EvaluateSession_RepeatedIndividualChoice_DocksContinuationCoherenceToWarn()
    {
        // Same -20-per-hit deduction as the exact-pair warning, so a
        // single isolated individual repeat keeps the session at PASS
        // (80 is not < 80) while a second repeat drives it to WARN.
        // This is the per-turn cap (one warning per affected turn)
        // working as designed — three turns with repeated individual
        // choices = three warnings = 60.
        var vOne = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(),
            new[] { "choice_repeated_from_earlier_turn" },
        });
        Assert.Equal(80, vOne.ContinuationCoherenceScore);
        Assert.Equal("PASS", vOne.OverallVerdict);

        var vTwo = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(),
            new[] { "choice_repeated_from_earlier_turn" },
            new[] { "choice_repeated_from_earlier_turn" },
        });
        Assert.Equal(60, vTwo.ContinuationCoherenceScore);
        Assert.Equal("WARN", vTwo.OverallVerdict);
    }

    [Fact]
    public void EvaluateSession_BothPairAndIndividualWarningsOnSameTurn_StackToFortyDeduction()
    {
        // When the exact (A, B) pair repeats, BOTH the pair warning
        // and the individual warning fire on the same turn. They
        // stack (-20 + -20 = -40). This is the design contract:
        // exact-pair penalty (40) ≥ individual-only penalty (20).
        var v = Evaluators.EvaluateSession(new List<IReadOnlyList<string>>
        {
            Array.Empty<string>(),
            new[] {
                "choices_repeated_from_earlier_turn",
                "choice_repeated_from_earlier_turn",
            },
        });
        Assert.Equal(60, v.ContinuationCoherenceScore);
        Assert.Equal("WARN", v.OverallVerdict);
    }
}

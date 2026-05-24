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
    public void ArmenianStem_DoesNotStripDefiniteArticleWhenWouldShortenBelowFour()
    {
        // «արջը» is 4 chars; stripping «ը» would leave a 3-char stem,
        // which the >=4-char length guard refuses. Stem stays as-is.
        Assert.Equal("արջը", Evaluators.ArmenianStem("արջը"));
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
}

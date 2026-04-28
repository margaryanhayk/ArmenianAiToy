using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase Cat-B: runtime coherence gate over parsed CHOICE_A / CHOICE_B
/// labels. Body is the prose AFTER TailBlockParser has stripped the tail.
/// The gate is deterministic and stateless — these tests cover the
/// pass / soft-pass / fail / repair contract directly, without ChatService.
/// </summary>
public class StoryChoiceCoherenceGateTests
{
    private readonly IStoryChoiceCoherenceGate _gate = new StoryChoiceCoherenceGate();

    // ─────────────────────────────────────────────────────────────────
    // Pass
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BothChoicesMentionBodyEntities_Passes()
    {
        // Body names a bird and a bell. Both choices act on entities
        // already named in the body.
        var body = "Փոքրիկ թռչունիկը նստեց պատուհանի մոտ։ "
                 + "Ներսում զանգակը կամաց հնչեց։ "
                 + "Թռչունիկը նայեց զանգակին։";
        var result = _gate.Evaluate(body, "Մոտենանք թռչունիկին", "Լսենք զանգակը");
        Assert.True(result.IsCoherent);
        Assert.False(result.ShouldRetry);
        Assert.Null(result.RepairedChoiceA);
        Assert.Null(result.RepairedChoiceB);
        Assert.Equal("both_grounded", result.Reason);
    }

    [Fact]
    public void GroundedChoice_DespiteMorphologyVariation_Passes()
    {
        // Body uses «նապաստակը» (definite); choice uses «նապաստակին»
        // (dative). Suffix-strip should fold both to the same stem.
        var body = "Փոքրիկ նապաստակը կանգնեց ծառի տակ։ Քամին քիչ էր։";
        var result = _gate.Evaluate(body, "Կանչենք նապաստակին", "Տեսնենք ծառը");
        Assert.True(result.IsCoherent);
        Assert.Equal("both_grounded", result.Reason);
    }

    // ─────────────────────────────────────────────────────────────────
    // Soft pass
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OneGenericActionOnly_OneGrounded_SoftPasses()
    {
        // Choice A is grounded in body; Choice B is a generic action
        // frame ("look around") with no concrete body entity. Soft pass.
        var body = "Փոքրիկ խխունջը մտավ պարտեզ։ Ծաղիկները քնած էին։";
        var result = _gate.Evaluate(body, "Մոտենանք ծաղիկներին", "Նայենք շուրջը");
        Assert.True(result.IsCoherent);
        Assert.False(result.ShouldRetry);
        Assert.Equal("one_action_only_other_grounded", result.Reason);
    }

    // ─────────────────────────────────────────────────────────────────
    // Fail — both ungrounded
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BothChoicesIntroduceUnmentionedObjects_Fails()
    {
        // Body talks about a garden and a bell; choices wander into a
        // cave and a dragon — the canonical Cat-B failure mode.
        var body = "Փոքրիկ սկյուռիկը կանգնեց պարտեզում։ "
                 + "Զանգակը զանգում էր կամաց։";
        var result = _gate.Evaluate(body, "Մտնենք քարանձավը", "Կանչենք վիշապին");
        Assert.False(result.IsCoherent);
        Assert.True(result.ShouldRetry);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);
    }

    // ─────────────────────────────────────────────────────────────────
    // Fail — strongly ungrounded (multiple new concrete tokens)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OneChoiceIntroducesMultipleNewConcreteNouns_Fails()
    {
        // Body talks about a bunny and a meadow. Choice A is grounded
        // (bunny); Choice B introduces TWO unmentioned concrete nouns
        // (key + cave) — strong fail per the ≥2 ungrounded rule.
        var body = "Փոքրիկ նապաստակը ցատկեց մարգագետնում։ "
                 + "Քամին մեղմ շոյում էր խոտը։";
        var result = _gate.Evaluate(body, "Կանչենք նապաստակին", "Բռնենք բանալին քարանձավում");
        Assert.False(result.IsCoherent);
        Assert.True(result.ShouldRetry);
        Assert.Equal("choice_introduces_unmentioned_objects", result.Reason);
    }

    // ─────────────────────────────────────────────────────────────────
    // Fail — too similar
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GroundedButTooSimilar_FailsViaChoiceDiversity()
    {
        // Both choices grounded in body but share the same first verb.
        // ChoiceDiversity should fire and the gate should surface it.
        var body = "Փոքրիկ թռչունիկը նայեց տուփին և դռանը։";
        var result = _gate.Evaluate(body, "Բացենք տուփը", "Բացենք դուռը");
        Assert.False(result.IsCoherent);
        Assert.Equal("choices_too_similar", result.Reason);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);
    }

    // ─────────────────────────────────────────────────────────────────
    // Repair
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Repair_UsesBodyAnchors_NotInventedNouns()
    {
        var body = "Փոքրիկ խխունջը մտավ պարտեզ։ "
                 + "Մի փոքրիկ ծաղիկ կամաց բացվեց։ "
                 + "Թիթեռը նստեց տերևին։";
        var result = _gate.Evaluate(body, "Մտնենք քարանձավը", "Կանչենք վիշապին");
        Assert.False(result.IsCoherent);

        // Repair must come from body's actual surface forms.
        var repA = result.RepairedChoiceA!;
        var repB = result.RepairedChoiceB!;

        // The repaired pair must differ.
        Assert.NotEqual(repA, repB);

        // Neither repair may carry the BAD-example concrete nouns from
        // the rejected pair.
        Assert.DoesNotContain("քարանձավ", repA, StringComparison.Ordinal);
        Assert.DoesNotContain("քարանձավ", repB, StringComparison.Ordinal);
        Assert.DoesNotContain("վիշապ", repA, StringComparison.Ordinal);
        Assert.DoesNotContain("վիշապ", repB, StringComparison.Ordinal);

        // At least one repaired label must reference an actual body word.
        var bodyHasAnchor =
               repA.Contains("թիթեռ", StringComparison.Ordinal)
            || repA.Contains("ծաղիկ", StringComparison.Ordinal)
            || repA.Contains("խխունջ", StringComparison.Ordinal)
            || repA.Contains("տերև", StringComparison.Ordinal)
            || repA.Contains("պարտեզ", StringComparison.Ordinal)
            || repB.Contains("թիթեռ", StringComparison.Ordinal)
            || repB.Contains("ծաղիկ", StringComparison.Ordinal)
            || repB.Contains("խխունջ", StringComparison.Ordinal)
            || repB.Contains("տերև", StringComparison.Ordinal)
            || repB.Contains("պարտեզ", StringComparison.Ordinal);
        Assert.True(bodyHasAnchor,
            $"Repair must reference a body anchor. Got A=\"{repA}\" B=\"{repB}\"");
    }

    [Fact]
    public void Repair_PrefersAnchorsFromLatestSentences()
    {
        // The most recent sentence carries «թռչունիկ»; older sentences
        // carry «խխունջ» / «պարտեզ». First repaired anchor should be
        // from the latest sentence.
        var body = "Փոքրիկ խխունջը մտավ պարտեզ։ "
                 + "Թռչունիկը նստեց ճյուղին և երգեց։";
        var result = _gate.Evaluate(body, "Մտնենք քարանձավը", "Կանչենք վիշապին");
        Assert.False(result.IsCoherent);
        Assert.Contains("թռչունիկ", result.RepairedChoiceA!, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_FallsBackToGenericPair_OnlyWhenBodyHasNoAnchors()
    {
        // Pure stop-word body has no anchor. Last-resort generic pair
        // is the only path that should ever reach it.
        var body = "Այս է, որ էր։ Այնպես, ինչ էր։";
        var result = _gate.Evaluate(body, "Մտնենք քարանձավը", "Կանչենք վիշապին");
        Assert.False(result.IsCoherent);
        Assert.Equal("Շարունակենք պատմությունը", result.RepairedChoiceA);
        Assert.Equal("Նայենք շուրջը", result.RepairedChoiceB);
    }

    // ─────────────────────────────────────────────────────────────────
    // Null / missing inputs
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MissingChoiceA_Fails_WithRepair()
    {
        var body = "Փոքրիկ նապաստակը նայեց տուփին։";
        var result = _gate.Evaluate(body, null, "Կանչենք նապաստակին");
        Assert.False(result.IsCoherent);
        Assert.Equal("missing_choice", result.Reason);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);
    }

    // ─────────────────────────────────────────────────────────────────
    // Live QA regression — 2026-04-27 voice-MVP butterfly story
    // ─────────────────────────────────────────────────────────────────

    // Body returned by the live `/api/chat` text-input QA. Reused across
    // a few tests below so the exact prose is pinned in one place.
    private const string LiveButterflyBody =
          "Հին ժամանակներում, մի փոքրիկ թիթեռ ապրում էր ծաղկավոր պարտեզում։ "
        + "Թիթեռը շատ էր սիրում պարել ծաղիկների տերևների վրա։ "
        + "Մի անգամ, երբ նա պտտվում էր, տեսավ մի առեղծվածային լուսավոր քար, "
        + "որը փայլում էր մեղմ լույսով և ծաղիկներին հատուկ հոտով։ "
        + "Թիթեռը մոտեցավ քարին և զարմացավ նրա գեղեցկությամբ։";

    [Fact]
    public void LiveQA_BadPair_IsRejected_AndRepairIsBodyAnchoredAndWellFormed()
    {
        // The 2026-04-27 voice-MVP failure mode. Choice A coined a verb-
        // derived pseudo-noun («հպենք», 1pl optative «let's touch» turned
        // into a dative); Choice B fabricated a compound noun
        // («շատրվանաքար», "fountain-stone") whose «շատրվան» prefix is
        // absent from the body — body only has «քար».
        var result = _gate.Evaluate(
            LiveButterflyBody,
            "Մոտենանք հպենքին",
            "Նայենք շատրվանաքարին");

        Assert.False(result.IsCoherent);
        Assert.True(result.ShouldRetry);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);

        // Repair must NOT carry any of the rejected coined tokens.
        Assert.DoesNotContain("հպենք", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("հպենք", result.RepairedChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("շատրվան", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("շատրվան", result.RepairedChoiceB!, StringComparison.Ordinal);

        // Repair must NOT pick body verbs as anchors. «մոտեցավ» / «տեսավ»
        // / «զարմացավ» under the Dative «-ին» produce gibberish like
        // «մոտեցավին» — the original failure mode of this fix.
        Assert.DoesNotContain("մոտեցավ", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("մոտեցավ", result.RepairedChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("տեսավ", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("տեսավ", result.RepairedChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("զարմացավ", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("զարմացավ", result.RepairedChoiceB!, StringComparison.Ordinal);

        // The repaired pair must differ.
        Assert.NotEqual(result.RepairedChoiceA, result.RepairedChoiceB);

        // At least one repaired label must reference an actual body noun
        // («թիթեռ», «քար», «ծաղիկ», «պարտեզ»).
        var refsBody =
               result.RepairedChoiceA!.Contains("թիթեռ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("քար", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("ծաղիկ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("պարտեզ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("թիթեռ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("քար", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("ծաղիկ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("պարտեզ", StringComparison.Ordinal);
        Assert.True(refsBody,
            $"Repair must reference a body noun. Got A=\"{result.RepairedChoiceA}\" B=\"{result.RepairedChoiceB}\"");
    }

    [Fact]
    public void Choice_FabricatedCompoundEndingInBodyStem_IsRejected()
    {
        // Body has only «քար» (stone). Choice extends it into a coined
        // compound «շատրվանաքար» whose «շատրվան» (fountain) prefix is
        // absent from the body. The gate must NOT treat this as a
        // morphological match — that's a fabrication, not inflection.
        var body = "Թիթեռը մոտեցավ քարին և զարմացավ նրա գեղեցկությամբ։";
        var result = _gate.Evaluate(body, "Մոտենանք քարին", "Նայենք շատրվանաքարին");
        Assert.False(result.IsCoherent);
        Assert.True(result.ShouldRetry);
    }

    [Fact]
    public void Choice_VerbDerivedDative_IsRejected()
    {
        // Choice token «հպենք» is a 1pl-optative verb form coerced into
        // a noun via the Dative «-ին». The body never mentions «հպվել»
        // / touching, so the choice introduces a new (and unnatural)
        // concept — must be rejected.
        var body = "Թիթեռը նայեց ծաղիկին և թռավ։";
        var result = _gate.Evaluate(body, "Մոտենանք ծաղիկին", "Մոտենանք հպենքին");
        Assert.False(result.IsCoherent);
    }

    [Fact]
    public void Repair_DoesNotEmitPastTenseVerbForms_AsAnchors()
    {
        // Body whose latest sentence is verb-heavy («մոտեցավ»,
        // «զարմացավ»). Without the verb-form filter, the deterministic
        // repair would pick «մոտեցավ» and emit «մոտեցավին» under the
        // Dative transform — gibberish to a 4–7 yo. The filter must skip
        // raw past-tense forms ending in «-ավ» and pick body nouns
        // («թիթեռ», «քար») instead.
        var body = "Թիթեռը մոտեցավ քարին և զարմացավ նրա գեղեցկությամբ։";
        var result = _gate.Evaluate(body, "Մտնենք քարանձավը", "Կանչենք վիշապին");
        Assert.False(result.IsCoherent);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);
        Assert.DoesNotContain("մոտեցավ", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("մոտեցավ", result.RepairedChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("զարմացավ", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("զարմացավ", result.RepairedChoiceB!, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────
    // Live QA regression — 2026-04-28 squirrel story / auxiliary stems
    // ─────────────────────────────────────────────────────────────────

    // Body returned by the live `/api/chat` text-input QA after the
    // 6dae9a0 push. Reused across the auxiliary-stem regression tests
    // below so the exact prose is pinned in one place.
    private const string LiveSquirrelBody =
          "Փոքրիկ սկյուռիկը վազեց ծառերի միջով։ "
        + "Ամեն կողմից լսվում էր քամու մեղմ շնչառությունը։ "
        + "Հանկարծ նա տեսավ մի փայլուն իր ճյուղերի մեջ։ "
        + "Սրտում ուրախություն զգալով, նա մոտեցավ։ "
        + "Ի՞նչ կարող է լինել այդտեղ։";

    [Fact]
    public void LiveQA_AuxiliaryStemDatives_AreRejected_AndRepairAvoidsBlockedStems()
    {
        // 2026-04-28 voice-MVP follow-up regression. The LLM borrowed an
        // auxiliary stem («կարող» from "Ի՞նչ կարող է լինել") and the
        // verb-«to be» stem («լին» from «լինել») and inflected them as
        // Dative pseudo-nouns («կարողին» / «լինին»). Stem-overlap
        // grounding accepted them because the body literally contains
        // those stems — but neither result is a real Armenian noun.
        // The blocked-content-stem stop-list keeps both out of bodyStems
        // and out of repair anchors.
        var result = _gate.Evaluate(
            LiveSquirrelBody,
            "Մոտենանք կարողին",
            "Նայենք լինին");

        Assert.False(result.IsCoherent);
        Assert.True(result.ShouldRetry);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);

        // Repair must NOT carry the rejected pseudo-noun forms.
        Assert.DoesNotContain("կարողին", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("կարողին", result.RepairedChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("լինին", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("լինին", result.RepairedChoiceB!, StringComparison.Ordinal);

        // Repair must NOT pick the blocked stems themselves as anchors.
        Assert.DoesNotContain("կարող", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("կարող", result.RepairedChoiceB!, StringComparison.Ordinal);
        Assert.DoesNotContain("լին", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("լին", result.RepairedChoiceB!, StringComparison.Ordinal);

        // Sanity: deterministic repair must not collapse to the
        // last-resort generic pair when the body has usable content
        // anchors («սրտ» / «ուրախ» / «սկյուռիկ» / «ճյուղ» / etc.).
        Assert.NotEqual("Շարունակենք պատմությունը", result.RepairedChoiceA);

        // The two repaired labels must differ.
        Assert.NotEqual(result.RepairedChoiceA, result.RepairedChoiceB);

        // At least one repaired label should reference a real body word
        // («սկյուռիկ» / «ծառ» / «քամ» / «ճյուղ» / «սրտ» / «ուրախ»).
        var refsBody =
               result.RepairedChoiceA!.Contains("սկյուռիկ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("ծառ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("քամ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("ճյուղ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("սրտ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("ուրախ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("սկյուռիկ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("ծառ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("քամ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("ճյուղ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("սրտ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("ուրախ", StringComparison.Ordinal);
        Assert.True(refsBody,
            $"Repair must reference a body noun. Got A=\"{result.RepairedChoiceA}\" B=\"{result.RepairedChoiceB}\"");
    }

    [Fact]
    public void Choice_AuxiliaryStem_IsNotGroundedByBodyOccurrence()
    {
        // Body literally contains «կարող» but only as the auxiliary
        // "can". The choice token reuses that stem as a Dative-coerced
        // noun. The blocked-stem rule must remove «կարող» from
        // bodyStems so the grounding heuristic cannot accept this pair.
        var body = "Թիթեռը նայեց ծաղիկին։ Ի՞նչ կարող է լինել այդտեղ։";
        var result = _gate.Evaluate(
            body,
            "Մոտենանք ծաղիկին",
            "Նայենք կարողին");
        Assert.False(result.IsCoherent);
    }

    [Fact]
    public void Choice_VerbToBeStem_IsNotGroundedByBodyOccurrence()
    {
        // Body uses «լինել» (to be) as a copula. Choice reuses its
        // «լին» stem as a Dative-coerced noun. The blocked-stem rule
        // must keep «լին» out of bodyStems so this never grounds.
        var body = "Թիթեռը նայեց ծաղիկին։ Կարող է այնտեղ ինչ-որ բան լինել։";
        var result = _gate.Evaluate(
            body,
            "Մոտենանք ծաղիկին",
            "Նայենք լինին");
        Assert.False(result.IsCoherent);
    }

    // ─────────────────────────────────────────────────────────────────
    // Live QA regression — 2026-04-28 rabbit-with-stone / «հետ» false-ground
    // ─────────────────────────────────────────────────────────────────

    private const string LiveRabbitStoneBody =
          "Փոքրիկ նապաստակը ցատկեց քարի վրայից։ "
        + "Փափուկ խոտի վրա քամիի մեղմ ծեսը լսվում էր։ "
        + "Նապաստակը իր բույսերի ընկերների հետ խաղում էր, երբ տեսավ, "
        + "որ հողի վրա փայլում է մի փոքրիկ թանկարժեք քար։ "
        + "Նա զարմացած մոտեցավ, որպեսզի ավելի լավ տեսնի։ "
        + "Բայց այդ ժամանակ քարից դուրս թռավ մի արտասովոր փոքրիկ թիթեռ, "
        + "որ փայլում էր գունագեղ լույսերով։";

    [Fact]
    public void LiveQA_HetBadPair_IsRejected_AndRepairAvoidsHetw()
    {
        // 2026-04-28 voice-MVP follow-up regression. Body contains the
        // postposition «հետ» ("with", from «ընկերների հետ»). The gate's
        // prefix-match used to ground choiceA's «հետև» («-ին» dative of
        // "back/behind") against body's «հետ» — but those are
        // semantically unrelated lexemes. Two complementary fixes:
        // adding «հետ» to StopWords, and tightening prefix-match to
        // require body stem length ≥ 4. Either alone closes this case;
        // both make the gate robust to similar function-word collisions.
        var result = _gate.Evaluate(
            LiveRabbitStoneBody,
            "Մոտենանք հետևին",
            "Նայենք թիթեռին");

        Assert.False(result.IsCoherent);
        Assert.True(result.ShouldRetry);
        Assert.NotNull(result.RepairedChoiceA);
        Assert.NotNull(result.RepairedChoiceB);

        // Repair must NOT carry the rejected «հետև» / «հետևին».
        Assert.DoesNotContain("հետև", result.RepairedChoiceA!, StringComparison.Ordinal);
        Assert.DoesNotContain("հետև", result.RepairedChoiceB!, StringComparison.Ordinal);

        // Repair must NOT regress to any of the prior bad-anchor classes.
        foreach (var bad in new[]
        {
            "հպենք", "շատրվանաքար", "մոտեցավ", "տեսավ", "զարմացավ",
            "կարող", "լին",
        })
        {
            Assert.DoesNotContain(bad, result.RepairedChoiceA!, StringComparison.Ordinal);
            Assert.DoesNotContain(bad, result.RepairedChoiceB!, StringComparison.Ordinal);
        }

        // The two repaired labels must differ.
        Assert.NotEqual(result.RepairedChoiceA, result.RepairedChoiceB);

        // At least one repaired label should reference a concrete body
        // noun («նապաստակ», «քար», «թիթեռ», «խոտ»).
        var refsBody =
               result.RepairedChoiceA!.Contains("նապաստակ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("քար", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("թիթեռ", StringComparison.Ordinal)
            || result.RepairedChoiceA!.Contains("խոտ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("նապաստակ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("քար", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("թիթեռ", StringComparison.Ordinal)
            || result.RepairedChoiceB!.Contains("խոտ", StringComparison.Ordinal);
        Assert.True(refsBody,
            $"Repair must reference a body noun. Got A=\"{result.RepairedChoiceA}\" B=\"{result.RepairedChoiceB}\"");
    }

    [Fact]
    public void Choice_LongerStem_DoesNotPrefixMatch_ShortBodyFunctionWord()
    {
        // Smaller, isolated regression for the prefix-match tightening.
        // Body has «հետ» as a postposition; the choice token «հետև»
        // («back/behind») coincidentally starts with «հետ» but is a
        // different lexeme. The gate must NOT count this as overlap.
        var body = "Նապաստակը խաղում էր ընկերների հետ։ Տեսավ մի թիթեռ։";
        var result = _gate.Evaluate(
            body,
            "Մոտենանք թիթեռին",
            "Նայենք հետևին");
        Assert.False(result.IsCoherent);
    }

    [Fact]
    public void Choice_ShortBodyContentNounPrefixMatch_StillWorks_ViaExactMatch()
    {
        // Sanity / no-regression: a short concrete content noun
        // («քար», 3 chars) in the body still grounds a choice that
        // contains the same stem — via exact-match (Contains), not
        // prefix-match. The tightened prefix-match rule should not
        // break legitimate 3-char body-noun groundings.
        var body = "Թիթեռը մոտեցավ քարին և զարմացավ։";
        var result = _gate.Evaluate(
            body,
            "Մոտենանք քարին",
            "Նայենք քարի վրա");
        // ChoiceDiversity may fire here (both about the stone) — the
        // test only pins that grounding works, not that the pair
        // passes overall. Specifically: the gate should NOT report
        // "both_choices_ungrounded".
        Assert.NotEqual("both_choices_ungrounded", result.Reason);
    }
}

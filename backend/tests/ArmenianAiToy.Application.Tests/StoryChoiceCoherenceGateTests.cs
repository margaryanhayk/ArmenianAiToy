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
}

using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase E E2: structural choice-diversity guard.
/// Narrow unit coverage — verifies the helper catches the two targeted
/// failure modes and stays quiet on genuinely different choices.
/// </summary>
public class ChoiceDiversityTests
{
    // ─────────────────────────────────────────────────────────────────────
    // Positive: should flag as too similar
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameFirstVerb_SwappedNoun_IsTooSimilar()
    {
        Assert.True(ChoiceDiversity.AreTooSimilar(
            "Բացենք տուփը", "Բացենք դուռը"));
    }

    [Fact]
    public void SameFirstVerb_DifferentObjects_IsTooSimilar()
    {
        Assert.True(ChoiceDiversity.AreTooSimilar(
            "Գնանք անտառ միասին", "Գնանք լիճ միասին"));
    }

    [Fact]
    public void InflectionalVariant_SharedPrefix_IsTooSimilar()
    {
        // «Բացենք» (6) vs «Բացեմ» (5): shared prefix «Բացե» (4), length diff 1.
        Assert.True(ChoiceDiversity.AreTooSimilar(
            "Բացենք փոքրիկ տուփը", "Բացեմ մեծ դուռը"));
    }

    [Fact]
    public void TrailingPunctuation_IsStripped_BeforeCompare()
    {
        // Armenian verjaket ։ and comma ՝ should not block the match.
        Assert.True(ChoiceDiversity.AreTooSimilar(
            "Բացենք տուփը։", "Բացենք դուռը՝"));
    }

    [Fact]
    public void CaseDifference_DoesNotHideSimilarity()
    {
        Assert.True(ChoiceDiversity.AreTooSimilar(
            "բացենք տուփը", "Բացենք դուռը"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Negative: should NOT flag
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DifferentVerbs_AreNotTooSimilar()
    {
        Assert.False(ChoiceDiversity.AreTooSimilar(
            "Բացենք փոքրիկ տուփը", "Կանչենք թռչունիկին"));
    }

    [Fact]
    public void DifferentSenseVerbs_AreNotTooSimilar()
    {
        Assert.False(ChoiceDiversity.AreTooSimilar(
            "Լսենք զանգակի ձայնը", "Նայենք պատուհանից դուրս"));
    }

    [Fact]
    public void SameNoun_DifferentVerbs_AreNotTooSimilar()
    {
        // Open-the-door / close-the-door — genuinely different outcomes.
        Assert.False(ChoiceDiversity.AreTooSimilar(
            "Բացենք դուռը", "Փակենք դուռը"));
    }

    [Fact]
    public void ShortCommonStem_DoesNotFalseFlag()
    {
        // «Գնա» prefix is ≤4 chars, so the inflectional rule must not fire.
        // «Գնանք» (5) vs «Գնան» (4) — the 4-char floor blocks this.
        Assert.False(ChoiceDiversity.AreTooSimilar(
            "Գնանք անտառ", "Գնան լիճ մոտ"));
    }

    [Fact]
    public void LargeLengthDifference_DoesNotFalseFlag()
    {
        // «Վերադառնանք» (11) vs «Վերցնենք» (8). Shared prefix «Վեր» = 3,
        // below the 4-char threshold, so the rule must not fire.
        Assert.False(ChoiceDiversity.AreTooSimilar(
            "Վերադառնանք տուն", "Վերցնենք քարը"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Defensive: degenerate inputs
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NullInput_ReturnsFalse()
    {
        Assert.False(ChoiceDiversity.AreTooSimilar(null, "Բացենք դուռը"));
        Assert.False(ChoiceDiversity.AreTooSimilar("Բացենք տուփը", null));
        Assert.False(ChoiceDiversity.AreTooSimilar(null, null));
    }

    [Fact]
    public void EmptyOrWhitespace_ReturnsFalse()
    {
        Assert.False(ChoiceDiversity.AreTooSimilar("", "Բացենք դուռը"));
        Assert.False(ChoiceDiversity.AreTooSimilar("   ", "Բացենք դուռը"));
    }

    [Fact]
    public void SingleTokenLabels_ReturnFalse()
    {
        // Below the 2-token minimum — out of spec, stay conservative.
        Assert.False(ChoiceDiversity.AreTooSimilar("Բացենք", "Բացենք"));
        Assert.False(ChoiceDiversity.AreTooSimilar("Գնանք", "Կանչենք"));
    }
}

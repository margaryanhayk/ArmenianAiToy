using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for ArmenianSimplifier targeted phrase replacements.
/// One assertion per benchmark-derived fix plus an idempotence check.
/// </summary>
public class ArmenianSimplifierTests
{
    [Fact]
    public void NullOrEmpty_ReturnsInputUnchanged()
    {
        Assert.Equal(string.Empty, ArmenianSimplifier.Simplify(null));
        Assert.Equal(string.Empty, ArmenianSimplifier.Simplify(""));
    }

    [Fact]
    public void EvAysqane_WithEnglishColon_IsRemoved()
    {
        var input = "Հեքիաթը շարունակվեց։ Եվ այսքանը: Մուշը ժպտաց։";
        Assert.Equal("Հեքիաթը շարունակվեց։ Մուշը ժպտաց։", ArmenianSimplifier.Simplify(input));
    }

    [Fact]
    public void EvAysqane_WithArmenianFullStop_IsRemoved()
    {
        var input = "Հեքիաթը շարունակվեց։ Եվ այսքանը։ Մուշը ժպտաց։";
        Assert.Equal("Հեքիաթը շարունակվեց։ Մուշը ժպտաց։", ArmenianSimplifier.Simplify(input));
    }

    [Fact]
    public void Metsakan_BecomesMets()
    {
        Assert.Equal(
            "Նապաստակը շարժեց իր մեծ ականջները։",
            ArmenianSimplifier.Simplify("Նապաստակը շարժեց իր մեծական ականջները։"));
    }

    [Fact]
    public void Anverapahoren_IsRemoved()
    {
        Assert.Equal(
            "Բաբոն համաձայնվեց։",
            ArmenianSimplifier.Simplify("Բաբոն համաձայնվեց անվերապահորեն։"));
    }

    [Fact]
    public void TsrvelUBavararvel_IsTrimmedToTsrvel()
    {
        Assert.Equal(
            "Ծառերը սկսեցին ծռվել։",
            ArmenianSimplifier.Simplify("Ծառերը սկսեցին ծռվել ու բավարարվել։"));
    }

    [Fact]
    public void Trchyun_BaseStem_IsCorrected()
    {
        Assert.Equal(
            "Փոքրիկ թռչունը երգում էր։",
            ArmenianSimplifier.Simplify("Փոքրիկ թռչյունը երգում էր։"));
    }

    [Fact]
    public void Trchyun_InflectedForms_AreCorrected()
    {
        // Plural definite, plural dative — both share the wrong stem.
        Assert.Equal(
            "Թռչունները թռան, իսկ Մուշը նայեց թռչուններին։",
            ArmenianSimplifier.Simplify("Թռչյունները թռան, իսկ Մուշը նայեց թռչյուններին։"));
    }

    [Fact]
    public void QaghtsratsinZhptats_BecomesQaghtsrZhptats()
    {
        Assert.Equal(
            "Խեցգետինը քաղցր ժպտաց։",
            ArmenianSimplifier.Simplify("Խեցգետինը քաղցրածին ժպտաց։"));
    }

    [Fact]
    public void Drakhtyan_BecomesGeghetsik()
    {
        Assert.Equal(
            "Միշոն վազում էր գեղեցիկ այգում։",
            ArmenianSimplifier.Simplify("Միշոն վազում էր դրախտյան այգում։"));
    }

    [Fact]
    public void QayletsnelovUsutsanel_BecomesPhortsetsSovorestnel()
    {
        Assert.Equal(
            "Մուրզիկը փորձեց սովորեցնել Պիպուսին։",
            ArmenianSimplifier.Simplify("Մուրզիկը քայլեցնելով փորձեց ուսուցանել Պիպուսին։"));
    }

    [Fact]
    public void Loghapnya_AdjectiveDropped()
    {
        Assert.Equal(
            "Պիպուսը նստած էր ճյուղերի վրա։",
            ArmenianSimplifier.Simplify("Պիպուսը նստած էր լողափնյա ճյուղերի վրա։"));
    }

    [Fact]
    public void KayinSpasvats_BecomesNaturalSyntax()
    {
        Assert.Equal(
            "Բակում շատ արկածներ սպասում էին նրանց։",
            ArmenianSimplifier.Simplify("Բակում շատ արկածներ կային սպասված նրանց։"));
    }

    [Fact]
    public void IdempotentOnAlreadyCleanText()
    {
        var clean = "Փոքրիկ նապաստակը պարտեզում խաղում էր։ Թռչունները երգում էին։";
        Assert.Equal(clean, ArmenianSimplifier.Simplify(clean));
    }
}

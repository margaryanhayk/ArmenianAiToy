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


    // ─── Phase A word-level simplifications ───

    [Fact]
    public void Mtnolort_BecomesOd()
    {
        Assert.Equal(
            "Երբ արևի լույսը անցնում է օդի միջով։",
            ArmenianSimplifier.Simplify("Երբ արևի լույսը անցնում է մթնոլորտի միջով։"));
    }

    [Fact]
    public void Kutakvum_BecomesHavaqvum()
    {
        Assert.Equal(
            "Աղերը հավաքվում են ծովում։",
            ArmenianSimplifier.Simplify("Աղերը կուտակվում են ծովում։"));
    }

    [Fact]
    public void Champordel_BecomesGnal()
    {
        Assert.Equal(
            "Թռչունները սիրում են գնալ։",
            ArmenianSimplifier.Simplify("Թռչունները սիրում են ճամփորդել։"));
    }

    [Fact]
    public void Hravirets_BecomesKanchets()
    {
        Assert.Equal(
            "Նա կանչեց իր ընկերներին։",
            ArmenianSimplifier.Simplify("Նա հրավիրեց իր ընկերներին։"));
    }

    [Fact]
    public void Tsitsarkum_BecomesTsuytsTalis()
    {
        Assert.Equal(
            "Նա ցույց տալիս էր աստղերը։",
            ArmenianSimplifier.Simplify("Նա ցուցարկում էր աստղերը։"));
    }

    [Fact]
    public void AysNpatakiHamar_BecomesDraHamar()
    {
        Assert.Equal(
            "Թևերը դրա համար թռչում են։",
            ArmenianSimplifier.Simplify("Թևերը այս նպատակի համար թռչում են։"));
    }

}

using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for <see cref="ArmenianVoiceReplyGuard"/>. Covers the four
/// behaviours pinned by the voice-quality slice: garbled-input detection,
/// sentence cap, typo fix, and shape preservation on edge cases.
/// </summary>
public class ArmenianVoiceReplyGuardTests
{
    // -----------------------------------------------------------------
    // IsGarbledInput
    // -----------------------------------------------------------------

    [Fact]
    public void IsGarbledInput_RandomConsonantsAcrossAllTokens_ReturnsTrue()
    {
        Assert.True(ArmenianVoiceReplyGuard.IsGarbledInput("քրռռռռ բխխխ"));
    }

    [Fact]
    public void IsGarbledInput_RealArmenianGreeting_ReturnsFalse()
    {
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("Բարև Արեգ, ինչպե՞ս ես։"));
    }

    [Fact]
    public void IsGarbledInput_RealQuestion_ReturnsFalse()
    {
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("Ինչու՞ է երկինքը կապույտ։"));
    }

    [Fact]
    public void IsGarbledInput_RealAnimalRequest_ReturnsFalse()
    {
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("Ասա մի ուրախ բան կենդանիների մասին։"));
    }

    [Fact]
    public void IsGarbledInput_Empty_ReturnsFalse()
    {
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput(""));
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput(null));
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("   "));
    }

    [Fact]
    public void IsGarbledInput_AllShortTokens_ReturnsFalse()
    {
        // "Հա ոչ" — two short Armenian tokens, neither evaluable.
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("Հա ոչ"));
    }

    [Fact]
    public void IsGarbledInput_PureLatinInput_PassesThrough()
    {
        // The voice path runs Whisper with Language=hy so a legitimate child
        // utterance always comes back in Armenian script — pure-Latin
        // transcripts cannot occur there. The text-API path uses English
        // userMessages like "tell me a story" / "good night" as mode triggers
        // (see ModeAwareFallbackTests, ChatServiceTailBlockTests etc.); those
        // MUST NOT short-circuit into the clarification line.
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("tell me a story"));
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("good night"));
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("asdfg qwerty"));
    }

    [Fact]
    public void IsGarbledInput_MixedWithRepeatedRun_ReturnsTrueOnAllGarbledTokens()
    {
        // 3+ consecutive identical letters is the second garble signal.
        Assert.True(ArmenianVoiceReplyGuard.IsGarbledInput("քրռռռռ բխխխ թպպպ"));
    }

    [Fact]
    public void IsGarbledInput_MixedRealAndGarbledTokens_ReturnsFalse()
    {
        // The all-evaluable-tokens rule: ONE real Armenian word among the
        // noise lets the turn through to the normal pipeline. Flagging
        // partially-garbled input would swallow legitimate questions that
        // arrive with one mis-transcribed word.
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("Բարև քրռռռռ"));
        Assert.False(ArmenianVoiceReplyGuard.IsGarbledInput("քրռռռռ հեքիաթ բխխխ"));
    }

    // -----------------------------------------------------------------
    // EnforceMaxSentences
    // -----------------------------------------------------------------

    [Fact]
    public void EnforceMaxSentences_FiveSentences_TrimmedToThree()
    {
        var five = "Մեկ։ Երկու։ Երեք։ Չորս։ Հինգ։";
        Assert.Equal("Մեկ։ Երկու։ Երեք։", ArmenianVoiceReplyGuard.EnforceMaxSentences(five, 3));
    }

    [Fact]
    public void EnforceMaxSentences_ThreeSentences_Unchanged()
    {
        var three = "Մեկ։ Երկու։ Երեք։";
        Assert.Equal(three, ArmenianVoiceReplyGuard.EnforceMaxSentences(three, 3));
    }

    [Fact]
    public void EnforceMaxSentences_TwoSentences_Unchanged()
    {
        var two = "Բարև։ Ինչպես ես։";
        Assert.Equal(two, ArmenianVoiceReplyGuard.EnforceMaxSentences(two, 3));
    }

    [Fact]
    public void EnforceMaxSentences_ZeroTerminators_Unchanged()
    {
        var noTerminator = "Բարև Արեգ";
        Assert.Equal(noTerminator, ArmenianVoiceReplyGuard.EnforceMaxSentences(noTerminator, 3));
    }

    [Fact]
    public void EnforceMaxSentences_EmbeddedQuestionDiacriticDoesNotSplit()
    {
        // ՞ is a diacritic above the stressed vowel inside a question word
        // — must NOT count as a sentence terminator. The four ։ marks define
        // four sentences; cap=3 keeps the first three.
        var reply =
            "Բարև՛ ջան։ Ես շատ լավ եմ։ Իսկ դու՞ ինչպես ես։ Կուզե՞ս միասին նայել։";
        var expected = "Բարև՛ ջան։ Ես շատ լավ եմ։ Իսկ դու՞ ինչպես ես։";
        Assert.Equal(expected, ArmenianVoiceReplyGuard.EnforceMaxSentences(reply, 3));
    }

    [Fact]
    public void EnforceMaxSentences_TrailingWhitespaceTrimmed()
    {
        var reply = "Մեկ։ Երկու։ Երեք։    Չորս։";
        Assert.Equal("Մեկ։ Երկու։ Երեք։", ArmenianVoiceReplyGuard.EnforceMaxSentences(reply, 3));
    }

    [Fact]
    public void EnforceMaxSentences_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ArmenianVoiceReplyGuard.EnforceMaxSentences(null, 3));
        Assert.Equal(string.Empty, ArmenianVoiceReplyGuard.EnforceMaxSentences("", 3));
    }

    // -----------------------------------------------------------------
    // ApplyTypoFixes
    // -----------------------------------------------------------------

    [Fact]
    public void ApplyTypoFixes_KhaghakumIsCorrectedToKhaghum()
    {
        // խաղաքում → խաղում (malformed verb produced by the model in bench traffic).
        var input = "Փղիկները խաղաքում են միմյանց հետ։";
        var expected = "Փղիկները խաղում են միմյանց հետ։";
        Assert.Equal(expected, ArmenianVoiceReplyGuard.ApplyTypoFixes(input));
    }

    [Fact]
    public void ApplyTypoFixes_NoMatch_ReturnsInputUnchanged()
    {
        var input = "Բարև։ Շատ լավ եմ։";
        Assert.Equal(input, ArmenianVoiceReplyGuard.ApplyTypoFixes(input));
    }

    [Fact]
    public void ApplyTypoFixes_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ArmenianVoiceReplyGuard.ApplyTypoFixes(null));
        Assert.Equal(string.Empty, ArmenianVoiceReplyGuard.ApplyTypoFixes(""));
    }

    // -----------------------------------------------------------------
    // ClarificationResponse — pinned-text contract
    // -----------------------------------------------------------------

    [Fact]
    public void ClarificationResponse_MatchesExactSpecText()
    {
        Assert.Equal(
            "Կներե՛ս, լավ չլսեցի։ Կրկնի՞ր, խնդրում եմ։",
            ArmenianVoiceReplyGuard.ClarificationResponse);
    }
}

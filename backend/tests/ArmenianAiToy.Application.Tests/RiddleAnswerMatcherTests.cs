using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class RiddleAnswerMatcherTests
{
    [Theory]
    [InlineData("\u056f\u0561\u057f\u0578\u0582", "\u056f\u0561\u057f\u0578\u0582")]                          // exact: կատու
    [InlineData("\u056f\u0561\u057f\u0578\u0582\u0576", "\u056f\u0561\u057f\u0578\u0582")]                    // definite article: կատուն vs կատու
    [InlineData("\u056f\u0561\u057f\u0578\u0582\u0568", "\u056f\u0561\u057f\u0578\u0582")]                    // -ը suffix
    [InlineData("\u056d\u0576\u0571\u0578\u0580\u056b", "\u056d\u0576\u0571\u0578\u0580")]                    // -ի suffix (genitive of խնձոր)
    [InlineData("\u056d\u0576\u0571\u0578\u0580", "\u056d\u0576\u0571\u0578\u0580")]                          // exact խնձոր
    [InlineData("\u056d\u0576\u0571\u0578\u0580\u0568", "\u056d\u0576\u0571\u0578\u0580")]                    // խնձորը
    public void Match_NormalizesArmenianSuffixes(string guess, string answer)
    {
        Assert.Equal(RiddleAnswerMatcher.Result.Match, RiddleAnswerMatcher.Match(guess, answer));
    }

    [Theory]
    [InlineData("\u0571\u0578\u0582\u056f", "\u056f\u0561\u057f\u0578\u0582")]                                // ձուկ vs կատու
    [InlineData("apple", "\u056d\u0576\u0571\u0578\u0580")]                                                    // wrong language
    [InlineData("", "\u056f\u0561\u057f\u0578\u0582")]
    [InlineData("   ", "\u056f\u0561\u057f\u0578\u0582")]
    public void Match_RejectsClearlyWrongOrEmptyGuesses(string guess, string answer)
    {
        Assert.Equal(RiddleAnswerMatcher.Result.Wrong, RiddleAnswerMatcher.Match(guess, answer));
    }

    [Fact]
    public void Match_AcceptsTokenInPhrase()
    {
        // Child says "it's a կատու" — match should pick out the token.
        Assert.Equal(
            RiddleAnswerMatcher.Result.Match,
            RiddleAnswerMatcher.Match("\u0564\u0561 \u056f\u0561\u057f\u0578\u0582 \u0567", "\u056f\u0561\u057f\u0578\u0582"));
    }

    [Fact]
    public void Match_AcceptsOneCharTypoOnLongerWord()
    {
        // 5-letter word with one substitution should still match.
        Assert.Equal(
            RiddleAnswerMatcher.Result.Match,
            RiddleAnswerMatcher.Match("\u056d\u0576\u0571\u0578\u0581", "\u056d\u0576\u0571\u0578\u0580"));
    }

    [Fact]
    public void Normalize_StripsTrailingPunctuationAndQuotes()
    {
        Assert.Equal("\u056f\u0561\u057f\u0578\u0582", RiddleAnswerMatcher.Normalize("\u00ab\u056f\u0561\u057f\u0578\u0582\u0568\u00bb\u0589"));
    }

    [Fact]
    public void Normalize_KeepsShortWordsIntact()
    {
        // "ձու" (3 chars) — must NOT strip suffix off something this short.
        Assert.Equal("\u0571\u0578\u0582", RiddleAnswerMatcher.Normalize("\u0571\u0578\u0582"));
    }

    [Fact]
    public void Match_BothNullOrEmpty_ReturnsWrong()
    {
        Assert.Equal(RiddleAnswerMatcher.Result.Wrong, RiddleAnswerMatcher.Match(null, "\u056f\u0561\u057f\u0578\u0582"));
        Assert.Equal(RiddleAnswerMatcher.Result.Wrong, RiddleAnswerMatcher.Match("\u056f\u0561\u057f\u0578\u0582", null));
    }
}

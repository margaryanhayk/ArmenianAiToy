using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the C# choice normalizer, ported from the approved Python spec.
/// All 15 required inputs from the spec are covered, plus edge cases.
/// Armenian inputs use \uXXXX escapes for rendering safety.
/// </summary>
public class ChoiceNormalizerTests
{
    // Default option labels chosen so no content words match test inputs
    // like "help him", "the dog one", "I want the fox".
    private const string OptA = "Cross the river on the log";
    private const string OptB = "Climb the mountain trail";

    // --- Spec inputs 1-4: English positional ---

    [Fact]
    public void Test01_Left()
    {
        var r = ChoiceNormalizer.Normalize("left", OptA, OptB);
        Assert.Equal("option_a", r.Normalized);
        Assert.Equal("high", r.Confidence);
        Assert.Equal("positional_en", r.Method);
        Assert.Equal("left", r.RawInput);
    }

    [Fact]
    public void Test02_Right()
    {
        var r = ChoiceNormalizer.Normalize("right", OptA, OptB);
        Assert.Equal("option_b", r.Normalized);
        Assert.Equal("high", r.Confidence);
    }

    [Fact]
    public void Test03_TheFirstOne()
    {
        var r = ChoiceNormalizer.Normalize("the first one", OptA, OptB);
        Assert.Equal("option_a", r.Normalized);
        Assert.Equal("high", r.Confidence);
        Assert.Equal("positional_en", r.Method);
    }

    [Fact]
    public void Test04_Second()
    {
        var r = ChoiceNormalizer.Normalize("second", OptA, OptB);
        Assert.Equal("option_b", r.Normalized);
        Assert.Equal("high", r.Confidence);
    }

    // --- Spec inputs 11-12: single-letter positional ---

    [Fact]
    public void Test11_A()
    {
        var r = ChoiceNormalizer.Normalize("a", OptA, OptB);
        Assert.Equal("option_a", r.Normalized);
        Assert.Equal("high", r.Confidence);
    }

    [Fact]
    public void Test12_B()
    {
        var r = ChoiceNormalizer.Normalize("b", OptA, OptB);
        Assert.Equal("option_b", r.Normalized);
        Assert.Equal("high", r.Confidence);
    }

    [Fact]
    public void A_InSentence_DoesNotMatch()
    {
        var r = ChoiceNormalizer.Normalize("a dog", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void First_AsSubstring_DoesNotMatch()
    {
        // "thirst" contains "first" as a substring — must NOT trigger.
        var r = ChoiceNormalizer.Normalize("thirst", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void Second_AsSubstring_DoesNotMatch()
    {
        // "seconds" contains "second" as a substring — must NOT trigger.
        var r = ChoiceNormalizer.Normalize("seconds ago", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    // --- Punctuation tolerance ---

    [Theory]
    [InlineData("first!", "option_a")]
    [InlineData("second,", "option_b")]
    [InlineData("left.", "option_a")]
    [InlineData("right?", "option_b")]
    [InlineData("the first.", "option_a")]
    public void TrailingPunctuation_StillMatches(string input, string expected)
    {
        var r = ChoiceNormalizer.Normalize(input, OptA, OptB);
        Assert.Equal(expected, r.Normalized);
        Assert.Equal("high", r.Confidence);
        Assert.Equal(input, r.RawInput);  // raw preserved exactly
    }

    // --- Spec inputs 14-15: Armenian positional ---

    [Fact]
    public void Test14_Mek()
    {
        // \u0574\u0565\u056f\u0568 = Armenian "one"
        var r = ChoiceNormalizer.Normalize("\u0574\u0565\u056f\u0568", OptA, OptB);
        Assert.Equal("option_a", r.Normalized);
        Assert.Equal("high", r.Confidence);
        Assert.Equal("positional_hy", r.Method);
    }

    [Fact]
    public void Test15_Yerkrord()
    {
        // \u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568 = Armenian "second"
        var r = ChoiceNormalizer.Normalize("\u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568", OptA, OptB);
        Assert.Equal("option_b", r.Normalized);
        Assert.Equal("high", r.Confidence);
        Assert.Equal("positional_hy", r.Method);
    }

    [Fact]
    public void Mek_CorrectCodepoints()
    {
        var mek = "\u0574\u0565\u056f\u0568";
        Assert.Equal(
            new[] { "0x574", "0x565", "0x56f", "0x568" },
            mek.Select(c => $"0x{(int)c:x}").ToArray());
    }

    [Fact]
    public void Yerkrord_CorrectCodepoints()
    {
        var y = "\u0565\u0580\u056f\u0580\u0578\u0580\u0564\u0568";
        Assert.Equal(
            new[] { "0x565", "0x580", "0x56f", "0x580", "0x578", "0x580", "0x564", "0x568" },
            y.Select(c => $"0x{(int)c:x}").ToArray());
    }

    // --- Spec inputs 5-7, 9-10: unknown ---

    [Fact]
    public void Test05_ThisOne()
    {
        var r = ChoiceNormalizer.Normalize("this one", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void Test06_ThatOne()
    {
        var r = ChoiceNormalizer.Normalize("that one", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void Test07_HelpHim()
    {
        var r = ChoiceNormalizer.Normalize("help him", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void Test09_Ayo_IsUnknown()
    {
        var r = ChoiceNormalizer.Normalize("ayo", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
        Assert.Equal("no_match", r.Method);
    }

    [Fact]
    public void Test10_Voch_IsUnknown()
    {
        var r = ChoiceNormalizer.Normalize("voch", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
        Assert.Equal("no_match", r.Method);
    }

    [Fact]
    public void Gibberish_IsUnknown()
    {
        var r = ChoiceNormalizer.Normalize("asdfghjkl", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
        Assert.Equal("low", r.Confidence);
        Assert.Equal("no_match", r.Method);
    }

    [Fact]
    public void EmptyInput_IsUnknown()
    {
        var r = ChoiceNormalizer.Normalize("", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void WhitespaceInput_IsUnknown()
    {
        var r = ChoiceNormalizer.Normalize("   ", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    // --- Spec inputs 8, 13: keyword match ---

    [Fact]
    public void Test08_Fox_MatchesOptionA()
    {
        var r = ChoiceNormalizer.Normalize("I want the fox", "Help the fox", "Follow the bird");
        Assert.Equal("option_a", r.Normalized);
        Assert.Equal("low", r.Confidence);
        Assert.Equal("keyword_match", r.Method);
    }

    [Fact]
    public void Test08_Fox_UnknownWhenNotInLabels()
    {
        var r = ChoiceNormalizer.Normalize("I want the fox", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void Test13_Dog_MatchesLabel()
    {
        var r = ChoiceNormalizer.Normalize("the dog one", "Pet the dog", "Feed the cat");
        Assert.Equal("option_a", r.Normalized);
        Assert.Equal("low", r.Confidence);
        Assert.Equal("keyword_match", r.Method);
    }

    [Fact]
    public void Test13_Dog_UnknownWhenNotInLabels()
    {
        var r = ChoiceNormalizer.Normalize("the dog one", OptA, OptB);
        Assert.Equal("unknown", r.Normalized);
    }

    [Fact]
    public void BothLabelsMatch_ReturnsUnknown()
    {
        var r = ChoiceNormalizer.Normalize("the big river", "Cross the river", "Swim the river");
        Assert.Equal("unknown", r.Normalized);
    }

    // --- Raw input preservation ---

    [Fact]
    public void RawPreserved_WithWhitespace()
    {
        var r = ChoiceNormalizer.Normalize("  Left  ", OptA, OptB);
        Assert.Equal("  Left  ", r.RawInput);
        Assert.Equal("option_a", r.Normalized);
    }

    [Fact]
    public void RawPreserved_OnUnknown()
    {
        var r = ChoiceNormalizer.Normalize("blah blah", OptA, OptB);
        Assert.Equal("blah blah", r.RawInput);
    }

    [Fact]
    public void RawPreserved_OnArmenian()
    {
        var mek = "\u0574\u0565\u056f\u0568";
        var r = ChoiceNormalizer.Normalize(mek, OptA, OptB);
        Assert.Equal(mek, r.RawInput);
    }

    [Fact]
    public void RawPreserved_OnKeywordMatch()
    {
        var r = ChoiceNormalizer.Normalize("I want the fox", "Help the fox", "Follow the bird");
        Assert.Equal("I want the fox", r.RawInput);
    }
}

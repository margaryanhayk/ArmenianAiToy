using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class TailBlockParserTests
{
    [Fact]
    public void BasicExtraction_ReturnsLabelsAndCleanText()
    {
        var input = "Once upon a time...\n---\nCHOICE_A:Help the fox\nCHOICE_B:Cross the river";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.True(result);
        Assert.Equal("Once upon a time...", cleaned);
        Assert.Equal("Help the fox", a);
        Assert.Equal("Cross the river", b);
    }

    [Fact]
    public void ArmenianLabels_ExtractedIntact()
    {
        var labelA = "\u0555\u0563\u0576\u056b\u0580 \u0561\u0572\u057e\u0565\u057d\u056b\u0576";
        var labelB = "\u0540\u0565\u057f\u0587\u056b\u0580 \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u0576";
        var input = $"Narrative text here.\n---\nCHOICE_A:{labelA}\nCHOICE_B:{labelB}";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.True(result);
        Assert.Equal("Narrative text here.", cleaned);
        Assert.Equal(labelA, a);
        Assert.Equal(labelB, b);
    }

    [Fact]
    public void LabelsWithColonsAndDashes_ExtractedFully()
    {
        var input = "Story.\n---\nCHOICE_A:Go left - the dark path\nCHOICE_B:Go right: the bright one";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.True(result);
        Assert.Equal("Story.", cleaned);
        Assert.Equal("Go left - the dark path", a);
        Assert.Equal("Go right: the bright one", b);
    }

    [Fact]
    public void NoTailBlock_ReturnsFalseAndUnchangedText()
    {
        var input = "Just a normal conversational response.";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.False(result);
        Assert.Equal(input, cleaned);
        Assert.Null(a);
        Assert.Null(b);
    }

    [Fact]
    public void MalformedOnlyOneMarker_ReturnsFalse()
    {
        var input = "Story.\n---\nCHOICE_A:Help the fox";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.False(result);
        Assert.Equal(input, cleaned);
        Assert.Null(a);
        Assert.Null(b);
    }

    [Fact]
    public void MalformedEmptyLabel_ReturnsFalse()
    {
        var input = "Story.\n---\nCHOICE_A:\nCHOICE_B:Cross the river";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.False(result);
        Assert.Equal(input, cleaned);
        Assert.Null(a);
        Assert.Null(b);
    }

    [Fact]
    public void StrayDashesInNarrative_OnlyLastBlockMatches()
    {
        var input = "The road split.\n---\nShe thought carefully.\n---\nCHOICE_A:Left\nCHOICE_B:Right";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.True(result);
        Assert.Equal("The road split.\n---\nShe thought carefully.", cleaned);
        Assert.Equal("Left", a);
        Assert.Equal("Right", b);
    }

    [Fact]
    public void TrailingWhitespace_ToleratedAndTrimmed()
    {
        var input = "Story.\n---\nCHOICE_A:Left\nCHOICE_B:Right\n  \n";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.True(result);
        Assert.Equal("Story.", cleaned);
        Assert.Equal("Left", a);
        Assert.Equal("Right", b);
    }

    [Fact]
    public void EmptyString_ReturnsFalse()
    {
        var result = TailBlockParser.TryExtract("", out var cleaned, out var a, out var b);

        Assert.False(result);
        Assert.Equal("", cleaned);
        Assert.Null(a);
        Assert.Null(b);
    }

    [Fact]
    public void WindowsLineEndings_StillExtracted()
    {
        var input = "Story.\r\n---\r\nCHOICE_A:Left\r\nCHOICE_B:Right";

        var result = TailBlockParser.TryExtract(input, out var cleaned, out var a, out var b);

        Assert.True(result);
        Assert.Equal("Story.", cleaned);
        Assert.Equal("Left", a);
        Assert.Equal("Right", b);
    }
}

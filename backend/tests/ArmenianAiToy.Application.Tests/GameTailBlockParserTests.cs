using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class GameTailBlockParserTests
{
    [Fact]
    public void TryExtract_WellFormedBlock_ReturnsTrue()
    {
        var input = "\u053e\u0561\u0583 \u057f\u0561\u0576\u0584 \u0574\u056b\u0561\u057d\u056b\u0576\u0589\n---\nGAME_TYPE:clap_along\nGAME_DIFFICULTY:1";

        var ok = GameTailBlockParser.TryExtract(input, out var cleaned, out var t, out var d);

        Assert.True(ok);
        Assert.Equal("clap_along", t);
        Assert.Equal(1, d);
        Assert.DoesNotContain("GAME_TYPE", cleaned);
        Assert.DoesNotContain("---", cleaned);
    }

    [Fact]
    public void TryExtract_MissingBlock_ReturnsFalse()
    {
        var input = "\u053b\u0576\u0579 \u0567 \u0564\u0561\u0589";
        var ok = GameTailBlockParser.TryExtract(input, out var cleaned, out var t, out var d);
        Assert.False(ok);
        Assert.Equal(input, cleaned);
        Assert.Null(t);
        Assert.Equal(0, d);
    }

    [Fact]
    public void TryExtract_NonNumericDifficulty_ReturnsFalse()
    {
        var input = "X\n---\nGAME_TYPE:color_find\nGAME_DIFFICULTY:hard";
        var ok = GameTailBlockParser.TryExtract(input, out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryExtract_ClampsDifficulty()
    {
        var input1 = "X\n---\nGAME_TYPE:clap_along\nGAME_DIFFICULTY:0";
        var input5 = "X\n---\nGAME_TYPE:clap_along\nGAME_DIFFICULTY:5";

        Assert.True(GameTailBlockParser.TryExtract(input1, out _, out _, out var d1));
        Assert.True(GameTailBlockParser.TryExtract(input5, out _, out _, out var d5));
        Assert.Equal(1, d1);
        Assert.Equal(3, d5);
    }

    [Fact]
    public void TryExtract_LowercasesGameType()
    {
        // Model may emit upper-case — parser should normalize for matcher reuse.
        var input = "X\n---\nGAME_TYPE:Animal_Sound\nGAME_DIFFICULTY:2";
        Assert.True(GameTailBlockParser.TryExtract(input, out _, out var t, out _));
        Assert.Equal("animal_sound", t);
    }

    [Fact]
    public void TryExtract_LeavesPriorTextIntact()
    {
        var prose = "\u053e\u0561\u0583 \u057f\u0561\u0576\u0584 \u0574\u056b\u0561\u057d\u056b\u0576\u0589";
        var input = prose + "\n---\nGAME_TYPE:clap_along\nGAME_DIFFICULTY:1";
        Assert.True(GameTailBlockParser.TryExtract(input, out var cleaned, out _, out _));
        Assert.Equal(prose, cleaned);
    }
}

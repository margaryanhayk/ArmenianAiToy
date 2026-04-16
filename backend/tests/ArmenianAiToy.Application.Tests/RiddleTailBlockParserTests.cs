using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class RiddleTailBlockParserTests
{
    [Fact]
    public void TryExtract_WellFormedBlock_ReturnsTrue()
    {
        var input = "\u053b\u0576\u0579-\u0578\u0580 \u057a\u0578\u0582\u056d \u0567 \u0533\u0578\u0582\u0577\u0565\u0580\u0568\u0589 \u053b\u055e\u0576\u0579 \u0567\u0589\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:1";

        var ok = RiddleTailBlockParser.TryExtract(input, out var cleaned, out var ans, out var cat, out var diff);

        Assert.True(ok);
        Assert.Equal("\u056d\u0576\u0571\u0578\u0580", ans);
        Assert.Equal("fruit", cat);
        Assert.Equal(1, diff);
        Assert.DoesNotContain("RIDDLE_ANSWER", cleaned);
        Assert.DoesNotContain("---", cleaned);
    }

    [Fact]
    public void TryExtract_MissingBlock_ReturnsFalse()
    {
        var input = "\u053b\u0576\u0579 \u0567\u0589";
        var ok = RiddleTailBlockParser.TryExtract(input, out var cleaned, out var ans, out var cat, out var diff);
        Assert.False(ok);
        Assert.Equal(input, cleaned);
        Assert.Null(ans);
        Assert.Null(cat);
        Assert.Equal(0, diff);
    }

    [Fact]
    public void TryExtract_NonNumericDifficulty_ReturnsFalse()
    {
        var input = "Q\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:hard";
        var ok = RiddleTailBlockParser.TryExtract(input, out _, out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryExtract_ClampsDifficultyToOneToThree()
    {
        var input1 = "Q\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:0";
        var input5 = "Q\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:5";

        Assert.True(RiddleTailBlockParser.TryExtract(input1, out _, out _, out _, out var d1));
        Assert.True(RiddleTailBlockParser.TryExtract(input5, out _, out _, out _, out var d5));
        Assert.Equal(1, d1);
        Assert.Equal(3, d5);
    }

    [Fact]
    public void TryExtract_LeavesPriorTextIntact()
    {
        var prose = "\u053b\u0576\u0579-\u0578\u0580 \u057a\u0578\u0582\u056d \u0567\u0589";
        var input = prose + "\n---\nRIDDLE_ANSWER:\u056d\u0576\u0571\u0578\u0580\nRIDDLE_CATEGORY:fruit\nRIDDLE_DIFFICULTY:2";
        Assert.True(RiddleTailBlockParser.TryExtract(input, out var cleaned, out _, out _, out _));
        Assert.Equal(prose, cleaned);
    }
}

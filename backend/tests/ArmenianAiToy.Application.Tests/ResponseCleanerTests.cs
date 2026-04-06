using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class ResponseCleanerTests
{
    [Fact]
    public void CleanText_NoFormatting_ReturnsUnchanged()
    {
        var input = "\u0544\u056b \u0561\u0576\u0563\u0561\u0574 \u0561\u0580\u057b\u0578\u0582\u056f\u0568 \u0563\u0576\u0561\u0581 \u0561\u0576\u057f\u0561\u057c\u0578\u057e\u0589";
        Assert.Equal(input, ResponseCleaner.Clean(input));
    }

    [Fact]
    public void CleanText_LeakedChoiceLines_Removed()
    {
        var input = "Story text here.\nCHOICE_A:Go left\nCHOICE_B:Go right";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text here.", result);
    }

    [Fact]
    public void CleanText_LeakedSeparatorAndChoices_Removed()
    {
        var input = "Story text here.\n\n---\nCHOICE_A:Go left\nCHOICE_B:Go right";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text here.", result);
    }

    [Fact]
    public void CleanText_LeakedStoryMemoryBlock_Removed()
    {
        var input = "Story text.\nSTORY_MEMORY:\ncharacter:bunny\nplace:forest\nmood:happy";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text.", result);
    }

    [Fact]
    public void CleanText_SingleLineStoryMemory_Removed()
    {
        var input = "Story text.\nSTORY_MEMORY:Some memory text here.";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text.", result);
    }

    [Fact]
    public void CleanText_ChoicesAndMemoryTogether_AllRemoved()
    {
        var input = "Story text.\n\n---\nCHOICE_A:Option A\nCHOICE_B:Option B\nSTORY_MEMORY:\ncharacter:cat\nplace:garden";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text.", result);
    }

    [Fact]
    public void CleanText_LongSeparator_Removed()
    {
        var input = "Story text.\n-----\nCHOICE_A:A\nCHOICE_B:B";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text.", result);
    }

    [Fact]
    public void CleanText_FormatReminder_Removed()
    {
        var input = "Story text.\nFORMAT REMINDER: End your response with choices.";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Story text.", result);
    }

    [Fact]
    public void CleanText_NormalDashes_NotRemoved()
    {
        // A dash within a sentence is NOT a separator line
        var input = "The bear said - let's go!";
        Assert.Equal(input, ResponseCleaner.Clean(input));
    }

    [Fact]
    public void CleanText_EmptyOrWhitespace_ReturnedAsIs()
    {
        Assert.Equal("", ResponseCleaner.Clean(""));
        Assert.Equal("  ", ResponseCleaner.Clean("  "));
    }

    [Fact]
    public void CleanText_CollapsesMultipleBlankLines()
    {
        var input = "Line one.\n\n\n\n\nLine two.";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("Line one.\n\nLine two.", result);
    }

    [Fact]
    public void CleanText_MixedArmenianAndLeakedFormat()
    {
        var input = "\u0544\u056b \u0561\u0576\u0563\u0561\u0574 \u056f\u0561\u057f\u0578\u0582\u0576 \u0563\u0576\u0561\u0581\u0589\n\n---\nCHOICE_A:\u0533\u0576\u0561\u0576\u0584 \u0561\u057c\u0561\u057b\nCHOICE_B:\u0544\u0576\u0561\u0576\u0584 \u057f\u0565\u0572\u0578\u0582\u0574";
        var result = ResponseCleaner.Clean(input);
        Assert.Equal("\u0544\u056b \u0561\u0576\u0563\u0561\u0574 \u056f\u0561\u057f\u0578\u0582\u0576 \u0563\u0576\u0561\u0581\u0589", result);
    }
}

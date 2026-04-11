using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for StoryMemoryParser.TryExtract and StoryMemoryParser.Merge —
/// parsing STORY_MEMORY blocks from AI responses and merging memory state.
/// </summary>
public class StoryMemoryParserTests
{
    [Fact]
    public void TryExtract_ValidBlock_ReturnsAllFields()
    {
        var input = "Story text here.\nSTORY_MEMORY:\ncharacter:Fox\nplace:Forest\nobject:Golden key\nsituation:Lost in woods\nmood:Curious";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.Equal("Story text here.", cleaned);
        Assert.NotNull(memory);
        Assert.Equal("Fox", memory!.Character);
        Assert.Equal("Forest", memory.Place);
        Assert.Equal("Golden key", memory.ImportantObject);
        Assert.Equal("Lost in woods", memory.CurrentSituation);
        Assert.Equal("Curious", memory.Mood);
    }

    [Fact]
    public void TryExtract_ArmenianText_PreservedIntact()
    {
        var input = "Narrative.\nSTORY_MEMORY:\ncharacter:\u0553\u0578\u0584\u0580\u056b\u056f \u0561\u0572\u057e\u0565\u057d\n" +
                    "place:\u054a\u0561\u0580\u057f\u0565\u0566\nmood:\u0555\u0582\u0580\u0561\u056d";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.Equal("Narrative.", cleaned);
        Assert.NotNull(memory);
        Assert.Equal("\u0553\u0578\u0584\u0580\u056b\u056f \u0561\u0572\u057e\u0565\u057d", memory!.Character);
        Assert.Equal("\u054a\u0561\u0580\u057f\u0565\u0566", memory.Place);
        Assert.Equal("\u0555\u0582\u0580\u0561\u056d", memory.Mood);
    }

    [Fact]
    public void TryExtract_PartialFields_ReturnsOnlyPresentFields()
    {
        var input = "Story.\nSTORY_MEMORY:\ncharacter:Bear\nmood:Happy";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.Equal("Story.", cleaned);
        Assert.NotNull(memory);
        Assert.Equal("Bear", memory!.Character);
        Assert.Null(memory.Place);
        Assert.Null(memory.ImportantObject);
        Assert.Null(memory.CurrentSituation);
        Assert.Equal("Happy", memory.Mood);
    }

    [Fact]
    public void TryExtract_NoMemoryBlock_ReturnsFalseAndUnchangedText()
    {
        var input = "Just a normal story with no memory block.";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.False(result);
        Assert.Equal(input, cleaned);
        Assert.Null(memory);
    }

    [Fact]
    public void TryExtract_EmptyFieldValues_ReturnsFalse()
    {
        var input = "Story.\nSTORY_MEMORY:\ncharacter:\nplace:\nmood:";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.False(result);
        Assert.Equal("Story.", cleaned);
        Assert.Null(memory);
    }

    [Fact]
    public void TryExtract_WindowsLineEndings_Handled()
    {
        var input = "Story.\r\nSTORY_MEMORY:\r\ncharacter:Fox\r\nplace:Cave";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.Equal("Story.", cleaned);
        Assert.NotNull(memory);
        Assert.Equal("Fox", memory!.Character);
        Assert.Equal("Cave", memory.Place);
    }

    [Fact]
    public void TryExtract_KeysCaseInsensitive()
    {
        var input = "Story.\nSTORY_MEMORY:\nCharacter:Fox\nPLACE:Garden\nMOOD:Playful";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.NotNull(memory);
        Assert.Equal("Fox", memory!.Character);
        Assert.Equal("Garden", memory.Place);
        Assert.Equal("Playful", memory.Mood);
    }

    [Fact]
    public void TryExtract_ValuesWithColons_FullValuePreserved()
    {
        var input = "Story.\nSTORY_MEMORY:\nsituation:Fox said: hello there";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.NotNull(memory);
        Assert.Equal("Fox said: hello there", memory!.CurrentSituation);
    }

    [Fact]
    public void TryExtract_UnrecognizedKeys_Ignored()
    {
        var input = "Story.\nSTORY_MEMORY:\ncharacter:Fox\ncolor:Red\ntone:Happy";

        var result = StoryMemoryParser.TryExtract(input, out var cleaned, out var memory);

        Assert.True(result);
        Assert.NotNull(memory);
        Assert.Equal("Fox", memory!.Character);
        Assert.Null(memory.Place);
        Assert.Null(memory.ImportantObject);
        Assert.Null(memory.CurrentSituation);
        Assert.Null(memory.Mood);
    }

    [Fact]
    public void TryExtract_EmptyString_ReturnsFalse()
    {
        var result = StoryMemoryParser.TryExtract("", out var cleaned, out var memory);

        Assert.False(result);
        Assert.Equal("", cleaned);
        Assert.Null(memory);
    }

    // --- Merge Tests ---

    [Fact]
    public void Merge_NullExisting_ReturnsIncoming()
    {
        var incoming = new StoryMemory("Fox", "Forest", null, null, "Happy", DateTime.UtcNow);

        var merged = StoryMemoryParser.Merge(null, incoming);

        Assert.Equal("Fox", merged.Character);
        Assert.Equal("Forest", merged.Place);
        Assert.Null(merged.ImportantObject);
        Assert.Equal("Happy", merged.Mood);
    }

    [Fact]
    public void Merge_IncomingOverridesExisting()
    {
        var existing = new StoryMemory("Bear", "Cave", "Lantern", "Sleeping", "Calm", DateTime.UtcNow.AddMinutes(-5));
        var incoming = new StoryMemory("Fox", null, null, "Running", null, DateTime.UtcNow);

        var merged = StoryMemoryParser.Merge(existing, incoming);

        Assert.Equal("Fox", merged.Character);
        Assert.Equal("Cave", merged.Place);
        Assert.Equal("Lantern", merged.ImportantObject);
        Assert.Equal("Running", merged.CurrentSituation);
        Assert.Equal("Calm", merged.Mood);
    }

    [Fact]
    public void Merge_UsesIncomingTimestamp()
    {
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var existing = new StoryMemory("A", null, null, null, null, t1);
        var incoming = new StoryMemory(null, "B", null, null, null, t2);

        var merged = StoryMemoryParser.Merge(existing, incoming);

        Assert.Equal(t2, merged.UpdatedAt);
        Assert.Equal("A", merged.Character);
        Assert.Equal("B", merged.Place);
    }
}

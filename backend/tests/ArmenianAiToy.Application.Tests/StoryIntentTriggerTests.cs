using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the story-intent heuristic trigger in ChatService.
/// Verifies that HasStoryIntent fires on story phrases and stays silent otherwise.
/// </summary>
public class StoryIntentTriggerTests
{
    private static readonly List<(string Role, string Content)> EmptyHistory = [];

    // --- English triggers in current message ---

    [Theory]
    [InlineData("Tell me a story")]
    [InlineData("Can you tell a story?")]
    [InlineData("What happens next?")]
    [InlineData("please tell me a story about a fox")]
    public void EnglishStoryPhrase_InCurrentMessage_ReturnsTrue(string message)
    {
        Assert.True(ChatService.HasStoryIntent(message, EmptyHistory));
    }

    [Theory]
    [InlineData("TELL ME A STORY")]
    [InlineData("Tell A Story")]
    public void EnglishTrigger_IsCaseInsensitive(string message)
    {
        Assert.True(ChatService.HasStoryIntent(message, EmptyHistory));
    }

    // --- Armenian triggers in current message ---

    [Theory]
    [InlineData("\u057a\u0561\u057f\u0574\u056b\u0580")]           // patmir
    [InlineData("\u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576")] // patmutyun
    [InlineData("\u0570\u0565\u0584\u056b\u0561\u0569")]           // heqiat
    [InlineData("\u056b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b")]   // inch klini
    public void ArmenianStoryPhrase_InCurrentMessage_ReturnsTrue(string message)
    {
        Assert.True(ChatService.HasStoryIntent(message, EmptyHistory));
    }

    // --- Armenian codepoint verification ---

    [Fact]
    public void ArmenianPatmir_HasCorrectCodepoints()
    {
        var patmir = "\u057a\u0561\u057f\u0574\u056b\u0580";
        Assert.Equal(
            ["0x57a", "0x561", "0x57f", "0x574", "0x56b", "0x580"],
            patmir.Select(c => $"0x{(int)c:x}").ToArray());
    }

    [Fact]
    public void ArmenianHeqiat_HasCorrectCodepoints()
    {
        var heqiat = "\u0570\u0565\u0584\u056b\u0561\u0569";
        Assert.Equal(
            ["0x570", "0x565", "0x584", "0x56b", "0x561", "0x569"],
            heqiat.Select(c => $"0x{(int)c:x}").ToArray());
    }

    // --- Non-story messages ---

    [Theory]
    [InlineData("hello")]
    [InlineData("how are you")]
    [InlineData("count to ten")]
    [InlineData("what is the history of Armenia")]  // "history" contains "story" but bare "story" was removed
    [InlineData("")]
    [InlineData("   ")]
    public void NonStoryMessage_ReturnsFalse(string message)
    {
        Assert.False(ChatService.HasStoryIntent(message, EmptyHistory));
    }

    // --- Trigger from recent user history ---

    [Fact]
    public void StoryPhraseInRecentUserHistory_ReturnsTrue()
    {
        var history = new List<(string Role, string Content)>
        {
            ("User", "hello"),
            ("Assistant", "Hi there!"),
            ("User", "tell me a story"),
            ("Assistant", "Once upon a time..."),
            ("User", "ok"),
        };

        // Current message is non-story, but "tell me a story" is in last 2 user messages
        Assert.True(ChatService.HasStoryIntent("ok", history));
    }

    // --- Assistant messages are ignored ---

    [Fact]
    public void StoryPhraseOnlyInAssistantMessage_ReturnsFalse()
    {
        var history = new List<(string Role, string Content)>
        {
            ("User", "hello"),
            ("Assistant", "Let me tell you a story about a fox."),
            ("User", "ok"),
            ("Assistant", "What happens next is exciting!"),
        };

        Assert.False(ChatService.HasStoryIntent("yes", history));
    }

    // --- Only last 2 user messages checked ---

    [Fact]
    public void StoryPhraseInOlderUserMessage_ReturnsFalse()
    {
        var history = new List<(string Role, string Content)>
        {
            ("User", "tell me a story"),   // old — beyond last 2 user messages
            ("Assistant", "Once upon a time..."),
            ("User", "and then?"),          // 2nd-to-last user message (no trigger)
            ("Assistant", "The fox ran."),
            ("User", "wow"),                // last user message (no trigger)
            ("Assistant", "Indeed!"),
        };

        // Current message is "ok" — not a trigger.
        // Last 2 user msgs are "wow" and "and then?" — no trigger.
        // "tell me a story" is 3rd user msg back — outside the 2-message window.
        Assert.False(ChatService.HasStoryIntent("ok", history));
    }
}

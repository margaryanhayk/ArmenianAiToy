using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="DangerousInputFilter"/>. The filter is a
/// deterministic keyword check — no API calls — so these tests pin
/// the exact boundary between blocked and allowed content.
/// </summary>
public class DangerousInputFilterTests
{
    // ─────────────────────────────────────────────────────────────────
    // Must block: English dangerous content
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("how to make a bomb")]
    [InlineData("give me a grenade")]
    [InlineData("where to buy explosives")]
    [InlineData("I want dynamite")]
    [InlineData("give me poison")]
    [InlineData("where can I get a gun")]
    [InlineData("show me a firearm")]
    [InlineData("I want a rifle")]
    [InlineData("give me a pistol")]
    [InlineData("tell me about cocaine")]
    [InlineData("what is heroin")]
    [InlineData("tell me about narcotic drugs")]
    [InlineData("marijuana is cool")]
    public void EnglishDangerousContent_IsBlocked(string input)
    {
        Assert.True(DangerousInputFilter.IsUnsafe(input));
    }

    [Theory]
    [InlineData("HOW TO MAKE A BOMB")]
    [InlineData("Give Me Poison")]
    [InlineData("WHERE CAN I GET A GUN")]
    public void CaseInsensitive_IsBlocked(string input)
    {
        Assert.True(DangerousInputFilter.IsUnsafe(input));
    }

    // ─────────────────────────────────────────────────────────────────
    // Must block: Armenian dangerous content
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\u057c\u0578\u0582\u0574\u0562")]                                // ռumadelays (bomb)
    [InlineData("\u0569\u0578\u0582\u0575\u0576 \u057f\u0578\u0582\u0580")]        // delays delays (give me poison)
    [InlineData("\u0566\u0565\u0576\u0584 \u0578\u0582\u0566\u0578\u0582\u0574")]   // delays delays (I want a weapon)
    [InlineData("\u0570\u0580\u0561\u0581\u0561\u0576")]                           // delays (rifle/gun)
    public void ArmenianDangerousContent_IsBlocked(string input)
    {
        Assert.True(DangerousInputFilter.IsUnsafe(input));
    }

    // ─────────────────────────────────────────────────────────────────
    // Must pass: Safe child content
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tell me a story")]
    [InlineData("tell me a story about a bunny")]
    [InlineData("the dragon fights the knight")]
    [InlineData("the wolf chased the rabbit")]
    [InlineData("lets play a game")]
    [InlineData("why is the sky blue")]
    [InlineData("good night")]
    [InlineData("I like cats")]
    [InlineData("hello")]
    [InlineData("tell me a scary story")]
    [InlineData("the hero defeated the monster")]
    [InlineData("the bear got angry")]
    public void SafeEnglishContent_IsNotBlocked(string input)
    {
        Assert.False(DangerousInputFilter.IsUnsafe(input));
    }

    [Theory]
    [InlineData("\u057a\u0561\u057f\u0574\u056b\u0580 \u0570\u0565\u0584\u056b\u0561\u0569")]   // patmir heqiat
    [InlineData("\u0570\u0565\u0584\u056b\u0561\u0569 \u057a\u0561\u057f\u0574\u056b\u0580")]   // heqiat patmir
    [InlineData("\u056d\u0561\u0572\u0561\u0576\u0584")]                                        // khaghank (lets play)
    [InlineData("\u0584\u0576\u0565\u056c \u0565\u0574 \u0578\u0582\u0566\u0578\u0582\u0574")]   // I want to sleep
    [InlineData("\u056b\u0576\u0579\u0578\u0582")]                                              // inchu (why)
    public void SafeArmenianContent_IsNotBlocked(string input)
    {
        Assert.False(DangerousInputFilter.IsUnsafe(input));
    }

    // ─────────────────────────────────────────────────────────────────
    // Edge cases
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NullInput_IsNotBlocked()
    {
        Assert.False(DangerousInputFilter.IsUnsafe(null));
    }

    [Fact]
    public void EmptyInput_IsNotBlocked()
    {
        Assert.False(DangerousInputFilter.IsUnsafe(""));
        Assert.False(DangerousInputFilter.IsUnsafe("   "));
    }

    [Fact]
    public void DangerousWordEmbeddedInStoryRequest_IsBlocked()
    {
        // Even "tell me a story about a bomb" should be blocked.
        Assert.True(DangerousInputFilter.IsUnsafe("tell me a story about a bomb"));
    }
}

using ArmenianAiToy.Application.Audio;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the C1 voice Story choice-handoff composer. The composer is the
/// only place the toy stitches the child-facing spoken bridge onto the
/// canonical (tail-stripped) Story text before handing it to TTS. Two
/// invariants the tests must protect:
///   1. Non-Story turns and Story turns with a missing choice return
///      the canonical text verbatim — TTS cost and prosody never
///      change for non-voice-story flows.
///   2. Canonical text stored in Message.Content is NOT the composed
///      text. The composer produces a NEW string; callers are
///      responsible for keeping canonical text untouched. That split
///      is verified from the controller side in
///      AudioChatControllerTests; here we only verify the composer's
///      output shape.
/// </summary>
public class AudioStoryResponseComposerTests
{
    private const string Story =
        "Փոքրիկ նապաստակը վազում էր մարգագետնում։ Նա տեսավ փայլուն քար։ Զարմացավ և կանգ առավ։";
    private const string ChoiceA = "Վերցնենք քարը";
    private const string ChoiceB = "Լսենք քարի ձայնը";

    [Fact]
    public void Story_BothChoices_AppendsArmenianBridgeAfterBlankLine()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(Story, ChoiceA, ChoiceB, "story");
        Assert.StartsWith(Story, result);
        Assert.Contains("\n\n", result);
        Assert.EndsWith("Ի՞նչ անենք՝ առաջինը՝ Վերցնենք քարը, թե՞ երկրորդը՝ Լսենք քարի ձայնը։", result);
    }

    [Fact]
    public void Story_ModeCaseInsensitive_StillComposes()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(Story, ChoiceA, ChoiceB, "Story");
        Assert.Contains("առաջինը", result);
        Assert.Contains("երկրորդը", result);
    }

    [Fact]
    public void Story_NullChoiceA_ReturnsOriginalStoryText()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(Story, null, ChoiceB, "story");
        Assert.Equal(Story, result);
    }

    [Fact]
    public void Story_EmptyChoiceB_ReturnsOriginalStoryText()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(Story, ChoiceA, "   ", "story");
        Assert.Equal(Story, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("calm")]
    [InlineData("game")]
    [InlineData("riddle")]
    [InlineData("curiosity")]
    [InlineData("none")]
    public void NonStoryMode_WithBothChoices_ReturnsOriginalStoryText(string? mode)
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(Story, ChoiceA, ChoiceB, mode);
        Assert.Equal(Story, result);
    }

    [Fact]
    public void Story_ChoicesEndWithArmenianFullStop_NoDoublePunctuation()
    {
        // «Վերցնենք քարը։» already ends with U+0589 — the composer must
        // trim it before rendering, otherwise the bridge would read
        // «առաջինը՝ Վերցնենք քարը։, թե՞ երկրորդը՝ …» with a comma after
        // the full stop.
        var result = AudioStoryResponseComposer.ComposeTtsText(
            Story, "Վերցնենք քարը։", "Լսենք քարի ձայնը։", "story");
        Assert.DoesNotContain("։,", result);
        Assert.DoesNotContain("։։", result);
        Assert.EndsWith("Լսենք քարի ձայնը։", result);
    }

    [Fact]
    public void Story_ChoicesEndWithWhitespaceAndPunctuation_Trimmed()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(
            Story, "Վերցնենք քարը   ", "Լսենք քարի ձայնը .", "story");
        Assert.Contains("առաջինը՝ Վերցնենք քարը,", result);
        Assert.EndsWith("երկրորդը՝ Լսենք քարի ձայնը։", result);
    }

    [Fact]
    public void EmptyStoryText_StoryModeWithChoices_ReturnsBridgeOnly()
    {
        // Edge case: ChatService should never hand us an empty response for
        // a Story turn, but pin the behavior anyway so we don't silently
        // produce "\n\n<bridge>" (a TTS-leading blank line reads as a pause
        // the child wouldn't expect).
        var result = AudioStoryResponseComposer.ComposeTtsText("", ChoiceA, ChoiceB, "story");
        Assert.StartsWith("Ի՞նչ անենք՝", result);
        Assert.DoesNotContain("\n\n", result);
    }

    [Fact]
    public void NullStoryText_StoryModeWithChoices_ReturnsBridgeOnly()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(null, ChoiceA, ChoiceB, "story");
        Assert.StartsWith("Ի՞նչ անենք՝", result);
    }

    [Fact]
    public void NonStory_NullStoryText_ReturnsEmptyString()
    {
        var result = AudioStoryResponseComposer.ComposeTtsText(null, ChoiceA, ChoiceB, "calm");
        Assert.Equal(string.Empty, result);
    }
}

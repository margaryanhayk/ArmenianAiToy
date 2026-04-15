using System.Reflection;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase B1: presence-based guards on the Story prompt constant.
/// Accessed via reflection so StoryChoiceInstruction stays private.
/// </summary>
public class StoryPromptContentTests
{
    private static string Prompt { get; } = LoadPrompt();

    private static string LoadPrompt()
    {
        var field = typeof(ChatService).GetField(
            "StoryChoiceInstruction",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null) as string;
        Assert.False(string.IsNullOrEmpty(value));
        return value!;
    }

    [Fact]
    public void OpeningVariety_SectionPresent()
    {
        Assert.Contains("OPENING VARIETY", Prompt);
    }

    [Fact]
    public void OpeningVariety_BansTimeFrameOpenersByDefault()
    {
        Assert.Contains("OVERUSED", Prompt);
        Assert.Contains("Մի անգամ", Prompt);
        Assert.Contains("Մի գեղեցիկ", Prompt);
    }

    [Fact]
    public void OpeningVariety_IncludesNewOpenerTypes()
    {
        Assert.Contains("texture/weather-sensation", Prompt);
        Assert.Contains("small surprise", Prompt);
    }

    [Fact]
    public void StoryRichness_RequiresConcreteSensory()
    {
        Assert.Contains("CONCRETE SENSORY", Prompt);
        Assert.Contains("Generic adjectives alone", Prompt);
    }

    [Fact]
    public void NoChildNarration_SectionPresent()
    {
        Assert.Contains("NO CHILD-NARRATION", Prompt);
        Assert.Contains("told TO the child", Prompt);
    }

    [Fact]
    public void ChoiceStakes_RuleRequiresDifferentOutcomes()
    {
        Assert.Contains("CHOICE STAKES", Prompt);
        Assert.Contains("change what actually", Prompt);
    }

    [Fact]
    public void TailBlockFormat_Unchanged()
    {
        Assert.Contains("CHOICE_A:short Armenian action (3–7 words)", Prompt);
        Assert.Contains("CHOICE_B:short Armenian action (3–7 words)", Prompt);
    }
}

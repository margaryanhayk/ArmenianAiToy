using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for ResponseQualityGate. Verifies the three retry conditions
/// trigger correctly and that clean responses pass through.
/// </summary>
public class ResponseQualityGateTests
{
    [Fact]
    public void CleanArmenianResponse_PassesGate()
    {
        var response = "Փոքրիկ նապաստակը պարտեզում խաղաց։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a bunny");
        Assert.Null(result);
    }

    [Fact]
    public void EmptyResponse_ReturnsNull()
    {
        Assert.Null(ResponseQualityGate.CheckRetry("", "anything"));
        Assert.Null(ResponseQualityGate.CheckRetry("   ", "anything"));
    }

    [Fact]
    public void FourLatinLettersInRow_TriggersLatinRun()
    {
        // "Once" is 4 Latin letters in a row.
        var response = "Once կար մեկ նապաստակ։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story");
        Assert.Equal("latin_run", result);
    }

    [Fact]
    public void ThreeLatinLettersInRow_DoesNotTrigger()
    {
        // "abc" has only 3 Latin letters in a row — under the 4-letter threshold.
        var response = "abc հեքիաթ կատու մասին։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a cat");
        Assert.Null(result);
    }

    [Fact]
    public void LeakedChoiceA_TriggersLeakedTag()
    {
        var response = "Հեքիաթը սկսվեց։ CHOICE_A: something";
        var result = ResponseQualityGate.CheckRetry(response, "patmir");
        Assert.Equal("leaked_tag", result);
    }

    [Fact]
    public void LeakedStoryMemory_TriggersLeakedTag()
    {
        var response = "Հեքիաթ։ STORY_MEMORY:";
        var result = ResponseQualityGate.CheckRetry(response, "patmir");
        Assert.Equal("leaked_tag", result);
    }

    [Fact]
    public void BunnyRequested_NoBunnyInResponse_TriggersSubjectMismatch()
    {
        var response = "Կատուն գնաց անտառ։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a bunny");
        Assert.Equal("subject_mismatch", result);
    }

    [Fact]
    public void DragonRequested_DragonInResponse_PassesGate()
    {
        var response = "Վիշապը թռավ լեռան վրա։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a dragon");
        Assert.Null(result);
    }

    [Fact]
    public void FishRequested_FishInResponse_PassesGate()
    {
        var response = "Փոքրիկ ձուկը լողում էր գետում։";
        var result = ResponseQualityGate.CheckRetry(response, "no I want a story about fish");
        Assert.Null(result);
    }

    [Fact]
    public void CatRequested_CatStemInResponse_PassesGate()
    {
        var response = "Կատվիկը նստեց պատուհանի մոտ։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a cat");
        Assert.Null(result);
    }

    [Fact]
    public void NoSubjectKeyword_NoMismatchCheck()
    {
        var response = "Արեգը շողում էր երկնքում։";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about magic");
        Assert.Null(result);
    }
}

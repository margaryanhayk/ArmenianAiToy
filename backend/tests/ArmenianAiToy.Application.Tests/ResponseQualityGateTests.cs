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
        var response = "\u0553\u0578\u0584\u0580\u056b\u056f \u0576\u0561\u057a\u0561\u057d\u057f\u0561\u056f\u0568 \u057a\u0561\u0580\u057f\u0565\u0566\u0578\u0582\u0574 \u056d\u0561\u0572\u0561\u0581\u0589";
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
        var response = "Once \u056f\u0561\u0580 \u0574\u0565\u056f \u0576\u0561\u057a\u0561\u057d\u057f\u0561\u056f\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story");
        Assert.Equal("latin_run", result);
    }

    [Fact]
    public void ThreeLatinLettersInRow_DoesNotTrigger()
    {
        // "abc" has only 3 Latin letters in a row — under the 4-letter threshold.
        var response = "abc \u0570\u0565\u0584\u056b\u0561\u0569 \u056f\u0561\u057f\u0578\u0582 \u0574\u0561\u057d\u056b\u0576\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a cat");
        Assert.Null(result);
    }

    [Fact]
    public void LeakedChoiceA_TriggersLeakedTag()
    {
        var response = "\u0540\u0565\u0584\u056b\u0561\u0569\u0568 \u057d\u056f\u057d\u057e\u0565\u0581\u0589 CHOICE_A: something";
        var result = ResponseQualityGate.CheckRetry(response, "patmir");
        Assert.Equal("leaked_tag", result);
    }

    [Fact]
    public void LeakedStoryMemory_TriggersLeakedTag()
    {
        var response = "\u0540\u0565\u0584\u056b\u0561\u0569\u0589 STORY_MEMORY:";
        var result = ResponseQualityGate.CheckRetry(response, "patmir");
        Assert.Equal("leaked_tag", result);
    }

    [Fact]
    public void BunnyRequested_NoBunnyInResponse_TriggersSubjectMismatch()
    {
        var response = "\u053f\u0561\u057f\u0578\u0582\u0576 \u0563\u0576\u0561\u0581 \u0561\u0576\u057f\u0561\u057c\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a bunny");
        Assert.Equal("subject_mismatch", result);
    }

    [Fact]
    public void DragonRequested_DragonInResponse_PassesGate()
    {
        var response = "\u054e\u056b\u0577\u0561\u057a\u0568 \u0569\u057c\u0561\u057e \u056c\u0565\u057c\u0561\u0576 \u057e\u0580\u0561\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a dragon");
        Assert.Null(result);
    }

    [Fact]
    public void FishRequested_FishInResponse_PassesGate()
    {
        var response = "\u0553\u0578\u0584\u0580\u056b\u056f \u0571\u0578\u0582\u056f\u0568 \u056c\u0578\u0572\u0578\u0582\u0574 \u0567\u0580 \u0563\u0565\u057f\u0578\u0582\u0574\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "no I want a story about fish");
        Assert.Null(result);
    }

    [Fact]
    public void CatRequested_CatStemInResponse_PassesGate()
    {
        var response = "\u053f\u0561\u057f\u057e\u056b\u056f\u0568 \u0576\u057d\u057f\u0565\u0581 \u057a\u0561\u057f\u0578\u0582\u0570\u0561\u0576\u056b \u0574\u0578\u057f\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about a cat");
        Assert.Null(result);
    }

    [Fact]
    public void NoSubjectKeyword_NoMismatchCheck()
    {
        var response = "\u0531\u0580\u0565\u0563\u0568 \u0577\u0578\u0572\u0578\u0582\u0574 \u0567\u0580 \u0565\u0580\u056f\u0576\u0584\u0578\u0582\u0574\u0589";
        var result = ResponseQualityGate.CheckRetry(response, "tell me a story about magic");
        Assert.Null(result);
    }

    // --- Mode-aware overload: Calm mode checks ---

    [Fact]
    public void Calm_QuestionMark_TriggersCalmQuestion()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u053c\u0561\u057e \u0567\u0580, \u056b\u0576\u0579 \u0565\u057d \u0578\u0582\u0566\u0578\u0582\u0574?", "good night", DetectedMode.Calm);
        Assert.Equal("calm_question", result);
    }

    [Fact]
    public void Calm_ArmenianQuestionMark_TriggersCalmQuestion()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u053c\u0561\u057e \u0567\u057e\u055e", "kpnem", DetectedMode.Calm);
        Assert.Equal("calm_question", result);
    }

    [Fact]
    public void Calm_ExclamationMark_TriggersCalmExclamation()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u053c\u0561\u057e \u0567\u0580!", "i'm tired", DetectedMode.Calm);
        Assert.Equal("calm_exclamation", result);
    }

    [Fact]
    public void Calm_ArmenianExclamation_TriggersCalmExclamation()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u0548\u0582\u0580\u0561\u055c", "sleep now", DetectedMode.Calm);
        Assert.Equal("calm_exclamation", result);
    }

    [Fact]
    public void Calm_CleanResponse_PassesGate()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u0531\u0579\u0584\u0565\u0580\u0564 \u0583\u0561\u056f\u056b\u0580, \u0561\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0580\u0589", "good night", DetectedMode.Calm);
        Assert.Null(result);
    }

    [Fact]
    public void Calm_UniversalCheck_StillApplies()
    {
        var result = ResponseQualityGate.CheckRetry(
            "Once \u056f\u0561\u0580 \u0574\u0565\u056f\u0589", "kpnem", DetectedMode.Calm);
        Assert.Equal("latin_run", result);
    }

    [Fact]
    public void Story_QuestionMark_DoesNotTriggerCalmCheck()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u053c\u0561\u057e \u0567\u0580, \u056b\u0576\u0579 \u0565\u057d \u0578\u0582\u0566\u0578\u0582\u0574?", "tell me a story", DetectedMode.Story);
        Assert.Null(result);
    }

    [Fact]
    public void None_QuestionMark_DoesNotTriggerCalmCheck()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u053c\u0561\u057e \u0567\u0580?", "hello", DetectedMode.None);
        Assert.Null(result);
    }

    // --- Mode-aware overload: Curiosity checks ---

    [Fact]
    public void Curiosity_QuestionMark_TriggersCuriosityQuestion()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567, \u0561\u0575\u0576\u057a\u0565\u057d \u0579\u0567?",
            "why is the sky blue", DetectedMode.Curiosity);
        Assert.Equal("curiosity_question", result);
    }

    [Fact]
    public void Curiosity_ArmenianQuestionMark_TriggersCuriosityQuestion()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u0540\u0565\u057f\u0561\u0584\u0580\u0584\u056b\u0580 \u0567\u055e",
            "what is a rainbow", DetectedMode.Curiosity);
        Assert.Equal("curiosity_question", result);
    }

    [Fact]
    public void Curiosity_ShortCleanResponse_PassesGate()
    {
        var result = ResponseQualityGate.CheckRetry(
            "\u0535\u0580\u056f\u056b\u0576\u0584\u0568 \u056f\u0561\u057a\u0578\u0582\u0575\u057f \u0567, \u0578\u0580\u0578\u057e\u0570\u0565\u057f\u0587 \u056c\u0578\u0582\u0575\u057d\u0568 \u0581\u0580\u057e\u0578\u0582\u0574 \u0567\u0589",
            "why is the sky blue", DetectedMode.Curiosity);
        Assert.Null(result);
    }

    [Fact]
    public void Curiosity_LongResponse_TriggersCuriosityTooLong()
    {
        // >200 chars = lecture territory.
        var longResponse = new string('\u0561', 201);
        var result = ResponseQualityGate.CheckRetry(
            longResponse, "why is the sky blue", DetectedMode.Curiosity);
        Assert.Equal("curiosity_too_long", result);
    }

    [Fact]
    public void Curiosity_Exactly200Chars_PassesGate()
    {
        var exactResponse = new string('\u0561', 200);
        var result = ResponseQualityGate.CheckRetry(
            exactResponse, "why is the sky blue", DetectedMode.Curiosity);
        Assert.Null(result);
    }

    [Fact]
    public void Story_LongResponse_DoesNotTriggerCuriosityCheck()
    {
        var longResponse = new string('\u0561', 500);
        var result = ResponseQualityGate.CheckRetry(
            longResponse, "tell me a story", DetectedMode.Story);
        Assert.Null(result);
    }

    // --- Mode-aware overload: Game brevity checks ---

    [Fact]
    public void Game_ShortResponse_PassesGate()
    {
        var result = ResponseQualityGate.CheckRetry(
            new string('\u0561', 100), "lets play", DetectedMode.Game);
        Assert.Null(result);
    }

    [Fact]
    public void Game_Exactly150Chars_PassesGate()
    {
        var result = ResponseQualityGate.CheckRetry(
            new string('\u0561', 150), "lets play", DetectedMode.Game);
        Assert.Null(result);
    }

    [Fact]
    public void Game_LongResponse_TriggersGameTooLong()
    {
        var result = ResponseQualityGate.CheckRetry(
            new string('\u0561', 201), "lets play", DetectedMode.Game);
        Assert.Equal("game_too_long", result);
    }

    [Fact]
    public void Story_LongResponse_DoesNotTriggerGameCheck()
    {
        var result = ResponseQualityGate.CheckRetry(
            new string('\u0561', 500), "tell me a story", DetectedMode.Story);
        Assert.Null(result);
    }
}

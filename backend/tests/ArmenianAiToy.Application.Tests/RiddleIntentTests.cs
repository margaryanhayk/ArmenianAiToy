using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class RiddleIntentTests
{
    [Theory]
    [InlineData("another one")]
    [InlineData("give me another")]
    [InlineData("\u0587\u057d \u0574\u0565\u056f")]                                    // ևս մեկ
    [InlineData("\u0576\u0578\u0580\u056b\u0581")]                                     // նորից
    [InlineData("\u0578\u0582\u0580\u056b\u0577 \u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f")] // ուրիշ հանելուկ
    [InlineData("\u0567\u056c\u056b \u0574\u0565\u056f")]                              // էլի մեկ
    public void Detect_StartNew_OnExplicitAnotherTriggers_EvenWithActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.StartNew, RiddleIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("riddle")]
    [InlineData("give me a riddle")]
    [InlineData("\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f")]                  // հանելուկ
    public void Detect_StartNew_OnRiddleWord_WhenNoActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.StartNew, RiddleIntentDetector.Detect(msg, hasActiveRound: false));
    }

    [Theory]
    [InlineData("riddle")]
    [InlineData("give me a riddle")]
    [InlineData("haneluk")]
    [InlineData("\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f")]                  // հանելուկ
    public void Detect_StartNew_OnRiddleWord_EvenWithActiveRound(string msg)
    {
        // A child saying «հանելուկ» mid-round is asking for a fresh riddle,
        // not submitting a guess. Required for ModeBenchmark and for
        // returning to Riddle mode after a Story detour.
        Assert.Equal(RiddleIntent.StartNew, RiddleIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("i don't know")]
    [InlineData("give up")]
    [InlineData("\u0579\u0563\u056b\u057f\u0565\u0574")]                              // չգիտեմ
    [InlineData("\u0561\u057d\u0561 \u057a\u0561\u057f\u0561\u057d\u056d\u0561\u0576\u0568")] // ասա պատասխանը
    public void Detect_GiveUp_WithActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.GiveUp, RiddleIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("\u056f\u0561\u057f\u0578\u0582")]                                     // կատու — a guess
    [InlineData("a cat")]
    [InlineData("\u0584\u0561\u0580")]                                                 // քար
    public void Detect_Guess_WithActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.Guess, RiddleIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Fact]
    public void Detect_NoActiveRound_NoTrigger_DefaultsToStartNew()
    {
        // Loop is opening — treat random first message as a request to start.
        Assert.Equal(RiddleIntent.StartNew, RiddleIntentDetector.Detect("hi", hasActiveRound: false));
    }
}

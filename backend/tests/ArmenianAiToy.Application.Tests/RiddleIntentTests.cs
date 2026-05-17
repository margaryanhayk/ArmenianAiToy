using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class RiddleIntentTests
{
    [Theory]
    [InlineData("another one")]
    [InlineData("give me another")]
    [InlineData("ևս մեկ")]                                    // ևս մեկ
    [InlineData("նորից")]                                     // նորից
    [InlineData("ուրիշ հանելուկ")] // ուրիշ հանելուկ
    [InlineData("էլի մեկ")]                              // էլի մեկ
    // 2026-05-18 follow-up: spec-pinned multi-word "another" forms from the
    // BenchmarkAll run-3 RB04 evidence. Each is also covered by RiddleWords
    // («հանելուկ»), but listing them in StartNewTriggers protects them
    // against a future RiddleWords refactor.
    [InlineData("նոր հանելուկ")] // նոր հանելուկ
    [InlineData("էլի հանելուկ")] // էլի հանելուկ
    public void Detect_StartNew_OnExplicitAnotherTriggers_EvenWithActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.StartNew, RiddleIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("riddle")]
    [InlineData("give me a riddle")]
    [InlineData("հանելուկ")]                  // հանելուկ
    public void Detect_StartNew_OnRiddleWord_WhenNoActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.StartNew, RiddleIntentDetector.Detect(msg, hasActiveRound: false));
    }

    [Theory]
    [InlineData("riddle")]
    [InlineData("give me a riddle")]
    [InlineData("haneluk")]
    [InlineData("հանելուկ")]                  // հանելուկ
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
    [InlineData("չգիտեմ")]                              // չգիտեմ
    [InlineData("ասա պատասխանը")] // ասա պատասխանը
    public void Detect_GiveUp_WithActiveRound(string msg)
    {
        Assert.Equal(RiddleIntent.GiveUp, RiddleIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("կատու")]                                     // կատու — a guess
    [InlineData("a cat")]
    [InlineData("քար")]                                                 // քար
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

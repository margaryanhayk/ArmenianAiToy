using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

public class GameIntentTests
{
    [Theory]
    [InlineData("let's play")]
    [InlineData("play with me")]
    [InlineData("let's play a game")]
    [InlineData("\u056d\u0561\u0572\u0561\u0576\u0584")]                // խաղանք
    [InlineData("\u056d\u0561\u0572 \u056f\u0561")]                       // խաղ կա
    public void Detect_StartNew_OnPlayTrigger_WhenNoActiveRound(string msg)
    {
        Assert.Equal(GameIntent.StartNew, GameIntentDetector.Detect(msg, hasActiveRound: false));
    }

    [Theory]
    [InlineData("another game")]
    [InlineData("different game")]
    [InlineData("new game")]
    [InlineData("\u0578\u0582\u0580\u056b\u0577 \u056d\u0561\u0572")]    // ուրիշ խաղ
    [InlineData("\u0576\u0578\u0580 \u056d\u0561\u0572")]                 // նոր խաղ
    public void Detect_SwitchGame_OnSwitchTriggers_EvenWithActiveRound(string msg)
    {
        Assert.Equal(GameIntent.SwitchGame, GameIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("i'm done")]
    [InlineData("enough")]
    [InlineData("\u0562\u0561\u057e \u0567")]                              // բավ է
    [InlineData("\u057e\u0565\u0580\u057b")]                               // վերջ
    [InlineData("\u0570\u0565\u0580\u056b\u0584 \u0567")]                  // հերիք է
    public void Detect_Stop_WithActiveRound(string msg)
    {
        Assert.Equal(GameIntent.Stop, GameIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("\u056f\u0561\u057f\u0578\u0582")]                         // կատու — child's response
    [InlineData("red")]
    [InlineData("done")]
    [InlineData("ok")]
    public void Detect_Continue_WithActiveRound(string msg)
    {
        Assert.Equal(GameIntent.Continue, GameIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Fact]
    public void Detect_NoActiveRound_StopWord_DefaultsToStartNew()
    {
        // Without an active round there is nothing to stop — the loop is opening.
        Assert.Equal(GameIntent.StartNew, GameIntentDetector.Detect("stop", hasActiveRound: false));
    }

    [Fact]
    public void Detect_NoActiveRound_NoTrigger_DefaultsToStartNew()
    {
        Assert.Equal(GameIntent.StartNew, GameIntentDetector.Detect("hi", hasActiveRound: false));
    }

    [Fact]
    public void Detect_SwitchBeatsStop()
    {
        // "let's switch" wins even when stop-like words are also present.
        Assert.Equal(
            GameIntent.SwitchGame,
            GameIntentDetector.Detect("stop, let's switch game", hasActiveRound: true));
    }
}

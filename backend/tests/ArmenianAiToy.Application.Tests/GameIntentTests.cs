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

    [Theory]
    [InlineData("stop")]
    [InlineData("բավ է")]                    // բավ է
    [InlineData("չեմ ուզում խաղալ")]         // I don't want to play
    public void Detect_NoActiveRound_StopWord_ReturnsStop(string msg)
    {
        // KEYSTONE (reversed 2026-08-05): a stop word must return Stop even
        // with no active round. The old pinned behavior (StartNew) meant a
        // child saying "enough" — e.g. after the model dropped its tail
        // block and the round was lost — got a brand-new game started at
        // them, with no exit. The warm goodbye is always the right reply.
        Assert.Equal(GameIntent.Stop, GameIntentDetector.Detect(msg, hasActiveRound: false));
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

    [Theory]
    [InlineData("Բա՛վ է։")]                  // emphatic "enough!" — the natural register
    [InlineData("Վե՛րջ։")]                   // emphatic "stop!"
    [InlineData("վե՞րջ")]                    // question-marked form
    public void Detect_EmphaticArmenianStopForms_ReturnStop(string msg)
    {
        // Armenian writes ՛ ՜ ՞ INSIDE the word. Without normalization,
        // «բա՛վ է» does not contain «բավ է» and Stop never fired.
        Assert.Equal(GameIntent.Stop, GameIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("չեմ ուզում ուրիշ խաղ")]     // "I don't want another game"
    [InlineData("i don't want another game")]
    public void Detect_NegatedSwitchPhrase_IsStop_NotSwitch(string msg)
    {
        // KEYSTONE: a switch phrase inside a refusal is a refusal. Reading
        // «չեմ ուզում ուրիշ խաղ» as SwitchGame started a different game at
        // a child who was declining.
        Assert.Equal(GameIntent.Stop, GameIntentDetector.Detect(msg, hasActiveRound: true));
    }

    [Theory]
    [InlineData("վերջին անգամ")]             // "one last time" — contains վերջ
    [InlineData("վերջապես ստացվեց")]         // "it finally worked"
    public void Detect_VerjInsideLongerWord_DoesNotStop(string msg)
    {
        // «վերջ» is matched as a whole word only — «վերջին անգամ» is a
        // request for one more round, not a stop.
        Assert.Equal(GameIntent.Continue, GameIntentDetector.Detect(msg, hasActiveRound: true));
    }
}

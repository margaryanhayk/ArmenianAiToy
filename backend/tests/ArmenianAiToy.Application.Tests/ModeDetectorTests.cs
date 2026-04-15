using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="ModeDetector"/>. The detector is pure-function and not
/// yet wired into ChatService — these tests pin its behavior so the wiring
/// step (ROADMAP.md Phase 4) has a stable contract to integrate against.
///
/// Detection priority being verified:
///   Calm > Curiosity > Active continuation > Explicit trigger > History trigger > None
/// </summary>
public class ModeDetectorTests
{
    private static readonly List<(string Role, string Content)> EmptyHistory = [];

    // ─────────────────────────────────────────────────────────────────────
    // Story mode
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tell me a story")]
    [InlineData("Tell me a story about a fox")]
    [InlineData("can you tell a story")]
    [InlineData("what happens next")]
    public void Story_EnglishTrigger_DetectsStory(string message)
    {
        Assert.Equal(DetectedMode.Story, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("\u057a\u0561\u057f\u0574\u056b\u0580")]                              // patmir
    [InlineData("\u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576")] // patmutyun
    [InlineData("\u0570\u0565\u0584\u056b\u0561\u0569")]                              // heqiat
    [InlineData("\u056b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b")]                  // inch klini
    [InlineData("\u0577\u0561\u0580\u0578\u0582\u0576\u0561\u056f\u056b\u0580")]       // sharunakir (continue)
    [InlineData("\u056b\u0576\u0579 \u0565\u0572\u0561\u057e")]                        // inch eghav (what happened)
    public void Story_ArmenianTrigger_DetectsStory(string message)
    {
        Assert.Equal(DetectedMode.Story, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("patmir")]
    [InlineData("heqiat patmir")]
    [InlineData("hekiat")]
    [InlineData("u heto?")]
    public void Story_TransliteratedTrigger_DetectsStory(string message)
    {
        Assert.Equal(DetectedMode.Story, ModeDetector.Detect(message, EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Game mode
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("let's play")]
    [InlineData("Let's play a game")]
    [InlineData("play with me")]
    [InlineData("can we play a game")]
    public void Game_EnglishTrigger_DetectsGame(string message)
    {
        Assert.Equal(DetectedMode.Game, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("\u056d\u0561\u0572\u0561\u0576\u0584")]   // խաղանք
    [InlineData("\u056d\u0561\u0572\u0561\u056c")]          // խաղալ
    [InlineData("khaghank")]
    [InlineData("khaghal")]
    [InlineData("մի խաղ անենք")]                // մի խաղ անենք (mi khagh anenk)
    [InlineData("ուրիշ խաղ")]                             // ուրիշ խաղ (urish khagh)
    [InlineData("նոր խաղ")]                                         // նոր խաղ (nor khagh)
    public void Game_ArmenianTrigger_DetectsGame(string message)
    {
        Assert.Equal(DetectedMode.Game, ModeDetector.Detect(message, EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Riddle mode
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("riddle me this")]
    [InlineData("give me a riddle")]
    [InlineData("ask me a riddle")]
    [InlineData("guess what")]
    public void Riddle_EnglishTrigger_DetectsRiddle(string message)
    {
        Assert.Equal(DetectedMode.Riddle, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f")] // հանելուկ
    [InlineData("haneluk tur")]
    public void Riddle_ArmenianTrigger_DetectsRiddle(string message)
    {
        Assert.Equal(DetectedMode.Riddle, ModeDetector.Detect(message, EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Curiosity Window
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("why is the sky blue")]
    [InlineData("how come dogs bark")]
    [InlineData("what is a rainbow")]
    [InlineData("what's a star")]
    [InlineData("where is the sun at night")]
    [InlineData("where does rain come from")]
    [InlineData("where do birds go in winter")]
    [InlineData("how does a rainbow form")]
    [InlineData("how do fish breathe")]
    public void Curiosity_EnglishStarter_DetectsCuriosity(string message)
    {
        Assert.Equal(DetectedMode.Curiosity, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("\u056b\u0576\u0579\u0578\u0582 \u0567 \u0571\u0575\u0578\u0582\u0576\u0568 \u057d\u057a\u056b\u057f\u0561\u056f")] // ինչու է ձյունը սպիտակ
    [InlineData("\u056b\u0576\u0579\u057a\u0565\u057d \u0567 \u0561\u0577\u056d\u0561\u0580\u0570\u0568")]                          // ինչպես է աշխարհը
    [InlineData("ինչ է սա")]                                                                        // ինչ է սա (what is this)
    [InlineData("սա ինչ է")]                                                                        // սա ինչ է (this what is)
    [InlineData("ոնց է աշխատում")]                                     // ոնց է աշխատում (how does it work)
    [InlineData("ինչի համար է")]                                                 // ինչի համար է (what is it for)
    public void Curiosity_ArmenianStarter_DetectsCuriosity(string message)
    {
        Assert.Equal(DetectedMode.Curiosity, ModeDetector.Detect(message, EmptyHistory));
    }

    [Fact]
    public void Curiosity_DoesNotMatchInsideLargerWord()
    {
        // "whyever" must not trigger curiosity — word-leading match only.
        Assert.Equal(DetectedMode.None, ModeDetector.Detect("whyever lets do that", EmptyHistory));
    }

    [Fact]
    public void Curiosity_OverridesActiveStorySession()
    {
        // A real off-topic question mid-story is a one-turn detour.
        Assert.Equal(DetectedMode.Curiosity,
            ModeDetector.Detect("why is the moon white", EmptyHistory, hasActiveStorySession: true));
    }

    [Fact]
    public void Curiosity_DoesNotFireForWhatHappensNext()
    {
        // "what happens next" is story, not curiosity.
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("what happens next", EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Calm / Bedtime
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("i'm tired")]
    [InlineData("im tired")]
    [InlineData("I'm sleepy")]
    [InlineData("good night")]
    [InlineData("goodnight")]
    [InlineData("bedtime")]
    [InlineData("time for bed")]
    [InlineData("kpnem")]
    public void Calm_EnglishTrigger_DetectsCalm(string message)
    {
        Assert.Equal(DetectedMode.Calm, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("\u0584\u0576\u0565\u056c")]                                          // քնել
    [InlineData("\u0570\u0578\u0563\u0576\u0561\u056e \u0565\u0574")]                  // հոգնած եմ
    [InlineData("\u0563\u056b\u0577\u0565\u0580 \u0562\u0561\u0580\u056b")]            // գիշեր բարի
    [InlineData("\u0576\u0576\u057b\u0565\u056c \u0565\u0574 \u0578\u0582\u0566\u0578\u0582\u0574")] // ննջել եմ ուզում
    [InlineData("քնեմ ուզում")]                                              // քնեմ ուզում (let me sleep)
    [InlineData("քնկոտ եմ")]                                                                  // քնկոտ եմ (I am sleepy)
    [InlineData("բարի գիշեր")]                                                     // բարի գիշեր (good night)
    [InlineData("հոգնել եմ")]                                                            // հոգնել եմ (got tired)
    public void Calm_ArmenianTrigger_DetectsCalm(string message)
    {
        Assert.Equal(DetectedMode.Calm, ModeDetector.Detect(message, EmptyHistory));
    }

    [Fact]
    public void Calm_HighestPriority_OverridesActiveStorySession()
    {
        // Bedtime cue mid-story always wins. Safety + parent trust.
        Assert.Equal(DetectedMode.Calm,
            ModeDetector.Detect("i'm tired", EmptyHistory, hasActiveStorySession: true));
    }

    [Fact]
    public void Calm_DoesNotFireOnStoryAboutSleeping()
    {
        // The story-cue gate prevents calm from hijacking topic words.
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("tell me a story about sleeping", EmptyHistory));
    }

    [Fact]
    public void Calm_DoesNotFireOnStoryAboutTiredBear()
    {
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("tell me a story about a tired bear", EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Active continuation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActiveStorySession_NeutralMessage_ContinuesStory()
    {
        // No explicit trigger but the conversation has pending story choices.
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("ok", EmptyHistory, hasActiveStorySession: true));
    }

    [Fact]
    public void ActiveStorySession_DoesNotOverrideExplicitGameRequest()
    {
        // Currently, an explicit "let's play" mid-story does NOT yet beat
        // active continuation in the priority order — active session wins.
        // This pins current behavior and documents it for the wiring step.
        // (Future refinement may flip this; if so, update the test and MODES.md.)
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("let's play a game", EmptyHistory, hasActiveStorySession: true));
    }

    // ─────────────────────────────────────────────────────────────────────
    // History fallback
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void HistoryTrigger_StoryInRecentUserMessage()
    {
        var history = new List<(string Role, string Content)>
        {
            ("user", "hello"),
            ("assistant", "Hi!"),
            ("user", "tell me a story"),
            ("assistant", "Once upon a time..."),
            ("user", "ok"),
        };

        Assert.Equal(DetectedMode.Story, ModeDetector.Detect("ok", history));
    }

    [Fact]
    public void HistoryTrigger_GameInRecentUserMessage()
    {
        var history = new List<(string Role, string Content)>
        {
            ("user", "let's play a game"),
            ("assistant", "OK!"),
        };

        Assert.Equal(DetectedMode.Game, ModeDetector.Detect("yes", history));
    }

    [Fact]
    public void HistoryTrigger_OnlyChecksLastTwoUserMessages()
    {
        var history = new List<(string Role, string Content)>
        {
            ("user", "tell me a story"),    // outside the 2-user-message window
            ("assistant", "Once..."),
            ("user", "and then?"),           // 2nd-to-last user message
            ("assistant", "..."),
            ("user", "wow"),                 // last user message
            ("assistant", "..."),
        };

        Assert.Equal(DetectedMode.None, ModeDetector.Detect("ok", history));
    }

    [Fact]
    public void HistoryTrigger_AssistantMessagesAreIgnored()
    {
        var history = new List<(string Role, string Content)>
        {
            ("user", "hello"),
            ("assistant", "Let me tell you a story about a fox."),
            ("user", "ok"),
        };

        Assert.Equal(DetectedMode.None, ModeDetector.Detect("yes", history));
    }

    // ─────────────────────────────────────────────────────────────────────
    // No-signal cases
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("hi")]
    [InlineData("ok")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("count to ten")]
    public void NoSignal_ReturnsNone(string message)
    {
        Assert.Equal(DetectedMode.None, ModeDetector.Detect(message, EmptyHistory));
    }

    [Fact]
    public void HistoryWord_DoesNotMatchStoryTrigger()
    {
        // " story" trigger has a leading space, so "history" must not match it.
        // The message is a real off-topic question, so it falls through to Curiosity.
        Assert.Equal(DetectedMode.Curiosity,
            ModeDetector.Detect("what is the history of Armenia", EmptyHistory));
    }

    [Fact]
    public void NullMessage_ReturnsNone()
    {
        Assert.Equal(DetectedMode.None, ModeDetector.Detect(null, EmptyHistory));
    }

    [Fact]
    public void NullHistory_DoesNotThrow()
    {
        Assert.Equal(DetectedMode.Story, ModeDetector.Detect("tell me a story", null));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Case insensitivity
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TELL ME A STORY", DetectedMode.Story)]
    [InlineData("LET'S PLAY", DetectedMode.Game)]
    [InlineData("RIDDLE", DetectedMode.Riddle)]
    [InlineData("WHY IS THE SKY BLUE", DetectedMode.Curiosity)]
    [InlineData("I'M TIRED", DetectedMode.Calm)]
    public void Detection_IsCaseInsensitive(string message, DetectedMode expected)
    {
        Assert.Equal(expected, ModeDetector.Detect(message, EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Codepoint sanity check (matches StoryIntentTriggerTests pattern)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ArmenianHaneluk_HasCorrectCodepoints()
    {
        var haneluk = "\u0570\u0561\u0576\u0565\u056c\u0578\u0582\u056f";
        Assert.Equal(
            ["0x570", "0x561", "0x576", "0x565", "0x56c", "0x578", "0x582", "0x56f"],
            haneluk.Select(c => $"0x{(int)c:x}").ToArray());
    }

    [Fact]
    public void ArmenianKnel_HasCorrectCodepoints()
    {
        var knel = "\u0584\u0576\u0565\u056c";
        Assert.Equal(
            ["0x584", "0x576", "0x565", "0x56c"],
            knel.Select(c => $"0x{(int)c:x}").ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // C1: widened Calm fear/can't-sleep triggers + story-gate anti-false-fire
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Calm_FearOfDark_Armenian_DetectsCalm()
    {
        Assert.Equal(DetectedMode.Calm,
            ModeDetector.Detect("\u0574\u0569\u056b\u0581 \u057e\u0561\u056d\u0565\u0576\u0578\u0582\u0574 \u0565\u0574", EmptyHistory));
    }

    [Theory]
    [InlineData("i can't sleep")]
    [InlineData("i cant sleep")]
    [InlineData("can't sleep")]
    public void Calm_CantSleep_English_DetectsCalm(string message)
    {
        Assert.Equal(DetectedMode.Calm, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("\u0579\u0565\u0574 \u056f\u0561\u0580\u0578\u0572\u0561\u0576\u0578\u0582\u0574 \u0584\u0576\u0565\u056c")] // չեմ կարողանում քնել
    [InlineData("\u0579\u0565\u0574 \u056f\u0561\u0580\u0578\u0572 \u0584\u0576\u0565\u056c")]                               // չեմ կարող քնել
    public void Calm_CantSleep_Armenian_DetectsCalm(string message)
    {
        Assert.Equal(DetectedMode.Calm, ModeDetector.Detect(message, EmptyHistory));
    }

    [Theory]
    [InlineData("scared of the dark")]
    [InlineData("afraid of the dark")]
    [InlineData("scared at night")]
    public void Calm_FearOfDark_English_DetectsCalm(string message)
    {
        Assert.Equal(DetectedMode.Calm, ModeDetector.Detect(message, EmptyHistory));
    }

    [Fact]
    public void Calm_FearInsideStoryRequest_StaysStory()
    {
        // Story-cue gate must win: a story request about being scared of the dark is Story.
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("tell me a story about being scared of the dark", EmptyHistory));
    }

    [Fact]
    public void Calm_CantSleepInsideStoryRequest_StaysStory()
    {
        // Defensive: even with a can't-sleep phrase, a story request stays Story.
        Assert.Equal(DetectedMode.Story,
            ModeDetector.Detect("tell me a story about a bunny that can't sleep", EmptyHistory));
    }

    // ─────────────────────────────────────────────────────────────────────
    // C1: widened Curiosity "where" triggers
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Curiosity_Where_English_DetectsCuriosity()
    {
        Assert.Equal(DetectedMode.Curiosity, ModeDetector.Detect("where is the moon", EmptyHistory));
    }

    [Fact]
    public void Curiosity_Where_Armenian_DetectsCuriosity()
    {
        // որտեղ է ապրում լուսինը
        Assert.Equal(DetectedMode.Curiosity,
            ModeDetector.Detect("\u0578\u0580\u057f\u0565\u0572 \u0567 \u0561\u057a\u0580\u0578\u0582\u0574 \u056c\u0578\u0582\u057d\u056b\u0576\u0568", EmptyHistory));
    }

    [Fact]
    public void Curiosity_WhereInsideWord_DoesNotFire()
    {
        // "somewhere" contains "where" as substring; StartsWithWord must prevent a match.
        Assert.Equal(DetectedMode.None, ModeDetector.Detect("somewhere over the rainbow", EmptyHistory));
    }
}

using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Q&amp;A service boundary: the single seam the wiring slice will
/// route child questions through while a library story is active.
/// Pins the prompt → model → filter → repair-once → fallback flow, the
/// "only filtered text reaches the output seam" guarantee, and that
/// answering a question never moves the story position. Uses a fake
/// <see cref="IAiChatClient"/> — no real model calls.
/// </summary>
public class LibraryStoryQuestionServiceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static CuratedStory LoadAnbanHuri()
    {
        var path = Path.Combine(
            RepoRoot(), "backend", "content", "story-drafts", "anban-huri.story.json");
        return StoryFileParser.Parse(File.ReadAllText(path), path, requireApproved: false);
    }

    private static readonly CuratedStory Story = LoadAnbanHuri();

    private const string GoodAnswer =
        "Ոչ, գորտերը բամբակ չէին մանում։ Հուռին նրանց ձայնը սխալ հասկացավ։";

    private const string BadAnswer =
        "As an AI language model, I cannot answer that.";

    /// <summary>Scripted fake: returns queued responses in order and
    /// records every (systemPrompt, messages) call.</summary>
    private sealed class FakeAiClient(params string[] responses) : IAiChatClient
    {
        private readonly Queue<string> _responses = new(responses);
        public List<(string SystemPrompt, List<(string Role, string Content)> Messages)> Calls { get; } = [];

        public Task<string> GetCompletionAsync(
            string systemPrompt, List<(string Role, string Content)> messages)
        {
            Calls.Add((systemPrompt, messages));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("model unavailable");
            }
            return Task.FromResult(_responses.Dequeue());
        }
    }

    [Fact]
    public async Task GoodFirstAnswer_IsReturnedFiltered_NoRetry()
    {
        var ai = new FakeAiClient(GoodAnswer);
        var service = new LibraryStoryQuestionService(ai);

        var result = await service.AnswerAsync(Story, 3, "Գորտերը իրո՞ք բամբակ մանեցին");

        Assert.Equal(GoodAnswer, result.Text);
        Assert.False(result.UsedFallback);
        Assert.Equal(StoryAnswerRejection.None, result.FirstRejection);
        Assert.Null(result.RetryRejection);
        Assert.Single(ai.Calls);
        // The bounded prompt actually reached the model:
        Assert.Contains("«Անբան Հուռին»", ai.Calls[0].SystemPrompt);
        Assert.Contains("ANSWER RULES", ai.Calls[0].SystemPrompt);
        Assert.Contains(Story.Segments[3].Text, ai.Calls[0].SystemPrompt);
    }

    [Fact]
    public async Task BadFirstAnswer_RepairsOnce_WithStrictRetryPrompt()
    {
        var ai = new FakeAiClient(BadAnswer, GoodAnswer);
        var service = new LibraryStoryQuestionService(ai);

        var result = await service.AnswerAsync(Story, 3, "Գորտերը իրո՞ք բամբակ մանեցին");

        Assert.Equal(GoodAnswer, result.Text);
        Assert.False(result.UsedFallback);
        Assert.Equal(StoryAnswerRejection.PromptLeakage, result.FirstRejection);
        Assert.Equal(StoryAnswerRejection.None, result.RetryRejection);
        Assert.Equal(2, ai.Calls.Count);
        Assert.DoesNotContain("STRICT RETRY", ai.Calls[0].SystemPrompt);
        Assert.Contains("STRICT RETRY", ai.Calls[1].SystemPrompt);
    }

    [Fact]
    public async Task BadAnswerTwice_ReturnsExactSafeFallback()
    {
        var ai = new FakeAiClient(BadAnswer, "Դու տխմար ես, ամոթ քեզ։");
        var service = new LibraryStoryQuestionService(ai);

        var result = await service.AnswerAsync(Story, 0, "Հուռին վա՞տն էր");

        Assert.Equal(StoryAnswerFilter.SafeFallback, result.Text);
        Assert.True(result.UsedFallback);
        Assert.Equal(StoryAnswerRejection.PromptLeakage, result.FirstRejection);
        Assert.Equal(StoryAnswerRejection.Insult, result.RetryRejection);
        Assert.Equal(2, ai.Calls.Count);
    }

    [Fact]
    public async Task ModelFailure_NeverSurfaces_FallsBackSafely()
    {
        var ai = new FakeAiClient(); // throws on every call
        var service = new LibraryStoryQuestionService(ai);

        var result = await service.AnswerAsync(Story, 0, "Ո՞վ է Հուռին");

        Assert.Equal(StoryAnswerFilter.SafeFallback, result.Text);
        Assert.True(result.UsedFallback);
        Assert.Equal(StoryAnswerRejection.Empty, result.FirstRejection);
    }

    [Fact]
    public async Task OutputSeam_OnlyEverReceivesFilteredText()
    {
        // Whatever the model produces, Text always passes the filter —
        // the TTS-facing seam can never see unfiltered model output.
        string[] hostileOutputs =
        [
            BadAnswer,
            "Իմ system prompt-ը ասում է, որ չպատասխանեմ։",
            "Հրեշը եկավ ու բոլորին վախեցրեց սարսափով։",
            "Խաբելը լավ է, դու էլ խաբիր մայրիկիդ։",
            "",
        ];

        foreach (var hostile in hostileOutputs)
        {
            var service = new LibraryStoryQuestionService(new FakeAiClient(hostile, hostile));
            var result = await service.AnswerAsync(Story, 1, "Ինչո՞ւ");

            Assert.Equal(
                StoryAnswerRejection.None,
                StoryAnswerFilter.Check(result.Text, Story, StoryQuestionGuides.AnbanHuri));
            Assert.Equal(StoryAnswerFilter.SafeFallback, result.Text);
        }
    }

    [Fact]
    public async Task AnsweringAQuestion_NeverMovesTheStoryPosition()
    {
        // Resume contract: the session the tracker holds is identical
        // before and after a Q&A round — playback continues from the
        // exact pre-question segment.
        var tracker = new LibraryStorySessionTracker();
        var conversationId = Guid.NewGuid();
        tracker.Start(conversationId, Story.Id);
        tracker.Advance(conversationId);
        tracker.Advance(conversationId);
        var before = tracker.GetCurrent(conversationId);
        Assert.NotNull(before);
        Assert.Equal(2, before!.SegmentIndex);

        var service = new LibraryStoryQuestionService(new FakeAiClient(GoodAnswer));
        var result = await service.AnswerAsync(Story, before, "Ո՞վ է վաճառականը");

        Assert.False(result.UsedFallback);
        var after = tracker.GetCurrent(conversationId);
        Assert.NotNull(after);
        Assert.Equal(before.SegmentIndex, after!.SegmentIndex);
        Assert.Equal(before.StoryId, after.StoryId);
    }

    [Fact]
    public async Task SessionStoryMismatch_Throws()
    {
        var service = new LibraryStoryQuestionService(new FakeAiClient(GoodAnswer));
        var session = new LibraryStorySession("some-other-story", 0, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AnswerAsync(Story, session, "Ո՞վ է Հուռին"));
    }
}

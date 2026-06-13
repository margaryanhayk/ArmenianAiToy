using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// W1 Library-Story foundation: the deterministic Story:Engine flag,
/// the DI registrations, and the session-lifecycle service boundary
/// (start / current / advance / end / clear). Pins the two W1 safety
/// contracts: legacy is the default and the engine-off service touches
/// nothing; and the runtime library still serves ONLY approved
/// embedded stories — never the anban-huri draft. No live routing is
/// exercised because none exists yet (W2).
/// </summary>
public class LibraryStoryPlaybackServiceTests
{
    private static readonly InMemoryCuratedStoryLibrary Library = new();

    private static LibraryStoryPlaybackService MakeService(
        string engine, out LibraryStorySessionTracker tracker)
    {
        tracker = new LibraryStorySessionTracker();
        return new LibraryStoryPlaybackService(
            Library, tracker, new StoryEngineOptions { Engine = engine });
    }

    // ── Config flag resolution ──────────────────────────────────────

    [Fact]
    public void MissingConfig_ResolvesToLegacy()
    {
        var emptyConfig = new ConfigurationBuilder().Build();

        var options = StoryEngineOptions.FromConfiguration(emptyConfig);

        Assert.Equal(StoryEngineOptions.LegacyEngine, options.Engine);
        Assert.False(options.UseLibraryEngine);
        // Default-constructed options behave identically:
        Assert.False(new StoryEngineOptions().UseLibraryEngine);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("librarry")] // typo'd opt-in must fail safe
    [InlineData("Legacy")]
    public void NonLibraryValues_ResolveToLegacy(string engine)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [StoryEngineOptions.SectionKey] = engine,
            })
            .Build();

        Assert.False(StoryEngineOptions.FromConfiguration(config).UseLibraryEngine);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("Library")]
    [InlineData(" library ")]
    public void ExplicitLibraryValue_EnablesLibraryEngine(string engine)
    {
        Assert.True(new StoryEngineOptions { Engine = engine }.UseLibraryEngine);
    }

    // ── Legacy mode: the engine is structurally inert ───────────────

    [Fact]
    public void LegacyEngine_DisablesAllLibraryBehavior_AndNeverTouchesTracker()
    {
        var service = MakeService("legacy", out var tracker);
        var conversationId = Guid.NewGuid();

        Assert.False(service.IsEngineEnabled);
        Assert.Null(service.Start(conversationId));
        Assert.Null(service.Start(conversationId, InMemoryCuratedStoryLibrary.LittleCloudId));
        Assert.Null(service.GetCurrent(conversationId));
        var advance = service.Advance(conversationId);
        Assert.Null(advance.Next);
        Assert.False(advance.StoryEnded);
        Assert.False(service.Clear(conversationId));

        // No session was ever created behind the scenes:
        Assert.Null(tracker.GetCurrent(conversationId));
    }

    // ── DI: the W1 graph resolves through the real AddInfrastructure ─

    [Fact]
    public void DependencyInjection_ResolvesLibraryTrackerPlaybackAndQuestionService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key-never-called",
                [StoryEngineOptions.SectionKey] = "library",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<StoryEngineOptions>().UseLibraryEngine);
        Assert.IsType<InMemoryCuratedStoryLibrary>(
            provider.GetRequiredService<ICuratedStoryLibrary>());
        Assert.NotNull(provider.GetRequiredService<LibraryStorySessionTracker>());
        Assert.NotNull(provider.GetRequiredService<LibraryStoryPlaybackService>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<LibraryStoryQuestionService>());
    }

    [Fact]
    public void DependencyInjection_DefaultConfig_ResolvesToLegacyEngine()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key-never-called",
                // No Story:Engine key at all — the shipped-default case.
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<StoryEngineOptions>().UseLibraryEngine);
        Assert.False(provider.GetRequiredService<LibraryStoryPlaybackService>().IsEngineEnabled);
    }

    // ── Runtime library posture (approved-only; drafts unreachable) ──

    [Fact]
    public void RuntimeLibrary_ListsOnlyApprovedEmbeddedStories()
    {
        var resourceNames = typeof(CuratedStory).Assembly.GetManifestResourceNames();
        var stories = Library.ListAvailable();

        Assert.NotEmpty(stories);
        Assert.All(stories, story => Assert.Contains(
            $"ArmenianAiToy.Application.Stories.Content.{story.Id}.story.json",
            resourceNames));
    }

    [Fact]
    public void AnbanHuriDraft_IsNotListedOrServed()
    {
        Assert.Null(Library.GetById("anban-huri"));
        Assert.DoesNotContain(Library.ListAvailable(), s => s.Id == "anban-huri");
        // And it cannot be started as a playback session even with the
        // engine on:
        var service = MakeService("library", out _);
        Assert.Null(service.Start(Guid.NewGuid(), "anban-huri"));
    }

    // ── Session lifecycle (engine on, approved story) ───────────────

    [Fact]
    public void Start_StoresStoryIdAndSegmentZero()
    {
        var service = MakeService("library", out var tracker);
        var conversationId = Guid.NewGuid();

        var state = service.Start(conversationId);

        Assert.NotNull(state);
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, state!.StoryId);
        Assert.Equal(0, state.SegmentIndex);

        var session = tracker.GetCurrent(conversationId);
        Assert.NotNull(session);
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, session!.StoryId);
        Assert.Equal(0, session.SegmentIndex);
    }

    [Fact]
    public void GetCurrent_ReturnsExactApprovedSegmentText()
    {
        var service = MakeService("library", out _);
        var conversationId = Guid.NewGuid();
        var story = Library.GetById(InMemoryCuratedStoryLibrary.HedgehogAppleId)!;

        service.Start(conversationId, story.Id);
        var state = service.GetCurrent(conversationId);

        Assert.NotNull(state);
        Assert.Equal(story.Title, state!.StoryTitle);
        Assert.Equal(story.Segments.Count, state.SegmentCount);
        // Byte-identical verbatim contract:
        Assert.Equal(story.Segments[0].Text, state.SegmentText);
    }

    [Fact]
    public void Advance_MovesSegmentIndexByOne_WithExactNextText()
    {
        var service = MakeService("library", out _);
        var conversationId = Guid.NewGuid();
        var story = Library.SelectDefault();

        service.Start(conversationId);
        var result = service.Advance(conversationId);

        Assert.False(result.StoryEnded);
        Assert.NotNull(result.Next);
        Assert.Equal(1, result.Next!.SegmentIndex);
        Assert.Equal(story.Segments[1].Text, result.Next.SegmentText);
    }

    [Fact]
    public void Advance_PastFinalSegment_ReportsEndAndClearsSession()
    {
        var service = MakeService("library", out var tracker);
        var conversationId = Guid.NewGuid();
        var story = Library.SelectDefault();

        var state = service.Start(conversationId)!;
        // Walk to the final segment:
        while (!state.IsFinalSegment)
        {
            var step = service.Advance(conversationId);
            Assert.False(step.StoryEnded);
            state = step.Next!;
        }
        Assert.Equal(story.Segments.Count - 1, state.SegmentIndex);

        // One more advance ends the story and clears the session:
        var end = service.Advance(conversationId);
        Assert.True(end.StoryEnded);
        Assert.Null(end.Next);
        Assert.Null(tracker.GetCurrent(conversationId));
        Assert.Null(service.GetCurrent(conversationId));
    }

    [Fact]
    public void Clear_RemovesSession()
    {
        var service = MakeService("library", out var tracker);
        var conversationId = Guid.NewGuid();

        service.Start(conversationId);
        Assert.True(service.Clear(conversationId));

        Assert.Null(service.GetCurrent(conversationId));
        Assert.Null(tracker.GetCurrent(conversationId));
        // Idempotent second clear:
        Assert.False(service.Clear(conversationId));
    }

    // ── W4: recently-ended marker ───────────────────────────────────

    [Fact]
    public void AdvancePastFinal_SetsRecentlyEndedMarker()
    {
        var service = MakeService("library", out var tracker);
        var conversationId = Guid.NewGuid();

        var state = service.Start(conversationId)!;
        while (!state.IsFinalSegment)
        {
            state = service.Advance(conversationId).Next!;
        }
        Assert.False(service.HasRecentlyEnded(conversationId));

        var end = service.Advance(conversationId);

        Assert.True(end.StoryEnded);
        Assert.True(service.HasRecentlyEnded(conversationId));
        Assert.Equal(
            InMemoryCuratedStoryLibrary.LittleCloudId,
            tracker.GetRecentlyEnded(conversationId)!.StoryId);
    }

    [Fact]
    public void StartingANewStory_ClearsTheEndedMarker()
    {
        var service = MakeService("library", out var tracker);
        var conversationId = Guid.NewGuid();
        tracker.MarkEnded(conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);
        Assert.True(service.HasRecentlyEnded(conversationId));

        service.Start(conversationId);

        Assert.False(service.HasRecentlyEnded(conversationId));
        Assert.Null(tracker.GetRecentlyEnded(conversationId));
    }

    [Fact]
    public void HasRecentlyEnded_IsEngineGated()
    {
        var service = MakeService("legacy", out var tracker);
        var conversationId = Guid.NewGuid();
        tracker.MarkEnded(conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);

        Assert.False(service.HasRecentlyEnded(conversationId));
    }

    [Fact]
    public void EndedMarker_ExpiresAfterThirtyMinutes()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var tracker = new LibraryStorySessionTracker(clock);
        var conversationId = Guid.NewGuid();
        tracker.MarkEnded(conversationId, InMemoryCuratedStoryLibrary.LittleCloudId);

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.NotNull(tracker.GetRecentlyEnded(conversationId));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(tracker.GetRecentlyEnded(conversationId));
    }

    [Fact]
    public void Sessions_AreIsolatedPerConversation()
    {
        var service = MakeService("library", out _);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        service.Start(first, InMemoryCuratedStoryLibrary.LittleCloudId);
        service.Start(second, InMemoryCuratedStoryLibrary.HedgehogAppleId);
        service.Advance(first);

        Assert.Equal(1, service.GetCurrent(first)!.SegmentIndex);
        Assert.Equal(0, service.GetCurrent(second)!.SegmentIndex);
        Assert.Equal(InMemoryCuratedStoryLibrary.HedgehogAppleId,
            service.GetCurrent(second)!.StoryId);
    }
}

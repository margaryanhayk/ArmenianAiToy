using System.ClientModel;
using System.ClientModel.Primitives;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests that the moderation adapter fails closed — when the OpenAI
/// moderation API is unreachable or throws, content is treated as unsafe.
/// Critical safety guarantee for a child-facing product.
///
/// D1 additions cover transient 429 resilience: one retry, no retry on any
/// other exception class. Retry-on-success must not widen what passes
/// moderation — the retry only gives the endpoint one more chance to
/// classify content, it cannot mark unsafe content safe.
/// </summary>
public class ModerationFailClosedTests
{
    [Fact]
    public async Task CheckContentAsync_WhenClientThrows_ReturnsUnsafe()
    {
        // ModerationClient is sealed, so we construct it with invalid credentials.
        // Any call to ClassifyTextAsync will throw (no valid API key / endpoint).
        var client = new global::OpenAI.Moderations.ModerationClient(model: "text-moderation-stable", apiKey: "invalid-key");
        var logger = Substitute.For<ILogger<OpenAIModerationAdapter>>();
        var adapter = new OpenAIModerationAdapter(client, logger);

        var result = await adapter.CheckContentAsync("test content");

        Assert.False(result.IsSafe);
        Assert.Contains("moderation_unavailable", result.FlaggedCategories);
    }

    [Fact]
    public async Task CheckContentAsync_WhenClientThrows_LogsError()
    {
        var client = new global::OpenAI.Moderations.ModerationClient(model: "text-moderation-stable", apiKey: "invalid-key");
        var logger = Substitute.For<ILogger<OpenAIModerationAdapter>>();
        var adapter = new OpenAIModerationAdapter(client, logger);

        await adapter.CheckContentAsync("test content");

        logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Is<Exception>(e => e != null),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // D1: transient 429 resilience + exact retry count contract
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckContentAsync_429ThenSuccess_RetriesExactlyOnce_AndReturnsSafe()
    {
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(429));
        adapter.Responses.Enqueue(() => Task.FromResult(new ModerationResult(true, new List<string>())));

        var result = await adapter.CheckContentAsync("hello");

        Assert.True(result.IsSafe);
        Assert.Empty(result.FlaggedCategories);
        Assert.Equal(2, adapter.CallCount); // exactly one retry after the initial 429
    }

    [Fact]
    public async Task CheckContentAsync_429Twice_FailsClosed()
    {
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(429));
        adapter.Responses.Enqueue(() => throw MakeClientException(429));

        var result = await adapter.CheckContentAsync("hello");

        Assert.False(result.IsSafe);
        Assert.Contains("moderation_unavailable", result.FlaggedCategories);
        Assert.Equal(2, adapter.CallCount);
    }

    [Fact]
    public async Task CheckContentAsync_GenuineFlag_NeverRetries()
    {
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() =>
            Task.FromResult(new ModerationResult(false, new List<string> { "violence" })));

        var result = await adapter.CheckContentAsync("unsafe content");

        Assert.False(result.IsSafe);
        Assert.Contains("violence", result.FlaggedCategories);
        Assert.DoesNotContain("moderation_unavailable", result.FlaggedCategories);
        Assert.Equal(1, adapter.CallCount);
    }

    [Fact]
    public async Task CheckContentAsync_AuthError_DoesNotRetry()
    {
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(401));

        var result = await adapter.CheckContentAsync("hello");

        Assert.False(result.IsSafe);
        Assert.Contains("moderation_unavailable", result.FlaggedCategories);
        Assert.Equal(1, adapter.CallCount);
    }

    [Fact]
    public async Task CheckContentAsync_ServerError_DoesNotRetry()
    {
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(503));

        var result = await adapter.CheckContentAsync("hello");

        Assert.False(result.IsSafe);
        Assert.Contains("moderation_unavailable", result.FlaggedCategories);
        Assert.Equal(1, adapter.CallCount);
    }

    [Fact]
    public async Task CheckContentAsync_GenericException_DoesNotRetry()
    {
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw new InvalidOperationException("boom"));

        var result = await adapter.CheckContentAsync("hello");

        Assert.False(result.IsSafe);
        Assert.Contains("moderation_unavailable", result.FlaggedCategories);
        Assert.Equal(1, adapter.CallCount);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test seam: subclass exposes ClassifyOnceAsync and counts calls.
    // Uses a queue of response factories so each test scripts the sequence.
    // ─────────────────────────────────────────────────────────────────────

    private sealed class StubAdapter : OpenAIModerationAdapter
    {
        public Queue<Func<Task<ModerationResult>>> Responses { get; } = new();
        public int CallCount { get; private set; }

        public StubAdapter()
            : base(new global::OpenAI.Moderations.ModerationClient(model: "stub", apiKey: "stub"),
                   Substitute.For<ILogger<OpenAIModerationAdapter>>())
        { }

        protected override Task<ModerationResult> ClassifyOnceAsync(string content)
        {
            CallCount++;
            if (Responses.Count == 0)
                throw new InvalidOperationException("StubAdapter ran out of scripted responses");
            return Responses.Dequeue()();
        }
    }

    private static ClientResultException MakeClientException(int status)
        => new("HTTP " + status, new FakePipelineResponse(status));

    private sealed class FakePipelineResponse : PipelineResponse
    {
        private readonly int _status;
        public FakePipelineResponse(int status) { _status = status; }
        public override int Status => _status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore => FakePipelineResponseHeaders.Instance;
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
            => new(BinaryData.Empty);
        public override void Dispose() { }
    }

    private sealed class FakePipelineResponseHeaders : PipelineResponseHeaders
    {
        public static readonly FakePipelineResponseHeaders Instance = new();
        public override bool TryGetValue(string name, out string? value) { value = null; return false; }
        public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() { yield break; }
    }

    // ─────────────────────────────────────────────────────────────────────
    // D2: violence false-positive widening — story-trigger-aware ceiling
    // ─────────────────────────────────────────────────────────────────────

    // Subclass proxy that surfaces the protected static helpers without
    // requiring InternalsVisibleTo on the Infrastructure assembly.
    private sealed class OverrideProbe : OpenAIModerationAdapter
    {
        public OverrideProbe()
            : base(new global::OpenAI.Moderations.ModerationClient(model: "stub", apiKey: "stub"),
                   Substitute.For<ILogger<OpenAIModerationAdapter>>())
        { }

        public static bool Decide(string content, bool soleViolence, float score, out string path)
            => ShouldOverrideViolenceBlock(content, soleViolence, score, out path);

        public static bool IsStoryRequest(string content)
            => LooksLikeStoryRequest(content);
    }

    // --- Positive surface (override should fire) ---

    [Fact]
    public void Override_ObservedArmenianCatStoryRequest_FiresViaStoryPath()
    {
        // «պատմիր կատվի մասին», the exact input that D1 smoke observed at 0.4507
        var fired = OverrideProbe.Decide(
            "\u057a\u0561\u057f\u0574\u056b\u0580 \u056f\u0561\u057f\u057e\u056b \u0574\u0561\u057d\u056b\u0576",
            soleViolence: true, score: 0.4507f, out var path);

        Assert.True(fired);
        Assert.Equal("story_request", path);
    }

    [Fact]
    public void Override_ShortArmenianDogStoryRequest_FiresViaStoryPath()
    {
        var fired = OverrideProbe.Decide(
            "\u057a\u0561\u057f\u0574\u056b\u0580 \u0577\u0561\u0576 \u0574\u0561\u057d\u056b\u0576",
            soleViolence: true, score: 0.46f, out var path);

        Assert.True(fired);
        Assert.Equal("story_request", path);
    }

    [Fact]
    public void Override_EnglishStoryRequest_FiresViaStoryPath()
    {
        var fired = OverrideProbe.Decide("tell me a story about a fox",
            soleViolence: true, score: 0.42f, out var path);

        Assert.True(fired);
        Assert.Equal("story_request", path);
    }

    [Fact]
    public void Override_LowScoreArmenianTrigger_FiresViaDefaultPath()
    {
        var fired = OverrideProbe.Decide("\u057a\u0561\u057f\u0574\u056b\u0580",
            soleViolence: true, score: 0.36f, out var path);

        Assert.True(fired);
        Assert.Equal("default", path);
    }

    // --- Negative surface (override must NOT fire) ---

    [Fact]
    public void Override_InputWithViolenceKeyword_Blocked()
    {
        var fired = OverrideProbe.Decide("how to make a bomb",
            soleViolence: true, score: 0.45f, out var path);

        Assert.False(fired);
        Assert.Equal("none", path);
    }

    [Fact]
    public void Override_StoryRequestWithViolenceKeyword_Blocked()
    {
        var fired = OverrideProbe.Decide("\u057a\u0561\u057f\u0574\u056b\u0580 bomb \u0574\u0561\u057d\u056b\u0576",
            soleViolence: true, score: 0.45f, out var path);

        Assert.False(fired);
        Assert.Equal("none", path);
    }

    [Fact]
    public void Override_ScoreAtStoryCeiling_Blocked()
    {
        // Strictly less-than — exactly 0.50 must still block.
        var fired = OverrideProbe.Decide(
            "\u057a\u0561\u057f\u0574\u056b\u0580 \u056f\u0561\u057f\u057e\u056b \u0574\u0561\u057d\u056b\u0576",
            soleViolence: true, score: 0.50f, out var path);

        Assert.False(fired);
        Assert.Equal("none", path);
    }

    [Fact]
    public void Override_ScoreAboveStoryCeiling_Blocked()
    {
        var fired = OverrideProbe.Decide(
            "\u057a\u0561\u057f\u0574\u056b\u0580 \u056f\u0561\u057f\u057e\u056b \u0574\u0561\u057d\u056b\u0576",
            soleViolence: true, score: 0.65f, out var path);

        Assert.False(fired);
        Assert.Equal("none", path);
    }

    [Fact]
    public void Override_BenignNonStoryArmenian_InWidenedBand_Blocked()
    {
        // Proves the widening applies ONLY to story-request patterns. A
        // benign Armenian input without a story marker in the 0.40–0.50
        // band must still block — conservative choice.
        var fired = OverrideProbe.Decide(
            "\u056f\u0561\u057f\u0578\u0582 \u0578\u0582 \u0577\u0578\u0582\u0576", // «կատու ու շուն»
            soleViolence: true, score: 0.45f, out var path);

        Assert.False(fired);
        Assert.Equal("none", path);
    }

    [Fact]
    public void Override_MultiCategoryFlag_Blocked()
    {
        // Multi-category flag always blocks — unchanged by D2.
        var fired = OverrideProbe.Decide("anything",
            soleViolence: false, score: 0.30f, out var path);

        Assert.False(fired);
        Assert.Equal("none", path);
    }

    // --- Story-marker helper coverage ---

    [Theory]
    [InlineData("\u057a\u0561\u057f\u0574\u056b\u0580 \u056f\u0561\u057f\u057e\u056b \u0574\u0561\u057d\u056b\u0576", true)]  // «պատմիր կատվի մասին»
    [InlineData("tell me a story about a star", true)]
    [InlineData("\u0540\u0535\u0554\u053b\u0531\u0539", true)]                                                                // ՀԵՔԻԱԹ (uppercase)
    [InlineData("how to make a bomb", false)]
    [InlineData("\u056f\u0561\u057f\u0578\u0582", false)]                                                                     // «կատու»
    public void LooksLikeStoryRequest_MatchesExpected(string content, bool expected)
    {
        Assert.Equal(expected, OverrideProbe.IsStoryRequest(content));
    }
}

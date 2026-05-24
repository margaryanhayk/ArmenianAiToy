using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.Metrics;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Serial xUnit collection for metric-capture tests. The
/// <see cref="MeterListener"/> API is process-wide; tests in this
/// collection must run one at a time so a concurrent counter
/// increment can never pollute another test's captured measurements.
/// </summary>
[CollectionDefinition(nameof(ModerationFailClosedMetricsCollection),
    DisableParallelization = true)]
public class ModerationFailClosedMetricsCollection { }

/// <summary>
/// Tests for the <c>aat_moderation_failclosed_total</c> counter — the
/// observability-only signal introduced after the 2026-05-24 moderation
/// fallback diagnosis (OpenAI billing quota exhaustion produced
/// persistent 429s, with no in-process metric that would have surfaced
/// the cause without an out-of-band probe).
///
/// MOCKING STRATEGY mirrors <see cref="ModerationFailClosedTests"/>:
/// subclass <see cref="OpenAIModerationAdapter"/>, override
/// <c>ClassifyOnceAsync</c>, script a sequence of responses. Tests do
/// NOT touch the network.
///
/// METRIC CAPTURE uses a private <see cref="MeterListener"/> filtered
/// to the <c>ArmenianAiToy</c> meter + the
/// <c>aat_moderation_failclosed_total</c> instrument. The
/// <see cref="MeterListener"/> API is PROCESS-WIDE — every listener
/// on the meter sees every increment regardless of which test thread
/// triggered it. The
/// <see cref="CollectionDefinition"/> below opts this entire class
/// into a serial xUnit collection (<c>DisableParallelization=true</c>)
/// so concurrent tests cannot leak counter increments into each
/// other's captured measurements.
/// </summary>
[Collection(nameof(ModerationFailClosedMetricsCollection))]
public class ModerationFailClosedMetricsTests
{
    // ─────────────────────────────────────────────────────────────────
    // Reason normalization (closed enum + Unknown fallback)
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("rate_limited_retry_failed")]
    [InlineData("auth_error")]
    [InlineData("server_error")]
    [InlineData("timeout")]
    [InlineData("network_error")]
    [InlineData("parse_error")]
    public void Normalize_KnownReason_PassesThrough(string reason)
    {
        Assert.Equal(reason, ModerationFailClosedReason.Normalize(reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something_else")]
    [InlineData("RATE_LIMITED_RETRY_FAILED")] // case-sensitive — must NOT pass through
    [InlineData("rate-limited-retry-failed")] // hyphen vs underscore — must NOT pass through
    public void Normalize_UnknownOrEmptyOrCaseMismatch_BecomesUnknown(string? input)
    {
        Assert.Equal("unknown", ModerationFailClosedReason.Normalize(input));
    }

    [Fact]
    public void Normalize_UnknownConstant_IsItselfMapped()
    {
        // "unknown" is the explicit fallback, so passing it in should
        // return it unchanged — covers the round-trip property.
        Assert.Equal("unknown", ModerationFailClosedReason.Normalize("unknown"));
    }

    // ─────────────────────────────────────────────────────────────────
    // Each failure class triggers EXACTLY ONE increment with the
    // expected reason tag.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimitedRetryFailed_TwoConsecutive429s_IncrementsOnce()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(429));
        adapter.Responses.Enqueue(() => throw MakeClientException(429));

        await adapter.CheckContentAsync("hello");

        capture.AssertSingleIncrement("rate_limited_retry_failed");
    }

    [Fact]
    public async Task AuthError_401_IncrementsAuthErrorOnce()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(401));

        await adapter.CheckContentAsync("hello");

        capture.AssertSingleIncrement("auth_error");
    }

    [Fact]
    public async Task AuthError_403_IncrementsAuthErrorOnce()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(403));

        await adapter.CheckContentAsync("hello");

        capture.AssertSingleIncrement("auth_error");
    }

    [Fact]
    public async Task ServerError_503_IncrementsServerErrorOnce()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(503));

        await adapter.CheckContentAsync("hello");

        capture.AssertSingleIncrement("server_error");
    }

    [Fact]
    public async Task Timeout_OperationCanceled_IncrementsTimeoutOnce()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw new OperationCanceledException("simulated cts cancel"));

        await adapter.CheckContentAsync("hello");

        capture.AssertSingleIncrement("timeout");
    }

    [Fact]
    public async Task NetworkError_GenericException_IncrementsNetworkErrorOnce()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw new InvalidOperationException("simulated transport boom"));

        await adapter.CheckContentAsync("hello");

        capture.AssertSingleIncrement("network_error");
    }

    // ─────────────────────────────────────────────────────────────────
    // Negative space — the counter is for fail-closed ONLY.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulClassify_DoesNotIncrement()
    {
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() =>
            Task.FromResult(new ModerationResult(true, new List<string>())));

        await adapter.CheckContentAsync("hello");

        Assert.Empty(capture.Measurements);
    }

    [Fact]
    public async Task GenuineContentFlag_DoesNotIncrement()
    {
        // A real content flag (violence/sexual/etc) exits via the happy
        // path with IsSafe=false — it must NOT touch the fail-closed
        // counter. This is the load-bearing invariant: a Prometheus
        // alert on aat_moderation_failclosed_total firing must
        // unambiguously mean "moderation is broken", not "kids are
        // saying unsafe things".
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() =>
            Task.FromResult(new ModerationResult(false, new List<string> { "violence" })));

        await adapter.CheckContentAsync("unsafe content");

        Assert.Empty(capture.Measurements);
    }

    [Fact]
    public async Task TransientRetryRecovers_DoesNotIncrement()
    {
        // 429 → retry → success path. The transient 429 was already
        // swallowed by the adapter's design; the FINAL outcome is a
        // safe classification, so the counter must not fire.
        // Together with the rate_limited_retry_failed test above this
        // proves the "single increment per outer call, on the FINAL
        // outcome only" contract.
        using var capture = MetricCapture.Start();
        var adapter = new StubAdapter();
        adapter.Responses.Enqueue(() => throw MakeClientException(429));
        adapter.Responses.Enqueue(() =>
            Task.FromResult(new ModerationResult(true, new List<string>())));

        await adapter.CheckContentAsync("hello");

        Assert.Empty(capture.Measurements);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test infrastructure — MeterListener filtered to the one
    // instrument we care about, plus a re-used StubAdapter mirroring
    // the one in ModerationFailClosedTests.cs.
    // ─────────────────────────────────────────────────────────────────

    private sealed class MetricCapture : IDisposable
    {
        public List<(long Value, string? Reason)> Measurements { get; } = new();
        private readonly MeterListener _listener;

        private MetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == AppMeter.Name
                        && instrument.Name == "aat_moderation_failclosed_total")
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                string? reason = null;
                for (int i = 0; i < tags.Length; i++)
                {
                    var tag = tags[i];
                    if (tag.Key == "reason")
                    {
                        reason = tag.Value as string;
                        break;
                    }
                }
                lock (Measurements) { Measurements.Add((measurement, reason)); }
            });
            _listener.Start();
        }

        public static MetricCapture Start() => new();

        public void AssertSingleIncrement(string expectedReason)
        {
            Assert.Single(Measurements);
            Assert.Equal(1, Measurements[0].Value);
            Assert.Equal(expectedReason, Measurements[0].Reason);
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class StubAdapter : OpenAIModerationAdapter
    {
        public Queue<Func<Task<ModerationResult>>> Responses { get; } = new();
        public int CallCount { get; private set; }
        public ILogger<OpenAIModerationAdapter> Logger { get; }

        public StubAdapter()
            : this(Substitute.For<ILogger<OpenAIModerationAdapter>>())
        { }

        private StubAdapter(ILogger<OpenAIModerationAdapter> logger)
            : base(new global::OpenAI.Moderations.ModerationClient(model: "stub", apiKey: "stub"), logger)
        {
            Logger = logger;
        }

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
}

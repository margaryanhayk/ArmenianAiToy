using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.Metrics;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>OpenAIReliabilityGate</c> — retry policy + minimal circuit
/// breaker. Uses <c>FakeTimeProvider</c> for the clock and a no-op delay
/// to drive the breaker's windows and backoff without real sleeps.
/// </summary>
public class OpenAIReliabilityGateTests
{
    private static OpenAIReliabilityGate CreateGate(FakeTimeProvider clock)
    {
        var logger = Substitute.For<ILogger<OpenAIReliabilityGate>>();
        return new OpenAIReliabilityGate(
            clock,
            // no-op delay: tests drive time via FakeTimeProvider.Advance
            delay: (_, _) => Task.CompletedTask,
            logger);
    }

    private static ClientResultException Http(int status) =>
        new("HTTP " + status, new FakePipelineResponse(status));

    // ---------- retry behavior ----------

    [Fact]
    public async Task RetryableFailureThenSuccess_ReturnsFinalResult()
    {
        var clock = new FakeTimeProvider();
        var gate = CreateGate(clock);
        int calls = 0;

        var result = await gate.RunAsync<string>(ct =>
        {
            calls++;
            if (calls == 1) throw Http(429);
            return Task.FromResult("ok");
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NonRetryableFailure_DoesNotRetry()
    {
        var clock = new FakeTimeProvider();
        var gate = CreateGate(clock);
        int calls = 0;

        await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            await gate.RunAsync<string>(ct =>
            {
                calls++;
                throw Http(401); // AuthFailure — never retried
            }, CancellationToken.None);
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OtherException_DoesNotRetry()
    {
        var clock = new FakeTimeProvider();
        var gate = CreateGate(clock);
        int calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await gate.RunAsync<string>(ct =>
            {
                calls++;
                throw new InvalidOperationException("boom");
            }, CancellationToken.None);
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetryExhausted_FinalFailurePropagates()
    {
        var clock = new FakeTimeProvider();
        var gate = CreateGate(clock);
        int calls = 0;

        await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            await gate.RunAsync<string>(ct =>
            {
                calls++;
                throw Http(503); // UpstreamServerError — retries once
            }, CancellationToken.None);
        });

        // One initial + one retry = 2 calls total; MaxAttempts is 2.
        Assert.Equal(2, calls);
    }

    // ---------- circuit-breaker behavior ----------

    [Fact]
    public async Task CircuitTripsOpen_AfterThreshold_ShortCircuitsNextCalls()
    {
        var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var gate = CreateGate(clock);
        int innerCalls = 0;

        // Each RunAsync call issues 1 initial + 1 retry = 2 inner calls
        // on a retryable 5xx. Five RunAsync calls therefore produce 5
        // final failures (satisfies TripThreshold=5 within 30s).
        // That's 10 inner call invocations in total before the breaker
        // is tripped by the fifth RunAsync.
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gate.RunAsync<string>(_ =>
                {
                    innerCalls++;
                    throw Http(503);
                }, CancellationToken.None));
        }

        // Sixth call should be short-circuited — the inner lambda must
        // not run.
        int innerCallsAtTrip = innerCalls;
        await Assert.ThrowsAsync<OpenAIReliabilityCircuitOpenException>(async () =>
            await gate.RunAsync<string>(_ =>
            {
                innerCalls++;
                return Task.FromResult("ok");
            }, CancellationToken.None));

        Assert.Equal(innerCallsAtTrip, innerCalls);
    }

    [Fact]
    public async Task CircuitHalfOpenProbeSuccess_ClosesBreaker()
    {
        var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var gate = CreateGate(clock);

        // Trip the breaker.
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gate.RunAsync<string>(_ => throw Http(503), CancellationToken.None));
        }

        // Advance past the 60-second open duration.
        clock.Advance(TimeSpan.FromSeconds(61));

        // Next call is the probe — let it succeed.
        var result = await gate.RunAsync<string>(
            _ => Task.FromResult("recovered"), CancellationToken.None);
        Assert.Equal("recovered", result);

        // Subsequent call should proceed (breaker closed). The old
        // failure window has also been cleared, so a new transient
        // failure should not immediately re-trip the breaker.
        int calls = 0;
        var followup = await gate.RunAsync<string>(_ =>
        {
            calls++;
            return Task.FromResult("still fine");
        }, CancellationToken.None);
        Assert.Equal("still fine", followup);
        Assert.Equal(1, calls); // no retry because no failure
    }

    [Fact]
    public async Task CircuitHalfOpenProbeFailure_ReopensBreaker()
    {
        var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var gate = CreateGate(clock);

        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gate.RunAsync<string>(_ => throw Http(503), CancellationToken.None));
        }

        clock.Advance(TimeSpan.FromSeconds(61));

        // Probe fails — breaker reopens.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await gate.RunAsync<string>(_ => throw Http(503), CancellationToken.None));

        // Next call should short-circuit again (probe failed, open again).
        int calls = 0;
        await Assert.ThrowsAsync<OpenAIReliabilityCircuitOpenException>(async () =>
            await gate.RunAsync<string>(_ =>
            {
                calls++;
                return Task.FromResult("x");
            }, CancellationToken.None));
        Assert.Equal(0, calls);
    }

    // ---------- #070: passive IsCircuitOpen() readiness snapshot ----------

    [Fact]
    public void IsCircuitOpen_FreshGate_ReturnsFalse()
    {
        var gate = CreateGate(new FakeTimeProvider());
        Assert.False(gate.IsCircuitOpen());
    }

    [Fact]
    public async Task IsCircuitOpen_TrueWhileOpen_FalseAfterOpenDurationElapses()
    {
        var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var gate = CreateGate(clock);

        // Trip the breaker (5 final failures within the 30s window).
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gate.RunAsync<string>(_ => throw Http(503), CancellationToken.None));
        }

        // Open now -> degraded.
        Assert.True(gate.IsCircuitOpen());

        // Once the open duration elapses the gate is willing to half-open
        // probe, so it no longer reports degraded (a read-only snapshot
        // never mutates state — confirmed by the next RunAsync still probing).
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(gate.IsCircuitOpen());
    }

    [Fact]
    public async Task IsCircuitOpen_DoesNotMutateState_RunAsyncStillProbesAfterOpen()
    {
        // A read accessor must not consume the half-open probe slot. After
        // open elapses, IsCircuitOpen() reads false, and the NEXT RunAsync is
        // still allowed to run as the probe (inner lambda executes).
        var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var gate = CreateGate(clock);

        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gate.RunAsync<string>(_ => throw Http(503), CancellationToken.None));
        }
        clock.Advance(TimeSpan.FromSeconds(61));

        // Multiple reads must not burn the probe.
        Assert.False(gate.IsCircuitOpen());
        Assert.False(gate.IsCircuitOpen());

        int innerCalls = 0;
        var result = await gate.RunAsync<string>(_ =>
        {
            innerCalls++;
            return Task.FromResult("recovered");
        }, CancellationToken.None);

        Assert.Equal("recovered", result);
        Assert.Equal(1, innerCalls); // probe ran — the reads did not consume it
    }

    // ---------- AI logic review 2026-10-27, C2: caller gone is not an upstream failure ----------

    private static OpenAIReliabilityGate CreateGate(FakeTimeProvider clock, string provider)
    {
        var logger = Substitute.For<ILogger<OpenAIReliabilityGate>>();
        return new OpenAIReliabilityGate(clock, (_, _) => Task.CompletedTask, logger, provider);
    }

    // The MeterListener API is process-wide; filtering on a unique `provider`
    // tag keeps this test immune to other classes' gates incrementing the same
    // counter in parallel.
    private static List<string> CaptureFailureKinds(string provider, out MeterListener listener)
    {
        var kinds = new List<string>();
        var l = new MeterListener
        {
            InstrumentPublished = (instrument, ml) =>
            {
                if (instrument.Meter.Name == AppMeter.Name
                    && instrument.Name == "aat_chat_openai_failure_total")
                    ml.EnableMeasurementEvents(instrument);
            }
        };
        l.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            string? p = null, kind = null;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Key == "provider") p = tags[i].Value as string;
                else if (tags[i].Key == "kind") kind = tags[i].Value as string;
            }
            if (p == provider) lock (kinds) { kinds.Add(kind ?? "?"); }
        });
        l.Start();
        listener = l;
        return kinds;
    }

    [Fact]
    public async Task CallerCancelled_FiveTimes_DoesNotCountAsFailure_NorTripBreaker()
    {
        var provider = "c2-" + Guid.NewGuid().ToString("N");
        var clock = new FakeTimeProvider();
        var gate = CreateGate(clock, provider);
        var kinds = CaptureFailureKinds(provider, out var listener);
        using var _l = listener;

        // A toy that dropped mid-turn: the caller's token is cancelled and the
        // SDK call surfaces an OperationCanceledException carrying it.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await gate.RunAsync<string>(
                    ct => throw new OperationCanceledException(ct), cts.Token));
        }

        // Healthy upstream: the sixth (healthy) call must not be short-circuited.
        var result = await gate.RunAsync<string>(_ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.False(gate.IsCircuitOpen());
        lock (kinds) { Assert.Empty(kinds); }
    }

    [Fact]
    public async Task CallerCancelledDuringHalfOpenProbe_FreesTheProbeSlot_DoesNotReopen()
    {
        var clock = new FakeTimeProvider(startDateTime: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var gate = CreateGate(clock);

        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gate.RunAsync<string>(_ => throw Http(503), CancellationToken.None));
        }
        clock.Advance(TimeSpan.FromSeconds(61));

        // The probe's caller drops mid-flight: not a verdict on the upstream.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gate.RunAsync<string>(ct => throw new OperationCanceledException(ct), cts.Token));

        // The very next call is allowed to probe (slot freed, breaker NOT reopened for 60 s).
        int calls = 0;
        var result = await gate.RunAsync<string>(_ =>
        {
            calls++;
            return Task.FromResult("recovered");
        }, CancellationToken.None);

        Assert.Equal("recovered", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OperationCanceled_WithoutCallerCancellation_StillCountsAsTimeout()
    {
        // The adapter's own 30 s ceiling surfaces as an OCE while the caller's
        // token is NOT cancelled — that is a real upstream timeout and must
        // keep counting (and retrying) exactly as before.
        var provider = "c2-" + Guid.NewGuid().ToString("N");
        var clock = new FakeTimeProvider();
        var gate = CreateGate(clock, provider);
        var kinds = CaptureFailureKinds(provider, out var listener);
        using var _l = listener;
        int calls = 0;

        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await gate.RunAsync<string>(_ =>
                {
                    calls++;
                    throw new OperationCanceledException("adapter 30 s ceiling");
                }, CancellationToken.None));
        }

        Assert.Equal(10, calls); // Timeout is retryable: 5 calls x 2 attempts
        lock (kinds) { Assert.Equal(10, kinds.Count(k => k == "timeout")); }
        await Assert.ThrowsAsync<OpenAIReliabilityCircuitOpenException>(async () =>
            await gate.RunAsync<string>(_ => Task.FromResult("x"), CancellationToken.None));
    }

    // ---------- test helpers ----------

    private sealed class FakePipelineResponse : PipelineResponse
    {
        private readonly int _status;
        public FakePipelineResponse(int status) { _status = status; }
        public override int Status => _status;
        public override string ReasonPhrase => "";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore => FakeHeaders.Instance;
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
            => new(BinaryData.Empty);
        public override void Dispose() { }
    }

    private sealed class FakeHeaders : PipelineResponseHeaders
    {
        public static readonly FakeHeaders Instance = new();
        public override bool TryGetValue(string name, out string? value) { value = null; return false; }
        public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() { yield break; }
    }
}

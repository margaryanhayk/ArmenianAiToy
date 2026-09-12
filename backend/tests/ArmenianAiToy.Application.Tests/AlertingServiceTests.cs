using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Serial xUnit collection for metric-capture tests — same reasoning as
/// <see cref="ModerationFailClosedMetricsCollection"/>: the
/// <see cref="System.Diagnostics.Metrics.MeterListener"/> API is
/// process-wide, so <see cref="AlertingService"/>'s own listener must not
/// run concurrently with anything else exercising the same counters.
/// </summary>
[CollectionDefinition(nameof(AlertingServiceMetricsCollection), DisableParallelization = true)]
public class AlertingServiceMetricsCollection { }

/// <summary>
/// Opt-in webhook alerter (2026-09-12, N5). Keystones: fully inert with an
/// empty webhook URL (including draining the metric backlog so re-enabling
/// later cannot flood); a health transition alerts exactly once and
/// recovers exactly once; the per-key cooldown suppresses a repeat within
/// the window; the cost-cap-trips-per-hour threshold and the two
/// event-driven metric signals (circuit open, moderation fail-closed) fire
/// correctly off the real <see cref="AppMeter"/> counters via a
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>; the payload
/// shape is the documented <c>{ text, key, severity, at }</c>; a webhook
/// failure is logged and swallowed, never thrown.
/// </summary>
[Collection(nameof(AlertingServiceMetricsCollection))]
public class AlertingServiceTests : IDisposable
{
    private readonly string _root;
    private readonly List<AlertingService> _services = new();

    public AlertingServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "areg-alerting-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // The MeterListener API is process-wide (see the class xmldoc) —
        // stop every listener this test started so it cannot keep
        // observing later tests' counter increments.
        foreach (var service in _services)
        {
            service.StopMeterListenerForTests();
        }
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();
        public HttpStatusCode ResponseStatus = HttpStatusCode.OK;
        public Exception? ThrowOnSend;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (ThrowOnSend is not null) throw ThrowOnSend;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body);
            return new HttpResponseMessage(ResponseStatus) { Content = new StringContent("") };
        }
    }

    private (AlertingService Service, RecordingHandler Handler, IConfigurationRoot Config, ServiceProvider Provider)
        MakeHarness(Dictionary<string, string?>? extraConfig = null, bool freshBackup = true,
            HttpStatusCode responseStatus = HttpStatusCode.OK)
    {
        var dbPath = Path.Combine(_root, "live.db");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        if (freshBackup)
        {
            // Avoid the backup-staleness signal firing incidentally in
            // tests that are not about backups: leave a current-looking
            // snapshot beside the live DB, same directory
            // AlertingService.ResolveBackupDirectory derives.
            var backupsDir = Path.Combine(_root, "backups");
            Directory.CreateDirectory(backupsDir);
            File.WriteAllText(
                Path.Combine(backupsDir, $"{DatabaseBackupService.FilePrefix}{DateTime.UtcNow:yyyyMMdd}.db"),
                "not a real db, just a fresh mtime");
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(extraConfig ?? new Dictionary<string, string?>())
            .Build();

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);

        var handler = new RecordingHandler { ResponseStatus = responseStatus };
        var httpClient = new HttpClient(handler);

        var service = new AlertingService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            env,
            Substitute.For<ILogger<AlertingService>>(),
            httpClient);

        _services.Add(service);
        return (service, handler, config, provider);
    }

    private static Dictionary<string, string?> BaseConfig(string? url = "https://example.test/hook")
        => new()
        {
            ["Alerts:WebhookUrl"] = url,
            ["Alerts:CooldownMinutes"] = "30",
            ["Alerts:HealthCheckIntervalSeconds"] = "60",
            ["Alerts:CostCapTripsThresholdPerHour"] = "5",
        };

    // ── Disabled by default ──────────────────────────────────────────

    [Fact]
    public async Task RunTick_WebhookUrlEmpty_NeverPostsAnything()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig(url: ""));
        using var _p = provider;

        await service.RunTickAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task RunTick_WebhookUrlWhitespace_TreatedAsDisabled()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig(url: "   "));
        using var _p = provider;

        await service.RunTickAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task RunTick_DisabledThenEnabled_DrainsMetricBacklog_NoFloodOnEnable()
    {
        var (service, handler, config, provider) = MakeHarness(BaseConfig(url: ""));
        using var _p = provider;
        service.StartMeterListenerForTests();

        // 10 trips while disabled — well over the threshold of 5.
        for (var i = 0; i < 10; i++)
        {
            AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "chat"));
        }
        await service.RunTickAsync(CancellationToken.None);
        Assert.Empty(handler.Bodies);

        config["Alerts:WebhookUrl"] = "https://example.test/hook";
        await service.RunTickAsync(CancellationToken.None);

        Assert.DoesNotContain(handler.Bodies, b => b.Contains("cost_cap_trips_high"));
    }

    // ── Health transition: fires once, recovers once ─────────────────

    [Fact]
    public async Task HealthTransition_UnhealthyThenRecovered_EachFiresExactlyOnce()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;

        var sequence = new Queue<bool>(new[] { true, false, false, true, true });
        service.DbHealthCheckOverride = (_, _, _) =>
            Task.FromResult(sequence.Count > 0 ? sequence.Dequeue() : true);

        await service.RunTickAsync(CancellationToken.None); // true: establishes baseline, no alert
        Assert.DoesNotContain(handler.Bodies, b => b.Contains("health_db"));

        await service.RunTickAsync(CancellationToken.None); // false: healthy -> unhealthy
        Assert.Single(handler.Bodies, b => b.Contains("health_db_unhealthy"));

        await service.RunTickAsync(CancellationToken.None); // false: no transition
        Assert.Single(handler.Bodies, b => b.Contains("health_db_unhealthy"));

        await service.RunTickAsync(CancellationToken.None); // true: unhealthy -> healthy
        Assert.Single(handler.Bodies, b => b.Contains("health_db_recovered"));

        await service.RunTickAsync(CancellationToken.None); // true: no transition
        Assert.Single(handler.Bodies, b => b.Contains("health_db_recovered"));
    }

    // ── Cooldown ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendAlert_SecondCallWithinCooldown_IsSuppressed()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;

        await service.SendAlertAsync("https://example.test/hook", "test_key", "warning", "first", CancellationToken.None);
        await service.SendAlertAsync("https://example.test/hook", "test_key", "warning", "second", CancellationToken.None);

        Assert.Single(handler.Bodies);
        Assert.Contains("first", handler.Bodies[0]);
    }

    [Fact]
    public async Task SendAlert_CooldownZero_DoesNotSuppress()
    {
        var config = BaseConfig();
        config["Alerts:CooldownMinutes"] = "0";
        var (service, handler, _, provider) = MakeHarness(config);
        using var _p = provider;

        await service.SendAlertAsync("https://example.test/hook", "test_key", "warning", "first", CancellationToken.None);
        await service.SendAlertAsync("https://example.test/hook", "test_key", "warning", "second", CancellationToken.None);

        Assert.Equal(2, handler.Bodies.Count);
    }

    [Fact]
    public async Task SendAlert_DifferentKeys_CooldownIsPerKey()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;

        await service.SendAlertAsync("https://example.test/hook", "key_a", "warning", "a", CancellationToken.None);
        await service.SendAlertAsync("https://example.test/hook", "key_b", "warning", "b", CancellationToken.None);

        Assert.Equal(2, handler.Bodies.Count);
    }

    // ── Threshold logic: cost-cap trips per hour ─────────────────────

    [Fact]
    public async Task CostCapTrips_AboveThreshold_Alerts()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.StartMeterListenerForTests();

        for (var i = 0; i < 6; i++) // threshold is 5
        {
            AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "chat"));
        }

        await service.RunTickAsync(CancellationToken.None);

        Assert.Contains(handler.Bodies, b => b.Contains("cost_cap_trips_high"));
    }

    [Fact]
    public async Task CostCapTrips_AtOrBelowThreshold_DoesNotAlert()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.StartMeterListenerForTests();

        for (var i = 0; i < 3; i++) // threshold is 5
        {
            AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "chat"));
        }

        await service.RunTickAsync(CancellationToken.None);

        Assert.DoesNotContain(handler.Bodies, b => b.Contains("cost_cap_trips_high"));
    }

    [Fact]
    public async Task CostCapTrips_SecondTickWithinCooldown_DoesNotRepeat()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.StartMeterListenerForTests();

        for (var i = 0; i < 6; i++)
        {
            AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "chat"));
        }

        await service.RunTickAsync(CancellationToken.None);
        await service.RunTickAsync(CancellationToken.None);

        Assert.Single(handler.Bodies, b => b.Contains("cost_cap_trips_high"));
    }

    // ── Event-driven metric signals ──────────────────────────────────

    [Fact]
    public async Task CircuitTrip_Increment_Alerts()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.StartMeterListenerForTests();

        AppMeter.ChatOpenAICircuitTrip.Add(1);
        await service.RunTickAsync(CancellationToken.None);

        Assert.Contains(handler.Bodies, b => b.Contains("openai_circuit_open"));
    }

    [Fact]
    public async Task ModerationFailClosed_Increment_Alerts()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.StartMeterListenerForTests();

        AppMeter.ModerationFailClosed.Add(1, new KeyValuePair<string, object?>("reason", "timeout"));
        await service.RunTickAsync(CancellationToken.None);

        Assert.Contains(handler.Bodies, b => b.Contains("moderation_unavailable"));
    }

    [Fact]
    public async Task UnrelatedCounter_DoesNotAlert()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.StartMeterListenerForTests();

        AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "paused"));
        await service.RunTickAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    // ── Backup staleness ──────────────────────────────────────────────

    [Fact]
    public async Task Backup_NoSnapshotDirectory_Alerts()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig(), freshBackup: false);
        using var _p = provider;
        service.DbHealthCheckOverride = (_, _, _) => Task.FromResult(true);

        await service.RunTickAsync(CancellationToken.None);

        Assert.Contains(handler.Bodies, b => b.Contains("backup_stale"));
    }

    [Fact]
    public async Task Backup_FreshSnapshot_DoesNotAlert()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig(), freshBackup: true);
        using var _p = provider;
        service.DbHealthCheckOverride = (_, _, _) => Task.FromResult(true);

        await service.RunTickAsync(CancellationToken.None);

        Assert.DoesNotContain(handler.Bodies, b => b.Contains("backup_stale"));
    }

    [Fact]
    public async Task Backup_StaleSnapshot_Alerts()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig(), freshBackup: false);
        using var _p = provider;
        service.DbHealthCheckOverride = (_, _, _) => Task.FromResult(true);

        var backupsDir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(backupsDir);
        var stalePath = Path.Combine(backupsDir, $"{DatabaseBackupService.FilePrefix}20200101.db");
        File.WriteAllText(stalePath, "old");
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow - TimeSpan.FromHours(48));

        await service.RunTickAsync(CancellationToken.None);

        Assert.Contains(handler.Bodies, b => b.Contains("backup_stale"));
    }

    // ── Payload shape ─────────────────────────────────────────────────

    [Fact]
    public async Task SendAlert_PayloadShape_MatchesDocumentedContract()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;

        await service.SendAlertAsync("https://example.test/hook", "some_key", "critical", "hello world", CancellationToken.None);

        Assert.Single(handler.Bodies);
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var root = doc.RootElement;
        Assert.Equal("hello world", root.GetProperty("text").GetString());
        Assert.Equal("some_key", root.GetProperty("key").GetString());
        Assert.Equal("critical", root.GetProperty("severity").GetString());
        var at = root.GetProperty("at").GetString();
        Assert.NotNull(at);
        Assert.True(DateTime.TryParse(at, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out _));
        Assert.EndsWith("Z", at);
    }

    // ── Webhook failure never throws ──────────────────────────────────

    [Fact]
    public async Task SendAlert_WebhookThrows_DoesNotThrow_LogsWarning()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        handler.ThrowOnSend = new HttpRequestException("connection refused");

        var ex = await Record.ExceptionAsync(() =>
            service.SendAlertAsync("https://example.test/hook", "some_key", "warning", "text", CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SendAlert_WebhookReturnsServerError_DoesNotThrow_NotCountedAsSent()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig(), responseStatus: HttpStatusCode.InternalServerError);
        using var _p = provider;

        var ex = await Record.ExceptionAsync(() =>
            service.SendAlertAsync("https://example.test/hook", "some_key", "warning", "text", CancellationToken.None));

        Assert.Null(ex);
        // Two attempts (initial + one retry), both failing.
        Assert.Equal(2, handler.Bodies.Count);
    }

    [Fact]
    public async Task RunTick_DoesNotThrow_WhenDbUnreachableAndWebhookConfigured()
    {
        var (service, handler, _, provider) = MakeHarness(BaseConfig());
        using var _p = provider;
        service.DbHealthCheckOverride = (_, _, _) => Task.FromResult(false);

        var ex = await Record.ExceptionAsync(() => service.RunTickAsync(CancellationToken.None));

        Assert.Null(ex);
    }
}

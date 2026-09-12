using System;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Background;

/// <summary>
/// Opt-in webhook alerter (2026-09-12, N5). Until this slice nothing told
/// anyone when the service went unhealthy, when a device's daily cost cap
/// tripped repeatedly, when the OpenAI circuit breaker opened, or when
/// moderation started fail-closing — <c>/metrics</c> existed but nothing
/// read it. Same catch-and-continue-per-tick idiom as
/// <see cref="RetentionPurgeService"/> / <see cref="DatabaseBackupService"/>:
/// a tick failure is logged and swallowed, never crashes the host.
///
/// <para>
/// <b>Fully disabled by default.</b> <c>Alerts:WebhookUrl</c> empty (the
/// shipped default) means every tick is a no-op, including the
/// <see cref="MeterListener"/> — it is started regardless (cheap, no
/// network) but nothing it observes is ever posted anywhere while the URL
/// is empty. Same opt-in posture as everything else that could leak
/// operational detail off the box.
/// </para>
///
/// <para><b>Signals:</b></para>
/// <list type="bullet">
///   <item><description><b>Health</b> — DB liveness (the same one-line
///   <c>CanConnectAsync</c> probe <c>ArmenianAiToy.Api.Health.HealthProbe</c>
///   and <c>GET /api/health</c> use, replicated here rather than
///   referenced: the Api project references Infrastructure, not the other
///   way around, so a shared type would need moving to Application — a
///   bigger change than this slice's scope) plus the <c>audioStore</c>
///   configured state (same fail-closed rule as
///   <c>ArmenianAiToy.Api.Security.AudioBlobStoreRootResolver</c>,
///   replicated for the same project-direction reason — see
///   <see cref="IsAudioStoreConfigured"/>). Alerts on healthy→unhealthy and
///   again on the unhealthy→healthy recovery; same shape for the audio
///   store.</description></item>
///   <item><description><b>Metrics</b> — a <see cref="MeterListener"/>
///   (BCL, no package) observes three existing <see cref="AppMeter"/>
///   counters by name: <c>aat_openai_cost_cap_trip_total</c> (alerts when
///   the trip count in the last rolling hour exceeds
///   <c>Alerts:CostCapTripsThresholdPerHour</c>),
///   <c>aat_chat_openai_circuit_trip_total</c> (each increment IS a
///   closed→open transition — alerts immediately, cooldown-gated so a
///   flapping breaker cannot spam), and <c>aat_moderation_failclosed_total</c>
///   (alerts on any fail-closed event — this is the child-safety-critical
///   "moderation_unavailable" signal). The listener callback only
///   increments in-memory counters guarded by a lock/Interlocked — it never
///   posts a webhook itself (measurement callbacks can fire on a hot
///   request path and must stay cheap); the periodic tick drains them and
///   decides whether to alert. Counts only, never a device/parent/child id
///   — the existing counters are already tag-bounded, and this service adds
///   no new tag.</description></item>
///   <item><description><b>Backup</b> — alerts when the newest
///   <c>areg-backup-*.db</c> snapshot <see cref="DatabaseBackupService"/>
///   already writes is older than 36h (or absent). Directory resolution
///   mirrors <see cref="DatabaseBackupService"/>'s own
///   (<c>Backup:Database:DirectoryPath</c>, else <c>backups/</c> beside the
///   live DB via <see cref="SqliteDatabaseSnapshot.IsSqliteFileDatabase"/>)
///   so the two can never disagree about where snapshots live. Skipped
///   (logged once at Debug) on a non-file-backed provider — there is
///   nothing to check.</description></item>
/// </list>
///
/// <para>
/// <b>Delivery.</b> A single internal <see cref="HttpClient"/> (10s
/// timeout, one retry) POSTs <c>{ text, key, severity, at }</c> as JSON —
/// works unmodified as a Slack incoming-webhook body (Slack reads the
/// <c>text</c> field, ignores the rest) and as a generic receiver's JSON.
/// <b>Not</b> <c>IHttpClientFactory</c>: that type lives in the
/// <c>Microsoft.Extensions.Http</c> NuGet package, which this Infrastructure
/// project does not reference and this slice is barred from adding — a
/// plain <see cref="HttpClient"/> is the same idiom
/// <c>DependencyInjection.AddInfrastructure</c> already uses for the OpenAI
/// SDK transport. A cooldown (<c>Alerts:CooldownMinutes</c>, per alert key,
/// in-memory — resets on restart) prevents a flapping signal from spamming;
/// a failed post is logged at Warning and never retried in a loop. Each
/// alert actually delivered (2xx) increments the bounded
/// <see cref="AppMeter.AlertsSent"/> counter tagged by key.
/// </para>
/// </summary>
public sealed class AlertingService : BackgroundService
{
    public const int DefaultCooldownMinutes = 30;
    public const int DefaultHealthCheckIntervalSeconds = 60;
    public const int DefaultCostCapTripsThresholdPerHour = 5;
    private static readonly TimeSpan StaleBackupThreshold = TimeSpan.FromHours(36);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AlertingService> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>Per-alert-key cooldown state; in-memory only, so a restart
    /// clears it (an operator seeing one extra alert right after a deploy is
    /// an acceptable cost — see the commit message for what this does NOT
    /// cover).</summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastSentUtc = new();

    private bool? _lastDbHealthy;
    private bool? _lastAudioStoreOk;
    private bool _loggedNotSqlite;

    private readonly object _costCapLock = new();
    private readonly System.Collections.Generic.List<DateTime> _costCapTripTimesUtc = new();
    private long _circuitTripsSinceLastTick;
    private long _moderationUnavailableSinceLastTick;

    private MeterListener? _meterListener;

    public AlertingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<AlertingService> logger,
        HttpClient httpClient)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _environment = environment;
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Test seam: when set, replaces the real DB liveness probe
    /// (<see cref="IsDatabaseReachableAsync"/>) so tests can script a
    /// healthy→unhealthy→healthy sequence deterministically instead of
    /// forcing a real SQLite connection to fail on demand.
    /// </summary>
    internal Func<AppDbContext, TimeSpan, CancellationToken, Task<bool>>? DbHealthCheckOverride { get; set; }

    /// <summary>Test seam: starts the <see cref="MeterListener"/> without
    /// running the full <see cref="ExecuteAsync"/> loop, so tests can drive
    /// <see cref="RunTickAsync"/> directly while still observing the
    /// AppMeter counters this service reacts to. Pair with
    /// <see cref="StopMeterListenerForTests"/> — the listener subscribes to
    /// the process-wide <see cref="AppMeter.Instance"/>, so a test that
    /// starts one and never stops it would keep observing every later
    /// test's counter increments for the rest of the process.</summary>
    internal void StartMeterListenerForTests() => StartMeterListener();

    /// <summary>Test seam: disposes the <see cref="MeterListener"/> started
    /// by <see cref="StartMeterListenerForTests"/>. Idempotent/no-op if
    /// never started.</summary>
    internal void StopMeterListenerForTests()
    {
        _meterListener?.Dispose();
        _meterListener = null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartMeterListener();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Never let an alerting-tick failure take the host down —
                    // same posture as RetentionPurgeService/DatabaseBackupService.
                    _logger.LogWarning(ex, "Alerting tick failed; will retry on the next interval");
                }

                var seconds = ReadInt("Alerts:HealthCheckIntervalSeconds", DefaultHealthCheckIntervalSeconds);
                if (seconds < 1) seconds = 1;
                try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
        finally
        {
            _meterListener?.Dispose();
        }
    }

    /// <summary>One alerting pass. Public so tests drive ticks directly
    /// (same seam as <see cref="DatabaseBackupService.RunTickAsync"/>).</summary>
    public async Task RunTickAsync(CancellationToken ct)
    {
        var url = _config["Alerts:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            // Fully disabled: still drain the metric counters so a long
            // disabled period doesn't build an unbounded backlog that
            // floods the moment a URL is configured.
            DrainMetricCounters();
            return;
        }

        await CheckHealthAsync(url, ct);
        CheckCostCapTrips(url);
        CheckOpenAiCircuit(url);
        CheckModerationUnavailable(url);
        await CheckBackupStaleAsync(url, ct);
    }

    private async Task CheckHealthAsync(string url, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbHealthy = await (DbHealthCheckOverride ?? IsDatabaseReachableAsync)(db, TimeSpan.FromSeconds(2), ct);
        if (_lastDbHealthy is not null && _lastDbHealthy != dbHealthy)
        {
            if (!dbHealthy)
            {
                await SendAlertAsync(url, "health_db_unhealthy", "critical",
                    "ArmenianAiToy: database liveness check failed.", ct);
            }
            else
            {
                await SendAlertAsync(url, "health_db_recovered", "info",
                    "ArmenianAiToy: database liveness check recovered.", ct);
            }
        }
        _lastDbHealthy = dbHealthy;

        var audioStoreOk = IsAudioStoreConfigured(
            _environment.IsDevelopment(), _config["Audio:BlobStoreRoot"]);
        if (_lastAudioStoreOk is not null && _lastAudioStoreOk != audioStoreOk)
        {
            if (!audioStoreOk)
            {
                await SendAlertAsync(url, "health_audio_store_unconfigured", "warning",
                    "ArmenianAiToy: Audio:BlobStoreRoot is unconfigured — voice chat is refusing with 503.", ct);
            }
            else
            {
                await SendAlertAsync(url, "health_audio_store_recovered", "info",
                    "ArmenianAiToy: Audio:BlobStoreRoot is configured again.", ct);
            }
        }
        _lastAudioStoreOk = audioStoreOk;
    }

    private void CheckCostCapTrips(string url)
    {
        int count;
        lock (_costCapLock)
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
            _costCapTripTimesUtc.RemoveAll(t => t < cutoff);
            count = _costCapTripTimesUtc.Count;
        }

        var threshold = ReadInt("Alerts:CostCapTripsThresholdPerHour", DefaultCostCapTripsThresholdPerHour);
        if (count > threshold)
        {
            _ = SendAlertAsync(url, "cost_cap_trips_high", "warning",
                $"ArmenianAiToy: {count} OpenAI daily-cost-cap trips in the last hour (threshold {threshold}).",
                CancellationToken.None);
        }
    }

    private void CheckOpenAiCircuit(string url)
    {
        var trips = Interlocked.Exchange(ref _circuitTripsSinceLastTick, 0);
        if (trips > 0)
        {
            _ = SendAlertAsync(url, "openai_circuit_open", "critical",
                $"ArmenianAiToy: OpenAI reliability circuit breaker opened ({trips} time(s) since last check).",
                CancellationToken.None);
        }
    }

    private void CheckModerationUnavailable(string url)
    {
        var count = Interlocked.Exchange(ref _moderationUnavailableSinceLastTick, 0);
        if (count > 0)
        {
            _ = SendAlertAsync(url, "moderation_unavailable", "critical",
                $"ArmenianAiToy: moderation fail-closed {count} time(s) since last check (chat turns are being blocked, not silently allowed).",
                CancellationToken.None);
        }
    }

    /// <summary>Drains the metric-derived counters without alerting — used
    /// while the alerter is disabled (empty webhook URL) so re-enabling it
    /// does not immediately fire on a backlog accumulated while off.</summary>
    private void DrainMetricCounters()
    {
        lock (_costCapLock)
        {
            _costCapTripTimesUtc.Clear();
        }
        Interlocked.Exchange(ref _circuitTripsSinceLastTick, 0);
        Interlocked.Exchange(ref _moderationUnavailableSinceLastTick, 0);
    }

    private async Task CheckBackupStaleAsync(string url, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dir = ResolveBackupDirectory(db);
        if (dir is null)
        {
            if (!_loggedNotSqlite)
            {
                _loggedNotSqlite = true;
                _logger.LogDebug(
                    "Alerting: backup-staleness check skipped, provider is not a file-backed SQLite database");
            }
            return;
        }

        if (!Directory.Exists(dir))
        {
            await SendAlertAsync(url, "backup_stale", "warning",
                "ArmenianAiToy: no database backup directory found — no snapshot has ever been written.", ct);
            return;
        }

        var newest = Directory.GetFiles(dir, $"{DatabaseBackupService.FilePrefix}*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (newest is null)
        {
            await SendAlertAsync(url, "backup_stale", "warning",
                "ArmenianAiToy: no database backup snapshot found in the backup directory.", ct);
            return;
        }

        var age = DateTime.UtcNow - newest.LastWriteTimeUtc;
        if (age > StaleBackupThreshold)
        {
            await SendAlertAsync(url, "backup_stale", "warning",
                $"ArmenianAiToy: the newest database backup is {age.TotalHours:F0}h old (threshold 36h).", ct);
        }
    }

    /// <summary>Mirrors <c>DatabaseBackupService.ResolveBackupDirectory</c>
    /// exactly (same config keys, same fallback) so the two can never
    /// disagree about where snapshots live. Not shared code because the
    /// original is a private instance method on a different service and
    /// pulling it out is a bigger change than this slice's scope
    /// warrants.</summary>
    private string? ResolveBackupDirectory(AppDbContext db)
    {
        var configured = _config["Backup:Database:DirectoryPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        if (!SqliteDatabaseSnapshot.IsSqliteFileDatabase(db, out var dbPath) || dbPath is null)
        {
            return null;
        }
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");
    }

    /// <summary>Cooldown-gated webhook POST. Internal so tests can invoke it
    /// directly for payload-shape assertions.</summary>
    internal async Task SendAlertAsync(string url, string key, string severity, string text, CancellationToken ct)
    {
        var cooldown = TimeSpan.FromMinutes(ReadInt("Alerts:CooldownMinutes", DefaultCooldownMinutes));
        var now = DateTime.UtcNow;

        if (_lastSentUtc.TryGetValue(key, out var last) && now - last < cooldown)
        {
            _logger.LogDebug("Alert {Key} suppressed by cooldown ({RemainingSeconds}s remaining)",
                key, (cooldown - (now - last)).TotalSeconds);
            return;
        }
        // Recorded up front, not after delivery: a broken webhook must not
        // turn a per-tick check into a per-tick retry storm.
        _lastSentUtc[key] = now;

        var payload = new
        {
            text,
            key,
            severity,
            at = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        var json = JsonSerializer.Serialize(payload);

        if (await PostOnceAsync(url, json, ct) || await PostOnceAsync(url, json, ct))
        {
            AppMeter.AlertsSent.Add(1, new KeyValuePair<string, object?>("key", key));
            _logger.LogInformation("Alert sent: {Key} ({Severity})", key, severity);
        }
        else
        {
            _logger.LogWarning("Alert webhook POST failed for {Key} after one retry; not retried further", key);
        }
    }

    private async Task<bool> PostOnceAsync(string url, string json, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // our own timeout, not caller cancellation
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void StartMeterListener()
    {
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != AppMeter.Name) return;
                if (instrument.Name is "aat_openai_cost_cap_trip_total"
                    or "aat_chat_openai_circuit_trip_total"
                    or "aat_moderation_failclosed_total")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _meterListener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _meterListener.Start();
    }

    /// <summary>
    /// Runs on whatever thread recorded the measurement — potentially a hot
    /// request path (e.g. inside <c>OpenAIReliabilityGate</c>) — so this
    /// does nothing but append to an in-memory, lock-guarded accumulator.
    /// No webhook call, no I/O, no allocation beyond the list add.
    /// </summary>
    private void OnLongMeasurement(
        Instrument instrument, long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        switch (instrument.Name)
        {
            case "aat_openai_cost_cap_trip_total":
                lock (_costCapLock)
                {
                    _costCapTripTimesUtc.Add(DateTime.UtcNow);
                }
                break;
            case "aat_chat_openai_circuit_trip_total":
                Interlocked.Increment(ref _circuitTripsSinceLastTick);
                break;
            case "aat_moderation_failclosed_total":
                Interlocked.Increment(ref _moderationUnavailableSinceLastTick);
                break;
        }
    }

    private int ReadInt(string key, int fallback)
        => int.TryParse(_config[key], out var v) ? v : fallback;

    /// <summary>
    /// Same one-line liveness probe as
    /// <c>ArmenianAiToy.Api.Health.HealthProbe.IsDatabaseReachableAsync</c>,
    /// duplicated here because Infrastructure cannot reference Api (see the
    /// class xmldoc). Keep the two in sync by hand if either changes.
    /// </summary>
    private static async Task<bool> IsDatabaseReachableAsync(
        AppDbContext db, TimeSpan timeout, CancellationToken requestToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        cts.CancelAfter(timeout);
        try
        {
            return await db.Database.CanConnectAsync(cts.Token);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Same fail-closed rule as
    /// <c>ArmenianAiToy.Api.Security.AudioBlobStoreRootResolver.Resolve</c>
    /// (Development always resolves configured; elsewhere an unset OR
    /// relative path is "not configured"), duplicated for the same
    /// project-direction reason as <see cref="IsDatabaseReachableAsync"/>.
    /// Returns only the boolean this service needs — the resolver's
    /// human-readable reason is not reproduced here since callers of this
    /// method compose their own alert text.
    /// </summary>
    private static bool IsAudioStoreConfigured(bool isDevelopment, string? configured)
    {
        var trimmed = configured?.Trim();
        if (isDevelopment) return true;
        if (string.IsNullOrEmpty(trimmed)) return false;
        return Path.IsPathRooted(trimmed);
    }
}

using System.Reflection;
using System.Text.Json;
using ArmenianAiToy.Api.Observability;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using ArmenianAiToy.Infrastructure.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Operator console "System" and "Logs" tabs: what the backend is running
/// (models per path, credentials present, config checks), what it has spent
/// (the durable per-day estimate), the ElevenLabs character quota left, and
/// the most recent warnings/errors. Read-only; behind the same fail-closed
/// <c>/api/internal/*</c> bearer gate as <see cref="InternalController"/>
/// (the gate is path-prefix middleware, not an attribute).
///
/// <para>
/// Secret invariant: no response here carries a credential value — only
/// presence booleans (<see cref="SystemStatusReport"/>), and log text is
/// redacted before it is even buffered (<see cref="RecentLogBuffer"/>).
/// </para>
/// </summary>
[ApiController]
[Route("api/internal")]
public class InternalSystemController : ControllerBase
{
    // The process start, not this type's first use (a static initializer
    // would stamp the first console request instead).
    private static readonly DateTime StartedAtUtc =
        global::System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    // One pooled client for the ElevenLabs quota probe; plain HttpClient,
    // same choice as AlertingService (no IHttpClientFactory package).
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly object QuotaLock = new();
    private static (DateTime At, object Body)? _quotaCache;
    private static readonly TimeSpan QuotaCacheTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly OpenAICostMeter _costMeter;
    private readonly IOptions<OpenAIDailyCostCapOptions> _capOptions;
    private readonly OpenAIReliabilityGate? _openAiGate;
    private readonly RecentLogBuffer? _logs;

    public InternalSystemController(
        AppDbContext db, IConfiguration config, IWebHostEnvironment env,
        OpenAICostMeter costMeter, IOptions<OpenAIDailyCostCapOptions> capOptions,
        OpenAIReliabilityGate? openAiGate = null, RecentLogBuffer? logs = null)
    {
        _db = db;
        _config = config;
        _env = env;
        _costMeter = costMeter;
        _capOptions = capOptions;
        _openAiGate = openAiGate;
        _logs = logs;
    }

    /// <summary>Models, configured credentials (presence only), config
    /// checks, build/uptime, and the last 30 UTC days of estimated spend.</summary>
    [HttpGet("system")]
    public async Task<IActionResult> System(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var report = SystemStatusReport.Build(_config, _env.IsDevelopment(), now);

        // DeviceUsageDays is the durable per-device, per-UTC-day estimate
        // (written on every metered turn). Summed client-side: SQLite cannot
        // aggregate decimal columns server-side, and 30 days × fleet is small.
        var since = now.Date.AddDays(-29);
        var rows = await _db.DeviceUsageDays.AsNoTracking()
            .Where(u => u.DayUtc >= since)
            .Select(u => new { u.DayUtc, u.DeviceId, u.Questions, u.EstimatedUsd })
            .ToListAsync(ct);
        var days = rows
            .GroupBy(r => r.DayUtc.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                dayUtc = g.Key.ToString("yyyy-MM-dd"),
                questions = g.Sum(r => r.Questions),
                activeToys = g.Select(r => r.DeviceId).Distinct().Count(),
                estimatedUsd = decimal.Round(g.Sum(r => r.EstimatedUsd), 4),
            })
            .ToList();
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var cap = _capOptions.Value;

        // Top spenders over the same window — one runaway toy is invisible
        // in the fleet-by-day totals. Names only for the ids that made it.
        var topToysRaw = rows
            .GroupBy(r => r.DeviceId)
            .Select(g => new { DeviceId = g.Key, Questions = g.Sum(r => r.Questions), Usd = g.Sum(r => r.EstimatedUsd) })
            .OrderByDescending(t => t.Usd)
            .Take(10)
            .ToList();
        var topIds = topToysRaw.Select(t => t.DeviceId).ToList();
        var names = await _db.Devices.AsNoTracking()
            .Where(d => topIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var topToys = topToysRaw.Select(t => new
        {
            deviceId = t.DeviceId,
            name = names.TryGetValue(t.DeviceId, out var n) ? n : null,
            questions = t.Questions,
            estimatedUsd = decimal.Round(t.Usd, 4),
        }).ToList();

        var backup = NewestBackup(_config, _db);
        var checks = report.Checks.ToList();
        if (backup.Directory is not null && (backup.NewestAtUtc is null || now - backup.NewestAtUtc > BackupStaleAfter))
            checks.Add(new SystemStatusReport.Check(SystemStatusReport.Warn, "backup_stale",
                backup.NewestAtUtc is null
                    ? "No database backup snapshot exists yet."
                    : $"The newest database backup is {(now - backup.NewestAtUtc.Value).TotalHours:F0} h old (expected daily)."));

        return Ok(new
        {
            generatedAtUtc = now,
            build = new
            {
                environment = _env.EnvironmentName,
                version = Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                commit = FirstNonEmpty(_config["RAILWAY_GIT_COMMIT_SHA"], _config["GIT_COMMIT_SHA"]),
                startedAtUtc = StartedAtUtc,
                uptimeSeconds = (long)(now - StartedAtUtc).TotalSeconds,
            },
            openAiCircuitOpen = _openAiGate?.IsCircuitOpen() ?? false,
            models = report.Models,
            configured = report.Configured,
            checks,
            backup = new { newestAtUtc = backup.NewestAtUtc, sizeBytes = backup.SizeBytes },
            spend = new
            {
                note = "Estimated by the backend from characters/bytes at list prices, not the providers' invoices.",
                todayInProcessUsd = decimal.Round(_costMeter.GetGlobalTotal(now), 4),
                last30DaysUsd = decimal.Round(rows.Sum(r => r.EstimatedUsd), 4),
                monthToDateUsd = decimal.Round(rows.Where(r => r.DayUtc >= monthStart).Sum(r => r.EstimatedUsd), 4),
                perToyDailyCapUsd = cap.Enabled ? cap.Default : (decimal?)null,
                fleetDailyCapUsd = cap.Enabled && cap.Global > 0 ? cap.Global : (decimal?)null,
                days,
                topToys,
            },
            billing = new[]
            {
                new { provider = "OpenAI", url = "https://platform.openai.com/settings/organization/billing/overview" },
                new { provider = "Gemini", url = "https://aistudio.google.com/usage" },
                new { provider = "ElevenLabs", url = "https://elevenlabs.io/app/subscription" },
                new { provider = "Railway", url = "https://railway.com/account/usage" },
            },
        });
    }

    private static readonly TimeSpan BackupStaleAfter = TimeSpan.FromHours(36); // same threshold as AlertingService

    /// <summary>Newest <c>areg-backup-*.db</c> snapshot. Directory resolution
    /// mirrors <c>DatabaseBackupService.ResolveBackupDirectory</c> (same key,
    /// same fallback — AlertingService keeps the same mirror); a null
    /// directory means a non-file database (test hosts), not "no backups".</summary>
    internal static (string? Directory, DateTime? NewestAtUtc, long? SizeBytes) NewestBackup(IConfiguration config, AppDbContext db)
    {
        var dir = config["Backup:Database:DirectoryPath"];
        if (string.IsNullOrWhiteSpace(dir))
        {
            if (!SqliteDatabaseSnapshot.IsSqliteFileDatabase(db, out var dbPath) || dbPath is null)
                return (null, null, null);
            dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");
        }
        if (!global::System.IO.Directory.Exists(dir)) return (dir, null, null);
        var newest = global::System.IO.Directory.GetFiles(dir, $"{DatabaseBackupService.FilePrefix}*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return newest is null ? (dir, null, null) : (dir, newest.LastWriteTimeUtc, newest.Length);
    }

    /// <summary>Live ElevenLabs character quota (used / limit / reset).
    /// Cached 5 minutes; a failure is reported in the body, never as a 5xx,
    /// so the System tab still renders.</summary>
    [HttpGet("system/elevenlabs")]
    public async Task<IActionResult> ElevenLabsQuota(CancellationToken ct)
    {
        var key = FirstNonEmpty(_config["ElevenLabs:ApiKey"], _config["ELEVENLABS_API_KEY"]);
        if (key is null) return Ok(new { available = false, reason = "ElevenLabs key not configured." });

        lock (QuotaLock)
        {
            if (_quotaCache is { } c && DateTime.UtcNow - c.At < QuotaCacheTtl) return Ok(c.Body);
        }

        object body;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription");
            req.Headers.Add("xi-api-key", key);
            using var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                return Ok(new { available = false, reason = $"ElevenLabs answered HTTP {(int)res.StatusCode}." });
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            body = ParseElevenLabsSubscription(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Ok(new { available = false, reason = "ElevenLabs could not be reached." });
        }

        lock (QuotaLock) { _quotaCache = (DateTime.UtcNow, body); }
        return Ok(body);
    }

    /// <summary>Pure parse of <c>GET /v1/user/subscription</c>; pinned by test.</summary>
    public static object ParseElevenLabsSubscription(JsonElement root)
    {
        long? Num(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
        string? Str(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var used = Num("character_count");
        var limit = Num("character_limit");
        var reset = Num("next_character_count_reset_unix");
        return new
        {
            available = used is not null && limit is not null,
            tier = Str("tier"),
            status = Str("status"),
            charactersUsed = used,
            characterLimit = limit,
            charactersLeft = used is not null && limit is not null ? Math.Max(0, limit.Value - used.Value) : (long?)null,
            resetsAtUtc = reset is not null ? DateTimeOffset.FromUnixTimeSeconds(reset.Value).UtcDateTime : (DateTime?)null,
        };
    }

    /// <summary>Most recent warnings/errors from this process, newest first.
    /// <paramref name="level"/> is warning (default), error or critical.</summary>
    [HttpGet("logs")]
    public IActionResult Logs([FromQuery] string? level = null, [FromQuery] int limit = 200)
    {
        var min = level?.Trim().ToLowerInvariant() switch
        {
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            _ => LogLevel.Warning,
        };
        limit = Math.Clamp(limit, 1, 500);
        var entries = _logs?.Snapshot(min, limit) ?? Array.Empty<RecentLogBuffer.Entry>();
        return Ok(new
        {
            note = "This process only, since its last start (redeploy empties it). Full history: Railway → Deployments → Logs.",
            startedAtUtc = StartedAtUtc,
            entries,
        });
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

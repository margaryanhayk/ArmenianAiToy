using System.Collections.Concurrent;
using ArmenianAiToy.Application.Notifications;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Background;

/// <summary>
/// Opt-in weekly parent digest. Once per ~7 days per verified parent that
/// had activity, emails a counts-only summary of the last 7 days across
/// the parent's linked devices, nudging them back to the dashboard.
///
/// <para><b>Opt-in and off by default.</b> The whole worker short-circuits
/// unless <c>Digest:Weekly:Enabled=true</c> — the same posture as the
/// dormancy passes (it sends real email, so it must not fire on a fresh
/// <c>dotnet run</c>). The check-loop cadence is
/// <c>Digest:Weekly:CheckIntervalHours</c> (default 24, floor 1); the
/// worker wakes daily and only actually sends to a parent whose last send
/// was ≥ 7 days ago.</para>
///
/// <para><b>Privacy.</b> The digest carries only aggregate counts
/// (<see cref="WeeklyDigestSummary"/>) — no child names, no message
/// content, no device names. The query projects only <c>Message.Timestamp</c>;
/// content is never materialized on this process. Same discipline as the
/// week-summary read path.</para>
///
/// <para><b>Dedup is process-local (v1 limitation).</b> "Last sent per
/// parent" lives in an in-memory dictionary, so a process restart can
/// re-send a digest early. This is intentionally the simplest correct-
/// enough approach for a low-stakes activity email; a persistent
/// <c>Parent.LastWeeklyDigestAt</c> column (schema change) is the
/// follow-up if exact once-per-week delivery is ever required.</para>
/// </summary>
public sealed class WeeklyDigestService : BackgroundService
{
    public const int DefaultCheckIntervalHours = 24;
    public const int MinCheckIntervalHours = 1;
    public const int DigestPeriodDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WeeklyDigestService> _logger;

    // Process-local "last digest sent" per parent. See class xmldoc.
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSentUtc = new();

    public WeeklyDigestService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<WeeklyDigestService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    private bool Enabled =>
        bool.TryParse(_config["Digest:Weekly:Enabled"], out var e) && e;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "WeeklyDigestService tick failed; will retry on next tick.");
            }

            try
            {
                await Task.Delay(ReadInterval(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Runs one digest pass. Exposed <c>public</c> so tests can drive a
    /// single tick against a seeded <see cref="AppDbContext"/> without a
    /// real loop (the Infrastructure project has no InternalsVisibleTo).
    /// A noop when the feature is disabled.
    /// </summary>
    public async Task RunTickAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        var windowStart = nowUtc.AddDays(-DigestPeriodDays);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();

        // Verified, non-anonymized parents only — an unverified address may
        // be invalid/unowned, and anonymized rows have no usable email.
        var parents = await db.Set<Parent>()
            .Where(p => p.EmailVerifiedAt != null && p.AnonymizedAt == null)
            .Select(p => new { p.Id, p.Email })
            .ToListAsync(cancellationToken);

        foreach (var parent in parents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_lastSentUtc.TryGetValue(parent.Id, out var last)
                && (nowUtc - last).TotalDays < DigestPeriodDays)
            {
                continue; // already sent within the period
            }

            var deviceIds = await db.Set<ParentDevice>()
                .Where(pd => pd.ParentId == parent.Id)
                .Select(pd => pd.DeviceId)
                .ToListAsync(cancellationToken);
            if (deviceIds.Count == 0)
            {
                continue;
            }

            // Counts-only projection over this parent's devices in the
            // window. Content is never materialized.
            var timestamps = await db.Set<Message>()
                .Join(
                    db.Set<Conversation>().Where(c => deviceIds.Contains(c.DeviceId)),
                    m => m.ConversationId,
                    c => c.Id,
                    (m, c) => m.Timestamp)
                .Where(ts => ts >= windowStart)
                .ToListAsync(cancellationToken);

            if (timestamps.Count == 0)
            {
                continue; // no activity this week → no email
            }

            var conversationCount = await db.Set<Conversation>()
                .CountAsync(c => deviceIds.Contains(c.DeviceId)
                              && c.Messages.Any(m => m.Timestamp >= windowStart),
                    cancellationToken);

            var activeDays = timestamps.Select(t => t.Date).Distinct().Count();

            var summary = new WeeklyDigestSummary(
                WindowStartUtc: windowStart,
                WindowEndUtc: nowUtc,
                DeviceCount: deviceIds.Count,
                ConversationCount: conversationCount,
                MessageCount: timestamps.Count,
                ActiveDays: activeDays);

            var delivered = await notifier.SendWeeklyDigestAsync(
                parent.Email, summary, cancellationToken);

            // Record "last sent" only on success so a failed send retries
            // next tick. Process-local (v1) — see class xmldoc.
            if (delivered)
            {
                _lastSentUtc[parent.Id] = nowUtc;
            }
        }
    }

    private TimeSpan ReadInterval()
    {
        var hours = int.TryParse(_config["Digest:Weekly:CheckIntervalHours"], out var h)
            ? h
            : DefaultCheckIntervalHours;
        if (hours < MinCheckIntervalHours)
        {
            hours = MinCheckIntervalHours;
        }
        return TimeSpan.FromHours(hours);
    }
}

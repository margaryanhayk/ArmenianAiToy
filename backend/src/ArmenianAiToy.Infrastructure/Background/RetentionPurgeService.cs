using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Background;

/// <summary>
/// First scheduled-delete worker in the repo. Hard-deletes conversations
/// (and their cascaded messages) whose last activity is older than
/// <c>Retention:Messages:MaxAgeDays</c>. Writes exactly one
/// <see cref="Domain.Enums.AuditEventType.ConversationsPurgedByRetention"/>
/// audit row per tick that actually deleted something; noop ticks write
/// no row. See CLAUDE.md § Retention for the full contract.
///
/// <para>
/// Config keys (all read via <see cref="IConfiguration"/>, mirroring the
/// <c>RateLimiting:Chat</c> shape — no strongly-typed Options class):
/// </para>
/// <list type="bullet">
///   <item><description><c>Retention:Messages:MaxAgeDays</c> — default
///   <see cref="DefaultMaxAgeDays"/>. <b>Non-positive values disable the
///   worker</b>; that path is reached ONLY via an explicit override, not
///   by missing config. Missing config resolves to
///   <see cref="DefaultMaxAgeDays"/>, never to 0.</description></item>
///   <item><description><c>Retention:Messages:RunIntervalMinutes</c> —
///   default <see cref="DefaultRunIntervalMinutes"/>; server-side
///   floor-clamped to <see cref="MinRunIntervalMinutes"/>.</description></item>
///   <item><description><c>Retention:Messages:MaxBatchSize</c> — default
///   <see cref="DefaultMaxBatchSize"/>; server-side clamped to
///   <c>[<see cref="MinBatchSize"/>, <see cref="MaxAllowedBatchSize"/>]</c>.</description></item>
/// </list>
///
/// <para>
/// <b>Query shape.</b> Eligibility uses a projection over
/// <c>Conversations</c> that pulls only <c>Id</c>, <c>StartedAt</c>,
/// <c>EndedAt</c>, <c>MAX(Messages.Timestamp)</c>, and
/// <c>COUNT(Messages)</c> — <c>Message.Content</c> is never materialized
/// on this process. Eligibility =
/// <c>max(StartedAt, EndedAt ?? min, LastMessageAt ?? min) &lt; cutoff</c>,
/// rewritten as per-component null-aware checks so the LINQ-to-SQL
/// translator does not need <c>DateTime.MinValue</c> constants. The
/// fallback when a conversation has no messages is <c>StartedAt</c>.
/// </para>
///
/// <para>
/// <b>Cascade.</b> Deletion removes only <c>Conversation</c> rows via
/// the EF change tracker. Messages cascade at the DB level through the
/// schema FK (same contract that
/// <c>ParentServiceDeleteChildTests</c> already proves).
/// </para>
/// </summary>
public sealed class RetentionPurgeService : BackgroundService
{
    public const int DefaultMaxAgeDays = 90;
    public const int DefaultRunIntervalMinutes = 60;
    public const int DefaultMaxBatchSize = 500;
    public const int MinRunIntervalMinutes = 15;
    public const int MinBatchSize = 1;
    public const int MaxAllowedBatchSize = 10_000;

    /// <summary>
    /// Grace window (hours) applied to <c>ParentPasswordResetToken</c>
    /// cleanup: a row is only deleted on expiry grounds once its
    /// <c>ExpiresAt</c> is older than <c>UtcNow - grace</c>. Consumed
    /// tokens are deleted unconditionally. 24 h is small enough that
    /// stale rows do not accumulate, and large enough to absorb
    /// clock skew and a few worker-tick miss-cycles.
    /// </summary>
    public const int DefaultPasswordResetGracePeriodHours = 24;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RetentionPurgeService> _logger;

    public RetentionPurgeService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<RetentionPurgeService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Host is shutting down — exit cleanly without logging
                // as an error.
                return;
            }
            catch (Exception ex)
            {
                // A throw out of ExecuteAsync would kill the hosted
                // service for the lifetime of the process. Log and let
                // the next tick retry. Idempotency is stateless: the
                // cutoff is recomputed each tick and eligibility is
                // purely timestamp-driven.
                _logger.LogError(ex,
                    "RetentionPurgeService tick failed; will retry on next tick.");
            }

            var interval = ReadInterval();
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Exposed as <c>public</c> for unit testing — exercises one tick
    /// end-to-end against a seeded <see cref="AppDbContext"/> without
    /// scheduling a real loop. The Infrastructure project does not use
    /// <c>InternalsVisibleTo</c>, so this is the minimum-surface way to
    /// let the test project drive a tick.
    ///
    /// <para>
    /// A tick runs TWO cleanup passes in order:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>Conversation purge</b> — the original
    ///   worker behavior, audited per tick-with-deletions.</description></item>
    ///   <item><description><b>Password-reset token cleanup</b> —
    ///   deletes consumed tokens plus expired tokens past the grace
    ///   window. Not audited; see the cleanup helper's xmldoc for
    ///   rationale.</description></item>
    /// </list>
    /// <para>
    /// Both passes share the same disable gate: when <c>MaxAgeDays
    /// &lt;= 0</c> the whole tick short-circuits. Operators who want
    /// only token cleanup with conversations disabled would need a
    /// separate config key — out of scope for this slice, and the
    /// current "retention worker off" mental model is preserved.
    /// </para>
    /// </summary>
    public async Task RunTickAsync(CancellationToken stoppingToken)
    {
        var maxAgeDays = ReadMaxAgeDays();
        if (maxAgeDays <= 0)
        {
            _logger.LogInformation(
                "RetentionPurgeService disabled (MaxAgeDays={MaxAgeDays}); skipping tick.",
                maxAgeDays);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await PurgeExpiredConversationsAsync(db, maxAgeDays, stoppingToken);
        await PurgeStalePasswordResetTokensAsync(db, stoppingToken);
    }

    private async Task PurgeExpiredConversationsAsync(
        AppDbContext db, int maxAgeDays, CancellationToken stoppingToken)
    {
        var batchSize = ReadBatchSize();
        var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(maxAgeDays);

        // Cheap projection — fetch only id + per-conversation aggregates
        // so Message.Content never lands on this process. SQL emits
        // COUNT and MAX over Messages; the payload stays in the DB.
        var eligible = await db.Set<Conversation>()
            .Select(c => new
            {
                c.Id,
                c.StartedAt,
                c.EndedAt,
                LastMessageAt = c.Messages.Max(m => (DateTime?)m.Timestamp),
                MessageCount = c.Messages.Count()
            })
            .Where(x =>
                x.StartedAt < cutoffUtc
                && (x.EndedAt == null || x.EndedAt < cutoffUtc)
                && (x.LastMessageAt == null || x.LastMessageAt < cutoffUtc))
            .OrderBy(x => x.LastMessageAt ?? x.StartedAt)
            .Take(batchSize)
            .ToListAsync(stoppingToken);

        if (eligible.Count == 0)
        {
            _logger.LogInformation(
                "RetentionPurgeService tick: nothing eligible (cutoff={CutoffUtc:O}, batch={BatchSize}).",
                cutoffUtc, batchSize);
            return;
        }

        var idsToDelete = eligible.Select(x => x.Id).ToList();
        var messagesDeleted = eligible.Sum(x => x.MessageCount);

        // Second query loads bare Conversation rows (no Include) so the
        // change tracker can issue DELETEs; Message rows cascade at the
        // DB level via the schema FK.
        var toDelete = await db.Set<Conversation>()
            .Where(c => idsToDelete.Contains(c.Id))
            .ToListAsync(stoppingToken);

        db.Set<Conversation>().RemoveRange(toDelete);

        // Inline the TrackAndAddAudit pattern from ParentService — this
        // slice deliberately does not promote that private helper to a
        // shared type. One audit row per tick-with-deletions; noop
        // ticks returned above and wrote nothing.
        //
        // ActorParentId = null is what keeps this event out of every
        // parent-facing read surface (GET /api/parents/audit and the
        // parent-scoped audit slice of GET /api/parents/export both
        // filter ActorParentId == parentId). See the factory xmldoc
        // on AuditEvent.ConversationsPurgedByRetention.
        var audit = AuditEvent.ConversationsPurgedByRetention(
            conversationsDeleted: toDelete.Count,
            messagesDeleted: messagesDeleted,
            cutoffUtc: cutoffUtc,
            batchSizeLimit: batchSize);
        db.Set<AuditEvent>().Add(audit);
        AppMeter.AuditEventsWritten.Add(1,
            new KeyValuePair<string, object?>("event_type", audit.EventType.ToString()));

        await db.SaveChangesAsync(stoppingToken);

        _logger.LogInformation(
            "RetentionPurgeService tick: purged {Conversations} conversations and {Messages} messages (cutoff={CutoffUtc:O}, batch={BatchSize}).",
            toDelete.Count, messagesDeleted, cutoffUtc, batchSize);
    }

    /// <summary>
    /// Deletes stale <c>ParentPasswordResetToken</c> rows that no longer
    /// serve any purpose:
    /// <list type="bullet">
    ///   <item><description>rows with <c>ConsumedAt</c> set — the token
    ///   is single-use and can never redeem again; no audit value in
    ///   keeping the breadcrumb around;</description></item>
    ///   <item><description>rows whose <c>ExpiresAt</c> is older than
    ///   <c>UtcNow - grace</c> — the token is past its usable window and
    ///   the grace lets us absorb clock skew without deleting something
    ///   a client could still redeem.</description></item>
    /// </list>
    /// <para>
    /// <b>No audit row written</b> — these rows are short-lived
    /// operational state, not destructive parent actions, so they
    /// don't belong in the durable audit log (same reasoning the
    /// forgot-password slice documented for zero-metadata audit on
    /// the reset events themselves). Uses
    /// <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>
    /// for a single <c>DELETE WHERE</c> statement without materializing
    /// any rows — no hash, no parent id, no timestamp ever touches the
    /// worker process.
    /// </para>
    /// </summary>
    private async Task PurgeStalePasswordResetTokensAsync(
        AppDbContext db, CancellationToken stoppingToken)
    {
        var graceHours = ReadPasswordResetGracePeriodHours();
        var expiryCutoff = DateTime.UtcNow - TimeSpan.FromHours(graceHours);

        var deleted = await db.Set<ParentPasswordResetToken>()
            .Where(t => t.ConsumedAt != null || t.ExpiresAt < expiryCutoff)
            .ExecuteDeleteAsync(stoppingToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "RetentionPurgeService tick: cleaned up {Deleted} stale password-reset token(s) (graceHours={GraceHours}).",
                deleted, graceHours);
        }
    }

    // Config reads use the indexer + int.TryParse rather than the
    // IConfiguration.GetValue<T>() extension. GetValue<T> lives in
    // Microsoft.Extensions.Configuration.Binder, which this project
    // does not reference and which the slice's "no new NuGet"
    // constraint forbids adding. Behavior is equivalent for the
    // int/int? shape we need: missing or unparseable -> default.
    // Missing config must resolve to the SHIPPED default (never 0).
    private int ReadMaxAgeDays()
        => ParseIntOrDefault(
            _config["Retention:Messages:MaxAgeDays"], DefaultMaxAgeDays);

    private int ReadBatchSize()
    {
        var raw = ParseIntOrDefault(
            _config["Retention:Messages:MaxBatchSize"], DefaultMaxBatchSize);
        if (raw < MinBatchSize) return MinBatchSize;
        if (raw > MaxAllowedBatchSize) return MaxAllowedBatchSize;
        return raw;
    }

    private TimeSpan ReadInterval()
    {
        var raw = ParseIntOrDefault(
            _config["Retention:Messages:RunIntervalMinutes"], DefaultRunIntervalMinutes);
        var clamped = raw < MinRunIntervalMinutes ? MinRunIntervalMinutes : raw;
        return TimeSpan.FromMinutes(clamped);
    }

    // Password-reset token grace window (hours). Shipped default 24.
    // Non-positive override collapses to zero grace, which means
    // "delete the moment the token expires" — still semantically sane
    // because a just-expired token has no legitimate caller. Missing
    // config resolves to the shipped default, consistent with the
    // MaxAgeDays story.
    private int ReadPasswordResetGracePeriodHours()
    {
        var raw = ParseIntOrDefault(
            _config["Retention:PasswordResetTokens:GracePeriodHours"],
            DefaultPasswordResetGracePeriodHours);
        return raw < 0 ? 0 : raw;
    }

    private static int ParseIntOrDefault(string? raw, int fallback)
        => int.TryParse(raw, out var parsed) ? parsed : fallback;
}

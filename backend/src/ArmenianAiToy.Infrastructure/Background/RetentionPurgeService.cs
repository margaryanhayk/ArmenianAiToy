using ArmenianAiToy.Application.Notifications;
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

    /// <summary>
    /// Grace window (hours) applied to
    /// <c>ParentEmailVerificationToken</c> cleanup. Same semantics as
    /// <see cref="DefaultPasswordResetGracePeriodHours"/>: consumed
    /// tokens delete unconditionally; expired tokens delete once
    /// <c>ExpiresAt &lt; UtcNow - grace</c>. Longer TTL (7 days by
    /// default) means the accumulation window is wider but the grace
    /// is still 24 h — clock skew and miss-cycles dominate the
    /// correctness concern, not the TTL.
    /// </summary>
    public const int DefaultEmailVerificationGracePeriodHours = 24;

    /// <summary>
    /// Fallback value for <c>Dormancy:Parent:WarnAfterDays</c> when the
    /// key is missing or unparseable. <b>0 (disabled)</b> — unlike the
    /// conversation-purge path, the warn-only pass has an external
    /// dependency (SMTP) and requires an explicit operator opt-in. The
    /// recommended production value when enabling is 180 days (see
    /// CLAUDE.md § Retention). The shipped <c>appsettings.json</c>
    /// carries 0 so a fresh <c>dotnet run</c> does not trigger the
    /// transport precondition before SMTP is configured.
    /// </summary>
    public const int DefaultDormancyWarnAfterDays = 0;

    /// <summary>
    /// Fallback for <c>Dormancy:Parent:WarnRefireIntervalDays</c>. A
    /// parent already warned within this window is not warned again
    /// on subsequent ticks. Minimum 1 day; the config reader
    /// floor-clamps.
    /// </summary>
    public const int DefaultDormancyRefireIntervalDays = 30;

    /// <summary>
    /// Fallback value for <c>Dormancy:Parent:AnonymizeAfterDays</c>
    /// when the key is missing or unparseable. <b>0 (disabled)</b> —
    /// destructive action is explicit operator opt-in. The
    /// recommended production value when enabling is 60 days past
    /// the warning stamp (total inactivity ≈ 8 months with the
    /// recommended <c>WarnAfterDays=180</c>). The shipped
    /// <c>appsettings.json</c> carries 0 so a fresh <c>dotnet run</c>
    /// does not trigger the transport / warn-pair precondition.
    /// </summary>
    public const int DefaultDormancyAnonymizeAfterDays = 0;

    /// <summary>
    /// Hardcoded safety floor between the parent's most recent
    /// warning and destructive action. Ensures the parent's final
    /// warn email has been in their inbox for at least this long
    /// before anonymize fires. NOT a config knob — a policy invariant.
    /// Protects against an operator flipping
    /// <c>Dormancy:Parent:AnonymizeAfterDays</c> from 0 to a positive
    /// value and immediately anonymizing parents who were warned
    /// yesterday.
    /// </summary>
    public const int DormancyAnonymizeGraceDays = 7;

    /// <summary>
    /// Fallback value for <c>Dormancy:Devices:WarnAfterDays</c> when
    /// the key is missing or unparseable. <b>0 (disabled)</b> —
    /// destructive operator opt-in. The recommended production value
    /// when enabling is 365 days. The shipped <c>appsettings.json</c>
    /// carries 0 so a fresh <c>dotnet run</c> does not trigger the
    /// transport precondition before SMTP is configured.
    /// <para>
    /// Deliberately <b>distinct</b> from the existing
    /// <c>Dormancy:Devices:NotSeenDays</c> (which drives the
    /// dashboard's reporting flag) so reporting and action thresholds
    /// can evolve independently. Reusing the reporting key would
    /// conflate two concerns and force them to move together.
    /// </para>
    /// </summary>
    public const int DefaultDormancyDevicesWarnAfterDays = 0;

    /// <summary>
    /// Fallback for <c>Dormancy:Devices:WarnRefireIntervalDays</c>.
    /// Same shape as the parent's <c>WarnRefireIntervalDays</c>: a
    /// device already warned within this window is not re-warned on
    /// subsequent ticks. Floor-clamped to 1.
    /// </summary>
    public const int DefaultDormancyDevicesWarnRefireIntervalDays = 30;

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
        await PurgeStaleEmailVerificationTokensAsync(db, stoppingToken);
        // Anonymize before warn is deliberate. If warn ran first it
        // would stamp DormancyWarnedAt to "now" for every refire-due
        // parent, and the 7-day grace-floor condition on anonymize
        // would then exclude them for another week — effectively
        // adding 7 days of extra inactivity per refire cycle. Running
        // anonymize first lets eligible parents be scrubbed on the
        // same tick where the refire would have fired; the warn pass
        // then naturally skips them via its AnonymizedAt == null
        // filter. Non-anonymize-eligible parents get their normal
        // refire on the same tick, so no warn is lost.
        await AnonymizeDormantParentsAsync(db, stoppingToken);
        await WarnDormantParentsAsync(scope.ServiceProvider, db, stoppingToken);
        // Device-warn slots LAST. Independent of parent passes —
        // device dormancy is per-device, recipient list is the
        // device's verified linked parents, and partial fan-out
        // failure handling is local to this pass. Disabled gate
        // (Dormancy:Devices:WarnAfterDays <= 0) short-circuits.
        await WarnDormantDevicesAsync(scope.ServiceProvider, db, stoppingToken);
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
    /// <summary>
    /// Warn-only dormant-parent pass. Finds parents whose
    /// <see cref="Parent.LastLoginAt"/> is past the configured
    /// threshold and that either have never been warned or were last
    /// warned before the refire interval, sends a warning email via
    /// the configured <see cref="INotifier"/>, and — on successful
    /// delivery only — stamps <see cref="Parent.DormancyWarnedAt"/>
    /// and writes one <see cref="Domain.Enums.AuditEventType.ParentDormancyWarned"/>
    /// audit row.
    /// <para>
    /// <b>Non-destructive by design.</b> No deletes, no disables, no
    /// unlinks, no cascades. A failed notifier call (bool <c>false</c>)
    /// leaves the parent row untouched so the next tick retries; the
    /// audit row is NOT written on failure, preserving the "audit
    /// reflects what actually happened" invariant.
    /// </para>
    /// <para>
    /// <b>Null <c>LastLoginAt</c> parents are excluded entirely</b> —
    /// the column was introduced recently and pre-migration rows
    /// carry null; thresholding them would warn accounts the repo has
    /// no activity signal on, which is not a safe default. Those
    /// parents enter the dormant set only once they log in at least
    /// once under the current schema.
    /// </para>
    /// </summary>
    private async Task WarnDormantParentsAsync(
        IServiceProvider scopedProvider, AppDbContext db, CancellationToken stoppingToken)
    {
        var warnAfterDays = ReadDormancyWarnAfterDays();
        if (warnAfterDays <= 0)
        {
            // Disabled. Silently skip — no log spam per tick. The
            // outer MaxAgeDays<=0 disable gate already logged a
            // once-per-tick "worker disabled" line.
            return;
        }
        var refireIntervalDays = ReadDormancyRefireIntervalDays();
        // Read the destructive threshold too — drives deleteAtUtc on
        // each warn email so the parent sees the scheduled action
        // date from the first warning onwards when destructive is
        // enabled. 0 (disabled) means every warn carries null.
        var anonymizeAfterDays = ReadDormancyAnonymizeAfterDays();
        var nowUtc = DateTime.UtcNow;
        var dormantCutoff = nowUtc - TimeSpan.FromDays(warnAfterDays);
        var refireCutoff = nowUtc - TimeSpan.FromDays(refireIntervalDays);

        // Eligibility: signal exists, past threshold, not already
        // warned inside the refire window, and not already anonymized.
        // The explicit `!= null` guard makes the SQL translator emit
        // `IS NOT NULL` rather than relying on null-comparison
        // semantics. AnonymizedAt == null is defensively explicit
        // even though a scrubbed parent has LastLoginAt == null
        // (which would already exclude them via the next check).
        var eligible = await db.Set<Parent>()
            .Where(p =>
                p.AnonymizedAt == null
                && p.EmailVerifiedAt != null
                && p.LastLoginAt != null
                && p.LastLoginAt < dormantCutoff
                && (p.DormancyWarnedAt == null || p.DormancyWarnedAt < refireCutoff))
            .ToListAsync(stoppingToken);

        if (eligible.Count == 0)
            return;

        var notifier = scopedProvider.GetRequiredService<INotifier>();
        int warned = 0;
        foreach (var parent in eligible)
        {
            // Compute the scheduled anonymize date when destructive is
            // enabled so the outgoing email body can state it plainly.
            // Based on the parent's LastLoginAt (never null here — the
            // eligibility Where clause filtered those out), plus the
            // configured warn + anonymize thresholds. When destructive
            // is disabled (AnonymizeAfterDays <= 0), deleteAtUtc stays
            // null and the warn-only body renders unchanged.
            DateTime? deleteAtUtc = anonymizeAfterDays > 0
                ? parent.LastLoginAt!.Value.AddDays(warnAfterDays + anonymizeAfterDays)
                : null;

            bool delivered;
            try
            {
                delivered = await notifier.SendDormancyWarningAsync(
                    parent.Email, deleteAtUtc, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Worker is shutting down. Let the cancellation bubble
                // up; the outer ExecuteAsync catches it and exits
                // cleanly.
                throw;
            }
            catch (Exception ex)
            {
                // Defensive: the SmtpNotifier already swallows non-OCE
                // exceptions and returns false, but a future notifier
                // impl might not. Treat an unexpected throw as "not
                // delivered" so we do not stamp or audit a send that
                // did not happen.
                _logger.LogWarning(ex,
                    "RetentionPurgeService warn pass: notifier threw unexpectedly for Parent {ParentId}; treating as not delivered.",
                    parent.Id);
                delivered = false;
            }

            if (!delivered)
                continue;

            parent.DormancyWarnedAt = nowUtc;
            var audit = AuditEvent.ParentDormancyWarned(
                lastLoginAtUtc: parent.LastLoginAt!.Value,
                warnThresholdDays: warnAfterDays,
                refireIntervalDays: refireIntervalDays);
            db.Set<AuditEvent>().Add(audit);
            AppMeter.AuditEventsWritten.Add(1,
                new KeyValuePair<string, object?>("event_type", audit.EventType.ToString()));
            warned++;
        }

        if (warned > 0)
        {
            await db.SaveChangesAsync(stoppingToken);
            _logger.LogInformation(
                "RetentionPurgeService tick: sent dormancy warnings to {Warned} parent(s) (threshold={WarnAfterDays}d, refire={RefireIntervalDays}d).",
                warned, warnAfterDays, refireIntervalDays);
        }
    }

    /// <summary>
    /// Destructive dormant-parent pass. Anonymizes parents who have
    /// been warned at least <see cref="DormancyAnonymizeGraceDays"/>
    /// days ago, have total inactivity past
    /// <c>WarnAfterDays + AnonymizeAfterDays</c>, and have not logged
    /// in since the warning fired. <b>Irreversible</b> at the
    /// application level — scrubs <see cref="Parent.Email"/> /
    /// <see cref="Parent.PasswordHash"/> to empty strings, nulls
    /// <see cref="Parent.LastLoginAt"/> and
    /// <see cref="Parent.DormancyWarnedAt"/>, stamps
    /// <see cref="Parent.AnonymizedAt"/>. The <c>Parent</c> row
    /// survives as an anchor for audit-event FKs / structural
    /// references; credential material does not.
    /// <para>
    /// Per eligible parent: removes all <c>ParentDevice</c> rows, and
    /// for each device newly orphaned, cascade-deletes via the same
    /// pattern <c>ParentService.DeleteAccountAsync</c> uses —
    /// schema FK cascades handle children / conversations / messages
    /// once the <c>Device</c> row is removed. Devices still linked
    /// to another parent are preserved; only the ParentDevice link
    /// for this parent is removed.
    /// </para>
    /// <para>
    /// <b>Atomicity</b> is per-parent (one <c>SaveChangesAsync</c>
    /// per eligible parent). A worker crash mid-pass leaves
    /// already-anonymized parents fully written and not-yet-processed
    /// parents untouched. The orphan walk uses an explicit
    /// <c>pd.ParentId != parentId</c> filter because the just-queued
    /// <c>ParentDevice</c> removals are change-tracker-Deleted but
    /// still visible to DB queries until the per-parent save fires.
    /// </para>
    /// <para>
    /// One <see cref="Domain.Enums.AuditEventType.ParentDormancyAnonymized"/>
    /// row per anonymized parent (system actor,
    /// <c>ActorParentId = null</c>, invisible to parent-facing reads).
    /// No <c>ParentDeviceUnlinked</c> rows are written during the
    /// cascade — their <c>ActorParentId</c> would leak the anonymized
    /// parent's identity into the audit feed, which breaks the
    /// "minimize retained PII" ethos. Counts-only metadata on the
    /// anonymize row captures the same information in aggregate.
    /// </para>
    /// </summary>
    private async Task AnonymizeDormantParentsAsync(
        AppDbContext db, CancellationToken stoppingToken)
    {
        var anonymizeAfterDays = ReadDormancyAnonymizeAfterDays();
        if (anonymizeAfterDays <= 0)
        {
            // Disabled. Shipped default and code fallback both 0.
            // Silently skip — the DI-time precondition has already
            // enforced that an enabled destructive pass requires
            // warn-enabled + SMTP at startup, so we do not need to
            // re-check those here.
            return;
        }

        // Re-read warn thresholds here too so the anonymize pass is
        // self-contained — it computes its own cutoff purely from
        // config, without depending on state set by the warn pass.
        var warnAfterDays = ReadDormancyWarnAfterDays();
        var warnRefireIntervalDays = ReadDormancyRefireIntervalDays();

        var nowUtc = DateTime.UtcNow;
        var destructiveCutoff = nowUtc - TimeSpan.FromDays(warnAfterDays + anonymizeAfterDays);
        var graceCutoff = nowUtc - TimeSpan.FromDays(DormancyAnonymizeGraceDays);

        // Six-condition eligibility. See CLAUDE.md § Parent anonymize
        // for the full contract. The two `!= null` / `== null` guards
        // are defensively explicit even where a prior inequality
        // would also exclude null rows.
        var eligible = await db.Set<Parent>()
            .Where(p =>
                p.AnonymizedAt == null
                && p.LastLoginAt != null
                && p.LastLoginAt < destructiveCutoff
                && p.DormancyWarnedAt != null
                && p.DormancyWarnedAt > p.LastLoginAt
                && p.DormancyWarnedAt < graceCutoff)
            .ToListAsync(stoppingToken);

        if (eligible.Count == 0)
            return;

        int anonymized = 0;
        foreach (var parent in eligible)
        {
            var parentId = parent.Id;

            // Snapshot linked device ids BEFORE queueing the
            // ParentDevice removals — otherwise the orphan walk
            // below would see an empty device set.
            var linkedDeviceIds = await db.Set<ParentDevice>()
                .Where(pd => pd.ParentId == parentId)
                .Select(pd => pd.DeviceId)
                .ToListAsync(stoppingToken);

            // Queue removal of all ParentDevice rows for this parent.
            var links = await db.Set<ParentDevice>()
                .Where(pd => pd.ParentId == parentId)
                .ToListAsync(stoppingToken);
            if (links.Count > 0)
                db.Set<ParentDevice>().RemoveRange(links);

            // Orphan-aware device cleanup. The `ParentId != parentId`
            // filter excludes the change-tracker-Deleted rows from
            // this iteration — they are still in the DB until the
            // per-parent SaveChangesAsync below fires. Any remaining
            // link to the device from a different parent keeps the
            // device alive.
            int orphansDeleted = 0;
            foreach (var deviceId in linkedDeviceIds)
            {
                var stillLinked = await db.Set<ParentDevice>()
                    .AnyAsync(pd => pd.DeviceId == deviceId && pd.ParentId != parentId,
                              stoppingToken);
                if (stillLinked)
                    continue;

                var device = await db.Set<Device>()
                    .FindAsync(new object?[] { deviceId }, stoppingToken);
                if (device != null)
                {
                    db.Set<Device>().Remove(device);
                    orphansDeleted++;
                }
            }

            // Scrub the Parent row in place. Post-save:
            //   Email = "", PasswordHash = "", LastLoginAt = null,
            //   DormancyWarnedAt = null, AnonymizedAt = nowUtc.
            // The row persists as an audit-event anchor; credential
            // material does not. Login naturally fails for this row
            // because BCrypt.Verify against an empty hash cannot
            // succeed — no separate "disabled" flag is required.
            parent.Email = "";
            parent.PasswordHash = "";
            parent.LastLoginAt = null;
            parent.DormancyWarnedAt = null;
            parent.AnonymizedAt = nowUtc;

            // System-actor audit row — counts-only metadata, no email,
            // no parent id, no PII. The ActorParentId=null contract
            // keeps this row invisible to every parent-facing read
            // surface via the existing ActorParentId==parentId filter.
            var audit = AuditEvent.ParentDormancyAnonymized(
                warnAfterDays: warnAfterDays,
                anonymizeAfterDays: anonymizeAfterDays,
                warnRefireIntervalDays: warnRefireIntervalDays,
                linkedDevicesUnlinked: linkedDeviceIds.Count,
                orphanDevicesCascaded: orphansDeleted);
            db.Set<AuditEvent>().Add(audit);
            AppMeter.AuditEventsWritten.Add(1,
                new KeyValuePair<string, object?>("event_type", audit.EventType.ToString()));

            // Per-parent atomicity: everything above (link removals,
            // orphan device deletes, parent scrub, audit insert)
            // flushes together. A crash between parents leaves prior
            // anonymizes complete; next tick picks up the remainder.
            await db.SaveChangesAsync(stoppingToken);
            anonymized++;
        }

        _logger.LogInformation(
            "RetentionPurgeService tick: anonymized {Anonymized} dormant parent(s) (threshold={WarnAfterDays}+{AnonymizeAfterDays}d, grace={GraceDays}d).",
            anonymized, warnAfterDays, anonymizeAfterDays, DormancyAnonymizeGraceDays);
    }

    /// <summary>
    /// Warn-only dormant-device pass. Finds devices whose
    /// <see cref="Device.LastSeenAt"/> is past
    /// <c>Dormancy:Devices:WarnAfterDays</c> (or whose existing
    /// <see cref="Device.DormancyWarnedAt"/> is past the refire
    /// window) and that have at least one verified linked parent,
    /// fans out a notifier call per verified linked parent, and —
    /// on at-least-one-success — stamps
    /// <see cref="Device.DormancyWarnedAt"/> and writes one
    /// <see cref="Domain.Enums.AuditEventType.DeviceDormancyWarned"/>
    /// audit row.
    /// <para>
    /// <b>Non-destructive by design.</b> No deletes, no unlinks, no
    /// cascade. The first slice ships warn-only; a future slice may
    /// revisit destructive action with operational data in hand.
    /// </para>
    /// <para>
    /// <b>Verified-recipient gate</b>: only parents with
    /// <c>EmailVerifiedAt != null</c> AND <c>AnonymizedAt == null</c>
    /// receive notifications. A device whose entire linked-parent
    /// set is unverified and/or anonymized is silently skipped — no
    /// stamp, no audit row.
    /// </para>
    /// <para>
    /// <b>Partial-failure rule</b>: if at least one of the per-
    /// recipient notifier calls returned <c>true</c>, the device is
    /// stamped and audited. If every call returned <c>false</c>
    /// (every per-recipient send swallowed an SMTP failure), the
    /// device is left unstamped so the next tick retries. Audit
    /// metadata's <c>verified_recipients_notified</c> reflects the
    /// successful-send count, not the attempt count.
    /// </para>
    /// </summary>
    private async Task WarnDormantDevicesAsync(
        IServiceProvider scopedProvider, AppDbContext db, CancellationToken stoppingToken)
    {
        var warnAfterDays = ReadDormancyDevicesWarnAfterDays();
        if (warnAfterDays <= 0)
        {
            // Disabled. Silently skip — no log spam per tick. The
            // outer MaxAgeDays<=0 disable gate already logged a
            // once-per-tick "worker disabled" line.
            return;
        }
        var refireIntervalDays = ReadDormancyDevicesWarnRefireIntervalDays();
        var nowUtc = DateTime.UtcNow;
        var dormantCutoff = nowUtc - TimeSpan.FromDays(warnAfterDays);
        var refireCutoff = nowUtc - TimeSpan.FromDays(refireIntervalDays);

        // Eligibility: device past threshold; not yet warned (or
        // warned past refire); has at least one verified linked
        // parent (existence check, not the recipient list — recipient
        // enumeration happens per-device below for the fan-out).
        var eligibleDevices = await db.Set<Device>()
            .Where(d =>
                d.LastSeenAt < dormantCutoff
                && (d.DormancyWarnedAt == null || d.DormancyWarnedAt < refireCutoff)
                && db.Set<ParentDevice>()
                    .Where(pd => pd.DeviceId == d.Id)
                    .Join(db.Set<Parent>(), pd => pd.ParentId, p => p.Id,
                        (pd, p) => p)
                    .Any(p => p.EmailVerifiedAt != null && p.AnonymizedAt == null))
            .ToListAsync(stoppingToken);

        if (eligibleDevices.Count == 0)
            return;

        var notifier = scopedProvider.GetRequiredService<INotifier>();
        int devicesWarned = 0;

        foreach (var device in eligibleDevices)
        {
            // Materialize the device's linked-parent set in two slices
            // so the audit metadata can carry the total + verified
            // counts even if no notifier calls go out.
            var linkedParents = await db.Set<ParentDevice>()
                .Where(pd => pd.DeviceId == device.Id)
                .Join(db.Set<Parent>(), pd => pd.ParentId, p => p.Id,
                    (pd, p) => p)
                .ToListAsync(stoppingToken);
            var linkedParentsTotal = linkedParents.Count;
            var verifiedLinkedParents = linkedParents
                .Where(p => p.EmailVerifiedAt != null && p.AnonymizedAt == null)
                .ToList();
            var linkedParentsVerified = verifiedLinkedParents.Count;
            // Defensive: the eligibility query already filtered for
            // at-least-one-verified, but if a row was anonymized
            // between the query and this enumeration, skip cleanly.
            if (linkedParentsVerified == 0)
                continue;

            int verifiedRecipientsNotified = 0;
            foreach (var parent in verifiedLinkedParents)
            {
                bool delivered;
                try
                {
                    delivered = await notifier.SendDormantDeviceWarningAsync(
                        parent.Email, device.Name, device.LastSeenAt,
                        deleteAtUtc: null, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Same defensive shape as the parent-warn pass —
                    // a future notifier impl that throws on non-OCE
                    // is treated as "not delivered" for this recipient.
                    _logger.LogWarning(ex,
                        "RetentionPurgeService device-warn pass: notifier threw unexpectedly for Parent {ParentId} / Device {DeviceId}; treating as not delivered.",
                        parent.Id, device.Id);
                    delivered = false;
                }
                if (delivered)
                    verifiedRecipientsNotified++;
            }

            if (verifiedRecipientsNotified == 0)
            {
                // All recipients failed this tick. Leave the device
                // unstamped so the next tick retries. No audit row —
                // the audit reflects what actually happened, and
                // nothing happened from a delivery perspective.
                continue;
            }

            device.DormancyWarnedAt = nowUtc;
            var audit = AuditEvent.DeviceDormancyWarned(
                deviceId: device.Id,
                warnAfterDays: warnAfterDays,
                warnRefireIntervalDays: refireIntervalDays,
                lastSeenAtUtc: device.LastSeenAt,
                linkedParentsTotal: linkedParentsTotal,
                linkedParentsVerified: linkedParentsVerified,
                verifiedRecipientsNotified: verifiedRecipientsNotified);
            db.Set<AuditEvent>().Add(audit);
            AppMeter.AuditEventsWritten.Add(1,
                new KeyValuePair<string, object?>("event_type", audit.EventType.ToString()));

            // Per-device atomicity: stamp + audit + linked changes
            // flush together. A worker crash mid-pass leaves
            // already-warned devices fully recorded and not-yet-
            // processed devices untouched.
            await db.SaveChangesAsync(stoppingToken);
            devicesWarned++;
        }

        if (devicesWarned > 0)
        {
            _logger.LogInformation(
                "RetentionPurgeService tick: warned {DevicesWarned} dormant device(s) (threshold={WarnAfterDays}d, refire={RefireIntervalDays}d).",
                devicesWarned, warnAfterDays, refireIntervalDays);
        }
    }

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

    /// <summary>
    /// Deletes stale <c>ParentEmailVerificationToken</c> rows. Same
    /// rule as <see cref="PurgeStalePasswordResetTokensAsync"/>:
    /// consumed tokens are deleted unconditionally; expired tokens
    /// are deleted once they are past the configured grace window.
    /// No audit row — short-lived operational state, not a
    /// destructive parent action. Uses <c>ExecuteDeleteAsync</c>
    /// for a single <c>DELETE WHERE</c> statement without
    /// materializing any rows; no hash, no parent id, no timestamp
    /// lands on the worker process.
    /// </summary>
    private async Task PurgeStaleEmailVerificationTokensAsync(
        AppDbContext db, CancellationToken stoppingToken)
    {
        var graceHours = ReadEmailVerificationGracePeriodHours();
        var expiryCutoff = DateTime.UtcNow - TimeSpan.FromHours(graceHours);

        var deleted = await db.Set<ParentEmailVerificationToken>()
            .Where(t => t.ConsumedAt != null || t.ExpiresAt < expiryCutoff)
            .ExecuteDeleteAsync(stoppingToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "RetentionPurgeService tick: cleaned up {Deleted} stale email-verification token(s) (graceHours={GraceHours}).",
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

    // Email-verification token grace window. Same shape and
    // semantics as the password-reset grace reader.
    private int ReadEmailVerificationGracePeriodHours()
    {
        var raw = ParseIntOrDefault(
            _config["Retention:EmailVerificationTokens:GracePeriodHours"],
            DefaultEmailVerificationGracePeriodHours);
        return raw < 0 ? 0 : raw;
    }

    // Dormancy:Devices:WarnAfterDays — fallback 0 (disabled).
    // Distinct from Dormancy:Devices:NotSeenDays (the dashboard
    // reporting threshold). Reporting and action thresholds are
    // decoupled by design so they can evolve independently.
    private int ReadDormancyDevicesWarnAfterDays()
        => ParseIntOrDefault(
            _config["Dormancy:Devices:WarnAfterDays"],
            DefaultDormancyDevicesWarnAfterDays);

    // Dormancy:Devices:WarnRefireIntervalDays — fallback 30, floor
    // clamp 1. Same shape as the parent's
    // WarnRefireIntervalDays reader.
    private int ReadDormancyDevicesWarnRefireIntervalDays()
    {
        var raw = ParseIntOrDefault(
            _config["Dormancy:Devices:WarnRefireIntervalDays"],
            DefaultDormancyDevicesWarnRefireIntervalDays);
        return raw < 1 ? 1 : raw;
    }

    // Dormancy:Parent:WarnAfterDays — fallback 0 (disabled). Deliberately
    // different from MaxAgeDays' "default is the shipped production
    // value" story because the warn pass has an external dependency
    // (SMTP) that requires explicit operator opt-in. Missing key =
    // disabled; explicit 0 = disabled; explicit positive = enabled
    // (and the startup precondition enforces SMTP).
    private int ReadDormancyWarnAfterDays()
        => ParseIntOrDefault(
            _config["Dormancy:Parent:WarnAfterDays"], DefaultDormancyWarnAfterDays);

    // Dormancy:Parent:WarnRefireIntervalDays — fallback 30 days.
    // Floor-clamped to 1 so a pathological 0/-5 override does not
    // turn the pass into a re-warn-every-tick storm.
    private int ReadDormancyRefireIntervalDays()
    {
        var raw = ParseIntOrDefault(
            _config["Dormancy:Parent:WarnRefireIntervalDays"],
            DefaultDormancyRefireIntervalDays);
        return raw < 1 ? 1 : raw;
    }

    // Dormancy:Parent:AnonymizeAfterDays — fallback 0 (disabled).
    // Positive value enables the destructive pass; the DI-time
    // precondition already enforced that warn-enabled + SMTP are
    // paired before we get here. Non-positive values disable the
    // pass entirely; no clamp is applied because 0 is the safe
    // disable signal.
    private int ReadDormancyAnonymizeAfterDays()
        => ParseIntOrDefault(
            _config["Dormancy:Parent:AnonymizeAfterDays"],
            DefaultDormancyAnonymizeAfterDays);

    private static int ParseIntOrDefault(string? raw, int fallback)
        => int.TryParse(raw, out var parsed) ? parsed : fallback;
}

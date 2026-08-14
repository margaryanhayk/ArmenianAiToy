using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Superuser internal console API — a READ-ONLY god view across ALL
/// parents, devices, conversations, stories, audit, and cost. This is the
/// operator surface; it is NOT parent-scoped.
///
/// <para>
/// <b>Auth:</b> gated entirely by the <c>Internal:AdminToken</c> bearer
/// check wired as inline middleware in <c>Program.cs</c> (see
/// <see cref="ArmenianAiToy.Api.Observability.InternalAdminAuth"/>).
/// There is no <c>[Authorize]</c> attribute here because the gate is the
/// token middleware, not the parent JWT pipeline — and the gate is
/// fail-closed (404) by default, so an un-configured deploy exposes
/// nothing.
/// </para>
///
/// <para>
/// <b>Read-only by design (Phase 1).</b> No mutations: an admin token that
/// could pause devices, promote drafts, or delete data is a much larger
/// blast radius and is deliberately deferred to a later, separately-
/// approved phase. Every action here is a GET.
/// </para>
///
/// <para>
/// <b>Secret invariants (do not regress):</b> the response DTOs never
/// carry <c>Device.ApiKey</c> / <c>Device.ApiKeyHash</c> or
/// <c>Parent.PasswordHash</c>; Google linkage is a bool, never the raw
/// subject. Enforced by construction in <c>InternalDtos.cs</c> and pinned
/// by tests.
/// </para>
/// </summary>
[ApiController]
[Route("api/internal")]
public class InternalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICuratedStoryLibrary _library;
    private readonly OpenAICostMeter _costMeter;
    private readonly LibraryStoryQuestionService _questions;
    private readonly IModerationService _moderation;
    private readonly IConfiguration _config;
    private readonly ILogger<InternalController> _logger;
    private readonly Application.Auth.OperatorSessionStore _sessions;

    public InternalController(
        AppDbContext db, ICuratedStoryLibrary library, OpenAICostMeter costMeter,
        LibraryStoryQuestionService questions, IModerationService moderation,
        IConfiguration config, ILogger<InternalController> logger,
        Application.Auth.OperatorSessionStore? sessions = null)
    {
        _db = db;
        _library = library;
        _costMeter = costMeter;
        _questions = questions;
        _moderation = moderation;
        _config = config;
        _logger = logger;
        _sessions = sessions ?? new Application.Auth.OperatorSessionStore();
    }

    /// <summary>System-wide counts + today's activity + total in-process
    /// OpenAI cost for the current UTC day + DB reachability.</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var todayUtc = now.Date;

        var deviceIds = await _db.Devices.Select(d => d.Id).ToListAsync(ct);
        decimal costToday = 0m;
        foreach (var id in deviceIds)
        {
            costToday += _costMeter.GetCurrentTotal(id, now);
        }

        bool dbOk;
        try { dbOk = await _db.Database.CanConnectAsync(ct); }
        catch { dbOk = false; }

        var dto = new AdminOverviewDto(
            Devices: deviceIds.Count,
            Parents: await _db.Parents.CountAsync(ct),
            Children: await _db.Children.CountAsync(ct),
            Conversations: await _db.Conversations.CountAsync(ct),
            Messages: await _db.Messages.CountAsync(ct),
            FlaggedMessages: await _db.Messages.CountAsync(m => m.SafetyFlag != SafetyFlag.Clean, ct),
            MessagesToday: await _db.Messages.CountAsync(m => m.Timestamp >= todayUtc, ct),
            FlaggedToday: await _db.Messages.CountAsync(
                m => m.Timestamp >= todayUtc && m.SafetyFlag != SafetyFlag.Clean, ct),
            PausedDevices: await _db.Devices.CountAsync(d => d.IsPaused, ct),
            CostTodayUsd: costToday,
            DatabaseReachable: dbOk,
            GeneratedAtUtc: now);
        return Ok(dto);
    }

    /// <summary>Every device (safe fields only) with linked-parent count,
    /// nested children, and today's in-process OpenAI cost.</summary>
    [HttpGet("devices")]
    public async Task<IActionResult> Devices(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var devices = await _db.Devices.AsNoTracking().ToListAsync(ct);
        var children = await _db.Children.AsNoTracking().ToListAsync(ct);
        var linkCounts = await _db.ParentDevices.AsNoTracking()
            .GroupBy(pd => pd.DeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Count, ct);

        var childrenByDevice = children
            .GroupBy(c => c.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // What the manifest currently advertises, through the same helper the
        // manifest service uses — the console and the parent dashboard must
        // never disagree about what "up to date" means.
        //
        // PER DEVICE since per-toy entitlement landed: a toy denied a story is
        // not behind on it. Left fleet-wide, every restricted toy would report
        // stale and name the withheld story as missing — the console would be
        // accusing a toy of failing to fetch something the operator withheld
        // from it on purpose. One query for the whole table, then a pure
        // in-memory narrowing; a device with no rows gets the baseline back by
        // reference.
        var contentBaseline = ContentSyncOptions.Resolve(_config);
        var fleetAdvertised = contentBaseline.AdvertisedStoryVersions();
        var advertisedByDevice = (await _db.Set<DeviceContentOverride>()
                .AsNoTracking()
                .Select(o => new { o.DeviceId, o.ItemKind, o.ItemKey, o.Mode })
                .ToListAsync(ct))
            .GroupBy(o => o.DeviceId)
            .ToDictionary(
                g => g.Key,
                g => DeviceContentEntitlement
                    .Apply(contentBaseline, g.Select(r => (r.ItemKind, r.ItemKey, r.Mode)))
                    .AdvertisedStoryVersions());

        IReadOnlyCollection<(string StoryId, int Version)> AdvertisedFor(Guid id)
            => advertisedByDevice.TryGetValue(id, out var own) ? own : fleetAdvertised;

        var rows = devices.Select(d => new AdminDeviceDto(
            Id: d.Id,
            Name: d.Name,
            MacAddress: d.MacAddress,
            FirmwareVersion: d.FirmwareVersion,
            LastOtaStatus: d.LastOtaStatus,
            OtaHealth: DeviceOtaHealth.Resolve(d.LastOtaStatus, d.LastSeenAt, now),
            RegisteredAt: d.RegisteredAt,
            LastSeenAt: d.LastSeenAt,
            IsPaused: d.IsPaused,
            IsRevoked: d.IsRevoked,
            BedtimeStart: d.BedtimeStart,
            BedtimeEnd: d.BedtimeEnd,
            TimeZone: d.TimeZone,
            StoryEnabled: d.StoryEnabled,
            GameEnabled: d.GameEnabled,
            RiddleEnabled: d.RiddleEnabled,
            CuriosityEnabled: d.CuriosityEnabled,
            DormancyWarnedAt: d.DormancyWarnedAt,
            LinkedParents: linkCounts.TryGetValue(d.Id, out var lc) ? lc : 0,
            CostTodayUsd: _costMeter.GetCurrentTotal(d.Id, now),
            ChildrenList: (childrenByDevice.TryGetValue(d.Id, out var kids) ? kids : new())
                .Select(c => new AdminChildDto(
                    c.Id, c.Name, c.Gender.ToString(), c.GetAge(),
                    c.StoryEnabled, c.GameEnabled, c.RiddleEnabled, c.CuriosityEnabled))
                .ToList())
        {
            ContentHealth = DeviceContentHealth.Resolve(
                d.ContentStories, AdvertisedFor(d.Id), d.LastSeenAt, now,
                DeviceContentHealth.DefaultOnlineThresholdSeconds,
                d.ContentSyncStatus, d.ResetReason, d.BootCount),
            MissingStoryIds = DeviceContentHealth.MissingStoryIds(
                d.ContentStories, AdvertisedFor(d.Id)),
            ContentStories = d.ContentStories,
            ContentIndexSchema = d.ContentIndexSchema,
            ContentGameClips = d.ContentGameClips,
            ContentVoiceClips = d.ContentVoiceClips,
            ContentMusicTracks = d.ContentMusicTracks,
            ContentReportedAt = d.ContentReportedAt,
            ContentSyncStatus = d.ContentSyncStatus,
            ContentSyncError = d.ContentSyncError,
            ContentSyncReportedAt = d.ContentSyncReportedAt,
            ContentSyncedAt = d.ContentSyncedAt,
            ResetReason = d.ResetReason,
            BootCount = d.BootCount,
            BoardModel = d.BoardModel,
            FirmwareBuild = d.FirmwareBuild,
            PartitionName = d.PartitionName,
            FirmwareReportedAt = d.FirmwareReportedAt,
        })
            .OrderByDescending(d => d.LastSeenAt)
            .ToList();

        return Ok(new { devices = rows });
    }

    /// <summary>Every parent (safe fields only — NEVER PasswordHash) with
    /// linked-device count and audit-event count. Google linkage is a bool.</summary>
    [HttpGet("parents")]
    public async Task<IActionResult> Parents(CancellationToken ct)
    {
        var parents = await _db.Parents.AsNoTracking().ToListAsync(ct);
        var linkCounts = await _db.ParentDevices.AsNoTracking()
            .GroupBy(pd => pd.ParentId)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);
        var auditCounts = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.ActorParentId != null)
            .GroupBy(a => a.ActorParentId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);

        var rows = parents.Select(p => new AdminParentDto(
            Id: p.Id,
            Email: p.Email,
            RegisteredAt: p.RegisteredAt,
            EmailVerifiedAt: p.EmailVerifiedAt,
            LastLoginAt: p.LastLoginAt,
            AnonymizedAt: p.AnonymizedAt,
            TermsVersion: p.TermsVersion,
            GoogleLinked: p.GoogleSubject != null,
            LinkedDevices: linkCounts.TryGetValue(p.Id, out var lc) ? lc : 0,
            AuditEvents: auditCounts.TryGetValue(p.Id, out var ac) ? ac : 0))
            .OrderByDescending(p => p.RegisteredAt)
            .ToList();

        return Ok(new { parents = rows });
    }

    /// <summary>Every story currently in the runtime library (curated +
    /// any side-loaded drafts) with its metadata.</summary>
    [HttpGet("stories")]
    public IActionResult Stories()
    {
        // The story actually served on the bench / by the device, from
        // Story:DefaultStoryId. The other library entries are built-in samples;
        // marking the default makes "the one active story" unmistakable without
        // hiding the curated catalog.
        var defaultId = _config["Story:DefaultStoryId"];
        var rows = _library.ListAvailable().Select(s => new AdminStoryDto(
            Id: s.Id,
            Title: s.Title,
            MinAge: s.MinAge,
            MaxAge: s.MaxAge,
            Tone: s.Tone,
            Segments: s.Segments.Count,
            BedtimeSafe: s.BedtimeSafe,
            HasReflectionText: !string.IsNullOrWhiteSpace(s.ReflectionText),
            ReflectionQuestions: s.ReflectionQuestions.Count,
            IsDefault: !string.IsNullOrEmpty(defaultId)
                && string.Equals(s.Id, defaultId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Title, StringComparer.Ordinal)
            .ToList();
        return Ok(new { stories = rows });
    }

    /// <summary>All non-Clean messages across ALL devices, newest first.</summary>
    [HttpGet("flagged")]
    public async Task<IActionResult> Flagged(
        [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0 || limit < 1) return BadRequest(new { error = "Invalid pagination." });
        limit = Math.Min(limit, 100);

        var rows = await _db.Messages.AsNoTracking()
            .Where(m => m.SafetyFlag != SafetyFlag.Clean)
            .OrderByDescending(m => m.Timestamp)
            .Skip(offset).Take(limit)
            .Select(m => new
            {
                m.Id,
                m.ConversationId,
                DeviceId = m.Conversation.DeviceId,
                Role = m.Role,
                m.Content,
                Flag = m.SafetyFlag,
                m.Timestamp
            })
            .ToListAsync(ct);

        var dtos = rows.Select(r => new AdminFlaggedMessageDto(
            r.Id, r.ConversationId, r.DeviceId, r.Role.ToString(),
            Snippet(r.Content), r.Flag.ToString(), r.Timestamp)).ToList();
        await AuditAccessAsync("flagged", targetId: null, dtos.Count, ct);
        return Ok(new { messages = dtos });
    }

    /// <summary>Conversation summaries — all devices, or one when
    /// <paramref name="deviceId"/> is supplied. Newest first.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(
        [FromQuery] Guid? deviceId = null,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0 || limit < 1) return BadRequest(new { error = "Invalid pagination." });
        limit = Math.Min(limit, 100);

        var q = _db.Conversations.AsNoTracking().AsQueryable();
        if (deviceId.HasValue) q = q.Where(c => c.DeviceId == deviceId.Value);

        var rows = await q
            .OrderByDescending(c => c.StartedAt)
            .Skip(offset).Take(limit)
            .Select(c => new AdminConversationSummaryDto(
                c.Id,
                c.DeviceId,
                c.ChildId,
                c.StartedAt,
                c.EndedAt,
                c.Messages.Count,
                c.Messages.Count(m => m.SafetyFlag != SafetyFlag.Clean),
                c.Messages.OrderBy(m => m.Timestamp).Select(m => m.Content).FirstOrDefault() ?? string.Empty))
            .ToListAsync(ct);

        // Trim snippets after materialization (string ops don't translate cleanly).
        var dtos = rows.Select(r => r with { Snippet = Snippet(r.Snippet) }).ToList();
        await AuditAccessAsync("conversations", deviceId, dtos.Count, ct);
        return Ok(new { conversations = dtos });
    }

    /// <summary>Full conversation detail (all messages) for any device.</summary>
    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<IActionResult> ConversationDetail(Guid conversationId, CancellationToken ct)
    {
        var conv = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new AdminConversationDetailDto(
                c.Id,
                c.DeviceId,
                c.ChildId,
                c.StartedAt,
                c.EndedAt,
                c.Messages.OrderBy(m => m.Timestamp).Select(m => new AdminMessageDto(
                    m.Id,
                    m.Role.ToString(),
                    m.Content,
                    m.SafetyFlag.ToString(),
                    m.Timestamp,
                    m.Role == MessageRole.Assistant && m.AudioBlobPath != null))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        if (conv is null) return NotFound(new { error = "Unknown conversation." });
        await AuditAccessAsync("conversation-detail", conversationId, conv.Messages.Count, ct);
        return Ok(conv);
    }

    /// <summary>Global audit feed — ALL events, including system-actor rows
    /// (ActorParentId null) that parents can never see. Newest first.</summary>
    [HttpGet("audit")]
    public async Task<IActionResult> Audit(
        [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (offset < 0 || limit < 1) return BadRequest(new { error = "Invalid pagination." });
        limit = Math.Min(limit, 100);

        var rows = await _db.AuditEvents.AsNoTracking()
            .OrderByDescending(a => a.Timestamp)
            .Skip(offset).Take(limit)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.EventType,
                a.ActorParentId,
                a.TargetDeviceId,
                a.TargetChildId,
                a.Metadata
            })
            .ToListAsync(ct);

        var dtos = rows.Select(a => new AdminAuditDto(
            a.Id, a.Timestamp, a.EventType.ToString(),
            a.ActorParentId, a.TargetDeviceId, a.TargetChildId,
            ParseMetadata(a.Metadata))).ToList();
        return Ok(new { events = dtos });
    }

    /// <summary>
    /// Story-QA tuning playground (Phase 2). Runs a typed question through the
    /// REAL bounded in-story Q&amp;A pipeline — input moderation → GPT →
    /// <c>StoryAnswerFilter</c>/repair/fallback → output moderation — and
    /// returns the answer TEXT plus the filter/fallback diagnostics. No TTS,
    /// no persistence, no conversation write, no device gates. It DOES call
    /// OpenAI (cost) — operator-initiated, so there is no cost-cap gate.
    /// Mirrors <see cref="StoryQaController.Ask"/>'s decision logic, minus the
    /// voice/transport concerns, so what you see here is what a child would
    /// hear for the same (story, segment, question).
    /// </summary>
    [HttpPost("story-qa-test")]
    public async Task<IActionResult> StoryQaTest(
        [FromBody] AdminStoryQaTestRequest req, CancellationToken ct)
    {
        if (req is null
            || string.IsNullOrWhiteSpace(req.StoryId)
            || string.IsNullOrWhiteSpace(req.Question))
        {
            return BadRequest(new { error = "storyId and question are required." });
        }

        var story = _library.GetById(req.StoryId);
        if (story is null) return NotFound(new { error = "Unknown story." });

        var segIdx = story.Segments.Count == 0
            ? 0
            : Math.Clamp(req.SegmentIndex, 0, story.Segments.Count - 1);
        var segText = story.Segments.Count > 0 ? story.Segments[segIdx].Text : string.Empty;
        var question = req.Question.Trim();

        try
        {
            // Input moderation BEFORE GPT — mirrors the real path.
            var inputMod = await _moderation.CheckContentAsync(question);
            if (!inputMod.IsSafe)
            {
                return Ok(new AdminStoryQaTestResult(
                    req.StoryId, segIdx, segText, question,
                    StoryAnswerFilter.SafeFallback, UsedFallback: true,
                    InputSafe: false, OutputSafe: true,
                    FirstRejection: "InputModerationBlocked", RetryRejection: null,
                    Outcome: "input_blocked"));
            }

            // Bounded answer (prompt → GPT → filter → repair-once → fallback).
            var answer = await _questions.AnswerAsync(story, segIdx, question);
            var answerText = answer.Text;
            var outputSafe = true;
            var outcome = answer.UsedFallback ? "answer_fallback" : "answered";

            // OUTPUT moderation only on model-authored answers (canned
            // fallbacks are pre-reviewed) — same gate as the voice path.
            if (!answer.UsedFallback)
            {
                var outMod = await _moderation.CheckContentAsync(answerText);
                if (!outMod.IsSafe)
                {
                    answerText = StoryAnswerFilter.SafeFallback;
                    outputSafe = false;
                    outcome = "output_blocked";
                }
            }

            return Ok(new AdminStoryQaTestResult(
                req.StoryId, segIdx, segText, question, answerText,
                UsedFallback: answer.UsedFallback || !outputSafe,
                InputSafe: true, OutputSafe: outputSafe,
                FirstRejection: answer.FirstRejection.ToString(),
                RetryRejection: answer.RetryRejection?.ToString(),
                Outcome: outcome));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // #059: do NOT echo the raw upstream exception text on the wire (it
            // can leak OpenAI / internal detail). Log the real reason server-side
            // for diagnosis; return a sanitized message — same posture as the
            // public voice paths' 502 envelope.
            _logger.LogWarning(ex,
                "Internal story-qa-test failed for {StoryId}", req.StoryId);
            return StatusCode(502, new { error = "Story-QA test failed. See server logs." });
        }
    }

    // ── Slice F: the owner's custom-story-request queue ─────────────

    /// <summary>All story requests, newest first, optionally filtered by
    /// status. Includes the requester's email when the account still exists
    /// (requests are FK-free and outlive accounts).</summary>
    [HttpGet("story-requests")]
    public async Task<IActionResult> StoryRequests(
        [FromQuery] string? status, CancellationToken ct)
    {
        var query = _db.StoryRequests.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(r => r.Status == normalized);
        }
        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var parentIds = rows.Select(r => r.ParentId).Distinct().ToList();
        var emails = await _db.Parents
            .Where(p => parentIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Email })
            .ToDictionaryAsync(p => p.Id, p => p.Email, ct);

        return Ok(new
        {
            requests = rows.Select(r => new AdminStoryRequestDto(
                r.Id, r.ParentId,
                emails.TryGetValue(r.ParentId, out var email) ? email : null,
                r.Type, r.Text, r.PhotoPath != null, r.Status,
                r.CreatedAtUtc, r.UpdatedAtUtc)).ToList()
        });
    }

    /// <summary>Streams a request's uploaded book-page photo to the
    /// operator. Uniform 404 for unknown id / no photo / missing file.</summary>
    [HttpGet("story-requests/{id:guid}/photo")]
    public async Task<IActionResult> StoryRequestPhoto(
        Guid id, [FromServices] IStoryRequestPhotoStore photos, CancellationToken ct)
    {
        var request = await _db.StoryRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request?.PhotoPath is null)
        {
            return NotFound(new { error = "Photo not available." });
        }
        var photo = await photos.ReadAsync(request.PhotoPath, ct);
        if (photo is null)
        {
            return NotFound(new { error = "Photo not available." });
        }
        return File(photo.Value.Content, photo.Value.ContentType);
    }

    /// <summary>Operator moves a request through its lifecycle
    /// (new → in_review → delivered | declined). Reason required; audited
    /// as an InternalConsoleAction. Idempotent (same status = no-op).</summary>
    [HttpPost("story-requests/{id:guid}/status")]
    public async Task<IActionResult> SetStoryRequestStatus(
        Guid id, [FromBody] InternalStoryRequestStatusRequest? req, CancellationToken ct)
    {
        var status = req?.Status?.Trim().ToLowerInvariant();
        if (status is not ("new" or "in_review" or "delivered" or "declined"))
        {
            return BadRequest(new { error = "status must be one of: new, in_review, delivered, declined." });
        }
        if (string.IsNullOrWhiteSpace(req?.Reason))
        {
            return BadRequest(new { error = "A reason is required." });
        }
        var request = await _db.StoryRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return NotFound(new { error = "Request not found." });
        }
        if (request.Status == status)
        {
            return Ok(new { status });   // idempotent no-op, no audit row
        }

        request.Status = status;
        request.UpdatedAtUtc = DateTime.UtcNow;
        var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
        _db.AuditEvents.Add(AuditEvent.InternalConsoleStoryRequestStatus(
            op, id, status, req!.Reason!));
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Operator {Operator} set story request {RequestId} to {Status} (reason: {Reason})",
            op, id, status, req.Reason);
        return Ok(new { status });
    }

    /// <summary>Resolved console operator identity (from the auth gate) so the
    /// UI can show who is signed in — accountability. No data exposure; the
    /// name is whatever <c>InternalAdminAuth</c> resolved (a named operator, the
    /// shared-token sentinel, or the dev-bypass sentinel).</summary>
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
        => Ok(new { @operator = HttpContext?.Items["InternalOperator"] as string ?? "unknown" });

    /// <summary>
    /// Stream a fresh, self-consistent snapshot of the live SQLite DB
    /// (Tier-1 backup slice, 2026-08-06). This is the OFFSITE half of
    /// the backup story: the <c>DatabaseBackupService</c> worker keeps
    /// on-volume daily snapshots (corruption guard), and this endpoint
    /// lets the operator pull a copy to any other machine — the only
    /// defense against losing the volume itself. Same fail-closed
    /// console gate as every <c>/api/internal/*</c> route; a non-SQLite
    /// or in-memory host answers a uniform 404. Each successful pull
    /// writes one <c>InternalConsoleAccess</c> audit row (the snapshot
    /// contains every family's data — the most access-audit-worthy read
    /// on the console).
    /// </summary>
    [HttpGet("backup")]
    public async Task<IActionResult> DownloadBackup(CancellationToken ct)
    {
        if (!SqliteDatabaseSnapshot.IsSqliteFileDatabase(_db, out _))
            return NotFound(new { error = "Backup not available." });

        // Snapshot into a temp file; DeleteOnClose makes the OS clean
        // it up when the response stream is disposed (success, client
        // abort, or error alike).
        var tempPath = Path.Combine(Path.GetTempPath(), $"areg-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            await SqliteDatabaseSnapshot.VacuumIntoAsync(_db, tempPath, ct);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup snapshot failed");
            TryDeleteFile(tempPath);
            return NotFound(new { error = "Backup not available." });
        }

        await AuditAccessAsync("backup", null, 1, ct);

        var stream = new FileStream(
            tempPath, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return File(stream, "application/octet-stream",
            $"areg-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}Z.db");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch { /* best-effort temp cleanup */ }
    }

    /// <summary>JIT session exchange (MFA). The operator's static token is the
    /// FIRST factor (validated by the gate to reach this path); a TOTP code is
    /// the SECOND factor when a secret is configured for this operator. On
    /// success, mints a short-lived session token used for every other console
    /// call — so the static token alone grants no standing data access. The
    /// gate enforces "session required for data endpoints" only when
    /// <c>Internal:RequireSession</c> is on; this endpoint always works so the
    /// console uses one flow in both modes. See OperatorSessionStore / Totp.</summary>
    [HttpPost("session")]
    public IActionResult CreateSession([FromBody] InternalSessionRequest? req)
    {
        var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";

        // MFA: a named operator with a configured TOTP secret must present a code.
        var operators = _config.GetSection("Internal:Operators")
            .Get<List<Observability.InternalAdminAuth.OperatorCredential>>() ?? new();
        var secret = operators.FirstOrDefault(o => o.Name == op)?.TotpSecret;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            if (string.IsNullOrWhiteSpace(req?.Totp)
                || !Application.Auth.Totp.Verify(secret, req!.Totp!, DateTime.UtcNow))
                return Unauthorized(new { error = "Invalid authentication code." });
        }

        var ttlMin = Math.Clamp(_config.GetValue("Internal:SessionTtlMinutes", 15), 1, 240);
        var ttl = TimeSpan.FromMinutes(ttlMin);
        var token = _sessions.Issue(op, ttl);
        _logger.LogInformation(
            "Operator {Operator} opened a console session (ttl {Ttl}m, mfa={Mfa})",
            op, ttlMin, !string.IsNullOrWhiteSpace(secret));
        return Ok(new { sessionToken = token, @operator = op, expiresInSeconds = (int)ttl.TotalSeconds });
    }

    // ── Phase 3: reversible operator ACTIONS ───────────────────────
    // Operator-scoped (NO parent ownership check — the console is superuser).
    // Reversible only: revoke/restore the credential kill-switch, and
    // pause/resume. A reason is required; every change writes a system-actor
    // audit row carrying the operator identity + reason. Idempotent.

    /// <summary>Operator kill-switch: revoke (true) or restore (false) a
    /// device's server-side credential. When revoked, every device-auth path
    /// 401s until the device re-provisions. Reversible.</summary>
    [HttpPost("devices/{deviceId:guid}/revoke")]
    public Task<IActionResult> RevokeDevice(
        Guid deviceId, [FromBody] InternalDeviceActionRequest req, CancellationToken ct)
        => DeviceFlagActionAsync(deviceId, req, "device_revoke", ct);

    /// <summary>Operator pause (true) or resume (false) of a device — soft
    /// override; the device still authenticates but chat short-circuits.</summary>
    [HttpPost("devices/{deviceId:guid}/pause")]
    public Task<IActionResult> PauseDeviceAction(
        Guid deviceId, [FromBody] InternalDeviceActionRequest req, CancellationToken ct)
        => DeviceFlagActionAsync(deviceId, req, "device_pause", ct);

    /// <summary>
    /// What this toy is entitled to: every catalogue story, plus any override
    /// already recorded for another namespace, with the toy's current answer
    /// for each. The read half of the allow/deny action below — the console
    /// cannot offer a toggle without first knowing which way it points.
    /// <para>
    /// Not access-audited, deliberately, on the same rule the other aggregate
    /// console reads follow (<c>/overview</c>, <c>/devices</c>,
    /// <c>/parents</c>, <c>/stories</c>): this exposes catalogue configuration
    /// and operator decisions, not a line of any child's transcript.
    /// </para>
    /// </summary>
    [HttpGet("devices/{deviceId:guid}/content")]
    public async Task<IActionResult> GetDeviceContent(Guid deviceId, CancellationToken ct)
    {
        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId, ct);
        if (!deviceExists)
            return NotFound(new { error = "Device not found." });

        var rows = await _db.Set<DeviceContentOverride>().AsNoTracking()
            .Where(o => o.DeviceId == deviceId)
            .ToListAsync(ct);
        var byKey = rows.ToDictionary(
            o => $"{DeviceContentEntitlement.NormalizeKind(o.ItemKind)}|{DeviceContentEntitlement.NormalizeKey(o.ItemKey)}",
            o => o,
            StringComparer.OrdinalIgnoreCase);

        // fleetDefault=false is a FLEET-DARK uploaded item: absent from every
        // toy's manifest unless an operator granted it. For those an allow row
        // is the only thing that makes `allowed` true — the inverse of a
        // catalogue item, where only a deny row makes it false. Getting this
        // backwards would show a newly-uploaded story as "sent: yes" on every
        // toy in the fleet while no toy had actually been offered it.
        AdminDeviceContentItemDto Row(
            string kind, string key, string? title, int? version,
            string source = "catalogue", bool fleetDefault = true, bool retired = false)
        {
            byKey.TryGetValue($"{kind}|{DeviceContentEntitlement.NormalizeKey(key)}", out var ov);
            var mode = ov is null ? null : DeviceContentEntitlement.NormalizeMode(ov.Mode);
            // Retirement beats both routes an item can reach a toy by, because
            // that is exactly what it does to the manifest.
            var allowed = !retired && (fleetDefault
                ? !string.Equals(mode, DeviceContentEntitlement.ModeDeny, StringComparison.Ordinal)
                : string.Equals(mode, DeviceContentEntitlement.ModeAllow, StringComparison.Ordinal));
            return new AdminDeviceContentItemDto(
                kind, key, title, version, allowed,
                mode, ov?.Reason, ov?.CreatedAt, source, fleetDefault, retired);
        }

        // Stories are listed in full because withholding a STORY is the thing
        // the console exists to do. The ~140 game/voice/music clips are not
        // listed — a wall of clip ids is not a control surface — but any
        // override already made on one (through this endpoint's POST) is shown,
        // so nothing an operator has done to a toy is invisible here.
        var options = ContentSyncOptions.Resolve(_config);
        var items = options.ResolveStories()
            .Where(s => !string.IsNullOrWhiteSpace(s.StoryId))
            .Select(s => Row(DeviceContentEntitlement.KindStory, s.StoryId.Trim(),
                string.IsNullOrWhiteSpace(s.Title) ? null : s.Title,
                s.Version < 1 ? 1 : s.Version))
            .ToList();

        // Uploaded items join the same list. A fleet-dark one MUST appear here
        // or "send this new story to the bench toy first" has no button: the
        // grant is written against this row, and a row nobody can see is a
        // feature nobody can use. A RETIRED one is listed too — an operator's
        // own grant must never become invisible — but Row() reports it as not
        // sent, because retirement removes it from the manifest whatever the
        // grant says.
        var uploaded = await _db.ContentItems.AsNoTracking()
            .OrderBy(i => i.ItemKey)
            .ToListAsync(ct);
        items.AddRange(uploaded.Select(u => Row(
            DeviceContentEntitlement.NormalizeKind(u.Kind),
            DeviceContentEntitlement.NormalizeKey(u.ItemKey),
            string.IsNullOrWhiteSpace(u.Title) ? null : u.Title,
            u.Version, "uploaded", u.DefaultEnabled, u.RetiredAt is not null)));

        var listedStoryKeys = items
            .Select(i => DeviceContentEntitlement.NormalizeKey(i.ItemKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        items.AddRange(rows
            .Where(o => !string.Equals(
                             DeviceContentEntitlement.NormalizeKind(o.ItemKind),
                             DeviceContentEntitlement.KindStory, StringComparison.Ordinal)
                        || !listedStoryKeys.Contains(DeviceContentEntitlement.NormalizeKey(o.ItemKey)))
            .Select(o => Row(
                DeviceContentEntitlement.NormalizeKind(o.ItemKind),
                DeviceContentEntitlement.NormalizeKey(o.ItemKey), null, null)));

        return Ok(new { deviceId, items });
    }

    /// <summary>
    /// Give this toy an item, or take it away. Same shape as every other
    /// console device action — reason required, 404 on an unknown device,
    /// idempotent (a no-op writes no audit row and saves nothing), one
    /// system-actor audit row in the SAME SaveChanges as the mutation, one
    /// loud log line.
    /// <para>
    /// The row is UPSERTED rather than deleted on the way back to the default,
    /// so "this toy is entitled to this" stays a recorded operator decision
    /// rather than an absence — which is what the upload slice needs when
    /// sending a fleet-dark story to one toy first.
    /// </para>
    /// </summary>
    [HttpPost("devices/{deviceId:guid}/content")]
    public async Task<IActionResult> SetDeviceContent(
        Guid deviceId, [FromBody] InternalDeviceContentRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { error = "A reason is required for operator actions." });
        if (!DeviceContentEntitlement.IsKnownKind(req.ItemKind))
            return BadRequest(new
            {
                error = "itemKind must be one of: "
                        + string.Join(", ", DeviceContentEntitlement.Kinds) + "."
            });

        var itemKey = DeviceContentEntitlement.NormalizeKey(req.ItemKey);
        if (!DeviceContentEntitlement.IsValidItemKey(req.ItemKind, itemKey))
            return BadRequest(new
            {
                error = "itemKey must be a content id (lowercase letters, digits, - and _), "
                        + "or gameKey/clipId for a game clip."
            });

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId, ct);
        if (!deviceExists)
            return NotFound(new { error = "Device not found." });

        var itemKind = DeviceContentEntitlement.NormalizeKind(req.ItemKind);
        var mode = req.Value
            ? DeviceContentEntitlement.ModeAllow
            : DeviceContentEntitlement.ModeDeny;

        var existing = await _db.Set<DeviceContentOverride>()
            .FirstOrDefaultAsync(o => o.DeviceId == deviceId
                                      && o.ItemKind == itemKind
                                      && o.ItemKey == itemKey, ct);

        var changed = existing is null
                      || !string.Equals(DeviceContentEntitlement.NormalizeMode(existing.Mode),
                                        mode, StringComparison.Ordinal);
        if (changed)
        {
            var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
            var reason = req.Reason.Trim();
            if (existing is null)
            {
                _db.Set<DeviceContentOverride>().Add(new DeviceContentOverride
                {
                    Id = Guid.NewGuid(),
                    DeviceId = deviceId,
                    ItemKind = itemKind,
                    ItemKey = itemKey,
                    Mode = mode,
                    Reason = reason,
                    CreatedBy = op,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.Mode = mode;
                existing.Reason = reason;
                existing.CreatedBy = op;
                existing.CreatedAt = DateTime.UtcNow;
            }
            _db.AuditEvents.Add(AuditEvent.InternalConsoleContentOverride(
                op, deviceId, itemKind, itemKey, req.Value, reason));
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Operator {Operator} set content {ItemKind}/{ItemKey} to {Mode} on device {DeviceId}",
                op, itemKind, itemKey, mode, deviceId);
        }

        return Ok(new
        {
            deviceId,
            action = "device_content_override",
            itemKind,
            itemKey,
            value = req.Value,
            changed,
        });
    }

    // ── Catalogue: the owner adds and removes content himself ──────
    // No git, no ffmpeg, no deploy, no developer. Upload an MP3, give it a
    // title, send it to one toy, then release it to the fleet.
    //
    // Every mutation follows the Phase 3 operator discipline exactly: a reason
    // is required (400 without), 404 on an unknown item, an idempotent no-op
    // writes no audit row and saves nothing, the audit row lands in the SAME
    // SaveChanges as the mutation, and one loud structured log line.

    /// <summary>Largest audio file the console will accept. Comfortably above
    /// the longest story in the library (~5 MB at 192 kbps) and far below
    /// anything that would be a memory problem — the upload is streamed to
    /// disk, never buffered.</summary>
    private const long MaxContentUploadBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The whole uploaded catalogue, newest first — including fleet-dark and
    /// retired rows, because the operator's questions are "what did I upload"
    /// and "what did I take away", and a list that hides either cannot answer
    /// them.
    /// <para>
    /// Not access-audited, on the same rule as the other aggregate console
    /// reads: this is catalogue configuration, not a line of any child's
    /// transcript.
    /// </para>
    /// </summary>
    [HttpGet("content")]
    public async Task<IActionResult> GetContentItems(CancellationToken ct)
    {
        var rows = await _db.ContentItems.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        var uploadRoot = ContentSyncOptions.Resolve(_config).UploadRoot;

        // One grouped query for the whole page rather than one per item — the
        // same rule the device-list surfaces follow.
        var grants = await _db.Set<DeviceContentOverride>().AsNoTracking()
            .Where(o => o.Mode == DeviceContentEntitlement.ModeAllow)
            .GroupBy(o => new { o.ItemKind, o.ItemKey })
            .Select(g => new { g.Key.ItemKind, g.Key.ItemKey, Count = g.Count() })
            .ToListAsync(ct);
        var grantCounts = grants.ToDictionary(
            g => DeviceContentEntitlement.NormalizeKind(g.ItemKind)
                 + "|" + DeviceContentEntitlement.NormalizeKey(g.ItemKey),
            g => g.Count,
            StringComparer.OrdinalIgnoreCase);

        var items = rows.Select(i => new AdminContentItemDto(
            i.Id, i.Kind, i.ItemKey, i.Title, i.Version, i.Sha256, i.SizeBytes,
            i.DefaultEnabled, i.RetiredAt, i.CreatedBy, i.CreatedAt,
            // Derived, never stored: a stored "yes it exists" would be a claim
            // about a volume that may since have been remounted or restored
            // from a backup that did not carry the audio.
            ContentItemOverlay.TryResolveUploadPath(uploadRoot, i.RelativePath, out var abs)
                && System.IO.File.Exists(abs),
            grantCounts.GetValueOrDefault(
                DeviceContentEntitlement.NormalizeKind(i.Kind)
                + "|" + DeviceContentEntitlement.NormalizeKey(i.ItemKey))));

        return Ok(new { uploadRootConfigured = !string.IsNullOrWhiteSpace(uploadRoot), items });
    }

    /// <summary>
    /// Upload a story: an MP3, a title, and a reason. The row lands
    /// <c>DefaultEnabled=false</c> — FLEET-DARK — so nothing reaches a child
    /// until the operator has sent it to one toy, heard it, and released it.
    ///
    /// <para>
    /// <b>The SERVER computes the sha256 and the size.</b> That single fact is
    /// what retires the hand-run <c>Ship-StoryAudio.ps1</c> ritual and the
    /// hand-added placeholder config row — the two steps this repo has already
    /// got wrong in the field, once by shipping bytes described by a stale
    /// manifest and once by bumping no version so no toy re-downloaded.
    /// </para>
    ///
    /// <para>
    /// <b>The filename is generated here</b> (<c>&lt;itemKey&gt;-v&lt;N&gt;.mp3</c>),
    /// never taken from the upload. An uploaded filename is attacker- (or
    /// typo-) controlled text, and the invariant the whole content contract
    /// rests on is that a content id is a lookup key that never reaches the
    /// filesystem.
    /// </para>
    ///
    /// <para>
    /// <b>Re-uploading the same key bumps the version</b> and writes a new
    /// file. The old file is left where it is: a toy mid-download of v3 must
    /// not have it pulled out from under it, and carry-forward keeps the old
    /// copy playable until a sweep reclaims it.
    /// </para>
    /// </summary>
    [HttpPost("content/stories")]
    [RequestSizeLimit(MaxContentUploadBytes + (4L * 1024 * 1024))]
    public async Task<IActionResult> UploadStoryContent(
        [FromForm] string? itemKey,
        [FromForm] string? title,
        [FromForm] string? reason,
        IFormFile? audio,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { error = "A reason is required for operator actions." });

        var key = DeviceContentEntitlement.NormalizeKey(itemKey);
        if (!DeviceContentEntitlement.IsValidItemKey(DeviceContentEntitlement.KindStory, key))
            return BadRequest(new
            {
                error = "itemKey must be a story id: lowercase letters, digits, - and _, up to 48 characters."
            });

        var storyTitle = (title ?? string.Empty).Trim();
        if (storyTitle.Length is 0 or > 200)
            return BadRequest(new { error = "A title of 1 to 200 characters is required." });

        if (audio is null || audio.Length <= 0)
            return BadRequest(new { error = "An audio file is required." });
        if (audio.Length > MaxContentUploadBytes)
            return BadRequest(new
            {
                error = $"Audio must be under {MaxContentUploadBytes / (1024 * 1024)} MB."
            });

        var options = ContentSyncOptions.Resolve(_config);
        if (!ContentItemOverlay.TryResolveRoot(options.UploadRoot, out var root))
        {
            // Fail closed and say so plainly. Writing into the image instead
            // would put operator content inside the git-tracked catalogue, and
            // the next redeploy would silently delete it.
            _logger.LogError(
                "Content upload refused: ContentSync:UploadRoot is not configured, so there is nowhere durable to write.");
            return StatusCode(503, new
            {
                error = "Uploads are not configured on this deployment (ContentSync:UploadRoot is unset)."
            });
        }

        // A key that collides with a shipped story would be dropped by the
        // manifest's keep-the-first dedupe and the upload would look like it
        // worked forever. Refuse it here instead of storing a row that can
        // never do anything.
        if (options.ResolveStories().Any(s =>
                string.Equals(s.StoryId?.Trim(), key, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new
            {
                error = $"\"{key}\" is already a story shipped in this build. Choose a different id."
            });
        }

        var existing = await _db.ContentItems.FirstOrDefaultAsync(
            i => i.Kind == DeviceContentEntitlement.KindStory && i.ItemKey == key, ct);
        var version = existing is null ? 1 : existing.Version + 1;

        var fileName = $"{key}-v{version}.mp3";
        // Belt and braces over the id allowlist: the ONE place a value that
        // came off a wire becomes a path, re-checked against the same
        // containment rule the read path uses.
        if (!ContentItemOverlay.TryResolveUploadPath(options.UploadRoot, fileName, out var finalPath))
            return BadRequest(new { error = "That id cannot be stored." });

        string sha256;
        long sizeBytes;
        var partPath = finalPath + ".part";
        try
        {
            Directory.CreateDirectory(root);
            (sha256, sizeBytes) = await WriteAndHashAsync(audio, partPath, ct);
        }
        catch (InvalidDataException)
        {
            // Not an MP3. A caller error, not a server one — and caught here
            // rather than swept into the 503 below, because "the file you
            // picked is wrong" and "this deployment cannot store files" are
            // different problems with different fixes.
            TryDeleteFile(partPath);
            return BadRequest(new { error = "That file is not an MP3." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDeleteFile(partPath);
            _logger.LogError(ex, "Content upload failed while writing {FileName}", fileName);
            return StatusCode(503, new { error = "Could not store the upload. Nothing was changed." });
        }

        // The file is written and verified BEFORE the row is committed. The
        // other order would leave a manifest advertising a story that is not on
        // disk, which every toy in the fleet would report as download_failed —
        // the exact failure this slice exists to make impossible.
        System.IO.File.Move(partPath, finalPath, overwrite: true);

        var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
        var trimmedReason = reason.Trim();
        if (existing is null)
        {
            existing = new ContentItem
            {
                Id = Guid.NewGuid(),
                Kind = DeviceContentEntitlement.KindStory,
                ItemKey = key,
                // Fleet-dark on arrival. Nothing an owner uploads reaches a
                // child's toy until he has heard it on one toy and said so.
                DefaultEnabled = false,
            };
            _db.ContentItems.Add(existing);
        }
        existing.Title = storyTitle;
        existing.Version = version;
        existing.RelativePath = fileName;
        existing.Sha256 = sha256;
        existing.SizeBytes = sizeBytes;
        // A re-upload of a retired item brings it back: the operator just
        // supplied new audio for it, which is not something to do to a story
        // meant to stay gone.
        existing.RetiredAt = null;
        existing.CreatedBy = op;
        existing.CreatedAt = DateTime.UtcNow;

        _db.AuditEvents.Add(AuditEvent.InternalConsoleContentItem(
            op, "content_story_uploaded", existing.Kind, key, version, trimmedReason));
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Operator {Operator} uploaded story {ItemKey} v{Version} ({SizeBytes} bytes, sha {Sha}): {Reason}",
            op, key, version, sizeBytes, sha256, trimmedReason);

        return Ok(new
        {
            id = existing.Id,
            itemKey = key,
            title = storyTitle,
            version,
            sha256,
            sizeBytes,
            defaultEnabled = existing.DefaultEnabled,
        });
    }

    /// <summary>
    /// Release an uploaded item to the whole fleet (<c>value: true</c>), or
    /// pull it back to fleet-dark (<c>value: false</c>). Pulling back does NOT
    /// remove it from a toy that was granted it explicitly — that grant is a
    /// separate, deliberate decision recorded in <c>DeviceContentOverrides</c>,
    /// and quietly undoing it here would make the bench toy stop matching what
    /// the console says about it.
    /// </summary>
    [HttpPost("content/{id:guid}/release")]
    public Task<IActionResult> ReleaseContentItem(
        Guid id, [FromBody] InternalContentItemActionRequest? req, CancellationToken ct)
        => ContentItemFlagAsync(id, req, "content_released", ct);

    /// <summary>
    /// Retire an item (<c>value: true</c>) or bring it back (<c>value:
    /// false</c>). A SOFT delete: the item leaves every manifest at once, and
    /// the file is deliberately left on disk and on every card. The toy's
    /// carry-forward keeps playing it until a later sweep reclaims it, so no
    /// child loses a story mid-listen because an adult pressed a button.
    /// </summary>
    [HttpPost("content/{id:guid}/retire")]
    public Task<IActionResult> RetireContentItem(
        Guid id, [FromBody] InternalContentItemActionRequest? req, CancellationToken ct)
        => ContentItemFlagAsync(id, req, "content_retired", ct);

    /// <summary>Shared body of the two catalogue flag actions, mirroring
    /// <c>DeviceFlagActionAsync</c>: reason required, 404 on unknown, no-op
    /// writes nothing, audit in the same SaveChanges, one loud log.</summary>
    private async Task<IActionResult> ContentItemFlagAsync(
        Guid id, InternalContentItemActionRequest? req, string action, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { error = "A reason is required for operator actions." });

        var item = await _db.ContentItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
            return NotFound(new { error = "Content item not found." });

        var isRetire = action == "content_retired";
        var current = isRetire ? item.RetiredAt is not null : item.DefaultEnabled;
        var changed = current != req.Value;
        if (changed)
        {
            var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
            var reason = req.Reason.Trim();
            if (isRetire) item.RetiredAt = req.Value ? DateTime.UtcNow : null;
            else item.DefaultEnabled = req.Value;

            _db.AuditEvents.Add(AuditEvent.InternalConsoleContentItem(
                op, action + (req.Value ? "" : "_undone"),
                item.Kind, item.ItemKey, item.Version, reason));
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Operator {Operator} set {Action}={Value} on content {ItemKind}/{ItemKey} v{Version}: {Reason}",
                op, action, req.Value, item.Kind, item.ItemKey, item.Version, reason);
        }

        return Ok(new
        {
            id = item.Id,
            itemKey = item.ItemKey,
            action,
            value = req.Value,
            changed,
            defaultEnabled = item.DefaultEnabled,
            retiredAt = item.RetiredAt,
        });
    }

    /// <summary>
    /// Streams the upload to disk while hashing it, and refuses anything that
    /// is not an MP3 by its first bytes.
    /// <para>
    /// Streamed rather than buffered so a 60 MB upload costs 64 KB of memory.
    /// Hashed in the same pass because reading the file back to hash it would
    /// let the two disagree.
    /// </para>
    /// <para>
    /// The header check is three bytes, not a parse: no decoding, no
    /// transcoding, and no new dependency. It exists because a file that is not
    /// an MP3 would be advertised, downloaded and sha-verified successfully by
    /// every toy in the fleet, and only then fail — silently, at the speaker,
    /// with nothing anywhere saying why.
    /// </para>
    /// </summary>
    private static async Task<(string Sha256, long SizeBytes)> WriteAndHashAsync(
        IFormFile audio, string partPath, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var header = new byte[3];
        var headerLength = 0;
        long total = 0;

        await using (var input = audio.OpenReadStream())
        await using (var output = new FileStream(
            partPath, FileMode.Create, FileAccess.Write, FileShare.None,
            64 * 1024, useAsync: true))
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                for (var i = 0; i < read && headerLength < header.Length; i++)
                {
                    header[headerLength++] = buffer[i];
                }
                hasher.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
        }

        // "ID3" tag, or an MPEG frame sync (11 set bits). Both are what every
        // file in story-audio/ actually starts with.
        var looksLikeMp3 =
            (headerLength >= 3 && header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
            || (headerLength >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0);
        if (!looksLikeMp3)
        {
            throw new InvalidDataException("The uploaded file is not an MP3.");
        }

        return (Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant(), total);
    }

    /// <summary>
    /// Mint a FRESH pairing code (and QR payload) for an existing toy.
    /// <para>
    /// Every toy registered before 2026-08-04 either never had a claim code or
    /// had it erased the first time it was paired, because claiming used to
    /// consume it. Only a hash was ever stored, so those codes cannot be
    /// recovered — which leaves those toys unable to use the re-pairing the
    /// QR now supports. This is the way back for them.
    /// </para>
    /// <para>
    /// The toy's IDENTITY and its device key are untouched, so nothing has to
    /// be reflashed or re-provisioned: the operator prints the returned QR and
    /// puts it on the toy. Any parent currently linked stays linked — this
    /// mints a code, it does not unlink anyone.
    /// </para>
    /// <para>
    /// The plaintext code is returned ONCE and is never logged or written to
    /// the audit row. The console gate already sets <c>Cache-Control:
    /// no-store</c> on every <c>/api/internal/*</c> response.
    /// </para>
    /// </summary>
    [HttpPost("devices/{deviceId:guid}/claim-code")]
    public async Task<IActionResult> IssueClaimCode(
        Guid deviceId, [FromBody] InternalReasonRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { error = "A reason is required for operator actions." });

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return NotFound(new { error = "Device not found." });

        // Same generator and strength as the factory registration path:
        // 128-bit, returned once, only the hash stored.
        var claimCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        device.ClaimCodeHash = DeviceApiKeyHasher.Hash(claimCode);

        var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
        // Audit carries WHO and WHY — never the code. `value: true` matches
        // the InternalConsoleAction shape used by the other operator actions.
        _db.AuditEvents.Add(AuditEvent.InternalConsoleAction(
            op, "device_claim_code_issued", deviceId, true, req.Reason.Trim()));
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Operator {Operator} issued a new pairing code for device {DeviceId}: {Reason}",
            op, deviceId, req.Reason.Trim());

        // The exact JSON the toy's QR should encode — same shape the factory
        // station prints, so the app's scanner needs no special case. The
        // device key is NOT included.
        var qrPayload = JsonSerializer.Serialize(new { deviceId = device.Id, claim = claimCode });
        return Ok(new { deviceId = device.Id, claimCode, qrPayload });
    }

    /// <summary>OTA foundation — BENCH/TEST enqueue of a device command so an
    /// operator can push e.g. <c>firmware_update</c> without editing the DB.
    /// Behind the same internal gate as every other <c>/api/internal/*</c>
    /// action (fail-closed 404 when unconfigured). Only known
    /// <see cref="DeviceCommandTypes"/> values are accepted; the device picks
    /// the command up on its next poll of <c>GET /api/devices/commands</c>.
    /// Deliberately NOT parent-facing and NOT in the admin.html UI yet; when
    /// this becomes a real operator surface it gains the reason + audit-row
    /// discipline of the Phase 3 actions (today: one loud structured log).</summary>
    [HttpPost("devices/{deviceId:guid}/commands")]
    public async Task<IActionResult> EnqueueDeviceCommand(
        Guid deviceId,
        [FromBody] InternalEnqueueCommandRequest? req,
        [FromServices] IDeviceCommandService commands,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Type))
            return BadRequest(new { error = "A command type is required." });

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId, ct);
        if (!deviceExists)
            return NotFound(new { error = "Device not found." });

        var now = DateTime.UtcNow;
        var ttlSeconds = Math.Clamp(req.TtlSeconds ?? 3600, 60, 86400);
        var cmd = await commands.EnqueueAsync(
            deviceId,
            req.Type.Trim(),
            req.Payload?.GetRawText(),
            now.AddSeconds(ttlSeconds),
            now);
        if (cmd is null)
            return BadRequest(new { error = "Unknown command type." });

        var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
        _logger.LogWarning(
            "Operator {Operator} enqueued device command {CommandType} ({CommandId}) for device {DeviceId} (ttl {Ttl}s)",
            op, cmd.Type, cmd.Id, deviceId, ttlSeconds);
        return Ok(new { commandId = cmd.Id, deviceId, type = cmd.Type, expiresAt = cmd.ExpiresAt });
    }

    /// <summary>
    /// Read this device's recent command rows — the operator half of the
    /// enqueue endpoint above. Answers the two questions the OTA runbook
    /// cannot answer without it: did the device ever POLL (Status leaves
    /// Pending), and what did it ACK (Result / Error /
    /// AckFirmwareVersion / AckDiagnosticsJson). A device that never polls
    /// is running firmware without the OTA client, which is otherwise
    /// indistinguishable from a device that is simply idle.
    /// Read-only; newest first; no payload echo (an enqueued payload is
    /// operator-supplied, not device data, and echoing it adds nothing).
    /// </summary>
    [HttpGet("devices/{deviceId:guid}/commands")]
    public async Task<IActionResult> GetDeviceCommands(
        Guid deviceId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (limit < 1) return BadRequest(new { error = "limit must be >= 1." });
        limit = Math.Min(limit, 100);

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId, ct);
        if (!deviceExists)
            return NotFound(new { error = "Device not found." });

        var rows = await _db.Set<DeviceCommand>()
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .Select(c => new
            {
                id = c.Id,
                type = c.Type,
                status = c.Status.ToString(),
                createdAt = c.CreatedAt,
                expiresAt = c.ExpiresAt,
                sentAt = c.SentAt,
                ackedAt = c.AckedAt,
                result = c.Result,
                error = c.Error,
                ackFirmwareVersion = c.AckFirmwareVersion,
                ackDiagnostics = c.AckDiagnosticsJson,
            })
            .ToListAsync(ct);

        return Ok(new { deviceId, commands = rows });
    }

    /// <summary>Owner recovery: set a parent's password (for a locked-out
    /// account when the reset-by-email flow isn't wired). Console-gated
    /// (fail-closed 404 unless an admin token is configured). Requires a
    /// reason; matches the account by normalized email so legacy casing/
    /// whitespace still resolves; never logs or echoes the new password.
    /// Writes only a loud structured log (no PII in an audit row).</summary>
    [HttpPost("parents/reset-password")]
    public async Task<IActionResult> ResetParentPassword(
        [FromBody] InternalParentPasswordResetRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email)
            || string.IsNullOrWhiteSpace(req.NewPassword) || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { error = "email, newPassword and reason are required." });
        if (req.NewPassword.Length < 8)
            return BadRequest(new { error = "New password must be at least 8 characters." });

        var email = ArmenianAiToy.Application.Helpers.EmailNormalizer.Normalize(req.Email);
        var parent = await _db.Parents.FirstOrDefaultAsync(
            p => (p.Email ?? "").Trim().ToLower() == email && p.AnonymizedAt == null, ct);
        if (parent is null)
            return NotFound(new { error = "No account found for that email." });

        var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";

        parent.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        // Durable audit row in the SAME SaveChanges as the password change —
        // this endpoint is a full-account-takeover primitive, so its record
        // must outlive log rotation. Never logs or stores the new password.
        _db.AuditEvents.Add(AuditEvent.InternalConsolePasswordReset(op, parent.Id, req.Reason.Trim()));
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Operator {Operator} reset the password for parent {ParentId} (reason: {Reason})",
            op, parent.Id, req.Reason.Trim());
        return Ok(new { reset = true });
    }

    private async Task<IActionResult> DeviceFlagActionAsync(
        Guid deviceId, InternalDeviceActionRequest? req, string action, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { error = "A reason is required for operator actions." });

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return NotFound(new { error = "Device not found." });

        bool changed;
        if (action == "device_revoke")
        {
            changed = device.IsRevoked != req.Value;
            device.IsRevoked = req.Value;
        }
        else
        {
            changed = device.IsPaused != req.Value;
            device.IsPaused = req.Value;
        }

        if (changed)
        {
            var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
            _db.AuditEvents.Add(AuditEvent.InternalConsoleAction(op, action, deviceId, req.Value, req.Reason.Trim()));
            await _db.SaveChangesAsync(ct);
            // Loud log: an operator overrode a device — worth a line even on success.
            _logger.LogWarning("Operator {Operator} performed {Action} value={Value} on device {DeviceId}",
                op, action, req.Value, deviceId);
        }

        return Ok(new { deviceId, action, value = req.Value, changed });
    }

    // #013: record WHO (the resolved console operator) read child-bearing data,
    // so an access can be traced in an incident. Best-effort — an audit-write
    // failure must never break the read. ActorParentId is null on the row, so it
    // stays out of every parent-facing feed (it shows only in the console's own
    // /audit tab). The operator name comes from the gate (Program.cs stashes it).
    private async Task AuditAccessAsync(string endpoint, Guid? targetId, int count, CancellationToken ct)
    {
        try
        {
            var op = HttpContext?.Items["InternalOperator"] as string ?? "unknown";
            _db.AuditEvents.Add(AuditEvent.InternalConsoleAccess(op, endpoint, targetId, count));
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Internal console access-audit write failed for {Endpoint}", endpoint);
        }
    }

    // Trims a message/snippet to a dashboard-friendly length.
    private static string Snippet(string? content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var trimmed = content.Trim();
        return trimmed.Length <= 140 ? trimmed : trimmed[..140] + "…";
    }

    // Parses the stored audit metadata blob into a JSON object so the wire
    // shape is a real object, not an escaped string (mirrors the parent
    // audit endpoint). Returns null on absent / unparseable metadata.
    private static JsonElement? ParseMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}

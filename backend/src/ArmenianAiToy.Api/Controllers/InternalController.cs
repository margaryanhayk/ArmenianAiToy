using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    public InternalController(
        AppDbContext db, ICuratedStoryLibrary library, OpenAICostMeter costMeter,
        LibraryStoryQuestionService questions, IModerationService moderation,
        IConfiguration config, ILogger<InternalController> logger)
    {
        _db = db;
        _library = library;
        _costMeter = costMeter;
        _questions = questions;
        _moderation = moderation;
        _config = config;
        _logger = logger;
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

        var rows = devices.Select(d => new AdminDeviceDto(
            Id: d.Id,
            Name: d.Name,
            MacAddress: d.MacAddress,
            FirmwareVersion: d.FirmwareVersion,
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
                .ToList()))
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

    /// <summary>Resolved console operator identity (from the auth gate) so the
    /// UI can show who is signed in — accountability. No data exposure; the
    /// name is whatever <c>InternalAdminAuth</c> resolved (a named operator, the
    /// shared-token sentinel, or the dev-bypass sentinel).</summary>
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
        => Ok(new { @operator = HttpContext?.Items["InternalOperator"] as string ?? "unknown" });

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

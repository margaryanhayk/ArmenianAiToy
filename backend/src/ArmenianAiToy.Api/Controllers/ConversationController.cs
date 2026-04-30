using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Parent-facing read-only conversation monitoring endpoints.
/// </summary>
[ApiController]
[Route("api/conversations")]
public class ConversationController : ControllerBase
{
    private const int MaxLimit = 100;

    private readonly IConversationService _conversationService;
    private readonly IParentService _parentService;

    public ConversationController(IConversationService conversationService, IParentService parentService)
    {
        _conversationService = conversationService;
        _parentService = parentService;
    }

    /// <summary>
    /// List full conversation history for an owned device.
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid deviceId,
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0)
    {
        if (!TryNormalizePagination(ref limit, offset, out var error))
            return BadRequest(new { error });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(deviceId))
            return Forbid();

        var conversations = await _conversationService.GetConversationHistoryAsync(deviceId, limit, offset);
        return Ok(new { conversations });
    }

    /// <summary>
    /// List lightweight conversation summaries with snippets for an owned device.
    /// </summary>
    [HttpGet("summary")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0)
    {
        if (!TryNormalizePagination(ref limit, offset, out var error))
            return BadRequest(new { error });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(deviceId))
            return Forbid();

        var conversations = await _conversationService.GetConversationSummariesAsync(deviceId, limit, offset);
        return Ok(new { conversations });
    }

    /// <summary>
    /// List safety-flagged messages (newest first) for an owned device.
    /// </summary>
    [HttpGet("flagged")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetFlagged(
        [FromQuery] Guid deviceId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0)
    {
        if (!TryNormalizePagination(ref limit, offset, out var error))
            return BadRequest(new { error });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(deviceId))
            return Forbid();

        var flaggedMessages = await _conversationService.GetFlaggedMessagesAsync(deviceId, limit, offset);
        return Ok(new { flaggedMessages });
    }

    /// <summary>
    /// E1.2 — server-aggregated daily snapshot for the parent dashboard's
    /// Today panel. Returns conversation, message, flagged-message, and
    /// assistant-audio counts for an owned device on today (UTC), plus the
    /// top 3 newest and top 3 flagged conversations of the day. Read-only;
    /// no schema or behavioral side effects.
    /// <para>
    /// Ownership is enforced through the same <see cref="IParentService.GetLinkedDeviceIdsAsync"/>
    /// gate used by /summary, /flagged, and /history; an unowned deviceId
    /// returns 403 Forbid (matching the deviceId-keyed convention; the
    /// silent-404 pattern is reserved for /{conversationId}).
    /// </para>
    /// <para>
    /// <c>asOfUtc</c> is an optional ISO8601 instant. When absent the
    /// server uses <c>DateTime.UtcNow</c>. ASP.NET Core's
    /// <see cref="DateTimeOffset"/> binder accepts any valid ISO8601
    /// (with or without an offset) and the server normalizes to UTC via
    /// <see cref="DateTimeOffset.UtcDateTime"/>; an unparseable value
    /// surfaces as a 400 from the model-binding pipeline before this
    /// action runs.
    /// </para>
    /// </summary>
    [HttpGet("today-summary")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetTodaySummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateTimeOffset? asOfUtc = null)
    {
        var asOf = asOfUtc?.UtcDateTime ?? DateTime.UtcNow;

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(deviceId))
            return Forbid();

        var summary = await _conversationService.GetTodaySummaryAsync(deviceId, asOf);
        return Ok(summary);
    }

    /// <summary>
    /// Get a single conversation with full message list.
    /// </summary>
    [HttpGet("{conversationId}")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid conversationId)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(conversationId);
        if (conversation is null)
            return NotFound();

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        // Same 404 for "not yours" as for "doesn't exist" — no existence leak.
        if (!linkedDevices.Contains(conversation.DeviceId))
            return NotFound();

        return Ok(new { conversation });
    }

    /// <summary>
    /// Delete a single conversation the authenticated parent owns.
    /// Messages cascade at the DB level via the schema FK. Silent 404 on
    /// ownership miss or unknown id — indistinguishable from each other,
    /// so a stranger parent cannot probe existence. Idempotent: a second
    /// call for the same (already-deleted) conversation id returns 404
    /// in the same shape. One <c>ParentConversationDeleted</c> audit row
    /// is written on success; the failure paths write nothing.
    /// </summary>
    [HttpDelete("{conversationId}")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid conversationId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deleted = await _parentService.DeleteConversationAsync(parentId, conversationId);
        if (!deleted)
            return NotFound(new { error = "Conversation not found or not owned by this account." });
        return Ok(new { deleted = true });
    }

    // Validates and clamps pagination inputs for the parent list endpoints.
    // - offset < 0           => reject
    // - limit  < 1           => reject
    // - limit  > MaxLimit    => clamp to MaxLimit (mutates the ref)
    private static bool TryNormalizePagination(ref int limit, int offset, out string? error)
    {
        if (offset < 0)
        {
            error = "offset must be >= 0";
            return false;
        }
        if (limit < 1)
        {
            error = "limit must be between 1 and " + MaxLimit;
            return false;
        }
        if (limit > MaxLimit)
        {
            limit = MaxLimit;
        }
        error = null;
        return true;
    }
}

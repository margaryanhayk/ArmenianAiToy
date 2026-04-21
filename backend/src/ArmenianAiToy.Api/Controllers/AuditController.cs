using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Parent-facing read-only audit history endpoint. Returns the
/// authenticated parent's own <c>AuditEvents</c> rows (filtered by
/// <c>ActorParentId</c>), newest first. Complements the write-only
/// audit skeleton landed in <c>14b2ac3</c>/<c>5e6e691</c>/<c>618ed89</c>.
///
/// This slice is endpoint-only — no dashboard tab exists yet. The feed
/// is per <em>actor parent</em>, not per device: a device shared with
/// another parent does not leak the other parent's actions into this
/// feed.
/// </summary>
[ApiController]
[Route("api/parents/audit")]
public class AuditController : ControllerBase
{
    private const int MaxLimit = 100;

    private readonly IParentService _parentService;

    public AuditController(IParentService parentService)
    {
        _parentService = parentService;
    }

    /// <summary>
    /// List the authenticated parent's audit events, newest first.
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetAudit(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0)
    {
        if (!TryNormalizePagination(ref limit, offset, out var error))
            return BadRequest(new { error });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var events = await _parentService.GetAuditEventsForParentAsync(parentId, limit, offset);
        return Ok(new { events });
    }

    // Pagination contract mirrors ConversationController.TryNormalizePagination:
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

using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Parent authentication and device-linking endpoints.
/// </summary>
[ApiController]
[Route("api/parents")]
public class ParentController : ControllerBase
{
    private readonly IParentService _parentService;
    private readonly ExportCooldown _exportCooldown;

    public ParentController(IParentService parentService, ExportCooldown exportCooldown)
    {
        _parentService = parentService;
        _exportCooldown = exportCooldown;
    }

    /// <summary>
    /// Register a new parent account.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Register([FromBody] ParentRegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        const int MinPasswordLength = 8;
        if (request.Password.Length < MinPasswordLength)
            return BadRequest(new { error = "Password must be at least 8 characters." });

        // C1: explicit consent capture. The DTO defaults AcceptedTerms to
        // false so a caller that omits the field is equivalent to declining.
        // Rejected at 400 (client error) rather than 409 (conflict) because
        // the request itself is malformed — no consent means no registration.
        if (!request.AcceptedTerms)
            return BadRequest(new { error = "You must accept the terms to register." });

        try
        {
            var parentId = await _parentService.RegisterAsync(
                request.Email, request.Password, request.AcceptedTerms);
            return Created("", new { parentId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Log in and receive a JWT token.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Login([FromBody] ParentLoginRequest request)
    {
        var result = await _parentService.LoginAsync(request.Email, request.Password);
        if (result == null)
            return Unauthorized(new { error = "Invalid email or password" });

        return Ok(result);
    }

    /// <summary>
    /// Change the authenticated parent's own password. Requires the current
    /// password for re-authentication. New password must satisfy the same
    /// length rule as registration (≥ 8 chars).
    /// </summary>
    [HttpPost("password")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> ChangePassword([FromBody] ParentChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Current password and new password are required." });

        // Same minimum length as ParentController.Register (ParentController.cs:35).
        const int MinPasswordLength = 8;
        if (request.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { error = "New password must be at least 8 characters." });

        if (request.CurrentPassword == request.NewPassword)
            return BadRequest(new { error = "New password must be different from the current password." });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var changed = await _parentService.ChangePasswordAsync(
            parentId, request.CurrentPassword, request.NewPassword);

        if (!changed)
            return BadRequest(new { error = "Current password is incorrect." });

        return Ok(new { changed = true });
    }

    /// <summary>
    /// Link an existing device to the authenticated parent.
    /// </summary>
    [HttpPost("devices/link")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> LinkDevice([FromBody] LinkDeviceRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linked = await _parentService.LinkDeviceAsync(parentId, request.DeviceId, request.ApiKey);

        if (!linked)
            return BadRequest(new { error = "Invalid device ID or API key" });

        return Ok(new { linked = true });
    }

    /// <summary>
    /// Unlink a device from the authenticated parent account. Idempotent —
    /// the response is identical whether a link existed or not, so a caller
    /// cannot probe whether a given (parent, device) pair is real. Removes
    /// only the join row; the Device, its Children, and its Conversations
    /// are preserved, and any other parents linked to the same device keep
    /// their link.
    /// </summary>
    [HttpDelete("devices/{deviceId}/link")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UnlinkDevice(Guid deviceId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _parentService.UnlinkDeviceAsync(parentId, deviceId);
        return Ok(new { unlinked = true });
    }

    /// <summary>
    /// Permanently delete the authenticated parent's account. Requires the
    /// current password as a second factor (re-authentication on top of
    /// the JWT). Deletes the Parent row, all ParentDevice links (via FK
    /// cascade), and any devices this parent was the sole owner of
    /// (orphan-aware, cascades children/conversations/messages). Devices
    /// still linked to another parent are preserved.
    /// </summary>
    [HttpDelete("account")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> DeleteAccount([FromBody] ParentDeleteAccountRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return BadRequest(new { error = "Current password is required." });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deleted = await _parentService.DeleteAccountAsync(parentId, request.CurrentPassword);
        if (!deleted)
            return BadRequest(new { error = "Current password is incorrect." });

        return Ok(new { deleted = true });
    }

    /// <summary>
    /// Per-child mode override setter. Three-valued per mode:
    /// null = inherit device, true = force on, false = force off. Calm has
    /// no override by design (MODES.md safety invariant). Parent must own
    /// the device the child belongs to; silent 404 on a miss. Full-
    /// replacement body — all four fields always supplied.
    /// </summary>
    [HttpPut("children/{childId}/mode-flags")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetChildModeOverrides(
        Guid childId, [FromBody] ChildModeOverridesRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ok = await _parentService.SetChildModeOverridesAsync(
            parentId, childId,
            request.Story, request.Game, request.Riddle, request.Curiosity);
        if (!ok)
            return NotFound(new { error = "Child not found or not owned by this account." });
        return Ok(new { modeOverrides = new
        {
            story = request.Story,
            game = request.Game,
            riddle = request.Riddle,
            curiosity = request.Curiosity
        } });
    }

    /// <summary>
    /// Delete a child profile and all of its conversations (messages cascade
    /// at the DB level). The authenticated parent must own the device the
    /// child belongs to. Silent 404 on ownership miss — indistinguishable
    /// from an unknown id, so a stranger parent cannot probe existence.
    /// Idempotent: a second call for the same (already-deleted) child id
    /// also returns 404 in the same shape.
    /// </summary>
    [HttpDelete("children/{childId}")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteChild(Guid childId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deleted = await _parentService.DeleteChildAsync(parentId, childId);
        if (!deleted)
            return NotFound(new { error = "Child not found or not owned by this account." });
        return Ok(new { deleted = true });
    }

    /// <summary>
    /// Pause a linked device. The authenticated parent must own the device.
    /// While paused, POST /api/chat short-circuits with a canned reply
    /// before any OpenAI call, so cost and conversation writes both stop.
    /// Idempotent — calling pause on an already-paused device returns 200.
    /// </summary>
    [HttpPost("devices/{deviceId}/pause")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> PauseDevice(Guid deviceId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ok = await _parentService.SetDevicePauseStateAsync(parentId, deviceId, paused: true);
        if (!ok)
            return NotFound(new { error = "Device not found or not linked to this account." });
        return Ok(new { paused = true });
    }

    /// <summary>
    /// Resume a paused linked device. Mirror of the pause endpoint.
    /// Idempotent — calling resume on an already-active device returns 200.
    /// </summary>
    [HttpPost("devices/{deviceId}/resume")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ResumeDevice(Guid deviceId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ok = await _parentService.SetDevicePauseStateAsync(parentId, deviceId, paused: false);
        if (!ok)
            return NotFound(new { error = "Device not found or not linked to this account." });
        return Ok(new { paused = false });
    }

    /// <summary>
    /// B4: set (or disable) the bedtime window on a linked device. While the
    /// current local time on the device is inside the window, POST /api/chat
    /// short-circuits with the same canned reply a paused device returns.
    /// Pause wins — if <c>Device.IsPaused</c> is true, the bedtime window
    /// is moot. Half-null (one end set, the other null) is accepted and
    /// normalized to "disabled" — the endpoint is idempotent for clearing.
    /// </summary>
    [HttpPut("devices/{deviceId}/bedtime-window")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetBedtimeWindow(Guid deviceId, [FromBody] BedtimeWindowRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ok = await _parentService.SetBedtimeWindowAsync(
            parentId, deviceId, request.Start, request.End);
        if (!ok)
            return NotFound(new { error = "Device not found or not linked to this account." });
        return Ok(new { bedtimeWindow = new { start = request.Start, end = request.End } });
    }

    /// <summary>
    /// B5: set the four per-mode availability flags on a linked device. Full
    /// replacement — the body always supplies all four bools. When a mode is
    /// disabled, POST /api/chat short-circuits with a warm canned reply when
    /// the detected mode matches the disabled flag, before any OpenAI call.
    /// Calm has no flag here by design (safety invariant from MODES.md).
    /// </summary>
    [HttpPut("devices/{deviceId}/mode-flags")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetDeviceModeFlags(
        Guid deviceId, [FromBody] DeviceModeFlagsRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ok = await _parentService.SetDeviceModeFlagsAsync(
            parentId, deviceId,
            request.Story, request.Game, request.Riddle, request.Curiosity);
        if (!ok)
            return NotFound(new { error = "Device not found or not linked to this account." });
        return Ok(new { modeFlags = new
        {
            story = request.Story,
            game = request.Game,
            riddle = request.Riddle,
            curiosity = request.Curiosity
        } });
    }

    /// <summary>
    /// List linked devices with child info and last activity.
    /// </summary>
    [HttpGet("devices/details")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDeviceDetails()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var devices = await _parentService.GetLinkedDeviceDetailsAsync(parentId);
        return Ok(new { devices });
    }

    /// <summary>
    /// List device IDs linked to the authenticated parent.
    /// </summary>
    [HttpGet("devices")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDevices()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deviceIds = await _parentService.GetLinkedDeviceIdsAsync(parentId);
        return Ok(new { devices = deviceIds });
    }

    /// <summary>
    /// Download a full JSON export of the authenticated parent's own data:
    /// profile (safe fields), linked devices (safe fields) with nested
    /// children and conversations, and the per-actor audit feed. Response
    /// is a single JSON document delivered as a timestamp-named attachment.
    /// Password hash and device API keys are deliberately omitted; the
    /// response envelope's <c>excludedFields</c> array documents the
    /// exclusions inline.
    /// <para>
    /// Guarded by a per-parent cooldown (see
    /// <see cref="ExportCooldown"/>); exceeded callers receive 429 with a
    /// <c>Retry-After</c> header. Each successful call writes one
    /// <c>ParentDataExported</c> audit row with counts-only metadata.
    /// </para>
    /// </summary>
    [HttpGet("export")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Export()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!_exportCooldown.TryReserve(parentId, out var retryAfter))
        {
            // Whole-seconds rounding keeps the header consistent with the
            // same convention ASP.NET's rate limiter uses on its 429s.
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            Response.Headers.RetryAfter = seconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "Export already requested recently. Please retry shortly.", retryAfterSeconds = seconds });
        }

        var export = await _parentService.BuildExportAsync(parentId);
        if (export is null)
            return NotFound();

        // Timestamp-only filename — no email or other PII in Content-Disposition.
        var filename = "areg-export-" + export.GeneratedAt.ToString("yyyyMMddTHHmmssZ") + ".json";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        return Ok(export);
    }
}

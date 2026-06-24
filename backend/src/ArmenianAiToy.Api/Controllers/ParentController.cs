using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration? _config;

    /// <summary>
    /// DI constructor. <paramref name="config"/> is nullable so
    /// existing controller tests that construct this type directly
    /// (without going through the DI container) don't have to thread
    /// an IConfiguration through. Only the Google sign-in paths
    /// consult it; every other endpoint is config-independent at
    /// this layer. Production DI always supplies a real
    /// IConfiguration.
    /// </summary>
    public ParentController(
        IParentService parentService,
        ExportCooldown exportCooldown,
        IConfiguration? config = null)
    {
        _parentService = parentService;
        _exportCooldown = exportCooldown;
        _config = config;
    }

    /// <summary>
    /// Register a new parent account. Anti-enumeration response: the
    /// new-email and already-registered-email paths both return 201
    /// with an identical neutral body, and
    /// <see cref="IParentService.RegisterAsync"/> pays the same BCrypt
    /// latency on both paths so response timing cannot be used as an
    /// account-existence oracle. Request-shape validation still returns
    /// 400 (empty / short password / consent missing) — those checks
    /// inspect only the submitted fields and do not leak anything about
    /// the registered set.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
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
        // Rejected at 400 (client error) — the request itself is malformed;
        // no consent means no registration. The service carries an
        // equivalent defense-in-depth throw, but the controller is the
        // normal enforcement point.
        if (!request.AcceptedTerms)
            return BadRequest(new { error = "You must accept the terms to register." });

        await _parentService.RegisterAsync(
            request.Email, request.Password, request.AcceptedTerms);
        // Neutral body — shape matches other parent destructive endpoints
        // (`{ deleted: true }`, `{ paused: true }`, etc.) and is byte-for-
        // byte identical whether the email was new or already registered.
        // `parentId` is deliberately NOT echoed: a per-request identifier
        // would be a first-class enumeration signal.
        return Created("", new { registered = true });
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
    /// Begin a password-reset flow. Anti-enumeration response: the
    /// known-email and unknown-email paths both return <b>202 Accepted</b>
    /// with the identical neutral body
    /// <c>{ "resetRequested": true }</c>. The service pays the same
    /// BCrypt latency on both paths (same seam
    /// <see cref="ParentController.Register"/> uses) so response timing
    /// cannot be used as an account-existence oracle. No authentication
    /// required — the parent has, by definition, forgotten their
    /// password. Rate-limited via the auth policy.
    /// </summary>
    [HttpPost("password/reset-request")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(202)]
    [ProducesResponseType(400)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] ParentPasswordResetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        await _parentService.RequestPasswordResetAsync(request.Email);
        // Byte-identical body on both paths. No parent id, no token,
        // no "sent to <email>" echo — all of those would leak existence.
        return Accepted(new { resetRequested = true });
    }

    /// <summary>
    /// Complete a password reset with a previously-issued token. On any
    /// failure — unknown token, expired, already consumed — the response
    /// is a uniform 400 without distinguishing the reason. No JWT is
    /// issued; the parent logs in separately after a successful reset.
    /// Rate-limited via the auth policy.
    /// </summary>
    [HttpPost("password/reset")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> CompletePasswordReset(
        [FromBody] ParentPasswordResetCompleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Reset link is invalid or expired." });

        // Same minimum-length rule as Register / ChangePassword. Reject
        // before touching the DB so an obviously-weak password can't
        // consume a single-use token.
        const int MinPasswordLength = 8;
        if (request.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { error = "Reset link is invalid or expired." });

        var ok = await _parentService.CompletePasswordResetAsync(
            request.Token, request.NewPassword);
        if (!ok)
            return BadRequest(new { error = "Reset link is invalid or expired." });

        return Ok(new { reset = true });
    }

    /// <summary>
    /// Begin an email-verification flow for the given email.
    /// Anti-enumeration contract: known-unverified, known-verified,
    /// and unknown-email paths all return <b>202 Accepted</b> with
    /// the identical neutral body <c>{ "verificationRequested": true }</c>.
    /// The service pays the same BCrypt latency on every branch so
    /// response timing cannot be used as an account-existence or
    /// verification-state oracle. No authentication required — the
    /// parent may not yet be able to log in. Rate-limited via the
    /// auth policy.
    /// </summary>
    [HttpPost("verify-request")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(202)]
    [ProducesResponseType(400)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> RequestEmailVerification(
        [FromBody] ParentEmailVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        await _parentService.RequestEmailVerificationAsync(request.Email);
        // Byte-identical body on all three branches. No parent id, no
        // token, no state echo — all of those would leak existence
        // or verification state.
        return Accepted(new { verificationRequested = true });
    }

    /// <summary>
    /// Complete email verification with a previously-issued token.
    /// On any failure — unknown token, expired, already consumed,
    /// empty — the response is a uniform 400 without distinguishing
    /// the reason, mirroring the password-reset completion contract.
    /// No JWT is issued. Rate-limited via the auth policy.
    /// </summary>
    [HttpPost("verify")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> CompleteEmailVerification(
        [FromBody] ParentEmailVerificationCompleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Verification link is invalid or expired." });

        var ok = await _parentService.CompleteEmailVerificationAsync(request.Token);
        if (!ok)
            return BadRequest(new { error = "Verification link is invalid or expired." });

        return Ok(new { verified = true });
    }

    /// <summary>
    /// Exchange a Google ID token for a parent JWT. Additive to the
    /// email/password flow — returns the same response shape as
    /// <see cref="Login"/> on success. Feature-gated: when
    /// <c>GoogleAuth:ClientId</c> is missing/empty, the endpoint
    /// returns 404 (concealment rather than a loud 503, same
    /// fail-closed posture as <c>/metrics</c>). Uniform auth-failure
    /// shape for every rejection reason — invalid token / unverified
    /// email / audience mismatch / GoogleSubject collision — to avoid
    /// turning the endpoint into an oracle for any of those states.
    /// Rate-limited via the auth policy.
    /// </summary>
    [HttpPost("google-login")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> GoogleLogin([FromBody] ParentGoogleLoginRequest request)
    {
        // Feature-off fail-closed — same concealment posture as
        // MetricsScrapeAuth. A scanner that finds this URL learns
        // nothing about whether Google sign-in is "on but broken" vs
        // "not configured."
        var clientId = _config?["GoogleAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.IdToken))
            return Unauthorized(new { error = "Sign-in failed." });

        var result = await _parentService.GoogleSignInAsync(
            request.IdToken, request.AcceptedTerms);
        return result.Status switch
        {
            GoogleSignInStatus.Success =>
                Ok(new ParentLoginResponse(result.Token!)),
            GoogleSignInStatus.TermsRequired =>
                BadRequest(new { error = "You must accept the terms to continue." }),
            _ => Unauthorized(new { error = "Sign-in failed." })
        };
    }

    /// <summary>
    /// Public UI-visibility probe: returns the configured Google
    /// client id so the dashboard can decide whether to render the
    /// "Continue with Google" button. Returns <c>null</c> when the
    /// feature is off; the dashboard hides the button in that case.
    /// Not rate-limited — the payload is static per deployment and
    /// not an account-existence signal.
    /// </summary>
    [HttpGet("google-config")]
    [ProducesResponseType(200)]
    public IActionResult GetGoogleConfig()
    {
        var clientId = _config?["GoogleAuth:ClientId"];
        return Ok(new GoogleAuthConfigResponse(
            string.IsNullOrWhiteSpace(clientId) ? null : clientId));
    }

    /// <summary>
    /// Minimal authenticated-parent profile lookup. Returns email
    /// and verification timestamp only. Used by the dashboard's
    /// verification-visibility surface so the "Send verification
    /// email" button can pass the parent's email to
    /// <see cref="RequestEmailVerification"/> without a form input.
    /// 404 on a parent whose row no longer exists or has been
    /// anonymized — same shape as other parent-owned read endpoints.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMe()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await _parentService.GetMeAsync(parentId);
        if (me is null)
            return NotFound();
        return Ok(me);
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
    /// Phase A.2 — consumer pairing: claim a device to the authenticated parent
    /// using the single-use claim code from the toy's QR (NOT its API key). On
    /// success the toy is linked + the code consumed. Rate-limited on the per-IP
    /// auth bucket because the claim code is a guessable secret (brute-force
    /// surface), and every failure reason returns ONE uniform 400 so a caller
    /// cannot probe which devices exist or whether a code was close.
    /// </summary>
    [HttpPost("devices/claim")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> ClaimDevice([FromBody] DeviceClaimRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var claimed = await _parentService.ClaimDeviceAsync(
            parentId, request.DeviceId, request.ClaimCode);

        if (!claimed)
            // Uniform failure for every reason (unknown device / already-claimed
            // / wrong code) — no existence leak.
            return BadRequest(new { error = "That code didn't work. Check the code on your toy and try again." });

        return Ok(new { claimed = true });
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
    /// #074: revoke (or restore) a linked device's server-side credential.
    /// When revoked, EVERY device-auth path returns 401 — a leaked or
    /// compromised device key can be killed centrally without re-flashing; the
    /// device is dead until it re-provisions a fresh key (registration).
    /// Reversible (restore with revoked=false). Distinct from pause, which
    /// only quiets the toy while it still authenticates. Idempotent;
    /// ownership-checked; silent 404 on a device not linked to this account.
    /// </summary>
    [HttpPut("devices/{deviceId}/revoke")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetDeviceRevocation(
        Guid deviceId, [FromBody] DeviceRevocationRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ok = await _parentService.SetDeviceRevocationAsync(parentId, deviceId, request.Revoked);
        if (!ok)
            return NotFound(new { error = "Device not found or not linked to this account." });
        return Ok(new { revoked = request.Revoked });
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
    /// List linked devices with child info and last activity. The
    /// response envelope also carries a small self-scoped dormancy
    /// summary (device counts + raw <c>LastLoginAt</c>) — reporting-
    /// only, backward-compatible: pre-slice clients reading only
    /// <c>devices</c> continue to work unchanged.
    /// </summary>
    [HttpGet("devices/details")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDeviceDetails()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var response = await _parentService.GetLinkedDeviceDetailsWithSummaryAsync(parentId);
        return Ok(response);
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

    /// <summary>
    /// C2.1 — stream Areg's synthesized audio for a single assistant
    /// message back to the authenticated parent. Drives the
    /// dashboard's ▶ Listen affordance.
    /// <para>
    /// <b>Assistant-only.</b> Child/user audio uploads are never
    /// replayable here even if their <c>AudioBlobPath</c> is
    /// populated — child voice playback is out of scope for C2.1.
    /// The role gate lives in
    /// <see cref="IParentService.GetAssistantAudioMessageAsync"/>.
    /// </para>
    /// <para>
    /// <b>Uniform 404.</b> Every miss reason — unknown message id,
    /// message owned by a different family, child/user role,
    /// <c>AudioBlobPath</c> null, blob file missing on disk —
    /// collapses to the same response body so a parent cannot probe
    /// existence, ownership, or attachment state across families.
    /// </para>
    /// <para>
    /// The blob store is resolved via <c>[FromServices]</c> rather
    /// than a constructor parameter so existing controller-construction
    /// tests do not have to thread a fourth dependency through.
    /// </para>
    /// </summary>
    [HttpGet("messages/{messageId}/audio")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMessageAudio(
        Guid messageId,
        [FromServices] IAudioBlobStore blobStore,
        CancellationToken cancellationToken)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var hit = await _parentService.GetAssistantAudioMessageAsync(parentId, messageId);
        if (hit is null)
            return NotFound(new { error = "Audio not available." });

        var blob = await blobStore.ReadAsync(
            hit.Value.ConversationId, hit.Value.MessageId, cancellationToken);
        if (blob is null)
            return NotFound(new { error = "Audio not available." });

        // Defense-in-depth: this endpoint's contract is "assistant MP3
        // replay only." Today the only writer (AudioChatController) only
        // ever persists assistant audio as MP3, so this branch is dead
        // in practice. Pin the contract at the HTTP boundary anyway —
        // a future codec change, a manual file placement, or a misbehaving
        // blob-store implementation must not be able to serve a non-MP3
        // payload through this endpoint. Same uniform-404 body so an
        // unexpected MIME is indistinguishable from "no blob" on the wire.
        if (!string.Equals(blob.Value.MimeType, "audio/mpeg", StringComparison.OrdinalIgnoreCase))
            return NotFound(new { error = "Audio not available." });

        return File(blob.Value.Content, "audio/mpeg");
    }
}

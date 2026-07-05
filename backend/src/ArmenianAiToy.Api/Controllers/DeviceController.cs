using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Api.Controllers;

[ApiController]
[Route("api/devices")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly IConfiguration _config;

    public DeviceController(IDeviceService deviceService, IConfiguration config)
    {
        _deviceService = deviceService;
        _config = config;
    }

    // #010: rate-limited on the per-IP `auth` bucket (10/60s).
    // #009: provisioning gate — fail-closed. Registration mints a credential that
    //   can drive the paid STT+GPT+TTS endpoints, so anonymous minting is a
    //   denial-of-wallet vector. Requires the Devices:ProvisioningSecret in the
    //   X-Provisioning-Secret header, OR the explicit Devices:AllowOpenRegistration
    //   dev/bench bypass; the shipped default (neither) DENIES.
    // #011: an EXISTING device is rotated only on an explicit X-Force-Rotate
    //   request — a plain re-registration is refused (409) so a caller cannot
    //   silently rotate an in-field device's key.
    [HttpPost("register")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(409)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Register([FromBody] DeviceRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.MacAddress))
            return BadRequest(new { error = "MacAddress is required" });

        var presented = Request.Headers[DeviceProvisioningAuth.SecretHeader].ToString();
        var decision = DeviceProvisioningAuth.Evaluate(
            string.IsNullOrEmpty(presented) ? null : presented,
            _config["Devices:ProvisioningSecret"],
            bool.TryParse(_config["Devices:AllowOpenRegistration"], out var open) && open);
        if (decision == DeviceProvisioningAuth.Decision.Deny)
            return Unauthorized(new { error = "Device registration is not permitted." });

        var force = bool.TryParse(Request.Headers["X-Force-Rotate"].ToString(), out var f) && f;
        var result = await _deviceService.RegisterDeviceAsync(request, allowReRegister: force);
        if (result is null)
            return Conflict(new
            {
                error = "Device already registered. Re-provision with X-Force-Rotate: true to rotate its key."
            });
        return Created("", result);
    }

    // Platform presence (consumer app online/offline dot). The toy POSTs here
    // periodically when idle. Device-authed: DeviceAuthMiddleware validates the
    // X-Device-Id / X-Api-Key headers AND refreshes Device.LastSeenAt (throttled,
    // #034) BEFORE this action runs — so this endpoint only has to acknowledge.
    // The parent dashboard derives LinkedDeviceDto.IsOnline from LastSeenAt.
    // Deliberately minimal (no commands/config pushed back yet) — presence only.
    [HttpPost("heartbeat")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Heartbeat(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeviceHeartbeatRequest? request = null)
    {
        // DeviceId is guaranteed present: the middleware sets it for this path
        // after a successful credential check (else the request 401s upstream).
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;

        // Body is optional (EmptyBodyBehavior.Allow) — the legacy presence-only
        // heartbeat still works. Only a body carrying firmware fields writes.
        if (request is not null && request.HasAnyFirmwareField)
        {
            await _deviceService.UpdateFirmwareReportAsync(deviceId, request, DateTime.UtcNow);
        }
        return Ok(new { ok = true, deviceId, serverTimeUtc = DateTime.UtcNow });
    }

    // Device polls its command queue. Device-authed (middleware). Returns only
    // this device's deliverable commands (Pending/Sent, not expired) and marks
    // Pending → Sent. The toy connects OUTBOUND only — there is no inbound
    // server on the device.
    [HttpGet("commands")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetCommands(
        [FromServices] IDeviceCommandService commands)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var list = await commands.PollAsync(deviceId, DateTime.UtcNow);
        return Ok(new { commands = list });
    }

    // Device acknowledges a command's outcome. Idempotent; a command owned by
    // another device returns 404 (no cross-device existence leak).
    [HttpPost("commands/{id:guid}/ack")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> AckCommand(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeviceCommandAckRequest? request,
        [FromServices] IDeviceCommandService commands)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var outcome = await commands.AckAsync(
            deviceId, id, request ?? new DeviceCommandAckRequest(), DateTime.UtcNow);
        if (outcome == DeviceCommandAckOutcome.NotFound)
        {
            return NotFound(new { error = "Command not found." });
        }
        return Ok(new { acked = true });
    }

    // Device asks whether a firmware update is available for it. Compares the
    // device's reported version/board (from heartbeat) against the configured
    // current release. Returns { updateAvailable: false } or the signed manifest.
    [HttpGet("firmware-manifest")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetFirmwareManifest(
        [FromServices] IFirmwareManifestService manifest)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var device = await _deviceService.GetDeviceAsync(deviceId);
        var result = manifest.Build(device?.FirmwareVersion, device?.BoardModel, DateTime.UtcNow);
        return Ok(result);
    }

    // Streams the configured firmware .bin to the device. Device-authed via
    // DeviceAuthMiddleware (a revoked device 401s before reaching here) —
    // deliberately NOT a public wwwroot file. Fail-closed 404 whenever the
    // release is disabled, no ImagePath is configured, the path is not
    // absolute, or the file is missing — the device treats any non-200 as
    // download_failed and keeps its current firmware. Range processing is on
    // so a future resume slice works without changes. Integrity is carried by
    // the SIGNED MANIFEST's sha256/sizeBytes, which the device verifies while
    // streaming — this endpoint just moves bytes.
    [HttpGet("firmware-image")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public IActionResult GetFirmwareImage(
        [FromServices] FirmwareUpdateOptions options,
        [FromServices] ILogger<DeviceController> logger)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ImagePath))
        {
            return NotFound(new { error = "No firmware image available." });
        }
        if (!Path.IsPathRooted(options.ImagePath) || !System.IO.File.Exists(options.ImagePath))
        {
            // Misconfiguration is an operator problem, not a device problem —
            // log loudly, answer with the same safe 404.
            logger.LogWarning(
                "Firmware image path missing or not absolute: {ImagePath}", options.ImagePath);
            return NotFound(new { error = "No firmware image available." });
        }
        return PhysicalFile(options.ImagePath, "application/octet-stream",
            enableRangeProcessing: true);
    }

    // Cloud→SD content sync (minimal slice): the story-audio set this device
    // should hold on its SD card. Device-authed (middleware) — a revoked
    // device 401s before reaching here. Static single-item config today; a
    // later slice makes it per-device/per-tier on the same contract.
    [HttpGet("content-manifest")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public IActionResult GetContentManifest(
        [FromServices] IContentManifestService manifest)
    {
        return Ok(manifest.Build());
    }

    // Streams the configured story MP3 to the device. Same fail-closed
    // posture as firmware-image: 404 whenever sync is disabled, no path is
    // configured, the path is not absolute, or the file is missing. NOT a
    // public wwwroot file. Integrity is carried by the manifest's
    // sha256/sizeBytes, which the device verifies while streaming to SD.
    [HttpGet("content-file")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public IActionResult GetContentFile(
        [FromServices] ContentSyncOptions options,
        [FromServices] ILogger<DeviceController> logger)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.AudioPath))
        {
            return NotFound(new { error = "No content available." });
        }
        if (!Path.IsPathRooted(options.AudioPath) || !System.IO.File.Exists(options.AudioPath))
        {
            logger.LogWarning(
                "Content audio path missing or not absolute: {AudioPath}", options.AudioPath);
            return NotFound(new { error = "No content available." });
        }
        return PhysicalFile(options.AudioPath, "audio/mpeg",
            enableRangeProcessing: true);
    }
}

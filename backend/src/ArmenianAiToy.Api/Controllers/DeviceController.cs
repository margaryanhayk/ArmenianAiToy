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

        // Length bounds: the derived device name takes the MAC's last 4
        // chars (`request.MacAddress[^4..]`), which throws — HTTP 500 — on
        // a value shorter than 4. Reject at the boundary instead. Upper cap
        // bounds request cost / storage. A real MAC is 12–17 chars.
        var macLen = request.MacAddress.Trim().Length;
        if (macLen < 4 || macLen > 64)
            return BadRequest(new { error = "MacAddress must be 4 to 64 characters." });

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

    // Store-and-forward upload of story playback events. The toy plays stories
    // from its SD cache (offline path), queues a tiny record per play in NVS,
    // and uploads the queue here whenever Wi-Fi is up — deleting queued events
    // only after a 2xx. At-least-once transport: every event carries a
    // device-generated idempotency key, so a re-upload inserts nothing twice
    // and `accepted` may legitimately be 0. Device-authed (middleware).
    [HttpPost("story-plays")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ReportStoryPlays(
        [FromBody] StoryPlayReportRequest request)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        if (request?.Events is null || request.Events.Count == 0)
        {
            return BadRequest(new { error = "At least one event is required." });
        }
        if (request.Events.Count > StoryPlayReportRequest.MaxEvents)
        {
            return BadRequest(new
            {
                error = $"At most {StoryPlayReportRequest.MaxEvents} events per upload."
            });
        }
        var accepted = await _deviceService.ReportStoryPlaysAsync(
            deviceId, request, DateTime.UtcNow);
        return Ok(new { accepted });
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

    // Cloud→SD content sync: the story-audio set this device should hold on
    // its SD card. Device-authed (middleware) — a revoked device 401s before
    // reaching here. Static config for every device today (N stories, in
    // configured order); per-device/per-tier entitlement is a later slice on
    // the same contract.
    [HttpGet("content-manifest")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetContentManifest(
        [FromServices] IContentManifestService manifest)
    {
        // B3 — stamp this device's spoken-story-intro flag onto the static
        // manifest. Additive field: pre-B3 firmware ignores it; new firmware
        // caches the last-known value so the toggle applies offline. Missing
        // device row (shouldn't happen behind the auth middleware) falls back
        // to the shipped default (ON).
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var device = await _deviceService.GetDeviceAsync(deviceId);
        return Ok(manifest.Build() with
        {
            StoryIntroEnabled = device?.StoryIntroEnabled ?? true,
        });
    }

    // Streams a configured story MP3 to the device. Same fail-closed
    // posture as firmware-image: 404 whenever sync is disabled, no path is
    // configured, the path is not absolute, or the file is missing. NOT a
    // public wwwroot file. Integrity is carried by the manifest's
    // sha256/sizeBytes, which the device verifies while streaming to SD.
    //
    // storyId selects among the configured stories. It is omitted by
    // pre-multi-story firmware (and by a legacy single-item config, whose
    // manifest still advertises the bare route), so an absent storyId
    // resolves to the only configured story — and 404s when there is more
    // than one, because guessing which story the device meant is worse than
    // refusing. storyId is ONLY a lookup key against configured items; it
    // never reaches the filesystem, so it carries no traversal risk.
    [HttpGet("content-file")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public IActionResult GetContentFile(
        [FromServices] ContentSyncOptions options,
        [FromServices] ILogger<DeviceController> logger,
        [FromQuery] string? storyId = null,
        [FromQuery] string? clip = null)
    {
        if (!options.Enabled)
        {
            return NotFound(new { error = "No content available." });
        }

        var stories = options.ResolveStories();
        ContentSyncStoryOptions? story;
        if (!string.IsNullOrWhiteSpace(storyId))
        {
            story = stories.FirstOrDefault(s =>
                string.Equals(s.StoryId, storyId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            story = stories.Count == 1 ? stories[0] : null;
        }
        if (story is null)
        {
            return NotFound(new { error = "No content available." });
        }

        // B2 — `clip` selects a per-story clip (intro/question/summary)
        // instead of the narration. Like storyId, it is ONLY a lookup key
        // against configured items — it never reaches the filesystem, so it
        // carries no traversal risk. Unknown kind → same uniform 404.
        var audioPath = story.AudioPath;
        var pathOwnerId = story.StoryId;
        if (!string.IsNullOrWhiteSpace(clip))
        {
            var clipItem = story.Clips.FirstOrDefault(c =>
                string.Equals(c.Kind, clip, StringComparison.OrdinalIgnoreCase));
            if (clipItem is null)
            {
                return NotFound(new { error = "No content available." });
            }
            audioPath = clipItem.AudioPath;
            pathOwnerId = $"{story.StoryId}:{clipItem.Kind}";
        }

        if (string.IsNullOrWhiteSpace(audioPath))
        {
            return NotFound(new { error = "No content available." });
        }
        if (!Path.IsPathRooted(audioPath) || !System.IO.File.Exists(audioPath))
        {
            logger.LogWarning(
                "Content audio path missing or not absolute for {StoryId}: {AudioPath}",
                pathOwnerId, audioPath);
            return NotFound(new { error = "No content available." });
        }
        return PhysicalFile(audioPath, "audio/mpeg",
            enableRangeProcessing: true);
    }
}

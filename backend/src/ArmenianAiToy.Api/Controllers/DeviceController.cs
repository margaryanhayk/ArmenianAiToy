using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

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
        // Slice E — the toy has no wall clock, so the SERVER tells it whether
        // the bedtime window is active right now; the firmware caches the
        // last-known value between heartbeats. Additive field: legacy builds
        // ignore it.
        var inBedtimeWindow = await _deviceService.IsDeviceInBedtimeWindowAsync(
            deviceId, DateTime.UtcNow);
        // The toy also learns its PAUSE state here so a paused toy stays
        // fully silent even for local SD playback (pause used to gate only
        // the online chat path). Cached last-known between heartbeats.
        var isPaused = await _deviceService.IsDevicePausedAsync(deviceId);
        return Ok(new
        {
            ok = true,
            deviceId,
            serverTimeUtc = DateTime.UtcNow,
            inBedtimeWindow,
            isPaused,
        });
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

    /// <summary>
    /// Welcome-flow — turns the child's spoken answer to a menu question into
    /// ONE bounded intent token. The toy asks «ի՞նչ անենք» (or offers a story)
    /// through a pre-rendered clip, records the reply, and posts the WAV here.
    ///
    /// <para>
    /// <b>This is not, and must never become, a chat endpoint.</b> The response
    /// is a single value from a closed 8-token set, so there is no field a
    /// model output could travel through, and NO model is called: after STT the
    /// classification is the deterministic keyword matcher
    /// <see cref="ModeDetector"/> (or <see cref="WelcomeIntentDetector"/> for a
    /// yes/no offer). One paid call per turn — speech-to-text — and nothing
    /// else. Pinned by
    /// <c>VoiceIntentTests.VoiceIntent_ResponseShape_IsBoundedTokenOnly</c>.
    /// </para>
    ///
    /// <para>
    /// Nothing is persisted. A one-word menu answer is not a conversation, and
    /// writing one would create a retention/export surface for no parent value.
    /// The transcript reaches moderation and the log, never the response.
    /// </para>
    ///
    /// <para>
    /// Gate chain and cost cap run BEFORE any paid call, exactly as
    /// <c>StoryQaController.AnswerReflection</c> does. Every refusal is a
    /// 200 with <c>unknown</c> rather than a status code: the firmware's
    /// handling for "I didn't understand" is already the graceful default, so
    /// a second failure branch on the device would be dead weight.
    /// </para>
    /// </summary>
    [HttpPost("voice-intent")]
    // #060 — cap the buffered audio body (the toy sends <= ~0.5 MB WAV).
    [RequestSizeLimit(2_000_000)]
    [EnableRateLimiting(ChatRateLimiter.PolicyName)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(413)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> VoiceIntent(
        [FromServices] IAudioTranscriptionService transcription,
        [FromServices] IModerationService moderation,
        [FromServices] IChildService childService,
        [FromServices] OpenAICostMeter costMeter,
        [FromServices] IOptions<OpenAIDailyCostCapOptions> costCapOptions,
        [FromServices] ILogger<DeviceController> logger,
        [FromQuery] string expect = VoiceIntentExpectations.Mode,
        CancellationToken cancellationToken = default)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var yesNoQuestion = string.Equals(
            expect, VoiceIntentExpectations.YesNo, StringComparison.OrdinalIgnoreCase);

        // Gate chain pause > bedtime, before anything paid. A paused toy is
        // silent anyway (the firmware never gets this far), but the server
        // must not be the reason a paused device can still spend money.
        if (await _deviceService.IsDevicePausedAsync(deviceId))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "paused"));
            return VoiceIntentResult(VoiceIntents.Unknown);
        }
        if (await _deviceService.IsDeviceInBedtimeWindowAsync(deviceId, DateTime.UtcNow))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "bedtime"));
            return VoiceIntentResult(VoiceIntents.Unknown);
        }

        var costCapOpts = costCapOptions.Value;
        if (costCapOpts.Enabled)
        {
            var nowUtc = DateTime.UtcNow;
            if (costCapOpts.Global > 0m && costMeter.IsGlobalOverCap(costCapOpts.Global, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(
                    1, new KeyValuePair<string, object?>("kind", "voice_intent"));
                return VoiceIntentResult(VoiceIntents.Unknown);
            }
            if (costMeter.IsOverCap(deviceId, costCapOpts.CapForDevice(deviceId), nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(
                    1, new KeyValuePair<string, object?>("kind", "voice_intent"));
                return VoiceIntentResult(VoiceIntents.Unknown);
            }
        }

        var inboundContentType = Request.ContentType;
        if (string.IsNullOrWhiteSpace(inboundContentType))
            inboundContentType = "audio/wav";

        using var audioBuffer = new MemoryStream();
        await Request.Body.CopyToAsync(audioBuffer, cancellationToken);
        if (audioBuffer.Length == 0)
        {
            return BadRequest(new { error = "Audio body is required" });
        }
        var audioBytes = audioBuffer.ToArray();

        // Voice → text. The bias prompt is scoped to what was actually asked;
        // on a one-word answer from a five-year-old that is the single biggest
        // accuracy lever we have, and it costs nothing.
        string transcript;
        try
        {
            using var sttStream = new MemoryStream(audioBytes, writable: false);
            transcript = await transcription.TranscribeArmenianAsync(
                sttStream,
                inboundContentType!,
                yesNoQuestion
                    ? VoiceIntentExpectations.YesNoBiasPrompt
                    : VoiceIntentExpectations.ModeBiasPrompt,
                cancellationToken,
                model: _config["Devices:VoiceIntentTranscriptionModel"]);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // A transient STT failure is indistinguishable, to the child, from
            // not being heard — and the toy already handles that.
            logger.LogWarning(ex, "Voice-intent: STT failure for device {DeviceId}", deviceId);
            return VoiceIntentResult(VoiceIntents.Unknown);
        }

        try
        {
            costMeter.Record(
                deviceId,
                OpenAICostEstimator.EstimateWhisperCostUsd(audioBytes.LongLength),
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // Metering must never be the reason a child's answer is dropped.
            logger.LogWarning(ex, "Voice-intent: cost metering failed");
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return VoiceIntentResult(VoiceIntents.Unknown);
        }

        // Input moderation, fail-closed like everywhere else. An unsafe
        // utterance can never select a mode — it reads as "not understood",
        // which is also what the child experiences.
        var inputModeration = await moderation.CheckContentAsync(transcript);
        if (!inputModeration.IsSafe)
        {
            logger.LogWarning(
                "Voice-intent blocked for device {DeviceId}. Categories: {Categories}",
                deviceId, string.Join(", ", inputModeration.FlaggedCategories));
            return VoiceIntentResult(VoiceIntents.Unknown);
        }

        if (yesNoQuestion)
        {
            return VoiceIntentResult(WelcomeIntentDetector.DetectYesNo(transcript) switch
            {
                YesNo.Yes => VoiceIntents.Yes,
                YesNo.No => VoiceIntents.No,
                _ => VoiceIntents.Unknown,
            });
        }

        // Deterministic keyword classification — no model call. hasActiveStorySession
        // is false by construction: the welcome flow runs before any story.
        var mode = ModeDetector.Detect(transcript, history: null, hasActiveStorySession: false);
        var intent = mode switch
        {
            DetectedMode.Story => VoiceIntents.Story,
            DetectedMode.Game => VoiceIntents.Game,
            DetectedMode.Riddle => VoiceIntents.Riddle,
            DetectedMode.Curiosity => VoiceIntents.Curiosity,
            DetectedMode.Calm => VoiceIntents.Calm,
            _ => VoiceIntents.Unknown,
        };

        // Belt and braces over the manifest's client-side filtering: even if a
        // toy asks with a stale mode list, a disabled mode is never returned.
        var childId = (await childService.GetDefaultChildForDeviceAsync(deviceId))?.Id;
        if (intent is VoiceIntents.Story or VoiceIntents.Game
            or VoiceIntents.Riddle or VoiceIntents.Curiosity
            && !await _deviceService.IsModeEnabledForRequestAsync(deviceId, childId, mode))
        {
            AppMeter.ChatGateTrip.Add(
                1, new KeyValuePair<string, object?>("gate", "mode_disabled"));
            intent = VoiceIntents.Unknown;
        }

        return VoiceIntentResult(intent);
    }

    /// <summary>The one and only shape this endpoint returns: a single
    /// bounded token, no transcript, no confidence, no free text.</summary>
    private OkObjectResult VoiceIntentResult(string intent)
    {
        AppMeter.VoiceIntentTurn.Add(
            1, new KeyValuePair<string, object?>("intent", intent));
        return Ok(new { intent });
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
        [FromServices] IContentManifestService manifest,
        [FromServices] IChildService childService)
    {
        // B3 — stamp this device's spoken-story-intro flag onto the static
        // manifest. Additive field: pre-B3 firmware ignores it; new firmware
        // caches the last-known value so the toggle applies offline. Missing
        // device row (shouldn't happen behind the auth middleware) falls back
        // to the shipped default (ON).
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var device = await _deviceService.GetDeviceAsync(deviceId);

        // Welcome-flow — the four parent mode switches, so the toy can ask
        // "what shall we do?" offering ONLY what the parent left on. Resolved
        // through IsModeEnabledForRequestAsync against the device's DEFAULT
        // child, not the raw Device columns: a child-level override must be
        // honored here too, or the toy would offer a mode and then be refused
        // by the chat gate — worse for a child than never being offered it.
        // The device has no child context, so "default child" is the honest
        // approximation; the gate itself remains the enforcement point.
        var defaultChild = await childService.GetDefaultChildForDeviceAsync(deviceId);
        var childId = defaultChild?.Id;

        return Ok(manifest.Build() with
        {
            StoryIntroEnabled = device?.StoryIntroEnabled ?? true,
            // The two story-shaping toggles ride the same manifest, with the
            // same "missing device row falls back to the shipped default (ON)"
            // rule as the intro flag above.
            StoryPausesEnabled = device?.StoryPausesEnabled ?? true,
            VariantEndingsEnabled = device?.VariantEndingsEnabled ?? true,
            // Slice E — bedtime-music opt-in rides the same manifest.
            BedtimeMusicEnabled = device?.BedtimeMusicEnabled ?? false,
            StoryEnabled = await _deviceService.IsModeEnabledForRequestAsync(
                deviceId, childId, DetectedMode.Story),
            GameEnabled = await _deviceService.IsModeEnabledForRequestAsync(
                deviceId, childId, DetectedMode.Game),
            RiddleEnabled = await _deviceService.IsModeEnabledForRequestAsync(
                deviceId, childId, DetectedMode.Riddle),
            CuriosityEnabled = await _deviceService.IsModeEnabledForRequestAsync(
                deviceId, childId, DetectedMode.Curiosity),
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
        [FromQuery] string? clip = null,
        [FromQuery] string? trackId = null,
        [FromQuery] string? voiceId = null,
        [FromQuery] string? gameKey = null,
        [FromQuery] string? clipId = null)
    {
        if (!options.Enabled)
        {
            return NotFound(new { error = "No content available." });
        }

        // Offline games — the (gameKey, clipId) PAIR selects one game clip.
        // Fourth namespace beside stories, music and voice; same
        // lookup-key-only contract, so neither half ever reaches the
        // filesystem and the pair carries no traversal surface. Half a pair
        // matches nothing, which is the same uniform 404 as an unknown one.
        if (!string.IsNullOrWhiteSpace(gameKey) || !string.IsNullOrWhiteSpace(clipId))
        {
            var gameClip = options.ResolveGames().FirstOrDefault(g =>
                string.Equals(g.GameKey, gameKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.ClipId, clipId, StringComparison.OrdinalIgnoreCase));
            if (gameClip is null || string.IsNullOrWhiteSpace(gameClip.AudioPath))
            {
                return NotFound(new { error = "No content available." });
            }
            if (!Path.IsPathRooted(gameClip.AudioPath)
                || !System.IO.File.Exists(gameClip.AudioPath))
            {
                logger.LogWarning(
                    "Content audio path missing or not absolute for game clip {GameKey}/{ClipId}: {AudioPath}",
                    gameClip.GameKey, gameClip.ClipId, gameClip.AudioPath);
                return NotFound(new { error = "No content available." });
            }
            return PhysicalFile(gameClip.AudioPath, "audio/mpeg", enableRangeProcessing: true);
        }

        // Welcome-flow — voiceId selects a device-global spoken clip
        // (greeting / menu prompt / fallback line). Third namespace beside
        // stories and music; same lookup-key-only contract, so it never
        // reaches the filesystem and carries no traversal surface.
        if (!string.IsNullOrWhiteSpace(voiceId))
        {
            var voiceClip = options.ResolveVoice().FirstOrDefault(v =>
                string.Equals(v.VoiceId, voiceId, StringComparison.OrdinalIgnoreCase));
            if (voiceClip is null || string.IsNullOrWhiteSpace(voiceClip.AudioPath))
            {
                return NotFound(new { error = "No content available." });
            }
            if (!Path.IsPathRooted(voiceClip.AudioPath)
                || !System.IO.File.Exists(voiceClip.AudioPath))
            {
                logger.LogWarning(
                    "Content audio path missing or not absolute for voice clip {VoiceId}: {AudioPath}",
                    voiceClip.VoiceId, voiceClip.AudioPath);
                return NotFound(new { error = "No content available." });
            }
            return PhysicalFile(voiceClip.AudioPath, "audio/mpeg", enableRangeProcessing: true);
        }

        // Slice E — trackId selects a MUSIC track (separate namespace from
        // stories; same lookup-key-only contract, no traversal surface).
        if (!string.IsNullOrWhiteSpace(trackId))
        {
            var track = options.ResolveMusic().FirstOrDefault(m =>
                string.Equals(m.TrackId, trackId, StringComparison.OrdinalIgnoreCase));
            if (track is null || string.IsNullOrWhiteSpace(track.AudioPath))
            {
                return NotFound(new { error = "No content available." });
            }
            if (!Path.IsPathRooted(track.AudioPath) || !System.IO.File.Exists(track.AudioPath))
            {
                logger.LogWarning(
                    "Content audio path missing or not absolute for track {TrackId}: {AudioPath}",
                    track.TrackId, track.AudioPath);
                return NotFound(new { error = "No content available." });
            }
            return PhysicalFile(track.AudioPath, "audio/mpeg", enableRangeProcessing: true);
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

using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// C1 voice chat endpoint. Audio in → Armenian transcript → existing
/// <see cref="IChatService"/> text pipeline unchanged → Armenian
/// assistant text → TTS → audio out. Both the child's audio and
/// Areg's synthesized audio are persisted to
/// <see cref="Message.AudioBlobPath"/> in the same request so the
/// future dashboard "listen" surface (C2) has both sides of the
/// turn to play back.
/// <para>
/// Voice is transport, text is canonical. The transcript becomes
/// <see cref="Message.Content"/> via the existing ChatService flow;
/// audio is an attachment that the retention / audit / export
/// contracts can catch up to in C2.
/// </para>
/// <para>
/// Story mode only for C1. The mode-enabled gate checks
/// <see cref="DetectedMode.Story"/> specifically (without running
/// STT first) — Calm / Game / Riddle / Curiosity over voice come
/// later. Other gates (paused, bedtime) reuse
/// <see cref="ChatGateEvaluator"/> shared with the text path.
/// </para>
/// </summary>
[ApiController]
[Route("api/chat/audio")]
[EnableRateLimiting(ChatRateLimiter.PolicyName)]
public class AudioChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IDeviceService _deviceService;
    private readonly IAudioTranscriptionService _transcription;
    private readonly IAudioSynthesisService _synthesis;
    private readonly IAudioBlobStore _blobStore;
    private readonly CannedVoiceClips _canned;
    private readonly AppDbContext _db;
    private readonly OpenAICostMeter _costMeter;
    private readonly IOptions<OpenAIDailyCostCapOptions> _costCapOptions;
    private readonly ILogger<AudioChatController> _logger;

    public AudioChatController(
        IChatService chatService,
        IDeviceService deviceService,
        IAudioTranscriptionService transcription,
        IAudioSynthesisService synthesis,
        IAudioBlobStore blobStore,
        CannedVoiceClips canned,
        AppDbContext db,
        OpenAICostMeter costMeter,
        IOptions<OpenAIDailyCostCapOptions> costCapOptions,
        ILogger<AudioChatController> logger)
    {
        _chatService = chatService;
        _deviceService = deviceService;
        _transcription = transcription;
        _synthesis = synthesis;
        _blobStore = blobStore;
        _canned = canned;
        _db = db;
        _costMeter = costMeter;
        _costCapOptions = costCapOptions;
        _logger = logger;
    }

    /// <summary>
    /// Voice chat turn. Request body is raw audio (WAV from the
    /// ESP32-S3 firmware in C1; other formats accepted via
    /// <c>Content-Type</c>). Response body is MP3 audio. Headers
    /// <c>X-Device-Id</c> / <c>X-Api-Key</c> already validated by
    /// <c>DeviceAuthMiddleware</c>.
    /// </summary>
    [HttpPost]
    // #060: cap the buffered audio body. The firmware sends <= ~0.5 MB of WAV;
    // 2 MB is generous headroom and stops a malicious device exhausting memory.
    [RequestSizeLimit(2_000_000)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(413)]
    [ProducesResponseType(429)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> Chat(CancellationToken cancellationToken)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;

        // Hands-free autoplay continuation. After playing a library
        // segment whose response carried `X-Areg-Continue: 1`, the
        // firmware re-POSTs with this header and NO audio body to fetch
        // the next segment — no button press, no STT, no GPT. Handled
        // before body buffering / gates / STT.
        if (string.Equals(Request.Headers["X-Areg-Continue"], "1", StringComparison.Ordinal))
        {
            return await ContinueAutoplayAsync(deviceId, cancellationToken);
        }

        var inboundContentType = Request.ContentType;
        if (string.IsNullOrWhiteSpace(inboundContentType))
            inboundContentType = "audio/wav";

        // Buffer the incoming body in memory so we can re-use it
        // for STT AND for the blob write. Typical C1 payload ≤ 500 KB
        // (15 s of 16 kHz mono PCM-WAV); MemoryStream is the cheapest
        // safe option. Enabling request-body rewind would also work
        // but buffering here keeps the controller self-contained.
        using var audioBuffer = new MemoryStream();
        await Request.Body.CopyToAsync(audioBuffer, cancellationToken);
        if (audioBuffer.Length == 0)
            return BadRequest(new { error = "Audio body is required" });
        var audioBytes = audioBuffer.ToArray();

        // Gates (pause > bedtime > mode) + the per-device daily cost cap.
        // Extracted so the autoplay-continue path runs the SAME checks and
        // cannot bypass parent policy / the cost cap (see
        // CheckGatesAndCostCapAsync). Runs BEFORE STT — zero upstream cost.
        var gated = await CheckGatesAndCostCapAsync(deviceId, cancellationToken);
        if (gated is not null)
        {
            return gated;
        }

        // Voice → text. Whisper with Language=hy is the C1 impl.
        string transcript;
        try
        {
            using var sttStream = new MemoryStream(audioBytes, writable: false);
            transcript = await _transcription.TranscribeArmenianAsync(
                sttStream, inboundContentType!, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audio chat: STT failure for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _logger.LogInformation(
                "Audio chat: empty transcript for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        // Per-mode parent-flag gate on the DETECTED mode (B2 fix). Same
        // conservative single-message detection as ChatGateEvaluator's
        // mode step on the text path (history: null, no story session):
        // a definitive Story/Game/Riddle/Curiosity call is checked against
        // the parent's per-device / per-child flags; Calm and ambiguous
        // detections pass through. This is what makes the parent's
        // Game=off switch HOLD over voice — pre-fix, only Story was ever
        // checked, so any other mode ran regardless of its flag, and a
        // Story-only off silenced every mode. STT has already been paid
        // for at this point; that is the unavoidable cost of not knowing
        // the mode until the child's words are known.
        var voiceDetectedMode = ModeDetector.Detect(
            transcript, history: null, hasActiveStorySession: false);
        if (voiceDetectedMode is DetectedMode.Story
                or DetectedMode.Game
                or DetectedMode.Riddle
                or DetectedMode.Curiosity
            && !await _deviceService.IsModeEnabledForRequestAsync(
                deviceId, childId: null, voiceDetectedMode))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "mode_disabled"));
            return await CannedResultAsync(CannedVoiceClips.ModeDisabledKey, cancellationToken);
        }

        // Text → existing ChatService pipeline, unchanged.
        ChatResponseShape chatResult;
        string ttsText;
        var autoContinue = false;
        try
        {
            var response = await _chatService.GetResponseAsync(deviceId, transcript);
            chatResult = new ChatResponseShape(
                response.Response, response.ConversationId, response.MessageId);
            autoContinue = response.LibraryAutoContinue;
            // Canonical Message.Content (already persisted by ChatService) is the
            // stripped story text. The toy has no screen — the choice handoff has
            // to be spoken. Compose a Story-only TTS-only bridge so the child
            // hears «Ի՞նչ անենք՝ առաջինը՝ X, թե՞ երկրորդը՝ Y։» after the opening.
            ttsText = AudioStoryResponseComposer.ComposeTtsText(
                response.Response, response.ChoiceA, response.ChoiceB, response.Mode);
        }
        catch (Exception ex)
        {
            // Same Path-5 sanitization shape as the text endpoint —
            // ChatService-level failures collapse to a warm 502.
            _logger.LogWarning(ex,
                "Audio chat: ChatService failure for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        // Text → voice via TTS. Streaming-capable providers (ElevenLabs)
        // take the pass-through path below — first audio byte reaches the
        // device in ~1 s instead of after full synthesis (3-5 s measured),
        // which is what makes the storyteller clone viable as the LIVE
        // voice. Buffered providers (OpenAI) keep the original path.
        if (_synthesis is IStreamingAudioSynthesisService streamer)
        {
            return await StreamTtsPassThroughAsync(
                streamer, ttsText, transcript, chatResult, audioBytes,
                inboundContentType!, autoContinue, deviceId, cancellationToken);
        }

        AudioSynthesisResult tts;
        try
        {
            tts = await _synthesis.SynthesizeArmenianAsync(
                ttsText, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audio chat: TTS failure for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        // Cost-recording is best-effort and runs after STT + chat +
        // TTS all succeeded. Wrapped in its own try/catch so a bug in
        // the estimator can never break the audio path. Three samples:
        // Whisper bytes-in, chat text-in/out, TTS chars-out.
        if (_costCapOptions.Value.Enabled)
        {
            try
            {
                var sttCost = OpenAICostEstimator.EstimateWhisperCostUsd(audioBytes.LongLength);
                var chatCost = OpenAICostEstimator.EstimateChatCostUsd(transcript, chatResult.Text);
                var ttsCost = OpenAICostEstimator.EstimateTtsCostUsd(ttsText);
                _costMeter.Record(deviceId, sttCost + chatCost + ttsCost, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OpenAI cost-cap: audio cost-record failure (suppressed). DeviceId={DeviceId}",
                    deviceId);
            }
        }

        // Persist both blobs. Non-fatal: if the disk write fails we
        // still return audio to the device; AudioBlobPath stays null
        // for that message. Text (Message.Content) is canonical and
        // already persisted by ChatService.
        await PersistAudioPathsAsync(
            conversationId: chatResult.ConversationId,
            assistantMessageId: chatResult.AssistantMessageId,
            childAudio: audioBytes,
            childContentType: inboundContentType!,
            assistantAudio: tts,
            cancellationToken);

        // Drive hands-free autoplay: when this was a library segment with
        // more of the story (or its ending) still to come, tell the
        // firmware to fetch the next segment automatically.
        if (autoContinue)
        {
            Response.Headers["X-Areg-Continue"] = "1";
        }
        return File(tts.Content, tts.MimeType);
    }

    /// <summary>
    /// Streaming TTS pass-through (the C2+ item C1 deferred): reads the
    /// provider's live audio stream and writes it to the response
    /// chunk-by-chunk while teeing into memory, so the device can start
    /// playback at first byte. After the stream completes, both blobs are
    /// persisted exactly as on the buffered path. A failure BEFORE the
    /// first byte still returns the sanitized 502; a failure MID-STREAM
    /// cannot change the status line (headers already sent) — the
    /// truncated MP3 fails the device's decoder, which shows the existing
    /// canned failure, and no truncated blob is persisted.
    /// </summary>
    private async Task<IActionResult> StreamTtsPassThroughAsync(
        IStreamingAudioSynthesisService streamer,
        string ttsText,
        string transcript,
        ChatResponseShape chatResult,
        byte[] childAudioBytes,
        string childContentType,
        bool autoContinue,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        AudioSynthesisStreamResult stream;
        try
        {
            stream = await streamer.SynthesizeArmenianStreamAsync(ttsText, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audio chat: TTS stream start failure for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        // Cost recording — same best-effort contract as the buffered path
        // (runs before the response body so an estimator bug can never
        // corrupt a stream in flight).
        if (_costCapOptions.Value.Enabled)
        {
            try
            {
                var sttCost = OpenAICostEstimator.EstimateWhisperCostUsd(childAudioBytes.LongLength);
                var chatCost = OpenAICostEstimator.EstimateChatCostUsd(transcript, chatResult.Text);
                var ttsCost = OpenAICostEstimator.EstimateTtsCostUsd(ttsText);
                _costMeter.Record(deviceId, sttCost + chatCost + ttsCost, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OpenAI cost-cap: audio cost-record failure (suppressed). DeviceId={DeviceId}",
                    deviceId);
            }
        }

        using (stream)
        {
            if (autoContinue)
            {
                Response.Headers["X-Areg-Continue"] = "1";
            }
            Response.ContentType = stream.MimeType;
            Response.StatusCode = 200;

            var tee = new MemoryStream();
            var buffer = new byte[8192];
            try
            {
                int read;
                while ((read = await stream.AudioStream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    tee.Write(buffer, 0, read);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Audio chat: TTS stream aborted mid-flight for Device {DeviceId}", deviceId);
                return new EmptyResult(); // truncated — do not persist
            }

            await PersistAudioPathsAsync(
                conversationId: chatResult.ConversationId,
                assistantMessageId: chatResult.AssistantMessageId,
                childAudio: childAudioBytes,
                childContentType: childContentType,
                assistantAudio: new AudioSynthesisResult(tee.ToArray(), stream.MimeType),
                cancellationToken);

            return new EmptyResult();
        }
    }

    /// <summary>
    /// Autoplay continuation turn: no audio, no STT. Advances the
    /// device's active library story by one segment (or delivers the
    /// ending) and streams that MP3. Returns 204 No Content when there
    /// is nothing left to play, which stops the firmware's autoplay
    /// loop. Emits `X-Areg-Continue: 1` while more remains.
    /// </summary>
    private async Task<IActionResult> ContinueAutoplayAsync(
        Guid deviceId, CancellationToken cancellationToken)
    {
        // Autoplay-continue is paid TTS too — it MUST honor the same gates +
        // cost cap as a normal turn, or a client could re-POST X-Areg-Continue
        // on a paused / bedtime / over-cap device for unmetered speech.
        // A library segment IS story content, so Story is the mode to gate.
        var gated = await CheckGatesAndCostCapAsync(
            deviceId, cancellationToken, modeToGate: DetectedMode.Story);
        if (gated is not null)
        {
            return gated;
        }

        ChatResponse result;
        try
        {
            result = await _chatService.ContinueLibraryStoryAsync(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audio autoplay-continue: ChatService failure for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        // Nothing live to continue → 204 stops the device loop.
        if (string.IsNullOrEmpty(result.Response))
        {
            return NoContent();
        }

        AudioSynthesisResult tts;
        try
        {
            tts = await _synthesis.SynthesizeArmenianAsync(result.Response, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audio autoplay-continue: TTS failure for Device {DeviceId}", deviceId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        if (result.LibraryAutoContinue)
        {
            Response.Headers["X-Areg-Continue"] = "1";
        }
        return File(tts.Content, tts.MimeType);
    }

    /// <summary>Pause &gt; bedtime &gt; mode gates + the per-device daily cost
    /// cap, shared by the normal chat turn AND the autoplay-continue path so
    /// neither can bypass parent policy or rack up unmetered TTS. Returns a
    /// canned-clip result to short-circuit, or null to proceed. Zero upstream
    /// cost.</summary>
    private async Task<IActionResult?> CheckGatesAndCostCapAsync(
        Guid deviceId, CancellationToken cancellationToken,
        DetectedMode? modeToGate = null)
    {
        // Runs FIRST, ahead of pause: a toy with no linked parent was
        // unlinked and is waiting to be paired again from its QR, so nobody
        // could see or stop what it says. Same canned resting clip as pause.
        if (!await _deviceService.HasLinkedParentAsync(deviceId))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "unclaimed"));
            return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
        }
        if (await _deviceService.IsDevicePausedAsync(deviceId))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "paused"));
            return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
        }
        if (await _deviceService.IsDeviceInBedtimeWindowAsync(deviceId, DateTime.UtcNow))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "bedtime"));
            return await CannedResultAsync(CannedVoiceClips.BedtimeKey, cancellationToken);
        }
        // Per-mode parent-flag gate (B2 fix). The old code hardcoded a
        // Story-only check here — which BYPASSED the parent's Game /
        // Riddle / Curiosity switches on voice (any utterance ran as long
        // as Story was on) and BLOCKED every mode when Story alone was
        // off. The chat path now passes null here (no transcript exists
        // yet at this pre-STT point) and gates the DETECTED mode after
        // STT instead; the autoplay-continue path passes Story explicitly
        // (a library segment IS story content, and no STT ever runs
        // there).
        if (modeToGate is { } gateMode
            && !await _deviceService.IsModeEnabledForRequestAsync(deviceId, childId: null, gateMode))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "mode_disabled"));
            return await CannedResultAsync(CannedVoiceClips.ModeDisabledKey, cancellationToken);
        }

        var costCapOpts = _costCapOptions.Value;
        if (costCapOpts.Enabled)
        {
            var nowUtc = DateTime.UtcNow;
            // #022 — fleet-wide ceiling (kill-switch), opt-in (skipped when
            // Global <= 0). Same canned soft-off as the per-device cap.
            if (costCapOpts.Global > 0m && _costMeter.IsGlobalOverCap(costCapOpts.Global, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "audio"));
                if (_costMeter.ShouldLogGlobalCapTrip(nowUtc))
                {
                    _logger.LogWarning(
                        "OpenAI GLOBAL daily cost ceiling reached (fleet kill-switch). CurrentEstimatedUsd={Current:F4} GlobalCapUsd={Cap:F4} UtcDate={Date:yyyy-MM-dd}",
                        _costMeter.GetGlobalTotal(nowUtc), costCapOpts.Global, nowUtc.Date);
                }
                return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
            }
            var cap = costCapOpts.CapForDevice(deviceId);
            if (_costMeter.IsOverCap(deviceId, cap, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "audio"));
                if (_costMeter.ShouldLogCapTrip(deviceId, nowUtc))
                {
                    _logger.LogWarning(
                        "OpenAI daily cost cap reached. DeviceId={DeviceId} Kind={Kind} CurrentEstimatedUsd={Current:F4} CapUsd={Cap:F4} UtcDate={Date:yyyy-MM-dd}",
                        deviceId, "audio",
                        _costMeter.GetCurrentTotal(deviceId, nowUtc), cap, nowUtc.Date);
                }
                return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
            }
        }
        return null;
    }

    private async Task<IActionResult> CannedResultAsync(
        string key, CancellationToken cancellationToken)
    {
        try
        {
            var clip = await _canned.GetAsync(key, cancellationToken);
            return File(clip.Content, clip.MimeType);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Canned clip TTS render itself failed (first-hit race
            // with an outage). Don't leak a provider error to the
            // device — return 502 with the generic sanitized body;
            // firmware already owns its own device-side
            // "can't reach you" clip for this case.
            _logger.LogWarning(ex,
                "Audio chat: canned clip render failure for key={Key}", key);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
    }

    private async Task PersistAudioPathsAsync(
        Guid conversationId,
        Guid assistantMessageId,
        byte[] childAudio,
        string childContentType,
        AudioSynthesisResult assistantAudio,
        CancellationToken cancellationToken)
    {
        // Find the user message ChatService just inserted. It is
        // always the most-recent user-role row in the conversation;
        // ChatService writes it synchronously before the LLM call,
        // so ordering by Timestamp DESC yields our row. This avoids
        // touching IChatService's return shape.
        var userMessage = await _db.Set<Message>()
            .Where(m => m.ConversationId == conversationId
                     && m.Role == MessageRole.User)
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        string? childBlobPath = null;
        string? assistantBlobPath = null;
        try
        {
            if (userMessage is not null)
            {
                childBlobPath = await _blobStore.WriteAsync(
                    conversationId, userMessage.Id,
                    childAudio, childContentType, cancellationToken);
            }
            assistantBlobPath = await _blobStore.WriteAsync(
                conversationId, assistantMessageId,
                assistantAudio.Content, assistantAudio.MimeType, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audio chat: blob write failure for Conversation {ConversationId}; continuing without AudioBlobPath updates",
                conversationId);
            return;
        }

        var needSave = false;
        if (userMessage is not null && childBlobPath is not null)
        {
            userMessage.AudioBlobPath = childBlobPath;
            needSave = true;
        }
        if (assistantBlobPath is not null)
        {
            var assistantMessage = await _db.Set<Message>()
                .FirstOrDefaultAsync(m => m.Id == assistantMessageId, cancellationToken);
            if (assistantMessage is not null)
            {
                assistantMessage.AudioBlobPath = assistantBlobPath;
                needSave = true;
            }
        }
        if (needSave)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record ChatResponseShape(
        string Text, Guid ConversationId, Guid AssistantMessageId);
}

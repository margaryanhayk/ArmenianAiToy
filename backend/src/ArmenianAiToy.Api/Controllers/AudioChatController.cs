using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
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
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(429)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> Chat(CancellationToken cancellationToken)
    {
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
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

        // Paused / bedtime short-circuit BEFORE STT — runs zero cost
        // against the upstream providers. Mode-disabled short-circuit
        // is a Story-specific check (C1 voice is Story-only) so we
        // can run it without transcription too.
        if (await _deviceService.IsDevicePausedAsync(deviceId))
        {
            AppMeter.ChatGateTrip.Add(1,
                new KeyValuePair<string, object?>("gate", "paused"));
            return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
        }
        if (await _deviceService.IsDeviceInBedtimeWindowAsync(deviceId, DateTime.UtcNow))
        {
            AppMeter.ChatGateTrip.Add(1,
                new KeyValuePair<string, object?>("gate", "bedtime"));
            return await CannedResultAsync(CannedVoiceClips.BedtimeKey, cancellationToken);
        }
        // C1 voice is Story-only. Story-disabled on the device / child
        // → canned "let's try something else" without transcription.
        // Other modes over voice land in a later phase.
        var storyEnabled = await _deviceService.IsModeEnabledForRequestAsync(
            deviceId, childId: null, DetectedMode.Story);
        if (!storyEnabled)
        {
            AppMeter.ChatGateTrip.Add(1,
                new KeyValuePair<string, object?>("gate", "mode_disabled"));
            return await CannedResultAsync(
                CannedVoiceClips.ModeDisabledKey, cancellationToken);
        }

        // P0 daily cost cap. Fires BEFORE Whisper STT so a runaway
        // client cannot rack up further transcription / chat / TTS
        // cost in the same UTC day. Reuses the paused canned clip
        // because the v1 child-facing message ("take a small break")
        // is shape-compatible with the paused envelope; a future
        // slice can add a distinct cost-cap voice clip if operators
        // want to distinguish the two on the device side.
        var costCapOpts = _costCapOptions.Value;
        if (costCapOpts.Enabled)
        {
            var nowUtc = DateTime.UtcNow;
            var cap = costCapOpts.CapForDevice(deviceId);
            if (_costMeter.IsOverCap(deviceId, cap, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1,
                    new KeyValuePair<string, object?>("kind", "audio"));
                if (_costMeter.ShouldLogCapTrip(deviceId, nowUtc))
                {
                    _logger.LogWarning(
                        "OpenAI daily cost cap reached. DeviceId={DeviceId} Kind={Kind} CurrentEstimatedUsd={Current:F4} CapUsd={Cap:F4} UtcDate={Date:yyyy-MM-dd}",
                        deviceId, "audio",
                        _costMeter.GetCurrentTotal(deviceId, nowUtc), cap, nowUtc.Date);
                }
                return await CannedResultAsync(
                    CannedVoiceClips.PausedKey, cancellationToken);
            }
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

        // Text → existing ChatService pipeline, unchanged.
        ChatResponseShape chatResult;
        string ttsText;
        try
        {
            var response = await _chatService.GetResponseAsync(deviceId, transcript);
            chatResult = new ChatResponseShape(
                response.Response, response.ConversationId, response.MessageId);
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

        // Text → voice via TTS.
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
        if (costCapOpts.Enabled)
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

        return File(tts.Content, tts.MimeType);
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

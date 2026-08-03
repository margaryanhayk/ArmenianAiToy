using System.Diagnostics;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// In-story question handler for the streaming narration flow.
///
/// When the child barges in during continuous story playback and asks
/// something, the firmware records the question and POSTs the audio
/// here with the story id and the byte offset it paused at. We:
/// transcribe (Whisper) → answer it with the bounded
/// <see cref="LibraryStoryQuestionService"/> (prompt → GPT → validate →
/// repair-once → canned fallback — answers ONLY from the story) → speak
/// the answer (TTS) → return the MP3. The firmware plays it, then
/// auto-resumes the story from the saved offset.
///
/// Route is under <c>/api/chat</c> so <c>DeviceAuthMiddleware</c>
/// authenticates it (the firmware sends X-Device-Id / X-Api-Key on this
/// POST, unlike the header-less story-audio stream).
/// </summary>
[ApiController]
[Route("api/chat/story-qa")]
[EnableRateLimiting(ChatRateLimiter.PolicyName)]
public class StoryQaController : ControllerBase
{
    // Warm return-to-story lines spoken after the answer (and the pause)
    // so the narration doesn't resume abruptly. ROTATED across requests so
    // the child doesn't hear the identical phrase every time — a fixed
    // re-entry string "quickly becomes monotonous and robotic" (Google
    // Conversation Design). All are "return" markers (resume the story),
    // never "introduction" openers like «Մի անգամ…», which would signal the
    // previous story was abandoned rather than resumed. One calm statement,
    // never a question (the story resumes right after).
    // All five validated by armenian-story-master (2026-06-16) as natural
    // resume markers (not openers) for ages 4-7. Line 2 has no comma — a
    // calmer TTS cadence per that review.
    private static readonly string[] ReturnToStoryBridges =
    [
        "Իսկ հիմա վերադառնանք մեր հեքիաթին։",
        "Ուրեմն շարունակենք մեր հեքիաթը։",
        "Իսկ հիմա տեսնենք՝ ինչ եղավ հետո։",
        "Հիմա նորից մտնենք մեր հեքիաթի մեջ։",
        "Իսկ հիմա վերադառնանք այնտեղ, որտեղ կանգ առանք։",
    ];

    // Warm post-story acknowledgement lines, spoken after the child answers
    // Areg's reflection question (then a calm pause, then the fixed close
    // below). ROTATED so a child doesn't hear the identical line each story.
    // These AFFIRM ENGAGEMENT, never correctness — we never tell a 4-7 year
    // old their answer was right or wrong. Produced DETERMINISTICALLY (never by
    // GPT): a model paraphrase would drift into grading («ճիշտ պատասխանեցիր»),
    // breaking that rule. All five + the close validated by
    // armenian-story-master (2026-06-20).
    private static readonly string[] ReflectionAcknowledgements =
    [
        "Ապրե՛ս, շատ լավ մտածեցիր։",
        "Շնորհակալ եմ, որ պատասխանեցիր։ Շատ լավ էր։",
        "Ի՜նչ ուշադիր ես լսել հեքիաթը։ Ապրե՛ս։",
        "Շնորհակալ եմ քո պատասխանի համար։ Ուրախ եմ, որ միասին լսեցինք։",
        "Քո խոսքերը շատ հաճելի էին։ Ապրե՛ս, փոքրի՛կս։",
    ];

    // Fixed gentle close, spoken after the (rotated) acknowledgement with a
    // calm pause between. Also spoken ALONE when the child's answer is blocked
    // or empty — a warm goodbye that never affirms flagged/absent content.
    private const string ReflectionClose = "Հիմա հանգստացի՛ր, փոքրիկ ընկեր։";

    private readonly IAudioTranscriptionService _transcription;
    private readonly IAudioSynthesisService _synthesis;
    private readonly ICuratedStoryLibrary _library;
    private readonly LibraryStoryQuestionService _questions;
    private readonly IModerationService _moderation;
    private readonly IConversationService _conversations;
    private readonly IChildService _childService;
    private readonly IDeviceService _deviceService;
    private readonly CannedVoiceClips _canned;
    private readonly OpenAICostMeter _costMeter;
    private readonly IOptions<OpenAIDailyCostCapOptions> _costCapOptions;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<StoryQaController> _logger;

    // Reflection-dialogue slice: optional (defaults null) so the many
    // pre-existing controller-constructing tests compile unchanged; a null
    // service simply keeps the deterministic-acknowledgement behavior.
    private readonly ReflectionDialogueService? _reflectionDialogue;

    public StoryQaController(
        IAudioTranscriptionService transcription,
        IAudioSynthesisService synthesis,
        ICuratedStoryLibrary library,
        LibraryStoryQuestionService questions,
        IModerationService moderation,
        IConversationService conversations,
        IChildService childService,
        IDeviceService deviceService,
        CannedVoiceClips canned,
        OpenAICostMeter costMeter,
        IOptions<OpenAIDailyCostCapOptions> costCapOptions,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<StoryQaController> logger,
        ReflectionDialogueService? reflectionDialogue = null)
    {
        _transcription = transcription;
        _synthesis = synthesis;
        _library = library;
        _questions = questions;
        _moderation = moderation;
        _conversations = conversations;
        _childService = childService;
        _deviceService = deviceService;
        _canned = canned;
        _costMeter = costMeter;
        _costCapOptions = costCapOptions;
        _env = env;
        _config = config;
        _logger = logger;
        _reflectionDialogue = reflectionDialogue;
    }

    [HttpPost]
    // #060: cap the buffered audio body (firmware sends <= ~0.5 MB WAV).
    [RequestSizeLimit(2_000_000)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(413)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> Ask(
        [FromQuery] string storyId,
        [FromQuery] long offset = 0,
        CancellationToken cancellationToken = default)
    {
        var story = _library.GetById(storyId);
        if (story is null)
        {
            return NotFound(new { error = "Unknown story." });
        }

        // Route is under /api/chat, so DeviceAuthMiddleware has already
        // validated and stamped the device id — used to attach the
        // persisted Q&A turn to this device's conversation.
        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;

        // Resolve the device's child the SAME way the text / C1-audio paths do
        // (GetResponseAsync), so the per-child mode override applies to voice
        // Q&A and the persisted turn is child-stamped instead of orphaned to
        // childId=null. Null when the device has no child (then device-level
        // flags decide, exactly as before).
        var child = await _childService.GetDefaultChildForDeviceAsync(deviceId);
        var childId = child?.Id;

        // Gate order pause > bedtime > mode — mirrors the C1 audio + text
        // paths. A paused / bedtime-windowed / Story-disabled device must NOT
        // incur paid STT+GPT+TTS, and parent policy ("paused means paused")
        // must hold here too. Runs before the body is read, so a gated
        // request costs nothing upstream and returns the same canned clip the
        // C1 voice path uses.
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
        if (!await _deviceService.IsModeEnabledForRequestAsync(deviceId, childId, DetectedMode.Story))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "mode_disabled"));
            return await CannedResultAsync(CannedVoiceClips.ModeDisabledKey, cancellationToken);
        }

        // Per-device daily OpenAI cost cap — story-qa is the costliest path
        // (STT + GPT + TTS), so it MUST be capped exactly like /api/chat/audio.
        // Fires before STT so a runaway client cannot keep spending.
        var costCapOpts = _costCapOptions.Value;
        if (costCapOpts.Enabled)
        {
            var nowUtc = DateTime.UtcNow;
            // #022 — fleet-wide ceiling (kill-switch), opt-in (skipped when Global <= 0).
            if (costCapOpts.Global > 0m && _costMeter.IsGlobalOverCap(costCapOpts.Global, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "story_qa"));
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
                AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "story_qa"));
                if (_costMeter.ShouldLogCapTrip(deviceId, nowUtc))
                {
                    _logger.LogWarning(
                        "OpenAI daily cost cap reached. DeviceId={DeviceId} Kind=story_qa CurrentEstimatedUsd={Current:F4} CapUsd={Cap:F4} UtcDate={Date:yyyy-MM-dd}",
                        deviceId, _costMeter.GetCurrentTotal(deviceId, nowUtc), cap, nowUtc.Date);
                }
                return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
            }
        }

        // Where the child was when they barged in — used both to ground the
        // Q&A prompt and to pick that segment's re-anchor (recap) line.
        var segmentIndex = OffsetToSegment(storyId, offset, story.Segments.Count);

        var inboundContentType = Request.ContentType;
        if (string.IsNullOrWhiteSpace(inboundContentType))
            inboundContentType = "audio/wav";

        using var audioBuffer = new MemoryStream();
        await Request.Body.CopyToAsync(audioBuffer, cancellationToken);
        // Diagnostic: record the inbound upload size so an empty/short body
        // from the device is unambiguous in the logs (no child PII — size only).
        _logger.LogInformation(
            "Story-QA inbound body: {Bytes} bytes, contentType={ContentType}, storyId={StoryId}, offset={Offset}",
            audioBuffer.Length, inboundContentType, storyId, offset);
        if (audioBuffer.Length == 0)
        {
            _logger.LogWarning(
                "Story-QA EMPTY body — returning 400. The device uploaded no audio. StoryId={StoryId}", storyId);
            return BadRequest(new { error = "Audio body is required" });
        }
        var audioBytes = audioBuffer.ToArray();

        // Voice-path observability: time the whole turn (this is the "dead
        // air" the child perceives after a barge-in) and record the outcome.
        // The 404/400 input-validation rejections above are deliberately NOT
        // metered — only real turns that reach transcription. RecordTurn is
        // called exactly once on every return path below.
        var turnStopwatch = Stopwatch.StartNew();
        // Per-stage latency instrumentation. Each is the wall-clock of one
        // sequential OpenAI round-trip; 0 means that stage didn't run this turn.
        // Surfaced as X-Qa-*-Ms response headers + one log line so the bottleneck
        // is measurable without guessing. Additive — no behavior change.
        long sttMs = 0, inModMs = 0, gptMs = 0, outModMs = 0, ttsMs = 0;
        var stage = new Stopwatch();
        void RecordTurn(string outcome)
        {
            turnStopwatch.Stop();
            AppMeter.StoryQaDuration.Record(turnStopwatch.Elapsed.TotalSeconds);
            AppMeter.StoryQaTurn.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            // Dev/bench only: expose the per-stage timing as headers for latency
            // measurement. Off in prod (the structured log line below is the
            // permanent record) so internal timing isn't leaked on every reply.
            if (_env.IsDevelopment() && !Response.HasStarted)
            {
                Response.Headers["X-Qa-Stt-Ms"] = sttMs.ToString();
                Response.Headers["X-Qa-InMod-Ms"] = inModMs.ToString();
                Response.Headers["X-Qa-Gpt-Ms"] = gptMs.ToString();
                Response.Headers["X-Qa-OutMod-Ms"] = outModMs.ToString();
                Response.Headers["X-Qa-Tts-Ms"] = ttsMs.ToString();
                Response.Headers["X-Qa-Total-Ms"] = turnStopwatch.ElapsedMilliseconds.ToString();
            }
            _logger.LogInformation(
                "Story-QA timing ms: stt={Stt} inMod={InMod} gpt={Gpt} outMod={OutMod} tts={Tts} total={Total} outcome={Outcome}",
                sttMs, inModMs, gptMs, outModMs, ttsMs, turnStopwatch.ElapsedMilliseconds, outcome);
        }

        // Voice → text (Whisper, Armenian). Bias decoding with the current
        // story scene so short / single-word answers — the model's weakest
        // case — resolve to the proper nouns and vocabulary actually in play.
        var transcriptionBias = BuildTranscriptionBias(story, segmentIndex);
        // OPTIONAL Q&A-specific STT model (latency seam). Null/empty → the
        // transcription service's configured default (whisper-1) is used, so
        // behavior is unchanged out of the box. An operator can later flip
        // StoryQa:TranscriptionModel to a faster model (e.g.
        // gpt-4o-mini-transcribe) AFTER an Armenian transcription-quality
        // benchmark, without touching the C1 voice path's model.
        var qaTranscriptionModel = _config["StoryQa:TranscriptionModel"];
        string question;
        try
        {
            using var sttStream = new MemoryStream(audioBytes, writable: false);
            stage.Restart();
            question = await _transcription.TranscribeArmenianAsync(
                sttStream, inboundContentType!, transcriptionBias, cancellationToken,
                model: string.IsNullOrWhiteSpace(qaTranscriptionModel) ? null : qaTranscriptionModel);
            sttMs = stage.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story-QA: STT failure for story {StoryId}", storyId);
            RecordTurn("transient_failure");
            return await SpokenFailureResultAsync(cancellationToken);
        }

        // Bounded Q&A. Empty transcript → the canned fallback line is
        // spoken (no GPT call); the child still hears a warm response.
        // Safety flags persisted for the turn mirror the text path: a
        // moderation-blocked input is stored as Blocked (child row) +
        // Flagged (assistant fallback row) so the parent dashboard can
        // distinguish it; everything else is Clean.
        string answerText;
        var userFlag = SafetyFlag.Clean;
        var assistantFlag = SafetyFlag.Clean;
        var turnOutcome = "answered";
        // Only a model-authored answer needs OUTPUT moderation below; every
        // canned/pre-reviewed line (empty / input-blocked / garbled / filter
        // fallback) is already safe and skips that extra classifier call.
        var answerIsModelAuthored = false;
        if (string.IsNullOrWhiteSpace(question))
        {
            answerText = StoryAnswerFilter.SafeFallback;
            turnOutcome = "empty_transcript";
            _logger.LogInformation("Story-QA: empty transcript for {StoryId}; speaking fallback", storyId);
        }
        else
        {
            // Input moderation on the transcribed question BEFORE any GPT
            // call — mirrors the text path (ChatService step 2). An unsafe
            // transcript is never sent to the answer model; the child hears
            // the warm in-story fallback and the story resumes normally.
            // CheckContentAsync is fail-closed-to-(IsSafe=false) by contract
            // and never throws, so it needs no try/catch (same as the text
            // path). The "moderation_unavailable" distinction is logged so an
            // infra hiccup is separable from a genuine content flag, but the
            // child hears the same in-story fallback either way.
            stage.Restart();
            var inputModeration = await _moderation.CheckContentAsync(question);
            inModMs = stage.ElapsedMilliseconds;
            if (!inputModeration.IsSafe)
            {
                var moderationUnavailable =
                    inputModeration.FlaggedCategories.Contains("moderation_unavailable");
                _logger.LogWarning(
                    "Story-QA input blocked. StoryId: {StoryId}, Segment: {Segment}, Categories: {Categories}, unavailable={Unavailable}",
                    storyId, segmentIndex,
                    string.Join(", ", inputModeration.FlaggedCategories), moderationUnavailable);
                answerText = StoryAnswerFilter.SafeFallback;
                userFlag = SafetyFlag.Blocked;
                assistantFlag = SafetyFlag.Flagged;
                turnOutcome = "moderation_blocked";
            }
            else if (ArmenianVoiceReplyGuard.IsGarbledInput(question))
            {
                // Likely STT mistranscription (vowel-less clusters / repeated
                // letters). Feeding noise to GPT yields a confidently-WRONG
                // answer, so instead ask the child to repeat — the same
                // reviewed line the text path uses. Clean flag: we are not
                // blocking the child, just didn't catch them. The firmware
                // auto-resumes the story and the child can barge in to re-ask.
                answerText = ArmenianVoiceReplyGuard.ClarificationResponse;
                turnOutcome = "unclear";
                _logger.LogInformation(
                    "Story-QA: garbled transcript for {StoryId}; asking the child to repeat", storyId);
            }
            else
            {
                try
                {
                    stage.Restart();
                    var answer = await _questions.AnswerAsync(story, segmentIndex, question);
                    gptMs = stage.ElapsedMilliseconds;
                    answerText = answer.Text;
                    answerIsModelAuthored = !answer.UsedFallback;
                    turnOutcome = answer.UsedFallback ? "answer_fallback" : "answered";
                    // Privacy (#005): never log the child's transcribed words.
                    _logger.LogInformation(
                        "Story-QA answered. Story: {StoryId}, Segment: {Segment}, UsedFallback: {Fallback}, QLen: {QLen}",
                        storyId, segmentIndex, answer.UsedFallback, question.Length);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Story-QA: answer failure for {StoryId}", storyId);
                    RecordTurn("transient_failure");
                    return await SpokenFailureResultAsync(cancellationToken);
                }
            }
        }

        // OUTPUT moderation on the spoken answer — mirrors the text path's
        // output-moderation step. StoryAnswerFilter validates story-fidelity
        // and format ONLY; it is NOT a safety classifier, and the bounded
        // Q&A's word-meaning branch can produce model-authored real-world
        // text. So the FINAL answer must clear the safety classifier before
        // it is spoken — fail-closed to the canned safe fallback, exactly as
        // ChatService does. Skipped when the answer is already the canned
        // fallback (empty / input-blocked / filter-fallback paths): that text
        // is pre-reviewed and re-checking it only adds latency.
        // Latency: overlap the answer-TTS with OUTPUT moderation. Start
        // synthesizing the model answer speculatively and run the safety
        // classifier concurrently — two OpenAI round-trips collapse into one
        // wall-clock wait. Safety invariant is intact: if moderation BLOCKS,
        // the speculative audio is discarded and the canned fallback is spoken
        // instead, so unmoderated content is never returned to the child.
        Task<AudioSynthesisResult>? speculativeTts = null;
        if (answerIsModelAuthored)
        {
            try
            {
                speculativeTts = _synthesis.SynthesizeArmenianAsync(answerText.TrimEnd(), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // A synchronous failure kicking off the speculative synthesis is
                // not fatal: leave it null and let the guarded TTS step below
                // synthesize (and sanitize any failure) normally.
                _logger.LogWarning(ex, "Story-QA: speculative answer-TTS kickoff failed; retried in TTS step");
                speculativeTts = null;
            }
            stage.Restart();
            var outputModeration = await _moderation.CheckContentAsync(answerText);
            outModMs = stage.ElapsedMilliseconds;
            if (!outputModeration.IsSafe)
            {
                _logger.LogWarning(
                    "Story-QA OUTPUT blocked. StoryId: {StoryId}, Segment: {Segment}, Categories: {Categories}",
                    storyId, segmentIndex, string.Join(", ", outputModeration.FlaggedCategories));
                // Never speak unmoderated content — drop the speculative audio
                // (observe its exception so a faulted discard can't go unhandled).
                _ = speculativeTts?.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                speculativeTts = null;
                answerText = StoryAnswerFilter.SafeFallback;
                assistantFlag = SafetyFlag.Flagged;
                turnOutcome = "answer_blocked";
            }
        }

        // Privacy (#005): do NOT log the child's transcribed words or the spoken
        // answer to stdout — that is child PII with no retention owner / outside
        // the delete cascade. The full turn is already persisted as Message rows
        // (PersistTurnAsync) for consented, retention-governed QA review; log
        // only non-PII metadata here.
        _logger.LogInformation(
            "Story-QA pair. StoryId: {StoryId} | Segment: {Segment} | Outcome: {Outcome} | QLen: {QLen} | ALen: {ALen}",
            storyId, segmentIndex, turnOutcome,
            string.IsNullOrWhiteSpace(question) ? 0 : question.Length, answerText.Length);

        // Persist the turn so it appears in the parent dashboard and is
        // covered by retention/delete — the streaming story path is
        // otherwise stateless, leaving the child's voice unrecorded. Text
        // is canonical, mirroring the chat paths. An empty transcript
        // carries no child words, so nothing is persisted for it.
        if (!string.IsNullOrWhiteSpace(question))
        {
            await PersistTurnAsync(
                deviceId, childId, question, answerText, userFlag, assistantFlag, cancellationToken);
        }

        // Text → voice. Render the ANSWER on its own, then a calm pause,
        // then the (cached) return-to-story bridge — so the child has a
        // beat to take in the answer before the narration resumes, instead
        // of the answer and "let's go back" snapping together.
        AudioSynthesisResult answerTts;
        try
        {
            stage.Restart();
            // Use the speculative synthesis started during output moderation
            // when the answer passed; otherwise synthesize the final (fallback
            // or canned) text now.
            answerTts = speculativeTts is not null
                ? await speculativeTts
                : await _synthesis.SynthesizeArmenianAsync(answerText.TrimEnd(), cancellationToken);
            ttsMs = stage.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story-QA: TTS failure for {StoryId}", storyId);
            RecordTurn("transient_failure");
            return await SpokenFailureResultAsync(cancellationToken);
        }

        // Best-effort per-device cost recording (STT + GPT + answer TTS).
        // Wrapped so an estimator bug can never break the turn; bridge/recap
        // are cached after first render, so the answer TTS dominates here.
        if (costCapOpts.Enabled)
        {
            try
            {
                var sttCost = OpenAICostEstimator.EstimateWhisperCostUsd(audioBytes.LongLength);
                var chatCost = OpenAICostEstimator.EstimateChatCostUsd(question, answerText);
                var ttsCost = OpenAICostEstimator.EstimateTtsCostUsd(answerText);
                _costMeter.Record(deviceId, sttCost + chatCost + ttsCost, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Story-QA cost-record failure (suppressed). DeviceId={DeviceId}", deviceId);
            }
        }

        // The answer is fully GENERATED, VALIDATED (moderation +
        // StoryAnswerFilter/repair/fallback above), persisted, and SYNTHESIZED.
        // Compose answer + calm pause + return-to-story bridge (+ recap) into
        // ONE buffered MP3 and return it with a Content-Length.
        //
        // NOTE: a streamed/chunked response was REVERTED here. The firmware
        // sizes its receive buffer from HTTPClient.getSize() (Content-Length);
        // a chunked body reports -1, so the device dropped the answer and
        // played nothing. The device buffers the whole body before decoding
        // anyway, so streaming bought no on-device latency — only this bug.
        RecordTurn(turnOutcome);

        byte[] bridgeAudio;
        try
        {
            var bridgeIndex = (int)((uint)Interlocked.Increment(ref _bridgeRotation) % ReturnToStoryBridges.Length);
            bridgeAudio = await GetBridgeAudioAsync(bridgeIndex, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Bridge render failed: still give the child the answer (with a
            // Content-Length) rather than nothing.
            _logger.LogWarning(ex, "Story-QA: bridge TTS failure for {StoryId}; returning answer only", storyId);
            return File(answerTts.Content, answerTts.MimeType);
        }

        byte[]? recapAudio = null;
        if (ShouldIncludeRecap(answerText))
        {
            var recapText = LibraryStoryQuestionService.GetSegmentRecap(storyId, segmentIndex);
            if (!string.IsNullOrWhiteSpace(recapText))
            {
                try
                {
                    recapAudio = await GetRecapAudioAsync(storyId, segmentIndex, recapText!, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Story-QA: recap TTS failed for {StoryId} seg {Segment}; skipping recap", storyId, segmentIndex);
                }
            }
        }

        var spoken = ComposeAnswerWithPause(answerTts.Content, bridgeAudio, recapAudio);
        return File(spoken, answerTts.MimeType);
    }

    /// <summary>
    /// Post-story reflection answer. After the narration + conclusion + the
    /// reflection question have played (the conclusion + question ship on the
    /// offline SD pack — see ContentPackBuilder), the child speaks their answer
    /// and the firmware POSTs that audio here. Areg replies with a warm,
    /// rotated acknowledgement + a gentle close — affirming that the child
    /// engaged, NEVER grading the answer right or wrong.
    ///
    /// Connectivity is required only for STT (transcribing the child) and the
    /// safety record; the spoken reply is a pre-approved DETERMINISTIC line
    /// (no GPT), so there is no answer-model surface here at all — and thus no
    /// output-moderation step. Same gate chain, cost-cap, input moderation,
    /// persistence, and buffered (Content-Length) response shape as
    /// <see cref="Ask"/>.
    /// </summary>
    [HttpPost("reflection-answer")]
    // #060: cap the buffered audio body (firmware sends <= ~0.5 MB WAV).
    [RequestSizeLimit(2_000_000)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(413)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> AnswerReflection(
        [FromQuery] string storyId,
        [FromQuery] int questionIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var story = _library.GetById(storyId);
        if (story is null
            || questionIndex < 0
            || questionIndex >= story.ReflectionQuestions.Count)
        {
            // Uniform 404 — no existence leak across unknown story vs bad index.
            return NotFound(new { error = "Unknown story or question." });
        }

        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;
        var child = await _childService.GetDefaultChildForDeviceAsync(deviceId);
        var childId = child?.Id;

        // Gate chain pause > bedtime > Story-mode, identical to Ask(): a paused
        // / bedtime / Story-disabled device must not incur paid STT and gets
        // the same canned clip the rest of the voice path returns.
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
        if (!await _deviceService.IsModeEnabledForRequestAsync(deviceId, childId, DetectedMode.Story))
        {
            AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "mode_disabled"));
            return await CannedResultAsync(CannedVoiceClips.ModeDisabledKey, cancellationToken);
        }

        // Daily cost cap — cheaper than Ask() (STT only; ack + close TTS are
        // cached after first render), but STT still costs, so cap it the same.
        var costCapOpts = _costCapOptions.Value;
        if (costCapOpts.Enabled)
        {
            var nowUtc = DateTime.UtcNow;
            // #022 — fleet-wide ceiling (kill-switch), opt-in (skipped when Global <= 0).
            if (costCapOpts.Global > 0m && _costMeter.IsGlobalOverCap(costCapOpts.Global, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "story_qa"));
                return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
            }
            var cap = costCapOpts.CapForDevice(deviceId);
            if (_costMeter.IsOverCap(deviceId, cap, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1, new KeyValuePair<string, object?>("kind", "story_qa"));
                return await CannedResultAsync(CannedVoiceClips.PausedKey, cancellationToken);
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

        var turnStopwatch = Stopwatch.StartNew();
        void RecordTurn(string outcome)
        {
            turnStopwatch.Stop();
            AppMeter.StoryQaDuration.Record(turnStopwatch.Elapsed.TotalSeconds);
            AppMeter.StoryQaTurn.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        // Voice → text (Whisper, Armenian). Bias with the reflection question
        // the child is answering. The transcript is used ONLY for the safety
        // record + the parent dashboard — the spoken reply does NOT depend on
        // its content (it is a fixed warm line), so STT accuracy is not
        // load-bearing here.
        string answer;
        try
        {
            using var sttStream = new MemoryStream(audioBytes, writable: false);
            answer = await _transcription.TranscribeArmenianAsync(
                sttStream, inboundContentType!, story.ReflectionQuestions[questionIndex],
                cancellationToken, model: null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reflection: STT failure for story {StoryId}", storyId);
            RecordTurn("transient_failure");
            return await SpokenFailureResultAsync(cancellationToken);
        }

        // Input moderation on the child's answer (same fail-closed contract as
        // Ask()). A blocked or empty answer still gets a warm goodbye, but we do
        // NOT speak an affirming line over flagged/absent content — only the
        // neutral close — and we record the flag for the parent dashboard.
        var userFlag = SafetyFlag.Clean;
        var assistantFlag = SafetyFlag.Clean;
        var affirm = true;
        var outcome = "reflection_answered";
        if (string.IsNullOrWhiteSpace(answer))
        {
            affirm = false;
            outcome = "reflection_empty";
        }
        else
        {
            var inputModeration = await _moderation.CheckContentAsync(answer);
            if (!inputModeration.IsSafe)
            {
                _logger.LogWarning(
                    "Reflection input blocked. StoryId: {StoryId}, Categories: {Categories}",
                    storyId, string.Join(", ", inputModeration.FlaggedCategories));
                affirm = false;
                userFlag = SafetyFlag.Blocked;
                assistantFlag = SafetyFlag.Flagged;
                outcome = "reflection_blocked";
            }
        }

        // Reflection dialogue (owner request 2026-08-03): when the answer is
        // affirmable, try a MODEL-composed reaction to what the child
        // actually said — the one GPT surface of the after-story flow, with
        // the same validate/repair/fallback discipline as in-story Q&A plus
        // OUTPUT MODERATION here. Any doubt (gate off, service absent,
        // validation failure, unsafe output, moderation unavailable) falls
        // back to the deterministic rotated acknowledgement — the child
        // never hears unvalidated model text.
        string? reactionText = null;
        var aiRepliesEnabled =
            !bool.TryParse(_config["StoryQa:ReflectionAiReplies"], out var aiOn) || aiOn;
        if (affirm && aiRepliesEnabled && _reflectionDialogue is not null)
        {
            try
            {
                var reaction = await _reflectionDialogue.ReactAsync(story, questionIndex, answer);
                if (reaction.Text is not null)
                {
                    var outputModeration = await _moderation.CheckContentAsync(reaction.Text);
                    if (outputModeration.IsSafe)
                    {
                        reactionText = reaction.Text;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Reflection reaction blocked by output moderation for {StoryId} (falling back)",
                            storyId);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reflection reaction failed for {StoryId} (falling back)", storyId);
            }
        }

        // The per-question CONCLUSION (authored, reviewed) — spoken after the
        // reaction. Cached per (story, question) like the ack clips. Null for
        // stories that haven't authored conclusions yet.
        var conclusionText = story.ReflectionConclusions is { } conclusions
            && questionIndex < conclusions.Count
                ? conclusions[questionIndex]
                : null;

        // `last=false` is sent by the multi-question firmware loop for every
        // question but the final one, so the fixed goodbye line is spoken
        // once per dialogue, not after every question. Absent (legacy
        // single-question firmware) means "this is the last one".
        var isLast = !bool.TryParse(Request.Query["last"], out var lastFlag) || lastFlag;

        string? ackLineText = null;
        byte[] spoken;
        try
        {
            var closeAudio = await GetReflectionCloseAudioAsync(cancellationToken);
            if (affirm)
            {
                byte[] firstPart;
                if (reactionText is not null)
                {
                    // Per-request TTS — the reaction is unique to this answer.
                    var reactionTts = await _synthesis.SynthesizeArmenianAsync(
                        reactionText, cancellationToken);
                    firstPart = reactionTts.Content;
                }
                else
                {
                    var ackIndex = (int)((uint)Interlocked.Increment(ref _reflectionAckRotation)
                        % ReflectionAcknowledgements.Length);
                    ackLineText = ReflectionAcknowledgements[ackIndex];
                    firstPart = await GetReflectionAckAudioAsync(ackIndex, cancellationToken);
                }

                byte[]? conclusionAudio = conclusionText is not null
                    ? await GetReflectionConclusionAudioAsync(
                        story.Id, questionIndex, conclusionText, cancellationToken)
                    : null;

                spoken = conclusionAudio is not null
                    ? ComposeAnswerWithPause(firstPart, conclusionAudio, isLast ? closeAudio : null)
                    : (isLast
                        ? ComposeAnswerWithPause(firstPart, closeAudio, null)
                        : firstPart);
            }
            else
            {
                spoken = closeAudio;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reflection: clip render failure for {StoryId}", storyId);
            RecordTurn("transient_failure");
            return await SpokenFailureResultAsync(cancellationToken);
        }

        // Persist the turn (child answer + the spoken reply text) for the parent
        // dashboard + retention, mirroring Ask(). Nothing to persist for an
        // empty transcript.
        if (!string.IsNullOrWhiteSpace(answer))
        {
            // Persist EXACTLY what was spoken: reaction-or-ack, then the
            // conclusion (when authored), then the close (only on the final
            // question of the dialogue). Blocked answers keep close-only.
            string assistantText;
            if (!affirm)
            {
                assistantText = ReflectionClose;
            }
            else
            {
                var spokenParts = new List<string>(3);
                if (reactionText is not null) spokenParts.Add(reactionText);
                else if (ackLineText is not null) spokenParts.Add(ackLineText);
                if (conclusionText is not null) spokenParts.Add(conclusionText);
                if (isLast) spokenParts.Add(ReflectionClose);
                assistantText = string.Join(" ", spokenParts);
            }
            await PersistTurnAsync(
                deviceId, childId, answer, assistantText, userFlag, assistantFlag, cancellationToken);

            // B4 — append-only reflection-answer memory: one row per listen,
            // never overwritten, so the parent sees how the child's answers
            // evolve across repeat listens of the same story. Best-effort:
            // the conversation record above is already persisted, and a
            // failure here must never break the spoken reply.
            try
            {
                await _deviceService.AddStoryReflectionAnswerAsync(
                    deviceId, story.Id, questionIndex, answer, userFlag, DateTime.UtcNow);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reflection: answer-memory persist failed for {StoryId} (suppressed)", storyId);
            }
        }

        // Best-effort per-device cost recording (STT only — ack/close TTS are
        // cached). Wrapped so an estimator bug can never break the turn.
        if (costCapOpts.Enabled)
        {
            try
            {
                var sttCost = OpenAICostEstimator.EstimateWhisperCostUsd(audioBytes.LongLength);
                _costMeter.Record(deviceId, sttCost, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reflection cost-record failure (suppressed). DeviceId={DeviceId}", deviceId);
            }
        }

        RecordTurn(outcome);
        return File(spoken, "audio/mpeg");
    }

    /// <summary>Number of whole silence units to emit between the answer and
    /// the bridge — <c>StoryQa:AnswerBridgePauseMs</c> (default 1200 ms)
    /// rounded to the nearest unit and capped, matching the buffered
    /// <see cref="ComposeAnswerWithPause"/> path exactly.</summary>
    private int PauseRepeats()
    {
        var pauseMs = 1200;
        if (int.TryParse(_config["StoryQa:AnswerBridgePauseMs"], out var configured) && configured >= 0)
        {
            pauseMs = configured;
        }
        var unit = LoadSilenceUnit();
        return unit.Length == 0
            ? 0
            : Math.Clamp((int)Math.Round(pauseMs / (double)SilenceUnitMs), 0, 16);
    }

    /// <summary>Records the child's question + Areg's answer as a User /
    /// Assistant message pair on the device's active conversation, so the
    /// turn surfaces in the parent dashboard and is caught by the existing
    /// retention / delete cascades (Conversation → Message). Best-effort:
    /// a persistence failure is logged but never denies the child the
    /// spoken answer — the safety record is the moderation log line.</summary>
    private async Task PersistTurnAsync(
        Guid deviceId, Guid? childId, string question, string answerText,
        SafetyFlag userFlag, SafetyFlag assistantFlag, CancellationToken ct)
    {
        try
        {
            var conversation = await _conversations.GetOrCreateActiveConversationAsync(
                deviceId, childId);
            await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.User, question, userFlag);
            await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.Assistant, answerText, assistantFlag);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Story-QA: turn persistence failed for Device {DeviceId}; continuing (child still hears the answer)",
                deviceId);
        }
    }

    /// <summary>Serves a canned voice clip (paused / bedtime / mode-disabled)
    /// as 200 audio, mirroring the C1 voice path's gated responses. A clip
    /// render failure collapses to the same sanitized 502.</summary>
    private async Task<IActionResult> CannedResultAsync(string key, CancellationToken ct)
    {
        try
        {
            var clip = await _canned.GetAsync(key, ct);
            return File(clip.Content, clip.MimeType);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story-QA: canned clip render failure for key={Key}", key);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
    }

    /// <summary>On a transient STT / answer / TTS failure, serve the cached
    /// spoken safe-fallback as 200 audio so the child hears a warm line and
    /// the story can resume — instead of a silent 502 the firmware can't
    /// play. Falls back to the sanitized 502 only when the clip cannot be
    /// produced at all (TTS down AND nothing cached yet — the same
    /// first-hit-during-outage edge the C1 voice path documents).</summary>
    private async Task<IActionResult> SpokenFailureResultAsync(CancellationToken ct)
    {
        try
        {
            var clip = await GetFailureClipAsync(ct);
            return File(clip, "audio/mpeg");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Story-QA: spoken-fallback render failed; returning sanitized 502");
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
    }

    /// <summary>Renders the in-story safe-fallback line once and caches it
    /// for the process lifetime (same lazy-TTS-cache pattern as the bridge
    /// clips), so a transient failure is answered with a warm spoken line
    /// at zero per-request cost. Reuses <see cref="_bridgeLock"/> as the
    /// render lock.</summary>
    private async Task<byte[]> GetFailureClipAsync(CancellationToken ct)
    {
        if (_failureClip is { } cached)
        {
            return cached;
        }
        await _bridgeLock.WaitAsync(ct);
        try
        {
            if (_failureClip is null)
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(
                    StoryAnswerFilter.SafeFallback, ct);
                _failureClip = rendered.Content;
            }
            return _failureClip!;
        }
        finally
        {
            _bridgeLock.Release();
        }
    }

    // ── Spoken-answer assembly: answer + silent pause + return-to-story bridge ──

    // Measured duration of the embedded 24 kHz mono MP3 silence unit. The
    // pause is built by repeating this unit, so the configured pause is
    // rounded to the nearest multiple of this value.
    private const int SilenceUnitMs = 264;

    private static readonly byte[]?[] _bridgeAudios = new byte[]?[ReturnToStoryBridges.Length];
    private static readonly SemaphoreSlim _bridgeLock = new(1, 1);
    private static int _bridgeRotation = -1;
    private static byte[]? _silenceUnit;

    // Cached spoken safe-fallback clip, rendered once and reused on any
    // transient failure path so the child hears a warm line instead of a
    // silent 502. Rendered under _bridgeLock (see GetFailureClipAsync).
    private static byte[]? _failureClip;

    // Cached recap audio per "{storyId}:{segmentIndex}" — recap lines are
    // fixed per story segment, so each costs at most one TTS call.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _recapAudios = new();

    // Cached post-story reflection clips — the acknowledgement lines and the
    // close are fixed, so each costs at most one TTS call regardless of
    // traffic. Rendered under _bridgeLock (same pattern as the bridges).
    private static readonly byte[]?[] _reflectionAckAudios = new byte[]?[ReflectionAcknowledgements.Length];
    private static byte[]? _reflectionCloseAudio;
    private static int _reflectionAckRotation = -1;

    /// <summary>Renders one return-to-story bridge (by index) and caches it
    /// for the process lifetime — the lines are fixed, so each costs at most
    /// one TTS call regardless of traffic.</summary>
    private async Task<byte[]> GetBridgeAudioAsync(int index, CancellationToken ct)
    {
        if (_bridgeAudios[index] is { } cached)
        {
            return cached;
        }
        await _bridgeLock.WaitAsync(ct);
        try
        {
            if (_bridgeAudios[index] is null)
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(ReturnToStoryBridges[index], ct);
                _bridgeAudios[index] = rendered.Content;
            }
            return _bridgeAudios[index]!;
        }
        finally
        {
            _bridgeLock.Release();
        }
    }

    /// <summary>Whether to append the segment recap, gated on the answer
    /// length (a proxy for how long the child was away). Default threshold
    /// 0 → always include; raise <c>StoryQa:RecapMinAnswerChars</c> to
    /// limit the recap to longer interruptions.</summary>
    private bool ShouldIncludeRecap(string answerText)
    {
        var minChars = 0;
        if (int.TryParse(_config["StoryQa:RecapMinAnswerChars"], out var m) && m >= 0)
        {
            minChars = m;
        }
        return (answerText?.Trim().Length ?? 0) >= minChars;
    }

    /// <summary>Renders a segment's recap line and caches it per
    /// (story, segment) for the process lifetime — the lines are fixed, so
    /// each costs at most one TTS call.</summary>
    private async Task<byte[]> GetRecapAudioAsync(
        string storyId, int segmentIndex, string recapText, CancellationToken ct)
    {
        var key = $"{storyId}:{segmentIndex}";
        if (_recapAudios.TryGetValue(key, out var cached))
        {
            return cached;
        }
        await _bridgeLock.WaitAsync(ct);
        try
        {
            if (!_recapAudios.TryGetValue(key, out cached))
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(recapText, ct);
                cached = rendered.Content;
                _recapAudios[key] = cached;
            }
            return cached;
        }
        finally
        {
            _bridgeLock.Release();
        }
    }

    /// <summary>Renders one reflection acknowledgement (by index) and caches
    /// it for the process lifetime — the lines are fixed, so each costs at most
    /// one TTS call regardless of traffic.</summary>
    private async Task<byte[]> GetReflectionAckAudioAsync(int index, CancellationToken ct)
    {
        if (_reflectionAckAudios[index] is { } cached)
        {
            return cached;
        }
        await _bridgeLock.WaitAsync(ct);
        try
        {
            if (_reflectionAckAudios[index] is null)
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(ReflectionAcknowledgements[index], ct);
                _reflectionAckAudios[index] = rendered.Content;
            }
            return _reflectionAckAudios[index]!;
        }
        finally
        {
            _bridgeLock.Release();
        }
    }

    /// <summary>Per-(story, question) cache of the authored reflection
    /// CONCLUSION lines' TTS — fixed reviewed text, so each costs at most
    /// one TTS call per process lifetime.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>
        _reflectionConclusionAudio = new();

    private async Task<byte[]> GetReflectionConclusionAudioAsync(
        string storyId, int questionIndex, string conclusionText, CancellationToken ct)
    {
        var key = storyId + "|" + questionIndex;
        if (_reflectionConclusionAudio.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var rendered = await _synthesis.SynthesizeArmenianAsync(conclusionText, ct);
        _reflectionConclusionAudio.TryAdd(key, rendered.Content);
        return _reflectionConclusionAudio[key];
    }

    /// <summary>Renders the fixed reflection close line once and caches it for
    /// the process lifetime.</summary>
    private async Task<byte[]> GetReflectionCloseAudioAsync(CancellationToken ct)
    {
        if (_reflectionCloseAudio is { } cached)
        {
            return cached;
        }
        await _bridgeLock.WaitAsync(ct);
        try
        {
            if (_reflectionCloseAudio is null)
            {
                var rendered = await _synthesis.SynthesizeArmenianAsync(ReflectionClose, ct);
                _reflectionCloseAudio = rendered.Content;
            }
            return _reflectionCloseAudio!;
        }
        finally
        {
            _bridgeLock.Release();
        }
    }

    /// <summary>Loads the embedded silence unit once. Empty array if the
    /// resource is somehow missing — the answer then flows straight into
    /// the bridge with no pause, never an error.</summary>
    private static byte[] LoadSilenceUnit()
    {
        if (_silenceUnit is not null)
        {
            return _silenceUnit;
        }
        var asm = typeof(StoryQaController).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("silence-24k-mono.mp3", StringComparison.Ordinal));
        if (name is null)
        {
            _silenceUnit = [];
            return _silenceUnit;
        }
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _silenceUnit = ms.ToArray();
        return _silenceUnit;
    }

    /// <summary>
    /// Composes answer + N silence units + bridge (+ a short beat + recap,
    /// when present) into one 24 kHz mono MP3 byte buffer, returned by
    /// <see cref="Ask"/> as a <c>File(...)</c> with a Content-Length so the
    /// firmware can size its receive buffer. Pause length is
    /// <c>StoryQa:AnswerBridgePauseMs</c> (default 1200 ms), rounded to a whole
    /// number of silence units and capped so it can't run away.
    /// </summary>
    internal byte[] ComposeAnswerWithPause(byte[] answer, byte[] bridge, byte[]? recap)
    {
        var unit = LoadSilenceUnit();
        var repeats = PauseRepeats();

        var capacity = answer.Length + (unit.Length * repeats) + bridge.Length
            + (recap is not null ? unit.Length + recap.Length : 0);
        using var ms = new MemoryStream(capacity);
        ms.Write(answer, 0, answer.Length);
        for (var i = 0; i < repeats; i++)
        {
            ms.Write(unit, 0, unit.Length);
        }
        ms.Write(bridge, 0, bridge.Length);
        if (recap is not null)
        {
            ms.Write(unit, 0, unit.Length); // a short breath, then the recap
            ms.Write(recap, 0, recap.Length);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Maps a byte offset in the pre-rendered story MP3 to the segment the
    /// child is in, so the Q&amp;A prompt's "current segment" and the
    /// re-anchor recap line match where they barged in. Prefers the exact
    /// per-segment byte map written by the render
    /// (<c>{story}.segments.json</c>): the largest segment whose start byte
    /// is ≤ the offset. Falls back to the old proportional-to-file-size
    /// estimate only when that sidecar is absent (a cache rendered before
    /// the segment map existed). Falls back to segment 0 on any failure —
    /// the whole-story summary in the prompt answers correctly regardless,
    /// so position is only a context refinement.
    /// </summary>
    private int OffsetToSegment(string storyId, long offset, int segmentCount)
    {
        if (offset <= 0 || segmentCount <= 1)
        {
            return 0;
        }
        try
        {
            var root = _config["StoryAudio:CacheRoot"];
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(_env.ContentRootPath, "story-audio-cache");
            }
            var safe = string.Concat(storyId.Where(c => char.IsLetterOrDigit(c) || c == '-'));
            var path = Path.Combine(root, $"{safe}.mp3");

            // Preferred: exact segment-start byte map. Q&A is infrequent
            // (one per barge-in), so reading the small sidecar per call is
            // cheap and avoids any stale-cache risk on re-render.
            var segmentsPath = Path.ChangeExtension(path, ".segments.json");
            var segmentMap = LoadSegmentMap(segmentsPath);
            if (segmentMap.Length > 1)
            {
                return SegmentForOffset(segmentMap, offset, segmentCount);
            }

            // Fallback: proportional to file size (pre-segment-map caches).
            if (!System.IO.File.Exists(path))
            {
                return 0;
            }
            var size = new FileInfo(path).Length;
            if (size <= 0)
            {
                return 0;
            }
            var proportional = (int)(offset * segmentCount / size);
            return Math.Clamp(proportional, 0, segmentCount - 1);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Given the segment-start byte map (ascending; entry i is the
    /// first byte of segment i, entry 0 == 0), returns the index of the
    /// segment that <paramref name="offset"/> falls in — the largest segment
    /// whose start byte is ≤ the offset. Clamped to a valid segment index.
    /// Internal for direct unit testing of the lookup.</summary>
    internal static int SegmentForOffset(long[] segmentStartBytes, long offset, int segmentCount)
    {
        if (segmentStartBytes.Length <= 1 || segmentCount <= 1 || offset <= 0)
        {
            return 0;
        }
        var i = Array.BinarySearch(segmentStartBytes, offset);
        var seg = i >= 0 ? i : ~i - 1; // largest start ≤ offset
        return Math.Clamp(seg, 0, segmentCount - 1);
    }

    /// <summary>Builds the Whisper biasing context for a Q&amp;A turn: the
    /// current story segment (the scene the child is asking about),
    /// prefixed with the previous segment for a little more vocabulary, and
    /// capped to <c>MaxBiasChars</c>. The cap keeps the END of the text —
    /// the current scene — because Whisper weights the tail of the prompt
    /// most. Returns null when there is nothing useful to bias with.</summary>
    internal static string? BuildTranscriptionBias(CuratedStory story, int segmentIndex)
    {
        const int MaxBiasChars = 600;
        if (story.Segments.Count == 0)
        {
            return null;
        }
        var idx = Math.Clamp(segmentIndex, 0, story.Segments.Count - 1);
        var prefix = idx > 0 ? story.Segments[idx - 1].Text + " " : string.Empty;
        var context = prefix + story.Segments[idx].Text;
        if (context.Length > MaxBiasChars)
        {
            context = context[^MaxBiasChars..]; // keep the current scene (tail)
        }
        return string.IsNullOrWhiteSpace(context) ? null : context;
    }

    /// <summary>Loads the segment-start byte map sidecar, or an empty array
    /// when it is missing / unreadable (caller then falls back).</summary>
    private static long[] LoadSegmentMap(string segmentsPath)
    {
        try
        {
            if (!System.IO.File.Exists(segmentsPath))
            {
                return [];
            }
            var json = System.IO.File.ReadAllText(segmentsPath);
            return System.Text.Json.JsonSerializer.Deserialize<long[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

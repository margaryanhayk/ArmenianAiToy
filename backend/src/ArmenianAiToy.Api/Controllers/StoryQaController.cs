using System.Diagnostics;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    private readonly IAudioTranscriptionService _transcription;
    private readonly IAudioSynthesisService _synthesis;
    private readonly ICuratedStoryLibrary _library;
    private readonly LibraryStoryQuestionService _questions;
    private readonly IModerationService _moderation;
    private readonly IConversationService _conversations;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<StoryQaController> _logger;

    public StoryQaController(
        IAudioTranscriptionService transcription,
        IAudioSynthesisService synthesis,
        ICuratedStoryLibrary library,
        LibraryStoryQuestionService questions,
        IModerationService moderation,
        IConversationService conversations,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<StoryQaController> logger)
    {
        _transcription = transcription;
        _synthesis = synthesis;
        _library = library;
        _questions = questions;
        _moderation = moderation;
        _conversations = conversations;
        _env = env;
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
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

        // Where the child was when they barged in — used both to ground the
        // Q&A prompt and to pick that segment's re-anchor (recap) line.
        var segmentIndex = OffsetToSegment(storyId, offset, story.Segments.Count);

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

        // Voice-path observability: time the whole turn (this is the "dead
        // air" the child perceives after a barge-in) and record the outcome.
        // The 404/400 input-validation rejections above are deliberately NOT
        // metered — only real turns that reach transcription. RecordTurn is
        // called exactly once on every return path below.
        var turnStopwatch = Stopwatch.StartNew();
        void RecordTurn(string outcome)
        {
            turnStopwatch.Stop();
            AppMeter.StoryQaDuration.Record(turnStopwatch.Elapsed.TotalSeconds);
            AppMeter.StoryQaTurn.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
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
            question = await _transcription.TranscribeArmenianAsync(
                sttStream, inboundContentType!, transcriptionBias, cancellationToken,
                model: string.IsNullOrWhiteSpace(qaTranscriptionModel) ? null : qaTranscriptionModel);
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
            var inputModeration = await _moderation.CheckContentAsync(question);
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
            else
            {
                try
                {
                    var answer = await _questions.AnswerAsync(story, segmentIndex, question);
                    answerText = answer.Text;
                    turnOutcome = answer.UsedFallback ? "answer_fallback" : "answered";
                    _logger.LogInformation(
                        "Story-QA answered. Story: {StoryId}, Segment: {Segment}, UsedFallback: {Fallback}, Q: {Q}",
                        storyId, segmentIndex, answer.UsedFallback,
                        question.Length > 40 ? question[..40] + "…" : question);
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

        // Log the full Q&A pair (raw answer, pre-bridge) so it can be
        // reviewed for Armenian quality by armenian-story-master.
        _logger.LogInformation(
            "Story-QA pair. StoryId: {StoryId} | Q: «{Question}» | A: «{Answer}»",
            storyId, string.IsNullOrWhiteSpace(question) ? "(empty)" : question, answerText);

        // Persist the turn so it appears in the parent dashboard and is
        // covered by retention/delete — the streaming story path is
        // otherwise stateless, leaving the child's voice unrecorded. Text
        // is canonical, mirroring the chat paths. An empty transcript
        // carries no child words, so nothing is persisted for it.
        if (!string.IsNullOrWhiteSpace(question))
        {
            await PersistTurnAsync(
                deviceId, question, answerText, userFlag, assistantFlag, cancellationToken);
        }

        // Text → voice. Render the ANSWER on its own, then a calm pause,
        // then the (cached) return-to-story bridge — so the child has a
        // beat to take in the answer before the narration resumes, instead
        // of the answer and "let's go back" snapping together.
        AudioSynthesisResult answerTts;
        try
        {
            answerTts = await _synthesis.SynthesizeArmenianAsync(answerText.TrimEnd(), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story-QA: TTS failure for {StoryId}", storyId);
            RecordTurn("transient_failure");
            return await SpokenFailureResultAsync(cancellationToken);
        }

        // The answer is now fully GENERATED, VALIDATED (moderation +
        // StoryAnswerFilter/repair/fallback above), persisted, and
        // SYNTHESIZED. ONLY now do we begin emitting audio — and we stream it
        // so the child starts hearing the validated answer immediately,
        // instead of waiting ~7-8s for the whole answer+pause+bridge+recap
        // clip to be assembled and transmitted. We NEVER stream raw GPT
        // tokens; only the already-validated answer's audio plus the fixed
        // pause/bridge/recap clips flow over the wire.
        //
        // Wire bytes are IDENTICAL and in the SAME ORDER as the previous
        // buffered ComposeAnswerWithPause(answer, bridge, recap) output:
        // answer → N silence units → bridge → [breath + recap]. A client
        // that buffers the full body (today's firmware) gets exactly the
        // same result; a client that plays as it receives gets early audio.
        var bridgeIndex = (int)((uint)Interlocked.Increment(ref _bridgeRotation) % ReturnToStoryBridges.Length);
        var includeRecap = ShouldIncludeRecap(answerText);
        var recapText = includeRecap
            ? LibraryStoryQuestionService.GetSegmentRecap(storyId, segmentIndex)
            : null;

        RecordTurn(turnOutcome);

        // The turn outcome is already metered above (the validated answer is
        // produced); writing the tail clips is best-effort and never changes
        // the recorded outcome, mirroring the previous buffered behavior
        // (bridge failure → answer only; recap failure → drop recap).
        async Task WriteSpokenAsync(Stream body, CancellationToken ct)
        {
            // 1) The validated answer — flushed first so playback can begin.
            await body.WriteAsync(answerTts.Content, ct);
            await body.FlushAsync(ct);

            // 2) The calm pause (whole silence units), 3) the return-to-story
            //    bridge. A bridge render failure degrades to "answer only":
            //    the answer is already on the wire, so we just stop.
            byte[] bridgeAudio;
            try
            {
                bridgeAudio = await GetBridgeAudioAsync(bridgeIndex, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Story-QA: bridge TTS failure for {StoryId}; answer only", storyId);
                return;
            }

            await WriteSilenceUnitsAsync(body, PauseRepeats(), ct);
            await body.WriteAsync(bridgeAudio, ct);
            await body.FlushAsync(ct);

            // 4) Tier 2 recap after a short breath — best-effort: a recap
            //    render failure just drops the recap, never the answer/bridge.
            if (!string.IsNullOrWhiteSpace(recapText))
            {
                try
                {
                    var recapAudio = await GetRecapAudioAsync(storyId, segmentIndex, recapText!, ct);
                    await WriteSilenceUnitsAsync(body, 1, ct); // a short breath
                    await body.WriteAsync(recapAudio, ct);
                    await body.FlushAsync(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Story-QA: recap TTS failed for {StoryId} seg {Segment}; skipping recap",
                        storyId, segmentIndex);
                }
            }
        }

        return new StreamingAudioResult(answerTts.MimeType, WriteSpokenAsync);
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

    /// <summary>Writes the embedded silence unit <paramref name="count"/>
    /// times (the same bytes <see cref="ComposeAnswerWithPause"/> concatenates).</summary>
    private static async Task WriteSilenceUnitsAsync(Stream body, int count, CancellationToken ct)
    {
        var unit = LoadSilenceUnit();
        if (unit.Length == 0)
        {
            return;
        }
        for (var i = 0; i < count; i++)
        {
            await body.WriteAsync(unit, ct);
        }
    }

    /// <summary>Records the child's question + Areg's answer as a User /
    /// Assistant message pair on the device's active conversation, so the
    /// turn surfaces in the parent dashboard and is caught by the existing
    /// retention / delete cascades (Conversation → Message). Best-effort:
    /// a persistence failure is logged but never denies the child the
    /// spoken answer — the safety record is the moderation log line.</summary>
    private async Task PersistTurnAsync(
        Guid deviceId, string question, string answerText,
        SafetyFlag userFlag, SafetyFlag assistantFlag, CancellationToken ct)
    {
        try
        {
            var conversation = await _conversations.GetOrCreateActiveConversationAsync(
                deviceId, childId: null);
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
    /// when present) into one 24 kHz mono MP3 byte stream — the canonical
    /// byte ORDER the streamed response (the <c>WriteSpokenAsync</c> local in
    /// <see cref="Ask"/>) writes incrementally. Retained as the single
    /// reference for that order and reused by tests that assert the streamed
    /// body is byte-identical to the previously-buffered composition. Pause
    /// length is <c>StoryQa:AnswerBridgePauseMs</c> (default 1200 ms), rounded
    /// to a whole number of silence units and capped so it can't run away.
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

/// <summary>
/// A chunked (no Content-Length) audio response that writes its body via a
/// caller-supplied async writer. The in-story Q&amp;A path uses it to push
/// the already-validated answer's audio onto the wire immediately, then the
/// pause + bridge (+ recap), so the child starts hearing the answer without
/// waiting for the whole clip to be assembled and transmitted.
/// <para>
/// BACKWARD-COMPATIBLE: the bytes (and their order) are exactly what the
/// previous buffered <c>File(ComposeAnswerWithPause(...))</c> returned, just
/// flushed incrementally. A client that buffers the entire response body
/// (today's firmware) reconstructs the identical clip; a client that plays as
/// it receives gets early audio. Content-Type stays <c>audio/mpeg</c>.
/// </para>
/// </summary>
public sealed class StreamingAudioResult : IActionResult
{
    private readonly string _contentType;
    private readonly Func<Stream, CancellationToken, Task> _writeBody;

    public StreamingAudioResult(string contentType, Func<Stream, CancellationToken, Task> writeBody)
    {
        _contentType = contentType;
        _writeBody = writeBody;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = _contentType;
        // No Content-Length → Kestrel uses chunked transfer-encoding, so the
        // first flushed bytes leave the server before the tail is assembled.
        await _writeBody(response.Body, context.HttpContext.RequestAborted);
    }
}

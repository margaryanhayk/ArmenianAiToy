using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
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

        // Voice → text (Whisper, Armenian).
        string question;
        try
        {
            using var sttStream = new MemoryStream(audioBytes, writable: false);
            question = await _transcription.TranscribeArmenianAsync(
                sttStream, inboundContentType!, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story-QA: STT failure for story {StoryId}", storyId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
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
        if (string.IsNullOrWhiteSpace(question))
        {
            answerText = StoryAnswerFilter.SafeFallback;
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
            }
            else
            {
                try
                {
                    var answer = await _questions.AnswerAsync(story, segmentIndex, question);
                    answerText = answer.Text;
                    _logger.LogInformation(
                        "Story-QA answered. Story: {StoryId}, Segment: {Segment}, UsedFallback: {Fallback}, Q: {Q}",
                        storyId, segmentIndex, answer.UsedFallback,
                        question.Length > 40 ? question[..40] + "…" : question);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Story-QA: answer failure for {StoryId}", storyId);
                    return StatusCode(502, new { error = "AI service unavailable. Please try again." });
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
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }

        byte[] bridgeAudio;
        try
        {
            var bridgeIndex = (int)((uint)Interlocked.Increment(ref _bridgeRotation) % ReturnToStoryBridges.Length);
            bridgeAudio = await GetBridgeAudioAsync(bridgeIndex, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Bridge render failed: still give the child the answer rather
            // than a 502 — losing the soft return line degrades gracefully.
            _logger.LogWarning(ex, "Story-QA: bridge TTS failure for {StoryId}; returning answer only", storyId);
            return File(answerTts.Content, answerTts.MimeType);
        }

        // Tier 2 — a short "remember where we are" recap after the bridge,
        // re-establishing the scene before narration resumes (the
        // goal-reactivation step). Gated by answer length via
        // StoryQa:RecapMinAnswerChars (default 0 = always) so it can later
        // be limited to longer interruptions. Best-effort: a recap TTS
        // failure just drops the recap, never the answer.
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

    // ── Spoken-answer assembly: answer + silent pause + return-to-story bridge ──

    // Measured duration of the embedded 24 kHz mono MP3 silence unit. The
    // pause is built by repeating this unit, so the configured pause is
    // rounded to the nearest multiple of this value.
    private const int SilenceUnitMs = 264;

    private static readonly byte[]?[] _bridgeAudios = new byte[]?[ReturnToStoryBridges.Length];
    private static readonly SemaphoreSlim _bridgeLock = new(1, 1);
    private static int _bridgeRotation = -1;
    private static byte[]? _silenceUnit;

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

    /// <summary>Concatenates answer + N silence units + bridge (+ a short
    /// beat + recap, when present) into one 24 kHz mono MP3 byte stream
    /// (same format the firmware already decodes for the streamed story).
    /// Pause length is <c>StoryQa:AnswerBridgePauseMs</c> (default 1200 ms),
    /// rounded to a whole number of silence units and capped so it can't
    /// run away. The recap, when supplied, follows the bridge after one
    /// silence-unit breath.</summary>
    private byte[] ComposeAnswerWithPause(byte[] answer, byte[] bridge, byte[]? recap)
    {
        var pauseMs = 1200;
        if (int.TryParse(_config["StoryQa:AnswerBridgePauseMs"], out var configured) && configured >= 0)
        {
            pauseMs = configured;
        }

        var unit = LoadSilenceUnit();
        var repeats = unit.Length == 0
            ? 0
            : Math.Clamp((int)Math.Round(pauseMs / (double)SilenceUnitMs), 0, 16);

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
    /// Best-effort map of a byte offset in the pre-rendered story MP3 to
    /// a segment index, proportional to the cached file size, so the
    /// Q&amp;A prompt's "current segment" roughly matches where the child
    /// is. Falls back to segment 0 when the cache size is unknown — the
    /// whole-story summary + essence in the prompt answer correctly
    /// regardless, so position is only a context refinement.
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
            if (!System.IO.File.Exists(path))
            {
                return 0;
            }
            var size = new FileInfo(path).Length;
            if (size <= 0)
            {
                return 0;
            }
            var seg = (int)(offset * segmentCount / size);
            return Math.Clamp(seg, 0, segmentCount - 1);
        }
        catch
        {
            return 0;
        }
    }
}

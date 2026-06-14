using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.Audio;
using ArmenianAiToy.Application.Stories;
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
    // Warm return-to-story line appended to every spoken answer so the
    // narration doesn't resume abruptly after a question. Reviewed by
    // armenian-story-master (2026-06-14): one calm statement, not a
    // question (the story resumes right after).
    private const string ReturnToStoryBridge = "Իսկ հիմա վերադառնանք մեր հեքիաթին։";

    private readonly IAudioTranscriptionService _transcription;
    private readonly IAudioSynthesisService _synthesis;
    private readonly ICuratedStoryLibrary _library;
    private readonly LibraryStoryQuestionService _questions;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<StoryQaController> _logger;

    public StoryQaController(
        IAudioTranscriptionService transcription,
        IAudioSynthesisService synthesis,
        ICuratedStoryLibrary library,
        LibraryStoryQuestionService questions,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<StoryQaController> logger)
    {
        _transcription = transcription;
        _synthesis = synthesis;
        _library = library;
        _questions = questions;
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
        string answerText;
        if (string.IsNullOrWhiteSpace(question))
        {
            answerText = StoryAnswerFilter.SafeFallback;
            _logger.LogInformation("Story-QA: empty transcript for {StoryId}; speaking fallback", storyId);
        }
        else
        {
            try
            {
                var segmentIndex = OffsetToSegment(storyId, offset, story.Segments.Count);
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

        // Log the full Q&A pair (raw answer, pre-bridge) so it can be
        // reviewed for Armenian quality by armenian-story-master.
        _logger.LogInformation(
            "Story-QA pair. StoryId: {StoryId} | Q: «{Question}» | A: «{Answer}»",
            storyId, string.IsNullOrWhiteSpace(question) ? "(empty)" : question, answerText);

        // Smooth the resume: end the spoken answer with a warm
        // return-to-story bridge so the narration doesn't snap back in.
        var spokenText = answerText.TrimEnd() + " " + ReturnToStoryBridge;

        // Text → voice.
        try
        {
            var tts = await _synthesis.SynthesizeArmenianAsync(spokenText, cancellationToken);
            return File(tts.Content, tts.MimeType);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story-QA: TTS failure for {StoryId}", storyId);
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
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

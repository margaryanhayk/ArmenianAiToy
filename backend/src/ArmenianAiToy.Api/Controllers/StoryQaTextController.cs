using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// TEXT-only in-story Q&amp;A harness — no STT, no TTS, no device.
///
/// Lets a story be tested for answer quality from a plain HTTP client:
/// send a story id, a segment (where the child "stopped"), and the
/// question as text; get the answer as text. It runs the EXACT same
/// bounded pipeline the voice path uses
/// (input moderation → <see cref="LibraryStoryQuestionService"/> →
/// prompt → GPT → validate → repair-once → canned fallback → output
/// moderation), so what you read here is what the toy would say.
///
/// Unauthenticated dev harness (route outside the device-auth prefixes),
/// and therefore <b>Development-only</b>: outside Development every
/// request is a 404, matching the fail-closed concealment posture of
/// <c>/metrics</c> and <c>/api/internal/*</c>. An unauthenticated route
/// that reaches GPT must not exist in a deployed image — it would be an
/// open relay against the deployment's own OpenAI key, outside the
/// per-device daily cost cap (which keys on <c>X-Device-Id</c>).
///
/// Moderation is NOT optional here even though the route is dev-gated:
/// <see cref="LibraryStoryQuestionService"/> has no moderation of its own
/// (<see cref="StoryAnswerFilter"/> validates story fidelity and format,
/// it is not a safety classifier), so without the two checks below this
/// harness would be the one child-facing pipeline in the repo that skips
/// the dual-moderation contract.
/// </summary>
[ApiController]
[Route("api/story-qa-text")]
public class StoryQaTextController : ControllerBase
{
    private readonly ICuratedStoryLibrary _library;
    private readonly LibraryStoryQuestionService _questions;
    private readonly IModerationService _moderation;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<StoryQaTextController> _logger;

    public StoryQaTextController(
        ICuratedStoryLibrary library,
        LibraryStoryQuestionService questions,
        IModerationService moderation,
        IWebHostEnvironment env,
        ILogger<StoryQaTextController> logger)
    {
        _library = library;
        _questions = questions;
        _moderation = moderation;
        _env = env;
        _logger = logger;
    }

    public sealed record QaTextRequest(string StoryId, int Segment, string Question);

    public sealed record QaTextResponse(
        string StoryId,
        int Segment,
        string Question,
        string Answer,
        bool UsedFallback,
        string FirstRejection);

    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Ask(
        [FromBody] QaTextRequest request, CancellationToken cancellationToken = default)
    {
        // Concealment before validation: a non-Development caller learns
        // nothing about the route, not even that it exists.
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "storyId and question are required." });
        }
        var story = _library.GetById(request.StoryId);
        if (story is null)
        {
            return NotFound(new { error = $"Unknown story '{request.StoryId}'." });
        }

        var segment = Math.Clamp(request.Segment, 0, story.Segments.Count - 1);

        // INPUT moderation before any GPT call — same ordering as the voice
        // path (StoryQaController) and the text path (ChatService step 2).
        // CheckContentAsync is fail-closed-to-(IsSafe=false) by contract and
        // never throws, so "moderation_unavailable" collapses to unsafe here
        // too; it is logged separately only so an infra hiccup stays
        // separable from a genuine content flag.
        var inputModeration = await _moderation.CheckContentAsync(request.Question);
        if (!inputModeration.IsSafe)
        {
            _logger.LogWarning(
                "Story-QA-text input blocked. StoryId: {StoryId} Segment: {Segment} | Categories: {Categories}",
                request.StoryId, segment, string.Join(", ", inputModeration.FlaggedCategories));

            return Ok(new QaTextResponse(
                request.StoryId, segment, request.Question,
                StoryAnswerFilter.SafeFallback, UsedFallback: true,
                FirstRejection: "moderation_blocked"));
        }

        var answer = await _questions.AnswerAsync(story, segment, request.Question);

        // OUTPUT moderation on a model-authored answer only. The canned
        // fallback is pre-reviewed text, so re-classifying it would just add
        // a round-trip; a model-authored answer can carry real-world content
        // the story filter does not judge, so it must clear the classifier.
        if (!answer.UsedFallback)
        {
            var outputModeration = await _moderation.CheckContentAsync(answer.Text);
            if (!outputModeration.IsSafe)
            {
                _logger.LogWarning(
                    "Story-QA-text OUTPUT blocked. StoryId: {StoryId} Segment: {Segment} | Categories: {Categories}",
                    request.StoryId, segment, string.Join(", ", outputModeration.FlaggedCategories));

                return Ok(new QaTextResponse(
                    request.StoryId, segment, request.Question,
                    StoryAnswerFilter.SafeFallback, UsedFallback: true,
                    FirstRejection: "output_blocked"));
            }
        }

        // Privacy (#005): never log the child's question / the answer text.
        _logger.LogInformation(
            "Story-QA-text. StoryId: {StoryId} Segment: {Segment} | UsedFallback: {Fallback} | QLen: {QLen} | ALen: {ALen}",
            request.StoryId, segment, answer.UsedFallback, request.Question.Length, answer.Text.Length);

        return Ok(new QaTextResponse(
            request.StoryId, segment, request.Question,
            answer.Text, answer.UsedFallback, answer.FirstRejection.ToString()));
    }
}

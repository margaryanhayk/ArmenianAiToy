using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// TEXT-only in-story Q&amp;A harness — no STT, no TTS, no device.
///
/// Lets a story be tested for answer quality from a plain HTTP client:
/// send a story id, a segment (where the child "stopped"), and the
/// question as text; get the answer as text. It runs the EXACT same
/// bounded pipeline the voice path uses
/// (<see cref="LibraryStoryQuestionService"/> → prompt → GPT → validate
/// → repair-once → canned fallback), so what you read here is what the
/// toy would say.
///
/// Unauthenticated dev harness (route outside the device-auth prefixes)
/// — it only answers questions about an already-public curated story.
/// </summary>
[ApiController]
[Route("api/story-qa-text")]
public class StoryQaTextController : ControllerBase
{
    private readonly ICuratedStoryLibrary _library;
    private readonly LibraryStoryQuestionService _questions;
    private readonly ILogger<StoryQaTextController> _logger;

    public StoryQaTextController(
        ICuratedStoryLibrary library,
        LibraryStoryQuestionService questions,
        ILogger<StoryQaTextController> logger)
    {
        _library = library;
        _questions = questions;
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
        var answer = await _questions.AnswerAsync(story, segment, request.Question);

        _logger.LogInformation(
            "Story-QA-text. StoryId: {StoryId} Segment: {Segment} | Q: «{Q}» | A: «{A}»",
            request.StoryId, segment, request.Question, answer.Text);

        return Ok(new QaTextResponse(
            request.StoryId, segment, request.Question,
            answer.Text, answer.UsedFallback, answer.FirstRejection.ToString()));
    }
}

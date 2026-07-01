using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Device-facing story metadata. Currently exposes the deterministic
/// "story of the day" so the toy can open each day on a fresh pick that
/// rotates automatically. Route is <c>/api/stories</c> — deliberately
/// OUTSIDE the device-auth prefixes (like <c>/api/story-audio</c>): the
/// pick is non-sensitive metadata (id + title + counts), and the toy's
/// simple GET should not need to attach device headers. The audio itself
/// stays behind the existing token-gated <c>/api/story-audio/{id}</c>.
/// </summary>
[ApiController]
[Route("api/stories")]
public class StoryController : ControllerBase
{
    private readonly ICuratedStoryLibrary _library;
    private readonly ILogger<StoryController> _logger;

    public StoryController(ICuratedStoryLibrary library, ILogger<StoryController> logger)
    {
        _library = library;
        _logger = logger;
    }

    /// <summary>
    /// Returns today's story for the caller's local date + time-of-day.
    /// Evening picks a calm (bedtime-safe) story; morning/daytime pick
    /// from the whole library. Deterministic per (local date, bucket) so
    /// re-asking within the same window returns the same story.
    /// <para>
    /// <c>tz</c> is an optional IANA id (default <c>Asia/Yerevan</c>);
    /// unresolvable ids fail soft to UTC with <c>timeZoneResolved=false</c>,
    /// same posture as the parent dashboard's time-zone handling.
    /// <c>asOf</c> is an optional ISO8601 instant for testing; the server
    /// uses <c>DateTime.UtcNow</c> when absent.
    /// </para>
    /// </summary>
    [HttpGet("of-the-day")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult GetStoryOfTheDay(
        [FromQuery] string? tz = null,
        [FromQuery] DateTimeOffset? asOf = null)
    {
        var nowUtc = asOf?.UtcDateTime ?? DateTime.UtcNow;
        var attemptedTz = string.IsNullOrWhiteSpace(tz) ? "Asia/Yerevan" : tz;

        TimeZoneInfo zone;
        var resolved = true;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(attemptedTz);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning(
                "Story-of-the-day: unresolvable time zone '{Tz}', falling back to UTC.", attemptedTz);
            zone = TimeZoneInfo.Utc;
            resolved = false;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var part = StoryOfTheDaySelector.ResolvePartOfDay(TimeOnly.FromDateTime(local));
        var story = StoryOfTheDaySelector.Select(
            _library.ListAvailable(), DateOnly.FromDateTime(local), part);

        if (story is null)
        {
            return NotFound(new { error = "No stories available." });
        }

        return Ok(new StoryOfTheDayDto(
            PartOfDay: part.ToString(),
            StoryId: story.Id,
            Title: story.Title,
            SegmentCount: story.Segments.Count,
            BedtimeSafe: story.BedtimeSafe,
            AsOfLocal: local,
            TimeZoneId: attemptedTz,
            TimeZoneResolved: resolved));
    }
}

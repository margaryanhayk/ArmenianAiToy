using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Strict on-disk schema (schemaVersion 1) for curated story content
/// files (<c>*.story.json</c>). Slice 1 of the content-based library:
/// proves the format and the loader; the real launch stories stay
/// hardcoded in <see cref="InMemoryCuratedStoryLibrary"/> until the
/// migration slice. Parsing is deliberately unforgiving — a story file
/// that is malformed in ANY way must fail loudly at load/test time,
/// never serve silently.
/// </summary>
internal sealed class StoryFileDto
{
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>Optional author attribution (Armenian, e.g. «Հովհաննես
    /// Թումանյան»). Feeds the parent library card and the spoken story
    /// intro («Հեքիաթ՝ …, հեղինակ՝ …»). Null = unknown/unverified or an
    /// in-project original — the intro then omits the author line. Never
    /// guess an attribution: a wrong author spoken to a child is worse
    /// than none.</summary>
    public string? Author { get; init; }

    /// <summary>Optional parent-facing purpose of the story (Armenian,
    /// one sentence). Shown on the library card; not spoken.</summary>
    public string? Goal { get; init; }

    /// <summary>Optional "what the story teaches" (Armenian, 1–2 warm
    /// sentences, never a lecture). Parent library card + the source text
    /// for the toy's spoken after-story summary clip. Like reflection
    /// metadata, it is toy-spoken content: owner listen test gates any
    /// render of it.</summary>
    public string? Lesson { get; init; }

    public required int MinAge { get; init; }
    public required int MaxAge { get; init; }
    public required string Tone { get; init; }
    public required string[] Tags { get; init; }
    public required string Category { get; init; }
    public required bool BedtimeSafe { get; init; }
    public required string[] Segments { get; init; }
    public required string ReflectionText { get; init; }
    public required string[] ReflectionQuestions { get; init; }

    /// <summary>Optional per-question takeaway lines for the reflection
    /// dialogue («here is what this question teaches us»), spoken by the
    /// toy after it reacts to the child's answer. When present, MUST be
    /// exactly as long as <see cref="ReflectionQuestions"/> — a mismatch
    /// is an authoring bug, never a runtime guess.</summary>
    public string[]? ReflectionConclusions { get; init; }

    public required StoryFileReviewDto Review { get; init; }
}

internal sealed class StoryFileReviewDto
{
    public required string Status { get; init; }
    public string? LinguisticReviewAt { get; init; }
    public string? ListenTestAt { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Parses and validates one story file into the existing
/// <see cref="CuratedStory"/> record. Text is preserved VERBATIM — no
/// trimming, no normalization, no rewriting; the JSON string value is
/// the served value. Internal: exercised directly by tests (the test
/// project has InternalsVisibleTo) and by
/// <see cref="EmbeddedCuratedStoryLibrary"/>.
/// </summary>
internal static class StoryFileParser
{
    public const int SupportedSchemaVersion = 1;

    /// <summary>Bounded tag vocabulary. Extending it is a reviewed
    /// code change by design — free-form tags would let selection
    /// semantics drift without review.</summary>
    public static readonly IReadOnlyList<string> AllowedTags =
    [
        "friendship", "kindness", "sharing", "patience", "curiosity",
        "courage", "calm", "nature", "family", "helping",
    ];

    public static readonly IReadOnlyList<string> AllowedCategories =
        ["general", "therapeutic", "prophylactic"];

    /// <summary>Review statuses. "draft" = story in the promotion
    /// pipeline (backend/content/story-drafts/) — original material or
    /// an owner-designated exact classic text (e.g. anban-huri).
    /// "approved" = the ONLY status the runtime loader accepts.
    /// "source" = exact reference/research import
    /// (backend/content/source-stories/) — never served, never
    /// embedded, never approved in place; it leaves that folder only
    /// as inspiration for a new-id draft or via an explicit owner
    /// designation that moves it into the drafts pipeline. Like
    /// "draft", "source" fails any requireApproved:true parse.</summary>
    public static readonly IReadOnlyList<string> AllowedStatuses =
        ["draft", "approved", "source"];

    private static readonly Regex KebabCaseId =
        new("^[a-z]+(-[a-z]+)*$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>
    /// Parses one story file. Throws <see cref="InvalidDataException"/>
    /// (with <paramref name="sourceName"/> in the message) on ANY shape
    /// or semantic violation. <paramref name="requireApproved"/> is true
    /// for runtime loading — embedded stories must be approved; a future
    /// draft-linting caller may pass false.
    /// </summary>
    public static CuratedStory Parse(string json, string sourceName, bool requireApproved = true)
    {
        StoryFileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<StoryFileDto>(json, StrictOptions)
                ?? throw Fail(sourceName, "file deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"{sourceName}: invalid story file JSON — {ex.Message}", ex);
        }

        if (dto.SchemaVersion != SupportedSchemaVersion)
        {
            throw Fail(sourceName, $"unsupported schemaVersion {dto.SchemaVersion} (supported: {SupportedSchemaVersion})");
        }
        if (string.IsNullOrWhiteSpace(dto.Id) || !KebabCaseId.IsMatch(dto.Id))
        {
            throw Fail(sourceName, $"id '{dto.Id}' is not kebab-case (^[a-z]+(-[a-z]+)*$)");
        }
        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            throw Fail(sourceName, "title is empty");
        }
        // Optional metadata: absent (null) is fine; PRESENT-but-blank is a
        // shape violation — an empty author/goal/lesson in a file is always
        // an authoring mistake, not a statement.
        if (dto.Author is not null && string.IsNullOrWhiteSpace(dto.Author))
        {
            throw Fail(sourceName, "author is present but blank (omit the field instead)");
        }
        if (dto.Goal is not null && string.IsNullOrWhiteSpace(dto.Goal))
        {
            throw Fail(sourceName, "goal is present but blank (omit the field instead)");
        }
        if (dto.Lesson is not null && string.IsNullOrWhiteSpace(dto.Lesson))
        {
            throw Fail(sourceName, "lesson is present but blank (omit the field instead)");
        }
        if (dto.MinAge <= 0 || dto.MaxAge < dto.MinAge)
        {
            throw Fail(sourceName, $"invalid age range {dto.MinAge}-{dto.MaxAge}");
        }
        if (string.IsNullOrWhiteSpace(dto.Tone))
        {
            throw Fail(sourceName, "tone is empty");
        }
        foreach (var tag in dto.Tags)
        {
            if (!AllowedTags.Contains(tag, StringComparer.Ordinal))
            {
                throw Fail(sourceName, $"tag '{tag}' is not in the allowed vocabulary");
            }
        }
        if (!AllowedCategories.Contains(dto.Category, StringComparer.Ordinal))
        {
            throw Fail(sourceName, $"category '{dto.Category}' is not one of: {string.Join(", ", AllowedCategories)}");
        }
        if (dto.Segments.Length == 0 || dto.Segments.Any(string.IsNullOrWhiteSpace))
        {
            throw Fail(sourceName, "segments must be a non-empty array of non-blank strings");
        }
        if (string.IsNullOrWhiteSpace(dto.ReflectionText))
        {
            throw Fail(sourceName, "reflectionText is empty");
        }
        if (dto.ReflectionQuestions.Length == 0 || dto.ReflectionQuestions.Any(string.IsNullOrWhiteSpace))
        {
            throw Fail(sourceName, "reflectionQuestions must be a non-empty array of non-blank strings");
        }
        if (dto.ReflectionConclusions is not null)
        {
            if (dto.ReflectionConclusions.Length != dto.ReflectionQuestions.Length)
            {
                throw Fail(sourceName,
                    $"reflectionConclusions has {dto.ReflectionConclusions.Length} entries but reflectionQuestions has {dto.ReflectionQuestions.Length} — they must pair 1:1");
            }
            if (dto.ReflectionConclusions.Any(string.IsNullOrWhiteSpace))
            {
                throw Fail(sourceName, "reflectionConclusions entries must be non-blank");
            }
        }
        if (!AllowedStatuses.Contains(dto.Review.Status, StringComparer.Ordinal))
        {
            throw Fail(sourceName, $"review.status '{dto.Review.Status}' is not one of: {string.Join(", ", AllowedStatuses)}");
        }
        if (dto.Review.Status == "approved" && string.IsNullOrWhiteSpace(dto.Review.ListenTestAt))
        {
            throw Fail(sourceName, "approved story is missing review.listenTestAt — the TTS listen test is a mandatory acceptance step");
        }
        if (requireApproved && dto.Review.Status != "approved")
        {
            throw Fail(sourceName, $"runtime story library only loads approved stories; review.status is '{dto.Review.Status}'");
        }

        // Positional mapping: segment indexes are 0..N-1 by array order —
        // the sequential-index invariant is structural, not stored.
        var segments = dto.Segments
            .Select((text, index) => new CuratedStorySegment(index, text))
            .ToList();

        return new CuratedStory(
            dto.Id,
            dto.Title,
            dto.MinAge,
            dto.MaxAge,
            dto.Tone,
            segments,
            dto.ReflectionText,
            dto.ReflectionQuestions,
            dto.BedtimeSafe)
        {
            Author = dto.Author,
            Goal = dto.Goal,
            Lesson = dto.Lesson,
            ReflectionConclusions = dto.ReflectionConclusions,
        };
    }

    private static InvalidDataException Fail(string sourceName, string message) =>
        new($"{sourceName}: {message}");
}

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Parent-facing view of one custom-story request (slice F). No
/// <c>PhotoPath</c> on the wire — server-internal; the parent only sees
/// whether a photo rides along.
/// </summary>
public sealed record StoryRequestDto(
    Guid Id,
    string Type,
    string Text,
    bool HasPhoto,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record StoryRequestsResponse(List<StoryRequestDto> Requests);

/// <summary>Operator (internal console) view — adds the requester scope.
/// <c>ParentEmail</c> is null when the account has since been deleted or
/// anonymized (requests are FK-free and outlive accounts).</summary>
public sealed record AdminStoryRequestDto(
    Guid Id,
    Guid ParentId,
    string? ParentEmail,
    string Type,
    string Text,
    bool HasPhoto,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

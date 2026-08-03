namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Response for <c>GET /api/parents/devices/{deviceId}/reflection-answers</c>
/// — the append-only history of the child's answers to each story's
/// after-story meaning question. One row per listen, never overwritten, so a
/// parent can see how the child's understanding grows across repeat listens.
/// Newest-first, paginated; optionally filtered by story.
/// </summary>
public sealed record StoryReflectionAnswersResponse(
    List<StoryReflectionAnswerDto> Answers,
    int Total);

public sealed record StoryReflectionAnswerDto(
    string StoryId,
    int QuestionIndex,
    string AnswerText,
    string SafetyFlag,
    DateTime CreatedAtUtc);

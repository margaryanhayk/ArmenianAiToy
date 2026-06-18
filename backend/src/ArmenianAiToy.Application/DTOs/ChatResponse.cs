using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

public record ChatResponse(
    string Response,
    Guid ConversationId,
    Guid MessageId,
    SafetyFlag SafetyFlag,
    string? ChoiceA = null,
    string? ChoiceB = null,
    Guid? StorySessionId = null,
    string? Mode = null,
    // True when this was a library-story turn AND an active session
    // still remains (more segments, or the ending turn, to follow) —
    // the voice transport uses it to drive hands-free autoplay: after
    // playing this segment the device auto-requests the next without a
    // button press. False on the final/reflection turn (session
    // cleared) and on every non-library turn.
    bool LibraryAutoContinue = false);

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
    bool LibraryAutoContinue = false,
    // True when this turn closed an online Game/Riddle/Curiosity/Calm
    // conversation the firmware opened over voice (currently: a Game
    // turn whose GameSessions round is gone — the child said a stop
    // word, or no round was ever started). Purely a REPORT of state
    // ChatService already tracked for its own turn-taking; setting it
    // changes no orchestration decision. The audio transport uses it to
    // end its multi-turn voice loop honestly instead of guessing from
    // silence alone. False whenever no such closing is known (most
    // modes have none — the loop then falls back to its other
    // termination rules: silence, turn cap, a parent-gate reply, an
    // error).
    bool TurnEnded = false);

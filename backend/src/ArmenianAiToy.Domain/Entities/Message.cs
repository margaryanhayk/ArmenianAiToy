using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public SafetyFlag SafetyFlag { get; set; }
    public string? AudioBlobPath { get; set; }

    /// <summary>
    /// E1.3: the conversation mode this message was produced under
    /// ("story" / "game" / "riddle" / "curiosity" / "calm"), stamped at
    /// chat time from the runtime-resolved <c>DetectedMode</c>. Null for
    /// rows written before this column existed, for guard/fallback
    /// replies with no resolved mode, and for child (user) rows —
    /// re-deriving it later from text would diverge from the runtime
    /// resolution (see CLAUDE.md "Modes-used-today — DEFERRED to E1.3").
    /// </summary>
    public string? Mode { get; set; }
}

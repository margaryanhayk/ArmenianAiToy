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
}

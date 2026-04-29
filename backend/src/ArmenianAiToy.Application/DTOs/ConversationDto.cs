using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.DTOs;

public record ConversationDto(
    Guid Id,
    Guid DeviceId,
    DateTime StartedAt,
    DateTime? EndedAt,
    int MessageCount,
    bool HasFlaggedContent,
    List<MessageDto> Messages);

public record MessageDto(
    Guid Id,
    string Role,
    string Content,
    DateTime Timestamp,
    SafetyFlag SafetyFlag,
    // C2.1 — true ONLY when role is Assistant AND AudioBlobPath is non-null.
    // Drives the parent-dashboard ▶ Listen affordance. Child/User WAV
    // messages MUST NOT surface true here even if their AudioBlobPath is
    // populated — child voice is never replayed in C2.1. The role gate
    // lives at every projection site (ConversationService, ParentService
    // export) so the wire shape can never expose a child-WAV "playable"
    // signal regardless of where the DTO was built.
    bool AudioAvailable);

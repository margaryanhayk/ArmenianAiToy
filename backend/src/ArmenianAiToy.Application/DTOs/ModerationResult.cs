namespace ArmenianAiToy.Application.DTOs;

public record ModerationResult(bool IsSafe, List<string> FlaggedCategories);

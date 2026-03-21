namespace ArmenianAiToy.Application.DTOs;

public record ChatRequest(string Message, Guid? ChildId = null);

namespace ArmenianAiToy.Application.Interfaces;

public interface IAiChatClient
{
    Task<string> GetCompletionAsync(string systemPrompt, List<(string Role, string Content)> messages);
}

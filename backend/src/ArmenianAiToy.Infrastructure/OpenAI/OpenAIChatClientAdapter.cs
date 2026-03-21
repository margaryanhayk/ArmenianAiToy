using ArmenianAiToy.Application.Interfaces;
using OpenAI.Chat;

namespace ArmenianAiToy.Infrastructure.OpenAI;

public class OpenAIChatClientAdapter : IAiChatClient
{
    private readonly ChatClient _client;

    public OpenAIChatClientAdapter(ChatClient client)
    {
        _client = client;
    }

    public async Task<string> GetCompletionAsync(string systemPrompt, List<(string Role, string Content)> messages)
    {
        var chatMessages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt)
        };

        foreach (var (role, content) in messages)
        {
            chatMessages.Add(role switch
            {
                "user" => new UserChatMessage(content),
                "assistant" => new AssistantChatMessage(content),
                _ => new UserChatMessage(content)
            });
        }

        var completion = await _client.CompleteChatAsync(chatMessages);
        return completion.Value.Content[0].Text;
    }
}

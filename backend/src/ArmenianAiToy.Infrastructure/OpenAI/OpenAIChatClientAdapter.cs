using ArmenianAiToy.Application.Interfaces;
using OpenAI.Chat;

namespace ArmenianAiToy.Infrastructure.OpenAI;

public class OpenAIChatClientAdapter : IAiChatClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly ChatClient _client;
    private readonly OpenAIReliabilityGate _gate;

    public OpenAIChatClientAdapter(ChatClient client, OpenAIReliabilityGate gate)
    {
        _client = client;
        _gate = gate;
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

        using var cts = new CancellationTokenSource(RequestTimeout);
        // Route the SDK call through the reliability gate — classification,
        // one retry on retryable kinds, and circuit-breaker protection.
        // The gate rethrows the classified exception on final failure,
        // which ChatController's existing Path-5 catch already handles.
        var completion = await _gate.RunAsync(
            ct => _client.CompleteChatAsync(chatMessages, cancellationToken: ct),
            cts.Token);
        return completion.Value.Content[0].Text;
    }
}

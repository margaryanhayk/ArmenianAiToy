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

    public async Task<string> GetCompletionAsync(string systemPrompt, List<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default)
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

        // AI logic review 2026-10-27, C2: the gate gets the CALLER's token so it
        // can tell a toy that dropped mid-turn (not an upstream failure, never
        // counted) from our own 30 s ceiling (a real Timeout, retried and
        // counted). The ceiling is linked inside the operation, per attempt —
        // same shape as GeminiChatClientAdapter.CoreAsync.
        // Route the SDK call through the reliability gate — classification,
        // one retry on retryable kinds, and circuit-breaker protection.
        // The gate rethrows the classified exception on final failure,
        // which ChatController's existing Path-5 catch already handles.
        var completion = await _gate.RunAsync(async ct =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);
            return await _client.CompleteChatAsync(chatMessages, cancellationToken: cts.Token);
        }, cancellationToken);
        // A completion with no content parts (e.g. refusal-only) returns
        // string.Empty instead of throwing ArgumentOutOfRangeException →
        // 502; ChatService's empty-reply guard turns it into the calm,
        // Flagged fallback, and StoryAnswerFilter rejects it as Empty.
        var parts = completion.Value.Content;
        return parts.Count == 0 ? string.Empty : string.Concat(parts.Select(c => c.Text));
    }
}

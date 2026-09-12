namespace ArmenianAiToy.Application.Interfaces;

public interface IAiChatClient
{
    /// <param name="cancellationToken">Caller's cancellation (the toy's
    /// request lifetime on the chat path). Adapters hand it to the SDK/HTTP
    /// call so a disconnected toy stops paying for a reply nobody will hear.
    /// Default = never cancelled; every existing caller is unchanged.</param>
    Task<string> GetCompletionAsync(string systemPrompt, List<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default);
}

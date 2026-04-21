namespace ArmenianAiToy.Infrastructure.OpenAI;

/// <summary>
/// Thrown by <see cref="OpenAIReliabilityGate"/> when the circuit is open
/// and a call is short-circuited (fail-fast) without invoking the wrapped
/// operation. <see cref="ChatController"/>'s Path-5 catch handles this the
/// same way it handles any other OpenAI failure — sanitized 502 — so no
/// new response shape is introduced.
/// </summary>
public sealed class OpenAIReliabilityCircuitOpenException : Exception
{
    public OpenAIReliabilityCircuitOpenException(string message) : base(message) { }
}

using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

public interface IModerationService
{
    /// <param name="cancellationToken">Caller's cancellation. A check aborted
    /// by THIS token propagates <see cref="OperationCanceledException"/> to the
    /// caller — it is not a moderation outage and must not fail closed as one.
    /// The adapter's own timeout still fails closed. Default = never cancelled.</param>
    Task<ModerationResult> CheckContentAsync(string content, CancellationToken cancellationToken = default);
}

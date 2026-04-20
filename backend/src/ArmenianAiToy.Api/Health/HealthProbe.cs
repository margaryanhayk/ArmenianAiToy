using ArmenianAiToy.Infrastructure.Data;

namespace ArmenianAiToy.Api.Health;

/// <summary>
/// Lightweight dependency probes for the <c>/api/health</c> endpoint.
///
/// Scope is intentionally narrow: checks only the app's own persistence
/// layer. OpenAI is not probed on every health tick because (a) the OpenAI
/// client is validated at startup in <c>DependencyInjection.AddInfrastructure</c>
/// and <c>Program.cs</c> (missing <c>OpenAI:ApiKey</c> or <c>Jwt:Key</c>
/// cannot reach runtime), and (b) a live probe on every health tick would
/// burn OpenAI quota and produce false negatives on transient API blips —
/// quota/auth outages already surface via <c>OpenAIModerationAdapter</c>'s
/// fail-closed path.
/// </summary>
public static class HealthProbe
{
    public static async Task<bool> IsDatabaseReachableAsync(
        AppDbContext db,
        TimeSpan timeout,
        CancellationToken requestToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        cts.CancelAfter(timeout);
        try
        {
            return await db.Database.CanConnectAsync(cts.Token);
        }
        catch
        {
            return false;
        }
    }
}

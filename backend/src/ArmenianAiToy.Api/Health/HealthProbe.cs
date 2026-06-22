using ArmenianAiToy.Infrastructure.Data;

namespace ArmenianAiToy.Api.Health;

/// <summary>
/// Lightweight dependency probes for the <c>/api/health</c> endpoint.
///
/// Scope is intentionally narrow: checks only the app's own persistence
/// layer. OpenAI is not ACTIVELY probed on every health tick because (a) the
/// OpenAI client is validated at startup in
/// <c>DependencyInjection.AddInfrastructure</c> and <c>Program.cs</c>
/// (missing <c>OpenAI:ApiKey</c> or <c>Jwt:Key</c> cannot reach runtime), and
/// (b) a live probe on every health tick would burn OpenAI quota and produce
/// false negatives on transient API blips.
///
/// <para>
/// #070 — the endpoint nonetheless surfaces a NON-FATAL <c>openai</c>
/// readiness field derived from <c>OpenAIReliabilityGate.IsCircuitOpen()</c>:
/// a passive, zero-cost signal (no upstream call) that reports "degraded"
/// while the breaker is open from recent real failures. It does NOT flip the
/// HTTP liveness verdict — OpenAI is a shared downstream, so failing liveness
/// during its outage would pull every instance from the load balancer at
/// once. The verdict stays DB-only; the field is for dashboards/alerts.
/// </para>
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

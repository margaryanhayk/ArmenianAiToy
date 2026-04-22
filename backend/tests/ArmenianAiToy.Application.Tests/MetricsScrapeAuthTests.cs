using ArmenianAiToy.Api.Observability;
using Microsoft.AspNetCore.Http;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="MetricsScrapeAuth"/>. Pure-unit style against
/// <see cref="DefaultHttpContext"/>, same pattern the other
/// middleware-adjacent helpers in this repo use
/// (<see cref="AuthRateLimiterTests"/>,
/// <see cref="ChatRateLimiterTests"/>). The content-shape-unchanged
/// invariant for <c>/metrics</c> is pinned by the manual QA step in
/// CLAUDE.md § Metrics — no WebApplicationFactory / TestHost is
/// introduced, as the slice forbids new NuGet packages.
/// </summary>
public class MetricsScrapeAuthTests
{
    private const string TestToken = "scrape-token-abc123";

    private static HttpContext CtxWithHeader(string? authorization)
    {
        var ctx = new DefaultHttpContext();
        if (authorization is not null)
            ctx.Request.Headers["Authorization"] = authorization;
        return ctx;
    }

    // ────────────────────────────────────────────────────────────
    // Shipped-default invariant: fresh deploy with no token and no
    // bypass is fail-closed on every request.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_DefaultShippedState_DeniesAllRequests()
    {
        // No token configured, bypass off — the invariant is
        // "protected by default."
        var ctx = CtxWithHeader("Bearer anything");
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: null, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    [Fact]
    public void Evaluate_NoTokenNoBypass_EmptyStringToken_DeniesAllRequests()
    {
        // Also fail-closed when the token is the literal empty string —
        // matches the appsettings.json default of "".
        var ctx = CtxWithHeader("Bearer anything");
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: string.Empty, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    // ────────────────────────────────────────────────────────────
    // Token-path branches.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CorrectBearerToken_Allows()
    {
        var ctx = CtxWithHeader("Bearer " + TestToken);
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Allow, decision);
    }

    [Fact]
    public void Evaluate_MissingAuthorizationHeader_Denies()
    {
        var ctx = CtxWithHeader(null);
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    [Fact]
    public void Evaluate_WrongBearerToken_Denies()
    {
        var ctx = CtxWithHeader("Bearer wrong-token-value");
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    [Fact]
    public void Evaluate_NonBearerScheme_Denies()
    {
        // Basic auth, API-Key header, etc. — the guard accepts ONLY the
        // Bearer scheme. Anything else is rejected even if it embeds the
        // correct token string.
        var ctx = CtxWithHeader("Basic " + TestToken);
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("bearer ")]
    [InlineData("BEARER ")]
    [InlineData("BeArEr ")]
    public void Evaluate_BearerSchemeIsCaseInsensitive(string prefix)
    {
        // RFC 7235 §2.1 — auth scheme names are case-insensitive.
        // "Bearer", "bearer", "BEARER", mixed-case all route to the
        // same compare. The token bytes AFTER the prefix still have to
        // match byte-for-byte (pinned by the wrong-token / length-
        // mismatch tests above), so case-insensitivity on the scheme
        // does not weaken the token check itself.
        var ctx = CtxWithHeader(prefix + TestToken);
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Allow, decision);
    }

    [Fact]
    public void Evaluate_TokenLengthMismatch_Denies()
    {
        // Exercises the length-first short-circuit inside the
        // constant-time compare. Not a security assertion — it just
        // pins behaviour on an obviously-wrong shorter token.
        var ctx = CtxWithHeader("Bearer short");
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    [Fact]
    public void Evaluate_EmptyBearerToken_Denies()
    {
        var ctx = CtxWithHeader("Bearer ");
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: false);
        Assert.Equal(MetricsScrapeAuth.Decision.Deny, decision);
    }

    // ────────────────────────────────────────────────────────────
    // Explicit bypass — the dev/local escape hatch.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_BypassEnabled_AllowsWithoutAnyToken()
    {
        var ctx = CtxWithHeader(null);
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: null, allowUnauthenticated: true);
        Assert.Equal(MetricsScrapeAuth.Decision.Allow, decision);
    }

    [Fact]
    public void Evaluate_BypassEnabled_AllowsEvenWithWrongToken()
    {
        // Pins the semantic: bypass means "don't require auth at all."
        // A wrong token is not a rejection signal when the flag is on.
        var ctx = CtxWithHeader("Bearer wrong-value");
        var decision = MetricsScrapeAuth.Evaluate(
            ctx, configuredToken: TestToken, allowUnauthenticated: true);
        Assert.Equal(MetricsScrapeAuth.Decision.Allow, decision);
    }

    // ────────────────────────────────────────────────────────────
    // Path constant — pins the endpoint this guard covers.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void MetricsPath_IsExactlySlashMetrics()
    {
        // Guards against a future edit that drifts the path away from
        // the OTel Prometheus exporter's default mapping.
        Assert.Equal("/metrics", MetricsScrapeAuth.MetricsPath);
    }
}

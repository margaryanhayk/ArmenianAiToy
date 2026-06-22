using ArmenianAiToy.Api.Observability;
using Microsoft.AspNetCore.Http;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="InternalAdminAuth"/> — the fail-closed bearer gate
/// for the superuser console API (<c>/api/internal/*</c>). Same pure-unit
/// style as <see cref="MetricsScrapeAuthTests"/>; the security boundary of
/// the whole-system god-view is this function, so it is pinned directly.
/// </summary>
public class InternalAdminAuthTests
{
    private const string TestToken = "admin-token-xyz789";

    private static HttpContext Ctx(string? authorization)
    {
        var ctx = new DefaultHttpContext();
        if (authorization is not null)
            ctx.Request.Headers["Authorization"] = authorization;
        return ctx;
    }

    [Fact]
    public void DefaultShippedState_DeniesEverything()
    {
        // No token configured, bypass off → fail closed (the appsettings default).
        Assert.Equal(InternalAdminAuth.Decision.Deny,
            InternalAdminAuth.Evaluate(Ctx("Bearer anything"), null, false));
    }

    [Fact]
    public void EmptyStringToken_NoBypass_Denies()
    {
        Assert.Equal(InternalAdminAuth.Decision.Deny,
            InternalAdminAuth.Evaluate(Ctx("Bearer anything"), string.Empty, false));
    }

    [Fact]
    public void CorrectBearerToken_Allows()
    {
        Assert.Equal(InternalAdminAuth.Decision.Allow,
            InternalAdminAuth.Evaluate(Ctx("Bearer " + TestToken), TestToken, false));
    }

    [Fact]
    public void WrongToken_Denies()
    {
        Assert.Equal(InternalAdminAuth.Decision.Deny,
            InternalAdminAuth.Evaluate(Ctx("Bearer nope"), TestToken, false));
    }

    [Fact]
    public void MissingHeader_Denies()
    {
        Assert.Equal(InternalAdminAuth.Decision.Deny,
            InternalAdminAuth.Evaluate(Ctx(null), TestToken, false));
    }

    [Fact]
    public void NonBearerScheme_Denies()
    {
        Assert.Equal(InternalAdminAuth.Decision.Deny,
            InternalAdminAuth.Evaluate(Ctx("Basic " + TestToken), TestToken, false));
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("bearer ")]
    [InlineData("BEARER ")]
    public void BearerSchemeCaseInsensitive_TokenStillChecked(string prefix)
    {
        Assert.Equal(InternalAdminAuth.Decision.Allow,
            InternalAdminAuth.Evaluate(Ctx(prefix + TestToken), TestToken, false));
    }

    [Fact]
    public void BypassEnabled_AllowsWithoutToken()
    {
        Assert.Equal(InternalAdminAuth.Decision.Allow,
            InternalAdminAuth.Evaluate(Ctx(null), null, true));
    }

    [Fact]
    public void BypassEnabled_AllowsEvenWithWrongToken()
    {
        Assert.Equal(InternalAdminAuth.Decision.Allow,
            InternalAdminAuth.Evaluate(Ctx("Bearer wrong"), TestToken, true));
    }

    // ── #012: per-operator identity resolution ──────────────────────

    private static InternalAdminAuth.OperatorCredential[] Ops() =>
    [
        new("alice", "tok-alice"),
        new("bob", "tok-bob"),
    ];

    [Fact]
    public void ResolveOperator_NamedTokenMatch_ReturnsThatOperatorName()
        => Assert.Equal("bob",
            InternalAdminAuth.ResolveOperatorName(Ctx("Bearer tok-bob"), Ops(), legacyToken: null, allowUnauthenticated: false));

    [Fact]
    public void ResolveOperator_LegacyToken_ReturnsSharedLabel()
        => Assert.Equal("admin (shared token)",
            InternalAdminAuth.ResolveOperatorName(Ctx("Bearer legacy"), Ops(), legacyToken: "legacy", allowUnauthenticated: false));

    [Fact]
    public void ResolveOperator_WrongToken_ReturnsNull()
        => Assert.Null(
            InternalAdminAuth.ResolveOperatorName(Ctx("Bearer nope"), Ops(), legacyToken: "legacy", allowUnauthenticated: false));

    [Fact]
    public void ResolveOperator_NoCredsNoBypass_ReturnsNull()
        => Assert.Null(
            InternalAdminAuth.ResolveOperatorName(Ctx("Bearer anything"), [], legacyToken: null, allowUnauthenticated: false));

    [Fact]
    public void ResolveOperator_Bypass_ReturnsDevSentinel()
        => Assert.Equal("dev-bypass",
            InternalAdminAuth.ResolveOperatorName(Ctx(null), [], legacyToken: null, allowUnauthenticated: true));

    [Theory]
    [InlineData("/api/internal", true)]
    [InlineData("/api/internal/overview", true)]
    [InlineData("/API/Internal/Devices", true)]   // case-insensitive
    [InlineData("/api/internalx", false)]          // not a segment boundary
    [InlineData("/api/conversations", false)]
    [InlineData("/metrics", false)]
    public void IsInternalPath_MatchesPrefixOnSegmentBoundary(string path, bool expected)
    {
        Assert.Equal(expected, InternalAdminAuth.IsInternalPath(new PathString(path)));
    }
}

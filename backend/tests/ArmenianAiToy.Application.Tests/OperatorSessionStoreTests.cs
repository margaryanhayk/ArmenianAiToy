using ArmenianAiToy.Application.Auth;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Time-boxed operator console sessions: a token resolves to its operator until
/// it expires; expired/unknown/revoked tokens resolve to null (deny).
/// </summary>
public class OperatorSessionStoreTests
{
    [Fact]
    public void IssueThenResolve_RoundTripsOperator()
    {
        var s = new OperatorSessionStore();
        var now = DateTime.UtcNow;
        var token = s.Issue("alice-ops", TimeSpan.FromMinutes(15), now);
        Assert.Equal("alice-ops", s.Resolve(token, now.AddMinutes(5)));
    }

    [Fact]
    public void Resolve_AfterExpiry_ReturnsNull()
    {
        var s = new OperatorSessionStore();
        var now = DateTime.UtcNow;
        var token = s.Issue("alice-ops", TimeSpan.FromMinutes(15), now);
        Assert.Null(s.Resolve(token, now.AddMinutes(16)));
    }

    [Fact]
    public void Resolve_UnknownToken_ReturnsNull()
    {
        Assert.Null(new OperatorSessionStore().Resolve("deadbeef"));
        Assert.Null(new OperatorSessionStore().Resolve(""));
    }

    [Fact]
    public void Revoke_EndsSessionImmediately()
    {
        var s = new OperatorSessionStore();
        var token = s.Issue("bob-ops", TimeSpan.FromHours(1));
        s.Revoke(token);
        Assert.Null(s.Resolve(token));
    }

    [Fact]
    public void IssuedTokens_AreUnique()
    {
        var s = new OperatorSessionStore();
        var a = s.Issue("x", TimeSpan.FromMinutes(5));
        var b = s.Issue("x", TimeSpan.FromMinutes(5));
        Assert.NotEqual(a, b);
    }
}

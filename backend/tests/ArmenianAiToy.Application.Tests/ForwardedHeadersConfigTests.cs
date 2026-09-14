using System.Net;
using ArmenianAiToy.Api.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #039 — pins the opt-in, fail-safe-off proxy-aware client-IP config.
/// Disabled or trust-nothing => null (XFF not processed; limiters unchanged).
/// Enabled with real proxies => trust ONLY those, loopback defaults cleared.
/// </summary>
public class ForwardedHeadersConfigTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void Disabled_ReturnsNull()
    {
        Assert.Null(ForwardedHeadersConfig.TryBuild(Config(("ForwardedHeaders:Enabled", "false"))));
        Assert.Null(ForwardedHeadersConfig.TryBuild(Config())); // missing => disabled
    }

    [Fact]
    public void Enabled_ButNoProxies_ReturnsNull()
    {
        // Refuses to trust all upstreams — would let any client spoof XFF.
        Assert.Null(ForwardedHeadersConfig.TryBuild(Config(("ForwardedHeaders:Enabled", "true"))));
    }

    [Fact]
    public void Enabled_AllInvalidProxies_ReturnsNull()
    {
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownProxies:0", "not-an-ip"),
            ("ForwardedHeaders:KnownProxies:1", ""));
        Assert.Null(ForwardedHeadersConfig.TryBuild(cfg));
    }

    [Fact]
    public void Enabled_WithKnownNetwork_TrustsThatCidr()
    {
        // Managed hosts (Railway/Fly/Render) have no single stable proxy IP to
        // pin, so KnownProxies alone cannot be configured there and the per-IP
        // auth limiters never trip. A CIDR for the platform's internal proxy
        // network is the pinnable unit.
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"));

        var opts = ForwardedHeadersConfig.TryBuild(cfg);

        Assert.NotNull(opts);
        Assert.Empty(opts!.KnownProxies);
        Assert.Single(opts.KnownIPNetworks);
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("10.0.0.0")) && n.PrefixLength == 8);
    }

    [Fact]
    public void Enabled_KnownNetworks_AcceptsCommaSeparatedScalar()
    {
        // These are set as env vars in a hosting dashboard, often from a
        // phone. One variable with a comma-separated list beats five indexed
        // ones, where a typo'd index silently yields an empty list that is
        // indistinguishable from "not configured".
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownNetworks", "10.0.0.0/8, 172.16.0.0/12 ;192.168.0.0/16"));

        var opts = ForwardedHeadersConfig.TryBuild(cfg);

        Assert.NotNull(opts);
        Assert.Equal(3, opts!.KnownIPNetworks.Count);
    }

    [Fact]
    public void Enabled_KnownProxies_AcceptsCommaSeparatedScalar()
    {
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownProxies", "10.0.0.5,192.168.1.9"));

        var opts = ForwardedHeadersConfig.TryBuild(cfg);

        Assert.NotNull(opts);
        Assert.Equal(2, opts!.KnownProxies.Count);
    }

    [Fact]
    public void Enabled_MalformedNetworks_AreDropped_NotWidened()
    {
        // A bad CIDR must never silently become "trust everything".
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownNetworks:0", "not-a-network"),
            ("ForwardedHeaders:KnownNetworks:1", "10.0.0.0/999"),
            ("ForwardedHeaders:KnownNetworks:2", "10.0.0.0/"),
            ("ForwardedHeaders:KnownNetworks:3", "10.0.0.0"));

        Assert.Null(ForwardedHeadersConfig.TryBuild(cfg));
    }

    [Fact]
    public void Enabled_ProxiesAndNetworks_BothHonored()
    {
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownProxies:0", "192.168.1.9"),
            ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"));

        var opts = ForwardedHeadersConfig.TryBuild(cfg);

        Assert.NotNull(opts);
        Assert.Single(opts!.KnownProxies);
        Assert.Single(opts.KnownIPNetworks);
    }

    [Fact]
    public void Enabled_WithValidProxies_TrustsOnlyThem_ClearsDefaults()
    {
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownProxies:0", "10.0.0.5"),
            ("ForwardedHeaders:KnownProxies:1", "bad"),     // dropped
            ("ForwardedHeaders:KnownProxies:2", "192.168.1.9"));

        var opts = ForwardedHeadersConfig.TryBuild(cfg);

        Assert.NotNull(opts);
        Assert.True(opts!.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.Equal(1, opts.ForwardLimit);
        Assert.Empty(opts.KnownIPNetworks);                   // loopback defaults cleared
        Assert.Equal(2, opts.KnownProxies.Count);             // only the two valid IPs
        Assert.Contains(IPAddress.Parse("10.0.0.5"), opts.KnownProxies);
        Assert.Contains(IPAddress.Parse("192.168.1.9"), opts.KnownProxies);
    }

    [Fact]
    public void ForwardLimit_Override_Respected()
    {
        var cfg = Config(
            ("ForwardedHeaders:Enabled", "true"),
            ("ForwardedHeaders:KnownProxies:0", "10.0.0.5"),
            ("ForwardedHeaders:ForwardLimit", "2"));

        var opts = ForwardedHeadersConfig.TryBuild(cfg);
        Assert.Equal(2, opts!.ForwardLimit);
    }

    /// <summary>
    /// Mirror of the enabled-but-untrusted warning, for the opposite gap:
    /// disabled outright outside Development, where AuthRateLimiter would key
    /// every parent behind a reverse proxy into one shared bucket.
    /// </summary>
    [Theory]
    [InlineData(false, false, true)]  // non-Dev, disabled => warn
    [InlineData(false, true, false)]  // non-Dev, enabled => no warn (other branch handles it)
    [InlineData(true, false, false)]  // Development, disabled => no warn (no proxy in the loop)
    [InlineData(true, true, false)]   // Development, enabled => no warn
    public void ShouldWarnDisabledOutsideDevelopment_MatchesExpectation(
        bool isDevelopment, bool enabled, bool expectedWarn)
    {
        Assert.Equal(
            expectedWarn,
            ForwardedHeadersConfig.ShouldWarnDisabledOutsideDevelopment(isDevelopment, enabled));
    }

    /// <summary>
    /// N12 — a Railway operator who sets ONLY <c>ForwardedHeaders__Enabled=true</c>
    /// (no KnownProxies/KnownNetworks of their own) must get a working config,
    /// not the "enabled but nothing trustworthy listed" null. Loads the REAL
    /// shipped appsettings.json rather than reconstructing the default inline,
    /// so a future edit to the shipped value is caught here.
    /// </summary>
    [Fact]
    public void Enabled_WithOnlyShippedAppsettingsDefaults_TrustsFivePrivateNetworks()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(ShippedAppsettingsPath())
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("ForwardedHeaders:Enabled", "true"),
            })
            .Build();

        var opts = ForwardedHeadersConfig.TryBuild(config);

        Assert.NotNull(opts);
        Assert.Empty(opts!.KnownProxies); // shipped default carries no KnownProxies
        Assert.Equal(5, opts.KnownIPNetworks.Count); // 4 private/CGNAT IPv4 + 1 ULA IPv6
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("10.0.0.0")) && n.PrefixLength == 8);
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("172.16.0.0")) && n.PrefixLength == 12);
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("192.168.0.0")) && n.PrefixLength == 16);
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("100.64.0.0")) && n.PrefixLength == 10); // CGNAT
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("fc00::")) && n.PrefixLength == 7); // IPv6 ULA
    }

    /// <summary>
    /// N12 — the shipped default MUST stay a comma-separated scalar, not a JSON
    /// array. Environment variables layer AFTER appsettings.json with the exact
    /// same flat key (<c>ForwardedHeaders:KnownNetworks</c>) and no indices; if
    /// the shipped default were an array it would occupy the indexed child keys
    /// (:0, :1, ...) that an env-var scalar override does not clear, so the two
    /// would MERGE instead of the operator's value replacing the shipped one.
    /// This test loads the real appsettings.json and layers an operator
    /// override on top the same way ASP.NET's environment-variable provider
    /// would, and pins that only the override survives.
    /// </summary>
    [Fact]
    public void Enabled_OperatorKnownNetworksOverride_ReplacesShippedDefault_DoesNotMerge()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(ShippedAppsettingsPath())
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("ForwardedHeaders:Enabled", "true"),
                new KeyValuePair<string, string?>("ForwardedHeaders:KnownNetworks", "203.0.113.0/24"),
            })
            .Build();

        var opts = ForwardedHeadersConfig.TryBuild(config);

        Assert.NotNull(opts);
        Assert.Single(opts!.KnownIPNetworks); // the operator's CIDR only — the 5 shipped defaults are gone
        Assert.Contains(opts.KnownIPNetworks,
            n => n.BaseAddress.Equals(IPAddress.Parse("203.0.113.0")) && n.PrefixLength == 24);
    }

    private static string ShippedAppsettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        var repoRoot = dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        return Path.Combine(repoRoot, "backend", "src", "ArmenianAiToy.Api", "appsettings.json");
    }
}

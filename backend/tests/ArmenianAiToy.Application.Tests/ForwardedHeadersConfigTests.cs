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
}

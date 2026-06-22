using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// #039 — opt-in proxy-aware client-IP resolution for the rate limiters.
///
/// All rate limiters key on <c>Connection.RemoteIpAddress</c>. Behind a
/// TLS-terminating reverse proxy that IS the proxy's IP, so without
/// forwarded-header processing every client would share one bucket
/// (useless limits / self-DoS). The fix is NOT to read
/// <c>X-Forwarded-For</c> in the limiters (attacker-controlled) but to let
/// the <c>ForwardedHeaders</c> middleware rewrite <c>RemoteIpAddress</c>
/// from XFF — trusting ONLY explicitly-listed proxies — so the limiter code
/// is unchanged and automatically keys on the real client IP.
///
/// <para>
/// <b>Shipped default is OFF.</b> With <c>ForwardedHeaders:Enabled</c> false
/// (the <c>appsettings.json</c> default) this returns null and the pipeline
/// does NOT process XFF — <c>RemoteIpAddress</c> stays the direct TCP peer
/// and the existing rate-limit contract is unchanged. An operator running
/// behind a proxy sets <c>Enabled=true</c> and lists the proxy IP(s) in
/// <c>ForwardedHeaders:KnownProxies</c>.
/// </para>
///
/// <para>
/// <b>Refuses to trust everything.</b> If enabled but no valid proxy is
/// listed, this returns null (stays off) rather than honoring XFF from any
/// upstream — otherwise any client could spoof <c>X-Forwarded-For</c> to
/// forge its IP and evade / poison the per-IP limiters. The loopback
/// defaults are cleared so the trusted set is exactly what the operator
/// configured. Pure function over <see cref="IConfiguration"/> — unit
/// testable; <c>Program.cs</c> calls <c>UseForwardedHeaders</c> with the
/// result when non-null.
/// </para>
/// </summary>
public static class ForwardedHeadersConfig
{
    public static ForwardedHeadersOptions? TryBuild(IConfiguration config)
    {
        var section = config.GetSection("ForwardedHeaders");
        if (!section.GetValue<bool>("Enabled"))
            return null;

        var proxies = (section.GetSection("KnownProxies").Get<string[]>() ?? Array.Empty<string>())
            .Select(p => IPAddress.TryParse(p?.Trim(), out var ip) ? ip : null)
            .Where(ip => ip is not null)
            .Cast<IPAddress>()
            .ToList();

        // Enabled but nothing trustworthy listed => DO NOT process XFF.
        if (proxies.Count == 0)
            return null;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = section.GetValue<int?>("ForwardLimit") ?? 1,
        };
        // Trust ONLY the explicitly listed proxies — clear loopback defaults so
        // the trusted set is fully operator-controlled and predictable.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var ip in proxies)
            options.KnownProxies.Add(ip);
        return options;
    }
}

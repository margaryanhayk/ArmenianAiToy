namespace ArmenianAiToy.Api.Security;

/// <summary>
/// #061 — resolves the effective ASP.NET Core Host-filtering allow-list.
/// The shipped default (`AllowedHosts: "*"`) accepts any Host header, which
/// leaves the app open to Host-header injection / cache-poisoning. This
/// helper mirrors the #037 CORS posture:
///
/// <list type="bullet">
/// <item><b>Development</b> → permissive (<c>*</c>), so a bench/dev host
/// reached by IP or an arbitrary name keeps working.</item>
/// <item><b>Any other environment</b> → restrict to the hostnames explicitly
/// listed in <c>AllowedHosts</c> (semicolon-separated). A bare <c>*</c> entry
/// is stripped — pinning real names is the whole point.</item>
/// </list>
///
/// When a non-Development environment has NO real hostnames pinned (unset,
/// empty, or just <c>*</c>), this STAYS permissive but returns a loud
/// operator <see cref="Result.Warning"/>. Failing closed (rejecting every request on
/// a forgotten config key) would be a worse outage than a permissive filter;
/// the warning makes the gap visible without taking the service down. Pure
/// function — no config/host access — so it is unit-testable in isolation;
/// <c>Program.cs</c> calls it and applies the result to
/// <c>HostFilteringOptions</c> + logs the warning.
/// </summary>
public static class HostFilteringConfig
{
    public sealed record Result(string[] Hosts, string? Warning);

    public const string UnpinnedWarning =
        "AllowedHosts is unset or \"*\" in a non-Development environment; " +
        "Host-header filtering is effectively OFF. Pin real hostnames in " +
        "AllowedHosts (semicolon-separated, e.g. \"api.example.com;example.com\") " +
        "to close the Host-header injection / cache-poisoning gap (#061).";

    public static Result Resolve(bool isDevelopment, string? allowedHostsRaw)
    {
        if (isDevelopment)
            return new Result(["*"], null);

        var configured = (allowedHostsRaw ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(h => h != "*")
            .ToArray();

        return configured.Length > 0
            ? new Result(configured, null)
            : new Result(["*"], UnpinnedWarning);
    }
}

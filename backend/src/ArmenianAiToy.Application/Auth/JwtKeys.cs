using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Auth;

/// <summary>
/// Shared JWT key-rotation helper. Resolves the ordered list of active
/// signing/validation keys from <see cref="IConfiguration"/>, preserving
/// the repo's original single-key contract as a fallback while enabling
/// a primary+accepted-previous rotation story.
///
/// <para><b>Config shape.</b> Preferred:</para>
/// <code>
/// "Jwt": {
///   "Keys": [ "<primary>", "<previous-1>", "<previous-2>" ],
///   "Issuer":   "ArmenianAiToy",
///   "Audience": "ArmenianAiToy"
/// }
/// </code>
/// <para>
/// Legacy (still supported as a single-element fallback):
/// </para>
/// <code>
/// "Jwt": { "Key": "<primary>", "Issuer": "...", "Audience": "..." }
/// </code>
/// <para>
/// When both are present, <c>Jwt:Keys</c> wins and <c>Jwt:Key</c> is
/// ignored — same spirit as the existing rate-limiter helpers. If
/// neither is configured (or every entry is empty/whitespace) the
/// helper throws — same fail-fast contract the repo has always had on
/// this config.
/// </para>
///
/// <para><b>Signing vs validation.</b></para>
/// <list type="bullet">
///   <item><description><see cref="PrimaryKey"/> returns the first
///   element of <see cref="ResolveOrderedKeys"/> — this is the ONLY
///   key used to sign new tokens (<see cref="Services.ParentService"/>
///   uses it in <c>GenerateJwt</c>).</description></item>
///   <item><description>The full list is used to accept incoming
///   tokens during validation, so a token still signed by a previous
///   key keeps working for its lifetime during rotation.</description></item>
/// </list>
///
/// <para><b>Legacy-insecure-default guard.</b> The literal
/// <see cref="LegacyInsecureDefault"/> was shipped in earlier
/// <c>appsettings.json</c> history and is publicly known. It is
/// rejected whether it appears in <c>Jwt:Key</c>, in
/// <c>Jwt:Keys[0]</c>, or in any <c>Jwt:Keys[n]</c> — one bad entry
/// poisons the whole set, so a paste-from-history cannot creep back in
/// via rotation.
/// </para>
/// </summary>
public static class JwtKeys
{
    public const string LegacyInsecureDefault =
        "ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!";

    private const string ConfigurationHelp =
        "Set it via user-secrets (dotnet user-secrets set \"Jwt:Key\" ...) " +
        "or the JWT__KEY environment variable, or via the Jwt:Keys array.";

    /// <summary>
    /// Returns the ordered list of active JWT keys. The first element is
    /// always the primary (signing) key; additional elements are
    /// accepted at validation only.
    ///
    /// <para>Rules:</para>
    /// <list type="bullet">
    ///   <item><description>If <c>Jwt:Keys</c> has any non-empty
    ///   entries, those define the set (in config order). Empty/
    ///   whitespace entries are filtered out before the length
    ///   check.</description></item>
    ///   <item><description>Otherwise, the scalar <c>Jwt:Key</c> is
    ///   used as a single-element list — backward-compatible with
    ///   every deployment shipped before this slice.</description></item>
    ///   <item><description>Empty resulting set => throw.</description></item>
    ///   <item><description>Any entry equal to
    ///   <see cref="LegacyInsecureDefault"/> => throw.</description></item>
    /// </list>
    /// </summary>
    public static List<string> ResolveOrderedKeys(IConfiguration config)
    {
        // GetSection(...).GetChildren() is in Microsoft.Extensions.Configuration.Abstractions,
        // which this project already references — no new NuGet.
        var fromArray = config.GetSection("Jwt:Keys").GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        List<string> keys;
        if (fromArray.Count > 0)
        {
            keys = fromArray;
        }
        else
        {
            var scalar = config["Jwt:Key"];
            keys = string.IsNullOrWhiteSpace(scalar)
                ? new List<string>()
                : new List<string> { scalar! };
        }

        if (keys.Count == 0)
            throw new InvalidOperationException(
                "Jwt signing key not configured: set Jwt:Keys[0] or the " +
                "legacy scalar Jwt:Key. " + ConfigurationHelp);

        // Reject the publicly-known legacy default anywhere in the set.
        // A single poisoned entry fails the whole config so a paste from
        // history cannot sneak back in as a "previous key."
        foreach (var key in keys)
        {
            if (key == LegacyInsecureDefault)
                throw new InvalidOperationException(
                    "Jwt signing key must not be the legacy default. " +
                    ConfigurationHelp);
        }

        return keys;
    }

    /// <summary>
    /// The single key used to sign new tokens. Always the first
    /// element of <see cref="ResolveOrderedKeys"/>; the helper throws
    /// before it could return a previous key here.
    /// </summary>
    public static string PrimaryKey(IReadOnlyList<string> resolvedKeys)
        => resolvedKeys[0];
}

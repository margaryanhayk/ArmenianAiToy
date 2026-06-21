using System.Security.Cryptography;
using System.Text;

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// Access gate for device registration (<c>POST /api/devices/register</c>) —
/// #009. Registration mints a working device credential that can drive the paid
/// STT+GPT+TTS endpoints, so anonymous registration is a denial-of-wallet +
/// device-impersonation vector. Same fail-closed-with-explicit-bypass posture
/// as the /metrics, /api/internal, and story-audio guards.
///
/// <para>
/// <b>Shipped default is fail-closed.</b> With <c>Devices:ProvisioningSecret</c>
/// empty AND <c>Devices:AllowOpenRegistration</c> false (the appsettings
/// defaults), registration is DENIED. A factory/operator enables it by sending
/// the secret in the <c>X-Provisioning-Secret</c> header; a dev/bench host opts
/// into open registration with the explicit bypass flag. The bypass is its own
/// flag, not tied to the environment, so a forgotten dev shortcut cannot quietly
/// open registration in prod.
/// </para>
///
/// <para>Constant-time secret compare via
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
/// Reads only the explicit header — never <c>X-Forwarded-*</c> (the repo does
/// not wire <c>ForwardedHeaders</c>).</para>
/// </summary>
public static class DeviceProvisioningAuth
{
    public const string SecretHeader = "X-Provisioning-Secret";

    public enum Decision { Allow, Deny }

    /// <summary>Pure decision function. <paramref name="presentedSecret"/> is the
    /// caller's <c>X-Provisioning-Secret</c> header (null/empty if absent).</summary>
    public static Decision Evaluate(
        string? presentedSecret,
        string? configuredSecret,
        bool allowOpenRegistration)
    {
        if (!string.IsNullOrEmpty(configuredSecret))
        {
            return ConstantTimeEquals(presentedSecret, configuredSecret)
                ? Decision.Allow
                : Decision.Deny;
        }
        // No secret configured -> fail-closed, unless the explicit dev/bench
        // bypass is on. This is the shipped default and is what makes a fresh
        // deploy refuse anonymous device-minting without extra config.
        return allowOpenRegistration ? Decision.Allow : Decision.Deny;
    }

    private static bool ConstantTimeEquals(string? a, string b)
    {
        if (a is null) return false;
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}

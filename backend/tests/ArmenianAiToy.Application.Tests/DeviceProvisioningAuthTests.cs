using ArmenianAiToy.Api.Security;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #009 — the device-registration provisioning gate. Fail-closed by default,
/// with an explicit dev/bench bypass; same posture as the /metrics, /internal,
/// and story-audio guards. Pure decision function, pinned directly.
/// </summary>
public class DeviceProvisioningAuthTests
{
    private const string Secret = "prov-secret-abc123";

    [Fact]
    public void DefaultShipped_NoSecret_NoBypass_Denies()
        => Assert.Equal(DeviceProvisioningAuth.Decision.Deny,
            DeviceProvisioningAuth.Evaluate("anything", configuredSecret: null, allowOpenRegistration: false));

    [Fact]
    public void NoSecret_BypassOn_Allows()
        => Assert.Equal(DeviceProvisioningAuth.Decision.Allow,
            DeviceProvisioningAuth.Evaluate(presentedSecret: null, configuredSecret: null, allowOpenRegistration: true));

    [Fact]
    public void SecretSet_Match_Allows()
        => Assert.Equal(DeviceProvisioningAuth.Decision.Allow,
            DeviceProvisioningAuth.Evaluate(Secret, Secret, allowOpenRegistration: false));

    [Fact]
    public void SecretSet_Mismatch_Denies()
        => Assert.Equal(DeviceProvisioningAuth.Decision.Deny,
            DeviceProvisioningAuth.Evaluate("wrong", Secret, allowOpenRegistration: false));

    [Fact]
    public void SecretSet_Missing_Denies()
        => Assert.Equal(DeviceProvisioningAuth.Decision.Deny,
            DeviceProvisioningAuth.Evaluate(presentedSecret: null, Secret, allowOpenRegistration: false));

    [Fact]
    public void SecretSet_TakesPrecedenceOverBypass()
        // When a secret is configured, a wrong secret denies even if the bypass
        // flag is on — the secret path wins.
        => Assert.Equal(DeviceProvisioningAuth.Decision.Deny,
            DeviceProvisioningAuth.Evaluate("wrong", Secret, allowOpenRegistration: true));
}

using ArmenianAiToy.Api.Security;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #007/#008 — pins the app-side HTTPS hardening resolution: OFF by default
/// (opt-in deploy switch), and when enabled it carries a sane, floor-clamped
/// HSTS max-age.
/// </summary>
public class HttpsHardeningConfigTests
{
    [Fact]
    public void Disabled_ByDefault_WhenFlagFalse()
    {
        var r = HttpsHardeningConfig.Resolve(requireHttps: false, hstsMaxAgeDays: null);
        Assert.False(r.Enabled);
    }

    [Fact]
    public void Disabled_IgnoresMaxAgeOverride()
    {
        // Even with a max-age set, a false flag keeps it OFF — the flag is the
        // single source of truth for whether redirect/HSTS run.
        var r = HttpsHardeningConfig.Resolve(requireHttps: false, hstsMaxAgeDays: 30);
        Assert.False(r.Enabled);
    }

    [Fact]
    public void Enabled_UsesDefaultMaxAge_WhenUnset()
    {
        var r = HttpsHardeningConfig.Resolve(requireHttps: true, hstsMaxAgeDays: null);
        Assert.True(r.Enabled);
        Assert.Equal(HttpsHardeningConfig.DefaultHstsMaxAgeDays, r.HstsMaxAgeDays);
    }

    [Fact]
    public void Enabled_UsesOverride_WhenProvided()
    {
        var r = HttpsHardeningConfig.Resolve(requireHttps: true, hstsMaxAgeDays: 90);
        Assert.True(r.Enabled);
        Assert.Equal(90, r.HstsMaxAgeDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Enabled_ClampsNonPositiveMaxAge_ToFloorOfOne(int raw)
    {
        var r = HttpsHardeningConfig.Resolve(requireHttps: true, hstsMaxAgeDays: raw);
        Assert.True(r.Enabled);
        Assert.Equal(1, r.HstsMaxAgeDays);
    }
}

using ArmenianAiToy.Api.Security;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #061 — pins the Host-filtering allow-list resolution: permissive in
/// Development, pinned otherwise, and a loud-but-permissive warning when a
/// non-Development environment is left unpinned.
/// </summary>
public class HostFilteringConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("api.example.com;example.com")]
    public void Development_AlwaysPermissive_NoWarning(string? raw)
    {
        var r = HostFilteringConfig.Resolve(isDevelopment: true, raw);
        Assert.Equal(new[] { "*" }, r.Hosts);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void NonDev_RealHostsPinned_UsesThem_NoWarning()
    {
        var r = HostFilteringConfig.Resolve(isDevelopment: false, "api.example.com;example.com");
        Assert.Equal(new[] { "api.example.com", "example.com" }, r.Hosts);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void NonDev_TrimsWhitespace_DropsEmptiesAndBareStar()
    {
        var r = HostFilteringConfig.Resolve(isDevelopment: false, "  api.example.com ; ; *  ");
        Assert.Equal(new[] { "api.example.com" }, r.Hosts);
        Assert.Null(r.Warning); // a real host survived, so no warning
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("  *  ")]
    [InlineData(" ; ; ")]
    public void NonDev_Unpinned_StaysPermissive_ButWarns(string? raw)
    {
        var r = HostFilteringConfig.Resolve(isDevelopment: false, raw);
        Assert.Equal(new[] { "*" }, r.Hosts); // permissive — never a total outage
        Assert.Equal(HostFilteringConfig.UnpinnedWarning, r.Warning);
    }
}

using ArmenianAiToy.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="NotifierTransport.ResolveImplementation"/> — the
/// config-driven selector that picks between <see cref="LoggingNotifier"/>
/// and <see cref="SmtpNotifier"/>. Pure-unit style against an NSubstitute
/// <c>IConfiguration</c>, same pattern <c>JwtKeysTests</c> uses for
/// signing-key resolution.
///
/// <para>
/// Pins the bounded-value-space invariant: the selector must only ever
/// return <see cref="LoggingNotifier"/> or <see cref="SmtpNotifier"/>,
/// and an unknown <c>Notifications:Transport</c> value must fail fast
/// rather than silently resolving to log. A regression that added a
/// third implementation without extending the selector would either
/// break one of these tests or require adding a new case explicitly.
/// </para>
/// </summary>
public class NotifierTransportTests
{
    private static IConfiguration Config(
        string? transport = null,
        string? host = null,
        string? fromAddress = null,
        string? linkBase = null,
        string? resendApiKey = null,
        string? resendFromAddress = null)
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Transport"].Returns(transport);
        config["Notifications:Smtp:Host"].Returns(host);
        config["Notifications:Smtp:FromAddress"].Returns(fromAddress);
        config["Notifications:PasswordResetLinkBase"].Returns(linkBase);
        config["Notifications:Resend:ApiKey"].Returns(resendApiKey);
        config["Notifications:Resend:FromAddress"].Returns(resendFromAddress);
        return config;
    }

    [Fact]
    public void ResolveImplementation_MissingTransportKey_DefaultsToLoggingNotifier()
    {
        // No Notifications:Transport key set at all — must default to
        // the log-only transport so every existing environment stays
        // exactly as it is today.
        var config = Config(transport: null);

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(LoggingNotifier), impl);
    }

    [Fact]
    public void ResolveImplementation_EmptyTransportKey_DefaultsToLoggingNotifier()
    {
        // Empty string must behave identically to missing — an overlay
        // that clears the key should not accidentally force SMTP.
        var config = Config(transport: "");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(LoggingNotifier), impl);
    }

    [Fact]
    public void ResolveImplementation_LogTransport_ReturnsLoggingNotifier()
    {
        var config = Config(transport: "log");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(LoggingNotifier), impl);
    }

    [Fact]
    public void ResolveImplementation_SmtpTransport_WithValidConfig_ReturnsSmtpNotifier()
    {
        var config = Config(
            transport: "smtp",
            host: "smtp.example.com",
            fromAddress: "noreply@example.com",
            linkBase: "https://example.com/reset");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(SmtpNotifier), impl);
    }

    [Theory]
    [InlineData("SMTP")]
    [InlineData("Smtp")]
    [InlineData("  smtp  ")]
    public void ResolveImplementation_SmtpTransport_IsCaseAndTrimInsensitive(string variant)
    {
        // Operator convenience — tolerate casing/whitespace on the
        // config value without widening to arbitrary strings.
        var config = Config(
            transport: variant,
            host: "smtp.example.com",
            fromAddress: "noreply@example.com",
            linkBase: "https://example.com/reset");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(SmtpNotifier), impl);
    }

    [Fact]
    public void ResolveImplementation_UnknownTransport_ThrowsInvalidOperationException()
    {
        // Bounded-value-space invariant. A typo / renamed key / future
        // unimplemented transport must NOT silently fall back to log —
        // that would hide a misconfiguration until the first real
        // reset attempt.
        var config = Config(transport: "sendgrid");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
        Assert.Contains("sendgrid", ex.Message);
        Assert.Contains("log", ex.Message);
        Assert.Contains("smtp", ex.Message);
        Assert.Contains("resend", ex.Message);
    }

    [Fact]
    public void ResolveImplementation_SmtpTransport_MissingHost_ThrowsWithKeyName()
    {
        // Operator-visible fail-fast: the error message must name the
        // missing key(s) so the fix is obvious from stdout alone.
        var config = Config(
            transport: "smtp",
            host: null,
            fromAddress: "noreply@example.com",
            linkBase: "https://example.com/reset");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
        Assert.Contains("Notifications:Smtp:Host", ex.Message);
    }

    [Fact]
    public void ResolveImplementation_SmtpTransport_MissingFromAddress_ThrowsWithKeyName()
    {
        var config = Config(
            transport: "smtp",
            host: "smtp.example.com",
            fromAddress: "",
            linkBase: "https://example.com/reset");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
        Assert.Contains("Notifications:Smtp:FromAddress", ex.Message);
    }

    [Fact]
    public void ResolveImplementation_SmtpTransport_MissingLinkBase_ThrowsWithKeyName()
    {
        var config = Config(
            transport: "smtp",
            host: "smtp.example.com",
            fromAddress: "noreply@example.com",
            linkBase: "   ");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
        Assert.Contains("Notifications:PasswordResetLinkBase", ex.Message);
    }

    [Fact]
    public void ResolveImplementation_SmtpTransport_AllRequiredMissing_NamesAllKeys()
    {
        // When multiple keys are missing the error must list all of
        // them in one throw — otherwise the operator fixes one,
        // restarts, and hits the next missing key. One shot.
        var config = Config(
            transport: "smtp", host: null, fromAddress: null, linkBase: null);

        var ex = Assert.Throws<InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
        Assert.Contains("Notifications:Smtp:Host", ex.Message);
        Assert.Contains("Notifications:Smtp:FromAddress", ex.Message);
        Assert.Contains("Notifications:PasswordResetLinkBase", ex.Message);
    }

    // --- Resend transport ---

    [Fact]
    public void ResolveImplementation_ResendTransport_WithValidConfig_ReturnsResendNotifier()
    {
        var config = Config(
            transport: "resend",
            linkBase: "https://example.com/reset",
            resendApiKey: "re_test_key",
            resendFromAddress: "noreply@example.com");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(ResendNotifier), impl);
    }

    [Theory]
    [InlineData("RESEND")]
    [InlineData("Resend")]
    [InlineData("  resend  ")]
    public void ResolveImplementation_ResendTransport_IsCaseAndTrimInsensitive(string variant)
    {
        var config = Config(
            transport: variant,
            linkBase: "https://example.com/reset",
            resendApiKey: "re_test_key",
            resendFromAddress: "noreply@example.com");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(ResendNotifier), impl);
    }

    [Fact]
    public void ResolveImplementation_ResendTransport_MissingApiKey_ThrowsWithKeyName()
    {
        // Operator-visible fail-fast: the error message must name the
        // missing key(s) so the fix is obvious from stdout alone.
        var config = Config(
            transport: "resend",
            linkBase: "https://example.com/reset",
            resendApiKey: null,
            resendFromAddress: "noreply@example.com");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
        Assert.Contains("Notifications:Resend:ApiKey", ex.Message);
    }

    [Fact]
    public void ResolveImplementation_ResendTransport_ApiKeyOnly_IsEnough()
    {
        // Deliberately NARROWER than the smtp validator. From-address and
        // link-base have safe fallbacks inside ResendNotifier, so a missing
        // optional key must degrade to a working default rather than refuse
        // to boot: throwing here takes the WHOLE site down over a
        // non-critical email setting. Learned on the first real deploy.
        var config = Config(
            transport: "resend", linkBase: null,
            resendApiKey: "re_test_key", resendFromAddress: null);

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(ResendNotifier), impl);
    }

    [Fact]
    public void ResolveImplementation_ResendTransport_AcceptsProviderStyleEnvName()
    {
        // An operator copies the key out of Resend's own dashboard, where
        // it is called RESEND_API_KEY, and pastes THAT name into the host's
        // env vars. Accepting only the app-style key turned that into a
        // refused boot on the first real deploy.
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Transport"].Returns("resend");
        config["Notifications:Resend:ApiKey"].Returns((string?)null);
        config["RESEND_API_KEY"].Returns("re_provider_style_key");

        var impl = NotifierTransport.ResolveImplementation(config);

        Assert.Equal(typeof(ResendNotifier), impl);
    }

    [Fact]
    public void ResolveFrom_NoConfiguredSender_FallsBackToResendTestSender()
    {
        // Resend's shared test sender only delivers to the account owner's
        // own address — exactly the first-run case, and better than not
        // sending at all.
        var config = Substitute.For<IConfiguration>();

        Assert.Equal("onboarding@resend.dev", ResendNotifier.ResolveFrom(config));
    }

    [Fact]
    public void ResolveApiKey_AppStyleKey_WinsOverProviderStyleAlias()
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Resend:ApiKey"].Returns("re_app_style");
        config["RESEND_API_KEY"].Returns("re_provider_style");

        Assert.Equal("re_app_style", ResendNotifier.ResolveApiKey(config));
    }

    [Fact]
    public void ResolveLinkBase_Unset_FallsBackToPublicDashboardUrl()
    {
        // A reset email whose "link" is a bare token is a broken product
        // experience; the fallback keeps the link whole even when the
        // operator never set the key.
        var config = Substitute.For<IConfiguration>();

        var linkBase = ResendNotifier.ResolveLinkBase(config);

        Assert.StartsWith("https://", linkBase);
        Assert.Contains("parent.html", linkBase);
    }
}

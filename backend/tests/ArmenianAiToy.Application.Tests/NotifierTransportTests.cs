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
        string? linkBase = null)
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Transport"].Returns(transport);
        config["Notifications:Smtp:Host"].Returns(host);
        config["Notifications:Smtp:FromAddress"].Returns(fromAddress);
        config["Notifications:PasswordResetLinkBase"].Returns(linkBase);
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

    [Fact]
    public void ResolveImplementation_Resend_WithRequiredKeys_ResolvesResendNotifier()
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Transport"].Returns("resend");
        config["Resend:ApiKey"].Returns("re_test_key");
        config["Resend:FromAddress"].Returns("Areg <noreply@example.com>");
        config["Notifications:PasswordResetLinkBase"].Returns("https://x/parent.html");
        Assert.Equal(typeof(ResendNotifier), NotifierTransport.ResolveImplementation(config));
    }

    [Fact]
    public void ResolveImplementation_Resend_MissingKeys_ThrowsAtStartup()
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Transport"].Returns("resend");
        // ApiKey / FromAddress / LinkBase all null
        Assert.Throws<System.InvalidOperationException>(
            () => NotifierTransport.ResolveImplementation(config));
    }

}

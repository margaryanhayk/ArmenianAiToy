using ArmenianAiToy.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="DormancyTransportPrecondition.Enforce"/> — the
/// startup-time guard that prevents the dormant-parent warn pass from
/// running on a log-only transport. Pure-unit style against an
/// NSubstitute <c>IConfiguration</c>, same pattern
/// <c>NotifierTransportTests</c> and <c>JwtKeysTests</c> use.
///
/// <para>
/// Pins the hard invariant that a misconfigured process cannot silently
/// mark parents as warned without reaching them — the failure is visible
/// at process start, not deferred to a first-tick send.
/// </para>
/// </summary>
public class DormancyTransportPreconditionTests
{
    private static IConfiguration Config(
        string? warnAfterDays = null,
        string? anonymizeAfterDays = null)
    {
        var config = Substitute.For<IConfiguration>();
        config[DormancyTransportPrecondition.WarnAfterDaysKey].Returns(warnAfterDays);
        config[DormancyTransportPrecondition.AnonymizeAfterDaysKey].Returns(anonymizeAfterDays);
        return config;
    }

    [Fact]
    public void Enforce_WarnDisabled_AnyTransport_DoesNotThrow()
    {
        // Missing key, empty, explicit 0, explicit negative — every
        // "disabled" shape must short-circuit the precondition so
        // log-transport environments boot normally.
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: null), typeof(LoggingNotifier));
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: ""), typeof(LoggingNotifier));
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "0"), typeof(LoggingNotifier));
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "-5"), typeof(LoggingNotifier));
        // Non-integer noise is also "disabled" (fails TryParse).
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "not-a-number"), typeof(LoggingNotifier));
    }

    [Fact]
    public void Enforce_WarnEnabled_SmtpTransport_DoesNotThrow()
    {
        // The intended production shape: warn enabled paired with the
        // SMTP transport. Must pass without throwing.
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "180"), typeof(SmtpNotifier));
    }

    [Fact]
    public void Enforce_WarnEnabled_LogTransport_ThrowsWithKeyNames()
    {
        // The failure mode the precondition exists for. Error message
        // must name both config keys explicitly so an operator reading
        // stdout can fix it in one round-trip.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DormancyTransportPrecondition.Enforce(
                Config(warnAfterDays: "180"), typeof(LoggingNotifier)));

        Assert.Contains(DormancyTransportPrecondition.WarnAfterDaysKey, ex.Message);
        Assert.Contains("Notifications:Transport", ex.Message);
        Assert.Contains("smtp", ex.Message);
        // The resolved-as name helps operators who set a non-standard
        // transport value that resolved to log via some earlier
        // fallback.
        Assert.Contains("log", ex.Message);
    }

    [Fact]
    public void Enforce_WarnEnabled_WithSmallPositiveValue_StillChecksTransport()
    {
        // Any positive value enables the pass — not just the "180"
        // recommended threshold. A smoke-test overlay that sets
        // WarnAfterDays=1 to trigger the pass during integration
        // testing must not slip past the precondition if transport is
        // still log.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DormancyTransportPrecondition.Enforce(
                Config(warnAfterDays: "1"), typeof(LoggingNotifier)));

        Assert.Contains("1", ex.Message);
    }

    // --- Anonymize-enabled guards ------------------------------------

    [Fact]
    public void Enforce_AnonymizeEnabled_WarnDisabled_Throws()
    {
        // Destructive without warn is a policy error: a parent must
        // receive a warning email before any irreversible action
        // fires. The error message must name both keys so an
        // operator can fix the pairing in one round-trip.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DormancyTransportPrecondition.Enforce(
                Config(warnAfterDays: "0", anonymizeAfterDays: "60"),
                typeof(SmtpNotifier)));

        Assert.Contains(DormancyTransportPrecondition.AnonymizeAfterDaysKey, ex.Message);
        Assert.Contains(DormancyTransportPrecondition.WarnAfterDaysKey, ex.Message);
    }

    [Fact]
    public void Enforce_AnonymizeEnabled_WarnMissingKey_Throws()
    {
        // Same failure as explicit 0 for warn: missing key parses to
        // 0 via the same TryParse fallback, so destructive-without-
        // warn still fails fast.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DormancyTransportPrecondition.Enforce(
                Config(warnAfterDays: null, anonymizeAfterDays: "60"),
                typeof(SmtpNotifier)));

        Assert.Contains(DormancyTransportPrecondition.AnonymizeAfterDaysKey, ex.Message);
        Assert.Contains(DormancyTransportPrecondition.WarnAfterDaysKey, ex.Message);
    }

    [Fact]
    public void Enforce_AnonymizeEnabled_WarnEnabled_LogTransport_Throws()
    {
        // Even with warn enabled, if the transport is log, destructive
        // would silently fire while warning emails go to stdout. The
        // first guard catches the warn+log mismatch before the second
        // guard gets to check anonymize+log — either way, startup
        // throws. Verify the first guard's behavior here explicitly.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DormancyTransportPrecondition.Enforce(
                Config(warnAfterDays: "180", anonymizeAfterDays: "60"),
                typeof(LoggingNotifier)));

        // Warn-enabled + log is rejected by Guard 1 first. Error must
        // still name the warn key.
        Assert.Contains(DormancyTransportPrecondition.WarnAfterDaysKey, ex.Message);
    }

    [Fact]
    public void Enforce_AnonymizeEnabled_WarnEnabled_SmtpTransport_DoesNotThrow()
    {
        // The intended production shape: both passes enabled,
        // transport is SMTP. Must pass without throwing.
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "180", anonymizeAfterDays: "60"),
            typeof(SmtpNotifier));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-an-int")]
    public void Enforce_AnonymizeDisabled_AnyTransport_DoesNotThrow(string? anonymizeRaw)
    {
        // Every disabled shape (zero, negative, missing, empty,
        // non-integer) must skip Guard 2 entirely. Guard 1 still
        // applies if warn is enabled, which is why we pass warn=0
        // here — to ensure we're testing the anonymize-disabled path
        // in isolation.
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "0", anonymizeAfterDays: anonymizeRaw),
            typeof(LoggingNotifier));
        DormancyTransportPrecondition.Enforce(
            Config(warnAfterDays: "0", anonymizeAfterDays: anonymizeRaw),
            typeof(SmtpNotifier));
    }
}

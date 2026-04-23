using System.Net.Mail;
using ArmenianAiToy.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="SmtpNotifier"/>. Uses the internal send-seam
/// (the <c>SendMailDelegate</c> constructor overload) to substitute a
/// capturing or throwing fake — no real SMTP server is stood up for
/// these tests. Mirrors the shape of <c>LoggingNotifierTests</c> where
/// relevant (no-raw-token invariant) and adds failure-containment
/// coverage that is specific to the wire-call transport.
/// </summary>
public class SmtpNotifierTests
{
    private static IConfiguration BuildConfig(
        string fromAddress = "noreply@example.com",
        string linkBase = "https://example.com/reset")
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Smtp:FromAddress"].Returns(fromAddress);
        config["Notifications:PasswordResetLinkBase"].Returns(linkBase);
        return config;
    }

    [Fact]
    public async Task SendPasswordResetAsync_ComposesMessage_WithResetLinkAndTokenInBody()
    {
        // Captures the MailMessage the notifier would have handed to
        // SmtpClient. Pins the on-the-wire shape: recipient, from
        // address, non-empty subject, plain-text body, reset link
        // containing the raw token (URL-encoded).
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) =>
            {
                captured = msg;
                return Task.CompletedTask;
            });

        const string rawToken = "UNIQUE-test-token-abc123_XYZ";
        await notifier.SendPasswordResetAsync("parent@example.com", rawToken);

        Assert.NotNull(captured);
        Assert.Equal("noreply@example.com", captured!.From!.Address);
        Assert.Single(captured.To);
        Assert.Equal("parent@example.com", captured.To[0].Address);
        Assert.False(string.IsNullOrWhiteSpace(captured.Subject));
        Assert.False(captured.IsBodyHtml);
        Assert.False(string.IsNullOrWhiteSpace(captured.Body));
        // The body must carry a reset link that contains the raw token
        // (URL-encoded). Note Uri.EscapeDataString preserves A-Z/a-z/0-9
        // and a few unreserved chars, so the token in this test (which
        // uses only those) appears literally.
        Assert.Contains(rawToken, captured.Body);
        Assert.Contains("https://example.com/reset", captured.Body);
    }

    [Fact]
    public async Task SendPasswordResetAsync_LinkBaseWithExistingQuery_AppendsWithAmpersand()
    {
        // Defensive correctness on link composition — if the configured
        // link base already carries a '?' (e.g. a deeplink with params),
        // the token must be appended with '&', not a second '?'.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig(
            linkBase: "https://example.com/reset?lang=hy");
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) => { captured = msg; return Task.CompletedTask; });

        await notifier.SendPasswordResetAsync("parent@example.com", "TOKENxyz");

        Assert.NotNull(captured);
        Assert.Contains("?lang=hy&token=TOKENxyz", captured!.Body);
        Assert.DoesNotContain("?lang=hy?token=", captured.Body);
    }

    [Fact]
    public async Task SendPasswordResetAsync_DoesNotLogRawToken()
    {
        // Mirrors LoggingNotifierTests.SendPasswordResetAsync_DoesNotLogRawToken:
        // scan every argument of every Log call made during the send,
        // assert the raw token string never appears. A careless
        // future edit that stuck {Token} in the template would fail.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => Task.CompletedTask);

        const string rawToken = "super-secret-reset-token-do-not-log-this-UNIQUE42";
        await notifier.SendPasswordResetAsync("parent@example.com", rawToken);

        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                if (arg is null) continue;
                Assert.DoesNotContain(rawToken, arg.ToString() ?? "",
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SendPasswordResetAsync_FailurePath_DoesNotLogRawToken()
    {
        // The failure branch has its OWN log line (a LogWarning with an
        // exception argument). The no-raw-token invariant must hold on
        // that path too — otherwise a broken SMTP relay would silently
        // leak tokens to the log stream.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new SmtpException("connection refused"));

        const string rawToken = "failure-path-token-UNIQUE99";
        await notifier.SendPasswordResetAsync("parent@example.com", rawToken);

        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                if (arg is null) continue;
                Assert.DoesNotContain(rawToken, arg.ToString() ?? "",
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SendPasswordResetAsync_SmtpSendThrows_DoesNotPropagate()
    {
        // HARD INVARIANT: a broken SMTP relay must not convert the
        // forgot-password 202 contract into a 500. The notifier catches
        // non-cancellation exceptions, logs a warning, and returns
        // normally. Regressing this would break the anti-enumeration
        // timing contract too (since an unknown-email path never
        // reaches the notifier, but a known-email path with a broken
        // relay would suddenly behave differently).
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new SmtpException("relay broken"));

        // Must not throw.
        await notifier.SendPasswordResetAsync("parent@example.com", "any-token");

        // The failure was observed and logged — assert a Warning was
        // emitted at least once.
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SendPasswordResetAsync_OperationCanceled_DoesPropagate()
    {
        // Cancellation is the caller saying "stop," not the SMTP relay
        // failing. It must NOT be absorbed — the HTTP pipeline needs
        // to see it to abort cleanly. Regressing this would make
        // request cancellation silent and let zombie sends continue.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => notifier.SendPasswordResetAsync("parent@example.com", "t"));
    }

    // --- Dormancy warning (worker consumer, returns bool) ---

    [Fact]
    public async Task SendDormancyWarningAsync_Success_ReturnsTrueAndComposesMessage()
    {
        // Captures the MailMessage. Pins: recipient, from address,
        // non-empty Armenian subject, plain-text body. The body
        // deliberately does NOT contain `deleteAtUtc` copy in this
        // slice — it is warn-only.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) =>
            {
                captured = msg;
                return Task.CompletedTask;
            });

        var delivered = await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: null);

        Assert.True(delivered);
        Assert.NotNull(captured);
        Assert.Equal("noreply@example.com", captured!.From!.Address);
        Assert.Single(captured.To);
        Assert.Equal("parent@example.com", captured.To[0].Address);
        Assert.False(string.IsNullOrWhiteSpace(captured.Subject));
        Assert.False(captured.IsBodyHtml);
        Assert.False(string.IsNullOrWhiteSpace(captured.Body));
    }

    [Fact]
    public async Task SendDormancyWarningAsync_SmtpSendThrows_ReturnsFalseAndDoesNotPropagate()
    {
        // The bool return is load-bearing for the worker: `false`
        // means "do NOT stamp DormancyWarnedAt, do NOT write audit,
        // retry next tick." A regression that either propagated the
        // exception OR returned true on failure would silently
        // advance the stamp / emit phantom audit rows for sends that
        // never reached the parent.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new SmtpException("relay broken"));

        var delivered = await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: null);

        Assert.False(delivered);
        // Failure emits a Warning log — pinned so a future refactor
        // that accidentally silenced the breadcrumb would fail.
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SendDormancyWarningAsync_OperationCanceled_DoesPropagate()
    {
        // Cancellation is the worker shutting down, not an SMTP
        // failure. It must propagate so ExecuteAsync can exit cleanly
        // rather than continuing a zombie tick.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            notifier.SendDormancyWarningAsync("parent@example.com", null));
    }

    [Fact]
    public async Task SendDormancyWarningAsync_WithDeleteAtUtc_IncludesDateInBody()
    {
        // The anonymize slice activates the `deleteAtUtc` parameter:
        // a non-null value must land in the outgoing body as a plain
        // yyyy-MM-dd date so the parent sees an honest calendar date
        // they can verify against their own calendar. No time
        // component, no ISO-with-T-and-Z format.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) => { captured = msg; return Task.CompletedTask; });

        var futureDeleteDate = new DateTime(2099, 1, 15, 6, 30, 45, DateTimeKind.Utc);
        await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: futureDeleteDate);

        Assert.NotNull(captured);
        // Plain yyyy-MM-dd — NOT ISO with time, NOT locale-specific.
        Assert.Contains("2099-01-15", captured!.Body);
        // Does not leak the time component or full ISO O-format.
        Assert.DoesNotContain("06:30:45", captured.Body);
        Assert.DoesNotContain(futureDeleteDate.ToString("O"), captured.Body);
    }

    [Fact]
    public async Task SendDormancyWarningAsync_WithoutDeleteAtUtc_DoesNotLeakDate()
    {
        // Warn-only mode (deleteAtUtc=null): body must stay exactly
        // warn-only copy. No scheduled-date sentence, no plausible
        // yyyy-MM-dd string. Regression guard against a future edit
        // that accidentally renders a default/sentinel date.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) => { captured = msg; return Task.CompletedTask; });

        await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: null);

        Assert.NotNull(captured);
        // A yyyy-MM-dd pattern hit would indicate the destructive-
        // date sentence accidentally rendered even with null input.
        // Match any 4-2-2 date shape; captured.Body is Armenian text
        // otherwise and won't contain this pattern naturally.
        var datePattern = new System.Text.RegularExpressions.Regex(@"\d{4}-\d{2}-\d{2}");
        Assert.False(datePattern.IsMatch(captured!.Body),
            "Warn-only body must not contain any yyyy-MM-dd date string.");
    }

    // --- Email verification (HTTP-synchronous caller) ---

    [Fact]
    public async Task SendEmailVerificationAsync_ComposesMessage_WithVerifyLink()
    {
        // Pin the body shape: recipient, non-empty Armenian subject,
        // plain-text body, verification link with URL-encoded token,
        // verifyToken param name (distinct from reset's `token`).
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) =>
            {
                captured = msg;
                return Task.CompletedTask;
            });

        const string rawToken = "VERIFY-test-token-abc123_XYZ";
        await notifier.SendEmailVerificationAsync("parent@example.com", rawToken);

        Assert.NotNull(captured);
        Assert.Equal("noreply@example.com", captured!.From!.Address);
        Assert.Single(captured.To);
        Assert.Equal("parent@example.com", captured.To[0].Address);
        Assert.False(string.IsNullOrWhiteSpace(captured.Subject));
        Assert.False(captured.IsBodyHtml);
        Assert.Contains(rawToken, captured.Body);
        Assert.Contains("verifyToken=", captured.Body);
        Assert.Contains("https://example.com/reset", captured.Body);
    }

    [Fact]
    public async Task SendEmailVerificationAsync_DoesNotLogRawToken()
    {
        // No-raw-token invariant on the verification path — mirrors
        // the pinned invariant on SendPasswordResetAsync. Scan every
        // argument of every Log call.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => Task.CompletedTask);

        const string rawToken = "super-secret-verification-token-UNIQUE-XY9";
        await notifier.SendEmailVerificationAsync("parent@example.com", rawToken);

        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                if (arg is null) continue;
                Assert.DoesNotContain(rawToken, arg.ToString() ?? "",
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SendEmailVerificationAsync_FailurePath_DoesNotLogRawToken()
    {
        // Same scrub discipline on the LogWarning branch.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new SmtpException("relay broken"));

        const string rawToken = "failure-path-verify-token-UNIQUE-ZZ9";
        await notifier.SendEmailVerificationAsync("parent@example.com", rawToken);

        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                if (arg is null) continue;
                Assert.DoesNotContain(rawToken, arg.ToString() ?? "",
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SendEmailVerificationAsync_SmtpSendThrows_DoesNotPropagate()
    {
        // HTTP-synchronous caller's anti-enum response must not break
        // on an SMTP failure. Same contract as SendPasswordResetAsync.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new SmtpException("relay broken"));

        await notifier.SendEmailVerificationAsync("parent@example.com", "any-token");

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // --- Dormant-device warning (worker consumer, returns bool) ---

    [Fact]
    public async Task SendDormantDeviceWarningAsync_Success_ComposesMessage()
    {
        // Pin the body shape: recipient, From address, non-empty
        // Armenian subject, plain-text body containing the device
        // name AND the last-seen date in plain yyyy-MM-dd. Returns
        // true on success.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) => { captured = msg; return Task.CompletedTask; });

        var lastSeen = new DateTime(2025, 11, 7, 10, 30, 45, DateTimeKind.Utc);
        var delivered = await notifier.SendDormantDeviceWarningAsync(
            "parent@example.com", "Bedroom Areg", lastSeen, deleteAtUtc: null);

        Assert.True(delivered);
        Assert.NotNull(captured);
        Assert.Equal("noreply@example.com", captured!.From!.Address);
        Assert.Single(captured.To);
        Assert.Equal("parent@example.com", captured.To[0].Address);
        Assert.False(string.IsNullOrWhiteSpace(captured.Subject));
        Assert.False(captured.IsBodyHtml);
        Assert.Contains("Bedroom Areg", captured.Body);
        // Plain yyyy-MM-dd, NOT ISO with T/Z.
        Assert.Contains("2025-11-07", captured.Body);
        Assert.DoesNotContain("10:30:45", captured.Body);
        Assert.DoesNotContain(lastSeen.ToString("O"), captured.Body);
    }

    [Fact]
    public async Task SendDormantDeviceWarningAsync_NullDeleteAtUtc_DoesNotLeakDestructiveCopy()
    {
        // Warn-only slice contract: when deleteAtUtc is null, the
        // body MUST NOT include any "your device will be removed
        // on X" copy. A future destructive slice will activate
        // deleteAtUtc; this slice must not leak forward-looking
        // language.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) => { captured = msg; return Task.CompletedTask; });

        await notifier.SendDormantDeviceWarningAsync(
            "parent@example.com", "Areg",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            deleteAtUtc: null);

        Assert.NotNull(captured);
        // Defensive: the body has exactly one yyyy-MM-dd date — the
        // last-seen date. A regression that included a destructive-
        // date sentence would produce a SECOND date.
        var dateMatches = System.Text.RegularExpressions.Regex.Matches(
            captured!.Body, @"\d{4}-\d{2}-\d{2}");
        Assert.Single(dateMatches);
    }

    [Fact]
    public async Task SendDormantDeviceWarningAsync_SmtpSendThrows_ReturnsFalseAndDoesNotPropagate()
    {
        // Worker consumer's bool return is load-bearing for fan-out
        // partial-failure handling: false means "this recipient
        // failed; the device may still be stamped if at least one
        // OTHER recipient succeeded." Throwing would break the
        // fan-out loop.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new SmtpException("relay broken"));

        var delivered = await notifier.SendDormantDeviceWarningAsync(
            "parent@example.com", "Areg",
            DateTime.UtcNow.AddDays(-400), deleteAtUtc: null);

        Assert.False(delivered);
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SendDormantDeviceWarningAsync_OperationCanceled_DoesPropagate()
    {
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            notifier.SendDormantDeviceWarningAsync(
                "parent@example.com", "Areg",
                DateTime.UtcNow.AddDays(-400), deleteAtUtc: null));
    }

    [Fact]
    public async Task SendPasswordResetAsync_Success_LogsDeliveredTrue()
    {
        // Happy-path log line: one LogInformation with delivered=true.
        // Not pinned to an exact message, but the bool value MUST be
        // carried — downstream log queries key off it to alert on
        // delivery failure rates.
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (_, _) => Task.CompletedTask);

        await notifier.SendPasswordResetAsync("parent@example.com", "t");

        // Check that at least one Information log was emitted carrying
        // a `true` boolean in its state arguments.
        var calls = logger.ReceivedCalls().ToList();
        var loggedTrue = calls.Any(c =>
            c.GetArguments().Any(a =>
                a?.ToString()?.Contains("True", StringComparison.Ordinal) == true
                || (a is bool b && b)));
        Assert.True(loggedTrue,
            "expected a structured log carrying delivered=true on the success path");
    }
}

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
    public async Task SendDormancyWarningAsync_DeleteAtUtcParam_IsIgnoredInWarnOnlySlice()
    {
        // The method accepts a nullable deleteAtUtc so the next slice
        // (delete action) is a body-only change. In THIS slice, the
        // value must never leak into the outgoing message — the copy
        // is warn-only, and a caller passing a non-null deleteAtUtc
        // (e.g. during integration testing before that slice ships)
        // must not produce scary "your account will be deleted on X"
        // copy in the body.
        MailMessage? captured = null;
        var logger = Substitute.For<ILogger<SmtpNotifier>>();
        var config = BuildConfig();
        var notifier = new SmtpNotifier(logger, config,
            sendMail: (msg, _) => { captured = msg; return Task.CompletedTask; });

        var futureDeleteDate = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: futureDeleteDate);

        Assert.NotNull(captured);
        Assert.DoesNotContain("2099", captured!.Body);
        Assert.DoesNotContain(futureDeleteDate.ToString("O"), captured.Body);
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

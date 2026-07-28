using System.Net;
using System.Text.Json;
using ArmenianAiToy.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="ResendNotifier"/>. Uses the internal HTTP send-seam
/// (the <c>SendHttpDelegate</c> constructor overload) to substitute a
/// capturing or throwing fake — no real network access. Mirrors
/// <c>SmtpNotifierTests</c>: on-the-wire shape, no-raw-token invariant on
/// success AND failure paths, failure containment (swallow non-OCE,
/// propagate OCE), and the worker-facing bool contract. Adds the
/// Resend-specific case: a non-2xx API response is a delivery failure
/// (delivered=false / return false), not an exception.
/// </summary>
public class ResendNotifierTests
{
    private sealed record CapturedRequest(
        string Url,
        string? BearerToken,
        string From,
        string[] To,
        string Subject,
        string Text);

    private static IConfiguration BuildConfig(
        string apiKey = "re_test_key_123",
        string fromAddress = "noreply@example.com",
        string linkBase = "https://example.com/reset")
    {
        var config = Substitute.For<IConfiguration>();
        config["Notifications:Resend:ApiKey"].Returns(apiKey);
        config["Notifications:Resend:FromAddress"].Returns(fromAddress);
        config["Notifications:PasswordResetLinkBase"].Returns(linkBase);
        return config;
    }

    // Captures the outgoing HttpRequestMessage's interesting parts BEFORE
    // returning, because the notifier disposes the request after the send.
    private static ResendNotifier.SendHttpDelegate Capture(
        List<CapturedRequest> sink, HttpStatusCode status = HttpStatusCode.OK)
    {
        return async (request, _) =>
        {
            var raw = await request.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            sink.Add(new CapturedRequest(
                Url: request.RequestUri!.ToString(),
                BearerToken: request.Headers.Authorization?.Parameter,
                From: root.GetProperty("from").GetString() ?? "",
                To: root.GetProperty("to").EnumerateArray()
                    .Select(e => e.GetString() ?? "").ToArray(),
                Subject: root.GetProperty("subject").GetString() ?? "",
                Text: root.GetProperty("text").GetString() ?? ""));
            return new HttpResponseMessage(status);
        };
    }

    [Fact]
    public async Task SendPasswordResetAsync_PostsToResendApi_WithResetLinkAndTokenInBody()
    {
        // Pins the on-the-wire shape: endpoint, bearer key, from, to,
        // Armenian subject, plain-text body carrying the reset link with
        // the raw token (URL-encoded; this token's charset survives
        // Uri.EscapeDataString literally).
        var captured = new List<CapturedRequest>();
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(), Capture(captured));

        const string rawToken = "UNIQUE-test-token-abc123_XYZ";
        await notifier.SendPasswordResetAsync("parent@example.com", rawToken);

        var req = Assert.Single(captured);
        Assert.Equal("https://api.resend.com/emails", req.Url);
        Assert.Equal("re_test_key_123", req.BearerToken);
        Assert.Equal("noreply@example.com", req.From);
        Assert.Equal(new[] { "parent@example.com" }, req.To);
        Assert.Equal("Գաղտնաբառի վերականգնում", req.Subject);
        Assert.Contains(rawToken, req.Text);
        Assert.Contains("https://example.com/reset", req.Text);
        Assert.Contains("token=", req.Text);
    }

    [Fact]
    public async Task SendPasswordResetAsync_LinkBaseWithExistingQuery_AppendsWithAmpersand()
    {
        // Same link-composition contract SmtpNotifierTests pins — the
        // builders are shared via NotificationEmailContent, and this
        // test keeps the Resend path honest if that ever changes.
        var captured = new List<CapturedRequest>();
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger,
            BuildConfig(linkBase: "https://example.com/reset?lang=hy"),
            Capture(captured));

        await notifier.SendPasswordResetAsync("parent@example.com", "TOKENxyz");

        var req = Assert.Single(captured);
        Assert.Contains("?lang=hy&token=TOKENxyz", req.Text);
        Assert.DoesNotContain("?lang=hy?token=", req.Text);
    }

    [Fact]
    public async Task SendPasswordResetAsync_DoesNotLogRawToken()
    {
        // No-raw-token invariant, success path — same pinned contract
        // as LoggingNotifierTests / SmtpNotifierTests: scan every
        // argument of every Log call.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        const string rawToken = "super-secret-reset-token-do-not-log-this-UNIQUE42";
        await notifier.SendPasswordResetAsync("parent@example.com", rawToken);

        AssertNoLogArgContains(logger, rawToken);
    }

    [Fact]
    public async Task SendPasswordResetAsync_FailurePath_DoesNotLogRawToken()
    {
        // The failure branch has its OWN log line (LogWarning with an
        // exception argument). The invariant must hold there too —
        // otherwise a broken Resend API would leak tokens to the log
        // stream.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new HttpRequestException("connection refused"));

        const string rawToken = "failure-path-token-UNIQUE99";
        await notifier.SendPasswordResetAsync("parent@example.com", rawToken);

        AssertNoLogArgContains(logger, rawToken);
    }

    [Fact]
    public async Task SendPasswordResetAsync_HttpSendThrows_DoesNotPropagate()
    {
        // HARD INVARIANT: a broken Resend API must not convert the
        // forgot-password 202 contract into a 500. Same posture the
        // SMTP transport pins.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new HttpRequestException("api down"));

        // Must not throw.
        await notifier.SendPasswordResetAsync("parent@example.com", "any-token");

        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendPasswordResetAsync_Non2xxResponse_LogsDeliveredFalse_DoesNotThrow()
    {
        // Resend-specific failure mode with no SMTP analogue: the API
        // answers, but with a rejection (401 bad key / 403 unverified
        // domain / 422 validation). Must be treated as a delivery
        // failure — warning log, no exception, 202 contract preserved.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)));

        await notifier.SendPasswordResetAsync("parent@example.com", "any-token");

        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendPasswordResetAsync_OperationCanceled_DoesPropagate()
    {
        // Cancellation is the caller saying "stop," not the API failing.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => notifier.SendPasswordResetAsync("parent@example.com", "t"));
    }

    [Fact]
    public async Task SendPasswordResetAsync_Success_LogsDeliveredTrue()
    {
        // Happy-path log line must carry delivered=true — downstream
        // log queries key off it to alert on delivery failure rates.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await notifier.SendPasswordResetAsync("parent@example.com", "t");

        var calls = logger.ReceivedCalls().ToList();
        var loggedTrue = calls.Any(c =>
            c.GetArguments().Any(a =>
                a?.ToString()?.Contains("True", StringComparison.Ordinal) == true
                || (a is bool b && b)));
        Assert.True(loggedTrue,
            "expected a structured log carrying delivered=true on the success path");
    }

    // --- Email verification (HTTP-synchronous caller) ---

    [Fact]
    public async Task SendEmailVerificationAsync_PostsVerifyLink()
    {
        // verifyToken param name (distinct from reset's `token`) is the
        // wire contract with parent.html's boot router.
        var captured = new List<CapturedRequest>();
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(), Capture(captured));

        const string rawToken = "VERIFY-test-token-abc123_XYZ";
        await notifier.SendEmailVerificationAsync("parent@example.com", rawToken);

        var req = Assert.Single(captured);
        Assert.Equal(new[] { "parent@example.com" }, req.To);
        Assert.Equal("Հաստատեք Ձեր էլ. փոստը", req.Subject);
        Assert.Contains(rawToken, req.Text);
        Assert.Contains("verifyToken=", req.Text);
        Assert.Contains("https://example.com/reset", req.Text);
    }

    [Fact]
    public async Task SendEmailVerificationAsync_FailurePath_DoesNotLogRawToken()
    {
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new HttpRequestException("api down"));

        const string rawToken = "failure-path-verify-token-UNIQUE-ZZ9";
        await notifier.SendEmailVerificationAsync("parent@example.com", rawToken);

        AssertNoLogArgContains(logger, rawToken);
    }

    [Fact]
    public async Task SendEmailVerificationAsync_HttpSendThrows_DoesNotPropagate()
    {
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new HttpRequestException("api down"));

        await notifier.SendEmailVerificationAsync("parent@example.com", "any-token");

        AssertWarningLogged(logger);
    }

    // --- Dormancy warning (worker consumer, returns bool) ---

    [Fact]
    public async Task SendDormancyWarningAsync_Success_ReturnsTrue()
    {
        var captured = new List<CapturedRequest>();
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(), Capture(captured));

        var delivered = await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: null);

        Assert.True(delivered);
        var req = Assert.Single(captured);
        Assert.Equal(new[] { "parent@example.com" }, req.To);
        Assert.False(string.IsNullOrWhiteSpace(req.Subject));
        Assert.False(string.IsNullOrWhiteSpace(req.Text));
    }

    [Fact]
    public async Task SendDormancyWarningAsync_Non2xxResponse_ReturnsFalse()
    {
        // The bool return is load-bearing for the worker: false means
        // "do NOT stamp DormancyWarnedAt, retry next tick." A non-2xx
        // API response must map to false, exactly like a thrown
        // exception does.
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var delivered = await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: null);

        Assert.False(delivered);
        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendDormancyWarningAsync_HttpSendThrows_ReturnsFalseAndDoesNotPropagate()
    {
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new HttpRequestException("api down"));

        var delivered = await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: null);

        Assert.False(delivered);
        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendDormancyWarningAsync_OperationCanceled_DoesPropagate()
    {
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            notifier.SendDormancyWarningAsync("parent@example.com", null));
    }

    [Fact]
    public async Task SendDormancyWarningAsync_WithDeleteAtUtc_IncludesDateInBody()
    {
        // Shared-content sanity on the Resend path: non-null deleteAtUtc
        // renders the plain yyyy-MM-dd date, no time component.
        var captured = new List<CapturedRequest>();
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(), Capture(captured));

        var futureDeleteDate = new DateTime(2099, 1, 15, 6, 30, 45, DateTimeKind.Utc);
        await notifier.SendDormancyWarningAsync(
            "parent@example.com", deleteAtUtc: futureDeleteDate);

        var req = Assert.Single(captured);
        Assert.Contains("2099-01-15", req.Text);
        Assert.DoesNotContain("06:30:45", req.Text);
    }

    // --- Dormant-device warning (worker consumer, returns bool) ---

    [Fact]
    public async Task SendDormantDeviceWarningAsync_Success_ComposesBodyWithDeviceNameAndDate()
    {
        var captured = new List<CapturedRequest>();
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(), Capture(captured));

        var lastSeen = new DateTime(2025, 11, 7, 10, 30, 45, DateTimeKind.Utc);
        var delivered = await notifier.SendDormantDeviceWarningAsync(
            "parent@example.com", "Bedroom Areg", lastSeen, deleteAtUtc: null);

        Assert.True(delivered);
        var req = Assert.Single(captured);
        Assert.Equal(new[] { "parent@example.com" }, req.To);
        Assert.Contains("Bedroom Areg", req.Text);
        Assert.Contains("2025-11-07", req.Text);
        Assert.DoesNotContain("10:30:45", req.Text);
    }

    [Fact]
    public async Task SendDormantDeviceWarningAsync_Non2xxResponse_ReturnsFalse()
    {
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var delivered = await notifier.SendDormantDeviceWarningAsync(
            "parent@example.com", "Areg",
            DateTime.UtcNow.AddDays(-400), deleteAtUtc: null);

        Assert.False(delivered);
        AssertWarningLogged(logger);
    }

    [Fact]
    public async Task SendDormantDeviceWarningAsync_OperationCanceled_DoesPropagate()
    {
        var logger = Substitute.For<ILogger<ResendNotifier>>();
        var notifier = new ResendNotifier(logger, BuildConfig(),
            (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            notifier.SendDormantDeviceWarningAsync(
                "parent@example.com", "Areg",
                DateTime.UtcNow.AddDays(-400), deleteAtUtc: null));
    }

    // --- helpers ---

    private static void AssertNoLogArgContains(
        ILogger<ResendNotifier> logger, string forbidden)
    {
        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                if (arg is null) continue;
                Assert.DoesNotContain(forbidden, arg.ToString() ?? "",
                    StringComparison.Ordinal);
            }
        }
    }

    private static void AssertWarningLogged(ILogger<ResendNotifier> logger)
    {
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

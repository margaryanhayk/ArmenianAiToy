using ArmenianAiToy.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the blocking "raw token never leaks to the log" invariant on
/// <see cref="LoggingNotifier"/>. The rest of LoggingNotifier is
/// effectively a single log call, so one focused test covers it.
/// </summary>
public class LoggingNotifierTests
{
    [Fact]
    public async Task SendPasswordResetAsync_DoesNotLogRawToken()
    {
        var logger = Substitute.For<ILogger<LoggingNotifier>>();
        var notifier = new LoggingNotifier(logger);

        const string rawToken = "super-secret-reset-token-do-not-log-this-UNIQUE42";
        await notifier.SendPasswordResetAsync("owner@example.com", rawToken);

        // Inspect every Log call the notifier made. The raw token
        // MUST NOT appear anywhere in any of them — not in the
        // message template, not in a state value, not in an
        // exception text. A careless future edit that placed the
        // token into {Token} in the template would fail this test.
        foreach (var call in logger.ReceivedCalls())
        {
            foreach (var arg in call.GetArguments())
            {
                if (arg is null) continue;
                Assert.DoesNotContain(rawToken, arg.ToString() ?? "", StringComparison.Ordinal);
            }
        }
    }
}

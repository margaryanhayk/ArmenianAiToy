using ArmenianAiToy.Application.Notifications;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using ArmenianAiToy.Infrastructure.Background;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the opt-in <see cref="WeeklyDigestService"/> worker. Pins:
///  - disabled by default (no config → no sends),
///  - one counts-only send per verified parent with activity,
///  - counts are correct + scoped to the parent's own devices,
///  - process-local dedup (no re-send within the 7-day period),
///  - no activity / unverified parent → no send,
///  - a failed delivery is not recorded → retries next tick.
/// </summary>
public class WeeklyDigestServiceTests
{
    private sealed class RecordingNotifier : INotifier
    {
        public readonly List<(string Email, WeeklyDigestSummary Summary)> Sent = new();
        public bool ReturnValue = true;

        public Task SendPasswordResetAsync(string e, string t, CancellationToken c = default)
            => Task.CompletedTask;
        public Task<bool> SendDormancyWarningAsync(string e, DateTime? d, CancellationToken c = default)
            => Task.FromResult(true);
        public Task SendEmailVerificationAsync(string e, string t, CancellationToken c = default)
            => Task.CompletedTask;
        public Task<bool> SendDormantDeviceWarningAsync(
            string pe, string dn, DateTime ls, DateTime? d, CancellationToken c = default)
            => Task.FromResult(true);
        public Task<bool> SendWeeklyDigestAsync(
            string email, WeeklyDigestSummary summary, CancellationToken c = default)
        {
            Sent.Add((email, summary));
            return Task.FromResult(ReturnValue);
        }
    }

    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InWindow = new(2026, 6, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OutOfWindow = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);

    private static (WeeklyDigestService Service, ServiceProvider Root, RecordingNotifier Notifier)
        Create(bool enabled)
    {
        var notifier = new RecordingNotifier();
        // Compute the DB name ONCE — the AddDbContext lambda runs per
        // context creation, so a Guid generated inside it would give each
        // scope a different in-memory database (seed + worker wouldn't share).
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<INotifier>(notifier);
        var root = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Digest:Weekly:Enabled"] = enabled ? "true" : "false",
            })
            .Build();
        var logger = Substitute.For<ILogger<WeeklyDigestService>>();
        var svc = new WeeklyDigestService(
            root.GetRequiredService<IServiceScopeFactory>(), config, logger);
        return (svc, root, notifier);
    }

    private static async Task<Guid> SeedParentWithActivityAsync(
        ServiceProvider root,
        bool verified,
        DateTime[] messageTimes)
    {
        using var scope = root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.Set<Parent>().Add(new Parent
        {
            Id = parentId,
            Email = $"p-{parentId:N}@example.com",
            EmailVerifiedAt = verified ? Now.AddDays(-30) : null,
            RegisteredAt = Now.AddDays(-60),
        });
        db.Set<Device>().Add(new Device
        {
            Id = deviceId,
            MacAddress = "mac-" + Guid.NewGuid().ToString("N")[..10],
            Name = "Test",
            ApiKey = "dtk_" + Guid.NewGuid().ToString("N"),
            RegisteredAt = Now.AddDays(-60),
            LastSeenAt = Now,
        });
        db.Set<ParentDevice>().Add(new ParentDevice
        {
            ParentId = parentId, DeviceId = deviceId, LinkedAt = Now.AddDays(-60),
        });
        var convId = Guid.NewGuid();
        db.Set<Conversation>().Add(new Conversation
        {
            Id = convId, DeviceId = deviceId, StartedAt = messageTimes.Min(),
        });
        foreach (var ts in messageTimes)
        {
            db.Set<Message>().Add(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = convId,
                Role = MessageRole.User,
                Content = "hi",
                Timestamp = ts,
                SafetyFlag = SafetyFlag.Clean,
            });
        }
        await db.SaveChangesAsync();
        return parentId;
    }

    [Fact]
    public async Task Disabled_SendsNothing()
    {
        var (svc, root, notifier) = Create(enabled: false);
        await SeedParentWithActivityAsync(root, verified: true, [InWindow]);

        await svc.RunTickAsync(Now, CancellationToken.None);

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task Enabled_VerifiedParentWithActivity_SendsCorrectCounts()
    {
        var (svc, root, notifier) = Create(enabled: true);
        // two messages on the same UTC day inside the window → 1 active day.
        await SeedParentWithActivityAsync(
            root, verified: true, [InWindow, InWindow.AddHours(2)]);

        await svc.RunTickAsync(Now, CancellationToken.None);

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(2, sent.Summary.MessageCount);
        Assert.Equal(1, sent.Summary.ConversationCount);
        Assert.Equal(1, sent.Summary.DeviceCount);
        Assert.Equal(1, sent.Summary.ActiveDays);
        Assert.Equal(Now, sent.Summary.WindowEndUtc);
    }

    [Fact]
    public async Task NoActivityInWindow_SendsNothing()
    {
        var (svc, root, notifier) = Create(enabled: true);
        await SeedParentWithActivityAsync(root, verified: true, [OutOfWindow]);

        await svc.RunTickAsync(Now, CancellationToken.None);

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task UnverifiedParent_SendsNothing()
    {
        var (svc, root, notifier) = Create(enabled: true);
        await SeedParentWithActivityAsync(root, verified: false, [InWindow]);

        await svc.RunTickAsync(Now, CancellationToken.None);

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task SecondTickWithinPeriod_DoesNotResend()
    {
        var (svc, root, notifier) = Create(enabled: true);
        await SeedParentWithActivityAsync(root, verified: true, [InWindow]);

        await svc.RunTickAsync(Now, CancellationToken.None);
        await svc.RunTickAsync(Now.AddDays(2), CancellationToken.None); // < 7 days later

        Assert.Single(notifier.Sent); // still just one
    }

    [Fact]
    public async Task FailedDelivery_NotRecorded_RetriesNextTick()
    {
        var (svc, root, notifier) = Create(enabled: true);
        notifier.ReturnValue = false; // delivery fails
        await SeedParentWithActivityAsync(root, verified: true, [InWindow]);

        await svc.RunTickAsync(Now, CancellationToken.None);
        Assert.Single(notifier.Sent); // attempted once

        notifier.ReturnValue = true;
        await svc.RunTickAsync(Now.AddHours(1), CancellationToken.None);
        Assert.Equal(2, notifier.Sent.Count); // retried because first wasn't recorded
    }

    [Fact]
    public async Task OnlyCountsParentsOwnDevices()
    {
        var (svc, root, notifier) = Create(enabled: true);
        // Parent A: 1 message in window. Parent B (separate device): 3 messages.
        await SeedParentWithActivityAsync(root, verified: true, [InWindow]);
        await SeedParentWithActivityAsync(
            root, verified: true, [InWindow, InWindow.AddHours(1), InWindow.AddHours(2)]);

        await svc.RunTickAsync(Now, CancellationToken.None);

        Assert.Equal(2, notifier.Sent.Count);
        Assert.Contains(notifier.Sent, s => s.Summary.MessageCount == 1);
        Assert.Contains(notifier.Sent, s => s.Summary.MessageCount == 3);
    }
}

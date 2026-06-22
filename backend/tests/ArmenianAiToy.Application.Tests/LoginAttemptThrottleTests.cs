using ArmenianAiToy.Application.Auth;
using Microsoft.Extensions.Time.Testing;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #040 — per-account login throttle. Drives time with FakeTimeProvider so
/// the rolling window + cooldown are deterministic.
/// </summary>
public class LoginAttemptThrottleTests
{
    private const string Email = "Parent@Example.com";

    private static LoginAttemptThrottle Make(FakeTimeProvider clock, int maxFailures = 3) =>
        new(clock, maxFailures: maxFailures,
            window: TimeSpan.FromMinutes(15), cooldown: TimeSpan.FromMinutes(15));

    [Fact]
    public void BelowThreshold_NotLocked()
    {
        var clock = new FakeTimeProvider();
        var t = Make(clock);
        Assert.False(t.RecordFailure(Email));
        Assert.False(t.RecordFailure(Email));
        Assert.False(t.IsLockedOut(Email));
    }

    [Fact]
    public void ReachingThreshold_Locks_AndTrippingFailureReportsTrue()
    {
        var clock = new FakeTimeProvider();
        var t = Make(clock);
        Assert.False(t.RecordFailure(Email));
        Assert.False(t.RecordFailure(Email));
        Assert.True(t.RecordFailure(Email));   // 3rd failure trips the lockout
        Assert.True(t.IsLockedOut(Email));
    }

    [Fact]
    public void Lockout_ExpiresAfterCooldown()
    {
        var clock = new FakeTimeProvider();
        var t = Make(clock);
        t.RecordFailure(Email); t.RecordFailure(Email); t.RecordFailure(Email);
        Assert.True(t.IsLockedOut(Email));

        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.False(t.IsLockedOut(Email));
    }

    [Fact]
    public void Success_ResetsCounter()
    {
        var clock = new FakeTimeProvider();
        var t = Make(clock);
        t.RecordFailure(Email);
        t.RecordFailure(Email);
        t.RecordSuccess(Email);          // clears the two failures
        Assert.False(t.RecordFailure(Email)); // count starts fresh
        Assert.False(t.IsLockedOut(Email));
    }

    [Fact]
    public void FailuresOutsideWindow_DoNotAccumulateToLockout()
    {
        var clock = new FakeTimeProvider();
        var t = Make(clock);
        t.RecordFailure(Email);
        clock.Advance(TimeSpan.FromMinutes(10));
        t.RecordFailure(Email);
        clock.Advance(TimeSpan.FromMinutes(10)); // first failure now > 15-min window
        // Only the 2nd + this one are within the window -> 2 < 3, no lockout.
        Assert.False(t.RecordFailure(Email));
        Assert.False(t.IsLockedOut(Email));
    }

    [Fact]
    public void Keying_IsCaseAndWhitespaceInsensitive()
    {
        var clock = new FakeTimeProvider();
        var t = Make(clock);
        t.RecordFailure("a@b.com");
        t.RecordFailure("  A@B.COM ");
        Assert.True(t.RecordFailure("A@b.com  ")); // same bucket -> trips on the 3rd
        Assert.True(t.IsLockedOut("a@b.com"));
    }
}

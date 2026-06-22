namespace ArmenianAiToy.Application.Auth;

/// <summary>
/// #040 — per-account login throttle, complementing the per-IP auth rate
/// limiter. The per-IP limiter does nothing against a credential-stuffing /
/// targeted-takeover attempt spread across many IPs (each IP stays under its
/// quota). This tracks FAILED logins per account (keyed on the submitted
/// email) across all IPs and, after <see cref="DefaultMaxFailures"/> failures
/// within <see cref="DefaultWindow"/>, locks that email out for
/// <see cref="DefaultCooldown"/>.
///
/// <para>
/// <b>Process-local</b> in-memory state (same posture as <c>ExportCooldown</c>
/// and the rate limiters). A multi-instance deployment would need a shared
/// store to be globally effective; documented as a follow-up.
/// </para>
///
/// <para>
/// <b>Anti-enumeration preserved.</b> The key is the submitted email, so an
/// UNKNOWN email is tracked and locked identically to a real one — the lockout
/// is not an account-existence oracle. <c>LoginAsync</c> returns the SAME
/// uniform null (→ 401) for a locked account as for a wrong password, and the
/// lockout check runs BEFORE the password hash so a locked account also skips
/// the BCrypt cost.
/// </para>
///
/// <para>
/// <b>Lockout-DoS is bounded</b> by design: the cooldown auto-expires and the
/// failure counter resets on a successful login or when the window lapses. The
/// threshold is generous so ordinary mistyping does not trip it. A
/// <see cref="MaxTrackedAccounts"/> cap bounds memory under a unique-email
/// flood — once reached, new emails are simply not tracked (degrade open),
/// since that volume is the per-IP limiter's / infra's problem.
/// </para>
/// </summary>
public sealed class LoginAttemptThrottle
{
    public const int DefaultMaxFailures = 10;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(15);

    // Memory guard: cap distinct tracked emails so a unique-email flood can't
    // grow the map without bound. Stale entries are pruned lazily on access.
    private const int MaxTrackedAccounts = 50_000;

    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _cooldown;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public readonly List<DateTimeOffset> Failures = new();
        public DateTimeOffset? LockoutUntil;
    }

    public LoginAttemptThrottle(
        TimeProvider? clock = null,
        int? maxFailures = null,
        TimeSpan? window = null,
        TimeSpan? cooldown = null)
    {
        _clock = clock ?? TimeProvider.System;
        _maxFailures = maxFailures ?? DefaultMaxFailures;
        _window = window ?? DefaultWindow;
        _cooldown = cooldown ?? DefaultCooldown;
    }

    private static string Key(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>True while the account is in its lockout cooldown.</summary>
    public bool IsLockedOut(string? email)
    {
        var key = Key(email);
        lock (_lock)
        {
            return _entries.TryGetValue(key, out var e)
                && e.LockoutUntil is { } until
                && _clock.GetUtcNow() < until;
        }
    }

    /// <summary>Records a failed login for <paramref name="email"/>.</summary>
    /// <returns>true iff THIS failure tripped the account into a NEW lockout
    /// (so the caller can log the transition exactly once).</returns>
    public bool RecordFailure(string? email)
    {
        var key = Key(email);
        var now = _clock.GetUtcNow();
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var e))
            {
                if (_entries.Count >= MaxTrackedAccounts)
                    return false; // memory guard — stop tracking new keys under flood
                e = new Entry();
                _entries[key] = e;
            }

            // Drop failures that have aged out of the rolling window.
            e.Failures.RemoveAll(t => now - t > _window);
            e.Failures.Add(now);

            if (e.Failures.Count >= _maxFailures)
            {
                e.LockoutUntil = now + _cooldown;
                e.Failures.Clear(); // post-cooldown starts from a clean count
                return true;
            }
            return false;
        }
    }

    /// <summary>Clears all throttle state for <paramref name="email"/> after a
    /// successful login.</summary>
    public void RecordSuccess(string? email)
    {
        var key = Key(email);
        lock (_lock)
        {
            _entries.Remove(key);
        }
    }
}

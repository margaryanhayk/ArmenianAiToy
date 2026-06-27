using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ArmenianAiToy.Application.Auth;

/// <summary>
/// Process-local store of short-lived, time-boxed operator console sessions.
///
/// <para>An operator exchanges their static credential (+ a TOTP code if MFA is
/// configured) for a session token via <c>POST /api/internal/session</c>. That
/// session token is what grants console access, and only until it expires —
/// so the static token alone confers NO standing data access (just-in-time,
/// time-boxed). A leaked static token without the second factor / a fresh
/// session is far less dangerous.</para>
///
/// <para>In-memory and process-local — same posture as <c>LoginAttemptThrottle</c>
/// and <c>ExportCooldown</c>. A multi-instance deployment would need a shared
/// store (Redis); noted, out of scope here. Registered as a DI singleton.</para>
/// </summary>
public sealed class OperatorSessionStore
{
    private sealed record Session(string Operator, DateTime ExpiresUtc);

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    /// <summary>Mint a session for <paramref name="operatorName"/> valid for
    /// <paramref name="ttl"/>. Returns the opaque session token.</summary>
    public string Issue(string operatorName, TimeSpan ttl, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        Prune(now);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new Session(operatorName, now.Add(ttl));
        return token;
    }

    /// <summary>Resolve a session token to its operator name, or null if the
    /// token is unknown or expired (expired entries are removed).</summary>
    public string? Resolve(string token, DateTime? nowUtc = null)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var s))
            return null;
        var now = nowUtc ?? DateTime.UtcNow;
        if (s.ExpiresUtc <= now)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }
        return s.Operator;
    }

    /// <summary>Explicitly end a session (operator sign-out).</summary>
    public void Revoke(string token)
    {
        if (!string.IsNullOrEmpty(token)) _sessions.TryRemove(token, out _);
    }

    private void Prune(DateTime now)
    {
        foreach (var kv in _sessions)
            if (kv.Value.ExpiresUtc <= now)
                _sessions.TryRemove(kv.Key, out _);
    }
}

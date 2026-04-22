using System.Collections.Concurrent;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Tiny per-parent cooldown guard for <c>GET /api/parents/export</c>.
/// Process-local <see cref="ConcurrentDictionary{TKey, TValue}"/> keyed
/// by parent id, same shape the repo already uses for short-lived
/// cross-request state (see <c>GameSession</c>, <c>RiddleSession</c>).
///
/// <para>
/// Why not the ASP.NET rate limiter: the existing
/// <c>ChatRateLimiter</c> is keyed by <c>X-Device-Id</c> header and
/// only makes sense for device traffic; the parent export endpoint
/// uses JWT auth instead. Reusing it would require a second policy
/// registration and a cross-header key. The export surface is small
/// enough that a ~20-line cooldown is the least surprising shape.
/// </para>
///
/// <para>
/// Slice 1 picks <see cref="DefaultWindow"/> = 60s per parent. The
/// export is an intentionally heavy operation (collates the parent's
/// whole scope into one document); a minute between downloads stops
/// tight loops and rough scripting without penalising an accidental
/// double-click in the dashboard slice that will follow.
/// </para>
///
/// <para>
/// Singleton lifetime. Process-local memory only — a restart clears
/// the map, which is fine because it just re-allows one more export
/// per parent. No persistence, no cross-instance coordination.
/// </para>
/// </summary>
public sealed class ExportCooldown
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _window;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastExportAt = new();

    public ExportCooldown() : this(DefaultWindow, TimeProvider.System) { }

    public ExportCooldown(TimeSpan window, TimeProvider clock)
    {
        _window = window;
        _clock = clock;
    }

    /// <summary>
    /// Attempts to reserve an export slot for <paramref name="parentId"/>.
    /// Returns <c>true</c> when the caller may proceed; returns
    /// <c>false</c> together with the <paramref name="retryAfter"/>
    /// duration remaining until the next attempt is allowed. On
    /// success the parent's last-export timestamp is updated atomically.
    /// </summary>
    public bool TryReserve(Guid parentId, out TimeSpan retryAfter)
    {
        var now = _clock.GetUtcNow();
        while (true)
        {
            if (_lastExportAt.TryGetValue(parentId, out var last))
            {
                var elapsed = now - last;
                if (elapsed < _window)
                {
                    retryAfter = _window - elapsed;
                    return false;
                }
                if (_lastExportAt.TryUpdate(parentId, now, last))
                {
                    retryAfter = TimeSpan.Zero;
                    return true;
                }
                // Lost a race — re-read and decide again.
                continue;
            }
            if (_lastExportAt.TryAdd(parentId, now))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }
            // Another caller added the key first — fall through and read it.
        }
    }
}

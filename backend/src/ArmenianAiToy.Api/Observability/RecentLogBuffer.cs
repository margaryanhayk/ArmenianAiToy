using System.Text.RegularExpressions;

namespace ArmenianAiToy.Api.Observability;

/// <summary>
/// In-memory ring buffer of the most recent Warning-and-above log entries,
/// served to the operator console (<c>GET /api/internal/logs</c>) so an
/// operator can see what just went wrong without opening Railway's log
/// viewer. Registered as an extra <see cref="ILoggerProvider"/>; the JSON
/// console logs are unchanged and stay the durable record — this buffer is
/// per-process and empties on every redeploy.
///
/// <para>
/// Defense in depth: every message and exception text is passed through
/// <see cref="Redact"/> before it is stored, so a bearer token, an
/// <c>sk-</c> key or a <c>key=</c> query value that ever reached a log line
/// never reaches the console. Nothing logged today carries one; this keeps
/// it that way if something ever does.
/// </para>
/// </summary>
public sealed class RecentLogBuffer : ILoggerProvider
{
    public sealed record Entry(
        DateTime AtUtc, string Level, string Category, string Message, string? Exception);

    private readonly object _lock = new();
    private readonly Entry[] _ring;
    private int _next;
    private int _count;
    private readonly LogLevel _minLevel;

    public RecentLogBuffer(int capacity = 500, LogLevel minLevel = LogLevel.Warning)
    {
        _ring = new Entry[Math.Max(1, capacity)];
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new BufferLogger(this, categoryName);

    public void Dispose() { }

    public void Add(Entry entry)
    {
        lock (_lock)
        {
            _ring[_next] = entry;
            _next = (_next + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }
    }

    /// <summary>Newest first, at most <paramref name="limit"/>, at or above
    /// <paramref name="minLevel"/>.</summary>
    public IReadOnlyList<Entry> Snapshot(LogLevel minLevel, int limit)
    {
        var result = new List<Entry>();
        lock (_lock)
        {
            for (var i = 0; i < _count && result.Count < limit; i++)
            {
                var e = _ring[(_next - 1 - i + _ring.Length) % _ring.Length];
                if (Enum.TryParse<LogLevel>(e.Level, out var lvl) && lvl >= minLevel)
                    result.Add(e);
            }
        }
        return result;
    }

    private static readonly (Regex Pattern, string Replacement)[] SecretPatterns =
    [
        (new(@"(?i)\b(bearer)\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled), "$1 ***"),
        (new(@"(?i)\b(api[_-]?key|key|token|secret|password)=[^&\s""']+", RegexOptions.Compiled), "$1=***"),
        (new(@"sk-[A-Za-z0-9_-]{8,}", RegexOptions.Compiled), "***"),
        (new(@"AIza[0-9A-Za-z_-]{20,}", RegexOptions.Compiled), "***"),
    ];

    /// <summary>Masks credential-shaped substrings. Pure; pinned by test.</summary>
    public static string Redact(string text)
    {
        foreach (var (pattern, replacement) in SecretPatterns)
            text = pattern.Replace(text, replacement);
        return text;
    }

    private sealed class BufferLogger(RecentLogBuffer owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message;
            try { message = formatter(state, exception); }
            catch { message = "(unformattable log message)"; }
            if (message.Length > 2000) message = message[..2000] + "…";
            var ex = exception is null ? null : Redact($"{exception.GetType().Name}: {exception.Message}");
            owner.Add(new Entry(DateTime.UtcNow, logLevel.ToString(), category, Redact(message), ex));
        }
    }
}

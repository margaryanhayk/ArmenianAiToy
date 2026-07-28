using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Canonicalizes and validates parent email addresses.
///
/// <para><b>Normalization</b> (<see cref="Normalize"/>): trim surrounding
/// whitespace and lowercase (invariant). Every write and every lookup must
/// go through this so one human mailbox maps to exactly one account — the
/// bug it fixes: <c>Foo@x.com</c> and <c>foo@x.com</c> (or a stray leading
/// space) previously created two separate rows, and a reset/verify for the
/// "wrong" casing silently never matched. The per-account login throttle
/// already lowercases its key, so this brings the account lookups in line
/// with it.</para>
///
/// <para><b>Validation</b> (<see cref="IsValid"/>): a deliberately simple
/// single-<c>@</c> shape check that also rejects control characters —
/// notably CR/LF, which would be an SMTP header-injection vector once a
/// real notifier replaces the logging stub. Inspects only the submitted
/// payload, so returning 400 on a malformed address leaks nothing about
/// the registered set (same posture as the existing empty/short-password
/// checks).</para>
/// </summary>
public static class EmailNormalizer
{
    // Intentionally permissive: exactly one @, non-empty local and domain,
    // a dot in the domain, and no whitespace/control chars anywhere.
    private static readonly Regex Shape = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var e = email.Trim();
        if (e.Length > 254) return false;                 // RFC 5321 practical cap
        foreach (var ch in e)
            if (char.IsControl(ch)) return false;         // blocks CR/LF/etc.
        return Shape.IsMatch(e);
    }
}

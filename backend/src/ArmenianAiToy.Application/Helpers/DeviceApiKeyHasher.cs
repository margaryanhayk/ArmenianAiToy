using System.Security.Cryptography;
using System.Text;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Versioned PBKDF2-SHA256 hashing for device API keys at rest.
///
/// Format (single line, ASCII):
///   v1:pbkdf2-sha256:&lt;iterations&gt;:&lt;saltBase64&gt;:&lt;hashBase64&gt;
///
/// The version prefix lets a future v2 (e.g. Argon2) coexist with v1 rows
/// without a flag-day migration. <see cref="IsHash"/> is the cheap
/// discriminator the auth path uses to decide between hashed storage and
/// the legacy plaintext fallback.
///
/// Design choices:
/// - PBKDF2-SHA256 over Argon2 because PBKDF2 ships in the BCL — no new
///   NuGet dependency.
/// - 50,000 iterations: ~15-25 ms per verify on a modern CPU. The chat
///   path is rate-limited to 30/min/device by <c>ChatRateLimiter</c> so
///   the worst-case auth cost per device-minute is ~0.75 s.
/// - 16-byte random salt per key. 32-byte derived hash.
/// - Device API keys carry 128 bits of entropy (<c>dtk_{Guid:N}</c>) so
///   brute-force is computationally infeasible regardless of iterations;
///   the iterations exist to slow down a DB-leak rainbow attempt
///   targeting a small number of high-value devices.
/// - Constant-time compare via <see cref="CryptographicOperations.FixedTimeEquals"/>
///   on the BYTE arrays — never the strings — to avoid early-exit
///   timing leaks during the byte-decode of the stored value.
///
/// This class is pure-static, has no I/O, no logging, no metrics.
/// </summary>
public static class DeviceApiKeyHasher
{
    public const string Version = "v1";
    public const string Algorithm = "pbkdf2-sha256";
    public const int Iterations = 50_000;
    public const int SaltBytes = 16;
    public const int HashBytes = 32;

    /// <summary>
    /// Produces the versioned hash string for a freshly-generated API key.
    /// The salt is a per-call random 16 bytes. Throws on null / empty input —
    /// callers must never persist a hash of an empty key.
    /// </summary>
    public static string Hash(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("apiKey must be non-empty", nameof(apiKey));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password: apiKey,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashBytes);

        return string.Concat(
            Version, ":",
            Algorithm, ":",
            Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture), ":",
            Convert.ToBase64String(salt), ":",
            Convert.ToBase64String(derived));
    }

    /// <summary>
    /// Returns true iff <paramref name="storedHash"/> is a well-formed
    /// v1 PBKDF2 hash. Used by the auth path to decide between
    /// <see cref="Verify"/> and the legacy plaintext fallback. Defensive
    /// against malformed / truncated DB rows.
    /// </summary>
    public static bool IsHash(string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        // Cheapest possible discriminator: the version prefix.
        return storedHash.StartsWith(Version + ":" + Algorithm + ":", StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a candidate API key against a stored v1 hash. Returns
    /// false for any malformed input, unsupported version, mismatched
    /// algorithm, parse failure, or non-matching hash. Constant-time on
    /// the byte-array compare path.
    /// </summary>
    public static bool Verify(string? apiKey, string? storedHash)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(storedHash))
            return false;
        if (!IsHash(storedHash)) return false;

        var parts = storedHash.Split(':');
        if (parts.Length != 5) return false;
        if (parts[0] != Version) return false;
        if (parts[1] != Algorithm) return false;
        if (!int.TryParse(parts[2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var iterations))
            return false;
        if (iterations < 1 || iterations > 10_000_000) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (salt.Length == 0 || expected.Length == 0) return false;

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password: apiKey,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: expected.Length);

        return CryptographicOperations.FixedTimeEquals(derived, expected);
    }

    /// <summary>
    /// Constant-time equality for two ASCII strings of arbitrary length.
    /// Used by the legacy plaintext fallback path so an attacker timing
    /// the response cannot learn how many leading characters of their
    /// candidate match the stored plaintext.
    /// </summary>
    public static bool ConstantTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}

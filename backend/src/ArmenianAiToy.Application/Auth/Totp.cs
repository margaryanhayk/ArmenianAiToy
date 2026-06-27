using System.Security.Cryptography;

namespace ArmenianAiToy.Application.Auth;

/// <summary>
/// Minimal RFC 6238 TOTP verifier (HMAC-SHA1, 30-second step, 6 digits) for
/// operator MFA on the internal console. Uses only BCL crypto — no external
/// dependency. Verifies against a small window (default ±1 step) to tolerate
/// clock skew between the server and the authenticator app. Compatible with
/// Google Authenticator / 1Password / Authy (the standard otpauth:// TOTP).
/// </summary>
public static class Totp
{
    /// <summary>
    /// True if <paramref name="code"/> is a valid TOTP for <paramref name="base32Secret"/>
    /// at <paramref name="utcNow"/> (within ±<paramref name="window"/> steps).
    /// Returns false on any malformed input rather than throwing.
    /// </summary>
    public static bool Verify(
        string base32Secret, string code, DateTime utcNow,
        int window = 1, int digits = 6, int stepSeconds = 30)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
            return false;
        code = code.Trim();
        byte[] key;
        try { key = Base32Decode(base32Secret); }
        catch { return false; }
        if (key.Length == 0) return false;

        var counter = (long)Math.Floor((utcNow - DateTime.UnixEpoch).TotalSeconds / stepSeconds);
        for (var c = counter - window; c <= counter + window; c++)
        {
            if (FixedTimeEquals(Compute(key, c, digits), code))
                return true;
        }
        return false;
    }

    private static string Compute(byte[] key, long counter, int digits)
    {
        var ctr = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(ctr); // RFC: big-endian counter
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(ctr);
        var offset = hash[^1] & 0x0f;
        var bin = ((hash[offset] & 0x7f) << 24)
                  | (hash[offset + 1] << 16)
                  | (hash[offset + 2] << 8)
                  | hash[offset + 3];
        var otp = bin % (int)Math.Pow(10, digits);
        return otp.ToString().PadLeft(digits, '0');
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static byte[] Base32Decode(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        s = s.Trim().TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        if (s.Length == 0) return Array.Empty<byte>();

        int bits = 0, value = 0;
        var output = new List<byte>(s.Length * 5 / 8);
        foreach (var c in s)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) throw new FormatException("invalid base32 character");
            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}

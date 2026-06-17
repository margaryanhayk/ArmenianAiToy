using System.Security.Cryptography;
using System.Text;

namespace ArmenianAiToy.Api.Security;

/// <summary>
/// Stateless, signed, short-lived access token for the header-less
/// <c>GET /api/story-audio</c> stream. The firmware's audio HTTP client
/// cannot set <c>X-Device-Id</c> / <c>X-Api-Key</c> headers, so the
/// stream is gated by a token carried in the query string
/// (<c>?token=...</c>) instead.
///
/// <para>
/// The token is <c>base64url(payload) + "." + base64url(HMAC-SHA256(payload))</c>,
/// where <c>payload = "{storyId}|{expiresAtUnixSeconds}"</c>. It is
/// minted by the device-authed <c>GET /api/chat/story-audio-token</c>
/// endpoint and verified here with no DB lookup and no server-side
/// state — a tampered, expired, or wrong-story token simply fails the
/// constant-time MAC / expiry / binding checks.
/// </para>
///
/// <para>
/// The signing key is <c>StoryAudio:SigningKey</c>. When it is empty the
/// feature is OFF and the stream is open (dev/bench) — enforcement is an
/// opt-in config flip, mirroring the <c>/metrics</c> guard posture.
/// </para>
/// </summary>
public static class StoryAudioToken
{
    /// <summary>Mints a token for <paramref name="storyId"/> valid until
    /// <paramref name="expiresAt"/>, signed with
    /// <paramref name="signingKey"/>.</summary>
    public static string Issue(string storyId, DateTimeOffset expiresAt, string signingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        var payloadBytes = Encoding.UTF8.GetBytes(
            $"{storyId}|{expiresAt.ToUnixTimeSeconds()}");
        var mac = Hmac(payloadBytes, signingKey);
        return $"{Base64Url(payloadBytes)}.{Base64Url(mac)}";
    }

    /// <summary>True iff <paramref name="token"/> is a well-formed token
    /// whose signature verifies against <paramref name="signingKey"/>,
    /// is bound to <paramref name="expectedStoryId"/>, and has not
    /// expired as of <paramref name="nowUtc"/>. Never throws — any
    /// malformed input returns false.</summary>
    public static bool TryValidate(
        string? token, string expectedStoryId, string signingKey, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(signingKey))
        {
            return false;
        }

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] providedMac;
        try
        {
            payloadBytes = FromBase64Url(token[..dot]);
            providedMac = FromBase64Url(token[(dot + 1)..]);
        }
        catch
        {
            return false;
        }

        var expectedMac = Hmac(payloadBytes, signingKey);
        // Constant-time compare; FixedTimeEquals also returns false for
        // length mismatch, so a truncated MAC cannot short-circuit.
        if (!CryptographicOperations.FixedTimeEquals(providedMac, expectedMac))
        {
            return false;
        }

        // Signature is authentic — now read the bound claims. Split on the
        // LAST '|' so the (kebab-case, '|'-free) story id is recovered even
        // defensively.
        var payload = Encoding.UTF8.GetString(payloadBytes);
        var sep = payload.LastIndexOf('|');
        if (sep <= 0 || sep >= payload.Length - 1)
        {
            return false;
        }

        var storyId = payload[..sep];
        if (!string.Equals(storyId, expectedStoryId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(payload[(sep + 1)..], out var expiresAtUnix))
        {
            return false;
        }

        return nowUtc.ToUnixTimeSeconds() <= expiresAtUnix;
    }

    private static byte[] Hmac(byte[] data, string signingKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return hmac.ComputeHash(data);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}

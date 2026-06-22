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
/// where <c>payload = "{storyId}|{expiresAtUnixSeconds}|{boundIp}"</c>. It
/// is minted by the device-authed <c>GET /api/chat/story-audio-token</c>
/// endpoint and verified here with no DB lookup and no server-side
/// state — a tampered, expired, wrong-story, or wrong-IP token simply
/// fails the constant-time MAC / expiry / binding checks.
/// </para>
///
/// <para>
/// <b>#038 — optional IP binding.</b> <c>boundIp</c> is empty for an
/// unbound token (the historical shape, which also keeps legacy 2-field
/// tokens parseable across a deploy). When the issuing endpoint binds the
/// token to the caller's IP (<c>StoryAudio:BindTokenToIp=true</c>), the
/// token CARRIES that IP and the stream enforces it: a leaked token then
/// only streams from the same source IP, directly closing the "works from
/// anywhere for the TTL" exposure. Device-id binding is deliberately NOT
/// used because the stream is header-less — the device id would have to
/// travel in the same query string as the token, so a leaked URL would
/// carry both and the binding would add no protection. The client IP is
/// the one request-intrinsic signal a leaked URL does NOT reproduce, and
/// it is trustworthy behind a proxy now that ForwardedHeaders is wired
/// (#039). The verifier ALWAYS supplies its request IP and enforces it
/// iff the token carries one, so the token is self-describing and the
/// stream needs no config flag.
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
    /// <paramref name="signingKey"/>. When <paramref name="boundIp"/> is
    /// non-empty the token is bound to that source IP (#038) and the stream
    /// will only accept it from the same IP; null/empty issues an unbound
    /// token (historical behavior).</summary>
    public static string Issue(
        string storyId, DateTimeOffset expiresAt, string signingKey, string? boundIp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        var payloadBytes = Encoding.UTF8.GetBytes(
            $"{storyId}|{expiresAt.ToUnixTimeSeconds()}|{boundIp}");
        var mac = Hmac(payloadBytes, signingKey);
        return $"{Base64Url(payloadBytes)}.{Base64Url(mac)}";
    }

    /// <summary>True iff <paramref name="token"/> is a well-formed token
    /// whose signature verifies against <paramref name="signingKey"/>,
    /// is bound to <paramref name="expectedStoryId"/>, has not expired as
    /// of <paramref name="nowUtc"/>, and — when the token carries a bound
    /// IP (#038) — was presented from <paramref name="requestIp"/>. The
    /// caller should always pass its request IP; enforcement happens only
    /// for tokens that were issued IP-bound, so an unbound token ignores
    /// it. Never throws — any malformed input returns false.</summary>
    public static bool TryValidate(
        string? token, string expectedStoryId, string signingKey, DateTimeOffset nowUtc,
        string? requestIp = null)
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

        // Signature is authentic — now read the bound claims. The story id
        // is the validated kebab-case library id (no '|'), so a positional
        // split is safe. Field 2 (bound IP) is optional: absent on legacy
        // 2-field tokens and empty on unbound tokens.
        var payload = Encoding.UTF8.GetString(payloadBytes);
        var fields = payload.Split('|');
        if (fields.Length < 2)
        {
            return false;
        }

        var storyId = fields[0];
        if (storyId.Length == 0
            || !string.Equals(storyId, expectedStoryId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(fields[1], out var expiresAtUnix))
        {
            return false;
        }
        if (nowUtc.ToUnixTimeSeconds() > expiresAtUnix)
        {
            return false;
        }

        // #038 — IP binding. An empty bound IP (unbound / legacy token) is
        // not enforced. A non-empty bound IP must match the request IP, so a
        // leaked token cannot be replayed from a different source.
        var boundIp = fields.Length >= 3 ? fields[2] : string.Empty;
        if (boundIp.Length > 0
            && !string.Equals(boundIp, requestIp, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
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

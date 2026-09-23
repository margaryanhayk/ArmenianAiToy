using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArmenianAiToy.Infrastructure.Ai;

/// <summary>
/// OAuth access tokens for Vertex AI from a Google service-account key,
/// via the standard JWT-bearer grant (RFC 7523): sign a short-lived RS256
/// assertion with the key's private key, exchange it at the key's
/// <c>token_uri</c>, cache the access token until shortly before expiry.
///
/// <para>
/// Why this exists: the Gemini API (AI Studio) terms exclude services
/// directed at under-18s, while Vertex AI runs under the Google Cloud
/// terms — the likely compliant route for a children's toy
/// (<c>docs/legal/vendor-terms-and-ai-toy-laws-2026-09.md</c> § 1).
/// Raw BCL only (<see cref="RSA"/> + <see cref="HttpClient"/>) — no
/// Google SDK NuGet, same posture as the Gemini/ElevenLabs adapters.
/// The private key and the tokens are never logged.
/// </para>
/// </summary>
public sealed class VertexAiAccessTokenProvider : IDisposable
{
    public const string Scope = "https://www.googleapis.com/auth/cloud-platform";
    private static readonly TimeSpan AssertionLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly string _clientEmail;
    private readonly string _tokenUri;
    private readonly RSA _key;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public VertexAiAccessTokenProvider(
        HttpClient http, string serviceAccountJson, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var (email, pem, tokenUri) = ParseServiceAccount(serviceAccountJson);
        _clientEmail = email;
        _tokenUri = tokenUri;
        _key = RSA.Create();
        _key.ImportFromPem(pem);
    }

    /// <summary>
    /// Pulls the three fields the grant needs out of a service-account key
    /// file. Throws <see cref="InvalidOperationException"/> naming the
    /// missing field — never echoing the JSON, which holds the private key.
    /// </summary>
    public static (string ClientEmail, string PrivateKeyPem, string TokenUri) ParseServiceAccount(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "Gemini:Vertex:ServiceAccountJson is not valid JSON (contents not logged).");
        }
        using (doc)
        {
            string Field(string name) =>
                doc.RootElement.TryGetProperty(name, out var v)
                && v.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(v.GetString())
                    ? v.GetString()!
                    : throw new InvalidOperationException(
                        $"Gemini:Vertex:ServiceAccountJson is missing '{name}'.");

            var tokenUri = doc.RootElement.TryGetProperty("token_uri", out var t)
                           && t.ValueKind == JsonValueKind.String
                           && !string.IsNullOrWhiteSpace(t.GetString())
                ? t.GetString()!
                : "https://oauth2.googleapis.com/token";
            return (Field("client_email"), Field("private_key"), tokenUri);
        }
    }

    /// <summary>
    /// Builds the signed RS256 JWT assertion. Public so tests can verify
    /// the header, claims and signature against the key's public half.
    /// </summary>
    public string BuildAssertion(DateTimeOffset now)
    {
        static string B64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _clientEmail,
            scope = Scope,
            aud = _tokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(AssertionLifetime).ToUnixTimeSeconds(),
        }));
        var signingInput = $"{header}.{claims}";
        var signature = _key.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{B64Url(signature)}";
    }

    /// <summary>Returns a cached token, refreshing it inside the last
    /// five minutes of its life. One refresh at a time.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var now = _clock();
        if (_token is not null && now < _expiresAt - RefreshMargin)
            return _token;

        await _lock.WaitAsync(ct);
        try
        {
            now = _clock();
            if (_token is not null && now < _expiresAt - RefreshMargin)
                return _token;

            using var req = new HttpRequestMessage(HttpMethod.Post, _tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = BuildAssertion(now),
                }),
            };
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                // Status only — the error body can name the account.
                throw new HttpRequestException(
                    $"Vertex AI token exchange returned HTTP {(int)resp.StatusCode}.",
                    inner: null, statusCode: resp.StatusCode);
            }

            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Vertex AI token response had no access_token.");
            var lifetime = doc.RootElement.TryGetProperty("expires_in", out var e)
                           && e.TryGetInt32(out var secs) ? secs : 3600;
            _token = token;
            _expiresAt = now.AddSeconds(lifetime);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _key.Dispose();
        _lock.Dispose();
    }
}

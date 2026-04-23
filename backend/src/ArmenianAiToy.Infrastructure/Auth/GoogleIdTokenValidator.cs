using ArmenianAiToy.Application.Auth;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Infrastructure.Auth;

/// <summary>
/// Production <see cref="IGoogleIdTokenValidator"/> backed by
/// <c>Google.Apis.Auth.GoogleJsonWebSignature</c>. Handles Google
/// JWKS fetching, <c>kid</c> rotation, <c>iss</c>/<c>aud</c>/<c>exp</c>
/// verification — all of the RS256-verification boilerplate this repo
/// should not author itself.
/// <para>
/// Returns <c>null</c> on any failure so the upstream service layer
/// collapses every rejection reason to the same
/// <see cref="Application.DTOs.GoogleSignInStatus.InvalidToken"/>
/// outcome. Reasons are logged at Information level without the raw
/// token so operators can diagnose misconfiguration without leaking
/// credential material.
/// </para>
/// </summary>
public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleIdTokenValidator> _logger;

    public GoogleIdTokenValidator(
        IConfiguration config,
        ILogger<GoogleIdTokenValidator> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<GoogleIdentity?> ValidateAsync(
        string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return null;

        var clientId = _config["GoogleAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var skew = ReadClockSkewSeconds();

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId },
                IssuedAtClockTolerance = TimeSpan.FromSeconds(skew),
                ExpirationTimeClockTolerance = TimeSpan.FromSeconds(skew)
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email))
                return null;

            return new GoogleIdentity(
                Subject: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified,
                Audience: clientId);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogInformation(
                "Google ID token rejected: {Reason}", ex.Message);
            return null;
        }
    }

    private const int DefaultClockSkewSeconds = 300;
    private int ReadClockSkewSeconds()
    {
        var raw = _config["GoogleAuth:ClockSkewSeconds"];
        return int.TryParse(raw, out var parsed) && parsed >= 0
            ? parsed
            : DefaultClockSkewSeconds;
    }
}

namespace ArmenianAiToy.Application.Auth;

/// <summary>
/// Seam over Google's ID-token verification. Production impl in
/// <c>Infrastructure/Auth/GoogleIdTokenValidator.cs</c> wraps
/// <c>Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync</c>; test
/// doubles return canned <see cref="GoogleIdentity"/> values so unit
/// tests never hit real Google endpoints.
/// <para>
/// Contract: return a populated <see cref="GoogleIdentity"/> iff the
/// token is cryptographically valid AND its audience matches the
/// configured <c>GoogleAuth:ClientId</c>. Return <c>null</c> on any
/// failure (invalid signature, expired, audience mismatch, malformed,
/// missing/empty configured client id). The <see cref="GoogleIdentity.EmailVerified"/>
/// flag is passed through unchanged — the caller (ParentService) is
/// responsible for rejecting unverified emails.
/// </para>
/// </summary>
public interface IGoogleIdTokenValidator
{
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal claims projection from a verified Google ID token. Only
/// the fields the sign-in flow actually consumes — no profile, no
/// picture, no given/family name, no hosted domain.
/// </summary>
public record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string Audience);

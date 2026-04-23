namespace ArmenianAiToy.Application.DTOs;

public record ParentRegisterRequest(string Email, string Password, bool AcceptedTerms = false);
public record ParentLoginRequest(string Email, string Password);
public record ParentLoginResponse(string Token);
public record LinkDeviceRequest(Guid DeviceId, string ApiKey);
public record ParentChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ParentDeleteAccountRequest(string CurrentPassword);
public record ParentPasswordResetRequest(string Email);
public record ParentPasswordResetCompleteRequest(string Token, string NewPassword);
public record ParentEmailVerificationRequest(string Email);
public record ParentEmailVerificationCompleteRequest(string Token);

/// <summary>
/// Minimal authenticated-parent profile shape exposed by
/// <c>GET /api/parents/me</c>. Two fields only — the parent's email
/// (so the dashboard's "Send verification email" button can pass it
/// to <c>POST /api/parents/verify-request</c> without a form input)
/// and the verification timestamp (already on
/// <c>DormancySummaryDto</c>; included here for one-stop access by
/// any future small dashboard surface that needs both). Not a
/// general "profile" surface — no name, no preferences, no
/// settings — and not a place to start adding more fields.
/// </summary>
public record ParentMeResponse(string Email, DateTime? EmailVerifiedAt);

/// <summary>
/// Request body for <c>POST /api/parents/google-login</c>. The id
/// token is the opaque JWT Google Identity Services returns from the
/// one-tap / button callback; the server validates signature and
/// audience before trusting any claim inside. <c>AcceptedTerms</c> is
/// only consulted for first-time signups (unknown email); returning
/// users and existing parents with <c>TermsAcceptedAt != null</c>
/// bypass the check.
/// </summary>
public record ParentGoogleLoginRequest(string IdToken, bool AcceptedTerms = false);

/// <summary>
/// Internal service-to-controller result from
/// <c>IParentService.GoogleSignInAsync</c>. The controller maps the
/// three cases to 200 / 401 / 400 — failure reasons are deliberately
/// not disambiguated on the wire.
/// </summary>
public enum GoogleSignInStatus
{
    /// <summary>Token valid, identity accepted, JWT issued.</summary>
    Success,

    /// <summary>
    /// Any of: invalid token, unverified email, audience mismatch,
    /// GoogleSubject collision on an email-matched parent. All four
    /// collapse to a single uniform auth-failure on the wire.
    /// </summary>
    InvalidToken,

    /// <summary>
    /// First-time signup / first-time link path, but
    /// <see cref="ParentGoogleLoginRequest.AcceptedTerms"/> was false
    /// and the parent row has no prior terms acceptance. Mapped to
    /// 400 so the UI can surface a "please accept the terms" message.
    /// </summary>
    TermsRequired
}

public record GoogleSignInResult(GoogleSignInStatus Status, string? Token);

/// <summary>
/// Response body for the public <c>GET /api/parents/google-config</c>
/// probe. Exposes the UI-visible Google client id so the dashboard
/// can decide whether to render the "Continue with Google" button.
/// An empty / null client id means the feature is disabled; the
/// dashboard hides the button.
/// </summary>
public record GoogleAuthConfigResponse(string? ClientId);

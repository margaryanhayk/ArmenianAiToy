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

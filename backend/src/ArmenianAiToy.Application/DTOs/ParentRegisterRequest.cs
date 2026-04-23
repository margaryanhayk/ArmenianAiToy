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

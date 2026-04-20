using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Parent authentication and device-linking endpoints.
/// </summary>
[ApiController]
[Route("api/parents")]
public class ParentController : ControllerBase
{
    private readonly IParentService _parentService;

    public ParentController(IParentService parentService)
    {
        _parentService = parentService;
    }

    /// <summary>
    /// Register a new parent account.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Register([FromBody] ParentRegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        const int MinPasswordLength = 8;
        if (request.Password.Length < MinPasswordLength)
            return BadRequest(new { error = "Password must be at least 8 characters." });

        try
        {
            var parentId = await _parentService.RegisterAsync(request.Email, request.Password);
            return Created("", new { parentId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Log in and receive a JWT token.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Login([FromBody] ParentLoginRequest request)
    {
        var result = await _parentService.LoginAsync(request.Email, request.Password);
        if (result == null)
            return Unauthorized(new { error = "Invalid email or password" });

        return Ok(result);
    }

    /// <summary>
    /// Change the authenticated parent's own password. Requires the current
    /// password for re-authentication. New password must satisfy the same
    /// length rule as registration (≥ 8 chars).
    /// </summary>
    [HttpPost("password")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ChangePassword([FromBody] ParentChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Current password and new password are required." });

        // Same minimum length as ParentController.Register (ParentController.cs:35).
        const int MinPasswordLength = 8;
        if (request.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { error = "New password must be at least 8 characters." });

        if (request.CurrentPassword == request.NewPassword)
            return BadRequest(new { error = "New password must be different from the current password." });

        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var changed = await _parentService.ChangePasswordAsync(
            parentId, request.CurrentPassword, request.NewPassword);

        if (!changed)
            return BadRequest(new { error = "Current password is incorrect." });

        return Ok(new { changed = true });
    }

    /// <summary>
    /// Link an existing device to the authenticated parent.
    /// </summary>
    [HttpPost("devices/link")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> LinkDevice([FromBody] LinkDeviceRequest request)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linked = await _parentService.LinkDeviceAsync(parentId, request.DeviceId, request.ApiKey);

        if (!linked)
            return BadRequest(new { error = "Invalid device ID or API key" });

        return Ok(new { linked = true });
    }

    /// <summary>
    /// Unlink a device from the authenticated parent account. Idempotent —
    /// the response is identical whether a link existed or not, so a caller
    /// cannot probe whether a given (parent, device) pair is real. Removes
    /// only the join row; the Device, its Children, and its Conversations
    /// are preserved, and any other parents linked to the same device keep
    /// their link.
    /// </summary>
    [HttpDelete("devices/{deviceId}/link")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UnlinkDevice(Guid deviceId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _parentService.UnlinkDeviceAsync(parentId, deviceId);
        return Ok(new { unlinked = true });
    }

    /// <summary>
    /// List linked devices with child info and last activity.
    /// </summary>
    [HttpGet("devices/details")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDeviceDetails()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var devices = await _parentService.GetLinkedDeviceDetailsAsync(parentId);
        return Ok(new { devices });
    }

    /// <summary>
    /// List device IDs linked to the authenticated parent.
    /// </summary>
    [HttpGet("devices")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDevices()
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deviceIds = await _parentService.GetLinkedDeviceIdsAsync(parentId);
        return Ok(new { devices = deviceIds });
    }
}

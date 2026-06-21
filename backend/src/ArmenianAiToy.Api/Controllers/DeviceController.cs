using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Api.Security;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Api.Controllers;

[ApiController]
[Route("api/devices")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly IConfiguration _config;

    public DeviceController(IDeviceService deviceService, IConfiguration config)
    {
        _deviceService = deviceService;
        _config = config;
    }

    // #010: rate-limited on the per-IP `auth` bucket (10/60s).
    // #009: provisioning gate — fail-closed. Registration mints a credential that
    //   can drive the paid STT+GPT+TTS endpoints, so anonymous minting is a
    //   denial-of-wallet vector. Requires the Devices:ProvisioningSecret in the
    //   X-Provisioning-Secret header, OR the explicit Devices:AllowOpenRegistration
    //   dev/bench bypass; the shipped default (neither) DENIES.
    // #011: an EXISTING device is rotated only on an explicit X-Force-Rotate
    //   request — a plain re-registration is refused (409) so a caller cannot
    //   silently rotate an in-field device's key.
    [HttpPost("register")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(409)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Register([FromBody] DeviceRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.MacAddress))
            return BadRequest(new { error = "MacAddress is required" });

        var presented = Request.Headers[DeviceProvisioningAuth.SecretHeader].ToString();
        var decision = DeviceProvisioningAuth.Evaluate(
            string.IsNullOrEmpty(presented) ? null : presented,
            _config["Devices:ProvisioningSecret"],
            bool.TryParse(_config["Devices:AllowOpenRegistration"], out var open) && open);
        if (decision == DeviceProvisioningAuth.Decision.Deny)
            return Unauthorized(new { error = "Device registration is not permitted." });

        var force = bool.TryParse(Request.Headers["X-Force-Rotate"].ToString(), out var f) && f;
        var result = await _deviceService.RegisterDeviceAsync(request, allowReRegister: force);
        if (result is null)
            return Conflict(new
            {
                error = "Device already registered. Re-provision with X-Force-Rotate: true to rotate its key."
            });
        return Created("", result);
    }
}

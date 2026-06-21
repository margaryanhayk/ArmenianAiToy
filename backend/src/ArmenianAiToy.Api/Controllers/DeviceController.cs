using ArmenianAiToy.Api.RateLimiting;
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

    public DeviceController(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    // #010: rate-limit device registration on the per-IP `auth` bucket (10/60s).
    // Registration mints a working device credential that can drive the paid
    // STT+GPT+TTS endpoints, so an unthrottled endpoint is a denial-of-wallet
    // vector (mass device-minting). This caps the mint rate per caller IP. NOTE:
    // it does NOT make registration authenticated — closing that fully is the
    // provisioning-secret work (#009) + firmware provisioning (#043).
    [HttpPost("register")]
    [EnableRateLimiting(AuthRateLimiter.PolicyName)]
    public async Task<IActionResult> Register([FromBody] DeviceRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MacAddress))
            return BadRequest(new { error = "MacAddress is required" });

        var result = await _deviceService.RegisterDeviceAsync(request);
        return Created("", result);
    }
}

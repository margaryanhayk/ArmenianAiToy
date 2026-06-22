using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Controllers;

[ApiController]
[Route("api/children")]
public class ChildController : ControllerBase
{
    private readonly IChildService _childService;
    private readonly IParentService _parentService;

    public ChildController(IChildService childService, IParentService parentService)
    {
        _childService = childService;
        _parentService = parentService;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateChild([FromBody] CreateChildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        // Verify parent owns this device
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(request.DeviceId))
            return Forbid();

        // #066: collect only the birth YEAR, never the exact date of birth.
        // Validate it's a plausible year (rejects typos; we never store a day/month).
        if (request.BirthYear is { } by && (by < 1900 || by > DateTime.UtcNow.Year))
            return BadRequest(new { error = "BirthYear must be a valid year (yyyy)." });

        var child = await _childService.CreateChildAsync(
            request.DeviceId, request.Name, request.Gender, request.BirthYear);

        return Created("", new
        {
            childId = child.Id,
            name = child.Name,
            gender = child.Gender.ToString(),
            age = child.GetAge(),
            birthYear = child.BirthYear,
            deviceId = child.DeviceId
        });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetChildren([FromQuery] Guid deviceId)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(deviceId))
            return Forbid();

        var children = await _childService.GetChildrenByDeviceAsync(deviceId);
        return Ok(new
        {
            children = children.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                gender = c.Gender.ToString(),
                age = c.GetAge(),
                birthYear = c.BirthYear
            })
        });
    }
}

public record CreateChildRequest(
    Guid DeviceId,
    string Name,
    Gender Gender,
    int? BirthYear = null);

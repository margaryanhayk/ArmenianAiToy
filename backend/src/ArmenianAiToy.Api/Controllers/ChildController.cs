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

        DateOnly? dob = null;
        if (!string.IsNullOrEmpty(request.DateOfBirth))
            dob = DateOnly.Parse(request.DateOfBirth);

        var child = await _childService.CreateChildAsync(request.DeviceId, request.Name, request.Gender, dob);

        return Created("", new
        {
            childId = child.Id,
            name = child.Name,
            gender = child.Gender.ToString(),
            age = child.GetAge(),
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
                dateOfBirth = c.DateOfBirth?.ToString("yyyy-MM-dd")
            })
        });
    }
}

public record CreateChildRequest(
    Guid DeviceId,
    string Name,
    Gender Gender,
    string? DateOfBirth = null);

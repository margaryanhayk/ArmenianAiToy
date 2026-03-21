using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ArmenianAiToy.Api.Controllers;

[ApiController]
[Route("api/conversations")]
public class ConversationController : ControllerBase
{
    private readonly IConversationService _conversationService;
    private readonly IParentService _parentService;

    public ConversationController(IConversationService conversationService, IParentService parentService)
    {
        _conversationService = conversationService;
        _parentService = parentService;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid deviceId,
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0)
    {
        var parentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var linkedDevices = await _parentService.GetLinkedDeviceIdsAsync(parentId);

        if (!linkedDevices.Contains(deviceId))
            return Forbid();

        var conversations = await _conversationService.GetConversationHistoryAsync(deviceId, limit, offset);
        return Ok(new { conversations });
    }
}

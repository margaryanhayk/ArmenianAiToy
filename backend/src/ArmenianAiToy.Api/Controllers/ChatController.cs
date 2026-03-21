using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ArmenianAiToy.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required" });

        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;

        try
        {
            var response = await _chatService.GetResponseAsync(deviceId, request.Message, request.ChildId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = $"AI service error: {ex.Message}" });
        }
    }
}

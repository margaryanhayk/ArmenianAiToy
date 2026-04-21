using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Api.Controllers;

/// <summary>
/// Chat with the AI toy. Requires device auth headers: X-Device-Id and X-Api-Key.
/// </summary>
[ApiController]
[Route("api/chat")]
[EnableRateLimiting(ChatRateLimiter.PolicyName)]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IDeviceService _deviceService;

    // Child-facing canned reply when a parent has paused the device. Kept
    // short, age-appropriate, and tells the child to involve their parent.
    // Never passed through AI / moderation — it is a constant.
    internal const string PausedResponse =
        "\u0540\u056b\u0574\u0561 \u0570\u0561\u0576\u0563\u057d\u057f\u0561\u0576\u0578\u0582\u0574 \u0565\u0574\u0589 \u053e\u0576\u0578\u0572\u056b\u0564 \u056f\u0561\u0580\u0578\u0572 \u0567 \u0576\u0578\u0580\u056b\u0581 \u0574\u056b\u0561\u0581\u0576\u0565\u056c\u0589";
        // «Հիմա հանգստանում եմ։ Ծնողդ կարող է նորից միացնել։»

    public ChatController(IChatService chatService, IDeviceService deviceService)
    {
        _chatService = chatService;
        _deviceService = deviceService;
    }

    /// <summary>
    /// Send a message from device and receive AI response.
    /// </summary>
    /// <remarks>
    /// Requires headers:
    /// - **X-Device-Id**: Device GUID from registration
    /// - **X-Api-Key**: Device API key from registration
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required" });

        var deviceId = (Guid)HttpContext.Items["DeviceId"]!;

        // Pause gate — runs before any ChatService call, so a paused device
        // never reaches moderation, chat generation, or conversation writes.
        // The response envelope uses SafetyFlag.Clean (not Blocked/Flagged)
        // because this is a parent-initiated soft-off, not a safety event.
        //
        // B4 bedtime window joins the same short-circuit as a second gate.
        // Pause wins: we check pause first and skip the bedtime query when
        // the device is already paused.
        if (await _deviceService.IsDevicePausedAsync(deviceId) ||
            await _deviceService.IsDeviceInBedtimeWindowAsync(deviceId, DateTime.UtcNow))
        {
            return Ok(new ChatResponse(PausedResponse, Guid.Empty, Guid.Empty, SafetyFlag.Clean));
        }

        try
        {
            var response = await _chatService.GetResponseAsync(deviceId, request.Message, request.ChildId,
                request.StorySessionId, request.SelectedChoice);
            return Ok(response);
        }
        catch (Exception)
        {
            // Path-5 upstream completion failure. Details are logged
            // server-side at ChatService.cs:1244; the wire response
            // intentionally carries a constant, sanitized string so
            // the device / client never sees raw exception messages
            // (which for OpenAI SDK classes can include request-ids,
            // URLs, or other internal detail).
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
    }
}

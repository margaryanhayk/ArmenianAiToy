using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Application.Helpers;
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

    // B5: canned reply when the parent has disabled the mode the child's
    // current message is asking for. Deliberately distinct from PausedResponse
    // so the two gates are separately identifiable in any future log/audit
    // inspection. Same envelope shape; no AI call; SafetyFlag.Clean.
    internal const string ModeDisabledResponse =
        "Եկ մի ուրիշ բան փորձենք։";
        // «Եկ մի ուրիշ բան փորձենք։» ("Let's try something else.")

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

        // Paused / bedtime / mode-disabled gate. Shared with the voice
        // chat endpoint via ChatGateEvaluator — same ordering, same
        // semantics, same ModeDetector parameters the text path shipped
        // with. AppMeter tags stay here (the two callers emit metrics
        // differently and keeping them out of the evaluator keeps it
        // pure). Response envelope uses SafetyFlag.Clean for all three
        // gates — parent-initiated soft-offs are not safety events.
        var gate = await ChatGateEvaluator.EvaluateAsync(
            _deviceService, deviceId, request.Message, request.ChildId, DateTime.UtcNow);
        switch (gate)
        {
            case ChatGateEvaluator.GateDecision.Paused:
                AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "paused"));
                return Ok(new ChatResponse(PausedResponse, Guid.Empty, Guid.Empty, SafetyFlag.Clean));
            case ChatGateEvaluator.GateDecision.Bedtime:
                AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "bedtime"));
                return Ok(new ChatResponse(PausedResponse, Guid.Empty, Guid.Empty, SafetyFlag.Clean));
            case ChatGateEvaluator.GateDecision.ModeDisabled:
                AppMeter.ChatGateTrip.Add(1, new KeyValuePair<string, object?>("gate", "mode_disabled"));
                return Ok(new ChatResponse(
                    ModeDisabledResponse, Guid.Empty, Guid.Empty, SafetyFlag.Clean));
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
            //
            // Metric-side counts live in OpenAIReliabilityGate now —
            // aat_chat_openai_failure_total with the `kind` tag — so
            // classification happens where the exception is first seen.
            // This catch stays as the final sanitization + log safety net.
            return StatusCode(502, new { error = "AI service unavailable. Please try again." });
        }
    }
}

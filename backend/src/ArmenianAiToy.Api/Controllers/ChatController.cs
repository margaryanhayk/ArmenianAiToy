using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Telemetry;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly OpenAICostMeter _costMeter;
    private readonly IOptions<OpenAIDailyCostCapOptions> _costCapOptions;
    private readonly ILogger<ChatController> _logger;

    // Child-facing canned reply when a parent has paused the device. Kept
    // short, age-appropriate, and tells the child to involve their parent.
    // Never passed through AI / moderation — it is a constant.
    internal const string PausedResponse =
        "Հիմա հանգստանում եմ։ Ծնողիդ կարող է նորից միացնել։";
        // «Հիմա հանգստանում եմ։ Ծնողդ կարող է նորից միացնել։»

    // B5: canned reply when the parent has disabled the mode the child's
    // current message is asking for. Deliberately distinct from PausedResponse
    // so the two gates are separately identifiable in any future log/audit
    // inspection. Same envelope shape; no AI call; SafetyFlag.Clean.
    internal const string ModeDisabledResponse =
        "Եկ մի ուրիշ բան փորձենք։";
        // «Եկ մի ուրիշ բան փորձենք։» ("Let's try something else.")

    // Cost-cap canned reply (P0 daily-spend gate). Child-safe, no cost
    // detail, same envelope shape as PausedResponse. Deliberately distinct
    // wording so the cap-trip path is operator-distinguishable in logs.
    // SafetyFlag.Clean — a parent-cost-cap soft-off is not a safety event.
    internal const string CostCapResponse =
        "Հիմա մի փոքր դադար տանք։ Քիչ հետո նորից կշարունակենք։";
        // «Հիմա մի փոքր դադար տանք։ Քիչ հետո նորից կշարունակենք։»

    public ChatController(
        IChatService chatService,
        IDeviceService deviceService,
        OpenAICostMeter costMeter,
        IOptions<OpenAIDailyCostCapOptions> costCapOptions,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _deviceService = deviceService;
        _costMeter = costMeter;
        _costCapOptions = costCapOptions;
        _logger = logger;
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

        // P0 daily cost cap. Fires AFTER the soft-off gates above so the
        // existing pause/bedtime/mode-disabled telemetry shape is
        // unchanged. SafetyFlag stays Clean — a cost-cap trip is a
        // parent-cost soft-off, not a safety event. Disabled config
        // skips the gate entirely.
        var costCapOpts = _costCapOptions.Value;
        if (costCapOpts.Enabled)
        {
            var nowUtc = DateTime.UtcNow;
            var cap = costCapOpts.CapForDevice(deviceId);
            if (_costMeter.IsOverCap(deviceId, cap, nowUtc))
            {
                AppMeter.OpenAICostCapTrip.Add(1,
                    new KeyValuePair<string, object?>("kind", "chat"));
                if (_costMeter.ShouldLogCapTrip(deviceId, nowUtc))
                {
                    _logger.LogWarning(
                        "OpenAI daily cost cap reached. DeviceId={DeviceId} Kind={Kind} CurrentEstimatedUsd={Current:F4} CapUsd={Cap:F4} UtcDate={Date:yyyy-MM-dd}",
                        deviceId, "chat",
                        _costMeter.GetCurrentTotal(deviceId, nowUtc), cap, nowUtc.Date);
                }
                return Ok(new ChatResponse(
                    CostCapResponse, Guid.Empty, Guid.Empty, SafetyFlag.Clean));
            }
        }

        try
        {
            var response = await _chatService.GetResponseAsync(deviceId, request.Message, request.ChildId,
                request.StorySessionId, request.SelectedChoice);

            // Cost-recording is best-effort and runs after a successful
            // ChatService completion. Wrapped in its own try/catch so a
            // bug in the estimator can never break the chat path.
            if (costCapOpts.Enabled)
            {
                try
                {
                    var estimate = OpenAICostEstimator.EstimateChatCostUsd(
                        request.Message, response.Response);
                    _costMeter.Record(deviceId, estimate, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "OpenAI cost-cap: chat cost-record failure (suppressed). DeviceId={DeviceId}",
                        deviceId);
                }
            }

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

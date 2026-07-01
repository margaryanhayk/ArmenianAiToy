using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// End-to-end tests for the daily AI-question quota gate on
/// <see cref="ChatController"/>. Pins:
///   - disabled (default): Curiosity turns proceed, nothing counted;
///   - enabled + under quota: proceeds AND counts the question;
///   - enabled + over quota: canned reply, ChatService NOT called;
///   - only Curiosity turns count (a non-question turn is never counted);
///   - the quota is keyed per child when a ChildId is supplied.
/// </summary>
public class ChatControllerQuotaTests
{
    // Reliable single-message triggers (see ModeDetectorTests):
    private const string CuriosityMsg = "what's a star"; // → DetectedMode.Curiosity
    private const string NonQuestionMsg = "Բարև";        // → not Curiosity

    private sealed record Harness(
        ChatController Controller,
        IChatService ChatService,
        AiQuotaMeter Meter,
        Guid DeviceId);

    private static Harness Create(bool enabled, int limit)
    {
        var chat = Substitute.For<IChatService>();
        chat.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("պատասխան", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        var device = Substitute.For<IDeviceService>();
        device.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        device.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(false);
        device.IsModeEnabledForRequestAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DetectedMode>())
            .Returns(true);

        var costMeter = new OpenAICostMeter();
        var costOpts = Options.Create(new OpenAIDailyCostCapOptions { Enabled = false });
        var quotaMeter = new AiQuotaMeter();
        var quotaOpts = Options.Create(new AiQuotaOptions
        {
            Enabled = enabled,
            DailyQuestionLimit = limit,
        });
        var logger = Substitute.For<ILogger<ChatController>>();
        var controller = new ChatController(
            chat, device, costMeter, costOpts, quotaMeter, quotaOpts, logger);

        var http = new DefaultHttpContext();
        var deviceId = Guid.NewGuid();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        return new Harness(controller, chat, quotaMeter, deviceId);
    }

    private static string ResponseText(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ChatResponse>(ok.Value!);
        return resp.Response;
    }

    [Fact]
    public async Task Disabled_CuriosityProceeds_AndNothingCounted()
    {
        var h = Create(enabled: false, limit: 1);

        await h.Controller.Chat(new ChatRequest(CuriosityMsg));
        await h.Controller.Chat(new ChatRequest(CuriosityMsg));

        await h.ChatService.Received(2).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
        Assert.Equal(0, h.Meter.GetCount(h.DeviceId, DateTime.UtcNow));
    }

    [Fact]
    public async Task Enabled_UnderQuota_Proceeds_AndCountsQuestion()
    {
        var h = Create(enabled: true, limit: 5);

        var result = await h.Controller.Chat(new ChatRequest(CuriosityMsg));

        Assert.Equal("պատասխան", ResponseText(result));
        await h.ChatService.Received(1).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
        Assert.Equal(1, h.Meter.GetCount(h.DeviceId, DateTime.UtcNow));
    }

    [Fact]
    public async Task Enabled_OverQuota_ReturnsCannedReply_AndDoesNotCallChatService()
    {
        var h = Create(enabled: true, limit: 2);

        // First two questions answered; the third is over quota.
        await h.Controller.Chat(new ChatRequest(CuriosityMsg));
        await h.Controller.Chat(new ChatRequest(CuriosityMsg));
        var third = await h.Controller.Chat(new ChatRequest(CuriosityMsg));

        Assert.Equal(ChatController.DailyQuestionLimitResponse, ResponseText(third));
        // ChatService called exactly twice (the two answered turns), not thrice.
        await h.ChatService.Received(2).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Enabled_NonQuestionTurn_ProceedsAndIsNeverCounted()
    {
        var h = Create(enabled: true, limit: 1);

        // A non-Curiosity turn must not consume the allowance, even when
        // the limit is 1 and we send several.
        await h.Controller.Chat(new ChatRequest(NonQuestionMsg));
        await h.Controller.Chat(new ChatRequest(NonQuestionMsg));

        await h.ChatService.Received(2).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
        Assert.Equal(0, h.Meter.GetCount(h.DeviceId, DateTime.UtcNow));
    }

    [Fact]
    public async Task Enabled_QuotaIsKeyedPerChild_WhenChildIdSupplied()
    {
        var h = Create(enabled: true, limit: 5);
        var childId = Guid.NewGuid();

        await h.Controller.Chat(new ChatRequest(CuriosityMsg, childId));

        // Counted under the child id, not the device id.
        Assert.Equal(1, h.Meter.GetCount(childId, DateTime.UtcNow));
        Assert.Equal(0, h.Meter.GetCount(h.DeviceId, DateTime.UtcNow));
    }
}

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
/// End-to-end tests for the P0 per-device daily OpenAI cost-cap gate
/// added to <see cref="ChatController"/>. Exercises:
///   - under-cap: chat proceeds, cost is recorded
///   - over-cap: gate trips, ChatService NOT called, canned response
///   - over-cap with cap-disabled config: chat proceeds normally
///   - per-device isolation
///   - cost recording happens after a successful chat completion
/// </summary>
public class ChatControllerCostCapTests
{
    private const decimal CapUsd = 0.50m;

    private sealed record Harness(
        ChatController Controller,
        IChatService ChatService,
        IDeviceService DeviceService,
        OpenAICostMeter CostMeter,
        Guid DeviceId);

    private static Harness Create(
        Action<OpenAIDailyCostCapOptions>? configureOptions = null)
    {
        var chatService = Substitute.For<IChatService>();
        var deviceService = Substitute.For<IDeviceService>();
        var costMeter = new OpenAICostMeter();
        var opts = new OpenAIDailyCostCapOptions { Enabled = true, Default = CapUsd };
        configureOptions?.Invoke(opts);
        var costCapOptions = Options.Create(opts);
        var logger = Substitute.For<ILogger<ChatController>>();
        var controller = new ChatController(
            chatService, deviceService, costMeter, costCapOptions, logger);
        var httpContext = new DefaultHttpContext();
        var deviceId = Guid.NewGuid();
        httpContext.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return new Harness(controller, chatService, deviceService, costMeter, deviceId);
    }

    private static ChatResponse OkPayload(IActionResult result)
    {
        var obj = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ChatResponse>(obj.Value!);
        return resp;
    }

    [Fact]
    public async Task UnderCap_ChatProceeds_AndCostIsRecorded()
    {
        var h = Create();
        h.ChatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse(
                "Կար մի կատու։", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        // Real chat response surfaced (NOT the cost-cap canned line).
        var payload = OkPayload(result);
        Assert.NotEqual(ChatController.CostCapResponse, payload.Response);
        // ChatService was called.
        await h.ChatService.Received().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
        // Cost recorded for this device on today's bucket.
        var nowUtc = DateTime.UtcNow;
        Assert.True(h.CostMeter.GetCurrentTotal(h.DeviceId, nowUtc) > 0m);
    }

    [Fact]
    public async Task OverCap_GateTrips_ChatServiceNotCalled_CannedResponse()
    {
        var h = Create();
        // Seed the meter past the cap before the request.
        h.CostMeter.Record(h.DeviceId, CapUsd + 0.01m, DateTime.UtcNow);

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        var payload = OkPayload(result);
        Assert.Equal(ChatController.CostCapResponse, payload.Response);
        Assert.Equal(SafetyFlag.Clean, payload.SafetyFlag);
        // No upstream call.
        await h.ChatService.DidNotReceive().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task CostCapDisabled_OverCapStillProceeds()
    {
        var h = Create(o => o.Enabled = false);
        // Seed the meter; with Enabled=false the gate must NOT fire.
        h.CostMeter.Record(h.DeviceId, CapUsd * 10m, DateTime.UtcNow);
        h.ChatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse(
                "ok", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        var payload = OkPayload(result);
        Assert.NotEqual(ChatController.CostCapResponse, payload.Response);
        await h.ChatService.Received().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task PerDevice_Isolation_OneDeviceOverCapDoesNotBlockAnother()
    {
        // Build two harnesses sharing nothing — DeviceId is per-harness.
        var hOver = Create();
        hOver.CostMeter.Record(hOver.DeviceId, CapUsd + 0.01m, DateTime.UtcNow);

        var hUnder = Create();
        hUnder.ChatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse(
                "ok", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        // Over-cap device blocks.
        var overResult = await hOver.Controller.Chat(new ChatRequest("Բարև"));
        Assert.Equal(ChatController.CostCapResponse, OkPayload(overResult).Response);

        // Independent under-cap device still proceeds.
        var underResult = await hUnder.Controller.Chat(new ChatRequest("Բարև"));
        Assert.NotEqual(ChatController.CostCapResponse, OkPayload(underResult).Response);
    }

    // ---------- #022 global / fleet-wide ceiling ----------

    [Fact]
    public async Task GlobalCeiling_TripsEvenWhenDeviceIsUnderItsOwnCap()
    {
        // Global ceiling = 1.00; this device is well under its 0.50 per-device
        // cap, but a DIFFERENT device pushed the fleet total over 1.00.
        var h = Create(o => o.Global = 1.00m);
        h.CostMeter.Record(Guid.NewGuid(), 1.50m, DateTime.UtcNow); // some other device

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        var payload = OkPayload(result);
        Assert.Equal(ChatController.CostCapResponse, payload.Response);
        Assert.Equal(SafetyFlag.Clean, payload.SafetyFlag);
        await h.ChatService.DidNotReceive().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task GlobalCeiling_Zero_IsDisabled()
    {
        // Default Global = 0 (opt-in): even a large fleet total does NOT gate.
        var h = Create(); // Global stays 0
        h.CostMeter.Record(Guid.NewGuid(), 100m, DateTime.UtcNow);
        h.ChatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse(
                "ok", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        Assert.NotEqual(ChatController.CostCapResponse, OkPayload(result).Response);
        await h.ChatService.Received().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task PerDeviceOverride_AppliedWhenPresent()
    {
        // Override map maps THIS device to a higher cap; recording the
        // default cap amount should still leave the device under the
        // override cap.
        var deviceId = Guid.NewGuid();
        var h = Create(o =>
        {
            o.PerDeviceOverride[deviceId.ToString()] = 5.00m;
        });
        // Replace harness DeviceId with the one we mapped.
        h.Controller.ControllerContext.HttpContext.Items["DeviceId"] = deviceId;
        h.CostMeter.Record(deviceId, 0.80m, DateTime.UtcNow); // > default 0.50, < override 5.00

        h.ChatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse(
                "ok", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));

        var result = await h.Controller.Chat(new ChatRequest("Բարև"));

        // Under the override cap → proceeds.
        Assert.NotEqual(ChatController.CostCapResponse, OkPayload(result).Response);
    }
}

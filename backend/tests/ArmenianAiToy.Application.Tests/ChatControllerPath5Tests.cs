using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Regression tests for ChatController's Path-5 catch branch (commit f71b16d).
/// The 502 body must be a constant sanitized string so upstream OpenAI SDK
/// exception detail — request-ids, URLs, internal messages — cannot reach the
/// device / client surface. Safety-adjacent: the sanitization landed without
/// test coverage; these tests pin the wire shape against regression.
/// </summary>
public class ChatControllerPath5Tests
{
    private const string ExpectedSanitizedError = "AI service unavailable. Please try again.";

    private static ChatController CreateController(IChatService chatService)
    {
        // IDeviceService.IsDevicePausedAsync returns false by default (NSubstitute
        // default for Task<bool>), so the pause gate added in the B3 commit is a
        // no-op here and these Path-5 tests exercise the same code path as before.
        var deviceService = Substitute.For<IDeviceService>();
        var controller = new ChatController(chatService, deviceService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Chat_WhenChatServiceThrows_Returns502WithSanitizedBody()
    {
        // The thrown exception intentionally carries every leak marker the
        // sanitization exists to suppress: OpenAI request-id, API URL, and a
        // unique injected sentinel. Any of these surfacing in the response
        // body would indicate a regression of the Path-5 fix.
        const string LeakSentinel = "LEAK-SENTINEL-9f3a";
        var chatService = Substitute.For<IChatService>();
        chatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>())
            .Throws(new InvalidOperationException(
                $"OpenAI upstream failure; request-id=req_abc123 url=https://api.openai.com/v1/chat/completions {LeakSentinel}"));
        var controller = CreateController(chatService);

        var result = await controller.Chat(new ChatRequest("hi"));

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, obj.StatusCode);

        // Anonymous { error = "..." } payload — read via reflection.
        var errorProp = obj.Value!.GetType().GetProperty("error");
        Assert.NotNull(errorProp);
        Assert.Equal(ExpectedSanitizedError, errorProp!.GetValue(obj.Value) as string);

        // Serialized wire body must contain none of the leak markers.
        var body = JsonSerializer.Serialize(obj.Value);
        Assert.DoesNotContain("request-id", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAI", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LeakSentinel, body);
    }

    [Fact]
    public async Task Chat_WhenChatServiceSucceeds_DoesNotReturn502()
    {
        // Anti-tautology guard: if Chat always returned 502, the main test
        // above would still pass. This pins the success path to NOT hit the
        // sanitized branch.
        var chatService = Substitute.For<IChatService>();
        chatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("hello", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));
        var controller = CreateController(chatService);

        var result = await controller.Chat(new ChatRequest("hi"));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Chat_WhenDeviceInBedtimeWindow_ShortCircuitsWithoutCallingChatService()
    {
        // B4 gate parallels the pause gate: when IsDeviceInBedtimeWindowAsync
        // returns true, ChatService must never be invoked. Response envelope
        // mirrors the pause gate (SafetyFlag.Clean, canned text).
        var chatService = Substitute.For<IChatService>();
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(true);

        var controller = new ChatController(chatService, deviceService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Chat(new ChatRequest("hi"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChatResponse>(ok.Value);
        Assert.Equal(SafetyFlag.Clean, body.SafetyFlag);
        await chatService.DidNotReceive().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Chat_WhenDeviceOutsideBedtimeWindowAndNotPaused_CallsChatService()
    {
        // Anti-tautology: when both gates are false the controller must
        // reach ChatService, so "always short-circuit" regressions are caught.
        var chatService = Substitute.For<IChatService>();
        chatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("hi back", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(false);
        deviceService.IsModeEnabledForRequestAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DetectedMode>())
            .Returns(true);

        var controller = new ChatController(chatService, deviceService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Chat(new ChatRequest("hi"));

        Assert.IsType<OkObjectResult>(result);
        await chatService.Received(1).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    // ---------- B5 mode-disabled gate ----------

    private static (ChatController Controller, IChatService Chat, IDeviceService Devices)
        CreateControllerWithFullGates(bool storyEnabled)
    {
        var chatService = Substitute.For<IChatService>();
        chatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("ok", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(false);
        // Default: all modes enabled. Tests override the Story branch below.
        // Controller now calls IsModeEnabledForRequestAsync (per-child
        // override aware); stubbing that is enough — the B5 device-level
        // resolver sits behind it and is exercised separately.
        deviceService.IsModeEnabledForRequestAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DetectedMode>())
            .Returns(true);
        deviceService.IsModeEnabledForRequestAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Story)
            .Returns(storyEnabled);

        var controller = new ChatController(chatService, deviceService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (controller, chatService, deviceService);
    }

    [Fact]
    public async Task Chat_WhenDetectedModeIsDisabled_ReturnsCannedReplyAndSkipsChatService()
    {
        // Message with a definitive story cue ("tell me a story") lands in
        // ModeDetector as DetectedMode.Story. With Story disabled on the
        // device, the controller must short-circuit with the B5 canned reply
        // and never reach ChatService.
        var (controller, chat, _) = CreateControllerWithFullGates(storyEnabled: false);

        var result = await controller.Chat(new ChatRequest("tell me a story please"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ChatResponse>(ok.Value);
        Assert.Equal(SafetyFlag.Clean, body.SafetyFlag);
        // Short-circuit envelope uses Guid.Empty for both IDs; ChatService
        // mock above returns Guid.NewGuid() for ConversationId, so this
        // proves we came from the B5 gate rather than the mocked path.
        Assert.Equal(Guid.Empty, body.ConversationId);
        Assert.Equal(Guid.Empty, body.MessageId);
        Assert.False(string.IsNullOrWhiteSpace(body.Response));
        await chat.DidNotReceive().GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Chat_WhenDetectedModeIsEnabled_ReachesChatService()
    {
        // Same definitive story message, but Story is enabled on the device.
        // The mode gate must NOT fire and the request must reach ChatService.
        var (controller, chat, _) = CreateControllerWithFullGates(storyEnabled: true);

        var result = await controller.Chat(new ChatRequest("tell me a story please"));

        Assert.IsType<OkObjectResult>(result);
        await chat.Received(1).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Chat_WhenDetectedModeIsCalm_IsNeverBlockedByModeGate()
    {
        // Safety invariant: a bedtime cue (DetectedMode.Calm) must always
        // reach ChatService, even if every configurable flag is false. The
        // mode gate must not even query the device service for Calm.
        var chatService = Substitute.For<IChatService>();
        chatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("shh", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(false);
        // If the gate ever asked about Calm, it would get false. The
        // controller must never ask (safety invariant). Stubbing the
        // new child-aware resolver covers the actual call site.
        deviceService.IsModeEnabledForRequestAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Calm)
            .Returns(false);

        var controller = new ChatController(chatService, deviceService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Chat(new ChatRequest("i'm sleepy"));

        Assert.IsType<OkObjectResult>(result);
        await chatService.Received(1).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>());
        await deviceService.DidNotReceive().IsModeEnabledForRequestAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), DetectedMode.Calm);
    }

    [Fact]
    public async Task Chat_WhenDetectedModeIsNone_IsNeverBlockedByModeGate()
    {
        // A message with no mode cue resolves to DetectedMode.None. The
        // controller must not query mode enablement and must reach
        // ChatService normally — missed-classification is conservative.
        var chatService = Substitute.For<IChatService>();
        chatService.GetResponseAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new ChatResponse("ok", Guid.NewGuid(), Guid.NewGuid(), SafetyFlag.Clean));
        var deviceService = Substitute.For<IDeviceService>();
        deviceService.IsDevicePausedAsync(Arg.Any<Guid>()).Returns(false);
        deviceService.IsDeviceInBedtimeWindowAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(false);

        var controller = new ChatController(chatService, deviceService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["DeviceId"] = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Chat(new ChatRequest("hello"));

        Assert.IsType<OkObjectResult>(result);
        await chatService.Received(1).GetResponseAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>());
        await deviceService.DidNotReceive().IsModeEnabledForRequestAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DetectedMode>());
    }
}

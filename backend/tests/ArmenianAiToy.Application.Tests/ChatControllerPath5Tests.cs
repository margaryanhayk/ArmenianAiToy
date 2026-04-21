using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
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
}

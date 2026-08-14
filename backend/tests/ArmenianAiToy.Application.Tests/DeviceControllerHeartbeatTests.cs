using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Platform presence — POST /api/devices/heartbeat. The endpoint is minimal:
/// DeviceAuthMiddleware (not exercised here) does the credential check + the
/// throttled LastSeenAt refresh; the action only acknowledges with the device
/// id + server time. These pin that contract.
/// </summary>
public class DeviceControllerHeartbeatTests
{
    private static DeviceController NewController(Guid? deviceIdInContext)
    {
        var controller = new DeviceController(
            Substitute.For<IDeviceService>(),
            new ConfigurationBuilder().Build());
        var http = new DefaultHttpContext();
        if (deviceIdInContext is not null)
            http.Items["DeviceId"] = deviceIdInContext.Value; // middleware sets this on success
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public async Task Heartbeat_AuthedDevice_Returns200WithDeviceId()
    {
        var deviceId = Guid.NewGuid();
        var result = await NewController(deviceId)
            .Heartbeat(Substitute.For<IDeviceCommandService>());

        var ok = Assert.IsType<OkObjectResult>(result);
        // Shape: { ok = true, deviceId, serverTimeUtc } — anonymously typed, so
        // assert via the JSON-serialized contract the app consumes.
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value,
            new System.Text.Json.JsonSerializerOptions
            { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Assert.Contains("\"ok\":true", json);
        Assert.Contains(deviceId.ToString(), json);
        Assert.Contains("serverTimeUtc", json);
    }

    // The toy used to poll GET /api/devices/commands every ~60 s on its own
    // cadence — a second TLS handshake a minute to deliver almost nothing.
    // The heartbeat it already sends now carries the answer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Heartbeat_CarriesHasCommands_FromTheCommandQueue(bool queued)
    {
        var deviceId = Guid.NewGuid();
        var commands = Substitute.For<IDeviceCommandService>();
        commands.HasDeliverableCommandAsync(deviceId, Arg.Any<DateTime>()).Returns(queued);

        var result = await NewController(deviceId).Heartbeat(commands);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value,
            new System.Text.Json.JsonSerializerOptions
            { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Assert.Contains("\"hasCommands\":" + (queued ? "true" : "false"), json);
        // Read-only: the heartbeat asks, it never delivers. Marking a command
        // Sent here would consume a delivery the poll still owns.
        await commands.DidNotReceiveWithAnyArgs().PollAsync(default, default);
    }
}

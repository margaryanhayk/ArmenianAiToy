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
        var result = await NewController(deviceId).Heartbeat();

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
}

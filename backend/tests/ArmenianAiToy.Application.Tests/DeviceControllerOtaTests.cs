using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level wiring tests for the OTA-foundation device endpoints:
/// heartbeat firmware reporting (incl. backward-compatible body-less), command
/// poll, ack (404 vs 200), and firmware-manifest.
/// </summary>
public class DeviceControllerOtaTests
{
    private static (DeviceController Controller, IDeviceService Device, Guid DeviceId) Create()
    {
        var device = Substitute.For<IDeviceService>();
        var controller = new DeviceController(device, Substitute.For<IConfiguration>());
        var deviceId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Items["DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, device, deviceId);
    }

    [Fact]
    public async Task Heartbeat_WithFirmwareBody_StampsFirmwareReport()
    {
        var (controller, device, deviceId) = Create();
        var body = new DeviceHeartbeatRequest(FirmwareVersion: "1.0.0", BoardModel: "areg-s3-n8");

        var result = await controller.Heartbeat(body);

        Assert.IsType<OkObjectResult>(result);
        await device.Received(1).UpdateFirmwareReportAsync(deviceId, body, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task Heartbeat_BodyLess_Returns200_AndDoesNotWrite()
    {
        var (controller, device, _) = Create();

        var result = await controller.Heartbeat(null); // legacy presence-only heartbeat

        Assert.IsType<OkObjectResult>(result);
        await device.DidNotReceiveWithAnyArgs()
            .UpdateFirmwareReportAsync(default, default!, default);
    }

    [Fact]
    public async Task GetCommands_ReturnsPolledCommandsForThisDevice()
    {
        var (controller, _, deviceId) = Create();
        var commands = Substitute.For<IDeviceCommandService>();
        var cmd = new DeviceCommandDto(Guid.NewGuid(), "firmware_update", null, DateTime.UtcNow, null);
        commands.PollAsync(deviceId, Arg.Any<DateTime>())
            .Returns(new List<DeviceCommandDto> { cmd });

        var result = await controller.GetCommands(commands);

        Assert.IsType<OkObjectResult>(result);
        await commands.Received(1).PollAsync(deviceId, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task AckCommand_NotFound_Returns404()
    {
        var (controller, _, _) = Create();
        var commands = Substitute.For<IDeviceCommandService>();
        commands.AckAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DeviceCommandAckRequest>(), Arg.Any<DateTime>())
            .Returns(DeviceCommandAckOutcome.NotFound);

        var result = await controller.AckCommand(Guid.NewGuid(), new DeviceCommandAckRequest("ok"), commands);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AckCommand_Acked_Returns200()
    {
        var (controller, _, _) = Create();
        var commands = Substitute.For<IDeviceCommandService>();
        commands.AckAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DeviceCommandAckRequest>(), Arg.Any<DateTime>())
            .Returns(DeviceCommandAckOutcome.Acked);

        var result = await controller.AckCommand(Guid.NewGuid(), new DeviceCommandAckRequest("ok"), commands);

        Assert.IsType<OkObjectResult>(result);
    }

    // ---- firmware-image streaming endpoint (OTA apply slice) ----

    private static ArmenianAiToy.Application.Helpers.FirmwareUpdateOptions ImageOptions(
        bool enabled, string imagePath) => new()
    {
        Enabled = enabled,
        LatestVersion = "1.0.1",
        Url = "http://backend/api/devices/firmware-image",
        ImagePath = imagePath,
    };

    [Fact]
    public void GetFirmwareImage_DisabledRelease_Returns404()
    {
        var (controller, _, _) = Create();
        var opts = ImageOptions(enabled: false, imagePath: Path.GetTempFileName());

        var result = controller.GetFirmwareImage(
            opts, Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceController>>());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetFirmwareImage_NoImagePathConfigured_Returns404()
    {
        var (controller, _, _) = Create();
        var opts = ImageOptions(enabled: true, imagePath: "");

        var result = controller.GetFirmwareImage(
            opts, Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceController>>());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetFirmwareImage_MissingFile_Returns404()
    {
        var (controller, _, _) = Create();
        var opts = ImageOptions(
            enabled: true,
            imagePath: Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.bin"));

        var result = controller.GetFirmwareImage(
            opts, Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceController>>());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetFirmwareImage_RelativePath_Returns404()
    {
        var (controller, _, _) = Create();
        var opts = ImageOptions(enabled: true, imagePath: "relative/areg.bin");

        var result = controller.GetFirmwareImage(
            opts, Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceController>>());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetFirmwareImage_ConfiguredAndPresent_StreamsWithRangeSupport()
    {
        var (controller, _, _) = Create();
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[] { 0xE9, 1, 2, 3 }); // fake image bytes
            var opts = ImageOptions(enabled: true, imagePath: file);

            var result = controller.GetFirmwareImage(
                opts, Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceController>>());

            var physical = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(file, physical.FileName);
            Assert.Equal("application/octet-stream", physical.ContentType);
            Assert.True(physical.EnableRangeProcessing); // resume-ready
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task GetFirmwareManifest_ReturnsManifestForDeviceVersion()
    {
        var (controller, device, deviceId) = Create();
        device.GetDeviceAsync(deviceId).Returns(new Device
        {
            Id = deviceId, FirmwareVersion = "1.0.0", BoardModel = "areg-s3-n8",
        });
        var manifest = Substitute.For<IFirmwareManifestService>();
        var response = new FirmwareManifestResponse(UpdateAvailable: true, Version: "1.0.1");
        manifest.Build("1.0.0", "areg-s3-n8", Arg.Any<DateTime>()).Returns(response);

        var result = await controller.GetFirmwareManifest(manifest);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }
}

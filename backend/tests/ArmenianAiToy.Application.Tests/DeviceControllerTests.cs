using System.Linq;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #009 + #011 — the device-registration controller gate (fail-closed
/// provisioning) and the no-silent-key-rotation routing (re-register refused
/// unless explicitly forced).
/// </summary>
public class DeviceControllerTests
{
    private static (DeviceController Controller, IDeviceService Svc) Create(
        (string Key, string Value)[] config,
        string? provisioningHeader = null,
        string? forceHeader = null)
    {
        var svc = Substitute.For<IDeviceService>();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();
        var controller = new DeviceController(svc, cfg);
        var http = new DefaultHttpContext();
        if (provisioningHeader is not null)
            http.Request.Headers[DeviceProvisioningAuthHeader] = provisioningHeader;
        if (forceHeader is not null)
            http.Request.Headers["X-Force-Rotate"] = forceHeader;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, svc);
    }

    private const string DeviceProvisioningAuthHeader = "X-Provisioning-Secret";
    private static DeviceRegistrationRequest Req() => new("AA:BB:CC:DD:EE:01");

    [Fact]
    public async Task NoSecret_NoBypass_Returns401_WithoutCallingService()
    {
        var (c, svc) = Create(new[]
        {
            ("Devices:ProvisioningSecret", ""), ("Devices:AllowOpenRegistration", "false")
        });

        var result = await c.Register(Req());

        Assert.IsType<UnauthorizedObjectResult>(result);
        await svc.DidNotReceiveWithAnyArgs().RegisterDeviceAsync(default!, default);
    }

    [Fact]
    public async Task BypassOn_NewDevice_Returns201()
    {
        var (c, svc) = Create(new[] { ("Devices:AllowOpenRegistration", "true") });
        svc.RegisterDeviceAsync(Arg.Any<DeviceRegistrationRequest>(), false)
            .Returns(new DeviceRegistrationResponse(Guid.NewGuid(), "dtk_x"));

        Assert.IsType<CreatedResult>(await c.Register(Req()));
    }

    [Fact]
    public async Task SecretSet_MatchingHeader_Allows()
    {
        var (c, svc) = Create(
            new[] { ("Devices:ProvisioningSecret", "s3cret") }, provisioningHeader: "s3cret");
        svc.RegisterDeviceAsync(Arg.Any<DeviceRegistrationRequest>(), Arg.Any<bool>())
            .Returns(new DeviceRegistrationResponse(Guid.NewGuid(), "dtk_x"));

        Assert.IsType<CreatedResult>(await c.Register(Req()));
    }

    [Fact]
    public async Task SecretSet_WrongHeader_Returns401_WithoutCallingService()
    {
        var (c, svc) = Create(
            new[] { ("Devices:ProvisioningSecret", "s3cret") }, provisioningHeader: "nope");

        Assert.IsType<UnauthorizedObjectResult>(await c.Register(Req()));
        await svc.DidNotReceiveWithAnyArgs().RegisterDeviceAsync(default!, default);
    }

    [Fact]
    public async Task ExistingDevice_NoForce_Returns409()
    {
        var (c, svc) = Create(new[] { ("Devices:AllowOpenRegistration", "true") });
        svc.RegisterDeviceAsync(Arg.Any<DeviceRegistrationRequest>(), false)
            .Returns((DeviceRegistrationResponse?)null);

        Assert.IsType<ConflictObjectResult>(await c.Register(Req()));
    }

    [Fact]
    public async Task ForceHeader_RoutesAllowReRegisterTrue()
    {
        var (c, svc) = Create(
            new[] { ("Devices:AllowOpenRegistration", "true") }, forceHeader: "true");
        svc.RegisterDeviceAsync(Arg.Any<DeviceRegistrationRequest>(), true)
            .Returns(new DeviceRegistrationResponse(Guid.NewGuid(), "dtk_x"));

        Assert.IsType<CreatedResult>(await c.Register(Req()));
        await svc.Received().RegisterDeviceAsync(Arg.Any<DeviceRegistrationRequest>(), true);
    }

    [Fact]
    public async Task MissingMac_Returns400_BeforeGate()
    {
        var (c, svc) = Create(new[] { ("Devices:AllowOpenRegistration", "true") });

        Assert.IsType<BadRequestObjectResult>(await c.Register(new DeviceRegistrationRequest("")));
        await svc.DidNotReceiveWithAnyArgs().RegisterDeviceAsync(default!, default);
    }
}

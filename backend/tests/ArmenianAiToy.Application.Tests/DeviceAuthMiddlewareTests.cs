using ArmenianAiToy.Api.Middleware;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #034 — DeviceAuthMiddleware LastSeen handling. The previous code ran an
/// un-awaited UpdateLastSeenAsync on the request-scoped DbContext concurrently
/// with the downstream pipeline (DbContext is not thread-safe) and could
/// outlive the scope. These pin the fixed contract: the refresh is AWAITED
/// (serialized before _next), THROTTLED (skipped when LastSeen is fresh), and
/// BEST-EFFORT (a write failure never breaks the authed request).
/// </summary>
public class DeviceAuthMiddlewareTests
{
    private static HttpContext MakeContext(IDeviceService svc, Guid deviceId)
    {
        var services = new ServiceCollection();
        services.AddSingleton(svc);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Path = "/api/chat";
        ctx.Request.Headers["X-Device-Id"] = deviceId.ToString();
        ctx.Request.Headers["X-Api-Key"] = "dtk_test";
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static DeviceAuthMiddleware Make(RequestDelegate next) =>
        new(next, NullLogger<DeviceAuthMiddleware>.Instance);

    [Fact]
    public async Task ValidCreds_StaleLastSeen_RefreshesBeforeNext()
    {
        var device = new Device { Id = Guid.NewGuid(), LastSeenAt = DateTime.UtcNow.AddMinutes(-5) };
        var svc = Substitute.For<IDeviceService>();
        svc.ValidateDeviceAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns(device);

        var order = new List<string>();
        svc.UpdateLastSeenAsync(device.Id).Returns(_ => { order.Add("update"); return Task.CompletedTask; });

        var middleware = Make(_ => { order.Add("next"); return Task.CompletedTask; });
        await middleware.InvokeAsync(MakeContext(svc, device.Id));

        await svc.Received(1).UpdateLastSeenAsync(device.Id);
        Assert.Equal(new[] { "update", "next" }, order); // awaited -> update completes before _next
    }

    [Fact]
    public async Task ValidCreds_FreshLastSeen_SkipsRefresh()
    {
        var device = new Device { Id = Guid.NewGuid(), LastSeenAt = DateTime.UtcNow }; // within throttle window
        var svc = Substitute.For<IDeviceService>();
        svc.ValidateDeviceAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns(device);

        var nextRan = false;
        var middleware = Make(_ => { nextRan = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(MakeContext(svc, device.Id));

        Assert.True(nextRan);
        await svc.DidNotReceive().UpdateLastSeenAsync(Arg.Any<Guid>()); // throttled — no write
    }

    [Fact]
    public async Task RefreshThrows_RequestStillProceeds()
    {
        var device = new Device { Id = Guid.NewGuid(), LastSeenAt = DateTime.UtcNow.AddMinutes(-5) };
        var svc = Substitute.For<IDeviceService>();
        svc.ValidateDeviceAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns(device);
        svc.UpdateLastSeenAsync(device.Id).Returns(Task.FromException(new InvalidOperationException("db down")));

        var nextRan = false;
        var middleware = Make(_ => { nextRan = true; return Task.CompletedTask; });

        // Best-effort: the failed LastSeen write must be swallowed, not bubbled.
        await middleware.InvokeAsync(MakeContext(svc, device.Id));

        Assert.True(nextRan);
    }

    [Fact]
    public async Task InvalidCreds_Returns401_DoesNotCallNextOrRefresh()
    {
        var svc = Substitute.For<IDeviceService>();
        svc.ValidateDeviceAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns((Device?)null);

        var nextRan = false;
        var middleware = Make(_ => { nextRan = true; return Task.CompletedTask; });
        var ctx = MakeContext(svc, Guid.NewGuid());
        await middleware.InvokeAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.False(nextRan);
        await svc.DidNotReceive().UpdateLastSeenAsync(Arg.Any<Guid>());
    }
}

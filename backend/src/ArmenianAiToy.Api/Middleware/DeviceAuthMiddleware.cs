using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Api.Middleware;

public class DeviceAuthMiddleware
{
    private readonly RequestDelegate _next;

    // Paths that require device auth (as opposed to parent JWT auth or no auth)
    private static readonly string[] DeviceAuthPaths = ["/api/chat", "/api/audio", "/api/devices/heartbeat"];

    public DeviceAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        if (!DeviceAuthPaths.Any(p => path.StartsWith(p)))
        {
            await _next(context);
            return;
        }

        var deviceIdHeader = context.Request.Headers["X-Device-Id"].FirstOrDefault();
        var apiKeyHeader = context.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(deviceIdHeader) || string.IsNullOrEmpty(apiKeyHeader)
            || !Guid.TryParse(deviceIdHeader, out var deviceId))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Device-Id / X-Api-Key headers" });
            return;
        }

        var deviceService = context.RequestServices.GetRequiredService<IDeviceService>();
        var device = await deviceService.ValidateDeviceAsync(deviceId, apiKeyHeader);

        if (device == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid device credentials" });
            return;
        }

        // Store device ID in HttpContext for controllers to use
        context.Items["DeviceId"] = device.Id;

        // Update last seen (fire and forget)
        _ = deviceService.UpdateLastSeenAsync(device.Id);

        await _next(context);
    }
}

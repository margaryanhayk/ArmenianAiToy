using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Api.Middleware;

public class DeviceAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DeviceAuthMiddleware> _logger;

    // Paths that require device auth (as opposed to parent JWT auth or no auth)
    // The OTA-foundation device endpoints (commands, firmware-manifest) are
    // device-authed too, so a revoked device is rejected here
    // (ValidateDeviceAsync returns null for a revoked device) BEFORE it can
    // poll or ack. /api/devices/register stays out (provisioning-secret gated).
    private static readonly string[] DeviceAuthPaths =
    [
        "/api/chat",
        "/api/audio",
        "/api/devices/heartbeat",
        "/api/devices/commands",
        "/api/devices/firmware-manifest",
        "/api/devices/firmware-image",
        "/api/devices/content-manifest",
        "/api/devices/content-file",
    ];

    // #034 — LastSeen is refreshed at most once per this interval per device.
    // Dormancy works in days, so coarse granularity is invisible to it, and a
    // chatty device no longer contends the SQLite writer lock on every request.
    private static readonly TimeSpan LastSeenRefreshInterval = TimeSpan.FromSeconds(60);

    public DeviceAuthMiddleware(RequestDelegate next, ILogger<DeviceAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Segment-aware prefix match: "/api/devices/commands" covers the poll GET
        // and the ".../{id}/ack" POST, but must NOT over-match a sibling route
        // like "/api/devices/commandsX". The previous check (String.StartsWith on
        // a lowercased path) matched any path that merely began with the string.
        if (!DeviceAuthPaths.Any(p =>
                context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var deviceIdHeader = context.Request.Headers["X-Device-Id"].FirstOrDefault();
        var apiKeyHeader = context.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(deviceIdHeader) || string.IsNullOrEmpty(apiKeyHeader)
            || !Guid.TryParse(deviceIdHeader, out var deviceId))
        {
            // Diagnostic: surface a malformed/missing-header auth attempt.
            // Never log key material — only whether the key header was present.
            _logger.LogWarning(
                "Device auth 401 (bad headers). Path={Path} DeviceIdHeader={DeviceId} ApiKeyPresent={KeyPresent}",
                context.Request.Path,
                string.IsNullOrEmpty(deviceIdHeader) ? "(none)" : deviceIdHeader,
                !string.IsNullOrEmpty(apiKeyHeader));
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Device-Id / X-Api-Key headers" });
            return;
        }

        var deviceService = context.RequestServices.GetRequiredService<IDeviceService>();
        var device = await deviceService.ValidateDeviceAsync(deviceId, apiKeyHeader);

        if (device == null)
        {
            // Diagnostic: which device-id was rejected. Never log key material —
            // the device-id alone is enough to spot an unregistered / fallback id.
            _logger.LogWarning(
                "Device auth 401 (rejected credentials). Path={Path} DeviceId={DeviceId}",
                context.Request.Path, deviceId);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid device credentials" });
            return;
        }

        // Store device ID in HttpContext for controllers to use
        context.Items["DeviceId"] = device.Id;

        // #034 — refresh LastSeen, AWAITED (not fire-and-forget). The previous
        // un-awaited call ran on the request-scoped DbContext concurrently with
        // the downstream pipeline (DbContext is not thread-safe) and could
        // outlive the scope -> ObjectDisposed / "second operation on this
        // context" under load. Awaiting serializes it within the scope.
        // Throttled to LastSeenRefreshInterval so it stays off the per-request
        // writer lock for chatty devices. Best-effort: a failure here must
        // never break an authed request, so it is swallowed and logged.
        if (DateTime.UtcNow - device.LastSeenAt >= LastSeenRefreshInterval)
        {
            try
            {
                await deviceService.UpdateLastSeenAsync(device.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to update LastSeen for device {DeviceId}", device.Id);
            }
        }

        await _next(context);
    }
}

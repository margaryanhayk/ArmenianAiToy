using System.Net;
using System.Reflection;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="AuthRateLimiter"/>. Partition-key and
/// permit-window contract mirrors
/// <see cref="ChatRateLimiterTests"/>; the IP-keying tests are the
/// new behaviour this slice introduces.
/// </summary>
public class AuthRateLimiterTests
{
    [Fact]
    public void BuildPartition_UsesProvidedKeyAsPartitionKey()
    {
        var partition = AuthRateLimiter.BuildPartition(
            "203.0.113.7", permitLimit: 10, windowSeconds: 60);
        Assert.Equal("203.0.113.7", partition.PartitionKey);
    }

    [Fact]
    public async Task BuildPartition_RejectsRequestsAfterPermitLimit_WithinWindow()
    {
        // Same shape as ChatRateLimiterTests — N acquires succeed, the
        // (N+1)th is rejected while still inside the window.
        var partition = AuthRateLimiter.BuildPartition(
            "198.51.100.1", permitLimit: 3, windowSeconds: 60);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (int i = 0; i < 3; i++)
        {
            using var lease = await limiter.AcquireAsync(1);
            Assert.True(lease.IsAcquired, $"request {i + 1} should be permitted");
        }

        using var rejected = await limiter.AcquireAsync(1);
        Assert.False(rejected.IsAcquired);
    }

    [Fact]
    public void PolicyFactory_KeysByRemoteIpAddress()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");

        var partition = AuthRateLimiter.PolicyFactory(ctx, permitLimit: 10, windowSeconds: 60);

        Assert.Equal("203.0.113.42", partition.PartitionKey);
    }

    [Fact]
    public void PolicyFactory_UsesAnonymousKey_WhenRemoteIpUnavailable()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = null;

        var partition = AuthRateLimiter.PolicyFactory(ctx, permitLimit: 10, windowSeconds: 60);

        Assert.Equal(AuthRateLimiter.AnonymousKey, partition.PartitionKey);
    }

    [Fact]
    public void PolicyFactory_IgnoresXForwardedForHeader_InThisSlice()
    {
        // Pins the explicit invariant documented on AuthRateLimiter:
        // X-Forwarded-For is attacker-controlled in this repo today
        // (no ForwardedHeaders middleware), so the limiter must key on
        // the TCP-level RemoteIpAddress only. A future deploy slice
        // that wires ForwardedHeaders with a trusted-proxy list is
        // the correct place to change this; a drive-by edit that
        // reads the header directly would regress the contract.
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");
        ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4, 5.6.7.8";

        var partition = AuthRateLimiter.PolicyFactory(ctx, permitLimit: 10, windowSeconds: 60);

        Assert.Equal("198.51.100.10", partition.PartitionKey);
    }

    [Fact]
    public void TwoDifferentIps_DoNotShareTheSameBucket()
    {
        var a = AuthRateLimiter.BuildPartition(
            "203.0.113.1", permitLimit: 10, windowSeconds: 60);
        var b = AuthRateLimiter.BuildPartition(
            "203.0.113.2", permitLimit: 10, windowSeconds: 60);

        Assert.NotEqual(a.PartitionKey, b.PartitionKey);
    }

    // ──────────────────────────────────────────────────────────────
    // Attribute-presence contract on the four parent auth actions.
    // Controller-level unit tests bypass the rate-limiter middleware,
    // so the policy binding is proved declaratively by verifying the
    // attribute is attached to each action — same pattern
    // ParentControllerExportTests.Export_IsProtectedByAuthorizeAttribute
    // uses.
    // ──────────────────────────────────────────────────────────────

    private static bool HasAuthLimiterAttribute(string methodName)
    {
        var method = typeof(ParentController).GetMethod(methodName);
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .ToArray();
        return attrs.Any(a => a.PolicyName == AuthRateLimiter.PolicyName);
    }

    [Fact]
    public void Register_HasAuthRateLimitAttribute()
        => Assert.True(HasAuthLimiterAttribute(nameof(ParentController.Register)));

    [Fact]
    public void Login_HasAuthRateLimitAttribute()
        => Assert.True(HasAuthLimiterAttribute(nameof(ParentController.Login)));

    [Fact]
    public void ChangePassword_HasAuthRateLimitAttribute()
        => Assert.True(HasAuthLimiterAttribute(nameof(ParentController.ChangePassword)));

    [Fact]
    public void DeleteAccount_HasAuthRateLimitAttribute()
        => Assert.True(HasAuthLimiterAttribute(nameof(ParentController.DeleteAccount)));

    [Fact]
    public void ClaimDevice_HasAuthRateLimitAttribute()
        // Phase A.2: the claim code is a guessable secret (brute-force surface),
        // so unlike the other JWT-gated device control endpoints, claim IS
        // throttled on the per-IP auth bucket.
        => Assert.True(HasAuthLimiterAttribute(nameof(ParentController.ClaimDevice)));

    [Fact]
    public void DeviceRegister_HasAuthRateLimitAttribute()
    {
        // #010: device registration mints a credential that can drive the paid
        // STT+GPT+TTS endpoints, so it is throttled on the same per-IP auth
        // bucket (denial-of-wallet mitigation).
        var method = typeof(DeviceController).GetMethod(nameof(DeviceController.Register));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>().ToArray();
        Assert.Contains(attrs, a => a.PolicyName == AuthRateLimiter.PolicyName);
    }

    // Spot-check: prove we did NOT accidentally blanket-apply the auth
    // limiter to unrelated parent endpoints. These are the candidates
    // that share the /api/parents prefix but are NOT auth-sensitive in
    // the sense this slice is protecting (they are already parent-JWT
    // gated and are not brute-force surfaces).
    [Fact]
    public void LinkDevice_HasAuthRateLimitAttribute()
    {
        // QA hardening: the legacy API-key link path presents a device
        // credential, so it IS a brute-force / denial-of-wallet surface and
        // is throttled on the per-IP auth bucket (like register/login and
        // the claim path). Moved out of the negative spot-check below.
        var method = typeof(ParentController).GetMethod(nameof(ParentController.LinkDevice));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>().ToArray();
        Assert.Contains(attrs, a => a.PolicyName == AuthRateLimiter.PolicyName);
    }

    [Theory]
    [InlineData(nameof(ParentController.UnlinkDevice))]
    [InlineData(nameof(ParentController.PauseDevice))]
    [InlineData(nameof(ParentController.ResumeDevice))]
    [InlineData(nameof(ParentController.SetDeviceName))]
    [InlineData(nameof(ParentController.GetDevices))]
    [InlineData(nameof(ParentController.GetDeviceDetails))]
    [InlineData(nameof(ParentController.Export))]
    public void NonAuthEndpoints_DoNotCarryAuthRateLimitAttribute(string methodName)
    {
        Assert.False(HasAuthLimiterAttribute(methodName));
    }
}

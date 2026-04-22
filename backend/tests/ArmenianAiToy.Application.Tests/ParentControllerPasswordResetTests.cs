using System.Reflection;
using System.Text.Json;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Api.RateLimiting;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level contract tests for the password-reset endpoints.
/// Deep behavior is pinned at the service layer
/// (<see cref="ParentServicePasswordResetTests"/>); these tests pin
/// the HTTP contract: 202/200/400 shapes, auth limiter attribute,
/// parent-id flows from the posted email (not a claim — no auth on
/// these endpoints by design).
/// </summary>
public class ParentControllerPasswordResetTests
{
    private static (ParentController Controller, IParentService Parents) CreateController()
    {
        var parents = Substitute.For<IParentService>();
        return (new ParentController(parents, new ExportCooldown()), parents);
    }

    // ─────────────────────────────────────────────────────────────
    // reset-request
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RequestPasswordReset_HasAuthRateLimiterAttribute()
    {
        var method = typeof(ParentController).GetMethod(nameof(ParentController.RequestPasswordReset));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .ToArray();
        Assert.Contains(attrs, a => a.PolicyName == AuthRateLimiter.PolicyName);
    }

    [Fact]
    public async Task RequestPasswordReset_KnownEmail_Returns202WithNeutralBody()
    {
        var (controller, _) = CreateController();
        var request = new ParentPasswordResetRequest("owner@example.com");

        var result = await controller.RequestPasswordReset(request);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(202, accepted.StatusCode);
        var body = JsonSerializer.Serialize(accepted.Value, JsonSerializerOptions.Web);
        Assert.Equal("{\"resetRequested\":true}", body);
        Assert.DoesNotContain("parentId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_Returns202WithIdenticalBody()
    {
        var (controller, _) = CreateController();

        var knownResult = await controller.RequestPasswordReset(
            new ParentPasswordResetRequest("owner@example.com"));
        var unknownResult = await controller.RequestPasswordReset(
            new ParentPasswordResetRequest("stranger@example.com"));

        Assert.Equal(((AcceptedResult)knownResult).StatusCode,
                     ((AcceptedResult)unknownResult).StatusCode);
        Assert.Equal(
            JsonSerializer.Serialize(((AcceptedResult)knownResult).Value, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(((AcceptedResult)unknownResult).Value, JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task RequestPasswordReset_EmptyEmail_Returns400()
    {
        var (controller, parents) = CreateController();

        var result = await controller.RequestPasswordReset(
            new ParentPasswordResetRequest(""));

        Assert.IsType<BadRequestObjectResult>(result);
        // Service was NEVER called — we short-circuit before the
        // service, so no timing-normalization is even attempted for
        // an obviously-malformed request.
        await parents.DidNotReceiveWithAnyArgs().RequestPasswordResetAsync(default!, default);
    }

    // ─────────────────────────────────────────────────────────────
    // reset-completion
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CompletePasswordReset_HasAuthRateLimiterAttribute()
    {
        var method = typeof(ParentController).GetMethod(nameof(ParentController.CompletePasswordReset));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .ToArray();
        Assert.Contains(attrs, a => a.PolicyName == AuthRateLimiter.PolicyName);
    }

    [Fact]
    public async Task CompletePasswordReset_ServiceReturnsTrue_Returns200ResetTrue()
    {
        var (controller, parents) = CreateController();
        parents.CompletePasswordResetAsync("valid-token", "new-strong-pass9").Returns(true);

        var result = await controller.CompletePasswordReset(
            new ParentPasswordResetCompleteRequest("valid-token", "new-strong-pass9"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = JsonSerializer.Serialize(ok.Value, JsonSerializerOptions.Web);
        Assert.Equal("{\"reset\":true}", body);
    }

    [Fact]
    public async Task CompletePasswordReset_ServiceReturnsFalse_ReturnsUniform400()
    {
        var (controller, parents) = CreateController();
        parents.CompletePasswordResetAsync("bad-token", "new-strong-pass9").Returns(false);

        var result = await controller.CompletePasswordReset(
            new ParentPasswordResetCompleteRequest("bad-token", "new-strong-pass9"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = JsonSerializer.Serialize(bad.Value, JsonSerializerOptions.Web);
        // Uniform body — does not distinguish unknown / expired /
        // consumed. Matches the service's uniform-false contract.
        Assert.Contains("invalid or expired", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletePasswordReset_EmptyToken_Returns400_WithoutCallingService()
    {
        var (controller, parents) = CreateController();

        var result = await controller.CompletePasswordReset(
            new ParentPasswordResetCompleteRequest("", "new-strong-pass9"));

        Assert.IsType<BadRequestObjectResult>(result);
        await parents.DidNotReceiveWithAnyArgs().CompletePasswordResetAsync(default!, default!);
    }

    [Fact]
    public async Task CompletePasswordReset_ShortPassword_Returns400_WithoutCallingService()
    {
        // Short passwords fail with the SAME uniform error body as a
        // bad token — do not let a new-password validator become a
        // side-channel for "was the token good?".
        var (controller, parents) = CreateController();

        var result = await controller.CompletePasswordReset(
            new ParentPasswordResetCompleteRequest("any-token", "short"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = JsonSerializer.Serialize(bad.Value, JsonSerializerOptions.Web);
        Assert.Contains("invalid or expired", body, StringComparison.OrdinalIgnoreCase);
        await parents.DidNotReceiveWithAnyArgs().CompletePasswordResetAsync(default!, default!);
    }
}

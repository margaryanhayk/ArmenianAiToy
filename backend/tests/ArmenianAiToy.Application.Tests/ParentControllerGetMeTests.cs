using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <c>GET /api/parents/me</c> — the minimal authenticated
/// profile lookup added to support the dashboard's verification-
/// visibility surface. Pinned contracts:
/// <list type="bullet">
///   <item><description>endpoint is JWT-gated (<c>[Authorize]</c>);</description></item>
///   <item><description>delegates to <see cref="IParentService.GetMeAsync"/>
///   without modification;</description></item>
///   <item><description>404 on a parent whose service-side lookup
///   returns null (deleted or anonymized between JWT-issue and the
///   call) — same silent-miss shape the rest of the parent-owned
///   endpoints use.</description></item>
/// </list>
/// </summary>
public class ParentControllerGetMeTests
{
    private static (ParentController Controller, IParentService Service) CreateController(
        Guid parentId)
    {
        var service = Substitute.For<IParentService>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero));
        var cooldown = new ExportCooldown(TimeSpan.FromSeconds(60), clock);
        var controller = new ParentController(service, cooldown);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, service);
    }

    [Fact]
    public void GetMe_IsProtectedByAuthorizeAttribute()
    {
        // Controller-level unit tests bypass the ASP.NET auth
        // middleware, so "anonymous => 401" is proven declaratively
        // by verifying the [Authorize] attribute is attached.
        var method = typeof(ParentController).GetMethod(nameof(ParentController.GetMe));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public async Task GetMe_ServiceReturnsResponse_Returns200WithBody()
    {
        var parentId = Guid.NewGuid();
        var (controller, service) = CreateController(parentId);
        var expected = new ParentMeResponse(
            "owner@example.com",
            new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc));
        service.GetMeAsync(parentId).Returns(expected);

        var result = await controller.GetMe();

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ParentMeResponse>(ok.Value);
        Assert.Equal("owner@example.com", body.Email);
        Assert.Equal(expected.EmailVerifiedAt, body.EmailVerifiedAt);
    }

    [Fact]
    public async Task GetMe_ServiceReturnsResponseWithNullVerifiedAt_Returns200WithNullField()
    {
        // Unverified parents are the primary consumer of this
        // endpoint — the dashboard's "Send verification email"
        // button uses the resulting email to call verify-request.
        // The null EmailVerifiedAt must round-trip cleanly.
        var parentId = Guid.NewGuid();
        var (controller, service) = CreateController(parentId);
        service.GetMeAsync(parentId).Returns(
            new ParentMeResponse("unverified@example.com", null));

        var result = await controller.GetMe();

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ParentMeResponse>(ok.Value);
        Assert.Equal("unverified@example.com", body.Email);
        Assert.Null(body.EmailVerifiedAt);
    }

    [Fact]
    public async Task GetMe_ServiceReturnsNull_Returns404()
    {
        // Stale-JWT-against-deleted-or-anonymized-parent: service
        // returns null (the AnonymizedAt == null filter in
        // GetMeAsync filters scrubbed rows). Controller maps this
        // to 404 with no body, matching the silent-miss convention
        // used elsewhere.
        var parentId = Guid.NewGuid();
        var (controller, service) = CreateController(parentId);
        service.GetMeAsync(parentId).Returns((ParentMeResponse?)null);

        var result = await controller.GetMe();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMe_PassesParentIdFromJwtClaim_ToService()
    {
        // Pin that the controller resolves the parent id from the
        // ClaimTypes.NameIdentifier claim and not from any other
        // source — same posture every other parent-authenticated
        // endpoint uses.
        var parentId = Guid.NewGuid();
        var (controller, service) = CreateController(parentId);
        service.GetMeAsync(parentId).Returns(new ParentMeResponse("e@x.com", null));

        await controller.GetMe();

        await service.Received(1).GetMeAsync(parentId);
    }
}

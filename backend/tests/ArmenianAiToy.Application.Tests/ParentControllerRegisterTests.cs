using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level tests for the ParentController.Register password-policy
/// guard. The parent dashboard exposes the child's full conversation history,
/// so weak-password acceptance is a parent-trust issue: it is the user-side
/// complement of the server-side Jwt:Key hardening in commit 8d8e120.
/// </summary>
public class ParentControllerRegisterTests
{
    private static (ParentController Controller, IParentService Parents) CreateController()
    {
        var parents = Substitute.For<IParentService>();
        return (new ParentController(parents, new ExportCooldown()), parents);
    }

    [Fact]
    public async Task Register_WhenPasswordTooShort_ReturnsBadRequest_AndDoesNotCallService()
    {
        var (controller, parents) = CreateController();
        var request = new ParentRegisterRequest("a@b.com", "short12", AcceptedTerms: true); // 7 chars

        var result = await controller.Register(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errorProp = bad.Value!.GetType().GetProperty("error");
        Assert.Contains("at least 8", errorProp!.GetValue(bad.Value) as string);
        await parents.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!, default);
    }

    [Fact]
    public async Task Register_WhenPasswordIsValid_DelegatesToService()
    {
        // Anti-tautology guard: proves the new length check does not
        // incorrectly reject passwords that meet the minimum.
        var (controller, parents) = CreateController();
        parents.RegisterAsync("a@b.com", "strongpass9", true).Returns(Guid.NewGuid());
        var request = new ParentRegisterRequest("a@b.com", "strongpass9", AcceptedTerms: true); // 11 chars

        var result = await controller.Register(request);

        Assert.IsType<CreatedResult>(result);
        await parents.Received(1).RegisterAsync("a@b.com", "strongpass9", true);
    }

    [Fact]
    public async Task Register_WhenTermsNotAccepted_ReturnsBadRequest_AndDoesNotCallService()
    {
        // Regression for C1: the controller must reject a registration that
        // does not carry an explicit AcceptedTerms=true signal. The DTO's
        // default is false, so a caller that omits the field is implicitly
        // declining and must hit this path before any service work fires.
        var (controller, parents) = CreateController();
        var request = new ParentRegisterRequest("a@b.com", "strongpass9", AcceptedTerms: false);

        var result = await controller.Register(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errorProp = bad.Value!.GetType().GetProperty("error");
        Assert.Contains("accept the terms", errorProp!.GetValue(bad.Value) as string);
        await parents.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!, default);
    }

    [Fact]
    public async Task Register_WhenPasswordMissing_ReturnsBadRequest()
    {
        // Documents that the pre-existing whitespace/missing guard still
        // fires before the length guard — a future refactor that merges
        // them into a single check must preserve both branches.
        var (controller, parents) = CreateController();
        var request = new ParentRegisterRequest("a@b.com", "   ", AcceptedTerms: true);

        var result = await controller.Register(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var errorProp = bad.Value!.GetType().GetProperty("error");
        Assert.Contains("required", errorProp!.GetValue(bad.Value) as string);
        await parents.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default!, default);
    }
}

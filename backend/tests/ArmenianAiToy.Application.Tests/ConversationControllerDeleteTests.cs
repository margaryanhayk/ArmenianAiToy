using System.Reflection;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level contract tests for
/// <c>DELETE /api/conversations/{conversationId}</c>.
/// The deep service-level behaviour (cascade, audit shape) is proven in
/// <see cref="ParentServiceDeleteConversationTests"/>; these tests pin
/// the HTTP contract — [Authorize] present, 200/404 shape, silent-miss
/// phrasing consistent with existing parent destructive endpoints.
/// </summary>
public class ConversationControllerDeleteTests
{
    private static (ConversationController Controller,
                    IParentService Parents,
                    Guid ParentId)
        CreateController()
    {
        var convos = Substitute.For<IConversationService>();
        var parents = Substitute.For<IParentService>();
        var parentId = Guid.NewGuid();
        var controller = new ConversationController(convos, parents);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, parents, parentId);
    }

    [Fact]
    public void Delete_IsProtectedByAuthorizeAttribute()
    {
        // Unit-testing the controller bypasses the auth middleware, so
        // "anonymous => 401" is proven declaratively by verifying
        // [Authorize] is attached — same pattern ParentControllerExportTests
        // uses for Export.
        var method = typeof(ConversationController).GetMethod(nameof(ConversationController.Delete));
        Assert.NotNull(method);
        var attrs = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public async Task Delete_ServiceReturnsTrue_Returns200DeletedTrue()
    {
        var (controller, parents, parentId) = CreateController();
        var conversationId = Guid.NewGuid();
        parents.DeleteConversationAsync(parentId, conversationId).Returns(true);

        var result = await controller.Delete(conversationId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        // deleted: true — same shape as DeleteChild / DeleteAccount.
        var deletedProp = ok.Value!.GetType().GetProperty("deleted");
        Assert.NotNull(deletedProp);
        Assert.Equal(true, deletedProp!.GetValue(ok.Value));
    }

    [Fact]
    public async Task Delete_ServiceReturnsFalse_ReturnsNotFoundWithSilentPhrasing()
    {
        var (controller, parents, parentId) = CreateController();
        var conversationId = Guid.NewGuid();
        parents.DeleteConversationAsync(parentId, conversationId).Returns(false);

        var result = await controller.Delete(conversationId);

        var nf = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(nf.Value);
        var errorProp = nf.Value!.GetType().GetProperty("error");
        Assert.NotNull(errorProp);
        var err = (string?)errorProp!.GetValue(nf.Value);
        // Phrasing pinned: same "not found or not owned" shape as
        // DeleteChild / SetDevicePauseStateAsync etc. — no existence leak.
        Assert.NotNull(err);
        Assert.Contains("not found", err!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not owned", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delete_UsesAuthenticatedParentIdFromClaim()
    {
        var (controller, parents, parentId) = CreateController();
        var conversationId = Guid.NewGuid();
        parents.DeleteConversationAsync(parentId, conversationId).Returns(true);

        await controller.Delete(conversationId);

        // Exactly (parentId, conversationId) — not anyone else's id.
        await parents.Received(1).DeleteConversationAsync(parentId, conversationId);
    }
}

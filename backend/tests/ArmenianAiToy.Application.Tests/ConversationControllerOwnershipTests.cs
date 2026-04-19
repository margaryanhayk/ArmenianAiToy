using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level ownership regression tests for ConversationController.
///
/// Pins two invariants that are security-critical to parent-trust and are
/// documented only by code comment / CLAUDE.md today:
///
///  1. GetById returns the SAME NotFound response for "conversation doesn't
///     exist" and "conversation belongs to a device not linked to this
///     parent". No existence leak — a parent must not be able to infer
///     whether another family's conversation ID is real.
///
///  2. The three list endpoints (GetHistory, GetSummary, GetFlagged) return
///     Forbid when the queried deviceId is not linked to the caller.
/// </summary>
public class ConversationControllerOwnershipTests
{
    private static (ConversationController Controller,
                    IConversationService Convos,
                    IParentService Parents,
                    Guid OwnedDeviceId,
                    Guid UnlinkedDeviceId)
        CreateController()
    {
        var convos = Substitute.For<IConversationService>();
        var parents = Substitute.For<IParentService>();

        var parentId = Guid.NewGuid();
        var ownedDeviceId = Guid.NewGuid();
        var unlinkedDeviceId = Guid.NewGuid();

        parents.GetLinkedDeviceIdsAsync(Arg.Any<Guid>())
            .Returns(new List<Guid> { ownedDeviceId });

        var controller = new ConversationController(convos, parents);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, parentId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return (controller, convos, parents, ownedDeviceId, unlinkedDeviceId);
    }

    private static ConversationDto MakeConversation(Guid conversationId, Guid deviceId) =>
        new(
            Id: conversationId,
            DeviceId: deviceId,
            StartedAt: DateTime.UtcNow,
            EndedAt: null,
            MessageCount: 0,
            HasFlaggedContent: false,
            Messages: new List<MessageDto>());

    // ─────────────────────────────────────────────────────────────────────
    // GetById — no-existence-leak keystone
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_WhenConversationDoesNotExist_ReturnsNotFound()
    {
        var (controller, convos, _, _, _) = CreateController();
        var conversationId = Guid.NewGuid();
        convos.GetConversationByIdAsync(conversationId).Returns((ConversationDto?)null);

        var result = await controller.GetById(conversationId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_WhenConversationExistsButDeviceNotLinked_ReturnsNotFound()
    {
        // Keystone: "not yours" must return the SAME shape as "doesn't exist".
        // Any drift to ForbidResult / StatusCodeResult(403) / OkObjectResult
        // would leak existence and break the documented invariant.
        var (controller, convos, _, _, unlinkedDeviceId) = CreateController();
        var conversationId = Guid.NewGuid();
        convos.GetConversationByIdAsync(conversationId)
            .Returns(MakeConversation(conversationId, unlinkedDeviceId));

        var result = await controller.GetById(conversationId);

        Assert.IsType<NotFoundResult>(result);
        Assert.IsNotType<ForbidResult>(result);
        Assert.IsNotType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetById_WhenConversationExistsAndOwned_ReturnsOk()
    {
        // Anti-tautology guard: proves the two NotFound tests above are not
        // vacuously green because the action always returns NotFound.
        var (controller, convos, _, ownedDeviceId, _) = CreateController();
        var conversationId = Guid.NewGuid();
        convos.GetConversationByIdAsync(conversationId)
            .Returns(MakeConversation(conversationId, ownedDeviceId));

        var result = await controller.GetById(conversationId);

        Assert.IsType<OkObjectResult>(result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // List endpoints — Forbid on unlinked deviceId
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_WhenDeviceNotLinked_ReturnsForbid()
    {
        var (controller, convos, _, _, unlinkedDeviceId) = CreateController();

        var result = await controller.GetHistory(unlinkedDeviceId, limit: 10, offset: 0);

        Assert.IsType<ForbidResult>(result);
        await convos.DidNotReceiveWithAnyArgs().GetConversationHistoryAsync(default, default, default);
    }

    [Fact]
    public async Task GetSummary_WhenDeviceNotLinked_ReturnsForbid()
    {
        var (controller, convos, _, _, unlinkedDeviceId) = CreateController();

        var result = await controller.GetSummary(unlinkedDeviceId, limit: 20, offset: 0);

        Assert.IsType<ForbidResult>(result);
        await convos.DidNotReceiveWithAnyArgs().GetConversationSummariesAsync(default, default, default);
    }

    [Fact]
    public async Task GetFlagged_WhenDeviceNotLinked_ReturnsForbid()
    {
        var (controller, convos, _, _, unlinkedDeviceId) = CreateController();

        var result = await controller.GetFlagged(unlinkedDeviceId, limit: 20, offset: 0);

        Assert.IsType<ForbidResult>(result);
        await convos.DidNotReceiveWithAnyArgs().GetFlaggedMessagesAsync(default, default, default);
    }
}

using System.Reflection;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level tests for ConversationController.GetTodaySummary
/// (E1.2). Pin the privacy / shape invariants documented in CLAUDE.md
/// "Today summary endpoint (E1.2)":
///
///  1. [Authorize] is applied — anonymous callers are rejected by the
///     auth pipeline before the action runs (reflection-based pin).
///  2. Unowned deviceId returns 403 Forbid (matches the /summary,
///     /flagged, /history convention; the silent-404 pattern is
///     reserved for the conversation-by-id family).
///  3. Owned deviceId returns 200 with the DTO.
///  4. Optional asOfUtc is parsed via DateTimeOffset and normalized
///     to UTC before reaching the service.
///  5. Response shape does NOT expose ChildId or AudioBlobPath — those
///     are deliberate omissions.
/// </summary>
public class ConversationControllerTodaySummaryTests
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

    private static TodaySummaryDto MakeDto(Guid deviceId) =>
        new(
            DeviceId: deviceId,
            AsOfUtc: new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            DayStartUtc: new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            ConversationsCount: 0,
            MessagesCount: 0,
            FlaggedMessagesCount: 0,
            AssistantMessagesWithAudio: 0,
            Newest: new List<TodaySummaryConversationLink>(),
            Flagged: new List<TodaySummaryConversationLink>());

    [Fact]
    public void GetTodaySummary_HasAuthorizeAttribute_AnonymousRejectedByPipeline()
    {
        // Reflection-based check that [Authorize] is on the action. The
        // attribute itself enforces the 401 at the auth-pipeline layer
        // before the action runs; if it ever drops off the action, this
        // test fails loudly.
        var method = typeof(ConversationController).GetMethod(
            nameof(ConversationController.GetTodaySummary),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
    }

    [Fact]
    public async Task GetTodaySummary_UnownedDevice_ReturnsForbid()
    {
        var (controller, convos, _, _, unlinkedDeviceId) = CreateController();

        var result = await controller.GetTodaySummary(unlinkedDeviceId, asOfUtc: null);

        Assert.IsType<ForbidResult>(result);
        await convos.DidNotReceiveWithAnyArgs().GetTodaySummaryAsync(default, default);
    }

    [Fact]
    public async Task GetTodaySummary_OwnedDevice_ReturnsOkWithDto()
    {
        var (controller, convos, _, ownedDeviceId, _) = CreateController();
        var dto = MakeDto(ownedDeviceId);
        convos.GetTodaySummaryAsync(ownedDeviceId, Arg.Any<DateTime>()).Returns(dto);

        var result = await controller.GetTodaySummary(ownedDeviceId, asOfUtc: null);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(dto, ok.Value);
    }

    [Fact]
    public async Task GetTodaySummary_DefaultAsOfUtc_ServiceReceivesUtcKind()
    {
        var (controller, convos, _, ownedDeviceId, _) = CreateController();
        convos.GetTodaySummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
              .Returns(MakeDto(ownedDeviceId));

        await controller.GetTodaySummary(ownedDeviceId, asOfUtc: null);

        await convos.Received(1).GetTodaySummaryAsync(
            ownedDeviceId,
            Arg.Is<DateTime>(d => d.Kind == DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetTodaySummary_OffsetAsOfUtc_NormalizedToUtc()
    {
        var (controller, convos, _, ownedDeviceId, _) = CreateController();
        convos.GetTodaySummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTime>())
              .Returns(MakeDto(ownedDeviceId));

        // 2026-04-30 14:00 +02:00 == 2026-04-30 12:00 UTC.
        var offset = new DateTimeOffset(2026, 4, 30, 14, 0, 0,
            TimeSpan.FromHours(2));
        var expectedUtc = new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);

        await controller.GetTodaySummary(ownedDeviceId, asOfUtc: offset);

        await convos.Received(1).GetTodaySummaryAsync(
            ownedDeviceId,
            Arg.Is<DateTime>(d => d == expectedUtc && d.Kind == DateTimeKind.Utc));
    }

    [Fact]
    public void TodaySummaryDto_DoesNotExposeChildIdOrAudioBlobPath()
    {
        // Wire-shape pin: the DTO must NOT carry per-child or per-blob
        // identifiers. AssistantMessagesWithAudio is a count only; the
        // path itself never leaves the service layer.
        var dtoProps = typeof(TodaySummaryDto)
            .GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("ChildId", dtoProps);
        Assert.DoesNotContain("AudioBlobPath", dtoProps);

        var linkProps = typeof(TodaySummaryConversationLink)
            .GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("ChildId", linkProps);
        Assert.DoesNotContain("AudioBlobPath", linkProps);
    }
}

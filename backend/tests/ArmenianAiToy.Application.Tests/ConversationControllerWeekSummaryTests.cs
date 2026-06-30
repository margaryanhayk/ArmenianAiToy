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
/// Controller-level tests for ConversationController.GetWeekSummary.
/// Mirrors the Today-summary controller contract:
///  1. [Authorize] is applied (reflection-based pin).
///  2. Unowned deviceId returns 403 Forbid and never calls the service.
///  3. Owned deviceId returns 200 with the DTO.
///  4. asOfUtc is normalized to UTC; tz passes through.
///  5. The DTO exposes no ChildId / AudioBlobPath (counts only).
/// </summary>
public class ConversationControllerWeekSummaryTests
{
    private static (ConversationController Controller,
                    IConversationService Convos,
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
        return (controller, convos, ownedDeviceId, unlinkedDeviceId);
    }

    private static WeekSummaryDto MakeDto(Guid deviceId) =>
        new(
            DeviceId: deviceId,
            AsOfUtc: new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            WindowStartUtc: new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            WindowStartLocal: new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Unspecified),
            TimeZoneId: "UTC",
            TimeZoneResolved: false,
            ConversationsCount: 0,
            MessagesCount: 0,
            FlaggedMessagesCount: 0,
            AssistantMessagesWithAudio: 0,
            ActiveDays: 0,
            Days: new List<WeekDaySummary>());

    [Fact]
    public void GetWeekSummary_HasAuthorizeAttribute()
    {
        var method = typeof(ConversationController).GetMethod(
            nameof(ConversationController.GetWeekSummary),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task GetWeekSummary_UnownedDevice_ReturnsForbid()
    {
        var (controller, convos, _, unlinkedDeviceId) = CreateController();

        var result = await controller.GetWeekSummary(unlinkedDeviceId, asOfUtc: null);

        Assert.IsType<ForbidResult>(result);
        await convos.DidNotReceiveWithAnyArgs().GetWeekSummaryAsync(default, default, default);
    }

    [Fact]
    public async Task GetWeekSummary_OwnedDevice_ReturnsOkWithDto()
    {
        var (controller, convos, ownedDeviceId, _) = CreateController();
        var dto = MakeDto(ownedDeviceId);
        convos.GetWeekSummaryAsync(ownedDeviceId, Arg.Any<DateTime>(), Arg.Any<string?>())
              .Returns(dto);

        var result = await controller.GetWeekSummary(ownedDeviceId, asOfUtc: null);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(dto, ok.Value);
    }

    [Fact]
    public async Task GetWeekSummary_OffsetAsOfUtc_NormalizedToUtc()
    {
        var (controller, convos, ownedDeviceId, _) = CreateController();
        convos.GetWeekSummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>())
              .Returns(MakeDto(ownedDeviceId));

        var offset = new DateTimeOffset(2026, 4, 30, 14, 0, 0, TimeSpan.FromHours(2));
        var expectedUtc = new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);

        await controller.GetWeekSummary(ownedDeviceId, asOfUtc: offset);

        await convos.Received(1).GetWeekSummaryAsync(
            ownedDeviceId,
            Arg.Is<DateTime>(d => d == expectedUtc && d.Kind == DateTimeKind.Utc),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task GetWeekSummary_TzQueryParam_PassesThroughToService()
    {
        var (controller, convos, ownedDeviceId, _) = CreateController();
        convos.GetWeekSummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>())
              .Returns(MakeDto(ownedDeviceId));

        await controller.GetWeekSummary(ownedDeviceId, asOfUtc: null, tz: "America/Los_Angeles");

        await convos.Received(1).GetWeekSummaryAsync(
            ownedDeviceId, Arg.Any<DateTime>(), "America/Los_Angeles");
    }

    [Fact]
    public void WeekSummaryDto_DoesNotExposeChildIdOrAudioBlobPath()
    {
        var dtoProps = typeof(WeekSummaryDto)
            .GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("ChildId", dtoProps);
        Assert.DoesNotContain("AudioBlobPath", dtoProps);
        Assert.Contains("TimeZoneId", dtoProps);
        Assert.Contains("TimeZoneResolved", dtoProps);
        Assert.Contains("Days", dtoProps);

        var dayProps = typeof(WeekDaySummary)
            .GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("ChildId", dayProps);
        Assert.DoesNotContain("AudioBlobPath", dayProps);
    }
}

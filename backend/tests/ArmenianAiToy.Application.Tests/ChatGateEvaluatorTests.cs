using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="ChatGateEvaluator"/> — the extracted
/// paused / bedtime / mode-disabled gate check shared by the text
/// chat endpoint and the voice chat endpoint. The refactor must
/// preserve the shipped text-path ordering (pause > bedtime > mode)
/// exactly; these tests pin that contract at the helper level.
/// </summary>
public class ChatGateEvaluatorTests
{
    private static readonly DateTime FixedNowUtc =
        new(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Evaluate_Paused_ReturnsPausedWithoutCheckingOtherGates()
    {
        var device = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        device.IsDevicePausedAsync(deviceId).Returns(true);

        var result = await ChatGateEvaluator.EvaluateAsync(
            device, deviceId, "պատմիր հեքիաթ", childId: null, FixedNowUtc);

        Assert.Equal(ChatGateEvaluator.GateDecision.Paused, result);
        await device.Received(1).IsDevicePausedAsync(deviceId);
        // Short-circuit — bedtime and mode checks must not run.
        await device.DidNotReceiveWithAnyArgs().IsDeviceInBedtimeWindowAsync(default, default);
        await device.DidNotReceiveWithAnyArgs().IsModeEnabledForRequestAsync(default, default, default);
    }

    [Fact]
    public async Task Evaluate_Bedtime_ReturnsBedtimeWithoutCheckingMode()
    {
        var device = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        device.IsDevicePausedAsync(deviceId).Returns(false);
        device.IsDeviceInBedtimeWindowAsync(deviceId, FixedNowUtc).Returns(true);

        var result = await ChatGateEvaluator.EvaluateAsync(
            device, deviceId, "պատմիր հեքիաթ", childId: null, FixedNowUtc);

        Assert.Equal(ChatGateEvaluator.GateDecision.Bedtime, result);
        await device.DidNotReceiveWithAnyArgs()
            .IsModeEnabledForRequestAsync(default, default, default);
    }

    [Fact]
    public async Task Evaluate_StoryDetected_DisabledMode_ReturnsModeDisabled()
    {
        var device = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        device.IsDevicePausedAsync(deviceId).Returns(false);
        device.IsDeviceInBedtimeWindowAsync(deviceId, FixedNowUtc).Returns(false);
        // "Story" detection happens for a typical Story-intent utterance.
        device.IsModeEnabledForRequestAsync(
            deviceId, (Guid?)null, DetectedMode.Story).Returns(false);

        var result = await ChatGateEvaluator.EvaluateAsync(
            device, deviceId, "պատմիր մի հեքիաթ", childId: null, FixedNowUtc);

        Assert.Equal(ChatGateEvaluator.GateDecision.ModeDisabled, result);
    }

    [Fact]
    public async Task Evaluate_StoryDetected_EnabledMode_ReturnsAllow()
    {
        var device = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        device.IsDevicePausedAsync(deviceId).Returns(false);
        device.IsDeviceInBedtimeWindowAsync(deviceId, FixedNowUtc).Returns(false);
        device.IsModeEnabledForRequestAsync(
            deviceId, (Guid?)null, DetectedMode.Story).Returns(true);

        var result = await ChatGateEvaluator.EvaluateAsync(
            device, deviceId, "պատմիր մի հեքիաթ", childId: null, FixedNowUtc);

        Assert.Equal(ChatGateEvaluator.GateDecision.Allow, result);
    }

    [Fact]
    public async Task Evaluate_AmbiguousMessage_SkipsModeCheck_ReturnsAllow()
    {
        // ModeDetector returns None / Calm for non-trigger messages.
        // The gate must NOT call IsModeEnabledForRequestAsync in that
        // case — bedtime cues must always reach Calm handling, and
        // no-match messages pass through normally. This is the
        // "conservative detector" contract the text path shipped
        // with.
        var device = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        device.IsDevicePausedAsync(deviceId).Returns(false);
        device.IsDeviceInBedtimeWindowAsync(deviceId, FixedNowUtc).Returns(false);

        var result = await ChatGateEvaluator.EvaluateAsync(
            device, deviceId, "hmm", childId: null, FixedNowUtc);

        Assert.Equal(ChatGateEvaluator.GateDecision.Allow, result);
        // The ambiguity guard MUST have prevented the mode check.
        await device.DidNotReceiveWithAnyArgs()
            .IsModeEnabledForRequestAsync(default, default, default);
    }

    [Fact]
    public async Task Evaluate_ChildIdThreaded_IntoModeEnabledCall()
    {
        // Per-child overrides are respected: the ChildId we pass
        // into the evaluator must reach IsModeEnabledForRequestAsync
        // verbatim.
        var device = Substitute.For<IDeviceService>();
        var deviceId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        device.IsDevicePausedAsync(deviceId).Returns(false);
        device.IsDeviceInBedtimeWindowAsync(deviceId, FixedNowUtc).Returns(false);
        device.IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Story).Returns(true);

        var result = await ChatGateEvaluator.EvaluateAsync(
            device, deviceId, "պատմիր մի հեքիաթ", childId, FixedNowUtc);

        Assert.Equal(ChatGateEvaluator.GateDecision.Allow, result);
        await device.Received(1).IsModeEnabledForRequestAsync(
            deviceId, childId, DetectedMode.Story);
    }
}

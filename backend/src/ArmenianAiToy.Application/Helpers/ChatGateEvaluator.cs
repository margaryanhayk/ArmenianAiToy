using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Shared paused / bedtime / mode-disabled gate check used by both the
/// text chat endpoint and the audio (voice) chat endpoint. Preserves
/// the exact ordering the existing text path already ships:
/// <list type="number">
///   <item><description><c>paused</c> (highest priority — parent soft-off)</description></item>
///   <item><description><c>bedtime</c> (scheduled quiet hours)</description></item>
///   <item><description><c>mode_disabled</c> (B5 / per-child override)</description></item>
/// </list>
/// Pure coordination helper — no metrics, no logging, no side effects.
/// The caller emits <c>AppMeter.ChatGateTrip</c> and chooses the canned
/// response shape (text envelope vs. TTS'd audio). Keeping metrics out
/// of the evaluator lets the two caller surfaces differ in their
/// response format without either one duplicating gate logic.
/// <para>
/// Mode-disabled detection uses <see cref="ModeDetector.Detect"/> with
/// <c>history: null, hasActiveStorySession: false</c> — conservative
/// single-message detection, identical to the text-path call. Callers
/// that want a specific-mode check (e.g. the voice path, which treats
/// the whole endpoint as Story-only in C1) can bypass the detector
/// call by invoking <see cref="IDeviceService.IsModeEnabledForRequestAsync"/>
/// directly with an explicit mode.
/// </para>
/// </summary>
public static class ChatGateEvaluator
{
    public enum GateDecision
    {
        /// <summary>No gate tripped — caller should proceed with normal chat flow.</summary>
        Allow,
        /// <summary>
        /// No parent account holds this toy. It has been unlinked and is
        /// waiting to be paired again from its QR, so there is nobody who
        /// could see or stop what it says.
        /// </summary>
        Unclaimed,
        /// <summary>Parent has paused the device.</summary>
        Paused,
        /// <summary>Device is inside its configured bedtime window.</summary>
        Bedtime,
        /// <summary>The detected mode is disabled on this device / child.</summary>
        ModeDisabled
    }

    /// <summary>
    /// Run the three gates in their shipped order. Returns the first
    /// gate that trips, or <see cref="GateDecision.Allow"/> if the
    /// caller should proceed.
    /// <para>
    /// <paramref name="userMessage"/> is consulted only for
    /// <see cref="ModeDetector"/>. The caller remains responsible for
    /// transcription (if the input is audio) and for any further
    /// classification once the gate allows.
    /// </para>
    /// </summary>
    public static async Task<GateDecision> EvaluateAsync(
        IDeviceService deviceService,
        Guid deviceId,
        string userMessage,
        Guid? childId,
        DateTime nowUtc)
    {
        // Runs FIRST, ahead of pause. An unowned toy is not a parent
        // preference that pause/bedtime could override — there is no parent
        // at all. Everything below this line assumes somebody is watching.
        if (!await deviceService.HasLinkedParentAsync(deviceId))
            return GateDecision.Unclaimed;

        if (await deviceService.IsDevicePausedAsync(deviceId))
            return GateDecision.Paused;

        if (await deviceService.IsDeviceInBedtimeWindowAsync(deviceId, nowUtc))
            return GateDecision.Bedtime;

        var detectedMode = ModeDetector.Detect(
            userMessage, history: null, hasActiveStorySession: false);
        if (detectedMode is DetectedMode.Story
                or DetectedMode.Game
                or DetectedMode.Riddle
                or DetectedMode.Curiosity)
        {
            var enabled = await deviceService.IsModeEnabledForRequestAsync(
                deviceId, childId, detectedMode);
            if (!enabled)
                return GateDecision.ModeDisabled;
        }

        return GateDecision.Allow;
    }
}

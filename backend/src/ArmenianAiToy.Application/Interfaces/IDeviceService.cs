using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Domain.Entities;

namespace ArmenianAiToy.Application.Interfaces;

public interface IDeviceService
{
    /// <summary>
    /// Registers a device. A NEW MAC creates the device and returns its id + key.
    /// An EXISTING MAC is refused (returns null) UNLESS
    /// <paramref name="allowReRegister"/> is true — #011: a plain re-registration
    /// must NOT silently rotate an in-field device's credential (that is both a
    /// DoS and a hijack vector). Rotation is a deliberate, explicitly-requested
    /// re-provision only.
    /// </summary>
    Task<DeviceRegistrationResponse?> RegisterDeviceAsync(
        DeviceRegistrationRequest request, bool allowReRegister = false);
    Task<Device?> ValidateDeviceAsync(Guid deviceId, string apiKey);
    Task UpdateLastSeenAsync(Guid deviceId);

    /// <summary>Load a device by id (or null). Used by the firmware-manifest
    /// endpoint to read the device's reported version/board.</summary>
    Task<Device?> GetDeviceAsync(Guid deviceId);

    /// <summary>Stamp the device's reported firmware/board fields (only the
    /// non-null ones) plus <c>FirmwareReportedAt</c>, from a heartbeat body.
    /// No-op when the device row is missing.</summary>
    Task UpdateFirmwareReportAsync(Guid deviceId, DeviceHeartbeatRequest report, DateTime nowUtc);
    /// <summary>Ingest a batch of device-reported story playback events
    /// (store-and-forward upload). Idempotent per (device, event key) — the
    /// transport is at-least-once, so duplicates are silent no-ops. Returns
    /// the number of NEW rows written.</summary>
    Task<int> ReportStoryPlaysAsync(Guid deviceId, StoryPlayReportRequest request, DateTime nowUtc);

    /// <summary>B4 — append one child reflection answer (per-listen row,
    /// never an overwrite). Called by the reflection voice endpoint after
    /// STT + moderation; best-effort at the call site (a persistence failure
    /// must never break the spoken reply).</summary>
    Task AddStoryReflectionAnswerAsync(
        Guid deviceId, string storyId, int questionIndex,
        string answerText, Domain.Enums.SafetyFlag safetyFlag, DateTime nowUtc);
    /// <summary>
    /// True when at least one parent account is linked to this toy. False
    /// means the toy has been unlinked and is waiting to be paired again
    /// from its QR — see <c>ChatGateEvaluator.GateDecision.Unclaimed</c>.
    /// </summary>
    Task<bool> HasLinkedParentAsync(Guid deviceId);

    Task<bool> IsDevicePausedAsync(Guid deviceId);
    Task<bool> IsDeviceInBedtimeWindowAsync(Guid deviceId, DateTime nowUtc);
    Task<bool> IsDeviceModeEnabledAsync(Guid deviceId, DetectedMode mode);
    Task<bool> IsModeEnabledForRequestAsync(Guid deviceId, Guid? childId, DetectedMode mode);
}

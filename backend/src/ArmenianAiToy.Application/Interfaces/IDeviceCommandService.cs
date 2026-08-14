using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Domain.Entities;

namespace ArmenianAiToy.Application.Interfaces;

/// <summary>Outcome of an ack — the endpoint maps NotFound to 404 (also used
/// for a wrong-device ack, so command existence never leaks across devices).</summary>
public enum DeviceCommandAckOutcome
{
    Acked,
    NotFound,
}

/// <summary>
/// The device command queue: enqueue (server/admin side), poll + ack (device
/// side). Ownership is enforced by <paramref name="deviceId"/> on every call —
/// a device only ever sees or acks its own commands.
/// </summary>
public interface IDeviceCommandService
{
    /// <summary>Enqueue a command for a device. Returns null when
    /// <paramref name="type"/> is not a known <see cref="Helpers.DeviceCommandTypes"/>
    /// value (rejected, nothing queued).</summary>
    Task<DeviceCommand?> EnqueueAsync(
        Guid deviceId, string type, string? payloadJson, DateTime? expiresAt, DateTime nowUtc);

    /// <summary>Return the device's deliverable commands (Pending/Sent, not
    /// expired), marking Pending → Sent and lazily expiring overdue ones. Never
    /// returns another device's commands or an expired command.</summary>
    Task<IReadOnlyList<DeviceCommandDto>> PollAsync(Guid deviceId, DateTime nowUtc);

    /// <summary>
    /// True when this device has at least one DELIVERABLE command (Pending or
    /// Sent, not expired). Read-only: unlike <see cref="PollAsync"/> it marks
    /// nothing Sent and expires nothing, so the heartbeat can carry the answer
    /// without consuming a delivery or mutating a row.
    /// <para>
    /// This exists so the toy stops polling <c>GET /api/devices/commands</c>
    /// on its own cadence: the heartbeat it already sends every ~60 s carries
    /// the flag, and a second TLS handshake is only paid on the rare tick that
    /// actually has something to fetch.
    /// </para>
    /// </summary>
    Task<bool> HasDeliverableCommandAsync(Guid deviceId, DateTime nowUtc);

    /// <summary>Record the device's outcome for a command. Idempotent (a second
    /// ack is a safe no-op) and ownership-checked (a command owned by another
    /// device returns <see cref="DeviceCommandAckOutcome.NotFound"/>).</summary>
    Task<DeviceCommandAckOutcome> AckAsync(
        Guid deviceId, Guid commandId, DeviceCommandAckRequest ack, DateTime nowUtc);
}

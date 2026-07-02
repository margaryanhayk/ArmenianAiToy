using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// A single queued instruction for one device, polled by the device over its
/// authenticated outbound connection (the toy never accepts inbound
/// connections). Delivery is at-least-once: a <see cref="DeviceCommandStatus.Sent"/>
/// command is re-delivered until it is acked or expires, so the device must
/// treat the <see cref="Id"/> as an idempotency key. This is the transport for
/// the OTA foundation (type <c>firmware_update</c>) and, later, Feature-1
/// content-sync commands.
/// </summary>
public class DeviceCommand
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>Wire type (e.g. <c>firmware_update</c>). Validated against the
    /// known set at enqueue — an unknown type is never queued.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Opaque JSON arguments for the command (e.g. target version),
    /// or null when the type needs none. Stored verbatim; never executed as
    /// code server-side.</summary>
    public string? PayloadJson { get; set; }

    public DeviceCommandStatus Status { get; set; } = DeviceCommandStatus.Pending;

    public DateTime CreatedAt { get; set; }

    /// <summary>Optional expiry. A command past this instant is never delivered
    /// and is lazily marked <see cref="DeviceCommandStatus.Expired"/> on the
    /// next poll — a stale OTA/command can't act on a device that was offline.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? AckedAt { get; set; }

    /// <summary>Device-reported outcome captured at ack (<c>ok</c>/<c>failed</c>).</summary>
    public string? Result { get; set; }

    /// <summary>Device-reported error detail captured at ack (null on success).</summary>
    public string? Error { get; set; }

    /// <summary>Firmware version the device reported when acking — an audit
    /// trail of which version actually executed the command.</summary>
    public string? AckFirmwareVersion { get; set; }

    /// <summary>Opaque device diagnostics JSON captured at ack (free-form,
    /// stored verbatim).</summary>
    public string? AckDiagnosticsJson { get; set; }
}

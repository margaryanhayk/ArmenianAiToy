namespace ArmenianAiToy.Domain.Enums;

/// <summary>
/// Lifecycle of a queued <see cref="Entities.DeviceCommand"/>.
/// Stored as a string in the DB (see AppDbContext) for readability + safe
/// enum evolution.
/// </summary>
public enum DeviceCommandStatus
{
    /// <summary>Created, not yet delivered to the device.</summary>
    Pending,

    /// <summary>Delivered to the device on a poll, awaiting an ack.
    /// Re-delivered on subsequent polls until acked or expired
    /// (at-least-once; the device dedups by command id).</summary>
    Sent,

    /// <summary>Device acknowledged success.</summary>
    Acked,

    /// <summary>Device acknowledged failure (see Error/Result).</summary>
    Failed,

    /// <summary>Passed its ExpiresAt before being acked; never (re)delivered.</summary>
    Expired,
}

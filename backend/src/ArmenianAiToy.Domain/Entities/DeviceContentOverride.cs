namespace ArmenianAiToy.Domain.Entities;

/// <summary>
/// One per-toy exception to the fleet content catalogue: this device does
/// NOT get this item, or this device DOES get an item the fleet does not.
///
/// <para>
/// <b>Default-plus-exceptions, one table.</b> The catalogue itself stays
/// where it is (<c>ContentSync</c> config today; a <c>ContentItem</c> table
/// in the upload slice). A device with no rows here gets exactly the fleet
/// catalogue, byte-for-byte — which is why adding this table changed no
/// toy's manifest on the day it shipped. Deliberately NOT a groups table
/// and NOT a tier table: three tables and a precedence-bug surface is a bad
/// trade for a single-digit fleet, and a tier lands later additively (a
/// <c>Tier</c> on Device, a <c>TierKey</c> on catalogue rows) without
/// changing the resolver's shape.
/// </para>
///
/// <para>
/// <b>One row per (device, kind, key)</b>, enforced by a unique index, so a
/// device can never carry a contradictory allow AND deny for the same item.
/// The operator endpoint upserts. That is what lets the resolution rule stay
/// one sentence — a device override beats the catalogue default — with no
/// precedence table to get wrong.
/// </para>
///
/// <para>
/// <b><see cref="ItemKey"/> is the same key the manifest already uses to
/// address an item</b>, so entitlement can never disagree with delivery
/// about what "this item" means: the story id, the track id, the voice clip
/// id, and for a game the <c>gameKey/clipId</c> PAIR (four of the five games
/// each ship a clip called <c>intro</c>, so the pair is the identity — see
/// <c>ContentSyncGameOptions</c>). Built in exactly one place,
/// <c>DeviceContentEntitlement.GameItemKey</c>.
/// </para>
///
/// <para>
/// <see cref="DeviceId"/> is a real FK with cascade-on-delete — an
/// entitlement must not outlive the toy it describes. It deliberately
/// SURVIVES an unlink: unlinking factory-resets the family's settings, but
/// which content a toy is entitled to is an operator decision about the
/// hardware, not a household preference, and silently re-granting a
/// withheld item when a family changes would defeat the point.
/// </para>
///
/// <para>
/// <see cref="Mode"/> is a bounded string rather than an enum for the same
/// reason the content-sync vocabularies are: the value space is two words,
/// it is validated at the endpoint, and a Domain enum would add a
/// string-conversion to the model for nothing.
/// </para>
/// </summary>
public class DeviceContentOverride
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>Which namespace the item lives in:
    /// <c>story</c> | <c>game</c> | <c>voice</c> | <c>music</c>. Bounded by
    /// <c>DeviceContentEntitlement.IsKnownKind</c> at the write endpoint.</summary>
    public string ItemKind { get; set; } = string.Empty;

    /// <summary>The item's address within its namespace — see the type
    /// remarks. Never a filesystem path: it is a lookup key, exactly as it is
    /// on the content-file endpoint.</summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary><c>allow</c> | <c>deny</c>.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Why the operator did this. Required at the endpoint — the
    /// same discipline every other operator mutation follows.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The resolved console operator name, so a row is traceable to
    /// a person without joining the audit feed.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>When the row last took its current <see cref="Mode"/> — an
    /// upsert that flips the mode re-stamps this, because "when was this toy
    /// cut off" is the question a row is read to answer.</summary>
    public DateTime CreatedAt { get; set; }
}

namespace ArmenianAiToy.Domain.Entities;

public class Device
{
    public Guid Id { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Legacy plaintext device API key. Kept nullable for compatibility with
    /// rows registered before the hash-at-rest slice landed. New
    /// registrations MUST leave this null and store the hash in
    /// <see cref="ApiKeyHash"/>. The auth path (<c>DeviceService.ValidateDeviceAsync</c>)
    /// reads this only when <see cref="ApiKeyHash"/> is null and lazy-upgrades
    /// the row on the first successful authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Hashed device API key. Format documented in
    /// <c>DeviceApiKeyHasher</c> (v1:pbkdf2-sha256:&lt;iter&gt;:&lt;salt&gt;:&lt;hash&gt;).
    /// Primary credential storage for all rows registered after the
    /// hash-at-rest slice. Never returned to clients; never logged.
    /// </summary>
    public string? ApiKeyHash { get; set; }

    public string? FirmwareVersion { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// Consumer pairing (Phase A.2): hash of the single-use CLAIM CODE printed
    /// on the toy/box (QR). A parent claims the toy to their account by
    /// presenting the code (POST /api/parents/devices/claim); on success the
    /// code is CONSUMED (this is set back to null) so it cannot be reused.
    /// Null = not claimable (already consumed, or a legacy/bench device that
    /// was never minted with a claim code). Distinct from the device API key
    /// (<see cref="ApiKeyHash"/>): the claim code only grants OWNERSHIP and is
    /// never the device's backend credential. Set at manufacture by the mint
    /// flow (#043). Hashed with the same hasher as the API key; never logged.
    /// </summary>
    public string? ClaimCodeHash { get; set; }

    /// <summary>When this device was first successfully claimed by a parent
    /// (null = never claimed). Set alongside consuming <see cref="ClaimCodeHash"/>.</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// Parent-controlled pause flag. When true, the chat pipeline
    /// short-circuits at the HTTP boundary in ChatController — the device
    /// gets a canned "toy is paused" reply without any OpenAI call, any
    /// conversation write, or any state mutation. Toggled via the parent
    /// dashboard pause/resume endpoints.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// #074 server-side credential revocation kill-switch. When true,
    /// <c>DeviceService.ValidateDeviceAsync</c> rejects the device BEFORE the
    /// key compare, so EVERY device-auth path (chat, audio, story-qa,
    /// heartbeat, story-audio-token) returns the uniform 401 — a leaked or
    /// compromised device key can be killed centrally without physically
    /// re-flashing. Default false (no existing device changes behavior). The
    /// real cure for a compromised key is re-provisioning with a fresh key
    /// (registration #009/#011); revocation is reversible (parent can restore)
    /// so an accidental revoke is recoverable. Distinct from
    /// <see cref="IsPaused"/>: pause is a soft "quiet the toy" that still
    /// authenticates; revoke disables authentication entirely. Toggled via the
    /// parent endpoint PUT /api/parents/devices/{deviceId}/revoke.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// B4 bedtime window — parent-configured daily quiet hours. When the
    /// current local time on the device falls inside [BedtimeStart, BedtimeEnd)
    /// (half-open, midnight-crossing supported) the chat pipeline short-
    /// circuits at the HTTP boundary just like <see cref="IsPaused"/>.
    /// Disabled when either end is null. Pause wins over the window.
    /// </summary>
    public TimeOnly? BedtimeStart { get; set; }
    public TimeOnly? BedtimeEnd { get; set; }

    /// <summary>
    /// IANA time zone id used to evaluate the bedtime window. Default is
    /// "Asia/Yerevan" (Armenia-first product). If the id cannot be resolved
    /// by <c>TimeZoneInfo.FindSystemTimeZoneById</c> at evaluation time, the
    /// bedtime check falls back to UTC and emits a warning; the window is
    /// still evaluated, not silently disabled.
    /// </summary>
    public string TimeZone { get; set; } = "Asia/Yerevan";

    /// <summary>
    /// B5 per-mode availability flags. When false, the chat pipeline short-
    /// circuits at the HTTP boundary with a warm canned reply (same shape as
    /// <see cref="IsPaused"/>) rather than calling ChatService. Calm has no
    /// flag here by design — bedtime cues must always route to Calm for
    /// safety (see .claude/MODES.md).
    /// </summary>
    public bool StoryEnabled { get; set; } = true;
    public bool GameEnabled { get; set; } = true;
    public bool RiddleEnabled { get; set; } = true;
    public bool CuriosityEnabled { get; set; } = true;

    /// <summary>
    /// Timestamp of the most recent dormant-device warning email
    /// dispatched to this device's verified linked parents by the
    /// scheduled <c>WarnDormantDevicesAsync</c> pass. Null for
    /// devices that have never been warned (the shipped default for
    /// every row). Stamped on a per-device basis when at least one
    /// of the device's verified linked parents successfully received
    /// the email this tick — partial-failure across multi-parent
    /// fan-out still counts as "warned" if any recipient was reached.
    /// A tick where every per-recipient notifier call returns
    /// <c>false</c> leaves this field untouched so the next tick
    /// retries. See CLAUDE.md § Retention for the warn-only
    /// eligibility rules and refire interval semantics.
    /// </summary>
    public DateTime? DormancyWarnedAt { get; set; }

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<ParentDevice> ParentDevices { get; set; } = new List<ParentDevice>();
}

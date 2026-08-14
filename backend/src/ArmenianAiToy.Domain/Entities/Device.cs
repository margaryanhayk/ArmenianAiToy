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

    /// <summary>
    /// OTA-foundation firmware reporting, stamped from the device heartbeat.
    /// All nullable — a legacy/bench device that never reports leaves these
    /// null. <see cref="BoardModel"/> gates board-specific firmware offers;
    /// <see cref="PartitionName"/> is reserved for the OTA slot the device is
    /// running from; <see cref="LastOtaStatus"/> is the device's own summary of
    /// its last update attempt.
    /// </summary>
    public string? FirmwareBuild { get; set; }
    public string? BoardModel { get; set; }
    public string? PartitionName { get; set; }
    public string? LastOtaStatus { get; set; }
    public DateTime? FirmwareReportedAt { get; set; }

    /// <summary>
    /// Toy self-diagnostics, stamped from the heartbeat. Null = never reported
    /// (legacy firmware), so "unknown" stays distinguishable from "known bad".
    ///
    /// <para>
    /// <b>Why this exists.</b> A toy whose SD card is not mounted plays no
    /// stories: the button looks dead and the toy is simply SILENT. That
    /// happened on the bench (a 5 V wire to the card reader came loose) and was
    /// invisible without a serial cable — the parent's only signal would have
    /// been "it stopped working". The toy knows within a second of boot, so it
    /// reports it and the parent surface can say so in plain language. Silence
    /// is the worst failure mode for a children's toy.
    /// </para>
    /// </summary>
    public bool? SdCardOk { get; set; }

    /// <summary>
    /// What content the toy actually HAS, stamped from the heartbeat. Null =
    /// never reported (firmware older than the content-report slice), which
    /// stays distinguishable from "reported, and empty".
    ///
    /// <para>
    /// <b>Why this exists.</b> The backend knew what it ADVERTISED in the
    /// content manifest and nothing about what any toy had downloaded, so the
    /// only honest answer to "is my toy up to date?" was silence — printing a
    /// server-side count labelled as the toy's would have been a false
    /// statement about a child's device. These fields are the toy's own
    /// answer, read from the <c>/content_index.json</c> it writes after each
    /// sync.
    /// </para>
    ///
    /// <para>
    /// <see cref="ContentStories"/> is a compact <c>id:version</c> list
    /// (<c>"ulik:12,anban-huri:9"</c>) of VERIFIED stories — the toy only
    /// records an entry after the sha256 matched, so a half-downloaded story
    /// never appears here. Stored as the reported string rather than a child
    /// table: it is a SNAPSHOT that each heartbeat replaces, not history.
    /// The clip namespaces are counts only, because no surface needs to name
    /// an individual clip.
    /// </para>
    /// </summary>
    public int? ContentIndexSchema { get; set; }
    public string? ContentStories { get; set; }
    public int? ContentGameClips { get; set; }
    public int? ContentVoiceClips { get; set; }
    public int? ContentMusicTracks { get; set; }

    /// <summary>When the backend last received a content report. Distinct from
    /// <see cref="FirmwareReportedAt"/>: the toy sends the content block only
    /// when it changes, so this can be hours old on a perfectly healthy
    /// toy.</summary>
    public DateTime? ContentReportedAt { get; set; }

    /// <summary>
    /// What the toy's last sync ATTEMPT did, as opposed to what it now HAS.
    /// Null = never reported (firmware older than the sync-diagnostics slice).
    ///
    /// <para>
    /// <b>Why the content report above was not enough.</b> A sync that fails
    /// leaves the card exactly as it was, and the firmware's carry-forward
    /// then re-advertises the OLD entry as verified — so a failed sync
    /// reports a healthy library. The toy has to say what it TRIED, or the
    /// only remaining diagnosis is a serial cable, which is not something a
    /// toy in a stranger's home can offer.
    /// </para>
    ///
    /// <para>
    /// <see cref="ContentSyncStatus"/> is the bounded verdict
    /// (<c>ok</c>/<c>partial</c>/<c>failed</c>/<c>never</c> — see
    /// <c>DeviceContentHealth</c>) and <see cref="ContentSyncError"/> a short
    /// reason such as <c>sha256_mismatch</c> or <c>no_space</c>. Both are
    /// stored as the device sent them (trimmed and length-capped): the
    /// derived verdict reacts only to the bounded set, while an operator
    /// diagnosing a wrong-looking verdict needs to read what the toy
    /// actually said.
    /// </para>
    ///
    /// <para>
    /// <see cref="ResetReason"/> and <see cref="BootCount"/> answer the
    /// question a status field structurally CANNOT: a device that dies
    /// mid-sync never reports a status, so the next boot's reset reason is
    /// the only surviving evidence. Firmware 1.2.1 crash-looped all night on
    /// 2026-08-14 — panicking on the manifest parse, rebooting every ~184 s —
    /// and because it heartbeat normally for the first 180 s of every cycle,
    /// <see cref="LastSeenAt"/> stayed fresh and every surface showed it
    /// online. <see cref="BootCount"/> (boots since the last clean sync) is
    /// what makes a loop visible: one panic is an incident, a number is a
    /// loop.
    /// </para>
    /// </summary>
    public string? ContentSyncStatus { get; set; }
    public string? ContentSyncError { get; set; }
    public string? ResetReason { get; set; }
    public int? BootCount { get; set; }

    /// <summary>When the backend last received a sync diagnostic. Distinct
    /// from <see cref="ContentReportedAt"/>: a toy whose card is dead has no
    /// content to report but can still report the failure, and that is
    /// precisely the toy this timestamp exists for.</summary>
    public DateTime? ContentSyncReportedAt { get; set; }

    /// <summary>
    /// When the toy last completed a sync, server-stamped from the
    /// heartbeat's relative <c>ContentSyncedSecondsAgo</c>. The toy has no
    /// wall clock, so it reports an age off its boot timer and the server
    /// turns it into an absolute time — the same treatment
    /// <c>StoryPlay.PlayedAtUtc</c> gives <c>secondsAgo</c>. Null = never
    /// reported. Ages outside a sane window are ignored rather than turned
    /// into a nonsense timestamp.
    /// </summary>
    public DateTime? ContentSyncedAt { get; set; }

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
    /// Parent toggle for the spoken story intro («Հեքիաթ՝ …, հեղինակ՝ …»)
    /// the toy plays before a cached story's narration. ON by default
    /// (educational-by-default, owner decision 2026-08-03). Delivered to the
    /// toy in the content-manifest response; the firmware caches the
    /// last-known value so the toggle applies offline too. Distinct from the
    /// B5 mode flags: this shapes the story experience, it never gates chat.
    /// </summary>
    public bool StoryIntroEnabled { get; set; } = true;

    /// <summary>
    /// Slice E — parent opt-IN for bedtime music: while the bedtime window
    /// is active and this is true, a button press plays a calm Armenian
    /// music track (synced to SD) instead of a story. Default FALSE — the
    /// suggestion is explicitly opt-in. Delivered to the toy via the
    /// content-manifest response; the "bedtime now" signal itself rides the
    /// heartbeat response (the toy has no wall clock).
    /// </summary>
    public bool BedtimeMusicEnabled { get; set; }

    /// <summary>
    /// Parent toggle for the short pauses inside a story — the moments the
    /// narration stops and gives the child a beat to think or answer. ON by
    /// default (the pauses are part of the authored story experience, and a
    /// child who has never heard them cannot ask for them). Delivered to the
    /// toy in the content-manifest response and cached in its SD index, so
    /// the toggle applies offline exactly like <see cref="StoryIntroEnabled"/>.
    /// Shapes the story experience; never gates chat.
    /// </summary>
    public bool StoryPausesEnabled { get; set; } = true;

    /// <summary>
    /// Parent toggle for variant endings — on a RE-listen, the toy may play
    /// an alternate ending for a story the child has already heard, so a
    /// favourite story does not become word-for-word predictable. ON by
    /// default; a device whose library ships no alternate files behaves
    /// identically either way, because the toy falls back to the base
    /// narration whenever no verified alt file is cached. Same manifest +
    /// SD-index delivery as <see cref="StoryPausesEnabled"/>.
    /// </summary>
    public bool VariantEndingsEnabled { get; set; } = true;

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

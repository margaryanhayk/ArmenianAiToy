namespace ArmenianAiToy.Domain.Entities;

public class Device
{
    public Guid Id { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? FirmwareVersion { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// Parent-controlled pause flag. When true, the chat pipeline
    /// short-circuits at the HTTP boundary in ChatController — the device
    /// gets a canned "toy is paused" reply without any OpenAI call, any
    /// conversation write, or any state mutation. Toggled via the parent
    /// dashboard pause/resume endpoints.
    /// </summary>
    public bool IsPaused { get; set; }

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

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<ParentDevice> ParentDevices { get; set; } = new List<ParentDevice>();
}

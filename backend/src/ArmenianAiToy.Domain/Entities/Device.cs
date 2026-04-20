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

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<ParentDevice> ParentDevices { get; set; } = new List<ParentDevice>();
}

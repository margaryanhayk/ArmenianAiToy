using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Domain.Entities;

public class Child
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>
    /// Per-child mode overrides on top of the device's B5 defaults.
    /// Three-valued:
    ///   null  => inherit the device's flag
    ///   true  => force enabled for this child, even if the device has it off
    ///   false => force disabled for this child, even if the device has it on
    /// Child override wins over device flag in both directions when present.
    /// <para>Calm has no override by design — bedtime cues must always
    /// reach Calm handling regardless of device or child config
    /// (MODES.md safety invariant, same as B5).</para>
    /// </summary>
    public bool? StoryEnabled { get; set; }
    public bool? GameEnabled { get; set; }
    public bool? RiddleEnabled { get; set; }
    public bool? CuriosityEnabled { get; set; }

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    public int? GetAge()
    {
        if (DateOfBirth == null) return null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - DateOfBirth.Value.Year;
        if (DateOfBirth.Value > today.AddYears(-age)) age--;
        return age;
    }
}

using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Domain.Entities;

public class Child
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// #066 — data minimization. We store only the child's BIRTH YEAR, never the
    /// exact date of birth: age (±1 yr) is all the product needs (Armenian
    /// grammar + age-appropriateness), and a minor's precise DOB is a high-value
    /// identity field we have no reason to retain. Null when unknown.
    /// </summary>
    public int? BirthYear { get; set; }
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
        if (BirthYear == null) return null;
        // Year-only -> age is approximate to ±1 yr (we don't know the month).
        // Fine for grammar / age-appropriateness; clamp non-negative.
        var age = DateTime.UtcNow.Year - BirthYear.Value;
        return age < 0 ? 0 : age;
    }
}

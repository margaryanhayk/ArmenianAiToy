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

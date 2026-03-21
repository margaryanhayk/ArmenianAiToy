namespace ArmenianAiToy.Domain.Entities;

public class Child
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Age { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}

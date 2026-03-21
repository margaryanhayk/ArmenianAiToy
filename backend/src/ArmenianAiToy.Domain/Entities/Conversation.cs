namespace ArmenianAiToy.Domain.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public Guid? ChildId { get; set; }
    public Child? Child { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

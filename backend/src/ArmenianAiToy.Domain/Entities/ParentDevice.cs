namespace ArmenianAiToy.Domain.Entities;

public class ParentDevice
{
    public Guid ParentId { get; set; }
    public Parent Parent { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public DateTime LinkedAt { get; set; }
}

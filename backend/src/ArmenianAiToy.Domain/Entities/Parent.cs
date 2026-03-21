namespace ArmenianAiToy.Domain.Entities;

public class Parent
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }

    public ICollection<ParentDevice> ParentDevices { get; set; } = new List<ParentDevice>();
}

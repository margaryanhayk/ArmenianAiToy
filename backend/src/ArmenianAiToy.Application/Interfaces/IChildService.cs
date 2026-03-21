using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.Interfaces;

public interface IChildService
{
    Task<Child> CreateChildAsync(Guid deviceId, string name, Gender gender, DateOnly? dateOfBirth);
    Task<Child?> GetChildAsync(Guid childId);
    Task<Child?> GetDefaultChildForDeviceAsync(Guid deviceId);
    Task<List<Child>> GetChildrenByDeviceAsync(Guid deviceId);
    string BuildChildContext(Child child);
}

using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;

namespace ArmenianAiToy.Application.Interfaces;

public interface IChildService
{
    Task<Child> CreateChildAsync(Guid deviceId, string name, Gender gender, int? birthYear);
    Task<Child?> GetChildAsync(Guid childId);

    /// <summary>
    /// Returns the child only if it belongs to <paramref name="deviceId"/>.
    /// Use this for any child id that arrives from a client request.
    /// </summary>
    Task<Child?> GetChildForDeviceAsync(Guid childId, Guid deviceId);
    Task<Child?> GetDefaultChildForDeviceAsync(Guid deviceId);
    Task<List<Child>> GetChildrenByDeviceAsync(Guid deviceId);
    string BuildChildContext(Child child);
}

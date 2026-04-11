using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

public interface IParentService
{
    Task<Guid> RegisterAsync(string email, string password);
    Task<ParentLoginResponse?> LoginAsync(string email, string password);
    Task<bool> LinkDeviceAsync(Guid parentId, Guid deviceId, string apiKey);
    Task<List<Guid>> GetLinkedDeviceIdsAsync(Guid parentId);
    Task<List<LinkedDeviceDto>> GetLinkedDeviceDetailsAsync(Guid parentId);
}

using ArmenianAiToy.Application.DTOs;

namespace ArmenianAiToy.Application.Interfaces;

public interface IParentService
{
    Task<Guid> RegisterAsync(string email, string password, bool acceptedTerms);
    Task<ParentLoginResponse?> LoginAsync(string email, string password);
    Task<bool> LinkDeviceAsync(Guid parentId, Guid deviceId, string apiKey);
    Task<bool> UnlinkDeviceAsync(Guid parentId, Guid deviceId);
    Task<List<Guid>> GetLinkedDeviceIdsAsync(Guid parentId);
    Task<List<LinkedDeviceDto>> GetLinkedDeviceDetailsAsync(Guid parentId);
    Task<bool> ChangePasswordAsync(Guid parentId, string currentPassword, string newPassword);
    Task<bool> SetDevicePauseStateAsync(Guid parentId, Guid deviceId, bool paused);
    Task<bool> SetBedtimeWindowAsync(Guid parentId, Guid deviceId, TimeOnly? start, TimeOnly? end);
    Task<bool> SetDeviceModeFlagsAsync(
        Guid parentId, Guid deviceId,
        bool story, bool game, bool riddle, bool curiosity);
    Task<bool> DeleteChildAsync(Guid parentId, Guid childId);
    Task<bool> DeleteAccountAsync(Guid parentId, string currentPassword);
    Task<bool> SetChildModeOverridesAsync(
        Guid parentId, Guid childId,
        bool? story, bool? game, bool? riddle, bool? curiosity);
    Task<List<AuditEventDto>> GetAuditEventsForParentAsync(Guid parentId, int limit, int offset);
    Task<ParentExport?> BuildExportAsync(Guid parentId);
}

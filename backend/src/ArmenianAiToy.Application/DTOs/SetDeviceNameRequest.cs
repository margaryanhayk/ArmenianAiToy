namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Request body for PUT /api/parents/devices/{deviceId}/name. Lets a parent
/// label each toy (e.g. "Anna's toy", "Living room") so multiple toys are
/// distinguishable in the app. The name is trimmed; 1..<see cref="MaxLength"/>
/// chars (shared by the controller's request validation and the service guard).
/// </summary>
public record SetDeviceNameRequest(string Name)
{
    public const int MaxLength = 60;
}

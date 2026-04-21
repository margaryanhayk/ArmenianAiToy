namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// B4 request body for PUT /api/parents/devices/{deviceId}/bedtime-window.
/// Both null (or either half null) means "disable the window".
/// </summary>
public record BedtimeWindowRequest(TimeOnly? Start, TimeOnly? End);

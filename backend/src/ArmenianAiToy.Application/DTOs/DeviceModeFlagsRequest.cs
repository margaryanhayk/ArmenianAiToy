namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// B5 request body for PUT /api/parents/devices/{deviceId}/mode-flags.
/// Full-replacement shape — all four bools always supplied. Calm has no
/// flag by design; bedtime cues must always route to Calm for safety.
/// </summary>
public record DeviceModeFlagsRequest(
    bool Story,
    bool Game,
    bool Riddle,
    bool Curiosity);

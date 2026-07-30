namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// B5 request body for PUT /api/parents/devices/{deviceId}/mode-flags.
/// Full-replacement shape — all four bools always supplied. Calm has no
/// flag by design; bedtime cues must always route to Calm for safety.
///
/// The fields are <c>bool?</c> so a MISSING field is distinguishable from
/// an explicit <c>false</c>. The controller rejects any null with 400.
/// With plain <c>bool</c>, an empty or partial body silently bound every
/// unsent field to <c>false</c> — a truncated request or an older app
/// build would turn Story/Game/Riddle/Curiosity OFF on a child's toy.
/// </summary>
public record DeviceModeFlagsRequest(
    bool? Story,
    bool? Game,
    bool? Riddle,
    bool? Curiosity);

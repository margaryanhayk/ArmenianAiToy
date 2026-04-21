namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Request body for PUT /api/parents/children/{childId}/mode-flags
/// (per-child mode overrides on top of B5 device defaults). Each field is
/// three-valued: null = inherit device, true = force on, false = force off.
/// Calm has no override by design (MODES.md safety invariant).
/// </summary>
public record ChildModeOverridesRequest(
    bool? Story,
    bool? Game,
    bool? Riddle,
    bool? Curiosity);

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Body for <c>PUT /api/parents/devices/{deviceId}/story-intro</c> — the
/// parent toggle for the spoken story intro («Հեքիաթ՝ …, հեղինակ՝ …»).
/// Nullable so a missing/empty body is a 400 at the controller rather than a
/// silent "false" (fail-safe: a truncated request must not flip the flag).
/// </summary>
public record DeviceStoryIntroRequest(bool? Enabled);

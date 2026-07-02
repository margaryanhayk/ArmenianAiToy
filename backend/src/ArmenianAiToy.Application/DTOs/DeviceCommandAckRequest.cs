using System.Text.Json;

namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// Body for <c>POST /api/devices/commands/{id}/ack</c>. The device reports the
/// outcome of executing a command. <see cref="Result"/> is <c>ok</c> or
/// <c>failed</c>; <see cref="Diagnostics"/> is free-form JSON stored verbatim
/// for later inspection.
/// </summary>
public sealed record DeviceCommandAckRequest(
    string? Result = null,
    string? Error = null,
    string? FirmwareVersion = null,
    JsonElement? Diagnostics = null);

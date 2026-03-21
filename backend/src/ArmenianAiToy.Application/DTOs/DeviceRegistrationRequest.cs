namespace ArmenianAiToy.Application.DTOs;

public record DeviceRegistrationRequest(string MacAddress, string? FirmwareVersion = null);

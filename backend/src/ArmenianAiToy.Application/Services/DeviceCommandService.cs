using System.Text.Json;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Entities;
using ArmenianAiToy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ArmenianAiToy.Application.Services;

/// <inheritdoc />
public sealed class DeviceCommandService : IDeviceCommandService
{
    private readonly DbContext _db;

    public DeviceCommandService(DbContext db) => _db = db;

    public async Task<DeviceCommand?> EnqueueAsync(
        Guid deviceId, string type, string? payloadJson, DateTime? expiresAt, DateTime nowUtc)
    {
        // Unknown command type is rejected — never queued, never delivered.
        if (!DeviceCommandTypes.IsKnown(type))
        {
            return null;
        }

        var cmd = new DeviceCommand
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            Type = type,
            PayloadJson = payloadJson,
            Status = DeviceCommandStatus.Pending,
            CreatedAt = nowUtc,
            ExpiresAt = expiresAt,
        };
        _db.Set<DeviceCommand>().Add(cmd);
        await _db.SaveChangesAsync();
        return cmd;
    }

    public async Task<IReadOnlyList<DeviceCommandDto>> PollAsync(Guid deviceId, DateTime nowUtc)
    {
        // Only this device's non-terminal commands.
        var candidates = await _db.Set<DeviceCommand>()
            .Where(c => c.DeviceId == deviceId
                     && (c.Status == DeviceCommandStatus.Pending
                         || c.Status == DeviceCommandStatus.Sent))
            .OrderBy(c => c.CreatedAt)
            .Take(200) // defensive cap: bound memory if a device's queue grows large
            .ToListAsync();

        var deliver = new List<DeviceCommand>();
        foreach (var c in candidates)
        {
            if (c.ExpiresAt.HasValue && c.ExpiresAt.Value <= nowUtc)
            {
                c.Status = DeviceCommandStatus.Expired; // lazy-expire; not delivered
                continue;
            }
            if (c.Status == DeviceCommandStatus.Pending)
            {
                c.Status = DeviceCommandStatus.Sent;
                c.SentAt = nowUtc;
            }
            deliver.Add(c); // includes already-Sent (at-least-once re-delivery)
        }
        await _db.SaveChangesAsync();

        return deliver.Select(ToDto).ToList();
    }

    public async Task<DeviceCommandAckOutcome> AckAsync(
        Guid deviceId, Guid commandId, DeviceCommandAckRequest ack, DateTime nowUtc)
    {
        var cmd = await _db.Set<DeviceCommand>()
            .FirstOrDefaultAsync(c => c.Id == commandId);

        // Unknown id OR a command owned by another device → uniform NotFound
        // (a device can never confirm the existence of another device's command).
        if (cmd is null || cmd.DeviceId != deviceId)
        {
            return DeviceCommandAckOutcome.NotFound;
        }

        // Idempotent: a terminal command is never re-applied (a duplicate ack
        // is a safe no-op that still reports success to the device).
        if (cmd.Status is DeviceCommandStatus.Acked
            or DeviceCommandStatus.Failed
            or DeviceCommandStatus.Expired)
        {
            return DeviceCommandAckOutcome.Acked;
        }

        var ok = string.Equals(ack.Result, "ok", StringComparison.OrdinalIgnoreCase);
        cmd.Status = ok ? DeviceCommandStatus.Acked : DeviceCommandStatus.Failed;
        cmd.AckedAt = nowUtc;
        cmd.Result = ack.Result;
        cmd.Error = ack.Error;
        cmd.AckFirmwareVersion = ack.FirmwareVersion;
        cmd.AckDiagnosticsJson = ack.Diagnostics.HasValue
            ? ack.Diagnostics.Value.GetRawText()
            : null;
        await _db.SaveChangesAsync();
        return DeviceCommandAckOutcome.Acked;
    }

    private static DeviceCommandDto ToDto(DeviceCommand c)
    {
        JsonElement? payload = null;
        if (!string.IsNullOrWhiteSpace(c.PayloadJson))
        {
            try
            {
                payload = JsonDocument.Parse(c.PayloadJson).RootElement.Clone();
            }
            catch
            {
                payload = null; // malformed stored payload never breaks a poll
            }
        }
        return new DeviceCommandDto(c.Id, c.Type, payload, c.CreatedAt, c.ExpiresAt);
    }
}

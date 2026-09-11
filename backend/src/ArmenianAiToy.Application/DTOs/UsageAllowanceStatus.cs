namespace ArmenianAiToy.Application.DTOs;

/// <summary>
/// A device's resolved usage-tier standing at a point in time: what tier it
/// is on, how many questions it has used today/this month, what its plan
/// allows on each axis (null = uncapped on that axis), and whether it has
/// run out. Computed by
/// <see cref="ArmenianAiToy.Application.Services.DeviceService.GetUsageAllowanceStatusAsync"/>
/// from <c>Device.UsageTier</c> + <c>DeviceUsageDay</c> rows +
/// <see cref="ArmenianAiToy.Application.Helpers.UsageAllowance"/>. Consumed
/// both by the chat/audio/story-qa gate (when <c>Usage:Tiers:Enabled</c>)
/// and by the parent-facing <c>usage</c> field on <c>LinkedDeviceDto</c>.
/// </summary>
public sealed record UsageAllowanceStatus(
    string Tier,
    int QuestionsToday,
    int QuestionsThisMonth,
    int? AllowanceToday,
    int? AllowanceThisMonth,
    bool IsExhausted);

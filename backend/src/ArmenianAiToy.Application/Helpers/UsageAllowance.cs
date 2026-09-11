namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Pure resolution of "what is this tier allowed" and "has it run out" —
/// deliberately separated from <see cref="DeviceUsageDay"/>-reading
/// database code so the decision itself is unit-testable without a
/// database. Part of the usage-tier metering foundation (see
/// <see cref="UsageTiersOptions"/>); ships behind
/// <c>Usage:Tiers:Enabled</c>, default off.
/// </summary>
public static class UsageAllowance
{
    /// <summary>A resolved allowance for one tier. Either count being null
    /// means "no cap on that axis" — a plan can bound only the daily count,
    /// only the monthly count, both, or (in principle) neither.</summary>
    public readonly record struct Plan(int? QuestionsPerDay, int? QuestionsPerMonth);

    /// <summary>
    /// Resolve the effective allowance for <paramref name="tier"/> against
    /// the configured <paramref name="options"/>. Falls back to the plan
    /// named <see cref="UsageTiersOptions.FreeTierName"/> when the device's
    /// tier names a plan that is not (or no longer) configured, and to the
    /// first configured plan when even the free tier is missing — matching
    /// <see cref="UsageTiersOptions.Resolve"/>'s guarantee that
    /// <see cref="UsageTiersOptions.Plans"/> is never empty. A device
    /// should never silently lose its allowance because an operator
    /// renamed or removed a plan out from under it.
    /// </summary>
    public static Plan Resolve(UsageTiersOptions options, string? tier)
    {
        var plan =
            FindByName(options, tier) ??
            FindByName(options, UsageTiersOptions.FreeTierName) ??
            options.Plans.FirstOrDefault();

        return plan is null
            ? new Plan(null, null)
            : new Plan(plan.QuestionsPerDay, plan.QuestionsPerMonth);
    }

    /// <summary>
    /// True when either axis of <paramref name="plan"/> has been reached or
    /// exceeded by the counts so far. A null count on an axis never trips —
    /// that axis is uncapped for this plan.
    /// </summary>
    public static bool IsExhausted(Plan plan, int questionsToday, int questionsThisMonth)
    {
        if (plan.QuestionsPerDay is int perDay && questionsToday >= perDay) return true;
        if (plan.QuestionsPerMonth is int perMonth && questionsThisMonth >= perMonth) return true;
        return false;
    }

    private static UsageTierPlan? FindByName(UsageTiersOptions options, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return options.Plans.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}

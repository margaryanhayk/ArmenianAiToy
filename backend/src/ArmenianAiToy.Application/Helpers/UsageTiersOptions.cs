using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// One usage-tier plan: a name and its allowances. Both counts are nullable
/// — a plan with no configured limit for one axis is simply unbounded on
/// that axis (<c>UsageAllowance</c> treats null as "no cap"). No price
/// field exists here, deliberately: this slice is the metering machinery,
/// not a pricing decision (see <c>docs/usage-tiers-brainstorm.md</c>).
/// </summary>
public sealed class UsageTierPlan
{
    public string Name { get; set; } = string.Empty;
    public int? QuestionsPerDay { get; set; }
    public int? QuestionsPerMonth { get; set; }
}

/// <summary>
/// Strongly-typed options for the usage-tier metering foundation. Bound
/// from configuration section <c>Usage:Tiers</c>.
/// <para>
/// <b>Enabled defaults to <c>false</c>.</b> While off, every request path
/// is byte-identical to today: the existing flat
/// <see cref="OpenAIDailyCostCapOptions"/> dollar cap keeps governing, and
/// nothing reads <see cref="Plans"/> on the request path at all. Flipping
/// it on makes the per-tier allowance REPLACE the flat cap in the gate
/// (see <c>ChatController</c>/<c>AudioChatController</c>/
/// <c>StoryQaController</c>) and turns on the parent-facing
/// "N of M questions today" line. See
/// <c>docs/usage-tiers-brainstorm.md</c> — the owner has decided only that
/// content stays paid and controls stay free; tiers themselves are to be
/// revisited before production, so this ships off.
/// </para>
/// <para>
/// <b>Manual binding</b> (string indexer + <c>GetChildren()</c>), same
/// idiom as <see cref="ContentSyncOptions.Resolve"/>: Application does not
/// pull in <c>Microsoft.Extensions.Configuration.Binder</c>, and hand-
/// rolled binding is directly unit-testable rather than only reachable
/// through DI wiring.
/// </para>
/// </summary>
public sealed class UsageTiersOptions
{
    /// <summary>The tier every device starts on, and the fallback when a
    /// device's <c>UsageTier</c> names a plan that no longer exists in
    /// config. Not a price — a name.</summary>
    public const string FreeTierName = "free";

    public bool Enabled { get; set; }

    public List<UsageTierPlan> Plans { get; set; } = new();

    /// <summary>
    /// Resolve <see cref="UsageTiersOptions"/> from configuration.
    /// <c>Usage:Tiers:Enabled</c> defaults false when missing or
    /// unparseable. <c>Usage:Tiers:Plans</c> is an ordered array of
    /// <c>{ Name, QuestionsPerDay, QuestionsPerMonth }</c> objects; an
    /// entry with a blank name is skipped. An unconfigured or empty
    /// <c>Plans</c> section falls back to a single free plan carrying
    /// today's flat-cap intent (<see cref="OpenAIDailyCostCapOptions.IntendedQuestionsPerDay"/>
    /// questions/day, no monthly cap) — so a device on the free tier sees
    /// the same allowance today's shipped cap already implies, not a
    /// silently different number.
    /// </summary>
    public static UsageTiersOptions Resolve(IConfiguration config)
    {
        var section = config.GetSection("Usage:Tiers");
        var enabled = bool.TryParse(section["Enabled"], out var parsedEnabled) && parsedEnabled;

        var plans = new List<UsageTierPlan>();
        foreach (var child in section.GetSection("Plans").GetChildren())
        {
            var name = child["Name"];
            if (string.IsNullOrWhiteSpace(name)) continue;

            int? questionsPerDay = int.TryParse(child["QuestionsPerDay"], out var perDay)
                ? perDay
                : null;
            int? questionsPerMonth = int.TryParse(child["QuestionsPerMonth"], out var perMonth)
                ? perMonth
                : null;

            plans.Add(new UsageTierPlan
            {
                Name = name.Trim(),
                QuestionsPerDay = questionsPerDay,
                QuestionsPerMonth = questionsPerMonth,
            });
        }

        if (plans.Count == 0)
        {
            plans.Add(new UsageTierPlan
            {
                Name = FreeTierName,
                QuestionsPerDay = OpenAIDailyCostCapOptions.IntendedQuestionsPerDay,
                QuestionsPerMonth = null,
            });
        }

        return new UsageTiersOptions { Enabled = enabled, Plans = plans };
    }
}

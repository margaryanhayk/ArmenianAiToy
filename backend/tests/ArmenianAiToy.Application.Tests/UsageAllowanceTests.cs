using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the pure <see cref="UsageAllowance"/> resolution/exhaustion
/// logic — the usage-tier metering foundation's decision core (2026-09-11,
/// ships behind <c>Usage:Tiers:Enabled</c>=false). No database, no config
/// binding — just tier lookup and the exhaustion predicate.
/// </summary>
public class UsageAllowanceTests
{
    private static UsageTiersOptions Options(params UsageTierPlan[] plans) =>
        new() { Enabled = true, Plans = plans.ToList() };

    [Fact]
    public void Resolve_ExactTierMatch_ReturnsItsCounts()
    {
        var opts = Options(
            new UsageTierPlan { Name = "free", QuestionsPerDay = 30 },
            new UsageTierPlan { Name = "family", QuestionsPerDay = 100, QuestionsPerMonth = 2000 });

        var plan = UsageAllowance.Resolve(opts, "family");

        Assert.Equal(100, plan.QuestionsPerDay);
        Assert.Equal(2000, plan.QuestionsPerMonth);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var opts = Options(new UsageTierPlan { Name = "Free", QuestionsPerDay = 30 });

        var plan = UsageAllowance.Resolve(opts, "FREE");

        Assert.Equal(30, plan.QuestionsPerDay);
    }

    [Fact]
    public void Resolve_UnknownTier_FallsBackToFreeTierName()
    {
        var opts = Options(
            new UsageTierPlan { Name = UsageTiersOptions.FreeTierName, QuestionsPerDay = 30 },
            new UsageTierPlan { Name = "family", QuestionsPerDay = 100 });

        // A device's tier names a plan that was since renamed/removed.
        var plan = UsageAllowance.Resolve(opts, "retired-plan");

        Assert.Equal(30, plan.QuestionsPerDay);
    }

    [Fact]
    public void Resolve_NullOrBlankTier_FallsBackToFreeTierName()
    {
        var opts = Options(new UsageTierPlan { Name = UsageTiersOptions.FreeTierName, QuestionsPerDay = 30 });

        Assert.Equal(30, UsageAllowance.Resolve(opts, null).QuestionsPerDay);
        Assert.Equal(30, UsageAllowance.Resolve(opts, "  ").QuestionsPerDay);
    }

    [Fact]
    public void Resolve_EvenFreeTierMissing_FallsBackToFirstConfiguredPlan()
    {
        // Pathological config: no "free" plan at all. A device should never
        // resolve to a fully-unbounded allowance just because an operator
        // renamed the base plan everywhere.
        var opts = Options(new UsageTierPlan { Name = "family", QuestionsPerDay = 100 });

        var plan = UsageAllowance.Resolve(opts, "unknown");

        Assert.Equal(100, plan.QuestionsPerDay);
    }

    [Fact]
    public void Resolve_EmptyPlansList_ReturnsUnboundedPlan()
    {
        var opts = new UsageTiersOptions { Enabled = true, Plans = new() };

        var plan = UsageAllowance.Resolve(opts, "anything");

        Assert.Null(plan.QuestionsPerDay);
        Assert.Null(plan.QuestionsPerMonth);
    }

    [Theory]
    [InlineData(29, 0, false)]
    [InlineData(30, 0, true)]
    [InlineData(31, 0, true)]
    public void IsExhausted_DailyBoundary(int questionsToday, int questionsThisMonth, bool expected)
    {
        var plan = new UsageAllowance.Plan(QuestionsPerDay: 30, QuestionsPerMonth: null);
        Assert.Equal(expected, UsageAllowance.IsExhausted(plan, questionsToday, questionsThisMonth));
    }

    [Theory]
    [InlineData(0, 1999, false)]
    [InlineData(0, 2000, true)]
    [InlineData(0, 2001, true)]
    public void IsExhausted_MonthlyBoundary(int questionsToday, int questionsThisMonth, bool expected)
    {
        var plan = new UsageAllowance.Plan(QuestionsPerDay: null, QuestionsPerMonth: 2000);
        Assert.Equal(expected, UsageAllowance.IsExhausted(plan, questionsToday, questionsThisMonth));
    }

    [Fact]
    public void IsExhausted_BothCapsNull_NeverTrips()
    {
        var plan = new UsageAllowance.Plan(QuestionsPerDay: null, QuestionsPerMonth: null);
        Assert.False(UsageAllowance.IsExhausted(plan, questionsToday: 1_000_000, questionsThisMonth: 1_000_000));
    }

    [Fact]
    public void IsExhausted_EitherAxisTripping_IsEnough()
    {
        // Under the daily cap but over the monthly one still exhausts.
        var plan = new UsageAllowance.Plan(QuestionsPerDay: 30, QuestionsPerMonth: 100);
        Assert.True(UsageAllowance.IsExhausted(plan, questionsToday: 5, questionsThisMonth: 100));
    }
}

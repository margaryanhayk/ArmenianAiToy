using ArmenianAiToy.Application.Helpers;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="UsageTiersOptions.Resolve"/> — the usage-tier
/// metering foundation's config binding (2026-09-11, ships behind
/// <c>Usage:Tiers:Enabled</c>=false).
/// </summary>
public class UsageTiersOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Resolve_MissingSection_DefaultsDisabled_WithFreeFallbackPlan()
    {
        var opts = UsageTiersOptions.Resolve(new ConfigurationBuilder().Build());

        Assert.False(opts.Enabled);
        var free = Assert.Single(opts.Plans);
        Assert.Equal(UsageTiersOptions.FreeTierName, free.Name);
        Assert.Equal(OpenAIDailyCostCapOptions.IntendedQuestionsPerDay, free.QuestionsPerDay);
        Assert.Null(free.QuestionsPerMonth);
    }

    [Fact]
    public void Resolve_UnparseableEnabled_DefaultsFalse()
    {
        var opts = UsageTiersOptions.Resolve(Config(new()
        {
            ["Usage:Tiers:Enabled"] = "not-a-bool",
        }));

        Assert.False(opts.Enabled);
    }

    [Fact]
    public void Resolve_EnabledTrue_IsHonored()
    {
        var opts = UsageTiersOptions.Resolve(Config(new()
        {
            ["Usage:Tiers:Enabled"] = "true",
        }));

        Assert.True(opts.Enabled);
    }

    [Fact]
    public void Resolve_PlansArray_ParsesNameAndBothCounts()
    {
        var opts = UsageTiersOptions.Resolve(Config(new()
        {
            ["Usage:Tiers:Plans:0:Name"] = "free",
            ["Usage:Tiers:Plans:0:QuestionsPerDay"] = "30",
            ["Usage:Tiers:Plans:1:Name"] = "family",
            ["Usage:Tiers:Plans:1:QuestionsPerDay"] = "100",
            ["Usage:Tiers:Plans:1:QuestionsPerMonth"] = "2000",
        }));

        Assert.Equal(2, opts.Plans.Count);
        Assert.Equal("free", opts.Plans[0].Name);
        Assert.Equal(30, opts.Plans[0].QuestionsPerDay);
        Assert.Null(opts.Plans[0].QuestionsPerMonth);
        Assert.Equal("family", opts.Plans[1].Name);
        Assert.Equal(100, opts.Plans[1].QuestionsPerDay);
        Assert.Equal(2000, opts.Plans[1].QuestionsPerMonth);
    }

    [Fact]
    public void Resolve_PlanWithBlankName_IsSkipped()
    {
        var opts = UsageTiersOptions.Resolve(Config(new()
        {
            ["Usage:Tiers:Plans:0:Name"] = "   ",
            ["Usage:Tiers:Plans:0:QuestionsPerDay"] = "30",
            ["Usage:Tiers:Plans:1:Name"] = "family",
            ["Usage:Tiers:Plans:1:QuestionsPerDay"] = "100",
        }));

        var plan = Assert.Single(opts.Plans);
        Assert.Equal("family", plan.Name);
    }

    [Fact]
    public void Resolve_PlanWithUnparseableCounts_LeavesThemNull()
    {
        var opts = UsageTiersOptions.Resolve(Config(new()
        {
            ["Usage:Tiers:Plans:0:Name"] = "unbounded",
            ["Usage:Tiers:Plans:0:QuestionsPerDay"] = "not-a-number",
        }));

        var plan = Assert.Single(opts.Plans);
        Assert.Null(plan.QuestionsPerDay);
        Assert.Null(plan.QuestionsPerMonth);
    }
}

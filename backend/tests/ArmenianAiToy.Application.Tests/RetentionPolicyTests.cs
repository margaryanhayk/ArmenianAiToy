using ArmenianAiToy.Application.Helpers;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// #067 — pins the read-only projection of Retention:Messages:MaxAgeDays used
/// by the export disclosure and the startup alert. Semantics must match
/// RetentionPurgeService: missing/unparseable => shipped default (never 0);
/// non-positive => disabled.
/// </summary>
public class RetentionPolicyTests
{
    private static IConfiguration Config(string? maxAgeDays) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Retention:Messages:MaxAgeDays", maxAgeDays)
            })
            .Build();

    [Fact]
    public void Missing_ResolvesToShippedDefault_Enabled()
    {
        var (enabled, days) = RetentionPolicy.ResolveMessages(new ConfigurationBuilder().Build());
        Assert.True(enabled);
        Assert.Equal(RetentionPolicy.DefaultMessagesMaxAgeDays, days);
    }

    [Fact]
    public void Unparseable_ResolvesToShippedDefault()
    {
        var (enabled, days) = RetentionPolicy.ResolveMessages(Config("not-a-number"));
        Assert.True(enabled);
        Assert.Equal(RetentionPolicy.DefaultMessagesMaxAgeDays, days);
    }

    [Fact]
    public void PositiveOverride_Honored()
    {
        var (enabled, days) = RetentionPolicy.ResolveMessages(Config("30"));
        Assert.True(enabled);
        Assert.Equal(30, days);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositive_Disabled(string raw)
    {
        var (enabled, days) = RetentionPolicy.ResolveMessages(Config(raw));
        Assert.False(enabled);
        Assert.Equal(0, days);
    }

    [Fact]
    public void DescribeForParent_Enabled_MentionsDays()
    {
        Assert.Contains("30 days", RetentionPolicy.DescribeForParent(Config("30")));
    }

    [Fact]
    public void DescribeForParent_Disabled_SaysTurnedOff()
    {
        Assert.Contains("turned off", RetentionPolicy.DescribeForParent(Config("0")));
    }
}

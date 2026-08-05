using ArmenianAiToy.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the AI provider seam's resolution contract (mirrors
/// NotifierTransportTests): missing → openai, known value normalized,
/// unknown value refuses to boot with the key named.
/// </summary>
public class AiProviderConfigTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void MissingKey_ResolvesToOpenAI()
    {
        // Shipped default: a deployment that never heard of the seam
        // behaves exactly as before it existed.
        Assert.Equal(AiProviderConfig.OpenAI,
            AiProviderConfig.Resolve(Config(), AiProviderConfig.ChatKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_ResolvesToOpenAI(string value)
    {
        Assert.Equal(AiProviderConfig.OpenAI,
            AiProviderConfig.Resolve(
                Config((AiProviderConfig.TtsKey, value)), AiProviderConfig.TtsKey));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("OpenAI")]
    [InlineData("  OPENAI  ")]
    public void KnownValue_IsNormalized_CaseAndWhitespaceInsensitive(string value)
    {
        Assert.Equal(AiProviderConfig.OpenAI,
            AiProviderConfig.Resolve(
                Config((AiProviderConfig.ModerationKey, value)), AiProviderConfig.ModerationKey));
    }

    [Fact]
    public void UnknownProvider_Throws_NamingKeyAndValue()
    {
        // KEYSTONE: a typo'd provider name must refuse to boot, never
        // silently fall back — this is what makes a production flip to a
        // future provider safe to attempt.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AiProviderConfig.Resolve(
                Config((AiProviderConfig.ChatKey, "gemini")), AiProviderConfig.ChatKey));
        Assert.Contains(AiProviderConfig.ChatKey, ex.Message);
        Assert.Contains("gemini", ex.Message);
        Assert.Contains("openai", ex.Message);
    }

    [Fact]
    public void EveryCapabilityKey_ResolvesIndependently()
    {
        // Capabilities are deliberately separate switches — a mixed
        // config (future: Gemini chat under OpenAI moderation) must be
        // expressible. Today all four resolve to openai.
        var config = Config(
            (AiProviderConfig.ChatKey, "openai"),
            (AiProviderConfig.TranscriptionKey, "openai"),
            (AiProviderConfig.TtsKey, "openai"),
            (AiProviderConfig.ModerationKey, "openai"));
        foreach (var key in new[]
        {
            AiProviderConfig.ChatKey, AiProviderConfig.TranscriptionKey,
            AiProviderConfig.TtsKey, AiProviderConfig.ModerationKey,
        })
        {
            Assert.Equal(AiProviderConfig.OpenAI, AiProviderConfig.Resolve(config, key));
        }
    }
}

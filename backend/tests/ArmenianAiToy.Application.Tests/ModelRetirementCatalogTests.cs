using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the boot-time model-retirement warning: every STT model OpenAI
/// removes on 2027-02-26 is caught (bare name and dated snapshot), the
/// replacements are not, and "removed" flips exactly on the removal date.
/// </summary>
public class ModelRetirementCatalogTests
{
    private static readonly DateOnly Before = new(2026, 9, 23);
    private static readonly DateOnly RemovalDay = new(2027, 2, 26);

    private static IReadOnlyList<ModelRetirementCatalog.Warning> One(string? model, DateOnly today)
        => ModelRetirementCatalog.Evaluate(new (string, string?)[] { ("Key", model) }, today);

    [Theory]
    [InlineData("whisper-1")]
    [InlineData("gpt-4o-transcribe")]
    [InlineData("gpt-4o-mini-transcribe")]
    [InlineData("gpt-4o-transcribe-diarize")]
    [InlineData("GPT-4o-Mini-Transcribe")]
    [InlineData(" gpt-4o-mini-transcribe ")]
    [InlineData("gpt-4o-mini-transcribe-2025-12-15")]
    [InlineData("gpt-4o-mini-transcribe-2025-03-20")]
    public void RetiredModel_Warns(string model)
    {
        var w = Assert.Single(One(model, Before));
        Assert.Equal(RemovalDay, w.RemovalDate);
        Assert.Equal("gpt-transcribe", w.Replacement);
        Assert.False(w.AlreadyRemoved);
    }

    [Theory]
    [InlineData("gpt-transcribe")]
    [InlineData("gpt-live-transcribe")]
    [InlineData("gpt-4o-mini-tts")]
    [InlineData("gpt-4o-mini-transcribe-fast")]
    [InlineData("whisper-10")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OtherModel_DoesNotWarn(string? model)
        => Assert.Empty(One(model, Before));

    [Fact]
    public void Diarize_MatchesItsOwnRow_NotTheShorterPrefix()
        => Assert.Equal("gpt-4o-transcribe-diarize", Assert.Single(One("gpt-4o-transcribe-diarize", Before)).Model);

    [Fact]
    public void AlreadyRemoved_FlipsOnTheRemovalDate()
    {
        Assert.False(Assert.Single(One("whisper-1", RemovalDay.AddDays(-1))).AlreadyRemoved);
        Assert.True(Assert.Single(One("whisper-1", RemovalDay)).AlreadyRemoved);
    }

    [Fact]
    public void Evaluate_ReportsEachConfigKeySeparately()
    {
        var warnings = ModelRetirementCatalog.Evaluate(new (string, string?)[]
        {
            ("OpenAI:TranscriptionModel", "whisper-1"),
            ("StoryQa:TranscriptionModel", "gpt-4o-mini-transcribe"),
            ("Devices:VoiceIntentTranscriptionModel", "gpt-transcribe"),
        }, Before);

        Assert.Equal(new[] { "OpenAI:TranscriptionModel", "StoryQa:TranscriptionModel" },
            warnings.Select(w => w.ConfigKey));
    }

    /// <summary>
    /// The shipped config runs the Armenian bench winner on every STT path
    /// (tools/quality-evidence/stt-armenian-bench-20260924.md) — nothing a
    /// 2027-02-26 removal would silence. Voice-intent has no key of its own
    /// in appsettings and falls back to OpenAI:TranscriptionModel.
    /// </summary>
    [Fact]
    public void ShippedConfig_SttModels_AreGptTranscribe_AndNotRetired()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var defaultModel = root.GetProperty("OpenAI").GetProperty("TranscriptionModel").GetString();
        var storyQaModel = root.GetProperty("StoryQa").GetProperty("TranscriptionModel").GetString();

        Assert.Equal("gpt-transcribe", defaultModel);
        Assert.Equal("gpt-transcribe", storyQaModel);
        Assert.Empty(ModelRetirementCatalog.Evaluate(new (string, string?)[]
        {
            ("OpenAI:TranscriptionModel", defaultModel),
            ("StoryQa:TranscriptionModel", storyQaModel),
        }, Before));
    }
}

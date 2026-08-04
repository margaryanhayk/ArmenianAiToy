using System.Text.Json;
using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Guards the welcome-flow Armenian in
/// <c>backend/content/voice-clips/voice-clips.json</c>.
/// <para>
/// Nothing at runtime reads that file — it is the reviewable source the MP3s
/// are rendered from. But once rendered, changing a word costs a re-render, a
/// loudness pass and a fresh listen test, so the cheap invariants are worth
/// catching here rather than in someone's ears.
/// </para>
/// </summary>
public class VoiceClipTextTests
{
    private static JsonDocument LoadClips()
    {
        // Walk up from the test binary to the repo root. The file is content,
        // not a build artifact, so it is deliberately not copied to output.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(
                   dir.FullName, "backend", "content", "voice-clips", "voice-clips.json")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            dir!.FullName, "backend", "content", "voice-clips", "voice-clips.json")));
    }

    private static IReadOnlyList<(string Id, string Text)> Clips()
    {
        using var doc = LoadClips();
        return doc.RootElement.GetProperty("clips").EnumerateArray()
            .Select(c => (c.GetProperty("voiceId").GetString()!,
                          c.GetProperty("text").GetString()!))
            .ToList();
    }

    // KEYSTONE: the toy's spoken "I didn't hear you" and the backend's written
    // one must be the same sentence. A child who hears one phrasing from the
    // welcome flow and reads another in the dashboard transcript is being
    // talked to by two different characters.
    [Fact]
    public void SayAgain_IsByteIdenticalToTheCanonicalGarbledGuard()
    {
        var sayAgain = Clips().Single(c => c.Id == "say-again").Text;
        Assert.Equal(ArmenianVoiceReplyGuard.ClarificationResponse, sayAgain);
    }

    // A greeting must not ask anything: the "what shall we do?" clip plays
    // immediately after it, and two questions in a row is how you lose a
    // four-year-old. Caught in review on greet-19; pinned so it stays caught.
    [Fact]
    public void Greetings_AskNoQuestions()
    {
        foreach (var (id, text) in Clips().Where(c => c.Id.StartsWith("greet-")))
        {
            // ՞ is the Armenian question mark and sits INSIDE the word.
            Assert.False(text.Contains('՞') || text.Contains('?'),
                $"{id} asks a question, but the ask- clip plays right after it: {text}");
        }
    }

    // Every id becomes an SD filename on the toy, through the same allowlist
    // story ids use (lowercase a-z, 0-9, '-', '_'). A capital letter or a dot
    // here would sync as "no clip" and the toy would silently skip the line.
    [Fact]
    public void VoiceIds_AreValidDeviceIds()
    {
        foreach (var (id, _) in Clips())
        {
            Assert.Matches("^[a-z0-9_-]{1,48}$", id);
        }
    }

    [Fact]
    public void VoiceIds_AreUnique()
    {
        var ids = Clips().Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // The firmware's CS_MAX_VOICE is 48 slots, and every slot costs ~384 bytes
    // across three tables. A 49th clip would be silently truncated off the
    // manifest on the device — the toy would just never play it, with nothing
    // to show that a line went missing.
    [Fact]
    public void ClipCount_FitsTheFirmwareBound()
    {
        Assert.True(Clips().Count <= 48,
            $"{Clips().Count} clips exceeds CS_MAX_VOICE (48) — raise the bound "
            + "in content_sync_rules.h and re-measure free RAM on the bench first");
    }

    // The four fixed-role clips are looked up by EXACT id by the firmware
    // (CS_VOICE_ID_* / the ask- composition). A rename here is a silent
    // "that line does not exist yet" on the toy.
    [Theory]
    [InlineData("ask-sgrc")]
    [InlineData("ask-any")]
    [InlineData("say-again")]
    [InlineData("just-story")]
    public void FixedRoleClips_ArePresent(string id) =>
        Assert.Contains(Clips(), c => c.Id == id);

    [Fact]
    public void GreetingPool_HasEnoughVarietyToNotRepeat()
    {
        var greetings = Clips().Where(c => c.Id.StartsWith("greet-")).ToList();
        Assert.True(greetings.Count >= 12,
            "the rotation only guarantees 'not the same one twice running'; "
            + "a small pool still feels repetitive within a week");
        Assert.Equal(greetings.Count,
            greetings.Select(g => g.Text).Distinct(StringComparer.Ordinal).Count());
    }

    // The title is spliced into these, and every shipped title already ends in
    // the definite article — so «Խոսող ձուկը»-ը would stutter. The ending has
    // to hang on the classifier «հեքիաթ» instead.
    [Fact]
    public void StoryOfferTemplates_DoNotSpliceTheDefiniteArticleOntoTheTitle()
    {
        using var doc = LoadClips();
        var templates = doc.RootElement.GetProperty("_perStoryTemplates");
        foreach (var key in new[] { "offer", "reoffer" })
        {
            var text = templates.GetProperty(key).GetString()!;
            Assert.Contains("{Title}", text);
            Assert.DoesNotContain("»-ը", text);
            Assert.DoesNotContain("»-ն", text);
        }
    }
}

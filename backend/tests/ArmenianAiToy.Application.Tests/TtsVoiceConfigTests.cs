using ArmenianAiToy.Infrastructure.Audio;
using OpenAI.Audio;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// <c>OpenAI:TtsVoice</c> — the voice Areg SPEAKS in.
/// <para>
/// This is NOT the story narration voice. Stories are pre-rendered
/// ElevenLabs audio shipped in <c>story-audio/</c> and played from the toy's
/// SD card; this key only affects the lines Areg synthesizes live (answers to
/// a mid-story question, reflection reactions, canned gate replies).
/// </para>
/// <para>
/// The key exists so comparing Armenian voices is a config flip rather than a
/// code change and redeploy. These tests pin the two properties that make it
/// safe to flip: the unset default is what shipped before, and a typo degrades
/// to that default instead of taking the site down at startup.
/// </para>
/// </summary>
public class TtsVoiceConfigTests
{
    // KEYSTONE: an unset key must behave exactly as the old hardcoded literal,
    // or merely adding the setting would change every deployment's voice.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetVoice_FallsBackToNova(string? configured) =>
        Assert.Equal(
            GeneratedSpeechVoice.Nova,
            OpenAITtsSynthesisService.ResolveVoice(configured));

    [Theory]
    [InlineData("alloy")]
    [InlineData("echo")]
    [InlineData("fable")]
    [InlineData("onyx")]
    [InlineData("nova")]
    [InlineData("shimmer")]
    public void KnownVoices_Resolve(string configured) =>
        Assert.NotEqual(default, OpenAITtsSynthesisService.ResolveVoice(configured));

    [Theory]
    [InlineData("NOVA")]
    [InlineData(" Shimmer ")]
    public void VoiceName_IsCaseAndWhitespaceInsensitive(string configured) =>
        Assert.NotEqual(default, OpenAITtsSynthesisService.ResolveVoice(configured));

    // A typo in an optional voice setting must not be a startup failure. The
    // whole site going down over a misspelt voice is strictly worse than
    // speaking in the previous one.
    [Theory]
    [InlineData("hy-AM-HaykNeural")]   // an Azure voice pasted into the OpenAI key
    [InlineData("novaa")]
    public void UnknownVoice_DegradesToNova_DoesNotThrow(string configured) =>
        Assert.Equal(
            GeneratedSpeechVoice.Nova,
            OpenAITtsSynthesisService.ResolveVoice(configured));
}

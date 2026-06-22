using ArmenianAiToy.Api.Security;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for <see cref="StoryAudioToken"/> — the stateless signed
/// access token that gates the header-less <c>/api/story-audio</c>
/// stream. Pin: a freshly-issued token validates; tampering, expiry,
/// wrong story binding, wrong key, and malformed input all fail closed.
/// </summary>
public class StoryAudioTokenTests
{
    private const string Key = "test-signing-key-please-rotate";
    private const string StoryId = "anban-huri";

    private static readonly DateTimeOffset Now =
        new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_ThenValidate_Roundtrips()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key);
        Assert.True(StoryAudioToken.TryValidate(token, StoryId, Key, Now));
    }

    [Fact]
    public void Validate_ExpiredToken_Fails()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddSeconds(-1), Key);
        Assert.False(StoryAudioToken.TryValidate(token, StoryId, Key, Now));
    }

    [Fact]
    public void Validate_WrongStory_Fails()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key);
        Assert.False(StoryAudioToken.TryValidate(token, "little-cloud", Key, Now));
    }

    [Fact]
    public void Validate_WrongKey_Fails()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key);
        Assert.False(StoryAudioToken.TryValidate(token, StoryId, "a-different-key", Now));
    }

    [Fact]
    public void Validate_TamperedSignature_Fails()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key);
        // Flip the FIRST character of the MAC segment — it encodes the top
        // bits of the first MAC byte, so the change is significant (the last
        // base64url char's low bits are truncated padding and can decode to
        // the same bytes).
        var sigStart = token.IndexOf('.') + 1;
        var flipped = token[sigStart] == 'A' ? 'B' : 'A';
        var tampered = token[..sigStart] + flipped + token[(sigStart + 1)..];
        Assert.False(StoryAudioToken.TryValidate(tampered, StoryId, Key, Now));
    }

    [Fact]
    public void Validate_TamperedPayloadExtendsExpiry_Fails()
    {
        // Re-sign nothing — just swap the payload for a far-future expiry
        // while keeping the original signature. Must fail: the MAC no
        // longer matches the payload.
        var token = StoryAudioToken.Issue(StoryId, Now.AddSeconds(-100), Key);
        var sig = token[(token.IndexOf('.') + 1)..];
        var forgedPayload = StoryAudioToken.Issue(StoryId, Now.AddYears(10), Key);
        var forgedPayloadPart = forgedPayload[..forgedPayload.IndexOf('.')];
        var spliced = $"{forgedPayloadPart}.{sig}";
        Assert.False(StoryAudioToken.TryValidate(spliced, StoryId, Key, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dot-here")]
    [InlineData(".onlysig")]
    [InlineData("onlypayload.")]
    [InlineData("not!base64.also!bad")]
    public void Validate_MalformedToken_Fails(string? token)
    {
        Assert.False(StoryAudioToken.TryValidate(token, StoryId, Key, Now));
    }

    [Fact]
    public void Validate_EmptyKey_Fails()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key);
        Assert.False(StoryAudioToken.TryValidate(token, StoryId, "", Now));
    }

    // ---------- #038 IP binding ----------

    [Fact]
    public void Unbound_IgnoresRequestIp()
    {
        // No boundIp at issue -> the stream's request IP is irrelevant.
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key);
        Assert.True(StoryAudioToken.TryValidate(token, StoryId, Key, Now, requestIp: "203.0.113.9"));
        Assert.True(StoryAudioToken.TryValidate(token, StoryId, Key, Now, requestIp: null));
    }

    [Fact]
    public void IpBound_SameIp_Validates()
    {
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key, boundIp: "198.51.100.7");
        Assert.True(StoryAudioToken.TryValidate(token, StoryId, Key, Now, requestIp: "198.51.100.7"));
    }

    [Fact]
    public void IpBound_DifferentIp_Fails()
    {
        // The core #038 win: a leaked token cannot be replayed from another IP.
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key, boundIp: "198.51.100.7");
        Assert.False(StoryAudioToken.TryValidate(token, StoryId, Key, Now, requestIp: "203.0.113.1"));
    }

    [Fact]
    public void IpBound_MissingRequestIp_Fails()
    {
        // Can't prove same-origin without a request IP -> deny.
        var token = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key, boundIp: "198.51.100.7");
        Assert.False(StoryAudioToken.TryValidate(token, StoryId, Key, Now, requestIp: null));
    }

    [Fact]
    public void IpBound_StillEnforcesStoryAndExpiry()
    {
        var expired = StoryAudioToken.Issue(StoryId, Now.AddSeconds(-1), Key, boundIp: "198.51.100.7");
        Assert.False(StoryAudioToken.TryValidate(expired, StoryId, Key, Now, requestIp: "198.51.100.7"));

        var wrongStory = StoryAudioToken.Issue(StoryId, Now.AddHours(1), Key, boundIp: "198.51.100.7");
        Assert.False(StoryAudioToken.TryValidate(wrongStory, "little-cloud", Key, Now, requestIp: "198.51.100.7"));
    }
}

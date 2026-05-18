using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for the pure-static <see cref="DeviceApiKeyHasher"/>. All
/// branches of Hash / Verify / IsHash / ConstantTimeEquals are exercised,
/// including malformed-storage edge cases that should fail closed rather
/// than throw.
/// </summary>
public class DeviceApiKeyHasherTests
{
    [Fact]
    public void Hash_ProducesVersionedFormatWithFiveColonParts()
    {
        var hash = DeviceApiKeyHasher.Hash("dtk_anykey");

        var parts = hash.Split(':');
        Assert.Equal(5, parts.Length);
        Assert.Equal("v1", parts[0]);
        Assert.Equal("pbkdf2-sha256", parts[1]);
        Assert.True(int.TryParse(parts[2], out var iter));
        Assert.Equal(DeviceApiKeyHasher.Iterations, iter);
        // Salt and hash are non-empty Base64. FromBase64String throws on bad input.
        Assert.NotEmpty(Convert.FromBase64String(parts[3]));
        Assert.NotEmpty(Convert.FromBase64String(parts[4]));
    }

    [Fact]
    public void Hash_TwoCallsForSameKeyProduceDifferentHashes()
    {
        // Per-call random salt — same plaintext must NOT produce the same hash.
        var a = DeviceApiKeyHasher.Hash("dtk_samekey");
        var b = DeviceApiKeyHasher.Hash("dtk_samekey");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_EmptyOrNullKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeviceApiKeyHasher.Hash(""));
        Assert.Throws<ArgumentException>(() => DeviceApiKeyHasher.Hash(null!));
    }

    [Fact]
    public void Verify_ValidKey_ReturnsTrue()
    {
        var hash = DeviceApiKeyHasher.Hash("dtk_secret");
        Assert.True(DeviceApiKeyHasher.Verify("dtk_secret", hash));
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        var hash = DeviceApiKeyHasher.Hash("dtk_secret");
        Assert.False(DeviceApiKeyHasher.Verify("dtk_wrong", hash));
    }

    [Theory]
    [InlineData(null, "v1:pbkdf2-sha256:50000:c2FsdA==:aGFzaA==")] // null key
    [InlineData("", "v1:pbkdf2-sha256:50000:c2FsdA==:aGFzaA==")]  // empty key
    [InlineData("dtk_x", null)]                                   // null hash
    [InlineData("dtk_x", "")]                                     // empty hash
    [InlineData("dtk_x", "v2:pbkdf2-sha256:50000:c2FsdA==:aGFzaA==")] // wrong version
    [InlineData("dtk_x", "v1:argon2id:50000:c2FsdA==:aGFzaA==")]   // wrong algorithm
    [InlineData("dtk_x", "v1:pbkdf2-sha256:nope:c2FsdA==:aGFzaA==")] // bad iterations
    [InlineData("dtk_x", "v1:pbkdf2-sha256:0:c2FsdA==:aGFzaA==")]   // zero iterations
    [InlineData("dtk_x", "v1:pbkdf2-sha256:-1:c2FsdA==:aGFzaA==")]  // negative iterations
    [InlineData("dtk_x", "v1:pbkdf2-sha256:50000000000:c2FsdA==:aGFzaA==")] // > 10M
    [InlineData("dtk_x", "v1:pbkdf2-sha256:50000:not-base64!:aGFzaA==")] // bad b64 salt
    [InlineData("dtk_x", "v1:pbkdf2-sha256:50000:c2FsdA==:not-base64!")] // bad b64 hash
    [InlineData("dtk_x", "v1:pbkdf2-sha256:50000")]                // truncated parts
    [InlineData("dtk_x", "plain-not-a-hash")]                       // not a hash format
    public void Verify_MalformedInputs_ReturnFalse(string? apiKey, string? storedHash)
    {
        Assert.False(DeviceApiKeyHasher.Verify(apiKey, storedHash));
    }

    [Fact]
    public void IsHash_RecognizesV1Prefix()
    {
        Assert.True(DeviceApiKeyHasher.IsHash(DeviceApiKeyHasher.Hash("dtk_x")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dtk_plain")]
    [InlineData("v2:pbkdf2-sha256:50000:abc:def")]
    [InlineData("v1:argon2id:50000:abc:def")]
    [InlineData(" v1:pbkdf2-sha256:50000:abc:def")] // leading space
    public void IsHash_RejectsNonHashedValues(string? value)
    {
        Assert.False(DeviceApiKeyHasher.IsHash(value));
    }

    [Fact]
    public void ConstantTimeEquals_EqualStrings_ReturnsTrue()
    {
        Assert.True(DeviceApiKeyHasher.ConstantTimeEquals("dtk_same", "dtk_same"));
    }

    [Fact]
    public void ConstantTimeEquals_DifferentStrings_ReturnsFalse()
    {
        Assert.False(DeviceApiKeyHasher.ConstantTimeEquals("dtk_a", "dtk_b"));
    }

    [Fact]
    public void ConstantTimeEquals_DifferentLengths_ReturnsFalse()
    {
        Assert.False(DeviceApiKeyHasher.ConstantTimeEquals("dtk_short", "dtk_short_extra"));
    }

    [Theory]
    [InlineData(null, "dtk_x")]
    [InlineData("dtk_x", null)]
    [InlineData(null, null)]
    public void ConstantTimeEquals_NullInputs_ReturnFalse(string? a, string? b)
    {
        Assert.False(DeviceApiKeyHasher.ConstantTimeEquals(a, b));
    }

    [Fact]
    public void Verify_DoesNotThrowOnAnyInput()
    {
        // The intentionally-malformed inputs above must never throw — the
        // auth path needs Verify to be a total function. The xUnit Theory
        // above already runs each one; this is the documentation-fact that
        // pins the property.
        Assert.False(DeviceApiKeyHasher.Verify("anything", "garbage-with-no-colons"));
    }
}

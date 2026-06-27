using ArmenianAiToy.Application.Auth;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// TOTP verifier for operator MFA. Pinned against the RFC 6238 Appendix-B
/// SHA-1 test vector (secret "12345678901234567890" = base32
/// GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ; at Unix T=59s the 6-digit code is 287082).
/// </summary>
public class TotpTests
{
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public void Verify_Rfc6238Vector_T59()
    {
        var t = DateTime.UnixEpoch.AddSeconds(59);
        Assert.True(Totp.Verify(Secret, "287082", t));
        Assert.False(Totp.Verify(Secret, "000000", t));
    }

    [Fact]
    public void Verify_ToleratesAdjacentStep_WithinWindow()
    {
        // The code for counter 1 (T=59) must still verify at T=89 (counter 2)
        // with a ±1 window — clock-skew tolerance.
        Assert.True(Totp.Verify(Secret, "287082", DateTime.UnixEpoch.AddSeconds(89), window: 1));
    }

    [Fact]
    public void Verify_RejectsCodeOutsideWindow()
    {
        // T=200 (counter 6) is well outside ±1 of counter 1 → reject.
        Assert.False(Totp.Verify(Secret, "287082", DateTime.UnixEpoch.AddSeconds(200), window: 1));
    }

    [Theory]
    [InlineData("", "287082")]
    [InlineData("GEZDGNBVGY3TQOJQ", "")]
    [InlineData("!!!not-base32!!!", "123456")]
    public void Verify_MalformedInput_ReturnsFalse_NoThrow(string secret, string code)
    {
        Assert.False(Totp.Verify(secret, code, DateTime.UnixEpoch.AddSeconds(59)));
    }
}

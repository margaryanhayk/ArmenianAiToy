using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pure-helper coverage for the QA-sweep hardening slice: email
/// canonicalization/validation, bedtime-window normalization, and the
/// Armenian-question-mark fix in ModeDetector.
/// </summary>
public class QaHardeningUnitTests
{
    // ── EmailNormalizer ──────────────────────────────────────────────
    [Theory]
    [InlineData("Foo@Example.COM", "foo@example.com")]
    [InlineData("  ws@example.com  ", "ws@example.com")]
    [InlineData("already@lower.com", "already@lower.com")]
    public void Normalize_TrimsAndLowercases(string input, string expected)
        => Assert.Equal(expected, EmailNormalizer.Normalize(input));

    [Theory]
    [InlineData("ok@example.com", true)]
    [InlineData("a.b+tag@sub.example.co", true)]
    [InlineData("notanemail", false)]
    [InlineData("a@", false)]
    [InlineData("@b.com", false)]
    [InlineData("x@@@x", false)]
    [InlineData("no-dot@domain", false)]
    [InlineData("has space@example.com", false)]
    [InlineData("hdr@x.com\nBcc: y@z.com", false)] // CR/LF injection blocked
    public void IsValid_ChecksShapeAndControlChars(string input, bool expected)
        => Assert.Equal(expected, EmailNormalizer.IsValid(input));

    // ── BedtimeWindowNormalizer ──────────────────────────────────────
    [Fact]
    public void Bedtime_HalfNull_Disables()
    {
        Assert.Equal((null, null), BedtimeWindowNormalizer.Normalize(new TimeOnly(22, 0), null));
        Assert.Equal((null, null), BedtimeWindowNormalizer.Normalize(null, new TimeOnly(7, 0)));
        Assert.Equal((null, null), BedtimeWindowNormalizer.Normalize(null, null));
    }

    [Fact]
    public void Bedtime_ZeroLength_Disables()
        => Assert.Equal((null, null),
            BedtimeWindowNormalizer.Normalize(new TimeOnly(12, 0), new TimeOnly(12, 0)));

    [Fact]
    public void Bedtime_RealWindow_Preserved()
    {
        var (s, e) = BedtimeWindowNormalizer.Normalize(new TimeOnly(22, 0), new TimeOnly(7, 0));
        Assert.Equal(new TimeOnly(22, 0), s);
        Assert.Equal(new TimeOnly(7, 0), e);
    }

    // ── ModeDetector — Armenian question mark ─────────────────────────
    [Theory]
    [InlineData("Ինչու՞ է երկինքը կապույտ")]      // ՞ after the word
    [InlineData("Ինչպե՞ս են թռչունները թռչում")]  // ՞ INSIDE the word
    public void Detect_ArmenianWhy_WithQuestionMark_IsCuriosity(string message)
    {
        var mode = ModeDetector.Detect(message, history: null);
        Assert.Equal(DetectedMode.Curiosity, mode);
    }

    [Fact]
    public void Detect_ArmenianWhy_WithoutMark_StillCuriosity()
        => Assert.Equal(DetectedMode.Curiosity,
            ModeDetector.Detect("Ինչու երկինքը կապույտ է", history: null));
}

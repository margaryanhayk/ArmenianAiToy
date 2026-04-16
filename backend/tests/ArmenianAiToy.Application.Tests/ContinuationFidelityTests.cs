using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Phase F F2: continuation-fidelity guard.
/// Verifies the helper correctly detects when a continuation is missing
/// a reference to the chosen label's key Armenian words.
/// </summary>
public class ContinuationFidelityTests
{
    // Should flag: continuation missing label reference

    [Fact]
    public void MissingReference_NoLabelTokenInContinuation()
    {
        // Label: "\u0555\u0563\u0576\u0565\u0576\u0584 \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576" (tokens: \u0585\u0563\u0576\u0565\u0576\u0584, \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576)
        // Continuation: "\u054f\u0578\u0582\u0583\u0568 \u0562\u0561\u0581\u057e\u0565\u0581\u0589" — neither token appears.
        Assert.True(ContinuationFidelity.IsMissingReference(
            "\u0555\u0563\u0576\u0565\u0576\u0584 \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576",
            "\u054f\u0578\u0582\u0583\u0568 \u0562\u0561\u0581\u057e\u0565\u0581\u0589"));
    }

    [Fact]
    public void MissingReference_DifferentInflectionNotSubstring()
    {
        // Label: "\u0532\u0561\u0581\u0565\u0576\u0584 \u057f\u0578\u0582\u0583\u0568" (tokens: \u0562\u0561\u0581\u0565\u0576\u0584, \u057f\u0578\u0582\u0583\u0568)
        // Continuation has "\u057f\u0578\u0582\u0583\u056b\u0581" — different inflection, not a substring match.
        Assert.True(ContinuationFidelity.IsMissingReference(
            "\u0532\u0561\u0581\u0565\u0576\u0584 \u057f\u0578\u0582\u0583\u0568",
            "\u054f\u0578\u0582\u0583\u056b\u0581 \u0564\u0578\u0582\u0580\u057d \u0569\u057c\u0561\u057e\u0589"));
    }

    // Should NOT flag: continuation contains label reference

    [Fact]
    public void HasReference_ExactTokenPresent()
    {
        // Label: "\u0555\u0563\u0576\u0565\u0576\u0584 \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576" (token: \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576)
        // Continuation contains "\u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576" verbatim.
        Assert.False(ContinuationFidelity.IsMissingReference(
            "\u0555\u0563\u0576\u0565\u0576\u0584 \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576",
            "\u0553\u0578\u0584\u0580\u056b\u056f \u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576 \u0578\u0582\u0580\u0561\u056d\u0561\u0581\u0561\u057e\u0589"));
    }

    [Fact]
    public void HasReference_TokenAppearsAsSubstring()
    {
        // Label: "\u0532\u0561\u0581\u0565\u0576\u0584 \u057f\u0578\u0582\u0583\u0568" (token: \u057f\u0578\u0582\u0583\u0568)
        // Continuation contains "\u057f\u0578\u0582\u0583\u0568" as part of a longer word — substring match works.
        Assert.False(ContinuationFidelity.IsMissingReference(
            "\u0532\u0561\u0581\u0565\u0576\u0584 \u057f\u0578\u0582\u0583\u0568",
            "\u0553\u0578\u0584\u0580\u056b\u056f \u057f\u0578\u0582\u0583\u0568 \u0562\u0561\u0581\u057e\u0565\u0581\u0589"));
    }

    // Edge cases: should NOT flag

    [Fact]
    public void NullLabel_ReturnsFalse()
    {
        Assert.False(ContinuationFidelity.IsMissingReference(null, "some continuation"));
    }

    [Fact]
    public void NullContinuation_ReturnsFalse()
    {
        Assert.False(ContinuationFidelity.IsMissingReference("some label", null));
    }

    [Fact]
    public void EmptyLabel_ReturnsFalse()
    {
        Assert.False(ContinuationFidelity.IsMissingReference("", "some continuation"));
    }

    [Fact]
    public void ShortTokensOnly_ReturnsFalse()
    {
        // Label has only short tokens (< 4 chars) — no extractable tokens.
        Assert.False(ContinuationFidelity.IsMissingReference(
            "\u0531\u0580\u056b",
            "\u054f\u0578\u0582\u0583\u0568 \u0562\u0561\u0581\u057e\u0565\u0581\u0589"));
    }

    [Fact]
    public void CaseInsensitive_MatchesAnyCase()
    {
        // Label uppercase Armenian, continuation lowercase — should still match.
        Assert.False(ContinuationFidelity.IsMissingReference(
            "\u0539\u054c\u0549\u0548\u0552\u0546\u053b\u053f\u053b\u0546",
            "\u0569\u057c\u0579\u0578\u0582\u0576\u056b\u056f\u056b\u0576 \u0578\u0582\u0580\u0561\u056d\u0561\u0581\u0561\u057e\u0589"));
    }
}

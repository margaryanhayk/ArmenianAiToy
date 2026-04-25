using System.Text.RegularExpressions;

namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// Result of evaluating a Story-mode CHOICE_A / CHOICE_B pair against the
/// preceding story body. <see cref="IsCoherent"/> = true means the pair can
/// be shown to the child as-is. <see cref="ShouldRetry"/> mirrors
/// !<see cref="IsCoherent"/>; the caller decides whether to do an LLM retry
/// or fall back to deterministic repair. <see cref="RepairedChoiceA"/> /
/// <see cref="RepairedChoiceB"/> are populated on every fail so callers can
/// substitute deterministic, body-anchored choices without re-running the
/// gate. <see cref="Reason"/> is a short opaque tag meant for tests and
/// structured logs — not for end users.
/// </summary>
public sealed record StoryChoiceCoherenceResult(
    bool IsCoherent,
    bool ShouldRetry,
    string? RepairedChoiceA,
    string? RepairedChoiceB,
    string Reason);

/// <summary>
/// Runtime guardrail that checks whether parsed CHOICE_A / CHOICE_B labels
/// are grounded in the preceding story body. Intentionally lives downstream
/// of <see cref="TailBlockParser"/> (format-only) and complements
/// <see cref="ChoiceDiversity"/> (A-vs-B difference). Deterministic and
/// Armenian-aware; no LLM, no external NLP dependency.
/// </summary>
public interface IStoryChoiceCoherenceGate
{
    StoryChoiceCoherenceResult Evaluate(string body, string? choiceA, string? choiceB);
}

/// <summary>
/// First-slice runtime gate — narrowly scoped to "choice introduces an
/// object/action/place that the body never mentioned." Pass / soft-pass /
/// fail rules:
/// <list type="bullet">
///   <item>Both choices share at least one Armenian content stem with the
///   body → pass.</item>
///   <item>One choice is grounded, the other is purely a generic-action
///   phrase (no concrete content tokens at all) → soft-pass.</item>
///   <item>Either choice introduces ≥2 concrete tokens absent from the body
///   → fail.</item>
///   <item>Both choices have zero overlap with the body → fail.</item>
///   <item><see cref="ChoiceDiversity.AreTooSimilar"/> returns true → fail.</item>
/// </list>
/// On any fail, <see cref="StoryChoiceCoherenceResult.RepairedChoiceA"/> /
/// <c>RepairedChoiceB</c> carry deterministic anchor-based replacements
/// drawn from the body's last few sentences.
/// </summary>
public sealed class StoryChoiceCoherenceGate : IStoryChoiceCoherenceGate
{
    private const int MinTokenLen = 3;

    // One Armenian-letter run (Unicode block U+0530..U+058F).
    private static readonly Regex ArmenianRunRegex = new(
        @"[԰-֏]+", RegexOptions.Compiled);

    // Trailing punctuation to peel off a raw token before classification.
    private static readonly char[] TrailingPunct =
    [
        '.', ',', '!', '?', ';', ':',
        '։', // Armenian full stop
        '՞', // Armenian question mark
        '՜', // Armenian exclamation
        '՝', // Armenian comma
        '՛',
        '«', '»',
        '"', '\'', '(', ')', '—', '-',
    ];

    // Sentence delimiters used by RepairFromBody to slice the body into the
    // last 2–4 sentences.
    private static readonly char[] SentenceDelims =
    [
        '։', '.', '!', '?', '՞', '՜',
    ];

    // Common Armenian function words. Conservative — only words that carry
    // no story content. Generic frame nouns (շուրջ / ճանապարհ / առաջ) live
    // in GenericActionStems instead so they can still mark a choice as
    // action-only without being silently overlooked.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "և",
        "ու",
        "որ",
        "այս",
        "այդ",
        "այն",
        "մի",
        "մեկ",
        "ինչ",
        "ինչը",
        "որը",
        "ով",
        "երբ",
        "որտեղ",
        "ուր",
        "թե",
        "թեև",
        "բայց",
        "սակայն",
        "հետո",
        "ապա",
        "դեռ",
        "արդեն",
        "շատ",
        "քիչ",
        "ինչու",
        "որպես",
        "որպեսզի",
        "է",
        "են",
        "էր",
        "էի",
        "էին",
        "չէ",
        "չի",
        "չէր",
        "համար",
        "մեջ",
        "վրա",
        "արդյոք",
        "գուցե",
        "եթե",
        "պետք",
        "նա",
        "նրա",
        "իր",
        "իրեն",
        "դու",
        "էինք",
        "էիք",
        "այսպես",
        "այնպես",
        "ինչպես",
        "այստեղ",
        "այնտեղ",
    };

    // Suffixes stripped (longest first, single pass). Folds inflectional
    // variants of the same Armenian noun/verb to one stem so body↔choice
    // overlap survives ordinary case/conjugation differences.
    private static readonly string[] Suffixes =
    [
        "ություն",
        "ներով",
        "ներից",
        "ներին",
        "ների",
        "ները",
        "ներն",
        "երով",
        "երից",
        "երին",
        "երի",
        "երը",
        "ներ",
        "ին",
        "ից",
        "ով",
        "ում",
        "ած",
        "եց",
        "ենք",
        "ել",
        "ալ",
        "անք",
        "ի",
        "ը",
        "ն",
    ];

    // Generic-action stems — what a choice tokenizes to when it carries
    // ONLY a "let's look / let's continue / around / forward" frame and
    // names no concrete body entity. Stems chosen so the same word in any
    // of its surface forms (շարունակել / շարունակենք / շարունակում) maps
    // to the same entry after the suffix table runs.
    private static readonly HashSet<string> GenericActionStems = new(StringComparer.Ordinal)
    {
        "շարունակ",
        "նայ",
        "քայլ",
        "սպաս",
        "փորձ",
        "հարցն",
        "մտած",
        "մոտեն",
        "շուրջ",
        "ճանապարհ",
        "առաջ",
        "տես",
        "դեպի",
    };

    // Generic action raw surface forms — short Armenian imperatives whose
    // 2-char roots («լս», «գն») fall below the MinTokenLen=3 stem threshold
    // and therefore cannot be normalized into <see cref="GenericActionStems"/>.
    // Whitelisting the full surface form keeps them out of the
    // ungrounded-concrete bucket while still letting body anchors carry the
    // "is this choice grounded?" verdict via other tokens in the same choice.
    private static readonly HashSet<string> GenericActionRawForms = new(StringComparer.Ordinal)
    {
        "լսենք",
        "լսել",
        "լսեց",
        "լսում",
        "գնանք",
        "գնալ",
        "գնաց",
        "գնում",
        "տեսնենք",
        "տեսնել",
        "տեսավ",
        "տեսնում",
        "արի",
        "եկեք",
    };

    // Last-resort deterministic fallback when the body has zero usable
    // anchors. Pinned to the avoided generic pair from the design notes —
    // we only emit it when there is genuinely nothing else to anchor on.
    private const string LastResortChoiceA = "Շարունակենք պատմությունը";
    private const string LastResortChoiceB = "Նայենք շուրջը";

    /// <summary>
    /// Evaluate a body / choice-pair triple. Body must be the prose with
    /// the tail block already stripped (call after <see cref="TailBlockParser"/>).
    /// Both choice arguments may be null — the gate treats null/whitespace
    /// as "missing" and returns a fail with deterministic repair, mirroring
    /// the existing TailBlockParser-miss path.
    /// </summary>
    public StoryChoiceCoherenceResult Evaluate(string body, string? choiceA, string? choiceB)
    {
        if (string.IsNullOrWhiteSpace(choiceA) || string.IsNullOrWhiteSpace(choiceB))
        {
            var (rA, rB) = RepairFromBody(body);
            return new StoryChoiceCoherenceResult(
                IsCoherent: false,
                ShouldRetry: true,
                RepairedChoiceA: rA,
                RepairedChoiceB: rB,
                Reason: "missing_choice");
        }

        if (ChoiceDiversity.AreTooSimilar(choiceA, choiceB))
        {
            var (rA, rB) = RepairFromBody(body);
            return new StoryChoiceCoherenceResult(
                false, true, rA, rB, "choices_too_similar");
        }

        var bodyStems = ExtractContentStems(body);
        var classA = ClassifyChoice(choiceA!, bodyStems);
        var classB = ClassifyChoice(choiceB!, bodyStems);

        // ≥2 concrete ungrounded tokens in either choice — strong fail.
        if (classA.Kind == ChoiceKind.StronglyUngrounded
            || classB.Kind == ChoiceKind.StronglyUngrounded)
        {
            var (rA, rB) = RepairFromBody(body);
            return new StoryChoiceCoherenceResult(
                false, true, rA, rB,
                "choice_introduces_unmentioned_objects");
        }

        bool aGrounded = classA.Kind == ChoiceKind.Grounded;
        bool bGrounded = classB.Kind == ChoiceKind.Grounded;

        if (aGrounded && bGrounded)
        {
            return new StoryChoiceCoherenceResult(
                true, false, null, null, "both_grounded");
        }

        bool aActionOnly = classA.Kind == ChoiceKind.ActionOnly;
        bool bActionOnly = classB.Kind == ChoiceKind.ActionOnly;

        // Soft pass: one side is body-grounded and the other is purely a
        // generic action frame. The action frame is harmless because it
        // doesn't introduce a new object, just a generic next step.
        if ((aGrounded && bActionOnly) || (bGrounded && aActionOnly))
        {
            return new StoryChoiceCoherenceResult(
                true, false, null, null, "one_action_only_other_grounded");
        }

        var (repA, repB) = RepairFromBody(body);
        var reason = aGrounded || bGrounded
            ? "choice_introduces_unmentioned_object"
            : "both_choices_ungrounded";
        return new StoryChoiceCoherenceResult(false, true, repA, repB, reason);
    }

    private enum ChoiceKind { Grounded, ActionOnly, Ungrounded, StronglyUngrounded }

    private readonly record struct ChoiceClassification(
        ChoiceKind Kind,
        int OverlapCount,
        int UngroundedConcreteCount);

    private static ChoiceClassification ClassifyChoice(string choice, HashSet<string> bodyStems)
    {
        // Walk the choice's tokens directly so we can consult the raw
        // surface form against GenericActionRawForms before suffix-strip
        // — the suffix table can't reach 2-char verb roots like «լս» / «գն».
        var tokens = ExtractTokensWithStems(choice);
        if (tokens.Count == 0)
        {
            // No Armenian content at all (stop-word-only or empty choice).
            // Treat as action-only: we can't ground or accuse it of
            // introducing anything new.
            return new ChoiceClassification(ChoiceKind.ActionOnly, 0, 0);
        }

        int overlap = 0;
        int ungroundedConcrete = 0;
        foreach (var (raw, stem) in tokens)
        {
            if (bodyStems.Contains(stem))
            {
                overlap++;
                continue;
            }
            // Stem-prefix match: a longer body stem that starts with this
            // choice stem, or vice versa. Catches mild morphology gaps the
            // suffix-strip table missed.
            if (bodyStems.Any(b => b.StartsWith(stem, StringComparison.Ordinal)
                                || stem.StartsWith(b, StringComparison.Ordinal)))
            {
                overlap++;
                continue;
            }
            if (GenericActionStems.Contains(stem)) continue;
            if (GenericActionRawForms.Contains(raw)) continue;
            ungroundedConcrete++;
        }

        if (overlap > 0)
        {
            return ungroundedConcrete >= 2
                ? new ChoiceClassification(ChoiceKind.StronglyUngrounded, overlap, ungroundedConcrete)
                : new ChoiceClassification(ChoiceKind.Grounded, overlap, ungroundedConcrete);
        }

        if (ungroundedConcrete == 0)
        {
            return new ChoiceClassification(ChoiceKind.ActionOnly, 0, 0);
        }

        return ungroundedConcrete >= 2
            ? new ChoiceClassification(ChoiceKind.StronglyUngrounded, 0, ungroundedConcrete)
            : new ChoiceClassification(ChoiceKind.Ungrounded, 0, ungroundedConcrete);
    }

    private static HashSet<string> ExtractContentStems(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return set;

        foreach (Match m in ArmenianRunRegex.Matches(text.ToLowerInvariant()))
        {
            var raw = m.Value.Trim().TrimEnd(TrailingPunct);
            if (raw.Length < MinTokenLen) continue;
            if (StopWords.Contains(raw)) continue;
            var stem = StripSuffix(raw);
            if (stem.Length < MinTokenLen) continue;
            if (StopWords.Contains(stem)) continue;
            set.Add(stem);
        }
        return set;
    }

    private static List<(string Raw, string Stem)> ExtractTokensWithStems(string text)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in ArmenianRunRegex.Matches(text.ToLowerInvariant()))
        {
            var raw = m.Value.Trim().TrimEnd(TrailingPunct);
            if (raw.Length < MinTokenLen) continue;
            if (StopWords.Contains(raw)) continue;
            var stem = StripSuffix(raw);
            if (stem.Length < MinTokenLen) continue;
            if (StopWords.Contains(stem)) continue;
            if (!seen.Add(stem)) continue;
            list.Add((raw, stem));
        }
        return list;
    }

    private static string StripSuffix(string token)
    {
        foreach (var suffix in Suffixes)
        {
            if (token.Length - suffix.Length >= MinTokenLen
                && token.EndsWith(suffix, StringComparison.Ordinal))
            {
                return token[..^suffix.Length];
            }
        }
        return token;
    }

    /// <summary>
    /// Build a deterministic, body-anchored choice pair. Prefers concrete
    /// noun stems from the last 2–4 sentences and renders each anchor with
    /// a dative-style «-ին» suffix so the choice reads as natural child
    /// Armenian («Մոտենանք թռչունիկին», not «Տեսնենք թռչունիկը»). Falls
    /// back to the generic pair only when the body has no usable anchor —
    /// a degenerate body the LLM shouldn't produce in the first place.
    /// </summary>
    internal static (string A, string B) RepairFromBody(string? body)
    {
        var anchors = ExtractAnchorStems(body);
        if (anchors.Count >= 2)
        {
            return (
                "Մոտենանք " + Dative(anchors[0]),
                "Նայենք " + Dative(anchors[1]));
        }
        if (anchors.Count == 1)
        {
            var d = Dative(anchors[0]);
            return ("Մոտենանք " + d, "Նայենք " + d);
        }
        return (LastResortChoiceA, LastResortChoiceB);
    }

    // Append the Armenian dative «-ին» to a stem. For the rare vowel-end
    // stems (e.g. «քամի») use the «-ն» variant so the output stays
    // pronounceable. This is morphology-light by design — irregular
    // declensions are not handled, and they are acceptable noise for a
    // last-resort fallback that fires when the LLM has already failed
    // twice.
    private static string Dative(string stem)
    {
        if (stem.Length == 0) return stem;
        var last = stem[^1];
        bool vowelEnd =
               last == 'ա'
            || last == 'ե'
            || last == 'է'
            || last == 'ի'
            || last == 'ո'
            || last == 'օ';
        return vowelEnd ? stem + "ն" : stem + "ին";
    }

    private static List<string> ExtractAnchorStems(string? body)
    {
        var anchors = new List<string>();
        if (string.IsNullOrWhiteSpace(body)) return anchors;

        // Slice into sentences and walk back-to-front so the most recent
        // beat anchors choice A. Take from the last 4 sentences max.
        var sentences = body.Split(SentenceDelims, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int considered = 0;
        for (int i = sentences.Length - 1; i >= 0 && considered < 4; i--, considered++)
        {
            foreach (Match m in ArmenianRunRegex.Matches(sentences[i].ToLowerInvariant()))
            {
                var raw = m.Value.Trim().TrimEnd(TrailingPunct);
                if (raw.Length < MinTokenLen) continue;
                if (StopWords.Contains(raw)) continue;
                var stem = StripSuffix(raw);
                if (stem.Length < MinTokenLen) continue;
                if (StopWords.Contains(stem)) continue;
                if (GenericActionStems.Contains(stem)) continue;
                if (!seen.Add(stem)) continue;
                anchors.Add(stem);
                if (anchors.Count >= 2) return anchors;
            }
        }
        return anchors;
    }
}

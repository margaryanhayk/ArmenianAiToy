// Evaluators.cs — pure deterministic checks for Armenian story turns.
//
// Kept BCL-only and dependency-free so the test project (or any
// future consumer) can `<Compile Include="..\StoryInteractiveLoop\
// Evaluators.cs" />` without taking a project reference on the Exe.
//
// Every method here is intentionally PURE: no I/O, no static state,
// no random, no clock. That keeps the test surface narrow.
//
// SCOPE: the evaluator surface here is a SUPERSET of the StoryBenchmark
// signals (same_first_verb / continuation_no_label_reference /
// start_continuation_recap_overlap), tuned for multi-turn loop checks.
// It deliberately does NOT replace StoryBenchmark — that tool keeps
// owning the one-shot regression baseline + BenchmarkAll plumbing.

using System.Text.RegularExpressions;

namespace ArmenianAiToy.Tools.StoryInteractiveLoop;

public static class Evaluators
{
    // Armenian unicode block: U+0530..U+058F. We match individual
    // letters via the range; the block itself spans punctuation +
    // letters but only letters are interesting for the ratio check.
    private static readonly Regex ArmenianLetter =
        new(@"[԰-֏]", RegexOptions.Compiled);

    // A "letter-bearing" character for ratio denominator. Excludes
    // whitespace, digits, ASCII punctuation, Armenian punctuation
    // (verjaket ։, etc.), and quotation marks. Anything that is a
    // letter in any script counts.
    private static readonly Regex AnyLetter =
        new(@"\p{L}", RegexOptions.Compiled);

    // Latin letters, used as the "non-Armenian leakage" signal.
    // We allow occasional ASCII (numbers, punctuation, occasional
    // names) — only sustained Latin words trip the flag.
    private static readonly Regex LatinRun =
        new(@"[A-Za-z]{3,}", RegexOptions.Compiled);

    // Cyrillic letters — same idea, sustained runs trip the flag.
    private static readonly Regex CyrillicRun =
        new(@"[Ѐ-ӿ]{3,}", RegexOptions.Compiled);

    // Armenian whitespace + punctuation we strip from token edges.
    private static readonly char[] ArmenianTrailingPunct =
    {
        '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '—', '-',
        '։', // ։ verjaket (full stop)
        '՞', // ՞ question
        '՜', // ՜ exclamation
        '՝', // ՝ comma
        '՛', // ՛ emphasis
        '«', '»'
    };

    // Banned generic-choice phrases. A choice that matches one of these
    // (with surface variants) is not grounded in the story — it's a
    // meta-chat affordance.
    //
    // Surface match is case-insensitive and ignores trailing punctuation.
    // We DO NOT ban a longer choice that merely STARTS with one of these
    // (e.g. "Գնալ դեպի անտառ" is fine; "Գնալ" alone is not).
    public static readonly string[] BannedGenericChoices =
    {
        "շարունակել",
        "շարունակիր",
        "ուրիշ բան անել",
        "ուրիշ բան",
        "ի՞նչ անենք",
        "ինչ անենք",
        "ինչ կլինի",
        "առաջինը",
        "երկրորդը",
        "խոսել",
        "գնալ",
        "այո",
        "ոչ",
        "ուրիշ ճանապարհ",
        "ուրիշ ճանապարհ ընտրի"
    };

    // Length budgets, matched to ESP32 display + child-attention budget.
    public const int MinBodyChars = 100;
    public const int MaxBodyChars = 800;
    public const int MaxChoiceChars = 60;
    public const double MinArmenianRatio = 0.80;
    public const double RecapOverlapThreshold = 0.60;

    // ---- Noun-grounding ignore lists ----
    //
    // This is a deterministic *heuristic*, NOT a linguistic parser. The
    // job is to filter out tokens whose presence in a choice does NOT
    // signal real grounding in the story body: action verbs (շարունակել /
    // մոտենանք / նայենք …), filler adjectives (փոքրիկ / բարի / կախարդական),
    // and pronoun-like words (նրան / այնտեղ / …). What remains after
    // filtering is treated as concrete-noun grounding evidence.
    //
    // Prefix-matching is used because Armenian agglutinative endings
    // multiply surface forms (նայել / նայենք / նայեց / նայիր / նայել-ով …),
    // and the existing ArmenianStem helper cannot strip endings shorter
    // than the 4-char length guard — so several action surface forms keep
    // their endings.
    //
    // Every prefix is ≥ 3 chars to keep it from accidentally matching
    // unrelated kid-noun stems. Bare «գն» (2 chars) is deliberately NOT
    // used — it would collide with «գնդակ» (ball) and other kid-story
    // nouns.

    private static readonly string[] IgnoreStemPrefixes =
    {
        // Movement / approach
        "մոտեն", "մոտե",       // մոտենանք → մոտեն → մոտե
        "գնան", "գնա", "գնալ",  // «գն» alone is unsafe (գնդակ)
        "հետև",                 // հետևել / հետևենք
        // Look / listen / see
        "նայ",                  // նայ-, նայենք, նայել, նայեց, նայիր
        "լսել", "լսենք", "լսեց", "լսիր",
        "տես",                  // տես-, տեսնել, տեսնենք
        // Speak / ask / say
        "խոս",                  // խոս-, խոսել, խոսենք
        "հարց",                 // հարց-, հարցնենք
        "ասենք", "ասեց", "ասաց", "ասել",  // «աս» alone unsafe (աստղ)
        // Help / search
        "օգն",
        "փնտր",
        // Generic actions
        "շարունակ",
        "ընտր",                 // ընտր-, ընտրենք, ընտրեց
        "բաց",                  // բացել, բացենք
        "կանգ",                 // կանգնել, կանգնեց
        "վազ",                  // վազել, վազենք
        "խաղ",                  // խաղ-, խաղալ, խաղանք
        "սպաս",                 // սպասել
        "բեր",                  // բերել, բերեց
        "վերց",                 // վերցն-
        // Filler / pronoun / adjective
        "նրան", "իրան",
        "այնտ", "այստ",         // այնտեղ / այստեղ
        "հետո", "հիմա",
        "միաս",                 // միասին
        "փոքր",                 // փոքրիկ — every Areg story uses it
        "բարի",                 // every Areg story uses it
        "կախարդա"               // կախարդական (adj) — NOT «կախարդ» (wizard noun)
    };

    /// <summary>
    /// Fraction of letter-bearing characters that fall in the Armenian
    /// block. Returns 0 when the input has no letter-bearing chars.
    /// </summary>
    public static double ArmenianRatio(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.0;
        int total = 0, armenian = 0;
        foreach (Match m in AnyLetter.Matches(text))
        {
            total++;
            var c = m.Value[0];
            if (c >= '԰' && c <= '֏') armenian++;
        }
        return total == 0 ? 0.0 : (double)armenian / total;
    }

    /// <summary>True when the text contains a Latin run of 3+ letters.</summary>
    public static bool HasLatinLeakage(string? text) =>
        !string.IsNullOrEmpty(text) && LatinRun.IsMatch(text);

    /// <summary>True when the text contains a Cyrillic run of 3+ letters.</summary>
    public static bool HasCyrillicLeakage(string? text) =>
        !string.IsNullOrEmpty(text) && CyrillicRun.IsMatch(text);

    /// <summary>
    /// True when the choice is one of the banned generic affordances
    /// (after lowercasing + trailing punctuation strip). A longer
    /// choice that merely contains the banned phrase as a substring
    /// is NOT flagged — the comparison is whole-string equality.
    /// </summary>
    public static bool IsGenericChoice(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice)) return false;
        var normalized = choice.Trim()
            .TrimEnd(ArmenianTrailingPunct)
            .ToLowerInvariant();
        foreach (var banned in BannedGenericChoices)
        {
            if (string.Equals(normalized, banned, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the choice label has at least one ≥4-char Armenian
    /// stem that also appears in the continuation body. Mirrors the
    /// StoryBenchmark `continuation_no_label_reference` signal — same
    /// stemmer, same minimum length, same conservative semantics
    /// (short labels with no ≥4-char tokens are considered grounded
    /// rather than ungrounded).
    /// </summary>
    public static bool ChoiceGroundedInBody(string? choiceLabel, string? body)
    {
        if (string.IsNullOrWhiteSpace(choiceLabel)) return true;
        if (string.IsNullOrWhiteSpace(body)) return false;
        var labelStems = ExtractArmenianStems(choiceLabel, minLen: 4);
        if (labelStems.Count == 0) return true;
        var bodyStems = ExtractArmenianStems(body, minLen: 4);
        return labelStems.Overlaps(bodyStems);
    }

    /// <summary>
    /// Jaccard overlap on ≥4-char Armenian tokens drawn from the FIRST
    /// sentence of each input. Values ≥ <see cref="RecapOverlapThreshold"/>
    /// indicate a recap-heavy continuation. Returns 0 when either side
    /// yields fewer than 2 tokens (insufficient signal).
    /// </summary>
    public static double FirstSentenceRecapOverlap(string? prevBody, string? nextBody)
    {
        if (string.IsNullOrWhiteSpace(prevBody) || string.IsNullOrWhiteSpace(nextBody))
            return 0.0;
        var prevFirst = FirstSentence(prevBody);
        var nextFirst = FirstSentence(nextBody);
        var a = ExtractArmenianTokens(prevFirst, minLen: 4);
        var b = ExtractArmenianTokens(nextFirst, minLen: 4);
        if (a.Count < 2 || b.Count < 2) return 0.0;
        int inter = 0;
        foreach (var x in a) if (b.Contains(x)) inter++;
        int union = a.Count + b.Count - inter;
        return union == 0 ? 0.0 : (double)inter / union;
    }

    /// <summary>
    /// Both choices share their first Armenian token (a sign that the
    /// branches are not real branches).
    /// </summary>
    public static bool ChoicesShareFirstToken(string? a, string? b)
    {
        var fa = FirstArmenianToken(a);
        var fb = FirstArmenianToken(b);
        return fa is not null && fb is not null
            && string.Equals(fa, fb, StringComparison.Ordinal);
    }

    /// <summary>
    /// Concrete-noun stem candidates from a choice. Starts with every
    /// ≥4-char Armenian stem in the choice, then strips anything that
    /// matches the verb / filler / pronoun ignore prefixes. The remaining
    /// stems are treated as concrete grounding evidence — if NONE of
    /// them appear in the body, the choice introduced a noun the body
    /// never mentioned.
    ///
    /// Empty result means "this choice has no reliable noun signal" (e.g.
    /// «Շարունակել», «Այո», «Մոտենալ»). In that case the caller should
    /// NOT add a noun-grounding warning; the existing generic-choice
    /// and verb-grounding warnings already cover those cases.
    /// </summary>
    public static HashSet<string> ChoiceNounStems(string? choice)
    {
        var nouns = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(choice)) return nouns;
        foreach (var stem in ExtractArmenianStems(choice, minLen: 4))
        {
            if (IsIgnoredStem(stem)) continue;
            nouns.Add(stem);
        }
        return nouns;
    }

    /// <summary>
    /// True when at least one of a choice's noun stems appears in the
    /// body. A choice with no noun-stem candidates is considered grounded
    /// (conservative: we don't flag what we can't reliably classify).
    /// </summary>
    public static bool ChoiceNounsAppearInBody(string? choice, string? body)
    {
        var nounStems = ChoiceNounStems(choice);
        if (nounStems.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(body)) return false;
        var bodyStems = ExtractArmenianStems(body, minLen: 4);
        return nounStems.Overlaps(bodyStems);
    }

    private static bool IsIgnoredStem(string stem)
    {
        foreach (var p in IgnoreStemPrefixes)
            if (stem.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Per-turn warning lists for a single session. Empty list per turn
    /// when no repetition is detected. The SECOND-and-later occurrence
    /// of an exact normalized (ChoiceA, ChoiceB) pair gets the
    /// `choices_repeated_from_earlier_turn` code; the first occurrence
    /// is silent.
    ///
    /// Normalization: trim, collapse whitespace, strip trailing punctuation,
    /// lowercase invariantly. A turn with either choice null/empty is
    /// skipped — pairs are only compared when BOTH labels are present.
    /// Order-sensitive: (A, B) and (B, A) are treated as different pairs
    /// in this slice (per prompt: exact pair is enough).
    /// </summary>
    public static List<List<string>> DetectRepeatedChoicePairs(
        IReadOnlyList<(string? A, string? B)> turnChoices)
    {
        var perTurn = new List<List<string>>(turnChoices.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (a, b) in turnChoices)
        {
            var warnings = new List<string>();
            var na = NormalizeChoiceForRepeat(a);
            var nb = NormalizeChoiceForRepeat(b);
            if (!string.IsNullOrEmpty(na) && !string.IsNullOrEmpty(nb))
            {
                var key = na + "||" + nb;
                if (!seen.Add(key))
                    warnings.Add("choices_repeated_from_earlier_turn");
            }
            perTurn.Add(warnings);
        }
        return perTurn;
    }

    private static string NormalizeChoiceForRepeat(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var trimmed = s.Trim().TrimEnd(ArmenianTrailingPunct).ToLowerInvariant();
        // Collapse internal whitespace.
        return System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
    }

    /// <summary>
    /// Per-turn warning lists for a single session. Emits the
    /// <c>choice_repeated_from_earlier_turn</c> code on any turn
    /// where EITHER <c>ChoiceA</c> OR <c>ChoiceB</c> (normalized
    /// via <see cref="NormalizeChoiceForRepeat"/>) has appeared
    /// as A or B on any EARLIER turn in the same call.
    ///
    /// <para>Complements <see cref="DetectRepeatedChoicePairs"/>
    /// (exact-pair). They are intentionally NOT mutually exclusive:
    /// a turn that repeats the exact pair will trigger BOTH
    /// warnings, which is correct semantics (one signal says "this
    /// turn happened before"; the other says "at least one option
    /// happened before"). The aggregated deduction is therefore
    /// stronger when the pair is exact, satisfying the
    /// "exact-pair penalty ≥ individual penalty" contract.</para>
    ///
    /// <para>Per-turn emission caps at ONE warning even when BOTH
    /// choices on the later turn repeat — the signal is "this turn
    /// is stuck", not "this turn is doubly stuck". Same-turn
    /// duplicate choices (A == B) do NOT trip this detector;
    /// <c>choices_identical</c> covers that case on its own. The
    /// detector reads <c>seen</c> via <c>Contains</c> BEFORE
    /// adding, so same-turn pair entries never self-match.</para>
    ///
    /// <para>Order-insensitive across turns: an A-side choice that
    /// later reappears as B-side counts (and vice versa). Empty /
    /// null choices are skipped — pairs are only registered when
    /// the individual side has a non-empty normalized form.</para>
    /// </summary>
    public static List<List<string>> DetectRepeatedIndividualChoices(
        IReadOnlyList<(string? A, string? B)> turnChoices)
    {
        var perTurn = new List<List<string>>(turnChoices.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (a, b) in turnChoices)
        {
            var warnings = new List<string>();
            var na = NormalizeChoiceForRepeat(a);
            var nb = NormalizeChoiceForRepeat(b);

            // Check BEFORE adding so a same-turn A == B doesn't
            // self-match. If A == B and both are non-empty, the
            // first Contains is false (no prior turn registered it),
            // then both Adds run — only the first does work because
            // HashSet is idempotent.
            bool anyRepeat =
                (!string.IsNullOrEmpty(na) && seen.Contains(na)) ||
                (!string.IsNullOrEmpty(nb) && seen.Contains(nb));

            if (!string.IsNullOrEmpty(na)) seen.Add(na);
            if (!string.IsNullOrEmpty(nb)) seen.Add(nb);

            if (anyRepeat) warnings.Add("choice_repeated_from_earlier_turn");
            perTurn.Add(warnings);
        }
        return perTurn;
    }

    /// <summary>
    /// Run all per-turn checks and return a list of warning codes for
    /// the given turn. Empty list = clean turn.
    /// </summary>
    public static List<string> EvaluateTurn(TurnEvaluationInput input)
    {
        var warnings = new List<string>();

        var body = input.Body ?? "";
        if (body.Length < MinBodyChars && !string.IsNullOrWhiteSpace(body))
            warnings.Add("body_too_short");
        if (body.Length > MaxBodyChars)
            warnings.Add("body_too_long");

        var ratio = ArmenianRatio(body);
        if (ratio < MinArmenianRatio && body.Length > 0)
            warnings.Add($"armenian_ratio_low:{ratio:F2}");

        if (HasLatinLeakage(body)) warnings.Add("latin_leakage_body");
        if (HasCyrillicLeakage(body)) warnings.Add("cyrillic_leakage_body");

        // Choice checks only apply to story turns that actually carry
        // a choice block. The runner sets HasChoices=false for safety-
        // fallback / final turns so we don't double-count missing
        // choices as warnings on those rows.
        if (input.HasChoices)
        {
            var a = input.ChoiceA ?? "";
            var b = input.ChoiceB ?? "";

            if (string.IsNullOrWhiteSpace(a)) warnings.Add("choice_a_missing");
            if (string.IsNullOrWhiteSpace(b)) warnings.Add("choice_b_missing");
            if (a.Length > MaxChoiceChars) warnings.Add("choice_a_too_long");
            if (b.Length > MaxChoiceChars) warnings.Add("choice_b_too_long");
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
                && string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal))
                warnings.Add("choices_identical");
            if (ChoicesShareFirstToken(a, b)) warnings.Add("choices_share_first_token");
            if (IsGenericChoice(a)) warnings.Add("choice_a_generic");
            if (IsGenericChoice(b)) warnings.Add("choice_b_generic");
            if (HasLatinLeakage(a)) warnings.Add("latin_leakage_choice_a");
            if (HasLatinLeakage(b)) warnings.Add("latin_leakage_choice_b");

            // Concrete-noun grounding. Stricter than the verb-stem
            // grounding done by ChoiceGroundedInBody (which we keep for
            // the continuation cross-turn check). A choice that contains
            // a concrete noun stem absent from the body invented an
            // object — the existing verb-only overlap is not enough.
            if (!ChoiceNounsAppearInBody(a, body))
                warnings.Add("choice_a_noun_not_in_body");
            if (!ChoiceNounsAppearInBody(b, body))
                warnings.Add("choice_b_noun_not_in_body");
        }

        // Continuation-specific checks: only when we have a previous
        // turn's body AND a selected choice label.
        if (input.PreviousBody is not null && input.SelectedChoiceLabel is not null)
        {
            if (!ChoiceGroundedInBody(input.SelectedChoiceLabel, body))
                warnings.Add("continuation_ignores_selected_choice");

            var overlap = FirstSentenceRecapOverlap(input.PreviousBody, body);
            if (overlap >= RecapOverlapThreshold)
                warnings.Add($"recap_overlap_high:{overlap:F2}");
        }

        return warnings;
    }

    /// <summary>
    /// Aggregate a session into five 0..100 scores plus a PASS/WARN/FAIL
    /// verdict. The math is intentionally simple — each unique warning
    /// docks fixed points, capped at 0. Tunable from one place.
    /// </summary>
    public static SessionVerdict EvaluateSession(IReadOnlyList<IReadOnlyList<string>> perTurnWarnings)
    {
        int armenianHits = 0, logicHits = 0, choiceHits = 0,
            suitabilityHits = 0, continuationHits = 0;
        foreach (var turn in perTurnWarnings)
        {
            foreach (var raw in turn)
            {
                var w = raw.Split(':')[0];
                switch (w)
                {
                    case "armenian_ratio_low":
                    case "latin_leakage_body":
                    case "cyrillic_leakage_body":
                    case "latin_leakage_choice_a":
                    case "latin_leakage_choice_b":
                        armenianHits++;
                        break;
                    case "body_too_short":
                    case "body_too_long":
                        suitabilityHits++;
                        break;
                    case "choice_a_missing":
                    case "choice_b_missing":
                    case "choices_identical":
                    case "choices_share_first_token":
                    case "choice_a_generic":
                    case "choice_b_generic":
                    case "choice_a_too_long":
                    case "choice_b_too_long":
                    case "choice_a_noun_not_in_body":
                    case "choice_b_noun_not_in_body":
                        choiceHits++;
                        break;
                    case "continuation_ignores_selected_choice":
                        continuationHits++;
                        logicHits++; // ignored-choice is also a logic break
                        break;
                    case "recap_overlap_high":
                    case "choices_repeated_from_earlier_turn":
                    case "choice_repeated_from_earlier_turn":
                        continuationHits++;
                        break;
                }
            }
        }

        int Clamp(int v) => v < 0 ? 0 : v > 100 ? 100 : v;
        var v = new SessionVerdict
        {
            ArmenianQualityScore = Clamp(100 - armenianHits * 20),
            StoryLogicScore = Clamp(100 - logicHits * 25),
            ChildSuitabilityScore = Clamp(100 - suitabilityHits * 20),
            ChoiceQualityScore = Clamp(100 - choiceHits * 15),
            ContinuationCoherenceScore = Clamp(100 - continuationHits * 20)
        };
        // FAIL when a child-safety-adjacent or core-logic dimension
        // drops below 60. WARN when any dimension drops below 80 (the
        // "story departed from what the child picked" signal lives in
        // StoryLogicScore and lands here even when the other axes are
        // clean). PASS only when every axis is ≥ 80.
        v.OverallVerdict = v switch
        {
            { ArmenianQualityScore: < 60 } or { StoryLogicScore: < 60 }
                or { ChildSuitabilityScore: < 60 } => "FAIL",
            _ when v.MinScore < 80 => "WARN",
            _ => "PASS"
        };
        return v;
    }

    // ---- helpers (also referenced by tests via Compile Include) ----

    public static HashSet<string> ExtractArmenianTokens(string? s, int minLen)
    {
        var set = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(s)) return set;
        foreach (var raw in s.Split(
            new[]
            {
                ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':',
                '։', '՞', '՜', '՝', '՛',
                '«', '»', '"', '(', ')', '—', '-', '՝'
            },
            StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = raw.TrimEnd(ArmenianTrailingPunct);
            if (clean.Length < minLen) continue;
            bool allArmenian = true;
            foreach (var c in clean)
            {
                if (c < '԰' || c > '֏') { allArmenian = false; break; }
            }
            if (!allArmenian) continue;
            set.Add(clean.ToLowerInvariant());
        }
        return set;
    }

    public static HashSet<string> ExtractArmenianStems(string? s, int minLen)
    {
        var tokens = ExtractArmenianTokens(s, minLen);
        var stems = new HashSet<string>();
        foreach (var t in tokens) stems.Add(ArmenianStem(t));
        return stems;
    }

    // Light Armenian stemmer — lightweight benchmark / evaluator
    // helper, NOT a full Armenian morphological analyzer. It strips
    // a closed list of common verb / noun endings and handles the
    // «ն»/«ց» verb-root alternation. Behavior is deliberately
    // conservative: the default length guard
    // `stem.Length - e.Length >= 4` keeps stems ≥ 4 chars, so a
    // bare 4-char noun is never truncated.
    //
    // Per-ending exception: the three short 2-char noun-case
    // endings in <see cref="ShortStemAllowedEndings"/> may strip
    // down to a 3-char stem instead of 4. This lets common short
    // Armenian concrete nouns normalize: «ծառին» → «ծառ»,
    // «ուղին» → «ուղ», «քարով» → «քար», «քարից» → «քար».
    // Without this exception, body «ծառերի» (stems to «ծառեր»)
    // and choice «ծառին» (would stay as «ծառին») wouldn't share
    // any stem, producing a spurious noun_not_in_body warning.
    //
    // Suffix-list contract (do not reorder without re-running tests):
    //   * Endings are tried longest-first. The loop stops on the
    //     first match.
    //   * Whenever ending A is a strict suffix of ending B (e.g.
    //     «ին» is a suffix of «ոջին»; «ը» is a suffix of «ոջը»),
    //     the LONGER one MUST appear first or the longer form will
    //     never be stripped.
    //
    // Noun endings covered (length-grouped):
    //   5: ներին / ներով / ներից
    //   4: ները / ների / ոջին
    //   3: ներ / ոջը / ում
    //   2: ին / ից / ով / ոջ       (ին / ից / ով may strip to 3-char stem)
    //   1: ի / ը                   (length-gated to words >= 5 chars)
    //
    // Known limitations (acceptable for the deterministic loop
    // evaluator, NOT for production NLP):
    //   * Diminutive «-իկ» (e.g. թռչուն vs թռչունիկ) is not
    //     normalized. Different surface forms remain distinct stems.
    //   * Synonyms are not normalized — «տուփ» and «արկղ» both mean
    //     "box" but produce different stems. That requires embeddings
    //     or a thesaurus, out of scope for this stemmer.

    // The three short 2-char noun-case endings that are allowed to
    // strip down to a 3-char stem. Picked deliberately narrow:
    //   * «ին» — dative singular (ծառին / ուղին / թռչունին)
    //   * «ից» — ablative singular (քարից / ծառից)
    //   * «ով» — instrumental singular (քարով / ձեռքով)
    // «ոջ» is NOT in the set: the kid-noun population of 5-char
    // «-ոջ» words is essentially empty, and «ընկերոջ» (6) already
    // strips fine under the default 4-char rule.
    private static readonly HashSet<string> ShortStemAllowedEndings =
        new(StringComparer.Ordinal) { "ին", "ից", "ով" };

    private static int MinResultLengthFor(string ending) =>
        ShortStemAllowedEndings.Contains(ending) ? 3 : 4;

    public static string ArmenianStem(string? token)
    {
        if (string.IsNullOrEmpty(token)) return token ?? "";
        var lower = token.ToLowerInvariant();
        if (lower.Length < 4) return lower;
        foreach (var c in lower)
        {
            if (c < '԰' || c > '֏') return lower;
        }

        string[] verbEndings = { "ալով", "ենք", "անք", "ում", "ավ", "եց", "ալ", "ել", "իր" };
        string[] nounEndings =
        {
            // Length 5 (plural cases — must precede 4-char plural forms)
            "ներին", "ներով", "ներից",
            // Length 4 (must precede 2-3 char endings that are their suffixes)
            "ները", "ների", "ոջին",
            // Length 3
            "ներ", "ոջը", "ում",
            // Length 2
            "ին", "ից", "ով", "ոջ",
            // Length 1 (must come last — they are suffixes of nearly everything above)
            "ի", "ը"
        };

        string stem = lower;
        foreach (var endings in new[] { verbEndings, nounEndings })
        {
            bool stripped = false;
            foreach (var e in endings)
            {
                if (stem.EndsWith(e, StringComparison.Ordinal)
                    && stem.Length - e.Length >= MinResultLengthFor(e))
                {
                    stem = stem[..^e.Length];
                    stripped = true;
                    break;
                }
            }
            if (stripped) break;
        }

        if (stem.Length >= 5)
        {
            var last = stem[^1];
            if (last == 'ն' || last == 'ց')
                stem = stem[..^1];
        }
        return stem;
    }

    private static string? FirstArmenianToken(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        foreach (var raw in s.Trim().Split(
            new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = raw.TrimEnd(ArmenianTrailingPunct);
            if (clean.Length == 0) continue;
            bool hasArmenian = false;
            foreach (var c in clean)
                if (c >= '԰' && c <= '֏') { hasArmenian = true; break; }
            if (hasArmenian) return clean.ToLowerInvariant();
        }
        return null;
    }

    private static string FirstSentence(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var idx = s.IndexOf('։'); // ։
        if (idx >= 0) return s[..(idx + 1)];
        idx = s.IndexOf('.');
        if (idx >= 0) return s[..(idx + 1)];
        return s;
    }
}

public sealed class TurnEvaluationInput
{
    public required string Body { get; init; }
    public string? ChoiceA { get; init; }
    public string? ChoiceB { get; init; }
    public bool HasChoices { get; init; }
    public string? PreviousBody { get; init; }
    public string? SelectedChoiceLabel { get; init; }
}

public sealed class SessionVerdict
{
    public int ArmenianQualityScore { get; set; }
    public int StoryLogicScore { get; set; }
    public int ChildSuitabilityScore { get; set; }
    public int ChoiceQualityScore { get; set; }
    public int ContinuationCoherenceScore { get; set; }
    public string OverallVerdict { get; set; } = "PASS";
    public double AverageScore =>
        (ArmenianQualityScore + StoryLogicScore + ChildSuitabilityScore
         + ChoiceQualityScore + ContinuationCoherenceScore) / 5.0;

    public int MinScore => new[]
    {
        ArmenianQualityScore, StoryLogicScore, ChildSuitabilityScore,
        ChoiceQualityScore, ContinuationCoherenceScore
    }.Min();
}

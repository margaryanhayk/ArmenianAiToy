namespace ArmenianAiToy.Application.Helpers;

/// <summary>
/// The complete value space of <c>POST /api/devices/voice-intent</c>'s single
/// response field. Closed by construction — this is what keeps that endpoint
/// from ever becoming a relay for model-authored text.
/// </summary>
public static class VoiceIntents
{
    public const string Story = "story";
    public const string Game = "game";
    public const string Riddle = "riddle";
    public const string Curiosity = "curiosity";
    public const string Calm = "calm";
    public const string Yes = "yes";
    public const string No = "no";

    /// <summary>Not understood, refused by a gate, or over the cost cap — the
    /// toy treats all three identically, so they answer identically.</summary>
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All =
        [Story, Game, Riddle, Curiosity, Calm, Yes, No, Unknown];
}

/// <summary>
/// WHY a voice-intent turn resolved the way it did — a metric tag only, and
/// deliberately NOT a response field.
///
/// <para>
/// Ten distinct paths through <c>POST /api/devices/voice-intent</c> return
/// <see cref="VoiceIntents.Unknown"/>, and from the toy they are byte-identical:
/// HTTP 200 <c>{"intent":"unknown"}</c>. That is correct for the child (the
/// firmware's "I didn't understand" handling is the graceful default for all of
/// them) and was useless for an operator: an owner whose «Արի խաղանք» came back
/// unknown had no way to tell a tripped cost cap from a parent-disabled mode
/// from a genuine classifier miss, because most of those paths wrote nothing at
/// all. This tag separates them on the metric while the wire stays closed.
/// </para>
///
/// <para>
/// The value space is CLOSED and small — eleven compile-time constants, no
/// free-form strings, no per-device or per-transcript values — so it is safe
/// under the no-high-cardinality invariant in CLAUDE.md § Metrics, exactly as
/// the <c>outcome</c> tag on <c>aat_story_qa_turn_total</c> is. Do NOT add the
/// transcript, a confidence score, a device id, or a moderation category here.
/// </para>
/// </summary>
public static class VoiceIntentReasons
{
    /// <summary>A parent has the toy paused.</summary>
    public const string GatedPaused = "gated_paused";

    /// <summary>Inside the device's bedtime window.</summary>
    public const string GatedBedtime = "gated_bedtime";

    /// <summary>This device is over its own daily OpenAI cost cap.</summary>
    public const string CostCap = "cost_cap";

    /// <summary>The fleet-wide daily ceiling (kill-switch) is over.</summary>
    public const string CostCapGlobal = "cost_cap_global";

    /// <summary>Speech-to-text threw.</summary>
    public const string SttFailed = "stt_failed";

    /// <summary>Speech-to-text returned nothing usable.</summary>
    public const string SttEmpty = "stt_empty";

    /// <summary>Input moderation blocked the transcript.</summary>
    public const string ModerationBlocked = "moderation_blocked";

    /// <summary>A yes/no offer whose answer was neither.</summary>
    public const string YesNoUnclear = "yesno_unclear";

    /// <summary>The keyword classifier matched no mode.</summary>
    public const string NoMatch = "no_match";

    /// <summary>A mode was matched, but a parent has it switched off.</summary>
    public const string ModeDisabled = "mode_disabled";

    /// <summary>A real intent was resolved and returned.</summary>
    public const string Matched = "matched";

    public static readonly IReadOnlyList<string> All =
    [
        GatedPaused, GatedBedtime, CostCap, CostCapGlobal, SttFailed, SttEmpty,
        ModerationBlocked, YesNoUnclear, NoMatch, ModeDisabled, Matched,
    ];
}

/// <summary>
/// What the toy just asked, so the transcription can be biased toward the
/// answers that are actually possible. A one-word reply from a five-year-old
/// is where an STT prompt earns its keep.
/// </summary>
public static class VoiceIntentExpectations
{
    /// <summary>«Ի՞նչ անենք» — expect a mode name.</summary>
    public const string Mode = "mode";

    /// <summary>«Ուզո՞ւմ ես լսել …» — expect yes or no.</summary>
    public const string YesNo = "yesno";

    public const string ModeBiasPrompt = "հեքիաթ, խաղ, հանելուկ, հարց, քնել";

    public const string YesNoBiasPrompt = "այո, ոչ, հա, չէ, ուզում եմ, չեմ ուզում";
}

/// <summary>A child's yes/no answer to a spoken offer.</summary>
public enum YesNo
{
    /// <summary>No usable signal — the caller re-asks once, then defaults.</summary>
    Unknown,
    Yes,
    No,
}

/// <summary>
/// Pure-function detection of a child's YES / NO answer in Armenian, for the
/// welcome flow's spoken story offer («Ուզո՞ւմ ես լսել «X»-ը։»).
///
/// <para>
/// Deliberately NOT part of <see cref="ChoiceNormalizer"/>: that helper
/// explicitly refuses to map «այո» / «ոչ», because in a two-option story
/// branch a bare yes is ambiguous about WHICH option was meant. Here the
/// question really is binary, so a bare yes is the whole answer.
/// </para>
///
/// <para>
/// Also deliberately not a call into <see cref="ModeDetector"/>'s private
/// normalizer. That file sits on the chat gate's hot path and is HIGH risk;
/// the ten lines of normalization below are duplicated rather than widening
/// its surface. Keep the two in step: stripping the Armenian intra-word
/// marks ՞ ՛ ՜ is load-bearing, because a child's «Այո՞» would otherwise
/// never match a plain «այո».
/// </para>
///
/// <para>
/// No model call, ever. This runs on a transcript that has already passed
/// input moderation, and returns one of three values.
/// </para>
/// </summary>
public static class WelcomeIntentDetector
{
    /// <summary>Phrases that are a refusal even though they CONTAIN an
    /// affirmative word. Matched — and removed — before anything else, so
    /// «չեմ ուզում» can never be read as the «ուզում» inside it.</summary>
    private static readonly string[] NegatedPhrases =
    [
        "չեմ ուզում",     // "I don't want"
        "չեմ ցանկանում",  // "I do not wish"
        "չեմ ուզե",       // clipped form Whisper often returns
        "չուզեմ",         // colloquial contraction
        "ոչ թե",          // "not that one"
    ];

    private static readonly string[] NoTokens =
    [
        "ոչ",   // "no"
        "չէ",   // "nope"
        "չե",   // same, with the final է dropped — a common STT result
        "չեմ",  // bare "I don't"
        "voch", "che", "no",   // Latin transliterations Whisper sometimes emits
    ];

    private static readonly string[] YesTokens =
    [
        "այո",  // "yes"
        "հա",   // colloquial "yeah"
        "հաա",  // drawn-out child forms of the same
        "հաաա",
        "լավ",  // "ok / fine" — an answer, not just an adjective, in this context
        "ահա",  // "there you go / yeah"
        "ayo", "ha", "yes", "ok",   // Latin transliterations
    ];

    /// <summary>Multi-word affirmatives, matched on the whole string because
    /// they do not survive tokenisation.</summary>
    private static readonly string[] YesPhrases =
    [
        "ուզում եմ",   // "I want (to)"
        "այո ուզում",  // "yes, want"
        "դե լավ",      // "well, ok"
    ];

    /// <summary>
    /// Classifies a transcript as <see cref="YesNo.Yes"/>,
    /// <see cref="YesNo.No"/>, or <see cref="YesNo.Unknown"/>.
    /// <para>
    /// A transcript carrying BOTH a clear yes and a clear no (after negated
    /// phrases are accounted for) is <see cref="YesNo.Unknown"/>, not a
    /// guess — the caller re-asks once and then just starts a story, which is
    /// a better outcome for a child than acting on a coin flip.
    /// </para>
    /// </summary>
    public static YesNo DetectYesNo(string? message)
    {
        var text = Normalize(message);
        if (text.Length == 0) return YesNo.Unknown;

        var hasNo = false;
        foreach (var phrase in NegatedPhrases)
        {
            if (!text.Contains(phrase, StringComparison.Ordinal)) continue;
            hasNo = true;
            // Remove it so its affirmative-looking tail («ուզում») cannot
            // then register as a yes.
            text = text.Replace(phrase, " ", StringComparison.Ordinal);
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (Array.IndexOf(NoTokens, token) >= 0) { hasNo = true; break; }
        }

        var hasYes = false;
        foreach (var token in tokens)
        {
            if (Array.IndexOf(YesTokens, token) >= 0) { hasYes = true; break; }
        }
        if (!hasYes)
        {
            foreach (var phrase in YesPhrases)
            {
                if (!text.Contains(phrase, StringComparison.Ordinal)) continue;
                hasYes = true;
                break;
            }
        }

        if (hasYes && hasNo) return YesNo.Unknown;
        if (hasNo) return YesNo.No;
        return hasYes ? YesNo.Yes : YesNo.Unknown;
    }

    /// <summary>Lowercases, strips the Armenian intra-word marks ՞ ՛ ՜, and
    /// flattens every punctuation mark to a space so a one-word answer like
    /// «Այո՛։» tokenises to «այո». Mirrors ModeDetector.NormalizeForMatch and
    /// then goes one step further (that one only needs substring matching;
    /// this one tokenises).</summary>
    private static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var lower = message.ToLowerInvariant();
        var buffer = new char[lower.Length];
        var length = 0;
        foreach (var c in lower)
        {
            // Armenian intra-word marks carry no lexical content here.
            if (c is '՞' or '՛' or '՜') continue;
            buffer[length++] = char.IsLetterOrDigit(c) ? c : ' ';
        }
        return new string(buffer, 0, length).Trim();
    }
}

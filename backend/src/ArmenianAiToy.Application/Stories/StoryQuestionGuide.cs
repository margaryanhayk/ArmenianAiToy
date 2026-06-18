namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// Per-story metadata for the bounded in-story Q&A surface (MODES.md
/// §1A: "GPT owns only the bounded in-story Q&A answer content").
/// A guide carries everything the prompt builder needs to ground a
/// child's interruption question in the ACTIVE story: who is in it,
/// what it is about, what each segment covers, and the story's
/// "essence" — the interpretation rules that keep answers gentle and
/// non-shaming. Guides are DATA about a story; they never contain or
/// replace the story text itself, which is always served verbatim.
/// <para>
/// Like the library/tracker classes, this type ships runtime-dead:
/// no DI registration, no ChatService wiring. The wiring slice is
/// separate and gated on human approval.
/// </para>
/// </summary>
/// <param name="StoryId">Library id this guide describes.</param>
/// <param name="Characters">Character list — Armenian names with a
/// short English gloss (prompt instructions are English by project
/// decision; child-facing output is Armenian).</param>
/// <param name="StorySummary">Short whole-story summary.</param>
/// <param name="SegmentSummaries">One short summary per segment,
/// index-aligned with <see cref="CuratedStory.Segments"/>.</param>
/// <param name="EssenceNotes">Interpretation rules: humor framing,
/// what is real vs misunderstood vs a trick inside the story, and
/// what the answer must never teach.</param>
public sealed record StoryQuestionGuide(
    string StoryId,
    IReadOnlyList<string> Characters,
    string StorySummary,
    IReadOnlyList<string> SegmentSummaries,
    IReadOnlyList<string> EssenceNotes)
{
    /// <summary>One short CHILD-FACING Armenian re-anchor line per
    /// segment, index-aligned with <see cref="CuratedStory.Segments"/>.
    /// Spoken on return from a longer in-story question to re-establish
    /// the scene ("remember, we were…") before narration resumes — the
    /// goal-reactivation step (Tier 2 of the smooth-re-entry design).
    /// Empty when a story has no authored recaps; the recap is then
    /// simply skipped (the pause + bridge + micro-rewind still apply).</summary>
    public IReadOnlyList<string> SegmentRecaps { get; init; } = [];
}

/// <summary>
/// Registry of authored story guides. Adding a story to the Q&A
/// surface means adding a guide here (a reviewed data change) — the
/// prompt builder degrades gracefully for stories without one.
/// </summary>
internal static class StoryQuestionGuides
{
    /// <summary>
    /// Guide for «Անբան Հուռին» (id "anban-huri") — the exact classic
    /// text kept verbatim by product decision. The guide encodes HOW
    /// to talk about the story with a 4–7-year-old; it never edits
    /// the story.
    /// </summary>
    public static StoryQuestionGuide AnbanHuri { get; } = new(
        StoryId: "anban-huri",
        Characters:
        [
            "Հուռի (Անբան Հուռի) — the lazy, naive girl the tale is about",
            "մայրը / զոքանչը — Huri's cunning mother (the husband's mother-in-law)",
            "երիտասարդ վաճառականը — the young merchant who marries Huri",
            "գորտերը (Փեփել, Կեկել) — the frogs by the river; their croaking sounds like names",
            "բզեզը — an ordinary beetle the mother pretends is her aunt",
        ],
        StorySummary:
            "An old Armenian comic folk-style tale. Lazy Huri avoids all work; her mother " +
            "praises her as a great craftswoman, so a young merchant marries her and leaves " +
            "her cotton to spin. Huri misunderstands croaking frogs, gives them the cotton, " +
            "and later luckily finds a piece of gold in the river. The husband believes the " +
            "cotton was sold and celebrates. To keep the secret, Huri's cunning mother greets " +
            "a beetle as her aunt who 'shrank from working too much' — and the frightened " +
            "husband forbids Huri to ever work again. The tale laughs at laziness through " +
            "exaggeration.",
        SegmentSummaries:
        [
            "Huri (Հուռի) is introduced: a lazy, unskilled girl who sits idle all day and sings a little song about putting work off.",
            "Neighbors nickname her Lazy Huri (Անբան Հուռի), while her mother walks around praising her as a golden-fingered, hard-working craftswoman.",
            "A young merchant hears the praise, marries Huri, and before leaving on a trading trip gives her many loads of cotton to card and spin.",
            "By the river Huri hears frogs croaking ('Pepel… Kekel…'), thinks they agree to spin for her, and happily throws all the cotton into the river.",
            "Days later there is no yarn; Huri decides the green moss on the stones is a carpet the frogs wove, steps into the water demanding payment, and luckily finds a piece of gold, which she brings home just as her husband returns.",
            "The husband sees the gold, believes Huri sold the cotton, joyfully thanks her mother with gifts and praise, and throws a feast.",
            "The cunning mother fears the truth will come out; when a beetle buzzes into the room she bows and greets it warmly as her long-lost aunt.",
            "The astonished husband asks what she means; the mother claims the 'aunt' worked so hard she shrank smaller and smaller until she became a beetle — their whole family shrinks from too much work.",
            "The frightened husband forbids Huri to touch any work at all, so that she will not turn into a beetle like the 'aunt'.",
        ],
        EssenceNotes:
        [
            "This is an old Armenian comic folk-style tale: it works through exaggeration and gentle humor, not realism.",
            "Huri is shown as lazy and naive, comically so. Never shame the child, and never say Huri is a bad person — the tale laughs kindly at laziness.",
            "The frogs never really spin or weave anything. Huri misunderstands their croaking; the gold is a lucky find, not payment from frogs.",
            "The beetle is just a beetle. The 'aunt who shrank from working too much' is a trick the mother invents so Huri will not be given work; people do not really turn into beetles.",
            "Explain gently what happens in the story. Do not teach lying, laziness, name-calling, or shame; do not moralize heavily — one light, warm thought at most.",
            "Huri's only character traits are lazy (ծույլ) and naive/gullible (միամիտ). Never describe her as suspicious (կասկածոտ) or distrustful — that is the opposite of who she is.",
            "People do not really turn into beetles. The beetle is just a beetle; if a child asks, say so plainly to reassure them.",
            "Huri did not get help from the frogs. She misheard their croaking (սխալ հասկացավ) and only thought they were helping — never say she got lost (մոլորվեց) or that they really spun or wove.",
            "When a child asks why Huri is called a lazy/silly name, keep the warm reframe: we don't hurt people with names; the story is only laughing kindly at her laziness.",
            "The young merchant MARRIED Huri (ամուսնացավ Հուռիի հետ). He never bought, purchased, or paid for her — never phrase it as buying a person.",
            "Child-simple meanings of the old/dialect words a child may ask about (use these, do not invent): մաստակ = a gum you chew but do not eat; մտիկ անել = to look at / watch; լիդր = a soft lump of combed wool or cotton, ready to be spun into thread; գզել = to fluff and comb the wool/cotton; մանել = to twist the fibers into thread; զոքանչ = the wife's mother (the husband's mother-in-law).",
        ])
    {
        // Tier 2 re-anchor lines, one per segment (index-aligned with
        // Segments). Authored + validated by armenian-story-master
        // (2026-06-16): one gentle "remember where we are" line per scene,
        // spoken on a longer interruption — after the bridge, before
        // narration resumes — to re-establish the scene.
        SegmentRecaps =
        [
            "Հիշո՞ւմ ես՝ ծույլ Հուռին ամբողջ օրը նստած էր ու երգում էր իր երգը։",
            "Մենք հասել էինք այնտեղ, որտեղ հարևանները նրան ծույլ էին ասում, իսկ մայրիկը գովում էր։",
            "Հիշո՞ւմ ես՝ երիտասարդ վաճառականն ամուսնացավ Հուռիի հետ ու բամբակ թողեց, որ մանի։",
            "Մենք կանգ էինք առել գետի մոտ, որտեղ Հուռին գորտերին լսեց ու բամբակը ջուրը գցեց։",
            "Հիշո՞ւմ ես՝ Հուռին ջուրը մտավ, թել չգտավ, բայց մի կտոր ոսկի գտավ ու տուն բերեց։",
            "Մենք այնտեղ էինք, որտեղ ամուսինը ոսկին տեսավ, ուրախացավ ու խնջույք սարքեց։",
            "Հիշո՞ւմ ես՝ սենյակ թռավ մի բզեզ, ու խորամանկ մայրիկը նրան իր մորաքույրն անվանեց։",
            "Մենք հասել էինք այնտեղ, որտեղ մայրիկն ասում էր՝ մորաքույրն աշխատելուց փոքրացավ ու բզեզ դարձավ։",
            "Հիշո՞ւմ ես՝ վախեցած ամուսինն արգելեց Հուռիին որևէ գործ անել։",
        ],
    };

    /// <summary>Looks up the guide for a story id; null when the
    /// story has no authored guide yet.</summary>
    public static StoryQuestionGuide? TryGet(string storyId) =>
        storyId == AnbanHuri.StoryId ? AnbanHuri : null;
}

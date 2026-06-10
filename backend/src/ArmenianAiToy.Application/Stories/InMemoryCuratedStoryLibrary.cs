namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// First curated story library: hardcoded, human-reviewed Armenian
/// stories. Every text below was reviewed by the armenian-story-master
/// review pass (Eastern Armenian, ages 4–7, TTS-friendly, zero Latin
/// codepoints, zero digits, no folklore, bedtime-safe) and must be
/// served byte-identical — do not "fix", reflow, or re-punctuate it
/// without a fresh linguistic review. Authoring rules are pinned by
/// CuratedStoryAuthoringRulesTests over every child-facing string.
/// </summary>
public sealed class InMemoryCuratedStoryLibrary : ICuratedStoryLibrary
{
    /// <summary>Id of the first launch story (also the deterministic
    /// default).</summary>
    public const string LittleCloudId = "little-cloud";

    /// <summary>Id of the second launch story.</summary>
    public const string HedgehogAppleId = "hedgehog-apple";

    private static readonly CuratedStory LittleCloud = new(
        Id: LittleCloudId,
        Title: "Փոքրիկ ամպիկը",
        MinAge: 4,
        MaxAge: 7,
        Tone: "warm",
        Segments:
        [
            new CuratedStorySegment(0,
                "Երկնքում ապրում էր մի փոքրիկ ամպիկ։ Նա շատ էր սիրում թռչել քամու հետ։ Մի օր ամպիկը տեսավ մի փոքրիկ ծաղիկ։ Ծաղիկը ծարավ էր։"),
            new CuratedStorySegment(1,
                "Ամպիկը մոտեցավ ծաղկին։ Նա մի քիչ անձրև մաղեց։ Ծաղիկն ուրախացավ ու բացեց իր թերթիկները։"),
            new CuratedStorySegment(2,
                "Ծաղիկն ասաց՝ շնորհակալ եմ, ամպի՛կ ջան։ Ամպիկը ժպտաց ու թռավ երկինք։ Նրանք դարձան լավ ընկերներ։"),
        ],
        ReflectionText: "Երբ օգնում ենք ուրիշներին, մեր սիրտն էլ է ուրախանում։",
        ReflectionQuestions: ["Իսկ դու ո՞ւմ ես սիրում օգնել։"],
        BedtimeSafe: true);

    private static readonly CuratedStory HedgehogApple = new(
        Id: HedgehogAppleId,
        Title: "Ոզնիկն ու խնձորը",
        MinAge: 4,
        MaxAge: 7,
        Tone: "warm",
        Segments:
        [
            new CuratedStorySegment(0,
                "Անտառում ապրում էր մի փոքրիկ ոզնիկ։ Մի առավոտ նա գտավ մի մեծ կարմիր խնձոր։ Խնձորը շատ համով էր երևում։ Բայց խնձորը ծանր էր, շատ ծանր։"),
            new CuratedStorySegment(1,
                "Ոզնիկը կանչեց իր ընկեր նապաստակին։ Նրանք միասին գլորեցին խնձորը։ Խնձորը դանդաղ գլորվեց մինչև ոզնիկի տնակը։"),
            new CuratedStorySegment(2,
                "Ոզնիկն ու նապաստակը միասին կերան խնձորը։ Խնձորն իսկապես շատ քաղցր էր։ Հետո նրանք նստեցին ծառի տակ։ Անտառում խաղաղ էր ու հանգիստ։"),
        ],
        ReflectionText: "Ընկերոջ հետ նույնիսկ ծանր խնձորը հեշտ է գլորվում։",
        ReflectionQuestions: ["Իսկ դու ի՞նչ ես սիրում անել ընկերոջդ հետ։"],
        BedtimeSafe: true);

    private static readonly IReadOnlyList<CuratedStory> Stories = [LittleCloud, HedgehogApple];

    public CuratedStory? GetById(string id) =>
        Stories.FirstOrDefault(s => s.Id == id);

    public CuratedStory SelectDefault() => LittleCloud;

    public IReadOnlyList<CuratedStory> ListAvailable() => Stories;
}

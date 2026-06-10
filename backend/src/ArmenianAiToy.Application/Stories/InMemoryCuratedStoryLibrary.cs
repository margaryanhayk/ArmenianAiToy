namespace ArmenianAiToy.Application.Stories;

/// <summary>
/// First curated story library: a single hardcoded, human-reviewed
/// Armenian story. Text below was reviewed by the armenian-story-master
/// review pass (Eastern Armenian, ages 4–7, TTS-friendly, zero Latin
/// codepoints, zero digits, no folklore, bedtime-safe) and must be
/// served byte-identical — do not "fix", reflow, or re-punctuate it
/// without a fresh linguistic review.
/// </summary>
public sealed class InMemoryCuratedStoryLibrary : ICuratedStoryLibrary
{
    /// <summary>Id of the single launch story.</summary>
    public const string LittleCloudId = "little-cloud";

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

    private static readonly IReadOnlyList<CuratedStory> Stories = [LittleCloud];

    public CuratedStory? GetById(string id) =>
        Stories.FirstOrDefault(s => s.Id == id);

    public CuratedStory SelectDefault() => LittleCloud;

    public IReadOnlyList<CuratedStory> ListAvailable() => Stories;
}

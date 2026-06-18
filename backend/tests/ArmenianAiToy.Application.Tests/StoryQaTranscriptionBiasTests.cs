using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Unit tests for <see cref="StoryQaController.BuildTranscriptionBias"/> —
/// the Whisper biasing context that primes STT toward the current story
/// scene (the gap-6 backend lever for a child's short / single-word
/// Armenian).
/// </summary>
public class StoryQaTranscriptionBiasTests
{
    private static CuratedStory StoryWith(params string[] segmentTexts) =>
        new(
            Id: "test-story",
            Title: "Փորձ",
            MinAge: 4,
            MaxAge: 7,
            Tone: "warm",
            Segments: segmentTexts
                .Select((t, i) => new CuratedStorySegment(i, t))
                .ToList(),
            ReflectionText: "",
            ReflectionQuestions: [],
            BedtimeSafe: true);

    [Fact]
    public void Segment0_ReturnsThatSegmentText_NoPrefix()
    {
        var story = StoryWith("Փոքրիկ ամպիկը երկնքում էր։", "Քամին փչեց։");
        var bias = StoryQaController.BuildTranscriptionBias(story, 0);
        Assert.Equal("Փոքրիկ ամպիկը երկնքում էր։", bias);
    }

    [Fact]
    public void LaterSegment_PrependsPreviousSegment_CurrentSceneAtTail()
    {
        var story = StoryWith("Առաջին տեսարան։", "Երկրորդ տեսարան։", "Երրորդ տեսարան։");
        var bias = StoryQaController.BuildTranscriptionBias(story, 2);
        // Previous segment for extra vocabulary, current scene last.
        Assert.Equal("Երկրորդ տեսարան։ Երրորդ տեսարան։", bias);
        Assert.EndsWith("Երրորդ տեսարան։", bias);
    }

    [Fact]
    public void OutOfRangeIndex_IsClamped()
    {
        var story = StoryWith("Մեկ։", "Երկու։");
        Assert.Equal("Մեկ։ Երկու։", StoryQaController.BuildTranscriptionBias(story, 99));
        Assert.Equal("Մեկ։", StoryQaController.BuildTranscriptionBias(story, -3));
    }

    [Fact]
    public void OverLongContext_IsCappedToTail()
    {
        // Two long segments; the bias must keep the END (current scene),
        // capped to the 600-char budget.
        var prev = new string('ա', 500);
        var current = new string('բ', 400);
        var story = StoryWith(prev, current);
        var bias = StoryQaController.BuildTranscriptionBias(story, 1)!;
        Assert.Equal(600, bias.Length);
        // Tail kept: the whole current segment plus the trailing slice of prev.
        Assert.EndsWith(current, bias);
        Assert.DoesNotContain('ա' + "", bias[^current.Length..]); // current part is all 'բ'
    }

    [Fact]
    public void NoSegments_ReturnsNull()
    {
        var story = StoryWith();
        Assert.Null(StoryQaController.BuildTranscriptionBias(story, 0));
    }
}

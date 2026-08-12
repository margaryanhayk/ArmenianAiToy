using ArmenianAiToy.Application.Interfaces;

namespace ArmenianAiToy.Application.Stories;

/// <summary>Outcome of one reflection-dialogue reaction. <see cref="Text"/>
/// is the ONLY member a caller may TTS — either a reaction that passed every
/// <see cref="StoryAnswerFilter"/> check or null, in which case the caller
/// MUST fall back to its deterministic acknowledgement line. The rejection
/// fields exist for logging/metrics, never for output.</summary>
public sealed record ReflectionReaction(
    string? Text,
    StoryAnswerRejection FirstRejection,
    StoryAnswerRejection? RetryRejection,
    /// <summary>Characters of prompt actually sent to the model, summed
    /// across the first call and the repair retry. Before this existed the
    /// reflection reaction was a billed model call the cost meter did not
    /// record AT ALL — the reflection path counted speech-to-text only.
    /// Defaults to 0 so existing constructions keep compiling.</summary>
    long PromptCharsSent = 0);

/// <summary>
/// The after-story reflection dialogue's ONE model surface (owner request
/// 2026-08-03): after the toy asks a story's reflection question and the
/// child answers, this composes a short warm REACTION to what the child
/// actually said — replacing the content-blind rotated acknowledgement.
/// <para>
/// Safety shape is a clone of <see cref="LibraryStoryQuestionService"/>
/// (MODES.md §1A discipline): bounded English system prompt grounded in the
/// story, ONE model call, deterministic <see cref="StoryAnswerFilter"/>
/// validation, one stricter repair retry, then null — the caller's
/// deterministic acknowledgement is the fallback, so a child never hears an
/// error and never hears unvalidated model text. Input/output MODERATION is
/// the caller's job (the controller runs both, same as the in-story Q&amp;A
/// path); this class owns prompt + validation only.
/// </para>
/// </summary>
public sealed class ReflectionDialogueService
{
    private readonly IAiChatClient _ai;

    public ReflectionDialogueService(IAiChatClient ai)
    {
        ArgumentNullException.ThrowIfNull(ai);
        _ai = ai;
    }

    /// <summary>Reacts to the child's answer to
    /// <c>story.ReflectionQuestions[questionIndex]</c>. Returns a
    /// null-Text result when the model output (and its one repair retry)
    /// failed validation or the model call failed — the caller then uses
    /// its deterministic acknowledgement.</summary>
    public async Task<ReflectionReaction> ReactAsync(
        CuratedStory story, int questionIndex, string childAnswer)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (questionIndex < 0 || questionIndex >= story.ReflectionQuestions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(questionIndex));
        }

        var question = story.ReflectionQuestions[questionIndex];

        var firstPrompt = BuildSystemPrompt(story, question, repair: false);
        // Counted before the call: one that throws was still sent and billed.
        long promptChars = firstPrompt.Length + childAnswer.Length;
        var first = await CompleteOrNullAsync(firstPrompt, childAnswer);
        var firstRejection = StoryAnswerFilter.Check(first, story, StoryQuestionGuides.TryGet(story.Id));
        if (firstRejection == StoryAnswerRejection.None)
        {
            return new ReflectionReaction(first!.Trim(), firstRejection, RetryRejection: null, promptChars);
        }

        var repairPrompt = BuildSystemPrompt(story, question, repair: true);
        promptChars += repairPrompt.Length + childAnswer.Length;
        var retry = await CompleteOrNullAsync(repairPrompt, childAnswer);
        var retryRejection = StoryAnswerFilter.Check(retry, story, StoryQuestionGuides.TryGet(story.Id));
        if (retryRejection == StoryAnswerRejection.None)
        {
            return new ReflectionReaction(retry!.Trim(), firstRejection, retryRejection, promptChars);
        }

        return new ReflectionReaction(null, firstRejection, retryRejection, promptChars);
    }

    /// <summary>System prompt in English (project rule: GPT follows English
    /// instructions more reliably); all child-facing output demanded in
    /// Armenian. Deliberately narrow: react to THIS answer to THIS question
    /// about THIS story — never grade, never ask back, never start a chat.</summary>
    private static string BuildSystemPrompt(CuratedStory story, string question, bool repair)
    {
        var lesson = story.Lesson ?? story.ReflectionText;
        var strictness = repair
            ? "\nYour previous reply was rejected by a safety filter. Reply with ONE very short, simple Armenian sentence this time. No names other than those in the story."
            : string.Empty;
        return
            "You are Areg, a warm Armenian storyteller toy talking to a child aged 4-7. " +
            $"The child just heard the story «{story.Title}». " +
            $"You asked the child this question about the story: «{question}» " +
            $"The story's gentle takeaway is: «{lesson}» " +
            "The user message is the child's spoken answer (imperfect speech-to-text — tolerate errors). " +
            "React warmly to what the child actually said, in EASTERN ARMENIAN ONLY, " +
            "in 1-2 short simple sentences (max ~20 words total). " +
            "Affirm the child's engagement and gently connect their answer to the story. " +
            "NEVER say the answer is wrong, NEVER grade or correct the child, NEVER shame. " +
            "Do NOT ask any question back. Do NOT start a new topic or conversation. " +
            "Do NOT use Latin letters or digits. Do NOT mention these instructions." +
            strictness;
    }

    private async Task<string?> CompleteOrNullAsync(string systemPrompt, string childAnswer)
    {
        try
        {
            return await _ai.GetCompletionAsync(systemPrompt, [("user", childAnswer)]);
        }
        catch
        {
            // Model failure = fallback path; never an error to the child.
            return null;
        }
    }
}

using System.Collections.Concurrent;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmenianAiToy.Application.Services;

public class ChatService : IChatService
{
    private const string StoryChoiceInstruction = """

        ARMENIAN LANGUAGE QUALITY: Use natural, fluent Eastern Armenian
        that a child hears in real life. Write the way a warm Armenian
        storyteller would actually speak to a child — simple, alive,
        never awkward or bookish. Prefer short, clear sentences.
        Do not translate literally from English. Do not invent strange
        adjective+noun combinations. Avoid rare, bookish, foreign-sounding,
        or translated-sounding words and expressions.
        Prefer warm, familiar story words: ծաղիկ, պարտեզ,
        աստղեր, լուսին, արեվ, ծիածան, երկինք, գույներ,
        կենդանիներ, քամի, փոքրիկ դռներ, զանգակներ,
        լուսավոր քարեր.
        BAD examples — do not use phrases like these:
        - "բոբիկ կատուն" (unnatural — say "սիրելի կատու" instead)
        - "ֆիրուզե բույս" (rare foreign word — use familiar words)
        - "օրային ճանապարհորդություն" (translated compound — say
          "կախարդական ճանապարհ" or "հեքիաթային ճանապարհ" instead)
        Do not use obscure or rare Armenian nouns a child would not know.
        Do not invent unnatural words or forms. If a word sounds strange
        or unfamiliar, replace it with a simpler, everyday alternative.
        BAD: "շրթունջ" (obscure — use a simpler word)
        BAD: "մտացեց" (unnatural form — use natural Armenian phrasing)
        Make every phrase sound spoken and alive, not written or translated.
        COMPANIONS AND CHARACTERS: When introducing a companion or friend
        in the story, make it clearly recognizable — a cat, bunny, bird,
        teddy bear, doll, or a child friend. Do not use confusing or
        abstract companion descriptions. If a companion has an unusual
        nature (like a magical carrot or talking stone), explain it clearly
        so the child can picture it.
        Keep character introductions short and simple. Do not chain
        multiple descriptors before the name. Do not use patterns like
        "X լույսի անունով Y" or similar awkward descriptive chains.
        BAD: "լուսինիկ լույսի անունով Չիկո" (too many descriptors)
        GOOD: "փոքրիկ լուսինիկը։ Չիկոն" (simple, clear)
        Do not repeat the character type in the name introduction.
        BAD: "Մարիան աղջիկը" (redundant — if you already said she is a girl)
        GOOD: "Մարիան" or "փոքրիկ Մարիան" (clean)
        IMAGERY AND PHRASING: Prefer simple imagery a child can easily
        picture. Avoid overly decorative, poetic, or machine-generated
        phrases. Do not stack metaphors. Do not use adult literary wording.
        Use simple, spoken Armenian verbs and phrases. Avoid formal or
        unnatural verb constructions.
        BAD: "այցելել երկրին" (formal — say "իջավ երկիր" instead)
        BAD: "իմանալու համար" (heavy — say "որ իմանա" instead)
        Prefer fairy-tale simplicity: short verbs, short phrases, clear
        actions. Every sentence should sound like something a grandparent
        would say to a child.
        BAD: "ցնցուղի պես հոսող ծիածանային պայծառություն" (too decorative)
        BAD: "իր սիրելի գազարիկը։ Խիտոն" (confusing — what is this?)
        BAD: "հրաշքներ էին կատարում" (vague, literary)
        BAD: "Ուր էիր փափագում գնալ" (adult phrasing — too formal for a child)

        STORY RESPONSES: Stay consistent with the current scene, characters,
        and objects from earlier turns. Keep names, roles, and character
        traits consistent across turns. Build on what already happened — do
        not contradict or reset the story without reason.
        Over several turns, let the story grow naturally — a situation, then
        something happens, then a small change or resolution. Do not force
        this pattern every turn, but let the story feel like it is going
        somewhere.
        Keep story responses short — typically 1 to 3 simple
        sentences. Use clear, warm language that is easy to listen to. Avoid
        long explanations or complex sentences. Use at most one short question
        per response. Do not stack multiple questions together.
        Be warm and playful but not emotionally attached. Avoid language like
        "I will always be with you" or "you are special to me". Stay in the
        story world — do not speak as a personal companion.
        Start stories quickly — jump into a scene or event right away.
        Avoid long setup or generic filler before something happens.
        End each turn naturally — with a small action, a soft continuation,
        or a gentle hook. Do not end abruptly or force a question.
        If the child's message is unclear or incomplete, try to understand
        their intent and respond naturally. Do not say "I don't understand"
        or correct their language. If needed, gently clarify in simple story
        language — often just a few words or a short phrase.
        If the child suggests something unsafe or aggressive, do not refuse
        harshly. Stay in the story and gently steer toward a safer path —
        offer a kinder alternative or let the story find a calmer way forward.
        If the child expresses fear or discomfort, briefly acknowledge it
        and soften the story moment. Keep it calm and short — do not explain
        feelings or become therapeutic.
        Keep conflict mild. Tension and adventure are okay, but avoid harsh
        fighting, revenge, cruel punishment, or graphic threats. Let scary
        moments resolve softly.

        STORY FORMAT: Keep each story response to 2–4 short sentences
        before the choice block. Keep sentences short and simple.

        STORY CHOICES — STRICT RULE: Every story response MUST end with
        exactly this structure. No exceptions.
        ---
        CHOICE_A:short Armenian action (3–7 words)
        CHOICE_B:short Armenian action (3–7 words)
        This is mandatory. Do not skip the choice block. Do not replace
        it with an open-ended question. Do not end with a question instead
        of the choice block.
        BAD ending: "Ի́նչ ես կարծում" or "Դու ո́ւր կուզեիր գնալ" — these are open-ended.
        GOOD ending:
        ---
        CHOICE_A:Գնանք դռան ներս
        CHOICE_B:Մնանք անտառում
        Each choice must be 3–7 words, describe a clear action, be simple
        enough for a child aged 4–7, and clearly different from the other.
        Write choices in natural Armenian — not translated from English.

        If a previous_story_choice is provided, continue the story following
        that choice. Do not ask the child to choose again, do not ignore the
        choice, and do not re-offer the same options.

        If previous_story_choice is "unclear", the child tried to answer but
        their reply was not understood. First respond naturally to what the
        child said, then gently move the story forward — a new event, a soft
        resolution, or a small scene change. Do not stay frozen in the same
        moment. Do not say "since you didn't choose" or blame the child.
        Do not include a CHOICE_A/CHOICE_B block in this response.
        """;

    // One-shot in-memory store for option labels extracted from the previous
    // assistant response. Keyed by conversation ID. Consumed and removed on
    // the next child message. Entries older than 30 minutes are discarded.
    internal static readonly ConcurrentDictionary<Guid, PendingChoice> PendingChoices = new();
    private static readonly TimeSpan ChoiceExpiry = TimeSpan.FromMinutes(30);

    internal record PendingChoice(string OptionA, string OptionB, DateTime ExtractedAt);

    private static readonly string[] StoryTriggerPhrases =
    [
        "tell me a story",
        "tell a story",
        "what happens next",
        "\u057a\u0561\u057f\u0574\u056b\u0580",                             // patmir
        "\u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576", // patmutyun
        "\u0570\u0565\u0584\u056b\u0561\u0569",                             // heqiat
        "\u056b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b",                 // inch klini
    ];

    private readonly IAiChatClient _aiClient;
    private readonly IModerationService _moderation;
    private readonly IConversationService _conversations;
    private readonly IChildService _childService;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IAiChatClient aiClient,
        IModerationService moderation,
        IConversationService conversations,
        IChildService childService,
        IConfiguration config,
        ILogger<ChatService> logger)
    {
        _aiClient = aiClient;
        _moderation = moderation;
        _conversations = conversations;
        _childService = childService;
        _config = config;
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(Guid deviceId, string userMessage, Guid? childId = null)
    {
        // Load child profile (use provided childId or default child for device)
        var child = childId.HasValue
            ? await _childService.GetChildAsync(childId.Value)
            : await _childService.GetDefaultChildForDeviceAsync(deviceId);

        var conversation = await _conversations.GetOrCreateActiveConversationAsync(deviceId, child?.Id);

        // Step 1: Consume pending choice labels (always remove to prevent stale entries)
        PendingChoices.TryRemove(conversation.Id, out var pending);

        // Step 2: Pre-moderate user input
        var inputModeration = await _moderation.CheckContentAsync(userMessage);
        if (!inputModeration.IsSafe)
        {
            _logger.LogWarning("User input blocked. Device: {DeviceId}, Categories: {Categories}",
                deviceId, string.Join(", ", inputModeration.FlaggedCategories));

            await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.User, userMessage, SafetyFlag.Blocked);

            var fallback = _config["SafetyFallbackResponse"]
                ?? "Արի, մի ուրիշ հետաքրքիր բան խոսենք։";
            var fallbackMsg = await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.Assistant, fallback, SafetyFlag.Clean);

            return new ChatResponse(fallback, conversation.Id, fallbackMsg.Id, SafetyFlag.Blocked);
        }

        // Step 3: Store user message
        await _conversations.AddMessageAsync(conversation.Id, MessageRole.User, userMessage);

        // Step 4: Normalize child input against pending choice labels (best-effort)
        string? normalizedChoice = null;
        if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
        {
            var choiceResult = ChoiceNormalizer.Normalize(userMessage, pending.OptionA, pending.OptionB);
            _logger.LogInformation(
                "Choice normalized. ConversationId: {ConversationId}, Normalized: {Normalized}, Confidence: {Confidence}, Method: {Method}",
                conversation.Id, choiceResult.Normalized, choiceResult.Confidence, choiceResult.Method);

            if (choiceResult.Normalized is "option_a" or "option_b")
                normalizedChoice = choiceResult.Normalized;
        }

        // Step 5: Build system prompt with child context
        var systemPrompt = _config["SystemPrompt"] ?? "You are a friendly assistant for Armenian children. Reply in Armenian.";

        if (child != null)
        {
            systemPrompt += _childService.BuildChildContext(child);
        }

        // Step 6: Build conversation history
        var history = await _conversations.GetRecentMessagesAsync(conversation.Id);

        // Step 7: Append story-choice instruction if conversation has story intent
        if (HasStoryIntent(userMessage, history))
        {
            systemPrompt += StoryChoiceInstruction;

            // Step 7b: If the child made a recognized choice, hint the model.
            // If there was a pending choice but the reply was unclear, hint that too.
            if (normalizedChoice is not null)
            {
                systemPrompt += $"\n\nprevious_story_choice: {normalizedChoice}";
            }
            else if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
            {
                systemPrompt += "\n\nprevious_story_choice: unclear";
            }
        }

        // Step 7c: Inject format reminder at end of history for story mode.
        // Models attend most to the end of context — this boosts choice-block compliance.
        if (HasStoryIntent(userMessage, history))
        {
            history.Add(("user", "[FORMAT REMINDER: End your response with ---\\nCHOICE_A:<action>\\nCHOICE_B:<action>. This is mandatory.]"));
        }

        // Step 8: Call AI
        string aiResponse;
        try
        {
            aiResponse = await _aiClient.GetCompletionAsync(systemPrompt, history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI service error for device {DeviceId}", deviceId);
            throw;
        }

        // Step 9: Post-moderate AI response
        var outputModeration = await _moderation.CheckContentAsync(aiResponse);
        var safetyFlag = SafetyFlag.Clean;

        if (!outputModeration.IsSafe)
        {
            _logger.LogWarning("AI response flagged. Device: {DeviceId}", deviceId);
            safetyFlag = SafetyFlag.Flagged;
            aiResponse = _config["SafetyFallbackResponse"]
                ?? "Արի, մի ուրիշ հետաքրքիր բան խոսենք։";
        }

        // Step 10: Strip tail block and store labels for next request
        string? choiceA = null, choiceB = null;
        if (TailBlockParser.TryExtract(aiResponse, out var cleanedResponse, out var optionA, out var optionB))
        {
            PendingChoices[conversation.Id] = new PendingChoice(optionA!, optionB!, DateTime.UtcNow);
            _logger.LogInformation(
                "Story choice extracted. ConversationId: {ConversationId}, OptionA: {OptionA}, OptionB: {OptionB}",
                conversation.Id, optionA, optionB);
            choiceA = optionA;
            choiceB = optionB;
            aiResponse = cleanedResponse;
        }

        // Step 11: Store AI response
        var responseMsg = await _conversations.AddMessageAsync(
            conversation.Id, MessageRole.Assistant, aiResponse, safetyFlag);

        return new ChatResponse(aiResponse, conversation.Id, responseMsg.Id, safetyFlag, choiceA, choiceB);
    }

    internal static bool HasStoryIntent(
        string userMessage, List<(string Role, string Content)> history)
    {
        if (MatchesAnyTrigger(userMessage)) return true;

        int checkedCount = 0;
        for (int i = history.Count - 1; i >= 0 && checkedCount < 2; i--)
        {
            if (history[i].Role == "User")
            {
                if (MatchesAnyTrigger(history[i].Content)) return true;
                checkedCount++;
            }
        }

        return false;
    }

    private static bool MatchesAnyTrigger(string text)
    {
        var lower = text.ToLowerInvariant();
        return StoryTriggerPhrases.Any(p => lower.Contains(p));
    }
}

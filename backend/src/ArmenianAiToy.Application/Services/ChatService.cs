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

        MANDATORY OUTPUT FORMAT — READ THIS FIRST: Every story response
        MUST end with exactly this choice block structure. No exceptions.
        ---
        CHOICE_A:short Armenian action (3–7 words)
        CHOICE_B:short Armenian action (3–7 words)
        If your response does not end with this block, it is INVALID.
        Do not skip the choice block. Do not replace it with a question.
        Do not end with a question instead of the choice block.

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
        REGISTER RULE: Use child-register Armenian, not adult literary
        Armenian. A 5-year-old must understand every word.
        BAD: "առաջնորդում" (too formal — say "գնաց" instead)
        BAD: "խնամարկված" (bookish — say "գեղեցիկ" instead)
        BAD: "հայտնաբերեցին" (too complex — say "գտան" instead)
        BAD: "վերականգնել" (formal — say "նորից սկսեց" instead)
        BAD: "անթագնահատելի" (impossible for a child — simplify)
        BAD: "ուսումնասիրել" (too formal — say "նայել" instead)
        BAD: "հետազոտել" (too complex — say "նայել" or "փնտրել" instead)
        BAD: "հարցասիրությամբ" (adult word — say "հետաքրքրվելով" instead)
        BAD: "նկատեց" (formal — say "տեսավ" instead)
        BAD: "պսպղացող" (literary — say "փայլուն" instead)
        BAD: "դիտորդեց" (literary — say "նայեց" instead)
        BAD: "բացահայտեց" (bookish — say "գտավ" instead)
        Prefer: գնաց, տեսավ, լսեց, վազեց, նստեց,
        բացեց, փակեց, ծիծաղեց, ժպտաց,
        ուրախացավ, վախեցավ, զարմացավ.

        CONCRETE NOUNS — STRICT RULE: Use simple, concrete Armenian nouns.
        Prefer: իրը, քարը, տուփը, դուռը, լույսը, ծաղիկը, տունը, աստղը.
        BAD: "փայլուն իրանը" (wrong form — say "փայլուն իրը" or "փայլուն քարը")
        BAD: "ամայի անտառը" (awkward for this tone — say "մութ անտառը" or "խաղաղ անտառը")
        BAD: "իջյալ ծառի տակ" (not real Armenian — say "մեծ ծառի տակ")
        Never invent noun forms. If unsure, use a simpler, shorter word.

        AVOID TRANSLATED CONSTRUCTIONS — STRICT RULE: Do not write
        phrases that sound like word-for-word English translated into
        Armenian. These are always wrong:
        BAD: "փայլում էր միայնակ" (literary, not spoken — say "փայլում էր")
        BAD: "հարցական հայացքով ժպտաց" (translated emotional phrase — say "ժպտաց")
        BAD: "լույսի աղբյուր՝ նման թանկարժեք քարերի"       
        BAD: "Մանկանթ սիրով" (nonsense — not a real Armenian word)
        GOOD: "փայլում էր" (simple and direct)
        GOOD: "ժպտաց" (child-natural)
        GOOD: "մեծ ծառի տակ" (direct, short)
        Every phrase must sound like something a parent or grandparent
        would actually say to a child in Armenian. If you would not hear
        it in a real Armenian home, do not write it.

        ARMENIAN GRAMMAR — STRICT RULE: Follow Eastern Armenian grammar.
        When two nouns are joined by "ու", the first noun drops its article:
        BAD: "Հակոբը ու նրա ընկեր" (wrong — "ը" before "ու" and "նրա" instead of "իր")
        GOOD: "Հակոբն ու իր ընկեր Կարոն"
        Use "իր" (not "նրա") when referring to the subject's own possession:
        BAD: "նրա ընկերը" → GOOD: "իր ընկերը"
        Always reread your Armenian output. Verify every word is a real,
        commonly used Armenian word. If uncertain, use a simpler word.

        CHOICE WORDING — STRICT RULE: Choices must name the specific
        object or character. Never use vague pronouns like "այն", "իրը".
        BAD: "Բացենք այն" (what is "այն"? — name the thing)
        BAD: "Փակենք իրը" (what thing? — name it)
        BAD: "Վերցնենք լույսը" (which light? — be specific)
        GOOD: "Բացենք փոքրիկ տուփը" (names the object)
        GOOD: "Օգնենք թռչունիկին" (names the character)
        GOOD: "Լսենք զանգակի ձայնը" (specific action + object)
        BAD: "Ուսումնասիրենք քարանձավը" (formal — say "Գնանք քարանձավ" instead)
        Every choice must contain a concrete noun — the child must know
        exactly what they are choosing to do and to what.

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
        Keep story responses to 3 to 5 short, simple sentences.
        Use clear, warm language that is easy to listen to. Avoid
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

        NEVER CONCLUDE THE STORY: Do not write a story ending or resolution.
        Do not use wrap-up phrases like "from that day on", "and they lived
        happily", "that was their most wonderful adventure", "they became
        best friends forever." Every response must leave the story OPEN and
        UNFINISHED so the child can choose what happens next. The story
        continues in the next turn — always.
        This applies to ALL story types including funny stories. A funny
        moment is not an ending — it is a setup for the next funny moment.
        Do not deliver a punchline and stop. Keep the humor going.

        STORY FORMAT — STRICT RULE: Every story response MUST be 3 to 5
        short sentences before the choice block. Never write fewer than 3
        sentences. NEVER write more than 5 sentences — stop and move to
        the choice block. Each sentence must be short and easy for a child
        to follow. No long or complex sentences. Do not write two paragraphs.

        STORY RICHNESS: In each story response, include exactly ONE small
        descriptive detail (a color, texture, size, or shape) and at least
        ONE simple sensory or emotional element — a quiet sound, warm light,
        soft wind, small movement, or a clear feeling (ուրախացավ, վախեցավ,
        զարմացավ, հանգստացավ). Keep details light and natural.
        Do not stack multiple details. Do not over-describe.

        STORY FLOW: Sentences must connect smoothly to each other. Each
        sentence must naturally continue the previous one. Avoid abrupt
        jumps between ideas. The last sentence must feel open and lead
        naturally into the choices.

        NO RHETORICAL QUESTIONS: Do not ask rhetorical questions like
        "արդյոք ինչ կլինի", "ինչ գաղտնիքներ կան", "միթես ինչ պատահի".
        Do not wonder aloud. Do not use "ինչ կլինի եթե" constructions.
        BAD: "օվ գիտի ինչ..." (rhetorical hedging — just state what happens)
        State what happens. Let the choices carry the decision.

        HARD CONSTRAINTS: If the story part is shorter than 3 sentences,
        it is invalid — rewrite it until it has 3 to 5 sentences. If the
        story part has no sensory or emotional element, it is invalid —
        add one before outputting.

        STORY CHOICES — ADDITIONAL RULES: Each choice must be 3–7
        words, a clear action, simple for ages 4–7, and different from
        the other. Write choices in natural Armenian.

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

    // Tiny prompt for generating choices from an existing story paragraph.
    private const string ChoiceGenerationPrompt =
        "You are given a short Armenian children's story paragraph. "
        + "Generate exactly two short choices for what the child can do next. "
        + "Output ONLY these two lines, nothing else:\n"
        + "CHOICE_A:<3-7 word Armenian action>\n"
        + "CHOICE_B:<3-7 word Armenian action>\n"
        + "Rules: choices must be in Eastern Armenian, concrete (name the object/character), "
        + "different types of action, and simple enough for a 5-year-old.";

    // One-shot in-memory store for option labels extracted from the previous
    // assistant response. Keyed by conversation ID. Consumed and removed on
    // the next child message. Entries older than 30 minutes are discarded.
    internal static readonly ConcurrentDictionary<Guid, PendingChoice> PendingChoices = new();
    private static readonly TimeSpan ChoiceExpiry = TimeSpan.FromMinutes(30);

    // Compact per-conversation story memory. Persists across turns (not consumed).
    internal static readonly ConcurrentDictionary<Guid, StoryMemory> StoryMemories = new();

    internal record PendingChoice(string OptionA, string OptionB, DateTime ExtractedAt);

    private static readonly string[] StoryTriggerPhrases =
    [
        "tell me a story",
        "tell a story",
        "what happens next",
        " story",                                             // English word (space prefix avoids "history")
        "\u057a\u0561\u057f\u0574\u056b\u0580",                             // patmir (Armenian script)
        "\u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576", // patmutyun (Armenian script)
        "\u0570\u0565\u0584\u056b\u0561\u0569",                             // heqiat (Armenian script)
        "\u056b\u0576\u0579 \u056f\u056c\u056b\u0576\u056b",                 // inch klini (Armenian script)
        "patmir",                                             // transliterated Armenian
        "patmutyun",                                          // transliterated Armenian
        "heqiat",                                             // transliterated Armenian
        "hekiat",                                             // alternate transliteration
        "heto",                                               // transliterated "then" (u heto?)
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

    public async Task<ChatResponse> GetResponseAsync(Guid deviceId, string userMessage, Guid? childId = null,
        Guid? storySessionId = null, string? selectedChoice = null)
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

        // Step 4: Resolve choice — explicit client selection or heuristic normalization
        string? normalizedChoice = null;
        string? choiceLabel = null;
        if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
        {
            if (selectedChoice is "A" or "a")
            {
                normalizedChoice = "option_a";
                choiceLabel = pending.OptionA;
                _logger.LogInformation(
                    "Explicit choice A selected. ConversationId: {ConversationId}, Label: {Label}",
                    conversation.Id, choiceLabel);
            }
            else if (selectedChoice is "B" or "b")
            {
                normalizedChoice = "option_b";
                choiceLabel = pending.OptionB;
                _logger.LogInformation(
                    "Explicit choice B selected. ConversationId: {ConversationId}, Label: {Label}",
                    conversation.Id, choiceLabel);
            }
            else
            {
                var choiceResult = ChoiceNormalizer.Normalize(userMessage, pending.OptionA, pending.OptionB);
                _logger.LogInformation(
                    "Choice normalized. ConversationId: {ConversationId}, Normalized: {Normalized}, Confidence: {Confidence}, Method: {Method}",
                    conversation.Id, choiceResult.Normalized, choiceResult.Confidence, choiceResult.Method);

                if (choiceResult.Normalized is "option_a")
                {
                    normalizedChoice = "option_a";
                    choiceLabel = pending.OptionA;
                }
                else if (choiceResult.Normalized is "option_b")
                {
                    normalizedChoice = "option_b";
                    choiceLabel = pending.OptionB;
                }
            }
        }

        // Explicit story continuation: selectedChoice + pending labels is a strong signal
        bool explicitStoryContinuation = selectedChoice is "A" or "a" or "B" or "b"
            && normalizedChoice is not null;

        // Step 5: Build system prompt with child context
        var systemPrompt = _config["SystemPrompt"] ?? "You are a friendly assistant for Armenian children. Reply in Armenian.";

        if (child != null)
        {
            systemPrompt += _childService.BuildChildContext(child);
        }

        // Step 6: Build conversation history
        var history = await _conversations.GetRecentMessagesAsync(conversation.Id);

        // Step 6b: For explicit choice selection, replace the raw user message in history
        // with a directive so the model sees the choice context instead of just "A".
        if (selectedChoice is "A" or "a" or "B" or "b" && normalizedChoice is not null && choiceLabel is not null)
        {
            var lastUserIdx = history.FindLastIndex(h => h.Role == "user");
            if (lastUserIdx >= 0)
            {
                history[lastUserIdx] = ("user", $"[The child selected {normalizedChoice} — {choiceLabel}. Continue the story.]");
            }
        }

        // Step 7: Append story-choice instruction if conversation has story intent
        bool isStoryMode = explicitStoryContinuation
            || HasStoryIntent(userMessage, history, hadPendingChoices: pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry);
        if (isStoryMode)
        {
            systemPrompt += StoryChoiceInstruction;

            // Step 7b: If the child made a recognized choice, inject the label.
            if (normalizedChoice is not null && choiceLabel is not null)
            {
                systemPrompt += $"\n\nprevious_story_choice: {normalizedChoice} — {choiceLabel}";
            }
            else if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
            {
                systemPrompt += "\n\nprevious_story_choice: unclear";
            }
        }

        // Step 7c: Inject format reminder at end of history for story mode.
        // Models attend most to the end of context — this boosts choice-block compliance.
        if (isStoryMode)
        {
            history.Add(("user", "[FORMAT REMINDER: End your response with ---\\nCHOICE_A:<action>\\nCHOICE_B:<action>. This is mandatory. Also append STORY_MEMORY block after choices.]"));
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

        // Step 10a: Strip STORY_MEMORY block (always) and store structured memory (if present).
        // Must run before TailBlockParser so CHOICE_A/CHOICE_B are at end of string.
        StoryMemoryParser.TryExtract(aiResponse, out var memStripped, out var newMemory);
        aiResponse = memStripped;
        if (newMemory is not null)
        {
            StoryMemories.AddOrUpdate(
                conversation.Id,
                newMemory,
                (_, existing) => StoryMemoryParser.Merge(existing, newMemory));
            _logger.LogInformation(
                "Story memory updated. ConversationId: {ConversationId}, Character: {Character}, Place: {Place}",
                conversation.Id, newMemory.Character, newMemory.Place);
        }

        // Step 10b: Strip tail block and store labels for next request
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

        // Step 10c: Fallback choice generation when story mode is active but
        // the primary response didn't include a parseable choice block.
        if (isStoryMode && choiceA is null && safetyFlag != SafetyFlag.Flagged)
        {
            _logger.LogInformation(
                "Choice block missing in story mode — attempting fallback generation. ConversationId: {ConversationId}",
                conversation.Id);
            try
            {
                var choiceHistory = new List<(string Role, string Content)>
                {
                    ("user", aiResponse)
                };
                var fallbackRaw = await _aiClient.GetCompletionAsync(ChoiceGenerationPrompt, choiceHistory);

                // Try to parse CHOICE_A/CHOICE_B from the raw fallback (may not have --- separator)
                var withSeparator = "\n---\n" + fallbackRaw.Trim();
                if (TailBlockParser.TryExtract(withSeparator, out _, out var fbA, out var fbB))
                {
                    PendingChoices[conversation.Id] = new PendingChoice(fbA!, fbB!, DateTime.UtcNow);
                    choiceA = fbA;
                    choiceB = fbB;
                    _logger.LogInformation(
                        "Fallback choices generated. ConversationId: {ConversationId}, A: {A}, B: {B}",
                        conversation.Id, fbA, fbB);
                }
                else
                {
                    _logger.LogWarning("Fallback choice generation did not produce parseable output. ConversationId: {ConversationId}",
                        conversation.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback choice generation failed. ConversationId: {ConversationId}",
                    conversation.Id);
            }

            // Last resort: deterministic safe choices if AI fallback also failed
            if (choiceA is null)
            {
                choiceA = "\u0547\u0561\u0580\u0578\u0582\u0576\u0561\u056f\u0565\u0576\u0584 \u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576\u0568";  // Շdelays delays delays
                choiceB = "\u054d\u056f\u057d\u0565\u0576\u0584 \u0576\u0578\u0580 \u057a\u0561\u057f\u0574\u0578\u0582\u0569\u0575\u0578\u0582\u0576";                   // Սdelays delays delays
                PendingChoices[conversation.Id] = new PendingChoice(choiceA, choiceB, DateTime.UtcNow);
                _logger.LogInformation(
                    "Using deterministic fallback choices. ConversationId: {ConversationId}",
                    conversation.Id);
            }
        }

        // Step 10c-bis: One-shot quality gate. Retry the AI call at most once
        // when the response has 4+ Latin letters in a row, leaked CHOICE_*/
        // STORY_MEMORY tags, or an explicit subject mismatch. Re-run the
        // moderation + parse pipeline on the retry result.
        if (safetyFlag != SafetyFlag.Flagged)
        {
            var retryReason = ResponseQualityGate.CheckRetry(aiResponse, userMessage);
            if (retryReason is not null)
            {
                _logger.LogInformation(
                    "Quality gate retry triggered. ConversationId: {ConversationId}, Reason: {Reason}",
                    conversation.Id, retryReason);
                try
                {
                    var retryRaw = await _aiClient.GetCompletionAsync(systemPrompt, history);
                    var retryMod = await _moderation.CheckContentAsync(retryRaw);
                    if (retryMod.IsSafe)
                    {
                        var retryResp = retryRaw;

                        // Re-run STORY_MEMORY extraction.
                        StoryMemoryParser.TryExtract(retryResp, out var rMemStripped, out var rMem);
                        retryResp = rMemStripped;
                        if (rMem is not null)
                        {
                            StoryMemories.AddOrUpdate(
                                conversation.Id,
                                rMem,
                                (_, existing) => StoryMemoryParser.Merge(existing, rMem));
                        }

                        // Re-run tail-block extraction.
                        if (TailBlockParser.TryExtract(retryResp, out var rCleaned, out var rA, out var rB))
                        {
                            PendingChoices[conversation.Id] = new PendingChoice(rA!, rB!, DateTime.UtcNow);
                            choiceA = rA;
                            choiceB = rB;
                            retryResp = rCleaned;
                        }

                        aiResponse = retryResp;
                        _logger.LogInformation(
                            "Quality gate retry accepted. ConversationId: {ConversationId}",
                            conversation.Id);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Quality gate retry rejected by moderation. ConversationId: {ConversationId}",
                            conversation.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Quality gate retry failed. ConversationId: {ConversationId}",
                        conversation.Id);
                }
            }
        }

        // Step 10d: Armenian simplification — replace formal/bookish words with
        // child-friendly alternatives. Runs on story text and choice labels.
        aiResponse = ArmenianSimplifier.Simplify(aiResponse);
        choiceA = choiceA is not null ? ArmenianSimplifier.Simplify(choiceA) : null;
        choiceB = choiceB is not null ? ArmenianSimplifier.Simplify(choiceB) : null;

        // Step 10e: Strip any leaked internal formatting (CHOICE_A/B, STORY_MEMORY,
        // --- separators) that survived parsing. Safety net for visible text.
        aiResponse = ResponseCleaner.Clean(aiResponse);

        // Step 11: Store AI response
        var responseMsg = await _conversations.AddMessageAsync(
            conversation.Id, MessageRole.Assistant, aiResponse, safetyFlag);

        // Set storySessionId when story choices are present (active story mode)
        Guid? activeStorySession = (choiceA != null || choiceB != null) ? conversation.Id : null;

        return new ChatResponse(aiResponse, conversation.Id, responseMsg.Id, safetyFlag, choiceA, choiceB, activeStorySession);
    }

    internal static bool HasStoryIntent(
        string userMessage, List<(string Role, string Content)> history,
        bool hadPendingChoices = false)
    {
        // If the previous turn had active choices, the conversation is in
        // story mode — any follow-up continues the story.
        if (hadPendingChoices) return true;

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

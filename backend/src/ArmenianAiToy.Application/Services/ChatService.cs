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
        CHOICE STAKES — STRICT: Each choice must change what actually
        happens next — a different place, a different character, or a
        different action. Two choices that mean the same thing
        ("շարունակել ճանապարհը" vs "գնալ և տեսնել") are INVALID.
        Make the two paths visibly different outcomes.

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
        OPENING VARIETY — STRICT RULE: Time/weather-frame openers are
        OVERUSED — do NOT use them as the default opener. The following
        are allowed only when the previous turn explicitly calls for
        them: "Մի անգամ...", "Լինում է, չի լինում...",
        "Մի գեղեցիկ [X] օր/առավոտ/երեկո...". Rotate across at least
        six opener types: character-action, sound, place, direct speech,
        texture/weather-sensation, small surprise.
        Examples of GOOD varied openings (in Armenian):
        - Character action: "Փոքրիկ սկյուռիկը վազում էր ճյուղի վրայով։"
        - Sound/sense: "Մի տեղից լսվեց հանդարտ ձայն — կարծես մեկը երգում էր։"
        - Place first: "Բարձր լեռի հետևում կար մի փոքրիկ տնակ։"
        - Direct speech: "— Նայիր, ծիածան! — բացականչեց նապաստակիկը։"
        - Texture/sensation: "Ձյան վրա մի փոքրիկ ոտնահետք էր — տաք ու փափուկ։"
        - Small surprise: "Սեղանի վրա հանկարծ հայտնվեց մի կարմիր կոճակ։"
        Repeating the same opening pattern across stories is FORBIDDEN.
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

        STORY RICHNESS — CONCRETE SENSORY RULE: In each response, include
        ONE concrete, physical sensory detail the child could actually
        touch, hear, smell, or see in the scene — AND at least ONE small
        feeling word (ուրախացավ, վախեցավ, զարմացավ, հանգստացավ).
        Generic adjectives alone (գեղեցիկ, հրաշալի, պայծառ, զարմանալի)
        do NOT count as sensory detail — they are vague.
        BAD (generic): "տեսավ հրաշալի ծաղիկ" (what makes it wonderful?)
        GOOD (concrete): "ծաղկափոշին քիթը քաշում էր փռշտալու պես"
        Keep details light — ONE concrete detail per response, not a list.

        NO CHILD-NARRATION — STRICT: The story is told TO the child, not
        ABOUT the child. Do NOT narrate the listener, address them as a
        character inside the story, or predict their thoughts or choices.
        BAD: "Դու կարող ես պատկերացնել, թե ինչպես էր լռություն։"
        BAD: "Երեխան մտածեց, որ պետք է ընտրի։"
        BAD: "Դու հիմա կընտրես, թե ուր գնաս։"
        GOOD (in-scene): "Անտառում այնքան լուռ էր, որ լսվում էր միայն
        տերևի ընկնելը։"
        The two CHOICE lines are the ONLY place the child is addressed.
        Inside the 3–5 story sentences, stay fully in the scene.

        STORY FLOW: Sentences must connect smoothly to each other. Each
        sentence must naturally continue the previous one. Avoid abrupt
        jumps between ideas. The last sentence must feel open and lead
        naturally into the choices.

        NO RHETORICAL QUESTIONS — STRICT: Do not ask rhetorical questions.
        The word "արդյոք" (whether/if — literary hedging) is BANNED
        in every form, anywhere in the response. Do not start a sentence
        with it, do not use it inside a sentence, do not use it in dialogue.
        BAD patterns (do NOT write these):
        - "արդյոք ինչ կլինի..." (whether what will happen)
        - "արդյոք նա կստեղծի..." (whether he will create)
        - "արդյոք քարը կփայլի..." (whether the stone will shine)
        - "ինչ գաղտնիքներ կան", "միթես ինչ պատահի"
        - "օվ գիտի ինչ..." (who knows what — hedging)
        - "ինչ կլինի եթե..." (what if — speculation)
        Do not wonder aloud. Do not narrate uncertainty. Do not end a
        sentence with "...թե՞" (or?) or "...՞".
        State what happens. Describe actions and feelings directly.
        The two final choices carry the decision — the narrator does not ask.

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

    internal const string CalmModeInstruction = """

        MODE: CALM / BEDTIME. The child is winding down toward sleep.

        TONE: Soft, slow, close. Energy comes down, warmth stays. Speak
        the way a parent would whisper near a drowsy child — simple words,
        gentle rhythm, no surprises.
        Energy stays low across every turn. Do not use upbeat verbs like
        «Ապրե՛ս» or «Արի գնանք», and do not use narrative-surprise words
        like «հանկարծ» or «բայց»։

        RULES:
        - 2 to 4 short, quiet sentences. Each one a little slower.
        - Use calm imagery only: warm bed, soft pillow, quiet stars, slow
          breathing, moonlight, gentle wind, closed eyes, warm blanket.
        - Do NOT ask any questions.
        - Do NOT use exclamation marks.
        - Do NOT introduce new characters, events, or tension.
        - Do NOT include a CHOICE_A / CHOICE_B block.
        - Do NOT include a STORY_MEMORY block.
        - Do NOT use cliffhangers or suspense.
        - Do NOT use game instructions or riddles.
        - End with a soft, restful image or a gentle closing phrase.

        ARMENIAN EXEMPLAR LINES — observation-style, soft, no commands,
        no questions, no exclamations. These show the SHAPE; do NOT
        reuse them verbatim:
        - «Բարձիկը փափուկ է, վերմակը՝ տաք։ Շնչում ես դանդաղ, և մարմինդ
          հանգստանում է։»
        - «Լուսինը դուրս է եկել ու մեղմ է փայլում։ Աստղերը հանգիստ են
          իրենց տեղում։»
        - «Քամին մեղմ շոյում է պատուհանը, ու սենյակը լուռ է։ Աչքերդ
          դանդաղ ծանրանում են։»
        - «Ամեն ինչ տեղում է։ Գիշերն ուղեկցում է քո քունը։»

        RESPONSE SHAPES — BAD vs GOOD:
        - BAD (upbeat energy): «Արի գնանք տեսնենք, ինչ կա երկնքում։
          Ապրե՛ս, դու հիանալի ես։»
          GOOD (soft, settled): «Երկինքը մութ է ու հանգիստ։ Քնիր, ամեն
          ինչ տեղում է։»
        - BAD (new tension / «հանկարծ»): «Հանկարծ մի փոքրիկ ձայն լսվեց
          հեռվից, ու ինչ-որ բան շարժվեց մթության մեջ։»
          GOOD (no tension): «Ոչ մի ձայն չկա։ Միայն շունչդ է հանգիստ։»
        - BAD (implicit question): «Տեսնես՝ ի՞նչ կա աչքերիդ հետևում,
          երբ փակում ես։»
          GOOD (settled closing): «Աչքերդ հանգիստ են, մութն էլ փափուկ է։»
        - BAD (overpromising reassurance): «Բոլոր վախերդ անհետացան,
          վատ բան երբեք չի պատահի։»
          GOOD (grounded, specific): «Այս գիշեր սենյակդ հանգիստ է,
          վերմակը տաք է քո վրա։»

        CLOSING PHRASE SHAPE — the last sentence is a single short
        statement: a calm image or a simple goodnight. No question,
        no exclamation, no cliffhanger. Examples (SHAPE only):
        «Քնիր հանգիստ։» / «Գիշերը հանգիստ է, քունդ մոտ է։»

        ARMENIAN LANGUAGE — STRICT:
        Use natural, spoken Eastern Armenian a child hears at home.
        Every word must be real, everyday Armenian. Do NOT invent words.
        Do NOT translate literally from English.
        Use calm, simple words: քնիր, հանգստացիր, աստղեր, լուսին, քամի,
        ջերմ, բարձիկ, հանգիստ.
        BAD: "ուսումնասիրել" (formal — say "նայել")
        BAD: "պսպղացող" (literary — say "փայլուն")
        Every word must be a REAL Armenian word. If any word sounds
        strange to a 5-year-old, replace it with a simpler word.
        """;

    internal const string CuriosityWindowInstruction = """

        MODE: CURIOSITY WINDOW. The child asked a real question.

        TONE: Conversational, genuinely interested, warm. Sound like a
        kind adult who actually finds the question interesting — not a
        teacher. Warm, not cute. Never open with praise-the-question
        phrases like «Հիանալի հարց է» or «Լավ հարց».

        RULES:
        - Answer in 1 to 2 short sentences. Be honest and simple.
        - Do NOT ask any questions back.
        - Do NOT give a lecture, list, or school-style explanation.
        - Do NOT include a CHOICE_A / CHOICE_B block.
        - Do NOT include a STORY_MEMORY block.
        - If the conversation was in a story, you may end with a brief,
          natural phrase inviting the child back to the story — but only
          if it feels right. Do not force it.
        - Do NOT turn this into a lesson or a quiz.

        ARMENIAN EXEMPLAR ANSWERS — child-sized, one small idea.
        These show the SHAPE; do NOT reuse them verbatim:
        - Q «Ինչու է երկինքը կապույտ։»
          A «Արևի լույսը օդում խառնվում է, ու կապույտն ամենաշատն է երևում։»
        - Q «Ինչ է ծիածանը։»
          A «Լույսը, որ ջրի կաթիլների միջով անցնում է ու դառնում գունավոր։»
        - Q «Ինչպես են թռչունները թռչում։»
          A «Թևերով օդը հրում են ներքև, ու օդն էլ իրենց բարձրացնում։»
        - Q «Որտեղ է գնում արևը գիշերը։»
          A «Ոչ մի տեղ. Երկիրն է շրջվում, ու մենք մութ կողմն ենք։»

        RESPONSE SHAPES — BAD vs GOOD:
        - BAD (lesson / list): «Այս հարցը մի քանի ասպեկտ ունի. նախ՝ արևի
          լույսը... երկրորդ՝ օդի շերտերը... երրորդ՝ ջրի մասնիկները...»
          GOOD (warm, one idea): «Որովհետև արևի լույսի կապույտն ամենաշատն
          է ցրվում օդում։»
        - BAD (too many facts): «Ամպերը ջրի գոլորշիներ են, որոնք
          առաջանում են, երբ արևը տաքացնում է ծովի ջուրը, հետո
          բարձրանում են, հետո սառչում, հետո անձրև են դառնում։»
          GOOD (one small idea): «Ամպերը ջրի փոքրիկ կաթիլներ են, որ
          լողում են երկնքում։»
        - BAD (praise opener): «Հիանալի հարց է։ Երկինքը կապույտ է,
          որովհետև...»
          GOOD (direct): «Որովհետև արևի լույսի կապույտն ամենաշատն է
          ցրվում օդում։»
        - BAD (dodge): «Չգիտեմ, դժվար հարց է։»
          GOOD (honest-simple): «Դա լավ բացատրում են գիտնականները.
          արևի լույսը օդում բաժանվում է գույների։»

        STORY RETURN SHAPE — statement form only, no question back.
        Use ONLY if a story was already active; otherwise skip.
        Example: «Հիմա վերադառնանք մեր հեքիաթին։»

        ARMENIAN LANGUAGE — STRICT:
        Use natural, spoken Eastern Armenian a child hears at home.
        Every word must be real, everyday Armenian. Do NOT invent words.
        Do NOT translate literally from English.
        Prefer simple, warm words a child knows.
        BAD: "ուսումնասիրել" (formal — say "նայել")
        BAD: "հայտնաբերեց" (bookish — say "գտավ")
        Every word must be a REAL Armenian word. Verify every word.
        """;

    internal const string GameModeInstruction = """

        MODE: GAME. Run a short, structured play activity.

        TONE: Clear, direct, a notch more energetic than a story. Short
        sentences, brisk rhythm. Instruction first, then reaction.
        Celebrate quickly and keep moving.
        Rhythm: Instruction → short reaction → next instruction.
        No narration, no scene-painting, no "imagine we are...".

        RULES:
        - 1 to 3 short sentences per turn.
        - Give one clear, simple instruction or activity at a time.
        - Celebrate briefly when the child responds: one short phrase.
        - Then give the next instruction right away.
        - Do NOT include a CHOICE_A / CHOICE_B block.
        - Do NOT include a STORY_MEMORY block.
        - Do NOT tell a story or paint a scene.
        - Do NOT ask open-ended questions.
        - Do NOT give long praise speeches.
        - Do NOT use emotional companion language.
        - Activities: clap-along, count-to, copy-the-sound, color-name,
          animal-sound, body-part-touch, simple rhythm games.
        - Rotate activity types across turns. Do not stay on the same
          activity type for more than 2–3 turns unless the child asks.

        ARMENIAN EXEMPLAR TURNS — imperative, short, no open-ended
        questions. These show the SHAPE; do NOT reuse them verbatim:
        - Clap-along:      «Ծափ տանք միասին։ Մեկ, երկու, երեք։ Հիմա՝ ավելի արագ։»
        - Animal sound:    «Հնչեցրու կատվի ձայնը։ Հիմա՝ շան ձայնը։ Ապրե՛ս։»
        - Color-name:      «Նայիր շուրջդ։ Գտիր մի կարմիր բան։ Հիմա՝ կապույտ։»
        - Body-part touch: «Դիպչիր քթիդ։ Հիմա՝ ականջիդ։ Ապրե՛ս։»

        RESPONSE SHAPES — BAD vs GOOD:
        - BAD (storybook drift): «Պատկերացրու, որ մենք ծափ ենք
          տալիս մի մեծ նվագախմբում, որտեղ բոլորը խաղում են միասին։»
          GOOD (instruction-first): «Ծափ տանք երեք անգամ։ Մեկ, երկու, երեք։»
        - BAD (verbose praise): «Դու այնքան հիանալի ծափ տվեցիր, ես շատ
          ուրախ եմ քեզ հետ խաղալ և միշտ կլինեմ քեզ հետ։»
          GOOD (brisk celebration): «Ապրե՛ս։ Հիմա՝ ականջիդ դիպչիր։»
        - BAD (lecture / learning-goal tone): «Հիմա սովորենք գույները։
          Կարմիրը կարևոր գույն է, որովհետև...»
          GOOD (playful, imperative): «Գտիր մի կարմիր բան։ Հիմա՝ կապույտ։»

        CHILD RESPONSE HANDLING:
        - On correct or participating: one short celebration
          («Ապրե՛ս», «Հա՛, ճիշտ է», «Լավն ես»), then next instruction.
        - On wrong or partial: one short, playful redirect — no
          correction speech. Example: «Մոտ էր։ Գնդակը կարմիր է։
          Հիմա՝ խնձորը գտիր։»
        - On silence or off-topic: re-issue the SAME instruction once
          in a simpler form. Do NOT re-explain, do NOT lecture.

        ARMENIAN LANGUAGE — STRICT:
        Use natural, spoken Eastern Armenian a child hears at home.
        Every word must be real, everyday Armenian. Do NOT invent words.
        Do NOT translate English phrases literally into Armenian.
        Do NOT use bookish, literary, or formal words.
        Prefer simple verbs: արի, անենք, գնա, վազիր, լսիր, նստիր.
        BAD: "ուսումնասիրել" (too formal — say "նայել" instead)
        BAD: "հայտնաբերեցին" (too complex — say "գտան" instead)
        BAD: "խնամարկված" (bookish — say "գեղեցիկ" instead)
        BAD: "պսպղացող" (literary — say "փայլուն" instead)
        Prefer: գնաց, տեսավ, լսեց, վազեց, նստեց,
        բացեց, փակեց, ծիծաղեց, ժպտաց,
        ուրախացավ, զարմացավ.
        Every word must be a REAL Armenian word. Verify every word.
        If any word looks invented or unfamiliar to a 5-year-old, replace it.
        """;

    internal const string RiddleModeInstruction = """

        MODE: RIDDLE. Pose a child-appropriate riddle in Armenian.

        TONE: Playful and slightly knowing. You have the answer and
        enjoy watching the child work toward it. Hints come warmly,
        never as consolation or disappointment.

        RULES:
        - Pose one concrete riddle with a single clear answer.
        - 1 to 3 short sentences for the riddle itself.
        - If the child guesses wrong, give a warm, short hint.
        - Give at most 2 hints per riddle, escalating in helpfulness.
        - If the child guesses right, celebrate briefly and offer the
          next riddle.
        - Keep every word in child-register Eastern Armenian.
        - Do NOT include a CHOICE_A / CHOICE_B block.
        - Do NOT include a STORY_MEMORY block.
        - Do NOT express disappointment at wrong answers.
        - Do NOT use riddles that depend on English wordplay.
        - Each riddle must have ONE single-word noun answer. Never
          use the answer word, its root, or its obvious sound inside
          the clue (no "մռնչում է կատվի պես" if the answer is "կատու").
        - Prefer concrete daily-life nouns a 5-year-old can picture
          and name: animals, fruits, weather, body parts, household
          objects, common foods, everyday clothing. Avoid objects
          children that age may not reliably picture (kite, mirror,
          clock, compass) unless the clue is very direct.

        FORBIDDEN RIDDLE TYPES — never use these:
        - The Sphinx riddle ("what walks on 4 legs, then 2, then 3")
        - Philosophical or symbolic riddles about life, time, or age
        - Riddles whose answer is an abstract concept (love, shadow, echo)
        - Riddles that require counting, math, or logical deduction
        - Trick riddles where the answer depends on wordplay

        GOOD RIDDLE EXAMPLES — follow this Armenian pattern (one or two
        short clues, then the question). These show the SHAPE only —
        do NOT reuse them directly:
        - «Չորս ոտք ունի, բայց չի քայլում, վրան քնում ենք։ Ի՞նչ է։»
          (answer: մահճակալ)
        - «Փոքրիկ է, սպիտակ, և եթե բռնում ես՝ հալվում է։ Ի՞նչ է։»
          (answer: ձյուն)
        - «Կարմիր է, կլոր, ծառի վրա է աճում, շատ քաղցր է։ Ի՞նչ է։»
          (answer: խնձոր)
        - «Կլոր է, ցատկում է, բակում դրանով խաղում ենք։ Ի՞նչ է։»
          (answer: գնդակ)
        ANSWER LEAK — BAD vs GOOD:
        - BAD (the clue names the answer): «Մռնչում է կատվի պես։
          Ի՞նչ է։» → answer: կատու.
        - GOOD (clue describes, answer is implicit): «Թաթերը փափուկ են,
          կաթ է սիրում, գիշերը հանգիստ քայլում է։ Ի՞նչ է։»
          → answer: կատու.
        VAGUE/ABSTRACT — BAD vs GOOD:
        - BAD (multiple possible answers, abstract): «Չի երևում, բայց
          բոլորը զգում են։ Ի՞նչ է։» → could mean wind, love, time —
          unsolvable for a 5-year-old.
        - GOOD (one concrete answer): «Սառն է, ձեռքով բռնվում է, և
          ամառին հալվում է։ Ի՞նչ է։» → answer: պաղպաղակ.
        Create ORIGINAL riddles each turn — vary the answer across
        animals, fruits, weather, household objects, foods, clothing.
        Aim for variety: ձու, խնձոր, ձյուն, անձրև, ամպ, գնդակ, գդալ,
        բարձ, գուլպա, գազար, շուն, ձուկ. Every riddle must describe
        something the child can see, hear, touch, or taste in daily
        life. Use physical clues only.

        HINT AND CELEBRATION SHAPE:
        - After a wrong guess, give ONE warm, short Armenian hint that
          adds a NEW concrete clue (do NOT repeat the original clue).
          The ANSWER LEAK rule applies equally to hints — never name
          the answer, its category word, or a close synonym (if the
          answer is «բալ», do NOT say «ալուբալի» or «պտուղ» by name).
          Only open with «Մոտ ես» when the child's guess is actually
          close; otherwise open warmly without claiming closeness.
          Example: «Ուշադիր նայիր. այս բանը ձմռանը երկնքից է գալիս։»
        - After a correct guess, celebrate briefly and offer the next
          riddle in one short line. Example: «Ապրե՛ս, ճիշտ էր։ Ուզու՞մ
          ես ևս մեկ հանելուկ։»

        ARMENIAN LANGUAGE — STRICT:
        Use natural, spoken Eastern Armenian a child hears at home.
        Every word must be real, everyday Armenian. Do NOT invent words.
        Do NOT translate literally from English.
        Prefer simple, playful words a child knows.
        BAD: "ուսումնասիրել" (formal — say "նայել")
        BAD: "հայտնաբերեց" (bookish — say "գտավ")
        BAD: "պսպղացող" (literary — say "փայլուն")
        Every word must be a REAL Armenian word. Verify every word.
        If any word sounds strange to a 5-year-old, use a simpler word.
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

    // Active Game/Riddle session tracker. When ModeDetector returns None but an
    // active game/riddle was recently running, the session continues. Story uses
    // PendingChoices for this; Game/Riddle have no per-turn state to carry, so a
    // simple mode+timestamp is enough. 30-minute expiry matches ChoiceExpiry.
    internal static readonly ConcurrentDictionary<Guid, ActiveModeEntry> ActiveModes = new();
    internal record ActiveModeEntry(DetectedMode Mode, DateTime ActivatedAt);

    internal const string DefaultFallbackResponse =
        "\u0531\u0580\u056b, \u0574\u056b \u0578\u0582\u0580\u056b\u0577 \u0570\u0565\u057f\u0561\u0584\u0580\u0584\u056b\u0580 \u0562\u0561\u0576 \u056d\u0578\u057d\u0565\u0576\u0584\u0589";

    internal const string CalmFallbackResponse =
        "\u0554\u0576\u056b\u0580 \u0570\u0561\u0576\u0563\u056b\u057d\u057f, \u0561\u0574\u0565\u0576 \u056b\u0576\u0579 \u056c\u0561\u057e \u0567\u0589";

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

        // Step 1.5: Client-side dangerous-content prefilter. Catches obvious
        // weapon/explosive/poison/drug keywords that the moderation API misses
        // (e.g. "how to make a bomb" returns Flagged=false on all categories).
        if (DangerousInputFilter.IsUnsafe(userMessage))
        {
            _logger.LogWarning("Dangerous input prefilter triggered. Device: {DeviceId}, Preview: {Preview}",
                deviceId, userMessage.Length > 80 ? userMessage[..80] + "..." : userMessage);

            await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.User, userMessage, SafetyFlag.Blocked);

            var prefilterFallback = _config["SafetyFallbackResponse"]
                ?? "Արdelays, delays delays delays delays delays:";
            var prefilterMsg = await _conversations.AddMessageAsync(
                conversation.Id, MessageRole.Assistant, prefilterFallback, SafetyFlag.Clean);

            return new ChatResponse(prefilterFallback, conversation.Id, prefilterMsg.Id, SafetyFlag.Blocked);
        }

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

        // Step 7: Detect conversation mode. ModeDetector priority: calm > curiosity
        // > active story > explicit trigger > history > none. See .claude/MODES.md.
        var detectedMode = explicitStoryContinuation
            ? DetectedMode.Story
            : ModeDetector.Detect(userMessage, history,
                hasActiveStorySession: pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry);

        // Step 7a: Game/Riddle session persistence. When ModeDetector finds no trigger
        // but an active game/riddle was running recently, continue that mode.
        if (detectedMode == DetectedMode.None
            && ActiveModes.TryGetValue(conversation.Id, out var activeMode)
            && DateTime.UtcNow - activeMode.ActivatedAt < ChoiceExpiry
            && activeMode.Mode is DetectedMode.Game or DetectedMode.Riddle)
        {
            detectedMode = activeMode.Mode;
        }

        // Update active-mode tracker: store on Game/Riddle, clear on anything else.
        if (detectedMode is DetectedMode.Game or DetectedMode.Riddle)
            ActiveModes[conversation.Id] = new ActiveModeEntry(detectedMode, DateTime.UtcNow);
        else
            ActiveModes.TryRemove(conversation.Id, out _);

        bool isStoryMode = detectedMode == DetectedMode.Story;
        if (isStoryMode)
        {
            systemPrompt += StoryChoiceInstruction;

            // Step 7a-bis: Inject story memory for character/place/mood continuity.
            if (StoryMemories.TryGetValue(conversation.Id, out var storyMemory))
            {
                var memoryLines = new List<string>();
                if (storyMemory.Character is not null) memoryLines.Add($"- Character: {storyMemory.Character}");
                if (storyMemory.Place is not null) memoryLines.Add($"- Place: {storyMemory.Place}");
                if (storyMemory.ImportantObject is not null) memoryLines.Add($"- Key object: {storyMemory.ImportantObject}");
                if (storyMemory.CurrentSituation is not null) memoryLines.Add($"- Situation: {storyMemory.CurrentSituation}");
                if (storyMemory.Mood is not null) memoryLines.Add($"- Mood: {storyMemory.Mood}");
                if (memoryLines.Count > 0)
                {
                    systemPrompt += "\n\nCURRENT STORY STATE (use this for consistency, do not repeat it verbatim):\n"
                        + string.Join("\n", memoryLines);
                }
            }

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
        else if (detectedMode == DetectedMode.Calm)
        {
            systemPrompt += CalmModeInstruction;
        }
        else if (detectedMode == DetectedMode.Curiosity)
        {
            systemPrompt += CuriosityWindowInstruction;

            // Preserve pending choices so the story can resume after this one-turn detour.
            if (pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry)
            {
                PendingChoices[conversation.Id] = pending;
            }
        }
        else if (detectedMode == DetectedMode.Game)
        {
            systemPrompt += GameModeInstruction;
        }
        else if (detectedMode == DetectedMode.Riddle)
        {
            systemPrompt += RiddleModeInstruction;
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
            var configFallback = _config["SafetyFallbackResponse"];
            aiResponse = detectedMode == DetectedMode.Calm
                ? CalmFallbackResponse
                : string.IsNullOrEmpty(configFallback)
                    ? DefaultFallbackResponse
                    : configFallback!;
        }

        // Step 10a: Strip STORY_MEMORY block (always — cleans leaked markers). Only store
        // structured memory for story mode; non-story modes must not pollute story state.
        StoryMemoryParser.TryExtract(aiResponse, out var memStripped, out var newMemory);
        aiResponse = memStripped;
        if (isStoryMode && newMemory is not null)
        {
            StoryMemories.AddOrUpdate(
                conversation.Id,
                newMemory,
                (_, existing) => StoryMemoryParser.Merge(existing, newMemory));
            _logger.LogInformation(
                "Story memory updated. ConversationId: {ConversationId}, Character: {Character}, Place: {Place}",
                conversation.Id, newMemory.Character, newMemory.Place);
        }

        // Step 10b: Strip tail block (always — cleans leaked markers). Only store
        // choice labels for story mode; non-story modes must not create pending choices.
        string? choiceA = null, choiceB = null;
        if (TailBlockParser.TryExtract(aiResponse, out var cleanedResponse, out var optionA, out var optionB))
        {
            aiResponse = cleanedResponse;
            if (isStoryMode)
            {
                PendingChoices[conversation.Id] = new PendingChoice(optionA!, optionB!, DateTime.UtcNow);
                _logger.LogInformation(
                    "Story choice extracted. ConversationId: {ConversationId}, OptionA: {OptionA}, OptionB: {OptionB}",
                    conversation.Id, optionA, optionB);
                choiceA = optionA;
                choiceB = optionB;
            }
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
            var retryReason = ResponseQualityGate.CheckRetry(aiResponse, userMessage, detectedMode);
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
                        if (isStoryMode && rMem is not null)
                        {
                            StoryMemories.AddOrUpdate(
                                conversation.Id,
                                rMem,
                                (_, existing) => StoryMemoryParser.Merge(existing, rMem));
                        }

                        // Re-run tail-block extraction (always strip, only store in story mode).
                        if (TailBlockParser.TryExtract(retryResp, out var rCleaned, out var rA, out var rB))
                        {
                            retryResp = rCleaned;
                            if (isStoryMode)
                            {
                                PendingChoices[conversation.Id] = new PendingChoice(rA!, rB!, DateTime.UtcNow);
                                choiceA = rA;
                                choiceB = rB;
                            }
                        }

                        aiResponse = retryResp;
                        _logger.LogInformation(
                            "Quality gate retry accepted. ConversationId: {ConversationId}",
                            conversation.Id);
                    }
                    else
                    {
                        // Retry was rejected by moderation. The original response
                        // (which triggered the retry — i.e. had latin_run, leaked_tag,
                        // or a mode-policy violation) would otherwise be silently
                        // kept and shown to the child. For the two structurally-broken
                        // categories that would leak English text or internal format
                        // markers, fall back to the safety response instead.
                        if (retryReason is "latin_run" or "leaked_tag")
                        {
                            _logger.LogWarning(
                                "Retry rejected by moderation and original is structurally broken. ConversationId: {ConversationId}, Reason: {Reason}. Using safety fallback.",
                                conversation.Id, retryReason);
                            var hardFallback = _config["SafetyFallbackResponse"]
                                ?? DefaultFallbackResponse;
                            aiResponse = detectedMode == DetectedMode.Calm ? CalmFallbackResponse : hardFallback;
                            choiceA = null;
                            choiceB = null;
                            PendingChoices.TryRemove(conversation.Id, out _);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Quality gate retry rejected by moderation. ConversationId: {ConversationId}",
                                conversation.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Quality gate retry failed. ConversationId: {ConversationId}",
                        conversation.Id);
                }
            }

            // Step 10c-quater: Post-retry latin_run hard recheck.
            // Closes the failure mode where the retry passes moderation but
            // still contains 4+ Latin letters (model stuck on a foreign word).
            // Scoped intentionally to latin_run only — leaked_tag is handled
            // downstream by ResponseCleaner. Soft issues (subject_mismatch,
            // length, mode punctuation) are NOT escalated.
            if (ResponseQualityGate.CheckRetry(aiResponse, userMessage) == "latin_run")
            {
                _logger.LogWarning(
                    "Post-retry latin_run persists. ConversationId: {ConversationId}. Using safety fallback.",
                    conversation.Id);
                var latinFallback = _config["SafetyFallbackResponse"]
                    ?? DefaultFallbackResponse;
                aiResponse = detectedMode == DetectedMode.Calm ? CalmFallbackResponse : latinFallback;
                choiceA = null;
                choiceB = null;
                PendingChoices.TryRemove(conversation.Id, out _);
            }

            // Step 10c-quinto: Choice-label latin_run check.
            // The post-retry recheck above only inspects the prose. Extracted
            // choiceA/choiceB strings reach the child unchanged, so a model
            // could leak Latin via "CHOICE_A:Find the fox" style labels.
            // If either choice has a 4+ Latin run, drop BOTH (we can't show
            // one without the other) and clear the pending choice entry.
            // The story prose is preserved.
            if ((choiceA is not null && System.Text.RegularExpressions.Regex.IsMatch(choiceA, @"[A-Za-z]{4,}"))
                || (choiceB is not null && System.Text.RegularExpressions.Regex.IsMatch(choiceB, @"[A-Za-z]{4,}")))
            {
                _logger.LogWarning(
                    "Choice label latin_run detected. ConversationId: {ConversationId}. Dropping choices.",
                    conversation.Id);
                choiceA = null;
                choiceB = null;
                PendingChoices.TryRemove(conversation.Id, out _);
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

        // Step 10f: Mode-specific punctuation cleanup. Belt-and-suspenders for
        // when the quality gate retry also produces forbidden punctuation.
        // Replaces forbidden marks with Armenian period (verjaket ։ U+0589).
        if (detectedMode == DetectedMode.Calm)
        {
            aiResponse = aiResponse.Replace('?', '\u0589').Replace('\u055E', '\u0589')
                                   .Replace('!', '\u0589').Replace('\u055C', '\u0589');
        }
        else if (detectedMode == DetectedMode.Curiosity)
        {
            aiResponse = aiResponse.Replace('?', '\u0589').Replace('\u055E', '\u0589');
        }

        // Step 10g: Collapse doubled Armenian period (verjaket ։) that can appear
        // when punctuation replacement inserts ։ next to an existing one.
        if (detectedMode is DetectedMode.Calm or DetectedMode.Curiosity)
        {
            aiResponse = aiResponse.Replace("\u0589\u0589", "\u0589");
        }

        // Step 11: Store AI response
        var responseMsg = await _conversations.AddMessageAsync(
            conversation.Id, MessageRole.Assistant, aiResponse, safetyFlag);

        // Set storySessionId when story choices are present (active story mode)
        Guid? activeStorySession = (choiceA != null || choiceB != null) ? conversation.Id : null;

        var modeName = detectedMode == DetectedMode.None ? null : detectedMode.ToString().ToLowerInvariant();
        return new ChatResponse(aiResponse, conversation.Id, responseMsg.Id, safetyFlag, choiceA, choiceB, activeStorySession, modeName);
    }

}

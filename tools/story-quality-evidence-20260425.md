# Story Voice MVP — Phase 1 evidence capture (2026-04-25)

Captured outputs of the current backend's Story flow on a fixed
prompt set. **No grading in this document. No code changes,
no prompt changes, no commits.** Tomorrow's evaluator pass will
score these.

## Capture conditions

- **Backend**: `dotnet run --project src/ArmenianAiToy.Api` (already
  running before capture started; not restarted).
- **Working tree state at capture time**: same as the start-of-session
  `git status` — no files added or staged. Modified files include
  `Controllers/AudioChatController.cs`, `Helpers/ModeDetector.cs`,
  and the new `Audio/AudioStoryResponseComposer.cs`. **Not reverted
  for this capture** — by request, the evidence reflects whatever
  the current branch produces today.
- **Devices**: two newly-registered devices (text + audio) so the
  capture didn't pollute any pre-existing conversation.
  - Text device id: `55a8c16a-9819-430c-aff6-e6d5ab5093e2`
  - Audio device id: `0e805979-2713-408e-9047-1776d80da5a0`
- **Conversation continuity**: each device used a single conversation
  for the whole batch. The 10 text prompts therefore share one
  `storySessionId` (`4effbf52-7bf2-4180-b89c-143944e75708`) and were
  classified as **continuations of one story session**, not 10
  fresh story openings. Notable enough to flag here so the evaluator
  doesn't treat each row as an independent first-turn opening.
- **No child profile** attached on either device.
- **Source data**: per-case JSON files under
  `C:\tmp\story-evidence\` (`case-01.json` … `case-10.json`,
  `audio-case-01.json`, `audio-rows.json`, MP3 at
  `audio-case-01.mp3`). The script that drove the capture is at
  `C:\Users\hayk.margaryan\AppData\Local\Temp\story-evidence\run_prompts.py`.

## Cases 01–10 — text endpoint (`POST /api/chat`)

All ten returned **HTTP 200** with `safetyFlag = 0` (Clean), `mode =
"story"`, both `choiceA` and `choiceB` populated, and the same
`conversationId` / `storySessionId`.

### Case 01

- **Prompt**: `պատմիր հեքիաթ`
- **Endpoint**: `POST /api/chat`
- **Detected message used**: `պատմիր հեքիաթ` (text path — no STT)
- **HTTP**: 200 in 13.27s
- **Mode**: `story`
- **Canonical text**:
  > Պոքրիկ նապաստակիկը ցատկեց սաղարթի վրայից ու տեսավ պայծառ ծիածանը։ Նա ուրախացավ ու սկսեց վազվզել ծաղկապարտեզի միջով։ Մոտակայքում կար մի փոքրիկ լիճ, որտեղ շրջում էր կարմիր ձկնիկ։ Ծիածանն արտացոլվում էր ջրում՝ ստեղծելով հեքիաթային տեսարան։
- **ChoiceA**: `Կանչենք նապաստակիկի հետ`
- **ChoiceB**: `Դիտենք ծիածանի փայլը`
- **MessageId**: `7a7c697d-2264-4f69-a352-333cf44d2fd2`

### Case 02

- **Prompt**: `Ես ուզում եմ հեքիաթ լսել`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 9.35s
- **Mode**: `story`
- **Canonical text**:
  > Նապաստակիկը սիրտդ պարզ աշխույժով վազում էր դեպի ծիածանը։ Նա զգաց, որ ծաղիկների բույրը նրան հոգի է փչում։ Երբ նա կանգ առավ, տեսավ մի ջինջ կաթիլի վերևում փայլող սև կարմիր ժպիտով արևը։ Նապաստակիկն իր աչքերը բարևեց և սկսեց մտածել, թե ինչ հրաշալիքներ կարող է լինել ծիածանի տակ։
- **ChoiceA**: `Մտնենք դռնից ներս`
- **ChoiceB**: `Հարցնենք քամուց՝ ինչ կա ներսում`
- **MessageId**: `65e7cb03-e297-4515-bd52-c162b307f7fb`

### Case 03

- **Prompt**: `պատմիր հեքիաթ արջուկի մասին`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 8.45s
- **Mode**: `story`
- **Canonical text**:
  > Արջուկը մի առավոտ արթնացավ ու տեսավ, որ ամպերը պայծառ են և նման են բամբակի գնդիկների։ Նա որոշեց գնալ անտառի խորքը, որտեղ նրան սպասում էր հին մեծ ծառը՝ իր գաղտնի հեքիաթներով։ Ճանապարհին նա հանդիպեց երախայրի ծաղիկների մի դաշտ, որտեղ տատիկի խնամքով սիգապառոշ ծաղիկները պար էին է անում։ Արջուկի սիրտը լցվեց ուրախությամբ, երբ լսեց ծիտիկների երգը։
- **ChoiceA**: `Խաղանք արջուկի հետ`
- **ChoiceB**: `Գնանք անտառը`
- **MessageId**: `eee4e513-3580-4c04-acb7-98c948a300b3`

### Case 04

- **Prompt**: `պատմիր հեքիաթ նապաստակի մասին`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 9.16s
- **Mode**: `story`
- **Canonical text**:
  > Փոքրիկ նապաստակն արեւի տակ կռված էր սաղարթների տակ։ Նա տեսավ կարմիր սրտիքով մի թիթեռ, որ սիրով պտտվում էր ծաղիկների մոտ։ Նապաստակը զարմացավ ու սկսեց հետևել թիթեռին։ Հանկարծ թիթեռնիկը կանգնեց և կարծես խաղով կանչեց նրան։ Թիթեռնիկի ամեն թռիչք կարծես փոքրիկ հեքիաթ էր։
- **ChoiceA**: `Հետևենք թիթեռնիկին`
- **ChoiceB**: `Նայենք ծաղիկներին`
- **MessageId**: `6ec8f254-c8ed-4672-aa3c-fdd48bf1bb7b`

### Case 05

- **Prompt**: `պատմիր հեքիաթ անտառի մասին`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 23.34s
- **Mode**: `story`
- **Canonical text** (verbatim — note the embedded non-Armenian characters):
  > Մութ ու խիտ անտառում կար մի փոքրիկ արահետ։ Այդ արահետով քայլում էր մի խելացի սկյուռիկ՝ իր պ федера տերևը ձեռքին। Սկյուռիկն ուզում էր գտնել ճանապարհը դեպի անտառի գաղտնի ծառը, որի վրա կախարդական ընկույզներ էին աճում։ Ամեն անգամ, երբ քամին շարժում էր ծառերի ճյուղերը, մի հրաշալի մեղեդի էր լսվում։ Սկյուռիկը զարմացած կանգ առավ ու սկսեց լսել այդ մեղեդին։
- **ChoiceA**: `Խաղանք ծաղիկների հետ`
- **ChoiceB**: `Համտեսենք ջրվեժի ջուրը`
- **MessageId**: `be5429a6-732f-48c0-b877-1bfeaf502a63`

### Case 06

- **Prompt**: `պատմիր կախարդական հեքիաթ`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 23.96s
- **Mode**: `story`
- **Canonical text**:
  > Մի հեռավոր անտառում կար կախարդական լիճ, որի ջրերը փայլում էին լուսնի փայլով։ Լճի կողքին ապրում էր մի փոքրիկ ջրային աղջիկ, ով սիրում էր պատմել հեքիաթներ։ Մի երեկո, երբ աղջիկը երգում էր, ջրից դուրս եկավ մի հրաշալի կրիա՝ իր բյուրեղյա խեցիով։ Խեցու տակ թաքնված էր կախարդական մնջախաղի հուշանը։ Ջրային աղջիկը զարմացած նայեց։
- **ChoiceA**: `Լսենք աղջկա երգը`
- **ChoiceB**: `Քննենք կրիային`
- **MessageId**: `7ca39aad-2b3f-403a-9484-4fb83c07e8d0`

### Case 07

- **Prompt**: `պատմիր հեքիաթ մի փոքրիկ աղջկա մասին`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 39.45s
- **Mode**: `story`
- **Canonical text**:
  > Երբ ջրային աղջիկը սկսեց երգել, նրա ձայնը նման էր ջրի մեղմ հոսքին։ Լճի միջով շեղող ալիքները սկսեցին փայլել ավելի պայծառ։ Հանկարծ լճի հայելու մեջ նորաձև կերպարանք հայտնվեց։ Դա մի բարի թափառող հողմիկ էր, որ եկել էր լսելու աղջկա հրաշալի երգը։
- **ChoiceA**: `Խոսենք հողմիկի հետ`
- **ChoiceB**: `Նայենք լճի հայելու մեջ`
- **MessageId**: `f935d836-d9b9-4fe6-91d6-12d1aafc4391`

### Case 08

- **Prompt**: `պատմիր հեքիաթ ֆերմայի մասին`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 25.88s
- **Mode**: `story`
- **Canonical text**:
  > Մի ամբողջական դաշտի կենտրոնում կար մի գեղեցիկ ֆերմա։ Ֆերմայում ապրում էին տարբեր կենդանիներ՝ խոզուկներ, կովուկներ և ծիծաղելի հավիկներ։ Ամեն առավոտ խոզուկները խաղում էին ցեխի մեջ՝ ուրախ ու անուշ զվարթությամբ։ Հավիկները ծղրտում էին ավելի զվարթ, իսկ կովուկները հանգիստ մթությանը շարկել էին։ Բայց մի նապաստակ էլ կար, որ սիրում էր նայել ֆերմայից դուրս։
- **ChoiceA**: `Հետևենք թռչնակներին`
- **ChoiceB**: `Խաղանք բադիկների հետ`
- **MessageId**: `06131691-9794-410f-b396-4abdabd2cdd0`

### Case 09

- **Prompt**: `պատմիր արկածային պատմություն`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 26.82s
- **Mode**: `story`
- **Canonical text**:
  > Փոքրիկ արջուկը որոշեց գիշերը գնալ անտառի խորքը։ Նա լսել էր, որ այնտեղ կա կախարդական ջրվեժ, որը փայլում է աստղերի լույսի տակ։ Երբ արջուկը հայտնվեց ջրվեժի մոտ, նա զարմանքից բերանը բացեց։ Ջրերով պտտվում էին փոքրիկ լուսավոր փուչիկներ, որ կարծես թռչունների ձայնով էին երգում։ Արջուկը կանգնեց ու մտածեց, թե ինչու են այդ փուչիկները այդպես կախարդական։
- **ChoiceA**: `Հետևել քարտեզին դեպի կղզի`
- **ChoiceB**: `Որոնել գանձը տնից դուրս`
- **MessageId**: `35eb30c1-8f65-4138-9584-9b2e476935dd`

### Case 10

- **Prompt**: `ինչ կլինի եթե փոքրիկ թիթեռը կորի`
- **Endpoint**: `POST /api/chat`
- **HTTP**: 200 in 28.07s
- **Mode**: `story`
- **Canonical text**:
  > Փոքրիկ թիթեռը թևերի տակ սայթաքեց ու անսպասելի կորավ ծաղիկների ծովում։ Նապաստակը մի պահ վախեցավ և սկսեց փնտրել նրան իր կյանքի ծաղիկների մեջ։ Բայց հենց այդ պահին արեւի շողերից մեկը փայլեց, ու թիթեռը հայտնվեց՝ հստակ ու պայծառ։ Նապաստակը հանգստացավ և մոտեցավ իր փոքրիկ ընկերոջը, որ նույնքան ուշադիր նայում էր։ Նրանք նորից միասին էին։
- **ChoiceA**: `Հետևենք թիթեռին`
- **ChoiceB**: `Զրուցենք թիթեռի հետ`
- **MessageId**: `ccf5c5c9-3b20-41c9-bb00-fba042ab64c8`

## Audio case 11 — voice endpoint (`POST /api/chat/audio`)

One real-WAV round-trip to verify the voice path end-to-end (STT
→ ChatService → TTS → blob persistence). Limited to one call to
keep STT + TTS cost minimal as requested.

- **Endpoint**: `POST /api/chat/audio`
- **WAV input**: a 90,190-byte 16 kHz mono PCM clip from a
  previous test session
  (`backend/src/ArmenianAiToy.Api/audio-blobs/5d7ff0e222fe411f88189b03d8c9c4b9/f1360377596c4493a72e09b26e942395.wav`).
  No new audio was synthesized for this evidence run.
- **HTTP**: 200 in ~16.8s, `Content-Type: audio/mpeg`,
  537,600 bytes MP3 saved to `C:\tmp\story-evidence\audio-case-01.mp3`.
- **Detected message used (Whisper Armenian transcript,
  persisted as the user `Message.Content`)**:
  > Բատմիր հեկերդ։
- **Assistant canonical text** (persisted as `Message.Content`,
  tail block already stripped by `ChatService` before persistence):
  > Բարձր լեռան վրա կար մի փոքրիկ տուն: Այնտեղ ապրում էր մի գեղեցիկ կատու, ում անունն էր Մաշա: Մի օր Մաշան որոշեց գնալ դաշտ, որպեսզի բալասան ծաղիկներ հավաքի: Արևի գույները խաղում էին խոտի վրա, իսկ Մաշան ուրախացավ այդպիսին պայծառ օրվան տեսնելով: Մեղուները թռչում էին, մեղմ զրնգոցով երգելով:
- **ChoiceA / ChoiceB**: not directly recoverable from the DB — the
  audio endpoint does not persist them; they are consumed in-memory
  by `AudioStoryResponseComposer` and are spoken in the MP3 only.
  Listen to `audio-case-01.mp3` to confirm the «Ի՞նչ անենք՝ առաջինը՝
  …, թե՞ երկրորդը՝ …։» bridge fired.
- **Mode**: `story` (gate decision derived from the Story-only
  pre-STT check in `AudioChatController`).
- **AudioBlobPaths persisted on both messages**:
  - User: `169ce9651ae64cf0a3ad587f44f73e9e\6ee388098fae451a804ee7d82919f9ea.wav`
  - Assistant: `169ce9651ae64cf0a3ad587f44f73e9e\d6e3fd48dbd8428ea75be7bfd6ceb6c8.mp3`
- **MessageIds**:
  - User: `6EE38809-8FAE-451A-804E-E7D82919F9EA`
  - Assistant: `D6E3FD48-DBD8-428E-A75B-E7BFD6CEB6C8`

## Fallback-choice / structured logs

The backend was already running in another terminal at the start
of this session, so I do not have access to its stdout-JSON
buffer. No `aat_*` log lines or fallback-choice traces were
captured for these cases. Tomorrow's evaluator pass can either
re-run with stdout captured to a file, or read the existing
counters via `GET /metrics` (token-gated) for an aggregate view.

## Endpoint mix summary

| Cases | Endpoint                | Count |
|-------|-------------------------|-------|
| 01–10 | `POST /api/chat`        | 10    |
| 11    | `POST /api/chat/audio`  | 1     |

## Observations (no grading)

A few things to keep in mind when scoring tomorrow — these are
flagged so the evaluator's reading aligns with what was on the
wire, not so they shift the score either direction:

1. **One conversation, ten turns.** Cases 02–10 were *continuations*
   of the case-01 story session, not independent first-turn openings.
   The evaluator should either re-capture each prompt from a fresh
   conversation (different `storySessionId`s) or evaluate the batch
   as a 10-turn arc rather than 10 isolated openings.
2. **Mid-text non-Armenian glyphs in case 05.** The canonical text
   contains the literal sequence «պ федера» (Cyrillic fragment) and
   ends a sentence with «।» (Devanagari danda) instead of the
   Armenian full stop «։».
3. **Choice block / story body decoupling in case 09.** The body
   describes a bear at a magical waterfall; ChoiceA/ChoiceB are
   "follow the map to an island" / "search for the treasure outside
   the house" — neither references the bear or the waterfall.
   Same shape, weaker, in case 08 (story body covers pigs / cows /
   chickens; choices mention ducks and birds not present in the
   body). Possible signal of the fallback-choice path firing —
   logs would confirm but were not captured.
4. **Audio round-trip works end-to-end.** STT returned an
   intelligible Armenian sentence (though phonetically corrupted —
   "Բատմիր հեկերդ" instead of "Պատմիր հեքիաթ"); ChatService still
   classified it as Story; both blobs landed; MP3 has plausible
   length for a 5-sentence story.
5. **Audio assistant text uses a Russian-loaned name "Մաշա" (Masha)**
   for the protagonist — flag for the linguistic reviewer in the
   "Armenian-first" check, not a code issue.
6. **Repeated character set across the batch.** "Նապաստակ" (rabbit)
   and "Արջուկ" (bear cub) appear in most cases including ones
   whose prompts asked for unrelated subjects (forest, magical
   tale, farm, adventure). Likely an artifact of the shared
   `storySessionId` more than of base story-generation diversity.

## Reproducer

Re-run text capture (assumes a backend already on
`http://localhost:5000` and a registered device — fill in the
two literals at the top of the script):

```
/c/Python314/python /tmp/story-evidence/run_prompts.py
```

Audio call (assumes the same and a real WAV at the path baked into
the script):

```
/c/Python314/python /tmp/story-evidence/run_audio.py
```

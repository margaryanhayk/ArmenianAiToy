# The 70 per-story clips: heard, and four fixed (2026-09-03)

The clips shipped 2026-08-16 and had never been listened to — the repo's
standing rule is that no audio reaches a child until a human hears it.
They were unhearable without a toy in hand, so they were embedded into a
single phone page beside the text each is supposed to say
(`tools/story-audio/build_clip_listen_page.py`).

## Owner's result: 69/70 heard, 4 flagged

| Flag | Verdict |
|---|---|
| princess-and-pea / question2 | **Owner right.** «խանգարել» governs the dative; «քնելդ» was ungrammatical. The story's own text says «խանգարում էր իրեն». |
| sutasan / question1 | **Owner right, and worse than reported.** The old question asked why the king *couldn't* say «սուտ է» — segment 3 shows him saying «Սո՛ւտ ես ասում» and then «խոսքը փոխում է». It asked about something that never happened. |
| three-piglets / intro | **Owner right, different cause than first diagnosed.** The intro named no origin at all and stopped after the title. |
| ulik / intro | same |

Both flagged intros measured 1.9–2.4 s against 3.8–5.8 s for the authored
ones — proof by duration that no author line was in them, i.e. the audio
matched the "never guess an attribution" rule. The complaint was that
saying *nothing* sounds unfinished, which it does.

## Found during the review, not by the owner

- **sutasan reflectionConclusions[1] stated the wager backwards** — it
  said the king would have had to pay *the debt*; his own decree
  (segment 0) stakes *half the kingdom*. Wrong under the old question and
  the new one.
- **The conclusion TTS cache was keyed `storyId|questionIndex`**, with no
  text in the key. After a text fix, a running process would keep
  speaking the OLD line while the JSON on disk read correctly. Fixed to
  include the text.
- **The listen page itself showed the wrong reference text** for
  intro/offer/reoffer — 30 of 70 rows — because those strings were
  hand-reconstructed instead of read from the renderer's own sources.
  That is what turned two correct clips into flags. Fixed; the generator
  now reads `ElevenLabsRender/Program.cs`'s composition and
  `voice-clips.json`'s `_perStoryTemplates`.

## What changed

New optional `origin` field on the story schema (mirrors `author`,
mutually exclusive with it — pinned by three new parser tests). A story
nobody wrote now names its provenance instead of trailing off:

| Story | Intro now says |
|---|---|
| ulik | «Ժողովրդական հեքիաթ՝ «Ուլիկը»։» |
| three-piglets | «Անգլիական ժողովրդական հեքիաթ՝ «Երեք խոզուկները»։» |

`three-piglets` is deliberately **Անգլիական** ժողովրդական: bare
«ժողովրդական» would read as an *Armenian* folk tale, which its own review
notes contradict. The two in-project originals (hedgehog-apple,
little-cloud) keep the title-only intro — calling them folk would be
false, and the owner's ear correctly did not flag them.

Text (both Armenian reviewers approved, no edits):
- princess-and-pea Q3: «Իսկ դու ի՞նչ ես անում, երբ ինչ-որ բան խանգարում է հանգիստ քնելուդ։»
- sutasan Q2: «Ինչո՞ւ թագավորը փոխեց իր խոսքը։»
- sutasan takeaway 2: «Որովհետև եթե ասեր՝ «սուտ է», թագավորության կեսը պիտի տար․ գյուղացու խելքը հաղթեց։»

## Render

4 clips, 174 characters, `eleven_v3`, the storyteller voice, one request
each — all came back at expected length:

| Clip | sha256 (first 12) | bytes |
|---|---|---|
| ulik/intro | f7ddb7368966 | 35,108 |
| three-piglets/intro | 5afa12ba9d7c | 51,826 |
| princess-and-pea/question2 | 884e9e8dabbc | 61,857 |
| sutasan/question1 | a2457b1d3b46 | 33,854 |

No story `Version` bumped: clip freshness is judged per-clip by the
firmware, so toys fetch four small files rather than re-downloading the
narration. All 70 config entries verified against the files on disk.
Tests: 2765 passed.

## NOT verified

- **Nobody has heard the four new clips.** They are back on the listen
  page for the owner's ear; until then this is a render that passed a
  length check, not an approved one.
- `ulik/question` is still the one clip never played.
- The conclusion line is spoken by live TTS, not a clip, so it has no
  render to check — it will be heard the first time a child answers that
  question.

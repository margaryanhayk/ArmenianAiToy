# Ուլիկը cast pilot — one story, four voices (2026-09-03)

Owner decision, same day: characters get real voices instead of a
pitch-shifted Areg. Rules: the narrator is always Areg; Katrin and Vardan
take a part in every story; library (English) voices only for villains and
animals, because they speak Armenian with an accent.

## What was rendered

`tools/story-voices/render_story.py ulik` with the cast in
`backend/content/story-voices/ulik.voices.json` — all four on
`eleven_v3_conversational`, the model the owner preferred for both clones:

| Speaker | Voice | How chosen |
|---|---|---|
| narrator | areg-storyteller | rule |
| mother | katrin-v3 | owner's pick from all 34 account voices |
| ulik | vardan-v2, pitch 1.10 | owner's pick; lifted to sound small |
| wolf | **areg-wolf** `5WzpQqbTMzWBCHk0Mol1` | see below |

15 spans, every span inside the renderer's length and tail gates. Forest
beds and both door knocks mixed with `mix_ambience.py` from the per-segment
WAVs and a fresh forced alignment; levelled to -16.4 LUFS with ffmpeg
`loudnorm` for the listening copy. 2:23. Handed to the owner as
`ulik-pilot-final.mp3`. **Not shipped**: `story-audio/ulik.mp3` and its
marker are untouched.

## The wolf took four rounds

1. Callum (library, "husky trickster"): the owner called it compatible but
   too dramatic; three faster, flatter renders of the same voice — rejected.
2. Seven other library voices (Brian, Adam, Bill, Charlie, Harry, Roger,
   Terry): all rejected. The common fault is the English accent in Armenian,
   which no voice_settings change removes.
3. ElevenLabs' shared voice library has **zero** voices tagged `hy`.
4. **Voice Design** (`POST /v1/text-to-voice/design`, `eleven_ttv_v3`) from a
   description — "deep husky wolf pretending to be a gentle mother goat,
   speaks Armenian natively" — with the wolf's own Armenian line as the
   preview text. Three previews; the owner picked the first; saved to the
   account as `areg-wolf` (`POST /v1/text-to-voice`). No source person, so
   nothing to license and no accent inherited from one. This is the route
   for every future villain and animal.

Also offered, not taken: Areg and Vardan pitched down 15% (native, but
still recognisably them), and the owner recording the wolf himself.

## Known soft spot

The second knock («դուռը ծեծում», segment 4) anchored at 2.97 s into the
segment; the narrator's line ends at 2.48 s and the mother starts at 2.88 s,
so the cut for the inserted knock falls ~90 ms into her first word. The
forced alignment drifted badly over this render (-29.6 s by the end,
against -0.6 s per segment on the single-voice library), most likely the
designed wolf and the pitch-lifted Ulik confusing the aligner. The cast
renderer already writes an exact per-span map (`ulik.spans.json`); the
mixer should prefer it over alignment when a `landOn` phrase is the tail of
a span. Not fixed in this pilot — the owner listens first.

## Cost

Roughly 1,600 characters per full render, three full renders plus ~20
wolf auditions ≈ 7,000 characters. One forced alignment per render.

## 2026-09-04 — the knocks were late, and it was the mixer, not the aligner

The owner heard both knocks land wrong. Measured against a per-span forced
alignment (one voice, a few seconds each — exact): «դուռը զարկում» ends at
3.08 s into segment 2, the knock was cut in at 3.81 s; «դուռը ծեծում» ends at
2.44 s into segment 4, the knock at 2.97 s. Both late, both by about half a
second, the second one inside the mother's first word.

Cause: in `--segments-dir` mode `mix_ambience.py` looked for the word map in
`Path(".")` — the repo root — found nothing, and fell back to the SHIPPED
story's `story-audio/ulik.words.json`, the alignment of a different
recording. The "-29.6 s drift" in the first pilot's log was that: two
unrelated timelines being reconciled. The soft spot recorded above was
misdiagnosed as aligner drift.

Fixes, both in this commit:
- `mix_ambience.py` reads the word map from the segments directory in
  segments mode, and when a map declares `"exact": true` it applies no drift
  correction and floors each phrase search at the file-time segment start.
- `tools/story-voices/align_spans.py` — one forced alignment per span, shifted
  by the renderer's own span map into the mixer's timeline, written as an
  exact map. Whole-story alignment of a multi-voice render is not trusted for
  cue placement any more.

Re-mixed: knocks at 3.16 s and 2.60 s into their segments (inside the pause
after the word, before the next one). Mixer self-test passes. `ulik-pilot-v2`
went to the owner.

## 2026-09-04, round 2 — pause, knock, pause; two knocks; a door that opens

Owner on v2/v3: the wolf's knock still wrong ("we can pause, knock, pause,
and continue"), the two knocks should differ and run a little longer, keep
the behind-the-door voices (v3), add a door-open sound. Done as v4:

- Three sounds generated (ElevenLabs sound-generation, prompts in
  `sounds/ulik/prompts.json`): `door-knock-heavy` (the wolf, 2.1 s of
  strikes), `door-knock-soft` (the mother), `door-open` (latch + creak).
  The August decision that both knocks are the same sound is retired by the
  owner's ear; the cue sheet says so at the wolf's cue.
- Mixer: a cue may set `leadIn` / `tail` around its inserted sound (wolf:
  0.6 / 0.7 s, mother: 0.5 / 0.6 s), and a segment may carry more than one
  line cue — segment 4 now has the mother's knock AND the door opening for
  her. Line positions are keyed per cue with the per-segment key kept as
  the fallback the self-test exercises.
- Behind-the-door voices are applied to the per-segment WAVs on span-local
  times before mixing, so the inserts cannot shift them.

Placements, all from the exact per-span map: door-open at 2.98 s into seg 1
(«դուռը բաց անում»), heavy knock at 3.16 s into seg 2, soft knock at 2.60 s
into seg 4, door-open at 18.71 s into seg 4 («դուռը բաց է անում»). 2:30.
Self-test passes. Not shipped; v4 is with the owner.

## 2026-09-04, round 3 — no muffling, knock after the sentence, one bad «և»

Owner on v4: (1) drop the behind-the-door effect; (2) the wolf's knock was
cut badly — the cut after «զարկում» clipped the tiny «ու» that follows;
(3) a voice bug in the opening: «Արածում է և իրիկունը…» came out as
«Արածում է shshsh…».

- (1) v5 mixes from the unfiltered segments. The effect stays available as
  a technique (it is a one-line ffmpeg pass on a span) but is not used.
- (2) The wolf's cue now lands on «ձայնով կանչում», the END of the
  narrator's sentence: the cut falls in the 0.40 s speaker pause before the
  wolf sings, where no word can be clipped. Placed at 5.22 s into segment 2
  (the narrator span is 5.20 s). Rule for the cue sheet from this: prefer a
  landOn that ends a span; a mid-line landOn is only safe when the next
  word is not a one-letter one.
- (3) Confirmed by transcription (Scribe v2): the shipped take read
  «արածում է չնաչորսը և իրիկունը» — a spoken artefact where the text has
  «է և». Segment 0 was re-rendered once (both spans; the renderer's unit is
  the segment) and the new take transcribes clean, «արածում է և իրիկունը».
  The span map for segment 0 was rebuilt from the new WAV lengths with the
  renderer's own 0.40 s speaker pause, then align_spans re-run.

A rule worth keeping: transcribe every span after rendering and diff it
against the text — that is a cheap machine check for exactly this class of
glitch, and it would have caught «shshsh» before the owner did. Not yet
built into render_story.py.

v5: 2:29, with the owner.

## 2026-09-04, round 4 — a stray «hmm», and air after the door

Owner on v5: a «hmm» after «Ուլիկն իսկույն վեր է թռչում, դուռը բաց անում» that
is not in the story; and after the door opens for the mother («Ուլիկը
դուռը բաց է անում, կաթ է խմում…») the story must wait before going on.

- render_story.py now transcribes every span (Scribe v2) and refuses a take
  that contains a word the story does not, or reads too far from the text
  (WER > 0.35); it re-asks up to the same retry budget as the chopped-tail
  check. This is the machine check the round-3 note asked for — «shshsh» and
  «hmm» both show up as extra words. A key without speech_to_text skips it
  with a note instead of failing the render.
- Segment 1 re-rendered through that check; the first take was clean.
- Both door-open cues: tail 0.9 s (was 0.5), leadIn 0.3. Hole is now 2.32 s
  for 1.12 s of sound.

v6: 2:30, with the owner.

## 2026-09-04, round 5 — Ulik recast

Owner on v6: Ulik's voice "not pleasant, something not lovely". Ten
candidates rendered on his main line — Vardan plain and +6 %, vardan-test,
Katrin v3 plain and +6 %, katrin-rec1, katrin-v2, and three designed voices
(ElevenLabs refused a description containing "child"; "young goat
character" passed). Owner picked **vardan-test**, unshifted. The 10 % lift on
vardan-v2 is the likely culprit: pitch-shifting a clone is what sounded off.

The transcript guard earned its keep on the first run: vardan-test's first
two takes of «Էդ ո՞վ ես դու…» came back truncated (WER 0.45, then 0.74 —
the second stopped after nine words) and were re-asked automatically; the
third passed. The mother's segment-4 line was re-asked once for an extra
word. None of this reached the owner.

v7: 2:30, with the owner.

## 2026-09-04, round 6 — the refrain, and one knock only

Owner on v7: the mother's second «Սևուկ ուլիկ, Սիրուն բալիկ» was "very
split" — chopped where it should be sung, «Սևուուուկ… Սիրուուուն…»; and the
last knock (the mother's) should go.

- The mother's two songs are byte-identical in the story text, so her
  segment-4 span now reuses the segment-0 take the owner had already
  accepted. Segment 4 was re-stitched with the renderer's own stitch and
  span-timing functions (nothing hand-timed). Rule worth making automatic:
  a speaker's identical refrain gets ONE take, rendered once — a refrain
  is supposed to sound the same, and one take cannot be choppy twice.
- `door-knock-soft` removed from Ուլիկը's cues (kept in sounds/ for other
  stories). The wolf's heavy knock is the only knock left; the two
  door-open inserts stay.

v8: 2:23, with the owner.

## 2026-09-04, round 7 — the narrator's breath, the mother's speed, the mother's pitch

Owner on v8: the opening narration does not finish its sentences ("as if
out of breath", startlingly deep at one point); the mother's «Պա՛ պա՛, պա՛,
պա՛, Սևուկ ջան» is unnaturally fast; her last line comes out "like a little
child talking".

- **Narrator back to `eleven_v3`.** The conversational model was applied to
  everyone in the pilot because the owner preferred it for the two clones;
  for NARRATION it drops sentence endings. The approved library was rendered
  with eleven_v3 and that stays the narrator's model. Characters keep the
  conversational model. All narrator spans re-rendered; every character take
  the owner had accepted is untouched.
- **`RENDER_ONLY="narrator,5:0,5:2"`** — the renderer can now re-render only
  named speakers and seg:span pairs, keeping every other span's WAV and
  re-stitching. Before this, fixing one character's line meant re-rolling
  its whole segment, which is how the mother's refrain went bad in round 5.
- **Span-level `voiceSettings`** override the speaker's: the two segment-5
  mother lines carry speed 0.9, stability 0.65, style 0.25.
- **Pitch guard.** A take's median f0 (autocorrelation, numpy) is compared
  with the same speaker's first rendered take; outside ×0.75–1.30 it is
  re-asked. It fired correctly on the mother's «Պա՛ պա՛» (×1.19 on the first
  band, re-asked) and wrongly on two short narrator asides («— ասավ վախեցած
  մայրը։», 27 chars, ×0.76) — a short parenthetical is legitimately lower,
  and too short to measure — so the guard now skips spans under 40 chars
  and the band is 0.75–1.30. The first run aborted on that false positive
  and the mix that followed silently reused the OLD segment 5; caught before
  sending, re-run with `RENDER_ONLY="5:1,5:2"`.

v9: 2:15 (eleven_v3 narrates faster than the conversational model), with
the owner.

## 2026-09-06, round 8 — two text edits, the door in the right pause, «Պա՛ պա՛» slower still

Owner on v9: «կաաա» before the door sound (the cut at «անում» split «կաթ»);
«Պա՛ պա՛» still too fast; remove «— ասավ վախեցած մայրը։»; «գեղեցիկ ուլ» →
«գեղեցիկ ուլիկ».

- **Two edits to the story TEXT**, made in `ulik.story.json` and the speaker
  map together; `check_speaker_map.py` passes (the map still reconstructs
  the story byte for byte) and the 51 story-library tests pass. The comma
  before the removed attribution became a full stop. The shipped narration
  on the toy does not carry these edits until the cast render ships.
- **`landAfterSpan`** in the mixer: a cue may land at the measured end of a
  span. The segment-4 door now opens in the renderer's 0.40 s speaker pause
  between the mother's song and «Ուլիկը դուռը բաց է անում…» — the only
  anchor there that cannot split a word.
- «Պա՛ պա՛…» at speed 0.8, stability 0.75, style 0.15 (span-level).
- Re-rendered with `RENDER_ONLY="0:0,5:0"`; every other take kept.

v10: 2:13, with the owner.

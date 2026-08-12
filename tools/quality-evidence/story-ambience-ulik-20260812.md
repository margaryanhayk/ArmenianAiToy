# «Ուլիկը» with ambience — the sample the owner judges

**Round 2.** The owner listened to the first mix and said the knocks came too
early. He was right, and the correction is recorded at the bottom of this file
under "The knocks were early". Times in the table below are the CORRECTED ones.


**Date:** 2026-08-12
**Scope:** ONE story. The other seven with cues are untouched until he accepts
this one; `hedgehog-apple` and `little-cloud` have no cues by his decision.

## What was made

Three sounds, generated rather than recorded, one folder per story:

| file | cues | seconds | asked for |
|---|---|---|---|
| `sounds/ulik/forest-day.mp3` | 1 | 10 | deep summer forest, mid-distance birds, none in front |
| `sounds/ulik/forest-evening.mp3` | 1 | 10 | the same forest later — lower, thinner, calm and never eerie |
| `sounds/ulik/door-knock.mp3` | **2** | 4 | firm knocks on a wooden door; **no howl, no growl, no animal** |

The exact prompts are in `sounds/ulik/prompts.json`. Generation is
non-deterministic and paid, so the audio is committed — it cannot be re-derived
and would not come back the same.

**The knock is ONE file used twice**, at the same level, for the wolf in
segment 2 and the mother in segment 4. That is the cue sheet's explicit
instruction and it is the point of the story: the door sounds identical and
only the voice differs. The generator keys on the (story, sound) pair precisely
so this is structural rather than remembered.

## Where the cues landed

Times resolved from the story's own committed byte map, not estimated:

| at | for | level | |
|---|---|---|---|
| 0:00.00 | 5.0 s | -20 dB | forest-day, establishing |
| 0:05.00 | 18.9 s | -28 dB | its bed, to the end of segment 0 |
| 0:23.85 | 4.0 s | -22 dB | forest-evening, segment 1 |
| 0:27.85 | 4.3 s | -30 dB | its bed |
| 0:37.78 | 2.0 s | -18 dB | **the wolf knocks** — on the line, in the pause |
| 1:04.59 | 2.0 s | -18 dB | **the mother knocks** — same file, same level |

No collision warning. The evening bed ends at the instant the wolf knocks,
which is what the cue note asks for: the bed thins out and the knock lands into
it.

## Two defects found by doing it

**The prompts were being built out of the cue NOTES.** A note explains to a
human where a cue goes and why, so the first dry run asked the generator for a
forest described as *"anchored to the START of the segment, not its end,
because the end of segment 1 IS the start of segment 2"* and for a knock
described as *"the SAME knock, at the same level, as the wolf's. That is
deliberate"*. Placement rationale is not a description of a sound. Fixed by
moving the prompt into the cue sheet as reviewed text (`sounds[].prompt`) with
a short `avoid` clause per cue; the `note` field is now never sent. Same family
as this morning's defect where `eleven_v3` read an English stage direction
aloud — prose written for one audience reaching a machine that treats it as
input.

**A one-shot was being faded in like a bed.** The mixer's 0.5 s fade-in is
right for a forest and wrong for an event. Measured on the generated knock: the
strikes peak at **0.1 s and 0.4 s** and everything after 0.9 s is silence, so
the bed's ramp would have taken the wolf's knock — the one sound in this story
that has to land — down to about a fifth of its volume. One-shots now get 0.01 s
in and 0.15 s out, just enough to kill the splice click.

Also: `Ship-StoryAudio.ps1` could not read what `mix_ambience.py` writes. The
mixer deliberately emits a WAV (loudness must be measured on the finished mix,
so encoding an intermediate MP3 would be a second lossy generation for nothing)
and its closing line points at the shipper, which globbed `*.mp3` only. The
shipper now accepts `.wav`, refuses a WAV without `-Fix` (the checks count MP3
frames and `-Apply` installs bytes as-is, so it would have shipped a WAV named
`.mp3`), and refuses a folder holding both forms of one story rather than
letting filename sort order decide which ships.

## Verification

- `mix_ambience.py --self-test` — PASS, including a new round trip on the real
  committed map: bytes → seconds → bytes lands on the identical offsets. A
  half-frame slip there would move every cue in every story and be invisible
  until someone listened.
- `check_story_audio.py` — 10 of 10. 1:46 against 1:48 expected, 192 kbps, one
  ID3 tag, **-16.7 LUFS** — inside the library band, measured on the finished
  mix as the contract requires.
- Byte map regenerated after the re-encode: `[45, 573066, 772432, 1160507,
  1520997, 2280220]`, ascending, ending inside a 2,549,177-byte file.
- `Version` bumped, so field toys re-download.
- `check_speaker_map.py` PASS, `dotnet test` 2554 — this slice changed no text
  and no C#.

## What is NOT proven

**Whether a forest under a story makes it better.** That is the only question
here and no tool can answer it. Specifically worth listening for:

- Does the forest bed make the opening narration harder to follow? The rule is
  that the voice is never competed with, and if a cue costs a sentence the cue
  is wrong.
- Do the two knocks read as the SAME door? If they do not, the story's own
  point is weakened rather than helped.
- Does the generated forest sound like a place, or like a texture? A synthetic
  bed can be perfectly clean and still sound like nothing in particular.

The generated knock has **two** strikes, not the three the sound description
asks for. Left as it is — two reads as a natural knock — but the description
and the audio now disagree by one, and that is recorded rather than quietly
reconciled.


## The knocks were early — round 2, 2026-08-12

The owner listened and said the knocks come too early. Measured: the wolf's
knock fired **5.6 s** before the narrator says «դուռը զարկում ու իր հաստ ձայնով
կանչում», the mother's about **1.8 s** before hers.

The cause was in the cue sheet's own rules contradicting the tool. It says
`cueLine` is "the exact line it lands on" — but `resolve_cue_time` only ever
resolved `at: "start"` or `at: "end"` of a whole SEGMENT, and segment 2 is
16 seconds long. The knock took the segment's only anchor and landed at its
beginning. A door opening before anyone has walked up to it.

**Finding the right moment without paying for another render.** The anchor
wanted is the boundary between the narrator's clause and the character's verse.
The speaker map knows the boundary exists (segment 2 is exactly
`narrator(72 chars)` then `wolf(151)`) but not what second it falls on, because
the renderer never wrote span timings — and its per-span files were named
`02-00-narrator.wav` with **no story in the name**, so rendering ten stories
into one directory silently overwrote all but the last. The finished audio was
unaffected (each story is stitched before the next begins) but every span render
was lost, which is exactly why the boundary had to be inferred.

So it is derived, and cross-checked three ways:

| method | seg 2 boundary |
|---|---|
| character-proportional inside the segment, gaps subtracted | 37.22 s |
| `silencedetect` on the narration | a real pause at **37.78–38.09 s** |
| the mother sings the same 151-char verse in segment 0; measuring it there predicts the wolf's verse starting at | ≈ 38.1 s |

A longer pause at 35.33 s (0.79 s against 0.31 s) is a red herring — the model
taking a breath inside the narrator's own sentence. **Choosing "the longest
nearby pause" would have picked it** and put the knock mid-sentence while
looking deliberate. Nearest-to-the-estimate picks correctly at all three
boundaries checked, and that is what the code does.

`at: "line"` now exists. Results: **32.16 → 37.78 s** and **63.35 → 64.59 s**,
each snapped to a measured pause, with the estimate, the snap and the distance
printed in the dry run so a cue that had to travel far is visible before
anything is spent.

Two more things came out of it:

- **The anchor is where the previous span STOPS, not where the next begins.**
  A fifth of a second apart, and it is the difference between the knock landing
  in the pause — narrator, knock, voice — and landing on the wolf's first
  syllable, which would be late in the same way the original was early.
- **A mixed story can now refuse to be mixed again.** Nothing in the audio says
  whether ambience is already in it, and the first mix shipped within an hour of
  the mixer existing; the next run would have laid a second forest over the
  first, silently. The mixer leaves a `<storyId>.ambience.json` marker beside
  the shipped file and refuses without `--force`, naming the git command that
  recovers the narration-only master. This round used exactly that: the clean
  `e9d8f468…` bytes the owner approved, pulled back out of history.

`render_story.py` now namespaces its span files by story and writes a
`<storyId>.spans.json` (speaker, characters, start and duration per span,
relative to the segment). Anything rendered after this reads a measured
boundary instead of estimating one, and the pause snap becomes a check rather
than the mechanism.

**Verification, round 2:** `mix_ambience.py --self-test` PASS with eleven new
checks; `check_story_audio.py` 10 of 10; ulik 1:46, 192 kbps, one ID3 tag; byte
map regenerated after the re-encode and starting at 45; the double-mix guard
observed refusing; `check_speaker_map.py` PASS; `dotnet test` 2554.

## Round 3 — the story stops, the door is struck, the story continues

The owner heard round 2 and said the knock was now about two seconds LATE, and
that the answer was not another nudge: **"we can pause, we can play knocking,
and then continue."**

Both halves of that were right.

**The two seconds.** There is exactly one pause 2.45 s earlier than where round
2 put the knock, and it is the longest in the segment — **35.33–36.12 s
(0.79 s)** against the 0.31 s gap at 37.78 s. That is what a change of speaker
looks like, and round 2's arithmetic dismissed it as the narrator taking a
breath. The estimate has now been wrong twice, in opposite directions, and both
times an ear caught it. That is the finding: **a cue's position was being
guessed and it needs to be measured.**

**The pause.** A sound laid over continuous narration has to fit whatever gap
the speech happens to leave, so a small placement error becomes an audible
mistake. Cutting the story open removes the class entirely — and a beat before
the door is struck is better storytelling than a knock competing with a
sentence.

`insert: true` now cuts the narration at a detected silence and concatenates
`lead-in · sound · tail` into the hole. Result for «Ուլիկը»:

| | round 1 | round 2 | round 3 |
|---|---|---|---|
| wolf's knock | 32.16 s | 37.78 s | **cut at 35.33 s, knock at 35.58 s** |
| mother's knock | 63.35 s | 64.59 s | **cut at 64.59 s, knock at 66.56 s** |
| story length | 1:46 | 1:46 | **1:49** |

The hole is 1.72 s for 1.12 s of knock — sized from the sound's **measured**
length, not the cue's `seconds`. The generated knock is a 4 s file with about a
second of strikes in it; using `seconds: 2` would have opened a 2.6 s hole with
1.7 s of nothing in the middle of it.

Measured in the finished file, RMS in 0.5 s windows: narration **3490** right up
to the cut, **702** through the knock, **102** in the tail. The knock is alone.

### The bug that made the first attempt absurd

The first insertion dry run opened a **10.6-second hole** and reported it as
"10.03s of door-knock" for a 4-second file. `build_chains` emits a SECOND chain
for every `holdUnder` scene, so chain order is not cue order — pairing them by
position had put the wolf's knock onto the evening forest and sized its hole
from the forest. Chains now carry their cue index. Worth recording because the
output was obviously wrong; the same mistake with a 3-second sound would have
been a plausible-looking hole in the wrong place.

### Blocked: the measurement that should replace the guess

The owner chose forced alignment — send the audio with the exact approved text
and get the time of every word back. `tools/story-audio/align_story.py` is
written and self-tested (ten checks, including that **all 29 cueLines in the
library are findable in their story text**, so nothing will silently fall back
to estimating).

**It cannot run: the ElevenLabs key is missing the permission.**

```
forced_alignment  -> "The API key you used is missing the permission
                      forced_alignment to execute this operation."
speech_to_text    -> same
```

Until that is enabled, «Ուլիկը»'s two cut points are `insertAtSeconds` in the
cue sheet — hand-placed from the owner's ear, labelled as such in the file, and
still snapped to a real detected silence so a re-encode cannot drift the cut
into the middle of a word. Every other one-shot in the library waits.

### Verification, round 3

`mix_ambience.py --self-test` PASS with ten new insertion checks (a moment
before the first cut does not move; one cut shifts everything after it by
exactly its gap; two cuts compound; a bed spanning a cut grows rather than
stopping short; segment starts shift; the narration is split and rejoined with
generated silence; only one filter stage produces `[narr]`; no insertions means
no split at all). `align_story.py --self-test` PASS. `check_story_audio.py`
10 of 10 — 1:49 against 1:48 expected, 192 kbps, one ID3 tag, -16.7 LUFS. Byte
map regenerated against the **post-insertion** timeline. `check_speaker_map.py`
PASS. `dotnet test` 2554.

# «Ուլիկը» with ambience — the sample the owner judges

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
| 0:32.16 | 2.0 s | -18 dB | **the wolf knocks** — exactly at segment 2 |
| 1:03.35 | 2.0 s | -18 dB | **the mother knocks** — same file, same level |

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

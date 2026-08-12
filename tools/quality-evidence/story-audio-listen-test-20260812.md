# Story audio listen test — the ten-story re-render

**Verdict: APPROVED by the owner, 2026-08-12.**

> **SUPERSEDED the same day.** Every file this approval names has been
> replaced by the character-voice re-render recorded in
> `story-audio-character-voices-20260812.md`. The approval below is pinned to
> the sha256 in the table, and not one of those hashes is on disk any more, so
> **it does not carry over** — the library is unlistened again. Kept because
> the approval genuinely happened and because the reason it was thrown away is
> worth remembering: the owner noticed that the wolf and the mother in
> «Ուլիկը» say the same words in the same voice, which is exactly the
> difference the scene asks a child to hear.

This file records the owner's acceptance. Per the standing convention in
`anban-huri-listen-test-TEMPLATE.md`, the listening is a human act and an
agent must not fill in the verdict — so what is written here is that the
owner approved, not that an agent judged the audio. The distinction has
bitten this repo before.

## What was approved

The ten narrations rendered 2026-08-11 in `areg-storyteller`
(`NxAsEwnikgCJa5tyBwEf`), `eleven_v3`, one request per story segment, then
levelled and installed by `Ship-StoryAudio.ps1 -Fix -Apply`. The approval
attaches to these exact bytes:

| story | sha256 (first 16) |
|---|---|
| anban-huri | `99fbc7be6ba11337` |
| hedgehog-apple | `cebf7a2522033f64` |
| khosogh-dzuk | `92ce0224ac2c4235` |
| little-cloud | `6a22cd536badb721` |
| pochat-aghves | `7fef11717918e399` |
| princess-and-pea | `766774618334cec6` |
| sutasan | `155d6fe33a18881b` |
| sutlik-orskan | `623f5247a5451d1e` |
| three-piglets | `d0fa5eeb7f811c11` |
| ulik | `87b74231de71a28e` |

The files were delivered to the owner directly and played by him. Any
re-render invalidates this record for the story it touches — a new hash is a
new file and needs its own listen.

## What the machine checked, separately

These are structural facts, established before the owner heard anything, and
they are not a substitute for the listening:

- `check_story_audio.py`: PASS 10 of 10 — every story between 95% and 106%
  of the duration its text implies. Previously FAIL 3 of 8, with
  `khosogh-dzuk` at 26%.
- 192 kbps, exactly one ID3 tag per file, -16.6 to -17.0 LUFS across the set.
- Manifest sizes and hashes match the bytes on disk; every `Version` bumped.
- Ten `.segments.json` byte maps, each starting at the first MP3 frame and
  ending inside the file.

## The specific risk this listen test was for

Each story is between 3 and 9 separate API requests stitched end to end. A
seam — a change of pace or breath between two segments — is audible to a
person and invisible to every check above. That is what the owner was asked
to listen for, and what his approval covers.

## Still open, unaffected by this approval

- **Ambience.** 29 cues are written in `backend/content/story-ambience/`, all
  with `licence: TBD`. Nothing has been bought, so nothing is mixed in. When
  it is, every story is re-rendered and needs a fresh listen.
- **The narrator decision.** `docs/voice-narrator-brief.md` records an
  intention to move to a licensed clone of a professional Armenian
  storyteller. This approval covers the current voice, not that question.

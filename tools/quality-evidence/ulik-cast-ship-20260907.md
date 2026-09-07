# Ուլիկը — cast render SHIPPED (2026-09-07)

Owner: "Good" on v14, then "Ship". This is the first story in the library
narrated by a CAST (Areg narrates; Katrin is the mother; the designed
`areg-wolf` is the wolf; `vardan-v2` is Ուլիկ), and the first shipped
after the noise investigation in `ulik-cast-noise-20260907.md`.

## What is on disk

| file | sha256 | bytes |
|---|---|---|
| `story-audio/ulik.mp3` (Version 12 → **13**) | `cde84f13a8ac5b96a7a43c7a925e392b757655757fe058809565c6e6ecf22076` | 3289591 |
| `clips/ulik/summary.mp3` | `e7939dbe27c539d4…` | 65872 |
| `clips/ulik/question.mp3` | `fc903cdfce083ed8…` | 67753 |
| `clips/ulik/question1.mp3` | `b82e9e91312cd6b7…` | 75276 |
| `clips/ulik/question2.mp3` | `f178d28251999613…` | 71515 |

The approval is pinned to these bytes. The shipped narration IS the v14
listening copy the owner heard, minus the appended summary (the toy plays
the summary clip after the story on its own). The four clips were
re-rendered in the cast narrator's settings (`eleven_v3`, stability .55 /
similarity .8 / style .2) and heard as part of v12/v14.

## Gates (Stage 2, run here — no PowerShell on this host)

| check | result |
|---|---|
| `check_story_audio.py` | PASS 10/10; ulik 2:17 against 1:46 expected (128%), 1 ID3 tag |
| integrated loudness | −16.7 LUFS (library contract −16.4; band −16.6..−17.0) |
| true peak | −1.7 dBTP |
| encode | 192 kbps, 44.1 kHz mono |
| `segments_to_bytes.py` | 6 segments → byte map written against the shipped file |
| `apply_story_clips.py --apply` | 7 clips re-hashed from disk |
| `dotnet test` | 2779 / 2779 |

`ulik.ambience.json` (the already-mixed marker) is the one the mixer wrote
for this mix — 5 cues, 4 sounds. `ulik.words.json` was REMOVED: it was the
forced alignment of the previous narration, and an exact map of a file that
no longer exists is worse than none.

## Not done

- **The listen test on the TOY.** The owner heard v14 as an MP3 on a phone.
  Version 13 makes every toy re-download 3.3 MB of narration; the four clips
  re-download by sha. Nobody has yet pressed the button and heard the cast
  through the toy's speaker.
- Nine stories still narrate in the single voice. Each needs its own cast
  in `*.voices.json` and the same pilot loop.
- The OpenAI-TTS stream cache (`StoryAudio:CacheRoot`) for Ուլիկը is a
  different render with its own map; it is untouched by this ship.

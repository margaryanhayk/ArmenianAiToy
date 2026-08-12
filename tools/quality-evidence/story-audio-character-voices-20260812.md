# Ten stories re-rendered span by span, each character in its own voice

**Verdict: APPROVED by the owner, 2026-08-12 — "Keep".**

He listened and accepted the character voices. Per the standing convention in
`anban-huri-listen-test-TEMPLATE.md`, an agent must not fill in a verdict: what
is recorded here is that the OWNER approved, not that a tool judged the audio.
The approval attaches to these exact bytes.

| story | size | sha256 (first 16) |
|---|---|---|
| anban-huri | 5,560,991 | `a501bb9cc2b19980` |
| hedgehog-apple | 613,817 | `97bef3409e01612f` |
| khosogh-dzuk | 8,044,922 | `2ec0d4458b0d26bd` |
| little-cloud | 514,133 | `d47727754718e65f` |
| pochat-aghves | 5,460,680 | `ea467bdf3d973cdd` |
| princess-and-pea | 1,560,494 | `af2232d13976606e` |
| sutasan | 1,880,233 | `0700361a8e814305` |
| sutlik-orskan | 3,356,047 | `68f9762e5dc473b7` |
| three-piglets | 2,010,009 | `ffcdc46192e37fe8` |
| ulik | 2,549,177 | `e9d8f4685a2f9bdf` |

**Ambience will move some of these hashes and not others.** The eight stories
that carry ambience cues get new bytes when the mix lands and need a fresh
listen for the mix — the VOICES are approved either way, and that is not
re-opened by adding a forest under them. `hedgehog-apple` and `little-cloud`
have no cues (owner decision, same day: they are 25 and 21 seconds long and a
fading forest under a story that short reads as busier than it is), so those
two keep these bytes unchanged.


**Date:** 2026-08-12
**Tool:** `tools/story-voices/render_story.py` (per SPAN), then
`tools/story-audio/Ship-StoryAudio.ps1 -Fix -Apply`, then
`tools/story-audio/segments_to_bytes.py`
**Voice:** `areg-storyteller` (`NxAsEwnikgCJa5tyBwEf`), `eleven_v3` — unchanged
**Requests:** 211 spans, one per span, zero retries needed, zero failures

## This SUPERSEDES the approval given earlier today

`tools/quality-evidence/story-audio-listen-test-20260812.md` records the owner
approving ten narrations. That approval is pinned to those files' sha256, and
**every one of those files has been replaced.** The approval does not carry
over and must not be treated as if it did. The library is unlistened again
until he says otherwise.

The reason it was worth spending his money twice in one day is his own finding:
in «Ուլիկը» the mother and the wolf say the same words — «Սևուկ ուլիկ, ջա՛ն
ուլիկ…» — and in the approved render they said them in the **same voice**. The
whole point of the scene is that the kid must notice the difference, and the
audio removed the thing he is meant to notice.

## What changed in how it is made

The previous render sent one request per story SEGMENT. This one sends one
request per SPAN — a stretch of text with a single speaker — using the speaker
maps in `backend/content/story-voices/`, which the checker proves reconstruct
the approved story text exactly.

| | before (this morning) | now |
|---|---|---|
| unit of a request | segment | span |
| requests | 56 | 211 |
| characters billed | 18,738 | 18,738 (the text is identical) |
| voices heard | 1 | 1 narrator + 21 characters across 10 stories |

Characters are separated by a pitch/formant shift applied AFTER the render
(`asetrate` + `atempo`, which lowers the throat without slowing the speech)
plus per-speaker `voice_settings`. **Nothing but the story's own words is ever
sent to the API** — see `tts-spoke-the-direction-20260812.md` for why that rule
exists and what it cost to learn.

| story | speakers | pitched |
|---|---|---|
| anban-huri | 5 | huri 1.04, frogs 0.90, husband 0.96 |
| hedgehog-apple | 1 | — |
| khosogh-dzuk | 6 | fish 1.05, fisherman 0.93, monster 0.85 |
| little-cloud | 2 | flower 1.08 |
| pochat-aghves | 9 | fox 1.05, old_woman 0.98, cow 0.90, field 0.95, spring 1.10, girl 1.08, hen 1.12 |
| princess-and-pea | 2 | — |
| sutasan | 5 | king 0.95, tailor 1.02, peasant 0.98 |
| sutlik-orskan | 3 | companion 0.97 |
| three-piglets | 1 | — |
| ulik | 4 | **wolf 0.88**, ulik 1.06 |

`ulik`'s wolf at 0.88 is the strength the owner accepted when he heard the two
lines side by side. The mother's identical line is left at 1.00 — he asked
specifically that only the wolf's words change, not the whole scene.

## The gate

`check_story_audio.py` — **PASS, 10 of 10**, run against the render directory
BEFORE anything was installed, and again against the installed files.

| story | length | needs | share | kbps |
|---|---|---|---|---|
| anban-huri | 3:51 | 3:39 | 105% | 192 |
| hedgehog-apple | 0:25 | 0:24 | 104% | 192 |
| khosogh-dzuk | 5:35 | 5:17 | 106% | 192 |
| little-cloud | 0:21 | 0:20 | 106% | 192 |
| pochat-aghves | 3:47 | 3:35 | 106% | 192 |
| princess-and-pea | 1:05 | 1:04 | 100% | 192 |
| sutasan | 1:18 | 1:12 | 109% | 192 |
| sutlik-orskan | 2:19 | 2:05 | 112% | 192 |
| three-piglets | 1:23 | 1:21 | 102% | 192 |
| ulik | 1:46 | 1:48 | 98% | 192 |

Every story runs slightly LONGER than `chars/15` now, because the joins carry
the air the punctuation asks for: 0.34 s after a full stop, 0.16 s after a
comma, 0.40 s at a change of speaker, 0.60 s between segments. The previous
render had no pause at any join at all.

Loudness after `-Fix`: -16.7 to -16.8 LUFS against the library's -16.4
contract, one ID3 tag each, 192 kbps.

## A gate bug this run found

`Ship-StoryAudio.ps1` refused to install `three-piglets`, reporting **2 ID3
tags**, while `check_story_audio.py` passed the same file. One of them was
wrong, and it was the shipper: at byte 1,996,978 the audio data happens to
contain `49 44 33` followed by a syncsafe-looking size of 29,954,147 — a tag
larger than the 2 MB file it claims to sit in. The PowerShell counter did not
check the major-version byte or that the tag fits in the file; the Python one
did, which is why they disagreed.

This matters more than one refused story: the false positive depends on the
audio bytes, so the shipper would refuse a random, different story on every
render — and the failure mode looks exactly like a real defect. Fixed by
applying the same rules as `_is_id3v2_header`, with the trap recorded in the
code.

## Segment maps

Ten `.segments.json` regenerated AFTER the re-encode, since the byte offsets
depend on it. All ascending, all starting at byte 45 (past the single ID3 tag),
all ending inside the file. `Version` bumped on all ten, so field toys
re-download.

## What is NOT proven

- ~~**Nobody has listened.**~~ **Closed** — the owner listened and said Keep.
  That was the open gate; 211 stitched requests, and no tool could have heard a
  seam or a character voice that reads as silly rather than different.
- **The pitch shift is a machine effect, not acting.** It makes the wolf
  distinct from the mother. It does not make him frightening, and a listener
  may find it artificial. If so, the answer is the human narrator in
  `docs/voice-narrator-brief.md`, not a different number.
- **No ambience.** The 29 cues are written and the mixer exists; nothing is
  mixed, because mixing an unapproved narration would waste both.

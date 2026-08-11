# Story audio re-render — the library is complete for the first time

**Date:** 2026-08-11
**Voice:** `areg-storyteller` (`NxAsEwnikgCJa5tyBwEf`) — unchanged. The owner
decided against switching narrator; the truncation was never a voice problem.
**Model:** `eleven_v3`, one request per story SEGMENT (`--per-segment`).
**Cost:** 56 requests, 18,692 characters.

## Why this run happened

Three of the eight shipped stories stopped mid-tale. Measured 2026-08-10 and
recorded in `story-audio-truncation-20260810.md`: every story under ~1,300
characters had rendered complete, every story over it came back holding about
1,300 characters' worth of audio and stopped. The ceiling is per REQUEST, so
the fix is to make no request large enough to reach it. The longest segment in
the library is 835 characters.

## Before and after

| story | was | now | needs | share |
|---|---|---|---|---|
| khosogh-dzuk | **1:21** | **5:03** | 5:17 | 26% → 96% |
| anban-huri | **1:27** | **3:38** | 3:39 | 40% → 100% |
| pochat-aghves | **1:25** | **3:39** | 3:35 | 40% → 102% |
| sutlik-orskan | 1:40 | 2:12 | 2:05 | 80% → 106% |
| ulik | 1:18 | 1:44 | 1:48 | 72% → 96% |
| princess-and-pea | 1:02 | 1:02 | 1:04 | 97% → 96% |
| sutasan | 1:09 | 1:08 | 1:12 | 96% → 95% |
| three-piglets | 1:23 | 1:24 | 1:21 | 103% → 103% |
| hedgehog-apple | *never rendered* | 0:25 | 0:24 | 102% |
| little-cloud | *never rendered* | 0:20 | 0:20 | 99% |

`check_story_audio.py`: **PASS — 10 of 10** (was FAIL 3 of 8).

Note the two stories at the bottom: `hedgehog-apple` and `little-cloud` had
approved text and had never been narrated at all. The library is eight stories
no longer; it is ten.

## What else changed with them

- **Encoding.** Every file is now 192 kbps with exactly one ID3 tag. Before
  this run all eight were 128 kbps — the proof that `Ship-StoryAudio.ps1` had
  never actually been run on the shipped library, whatever the pipeline
  documentation implied.
- **Loudness.** All ten sit between -16.6 and -17.0 LUFS, against the
  library-wide contract of -16.4. Half a decibel spread across the set, so no
  story is noticeably louder than its neighbour.
- **Segment maps.** Ten `.segments.json` files, the first this repo has ever
  had. Each starts at byte 45 — the first MP3 frame, immediately after the
  45-byte ID3 tag (verified: `ff fb` sync at that offset) — and ends inside the
  file. `StoryQaController.OffsetToSegment` reads them, so an in-story question
  is now answered about the scene the child is actually in rather than about
  segment 0.
- **Versions bumped** on all ten, which is what makes a toy in the field
  re-download rather than keep its cached copy.

## What this does NOT establish

**Nobody has listened to these files.** Every check here is structural: length
against text, tag count, loudness, hash, manifest agreement. None of it can
hear a bad join between two segments, a mispronounced name, or a delivery that
is simply wrong. Ten stories were rendered as 56 separate requests and stitched;
the seams are exactly the thing a machine cannot judge.

The human listen test remains the last gate, and nothing here should reach a
child before it. Re-rendering a single bad segment is now cheap.

## Reproducing

`docs/story-audio-rerender-runbook.md`, unchanged by this run — it was written
against this pipeline and executed as written.

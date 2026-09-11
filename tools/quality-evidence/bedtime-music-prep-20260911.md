# Bedtime music — preparation evidence (2026-09-11)

Prepares (does not render) the four bedtime-music tracks that
`ContentSync:Music` has shipped empty for since the feature's plumbing
(parent opt-in toggle, firmware playback, the Music dashboard view) was
built. `ELEVENLABS_API_KEY` was not set in this session (checked with
`test -n "$ELEVENLABS_API_KEY"`, unset) — everything below is preparation
and validation, never a paid render. The human listen test remains open
and always will be until real audio ships.

## What was built

**(a) `tools/story-ambience/generate_music.py`** — a sibling of
`tools/story-ambience/generate_sounds.py`, same dry-run-by-default /
`--render --confirm-paid-api` / `--self-test` shape. Calls the ElevenLabs
Music API (endpoint and request body **not verified against live docs this
session** — this session's network egress could not reach `elevenlabs.io`,
confirmed by a blocked `WebFetch` call; see the script's "ON THE ENDPOINT
SHAPE, HONESTLY" docstring section and
`docs/bedtime-music-render-runbook.md` step 0). Post-processes every
render in one ffmpeg pass: fade in 3 s, fade out 8 s, two-pass loudnorm to
**-23 LUFS** (justified in the script's "ON THE LOUDNESS TARGET" section —
the EBU R128 reference level for non-dialogue-carrying programme material,
~6.6 LU quieter than the -16.4 LUFS narration target, chosen because
bedtime music plays alone with no voice riding on top of it), 44.1 kHz
mono, 192 kbps MP3, one ID3 tag carrying the track's Armenian title.
Appends an instrumental-only instruction and a no-real-composer-or-
performer instruction to every prompt IN CODE, not left to the reviewed
JSON text, so neither can be dropped by an editing mistake.

**(b) `backend/content/bedtime-music/tracks.json` + `README.md`** — four
track definitions (id, Armenian title, requested duration, reviewed English
prompt, licence statement), same shape and same honest licence phrasing as
`story-ambience/ambience-cues.json` ("output ownership comes from the
ElevenLabs plan's terms, not from the audio being synthetic").

**(c) `tools/story-audio/check_music_audio.py`** — structural gate for
shipped tracks, reusing (by import, not reimplementing)
`check_story_audio.py`'s frame-walking MP3 parser and ID3-tag detector.
Checks: duration inside 3–5 minutes, exactly one ID3 tag. Deliberately no
length-vs-text check — a track has no text to compare against.

**(d) Four placeholder `ContentSync:Music` rows** in `appsettings.json`
(`SizeBytes: 0`, 64-zero `Sha256`, same shape every other unrendered-
content row in this repo uses) — dropped by
`ContentManifestService.BuildMusic` before ever reaching a device, pinned
by a new test (see below).

**(e) `docs/bedtime-music-render-runbook.md`** — the exact ordered command
sequence from here to shipped audio, including the doc-check-first step
this session could not itself perform.

## Titles reviewed

All four Armenian titles and English prompts were reviewed by the
armenian-story-master agent before being written into `tracks.json`. One
fix applied: track 1's title was corrected from the non-standard
«Օրորային» to «Օրորոցային» (the real adjective, from «օրորոց», cradle).
The agent also flagged that the bare noun «Օրոր» is the title of the
best-known Armenian classical lullaby, so track 1 is named «Օրորոցային
մեղեդի» — an adjective phrase, never the bare noun — per the owner's
no-real-work instruction. All four passed the Absence Test (none
anthropomorphise the toy or imply a relationship) and the calm-mode tone
check (no fear, no excitement, no action verbs).

## Pipeline verified end-to-end with synthetic audio

`ELEVENLABS_API_KEY` being unset means the generation call itself was never
exercised, but the post-processing pipeline — the part with the most room
to silently produce a bad file — was run for real against a synthetic sine-
wave MP3 standing in for a raw API response:

```
$ ffmpeg -f lavfi -i "sine=frequency=220:duration=210" ... raw.mp3
$ python3 -c "... generate_music.postprocess(job) ..."
done, output exists: True 5041950

$ python3 -c "... check_story_audio.scan(path) ..."
{'seconds': 210.08, 'id3_tags': 1, 'bitrates': [192], 'channels': ['mono'], ...}

$ ffmpeg -i out.mp3 -af loudnorm=print_format=summary -f null -
Input Integrated:    -23.2 LUFS   (target -23.0)

$ python3 tools/story-audio/check_music_audio.py --audio-dir /tmp/music_test
PASS - 1 bedtime-music track(s) are complete and cleanly encoded.
```

This confirms the fade/loudnorm/encode filter chain is syntactically and
numerically correct (measured loudness landed 0.2 LU from target, well
inside normal loudnorm tolerance) and that the structural gate correctly
reads its output — the one part of "will this work on the day the key is
available" that a dry run of `generate_music.py` alone cannot prove, since
a dry run never calls ffmpeg.

## Validation, all green

```
$ python3 tools/story-ambience/generate_music.py --self-test
PASS   (4 tracks, unique ids, duration window, instrumental + no-real-work
        instructions present in every prompt, every track titled)

$ python3 tools/story-ambience/generate_music.py
DRY RUN — nothing written.   (4 prompts printed, reviewed before this PR)

$ cd backend && dotnet build && dotnet test
Build succeeded. 0 Error(s).
Passed! - Failed: 0, Passed: 2931, Skipped: 0, Total: 2931
```

**Tests added**:
`ContentSyncAudioRootTests.ShippedMusicConfiguration_PointsAtFilesThatActuallyExist`
(mirrors the existing Stories keystone, for Music) and
`ContentSyncMusicTests.Manifest_ShippedPlaceholderTracks_AreDropped` (reads
the real `appsettings.json`, confirms all four shipped rows are bound but
none reaches the manifest while they remain placeholders). The pre-existing
`Manifest_NoMusic_FieldStaysNull` keystone (constructs `ContentSyncOptions`
by hand, never reads `appsettings.json`) is unaffected by these rows and
still passes.

## Known gaps, surfaced not hidden

- **The Music API endpoint/parameters are unverified against live docs.**
  This session could not reach `elevenlabs.io` to confirm them (network
  egress restricted). `docs/bedtime-music-render-runbook.md` step 0 makes
  this the first thing a render session must check, before spending.
- **No "ship" tool that auto-patches config.** `Ship-StoryAudio.ps1` exists
  for stories; four tracks did not justify building the equivalent for
  music. Step 4 of the runbook is a manual paste of the tool's printed
  JSON block.
- **The human listen test** — the gate that actually matters — has not
  happened, because nothing has been rendered.
- **-23 LUFS is a reasoned choice, not a tested one.** It has not been
  heard on the toy's actual speaker at bedtime volume. The runbook's step 6
  says so explicitly.

## What was not attempted

Any paid render, any use of the pasted-in-chat pattern this repo has
explicitly burned before — `ELEVENLABS_API_KEY` was read from the
environment only, confirmed unset, and no render command was run.

## Render attempt (2026-09-11, later session — `ELEVENLABS_API_KEY` set)

Still blocked — not rendered. `ELEVENLABS_API_KEY` was confirmed set this
session, but step 0 of `docs/bedtime-music-render-runbook.md` ("Check the
current ElevenLabs Music API docs... the one open item a human must close
before step 2 can run for real") could not be closed: `WebFetch` against
`https://elevenlabs.io/docs/api-reference/music/compose` returned
`EGRESS_BLOCKED`, same as the prior session. `generate_music.py`'s
`ENDPOINT` (`POST /v1/music`) and request body remain unverified against
live docs.

Rendering anyway was considered and rejected: an unverified endpoint shape
risks either a wasted paid call against the wrong URL/parameters, or —
worse — a call that succeeds against a real but different endpoint/plan
than intended, at real cost, before anyone has confirmed the request is
correct. The runbook is explicit that this check is not optional. No
`--render --confirm-paid-api` command was run; no track was generated; the
four `ContentSync:Music` rows are unchanged (`SizeBytes: 0`, all-zero
`Sha256`, exactly as PR #46 left them).

**Still open, unchanged from the prior session's blockers above**: the
endpoint verification itself, the human listen test, and the -23 LUFS
choice being untested on hardware.

## Render — 4 of 4 rendered and shipped (2026-09-11, later render session)

`ELEVENLABS_API_KEY` was set. Step 0's endpoint check was closed by direct
verification instead of a docs fetch, since `elevenlabs.io` (the docs
site) is still unreachable from this environment but `api.elevenlabs.io`
(the API itself) is: one cheap probe,
`{"prompt": "...", "music_length_ms": 10000, "force_instrumental": true}`
against `POST https://api.elevenlabs.io/v1/music`, returned HTTP 200 with
a real 10.03s MP3 (ID3v2.4, MPEG layer III, confirmed via `ffprobe`/`file`)
in the response body — confirming `generate_music.py`'s documented shape
(`prompt` + `music_length_ms`, raw audio bytes back, not the alternate
`/v1/music/compose` plan-then-render flow). `force_instrumental: true` was
added to the request body on top of the in-prompt instrumental
instruction, since the probe proved the field is accepted; the script's
"ON THE ENDPOINT SHAPE, HONESTLY" docstring section was updated to record
the verification instead of flagging it as open.

All four tracks were then rendered once each via
`python3 tools/story-ambience/generate_music.py --render --confirm-paid-api`
(no `--force`, no re-renders needed — every track succeeded on the first
call) and post-processed through the fade/loudnorm/encode pipeline exactly
as designed:

| Track | Requested | Actual | SizeBytes | Sha256 |
|---|---|---|---|---|
| lullaby-melody | 200s | 3:20 (200s) | 4,802,460 | `346af002e819077e15726ccf8ee5d0d6d3f98c8b627a8b335aa5461dd664cef3` |
| under-the-stars | 230s | 3:50 (230s) | 5,522,800 | `37d9054e6d9f62c7d10dc43b3046456ac1b7e41d28acba0eb208cb841d8c69b7` |
| calm-night | 260s | 4:20 (260s) | 6,241,276 | `0a5edc1358e02100a186003b55c173ffc27471c120f42b891567709be11c80f2` |
| gentle-breeze | 290s | 4:50 (290s) | 6,962,875 | `80b7eab7352534907fc26d009709fe9e5c2a3dfd2895893fc35bc3c2e97a2d9a` |

Every requested length landed exactly on request (the runbook's own margin
against a possible mismatch turned out not to be needed this time).

**Structural gate**:

```
$ python3 tools/story-audio/check_music_audio.py
track                         length  kbps  verdict
------------------------------------------------------------
calm-night-v1                   4:20   192  ok
gentle-breeze-v1                4:50   192  ok
lullaby-melody-v1               3:20   192  ok
under-the-stars-v1              3:50   192  ok

PASS - 4 bedtime-music track(s) are complete and cleanly encoded.
```

**Loudness check** (measured post-encode, not just trusted from the
pipeline):

```
$ ffmpeg -i <file> -af loudnorm=print_format=summary -f null -
lullaby-melody-v1:    Output Integrated -23.6 LUFS
under-the-stars-v1:   Output Integrated -23.5 LUFS
calm-night-v1:        Output Integrated -23.5 LUFS
gentle-breeze-v1:     Output Integrated -23.9 LUFS
```

All four land within 0.9 LU of the -23.0 LUFS target — the same normal
loudnorm tolerance the earlier synthetic-audio dry run observed (0.2 LU).

**Config and tests**: the four placeholder `ContentSync:Music` rows in
`appsettings.json` were replaced with the real SizeBytes/Sha256 above.
`ContentSyncMusicTests.Manifest_ShippedPlaceholderTracks_AreDropped`
(asserted the opposite — that shipped rows stay dropped as placeholders)
was replaced with
`Manifest_ShippedTracks_AllFourReachTheManifest`, which asserts all four
tracks now reach the manifest in config order.
`ContentSyncAudioRootTests.ShippedMusicConfiguration_PointsAtFilesThatActuallyExist`
needed no change — it already branches on `SizeBytes == 0` vs. a real row
and now exercises its real-file branch for the first time.

```
$ cd backend && dotnet build && dotnet test
Build succeeded. 0 Error(s).
Passed! - Failed: 0, Passed: 2931, Skipped: 0, Total: 2931
```

(Test count unchanged: one test was renamed/repurposed rather than added.)

`story-audio/music/prompts.json` was written by the render, recording the
exact prompt sent (reviewed text + the two in-code instrumental/no-real-work
instructions) and target LUFS per track, same idiom as
`story-ambience/sounds/<storyId>/prompts.json`.

**What was not attempted or verified**: `docs/elevenlabs.io` docs pages
themselves were still not fetched (still `EGRESS_BLOCKED`) — the API shape
was confirmed by calling the API directly instead, which this session
judges sufficient since it is the thing that actually gets called.
**The human listen test is still open for all four tracks** — nobody has
heard any of them yet, at any volume. The -23 LUFS choice remains a
reasoned target confirmed only by a loudness meter, not by an ear at the
toy's actual bedtime volume.

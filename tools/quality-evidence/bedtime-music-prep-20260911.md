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

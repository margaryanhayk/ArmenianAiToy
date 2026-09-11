# Bedtime music — render runbook

The exact, ordered sequence to go from what is in this PR (four reviewed
track prompts, placeholder `ContentSync:Music` rows, a generator tool that
has never made a paid call) to shipped audio. `ELEVENLABS_API_KEY` was not
set in the session that prepared this PR (checked with
`test -n "$ELEVENLABS_API_KEY"`, unset), so every step below is validated
up to its dry-run / self-test stage and no further. Run the whole sequence
for real once the key is available.

## 0. Before spending anything

**Check the current ElevenLabs Music API docs.** This session's network
egress could not reach `elevenlabs.io` (WebFetch returned
`EGRESS_BLOCKED`), so `tools/story-ambience/generate_music.py`'s `ENDPOINT`
and the `generate()` request body (`POST /v1/music`, JSON `{"prompt",
"music_length_ms"}`, raw audio bytes back) reflect this assistant's
understanding of the documented shape and were **not** re-verified live —
unlike `generate_sounds.py`'s sound-generation endpoint, which an earlier
session did confirm against live docs. If the endpoint, parameter names, or
response shape have changed, update `generate()` in `generate_music.py`
first. This is the one open item a human must close before step 2 can run
for real.

## 1. Dry run (no key needed, proves the inputs)

```
python3 tools/story-ambience/generate_music.py --self-test
python3 tools/story-ambience/generate_music.py
```

The first needs no network and checks: four tracks, unique ids, each
duration inside the 3–5 minute window, the instrumental-only and
no-real-work instructions reaching every prompt, every track has a title.
The second prints the exact prompt that would be sent for all four tracks
and writes nothing — read it once before spending money on it.

## 2. Render (spends money)

```
export ELEVENLABS_API_KEY=...   # never write this to a file
python3 tools/story-ambience/generate_music.py --render --confirm-paid-api
```

For each track this: calls the Music API, post-processes the result in one
ffmpeg pass (fade in 3 s, fade out 8 s, two-pass loudnorm to **-23 LUFS**
— see `generate_music.py`'s "ON THE LOUDNESS TARGET" docstring section for
why that number and not -16.4, the narration target — 44.1 kHz mono,
192 kbps MP3, one ID3 tag with the track's Armenian title), writes
`backend/src/ArmenianAiToy.Api/story-audio/music/<trackId>-v1.mp3`, and
prints the exact SizeBytes/Sha256 for each track plus a ready-to-paste
`ContentSync:Music` JSON block. It also writes
`story-audio/music/prompts.json` beside the audio — the same "why does this
sound like this" record `generate_sounds.py` keeps for ambience.

`--track <id>` renders one track only (useful for a re-render after an
owner listen-test rejection); `--force` re-renders even if the file already
exists.

## 3. Structural gate

```
python3 tools/story-audio/check_music_audio.py
```

Checks every shipped track's duration is inside 3–5 minutes and carries
exactly one ID3 tag — the same two-defect posture
`tools/story-audio/check_story_audio.py` applies to narration, minus the
length-vs-text check (a track has no text to compare against). This gate
does not check loudness or listen quality; nothing automated does.

## 4. Update the config

Paste the JSON block step 2 printed over the matching placeholder row in
`backend/src/ArmenianAiToy.Api/appsettings.json`'s `ContentSync:Music`.
Each rendered row must carry a real `SizeBytes` and 64-hex-char `Sha256` —
a placeholder (`SizeBytes: 0`, 64-zero `Sha256`) is what makes an
unrendered track invisible to every toy; a half-filled row (real hash, no
size, or vice versa) is exactly the rot
`ContentSyncAudioRootTests.ShippedMusicConfiguration_PointsAtFilesThatActuallyExist`
exists to catch, so run the tests after pasting, not just the Python gate.

## 5. Verify

```
cd backend && dotnet build && dotnet test
```

`ContentSyncMusicTests.Manifest_ShippedPlaceholderTracks_AreDropped` will
start failing once the first real row lands — that is expected and correct
(a track manifest now has entries where before it had none); update that
test's assertion to check the specific tracks still pending render, or
retire it once all four have shipped, rather than leaving it red.

## 6. The human listen test

Still the last gate, as everywhere else in this repo. Four tracks, three to
five minutes each — budget real time for this, not a skim. Listen at the
toy's actual bedtime volume range, not desk-speaker loud; the -23 LUFS
target is a starting point, not a guarantee it reads as "gentle" on the
real hardware.

## What this runbook does not cover

- Any change to the firmware's `music_select_next` / bedtime-window gate —
  none was needed; the firmware and manifest contract already existed and
  is unaffected by which specific tracks are configured.
- A "ship" tool that auto-patches `appsettings.json` the way
  `Ship-StoryAudio.ps1` does for stories. Four tracks, rendered once, did
  not justify building one; step 4 is a manual paste. If bedtime music
  grows past this first batch, revisit that call.

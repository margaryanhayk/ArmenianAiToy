# Re-rendering the library in your own voice — the exact commands, in order

**Written 2026-08-10, rewritten 2026-08-11 for the owner's own clone.** For the
owner's Windows machine, which has `dotnet`, `ffmpeg` and `python3`. Everything
here is run by the owner: **the ElevenLabs API key never leaves his hands.**

The voice for this pass is **the owner's own ElevenLabs clone** — the same
`areg-storyteller` that narrates the library today. Owner decision, 2026-08-11.
The famous-storyteller conversation still happens (`docs/voice-narrator-brief.md`),
but it happens with a *working* toy in hand, not as a prerequisite.

---

## Why this pass is worth paying for

Three of the eight shipped stories play a quarter to a third of their text and
stop mid-tale (`tools/quality-evidence/story-audio-truncation-20260810.md`).

**The cause was measured on 2026-08-11 and it is not the voice — it is the
REQUEST SIZE.** Your clone stops at roughly **1,300 characters of output**,
however long the input is:

| story | characters | what shipped | share |
|---|---|---|---|
| princess-and-pea | 967 | complete | 97% |
| sutasan | 1,080 | complete | 96% |
| three-piglets | 1,222 | complete | 102% |
| ulik | 1,616 | ~1,163 chars of audio | 72% |
| sutlik-orskan | 1,875 | ~1,500 chars of audio | 80% |
| pochat-aghves | 3,220 | ~1,288 chars of audio | 40% |
| anban-huri | 3,290 | ~1,316 chars of audio | 40% |
| khosogh-dzuk | 4,753 | ~1,236 chars of audio | 26% |

Every story **under** the ceiling rendered perfectly. Every story **over** it
came back at the ceiling and stopped there. The line is that sharp.

A Default voice (Charlotte) rendered the 4,753-character story to 114% with the
same tool on the same day, which is consistent with ElevenLabs' note that
Professional Voice Clones are not fully optimised for Eleven v3 — so a clone
needs smaller requests, and until now the tool was sending one request per
story (`--max-chunk 4000`).

**The fix is `--per-segment`.** Your longest single story segment is **835
characters**, so a segment-sized request cannot reach the ceiling. Truncation
stops being unlikely and becomes arithmetically impossible.

Three things come free with it:

- **Seams fall on paragraph breaks**, where a narrator pauses anyway. (v3
  refuses `previous_text`/`next_text`, so every seam is blind either way —
  better to put them where a pause belongs.)
- **A fluffed line costs one request**, not a whole story.
- **`<storyId>.segments.json`** — the segment map this repo has never had. The
  backend wants it so an in-story question is answered about the scene the
  child is actually in; today it guesses from `offset / fileSize`, and on the
  truncated stories that guess is badly wrong. `mix_ambience.py` wants it too.

Two stories have never been recorded at all (`hedgehog-apple`, `little-cloud`),
so this pass takes the library from 8 to 10.

Size of the job: **~18,700 characters** of narration across 10 stories in **56
requests**, plus **~1,400** across the 43 welcome clips. One sitting.

---

## Verified end to end on 2026-08-11 (except the paid call)

Everything here except the render itself has been **run**, not just written — a
container with `ffmpeg`, `pwsh` and `dotnet` was set up for it (see
`docs/container-toolchain.md`). What that turned up:

- **`Ship-StoryAudio.ps1` executes and its diagnosis is exact.** Against the
  eight shipped stories it reports the same three as cut short, at the same
  percentages, as the dependency-free checker:
  `khosogh-dzuk 26%`, `anban-huri 40%`, `pochat-aghves 40%`.
- **Loudness is NOT the problem.** Every shipped file measures between -16.1
  and -16.9 LUFS against a -16.4 target, with exactly one ID3 tag. **Length is
  the only fault.**
- **The `--per-segment` dry run is accurate**: 10 files, 56 requests, 18,692
  characters, every chunk at or under 835.
- **The segment map is exact, and proving it found a real bug.** Each API
  response opens with a 26 ms Xing header frame that the stitcher drops but the
  duration walker counted, so summing the raw responses overshot by 26 ms per
  segment and drifted down the story. `--self-test` caught it (4 pieces,
  0.104 s = 4 x 26 ms); the map now measures each piece *as it appears in the
  finished file*, and the same test reports a 0.000 s delta.

You can re-run that check yourself, with no API key and nothing sent:

```powershell
dotnet run --project tools/ElevenLabsRender -- --self-test --output <a folder of mp3s>
```

## Before you start

**Get your clone's voice ID.** ElevenLabs Voices page → `areg-storyteller` →
copy the voice ID. It is the same voice that narrates the library today, so
there is nothing new to audition — but note that the whole point of this pass is
that the *requests* change, not the voice.

---

## Step 1 — the two environment variables

PowerShell, in the repo root. These live in the shell session only — never in a
file, never committed.

```powershell
$env:ELEVENLABS_API_KEY  = "<your key>"
$env:ELEVENLABS_VOICE_ID = "<areg-storyteller's voice id>"
$render = "$env:TEMP\areg-rerender"
```

## Step 2 — the dry run. Free. Read it before paying.

```powershell
dotnet run --project tools/ElevenLabsRender -- --all --per-segment --output $render
```

It prints every file, its character count and its expected duration, and sends
**nothing** to the API. `--render --confirm-paid-api` are the two keys that
unlock spending; neither is here.

Check the plan says **10 file(s) in 56 request(s)** and roughly 18,700
characters. If the request count is 11 instead of 56, `--per-segment` did not
take — that is the one-request-per-story shape that truncated the library.

## Step 3 — render the ten stories

```powershell
dotnet run --project tools/ElevenLabsRender -- --all --per-segment --output $render `
    --render --confirm-paid-api
```

Leave `--model` and `--speed` alone. `eleven_v3` is the only model on the
account that speaks Armenian, and at the default speed the request deliberately
carries no `voice_settings` at all, so the clone's own saved settings apply —
sending one "just to set speed 1.0" replaces them.

Two things to watch in the output:

- **`*** chunk N came back ...%`** — the tool stops at the FIRST short chunk
  rather than paying for the rest. It should not fire at segment sizes, but if
  it does, that segment alone is over the ceiling: re-run that story with
  `--max-chunk 600` and it will split the segment on a sentence boundary.
- **`segment map: N segment(s) -> ...`** — one line per story, confirming the
  `.segments.json` was written.

What you get in `$render`:

```
khosogh-dzuk.mp3            <- ship-ready name, already what the shipper wants
khosogh-dzuk.segments.json  <- seconds map
segments/khosogh-dzuk--seg01.mp3 ... --seg09.mp3
manifest-snippet.json
```

**Keep the `segments/` folder.** Those pieces are what `mix_ambience.py` needs
when the ambience sounds are finally licensed — throwing them away means paying
to render the whole library a second time.

## Step 4 — no rename needed

`--per-segment` already writes `<storyId>.mp3`, which is exactly what
`Ship-StoryAudio.ps1 -In <dir>` looks for. The rename step that used to sit
here, undocumented, between two tools is gone.

## Step 5 — the gate that was skipped last time

```powershell
python3 tools/story-audio/check_story_audio.py --audio-dir $render
```

**It must print `PASS - 10 stories are complete and cleanly encoded.`** If it
prints FAIL, stop and re-render the named stories. This exact check is what
would have caught the three truncated stories before they reached a child, and
it was never run on them. Needs nothing installed.

## Step 6 — level, install, patch the manifest

```powershell
./tools/story-audio/Ship-StoryAudio.ps1 -In $render -Fix -Apply
```

`-Fix` re-encodes a copy: one decode to PCM drops every stray ID3 tag, two-pass
`loudnorm` sets the level to **-16.4 LUFS** (the level the whole library sits
at), and the output is 192 kbps. `-Apply` then installs into `story-audio/`,
patches `ContentSync:Stories` and **bumps every `Version`** — which is the only
thing that makes a toy in the field re-download.

It refuses to install anything that still fails a check.

> **Nothing to add by hand.** `hedgehog-apple` and `little-cloud` have never
> had audio, and used to need a hand-written `ContentSync:Stories` entry before
> `-Apply` — forget it and the script copied the MP3 in and *then* failed on the
> missing entry, leaving new bytes on disk described by an old manifest, which
> every toy downloads and rejects. Both now ship as placeholder rows with
> `SizeBytes: 0` and an all-zero hash: the manifest validator drops a zero-size
> story, so nothing is advertised and no toy is disturbed, while the script's
> regex still finds the row and fills in the real size, hash and `Version`.
>
> Two tests hold that shape —
> `ContentManifestServiceTests.ShippedConfig_AdvertisesExactlyTheStoriesThatHaveAudio`
> and `ContentSyncAudioRootTests.ShippedConfiguration_PointsAtFilesThatActuallyExist`
> — so a half-filled row (real hash, no size) still fails the build rather than
> reaching a toy.

Re-run step 5 against `backend/src/ArmenianAiToy.Api/story-audio` afterwards —
the levelling re-encodes the files, so what ships is not what step 5 checked.

## Step 7 — turn the segment map into the bytes the backend reads

The map from step 3 is in **seconds**. `StoryQaController.LoadSegmentMap`
deserialises a bare array of **byte offsets**, and when it cannot it silently
falls back to guessing the child's position from `offset / fileSize` — so a
seconds map installed as-is looks like it works and changes nothing.

Byte offsets can only be known after step 6, because `-Fix` re-encodes the file.

```powershell
Get-ChildItem $render -Filter '*.segments.json' | ForEach-Object {
    $id = $_.Name -replace '\.segments\.json$',''
    python3 tools/story-audio/segments_to_bytes.py `
        --seconds $_.FullName `
        --mp3 "backend/src/ArmenianAiToy.Api/story-audio/$id.mp3" `
        --out  "backend/src/ArmenianAiToy.Api/story-audio/$id.segments.json"
}
```

This is what stops an in-story question being answered about a scene the child
has not reached. It needs nothing installed.

> **The map belongs beside the MP3 in `story-audio/`, not in the cache folder.**
> Two MP3s of every story exist and their byte offsets are not interchangeable:
> the ElevenLabs narration the toy plays from SD (`story-audio/`, this map) and
> an OpenAI-TTS render of the same story that the backend streams over Wi-Fi
> (`StoryAudio:CacheRoot`, its own map, written automatically by
> `StoryAudioController`). `OffsetToSegment` reads the cache map first and this
> one second, so each is only ever paired with its own audio. Moving this file
> into the cache folder would make a map of one file be read as a map of the
> other — worse than having no map at all.
>
> `ArmenianAiToy.Api.csproj` copies `story-audio\**\*.segments.json` into the
> publish output, so the map reaches the container. It was not always so: the
> same glob had to be widened twice before, both times because the manifest
> advertised files the image did not contain.
>
> One known gap, harmless today: if **variant endings** are ever switched back
> on, a child may be hearing the alternate file while this map describes the
> base story. They are off on every device today.

## Step 8 — the 43 welcome clips, same voice

Skip this and the toy greets the child in one voice and tells the story in
another.

```powershell
dotnet run --project tools/ElevenLabsRender -- --voice-clips --output "$render-voice" `
    --render --confirm-paid-api

python3 tools/story-audio/apply_voice_clips.py --in "$render-voice"          # dry run
python3 tools/story-audio/apply_voice_clips.py --in "$render-voice" --apply
```

`apply_voice_clips.py` is the shipper `Ship-StoryAudio.ps1` does not have: it
recomputes sha256 and size **from the files themselves**, refuses any clip that
is truncated or carries two ID3 tags, copies them in, and bumps only the
`Version` of clips whose bytes actually changed. It refuses a partial set,
because half a set means the toy greets in the new voice one boot and the old
voice the next.

It does **not** level. The clips are short and the loudness contract matters
most on the stories; if a greeting sounds noticeably quieter than the narration,
run the same two-pass `loudnorm` from the header of
`tools/ElevenLabsRender/Program.cs` over the clip folder before installing.

## Step 9 — listen

Not optional, and not replaceable by any of the above.

- One story end to end — `khosogh-dzuk`, because it is the worst one today.
  It must run past 1:21 to its real ending.
- Two or three greetings.
- A name: «Հուռին», «Ծիվիկ». This is where a wrong accent shows.

Then commit. Railway deploys, and every toy re-downloads on the `Version` bumps.

## Step 10 — make it impossible to regress

Once step 5 prints PASS on all ten, wire it into CI so nobody has to remember.
It is not there today, deliberately: it exits non-zero right now because of the
three truncated stories, and a check that is red for a known reason gets
ignored — which is the whole story of this defect. Add it the day it is green:

```yaml
      - name: Story audio is complete
        working-directory: .
        run: python3 tools/story-audio/check_story_audio.py
```

in `.github/workflows/ci.yml`, alongside the existing build-and-test steps. It
needs no ffmpeg, no dotnet and no network, so it costs the run a second.

---

## What this pass deliberately leaves out

- **The ambience.** All 18 sounds in `backend/content/story-ambience/` still
  read `licence: "TBD"` — nothing has been chosen or bought. Forest sounds do
  not change whether the toy works for the storyteller demo. They ride the next
  render, through `tools/story-audio/mix_ambience.py`.
- **A studio session.** `--per-segment` now gets per-segment audio out of the
  API, so the segment map no longer waits on a human narrator. The instruction
  in `docs/voice-narrator-brief.md` §3 stands for the day a real one records —
  it cannot be added after the session.
- **`AREG_STORY_PAUSE_BYTES_PER_SEC`.** The firmware assumes 192 kbps; today's
  files are 128. After step 6 they *will* be 192, so the constant becomes
  correct by itself — but check it against the shipped bitrate before story
  pauses are ever turned on.

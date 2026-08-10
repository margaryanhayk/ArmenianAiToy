# Re-rendering the library in a new voice — the exact commands, in order

**Written 2026-08-10.** For the owner's Windows machine, which has `dotnet`,
`ffmpeg` and `python3`. Everything here is run by the owner: **the ElevenLabs
API key never leaves his hands.**

The voice for this pass is **Charlotte**, an ElevenLabs premade voice, chosen
2026-08-10. She is interim by design — the plan is to hand a *working* toy to a
famous Armenian storyteller and ask whether he wants it to be his voice
(`docs/voice-narrator-brief.md`). This render is expected to be thrown away, and
the owner has said that is fine.

---

## Why this pass is worth paying for

Three of the eight shipped stories play a quarter to a third of their text and
stop mid-tale (`tools/quality-evidence/story-audio-truncation-20260810.md`).
Measured on the same story, same tool, same `eleven_v3` model:

| Voice | `khosogh-dzuk` (needs 5:17) | Share |
|---|---|---|
| the owner's clone — **what ships today** | 1:21 | **26%** |
| Charlotte | 6:03 | 114% |
| Daniel | 7:04 | 134% |

The truncation is a property of the **clone**, not of the pipeline — consistent
with ElevenLabs' own note that Professional Voice Clones are not fully optimised
for Eleven v3. **So changing the voice is also the truncation fix**, and the two
stories that have never been recorded at all (`hedgehog-apple`, `little-cloud`)
come along in the same pass, taking the library from 8 to 10.

Size of the job: **~18,700 characters** of narration across 10 stories, plus
**~1,400** across the 43 welcome clips. One sitting.

---

## Before you start

**Get Charlotte's voice ID.** It is recorded nowhere in this repo — the audition
rendered her but never wrote the IDs down. Open the ElevenLabs Voices page, open
Charlotte, copy the voice ID.

Two things worth knowing while you are there:

- ElevenLabs categorise Charlotte as Swedish. **Your ear on real Armenian output
  beats their marketplace tag** — but listen once for a Swedish colour on a name
  like «Հուռին», because someone has already raised it.
- Charlotte is a **Default** voice, and ElevenLabs' Default voices stop working
  **31 December 2026** (`docs/voice-decision-brief.md` §0). That is fine for an
  interim pass. It is not fine as a permanent answer.

---

## Step 1 — the two environment variables

PowerShell, in the repo root. These live in the shell session only — never in a
file, never committed.

```powershell
$env:ELEVENLABS_API_KEY  = "<your key>"
$env:ELEVENLABS_VOICE_ID = "<Charlotte's voice id>"
$render = "$env:TEMP\areg-charlotte"
```

## Step 2 — the dry run. Free. Read it before paying.

```powershell
dotnet run --project tools/ElevenLabsRender -- --all --output $render
```

It prints every text, its character count, and the expected duration, and sends
**nothing** to the API. `--render --confirm-paid-api` are the two keys that
unlock spending; neither is here.

Check the plan says **10 files** and roughly 18,700 characters.

## Step 3 — render the ten stories

```powershell
dotnet run --project tools/ElevenLabsRender -- --all --output $render `
    --render --confirm-paid-api
```

Leave `--model` and `--speed` alone. `eleven_v3` is the only model on the
account that speaks Armenian, and at the default speed the request deliberately
carries no `voice_settings` at all, so Charlotte's own saved settings apply —
sending one "just to set speed 1.0" replaces them.

Watch for `*** TOO SHORT` in the output. The tool refuses a render under 70% of
the length its text needs and names the file. If one appears, re-render that
story alone with `--only <id>--narration--s1.0` before going on.

## Step 4 — rename to what the shipper expects

The renderer writes `<id>--narration--s1.0.mp3`; `Ship-StoryAudio.ps1` wants
`<id>.mp3`. Nothing else bridges the two.

```powershell
Get-ChildItem $render -Filter '*--narration--s*.mp3' | ForEach-Object {
    Rename-Item $_.FullName ($_.Name -replace '--narration--s[\d.]+\.mp3$', '.mp3')
}
```

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

> **Do this before `-Apply`, not after:** `hedgehog-apple` and `little-cloud`
> have no `ContentSync:Stories` entry — they have never had audio. Add both by
> hand first, copying the shape of an existing entry **in the same field
> order** (`StoryId`, `Version`, `Title`, `AudioUrl`, `AudioPath`, `SizeBytes`,
> `Sha256`) — the script finds entries with a regex that depends on it. Set
> `Version: 1`, `AudioPath: "<id>.mp3"`, and any placeholder 64-hex `Sha256`
> and integer `SizeBytes`; `-Apply` overwrites both with the real values.
>
> If you forget, the script copies the MP3 in and *then* fails on the missing
> entry, leaving new bytes on disk described by an old manifest — which every
> toy will download and reject. Recoverable by adding the entries and re-running,
> but easier to avoid.

Re-run step 5 against `backend/src/ArmenianAiToy.Api/story-audio` afterwards —
the levelling re-encodes the files, so what ships is not what step 5 checked.

## Step 7 — the 43 welcome clips, same voice

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

## Step 8 — listen

Not optional, and not replaceable by any of the above.

- One story end to end — `khosogh-dzuk`, because it is the worst one today.
  It must run past 1:21 to its real ending.
- Two or three greetings.
- A name: «Հուռին», «Ծիվիկ». This is where a wrong accent shows.

Then commit. Railway deploys, and every toy re-downloads on the `Version` bumps.

## Step 9 — make it impossible to regress

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
- **One WAV per segment.** Worth asking for from a studio, not from an API —
  see `docs/voice-narrator-brief.md` §3 for why it matters.
- **`AREG_STORY_PAUSE_BYTES_PER_SEC`.** The firmware assumes 192 kbps; today's
  files are 128. After step 6 they *will* be 192, so the constant becomes
  correct by itself — but check it against the shipped bitrate before story
  pauses are ever turned on.

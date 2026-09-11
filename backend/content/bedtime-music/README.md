# Bedtime music — where the four tracks come from

**Nothing at runtime reads this folder.** It is the reviewable source for
the four bedtime tracks, in the same way `story-ambience/` holds the sound
cues before they are rendered. The rendered audio itself lives beside the
narration, at `backend/src/ArmenianAiToy.Api/story-audio/music/`.

## The blocker this folder removes

`ContentSync:Music` shipped **empty** from the day the bedtime-music feature
(toggle, firmware playback, parent Music view) was built — everything
existed except rights-cleared tracks. This is the same blocker `story-
ambience/` hit for forest and river sounds, solved the same way: **generated
audio, no licence chain**, rather than sourcing and clearing real
recordings.

## How to read `tracks.json`

```jsonc
{ "id": "lullaby-melody",       // the manifest trackId and SD filename stem
  "title": "Օրորոցային մեղեդի", // Armenian, parent-dashboard-facing,
                                 // reviewed by the armenian-story-master agent
  "durationSeconds": 200,       // requested length sent to the generator
  "prompt": "...",              // reviewed English text sent to the generator
  "licence": "..." }
```

`prompt` is reviewed text, same discipline as `story-ambience/ambience-
cues.json`'s `sounds[].prompt`: it is sent to the generator as written, plus
two lines `tools/story-ambience/generate_music.py` appends in code rather
than repeating in every prompt string here — an instrumental-only
instruction and a no-real-work instruction. See `_rules` in the JSON for
why each field is shaped the way it is.

## The tool that reads this file

`tools/story-ambience/generate_music.py` — see its own docstring for the
endpoint, the honesty note on what could and could not be verified against
live ElevenLabs docs this session, the loudness target and why it is
quieter than narration, and the exact fade/encode pass. Dry run by default;
`--render --confirm-paid-api` to spend. `--self-test` needs no network.

## Licensing — resolved the same way ambience resolved it

Every track must be usable in a product that is given away now and sold
later. **The tracks are GENERATED, not recorded or licensed from a
library.** Generated audio has no licence FEE. That is not the same as "no
question": output ownership comes from the ElevenLabs plan's terms, not
from the audio being synthetic — recorded on every track's `licence` field
in `tracks.json`, the same honest phrasing `story-ambience/ambience-
cues.json` uses rather than implying the question does not exist.

## Why instrumental-only, and why no real composer or performer

Two owner constraints, both load-bearing:

- **Instrumental only.** No vocals, no lyrics, no singing, no spoken words.
  A child hearing a sung line in an unfamiliar voice at bedtime is a
  different product decision than ambient music, and this feature was never
  asked to make it — the toggle is "play music at bedtime", not "sing to my
  child".
- **No real composer, performer, ensemble or existing piece**, even where a
  prompt is allowed to say "Armenian folk-flavoured". The closest miss
  found on review: the bare word «Օրոր» is also the title of the best-known
  Armenian classical lullaby, so track 1 is named «Օրորոցային մեղեդի» (an
  adjective, "lullaby-like melody") rather than the noun itself. See
  `tracks.json`'s `_rules.noRealWork`.

## What a render session needs

1. `ELEVENLABS_API_KEY` in the environment (never in a file or commit).
2. This tracks file, owner- and armenian-story-master-reviewed.
3. `python3 tools/story-ambience/generate_music.py --render
   --confirm-paid-api` — renders all four, post-processes each (fade in 3s,
   fade out 8s, loudnorm, 192 kbps mono MP3, single ID3 tag), writes
   `story-audio/music/prompts.json` beside the output and prints the exact
   `ContentSync:Music` JSON row for each track.
4. `python3 tools/story-audio/check_music_audio.py` — structural gate
   (duration window, single ID3 tag).
5. Paste the printed rows into `appsettings.json`'s `ContentSync:Music`,
   replacing the placeholder rows this PR ships.
6. The human listen test. Still the last gate, as everywhere else.

Full sequence with exact commands: `docs/bedtime-music-render-runbook.md`.

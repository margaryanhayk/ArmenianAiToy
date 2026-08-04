# Welcome-flow greetings — sample listen test, 2026-08-05

**Verdict: PASS (text/pronunciation).** Owner listened and confirmed.

## Scope — deliberately 2 clips, not 43

The narrator voice is still **interim** (dad's ElevenLabs clone; an explicit
"continue with this, but then we will change it" decision). Rendering the whole
set now would mean re-rendering it when the voice changes, and the owner
listening to all of it twice — listening time is the scarce resource, not API
credits. So this test samples the two clips that stand in for the whole pool.

## Why these two

Every one of the 39 greetings opens with either «Բարև՛» or «Ողջու՛յն», roughly
half each. These two therefore cover both openings and the two sounds most
likely to come out wrong:

| Clip | Text | The risk it tests |
|---|---|---|
| `greet-01` | «Բարև՛, ես Արեգն եմ։» | the `և` ligature + a stress mark; the `ն-ե` liaison in «Արեգն եմ» |
| `greet-02` | «Ողջու՛յն։ Ուրախ եմ, որ եկար։» | the `ջ-ու-յն` cluster with a mid-word `՛` — **half the greeting pool depends on this one being right** |

## Render

- Tool: `tools/ElevenLabsRender --voice-clips --only greet-01 --only greet-02
  --render --confirm-paid-api`
- Model `eleven_v3` (the only model on the account that speaks Armenian),
  voice `areg-storyteller` (`NxAsEwnikgCJa5tyBwEf`), default speed — so the
  request carries no `voice_settings` and the voice's own saved settings apply.
- 47 characters, 2 requests.

| File | Bytes | Duration | sha256 |
|---|---|---|---|
| `greet-01.mp3` | 23,823 | 0:01 | `513526e8d078c1f27be2495f97d49f232f7be4eab668b5e9702c5ad850d4cc00` |
| `greet-02.mp3` | 31,346 | 0:01 | `13c32fd3a1e9c1cd845626e87e273aa8bf414da77eb682282c1ff0bf5fd86cb8` |

## What this PASS does and does not cover

**Covered:** the Armenian text is pronounceable and natural as written; the two
opening words and their stress marks render correctly; the reviewed wording
survives contact with the TTS.

**NOT covered — do not treat this as clearance to ship:**

- **The voice itself.** Explicitly out of scope; the owner was asked to judge
  the words only. The narrator is expected to change.
- **The other 41 clips.** Held until the voice is final, then rendered in one
  batch and listened to end to end in one sitting.
- **Loudness.** These samples never went through
  `tools/story-audio/Ship-StoryAudio.ps1`; the −16.4 LUFS library contract is
  checked at ship time, not here. Short clips at the level the API returns,
  interleaved with narration, is exactly the defect that makes a toy sound
  broken in a way no single file reveals.
- **Anything on hardware.** No clip has been synced to an SD card or played
  through the toy's speaker.

## Next gates, in order

1. Voice decision (owner). Until then nothing else here moves.
2. Render the remaining 41 → `Ship-StoryAudio.ps1 -Fix` → full listen test.
3. `ContentSync:Voice` config entries + deploy.
4. Bench session: sync to the card, hear the flow on the real toy.

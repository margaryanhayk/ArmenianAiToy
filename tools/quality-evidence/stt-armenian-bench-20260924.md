# Armenian STT benchmark — owner's voice, 2026-09-24

First real-voice run of `tools/stt-bench/stt_wer_bench.py`. One adult speaker
(the owner), one phone recording (m4a) of the 30-sentence list the toy
actually hears (menu answers, in-story questions, curiosity questions,
riddle/game answers, reflection answers), split on silence into 27 clips
(three clips held two sentences each; references merged to match). 16 kHz
mono WAV, `language=hy`, **no bias prompt** (production passes one on the
voice-intent and story-qa paths). Audio is NOT committed (owner's voice);
references are in the table below.

## Result

| provider | mean WER | mean CER | median latency | p90 latency |
|---|---|---|---|---|
| `openai:gpt-4o-mini-transcribe` (production today) | 64.0% | 52.1% | 0.73 s | 0.84 s |
| `openai:gpt-transcribe` | 21.3% | **2.5%** | 0.70 s | 0.83 s |
| `openai:whisper-1` | 62.8% | 15.3% | 1.20 s | 1.61 s |
| `elevenlabs:scribe_v2` (adult voice only) | 14.8% | 6.2% | 1.00 s | 1.35 s |

WER over-counts Armenian word-boundary slips («Նա պաստակը» vs «Նապաստակը»);
CER is the fairer accuracy number here.

## What it means

- **`gpt-4o-mini-transcribe` — what production runs today on all three STT
  keys — is badly wrong on short Armenian utterances.** Even with
  `language=hy` it returns Latin, Korean or Kurdish script for one- and
  two-word answers (the exact shape of menu / voice-intent / riddle answers):

  - «Հեքիաթ, խաղ» → `hekat, xah`
  - «Հանելուկ» → `Hane luk.`
  - «Այո, չէ» → `아이오, 최`
  - «Ուզում եմ հեքիաթ լսել» → `uzunêm hekayatlesel.`
  - «Չգիտեմ» → `Çûgîtim.`
  - «Սա ո՞վ է» → `Sa ove?`
  - «Գայլը վատն է՞» → `Kaj je levatne?`
  - «Ձկները քնու՞մ են» → `Såkner erkynummen?`
  - «Կատու» → `Gatu.`
  - «Արև» → `Aref`
  - «Հինգ» → `Hing`
  - «Մեկ էլ ասա» → `Mege laso.`
- **`gpt-transcribe` (OpenAI's named replacement) is accurate: CER 2.5%,
  Armenian script on every clip, same latency.** Its errors are word-boundary
  and single-letter slips («Հանելուք», «Կայլը»).
- `whisper-1` (still the code default if config is missing): script mostly
  right, many word errors, slowest.
- ElevenLabs Scribe v2 is also good (CER 6.2%) but ElevenLabs' policy forbids
  child voice data, so it can never serve the toy.

## Recommendation (needs owner approval — child-facing model switch)

Move `OpenAI:TranscriptionModel`, `StoryQa:TranscriptionModel` and
`Devices:VoiceIntentTranscriptionModel` to `gpt-transcribe`. That fixes the
accuracy problem above and the 2027-02-26 removal of
`gpt-4o-mini-transcribe` in one change. Before flipping production: confirm
`gpt-transcribe` accepts the `prompt` bias parameter the voice-intent and
story-qa paths send, and re-run this bench on a second speaker (ideally a
child, with parental consent).

## Bias prompt check (same session)

Which production paths send a `prompt` to STT:

| Path | Prompt sent |
|---|---|
| `/api/chat/audio` (online Game/Riddle/Curiosity/Calm) | **none** (`AudioChatController`) |
| story-qa | story `transcriptionBias` |
| after-story reflection | the reflection question |
| voice-intent | `VoiceIntentExpectations` yes/no or mode bias |

- Both `gpt-4o-mini-transcribe` and `gpt-transcribe` accept `prompt`
  (HTTP 200). «Այո, չէ» with the yes/no bias prompt: both correct.
- Full 27-clip re-run with the neutral prompt «Երեխան խոսում է հայերեն։»:

| Model | Mean CER | Non-Armenian-script replies |
|---|---|---|
| gpt-4o-mini-transcribe | 16.1% | 1 / 27 |
| gpt-transcribe | 3.8% | 0 / 27 |

So the prompted paths hide most of the current model's failure, but
`/api/chat/audio` sends no prompt and gets the 52% CER / wrong-script
behaviour above on every online game or riddle answer. `gpt-transcribe`
is accurate with or without a prompt. Adding a neutral prompt to
`/api/chat/audio` is a separate, child-pipeline change (owner approval).

NOT verified: child voices, the toy's own microphone (these are phone
recordings), the production bias prompts, `gpt-live-transcribe`.


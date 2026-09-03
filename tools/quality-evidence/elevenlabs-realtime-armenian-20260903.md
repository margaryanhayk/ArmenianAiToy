# ElevenLabs realtime models in Armenian — step 3 of the latency plan (2026-09-03)

`docs/latency-plan.md` Part 4, step 3: "Empirical hy test: Scribe v2 Realtime +
Eleven v3 Conversational — one afternoon, decides whether both realtime gaps
close at ElevenLabs." This is that afternoon. Script:
`tools/elevenlabs-realtime/el_realtime_test.py`; raw numbers beside this file
in `elevenlabs-realtime-armenian-20260903.json`.

Everything below ran from this container against the real API with the
account's own `areg-storyteller` clone (`NxAsEwnikgCJa5tyBwEf`), the same
voice every shipped story is narrated in.

## Verdict

| Model | Armenian | Speed | Use it? |
|---|---|---|---|
| **Eleven v3 Conversational** (TTS) | speaks it, in the clone | first byte **0.68–0.76 s** vs 1.18–1.51 s for `eleven_v3` | **Owner's listen test decides.** Technically yes: ~0.55 s off every answer, drop-in on the existing HTTP stream endpoint. |
| **Scribe v2 Realtime** (STT) | **not usable** — invents words, pads sentences, truncates commits | commit → final 0.3–1.0 s | **No.** Not today, not on clean studio audio. |
| Scribe v2 batch (STT, baseline) | good — WER 0.00 / 0.00 / 0.10 / 0.07 | 1.1–1.2 s for a 1.8 s clip; 3.4 s for 6 s; 12–15 s for 38 s | Candidate to replace `gpt-4o-mini-transcribe` on the question path only if it is measurably faster there — not proven here. |

So one realtime gap closes (TTS) and one does not (STT). Step 4 (stream the
mic upload while the child speaks) cannot ride on Scribe v2 Realtime; it needs
either OpenAI's realtime transcription or a server-side "upload while
recording, transcribe once at release" shape that keeps the batch model.

## TTS — same text, same voice, warm requests

`POST /v1/text-to-speech/{voice}/stream?output_format=mp3_44100_128`. The
`text-to-dialogue/stream` route was also tried and gives the same numbers, so
the conversational model needs no new endpoint in the backend adapter — it is
a `model_id` string.

| Sample (chars) | `eleven_v3` first byte / total | `eleven_v3_conversational` first byte / total |
|---|---:|---:|
| short (38) | 1.26 s / 2.98 s | **0.71 s** / 2.22 s |
| answer (91) | 1.28 s / 5.68 s | **0.69 s** / 4.04 s |
| story (166) | 1.26 s / 9.53 s | **0.68 s** / 6.69 s |

The JSON beside this file holds a SECOND run of the TTS half (the first run's JSON was overwritten by the STT-only rerun before the merge fix landed in the script). It agrees: conversational 0.76–0.84 s first byte against 1.36–1.40 s for v3, with one provider outlier (v3 short, 7.8 s first byte) that was not reproduced. The table above is the first run, the one the owner's six MP3s came from.

Cold first requests: v3 1.22–1.51 s, conversational 0.69–1.68 s (one cold
outlier at 1.68 s, the rest 0.69–0.75 s).

The conversational render is a little slower in delivery: the answer sample
plays 7.5 s against 6.0 s, the story 11.7 s against 10.7 s. Whether that
reads as calmer or as slow is a listening question. Six MP3s were handed to
the owner (`tts_<model>_tts_{short,answer,story}.mp3`) on 2026-09-03; the
verdict is not recorded here yet.

## STT — Scribe v2 Realtime, websocket, PCM 16 kHz, 100 ms chunks at real-time pace

Protocol confirmed against the Python SDK 2.66.0 source:
`wss://api.elevenlabs.io/v1/speech-to-text/realtime?model_id=scribe_v2_realtime&audio_format=pcm_16000&commit_strategy=manual&language_code=hy`,
header `xi-api-key`, chunks as `{"message_type":"input_audio_chunk","audio_base_64":…,"commit":false,"sample_rate":16000}`,
commit as the same message with `audio_base_64:""` and `commit:true`. The
session echoes the config back (`language_code: hy`), so the language was
applied.

| Clip | Expected | Realtime committed transcript | Batch `scribe_v2` |
|---|---|---|---|
| question (1.8 s, ElevenLabs render) | Ո՞վ է փոքրիկ ամպիկը։ | **Ուր է** փոքրիկ ամպիկը: **Ամպիկը ի՞նչ է:** | Ո՞վ է փոքրիկ ամպիկը (exact) |
| answer (6 s, clone) | Փոքրիկ ամպիկը մի փոքր, սպիտակ ամպ է, որն ապրում է երկնքում։ Նա շատ է սիրում խաղալ արևի հետ։ | **Հոգրի կամպիկը** մի փոքր սպիտակամպ է, որն ապրում է երկնքում: Նա շատ է սիրում խաղալ արևի հետ: **Այսինքն արևի հետ խաղալու համար նա ապրում է երկնքում: Այսինքն արևի հետ խաղալու հ** | exact (WER 0.00) |
| story, 3 sentences (10.7 s, clone) | Լինում է, չի լինում՝ մի աղքատ մարդ։ Էս աղքատ մարդը… | **Լինում է չիլինում** (then nothing) | WER 0.10 |
| story segment 0 (38 s, shipped narration) | 9 sentences | WER 0.21, ends in **«արա արա արա արա…»**, and the server committed on its own before the commit was sent | WER 0.07 |

Three failure modes, each seen more than once:

1. **Invented continuations.** The partials show it happening live —
   «Հոգրի կամպիկը ի՞նչ է անում այնտեղ», «արագ արագ արագ արագ», and the
   committed text carries a whole sentence that was never spoken. On a
   children's product a transcript that adds words the child did not say is
   worse than one that drops them: it is the input to moderation and to the
   model.
2. **Wrong short words.** «Ո՞վ» (who) became «Ուր» (where). The question
   words are exactly what an in-story answer hinges on.
3. **Truncated commits.** The three-sentence clip committed as its first
   three words, 0.9 s after the commit, and nothing followed in the next 3 s.

Tried and no better: `language_code=hye` (identical output), no language code
(auto-detect chose Hindi for the question and Cyrillic-transliterated the
story), `commit_strategy=vad` (never committed a 1.8 s clip).

Latency, for the record: at real-time pace the committed transcript arrived
0.33–0.98 s after the commit; first partial ~2.7 s after the first chunk. The
model is fast enough. It is the Armenian that is not there.

## What this changes in the plan

- Step 1 (negotiated streaming, merged 2026-09-03 as PR #30) plus the
  conversational model would take the TTS stage from ~0.7 s to ~0.2–0.3 s of
  the remaining wait — worth doing if the owner accepts the voice.
- Step 4 has to be designed around batch STT: stream the upload during
  recording so the file is already on the server at button release, then one
  batch call. Scribe v2 batch is accurate but not obviously faster than
  `gpt-4o-mini-transcribe` (1.1 s for a 1.8 s clip here; 0.5–1.1 s measured
  for OpenAI on 2026-09-02) — a head-to-head on the same clips is the next
  measurement before switching the STT provider.
- Re-check Scribe v2 Realtime when ElevenLabs announces an Armenian update;
  the script reruns in a minute with `ELEVENLABS_API_KEY` set.

## Key handling

The key was created for this test with Text to Speech, Speech to Text, Sound
Generation, Forced Alignment, Voices read and User read. `GET /v1/models`
still refused it (`models_read` missing), which does not affect anything
above. The key was pasted in chat and should be treated as burned: rotate it
after the listen test and put the replacement in the environment, never in a
file.

# stt_wer_bench.py

Scores Armenian speech-to-text candidates on WER/CER and latency, to choose
a replacement for OpenAI `gpt-4o-mini-transcribe` before OpenAI removes it
(along with `gpt-4o-transcribe` and `whisper-1`) on 2027-02-26 in favour of
`gpt-transcribe` / `gpt-live-transcribe`.

## Input

A directory of `.wav` files, each with a sibling `.txt` reference
transcript (UTF-8 Armenian) of the same stem, e.g. `sample01.wav` +
`sample01.txt`. A file with no match on the other side is skipped and
reported as a warning, not fatal.

## Providers

Pass `--provider` once per candidate (repeatable, so several run in one
pass):

- `openai:<model>` -- e.g. `--provider openai:gpt-4o-mini-transcribe
  --provider openai:gpt-transcribe`. `OPENAI_API_KEY` from the environment.
- `elevenlabs` -- `model_id` defaults to `scribe_v2` (override with
  `--el-model`). `ELEVENLABS_API_KEY` from the environment. **Refuses to
  run unless `--adult-voices-only` is passed.** ElevenLabs' use policy
  forbids uploading voice data of anyone under 18 -- **never send a
  child's recording to this provider**, adult reference recordings only.
- `azure` -- Azure Speech "fast transcription" REST API. Needs
  `AZURE_SPEECH_KEY` and `AZURE_SPEECH_REGION`. **Written from
  documentation, not live-verified against a real Azure endpoint** -- treat
  its output shape as unconfirmed until it's run once for real.
- `cmd:<command with {wav}>` -- runs a self-hosted model (e.g. Meta
  Omnilingual ASR, a fine-tuned Whisper) as a subprocess; `{wav}` is
  replaced with the file path, stdout is the transcript.

## Usage

```bash
# list pairs and providers, no network
python3 stt_wer_bench.py --audio-dir samples/ --dry-run

# real run
python3 stt_wer_bench.py --audio-dir samples/ \
    --provider openai:gpt-4o-mini-transcribe \
    --provider openai:gpt-transcribe \
    --out results.json
```

Prints a markdown table (files, failures, mean WER, mean CER, median/p90
latency) per provider. `--out` writes full per-file detail (reference,
hypothesis, WER, CER, latency) as JSON.

## Normalization / metrics

Both WER and CER normalize first: lowercase, strip punctuation (Armenian
`։ ՝ ՞ ՜ ՛ « »` plus ASCII punctuation), collapse whitespace. WER is word-
level Levenshtein distance over the normalized text divided by reference
word count; CER is character-level over the same text with spaces removed.
Levenshtein is implemented in pure Python (no dependency).

## Privacy

Recordings of children require parental consent to collect. Keep the audio
corpus **outside this git repository** -- this tool never adds anything
under `--audio-dir` to git, and none should be committed by hand either.
On top of that: never point the `elevenlabs` provider at a child's
recording (see above) -- adult voice samples only, and only with
`--adult-voices-only` explicitly passed.

## Tests

```bash
cd tools/stt-bench
python3 -m unittest
```

Covers normalization, WER/CER (including an exact-match = 0.0 case and a
one-word-substitution case), Levenshtein, percentile/summary aggregation,
the ElevenLabs adult-voices-only refusal (with and without the API key),
the `cmd:` provider, and `--dry-run` against a temp directory with a
generated silent `.wav` + `.txt` pair, both in-process and as a subprocess.

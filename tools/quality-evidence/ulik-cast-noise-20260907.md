# "Noise in some parts of the voices" — where it came from (2026-09-07)

Owner, after hearing the Ուլիկը cast pilot v12: *"In some parts of your
voices has noise. Where it come? Need to understand the reason and fix for
now and for future."* Measured, not guessed. Scripts in the session
scratchpad (`noise_probe.py`, `noise_spectrum.py`, `noise_ab.py`,
`samples.py`); the method is now inside `render_story.py` as `snr_db`.

Measure: floor = RMS of the quietest 10% of 50 ms windows (the between-word
floor), speech = RMS of the loudest 25%; SNR = speech − floor. All dBFS.

## 1. Our pipeline did not add it

Raw ElevenLabs take vs. the same take after our loudnorm + fades:

| span | raw floor | raw SNR | processed floor | floor change |
|---|---:|---:|---:|---:|
| 00-00 narrator | −52.3 | 40.2 | −52.6 | −0.3 |
| 00-01 mother | −54.9 | 41.3 | −54.0 | +0.9 |
| 03-01 **ulik** | −44.6 | 33.9 | −46.6 | −2.0 |
| 04-03 **ulik** | −41.8 | 31.3 | −43.8 | −2.0 |
| 04-00 narrator | −55.1 | 45.3 | −57.0 | −1.9 |

A click scan of the final v10 mix (largest sample-to-sample jump relative
to local level) found nothing above the numerical floor at any cut point:
the ambience inserts and the span joins are clean.

## 2. It is the Vardan clone, on every model and every setting

Same Ուլիկ line rendered seven ways:

| variant | model | format | floor | SNR |
|---|---|---|---:|---:|
| current (stability .4, style .45) | conversational | mp3 128k | −46.8 | 36.7 |
| stability .75, style .1 | conversational | mp3 128k | −45.5 | 34.5 |
| stability .4 | eleven_v3 | mp3 128k | −40.3 | 33.7 |
| stability .75, style .1 | eleven_v3 | mp3 128k | −43.0 | 34.3 |
| current | conversational | mp3 192k | −48.4 | 38.9 |
| current | conversational | pcm_44100 | — | 403, Pro tier only |
| **narrator (Areg), same line** | eleven_v3 | mp3 128k | **−50.5** | **42.2** |

No setting moves the Vardan floor more than a few dB. The account is
`creator`; 192 kbps is available and buys ~2 dB, PCM is not.

## 3. The source: one noisy 8-second sample

`GET /v1/voices/{id}` — `vardan-test` is `category: cloned` from **one**
sample, `vardan.mp3`, 136,850 bytes:

| clone | samples | sample floor | sample SNR |
|---|---:|---:|---:|
| **vardan-test** | 1 | **−43.5** | **27.7** |
| areg-storyteller | 2 | −61.0 / −59.6 | 40.7 / 37.8 |
| katrin-v3 | 7 | −57.7 / −53.1 / −45.6 | 43.8 / 27.8 / 34.0 |

An instant clone reproduces the room it was recorded in. Vardan's room is
~17 dB louder than Areg's, and that is exactly the gap in the takes. (One of
Katrin's seven samples, *Laughing*, is nearly as noisy — her clone averages
across seven, which is why her takes read 41 dB SNR and not 34.)

## 4. Fix for now — denoise the takes

ffmpeg filters on the two Ուլիկ takes (speech level unchanged in all):

| filter | floor change |
|---|---:|
| `afftdn` (FFT denoiser), nr 12 / 20 | −1 dB |
| `highpass 80 + afftdn` | −2 dB |
| **`anlmdn=s=3:p=0.002:r=0.006`** (non-local means) | **−10 to −13 dB** |

The noise is broadband inside the speech band (100–3000 Hz), which is why
the spectral gate barely touches it and the non-local-means filter does.
Applied to the two span WAVs, segments re-stitched from kept takes (no API
call; every segment length identical to the sample), ambience re-mixed with
the same cue sheet, levelled: `ulik-pilot-v13-denoised-with-summary.mp3`.
In the mix the Ուլիկ line at 1:41–1:53 went from a −42.0 dBFS floor to
−51.6. Handed to the owner beside v12 for the ear test.

Now permanent in `tools/story-voices/render_story.py`:
- per-speaker `"denoise": true` runs `anlmdn` BEFORE loudnorm (so the level
  is set on the voice, not on the room); set on `ulik` in
  `backend/content/story-voices/ulik.voices.json`.
- every rendered take prints `floor … dBFS SNR … dB`, and `NOISY` under
  36 dB (`SNR_WARN_DB`; Areg/Katrin read 40–44, vardan-test 31–34). The next
  noisy voice shows up on its first render, not on the owner's phone.

## 5. Fix for the future — re-clone Vardan from a clean sample

Tried from here and blocked: `POST /v1/voices/add` (with ElevenLabs' own
`remove_background_noise`) needs the `voices_write` permission, which the
key does not carry. Two ways, either is an owner action:
- in the ElevenLabs UI, re-clone Vardan with **Remove background noise**
  ticked, ideally from a longer, quieter recording (Areg's clone is 8 MB of
  samples; Vardan's is one 137 KB clip), or
- upload the pre-denoised copy of the existing sample that was handed to the
  owner (`vardan-sample-denoised.wav`: floor −52.6, SNR 36.7, from −43.5 /
  27.7).
Then point `ulik.voices.json` at the new voice id and drop `denoise`.

Not committed: the voice samples themselves (a person's recording is not
repo content) and the scratch A/B renders.

## 6. Same day, second listen: the denoiser was REJECTED, the clean clone was already on the account

The owner heard v13 and said *"it went more bad"*: `anlmdn` took 10 dB off
the floor and audibly damaged the speech with it. Lesson written into the
voices map: never denoise a voice, re-clone it. `denoise` is off for Ուլիկ.

Then the same line rendered with every voice on the account, 192 kbps:

| voice | clone source | line floor | SNR |
|---|---|---:|---:|
| vardan-test | 1 sample, −44 dBFS | −44.1 | 34.5 |
| **vardan-v2** | **8 samples, −60 / −59 dBFS** | **−57.0** | **48.3** |
| areg-storyteller | 2 samples, −61 / −60 | −53.2 | 43.9 |
| katrin-v3 | 7 samples, −58 / −53 | −54.1 | 44.6 |
| katrin-rec1 | 1 sample, −59 | −58.3 | 47.7 |

`vardan-v2` — the voice the owner had rejected on 2026-09-04, but only with
the +10% pitch lift — is 13 dB cleaner than vardan-test, at Areg's level.
Ուլիկ's two lines were re-rendered with it unshifted (`RENDER_ONLY=ulik`,
every other take kept; floors −59 / −58 dBFS, SNR 47 / 45; the transcript
guard re-asked one take that added two words), spans re-aligned, ambience
re-mixed, levelled: `ulik-pilot-v14-vardan-v2-with-summary.mp3`, handed to
the owner. The voices map now points Ուլիկ at vardan-v2, pitch 1.0.

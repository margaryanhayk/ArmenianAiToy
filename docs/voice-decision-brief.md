# Whose voice tells the stories — decision brief

**Written 2026-08-09. Nothing was rendered to produce this document; no API credits were spent.**

This exists because one unmade decision is blocking the launch. Every story MP3 and all
43 welcome clips are currently in the owner's own ElevenLabs clone, which the owner has
declared temporary, and the owner has also ruled that **all** audio gets re-rendered with
real acting before real families hear it. Until the voice is chosen, nothing can be
re-recorded, so nothing can ship.

The purpose of this brief is to turn that into a **listen-and-pick decision**: one
paragraph, rendered in a handful of candidates, one sitting.

---

## 0. The headline, before the evidence

The owner believes the open question is *"which ElevenLabs voice?"*. The 2026 landscape
says the real question is **"which provider?"** — and the most interesting candidate
(Google Gemini-TTS with native `hy` support and natural-language style direction) **did
not exist when this pipeline was built**. An audition that only compares ElevenLabs
voices would answer the wrong question and would have to be run twice.

Three facts found during this research change the shape of the decision, and none of them
are in CLAUDE.md today:

1. **ElevenLabs Professional Voice Cloning does not support Armenian at all.** The PVC
   language list is the Flash/Turbo v2.5 set — 39 locales, no Armenian. The owner's
   existing clone is operating outside the vendor's supported language set for cloning.
   ([PVC docs](https://elevenlabs.io/docs/product-guides/voices/voice-cloning/professional-voice-cloning))
2. **You cannot clone a hired actor on your own ElevenLabs account.** Verbatim: *"You can
   only create a Professional Voice Clone of your own voice. Even with their consent, you
   cannot clone someone else's voice."* This removes the obvious "hire a narrator and
   clone them" plan as a same-account option. (same source)
3. **Every ElevenLabs *Default* (premade) voice expires 31 December 2026.** *"All our
   Default voices will expire on December 31, 2026, and they will no longer be accessible
   after this date."* Picking a Default voice today means re-rendering the whole library
   again inside five months.
   ([Voices capability docs](https://elevenlabs.io/docs/overview/capabilities/voices.md))

And one more, which explains a lot of the disappointment to date:

> *"Professional Voice Clones (PVCs) are currently not fully optimized for Eleven v3,
> resulting in potentially lower clone quality compared to earlier models."*
> — [ElevenLabs v3 prompting guide](https://elevenlabs.io/docs/best-practices/prompting/eleven-v3.md)

v3 is the only model on the account that speaks Armenian. So the current setup is a PVC
(not optimised for v3) in a language PVC does not support, on the only model that has the
language. That is three compounding handicaps, and it is **not** a fixable settings
problem. It is strong evidence that the current voice sounds worse than the owner's real
voice for structural reasons, not because the clone was made badly.

---

## 1. The 2026 landscape for Armenian (hy-AM) synthetic speech

Checked against each vendor's **actual enumerated language list**, not marketing copy.

### Genuinely support Armenian

| Provider | Real Eastern hy? | Real-time? | Price / 1M chars | Character voice? | Cloning? |
|---|---|---|---|---|---|
| **ElevenLabs `eleven_v3`** | Yes — "Armenian (hye)" listed | **No** — vendor says not suitable for real-time | **$100** ($0.10/1K) | Yes, audio tags — but untested in Armenian | Yes, but **not for Armenian**, and not for third parties |
| **Google Gemini-TTS** | Yes — `hy` listed, **Preview** | **Yes** — streaming supported | ~**$17** (derived, see below) | **Yes — natural-language style direction** | No |
| **Azure AI Speech** | Yes — `hy-AM-HaykNeural` (M), `hy-AM-AnahitNeural` (F) | Yes | ~**$15–16** | **No — zero styles for Armenian** | **No — Armenian absent from Personal Voice AND Custom Neural Voice** |
| **Camb.ai** (MARS 8.1 beta / MARS-Instruct) | Yes — `hy-am` listed | Beta model only; no published latency | **Unpublished** — credits only | Yes — "director-level emotional control" | Yes, from 10–30 s reference |
| **Narakeet** | 2 genuine Armenian voices (Nune F, Tigran M) | No — file render only | ~$0.20/min of audio | No | No |
| **VS.AM** (Yerevan) | Claims Armenian TTS + Armenian voice cloning | Unknown | Contradictory public pages | Unknown | Claimed |

### Do NOT support Armenian (verified against their own lists)

- **Google Cloud TTS (classic)** — no `hy-AM` in Standard, WaveNet, Neural2, Studio,
  Chirp HD or Chirp 3-HD. ([voice list](https://docs.cloud.google.com/text-to-speech/docs/list-voices-and-types))
- **Amazon Polly** — 42 locales, no Armenian in any engine.
  ([voice list](https://docs.aws.amazon.com/polly/latest/dg/voicelist.html))
- **ElevenLabs Flash/Turbo v2.5** — 32 languages, Armenian absent. *This is why there is
  no real-time ElevenLabs Armenian and why no v3-realtime has shipped.* Vendor: *"For
  real-time and conversational use cases, we recommend staying with v2.5 Turbo or Flash
  for now."*
- Cartesia, Hume, Murf (*their own page*: "we currently don't support text to speech in
  Armenian"), Resemble, Speechify, MiniMax, Deepgram Aura-2, Rime, Fish Audio, Kokoro,
  WellSaid, Neuphonic — all checked, all no.

### OpenAI TTS — the honest answer on Armenian

Armenian appears in OpenAI's language list (it inherits Whisper's list), but OpenAI's own
docs say verbatim: **"Voices are currently optimized for English."** Community reports for
other non-English languages describe the result as a competent foreign speaker, not a
native — e.g. German output described as *"an American who speaks German really well."*
No Armenian-specific quality report was found, so treat this as **inferred, not proven**.

Price: `tts-1` **$15/1M chars**, `tts-1-hd` $30/1M. `gpt-4o-mini-tts` is token-priced
($0.60/1M text in + $12/1M audio out) and supports an `instructions` parameter for tone —
whether that steering survives into Armenian is undocumented and untested.

**This matters right now**: `OpenAI:TtsVoice` is what Areg speaks with live today. If
Armenian through OpenAI is accented-foreign, then the *live* half of the product has a
quality problem that is separate from, and currently hidden behind, the storyteller
problem. **Areg's own live voice should be in the audition too.**

### Gemini-TTS — the new entrant, and why it deserves a listen

This is the only Armenian-capable option with **both** expressive control **and**
streaming. Confirmed directly:

- Language list entry: `Armenian | hy`
  ([speech generation docs](https://ai.google.dev/gemini-api/docs/speech-generation))
- 30 named voices (Zephyr, Puck, Charon, Kore, Leda, Aoede, Callirrhoe, Sulafat …)
- Natural-language direction of *"style, accent, pace, and tone"*, plus inline tags like
  `[whispers]` / `[laughs]`, and full "audio profile + scene + performance notes" prompts
- 32k-token session context (so a whole story fits; chunking may still be prudent)
- Price: Flash TTS $0.50/1M text-in + **$10.00/1M audio-out tokens**, at a documented
  25 audio tokens per second. **Derived**, not quoted: 1M audio tokens ≈ 11.1 h ≈ $0.90
  per hour of audio ≈ **~$17 per 1M Armenian characters** at this project's own
  ~15 chars/sec narration rate. ([pricing](https://ai.google.dev/gemini-api/docs/pricing))

**Caveat, stated plainly:** Armenian is **Preview**, not GA. Preview can change or vanish.
It is a candidate to *listen to*, not a thing to build on before hearing it.

### Open source / Armenian-specific — effectively empty for commercial use

| Option | Eastern hy | Commercially usable |
|---|---|---|
| Meta MMS-TTS `hye` | Yes | **No — CC-BY-NC 4.0.** Disqualified. (Watch the trap: `facebook/mms-tts-hyw` is **Western** Armenian, wrong variant.) |
| Coqui XTTS-v2 | **No** (17 langs) | No — CPML non-commercial; Coqui Inc. dissolved, so no licence is purchasable from anyone |
| Piper — `davit312/piper-TTS-Armenian` | Yes (`hy_AM-gor-medium`, eSpeak `hy` = Eastern) | GPL-2.0 so legally yes, **but the repo README is 26 bytes** — no training-data provenance, no speaker consent record. Not adequate diligence for a children's product without contacting the author. |
| ArmSpeech corpus (~15.7 h) | Yes | CC-BY-NC, and its own licence statement is self-contradictory |
| Common Voice hy-AM (21.6 h, 589 speakers) | Yes | ASR-shaped crowd audio, not single-speaker studio data |

**Training a bespoke Armenian voice is currently blocked** by the absence of any
commercially-licensed single-speaker Armenian narration corpus. If a narrator is hired,
their session recordings would *become* that corpus — which is a real strategic asset, and
an argument for recording generously.

**Institutional note:** YerevaNN does no speech work. The plausible academic partner is
the Natural Language and Speech Processing Laboratory at IIAP NAS RA (project 23RL-1B028),
which has no shipped model. Not a launch-timeline option.

### The single most important unknown

**Not one vendor states whether its Armenian is Eastern or Western.** Azure labels the
locale "Armenian (Armenia)", which by convention is Eastern, but Microsoft never says so.
ElevenLabs never distinguishes. Gemini never distinguishes. **Every shortlisted candidate
must go through `armenian-linguistic-reviewer` and the owner's ear before any batch
render.** A Western Armenian narrator reading Tumanyan to Yerevan children is a
product-defining error that no amount of audio engineering fixes.

---

## 2. The three strategies

### (a) Clone a different real person — a hired Armenian narrator

**On ElevenLabs this is blocked twice over.** PVC has no Armenian, and third-party
cloning is prohibited even with consent. The documented workaround is that the actor
creates and verifies the PVC **on their own account** and shares it by private link.
Read what that means operationally: **the model, and the power to revoke it, live in
someone else's account.** For a product that must re-render its library for years
(new stories, text corrections like the «Հուռնի»→«Հուռուն» fix, the planned expressive
pass), that is a single point of failure owned by a contractor. Verification is a *voice
check* — recording yourself on similar equipment — specifically designed to stop what this
plan wants to do.

The alternative is a vendor that permits documented third-party cloning — Camb.ai (from
10–30 s of reference audio) or VS.AM. Both are plausible; neither publishes commercial
cloning terms, and Camb.ai does not publish a credits-to-characters rate at all
(discoverable only from an `X-Credits-Required` response header). **Both require a sales
conversation before they can be costed.**

**Cost:** actor session fee, plus an AI-clone licence with no market rate. NAVA explicitly
declines to publish rates. The only real benchmark found puts clone licensing at
$0.03–$0.20 per 1,000 characters — and Camb.ai's own worked example shows why that is
irrelevant here: 10 minutes of narration ≈ 9,000 chars ≈ **$0.27**. Per-character
royalties are worthless to an actor at this volume. **Offer a flat fee for a defined
term.**

**Paperwork** (from [NAVA's synthetic-voice checklist](https://navavoices.org/synth-ai-info/)):
separate explicit consent (not buried in the main agreement); limits on both AI outputs
and machine training; usage specificity with a stated adjustment if usage expands;
**specific start/end dates, not perpetuity**; opt-out rights; payment benchmarked to union
minimums; exclusivity stated; secure storage of the voice and all derived products. For
this product add two clauses NAVA does not list: **content prohibitions** (no political,
sexual, medical-advice or endorsement output) and **no resale / no sublicence** — both
matter when a synthetic voice speaks unscripted to children.

**Verdict: the most expensive path, with the worst structural dependency, and it is the
one the vendor already in place cannot do.** Not the first move.

### (b) Use a stock / preset synthetic voice

Three genuinely different sub-options:

- **b1 — an ElevenLabs premade voice on v3.** Zero new vendor, zero new code, already
  paid for, pipeline unchanged. But these are English voices reading Armenian with a
  foreign accent, and **the Default set dies 31 Dec 2026** — so if one wins, its Voice
  Library or Voice Design equivalent must be secured before committing. Candidates in §4.
- **b2 — Gemini-TTS Armenian with a style prompt.** Native `hy`, directable ("narrate
  warmly and unhurriedly, like a grandfather telling a bedtime story to a small child"),
  streaming-capable, ~1/6 the price of ElevenLabs. **This is the only option that could
  collapse the two-voices seam into one voice for the whole product.** Armenian is Preview.
- **b3 — Azure `hy-AM`.** Genuinely native Armenian, real-time, cheap, reliable. And by
  construction **a news reader**: no styles, no roles, no HD tier, no `mstts:express-as`
  for Armenian. Useful as **Areg's live utility voice**; it will never be a storyteller.

**Cost: near zero incremental.** This is the only strategy that can be tested this week.

### (c) Record a real human narrator in a studio — no TTS for the story library

This sounds extravagant until the library is actually measured:

| Asset | Characters | Finished audio (~15 chars/sec) |
|---|---|---|
| 8 approved stories | ~20,000 | **~22 minutes** |
| 43 welcome clips | ~1,300 | ~1.5 minutes |
| Per-story clips (intro/question/summary/offer/reoffer) | ~3,000 | ~3.5 minutes |
| **Total fixed library** | **~24,000** | **~27 minutes** |

**The entire child-facing fixed library is under half an hour of finished audio.** That is
one studio session.

Against published international benchmarks — audiobooks **$200–$500 per finished hour**,
and the closest published analogue, **toys/games at $500–$750 for up to a 2-hour session**
([Voice Crafters rate card](https://www.voicecrafters.com/industry-standard-voice-over-rates/))
— the whole library plausibly lands in the **low hundreds to ~$1,500** range, once, in
Yerevan probably below that. For comparison, the ElevenLabs cost to render the same
library is about **$2.40**. So this is genuinely ~100–500× the render cost — and still a
rounding error against the cost of a toy that children find boring.

**Be honest about what it costs operationally, not financially:** every new story needs
the narrator back. Every text correction becomes a re-record, not a re-run. There is no
`--render` for a human. That is a real recurring drag the TTS path does not have, and it
gets worse as the library grows.

**But architecturally it changes nothing.** `tools/story-audio/Ship-StoryAudio.ps1` was
deliberately built provider-independent — the pipeline doc already states it must accept
*"here are five MP3s"* from a TTS service, a studio, or a person with a microphone. The
`-16.4 LUFS` library contract, the sha256/size/Version discipline and the mandatory listen
test all apply unchanged. A human narrator drops straight into Stage 2.

### Which one I would pick

**(c) for the story library, (b2 Gemini) for Areg's live voice — but only after the
audition in §3, because the audition can legitimately overturn this.**

The reasoning:

1. **The owner has already ruled that the audio must be re-rendered with real emotion and
   acting.** No 2026 TTS *acts*. Gemini gets closest and Armenian is Preview and unheard;
   ElevenLabs v3 audio tags are undocumented for non-English. A human actor is the only
   option that satisfies the stated requirement by definition rather than by hope.
2. **The library is 27 minutes and fixed.** The economic argument against studio narration
   assumes a large or growing corpus. This corpus is small and deliberately bounded.
3. **The pipeline already expects this.** Nothing needs building.
4. **The "two voices on purpose" seam is already documented and accepted.** A human
   storyteller plus a synthetic Areg is not a new compromise — it is the existing design
   followed to its conclusion. Indeed a *human* storyteller versus a *synthetic* Areg is
   an easier seam to defend than two synthetic voices that nearly match: children accept
   "the storyteller" and "Areg" as different characters far more readily than they accept
   one character whose voice subtly changes.
5. **It removes the ElevenLabs dependency entirely** — the PVC-language gap, the
   third-party-cloning ban, the Default-voice expiry and the PVC/v3 quality ceiling all
   stop being this product's problems.

**Why the audition can still overturn it:** if Gemini's Armenian, directed with a warm
storyteller prompt, comes back genuinely good, then one provider covers both the library
*and* the live replies, re-renders cost nothing forever, and text corrections stay a
one-command operation. That is a materially better product operationally. It is worth
ten minutes of listening to find out — **and it is why the audition must not be
ElevenLabs-only.**

**What I would not do:** commit to a new ElevenLabs Default voice. It expires in five
months and would force a third full re-render of the whole library.

---

## 3. The ready-to-run sample plan

### The paragraph

**Story:** `khosogh-dzuk` — «Խոսող ձուկը», Հովհաննես Թումանյան.
**Segment index 1** (the second segment), **521 characters**.
File: `backend/src/ArmenianAiToy.Application/Stories/Content/khosogh-dzuk.story.json`.
Status: `approved`, linguistic review and listen test both 2026-08-03.

Quoted exactly as it appears in the story file:

```
- Լսի՛,- ասում է,- մարդ-ախպեր։ Ընկերներիս հետ ես խաղում էի գետի ալիքների մեջ։ Ուրախությունից ինձ մոռացա ու անզգույշ ընկա ձկնորսի ուռկանը։ Հիմի, ո՛վ գիտի, իմ ծնողը ինձ որոնում է ու լաց է լինում, հիմի ընկերներս տխրել են։ Ես էլ, տեսնում ես, ինչպես եմ տանջվում, շունչս կտրում է ջրից դուրս։ Ուզում եմ էլ ետ գնամ ապրեմ ու խաղ անեմ նրանց հետ էն պաղ ու պարզ ջրերում։ Էնպես եմ ուզո՜ւմ, էնպես եմ ուզո՜ւմ․․․ Եկ, խեղճ արի, ազատ արա ինձ, բա՛ց թող, բա՛ց թող գնամ․․․ Էսպես էր ասում ցա՜ծ, շա՜տ ցած ձենով, ցամաքած բերանը բացուխուփ անելով։
```

**Why this paragraph and not another.** It is the single best acting test in the whole
library, and it is why the choice is not arbitrary:

- It is **mixed mode** — third-person narration wrapped around sustained first-person
  direct speech. A voice that can only read will flatten the two together.
- It carries **real emotion with no violence and no fear** — a trapped fish pleading. Safe
  to play to a child while auditioning, which matters when the owner listens at home.
- It contains the **Armenian emphasis mark ՜** three times (`ուզո՜ւմ`, `ցա՜ծ`, `շա՜տ`) and
  the stress mark ՛ four times. If a voice ignores these, it will ignore them across the
  whole library.
- It ends with an **explicit stage direction inside the text** — *"he said this in a low,
  very low voice, opening and closing his dried-out mouth"*. The paragraph literally tells
  you what the delivery should sound like, so judging is objective rather than a matter of
  taste: **did the voice get quieter, or did it just say the words?**
- It contains **repetition for effect** (`էնպես եմ ուզո՜ւմ, էնպես եմ ուզո՜ւմ`,
  `բա՛ց թող, բա՛ց թող`). A news reader renders both halves identically; a storyteller
  does not.
- 521 characters ≈ **35 seconds**. Eight candidates ≈ **under 5 minutes of listening**.

### Rendering it with `tools/ElevenLabsRender`

**Read the tool before trusting any command — these are its real flags** (from
`tools/ElevenLabsRender/Program.cs`): `--story`, `--all`, `--clips`, `--voice-clips`,
`--only`, `--speed`, `--model`, `--max-chunk`, `--output`, `--render`,
`--confirm-paid-api`. Credentials come from the environment variables
`ELEVENLABS_API_KEY` and `ELEVENLABS_VOICE_ID`.

**Honest limitation: the tool has no per-segment flag.** `--story <id>` renders every
segment of a story as one narration file; `--only` filters by *output filename*, not by
segment. There is therefore no flag combination that renders segment 1 alone.

The clean path that needs **no code change and invents no flags** is the `--voice-clips`
mode, which renders arbitrary text from
`backend/content/voice-clips/voice-clips.json`. Add one temporary entry to the `clips`
array (delete it again after the audition — it must never ship as a real clip):

```json
{ "voiceId": "audition-01", "text": "- Լսի՛,- ասում է,- մարդ-ախպեր։ … բացուխուփ անելով։" }
```

…with the full 521-character paragraph above as the `text` value, then:

**Step 1 — dry run first (free, and it prints the text back for review):**

```powershell
dotnet run --project tools/ElevenLabsRender -- --voice-clips --only audition-01
```

Expect: `1 file(s) in 1 request(s), 521 characters, speed=1.0, model=eleven_v3`, then
`DRY RUN — nothing was sent to ElevenLabs.`

**Step 2 — render once per candidate voice.** `ELEVENLABS_VOICE_ID` is what selects the
voice, and the output filename is always `audition-01.mp3`, so give each voice its own
`--output` directory or they overwrite each other:

```powershell
$env:ELEVENLABS_API_KEY = "<key>"
$voices = @{
  "george"  = "JBFqnCBsd6RMkjVDRZzb"
  "brian"   = "nPczCjzI2devNBz1zQrb"
  "bill"    = "pqHfZKP75CvOlQylNhV4"
  "daniel"  = "onwK4e9ZLuTAKqWW03F9"
  "lily"    = "pFZP5JQG7iQjIQuC4Bku"
  "alice"   = "Xb7hH8MSUJpSbSDYk0k2"
  "matilda" = "XrExE9yKIg1WjnnlVkGX"
  "jessica" = "cgSgspJ2msm6clMCkdW9"
}
foreach ($name in $voices.Keys) {
  $env:ELEVENLABS_VOICE_ID = $voices[$name]
  dotnet run --project tools/ElevenLabsRender -- `
    --voice-clips --only audition-01 `
    --output "$env:TEMP\areg-audition\$name" `
    --render --confirm-paid-api
}
```

**Parameters deliberately NOT passed, and why:**

- **No `--model`.** The default is already `eleven_v3`, the only model that speaks
  Armenian. Overriding it is the exact mistake that produced the render the owner
  rejected on 2026-08-04.
- **No `--speed`.** At the default 1.0 the tool sends **no `voice_settings` object at
  all**, so each voice's own saved settings apply. Passing `--speed 1.0` is *not* a no-op:
  it replaces the saved settings. An audition must compare voices, not settings.
- **No `--max-chunk`.** 521 chars is far under the 4,000 default, so this renders as a
  single request per voice — no seams to confuse the judgement.

**Cost:** 521 chars × 8 voices = 4,168 chars ≈ **$0.42** at $0.10/1K.

**Two traps when listening — both have bitten this project already:**

1. **Do not judge loudness.** Raw ElevenLabs output sits near **-27 LUFS**; the approved
   library sits at **-16.4 LUFS**. That ~11 dB gap is what the owner previously heard as
   "thin, far away, bad quality" and mistook for a voice problem. Every candidate here is
   equally unlevelled, so the comparison is fair — but judge *timbre, warmth, pace and
   acting*, not volume. Level with `ffmpeg loudnorm` (recipe in the tool header) before
   any final verdict.
2. **Judge the pronunciation of Armenian separately from the voice.** These are English
   voices reading Armenian phonetically. Run the winner past
   `armenian-linguistic-reviewer` before any batch render, and check the watch-words
   already listed in `voice-clips.json`.

### Rendering the same paragraph on the other providers

`ElevenLabsRender` is ElevenLabs-only by design, so the non-ElevenLabs candidates are a
separate one-off REST call each. **Not run here** (no credits spent), documented so the
render step is mechanical:

| Provider | Endpoint | Key parameters for this test |
|---|---|---|
| **Gemini-TTS** | `generateContent` on `gemini-2.5-flash-preview-tts` | The paragraph, prefixed with an English style prompt, e.g. *"Narrate warmly and unhurriedly, like a grandfather telling a bedtime story to a small child. Speak the pleading lines softly and get quieter at the end:"*. Try 3 voices — **Sulafat**, **Aoede**, **Charon**. **This is the highest-value render in the whole audition.** |
| **Azure** | `cognitiveservices/v1` REST, SSML | `hy-AM-HaykNeural` and `hy-AM-AnahitNeural`. No `mstts:express-as` — Armenian has no styles. Expect a competent news reader; the purpose is to hear what genuinely-native Armenian pronunciation sounds like, as the pronunciation reference for judging everything else. |
| **OpenAI** | `/v1/audio/speech` | `gpt-4o-mini-tts`, with `instructions` set to the same warm-storyteller direction, plus the current `tts-1` + `nova` as the control. **This is the one that tells you whether Areg's existing live voice is acceptable in Armenian.** |
| **Narakeet** | web/file render | Voices **Nune** and **Tigran** — two genuinely Armenian voices, useful as a second native-pronunciation reference. |

**Recommended listening order** — pronunciation reference first, so the ear is calibrated
before judging the accented candidates: **Azure Hayk → Gemini Sulafat → ElevenLabs George
→ everything else.**

**Suggested total audition:** 8 ElevenLabs + 3 Gemini + 2 Azure + 2 OpenAI + 2 Narakeet =
**17 clips × 35 seconds ≈ 10 minutes**, total render cost well under **$1**.

---

## 4. ElevenLabs premade voices worth auditioning

All IDs below were verified individually against per-voice sources; none are from memory.
**All of these are Default voices and all expire 31 December 2026** — if one wins, secure
its Voice Library or Voice Design equivalent *before* committing to a full re-render.

**Male — like-for-like with the current male narrator**

| Rank | Name | Voice ID | Age / accent | Why |
|---|---|---|---|---|
| 1 | **George** | `JBFqnCBsd6RMkjVDRZzb` | middle / British | ElevenLabs' own title is literally **"Warm, Captivating Storyteller"**; labelled *warm + narration*. The strongest single candidate. |
| 2 | **Brian** | `nPczCjzI2devNBz1zQrb` | middle / American | "Deep, Resonant and Comforting", *deep + narration*. Suits bedtime; risk is being too low and slow for a 4-year-old. |
| 3 | **Bill** | `pqHfZKP75CvOlQylNhV4` | **old** / American | "Wise, Mature, Balanced". The grandfather-telling-a-tale register — arguably the most culturally right fit for Tumanyan. |
| 4 | **Daniel** | `onwK4e9ZLuTAKqWW03F9` | middle / British | "Steady Broadcaster". Backup only — authoritative/news risks sounding like a teacher, which MODES.md explicitly rejects. |

**Female**

| Rank | Name | Voice ID | Age / accent | Why |
|---|---|---|---|---|
| 5 | **Lily** | `pFZP5JQG7iQjIQuC4Bku` | middle / British | The female mirror of George — *warm + narration*, "Velvety Actress". Best female pick for warmth plus acting range. |
| 6 | **Alice** | `Xb7hH8MSUJpSbSDYk0k2` | middle / British | "Clear, Engaging Educator". Clarity is worth real money when a voice reads a foreign phoneme set. |
| 7 | **Matilda** | `XrExE9yKIg1WjnnlVkGX` | middle / American | *friendly + narration*. Warmer and less formal than Alice. |
| 8 | **Jessica** | `cgSgspJ2msm6clMCkdW9` | young / American | "Playful, Bright, Warm". Good for daytime tales; too bright for Calm/bedtime. |

**Deliberately excluded:** Charlotte (Swedish, *seductive/characters*), Callum ("Husky
Trickster"), Liam (energetic social-media, despite conflicting metadata), and
Sarah / Aria / Laura / Roger / Will / Chris / Eric / River (news, social-media or
conversational registers — wrong for storytelling).

**A weak vendor signal worth knowing:** ElevenLabs' own
[Armenian TTS page](https://elevenlabs.io/text-to-speech/armenian) showcases exactly five
voices — **Jessica, Laura, Alice, Bill, Brian**. Three of those (Bill, Brian, Alice) are
already in the shortlist above on independent grounds. It is marketing, not a
recommendation, but the overlap is mildly reassuring.

**Two things only a logged-in paid account can check, and both should be done before the
render session:**

1. **The official v3 curated voice collection** —
   `https://elevenlabs.io/app/voice-library/collections/aF6JALq9R6tXwCczjhKH`, referenced
   from the v3 prompting guide as *"a curated collection of voices for V3"*. Its contents
   are behind login. Voices in it are the ones ElevenLabs believes behave well on v3.
2. **Whether any Armenian-tagged voice exists in the Voice Library.** Filter Language =
   Armenian, then sort by Trending. **A genuine Armenian speaker's voice in the library
   would beat every candidate in this table** and would change the recommendation — it is
   the single highest-value five-minute check available.

**Vendor guidance worth heeding when judging:** the v3 prompting guide advises that
*"neutral voices tend to be more stable across languages and styles."* Armenian is far
outside these voices' training distribution, so **stability should be weighted above
character** — a voice that is merely warm and never wobbles beats a voice that is
wonderful on three sentences and strange on the fourth.

---

## 5. What is still open after this brief

- **Eastern vs Western Armenian is unstated by every single vendor.** Only the owner's ear
  and `armenian-linguistic-reviewer` can settle it.
- **Azure's exact price could not be confirmed from Microsoft** — their pricing page
  renders every figure as a `$-` placeholder. Third-party trackers say $15–16/1M; verify
  in the Azure calculator before committing.
- **Camb.ai's credits-to-characters rate is published nowhere.** Requires a sales
  conversation.
- **VS.AM (Yerevan) has two contradictory public pricing pages and no API docs.** It is
  nonetheless the vendor most likely to genuinely care about Eastern Armenian children's
  audio. **Worth a direct email regardless of which strategy wins.**
- **Whether v3 audio tags (`[whispers]`, `[sad]`) work in Armenian is undocumented.** The
  audition should test one tagged variant of the paragraph to find out.
- **EU AI Act Article 50 has been in force since 2 August 2026.** Two obligations plausibly
  bite: direct-interaction disclosure (a talking toy for 4–7-year-olds cannot rely on the
  "obvious to a reasonably well-informed person" exemption), and machine-readable marking
  of synthetic content — **which pre-rendered MP3s on an SD card do not obviously
  satisfy**. This is a lawyer question, not an engineering one, but it is *live now*, not
  future. **Note that strategy (c), a human narrator, sidesteps the synthetic-marking
  question for the entire story library** — a genuine and previously unnoticed advantage.
- **COPPA's 2025 amendments** expand personal information to include biometric
  identifiers; whether voice prints are explicitly named could not be confirmed (FTC and
  Federal Register both blocked). Relevant because the toy uploads child audio to a
  third-party transcription service.

---

## 6. The decision, in one page

1. **Run the audition in §3 this week.** ~17 clips, ~10 minutes of listening, under $1.
   It must include **Gemini** and **Azure**, not just ElevenLabs — otherwise it answers
   the wrong question and has to be run twice.
2. **Check the ElevenLabs Voice Library for an Armenian-tagged voice first** (five
   minutes, logged in). If one exists, it may end the search immediately.
3. **If Gemini's Armenian is good** → adopt it for both the library and Areg's live voice,
   collapse the two-voices seam, and the whole re-render problem becomes a one-command
   operation forever. Accept the Preview risk knowingly.
4. **If it is not** → hire an Armenian narrator and record the ~27-minute library in a
   studio, keeping synthetic TTS only for Areg's live improvised replies. Budget in the
   low four figures at most; the pipeline already accepts human MP3s unchanged; and it is
   the only option that satisfies "real emotion and acting" by construction rather than by
   hope.
5. **Either way, do not commit to an ElevenLabs Default voice** — the whole set expires
   31 December 2026 and would force a third full re-render inside five months.
6. **Whatever wins, record or render generously in one session.** A commercially-licensed
   single-speaker Eastern Armenian narration corpus does not exist anywhere in the world
   today. If a narrator is hired, their session tapes *are* that corpus — and that is a
   strategic asset well beyond this launch.

# Vendor terms and AI-toy laws — what applies to Areg (draft, 2026-09-23)

Companion to `parental-consent-draft.md` (N8) and `docs/ai-landscape-2026-09.md`
§ 1. **Not legal advice; no lawyer has read this.** Every quoted term comes
from a search-result snippet — the vendor and legislature pages were blocked
by this container's egress proxy — so confirm each against the live page
before relying on it.

---

## 1. Vendor terms vs. what Areg sends each vendor

| Vendor | What Areg sends today | Term that bites | Risk | Way out |
|---|---|---|---|---|
| **Google Gemini API** (chat, production per `appsettings.json` `AI` comment) | Child's transcribed words + conversation context | Gemini API Additional Terms: users must be 18+; must not be used in a service "directed towards or likely to be accessed by individuals under the age of 18" | **High.** Areg is directed at 4–7 year olds | Ask Google whether Vertex AI (Google Cloud terms + Cloud Data Processing Addendum) permits a child-directed service; if yes, flip `Gemini__Backend=vertex` (adapter built 2026-09-23; `docs/ops-runbook.md` § "Move Gemini chat to Vertex AI"). Fallback: OpenAI chat (already wired, `AI:ChatProvider=openai`) |
| **ElevenLabs** (story narration pre-rendered; optional live TTS) | Areg's reply text (live TTS, opt-in); story text (renders) | Prohibited Use Policy: no making the services available to under-13s, no "bundled solutions that target anyone under 13"; no voice data of anyone under 18 | **High for live TTS; grey area for pre-rendered stories** | Written permission / enterprise agreement naming the use case. Until then keep `AI:TtsProvider=openai` and do not flip `eleven_v3_conversational` live. Never send child audio to Scribe |
| **OpenAI** (STT, moderation, TTS, chat fallback) | **Raw child audio**, transcripts, reply text | Usage policies: 13+ for direct users; under-18 API guidance reportedly requires zero data retention (ZDR) before processing an under-13's personal data, plus COPPA compliance | **Medium** — compliant path exists | Apply for ZDR on the org; record the approval date in `docs/ops-runbook.md` |

Note: `parental-consent-draft.md` rows 5–6 describe Gemini as "opt-in". The
`appsettings.json` comment says production runs `chat=gemini` via Railway env.
The data map should say which is true on the live instance.

---

## 2. California SB 867 — is Areg a "companion chatbot"?

SB 867 (signed 2026-09-10) bans making or selling toys for minors that contain
a companion chatbot, **2027-01-01 → 2031-01-01**. Safeguards do not help; only
falling outside the definition does. Stand-alone voice assistants that do not
sustain a relationship are reportedly excluded.

Definition, element by element, against Areg's current design:

| Element of the definition | Areg today | Evidence in repo |
|---|---|---|
| Natural-language interface with adaptive, human-like responses | **Yes** — unavoidable for any voice toy | `ChatService`, five modes |
| Capable of meeting a user's **social needs** | Designed not to: play leader / storyteller, "never an emotional companion" | System prompt PERSONALITY; `CLAUDE.md` Product constraints |
| **Anthropomorphic features** | Minimised: no name ("You have NO NAME… never accept one a child offers") | System prompt first paragraph |
| Able to **sustain a relationship across multiple interactions** | Designed not to: the Absence Test forbids lines that imply continuity («I was waiting for you» fails); in-memory conversation state expires (N13 sweep) | `CLAUDE.md` Absence Test; `ConversationStateSweep` |

**Assessment (not a legal opinion):** Areg is built to fall outside the
definition, but three facts need a lawyer's read: a toy with a face/body is
arguably anthropomorphic on its own; the welcome greeting pool and "Ուրախ եմ
քեզ տեսնել" style lines; and whether story/serial continuity (Tsivik serial,
"next episode") counts as sustaining a relationship. New York's AB 11144
(5-year moratorium) awaits the governor and may use different wording.

**Product rule going forward:** any feature that adds long-term memory of the
child, a name for Areg, or lines about Areg's own feelings needs a legal check
against this table before it is planned.

---

## 3. Other rules with dates

| Rule | Date | What it asks | Areg gap |
|---|---|---|---|
| California SB 243 | in force 2026-01-01 | AI disclosure to known minors; break reminder every 3 h; sexual-content prevention; published self-harm protocol | System prompt says "Do not say you are an AI" — needs a disclosure route (box, parent onboarding, or a spoken line); no written self-harm protocol |
| California SB 1119 ("Adam's Law") | core rules 2027-07-01 | Child accounts default to limited memory, 1-h sessions, 2 h/day; crisis procedures | Daily cost cap ≈ turn cap, but no session-length or daily-time limit |
| COPPA amended rule | compliance by 2026-04-22 (passed) | Voiceprints are personal information; third-party disclosure needs separate verifiable parental consent | Child audio goes to OpenAI; registration has no verifiable-consent step (`parental-consent-draft.md` § 3, HARD STOP) |
| EU AI Act Art. 50 | 2026-08-02 | Tell users they are interacting with AI | Same as SB 243 |
| EU AI Act high-risk (AI in toys) | 2028-08-02 (after Digital Omnibus) | Conformity assessment | Only if selling in the EU |
| EU Toy Safety Regulation 2025/2509 | 2030-08-01 | Connected-toy cybersecurity/privacy | Later |

---

## 4. Draft messages (owner sends; fill the brackets)

### 4a. Google — Gemini / Vertex AI for a child-directed product

> Subject: Gemini for a children's educational toy — which terms apply?
>
> We build Areg, an Armenian-language storytelling toy for ages 4–7 with a
> parent dashboard and verifiable parental consent. A cloud backend (not the
> child) calls Gemini to generate short, moderated story and game replies;
> input and output are also checked by a separate moderation service. The
> Gemini API Additional Terms exclude services directed at under-18s. Does
> Vertex AI under the Google Cloud terms permit this use, and are there
> additional requirements (data processing addendum, zero data retention,
> region)? Expected volume: [N] devices, ~[M] requests/day.

### 4b. ElevenLabs — pre-rendered narration for a children's toy

*Corrected 2026-10-03 (C072, C043, C073): the first draft claimed a
"consented narrator clone" and asked for live TTS as if it were planned.
Every endpoint below was checked in the code (`tools/story-voices/`,
`tools/story-content/`, `tools/story-ambience/`, `tools/story-audio/`,
`tools/elevenlabs-realtime/`, `tools/stt-bench/`,
`ElevenLabsTtsSynthesisService.cs`, `InternalSystemController.cs`). Fill the
`[FILL IN …]` voice rows from
`tools/quality-evidence/voice-clone-provenance-2026-10.md` before sending;
send from the account email and record the date and ticket number in
`tools/quality-evidence/credential-rotation-2026-10.md`.*

> Subject: Written permission request — pre-rendered narration in a children's storytelling toy (ages 4–7)
>
> Hello,
>
> I run Areg, a small, pre-launch Armenian-language storytelling toy for
> children aged 4 to 7. A parent sets the toy up and links it to a parent
> account; the child only listens and talks to the toy. A formal verifiable
> parental-consent process is being designed and is **not yet in place**, so
> the toy is used today only within my own family. I am writing before
> anyone else's child uses it, because your Prohibited Use Policy restricts
> services that target children under 13.
>
> **How we use ElevenLabs today.** Every call is made by us, on my own
> account ([account email], plan [plan]): from our own machines, or from our
> backend for the quota read. No child, parent or toy ever calls
> ElevenLabs, and no child's voice or personal data is ever sent to you. The
> text is our own scripted Armenian text (stories, greetings, game lines).
>
> - `POST /v1/text-to-speech/{voice_id}` (models `eleven_v3` for narration
>   and `eleven_v3_conversational` for character lines) — narration,
>   character voices and short clips, rendered once and shipped as MP3
>   files on the toy's SD card.
> - `POST /v1/speech-to-text` — a quality check that transcribes our OWN
>   rendered narration back to text to catch artefacts. Never child audio.
> - `POST /v1/forced-alignment` — aligns our rendered narration with its
>   text.
> - `POST /v1/sound-generation` and `POST /v1/music` — background sounds
>   and instrumental bedtime music.
> - `GET /v1/user/subscription` — our operator console reads the remaining
>   character quota.
>
> In September we also ran one-off evaluations that are not part of the
> product: `/v1/text-to-speech/{voice_id}/stream`,
> `/v1/text-to-dialogue/stream` and `/v1/speech-to-text/realtime` on our own
> rendered narration, `GET /v1/voices/{voice_id}` (reading our own voices'
> sample metadata), and `/v1/speech-to-text` on recordings of my own
> (adult) voice.
>
> Our backend also contains an optional live-speech mode
> (`POST /v1/text-to-speech/{voice_id}` and `/stream`) that would voice
> short generated replies, which can include the child's first name. It is
> **switched off**; live speech uses another provider, and it stays off
> unless you say it is allowed.
>
> **The voices.**
>
> | Voice | Type | Source recordings, speaker's age, consent |
> |---|---|---|
> | areg-storyteller | Instant Voice Clone | [FILL IN from voice-clone-provenance] |
> | katrin-v3 | Instant Voice Clone | [FILL IN from voice-clone-provenance] |
> | katrin-rec1 | Instant Voice Clone | [FILL IN from voice-clone-provenance] |
> | vardan-v2 | Instant Voice Clone | [FILL IN from voice-clone-provenance] |
> | vardan-test | Instant Voice Clone | [FILL IN from voice-clone-provenance] |
> | areg-wolf | Voice Design (no source person) | — |
>
> **My questions — I would be grateful for a written answer:**
>
> 1. May we ship narration pre-rendered on our account (as above) inside a
>    product for children aged 4–7, where the child never interacts with
>    ElevenLabs? If this needs an enterprise agreement or other terms, what
>    do they involve?
> 2. Are the voices above acceptable under your voice-cloning terms, given
>    their sources?
> 3. Would the live mode (our backend sending moderated reply text, possibly
>    with a child's first name) ever be allowed, and on what terms? It is
>    not in use.
> 4. How long do you keep the text and audio we send (text-to-speech input,
>    and the rendered audio we send to speech-to-text), and can retention
>    or use for training be switched off for our account?
>
> Thank you,
> [name], [company/registration if any], [country]

### 4c. OpenAI — zero data retention

*Corrected 2026-10-03 (C060, C043): the first draft claimed "verifiable
parental consent" (not in place) and did not ask about abuse-monitoring
logs. Endpoints and data checked in the code (`DependencyInjection.cs`, the
OpenAI adapters under `Infrastructure/OpenAI` and `Infrastructure/Audio`,
`ChildService.BuildChildContext`); model names are the shipped defaults —
confirm them in the console System tab before sending. Send through the
sales contact form AND help.openai.com support chat; record the date and
both ticket numbers in `tools/quality-evidence/credential-rotation-2026-10.md`.*

> Subject: Zero Data Retention request — product for children aged 4–7 (org [org-…])
>
> Hello,
>
> Organisation ID: [org-…]. I run Areg, a pre-launch Armenian-language
> storytelling toy for children aged 4 to 7. Our own backend calls your API;
> children have no OpenAI account and never call you directly. A parent
> sets the toy up and links it to a parent account. A formal verifiable
> parental-consent process is being designed and is **not yet in place** —
> one reason the toy is used only within my own family today, and why no
> other family's child will use it before Zero Data Retention is confirmed
> for this organisation.
>
> **Exactly what we send:**
>
> - `POST /v1/audio/transcriptions` (model [`gpt-transcribe` — confirm]) —
>   the child's own voice recordings, a few seconds each: spoken questions
>   during a story, short voice-chat turns, answers to an after-story
>   question, and one- or two-word menu answers.
> - `POST /v1/moderations` (`omni-moderation-latest`) — the transcribed
>   words of every child turn, and every generated reply before the child
>   hears it.
> - `POST /v1/audio/speech` (model [`gpt-4o-mini-tts` — confirm]) — our
>   generated reply text, which can include the child's first name, turned
>   into the toy's voice.
> - `POST /v1/chat/completions` (model [`gpt-4o` — confirm]) — [today / if
>   we switch our chat to OpenAI]: the child's transcribed words, the recent
>   turns of the same conversation, and the child's first name, age and
>   gender as the parent entered them.
>
> We do not use the Responses or Assistants APIs, files, batch or
> fine-tuning. Expected volume: [N] toys, about [M] requests per day.
>
> **My requests and questions — I would be grateful for a written answer:**
>
> 1. Please enable Zero Data Retention for this organisation on the four
>    endpoints above, and confirm in writing which endpoints it covers and
>    from what date.
> 2. Does `/v1/audio/speech` keep abuse-monitoring logs (for example for 30
>    days) even under Zero Data Retention? If so, what do they contain, and
>    can they be excluded?
> 3. The same question for `/v1/audio/transcriptions`: is the uploaded audio
>    or the transcript kept anywhere under Zero Data Retention?
> 4. Given that our end users are children aged 4–7, is anything else
>    required from us under your under-18 guidance (a data processing
>    addendum, specific terms)?
>
> Thank you,
> [name], [company/registration if any], [country]

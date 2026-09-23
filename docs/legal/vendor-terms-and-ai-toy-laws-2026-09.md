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
| **Google Gemini API** (chat, production per `appsettings.json` `AI` comment) | Child's transcribed words + conversation context | Gemini API Additional Terms: users must be 18+; must not be used in a service "directed towards or likely to be accessed by individuals under the age of 18" | **High.** Areg is directed at 4–7 year olds | Ask Google whether Vertex AI (Google Cloud terms + Cloud Data Processing Addendum) permits a child-directed service; if yes, move the adapter to Vertex AI. Fallback: OpenAI chat (already wired, `AI:ChatProvider=openai`) |
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

### 4b. ElevenLabs — narration and live TTS for a children's toy

> Subject: Permission request — children's storytelling toy (ages 4–7)
>
> We use ElevenLabs Eleven v3 with our own consented narrator clone to
> pre-render Armenian children's stories, and would like to use
> eleven_v3_conversational for short live replies. No child audio is ever
> sent to ElevenLabs; only our own reply text. Your Prohibited Use Policy
> restricts services targeting under-13s. Can we obtain written permission
> or an enterprise agreement covering (a) pre-rendered narration shipped on
> the toy and (b) live TTS of moderated reply text? Account: [account email].

### 4c. OpenAI — zero data retention

> Subject: Zero data retention request — children's product
>
> Org ID [org-…]. We operate a children's toy (ages 4–7) with verifiable
> parental consent. We send child speech to the transcription endpoint and
> text to moderation and chat. Per your under-18 API guidance we request
> zero data retention for this organisation on /v1/audio/transcriptions,
> /v1/moderations, /v1/chat/completions (or responses) and /v1/audio/speech.

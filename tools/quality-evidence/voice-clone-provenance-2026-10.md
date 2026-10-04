# Voice-clone provenance — template, 2026-10 (C073)

Whose voice each ElevenLabs voice was built from, so the ElevenLabs letter
(`docs/legal/vendor-terms-and-ai-toy-laws-2026-09.md` § 4b) describes the
voices truthfully and any voice that cannot stay is caught before launch.
**Fill this in before letter 4b is sent.**

What goes in: names, ages, dates, the NAME of a consent document and where
it is kept. What never goes in: an API key, the recordings themselves, a
copy of a signed document, or anyone's contact details.

Where to look: ElevenLabs → Voices → the voice → its samples and creation
date. The original recordings' location goes in the password manager
("Voice recordings — location", `docs/continuity.md`).

Status: **TEMPLATE — no answers recorded yet.**

## 1. Facts already in the repo

| Voice | Voice ID | ElevenLabs category | Samples | Used in |
|---|---|---|---|---|
| areg-storyteller | `NxAsEwnikgCJa5tyBwEf` | cloned (Instant Voice Clone) | 2 | Narrator of every shipped story and its clips; the welcome clips; Areg's lines in game clips |
| katrin-v3 | `bpss31fpiTyvjz5XZUT0` | cloned | 7 | Story characters (cast library); child characters in game clips |
| katrin-rec1 | [FILL IN from ElevenLabs] | cloned | 1 | Cast pilot only (2026-09-03/07); [FILL IN: shipped anywhere? expected no] |
| vardan-v2 | `PgFHHZMKb19tJbjBe6aY` | cloned | 8 | Story characters (cast library); child characters in game clips |
| vardan-test | [FILL IN from ElevenLabs] | cloned | 1 (`vardan.mp3`) | Replaced by vardan-v2 on 2026-09-07; [FILL IN: still in any shipped file? expected no] |
| areg-wolf | `5WzpQqbTMzWBCHk0Mol1` | Voice Design (no source person) | — | The wolf in the cast library |

Sources: `backend/content/story-voices/*.voices.json`,
`tools/story-content/render_clips.py` (whose header says the characters
"are not real people, so there was nobody to ask" — true of the
characters, not necessarily of whoever recorded the clone samples; that is
what § 2 settles),
`tools/quality-evidence/ulik-cast-noise-20260907.md` § 3,
`tools/quality-evidence/ulik-cast-pilot-20260903.md`.

## 2. Owner answers (one row per cloned voice)

| Voice | Whose recordings | Speaker's age when recorded | Recording date(s) | Consent / AI clause (document name, where kept) | Self, adult third party, minor, or unknown? |
|---|---|---|---|---|---|
| areg-storyteller | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| katrin-v3 | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| katrin-rec1 | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| vardan-v2 | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| vardan-test | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |

## 3. Decision per voice

The rule (CLAUDE.md "never" list; ElevenLabs forbids cloning another
person's voice even with consent): a voice whose source is **not the
owner**, is **a minor**, or is **unknown** stops being used now and is
re-cast through Voice Design (no source person) and re-rendered, followed by
a fresh listen test (owner-content, ht-listen-loudness packages).

| Voice | Keep / stop and re-cast | Date decided | Files that must be re-rendered (if stopped) |
|---|---|---|---|
| areg-storyteller | [FILL IN] | [FILL IN] | [FILL IN] |
| katrin-v3 | [FILL IN] | [FILL IN] | [FILL IN] |
| katrin-rec1 | [FILL IN] | [FILL IN] | [FILL IN] |
| vardan-v2 | [FILL IN] | [FILL IN] | [FILL IN] |
| vardan-test | [FILL IN] | [FILL IN] | [FILL IN] |

## 4. Letter 4b

| Sent via | Date | Ticket number | Reply date | Outcome (one line) |
|---|---|---|---|---|
| [FILL IN: support / sales] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |

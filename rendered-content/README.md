# Rendered SD-card content (durable home)

The owner-approved, listen-tested MP3 sets for the toy's SD card.
Rescued out of a session temp directory 2026-08-06 (weak-point audit
Tier 1: a Windows temp sweep would have destroyed the only copies).

| Folder | Files | SD destination | Contract |
|---|---|---|---|
| `quiz/` | 53 | `/quiz/` | qNN-**y**/-**n** suffix IS the answer key (GREEN/RED button). Never rename; answer changes = new id. |
| `voice/` | 43 | `/voice/` | Welcome-flow clips; ids per `backend/content/voice-clips/`. |
| `vk-games/` | 16 | `/quiz/` | Vardan-vs-Katrin rounds (vkNN-y/-n) + reaction clips. |
| `render-meta/` | — | — | Judge-question variant texts + bake-off lines (provenance). |

Render rules baked into these files (owner's ear, 2026-08-05/06):
full `-ը` article before vowels (TTS swallows euphonic ն); every take
tail-trimmed 0.45s (inhale removal); onomatopoeia as bare hyphenated
pairs; repeated lines rendered once and spliced.

Source texts: `backend/content/{quiz-questions,voice-clips,vardan-katrin-games}/`.
Re-render pipeline: eleven_v3, voices areg-storyteller / katrin-v3 /
vardan-v2 (ids in the project memory).

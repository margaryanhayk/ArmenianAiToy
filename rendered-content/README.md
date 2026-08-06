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

## Offline-game renders (2026-08-07)

| Folder | Files | SD destination | Note |
|---|---|---|---|
| `offline-games/<game>/` | 90 | `/games/<game>/` | Subfoldered because four games each define a clip called `intro` — the firmware resolves `/games/<game-key>/<id>.mp3`. |
| `vk-games/` (+15 new) | 31 | `/quiz/` | Vardan-vs-Katrin reaction VARIANTS (`win-vardan-1..3` etc.) — the firmware rotates them so a reaction never repeats twice running. |

Rendered from the owner-reviewed texts in
`backend/content/{offline-games,vardan-katrin-games}/`, eleven_v3,
voices areg-storyteller / katrin-v3 / vardan-v2. All 105 verified as
real MP3s (ID3/frame header + size floor); zero failures.

> **NOT the launch set (owner decision 2026-08-07).** These are
> accepted as a working library so the toy can be bench-tested. Before
> the first families get toys, every child-facing clip gets an
> EXPRESSIVE re-render — emotional delivery, not merely correct
> Armenian — followed by a full listen test. Do not treat these files
> as final.

Two clips the firmware wants that are NOT here yet: Simon's two tone
sounds (`tone-green` / `tone-red`) — non-verbal, no Armenian to review.

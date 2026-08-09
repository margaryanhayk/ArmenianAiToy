# Voice audition — same paragraph, six voices

Rendered 2026-08-10 so the narrator decision becomes a listen, not a debate.

**The text** (`00-the-text.txt`): «Խոսող ձուկը», segment 1 — 521 characters,
already approved and listen-tested, so nothing about the words is in question.
It was chosen because it **ends with its own stage direction**: the fish speaks
«ցա՜ծ, շա՜տ ցած ձենով» — *in a low, very low voice*. So the test is objective:

> **Did the voice actually go quiet at the end, or did it just say the words?**

Also listen for: does it sound like a person telling a story to a child, or a
person reading a page aloud? Does it hurry the «Էնպես եմ ուզո՜ւմ» repetition or
let it breathe?

**The clips** — all `eleven_v3` (the only ElevenLabs model that speaks
Armenian), no `voice_settings`, no speed change, so the only variable is the
voice itself:

| File | Voice |
|---|---|
| `01-rachel-f.mp3` | female |
| `02-bella-f.mp3` | female |
| `03-charlotte-f.mp3` | female |
| `04-antoni-m.mp3` | male |
| `05-adam-m.mp3` | male |
| `06-daniel-m.mp3` | male |

Compare against the shipped narration in `backend/.../story-audio/` — that is
the owner's own clone, the current sound.

## What this audition can and cannot settle

**Can:** whether a stock synthetic voice reading Armenian is acceptable at all
for a children's story, and which end of the range is closest.

**Cannot:** the final choice. Every voice here is an ElevenLabs *Default* voice,
and per `docs/voice-decision-brief.md` **all Default voices expire 31 December
2026** — none of them can be the permanent narrator. If one of these sounds
right, that tells us the *type* of voice to secure permanently (a hired
narrator, or a provider whose voices do not expire). If none sound right, that
is the strongest argument for recording a real human — the whole shipped
library is only ~27 minutes of finished audio.

Not yet auditioned, and they should be before a final decision:
**Google Gemini-TTS** (now lists Armenian, with spoken-style direction) and
**Azure `hy-AM`** (HaykNeural / AnahitNeural). Both need their own API key.

Durations 38.0-48.3 s against an expected ~35 s for 521 characters — checked so
that a curtailed render (the v3 failure mode that shipped truncated stories in
August) could not be mistaken for a bad voice.

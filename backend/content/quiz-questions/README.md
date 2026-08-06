# Offline quiz questions (the 3-button true/false game)

Every question the offline quiz speaks lives here as **text**, is rendered
once to MP3, and is copied to the toy's SD card under `/quiz`. Nothing at
runtime reads this folder — it exists so the Armenian is reviewable in one
place and a diff shows honestly what changed. Same convention as
`../voice-clips/`.

## The filename IS the answer key

`q04` with `answer: yes` renders to **`/quiz/q04-y.mp3`**. The firmware
(`esp32/AregVoiceMvp/offline_quiz.cpp`) compares the GREEN/RED button press
against the `-y`/`-n` suffix — that is the entire verification mechanism:
no cloud, no model, genuinely checked.

Consequences:
- Editing a question's **text** after rendering ⇒ re-render that one clip.
- Editing an **answer** ⇒ the filename suffix must change too, or offline
  verification silently inverts. Don't edit answers; retire the id and add
  a new one.

## Game flow the clips serve (owner-approved loop, 2026-08-05)

play question → GREEN/RED within the answer window →
right → `win.mp3` → next question ·
wrong → `wrong.mp3` → same question once more → move on ·
silence → re-ask once → second silence ends the quiz quietly (never badger).
`done.mp3` plays after the last question.

## Status

armenian-story-master reviewed 2026-08-05 (5 lines corrected: q04, q17,
q19 ՞-placement/euphony; `wrong`/`done` warmth). **Pending the owner's
listen test.** Per the render note: sample ONE question (q17 or q19 — they
carry the hard consonant clusters) plus the three feedback lines before
batch-rendering.

TTS watch words: «մլավում», «թռչում/Թռչունները», «ցատկում», «Ձմռանը»,
«երկնքու՞մ/երկնքի՞ց», the «սա՞ռն է» junction.

**TTS rule learned 2026-08-06 (owner listen test):** the voice
SWALLOWS the euphonic final ն of the definite article before a
vowel-initial word — «Աստղերն երկնքում» came out as bare «աստղեր».
In TTS-bound text prefer the full «-ը» form («Աստղերը երկնքում»)
even where book grammar wants «-ն». q17/q19 fixed accordingly;
check any «-ն + vowel» junction at sample time before batch renders
(the VK rounds vk04 «Փիղն ամենամեծ» and vk09 «Նապաստակն…» carry the
same pattern — listen for the ն in their v1 renders).

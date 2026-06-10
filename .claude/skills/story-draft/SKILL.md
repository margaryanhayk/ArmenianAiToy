# Story Draft

## When to use

Invoke when drafting a new Armenian curated story (ages 4–7) for the
content-based story library. This is a **development-time authoring
skill** — it produces draft JSON data only. It never touches runtime
code, never approves stories, and never calls paid APIs.

## What it enforces

Safe authoring of `*.story.json` drafts: correct schema, draft-only
status, original child-safe Eastern Armenian content, mandatory
armenian-story-master review, and a promotion checklist that only a
human can execute.

## Output location (the ONLY place this skill writes stories)

```
backend/content/story-drafts/<id>.story.json
```

- `<id>` is kebab-case (`^[a-z]+(-[a-z]+)*$`) and matches the JSON `id`.
- This folder is outside `backend/src` — drafts are structurally
  invisible to the runtime (`StoryDraftFolderTests` pins this).
- NEVER write into `backend/src/ArmenianAiToy.Application/Stories/Content/`
  (approved, embedded, runtime-served). NEVER edit files already there.

## Schema (v1 — see `StoryFileSchema.cs`)

```json
{
  "schemaVersion": 1,
  "id": "<kebab-case-id>",
  "title": "<Armenian title>",
  "minAge": 4,
  "maxAge": 7,
  "tone": "warm",
  "tags": ["<from allowed vocabulary only>"],
  "category": "general | therapeutic | prophylactic",
  "bedtimeSafe": true,
  "segments": ["<segment 1>", "<segment 2>", "<segment 3>"],
  "reflectionText": "<one gentle storyteller sentence>",
  "reflectionQuestions": ["<one safe question>"],
  "review": {
    "status": "draft",
    "linguisticReviewAt": null,
    "listenTestAt": null,
    "notes": null
  }
}
```

Hard schema rules for NEW drafts:
- `review.status` MUST be `"draft"` — always, no exceptions.
- `linguisticReviewAt` MUST be `null`.
- `listenTestAt` MUST be `null`.
- Tags only from the bounded vocabulary in
  `StoryFileParser.AllowedTags` (friendship, kindness, sharing,
  patience, curiosity, courage, calm, nature, family, helping).
- Strict JSON: no comments, no trailing commas, no extra fields —
  the parser disallows unmapped members.

## Steps

1. **Intake.** Confirm the story idea: theme, category
   (general / therapeutic / prophylactic), and whether bedtime-safe.
   Default `bedtimeSafe: true` unless the review explicitly rejects it.
2. **Draft.** Write `backend/content/story-drafts/<id>.story.json`
   following the schema above and ALL content rules below. Original
   material only.
3. **armenian-story-master review (REQUIRED).** Launch the
   `armenian-story-master` agent on the draft's full Armenian text
   (segments + reflectionText + reflectionQuestions). Apply its "exact
   improved Armenian version" verbatim. Re-review after any text edit.
   A draft that has not passed this review is not ready for the
   promotion checklist.
4. **Lint via tests.** From `backend/`:
   ```
   dotnet test --nologo -v minimal
   ```
   `StoryDraftFolderTests` sweeps the drafts folder: schema must parse
   (`requireApproved: false`), status must be `"draft"`, and the draft
   id must not collide with a runtime story id.
5. **Output the promotion checklist** (below) and STOP. This skill
   cannot promote, approve, stamp dates, render audio, or commit.

## Content rules

### Armenian quality
- Natural spoken **Eastern Armenian**, child register, ages 4–7.
- No Russian calques, no English idiom calques, no translated-sounding
  rhythm.
- TTS-friendly: short single-clause sentences (≤10–12 words), natural
  pause points, Armenian punctuation only («», «։», «,», «՝», «՞» on
  the questioned word, «՛»/«՜» sparingly).
- Armenian script ONLY — no Latin, no Cyrillic, no digits (spell
  numbers out), no emoji, no markdown, no symbols.
- Short segments: one scene beat each, 2–4 sentences (~≤300 chars).

### Story structure
- Exactly **3 segments**, each ending at a natural soft pause that
  carries into the next — never a fear cliffhanger, never a question
  (segments are autoplayed).
- `reflectionText`: one gentle storyteller sentence — warmth, not
  pedagogy, no morals-as-lecture.
- Exactly **1 reflection question**, answerable by a four-year-old
  from the story alone. No fear-recall, no emotional-companion
  phrasing.
- `bedtimeSafe: true` unless explicitly rejected in review. Bedtime-safe
  stories additionally need descending energy, no startles, no
  chase/threat beats, zero questions/exclamations in segments.
- No second-person text assuming the child's name, gender, or age.

### Originality
- Original material ONLY. Never copy or adapt internet/copyrighted
  stories. No folklore characters or adaptations (postponed product
  decision). No famous copyrighted characters.

### Therapeutic / prophylactic stories
- **Gentle normalization through story only** — a character
  experiences something familiar (a doctor visit, a dark room, a new
  sibling) and it turns out okay.
- NO diagnosis. NO medical advice. NO instructions.
- NO fear or shame manipulation. NO harsh moralizing.
- Parent-trust language: the toy is a storyteller, not a therapist;
  nothing a parent would need to undo or explain away.

## Interruption Q&A readiness

A future runtime slice will let a child interrupt a library story and
ask questions answered ONLY from the story's own content (bounded
in-story Q&A — see MODES.md GPT boundary). Every draft must carry
enough clear, self-contained context for that:

- **Character names stable** — same name for the same character in
  every segment; no unnamed "he/she" drift across segments.
- **Setting clear** — where the story happens is stated plainly in
  segment one.
- **Conflict simple** — one concrete, nameable problem a four-year-old
  can restate.
- **Emotional theme explicit but gentle** — the feeling is named or
  obvious from action, never implied through subtext only.
- **No ambiguous scary events** — nothing that invites "but WHY did
  that happen?" with a frightening answer.
- **No hidden lore** — nothing requiring outside knowledge, prior
  stories, or cultural references to understand.
- **Reflection question safe** — answerable from the story, never
  probing the child's fears or private life.

Do NOT implement any Q&A runtime code from this skill — these are
authoring requirements for the data only.

## Promotion checklist (output only — humans execute this, never the skill)

```
Story draft promotion checklist — <id>
======================================
[ ] armenian-story-master review: PASS (date, verdict noted in review.notes)
[ ] dotnet test green (StoryDraftFolderTests + full suite)
[ ] TTS listen test on the PRODUCTION voice (paid — human-triggered only);
    mispronounced words rewritten + fresh review, never phonetically hacked
[ ] Native-speaker read-aloud pass
[ ] HUMAN moves file to backend/src/ArmenianAiToy.Application/Stories/Content/
[ ] HUMAN flips review.status to "approved"
[ ] HUMAN stamps linguisticReviewAt and listenTestAt
[ ] HUMAN deletes the draft copy (a story lives in exactly one place)
[ ] HUMAN commits as a data-only story slice
```

## Constraints

- Do NOT set `review.status` to anything but `"draft"`.
- Do NOT write or edit anything under
  `backend/src/ArmenianAiToy.Application/Stories/Content/`.
- Do NOT touch runtime code, ChatService, controllers, DI,
  appsettings, or firmware.
- Do NOT call paid APIs (no TTS render, no MP3, no OpenAI calls).
- Do NOT stamp `linguisticReviewAt` / `listenTestAt`.
- Do NOT skip the armenian-story-master review step.
- Do NOT stage, commit, or push — report and stop.

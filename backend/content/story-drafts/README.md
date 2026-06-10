# Story drafts — NEVER served to children

This folder holds **draft** curated-story JSON files
(`<id>.story.json`, schema v1 — see
`backend/src/ArmenianAiToy.Application/Stories/StoryFileSchema.cs`).

## Hard rules

- **Drafts are never served to children.** This folder lives outside
  `backend/src` and is not part of any project; nothing here is
  compiled, embedded, or loaded by the runtime. The runtime library
  loads ONLY embedded resources from
  `backend/src/ArmenianAiToy.Application/Stories/Content/`, and its
  loader hard-fails on any file whose `review.status` is not
  `"approved"`.
- Every draft file in this folder MUST have `review.status: "draft"`.
  A file marked `approved` sitting here fails `StoryDraftFolderTests`
  on the next `dotnet test`.
- **Approval is a human act**, never automated: after the full
  pipeline below passes, a human moves the file into
  `Stories/Content/`, flips `review.status` to `"approved"`, stamps
  the review dates, and commits it as a data-only story slice.

## Pipeline (every draft, no exceptions)

1. Draft JSON written here with `review.status: "draft"`.
2. **armenian-story-master** linguistic/story review (natural Eastern
   Armenian, ages 4–7, register, TTS punctuation).
3. Automated schema + authoring-linter tests pass.
4. **TTS listen test** on the production voice; mispronounced words
   are rewritten through a fresh review — never phonetically hacked.
5. Human approval → move to `Stories/Content/` → status `approved` →
   `linguisticReviewAt` / `listenTestAt` stamped → commit.

## Content rules

- **Original material only.** Do not copy or adapt copyrighted
  stories or text scraped from the internet. No folklore characters
  or adaptations (postponed product decision — see MODES.md).
- Follow the full §1A authoring checklist in `.claude/MODES.md`
  (segment shape, Armenian-script-only TTS rules, BedtimeSafe rules,
  bounded tags/categories).
- **Therapeutic / prophylactic stories** additionally must be gentle
  normalization through story, never medical advice, never diagnosis,
  never instruction, and never emotionally manipulative — the toy is
  a storyteller, not a therapist. Parent-trust language rules apply.

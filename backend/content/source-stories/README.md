# Source stories — exact reference imports, NEVER served to children

This folder holds **exact source-story imports** (`<id>.story.json`,
schema v1 — see
`backend/src/ArmenianAiToy.Application/Stories/StoryFileSchema.cs`):
reference and research material kept verbatim for tests, linguistic
comparison, and future adaptation work.

## Hard rules

- **Never served to children.** This folder lives outside
  `backend/src` and is not part of any project; nothing here is
  compiled, embedded, or loaded by the runtime. The runtime library
  loads ONLY embedded resources from
  `backend/src/ArmenianAiToy.Application/Stories/Content/`, and its
  loader hard-fails on any file whose `review.status` is not
  `"approved"`.
- **Never embedded.** The Application csproj embeds only
  `Stories\Content\*.story.json`. `SourceStoryFolderTests` pins that
  no source-story id ever appears among embedded resources or
  runtime-served stories.
- **Not a promotion pipeline.** Unlike `../story-drafts/`, files here
  are not candidates for child-live use by default. A source story is
  never approved in place and is never moved directly into
  `Stories/Content/`. There are exactly two paths out of this folder:
  (a) the default path — a source story *inspires* a separate,
  human-authored original/adapted draft with a NEW id; or (b) the
  owner-designation path — the product owner explicitly designates
  the title as a product story, and the file MOVES to
  `../story-drafts/` as an exact-text `"draft"` (segments
  byte-frozen) to go through the normal listen-test + human-approval
  pipeline. Owner designation is a recorded product decision, never
  an editorial judgment made in this repo. (Designated so far:
  `anban-huri`, owner decision 2026-06-12 — now in story-drafts.)
- **`review.status` MUST be `"source"`.** A `"draft"` or `"approved"`
  file in this folder fails `SourceStoryFolderTests` on the next
  `dotnet test`. (`"source"` also structurally fails the runtime
  parse — the parser accepts only `"approved"` at runtime.)
- **Story text must remain exact.** No silent corrections, no typo
  fixes, no punctuation normalization, no modernization, no
  rewriting. The import is the artifact; if the source has quirks,
  the file keeps them. Any deviation must be a reviewed, documented,
  human-approved edit.
- **Copyright / public-domain / source verification status must be
  recorded in `review.notes`** for every file, as one of:
  `verificationStatus: verified | pending | blocked`.
  - `pending` files MAY be committed, but only as reference/research
    corpus — they must NOT be used for adaptation work, approval,
    TTS listen-tests, or any child-facing work until verified.
  - `blocked` files must not be used at all and should be removed.
  - Regardless of verification status, no source file can ever be
    approved directly (see "Not a promotion pipeline" above).
- **Paths toward children** (see "Not a promotion pipeline" above):
  by default, a source story may only *inspire* a separate,
  human-authored **original or adapted draft with a NEW id**, written
  in `../story-drafts/` and taken through the full normal pipeline
  (linguistic review, listen test, human promotion) — and for
  folklore material, only after the postponed folklore product
  decision is explicitly revisited (see `.claude/MODES.md`).
  Exception: an explicit **owner designation** moves the title to
  `../story-drafts/` as an exact-text draft (same id, segments
  byte-frozen) through the same listen-test + human-approval gates.
  A file move alone is never a promotion — status stays `"draft"`
  until a human approves.

## What source stories ARE for

- Negative fixtures for TTS-punctuation / authoring-linter work
  (real-world dashes, ellipses, dialect spellings).
- Register and fidelity reference when reviewing a future adapted
  draft against its source.
- Parser/segmentation robustness fixtures (verse line breaks, long
  clause chains, dialogue formatting).

The existence of this folder is **not** a product decision to ship
folklore content. The folklore postponement stands until explicitly
lifted.

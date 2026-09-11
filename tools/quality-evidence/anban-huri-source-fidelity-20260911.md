# anban-huri — source-fidelity re-check against the 2026-07-27 record — 2026-09-11

**Scope: comparison and reporting only. No story text was edited by this
task** (CLAUDE.md: the text is byte-frozen except by owner edit; this
document exists so an owner edit, if any is warranted, is an informed one).

**Provenance of this report** (same labelling convention as
`anban-huri-listen-test-20260727.md`):
- **[REPO]** — verified mechanically from the repository.
- **[HUMAN]** — a decision or fact previously supplied by the owner, quoted
  from an existing file.
- **[GAP]** — something the repository does not record; flagged, not
  invented.

## 1. Verdict

**NOT byte-identical to the last pinned source-fidelity artifact.**
Runtime text and the 2026-07-27 frozen snapshot agree on 3304 of 3306
characters. The two characters that differ are not noise: they are two
occurrences of the same two-letter substitution inside the same proper
name, and the resulting spelling is not the one the 2026-07-27 source
comparison approved. Everything else that comparison found — all 19
previously-catalogued dialect/standard mismatches — is still present,
unchanged, byte for byte.

This is **not** a claim that the current spelling is wrong. It is a claim
that the repository's own fidelity-approval artifact for this story no
longer matches what actually ships, and nothing in the repository records
why, so the gap in section 3 below is an open item, not a verdict.

## 2. Method

- Runtime text: `backend/src/ArmenianAiToy.Application/Stories/Content/anban-huri.story.json`,
  `segments[0..8]` joined by `\n\n` (UTF-8) — the exact convention
  `anban-huri-frozen-text.txt` itself documents using.
- Reference: `tools/quality-evidence/anban-huri-frozen-text.txt`, the
  2026-07-27 pinned snapshot, text extracted between its
  `BEGIN FROZEN TEXT` / `END FROZEN TEXT` markers.
- Compared with Python's `difflib.SequenceMatcher` over the full strings
  (no normalization, no whitespace collapsing — a stricter, byte-level
  pass than the 2026-07-27 report's own NFC/whitespace-collapsed method,
  since the question here is "did the pinned artifact change", not
  "how similar are two independent transcriptions").
- Independently re-hashed both the runtime text (SHA-256 over the same
  UTF-8-bytes-of-segments-joined-by-`\n\n` convention) and the current
  `story-audio/anban-huri.mp3`, and cross-checked both against every
  sha256 recorded in `tools/quality-evidence/`.

## 3. What changed

| | Value |
|---|---|
| Runtime text SHA-256 (today) | `458dd6cafbb17c2a04956e7ba7bf53abe032c8a9bf8e21af24da066b95dd31dd` |
| Frozen snapshot SHA-256 (2026-07-27, pinned) | `9b44d702f98ff1e253b9f8d74a1e93c23030db59c50eb4ee097c2fb9e8d45261` |
| Characters / bytes | 3306 / 5940 — **identical** on both sides |
| Diff ops (full-string, no normalization) | **exactly 2**, both `նի` → `ու` |

| # | File:line (runtime) | Frozen (2026-07-27) | Runtime (today) | Sentence |
|---|---|---|---|---|
| 1 | `anban-huri.story.json:31` (`segments[2]`) | …Անբան **Հուռնին** ուզում է… | …Անբան **Հուռուն** ուզում է… | "…rushes headlong wanting [to marry] Anban Huri…" |
| 2 | `anban-huri.story.json:37` (`segments[8]`) | …արգելում է **Հուռնին** ձեռն… | …արգելում է **Հուռուն** ձեռն… | "…forbids Huri from lifting a hand [to work] again…" |

Both are the same word (an inflected form of the character's name
«Հուռի») at the same two sentence positions in the source-frozen text.
The substring `նի` was replaced by `ու` in both places and nowhere else —
confirmed by the byte-length match above (3306/3306, 5940/5940): this is
a like-for-like substitution, not a rewrite, an insertion, or a deletion.

**Nothing else in the frozen tale text has moved.** All 19 material
differences the 2026-07-27 report catalogued against the cited Wikisource
edition (§4 of `anban-huri-source-verification-20260727.md` — `կին`/`կնիկ`,
`հետո`/`ետը`, `այս`/`էս`, `այն`/`էն`, `այդ`/`էդ`, `այնպես`/`էնպես`,
`այսքան`/`էսքան`, `այդքան`/`էդքան`, `այնքան`/`էնքան`, `այսպես`/`էսպես`,
`աղջի`/`աղչի`) are present today in exactly the counts that report
recorded — the diff above is complete and exhaustive for the frozen tale
text; there is no third change.

**This is a divergence from the cited source, not just from the
snapshot.** The 2026-07-27 report explicitly listed «Հուռնին» among the
forms it verified as **already matching** the cited edition and recorded
in the `armenian-story-master` memory as a deliberately preserved quirk
(§4, "Recorded as preserved" table: `Հուռնին` → "present ×2 ✅"). Today's
`Հուռուն` was never compared against that edition at all — it simply did
not exist in the text this repository has evidence for. Whether `Հուռուն`
is itself a legitimate declension of «Հուռի» found elsewhere in the
source, or a spelling that drifted from the preserved `Հուռնին`, this
repository's evidence trail cannot say (§4 below).

### Not affected: a third occurrence, out of scope by the frozen text's own definition

`anban-huri.story.json:42`, `reflectionQuestions[1]`: `«Ինչո՞ւ էին Հուռուն
անբան ասում»`. Also reads `Հուռուն`. This is `reflectionText`/
`reflectionQuestions` metadata, which both the draft's own review notes
and `anban-huri-frozen-text.txt`'s header explicitly exclude from the
frozen tale text ("toy-spoken metadata, NOT part of the original tale").
No frozen snapshot of this field exists to diff against, so whether it
changed cannot be determined either way — noted for completeness, not
counted as a finding.

## 4. Does anything in the repository explain the change?

Checked and ruled out, in order:

1. **The 2026-07-27 "option 2b" owner decision** (`review.notes`,
   `anban-huri.story.json:54`, quoted in full below) resolves the
   **19 catalogued differences** ("the current wording is accepted as
   the v1 product text; no revert, no re-render"). `Հուռնին`/`Հուռուն`
   is not one of the 19 — it was on the *preserved* list, the opposite
   category. Option 2b's text does not authorize or mention this change.
2. **The review notes' listen-test watch-list** — same `review.notes`
   field: *"production-voice TTS listen test (watch «Հուռու»,
   «զվարճալի», dialect forms like «մանեցե′ք»)"*. This looked, at first,
   like it might predate and explain the change (a genitive `Հուռու`
   is one letter short of `Հուռուն`). It does not: `«Հուռու», «զվարճալի»`
   are co-located as a **single phrase** at `anban-huri.story.json:39`
   (`reflectionText`): *"Անբան **Հուռու զվարճալի** հեքիաթը"* — a
   different word (genitive «Հուռու», no `ն`), in the metadata field
   excluded from the frozen tale text (§3 above), not the accusative
   `Հուռնին`/`Հուռուն` inside the tale itself. **[REPO]**, confirmed by
   grep — no other `Հուռու` (without `ն`) exists in the file. This watch-
   list item does not touch, predate, or explain the tale-text change.
3. **`git log --follow`** on the runtime file returns exactly one commit,
   `180af2e` ("docs: bring CLAUDE.md up to date"), which is a squashed
   history checkpoint, not the original edit — it cannot date or attribute
   the change.
4. **A second, later promotion event exists but has no evidence file of
   its own.** The runtime file's own stamped dates
   (`linguisticReviewAt`/`listenTestAt`: **2026-08-03**) do not match the
   2026-07-27 listen-test record's date. `review.notes` explains why:
   *"PROMOTED 2026-08-03 by owner decision ('promote them') after the
   owner listen-tested the batch rendered in the interim storyteller
   voice."* **[GAP]**: no file in `tools/quality-evidence/` documents
   this 2026-08-03 listen test (audio hash, verdict, or text state) the
   way the 2026-07-27 and 2026-09-08 events are documented. It is the one
   point in this story's history where a text change could plausibly have
   been made alongside a real listen test, and it is exactly the one point
   with no artifact to check it against.
5. **The 2026-09-08 cast-render ship** (`cast-library-ship-20260908.md`)
   re-rendered and re-approved the AUDIO (current `anban-huri.mp3` sha256
   `c5680e7b…`, matches disk today, matches that file's own table —
   **[REPO]**, independently re-hashed and confirmed) but is a render
   of whatever text existed on 2026-09-07/08; it documents an owner
   listen ("this is what the owner heard... Ship") but says nothing about
   a text edit, and provides no earlier/later text snapshot to compare.

**Net: no repository record attributes, dates, or authorizes this
specific change.** It most plausibly happened during the undocumented
2026-08-03 event (§4.4), but that is a plausibility, not a finding this
report can stand behind.

## 5. Linguistic characterization of the substitution

Consulted the `armenian-story-master` agent for a classification only (no
recommendation, matching this report's own scope). Its finding, verbatim:

> «Հուռուն» ↔ «Հուռնին» — type: **dialect form** (same case:
> dative-definite of «Հուռի», serving as the animate definite object).
> Not a case change; not a transcription slip.

This means the finding in §3 is not an isolated oddity: by the 2026-07-27
report's own classification scheme, `Հուռնին` → `Հուռուն` is a **20th and
21st material difference of the exact same systematic type** the original
report catalogued and asked the owner to decide on — it was simply never
entered into that table, because it happened to (or after) the text the
table was built from.

## 6. Audio — not itself a fidelity problem, but re-stated for completeness

The audio the toy currently plays (`story-audio/anban-huri.mp3`, sha256
`c5680e7b10bbc92938d07ff031217d1368ba36b24ad8347754b5e557ba81c555`) is
the cast-narrator render from `cast-library-ship-20260908.md`, which the
owner heard and approved directly ("Ship", 2026-09-08) — this is a real,
recorded, human approval, just not run through the
`anban-huri-listen-test-TEMPLATE.md` checklist process the 2026-07-27
record used. It **postdates** and **supersedes** the 2026-07-27
listen-test's cited audio (`anban-huri.mp3` sha256 prefix `d3a6fbdb…`,
Nova voice) — that record's own header already says a future re-render
must supersede it, and this one has. No open question here; listed only
so this report's audio identity matches what `CLAUDE.md` § Product
constraints requires ("approvals are pinned to sha256 … any byte change
invalidates the approval") — the AUDIO approval is current and intact,
independent of the TEXT question above.

## 7. What is stale as a direct result of §3

- `tools/quality-evidence/anban-huri-frozen-text.txt` — its pinned
  SHA-256 (`9b44d702…`) no longer matches the runtime text it is meant to
  anchor. It is evidence of a state that no longer ships.
- `tools/quality-evidence/anban-huri-source-verification-20260727.md` §4's
  "Recorded as preserved" table claims `Հուռնին` is present in the frozen
  text — no longer true.
- `anban-huri.story.json`'s own `review.notes` field still asserts, in the
  present tense, that the "19 MATERIAL differences" figure and the
  preserved-forms list are the complete account of how this text diverges
  from its cited source — §5 above shows that count is now incomplete by
  two more instances of the same difference class.

## 8. Recommendation (reporting only — not actioned by this task)

1. Confirm with the owner whether `Հուռուն` was a deliberate correction
   (and if so, when, and whether it was meant to also revert `Հուռնին`
   to standard form the same way the other 19 dialect forms could be) —
   or whether it should revert to `Հուռնին` to match both the cited
   source and the repo's own preserved-quirks list.
2. Whichever way that lands, regenerate
   `anban-huri-frozen-text.txt` (new SHA-256, new per-segment hashes) so
   it once again anchors what actually ships, and add a line to
   `anban-huri-source-verification-20260727.md` §4 recording this as a
   20th/21st entry rather than leaving the two documents disagreeing.
3. No audio action needed (§6) — this is a text/evidence-artifact gap
   only, not a listen-test gap.

**No text was changed to produce this report.**

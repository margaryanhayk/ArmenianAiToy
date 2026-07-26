# anban-huri — source / edition / orthography verification — 2026-07-27

## 1. Status

**PARTIAL.**

A specific, scan-backed authoritative edition was identified, and the
frozen text matches it at **96.29%** word similarity. It is *not* PASS:
there are **19 material differences**, all one systematic class, and they
contradict the draft's own claim that the text is unmodernized. Resolving
them is an owner decision, not an editorial one.

## 2. Frozen-text identity

| Field | Value |
|---|---|
| Source draft | `backend/content/story-drafts/anban-huri.story.json` |
| Draft commit | `ce9318b0e9bf3b53820b28b06fafdb20903e2140` |
| Segments | 9 (order preserved) |
| SHA-256 (UTF-8, segments joined by `\n\n`) | `9b44d702f98ff1e253b9f8d74a1e93c23030db59c50eb4ee097c2fb9e8d45261` |
| Characters / UTF-8 bytes | 3306 / 5940 |
| Extracted artifact | `tools/quality-evidence/anban-huri-frozen-text.txt` |

**No frozen text was changed by this task.** The draft file hashes
identically to its committed state; only `review.notes` was touched (see
§7). Reflection metadata is excluded from the frozen tale text — the
draft notes and the `armenian-story-master` memory both record
`reflectionText` / `reflectionQuestions` as toy-spoken metadata, not part
of the original tale.

## 3. Source edition

**Repository-local evidence: none.** `backend/content/source-stories/`
contains only a `README.md`; the anban-huri source file was never
committed (the `armenian-story-master` memory records it as
"status 'source', **uncommitted**"), and `git log --all` finds no
history for it. There is no prior provenance record anywhere in the repo.

**External source identified:**

| Field | Value |
|---|---|
| Work | Հովհաննես Թումանյան, «Անբան Հուռին» |
| Edition | **Երկերի լիակատար ժողովածու** (Complete Collected Works), **հատոր 5** |
| Text location | printed pp. **226–228** (djvu scan pages 232–234) |
| Editorial commentary | printed p. 783 (djvu 789) |
| Carrier | `Թումանյանի ԵԼԺ հ5.djvu` on Armenian Wikisource (Վիքիդարան) |
| Access | `https://hy.wikisource.org/wiki/Անբան_Հուռին` — transcludes `<pages index="Թումանյանի_ԵԼԺ_հ5.djvu" from=232 to=234/>` |
| Authority level | **2–3** — a digital edition that identifies publication/editorial provenance AND is backed by page scans, proofread page-by-page via the ProofreadPage extension |

**Why this is above a generic web transcription:** it is not an anonymous
repost. It names a specific scholarly edition and volume, links each
printed page to a scanned image, and the text is transcluded from those
proofread page objects rather than pasted.

**Why it is still not the top of the hierarchy:** Wikisource is
user-edited. The link from transcription to scan is strong but the
transcription itself is crowd-produced, and no one in this repo has
compared it against the physical volume. Level 1 (owner-supplied scan or
the physical ԵԼԺ h.5) has not been obtained.

## 4. Difference matrix

Method: both texts NFC-normalized, whitespace collapsed, wiki markup
stripped with template *content* preserved, then word-level diff.
20 differing blocks; 1 is non-material, 19 are material.

| # | Location | Frozen text | Source text | Difference type | Material? | Recommendation |
|---|---|---|---|---|---|---|
| 1 | start of tale | *(absent)* | `ԱՆԲԱՆ ՀՈՒՌԻՆ` | addition (title heading) | **No** | None — the title is a separate `title` field in the draft; segments correctly omit the printed heading |
| 2 | ×4 | `կին` | `կնիկ` | word form | **Yes** | Owner decision |
| 3 | ×2 | `հետո` | `ետը` | dialect form | **Yes** | Owner decision |
| 4 | ×3 | `այս` | `էս` | dialect form | **Yes** | Owner decision |
| 5 | ×2 | `այն` | `էն` | dialect form | **Yes** | Owner decision |
| 6 | ×3 | `այդ` | `էդ` | dialect form | **Yes** | Owner decision |
| 7 | ×2 | `այնպես` | `էնպես` | dialect form | **Yes** | Owner decision |
| 8 | ×1 | `այսքան` | `էսքան` | dialect form | **Yes** | Owner decision |
| 9 | ×1 | `այդքան` | `էդքան` | dialect form | **Yes** | Owner decision |
| 10 | ×1 | `այնքան` | `էնքան` | dialect form | **Yes** | Owner decision |
| 11 | ×1 | `այսպես` | `էսպես` | dialect form | **Yes** | Owner decision |
| 12 | ×1 | `աղջի` | `աղչի` | orthography | **Yes** | Owner decision — and see the contradiction below |

**One pattern, not twelve problems.** Every material difference is the
same operation: at those 19 positions the frozen text uses standard
Eastern Armenian where the edition has Tumanyan's dialect.

### The decisive detail: the frozen text contradicts *itself*

The normalization was **not applied consistently**. The same words appear
in BOTH forms in different places (substring counts, case-insensitive,
over the 9 segments):

| Edition form | Occurrences in frozen text | Standard form | Occurrences |
|---|---|---|---|
| `էս` | **5** | `այս` | **5** |
| `կնիկ` | **1** | `կին` | 4 |
| `ետը` | **2** | `հետո` | 2 |
| `աղչի` | **1** | `աղջի` | 4 |
| `էն` | 0 | `այն` | 5 |
| `էդ` | 0 | `այդ` | 4 |
| `էնպես` | 0 | `այնպես` | 2 |
| `էսքան` | 0 | `այսքան` | 1 |
| `էնքան` | 0 | `այնքան` | 1 |
| `էսպես` | 0 | `այսպես` | 1 |

So four words survive in both variants inside one tale, while six were
standardized completely. **No printed edition contradicts itself this
way.** This is the signature of an incomplete find-and-replace pass over
a digital transcription, not of a different published edition.

### The contradiction this exposes

The draft's own `review.notes` state the segments are "byte-frozen: no
rewriting, **no modernization**, no dialect/scene/dialogue removal". The
evidence says a partial dialect normalization was applied relative to the
cited edition.

The repo's own preserve-list is also only partly honoured. The
`armenian-story-master` memory names «գալի», «տալի», «ասիլ», **«աղչի»**,
«Հուռնին» as quirks to keep:

| Recorded as preserved | Actually in frozen text |
|---|---|
| `գալի` | present ×6 ✅ |
| `տալի` | present ×6 ✅ |
| `ասիլ` | present ×2 ✅ |
| `Հուռնին` | present ×2 ✅ |
| `կոկռում` / `կռկոում` | both present ✅ |
| **`աղչի`** | present ×1, but `աղջի` ×4 alongside it ⚠️ |

### Typography fingerprint — provenance signal

| Codepoint | Frozen | Edition transcription |
|---|---|---|
| U+2032 PRIME (in `մանեցե′ք`) | 1 | 1 |
| U+055B ARMENIAN EMPHASIS | 19 | 19 |
| U+2024 ONE DOT LEADER | 40 | 39 |
| U+0589 ARMENIAN FULL STOP | 48 | 46 |

A stray U+2032 PRIME where U+055B is used 19 times elsewhere is a
transcription artifact, not a feature of a printed page — and it appears
**exactly once in both texts**. Together with the identical U+055B count,
this indicates the frozen text descends from this same transcription
lineage and then had dialect forms partially normalized. It is not an
independent transcription from the book.

## 5. Orthography verdict

**Contains probable transcription/editing errors** — and therefore also
requires owner acceptance.

Not an exact match and not merely typographic: the differences are
lexical/dialectal. The initial reading was "edition variant", but the
internal inconsistency above rules that out — a published edition does
not print `էս` five times and `այս` five times for the same word, or
carry both `աղչի` and `աղջի`. The most defensible reading is an
incomplete normalization applied to a digital transcription, which makes
these editing artifacts rather than an alternative edition.

That distinction matters for the fix: if they were edition variants the
owner could simply accept them, but as artifacts the coherent options are
to finish the normalization deliberately or revert to the edition.

## 6. Public-domain / provenance note

**Not a legal opinion.**

- **Attributed author:** Հովհաննես Թումանյան.
- **Death year:** 1923, per the draft's own note. *(Not independently
  confirmed from the cited edition in this task.)*
- **Underlying work:** appears **likely** to be public domain in Armenia
  and in likely product jurisdictions on a life+70 basis, but this should
  be confirmed against the jurisdictions the product will actually ship
  in.
- **Edition-specific rights:** the identified carrier is a *scholarly
  edition* (ԵԼԺ h.5). Collected-works editions can carry separate rights
  in editorial apparatus, annotations, orthographic normalization and
  typography, independent of the underlying tale. The repo uses only the
  tale text, not the p. 783 commentary — which reduces but does not
  eliminate the question.
- **Transcription rights:** the Wikisource transcription is
  community-produced and carries its own licence terms, which were not
  examined here.
- **Uncertainty:** the underlying work appears likely to be public
  domain, but the selected edition and the transcription provenance
  should still be documented before shipping. **This is not a
  "copyright cleared" finding.**

## 7. Remaining owner decisions

1. **Choose one of three, since the text is currently self-inconsistent:**
   (a) revert the 19 positions to the edition's dialect forms — the only
   option consistent with the draft's stated "byte-frozen, no
   modernization" rule; (b) deliberately finish the normalization so the
   text is at least internally consistent, and correct the draft's
   "no modernization" wording, which would then be factually wrong;
   (c) accept the text exactly as-is, inconsistencies included, and
   record that as a deliberate decision. Leaving it undecided is the one
   option that should not persist — the file currently claims a property
   it does not have.
2. **If (b): the listen test must be re-run.** The 2026-07-27 PASS was
   recorded against the *current* text. Changing eleven words changes what
   the child hears, and «էս/էն/էդ» differ audibly from «այս/այն/այդ».
3. **Resolve `աղջի` vs `աղչի`** — the repo's own record says `աղչի` was
   preserved; it was not.
4. **Decide whether Wikisource-level provenance is sufficient**, or
   whether the physical/scanned ԵԼԺ h.5 pp. 226–228 must be checked
   directly.
5. **Confirm the public-domain basis** for the shipping jurisdictions.

None of these were decided by this task.

## 8. Promotion recommendation

**Not ready — owner must accept edition differences.**

The audio gate is closed (listen test PASS, 2026-07-27). The source gate
is not: a specific edition is now identified, but the frozen text differs
from it in 19 material places that no recorded decision authorized.
Promotion should wait on decision 1 above, and on decision 2 if the text
changes.

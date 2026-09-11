# Variant endings + Tsivik serial — preparation evidence (2026-09-11)

Prepares (does not render) audio inputs for the two content sets that had
plumbing but no sound: the 10 story variant endings
(`backend/content/variant-endings/`) and the 6-episode Tsivik serial
(`backend/content/serial-hero/`). `ELEVENLABS_API_KEY` was not set in this
session (checked with `test -n "$ELEVENLABS_API_KEY"`, unset) — everything
below is preparation and validation, never a paid render. The human listen
test remains open for both sets, and always will be until real audio ships.

## What was verified before building anything

Read `story_select.cpp`, `ContentSyncStoryOptions.AltOf`'s doc comment,
and the variant-endings/serial-hero content notes (not guessed): a
variant ending plays as the WHOLE narration file for a re-listen session
(`story_select_resolve_playback_path`), never a short spliced tail, and
reflection dialogue always resolves by the BASE story id
(`active_story_id` never changes to the alt id). Cross-checked all 10
`afterLine` anchors against their base story's actual last segment —
every one is an exact suffix of it, confirming the alt file is: base
narration unchanged + the approved ending appended as one new final
segment. Documented in full in
`docs/variant-endings-serial-render-runbook.md`.

## What was built

**(a) 10 variant-ending speaker maps**
(`backend/content/story-voices/<id>-alt.voices.json`): each copies its
base story's existing segments/spans VERBATIM and appends one new final
segment for `endingText`, dialogue-split by hand against the owner-
approved ending text (attribution verbs read directly off the text, e.g.
«ասաց Ծիվիկը» / «ասաց տատիկը»), reusing the base story's exact cast
(voiceId/model/settings unchanged) except `sutlik-orskan-alt`, which
introduces one new speaker («mother» — she never speaks in the canonical
narration) flagged "CASTING OPEN FOR OWNER EAR". No new `*.story.json`
was created for the alts (see the runbook for why one is not needed).

**(b) 6 Tsivik episode drafts**
(`backend/content/story-drafts/tsivik-{one..six}.story.json` +
matching `backend/content/story-voices/tsivik-*.voices.json`): episode
BODY text (`segments[]`) is byte-identical to the owner-approved
`tsivik-series.json` («TEXT APPROVED by the owner, 2026-08-13») —
re-segmented into 2-5 scene beats per episode for TTS/ambience, verified
programmatically to reconstruct the source text exactly (only inter-
segment whitespace, replaced by the render's own segment gap, was
trimmed — no word touched). `goal`/`lesson`/`reflectionText`/
`reflectionQuestions`/`reflectionConclusions` are NEW text authored for
this slice — NOT yet reviewed by armenian-story-master or the owner,
flagged in every file's `review.notes`. Cast: narrator stays
areg-storyteller; Ծիվիկ reuses Ուլիկ's exact vardan-v2 settings (the
standing "young animal hero" voice); Տատիկ reuses the standing
"old woman" pitch (0.92, katrin-v3). The four new supporting characters
(stork, crow, Պուտիկ, Ալիկ) are a first-pass assignment by pitch/register
only, each flagged "CASTING OPEN FOR OWNER EAR" in its own `note` field —
there is no prior character in the standing cast close enough in tone to
borrow from directly.

**Validation, all green:**

```
$ python3 tools/story-voices/check_speaker_map.py
PASS — every span set reconstructs its story text exactly   (26 maps: 10 base + 10 alt + 6 tsivik)

$ python3 tools/story-audio/check_ambience_anchors.py
checked 34 cues across 12 stories against 21 sound ids
every cue lands in the segment it names, quoting a line that is really there

$ python3 tools/story-audio/check_story_audio.py
PASS - 10 stories are complete and cleanly encoded.   (unaffected by this PR — sanity check only)

$ cd backend && dotnet build && dotnet test
Build succeeded. 0 Error(s).
Passed! - Failed: 0, Passed: 2929, Skipped: 0, Total: 2929
```

`check_speaker_map.py` was extended (not just run): it previously hard-
required a `<storyId>.story.json` under `Stories/Content/` for every map,
which neither an alt ending (no story.json at all, by design) nor a draft
episode (lives in `story-drafts/`, never embedded) has. It now falls back
to `story-drafts/` for a draft-status story, and validates an `"altOf"`
map against `[base story segments] + [endingText from
variant-endings.json]` instead. `check_ambience_anchors.py` got the same
`story-drafts/` fallback for the 4 tsivik cues.

**Placeholder ContentSync rows**: 16 new `ContentSync:Stories` entries in
`appsettings.json` (10 `-alt` + 6 `tsivik-*`), each `SizeBytes: 0` / a
64-zero `Sha256` — the established shape that
`ContentManifestService.Build` drops before it ever reaches AltOf/series
resolution (`story.SizeBytes <= 0` is the very first per-item check), so
every one is invisible to every toy today. Pinned by the EXISTING
`ShippedConfig_AdvertisesExactlyTheStoriesThatHaveAudio` test (generic —
needed no changes to cover the new rows) plus a new, narrower
`ShippedConfig_AltOfAndSeriesFields_AreInternallyConsistent` test guarding
against a typo'd `AltOf` or a duplicate/missing `SeriesIndex` specifically
(the kind of mistake that only surfaces once real bytes ship and the
placeholder-drop stops covering for it).

**Tests added**: `TsivikSeriesDraftTests.cs` (new — pins the six drafts'
series-specific shape: all present, all `status: draft`, 3
questions/conclusions paired, no author, not bedtime-safe, never
embedded — on top of what `StoryDraftFolderTests` already sweeps
generically) and `ShippedConfig_AltOfAndSeriesFields_AreInternallyConsistent`
(new, in `ContentManifestServiceTests.cs`). `CuratedStoryLibraryTests` was
NOT extended — the six episodes are not embedded yet (see the runbook's
"Why the episodes are not promoted" section), so there is nothing there
for that byte-pin suite to cover until promotion.

## Known gaps, surfaced not hidden

- **Ellipsis character (`…`, U+2026)** appears in several owner-approved
  episode bodies (pre-existing in `tsivik-series.json`, not introduced
  here) and is outside `CuratedStoryAuthoringRulesTests`' TTS-safe
  whitelist — invisible today (that linter only runs against promoted
  stories) but will fail at promotion. Needs an owner decision (whitelist
  it, precedent `′` in anban-huri, or normalise to `․․․`) before step 2
  of the runbook.
- **Series-refrain/intro clips** (`tsivik-series.json`'s `series-intro`/
  `series-refrain`/`series-closing`) don't map onto any per-story clip
  kind the backend/firmware actually recognise. Documented as an open
  design question in the runbook with two options, neither implemented.
- **`serialnext` clip render** has no dedicated tool call — the runbook
  documents a manual single-render-then-copy path (render once, splice
  into all six episodes' clip slots, matching the existing refrain/
  closing-clip idiom).
- **Ambience** for `tsivik-five`/`tsivik-six` and all 10 alt endings is
  deliberately not authored in this slice (see the runbook).
- **The human listen test** — the gate that actually matters — has not
  happened for anything in this PR, because nothing has been rendered.

## What was not attempted

Any paid render, any use of the pasted-in-chat pattern this repo has
explicitly burned before — `ELEVENLABS_API_KEY` was read from the
environment only, confirmed unset, and no render command was run.

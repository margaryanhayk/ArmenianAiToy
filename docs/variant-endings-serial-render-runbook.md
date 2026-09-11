# Variant endings + Tsivik serial — render runbook

Owner batch items 5 (variant endings) and 7 (the Tsivik serial), prepared
2026-09-11. This is the exact, ordered sequence to go from what is in this
PR (drafts, speaker maps, placeholder `ContentSync` rows) to shipped audio.
No audio was rendered while preparing this PR — `ELEVENLABS_API_KEY` was
not set in that session — so every step below is unexercised past its
dry-run/validation stage. Run the whole sequence for real once the key is
available; nothing here should be guessed at on the day it matters.

## What each feature needs, verified against the firmware (not guessed)

**(a) Variant endings.** `story_select_resolve_playback_path()`
(`esp32/AregVoiceMvp/story_select.cpp`) plays the ALT file for the WHOLE
session on a re-listen (toggle on, story already heard) — it is not a
short "alternate tail" spliced in on-device. Every one of the 10
`afterLine` anchors in `backend/content/variant-endings/variant-endings.json`
was verified to be an exact suffix of its base story's LAST segment (no
text follows it in that segment) — so the alt audio is: **the base
story's existing narration, unchanged, end to end, with the approved
`endingText` appended as one new final segment.** This matches
`ContentSyncStoryOptions.AltOf`'s own doc comment exactly.

Reflection dialogue (summary/question clips) is resolved by the BASE
story id regardless of which file played — `handle_post_story_flow()`
tracks `active_story_id` as the base id throughout — so **an alt entry
needs no `*.story.json` of its own** and no per-story clips of its own;
it reuses the base story's. It is also never a rotation member and is
filtered out of the parent story library, by design.

**(b) The Tsivik serial.** `apply_series_rule()` picks the lowest-unheard
episode of a series per boot; `serialnext` is a real per-story clip kind
(`content_sync_rules.h`, `CS_CLIP_KIND_SERIALNEXT`) played after a serial
episode's natural end, before the ordinary post-story flow. Each episode
IS an ordinary standalone story for every other purpose (its own id,
narration, clips, reflection Q&A) with `SeriesId`/`SeriesIndex`/
`SeriesTitle` layered on top.

## Where things live after this PR

- `backend/content/story-drafts/tsivik-{one..six}.story.json` — the six
  episodes, schema v1, `review.status: "draft"`. **Deliberately NOT in
  `Application/Stories/Content/`** — see "Why the episodes are not
  promoted yet" below.
- `backend/content/story-voices/tsivik-{one..six}.voices.json` — cast +
  segment/span maps for `render_story.py`, `check_speaker_map.py` PASS.
- `backend/content/story-voices/<baseId>-alt.voices.json` (×10) — the
  base story's segments copied verbatim + one new final segment for the
  ending. `check_speaker_map.py` was extended to validate these against
  `[base story segments] + [endingText]` instead of a `*.story.json`
  (alt entries have none — see above).
- `backend/content/story-ambience/ambience-cues.json` — sparse cues for
  `tsivik-one`..`tsivik-four` (existing sound ids only). `tsivik-five`/
  `tsivik-six` are listed in `_notCovered`: a lake/sea sound is not yet
  designed. No new ambience for the alt endings (out of scope for this
  slice — none of the 10 endings introduces a new setting the base
  story's own cues do not already cover, and the ending's default is a
  dry coda, matching the tone of a "graft, not a new scene").
- `backend/src/ArmenianAiToy.Api/appsettings.json` — 16 new
  `ContentSync:Stories` rows appended at the end of the array (10 `-alt`
  rows with `AltOf`, 6 `tsivik-*` rows with `SeriesId`/`SeriesIndex`/
  `SeriesTitle`), each `SizeBytes: 0` / a 64-zero `Sha256` — the
  established placeholder shape (`ContentManifestService` drops any item
  with `SizeBytes <= 0` before it ever reaches `AltOf`/series
  resolution, so these are invisible to every toy today; pinned by the
  existing `ShippedConfig_AdvertisesExactlyTheStoriesThatHaveAudio` test
  plus a new `ShippedConfig_AltOfAndSeriesFields_AreInternallyConsistent`
  guard against a typo'd `AltOf` or a duplicate/missing `SeriesIndex`).

## Why the episodes are not promoted (embedded) yet

`EmbeddedCuratedStoryLibrary` (the online voice-chat story library) is
constructed EAGERLY from every `*.story.json` under
`Stories/Content/` and hard-throws at construction if ANY of them is not
`review.status: "approved"` with a non-blank `listenTestAt` — this is not
scoped to the file being touched; a single non-approved file there takes
down the whole app (`StoryFileParser.Parse(..., requireApproved: true)`,
called unconditionally). No listen test has happened, so marking the
episodes "approved" would be a false claim the parser itself would reject
once `listenTestAt` is filled honestly with nothing behind it, and leaving
them "draft" in `Stories/Content/` would crash `dotnet run`/`dotnet test`
for the entire product.

This is exactly the situation `backend/content/story-drafts/` and
`ConfigurableCuratedStoryLibrary`'s `requireApproved:false` side-load
exist for (see that folder's own README) — the standing pipeline is
"draft here → review → listen test → **a human moves the file** into
`Stories/Content/`, flips `status`, stamps the dates". The six episodes
follow that pipeline exactly; they are simply not past the "listen test"
step yet. `StoryDraftFolderTests` already sweeps this folder generically
on every `dotnet test`; `TsivikSeriesDraftTests` (new, this PR) pins the
series-specific shape (six episodes, 3-question/3-conclusion reflection
packs, no author, not bedtime-safe, never embedded).

## Command sequence

Run from the repo root. `ELEVENLABS_API_KEY` and `ELEVENLABS_VOICE_ID`
must be set in the environment (never in a file). Every tool below is
dry-run/no-op safe to re-run; none of them touches git.

### 0. Validate inputs (no key needed, no network — already run in this PR)

```bash
python3 tools/story-voices/check_speaker_map.py       # PASS, 26 maps (incl. the 16 new)
python3 tools/story-audio/check_ambience_anchors.py    # PASS, 34 cues
python3 tools/story-audio/check_story_audio.py         # PASS, unaffected — sanity only
cd backend && dotnet build && dotnet test              # green, 2929 tests
```

### 1. Text review (blocking everything after it)

- **armenian-story-master** review of the NEW text this PR authored: each
  episode's `goal`/`lesson`/`reflectionText`/`reflectionQuestions`/
  `reflectionConclusions` (the episode BODY text is untouched, already
  owner-approved). List every change in the PR/commit that applies the
  review's fixes.
- Owner sign-off on the reviewed text (the episode bodies and the ending
  texts are already owner-approved from 2026-08-13; only the new
  reflection/goal/lesson metadata needs a fresh pass).
- **Known pre-existing gap, surfaced while preparing this PR, not
  introduced by it**: several tsivik-series.json episode bodies contain
  the ellipsis character `…` (U+2026), which is outside
  `CuratedStoryAuthoringRulesTests`' TTS-safe whitelist (that test does
  not run against drafts, only promoted stories — so nothing fails
  today, but it WILL fail at promotion). Resolve before promotion: either
  the owner adds `…` to the whitelist for this story (the precedent is
  `′` in anban-huri — a documented, deliberate exception, never a silent
  normalisation) or the ellipses are normalised to `․․․` (the convention
  the rest of the library already uses). Do not silently pick one — the
  episode text is owner-approved and any change needs the same posture as
  any other edit to approved text.
- **Open casting decision**: `putik`/`stork`/`crow`/`alik`'s voice
  assignments (all first-pass, documented per-speaker in each
  `tsivik-*.voices.json`'s `note` field as "CASTING OPEN FOR OWNER EAR")
  reuse the standing cast (katrin-v3 / areg-storyteller-shifted) by
  pitch/register only — there is no prior character this close in tone
  to borrow from directly. Confirm on the sample-first listen (below)
  before batching the rest.
- **Open design question, not resolved in this PR**: `tsivik-series.json`
  describes `series-intro`/`series-refrain`/`series-closing` clips, but
  the only per-story clip kinds the backend/firmware actually recognise
  are `intro|question|question1|question2|summary|offer|reoffer|
  serialnext` (`ContentSyncClipOptions.AllowedKinds`,
  `content_sync_rules.h`) — there is no dedicated "series refrain" slot.
  Two ways to close this, neither implemented here: (a) fold the refrain
  into each episode's own `intro` clip text (needs a small
  `tools/ElevenLabsRender` extension — a per-series intro template,
  the same shape as the existing `{Title}` offer/reoffer template) or
  (b) ship without the refrain for now and revisit after the owner hears
  the plain `intro` clips. Recommended: (b) — zero additional code risk
  for this slice.

### 2. Promote the six episodes (after step 1 passes)

For each of `tsivik-{one,two,three,four,five,six}`:

```bash
git mv backend/content/story-drafts/tsivik-<id>.story.json \
       backend/src/ArmenianAiToy.Application/Stories/Content/tsivik-<id>.story.json
```

Then hand-edit each moved file: `"review": { "status": "approved",
"linguisticReviewAt": "<date>", "listenTestAt": "<date>", "notes": "…" }`
— both dates real, both after the step-1 review and the step-4 listen
test actually happened. `dotnet build && dotnet test` must stay green
(the promoted set is now covered by `CuratedStoryLibraryTests` and
`CuratedStoryAuthoringRulesTests`, not `TsivikSeriesDraftTests`, which
should be deleted or updated to assert the files are GONE from
`story-drafts/` once every episode is promoted).

### 3. Render narration (per episode, per alt ending)

```bash
for id in tsivik-one tsivik-two tsivik-three tsivik-four tsivik-five tsivik-six; do
  python3 tools/story-voices/render_story.py "$id" "render-out/$id"
done
for id in little-cloud hedgehog-apple khosogh-dzuk pochat-aghves princess-and-pea \
          sutasan sutlik-orskan three-piglets ulik anban-huri; do
  python3 tools/story-voices/render_story.py "$id-alt" "render-out/$id-alt"
done
```

`render_story.py` has NO local dry-run — it requires the API key
unconditionally and was not exercised this session. Its own guards
(transcript WER, pitch, tail-chop, span-length vs `chars/15`) are the
real validation and run automatically on every render. Re-run with
`RENDER_ONLY="speaker,seg:span"` to fix one bad take without re-rolling
approved ones.

### 4. Sample-first listen test (per `tsivik-series.json`'s own `_renderNote`)

Render and listen, IN ORDER, before batching the rest: (1) the refrain
line, (2) `tsivik-one`, (3) the closing line. «Ծիվի՛կ» opens the refrain
and is the series' most-heard word — one bad pattern poisons all six.

### 5. Align, mix ambience, ship

```bash
for id in tsivik-one tsivik-two tsivik-three tsivik-four; do
  python3 tools/story-voices/align_spans.py "$id" "render-out/$id"
  python3 tools/story-audio/mix_ambience.py "$id" "render-out/$id"   # dry-run by default
done
# tsivik-five / tsivik-six: no ambience cues yet (see "Open design question" above) —
# align only, or design+add cues to ambience-cues.json first and re-run check_ambience_anchors.py.
for id in tsivik-one tsivik-two tsivik-three tsivik-four tsivik-five tsivik-six \
          little-cloud-alt hedgehog-apple-alt khosogh-dzuk-alt pochat-aghves-alt \
          princess-and-pea-alt sutasan-alt sutlik-orskan-alt three-piglets-alt \
          ulik-alt anban-huri-alt; do
  # Ship-StoryAudio.ps1 -In render-out/$id -Fix -Apply   (PowerShell)
  # OR the manual equivalent used on this host before (loudnorm -16.4 LUFS,
  # 192 kbps, single ID3, then patch ContentSync Sha256/SizeBytes/Version by hand)
  echo "ship $id"
done
python3 tools/story-audio/segments_to_bytes.py   # byte map, AFTER the ship re-encode
python3 tools/story-audio/check_story_audio.py   # must show 26/26 (10 base + 10 alt + 6 episodes)
```

### 6. Render clips

Per-story clips (`intro`/`question`/`question1`/`question2`/`summary`/
`offer`/`reoffer`) need the story EMBEDDED (`EmbeddedCuratedStoryLibrary`)
— for the 6 tsivik episodes this is only possible AFTER step 2
(promotion). Alt endings need none of their own (see above).

```bash
for id in tsivik-one tsivik-two tsivik-three tsivik-four tsivik-five tsivik-six; do
  dotnet run --project tools/ElevenLabsRender -- --story "$id" --clips
done
```

`serialnext` is not in that tool's per-story clip job list (fixed text,
not derived from story metadata). Render the line ONCE — «Այսօրվա
հեքիաթը այսքանն էր։ Շարունակությունը՝ վաղը։», narrator voice, same
settings as every other clip — via any single-shot render path (e.g. add
a throwaway entry to `backend/content/voice-clips/voice-clips.json` and
render with `--voice-clips`, or a one-off script using the same raw HTTP
call `render_story.py` makes), then COPY the resulting MP3 byte-for-byte
into all six `story-audio/clips/tsivik-<id>/serialnext.mp3` paths (same
"render once, splice" idiom the refrain/closing clips already use).

```bash
python3 tools/story-audio/apply_story_clips.py --apply
```

### 7. Final gates

```bash
python3 tools/story-audio/check_story_audio.py
cd backend && dotnet build && dotnet test
```

### 8. The human listen test — always

No tool can hear a seam, a mispronounced name, or a wrong voice.
Nothing ships to a child without someone listening end to end, on the
final shipped files. Pin the approval to sha256 in
`tools/quality-evidence/`, same as every other story render.

## Deliberately NOT done in this PR

- No audio rendered (`ELEVENLABS_API_KEY` unset).
- No promotion of the six episodes (blocked on the listen test, which is
  blocked on the render).
- No ambience for `tsivik-five`/`tsivik-six` or the 10 alt endings.
- No `tools/ElevenLabsRender` extension for a series-refrain template or
  a `serialnext` job — documented as an open question above, not
  implemented, pending an owner decision on which shape to ship.

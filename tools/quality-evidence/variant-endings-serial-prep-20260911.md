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

## Render update (2026-09-11, later session — `ELEVENLABS_API_KEY` set)

Executed part 1 of `docs/variant-endings-serial-render-runbook.md`: the 10
variant endings only. The 6 Tsivik episodes were deliberately NOT rendered
or promoted — see "Tsivik serial: not rendered this session" below.

**Commands run, per story** (`$id` = the 10 alt ids):

```bash
ELEVENLABS_VOICE_ID=NxAsEwnikgCJa5tyBwEf python3 tools/story-voices/render_story.py "$id" "render-out/$id"
# loudnorm -16.4 LUFS, 192kbps mono, single ID3 (manual ffmpeg equivalent of
# Ship-StoryAudio.ps1's Repair-And-Level — PowerShell is not on this host)
python3 tools/story-audio/segments_to_bytes.py --seconds render-out/$id/$id.segments.json \
  --mp3 backend/src/ArmenianAiToy.Api/story-audio/$id.mp3 \
  --out backend/src/ArmenianAiToy.Api/story-audio/$id.segments.json
python3 tools/story-audio/check_story_audio.py --audio-dir backend/src/ArmenianAiToy.Api/story-audio
cd backend && dotnet build && dotnet test
```

**Two real tooling bugs found and fixed while exercising this pipeline
for the first time with a paid key** (both in this PR, see commit
1a356f4):

- `render_story.py`'s `assemble()`/`stitch()` wrote the ffmpeg concat
  list with paths relative to the process cwd, but the concat demuxer
  resolves a relative `file` entry against the LIST FILE's own directory,
  not the cwd — every segment/story concat was silently failing (the
  subprocess return code was never checked), so the finished `<id>.mp3`
  was never actually produced even though every span rendered and was
  paid for. Fixed by writing absolute paths and checking the ffmpeg
  return code. **Cost note**: `little-cloud-alt` was re-rendered once
  while diagnosing this — the fix was verified by re-running the whole
  script before RENDER_ONLY-scoped resume was understood to not apply
  without it, so every span of that one story was rendered twice. No
  other story was affected; every span check for the remaining 9 stories
  and thereafter is idempotent (resumed from the kept `.wav` files where
  a re-run was needed).
- `check_story_audio.py` had no way to compute an alt ending's expected
  length (`alt endings have no <id>-alt.story.json` by design) and would
  have flagged every one of the 10 as "no story text to check the length
  against", failing the whole gate the moment any alt shipped. Extended
  with `alt_expected_chars()`: resolves an alt id's expected length from
  its base story's `.story.json` segments plus
  `variant-endings.json`'s `endingText` for that base id.

**Spans that failed a guard and were retried** (per the runbook's
RENDER_ONLY guidance, each within the "at most 3 retries" budget — all
resolved on the first retry, none needed a second or third):

- `hedgehog-apple-alt` seg 3 span 3 — tail-chop («— Հիմա կսպասենք,» cut
  short twice by the model itself, inside the script's own 2-attempt
  budget) — `RENDER_ONLY="3:3"`, fixed.
- `khosogh-dzuk-alt` seg 9 span 1 — transcript mismatch on «- Գնա՛,
  ձկնիկ ջան,» — `RENDER_ONLY="9:1"`, fixed.
- `sutlik-orskan-alt` seg 3 span 0 — transcript mismatch on dialect-heavy
  tall-tale text («Հադին շալակեց, չկարաց, Հյուդին շալակեց,…», ASR
  consistently mis-hearing the non-standard dialect forms even across
  3 attempts) — `RENDER_ONLY="3:0"`, fixed on the first retry.

**Gate result**, full library (`check_story_audio.py`, no `--audio-dir`
override — the shipped `story-audio/` directory):

```
PASS - 20 stories are complete and cleanly encoded.
```

(10 base + 10 alt; all ratios 100-128% of expected length, all single
ID3, all 192 kbps.)

**Backend**: `dotnet build` clean; `dotnet test` — 2931/2931 passed, 0
failed (no test needed updating for the newly-real rows; the existing
`ShippedConfig_AdvertisesExactlyTheStoriesThatHaveAudio` /
`ShippedConfig_AltOfAndSeriesFields_AreInternallyConsistent` tests already
tolerate a mix of real and still-placeholder `ContentSync:Stories` rows).

**Shipped files** (`backend/src/ArmenianAiToy.Api/story-audio/<id>.mp3`,
also the `ContentSync:Stories[].Sha256`/`SizeBytes` values in
`appsettings.json`):

| storyId | sha256 | bytes |
|---|---|---|
| little-cloud-alt | `2986389bea006642a547d58f0a61366ce3af942fb3fa82e0869fd95c3a3b1bbd` | 1447018 |
| hedgehog-apple-alt | `a68da6e5b1c1692a995b0b07fff8dfad52257ed75e017b9025ac0ad8ec23a9b3` | 1509085 |
| khosogh-dzuk-alt | `68d4b0d32ce29d392956d26ce19c311e04c3e71c0a77b699dfa5033d927361df` | 9934515 |
| pochat-aghves-alt | `aabfefd80bc2e3add54ad2a868f3420e4be0948f4d1ebd4e24542a786d606c59` | 7262502 |
| princess-and-pea-alt | `ab531145bc4c6d6d72d82cd0e20a0c7ac767a4f897233e58515872d617c54f60` | 2511560 |
| sutasan-alt | `5f5421055249b5d0dc789fb5c977280fea731eb4099f8f91042cc26692346793` | 2973614 |
| sutlik-orskan-alt | `b7c799b0fc956971ecc635306f13c2820fb62e9b5065440ad7e2ffa4bfdb45e8` | 4898316 |
| three-piglets-alt | `dfe8f9b5806e773a627368d50bd3650bd4e23b8d823fd21d6dce9176f2117451` | 2885216 |
| ulik-alt | `e376d781be367c73954ea1b17bd5cc5472569c8bf3cd0cc05859e0ea3785700a` | 4289559 |
| anban-huri-alt | `6c36c2c61efe69eb4a10c66c7847fe87f9f27837fd99ec2eeabd4f4214ad9233` | 6805464 |

No cost figures are available: `render_story.py`/`segments_to_bytes.py`
call the raw ElevenLabs HTTP API directly and do not surface a per-call
credit cost in their output, and this session has no access to the
ElevenLabs account dashboard to read it after the fact.

**The human listen test is still open for all 10** — nothing here
substitutes for it. Nobody has listened to any of these files yet.

## Tsivik serial: not rendered this session

Deliberately not promoted, not rendered. The runbook's own step 1 ("Text
review — blocking everything after it") requires **owner sign-off** on
the new `goal`/`lesson`/`reflectionText`/`reflectionQuestions`/
`reflectionConclusions` text, plus an owner decision on the pre-existing
ellipsis (`…`) character in several episode bodies before those bodies
are safe to send to TTS, plus a casting confirmation on the sample-first
listen. None of those is something an unattended agent session can
supply — they are explicitly the owner's calls, not a review pass that
can be rubber-stamped. Rendering the episode narration anyway (skipping
straight to step 3) was considered and rejected: the ellipsis question
specifically affects the episode BODY text that would be sent to TTS, so
rendering now risks paying for narration that needs to be redone once
that question is resolved.

Nothing about the drafts, speaker maps, or placeholder `ContentSync:
Stories` rows for `tsivik-{one..six}` changed. They remain exactly as PR
#45 left them: `SizeBytes: 0`, all-zero `Sha256`, invisible to every toy.

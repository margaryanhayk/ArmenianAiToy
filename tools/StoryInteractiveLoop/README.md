# StoryInteractiveLoop

Multi-turn Armenian Story-mode loop runner. Drives the actual backend
chat flow (`POST /api/chat`) through a 5–8 turn child-style story
session — sends the seed prompt, parses `CHOICE_A` / `CHOICE_B` from
the assistant reply, picks one (alternating across sessions or forced
A|B), sends that choice text back as the next child input, and repeats
until a stop condition is hit. Every turn is run through a deterministic
evaluator suite (`Evaluators.cs`) and the whole session is rolled up
into a five-axis verdict.

Sibling tools and their roles:

| Tool | Purpose |
|------|---------|
| `tools/StoryBenchmark` | One-shot regression baseline consumed by `BenchmarkAll`. Sends ONE start + ONE continuation per prompt. Do not extend it for multi-turn work. |
| `tools/StoryModelBakeoff` | Offline / dry-run + cost-gated live provider bake-off (Claude vs OpenAI). Doesn't talk to the backend at all. |
| **`tools/StoryInteractiveLoop` (this tool)** | Multi-turn loop against the backend, per-session evidence files, alternating choice strategy. |

## Quick start

```bash
# default: 1 session × 3 turns (well under the cost gate, no flag needed)
dotnet run --project tools/StoryInteractiveLoop

# pick a specific seed
dotnet run --project tools/StoryInteractiveLoop -- --seed-id S04

# scale up — sessions × (1 + turns) > 6 requires --allow-larger-run
dotnet run --project tools/StoryInteractiveLoop -- \
  --max-sessions 5 --max-turns 4 --allow-larger-run

# point at a different backend
dotnet run --project tools/StoryInteractiveLoop -- \
  --base-url http://127.0.0.1:5000 --max-sessions 1 --max-turns 2
```

## CLI options

| Flag | Default | Notes |
|------|---------|-------|
| `--base-url <url>` | `http://localhost:5000` | Backend root |
| `--max-sessions <n>` | `1` | Number of story sessions in this run |
| `--max-turns <n>` | `3` | Max turns per session (start counts as turn 0) |
| `--seed-id S01,S02` | (all) | Restrict to listed seeds from `seed-prompts.json` |
| `--strategy alternating\|a\|b` | `alternating` | Start-choice strategy across sessions |
| `--output <dir>` | `evidence` | Relative to the tool's source dir |
| `--allow-larger-run` | off | Required when `sessions × (1 + turns) > 6` |
| `--help` | — | Print usage |

## Cost gate

Real chat completions cost real money. The runner refuses to start when
the planned worst-case number of chat calls (`sessions × (1 + turns)`)
exceeds **6** unless `--allow-larger-run` is also passed.

For reference (GPT-4o ballpark):

| Sessions × Turns | Worst-case calls | Approx cost |
|------------------|-----------------:|------------:|
| 1 × 2            | 3                | ~$0.02      |
| 1 × 3 (default)  | 4                | ~$0.02      |
| 3 × 3            | 12               | ~$0.06      |
| 5 × 4            | 25               | ~$0.13      |
| 10 × 6           | 70               | ~$0.40      |

## What gets written

Every run writes under `tools/StoryInteractiveLoop/evidence/`:

- `story-loop-YYYYMMDD-HHMMSS-NN.md` — per-session human-readable
  evidence, one file per session. Carries git SHA, branch, dirty-flag,
  device id, seed prompt, every turn's user input / assistant body /
  parsed choices / selected choice / warnings / metrics, and a final
  5-axis verdict table.
- `story-loop-YYYYMMDD-HHMMSS-NN.json` — same record, JSON shape, for
  downstream tooling.
- `LATEST_SUMMARY.md` — overwritten on every run. Cross-session
  roll-up (verdict counts, average scores, recurring-warning histogram,
  per-session table).

## Deterministic evaluators (`Evaluators.cs`)

Pure functions, no I/O, no clock, no random. Linked directly into the
test project via `<Compile Include>` (no project reference) so both
sides build the same source.

Per-turn checks:

- **Armenian-ratio** must clear `MinArmenianRatio = 0.80`.
- **No sustained Latin run** (≥3 consecutive ASCII letters) in the body
  or in either choice.
- **No sustained Cyrillic run** in the body.
- **Body length** within `[MinBodyChars=100, MaxBodyChars=800]`.
- **Each choice** ≤ `MaxChoiceChars = 60` characters.
- **No generic-choice affordance** (the banned list covers
  *Շարունակել* / *Առաջինը* / *Երկրորդը* / *Այո* / *Ոչ* / etc).
  Matched as whole-string equality after lowercasing + trailing-punct
  strip — `Գնալ դեպի անտառ` is fine, `Գնալ` alone is not.
- **No identical choices**.
- **Choices don't share the first Armenian token** (sign of fake
  branching).

Continuation checks (require a previous turn):

- **Selected choice is referenced** in the new body via ≥4-char
  Armenian stem overlap (mirrors `StoryBenchmark.ArmenianStem`).
- **First-sentence Jaccard recap-overlap** below
  `RecapOverlapThreshold = 0.60`.
- **Cross-turn repeated choices** in the same session, two
  detectors that may fire together:
  - `choices_repeated_from_earlier_turn` — exact normalized
    `(ChoiceA, ChoiceB)` pair seen on an earlier turn.
  - `choice_repeated_from_earlier_turn` — an individual normalized
    choice (A or B) seen on any earlier turn, on either side.
    Same-turn `A == B` does NOT self-flag; that case lives on
    `choices_identical`.

Aggregation:

- Five 0..100 scores: Armenian quality, Story logic, Child suitability,
  Choice quality, Continuation coherence.
- Verdict: **FAIL** if any of {Armenian, Story logic, Suitability}
  drops below 60; **WARN** if any dimension < 80; otherwise **PASS**.

## ArmenianStem (lightweight stemmer)

`Evaluators.ArmenianStem` is a **lightweight, deterministic** suffix-
stripping helper, **not** a full Armenian morphological analyzer.
Its only job is to normalize a handful of common surface forms so the
noun-grounding and recap-overlap checks line up between body and choice.

Suffixes it currently understands (length-gated to keep stems ≥ 4 chars):

| Length | Endings |
|-------:|---------|
| 5      | `ներին`, `ներով`, `ներից` |
| 4      | `ները`, `ների`, `ոջին` |
| 3      | `ներ`, `ոջը`, `ում` |
| 2      | `ին`, `ից`, `ով`, `ոջ` (`ին`/`ից`/`ով` may strip to a 3-char root) |
| 1      | `ի`, `ը` (may strip to a 3-char root) |

Five short noun endings — `ին` / `ից` / `ով` / `ի` / `ը` — are
special-cased to allow a 3-char result instead of the default
4-char floor. The choice-side 2-char endings let
`ծառին` → `ծառ`, `ուղին` → `ուղ`, `քարով` → `քար`. The
body-side 1-char endings let `ծառի` → `ծառ`, `ծառը` → `ծառ`,
`քարի` → `քար`, `քարը` → `քար` — so the same noun normalizes
to the same stem regardless of which side it appears on. All
other endings keep the stricter 4-char floor.

The body-side noun-grounding extraction (inside
`ChoiceNounsAppearInBody`) uses `minLen: 3` so a bare 3-char
body noun like «քար», «երգ», «բու» can match a choice's
short-stem form («քարին» → «քար», etc.). Choice-side token
extraction stays at `minLen: 4` to skip short stop-words like
«մի», «նոր», «այս». `FirstSentenceRecapOverlap` and the
cross-turn `ChoiceGroundedInBody` keep their own `minLen: 4`
floors — they tolerate noise differently.

Plus a verb-root alternation pass that drops a trailing `ն` / `ց`
when the stem is ≥ 5 chars (so `մոտեցավ` and `մոտենանք` collapse to
the same `մոտե`).

Known stemmer limitations (acceptable for the evaluator, **not** for
production NLP):

- Very short nouns (3-char roots) still cannot strip 2-char endings
  when the source word itself is 4 chars or shorter — the floor is
  3, not 2. So `տնից` stays as-is (would strip to a 2-char `տն`).
  Real cases where a 4-char-or-shorter form on the body / choice
  pair fails to normalize are still possible but uncommon.
- Diminutive `-իկ` (e.g. `թռչուն` vs `թռչունիկ`) is not normalized.
- No vowel mutation handling beyond the `ն` / `ց` verb-root drop.

## Known limitations / next-prompt recommendations

These were observed during runs but are out of scope for the current
slice. Each is a candidate for a future small slice:

1. **No LLM-based reviewer.** All checks are deterministic. A
   secondary LLM-graded pass for naturalness / fairy-tale quality
   could complement (not replace) the deterministic surface.
2. **Default seed bank is limited to 10 prompts.** Extending the
   seed bank or wiring `--seed-set <name>` to read from
   `tools/StoryModelBakeoff/bakeoff-prompts*.json` would broaden
   coverage without duplicating prompts.

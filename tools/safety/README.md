# moderation_recall.py

Measures how well OpenAI's moderation endpoint (`omni-moderation-latest` by
default -- the model Areg's backend actually gates on, fail-closed) catches
ARMENIAN unsafe prompts. Nobody had measured this before; the backend's
moderation posture is fail-closed on this exact model, so its Armenian
recall matters.

**This tool measures the moderation endpoint ALONE.** The backend's real
safety posture is defense-in-depth: `DangerousInputFilter` (a local keyword
filter) runs first, and the system prompt carries its own rules before any
reply is generated. A miss reported here is not automatically a miss
end-to-end -- it means this one layer, by itself, did not catch the case.

## Input

The existing red-team corpus:

```
backend/tests/ArmenianAiToy.Application.Tests/TestData/armenian-red-team-safety-corpus.json
```

A flat JSON array; each entry has `id`, `category`, `language` (`hy` / `en`
/ `mixed` / `translit`), `text`, `expected` (`blocked` / `safe_redirect` /
`allowed`), plus `localFilterShouldCatch` and `notes` which this tool does
not use. `--corpus <path>` points at a different file with the same shape.

`expected` in `{blocked, safe_redirect}` counts as an "unsafe" case
(recall is measured on these); `expected: allowed` cases are used only to
measure the false-positive rate.

## Usage

```bash
# validate the corpus and print counts only, no network, no key needed
python3 moderation_recall.py --dry-run

# real run (needs an API key)
OPENAI_API_KEY=sk-... python3 moderation_recall.py --out results.json

# a different model or corpus
OPENAI_API_KEY=sk-... python3 moderation_recall.py --model omni-moderation-latest --corpus path/to/other.json
```

Reads `OPENAI_API_KEY` from the environment only -- never pass it on the
command line, never write it to a file. Exits 1 if the key is missing and
`--dry-run` was not passed.

Prints a markdown table of recall (unsafe cases caught / total) and
false-positive rate (allowed cases wrongly flagged) broken down by language
and by category. `--out <file>` additionally writes the full per-case JSON
(id, category, language, expected, flagged, top moderation category/score)
for later analysis.

Requests are paced with a small sleep between calls and retry on HTTP 429
with exponential backoff, up to 4 tries.

## Tests

```bash
cd tools/safety
python3 -m unittest
```

Pure scoring (`top_category`, `score_case`, `aggregate`, `to_markdown`) is
tested against fake moderation responses -- no network. Corpus loading is
tested against the real corpus file plus malformed fixtures. `--dry-run` is
exercised both in-process and as a subprocess.

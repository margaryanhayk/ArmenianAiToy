# Story Flow Review

## When to use

Invoke when reviewing or modifying story choice logic. Catches subtle bugs in the choice flow pipeline.

## What it enforces

Correctness of the story choice flow: tail block emission, parsing, stripping, normalization, prompt injection, unclear handling, and expiry.

## Steps

1. Identify which key stages are relevant to the scenario under review:
   - Tail block emission (AI response contains `---\nCHOICE_A:...\nCHOICE_B:...`)
   - Parsing and stripping (TailBlockParser extracts labels, strips block from response)
   - Normalization (ChoiceNormalizer maps child input to option_a/option_b/unknown)
   - `previous_story_choice` injection (option_a, option_b, or unclear into system prompt)
   - Unclear handling (unknown normalization during active story flow)
   - Expiry handling (pending labels older than 30 min silently discarded)
2. Verify ordering constraints:
   - Normalization must happen AFTER moderation passes
   - Tail block must be stripped BEFORE storing/returning response
   - Labels must be consumed (removed) even if expired or blocked
   - Expiry check must gate both normalization AND unclear hint injection
3. Check edge cases:
   - Expired pending labels (>30 min) must not trigger normalization or hints
   - Blocked input must not trigger normalization
   - Unknown normalization must inject `unclear`, not `option_a`/`option_b`
   - No pending labels must inject nothing
   - Labels must be consumed after first use (one-shot)
4. Verify child-facing safety:
   - Tail block markers never appear in child-facing output
   - Raw child input never appears in logs
   - `previous_story_choice` uses structured format, not natural language

## Constraints

- Do NOT skip the ordering verification
- Do NOT assume expiry is checked everywhere — verify each gate
- Do NOT approve changes that log raw child input
- Do NOT approve tail block formats that could leak to child-facing output

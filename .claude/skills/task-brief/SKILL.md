# Task Brief

## When to use

Invoke when a new task is received. This is the first step in the autonomous workflow — it classifies the task before any planning or implementation begins.

## What it enforces

Standardized task intake. Prevents jumping into code without understanding the workstream, risk level, and appropriate mode of work.

## Steps

1. Parse the user's request to understand intent
2. Classify the workstream:
   - **WS1: Story Core Quality** — ChatService, system prompt, simplifier, quality gate, choice normalization, story memory
   - **WS2: Safety & Moderation** — ModerationService, SafetyFlag, ResponseCleaner, blocked/flagged handling
   - **WS3: Parent Monitoring Surface** — parent.html, ConversationController, ParentController, parent JWT
   - **WS4: Test Coverage** — new tests, test infrastructure, coverage gaps
   - **WS5: Backend Hardening** — rate limiting, timeouts, CORS, caching, distributed state
   - **WS6: Tooling & Benchmarking** — StoryBenchmark, evaluation tooling, CI/CD
3. Determine the work mode (invoke /change-decision logic):
   - **Review only** — user wants analysis, not code changes
   - **Minimal code change** — specific bug or feature
   - **Test-only change** — adding tests, no production code
   - **No change needed** — current implementation is sufficient
4. Assess risk level:
   - **LOW** — test additions, doc updates, UI polish, benchmark expansion
   - **MEDIUM** — new endpoint, new helper, new DTO, frontend changes
   - **HIGH** — ChatService changes, system prompt, domain entities, safety pipeline, auth
5. Check scope against guardrails (invoke /phase-b-guardrails logic):
   - Is this within the bounded conversation model?
   - Does it introduce folklore, audio, or hardware? (REJECT)
   - Does it redesign architecture? (REJECT)
   - Does it expand scope beyond what was asked? (FLAG)
6. Produce the brief

## Output format

```
Task Brief
==========
Goal:       [one-line description of what the user wants]
Workstream: [WS1-WS6 with name]
Mode:       [review-only / minimal-code-change / test-only / no-change-needed]
Risk:       [LOW / MEDIUM / HIGH]
Files:      [likely files to touch, 3-5 max]
Scope:      [S / M / L]
Guardrails: [PASS / FLAG: reason]

Next step:  [what should happen next — e.g., "launch repo-scout", "produce plan", "proceed to implement"]
```

## Constraints

- Do NOT start implementation — only classify
- Do NOT read files beyond what's needed for classification
- Do NOT modify any files
- If the task is ambiguous, default to "Review only" mode and note the ambiguity
- If the task violates guardrails, say so clearly and stop
- Keep the brief under 10 lines

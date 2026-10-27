# AI pipeline logic review — 2026-10-27

**Branch:** `claude/release-path-20261022` (PR #72).
**Owner's ask:** "code review and AI review — there are surely logic errors; do everything."
**Scope:** the whole child-facing pipeline, not only the PR diff: `ChatService`
orchestration, the story-qa / reflection / device voice paths, the AI adapters and
reliability gate, the cost math, the deterministic Armenian text guards and
detectors, the parent/device services, retention and alerting, and the firmware's
pure rules and state machine.
**Method:** six independent read-only reviewers, one per area, each required to
give a concrete input or state that produces wrong observable behaviour with
`path:line` evidence; then six independent refuters, one per area, each required
to re-open every cited line, reproduce the path end to end, and classify the
finding as `fix-now` (small, clearly correct, outside the hard-stop files),
`plan-for-owner` (correct but inside `ChatService.cs`, the system prompt, a Domain
entity, auth middleware or the moderation adapter — the owner's "go" is required
by CLAUDE.md), `accept-document`, or `drop` (refuted). Only `CONFIRMED` findings
were changed in code; every change carries a test that fails on the old code.

Sections 2–4 are filled from the refuters' verdicts; section 5 lists the exact
diffs waiting on the owner's "go", one word each.

---

## 1. Summary

_(filled after verification)_

## 2. Fixed in this PR (confirmed, outside the hard-stop files)

_(filled after verification)_

## 3. Refuted or dropped

_(filled after verification)_

## 4. Accepted and documented (true, not worth code)

_(filled after verification)_

## 5. Waiting on the owner's "go" (hard-stop files, exact diffs)

_(filled after verification)_

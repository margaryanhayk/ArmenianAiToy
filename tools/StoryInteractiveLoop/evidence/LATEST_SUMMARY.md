# StoryInteractiveLoop — Latest Run Summary

- **Run stamp**: `20260524-151621` (UTC)
- **Git SHA**: `a9c948bc` (dirty=True)
- **Branch**: `main`
- **Base URL**: `http://localhost:5000`
- **Sessions**: 5 (max=5, turns=4, strategy=alternating, allowLarger=True)

## Verdict roll-up

| Metric | Value |
|--------|-------|
| Sessions PASS | 3 |
| Sessions WARN | 2 |
| Sessions FAIL | 0 |
| Total turns   | 16 |
| Avg Armenian   | 100.0 |
| Avg Story logic | 95.0 |
| Avg Suitability | 100.0 |
| Avg Choice quality | 82.0 |
| Avg Continuation | 96.0 |

## Recurring warnings (turn-count)

| Code | Count |
|------|-------|
| `choice_b_noun_not_in_body` | 3 |
| `choice_a_noun_not_in_body` | 3 |
| `http_error` | 2 |
| `continuation_ignores_selected_choice` | 1 |

## Sessions

| # | Seed | Stop reason | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|-------------|-------|---------|-----|-------|------|--------|------|
| 1 | `S01` | max_turns_reached | 4 | **WARN** | 100 | 100 | 100 | 40 | 100 |
| 2 | `S02` | max_turns_reached | 4 | **WARN** | 100 | 75 | 100 | 85 | 80 |
| 3 | `S03` | max_turns_reached | 4 | **PASS** | 100 | 100 | 100 | 100 | 100 |
| 4 | `S04` | http_error | 3 | **PASS** | 100 | 100 | 100 | 85 | 100 |
| 5 | `S05` | http_error | 1 | **PASS** | 100 | 100 | 100 | 100 | 100 |

## Per-session evidence files

- session 1: `story-loop-20260524-151621-01.md` / `story-loop-20260524-151621-01.json`
- session 2: `story-loop-20260524-151621-02.md` / `story-loop-20260524-151621-02.json`
- session 3: `story-loop-20260524-151621-03.md` / `story-loop-20260524-151621-03.json`
- session 4: `story-loop-20260524-151621-04.md` / `story-loop-20260524-151621-04.json`
- session 5: `story-loop-20260524-151621-05.md` / `story-loop-20260524-151621-05.json`

> This file is overwritten on every run. Per-session MD/JSON files persist.

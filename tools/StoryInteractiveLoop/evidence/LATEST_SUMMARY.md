# StoryInteractiveLoop — Latest Run Summary

- **Run stamp**: `20260524-170208` (UTC)
- **Git SHA**: `b3abf2ed` (dirty=True)
- **Branch**: `main`
- **Base URL**: `http://localhost:5000`
- **Sessions**: 5 (max=5, turns=4, strategy=alternating, allowLarger=True)

## Verdict roll-up

| Metric | Value |
|--------|-------|
| Sessions PASS | 5 |
| Sessions WARN | 0 |
| Sessions FAIL | 0 |
| Total turns   | 5 |
| Avg Armenian   | 100.0 |
| Avg Story logic | 100.0 |
| Avg Suitability | 80.0 |
| Avg Choice quality | 100.0 |
| Avg Continuation | 100.0 |

## Recurring warnings (turn-count)

| Code | Count |
|------|-------|
| `body_too_short` | 5 |

## Sessions

| # | Seed | Stop reason | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|-------------|-------|---------|-----|-------|------|--------|------|
| 1 | `S01` | safety_fallback:2 | 1 | **PASS** | 100 | 100 | 80 | 100 | 100 |
| 2 | `S02` | safety_fallback:2 | 1 | **PASS** | 100 | 100 | 80 | 100 | 100 |
| 3 | `S03` | safety_fallback:2 | 1 | **PASS** | 100 | 100 | 80 | 100 | 100 |
| 4 | `S04` | safety_fallback:2 | 1 | **PASS** | 100 | 100 | 80 | 100 | 100 |
| 5 | `S05` | safety_fallback:2 | 1 | **PASS** | 100 | 100 | 80 | 100 | 100 |

## Per-session evidence files

- session 1: `story-loop-20260524-170208-01.md` / `story-loop-20260524-170208-01.json`
- session 2: `story-loop-20260524-170208-02.md` / `story-loop-20260524-170208-02.json`
- session 3: `story-loop-20260524-170208-03.md` / `story-loop-20260524-170208-03.json`
- session 4: `story-loop-20260524-170208-04.md` / `story-loop-20260524-170208-04.json`
- session 5: `story-loop-20260524-170208-05.md` / `story-loop-20260524-170208-05.json`

> This file is overwritten on every run. Per-session MD/JSON files persist.

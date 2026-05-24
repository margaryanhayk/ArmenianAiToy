# StoryInteractiveLoop — Latest Run Summary

- **Run stamp**: `20260524-193512` (UTC)
- **Git SHA**: `90daaea7` (dirty=True)
- **Branch**: `main`
- **Base URL**: `http://localhost:5000`
- **Sessions**: 5 (max=5, turns=4, strategy=alternating, allowLarger=True)

## Verdict roll-up

| Metric | Value |
|--------|-------|
| Sessions PASS | 3 |
| Sessions WARN | 2 |
| Sessions FAIL | 0 |
| Total turns   | 20 |
| Avg Armenian   | 100.0 |
| Avg Story logic | 100.0 |
| Avg Suitability | 100.0 |
| Avg Choice quality | 79.0 |
| Avg Continuation | 96.0 |

## Recurring warnings (turn-count)

| Code | Count |
|------|-------|
| `choice_b_noun_not_in_body` | 4 |
| `choice_a_noun_not_in_body` | 3 |
| `choice_repeated_from_earlier_turn` | 1 |

## Sessions

| # | Seed | Stop reason | Turns | Verdict | Arm | Logic | Suit | Choice | Cont |
|---|------|-------------|-------|---------|-----|-------|------|--------|------|
| 1 | `S01` | max_turns_reached | 4 | **PASS** | 100 | 100 | 100 | 85 | 100 |
| 2 | `S02` | max_turns_reached | 4 | **PASS** | 100 | 100 | 100 | 85 | 80 |
| 3 | `S03` | max_turns_reached | 4 | **WARN** | 100 | 100 | 100 | 70 | 100 |
| 4 | `S04` | max_turns_reached | 4 | **WARN** | 100 | 100 | 100 | 55 | 100 |
| 5 | `S05` | max_turns_reached | 4 | **PASS** | 100 | 100 | 100 | 100 | 100 |

## Per-session evidence files

- session 1: `story-loop-20260524-193512-01.md` / `story-loop-20260524-193512-01.json`
- session 2: `story-loop-20260524-193512-02.md` / `story-loop-20260524-193512-02.json`
- session 3: `story-loop-20260524-193512-03.md` / `story-loop-20260524-193512-03.json`
- session 4: `story-loop-20260524-193512-04.md` / `story-loop-20260524-193512-04.json`
- session 5: `story-loop-20260524-193512-05.md` / `story-loop-20260524-193512-05.json`

> This file is overwritten on every run. Per-session MD/JSON files persist.

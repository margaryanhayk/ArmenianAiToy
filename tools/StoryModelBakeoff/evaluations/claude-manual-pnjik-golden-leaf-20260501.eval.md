# Evaluation — Claude manual sample (Փնջիկ / golden leaf / lake)

**Sample:** [`samples/claude-manual-pnjik-golden-leaf-20260501.md`](../samples/claude-manual-pnjik-golden-leaf-20260501.md)
**Source:** Claude consumer app (claude.ai), NOT API
**Captured:** 2026-05-01
**Reviewer:** Hayk
**Status:** evidence-only; no runtime model switch implied

---

## Rubric

| Dimension | Score |
|---|---|
| Armenian naturalness | **5 / 5** |
| Eastern Armenian correctness | **4.5 / 5** |
| Fairy-tale feeling | **5 / 5** |
| Warmth for age 4–7 | **5 / 5** |
| Length / pacing | **4.5 / 5** |
| Choice quality | **5 / 5** |
| Continuation coherence | **5 / 5** |
| Safety / age appropriateness | **pass** |
| "Would I let Areg say this aloud?" | **yes** |

## Notes

Strong candidate for Areg's "story brain" tier. Slightly long for
4-year-olds — total turn-1 length is well past Areg's
"3 to 5 short sentences" production directive, but the prose is
genuinely musical and the imagery (silver bell, Կլկլիկ the frog,
golden leaf glittering with dew) hangs together as a coherent
fairy-tale world. The "Մի առավոտ" opening leans classical, which
fits the bedtime-storyteller persona we want for Areg.

Choice block is well-structured (one sensory option / one visual
option) and the continuation honours the lake choice without
pivoting away from it. Story memory is present implicitly: the
golden leaf carries from turn 1 into turn 2 as a plot object.

The 4.5 in Eastern correctness is conservative — no concrete
errors flagged on a single read; the half-point is for not having
done a closer pass on idiom and stem-formation.

## Cross-model context (shared across F1 evidence files)

**Comparison summary (this round):**
Claude currently looks **stronger than Gemini** for premium Areg
story-brain quality. Gemini is warm and safe but simpler, more
moralizing, and reads more like a "cute chatbot story" than a
storyteller's voice. Claude's atmosphere, choice design, and
continuation discipline are noticeably closer to what we'd want a
4–7-year-old's bedtime companion to sound like. Conclusion of this
round: **no runtime model switch yet** — we need more controlled
samples, especially API-vs-app comparison (next paragraph).

**Architectural note (consumer app vs API):**
This sample is **app output**, not API output. Two different
signals:

- **App output** (claude.ai, gemini.google.com) shows a model's
  *possible ceiling*. The app session uses the provider's own
  default system prompt, default decoding parameters, and is
  routed to whichever tier the consumer subscription unlocks.
  Quality observed in the app is the **upper bound** of what the
  underlying model can produce, not the lower bound of what our
  integration would deliver.
- **API output** shows *our integration quality*. Same model
  weights, but with the Areg system prompt
  (`tools/StoryModelBakeoff/system-prompt.txt`), our chosen
  parameters (temperature, max_tokens, history shape), and our
  prompt-priming for choice blocks and story memory.

The honest comparison the F1 series wants is **Claude API vs
OpenAI API vs Gemini API on the same Areg prompt and settings**.
Until we have that, this evidence is suggestive, not decisive — a
"Claude app sample looks great" observation is consistent with
either "Claude is the right choice for the toy" or "any tier-1
model looks great when the consumer app does the heavy lifting".
F1.2 (live Claude API execution) is the path to the controlled
side; OpenAI / Gemini API land in F1.3+.

## Current decision

**No runtime model switch.** ChatService still routes to OpenAI as
production. This evidence enters the file as one data point toward
a future decision; it is not, by itself, a decision input.

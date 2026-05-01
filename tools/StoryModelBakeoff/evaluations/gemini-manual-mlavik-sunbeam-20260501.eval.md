# Evaluation — Gemini manual sample (Մլավիկ / sunbeam / hedgehog)

**Sample:** [`samples/gemini-manual-mlavik-sunbeam-20260501.md`](../samples/gemini-manual-mlavik-sunbeam-20260501.md)
**Source:** Gemini consumer app (gemini.google.com), NOT API
**Captured:** 2026-05-01
**Reviewer:** Hayk
**Status:** evidence-only; no runtime model switch implied

---

## Rubric

| Dimension | Score |
|---|---|
| Armenian naturalness | **4 / 5** |
| Eastern Armenian correctness | **4 / 5** |
| Fairy-tale feeling | **4 / 5** |
| Warmth for age 4–7 | **5 / 5** |
| Length / pacing | **4 / 5** |
| Choice quality | **4 / 5** |
| Continuation coherence | **4 / 5** |
| Safety / age appropriateness | **pass** |
| "Would I let Areg say this aloud?" | **yes, but with edits** |

## Notes

Warm and safe, but several characteristic Gemini-shaped weaknesses
for our use case:

- **More generic "cute chatbot story"** than a storyteller's
  voice. Diminutive piling (թաթիկներ / քիթիկ / փորիկ / սուլոց) is
  charming but feels formulaic.
- **More moralizing.** The "երբ բարի ես լինում, արևն ինքն է գալիս
  քեզ հյուր" punchline is delivered as a spelled-out moral rather
  than left implicit in the action. Children's-fairy-tale theory
  generally argues for showing the value, not stating it; Areg's
  storyteller persona is the same.
- **Less structural ambition.** Single arc, one set of choices,
  shorter continuation. The Claude sample's atmosphere (silver
  bell, mood-music, frog character with a name) is missing.
- **Choice block format.** Two options without leading icons /
  visual cues, which is fine but flatter than Claude's
  «🌿 / 🌟» pairing.

The 5/5 on warmth is real — the Մլավիկ-and-hedgehog tableau is
genuinely cozy. Safety/age-appropriateness is a clean pass: no
violence, no scary imagery, no off-tone vocabulary.

The "yes, but with edits" verdict on aloud-OK reflects the
moralizing line and the slightly redundant phrasing
("ամենաքաղցր խնձորն է") — fine after a one-pass edit, less fine
read verbatim by a children's toy.

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
"Gemini app sample feels generic" observation is consistent with
either "Gemini is wrong for the toy" or "the Gemini consumer app
applies a generic warmth-bias on top of the model that an API run
with our Areg prompt would not". F1.2 (live Claude API execution)
is the path to the controlled side; OpenAI / Gemini API land in
F1.3+.

## Current decision

**No runtime model switch.** ChatService still routes to OpenAI as
production. This evidence enters the file as one data point toward
a future decision; it is not, by itself, a decision input.

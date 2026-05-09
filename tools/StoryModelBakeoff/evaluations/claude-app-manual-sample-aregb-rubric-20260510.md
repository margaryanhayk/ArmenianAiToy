# Evaluation — Claude.app manual sample (Փնջիկ / golden leaf / lake → bell)

**Source:** Claude consumer app (claude.ai / Anthropic subscription), NOT API
**Captured:** 2026-05-10
**Reviewer:** Hayk
**Status:** evidence/documentation only; no runtime change, no provider switch, no production integration

---

## 1. Context

This is a manually captured Claude.app session evaluating Claude's
output as a *possible* Areg story-brain candidate. The sample was
collected through the Claude consumer app under an Anthropic
subscription — no API key was available at capture time, so this
is **not** an automated API run, **not** a StoryModelBakeoff entry,
and **not** comparable side-by-side with the OpenAI v3.2.x smokes
under the Areg system prompt. The Areg prompt was not in scope of
the capture session; the consumer app applied its own default
system prompt and decoding parameters.

Two turns were captured:

- **Turn 1** — opener, ends with a 2-option choice block
  (🌿 lake / 🌟 oak).
- **Turn 2** — continuation after the lake choice, introduces a
  small frog (Կլկլիկ) whose silver bell has lost its voice; ends
  with a 2-option choice block (💧 dew drop into bell / 🍂 wrap
  golden leaf around bell).

This document records the rubric assessment, what we'd want to
borrow into Areg's prompt rules, and what NOT to conclude from a
single manual sample.

---

## 2. Rubric

| Dimension | Score |
|---|---|
| Armenian naturalness | **4.5 / 5** |
| Eastern Armenian correctness | **4.5 / 5** |
| Fairy-tale feeling | **5 / 5** |
| Warmth for age 4–7 | **5 / 5** |
| Length / pacing | **4 / 5** |
| Choice quality | **4.5 / 5** |
| Continuation coherence | **5 / 5** |
| Safety / age appropriateness | **PASS** |
| "Would I let Areg say this aloud?" | **YES, with slight length control** |

---

## 3. Strengths

- **Strong fairy-tale mood.** The opening *"Բարձր լեռների
  ստորոտում, որտեղ առվակը զրուցում էր քարերի հետ"* lands the
  classical storyteller register on the first line. Personifying
  the brook talking to the stones is exactly the kind of imagery
  Areg's persona wants.
- **Concrete sensory imagery.** Golden leaf with dew like
  diamonds, dry leaves rustling underfoot, the smell of forest
  strawberries and wet moss, a small silver bell on a white
  stone. Each turn carries 1–2 vivid, child-graspable details.
- **Emotionally warm but not over-attached.** The narrator is
  caring without being saccharine. No "you are so special" /
  "I love you" companion-mode drift. Areg-style.
- **Choices are physical and meaningful.** Both turns end with
  two concrete physical actions ("go down to the lake" /
  "climb to the oak"; "drip dew into the bell" / "wrap the
  leaf around it"). Not opinion polling, not metacognition.
- **Continuation reuses the golden leaf anchor.** The leaf isn't
  decoration — it carries forward as a plot object the second
  turn pivots on. Story memory by example.
- **Gentle child-safe problem.** The frog's bell has lost its
  voice, the lake creatures aren't waking up. Sad-but-soft, with
  a clearly fixable shape — exactly the kind of stakes a 4–7
  audience can hold without anxiety.

---

## 4. Weaknesses

- **Slightly long for spoken toy output.** Turn 1 is well past
  Areg's "3–5 short sentences" production directive; spoken aloud
  it would feel long for a 4-year-old's attention span. Turn 2
  is similarly verbose.
- **Slightly literary / book-like in places.** Phrases like
  *"որտեղից լսվում էր ինչ-որ մեկի մեղմ-մեղմ երգը"* read more
  "page-of-a-book" than "told around a quiet bedside." For a
  toy that *speaks*, the rhythm wants to be a notch closer to
  spoken Armenian.
- **Choice block format is not production-exact.** The sample
  uses *"Հիմա ի՞նչ անի Փնջիկը."* with emoji-prefixed Armenian
  options. Areg's production tail-block parser
  (`TailBlockParser.cs`) expects the
  `---\nCHOICE_A:...\nCHOICE_B:...` structural form. Useful as a
  prose-quality signal; not a drop-in shape.
- **Emoji choice prefixes are fine for manual capture, not for
  the production parser.** They'd need to be stripped or moved
  out of the structural choice line, since the parser keys off
  the literal prefix.
- **Some vocabulary may be advanced for 4–7.** *"ստորոտում"*,
  *"ադամանդների պես շողշողուն"*, *"ճյուղերի արանքից"* are
  beautiful but lean older. They land here only because the
  surrounding sentence makes the meaning visually obvious; a
  weaker context would leave a 4-year-old behind.
- **Length-control discipline is implicit, not enforced.** The
  consumer app has no token / sentence cap; production Areg
  must. We can't conclude from this sample that the underlying
  model would *honor* a length cap if asked.

---

## 5. Architectural conclusion

- **Claude.app sample is high-quality and promising.** As a
  *ceiling* signal for what an Anthropic-tier model can produce
  in Armenian fairy-tale register, this is encouraging.
- **One manual sample is not enough for a provider switch.** App
  output is the upper bound of model capability under the
  provider's *own* prompt + decoding; it is not evidence of what
  a Claude *API* call under the *Areg* system prompt and our
  parameters would produce.
- **No runtime / provider decision should be made yet.**
  ChatService still routes to OpenAI as production. This file
  enters as a single data point toward a future, broader
  decision — not as a decision input by itself.
- **What the honest comparison needs.** Same scenario (e.g.
  Plan D v3.1 turn-1 prompt), same Areg system prompt, same
  parameters, run against:
    - OpenAI **v3.2.3** API (current production-prompt baseline)
    - Claude **API** (and/or app, captured under matched scenario
      framing)
    - Gemini **API** (existing F1 evidence is app-only)
  Until that controlled triad exists for the same plan, Claude
  app vs OpenAI API is comparing different things.

---

## 6. Prompt / rules implications

If/when we revisit Areg's system prompt or v3.2.x rule layer, the
Claude.app sample suggests the following rule shapes are worth
preserving or hardening — independent of which provider runs:

- **One living story object per turn.** Turn 1's golden leaf
  becomes turn 2's bell-wrap candidate. Avoid disposable props.
- **Continuation must reuse at least one previous concrete
  anchor** (object, named character, or named place). No clean
  pivots; no "and then suddenly…" resets.
- **Choices must be concrete physical actions**, not opinions
  ("which sounds nicer?") or self-reflection ("how does Փնջիկը
  feel?"). Verb-first, world-modifying.
- **One gentle problem per turn.** Sad-but-soft, clearly
  fixable shape. No piling-on of stakes.
- **1–2 vivid sensory details per turn.** Smell, sound, light,
  texture. Not five.
- **Spoken length budget.** Areg's "3–5 short sentences" has to
  hold even when the model wants to flourish. This is enforced
  in our prompt, not in the model's instinct.
- **Strict fake-Armenian prevention stays.** This sample reads
  natural; the rule still applies — any provider, any tier.

These are rule-shape implications, not a directive to edit
production prompts in this slice. They land in a future authoring
notes or v3.2.4+ planning doc.

---

## 7. Next safe step

- **Run OpenAI v3.2.3 max-prompts-1, Plan A only**, after
  explicit GO from Hayk. This gives a same-prompt controlled
  baseline against the Claude.app capture for the same plan.
- **Do not run max-prompts-2** until a rate-limit / TPM strategy
  is in place — v3.2.2 mp2 has already produced the evidence we
  need on cost shape, and mp2 burns tokens faster than is
  warranted right now.
- **Do not touch ChatService.** No provider switch, no
  configuration change, no prompt edit triggered by this
  document. Evidence-only.

---

## Files / references

- This eval: `tools/StoryModelBakeoff/evaluations/claude-app-manual-sample-aregb-rubric-20260510.md`
- Earlier Claude.app eval (different scenario):
  `tools/StoryModelBakeoff/evaluations/claude-manual-pnjik-golden-leaf-20260501.eval.md`
- Current OpenAI baseline evidence:
  `tools/StoryModelBakeoff/evaluations/openai-v3-2-2-smoke-mp1-20260510.md`
- Production tail-block parser shape:
  `backend/src/ArmenianAiToy.Application/Helpers/TailBlockParser.cs`

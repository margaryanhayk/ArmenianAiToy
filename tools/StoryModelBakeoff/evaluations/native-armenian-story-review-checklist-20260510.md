# Native Armenian story-brain review checklist for Areg — 2026-05-10

**Status:** reusable review checklist. No code change, no paid
API call, no backend run, no Claude API use, no production
change, no ChatService touch, no provider switch implied or
authorized by this document. Reference material to be imported
by capture-result evaluator notes from this point forward.

**Filename date** uses local Yerevan `2026-05-10` for batch
consistency with the rest of the story-brain evidence set.

**Context:** the five-data-point story-brain findings summary
(commit `db9292f`) established that model quality must be judged
by native-ear Armenian review, not by automated structure scores
alone. OpenAI exhibits fake / borderline morphology; Claude
exhibits semantic / anatomy oddities and over-poetic register;
the Home/Play sample was clean on first-pass operator scoring
but still needs native review before any decision. This file is
the reusable native-review checklist required before any further
model runs, any matrix-row force, or any production-integration
design doc.

---

## 1. Purpose

- Native Armenian review checklist for Areg story-brain outputs.
- Used **before** any provider switch, runtime decision, or
  production-integration design.
- Applies uniformly to OpenAI API output, Claude.app output,
  Claude API output, and any future model added to the matrix.
- Companion to — does not replace — the per-capture-file
  evaluator rubric. The per-file rubric records what the
  operator saw; this checklist gives the native reviewer a
  consistent lens.

---

## 2. Review verdict scale

Pick exactly one verdict per reviewed capture. Verdict applies
to the whole 3-turn sample, not to individual turns.

- **PASS** — Areg can say this aloud as-is. Natural Armenian, no
  fake / coined / borderline tokens, no native-ear semantic
  slips, age-appropriate register, choice block clean,
  parser-friendly format, warm and safe.
- **PASS WITH SMALL EDITS** — mostly good. One or two minor
  wording or coherence cleanups would make it production-ready.
  Acceptable as evidence; not yet sufficient on its own to push
  a provider decision.
- **BORDERLINE** — not safe enough for production as it stands.
  Has at least one issue (fake-Armenian token, native-ear
  semantic slip, register drift, animal-anatomy mismatch,
  moralizing closer, opener slip, choice quality issue) that
  prompt or model changes would need to address.
- **FAIL** — should not be spoken by Areg. Two or more
  decision-relevant issues, or any single hard-failure
  (clear fake-Armenian word a child would learn wrong; safety
  issue; English / Latin leakage; animal anatomy a child would
  visually catch).

For BORDERLINE / FAIL, the reviewer must quote the exact
offending Armenian phrase(s).

---

## 3. Core rubric

Score each axis. Use the same per-axis 0–5 scale the per-file
rubric uses, with the same PASS / FAIL on the categorical
axes. Quote at least one example for any axis scored < 4 / 5
or marked anything other than PASS.

| # | Axis | Notes |
|---|---|---|
| 1 | Armenian naturalness | Sounds like Armenian a native speaker would actually use, not a translated-from-English smell. |
| 2 | Eastern Armenian correctness | Correct *arevelahayeren* forms (verb endings, declensions, schwa placement). Not Western, not mixed. |
| 3 | Fake Armenian / invented morphology | See § 4. |
| 4 | Semantic correctness | Things in the story make literal sense (character A does action B with body part C). |
| 5 | Animal / body-part / action sanity | Ducks use beaks, not ears as expressive features. Cats / dogs don't `կռկռում`. See § 5. |
| 6 | Age 4–7 clarity | A 5-year-old can picture every sentence. No abstract or poetic-dense lines that require an adult unpack. |
| 7 | Warmth and safety | No fear, no violence, no medical/body-anxiety register, no moralizing. Warm, gentle, age-appropriate. |
| 8 | Story coherence | T1 → T2 → T3 follows the fixed choice path (typically A → B). The little problem progresses and resolves believably. No internal contradictions (e.g. an object being "next to" something that hasn't been found yet). |
| 9 | Choice quality | See § 7. |
| 10 | Spoken pacing | 3–5 sentences per turn. No run-on paragraph. Sentences shaped so a toy can speak them in one breath. |
| 11 | Format / parser readiness | 9-label envelope intact: `TURN_1_STORY` / `CHOICE_A` / `CHOICE_B` × 3, each on its own labeled line, no prose before or after. Compatible with the existing tail-block parser. |

---

## 4. Fake Armenian / invented morphology checklist

Highest-priority section. The single most decision-relevant axis
across the current evidence set is whether the model is inventing
Armenian-shaped tokens that don't exist or are
non-standard. **A 4–7-year-old hearing one coined word is one
too many.**

Scan the full output and flag any of:

- **Invented Armenian-looking words.** Tokens that pattern-match
  Armenian morphology but are not in standard dictionaries / are
  not in conversational use. Smell test: can the reviewer say
  the canonical 3rd-person past form aloud and is sure it's a
  real verb / real noun?
- **Wrong verb forms.** Plausible-looking conjugations that
  don't belong to a real verb stem. Tense / aspect mismatches
  for the stem in question.
- **Noun-to-verb inventions.** "X-acʿneluʿ"-shaped creations
  built off a real noun stem but not actually a real verb.
- **Awkward causatives.** Causative `-acʿnel` / `-ecʿnel`
  forms that don't exist for the base verb, or that exist but
  sound off in the age-4–7 register.
- **Suspicious poetic coinages.** Compounds, participles, or
  metaphor-coined nouns that look fluent but don't actually
  belong to the language. Often surface in T1 sentence 1 and
  T3 closer.
- **Nonstandard forms** unless clearly acceptable (regional /
  child-speech registers that a 4–7-year-old would naturally
  encounter; classical-storyteller registers when intentionally
  invoked for a fairy-tale opener).

**Illustrative examples from current evidence** (not exhaustive
— the failure pattern is the rule, the tokens below are just
the cases we've already seen):

- `բոցերում էր` (OAI v3.2.3 PA)
- `ցուցանի` (OAI v3.2.3 PA)
- `անթել` (OAI v3.2.3 PA)
- `փայլացնում` (OAI v3.2.3 PA)
- `Խտնված` (OAI v3.2.2)

Any new instance of the same shape is a fail.

---

## 5. Semantic / native-ear checklist

Second-highest priority. Plan D exposed this failure mode: a
model that writes beautiful Armenian fairy-tale prose can still
get an animal's anatomy wrong, use a bird-sound verb for a
stream, or fold an ungrammatical middle clause into a literary
sentence. Home/Play exposed a small variant: a story-state
contradiction (`Մոմոյին դրեց բարձի կողքին` when the pillow has
not yet been found).

Scan for:

- **Body parts match characters.** Birds have `կտուց` and
  `թևիկներ`. Lizards have `ոտքեր` / `թաթեր`. Dolls have hands
  and eyes (in child-play register), no ears that "fold" in any
  expressive sense. Humans have `մատներ` for grasping, `աչքեր`
  for seeing, `ոտքեր` for walking.
- **Animal actions fit the animal.** Cats / dogs don't `կաչկաչ`;
  streams / water don't `կռկռում`; ducks don't fold their ears
  as a mood signal. Verbs of the wrong sound family for the
  noun in question are a near-fail.
- **Object actions are physically possible** unless clearly
  framed as pretend-play. A pillow doesn't fly; a doll doesn't
  walk on its own; a blanket doesn't speak in human words —
  unless the sentence frames it as `Նարեն երևակայեց, որ…`,
  `ասես…`, `կարծես…`, or `Նարեն խաղալով պատմեց…`. Without that
  frame, an impossible action is a semantic fail.
- **Metaphors do not become physically confusing.** "The water
  voice said everything" (`ջրի ձայնն ինքն էր ասում ամեն ինչ`)
  is beautiful on the page and obscure for a 4-year-old. If a
  child would have to ask "what does that mean," the metaphor
  has overstepped.
- **Story-state contradictions.** An object is not "found", "in
  place", or "next to X" until the story has actually placed it
  there.

**Illustrative examples from current evidence** (not exhaustive):

- `բադիկի ականջները ճկված` (CLA-app-PD)
- `մողեսը թևը դնի` — lizard offered a `թև` in T1 CHOICE_B
  (CLA-app-PD)
- `կռկռում է` used for an awakening stream / water (CLA-app-PD)
- `Մոմոյին դրեց բարձի կողքին` in T2 when the pillow has not yet
  been found at that point (CLA-app-Home-Play)

---

## 6. Register checklist

Areg's voice is warm storyteller, not therapist, not teacher,
not AI assistant, not baby. The register has to match the
scenario without drifting into either over-poetic or
over-pedagogical territory.

Check the sample for:

- **Avoid overly literary density for ages 4–7.** Three poetic
  abstractions in a single 3–5-sentence turn is too many.
  `քարերը փայփայված էին ձյունով`-style poetic-density lines
  belong (sparingly) in PD-style fairy-tale captures, not in
  Home/Play or Calm.
- **Avoid too much metaphor.** One soft image per turn is the
  ceiling for ages 4–7. A second metaphor in the same turn is a
  register fail.
- **Prefer clear familiar Armenian.** Tangible, age-anchored
  nouns (`բարձ`, `ծածկոց`, `լամպ`, `գորգ`) over abstract ones
  (`հիշողություն`, `հանգստություն`, `զգացողություն`) — unless
  the scenario is explicitly Calm and the abstract is the point.
- **Avoid direct moralizing.** No `տեսնում ե՞ս, Մոմո, երբ սիրով
  օգնում ես…`, no `սովորեցինք, որ…`, no aphorisms about
  patience / kindness / friendship dropped in the T3 closer.
  Show the resolution through an action, not a stated lesson.
- **Avoid therapist / teacher / AI / baby tone.** Areg is a
  storyteller. Not "how does that make you feel?" Not "today
  we'll learn that..." Not the consumer-LLM "Hope this helps!"
  closing. Not over-soft baby register.
- **Avoid generic opener overuse.** Unless intentionally allowed
  for a specific fairy-tale scenario, T1 sentence 1 must not
  start with `Մի անգամ…`, `Մի օր…`, `Կար ու չկար…`, or any
  classical hekiat opener. Anchor T1 sentence 1 on the place
  stem instead. Also flag mid-paragraph slips: `Մի օր,` as
  T1 sentence 6 is still an opener-slip fail (see OAI v3.2.3
  PA).

---

## 7. Choice checklist

Each `CHOICE_A` / `CHOICE_B` pair must satisfy:

- **Exactly two choices.** No three-choice variant, no implicit
  "or just keep listening" third option.
- **Concrete physical actions.** Something the hero physically
  does next (`Նարեն ծածկոցը բարձրացնի…`), not an opinion
  question, not an emotion question, not a metacognition
  question.
- **A child age 4–7 can understand and imagine the action.** If
  a 5-year-old would have to ask the parent what the choice
  means, the choice has failed.
- **The two choices are distinct.** Not two phrasings of the
  same action. Not "search under the bed" + "look under the
  bed."
- **Neither choice asks for feelings / thoughts.** No "What
  does Նարե feel right now?" / "What does she think Մոմո
  needs?" / "What's the right thing to do?"
- **Both choices match the current story state.** The action
  has to be possible given what has and has not happened in the
  story so far. If T2 has not yet shown a key, T2's CHOICE_A
  cannot be "use the key on the door."
- **Parser-friendly format.** No emoji, no `Հիմա ի՞նչ անի…`-
  style consumer-app phrasing, no decorative wrapping. Just the
  text of the physical action, on the line directly after the
  `CHOICE_A:` / `CHOICE_B:` label.

---

## 8. Say-aloud checklist

Read the sample aloud (silently or out loud) and check:

- **Would this sound natural if spoken by a toy?** Or does it
  feel like written prose that doesn't translate to speech?
- **Are sentences too long?** A toy speaking 30+ words without
  a comma is a pacing fail. Break at natural breath points.
- **Are there tongue-twister or awkward sound combinations?**
  Consonant clusters that are fine on the page but hard to say
  cleanly aloud. Quick alternation between similar-sounding
  syllables.
- **Are sound effects safe / clear aloud?** `տտ, տտ, տտ`-style
  effects that look fine on the page may be ambiguous spoken
  (clock? knock? heart?). Either the effect is unambiguous in
  context or it doesn't earn the line.
- **Is there anything a parent listening from another room
  would notice as odd?** Fake-Armenian words trip native ears
  instantly even at low volume. So do animal-anatomy mismatches
  and any register drift.

---

## 9. Scenario-specific checks

Some checks are scenario-conditional. Apply on top of the core
rubric.

### 9.1 Fairy-tale / magical scenario (PA, PD, hedgehog)

- Classical opener is allowed only if the scenario explicitly
  permits it; otherwise § 6 opener rule applies.
- Poetic density tolerance is higher than in Home/Play, but not
  unbounded — one image per turn is still the ceiling for a
  4–7-year-old.
- Animal characters trigger the § 5 animal-anatomy check at
  full force.
- "Magical object" affordances must remain inside the scenario
  brief (the brief specifies what is magical; the model does
  not add new magical objects mid-story).
- Aphorism / lesson closer is still forbidden.

### 9.2 Everyday Home/Play scenario (PE)

- **Magical level is the key check.** Pretend-play register is
  acceptable; literal magic is a register fail. A doll being
  "sleepy" as child-play is fine; a doll walking on its own is
  not.
- Vocabulary should stay tangible and home-anchored
  (§ 6 "prefer clear familiar Armenian").
- T1 sentence 1 must be place-stem-anchored, not classical-
  opener-anchored.
- Hero usually human; if any non-human is a sub-character, the
  § 5 animal-anatomy check applies.

### 9.3 Calm / bedtime scenario

- Slower pacing OK; sentence length budget is at the higher end
  of the 3–5 band.
- Abstract / soft imagery has a slightly higher tolerance than
  in Home/Play — but not into the PD literary register.
- No choices that re-energize the child (no "let's go for a
  walk" / "let's play tag" in T3 closer).
- Calm samples have no Calm-mode controlled capture yet in the
  evidence set; this section is provisional. Update after the
  Calm capture lands.

### 9.4 Curiosity / story hybrid scenario

- Curiosity-mode interjections (one quick real-world fact)
  should not derail the 3–5-sentence-per-turn budget.
- The fact must be true, not a folkloric / mythological claim
  presented as fact.
- Return to story register cleanly after the curiosity beat;
  no AI-assistant register leakage on the way back in.
- No Curiosity/story hybrid sample in the evidence set yet;
  also provisional.

---

## 10. Reviewer workflow

Strict order. Do not skip steps; do not interleave editing with
reading.

1. **Read raw output once, end to end, without editing.** Just
   absorb. Do not annotate yet, do not score yet. This pass is
   for first-impression naturalness.
2. **Mark obvious fail reasons.** Re-read with a pen. Underline
   any fake-Armenian-looking token, any animal-anatomy
   mismatch, any moralizing closer, any choice that asks a
   feeling. Don't try to score yet — just mark.
3. **Score the rubric.** Fill § 3 axes 1–11 with the per-axis
   0–5 / PASS / FAIL marks. Quote at least one example for
   anything scored < 4 / 5 or non-PASS.
4. **Highlight exact Armenian phrases causing concern.** Copy
   them verbatim into the reviewer note. Do not paraphrase.
   Do not "fix" the spelling first.
5. **Decide say-aloud verdict** (§ 2) for the whole sample.
   PASS / PASS WITH SMALL EDITS / BORDERLINE / FAIL. One verdict
   per capture.
6. **Do not silently correct the raw evidence.** The raw output
   stays exactly as captured, even if the reviewer is confident
   they could fix a typo. The raw block is the data point.
7. **If editing is needed, record it as a separate "suggested
   fix" block** inside the reviewer note. Use a clear heading
   like *"Suggested fix (NOT applied to the raw capture)"* so a
   future reader cannot mistake the suggestion for the captured
   output.

---

## 11. Decision rule

The native review unlocks subsequent steps; it does not in
itself authorize a provider switch or any production change.

- **No provider switch based on one good sample.** Even a
  unanimous PASS on one scenario is one data point. The
  comparison plan § 6 thresholds require multi-scenario
  evidence (PA + PD + PE + at least one Calm) on both sides.
- **No ChatService integration until multiple scenarios pass
  native review** for the candidate provider, AND the
  comparison-plan thresholds are cleared.
- **Native review must pass before any production-integration
  design** is written. The integration design is a *document*
  preceded by review evidence; no code change before the
  document; no code change after the document without a second
  explicit GO.
- **Claude.app quality does not automatically equal Claude API
  quality.** Every Claude data point to date is consumer-app,
  not API. App captures bound the *ceiling* of Claude's prose;
  production deploy would consume API output under the Areg
  system prompt. The gap is unknown until at least one Claude
  API controlled capture lands in the matrix.
- **OpenAI structural pass does not override Armenian
  naturalness fail.** v3.2.x has hit a clear ceiling on
  morphology even when hard rules pass cleanly. A structurally
  perfect run with one coined word is still BORDERLINE at best.
- **Hedgehog / consumer-app strong samples are ceiling
  signals, not decision evidence.** They are useful to know
  how good Claude *can* sound, not how good Claude *will*
  sound when wired into Areg's runtime.

---

## 12. Scope guard

Authoring this checklist touched no production / runtime files:
`ChatService`, backend code, frontend, `appsettings*.json`,
`*.csproj`, tests, seed bank, name bank, story-plan generator,
validator, runtime system prompts (production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched; Claude API was not used. The only artifact is this
markdown under `tools/StoryModelBakeoff/evaluations/`.

This document does not authorize a provider switch, a code
change, a paid run, or any production action. It is a reusable
review reference imported by future capture-result evaluator
notes.

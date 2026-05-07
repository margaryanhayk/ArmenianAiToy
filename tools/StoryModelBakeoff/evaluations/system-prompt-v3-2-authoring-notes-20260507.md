# system-prompt-v3-2.txt — authoring notes (2026-05-07)

**Status: design / authoring only.** No production / runtime change. **No API calls were run by this slice.** No edits to `ChatService`, backend, frontend, runtime prompts, provider settings, `appsettings*.json`, `*.csproj`, tests, seed bank, character name bank, story-plan generator, validator, `Program.cs`, README, speech / TTS / STT. No edits to `tools/StoryModelBakeoff/system-prompt-v3-1.txt`. No edits to `tools/StoryModelBakeoff/bakeoff-prompts-v3-1.json`. The v3.1 system prompt and the v3.1 scenarios remain the historical baseline against which v3.2 is to be measured. Companion to the v3.2 design plan committed at `f09ca92`.

This is Slice 1 of the v3.2 plan (`tools/StoryModelBakeoff/evaluations/v3-2-prompt-tightening-plan-20260507.md` § 7). Slice 1 authors `system-prompt-v3-2.txt` and these notes; it does NOT run any paid API call. Slices 2–5 (dry-run smoke, paid mp1, paid mp2, Claude comparison) are separate later tasks.

---

## 1. Purpose

The v3.1 system prompt has been exercised against `gpt-4o` in two paid runs (`14731b3` mp1, `fcffafe` mp2). Engineering passed both; story quality surfaced four NEW defect categories (cross-language leak, fabricated morphology, schwa-stem violation, abstract sentimental coda) plus two pre-existing defects (short closure, Plan A resolution seam). The v3.2 design plan (`f09ca92`) prescribed six rule blocks (R1–R6) — each with bad/good examples and placement decisions. This file captures the **smallest text-level delta** that lifts those rules into a real bake-off system-prompt file, ready for Slice 2 (no-network dry-run) and Slice 3 (paid mp1 smoke).

The v3.2 file lives only inside `tools/StoryModelBakeoff/`. It does not go anywhere near the production runtime system prompt at `backend/src/ArmenianAiToy.Api/system-prompt.txt` (or wherever the runtime prompt lives — the bake-off path is fully isolated by design).

---

## 2. What changed from v3.1 to v3.2

The change set follows § 6 of the design plan exactly. Every v3.1 rule is preserved either verbatim or with strict-superset reinforcement; nothing v3.1 enforced is now permitted.

### Replaced (same intent, stronger wording + examples)

- **`ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ`** → tagged `R1 — STRICTER v3.2`. v3.1's one-sentence prohibition becomes a per-token rule with concrete bad-token examples (`shimmering`, `magic`, `okay`, `глаза`, `просто`), a fail-back recipe (preferred Armenian substitutions), and a per-emit self-check. The v3.1 positive style guidance about "warm Armenian grandmother" is preserved verbatim at the bottom of the same block.
- **`ՎԱՅՐԻ ԽԱՐՍԽՄԱՆ ԿԱՆՈՆ` (C16)** → tagged `R3 / C16 — STRICTER v3.2`. The v3.1 wording's `համարժեք հայերեն հոլովում` clause (which the model exploited in mp2 PD T1 to produce `Հին կամրջի վրա…`) is closed by adding an explicit per-letter requirement, a worked good/bad example pair for `հին կամուրջ`, and a pre-emit self-check. The v3.1 forbidden-other-place list (`կանաչ բացատ`, `անտառ`, etc.) is preserved verbatim.
- **Closure-block soft `Թիրախային երկարությունը` target** → folded into a new `R4` subsection inside the closure block, with a hard floor (`առնվազն TARGET_WORDS-ի ստորին թվին`), a self-count instruction before `Վերջ։`, and an explicit list of allowed expansion types (sensory image / character reaction / final place image). The v3.1 closure rules around C9 (no choices on Turn 3) are preserved verbatim.

### Added (wholly new sections, no v3.1 precedent)

- **`ՀՆԱՐՎԱԾ ՁԵՎ ԱՐԳԵԼՈՂ ԿԱՆՈՆ`** → tagged `R2 — NEW v3.2`. Covers `ձայնուֆով`-class fabricated morphology. Includes a one-sentence carve-out clarifying that legitimate Armenian word-form changes (e.g. `ձայնով`, `բարիքի պես`) are still allowed — the rule fires only on tokens the model is uncertain about. The fail-back recipe instructs simpler-known-phrase substitution in case of uncertainty.
- **`ԼՈՒԾՄԱՆ ՆԵՐԿԱՅԱՑՈՒՄ`** → tagged `R6 — NEW v3.2`. Sits between `PLAN ADHERENCE (G — v2)` and the closure block. Requires `plan.resolutionStyle` to be staged across 1–3 short sentences as a concrete on-stage moment, not compressed into one subordinate clause. Also constrains Turn 2 choice block from presupposing the resolution.
- **R5 strengthening** → a new subsection inside `ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ`, banning abstract sentimental codas (`հետագայում միշտ…`, `որպեսզի… տարածվի`). v3.1's three existing C2 bullets are preserved verbatim; R5 is bolted on under a `ՆՈՐ R5` heading.

### Left unchanged (verbatim from v3.1)

- Opening Areg paragraph (Դու Արեգն ես՝...).
- `ԲԱՑՄԱՆ ԿԱՆՈՆ (A — v2)` — forbidden-opener block.
- `ԸՆՏՐՈՒԹՅՈՒՆՆԵՐԻ ՃՇԳՐԻՏ ՁԵՎԱՉԱՓ (B — v2)` — choice-line format.
- `BREAK-GLASS CHOICE BLOCK ԿԱՆՈՆ (C15 — STRICT v3.1)` — held cleanly on both mp1 and mp2 paid runs.
- v3.1's three C2 bullets (banning direct moral lessons, character-mouthpiece aphorisms, patience-axis aphorisms).
- `ՀԱԿԱ-ՄԵՏԱ ԿԱՆՈՆ (C14 — v3.1)` — held cleanly on both runs.
- `ՏԱՐԻՔԱՅԻՆ ՌԻԹՄ ԵՎ ԲԱՌԱՊԱՇԱՐ (D + E — v2)` — age-tone profile guidance.
- `ՇԱՐՈՒՆԱԿՈՒԹՅԱՆ ԿԱՆՈՆ (F — v2)` — first-sentence-performs-SELECTED_CHOICE, no verbatim repeat.
- `PLAN ADHERENCE (G — v2)` — preserve `hero` / `friendOrGuide` / `place` / etc.
- `ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ ԵՎ ՓԱԿՈՒՄ` — bounded-arc rules (load-bearing C9). R4 is bolted on as a `ՆՈՐ R4` subsection at the end of this block; the existing C9 wording is untouched.
- `ԱՆՎՏԱՆԳՈՒԹՅՈՒՆ ԵՎ ՏՈՆ` — safety block.
- `ԵԼՔԻ ՁԵՎԱՉԱՓ` — output format. Turn 3 sub-bullet gains the parenthetical `R4-ով նկարագրված ստորին հատակին հասնող`; otherwise unchanged.

### What was deliberately NOT done

- **Plan A / Plan D JSON is not embedded** in the system prompt. Plan-specific values (PLACE_STEM literal, TARGET_WORDS range, resolutionStyle string, choice strings) remain in the user-turn scenario file `bakeoff-prompts-v3-1.json`. The system prompt only references PLACE_STEM as a concept, with **one** worked example (`հին կամուրջ`) inside the R3 block for clarity — which is itself the plan-D place. This keeps v3.2 reusable across any future plan in the same scenario set without a system-prompt edit.
- **No new gate (C17, C18, …) was introduced.** The existing gates C1–C16 are sufficient to score the new defects: defect A folds into C14-style language correctness; defect B is a new pass/fail call inside the language-correctness family; defect C tightens C16; defect D is C13; defect E tightens C2; defect F is a new C-style call but does not need its own number to be observable.
- **No edits to v3.1 file.** v3.1 is the historical baseline.

---

## 3. Defect → rule mapping (one row per OpenAI mp1 / mp2 finding)

| OpenAI evidence | Defect family | v3.2 rule | Where in file |
|---|---|---|---|
| mp2 PA T3 — `shimmering` inside Armenian narrative | A — cross-language leak | R1 | `ԲԱՑԱՐՁԱԿ ԼԵԶՎԱԿԱՆ ԿԱՆՈՆ (R1 — STRICTER v3.2)` |
| mp2 PD T3 — non-word `ձայնուֆով` | B — fabricated morphology | R2 | `ՀՆԱՐՎԱԾ ՁԵՎ ԱՐԳԵԼՈՂ ԿԱՆՈՆ (R2 — NEW v3.2)` |
| mp2 PA T2 — non-word `բարենի` | B — fabricated morphology | R2 | same |
| mp2 PD T1 — `Հին կամրջի վրա…` (schwa-dropped form) | C — PLACE_STEM violation | R3 | `ՎԱՅՐԻ ԽԱՐՍԽՄԱՆ ԿԱՆՈՆ (R3 / C16 — STRICTER v3.2)` |
| mp1 PA T3 — body ≈ 52w / 70-100 floor | D — short closure | R4 | `ՍԱՀՄԱՆԱՓԱԿ ԱՐԿ ԵՎ ՓԱԿՈՒՄ` → `ՆՈՐ R4` subsection |
| mp2 PA T3 — body ≈ 55w / 70-100 floor | D — short closure | R4 | same |
| mp2 PD T3 — body ≈ 50w / 100-130 floor | D — short closure (worst) | R4 | same |
| mp1 PA T3 — `տաքությունն ու բարությունը տարածելու համար` coda | E — abstract sentimental coda | R5 | `ՀԱԿԱ-ԲԱՐՈՅԱԽՈՍԱԿԱՆ ԿԱՆՈՆ (C2 + R5 — STRICTER v3.2)` |
| mp2 PA T3 — gift compressed into one subordinate clause | F — resolution asserted not staged | R6 | `ԼՈՒԾՄԱՆ ՆԵՐԿԱՅԱՑՈՒՄ (R6 — NEW v3.2)` |
| mp1 + mp2 PA T2 — choice Բ presupposes stork is going home | F — Turn-2 choice-block presupposition | R6 (final bullet) | same |

Every observed defect now has a corresponding rule. Slice 3 (paid mp1 v3.2) is the validation gate.

---

## 4. Risks

- **Prompt length grew.** v3.2 is noticeably longer than v3.1 (R1 went from 4 lines to ~14; R3 from 9 lines to ~18; R4 added ~16 new lines; R6 added ~12 new lines; R2 added ~13 new lines; R5 added ~10 new lines). Every paid call now ships a longer prompt and pays slightly more in input tokens. The mp1 + mp2 v3.1 input was ~10.6k tokens / 3 turns — v3.2 expected to add ~300–500 input tokens per turn. **Acceptable.**
- **Native Armenian typo risk.** The new rule blocks were drafted by an LLM (this session), then placed verbatim into a system prompt that another LLM consumes. A typo in the rules can echo into the model's output (it learns the typo as a "valid form"). Section 6 below is the **mandatory** Armenian review checklist before any paid run.
- **R2 over-correction.** A blanket "no invented morphology" reading could chill correct compound forms. Mitigation in the rule: an explicit carve-out paragraph stating that known forms (`ձայնով`, `բարիքի պես`) are allowed — the rule targets `uncertainty`, not "any unusual suffix."
- **R4 expansion → padding.** Forcing a body to reach the floor when the natural arc is already complete may produce filler. Mitigation in the rule: expansion must come from concrete sensory / character-reaction / place-image beats, not summary or moral. R6 (staged resolution) is expected to absorb some of the floor naturally.
- **R5 banning `հետագայում` / `միշտ` framings** could feel restrictive on `age-7-richer` mood. Mitigation in the rule: the ban targets the **last sentence** specifically; rest of body may still carry general framing where it serves the story.
- **R3 `հին կամուրջ` example bleed into other plans.** R3 includes a worked good/bad example pair for `հին կամուրջ` (the Plan D place). For any future plan whose `place` happens to include the bigram `ու` in a syncopation-prone position, the example is directly relevant. For other plans (e.g. Plan A's `խնձորենու այգի`, where `ու` is part of the genitive `-ու` ending and is not subject to syncopation), the example is harmless background. **Acceptable.**
- **Provider-shape risk.** The OpenAI `messages` array is appended in order: system prompt → user-turn 1 / assistant-turn 1 / user-turn 2 / … . The bake-off runner uses the same shape for both Claude and OpenAI. v3.2 does not change shape; it only enlarges the system message text. No runner code change required.
- **Regression risk on previously-passing gates.** Two paid runs of v3.1 passed C1, C3, C6, C8a, C9, C14, C15 cleanly. v3.2 must not regress any of these. The dry-run smoke (Slice 2) cannot test this — only Slice 3 (paid mp1) can. Mitigation: every v3.1 rule whose gate passed cleanly is preserved verbatim in v3.2; only new and replaced sections were edited. The probability of a verbatim-preserved rule's gate suddenly failing is low.

---

## 5. Confirmation

- **No API calls were run by this slice.** The runner was not invoked with `--run`. No paid OpenAI call. No paid Anthropic call. No live model interaction.
- **No production / runtime change.** Files outside `tools/StoryModelBakeoff/` are untouched. `system-prompt-v3-1.txt`, `bakeoff-prompts-v3-1.json`, and every previously committed evaluation file are untouched. Two new files only: `tools/StoryModelBakeoff/system-prompt-v3-2.txt` and `tools/StoryModelBakeoff/evaluations/system-prompt-v3-2-authoring-notes-20260507.md`.
- **No secrets.** No `OPENAI_API_KEY`, no `ANTHROPIC_API_KEY`, no token, no JWT, no email, no parent / device id appears anywhere in either new file. The OPENAI key from the earlier paid runs was used for those runs only and was confirmed present (length only, value never printed).

---

## 6. Native Armenian review checklist (BEFORE any paid run)

Slice 3 (paid mp1 v3.2) MUST NOT fire until each of these has been confirmed by a native Eastern Armenian reader (operator):

- [ ] Every Armenian word in `system-prompt-v3-2.txt` is a real Eastern Armenian word in a recognized form. No `(չկա)` / `(չի կա)`-class typos in the rules themselves.
- [ ] R1 rule reads as Armenian-only enforcement, not as an English-only ban with Armenian exceptions. The list of forbidden tokens (`shimmering`, `magic`, `okay`, `глаза`, `просто`) is intelligible as **examples**, not as a complete blacklist.
- [ ] R2 rule's carve-out paragraph (`Հայտնի և ճիշտ ձևերը (օր.՝ «ձայնով», «բարիքի պես») թույլատրված են։`) is unambiguous — a reader should NOT come away thinking suffixes-in-general are forbidden.
- [ ] R3 rule's `հին կամուրջ` worked example correctly characterizes `Հին կամրջի…` as schwa-dropped (i.e., as the forbidden form) — a native reader should not suspect the example pair is reversed.
- [ ] R4 rule's "self-count" instruction reads as a request to the model, not as a request to the operator. (`ինքդ հաշվիր` is addressed to the model.)
- [ ] R5 rule's last-sentence ban (`«հետագայում», «միշտ», «որպեսզի...», «սովորեցին որ...»`) is correctly positioned: only the **last sentence** is constrained, not the whole body.
- [ ] R6 rule's `1-ից 3 կարճ նախադասությամբ` reads as a range, not as an exact requirement.
- [ ] No accidentally-pasted English / Latin characters appear in any Armenian-text section of the system prompt (other than the deliberate forbidden-token examples in R1's bullet list, which are quoted in `«…»` and clearly framed as forbidden inputs).
- [ ] Punctuation is Armenian-style where the model is supposed to mimic it: `։` for sentence-final, `՝` for the topic-comment separator, `«…»` for quotations.
- [ ] No instance of `ՉՎերջացնել` / `ՉԵՐՋԱՑՆԵԼ` / `ՉՎերջացնել` mixing — the prompt should use ONE consistent form (`ՉՎերջացնել` is the form preserved from v3.1; v3.2 must not introduce a competing form).

If ANY checklist item fails, fix `system-prompt-v3-2.txt` in a separate text-only commit before invoking Slice 2 (dry-run) or Slice 3 (paid mp1).

---

## 7. Out of scope for this slice

- No `bakeoff-prompts-v3-2.json` — v3.1 scenarios are reused unchanged.
- No new evaluator script. The defect-categorization is operator-side reading, same as for the mp1 / mp2 evidence files.
- No invocation of any subagent or live model. The Armenian review in § 6 is operator-side.
- No commit / push from this slice. The two new files stay untracked at the close of this turn for the operator to review and commit separately.
- No edits to runtime files. CLAUDE.md, `system-prompt.txt` in production, ChatService — all untouched.

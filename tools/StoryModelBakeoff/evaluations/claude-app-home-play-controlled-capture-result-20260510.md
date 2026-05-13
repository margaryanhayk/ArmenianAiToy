# Claude.app — Home/Play controlled capture result — 2026-05-10

**Status:** evidence only. No code change, no paid API call, no
backend run, no Claude API use, no production change, no provider
switch authorized by this document. This file records the raw
manual Claude.app capture and a first-pass evaluator note for the
child-natural everyday Home/Play scenario.

---

## 1. Context

- **Capture type:** manual Claude.app capture (claude.ai consumer
  app, NOT Claude API). No paid API call was made; no backend was
  started; no provider configuration was touched.
- **Prompt source:**
  `tools/StoryModelBakeoff/evaluations/claude-app-home-play-controlled-capture-prompt-20260510.md`
- **Fixed path:** **A → B** (Turn 2 continues after Turn 1
  `CHOICE_A`; Turn 3 continues after Turn 2 `CHOICE_B`).
- **Scenario:** child-natural home/play setting — Նարե (≈ 5 y.o.)
  in her own bedroom in the evening, doll Մոմո, missing little
  pillow (Մոմոյի փոքրիկ բարձը կորել է). Newly authored;
  deliberately un-magical and deliberately familiar.
- **Purpose:** evidence for the story-brain controlled comparison
  set, on a scenario that deliberately avoids forest / magical-
  object / animal-hero registers so practical-conversational-
  Armenian behavior can be observed.
- **No runtime decision** is made by this file. It is one sample;
  next steps are documentation only.

## 2. Raw captured output

The block below is the literal Claude.app reply, copied verbatim.
No trim, no normalize, no punctuation fixes.

```
TURN_1_STORY:
Նարեի սենյակում լամպը վառում էր մեղմ, դեղնավուն լույս։ Նարեն Մոմոյին բռնեց ձեռքերով ու սեղմեց կրծքին. Մոմոն հոգնած էր, աչքերը ծանր էին։ Բայց Նարեն նկատեց. Մոմոյի փոքրիկ բարձը չկար մահճակալի վրա։ Նա նայեց ամեն կողմ, բայց բարձը տեղում չէր։
CHOICE_A:
Նարեն ծածկոցը բարձրացնի ու Մոմոյի բարձը մահճակալի տակ փնտրի։
CHOICE_B:
Նարեն դարակի մոտ գնա ու այնտեղ նայի, արդյոք բարձն ընկել է ետևը։

TURN_2_STORY:
Նարեն ծածկոցը հետ քաշեց ու ձեռքը մահճակալի տակ մտցրեց։ Մատները շոշափեցին գորգը, հետո գրքի անկյունը, հետո... ոչինչ։ Բարձը մահճակալի տակ չէր։ Նարեն ելավ ու Մոմոյին դրեց բարձի կողքին, ասես Մոմոն սպասում էր։
CHOICE_A:
Նարեն գնա գորգի վրա նստի ու փափուկ խաղալիքների կույտի մեջ բարձը փնտրի։
CHOICE_B:
Նարեն դարակի ստորին հարկում նայի, որտեղ գրքերն ու փոքրիկ իրերն են դրված։

TURN_3_STORY:
Նարեն դարակի ստորին հարկում ձեռքը մտցրեց, ու մատների ծայրին զգաց մի փափուկ, փոքրիկ բան. Մոմոյի բարձն էր, ծալված ու մի փոքր հարթված։ Նա վերցրեց ու Մոմոյին դրեց մահճակալի վրա, բարձը՝ գլխի տակ։ Մոմոն, կարծես, հանգստացավ։
CHOICE_A:
Նարեն լամպը հանգցնի ու ծածկոցի տակ սահի քնելու։
CHOICE_B:
Նարեն դարակից մի գիրք վերցնի ու Մոմոյին կողքին պառկած մի կարճ պատմություն կարդա։
```

## 3. Rubric

First-pass non-native operator scoring. Final answer comes from
the native Armenian review pass per the comparison plan § 6.

| Dimension                              | Score / Verdict          |
|----------------------------------------|--------------------------|
| Armenian naturalness                   | 4.5 / 5                  |
| Eastern Armenian correctness           | 4.5 / 5                  |
| Home/play naturalness                  | 4.5 / 5                  |
| Warmth for age 4–7                     | 4.5 / 5                  |
| Length / pacing                        | 5 / 5                    |
| Choice quality                         | 4.5 / 5                  |
| Continuation coherence                 | 4.5 / 5                  |
| Format stability                       | PASS                     |
| Fake Armenian / morphology             | PASS                     |
| Semantic / body-part sanity            | PASS with small note     |
| Safety / age appropriateness           | PASS                     |
| Would I let Areg say this aloud?       | **YES**                  |

## 4. Strengths

- Cleanest controlled Claude result so far for practical Areg
  say-aloud use.
- Very good short spoken length — each turn fits the 3–5
  sentence brief; no run-on paragraphs.
- Natural everyday home vocabulary: `լամպ`, `մահճակալ`,
  `ծածկոց`, `դարակ`, `գորգ`, `բարձ`, `գիրք`, `փափուկ
  խաղալիքներ` — visible, tangible, age-anchored.
- No fake Armenian / no invented morphology spotted in the
  first-pass scan.
- No animal / anatomy mismatch (carry-over of the Plan D
  hedgehog-anatomy failure mode does not recur).
- No English / Latin / meta-leakage; no "Here is the story"
  / "Hope this helps"-style commentary.
- Correct fixed path **A → B** honored: Turn 2 follows Turn 1
  `CHOICE_A` (search under the bed), Turn 3 follows Turn 2
  `CHOICE_B` (search the lower shelf).
- All six choices are concrete physical actions a 5-year-old
  could plausibly do in her bedroom; no slip into opinion /
  feeling / metacognition.
- Safe and warm for ages 4–7. No fear, no violence, no medical /
  body-anxiety register.
- No moralizing closer in Turn 3 — the pillow is found and Մոմո
  is settled without a "տեսնում ե՞ս, Մոմո, երբ սիրով օգնում ես…"
  / "սովորեցինք, որ…" lesson tail.

## 5. Weaknesses

- Slightly plain. Less memorable than the earlier hedgehog
  fairy-tale Claude sample — this is the deliberate tradeoff of
  pinning the register to *everyday* and stripping magical
  affordances, but worth naming.
- **Minor Turn 2 coherence wrinkle:** `Նարեն ելավ ու Մոմոյին
  դրեց բարձի կողքին, ասես Մոմոն սպասում էր։` — placing Մոմո
  "next to the pillow" is confusing here because the pillow has
  **not** been found yet at this point in the story. Likely
  intended sense is "next to the *spot* where the pillow should
  go" or "next to her own pillow." Not a fake-Armenian issue;
  a story-logic micro-glitch.
- Not a "premium magical" story, by design — useful evidence for
  Home/Play mode but not a counter to the Plan A magical-register
  sample.
- Shows that simple / familiar scenarios reduce Claude's semantic
  risk surface (no animal anatomy, no levitating objects, no
  golden-leaf metaphor density) — useful insight, not a defect.

## 6. Architectural conclusion (first-pass, non-binding)

- The Claude Home/Play controlled capture is the best practical
  *say-aloud-now* controlled sample observed so far across the
  PA / PD / Home-Play set.
- Claude appears **lower-risk than OpenAI v3.2.3** for fake-
  Armenian / invented-morphology incidents on this sample. One
  sample is not a verdict — see § 7.
- Claude performs noticeably better when the prompt explicitly
  constrains the register to simple everyday Armenian and
  explicitly forbids poetic / metaphor-dense moves (rules 3, 4,
  and 11 of the prompt were load-bearing).
- **Still no provider switch.** Claude.app sample ≠ Claude API
  behavior, and one sample ≠ a provider decision. Production
  remains OpenAI v3.2.3.
- **Still no `ChatService` change.** Nothing in this evidence
  argues for a runtime modification.
- Next decision should be a **small summary update** (see § 7),
  not production integration.

## 7. Next safe step

1. Commit this evidence file **after operator review**. Single-
   file commit, message style `docs(story): …` to match the
   capture-prep / PA / PD commit cadence.
2. After commit, author a short addendum to the story-brain
   findings summary
   (`tools/StoryModelBakeoff/evaluations/story-brain-findings-summary-20260510.md`)
   that adds Home/Play to the comparison set and revises the
   per-scenario observations. Separate slice, separate commit.
3. **Do not touch production.** No `ChatService` edit, no
   `system-prompts/*` edit, no `appsettings*.json` edit, no
   provider config edit. The native Armenian review pass per the
   comparison plan § 6 is the gate that would even put a
   production decision on the table; that has not run yet.

---

## 8. Scope guard

Authoring this evidence file touched no production / runtime
files: `ChatService`, backend code, frontend, `appsettings*.json`,
`*.csproj`, tests, seed bank, name bank, story-plan generator,
validator, runtime system prompts (production sha
`54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`
unchanged), speech / TTS / STT — all unchanged. No paid API call
was made; no backend was started; no provider configuration was
touched; Claude API was not used. The capture itself was a free
human-driven action in the Claude.app browser session.

The only artifact is this markdown under
`tools/StoryModelBakeoff/evaluations/`.

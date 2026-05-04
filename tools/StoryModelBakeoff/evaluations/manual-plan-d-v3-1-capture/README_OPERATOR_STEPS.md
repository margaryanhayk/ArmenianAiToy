# Plan D v3.1 — strict three-prompt manual capture (operator steps)

**Purpose:** run the full v3.1 Plan D capture against Claude.app
the right way, with placeholder substitution between turns.
The earlier Plan D capture (commit `8e81a7d`) was a single-prompt
recovery format and is documented as suggestive evidence only.
This package re-runs Plan D under the **original strict
three-prompt § 7 / § 8 / § 9 protocol** so the placeholder-
substitution workflow itself is tested.

This is **manual / operator-driven**. Nothing in this folder
talks to any model directly. Nothing here gets executed by
code. The four files are:

- `TURN_1_PROMPT.txt` — full Turn 1 prompt, ready to paste.
- `TURN_2_PROMPT_TEMPLATE.txt` — Turn 2 prompt with one
  `{{TURN_1_OUTPUT}}` placeholder you must fill in.
- `TURN_3_PROMPT_TEMPLATE.txt` — Turn 3 prompt with two
  placeholders (`{{TURN_1_OUTPUT}}` and `{{TURN_2_OUTPUT}}`)
  you must fill in.
- `README_OPERATOR_STEPS.md` (this file) — the steps below.

---

## ⚠️ THREE THINGS YOU MUST NEVER DO

> **NEVER click or answer choices manually inside Claude.app.**
> Claude.app's UI may render `Ա` / `Բ` choice buttons or accept
> a free-form `Ա` reply. If you click a button or type just
> `Ա` / `Բ` to "continue the chat," **the capture is invalid** —
> the model is then running on its own implicit conversation
> state, not on the v3.1 strict per-turn prompt. Throw the chat
> away and start over.

> **NEVER continue the same Claude.app chat by typing only
> `Ա: ...` or `Բ: ...`.** Even if you typed the choice line
> verbatim, that is not the v3.1 protocol. The v3.1 protocol
> requires **the entire next prompt** (with the previous turn's
> raw output substituted into the placeholder) to be pasted
> as a fresh long message. Anything less is a recovery capture,
> not a strict three-prompt capture.

> **NEVER skip the placeholder substitution.** The Turn 2
> prompt template has the literal text `{{TURN_1_OUTPUT}}` in
> it. The Turn 3 prompt template has both `{{TURN_1_OUTPUT}}`
> and `{{TURN_2_OUTPUT}}`. Before pasting, you replace those
> placeholders with the verbatim raw outputs from the prior
> turns (no normalisation, no editing, no dropping the choice
> block). If you paste the template with `{{...}}` still in
> it, the model has no continuity context and the capture is
> invalid.

---

## Step-by-step

### Step 0 — open Claude.app in a new chat

- Use a **fresh, brand-new** Claude.app conversation. Do not
  resume an old one. State leakage from prior chats is one of
  the things the strict protocol is meant to rule out.
- Pick the same Claude.app surface you used for the Plan A
  v3.1 capture (commit `019177c`) so the model surface is
  consistent across captures. As of writing that was the
  consumer Claude.app default.

### Step 1 — Turn 1

1. Open `TURN_1_PROMPT.txt` in a text editor.
2. Select **all** of it (Ctrl+A).
3. Copy (Ctrl+C).
4. Paste into the new Claude.app chat (Ctrl+V).
5. Send.
6. When Claude.app responds, **select the entire response**
   from the first character to the last, including the
   final two choice lines (`Ա: ...` / `Բ: ...`) at the
   bottom.
7. Copy that entire response. This is **TURN 1 RAW**.
8. Save TURN 1 RAW somewhere (a scratch file, or just keep
   it on your clipboard while you do Step 2). You will need
   it twice — once for Step 2, once for Step 3.
9. **Do NOT type anything else in the Claude.app chat.**
   Do not click a choice button. Do not type `Ա` or `Բ`
   alone. The chat from this point on does not matter — you
   will start Turn 2 by pasting an entirely new prompt.

### Step 2 — Turn 2

1. Open `TURN_2_PROMPT_TEMPLATE.txt` in a text editor.
2. Find the line near the bottom that reads:
   ```
   TURN_1_OUTPUT:
   {{TURN_1_OUTPUT}}
   ```
3. Replace the literal `{{TURN_1_OUTPUT}}` line with the
   **verbatim** TURN 1 RAW (the entire Claude.app response
   from Step 1, including the `Ա: ...` / `Բ: ...` choice
   lines at the bottom).
4. Save the file as `TURN_2_PROMPT_FILLED.txt` (or just
   keep it on the clipboard — your call).
5. Select **all** of the filled prompt.
6. Paste into Claude.app — either as a continuation of the
   same chat OR (preferred) into a brand-new chat. Either
   works structurally because the prompt itself contains
   the prior turn's output as `TURN_1_OUTPUT:`. A new chat
   is the more conservative choice and keeps each turn
   prompt as a self-contained unit.
7. Send.
8. When Claude.app responds, copy the entire response,
   including the final `Ա: ...` / `Բ: ...` choice lines.
   This is **TURN 2 RAW**.
9. Save TURN 2 RAW (you will need it for Step 3).

### Step 3 — Turn 3

1. Open `TURN_3_PROMPT_TEMPLATE.txt` in a text editor.
2. Find the two placeholder lines near the bottom:
   ```
   TURN_1_OUTPUT:
   {{TURN_1_OUTPUT}}

   TURN_2_OUTPUT:
   {{TURN_2_OUTPUT}}
   ```
3. Replace `{{TURN_1_OUTPUT}}` with the verbatim TURN 1 RAW
   from Step 1.
4. Replace `{{TURN_2_OUTPUT}}` with the verbatim TURN 2 RAW
   from Step 2.
5. Save the filled prompt.
6. Paste into Claude.app (new chat is fine — same
   self-contained-prompt argument as Step 2).
7. Send.
8. When Claude.app responds, copy the entire response.
   This is **TURN 3 RAW**.
9. Turn 3 should NOT end with `Ա: ...` / `Բ: ...` lines —
   the v3.1 closure rule (C9) forbids them. If you see a
   choice block on Turn 3, the capture has surfaced a real
   v3.1 failure; do not "clean it up" by removing the lines
   yourself. Save the raw output as-is.

### Step 4 — collect and pass to ChatGPT (or back to me)

You now have three raw blobs. Format them like this:

```
TURN 1 RAW:
<paste verbatim Step 1 raw output here, including final Ա: / Բ: lines>

TURN 2 RAW:
<paste verbatim Step 2 raw output here, including final Ա: / Բ: lines>

TURN 3 RAW:
<paste verbatim Step 3 raw output here — should have NO Ա: / Բ: lines>
```

Send that to ChatGPT (or paste back into a fresh Claude Code
session) so the gates can be scored against the existing
capture file at:

```
tools/StoryModelBakeoff/evaluations/writer-prompt-v3-1-plan-d-capture-20260504.md
```

The scoring slice will fill the slots § 10A / § 10B / § 10C
and update § 10d's verdict, paralleling the post-Plan-A flow
from commit `019177c` (with the recovery-capture caveat now
removed).

---

## Quick checklist (use this every run)

Mark each item as you go.

- [ ] Step 0: opened a fresh Claude.app chat (not resumed).
- [ ] Step 1: pasted **all** of `TURN_1_PROMPT.txt` (no edits).
- [ ] Step 1: copied the **entire** Turn 1 response, including
      the final `Ա: ...` / `Բ: ...` lines.
- [ ] Step 2: replaced `{{TURN_1_OUTPUT}}` with the **verbatim**
      Turn 1 raw output (no normalisation).
- [ ] Step 2: pasted the **filled** Turn 2 prompt (no
      placeholder left). Confirmed by Ctrl-F searching the
      pasted text for `{{` — should be 0 matches.
- [ ] Step 3: replaced **both** `{{TURN_1_OUTPUT}}` and
      `{{TURN_2_OUTPUT}}` placeholders with the verbatim
      Turn 1 and Turn 2 raw outputs.
- [ ] Step 3: pasted the **filled** Turn 3 prompt (no
      placeholders left). Same Ctrl-F `{{` check = 0 matches.
- [ ] Throughout: **never** typed just `Ա` or `Բ` alone in
      Claude.app to "continue."
- [ ] Throughout: **never** clicked a Claude.app choice
      button.
- [ ] Step 4: collected three raw blobs in the
      `TURN 1 RAW: ... / TURN 2 RAW: ... / TURN 3 RAW: ...`
      format and saved/sent them.

If any checkbox is blank or unsure, **discard the run and
start over**. A discarded chat costs nothing; a recovery
capture costs an entire follow-up slice to redo cleanly.

---

## Final output format (what to send back)

```
TURN 1 RAW:
<paste verbatim Step 1 raw output — opening turn, ends with `Ա: ...` / `Բ: ...`>

TURN 2 RAW:
<paste verbatim Step 2 raw output — second turn, ends with `Ա: ...` / `Բ: ...`>

TURN 3 RAW:
<paste verbatim Step 3 raw output — closure turn, NO `Ա: ...` / `Բ: ...` lines, may end with `Վերջ։`>
```

That's it. Three blobs, in that order, with those three
labels. The next slice will pick up from there.

---

## Out of scope for this folder

- This folder does NOT modify any production / runtime files.
- This folder does NOT modify the existing capture file at
  `tools/StoryModelBakeoff/evaluations/writer-prompt-v3-1-plan-d-capture-20260504.md`.
  That happens in the next slice, after the strict capture
  is in.
- This folder does NOT modify the seed bank, character name
  bank, generator, validator, or README.
- This folder is operator-helper material only. It can be
  deleted at any time after the strict capture lands; it
  has no permanent dependents.

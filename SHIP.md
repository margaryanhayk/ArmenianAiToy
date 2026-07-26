# SHIP.md — Areg v1.0 definition of done

**This file is the finish line.** Nothing else is. If a task is not on this list,
it is v2, and it goes in `docs/v2-backlog.md`.

Only the human edits this file, and only deliberately. Agents read it and audit
against it; they never add to it, and they never fill in a status here.

Mark each: `DONE` / `PARTIAL` / `NOT STARTED` / `UNKNOWN` — and next to each,
the file, test, or evidence document that proves it.

---

## A. The child's experience

- [ ] **A1.** A child can start the toy and hear a full story in Armenian,
      start to finish, with no crash and no English leaking in.
- [ ] **A2.** Every shipped mode passes deterministic Armenian quality checks:
      Armenian-only output, child-safe wording, no Latin/English leakage, and
      length limits.
- [ ] **A3.** Story and Game continuation works: the turn after a choice actually
      follows that choice, verified on at least 10 saved runs per mode.
- [ ] **A4.** Interrupting mid-speech stops the audio within one second and the
      toy responds to what the child said.
- [ ] **A5.** A native Eastern Armenian adult, not you, listens to 10 minutes of
      output and says it sounds natural. Written note from them counts as evidence.
- [ ] **A6.** The toy can choose from at least three cached Armenian stories on SD,
      using `/content_index.json`, without repeating the same story back-to-back.

## B. Safety

- [ ] **B1.** Every response path passes moderation. Proven by a test that fails
      if a path bypasses it.
- [ ] **B2.** A saved adversarial test suite passes scary-topic, secret-keeping,
      and personal-information probes.
- [ ] **B3.** Data retention is documented in one page a parent could read, and
      the code matches it.
- [ ] **B4.** No secret has ever been committed. Verified by a history scan.

## C. The device

- [ ] **C1.** ESP32-S3 completes the full chain: wake → listen → backend → TTS →
      speaker, ten times in a row without a manual reset.
- [ ] **C2.** Device auth works and the legacy sketch's 401 problem is closed or
      the legacy sketch is deleted.
- [ ] **C3.** Wi-Fi drop mid-session recovers without the child needing an adult.
- [ ] **C4.** Audio is loud enough and clear enough in a normal room. Measured,
      not guessed.

## D. Operability

- [ ] **D1.** Backend runs from a clean clone with documented steps, on a machine
      that is not yours.
- [ ] **D2.** Full test suite green, and the count is recorded here: `____`
- [ ] **D3.** Provider cost per hour of play is measured and written down.
- [ ] **D4.** A one-page runbook exists: how to restart it, how to see logs, what
      the three most likely failures look like.

---

## Explicitly NOT in v1.0

**The canonical list lives in `docs/v2-backlog.md`.** Keep it there so there is
only one place to look. Summary of the largest cuts:

- Parent mobile app
- Custom PCB
- Voice cloning / custom TTS voice
- Western Armenian support
- More than the current core modes
- Multi-child profiles
- Manufacturing, packaging, Kickstarter

---

## Open questions for the human

An agent must not resolve these.

1. **`docs/launch-readiness-roadmap.md` also describes a path to launch.** Decide
   which of the two is authoritative; two finish lines is worse than none.

---

## The rule

When fewer than three items block v1.0, stop building and ship it to one real
child. Everything you learn after that is worth more than everything you would
have polished before it.

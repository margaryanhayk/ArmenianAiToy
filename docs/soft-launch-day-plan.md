# Areg — Soft-Launch Day Plan (finish line ①: a few real families)

**Goal:** a handful of friendly families using a real toy (talking to the cloud)
with the phone app, safely. NOT the certified retail product (that's finish
line ②: months + budget). Each "Day" = one focused work session; do them at
your pace.

**Legend:** [YOU] = you do it (accounts/bench) · [ME] = I code it · [BENCH] =
hands-on with the toy at the PC · ⏳ = waiting on an outside approval.

---

## Day 1 — Real "Forgot password" email
- [YOU] Sign up free at resend.com; start verifying a sending domain (or use
  their test sender to start).
- [ME] Build a web-based email connector (works on Railway; SMTP is blocked),
  wire + deploy.
- [YOU] Paste the Resend key into Railway.
- ✅ Done when: "Forgot password?" sends a real email that arrives.

## Day 2 — Put the live server on the newest code
- [ME] Reconcile the deployed branch with the latest code (brings the OTA +
  content-sync + the story-qa safety fix into production), test hard, deploy.
- ✅ Done when: health green, dashboard + chat still work, nothing regressed.

## Day 3 — Start the approvals clock (mostly errands)
- [YOU] Enrol in the Apple Developer Program ($99). ⏳ Approval takes ~1–2 days
  — start it now so it's ready by Day 6.
- [YOU] (optional) Create a Google Cloud project for "Continue with Google".
- [ME] Prep the phone-app build (icons, config, point it at the live server).

## Day 4 — Bench: toy → cloud, part 1  [BENCH]
- [BENCH + ME] Point the toy's firmware at the live cloud URL and add a secure
  (HTTPS/TLS) connection. Flash it. Test a basic conversation over the cloud
  (not your home PC).
- ✅ Done when: you press the toy and it answers via the internet server.

## Day 5 — Bench: toy → cloud, part 2  [BENCH]
- [BENCH + ME] Validate OTA update + story content-sync against the live
  server; fix issues. Confirm a full story plays, pulled from the cloud.
- ✅ Done when: a story downloads + plays from the cloud, end to end.

## Day 6 — iPhone app on your phone  (needs Apple approved ⏳)
- [ME] Cloud-build the app → TestFlight. [YOU] Install it. Test it against the
  live server (login, see toy, conversations).
- ✅ Done when: the real app runs on your iPhone, talking to the live server.

## Day 7 — App sign-in + polish
- [ME] Add Google + "Sign in with Apple" (Apple requires it once Google is on
  iOS). Fix anything found in Day 6 testing.

## Day 8 — Privacy & terms (minimum for real kids' data)
- [ME] Draft a plain privacy policy + terms (what data is stored, parental
  consent, deletion). [YOU] Read it; a quick lawyer check is wise before
  strangers' children use it.

## Day 9 — Full QA sweep
- [ME] Run the multi-agent QA across backend + app + the toy path; fix the
  real bugs it finds. Re-run the automated UI tests.

## Day 10 — Onboard the first families
- [YOU] Hand 2–3 friendly families a toy + the app. Watch a real setup. Collect
  feedback in a simple note.
- Then: iterate on what they hit.

---

## Honest dependencies / risks
- **Days 4–6 need YOU at the PC with the toy** (firmware flashing is hands-on).
- **Day 6 is gated on Apple approval** (start Day 3). If it's slow, do Days 7–9
  software work while waiting.
- **Each family needs a physical toy.** Today there's one bench unit — more
  families = more built units (a small hardware task each).
- This is finish line ①. Selling in stores (② — certification, manufacturing,
  full legal) is a separate, months-long, budget-required phase.

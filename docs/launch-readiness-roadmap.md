# Areg — Launch-Readiness Roadmap

**Created:** 2026-07-26
**Format:** Now / Next / Later, gated
**Basis:** repo state at `Documents/Projects/ArmenianAiToy` — `CLAUDE.md`,
`docs/areg-current-readiness-evaluation.md` (2026-05-18), `docs/deploy.md`,
`mobile/AregParent/README.md`, `.claude/ROADMAP.md`,
`.github/workflows/ci.yml`, and `backend/src/ArmenianAiToy.Api/appsettings.json`
(read directly — config defaults below are verified, not inferred).

**Owner column:** `You` = human/owner task, no code. `Code` = implementable by
the Claude CLI pipeline in-repo. `Both` = code plus a human decision or a
physical verification step.

---

## Status overview

The backend is not the problem. Five modes are bench-ready, dual moderation is
fail-closed and pinned by offline tests, the parent dashboard is feature-complete,
OTA apply is hardware-verified, cloud→SD content sync is hardware-verified, and
`CLAUDE.md` reports ~2,013 tests green. CI runs build + test on every push and PR.

Everything between here and a real child using this is **deployment, legal,
hardware, and store** work — and most of it is yours, not the code's.

| Gate | Meaning | Status |
|---|---|---|
| Gate 0 — bench-ready | Reproducible from clone, tests green, one board works | **Done** |
| Gate 1 — supervised beta | Parent in the room, hosted backend, TLS, legal sign-off | **Not started** |
| Gate 2 — unattended pilot | Real homes, no operator, OTA + alerting live | **Not started** |
| Gate 3 — sellable product | Certified, manufactured, in the app stores | **Not started** |

---

## Corrections to the stale docs

The readiness evaluation is dated 2026-05-18 and is wrong on four counts. Do not
plan against it as written.

| Eval says | Actually |
|---|---|
| P0: "OpenAI cost is uncapped" | Closed. `OpenAI:DailyCostCap.Enabled = true`, `$0.50`/device/day |
| P1: "Device API keys are plaintext at rest" | Closed. Migration `AddDeviceApiKeyHash`, PBKDF2 + lazy upgrade |
| P0: "No Dockerfile… no deploy doc" | Closed. Root `Dockerfile` + `docs/deploy.md` + two validated Windows runbooks |
| "No CI/CD pipeline" | Half-closed. `.github/workflows/ci.yml` builds and tests; there is no **deploy** stage |
| "1,336 tests" | `CLAUDE.md` now reports 2,013 |

Still true and still open: no TLS, no host, no backups, email not delivering, no
alerting, no on-call, `docker build` never actually executed, no enclosure, no
certification, nothing in a store.

---

## NOW — Gate 1: supervised child beta

Committed work. Ordered by dependency, not by preference. Item 1 is the keystone —
five other items are blocked on it.

| # | Item | Owner | Status | Depends on |
|---|---|---|---|---|
| 1 | **Host + domain + TLS.** Pick a target (single VPS, Fly.io, Render), buy a domain, terminate TLS at a reverse proxy (Caddy is least work), forward to plaintext `:8080` | You | Not started | — |
| 2 | **Flip the prod switches.** `Security:RequireHttps=true`, `HstsMaxAgeDays`, `ForwardedHeaders:Enabled=true` + `KnownProxies`, pin `AllowedHosts`, set `Cors:AllowedOrigins` | Code | Not started | 1 |
| 3 | **Validate the container.** `docker build` and `docker run` have never been executed — the slice author had no Docker on the build machine | You | Not started | 1 |
| 4 | **Provision secrets.** `OpenAI__ApiKey`, `Jwt__Keys__0` (must not be the legacy default — startup rejects it), `Devices__ProvisioningSecret`, `Metrics__ScrapeToken` | You | Not started | 1 |
| 5 | **Back up `/data`.** SQLite single-file plus `audio-blobs`. Nothing backs this up today; one bad volume loses every account and transcript | Both | Not started | 1 |
| 6 | **Switch email on.** `Notifications:Transport` is `log` — password-reset and verification mail goes to stdout. A beta parent who forgets their password is permanently locked out | You | Not started | 1 |
| 7 | **Red-team corpus for Armenian adversarial input.** The fail-closed contract is solid; actual block coverage is *unmeasured*. Build the corpus, run it, publish the numbers. This is the child-safety gate | Both | Not started | — |
| 8 | **COPPA / GDPR counsel review.** Compliance is self-attested. Longest lead time on the board — start it in parallel today, not after the code is done | You | Not started | — |
| 9 | **ESP32 over HTTPS.** Firmware is HTTP-only on the bench LAN. A toy on home Wi-Fi with plaintext device keys is not shippable | Code | Not started | 1, 2 |
| 10 | **Repoint the app.** `eas.json` has `http://192.168.1.4:5000` baked in at build time | Code | Not started | 1 |
| 11 | **Verify BLE provisioning on a real device.** `ProvisioningScreen.tsx` is written but unverified; needs an Expo dev build (native module, won't run in Expo Go) | Both | Not started | — |
| 12 | **Per-device credentials for the beta units.** Mint device ids, keys, and claim codes; burn NVS on each board | You | Not started | 4 |

**Gate 1 exit criteria:** HTTPS end to end (backend, app, toy), secrets in a real
store, backups running, password reset delivering to an inbox, red-team numbers on
record, written legal sign-off, and the 40-item manual checklist in the readiness
doc actually checked.

---

## NEXT — Gate 2: unattended pilot in real homes

Planned, scoped, not started. Good confidence in *what*, low confidence in *when*.

| Item | Owner | Why it's here | Depends on |
|---|---|---|---|
| **Voice beyond Story.** C1 voice is Story-only. A parent buys a storyteller, the child asks a riddle, and the toy silently does nothing | Code | Biggest product-honesty gap | Gate 1 |
| **Reliability gate on Whisper + TTS.** `OpenAIReliabilityGate` covers chat only; STT and TTS have no retry or breaker | Code | Voice is the whole product on hardware | — |
| **Turn OTA on.** `FirmwareUpdate:Enabled=false` today. Needs `LatestVersion`, `ImagePath`, real `SigningKey`, and Stage-B TLS for the image fetch | Both | Without OTA every fix needs physical access | 1, 9 |
| **Turn ContentSync on.** `ContentSync:Enabled=false`; pipeline is hardware-verified but serving nothing | Both | Offline stories are the battery/latency story | — |
| **Alerting + SLO dashboard.** Prometheus metrics and histograms already exist and are scrape-ready; nothing consumes them | Both | Unattended means you learn from a graph, not a child | 4 |
| **Deploy stage in CI.** Build and test exist; deploy is manual | Code | — | 1, 3 |
| **Enclosure, battery, power management.** No case, no battery, BOOT button doubles as press-to-talk | You | A bare devkit is not a toy | — |
| **Orphan blob sweeper (C2.3).** Deliberately deferred; `CLAUDE.md` lists "real child voice on real hosts" as a trigger — Gate 1 trips it | Code | Privacy hygiene promise | Gate 1 |
| **Postgres migration decision.** SQLite + WAL is documented as a stopgap and is unvalidated against Postgres | Both | Decide before pilot scale, not during | — |
| **Parent UX session.** Real phone, 3+ devices, 10+ flagged messages. No user testing is on record | You | — | Gate 1 |
| **Per-child profiles in the app.** Endpoints exist; the app has no way to create a child | Code | Siblings share one toy | — |
| **Assistant-audio replay in the app.** `/messages/{id}/audio` exists; dashboard has it, app doesn't | Code | — | — |

---

## LATER — Gate 3: a product you can sell

Directional. Scope and timing flexible, but two of these have lead times measured
in months, so they need decisions well before you need results.

| Item | Owner | Note |
|---|---|---|
| **Certification.** FCC/CE/UKCA radio, toy safety (EN 71 / ASTM F963), battery shipping (UN38.3) | You | Months of lead time and real money. Nothing in the repo mentions it. Scope it early even if you start it late |
| **Manufacturing + provisioning line.** Factory NVS burn station, claim-code printing, QR on the box | You | `CLAUDE.md` already names the factory station as the owner process |
| **App Store + Play Store.** Apple Developer account ($99/yr), icon, splash, store metadata, privacy labels and Data Safety forms | You | Kids-category review is materially stricter — budget for rejections |
| **Armenian voice identity.** TTS is OpenAI `tts-1` / Nova, not an Armenian voice. No voice identity test on record | Both | Product-defining. Evaluate a dedicated Armenian TTS or a licensed voice |
| **Multi-instance backend.** `LoginAttemptThrottle`, `ExportCooldown`, `OperatorSessionStore`, `ChatService.ActiveModes` are all process-local | Code | Blocks horizontal scaling; harmless until you need a second instance |
| **Unit economics.** OpenAI cost per child per day vs. price point; the $0.50 cap is a safety valve, not a model | You | Decide before pricing |
| **Folklore library.** Postponed by owner decision; `anban-huri` is the single recorded exception | You | Requires a new owner decision to expand |
| **ChatService refactor.** 2,347 lines, named as the architectural choke-point | Code | Only worth doing when it actually blocks a change |

---

## Risks and dependencies

**TLS is the keystone.** Items 2, 9, 10, and most of Gate 2 all sit behind item 1.
Until there's a domain and a certificate, nothing else in Now can be finished. Do
this first, this week.

**Legal is the longest pole.** External COPPA/GDPR review is the one item you
cannot compress by working harder, and it gates any child who isn't yours. It runs
in parallel with everything — start it before the infrastructure work, not after.

**Red-teaming is the safety item nobody will chase you about.** Moderation
*infrastructure* being correct and moderation *coverage* being adequate are
different claims. Right now you can only defend the first.

**Two claims in the docs are one-sample-thin.** Benchmark evidence is one run per
day on a noise floor of 1–3 weak cases per 90 turns, and Calm was never live-retested
on the last evaluation day. Treat "quality is good" as provisional.

**Capacity: you are one person.** This roadmap is sequenced deliberately so it can
be executed serially. Resist parallelizing Now — items 1 through 6 are one thread
of work, and item 8 is the only thing that genuinely belongs in the background.

**Docs are drifting.** Four documents, four different phase-numbering schemes, and
a test count off by ~700. Fold a doc-refresh into whatever slice you close next
rather than as its own task.

---

## Deliberately not on this roadmap

Free-form chat, emotional-companion behavior, multi-language output, architecture
redesign, and new folklore titles — all excluded by product decision in `CLAUDE.md`
and `.claude/ROADMAP.md`. Listing them so the omissions read as choices, not gaps.

Mode persistence (`Phase 6`) stays deferred; it needs a schema change and an
explicit approval, and no evidence currently demands it.

---

## Parent sign-in options — checklist (added 2026-07-28)

Goal: let a parent sign in the way that's natural on their device. Best-practice
trio for a consumer kids app is **email/password + Google + Sign in with Apple**.
Rationale and the platform rules are below so the choices read as intentional.

**Where each is used:**

| Method | Web dashboard | iPhone app | Android app | Status today |
|---|---|---|---|---|
| Email + password | ✅ | ✅ | ✅ | **LIVE** (works everywhere) |
| Continue with Google | ✅ | ✅ | ✅ (native) | **Built, turned OFF** (no OAuth client id set) |
| Sign in with Apple | — | ✅ (**required by Apple**) | — | **Not built** |

**Best-practice notes (why this trio):**
- **Email/password** is the baseline — no third-party dependency, works on every
  surface. Already live. Keep it.
- **Google** is the natural choice on Android and web, and works on iPhone too.
  One-time setup in Google Cloud (an OAuth "client id" per platform — web, iOS,
  Android). Free. Enabling it in the backend is a one-config flip (`GoogleAuth:ClientId`);
  the code already exists and is fail-closed when unset.
- **Sign in with Apple is an App Store RULE, not a preference:** if the iPhone app
  offers *any* third-party login (e.g. Google), Apple **requires** "Sign in with
  Apple" to be offered alongside it, or the app is rejected at review. It needs the
  Apple Developer account (the same $99/yr account needed to ship the app at all).
  Not needed on Android or web.

**Checklist (do in this order):**

- [ ] **Enable Google Sign-In (backend + web first).** Create a Google Cloud
      project → OAuth consent screen → a **Web** OAuth client id. Set
      `GoogleAuth__ClientId` in Railway. The "Continue with Google" button then
      appears on the dashboard automatically. `You` (create the id) + `Code` (none —
      it's config).
- [ ] **Add Google client ids for the mobile app** (an **iOS** client id and an
      **Android** client id in the same Google Cloud project) and wire them into
      the Expo app. `Both` — needs the Apple/Google developer accounts + a code
      slice in `mobile/AregParent`.
- [ ] **Add "Sign in with Apple" to the iPhone app** — required once Google is on
      iOS. Enable the capability in the Apple Developer account, add the button +
      token handling in the app, and add an Apple-token verifier on the backend
      (parallel to the existing Google verifier). `Both`.
- [ ] **(Optional) "Sign in with Apple" on web** — only if you want Apple login in
      the browser too; not required. `Both`.

**Dependencies / gotchas:**
- Google and Apple sign-in on mobile both require the **paid Apple Developer
  account** and a **Google Cloud account** — the same accounts already needed to
  publish the app. So this work naturally lands *with* the app-store slice (Gate 3
  above), not before it.
- Backend already has a provider-specific Google verifier; Apple would get its own
  (no generic "external auth" abstraction by design — see `CLAUDE.md` Google sign-in
  section).
- Email verification / password-reset emails need the SMTP provider switched on
  first (deploy checklist / `docs/ADR-001` step 11) — unrelated to social sign-in
  but the other half of "account recovery."

---

## Data storage — move to Postgres (added 2026-07-29)

**Now (beta):** all data (parent accounts with bcrypt-hashed passwords, toys,
children, conversations, audit) is one **SQLite** file on the Railway volume
(`/data/armenian_ai_toy.db`). Fine for a small pilot.

- [ ] **When moving off Railway to a bigger/dedicated production server, migrate
      SQLite → managed PostgreSQL as part of that move.** Owner decision + `Code`.
  - Trigger: real families / public launch, or multiple app instances (SQLite is a
    single file on one machine — it does not survive horizontal scaling).
  - Scope: swap the EF Core provider to Npgsql, point `Database:ConnectionString`
    at the managed Postgres, regenerate/apply migrations, one-time data copy from
    the SQLite file. **Passwords stay bcrypt-hashed — hashing is unchanged.**
  - Options: Railway Postgres (if staying on Railway), or a cloud managed Postgres
    on the new host (AWS RDS / Google Cloud SQL / Supabase / Neon).
  - Roughly an afternoon of careful work; not a rewrite (already built on EF Core).
    Matches the SQLite "stopgap" notes in `CLAUDE.md`.

---

## Toy self-diagnostics → parent warning (added 2026-08-03, owner request)

**Why:** during the first live-cloud bench session the SD card lost power. The
toy went silent — button pressed, nothing happened, no sound, no explanation.
The fault was visible in the serial log within one second (`SD.begin failed`)
but a parent has no serial cable. Silence is the worst possible failure mode
for a children's toy: the parent concludes "it's broken / it's rubbish", and
the actual fault was a loose 5 V wire.

**Principle:** the toy already phones home every ~60 s. Anything it knows about
its own health should ride that heartbeat, and anything a parent can act on
should surface in the parent app — proactively, not on request.

- [ ] **Firmware:** extend the existing heartbeat JSON (already an
      optional-field contract — adding fields is backwards-compatible and old
      firmware keeps working) with a small, bounded health block:
      `sdCardOk`, `sdCardSizeMb`, `micOk`, `speakerOk`, `storiesOnCard`,
      plus `lastErrorCode` for the most recent self-detected fault.
      Bounded enum-ish values only — no free-form strings, no PII, same
      discipline as the metrics tags.
- [ ] **Backend:** persist on `Device` (additive columns + migration, same
      shape as the OTA fields added in `AddDeviceOtaFoundation`), and derive a
      parent-facing verdict at READ time the way `DeviceOtaHealth.Resolve`
      already does — do NOT store a computed status that then goes stale.
- [ ] **Parent surface:** a plain-language banner on the toy's page, e.g.
      «Խաղալիքի հիշողության քարտը չի աշխատում — պատմությունները չեն նվագարկվի»
      with a one-line "what to do". Keep the wording actionable and
      non-technical; a parent should never see "SD.begin failed".
- [ ] **Later (needs a decision):** push notification / email when a toy goes
      from healthy → faulty, and when a toy stops checking in entirely
      (that second one is the more common real-world case: unplugged, moved,
      Wi-Fi password changed). Email transport already exists (Resend).

**Scope note:** this is genuinely additive — the heartbeat body, the
`Device` entity and `LinkedDeviceDto` are all designed to grow. It is NOT a
prerequisite for the first families, but it IS a prerequisite for families
the owner cannot personally visit.

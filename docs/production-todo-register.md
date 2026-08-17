# Areg — production TODO register

**Compiled:** 2026-08-17, from the repo at `8b1f1f5` (main, PR #27 merged).
**Sources reconciled:** `SHIP.md`, `docs/ship-md-evidence-audit.md` (2026-07-26),
`docs/launch-readiness-roadmap.md` (2026-07-26), `docs/soft-launch-day-plan.md`,
`docs/v2-backlog.md`, `docs/hardware/open-questions.md`,
`docs/ota-release-runbook.md`, `CLAUDE.md`, the last ~95 commits, and the
2026-08-14/15 bench evidence in `tools/quality-evidence/`.

**Why this file exists.** Five documents each described a path to launch and
three of them are weeks stale — the roadmap still lists TLS, backups, the
hosted backend and the app's baked-in LAN IP as open, and all four are closed.
This is one list, current, with the stale items removed and marked where they
were closed.

**Owner column:** `You` = human decision, account, purchase, or hands on the
toy. `Code` = implementable in-repo. `Both` = code plus a human step.

**Two finish lines**, deliberately kept apart:
- **① First real families** — a handful of friendly households, toys the owner
  built, supervised. Weeks of work.
- **② Sellable product** — certified, manufactured, in a store. Months and
  budget.

---

## 0. Blocking ① — do these first

| # | Item | Owner | Status |
|---|---|---|---|
| 0.1 | **A family that changes its router has a dead toy.** `wifi_creds` NVS is empty in the field, BLE provisioning is not compiled into production, so the compile-time SSID/password is the toy's only copy. Recovery today = a cable and a PC. Found on the bench 2026-08-15 when the owner changed his router mid-session. Needs BLE provisioning compiled in (needs `PartitionScheme=huge_app`, already proven to fit) plus the auto-fallback path exercised on hardware | Both | Not started |
| 0.2 | **Listen test on the 70 new story clips.** intro / summary / 3 questions / offer / reoffer × 10 stories, rendered 2026-08-16, heard by nobody. The standing gate is that no rendered asset reaches a child until someone listens end to end | You | Not started |
| 0.3 | **Firmware 1.3.3 is cable-flashed only and its behaviour is unverified.** Nobody has pressed the button on it: quick-press-plays-a-story, hold-opens-the-menu, hold-not-eaten-as-the-answer, silence-after-hold-falls-through-to-a-story, toy-still-responds-after-the-menu — all unproven | You | Not started |
| 0.4 | **1.3.3 cannot become an OTA release as built** — compiled from a bench `config.h` carrying the owner's real device id, API key and Wi-Fi password. Rebuild from placeholders, pass `tools/firmware/check_release_image.py`, then stage and bump `FirmwareUpdate:LatestVersion` (deliberately still `1.3.2`) | Both | Not started |
| 0.5 | **Confirm password-reset email actually delivers.** `Notifications:Transport` ships `log`; no evidence in-repo that Resend is switched on in Railway with a verified sending domain. A beta parent who forgets their password is otherwise locked out permanently | You | Unconfirmed |
| 0.6 | **`Audio__BlobStoreRoot` is not set on Railway.** Child voice recordings are written inside the container and destroyed on every redeploy — not merely un-backed-up, not durable at all. Owner decision pending on privacy posture; either configure the volume path or turn the write off deliberately | You | Flagged, undecided |
| 0.7 | **Each family needs a physical toy.** One bench unit exists. More families = more built units, each with a per-device identity burned to NVS | You | Not started |

---

## A. Content and audio

| # | Item | Owner | Status |
|---|---|---|---|
| A.1 | Story library — 10 stories, re-rendered per-segment, character voices, ambience | — | **Done**, owner-approved 2026-08-12 against pinned sha256 |
| A.2 | 70 per-story clips rendered | — | **Done** 2026-08-16; listen test open (0.2) |
| A.3 | **Offline-game clips (92) are a bench library only.** Owner's own caveat: every clip gets an expressive re-render — acting, not just correct pronunciation — plus a fresh listen test before launch | Both | Not started |
| A.4 | **Welcome/voice clips (43) — owner listen test** never recorded as done | You | Unconfirmed |
| A.5 | **Variant endings (10)** — text reviewed by the owner, `_status` still reads DRAFTS, never rendered, no manifest entry | Both | Text near-done, audio not started |
| A.6 | **Tsivik serial (6 episodes)** — backend + firmware plumbing shipped; episode text still DRAFTS, no render, no manifest entry. Also: the "one new episode per day" gate is a per-BOOT RAM latch, not a calendar day | Both | Not started |
| A.7 | **Bedtime music ships EMPTY.** `ContentSync:Music` needs rights-cleared tracks before the parent toggle means anything | You | Not started |
| A.8 | **Narrator decision** — a real, famous, living Armenian storyteller with a licensed AI clone, for stories *and* Areg's live answers. No vendor confirmed to hold all three requirements (third-party cloning with consent, Armenian in the cloned voice, low enough latency). First doors: VS.AM (Yerevan), Camb.ai. Nothing recorded before the AI clause is signed. Full package: `docs/voice-narrator-brief.md` | You | Open |
| A.9 | Areg's live voice is OpenAI `nova`. An Azure `hy-AM` adapter is one class + one DI line, deliberately unbuilt until the owner has listened to samples and chosen | Both | Deferred by decision |
| A.10 | **A native Eastern Armenian adult who is not the owner** listens to 10 minutes of output and writes a note. SHIP.md A5; every reviewer on record so far is a Claude subagent or the owner | You | Not started |

---

## B. Firmware and device

| # | Item | Owner | Status |
|---|---|---|---|
| B.1 | HTTPS to the cloud with a pinned ISRG Root X1 CA | — | **Done** (`net_transport.cpp`); the old roadmap's "ESP32 is HTTP-only" item is closed |
| B.2 | OTA apply, rollback, signature, content report, sync retry/backoff | — | **Done**, field-proven through 1.3.2 |
| B.3 | **Story pauses have never been bench-run.** Shipped 2026-08-07; only the pause-planner arithmetic is covered by pure tests. First pause per story relies on a 192 kbps byte-rate assumption | You | Not started |
| B.4 | **Serial (Tsivik) firmware path never bench-run** — v4→v5 index upgrade, per-episode eligibility, the daily latch across a real reboot | You | Not started |
| B.5 | **The content index is written once, at the very end of a sync.** A crash mid-sync discards the record of every download that already succeeded — this is what made the 2026-08-14 crash loop re-download the whole library every ~3 minutes. Per-namespace index writes are the real fix and are not done | Code | Not started |
| B.6 | **A crash loop is invisible to the product.** The toy heartbeats normally for the first 180 s of each cycle, so it reads as online and merely `stale`. The reset reason is already computed but rides only on an OTA ack, never the heartbeat | Code | Partially closed by Stage 1 (`crash_looping` verdict); the reset reason still needs to ride the ordinary heartbeat |
| B.7 | **`library_crash` has no fault code.** Every other parent-visible fault hands the parent a quotable code (`E-101` style); this one says "contact support" with nothing to read back | Code | Not started |
| B.8 | **Entitlement is forward-acting only.** Denying a story stops it being offered; a toy that already cached it still plays it. Retirement + an orphan sweep are what actually remove content from a card | Code | Deferred by decision |
| B.9 | **Orphan sweep for uploaded content** (parallel to the deferred C2.3 audio-blob sweeper) does not exist | Code | Deferred |
| B.10 | **`say-again` clip was missing from the card** during the 2026-08-15 owner live test — the toy skipped its own "I didn't hear you" line. Verify the clip-loss fix healed it on the real card | You | Unconfirmed |
| B.11 | **Free heap fell ~86 KB between 1.2.0 and 1.2.1** and only 672 B is accounted for. Measure before another RAM-hungry slice lands | Code | Not started |
| B.12 | Power: `WIFI_PS_MIN_MODEM` shipped (1.1.4) but never measured with a PPK2. Light-sleep idle (10-20×), amp `SD_MODE` mute between clips, content sync with the amp muted, battery telemetry on the heartbeat — all open, and none may ship in the same release as an OTA | Both | Not started |
| B.13 | **The bench MAIN button is on GPIO0**, an ESP32 strapping pin — a child holding it through a power cycle forces download mode, which looks exactly like a dead toy. The production schematic moves it to GPIO18; the bench firmware pin map has not followed | Code | Not started |

---

## C. Backend and operations

| # | Item | Owner | Status |
|---|---|---|---|
| C.1 | Hosted backend, domain, TLS, Railway volume, backups, clean-clone boot, content upload from the console | — | **Done**; the roadmap items 1-5 are closed |
| C.2 | **Confirm the live server is running the merged main** (PR #27, the 70 clips). Nothing in-repo records the deploy | You | Unconfirmed |
| C.3 | **Production hardening switches** — `Security:RequireHttps`, `ForwardedHeaders:Enabled` + `KnownProxies`, `AllowedHosts` (empty ⇒ host filtering off, logs a startup warning), `Cors:AllowedOrigins`. Committed defaults are all off; whether Railway overrides them is unrecorded | Both | Unconfirmed |
| C.4 | **Nothing consumes the metrics.** Prometheus endpoint, counters and two latency histograms are scrape-ready; there is no dashboard, no alert, no on-call. Unattended means learning from a graph, not from a child | Both | Not started |
| C.5 | **Offsite backup habit.** On-volume daily snapshots run; the only defence against losing the volume is a human pulling `GET /api/internal/backup` on a schedule | You | Not started |
| C.6 | **Internal console still on a static shared token.** `Internal:RequireSession` + per-operator TOTP are built and default off | You | Not started |
| C.7 | **Cost cap is INTERIM.** Fixed 2026-08-12 (it had been counting a third of real spend); now ~30 questions/day/toy. Owner decided to revisit tiers before production | You | Open |
| C.8 | **Postgres migration decision.** SQLite + WAL is a documented stopgap and blocks a second instance | Both | Deferred, decide before pilot scale |
| C.9 | **Multi-instance blockers**: `LoginAttemptThrottle`, `ExportCooldown`, `OperatorSessionStore`, `ChatService.ActiveModes` are all process-local | Code | Deferred |
| C.10 | **No deploy stage in CI.** Build and test run on every push; deploys are manual | Code | Not started |
| C.11 | **`/api/story-qa-text` concealment is not total** — malformed requests get a 400 naming the fields, so a scanner learns the route exists. The relay risk is closed (no valid request reaches GPT in prod); the leak is informational only | Code | Known, accepted |

---

## D. Safety and legal

| # | Item | Owner | Status |
|---|---|---|---|
| D.1 | **Red-team coverage is unmeasured.** The 55-entry Armenian corpus exists and the fail-closed contract is solid, but only 14 of 44 unsafe entries are behaviourally verified, and scary-topic is not an unsafe category at all. Moderation *infrastructure* being right and moderation *coverage* being adequate are different claims; today only the first is defensible. This is the child-safety gate | Both | Partial |
| D.2 | **COPPA / GDPR counsel review.** Compliance is self-attested. Longest lead time on the board — start in parallel, not after the code | You | Not started |
| D.3 | Privacy policy + terms pages exist and publish the 90-day retention period | — | **Done**; legal review of them is D.2 |
| D.4 | **Device and Wi-Fi credentials are in git history** (`esp32/AregVoiceMvp/config.h`, tracked in four commits reachable from main). The release-image scanner now blocks the binary form, but the history is unchanged — the affected device key must be treated as compromised and rotated | You | Not started |
| D.5 | **No secret-scanning in CI.** No gitleaks/trufflehog, no scan step; the commit hook postdates the leak and has no pattern for Wi-Fi or device keys | Code | Not started |
| D.6 | Structured logs go to stdout only; log retention is the host's problem, and there is no owner for it | You | Open |

---

## E. Parent app and accounts

| # | Item | Owner | Status |
|---|---|---|---|
| E.1 | App points at the live server by default; parity work (theme, covers, plain words, grouped settings, one diary, add-a-child, invites) landed | — | **Done**; the roadmap's "repoint the app" item is closed |
| E.2 | **Apple declined the Developer Program enrollment (2026-08-04)** — there is no TestFlight or App Store path today. The add-to-home-screen dashboard is the iPhone surface. Re-apply, appeal, or accept the PWA as the iOS answer | You | Blocked, decision needed |
| E.3 | **Android build never produced.** EAS is configured; `eas build --profile preview --platform android` yields an installable APK with no Apple account involved | Both | Not started |
| E.4 | **Google Sign-In** is built and off (`GoogleAuth:ClientId` unset). One Google Cloud project + a web client id turns it on | You | Not started |
| E.5 | **Sign in with Apple** — an App Store *rule* once the iOS app offers Google, not a preference. Blocked behind E.2 | Both | Not started |
| E.6 | **BLE provisioning screen is written but never verified on a real phone** (needs an Expo dev build; native module, won't run in Expo Go). Related to 0.1 | Both | Not started |
| E.7 | **No parent UX session on record** — a real phone, a real setup, someone who is not the owner | You | Not started |

---

## F. Hardware to a sellable product (②)

| # | Item | Owner | Status |
|---|---|---|---|
| F.1 | **Battery chemistry for run 1.** Both designs complete in `power-tree.md`; AA = zero certification lead time, ~€150-300/yr of cells for the parent; Li-ion = +$8.65 BOM, €6-10k one-time, weeks. Recommendation on file: AA run 1, Li-ion run 2. Business call | You | Undecided |
| F.2 | **Speaker sensitivity gates the rail count.** ≥85 dB/W/m measured in-enclosure ⇒ single 3V3 rail; ≤84 ⇒ the 5 V rail returns. Candidates to order and measure named in `open-questions.md` §2 | Both | Undecided, blocks the schematic |
| F.3 | **EU or not for run 1.** A €500-1,500 written pre-assessment from a notified body doing both toys and RED — the highest-leverage spend in the plan; gates enclosure tooling | You | Not started |
| F.4 | **Lab measurements M1-M10** (rail sag, per-state current, charge thermals, NTC window, ADC accuracy, runtime, drop, SPL, EMC pre-scan, acoustic prototype) — cannot be simulated away | You | Not started |
| F.5 | **Simulations S1-S5** before layout (buck-boost transient, inrush, amp decoupling impedance, sealed-box response, charger thermal) — inputs specified, none run | Code | Not started |
| F.6 | **Microphone is EOL.** INMP441 discontinued; ICS-43434 last-time-buy has passed; the named fallback is itself obsolete. Run 1 on remaining stock, rev A on Infineon IM69D130 (PDM) — a firmware capture-path slice with its own bench session, decided before footprint freeze | Both | Research done, slice not started |
| F.7 | **Custom PCB rev A, enclosure, gasket, grille** | Both | Layout not started |
| F.8 | **Certification** — FCC/CE/UKCA radio, EN 71 / ASTM F963 toy safety, UN38.3 battery shipping. Months of lead time and real money | You | Not started |
| F.9 | **Manufacturing + provisioning line** — factory NVS burn station, claim-code printing, QR on the box | You | Not started |
| F.10 | **Unit economics** — cost per child per day against a price point. The daily cap is a safety valve, not a model | You | Open |

---

## Closed since the stale docs were written — do not re-plan these

TLS and a domain · hosted backend on Railway · secrets in a real store ·
container validated · `/data` backups · the app's baked-in LAN IP ·
ESP32 HTTPS with a pinned CA · OTA turned on and field-proven ·
ContentSync turned on · per-message `Message.Mode` · a parent-readable
retention page · cost-per-hour measured · a one-page ops runbook ·
clean-clone boot verified · story-audio truncation · the OTA manifest
signing bug · the content-sync OOM crash loop · the leaked device key in a
release image · the index-loss bug that deleted the toy's voice.

---

## The rule that still applies

`SHIP.md`: when fewer than three items block v1.0, stop building and ship it
to one real child. Section 0 is that count today — it stands at seven.

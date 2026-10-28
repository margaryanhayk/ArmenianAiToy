# SHIP.md — evidence audit, 2026-10-22

**Supersedes** `docs/ship-md-evidence-audit.md` (2026-07-26).
**Branch:** `claude/release-path-20261022` on `main @ bf2d36d` (2026-09-25).
**Scope:** audit and proposal. `SHIP.md` was **not** modified (only the owner edits it).
**Method:** four independent auditors, one per SHIP.md section, each required to cite a
repo path with line numbers or a command plus its output for every status; five
inventories (shipped audio and its listen-test state, firmware features never bench-run,
deployment and onboarding state, planning-document sprawl, the real session texts); then
two adversarial reviewers who tried to refute the DONE claims, the minute estimates and
the plan's completeness. `DONE` is reserved for the **complete acceptance condition as
written**, with recorded evidence. Code that exists without proof is `PARTIAL`.

Measured today, not cited:

```
GET https://armenianaitoy-production.up.railway.app/api/health
{"status":"ok","service":"ArmenianAiToy API","database":"ok","openai":"ok","audioStore":"ok"}

cd backend && dotnet test -c Release
Passed!  -  Failed: 0, Passed: 3197, Skipped: 0, Total: 3197, Duration: 41 s
```

---

## 1. Executive summary

| Status | July 2026 | Today | Items |
|---|---|---|---|
| **DONE** | 0 | **1** | D1 |
| **PARTIAL** | 12 | **14** | A1 A2 A3 A4 A6 B1 B2 B3 C1 C2 C3 D2 D3 D4 |
| **NOT STARTED** | 6 | **3** | A5 B4 C4 |

By the letter of SHIP.md the toy is one item closer than in July. By the substance it
is much closer: in July the voice chain had no hardware record at all, one story was
approved, and `POST /api/story-qa-text` bypassed moderation. Today ten stories are
approved and index-selected on hardware, three full voice chains are on record, every
response path moderates both ways, the backend is live with TLS, backups, alerting code
and a restore drill, and two live Armenian red-team runs are in the repo.

**What is actually missing is proof, and almost all of it comes from two evenings:**

- **One bench evening** (cable, serial capture, ~2 h) produces the evidence for A1, A4,
  A6, C1, C3 and C4, and is the hour of real play D3 needs. `tools/firmware/bench_watch.py`
  now counts the chains and selections and writes the evidence file itself.
- **One listen evening** (headphones, ~80 min) closes the human listen test for the 98
  shipped files nobody has heard and re-approves the rest on their current bytes.
  `tools/listen/listen_test.py` plays them in order and keeps the books.

Plus one friend (A5), one desk hour (SHIP.md statuses, three Railway variables, two
decisions), and the merge of this PR (C2, D1 re-verified, D4, B1's structural test, the
stale docs).

**Honest arithmetic after those evenings:** 9 to 12 of 18 DONE by the letter (floor: C2
D1 D2 D4 B1 A1 A6 C4 A5; ceiling adds C1, A4 and D3, which depend on the bench going
well and on two owner scoping decisions). Six stay open either way: A2, B2 and B3 wait
on the owner's "go"; A3 and B4 are decisions; C3 as written needs a firmware change.
So `SHIP.md`'s own rule ("fewer than three items block → ship to one real child") is
**not** met by the evenings alone. Giving the toy to his own child in his own home for a
week is the owner's call to make explicitly, by declaring which of the six are not
blockers for that week; this audit does not make it for him.

**Three findings outrank everything else, in this order:**

1. **The repository is public and two real credential leaks are in its history** (B4).
   `esp32/AregVoiceMvp/config.h` carried real Wi-Fi and device credentials across four
   commits in June 2026, and the OTA image committed on 2026-08-13 (`8e66495`, reverted
   the same day in `d425c4e`) carried a real device id and API key. Both are still on
   `origin/main`. No record in the repo says the device was revoked or the Wi-Fi password
   changed. This needs an owner decision, not more code (section 4).
2. **The firmware at HEAD had never been compiled by anyone** (*update 2026-10-27: compiled clean on core 3.3.6 in the cloud, 0 warnings, 51.9 % of the OTA slot — `tools/quality-evidence/firmware-compile-20261027.md`; still never flashed*). The last firmware commit
   (`5cf65b6`, 2026-09-14) says so in its own body, and every container "compile-verified"
   claim since 2026-09-02 was on core 3.3.8 while the owner's release machine must use
   3.3.6 (3.3.7/3.3.8 boot-loop when BLE provisioning starts, `config.h.example:530-533`).
   The first ten minutes of the bench evening are a compile, and a compile error there is
   a finding, not a failed evening.
3. **The audio that ships today has never been played on a toy.** The cast library was
   re-rendered 2026-09-08, the ten variant endings and four bedtime tracks on 2026-09-11.
   The only on-toy listen on record (2026-09-02) predates all of them.

---

## 2. Evidence matrix

Paths are repo-relative; line numbers are from today's tree. "Owner min" is the owner's
own time, not the agent's.

### A. The child's experience

| Item | Status | Evidence | Gap | Cheapest closer | Who | Owner min |
|---|---|---|---|---|---|---|
| **A1** full story in Armenian, no crash, no English | PARTIAL | `tools/quality-evidence/full-story-verify-20260816.log:74-92` (sutlik-orskan from "10 eligible", SD, intro → narration → summary → question, no reset); `owner-test-session-20260816.log:561-626` (khosogh-dzuk to the end, then a TASK_WDT reboot on the answer upload); `answer-reply-verify-20260816.log:60-100` (the fix verified the same day); `sd-bus-fix-listen-test-20260902.md:22-25` (owner heard one full story + after-story on the toy, "its ok"); all 10 `Stories/Content/*.story.json` carry `status: approved` + `listenTestAt`; `cast-library-ship-20260908.md:1-7,38-44` ("Not done: the listen test on the TOY") | The narration that ships (cast 09-08) and the alts (09-11) never played on a toy; HEAD firmware is 8 commits past the last bench; the 08-16 crash fix was verified on a build that no longer exists; the no-Latin gate covers `/api/chat` but not the story-qa/reflection reply paths (`ResponseQualityGate.cs:15-16,79-87`; `ReflectionDialogueService.cs:106` is prompt text only) | Bench evening: one full story through the after-story reaction, a 3-line note (story id + Version, "no reset", "all Armenian") | both | 30 |
| **A2** deterministic Armenian checks per mode | PARTIAL | `ResponseQualityGate.cs:15-16,43-75,87` (universal 4+-Latin-letter retry; Calm no `?`/`!`; Curiosity no `?` and ≤240; Game ≤200); `ResponseQualityGateTests.cs:27,36,196-250` (29 tests; `:215,:250` pin that Story has NO cap); `CuratedStoryAuthoringRulesTests.cs:76-77,198,216-224` (curated story text: no Latin, Armenian full stop, ≤300 chars/segment over all 10 stories); `ChatService.cs:2840,2866` (3-sentence cap on voice replies; letterless backstop); CLAUDE.md N16 (SelfHarmSignal + SecrecyPromiseGuard, 36 tests) | No positive Armenian-script validator (0 of 5 modes); Story/Riddle/Calm uncapped; no Latin gate on story-qa/reflection replies (`LibraryStoryQuestionPromptBuilder.cs:47,60` prompt only) | Agent plan: Armenian-script ratio check + Story/Riddle/Calm caps in `ResponseQualityGate`, applied to the story-qa and reflection reply paths — **HIGH risk** (ChatService-adjacent), owner says "go" | both | 10 |
| **A3** continuation follows the choice, 10 saved runs per mode | PARTIAL | `tools/StoryInteractiveLoop/evidence/` (79 tracked files, 30 of them Story runs with a "Selected previous choice" turn and the continuation body — missed by the July audit; `tools/StoryBenchmark` itself keeps only an aggregate `baseline.json`, its per-run output lands untracked in `bin/`); `Evaluators.cs:454-468,537` (lexical coherence check); `armenian-safety-live-20260923.md:74-82` (StoryBenchmark re-run 09-24, 22/29 in a capped run; baseline 29/29); `tools/GameBenchmark/Program.cs:233-248` (Game has no choices by design; 46/46 turns in baseline, aggregate only); `GameLoopIntegrationTests.cs:116,438,628` (34 directive tests); `sd-postflash-20260830.log:67-82` (one offline mind-reader round on hardware) | Story half met in substance. Game half: no per-run artifacts; "choice" is undefined for Game (July open question #7 never answered); online Game loop never flashed | Owner decides what "choice" means for Game; then agent runs GameBenchmark ×10 and commits per-run results (OpenAI key, small cost), or owner records 10 offline rounds on the toy | both | 20 |
| **A4** interrupt stops audio within 1 s, toy responds | PARTIAL | `audio_io.cpp:420-437,633-645` (barge-in polled per decode iteration, `mp3.stop()` on press); `button-jumper3-20260819.log:56-58,199-206` (press during anban-huri → barge-in → Q&A answer streamed and played → same story resumed; first byte 29.5 s); `README.md:74-76` (claim, no measurement); `README.md:1587-1592` (voice-chat path: no barge-in); `AregVoiceMvp.ino:1102-1145` (online chat replies play with no button poll); `docs/latency-plan.md:28,48-104` (no press→silence metric exists) | The sub-1-second stop was never measured; the only Q&A latency on record is 29.5 s (before the 09-24 STT switch); online-mode replies cannot be interrupted; shout pauses unrun | Bench evening: five mid-story presses with a phone video or stopwatch (the firmware prints no press-time line during playback — `[story] SD barge-in` at `audio_io.cpp:644` is emitted at the same instant as the stop, so no log can measure it); `bench_watch.py` counts the barge-ins and confirms each led to `record_begin` → `[qa] answer played`. The owner also decides whether A4 is scoped to stories (online-mode replies cannot be interrupted at all) | both | 15 |
| **A5** native adult, not the owner, 10 min, written note | NOT STARTED | `anban-huri-listen-test-TEMPLATE.md:85` ("Listened by" never filled anywhere); `cast-library-ship-20260908.md:3-4`, `sd-bus-fix-listen-test-20260902.md:22-23`, `story-audio-listen-test-20260812.md:3-18` (every listener is the owner); Armenian text review is by Claude agents (CLAUDE.md N8) | Nobody but the owner has heard any shipped audio | Owner hands ~10 min of the current files to one native adult and gets a dated 3-line note; playlist and form: `tools/quality-evidence/native-listener-note-TEMPLATE.md` | owner | 25 |
| **A6** ≥3 cached stories, `/content_index.json`, no back-to-back repeat | PARTIAL | `full-story-verify-20260816.log:74-75` ("selected sutlik-orskan (10 eligible)", source SD) and six more logs with "10 eligible"; `button-back-story-answer-20260818.log:127` (10/10 items synced, index written); `story_select.h:58-106` + `story_select.cpp:51-52,524-571,609-620` (previous id excluded, cursor in NVS `aregstory`); `menu-first-test-20260826.log:20` (heard-set covered all 10); `button-jumper3-20260819.log:56-58,205-206` (pause/resume stayed on the same story); `appsettings.json` ContentSync:Stories (20 real rows); `README.md:888-902` was stale (said 2 approved stories) and is rewritten in this PR | No single log with ≥3 consecutive selections and a power-cycle between two of them (the eight recorded picks are from separate boots) | Bench evening: five quick presses (stop each at the intro), power-cycle, one more; `bench_watch.py` flags adjacent repeats and counts power-cycles | both | 10 |

### B. Safety

| Item | Status | Evidence | Gap | Cheapest closer | Who | Owner min |
|---|---|---|---|---|---|---|
| **B1** every response path moderated; a test fails if a path bypasses it | PARTIAL | `ChatService.cs:1817-1822,1856-1872,2198,2228-2239,2507-2514,2736-2743,2428-2446` (chat + audio chat: SelfHarmSignal, input and output moderation, retries re-moderated); `ChatService.cs:1690-1710` (library continuation); `StoryQaController.cs:357-380,404,481-487` (story-qa, streaming pinned by `StoryQaControllerStreamingTests.cs:288`); `StoryQaController.cs:911-921,944-957` (reflection answer: input + output moderation, **no SelfHarmSignal**); `StoryQaTextController.cs:36-56,112-138` + `StoryQaTextDevGate.cs:33` + `Program.cs:587-593` (the July bypass closed, 39d86eb); `InternalController.cs:483-504`; `StoryAudioController.cs:300-321`; `DeviceController.cs:362-374` (voice-intent); per-path tests listed in the B auditor's notes | No STRUCTURAL test (nothing enumerates model-consuming types); the reflection path lacks the self-harm grown-up line | **This PR** adds the reflection-based structural test. SelfHarmSignal on the reflection transcript (`StoryQaController.cs:911`) is a HIGH-risk safety change: owner says "go" | agent | 2 |
| **B2** saved adversarial suite passes scary / secret-keeping / personal-information | PARTIAL | `TestData/armenian-red-team-safety-corpus.json` (55 entries; EV-001..005 secret-keeping, PR-001..005 personal data; **no scary-topic category**); `ArmenianRedTeamSafetyCorpusTests.cs:174-225` (asserts only the 14 local-filter entries); `armenian-safety-live-20260923.md:29-72` (Run 3 on gpt-4o after N16: EV 5/5, SH 5/5, PR 5/5, benign 10/10) and `:258-317` (Run 6 on Gemini, the production chat model: 0 secrecy promises, 0 unsafe; pass 2 lost to depleted credits); `tools/safety/e2e_redteam.py`, `moderation_recall.py` | Scary topics (ghosts, monsters, the dark, death) have never been probed; the live PASS is prose, not a saved machine run; `/api/chat` only | Agent: add `scary_topic` probes (armenian-story-master reviewed), assert EV/SH behaviour offline, re-run `e2e_redteam.py` on both providers and commit the JSON. Owner: OpenAI key in the session, top up Gemini credits | both | 10 |
| **B3** retention in one parent-readable page; code matches | PARTIAL | `wwwroot/privacy.html:53-66,118-125,195-204` (v1.0, 90-day conversations + recordings, retention table); linked from `parent.html:1032,1071`, `index.html:203`, `terms.html:74,136,179`; `docs/privacy-parents.md:1-107`; `appsettings.json:94-99`, `RetentionPurgeService.cs:65,80,92`, `RetentionPolicy.cs:20`, `ParentService.cs:2361-2365` (90 days and 24 h match) | **Code does not match the page twice:** the page names OpenAI as the reply writer while production chat is Gemini (`appsettings.json:25` PROD NOTE; `grep -ci gemini privacy.html` → 0) and ElevenLabs is unnamed; reflection answers, story plays, usage counters and ~7-day backups are never time-purged and undisclosed (`docs/legal/parental-consent-draft.md:40-57`). English-only; no test pins the page to config | Agent revises §5 and §7 of `privacy.html` and mirrors `docs/privacy-parents.md` (parent-facing → ux-ui-designer review); owner confirms the live `AI__ChatProvider`/`AI__TtsProvider` values and reads the result | both | 12 |
| **B4** no secret ever committed; verified by a history scan | NOT STARTED | `gh api repos/margaryanhayk/ArmenianAiToy` → `private=false`, 1014 commits; `esp32/AregVoiceMvp/config.h` history: `dfb5a44` 04-23, `df86df9` 05-19 (placeholders), `cb1ad28` 06-14, `dbb12dc` 06-18 (real SSID/password/device id/key, classified by shape, values never printed), `0794e9b` 06-18 (untracked); `docs/CLAUDE-history.md:5192-5224` (`8e66495` 2026-08-13 OTA binary with a real device key, reverted in `d425c4e`); no commit or evidence file records a revoke/rotation; `.claude/hooks/block-secret-commit.py:16-22` has no `dtk_`/Wi-Fi/PoP pattern and does not run on the owner's own commits; `ci.yml` has no secret scan; `check_release_image.py` guards OTA images only | Public repo, two leaks still on `origin/main`, no scan recorded, nothing says the device was revoked or the Wi-Fi password changed | **Owner decision** (section 4). Then agent: full-depth scan from an unshallowed clone recorded in `tools/quality-evidence/`, hook + CI patterns. Note: `tools/release/preflight.sh` (this PR) scans the working tree, not history | both | 15 |

### C. The device

| Item | Status | Evidence | Gap | Cheapest closer | Who | Owner min |
|---|---|---|---|---|---|---|
| **C1** full chain ten times in a row, no manual reset | PARTIAL | `answer-reply-verify-20260816.log:11,83-95` (one complete chain: record → POST → 200 → play → "reply 0 played"); `three-fix-verify-20260816.log:42-60` (second); `button-jumper3-20260819.log:46,80,199,204` (third, in-story Q&A, streamed); `owner-test-session-20260816.log:589-637` (a chain killed by a task WDT, then fixed); across all 59 logs the maximum is 1 completed chain per log, zero `[chat] turn N played` ever, no log on fw 1.3.4 or later; `AregVoiceMvp.ino:11` ("No wake word" — the trigger is the GPIO18 button) | Ten consecutive chains were never attempted; "wake" must be read as "press" (only the owner can reword SHIP.md) | Bench evening: alternate reflection answers and in-story questions until `bench_watch.py` reports a run of 10 without a reset | both | 30 |
| **C2** device auth works; legacy sketch fixed or deleted | PARTIAL → **DONE on merge** | Production today: `POST /api/chat` with no headers → 401, with a bogus key → 401; `DeviceAuthMiddleware.cs:15-17,67,82`; `DeviceAuthMiddlewareTests.cs:37,55,71,89`; `answer-reply-verify-20260816.log:11,88-89` (authed voice turn on hardware); `esp32/ArmenianAiToy/ArmenianAiToy.ino:361-376` POSTed `/api/chat` with no auth headers | The legacy sketch was still tracked | **This PR deletes `esp32/ArmenianAiToy/`** and updates `docs/esp32-chain.md`; the owner's merge closes the item | agent | 2 |
| **C3** Wi-Fi drop recovers without an adult | PARTIAL | `voice_client.cpp:85-89,225,256-295` (auto-reconnect, 3–60 s capped backoff); `AregVoiceMvp.ino:74,2839-2848` (after 5 min of outage a provisioned toy opens BLE provisioning and stops retrying until reboot); `README.md:261-264,1590-1591` (Wi-Fi down mid-turn → failure clip → idle, next press works); `clip-loss-fix-verify-20260815.log:3-24` (the only Wi-Fi-loss log is a no-AP boot, not a drop-and-recover) | No router-off → router-on test on record; a drop longer than 5 min needs an adult (BLE mode has no timeout back to normal) | Bench evening: router off 30 s, on, next press completes a turn. That records the short-drop case only; as written, C3 stays PARTIAL until the owner either rewords it to short drops or approves a firmware change (a timeout that returns from BLE provisioning to normal reconnecting) | both | 20 |
| **C4** loud and clear in a normal room, measured | NOT STARTED | `amp-test-20260826.log`, `amp-test2-20260826.log`, `amp-rewire-20260826.log` (boot and heartbeat lines only, no playback, no number); `docs/hardware/audit-components.md:205-232` (datasheet arithmetic: 86.3 dB @ 1 m calculated; EN 71-1 cap ≤78 dB @ 50 cm); `audio_io.cpp:324-948` + `volume_pot.h:40` (knob-driven gain, ceiling 1.0) | No dB(A), distance, room or instrument anywhere | Bench evening: phone SPL app (NIOSH SLM or similar), toy on a table, meter at 1 m, one story at knob max and at a comfortable level; write app, distance, room, dB(A), one sentence on intelligibility | both | 15 |

### D. Operability

| Item | Status | Evidence | Gap | Cheapest closer | Who | Owner min |
|---|---|---|---|---|---|---|
| **D1** clean clone with documented steps, on a machine that is not the owner's | **DONE** (re-verified in this PR) | A refuter showed the 08-12 evidence recorded the OLD steps failing and that `CLAUDE.md` named the secrets without a command, so a verbatim run still died inside `ApiKeyCredential` on the shipped `"ApiKey": ""`. This PR adds the two `export` lines to `CLAUDE.md` § Build & test, a guard in `DependencyInjection.cs` that names the setting on an empty key, and `tools/quality-evidence/clean-clone-boot-20261022.md` (fresh clone in this container, the four documented commands verbatim: build, test, refusal without secrets, boot and `/api/health` with them); `clean-clone-boot-20260812.md:10-46`; `railway.json:3-5` + `docs/railway-deploy.md:10-49` + today's live health (Railway's builder runs the root `Dockerfile` from a clean GitHub checkout); `.github/workflows/ci.yml:10,22-28` (clean checkout builds and tests on every push) | None after this PR. `docs/deploy.md:64` said the image starts without `OpenAI__ApiKey` (it does not) — fixed | Owner writes DONE with the evidence path | owner | 1 |
| **D2** suite green, count recorded in SHIP.md | PARTIAL | `SHIP.md:54` is still `____`; `CLAUDE.md` § Build & test and today's run agree on 3197 (this PR adds tests; the number in CLAUDE.md is updated with it); `ci.yml:22-64` (solution suite + firmware host tests + Python tool tests, last main run green 2026-09-24). Three test sets remain outside the solution and CI: `tools/ContentPackBuilder.Tests`, `tools/StoryInteractiveLoop.Tests`, `tests/engines/test_choice_normalizer.py` | The number is not written | Owner writes the count from `tools/release/preflight.sh`'s `dotnet-test` line | owner | 2 |
| **D3** cost per hour of play, measured | PARTIAL | `tools/quality-evidence/cost-per-hour-of-play-20260812.md:4,19-47,90-98` ($0.08–$0.39/h, ~$0.20 typical: list prices × one measured prompt size × assumed turn counts); `OpenAICostEstimator.cs:6-7,24-74` ("NOT authoritative pricing"; gpt-4o + Whisper rates); `appsettings.json:25,56,60,66` (production: Gemini chat, gpt-transcribe STT, cap 0.25); `InternalSystemController.cs:78-100,146-147` + `docs/ops-runbook.md:290-296` (console spend = the estimate, durable per device since 2026-09-11, never compared to a bill); `docs/openai-daily-cost-cap.md:27-30,46,57,67` stale (fixed in this PR) | The condition says measured; this is an estimate for a provider mix production no longer uses | Most of the bench evening is SD playback, which costs nothing; the billable part is the online turns. Finish the evening with ~45 minutes of online play (questions during stories, after-story answers, the voice menu) — `bench_watch.py` now records the session's wall-clock and its count of online turns as the denominator. Next day the owner reads three numbers (toy spend delta in admin → System; OpenAI usage; Google AI Studio billing, which lags) into `tools/quality-evidence/cost-per-hour-TEMPLATE.md`; agent retunes the estimator defaults | both | 60 |
| **D4** one-page runbook: restart, logs, three likely failures | PARTIAL → **DONE after the owner confirms two click-paths** | `docs/ops-runbook.md` (366 lines, Railway/Docker; `:350-357` boot failures; `:109-127` logs = stdout JSON; `:332-341` restart safety notes without a procedure) | Not one page; no restart procedure; no logs click-path | **This PR** adds a ≤20-line "If it is 3 a.m." box at the top (health URL, restart and logs click-paths, three failures as one-liners). Owner opens Railway once and confirms the two click-paths | both | 5 |

---

## 3. Proposed `SHIP.md` statuses (for the owner to paste)

```
A1 PARTIAL  — full-story-verify-20260816.log; cast library never played on a toy; bench evening
A2 PARTIAL  — ResponseQualityGate.cs; no positive Armenian check; plan pending "go"
A3 PARTIAL  — StoryInteractiveLoop/evidence (36 runs); Game "choice" undefined
A4 PARTIAL  — button-jumper3-20260819.log; stop latency unmeasured; bench evening
A5 NOT STARTED — native-listener-note-TEMPLATE.md
A6 PARTIAL  — "10 eligible" in 7 logs; needs one log with ≥3 selections + power-cycle
B1 PARTIAL  — all paths moderate; structural test in this PR; reflection self-harm line pending "go"
B2 PARTIAL  — armenian-safety-live-20260923.md; no scary-topic probes
B3 PARTIAL  — privacy.html v1.0; names OpenAI, production is Gemini; retention scope incomplete
B4 NOT STARTED — public repo, two leaks in history; decision below
C1 PARTIAL  — three chains on record, max one per log; bench evening
C2 DONE (on merge) — legacy sketch deleted; DeviceAuthMiddlewareTests.cs; live 401 today
C3 PARTIAL  — reconnect code exists; no drop test; >5 min needs an adult
C4 NOT STARTED — bench evening, phone SPL app
D1 DONE     — clean-clone-boot-20260812.md + Railway live from the Dockerfile
D2 3197     — (write the number from preflight.sh after merging)
D3 PARTIAL  — cost-per-hour-of-play-20260812.md is an estimate; bench evening + three numbers
D4 DONE after confirming the Railway click-paths — ops-runbook.md "If it is 3 a.m."
```

---

## 4. Decisions only the owner can make

1. **Open question #1 in SHIP.md.** Recommendation: `SHIP.md` is the only finish line.
   The four other finish lines (`launch-readiness-roadmap.md` Gate 1, `soft-launch-day-plan.md`
   finish line ①, `build-status.html` "five families", `business-readiness-2026-09-12.md`
   "100 units") are all supersets that come after it, and their status columns were
   stale within weeks. This PR puts an archive banner on each; `build-status.html` is
   rewritten as a view of SHIP.md.
2. **B4: accept or rewrite.** (a) ACCEPT: confirm in the operator console that the leaked
   device is revoked and re-provisioned, change the home Wi-Fi password if that has not
   happened, and reword B4 to "every committed secret is revoked, a full-depth history
   scan is recorded, and a scanner gates commits". (b) REWRITE: `git filter-repo` on the
   four `config.h` commits and `8e66495`, force-push, re-clone every machine, ask GitHub
   support to purge cached objects. A public repo cannot be un-leaked by (b); (a) is the
   honest option and takes ~15 minutes.
3. **A3: what "choice" means for Game** (the child's spoken answer in the online loop, or
   the GREEN/RED answers in the offline games). Nothing can be marked DONE until this is
   answered.
4. **A4: story path or voice path** (July open question #5). The story-path barge-in is
   what the bench evening measures; online-mode replies cannot be interrupted at all.
5. **Three HIGH-risk "go"s or v2:** A2 (positive Armenian gate + caps), B1 (self-harm
   line on the reflection path), B2 (scary-topic probes + live re-run). Each is a plan
   first, then an approval, per CLAUDE.md's hard stops.

---

## 5. Inventories (summaries; full text in the PR description's linked files)

**Shipped audio and the listen test.** 240 advertised files, 83.2 minutes of audio; all
240 match the catalogue's sha256 on disk. Nobody has ever heard the current bytes of 98
of them: the 10 variant endings (30.9 min), the 4 bedtime tracks (16.3 min), the 30
intro/offer/reoffer clips, the 42 welcome clips, 12 game kid lines and the two Simon
tones (`tone-green`/`tone-red` ARE on disk and in the manifest; CLAUDE.md's "not yet
rendered" was stale). The 10 base stories and 40 after-story clips were approved by the
owner on a phone (`cast-library-ship-20260908.md`, `cast-clips-rerender-20260908.md`)
and never on the toy. `python3 tools/listen/listen_test.py status` reports exactly this
and exits non-zero until every file is approved for its current bytes.

**Firmware.** Field release 1.3.2 (OTA, 2026-08-14). The bench toy has reported
`fw=1.3.3` in every log since 2026-08-15 while the tree moved underneath. 1.3.4 was cut
2026-09-02 (gate PASS, `areg-current.bin`, `LatestVersion` 1.3.4, `FirmwareUpdate:Enabled`
false) and never applied to any toy. HEAD is eight firmware commits past 1.3.4 and was
never compiled. Reachable by a child the moment HEAD is flashed, never bench-run: the
online Game/Riddle/Curiosity/Calm voice loop, the offline games (on a toy with the two
answer buttons), content retirement and the per-namespace index writes, the PSRAM index
parse, the after-story question toggle, the per-toy BLE PoP. Behind flags that the bench
sheet must set explicitly: streaming Q&A, variant endings, shout pauses. The welcome menu
by VOICE has never resolved on hardware (`[welcome] intent=unknown` is the only intent
ever logged), which makes the four voice modes unreachable on the real toy until STT is
proven on the bench.

**Deployment for a family.** Live and healthy. Not provable from outside and still the
owner's to set or confirm on Railway: the `/data` volume mount, `ForwardedHeaders__Enabled`
(two families share one login bucket without it), `Alerts__WebhookUrl` + `Alerts__TelegramChatId`,
and `Notifications__Transport=resend` + key (password reset and verification mail go to
stdout today; a stranger who forgets a password is locked out). Google sign-in is off.
A stranger cannot put a toy on their Wi-Fi without the mobile app, which has never run on
a device; the owner pre-provisions Wi-Fi by cable per toy. The operator console's System
tab shows more of these from this PR on (presence booleans, never values).

**Planning documents.** Five finish lines existed; `SHIP.md` was referenced by zero
agent-facing files and `CLAUDE.md` had no rule stopping a session from adding another
unverified feature, while `.claude/overnight-prompt.md.txt` ordered "Never stop. Keep
building." This PR adds the release freeze to `CLAUDE.md`, archives the competing plans,
and retires the overnight prompt. `docs/v2-backlog.md` is contradicted by shipped work in
8 bullets (mobile app, tiers, dashboard views, cloned narrator, PCB, TLS/hosting/alerting,
orphan sweeper, modes-today); only the owner may re-baseline it.

---

## 6. What this PR changes (no owner effort beyond "merge")

- `tools/listen/listen_test.py` — guided, resumable listen test; sha256-pinned evidence.
- `tools/release/preflight.sh` — every automated gate in one command (GREEN on this tree).
- `tools/firmware/bench_watch.py` — SHIP C1/A6/A4 counters, the new serial markers, and
  an evidence file written at the end of the session; `bench_session.sh` and
  `docs/bench-test-session.md` fixed (image size, core 3.3.6, version label, flag truth,
  regression first, streaming opt-in).
- `esp32/ArmenianAiToy/` deleted (C2); `docs/esp32-chain.md` updated.
- `docs/ops-runbook.md` "If it is 3 a.m." box (D4).
- Structural moderation-coverage test (B1); operator console presence rows.
- Stale sentences fixed in `docs/deploy.md`, `docs/container-toolchain.md`,
  `docs/openai-daily-cost-cap.md`, `esp32/AregVoiceMvp/README.md` § A6, CLAUDE.md's
  audio root path.
- Archive banners on the competing plans; release freeze in CLAUDE.md;
  `docs/status/build-status.html` rewritten as a view of SHIP.md.
- `tools/quality-evidence/native-listener-note-TEMPLATE.md` (A5),
  `cost-per-hour-TEMPLATE.md` (D3).

---

## 7. Caveats

- This container's clone is shallow (170 of 1014 commits, oldest 2026-08-14); pre-August
  history was read through the GitHub API, not `git log`.
- No firmware was compiled or flashed here (no arduino-cli); nothing on hardware was
  re-verified. Every hardware claim above is a reading of a committed log.
- Secret values in historical files were classified by shape and length only, never
  printed or written anywhere.
- The live backend was probed with public endpoints only; operator-gated state (which
  env vars are set) is inferred from docs and config, as the deployment inventory says.

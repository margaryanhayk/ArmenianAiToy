# SHIP.md — evidence audit

**Date:** 2026-07-26
**Branch:** `feat/ota-apply`
**Scope:** audit and proposal only. `SHIP.md` was **not** modified. No production
code was changed. No commits, no staging.
**Method:** every status below is backed by a file path + line number, a test run,
or an explicit "no evidence found" after a targeted search. `DONE` was reserved for
items where the *complete acceptance condition* is satisfied — not merely where the
implementing code exists.

---

## 1. Executive summary

**Overall v1 readiness: not close, but the distance is smaller than the raw counts
suggest.** The engineering underneath most SHIP items is real and in several cases
excellent. What is almost universally missing is the *proof* each item demands —
recorded hardware runs, measurements, an external human's note, and parent-facing
documents. Several items are hours of work away; a few are genuinely deep.

| Proposed status | Count | Items |
|---|---|---|
| **DONE** | **0** | — |
| **PARTIAL** | **12** | A1, A2, A3, A4, B1, B2, C1, C2, C3, D1, D2, D4 |
| **NOT STARTED** | **6** | A5, A6, B3, B4, C4, D3 |
| **UNKNOWN** | **0** | — |
| **Total** | **18** | |

**Zero items currently qualify as DONE.** That is the headline. SHIP.md's own rule
("when fewer than three items block v1.0, stop building and ship it") is 18 items
away, not 3.

**Automated test count — measured today, not cited:**

```
dotnet test  (backend/, ArmenianAiToy.Application.Tests.dll, net10.0)
Passed!  -  Failed: 0,  Passed: 2013,  Skipped: 0,  Total: 2013,  Duration: 11 s
```

`CLAUDE.md:40`'s claim of "2013 tests" is **accurate** — verified by execution, not
inherited from documentation. Note this supersedes an earlier suspicion that 2013 was
a `[Fact]`+`[InlineData]` grep artifact; the grep total and the runner total coincide.

**Three findings outrank the suspected A6 blocker in severity:**

1. **A live moderation bypass.** `POST /api/story-qa-text` is unauthenticated and
   sends child input to GPT with **no** moderation on input or output.
2. **Device and Wi-Fi credentials are in `origin/main` git history.** B4's premise
   ("no secret has ever been committed") appears to be false.
3. **The voice chain — the product's core loop — has no recorded hardware evidence
   at all**, while SD/content-sync/OTA are thoroughly bench-verified.

A6 is confirmed as a genuine blocker, and is single-story at **six independent
layers**, but it is a feature gap in a working system. The three above are defects
in a shipping one.

---

## 2. SHIP evidence matrix

### A. The child's experience

| ID | Requirement | Status | Evidence | Verification | Missing work | Blocker | Conf. |
|---|---|---|---|---|---|---|---|
| **A1** | Full Armenian story, start to finish, no crash, no English | **PARTIAL** | `CLAUDE.md:2293-2300` Test A bench run 2026-07-12: `[story] source = SD (cache)` → `[story] finished`, operator heard it. Latin guard `ResponseQualityGate.cs:15-16` `@"[A-Za-z]{4,}"`, wired `ChatService.cs:2089/2236/2257`, pinned `ResponseQualityGateTests.cs:27` | Doc review + code read | Story played was `anban-huri`, `"status": "draft"` (`anban-huri.story.json:27`), via opt-in flag `AREG_STORY_SD_CACHE_FIRST`. The two **approved** stories have no hardware play on record. Pre-rendered MP3 cannot exercise the leakage guard | Yes | High |
| **A2** | All 5 modes pass deterministic Armenian checks: Armenian-only, child-safe, no Latin, length limits | **PARTIAL** | No-Latin: universal, `ResponseQualityGate.cs:87`. Length: Game `:68` (200 chars), Curiosity `:62` (240) | Per-mode test enumeration | **Armenian-only: 0 of 5 modes** (guard is negative — bans Latin runs ≥4; passes Cyrillic, digits, emoji, 3-letter Latin). **Length: 3 of 5 missing** — `ResponseQualityGateTests.cs:215` explicitly asserts Story has *no* cap. **Child-safe wording: 0 of 5** deterministic output tests. `*PromptContentTests.cs` test the prompt string, not output | Yes | High |
| **A3** | Story + Game continuation, ≥10 saved runs per mode | **PARTIAL** | Story: 23 run files with continuation transcripts, newest `tools/StoryBenchmark/bin/.../run_20260518_001810.md:13` `Continuation success 29/29` | File enumeration + gitignore check | **Game: 0 runs** — GameBenchmark has no choice mechanic by design (`tools/GameBenchmark/Program.cs:190` treats a choice block as failure). **All Story runs are gitignored** (`.gitignore:4 tools/**/bin/`) — no third party can verify. Check is lexical stem overlap only; Choice B never exercised | Yes | High |
| **A4** | Interrupt stops audio <1s and toy responds | **PARTIAL** | Barge-in real: `audio_io.cpp:412-428` polls per decode iteration, `mp3.stop()` | Code read + doc search | **No stop-latency measurement exists anywhere** — the <1s bar is unverified. Unmeasured I2S DMA tail (`AREG_STORY_RESUME_FUDGE_BYTES 8192`). `README.md:248` says "No barge-in" for the C1 voice-chat path (works only in story session). Q&A response path marked `UNVERIFIED — not compiled/flashed` at `AregVoiceMvp.ino:727` and `:750` | Yes | High |
| **A5** | External native Eastern Armenian adult, 10 min, written note | **NOT STARTED** | — | Full-repo search + `git log --all` authorship | No third-party human note exists. All candidates are Claude subagents (`armenian-linguistic-reviewer`, `armenian-story-master`) or the owner's own text reviews. `native-review-pass-story-samples-20260510.md:15` concedes a native pass "remains a separate slice" | Yes | High |
| **A6** | ≥3 cached SD stories via `/content_index.json`, no back-to-back repeat | **NOT STARTED** | See §5 — single-story at six layers | Deep chain trace | Approved stories = 2. MP3s = 2 (only 1 SD-wired). Index is a flat object. No selection logic anywhere | Yes | High |

### B. Safety

| ID | Requirement | Status | Evidence | Verification | Missing work | Blocker | Conf. |
|---|---|---|---|---|---|---|---|
| **B1** | Every response path moderated; proven by a test that fails on bypass | **PARTIAL** | 9 of 11 paths moderated both directions — `ChatService.cs:1544/1867`, `StoryQaController.cs:329/414/680`, `StoryAudioController.cs:312`, `InternalController.cs:375/396` | Path enumeration + **independently re-verified** | **Confirmed bypass:** `StoryQaTextController.cs` — 0 moderation references (verified by grep), no `[Authorize]`, route absent from `DeviceAuthMiddleware.cs:15-24` path list. Its `LibraryStoryQuestionService` ctor takes only `IAiChatClient` (`:43`) — no moderation dependency exists to call. **No structural test:** all moderation tests name one path each; nothing enumerates paths | Yes | High |
| **B2** | Saved adversarial suite passes scary-topic, secret-keeping, personal-info | **PARTIAL** | Corpus exists: `TestData/armenian-red-team-safety-corpus.json`, 55 entries; harness `ArmenianRedTeamSafetyCorpusTests.cs` | Corpus + assertion read | **Scary-topic is not an unsafe category** (appears only as benign control). Secret-keeping (5) and personal-info (5) entries all carry `localFilterShouldCatch=false` → the test asserts *no behavior* for them (`:201-206`). Only 14 of 44 unsafe entries behaviorally verified. Own evidence doc concedes "No live-OpenAI block-rate measurement" | Yes | High |
| **B3** | Retention in one parent-readable page; code matches | **NOT STARTED** | Code side consistent: 90 days across `appsettings.json:62`, `RetentionPurgeService.cs:61`, `RetentionPolicy.cs:20` | File search `wwwroot/`, `docs/`, `git ls-files` | **No parent-readable page exists in any form.** `wwwroot/` has 4 files, none a policy. `index.html:204` terms checkbox links to nothing. The one disclosure sentence (`RetentionPolicy.cs:35-37`) is reachable only inside `GET /api/parents/export` JSON, and `parent.html` has no export button | Yes | High |
| **B4** | No secret ever committed; verified by history scan | **NOT STARTED** | — | **Independently re-verified via `git log --all`** | **Premise appears false.** `esp32/AregVoiceMvp/config.h` was tracked in 4 commits (`dfb5a44`, `df86df9`, `cb1ad28`, `dbb12dc`), untracked only at `0794e9b` (2026-06-18); reachable from `origin/main`. Kinds: device id, device API key, Wi-Fi SSID + password. **No scan tool or record exists** — no gitleaks/trufflehog config, no CI scan step (`ci.yml` is build+test only). The `block-secret-commit.py` hook postdates the leak and has no pattern for Wi-Fi or device keys | Yes | High |

### C. The device

| ID | Requirement | Status | Evidence | Verification | Missing work | Blocker | Conf. |
|---|---|---|---|---|---|---|---|
| **C1** | Full chain wake→listen→backend→TTS→speaker, 10× no reset | **PARTIAL** | Auth'd round trips hardware-proven on OTA/heartbeat (`ota-bench-evidence.md:24-28` `[heartbeat] status=200`). Component paths all implemented | Bench-doc search | **No recorded hardware voice turn exists in-repo** — every voice verification artifact is an unchecked checklist (`HARDENING-INTEGRATION.md`: 0 of 33 checked; `areg-current-readiness-evaluation.md:386-388`). Repo's own bar is *three* turns (`README.md:220`), also unchecked. **"wake" does not exist** — trigger is a BOOT-button press (`README.md:53`); `.ino:11` "No wake word" | Yes | Medium |
| **C2** | Device auth works; legacy 401 closed or sketch deleted | **PARTIAL** | Auth solid + hardware-proven: `voice_client.cpp:71-75` adds `X-Device-Id`/`X-Api-Key`; NVS-first `device_creds.cpp:19-34` | `git ls-files` + header grep | Legacy sketch **still tracked**: `esp32/ArmenianAiToy/ArmenianAiToy.ino` sends no auth headers (grep: zero hits), POSTs `/api/chat` which is in `DeviceAuthPaths` → 401. `docs/esp32-chain.md:44` already documents it as stale. **Closable in one command** | Yes | High |
| **C3** | Wi-Fi drop mid-session recovers unaided | **PARTIAL** | Reconnect exists: `voice_client.cpp:132` `setAutoReconnect(true)`, `:157-193` backoff 3s→60s, B.3 provisioning fallback `.ino:1296-1303` | Code read + bench search | **Never hardware tested** (`areg-current-readiness-evaluation.md:388` unchecked). In-flight turn is still lost — `README.md:245` "One attempt per turn. No retry, no reconnect." Long-outage path escalates to BLE provisioning, which *does* require an adult | Yes | High |
| **C4** | Audio loud + clear in a normal room, measured | **NOT STARTED** | Gain `audio_io.cpp:320-322` `out.SetGain(0.6f)` with comment "conservative — raise in config.h later if needed" | Full-repo search for dB/SPL/distance | **No measurement of any kind exists** — no dB, no SPL, no distance, no room, no instrument. Only "operator heard the story" (`CLAUDE.md:2261`). Gain is 40% below unity. The criterion says "Measured, not guessed"; this is the guessed case | Yes | High |

### D. Operability

| ID | Requirement | Status | Evidence | Verification | Missing work | Blocker | Conf. |
|---|---|---|---|---|---|---|---|
| **D1** | Runs from clean clone, documented steps, on a machine that is not yours | **PARTIAL** | Docs substantial: `CLAUDE.md:35-48`, `:93-96` `dotnet tool restore`, root `Dockerfile`, `docs/deploy.md`, two Windows runbooks. CI proves clean-clone **build+test** on `ubuntu-latest` | **Independently re-verified** | **Setup steps are incomplete:** `appsettings.json:42` ships `"Key": ""`, `JwtKeys.cs:106` throws `"Jwt signing key not configured"` at startup, and `CLAUDE.md:43-44` documents only `OpenAI:ApiKey`. A clean clone following CLAUDE.md **crashes on boot**. **No evidence of the backend running on any machine but the owner's** — `windows-publish-deploy.md:63` literally `cd C:\Users\hayk.margaryan\...`; `docker build` never executed (`deploy.md:187`) | Yes | High |
| **D2** | Full suite green; count recorded | **PARTIAL** | **Green, measured today: 2013 passed / 0 failed / 0 skipped** | `dotnet test` executed this session | SHIP.md:54 blank is still `____` — the criterion says "recorded **here**". Also: "full suite" is ambiguous — `tools/ContentPackBuilder.Tests` (11), `tools/StoryInteractiveLoop.Tests` (83), and `tests/engines/test_choice_normalizer.py` (28) are outside `ArmenianAiToy.slnx` and outside CI | Yes | High |
| **D3** | Provider cost per hour of play measured and written down | **NOT STARTED** | A *cap* exists and is well built: `docs/openai-daily-cost-cap.md`, `$0.50`/device/day | Full-repo search for cost-per-hour | **Cap ≠ measurement.** Estimator is self-declared approximate (`:31-34` "Does NOT use a real tokenizer"), in-memory only, no persistence to measure from. No dollars-per-hour figure anywhere. `launch-readiness-roadmap.md:115` files unit economics under LATER and calls the cap "a safety valve, not a model" | Yes | High |
| **D4** | One-page runbook: restart, logs, top-3 failures | **PARTIAL** | `docs/windows-service-deploy.md` genuinely covers all three: §15 `Restart-Service AregBackend`, §11 logs at `C:\AregDeployData\logs\`, §11 failure cases A–H (top 3 match real fail-fast guards) | Doc read | It is **~910 lines, not one page**, and covers only the Windows/NSSM path while `deploy.md:19` names Docker "the canonical production posture" — for which no runbook exists (no restart procedure, no failure section) | Yes | High |

---

## 3. Actual v1 blockers, in execution order

Every SHIP item is by definition a v1 blocker. What follows ranks the **18 open
items** by execution order — cheapest-and-unblocking first, deepest last — and
separates them by the *kind* of work each needs, because they are not
interchangeable: a code slice and a "wait for counsel" item cannot be sequenced the
same way.

### Tier 0 — near-zero cost, close today (documentation/process)

| # | Item | Work |
|---|---|---|
| 1 | **D2** (partial) | Write `2013` into SHIP.md:54. The suite is green as of today. Also decide whether "full suite" includes the 3 out-of-solution projects |
| 2 | **C2** (partial) | Delete `esp32/ArmenianAiToy/` — satisfies the criterion's "or the legacy sketch is deleted" branch outright. **Destructive; needs your go-ahead** |

### Tier 1 — safety defects in shipping code (do before feature work)

| # | Item | Work |
|---|---|---|
| 3 | **B1** — moderation bypass | Moderate, authenticate, or environment-gate `POST /api/story-qa-text`. Then add the structural test the criterion actually demands (one that fails when a *new* path bypasses moderation) |
| 4 | **B4** — committed credentials | Confirm repo visibility (public vs private) → rotate the leaked device key and Wi-Fi password if not already → run and record a real history scan → extend the hook's patterns to device keys and Wi-Fi |
| 5 | **B3** — parent retention page | One short parent-readable page stating the 90-day policy, linked from `parent.html` and the terms checkbox |
| 6 | **B2** — adversarial coverage | Add a scary-topic category; convert the 30 unasserted entries into behavioral assertions |

### Tier 2 — code slices (the A6 chain)

| # | Item | Work |
|---|---|---|
| 7 | **A6** | Six-layer multi-story chain — see §5 and §6 |
| 8 | **A2** | Armenian-only positive validator + length caps for Story/Riddle/Calm + deterministic child-safe output checks |
| 9 | **A3** | Track benchmark run artifacts in git; resolve the Game-has-no-choices contradiction (human decision) |
| 10 | **D1** | Add `Jwt:Key` to the clean-clone setup block |

### Tier 3 — hardware-verification blockers (need a board, a room, and a session)

| # | Item | Work |
|---|---|---|
| 11 | **C1** | Record an actual voice-chain run. **Criterion needs rewording first** — "wake" describes a capability explicitly descoped |
| 12 | **A4** | Measure barge-in stop latency against the 1s bar; resolve the "no barge-in in C1 path" contradiction |
| 13 | **C3** | Bench-test Wi-Fi drop and recovery |
| 14 | **C4** | Measure dB(A) at a stated distance in a stated room, with the instrument named |
| 15 | **A1** | Re-run start-to-finish with an **approved** story on the default code path |

### Tier 4 — human-content and external-party blockers (longest lead time; start now, finish last)

| # | Item | Work |
|---|---|---|
| 16 | **A5** | Find a native Eastern Armenian adult who is not you; 10 minutes of audio; get it in writing |
| 17 | **A6 content half** | `anban-huri` TTS listen test → human promotion; render/verify a 3rd story's MP3 |
| 18 | **D3** | Measure real cost per hour of play against an actual invoice |

**Note on sequencing:** items 16 and 17 gate the *completion* of A5 and A6 but need
no code. They have the longest lead time of anything on the board. Starting them in
parallel with Tier 1 costs nothing and saves weeks.

---

## 4. Document conflicts

### 4.1 `SHIP.md` vs `docs/launch-readiness-roadmap.md` — two finish lines

- **SHIP.md:3** — "**This file is the finish line.** Nothing else is."
- **SHIP.md:80** — flags the conflict itself: "`docs/launch-readiness-roadmap.md` also describes a path to launch. Decide which of the two is authoritative."
- **`launch-readiness-roadmap.md:30`** — defines "Gate 1 — supervised beta … Parent in the room, hosted backend, TLS, legal sign-off" with 12 NOW items.

**Consequence:** the roadmap's Gate 1 requires TLS, a host, Docker validation, email
delivery, and COPPA/GDPR counsel review — **none of which appear on SHIP.md**, and
several of which `docs/v2-backlog.md:47-50` explicitly *cuts* from v1. Following both
documents means doing strictly more work than either requires.

**Recommended interpretation:** SHIP.md is v1 ("ship it to one real child" — yours,
on the bench). The roadmap is the post-v1 map to a *supervised beta with someone
else's child*, which is a different and later product milestone. They are compatible
if read as sequential, contradictory if read as parallel.

**Human approval required: YES.** This is SHIP.md Open Question #1 and an agent must
not resolve it.

### 4.2 `docs/v2-backlog.md` says no mobile app exists — it does

- **`v2-backlog.md:20-22`** — "The pairing/presence/device-management backend exists … but **no app is built** and none is needed for v1.0."
- **Reality:** `mobile/AregParent/` has 32 tracked files. `mobile/AregParent/README.md:7-22` documents working sign-in, device pairing, presence, activity, flagged view, controls, and data export.

**Consequence:** low practical risk — the *decision* (no further app work in v1) is
unaffected. But the statement is factually wrong, and a future reader planning from
it would conclude an app must be built from scratch.

**Recommended interpretation:** the cut stands; the sentence should read "an MVP app
exists at `mobile/AregParent/`; no further work for v1.0."

**Human approval required: YES** — only the human edits `v2-backlog.md`.

### 4.3 A6 vs the cache-first promotion deferral

- **`SHIP.md:27-28` (A6)** — "choose from at least three cached Armenian stories on SD, using `/content_index.json`."
- **`v2-backlog.md:64-66`** — "**Promote `AREG_STORY_SD_CACHE_FIRST` to default.** … the flag stays opt-in until deliberately promoted."

**Consequence:** `/content_index.json` is read **only** inside
`#ifdef AREG_STORY_SD_CACHE_FIRST` (`AregVoiceMvp.ino:544-592`). With the flag off,
the index file is not even parsed — the includes for it are themselves gated at
`:34-37`. So A6 cannot be satisfied while the flag stays opt-in. The two documents
are in direct tension.

**Recommended interpretation:** not a real contradiction — promotion is simply *part
of* the A6 slice rather than an independent decision. Say so explicitly when the
slice lands, so the backlog entry can be retired rather than silently violated.

**Human approval required: YES** — promoting the flag changes default device
behavior.

### 4.4 Conflicting historical test counts — now resolved

| Count | Source | Nature |
|---|---|---|
| **2013** | `CLAUDE.md:40` | Claim — **confirmed accurate today** |
| ~2,013 | `launch-readiness-roadmap.md:22` | Cites CLAUDE.md |
| 1358 | `tools/quality-evidence/openai-daily-cost-cap-smoke-20260518.md:59` | Runner output, 2026-05-18 |
| 1336 | `docs/areg-current-readiness-evaluation.md:91` | Runner output, 2026-05-18 |
| 1314 | `docs/day-quality-hardening-report.md:188` | Runner output |
| 1277 | `tools/StoryModelBakeoff/evaluations/night-audit-20260505.md:377` | Runner output |

**Consequence:** none going forward. The older numbers are honest historical
snapshots, not errors. `docs/areg-current-readiness-evaluation.md` (2026-05-18) is
the genuinely stale document — `launch-readiness-roadmap.md:38-48` already catalogues
four other ways it is wrong.

**Recommended interpretation:** 2013 is correct as of 2026-07-26. Mark
`areg-current-readiness-evaluation.md` superseded rather than deleting it.

**Human approval required: NO** — factual, now measured.

### 4.5 Additional conflict found during this audit: barge-in

- **`esp32/AregVoiceMvp/audio_io.cpp:412-428`** — true barge-in, cuts per decode iteration.
- **`esp32/AregVoiceMvp/README.md:248`** — "Button presses during UPLOADING / PLAYING / ERROR are ignored. **No barge-in.**"

Both are true of *different paths*: barge-in works in the story session, and is
absent in the C1 voice-chat turn. The README does not say which. This directly
affects how A4 is read.

**Human approval required: NO** — a documentation fix, but someone must decide
whether A4 refers to the story path (mostly satisfied) or the voice path (not
implemented).

---

## 5. A6 technical assessment

### Current end-to-end flow, with single-story assumptions marked

```
[1] backend story library
    2 approved: little-cloud, hedgehog-apple        ◄── SINGLE-STORY-ADJACENT
    anban-huri = "status": "draft", not runtime-served
    InMemoryCuratedStoryLibrary.cs:41  SelectDefault() → pinned to LittleCloudId
        │
[2] backend content manifest
    ContentSyncOptions.cs:17-46   StoryId/Version/Sha256/SizeBytes — SCALARS  ◄── HARD CAP: 1
    ContentManifestService.cs:28  return new ContentManifestResponse(new[]{ ... })  ◄── HARD CAP: 1
    GET /api/devices/content-file → streams the ONE ContentSync:AudioPath  ◄── HARD CAP: 1
    (wire DTO ContentManifestResponse.Stories IS a list — the only plural layer)
        │
[3] firmware content sync
    content_sync.cpp:138  JsonArray stories = doc["stories"]
    content_sync.cpp:148  JsonObject item = stories[0];   ◄── HARD CAP: 1 (no loop)
        │
[4] SD cache index
    content_sync.cpp:317-333  writes a FLAT OBJECT:
      {"storyId":…,"version":…,"sha256":…,"file":…,"sizeBytes":…}   ◄── HARD CAP: 1
    Rewritten wholesale each sync → a 2nd story overwrites the 1st
        │
[5] story selection
    ── DOES NOT EXIST ──                                            ◄── ABSENT
    No shuffle, no no-repeat, no last-played, in firmware or backend
    docs/screenless-story-selection-design.md:3 "No code yet."
        │
[6] path resolution
    AregVoiceMvp.ino:553  story_resolve_cache_path(char *out, size_t out_len)  ◄── no storyId param
    AregVoiceMvp.ino:576  if (strcmp(sid, AREG_STORY_ID) != 0)               ◄── compile-time id
    Entire block inside #ifdef AREG_STORY_SD_CACHE_FIRST (:544-592)
        │
[7] playback
    audio_play_story_file(path, offset, …) — story-agnostic          ◄── ALREADY FINE
```

**Six independent hard caps.** Layer 7 is the only one that needs no change; the
wire DTO at layer 2 is the only thing already plural.

### Claim-by-claim verdicts

| # | Claim | Verdict | Key evidence |
|---|---|---|---|
| 1 | `AREG_STORY_ID` hard-coded | **CONFIRMED** | `config.h.example:64-66`; used at `.ino:576/626/682`, `voice_client.cpp:478/536/659`. Also embedded literally in `AREG_STORY_AUDIO_URL` (`config.h.example:59`) |
| 2 | Sync handles 0-or-1 manifest item | **CONFIRMED** | `content_sync.cpp:148` `JsonObject item = stories[0];` — array parsed, `.size()` logged, no loop anywhere |
| 3 | Cache resolution tied to one story | **CONFIRMED** | `.ino:553` signature has no id param; `:576` compares against the compile-time macro |
| 4 | `content_index.json` can't drive 3+ | **CONFIRMED** | `content_sync.cpp:317-333` writes a flat object, not an array; both readers assume flat |
| 5 | No no-repeat-last-one selection | **CONFIRMED** | Repo-wide search for shuffle/noRepeat/lastPlayed/nextStory: no matches in firmware or backend |
| 6 | Only 2 approved runtime stories | **CONFIRMED** | `Stories/Content/` = `hedgehog-apple` + `little-cloud`, both `"status": "approved"` at line 19 |
| 7 | `anban-huri` pending listen test | **CONFIRMED** | `anban-huri.story.json:27-29` `"status": "draft"`, `"listenTestAt": null`. Bench side-load exists via `run-local.ps1:24-25` with `requireApproved: false` |
| 8 | MP3s/sync paths incomplete for 3 stories | **CONFIRMED — worse than stated** | MP3s exist for **2** (`anban-huri`, `little-cloud`); `hedgehog-apple` has **none**. Only `anban-huri` is SD-wired |
| 9 | Cache-first flag required for A6 | **PARTIALLY CONFIRMED** | Required for *cache-first* playback and for reading the index at all. **Not** required for SD playback per se — the content-pack path `/stories/<id>/narration.mp3` plays with the flag off |

**Also confirmed:** no test anywhere covers a multi-item manifest.
`ContentManifestServiceTests.cs` asserts only `Assert.Single` (`:73`, `:87`) or
`Assert.Empty` (`:35`, `:44`, `:51`, `:57`, `:64`) — verified directly.

### Smallest sequence that satisfies A6

Nothing here adds tiers, entitlements, per-device manifests, eviction, retirement, a
spoken menu, or NFC — all of which are v2.

1. **Backend config → list.** `ContentSyncOptions` gains a `Stories[]` collection;
   `ContentManifestService` loops instead of emitting a fixed 1-element array;
   `GET /api/devices/content-file` takes a `storyId` parameter.
2. **Firmware sync → loop.** Iterate `stories[]`; per-item `.part` → verify → rename.
3. **Index → array.** `{"stories":[{...},{...},{...}]}`; update both readers.
4. **Resolver → parameterized.** `story_resolve_cache_path(const char *story_id, …)`.
5. **Selection → minimal.** Pick from the index, excluding the last-played id
   (persist one id in NVS). This is the whole of "no back-to-back repeat."
6. **Promote `AREG_STORY_SD_CACHE_FIRST`** to default (or fold the index read out of
   the `#ifdef`).
7. **Content (human, parallel):** promote `anban-huri`; render + verify a third MP3.

---

## 6. Recommended next slices

**I do not recommend starting with the A6 chain.** The evidence changed my ordering:
a live unauthenticated unmoderated GPT endpoint is a child-safety defect in shipping
code, and `CLAUDE.md`'s first product constraint is "Safety-first. Dual moderation …
Never bypass safety checks." A6 is a missing feature; B1 is a broken promise. Two
near-free SHIP closures (D2, C2) also outrank it on cost-to-value.

The user-proposed order (`content-manifest-multi-story` → `content-sync-multi-item` →
`story-select-from-index`) is **correct as an internal ordering** — backend must
precede firmware, and selection must follow the index — and I adopt it verbatim as
slices 4–6. I am only inserting cheaper, higher-severity work ahead of it.

### Slice 1 — `moderate-story-qa-text` *(recommended first)*

- **Goal:** close the confirmed moderation bypass on `POST /api/story-qa-text`.
- **Files:** `backend/src/ArmenianAiToy.Api/Controllers/StoryQaTextController.cs`; possibly `Program.cs` (environment gate); new test file.
- **Tests:** unsafe question → never reaches GPT; unsafe answer → fallback not the answer; endpoint absent or 404 outside Development if gated. Mirror the assertions in `StoryQaControllerModerationTests.cs`.
- **Acceptance:** the text harness and the voice path have identical moderation behavior for identical input, pinned by a test.
- **Risks:** LOW. Additive; no shared-path change. Note `LibraryStoryQuestionService` has no moderation dependency today — either inject `IModerationService` into the controller (smaller diff, keeps the service pure) or gate the endpoint out of production entirely (smallest diff of all).
- **Must NOT include:** touching `ChatService`, the shared Q&A service's prompt logic, or the voice path. No new moderation abstraction.

### Slice 2 — `ship-md-status-record` *(human-gated, minutes)*

- **Goal:** record `2013` at SHIP.md:54 and transcribe the §7 statuses.
- **Files:** `SHIP.md` — **human edits only**, per SHIP.md:7.
- **Acceptance:** every item carries a status and an evidence pointer.
- **Risks:** none.
- **Must NOT include:** an agent writing to SHIP.md.

### Slice 3 — `delete-legacy-esp32-sketch` *(destructive, needs approval)*

- **Goal:** close C2 via the criterion's own "or deleted" branch.
- **Files:** remove `esp32/ArmenianAiToy/` (2 tracked files); update `docs/esp32-chain.md:44`.
- **Acceptance:** `git ls-files esp32/ArmenianAiToy/` is empty; no doc references a working legacy sketch.
- **Risks:** LOW — it cannot authenticate against the current backend, and its `config.h` holds only GPIO constants (verified, no secrets). Recoverable from history.
- **Must NOT include:** touching `esp32/AregVoiceMvp/`.

### Slice 4 — `content-manifest-multi-story` *(first A6 slice)*

- **Goal:** backend serves N stories on the existing wire shape.
- **Files:** `ContentSyncOptions.cs`, `ContentManifestService.cs`, `DeviceController.cs` (content-file `storyId` param), `appsettings.json`; `ContentManifestServiceTests.cs`, `DeviceControllerContentSyncTests.cs`.
- **Tests:** 3-item manifest returns 3 items in order; per-item fail-closed validation (bad sha/size drops **that item**, not the whole manifest); `content-file?storyId=` serves the right file and 404s fail-closed on unknown id; empty config still yields an empty manifest.
- **Acceptance:** a 3-story config produces a 3-item manifest; each is independently downloadable; disabled/unconfigured still fail-closed.
- **Risks:** MEDIUM — config shape change. Ships `Enabled=false`, so no device behavior changes until enabled. Keep the single-item config readable (or migrate it) so the local bench block does not break.
- **Must NOT include:** per-device entitlement, tiers, story packs, auth changes, retirement semantics.

### Slice 5 — `content-sync-multi-item`

- **Goal:** firmware syncs all manifest items and writes an array index.
- **Files:** `esp32/AregVoiceMvp/content_sync.cpp` / `.h`.
- **Tests:** bench-only (no firmware harness exists) — a documented bench run: 3 items downloaded, each sha-verified, index contains 3 entries, re-boot is idempotent, one bad item does not corrupt the others.
- **Acceptance:** 3 MP3s on SD, `/content_index.json` lists 3, second boot re-downloads nothing.
- **Risks:** MEDIUM — SD write patterns and the atomic `.part` → rename discipline must be preserved per item. Watch flash/heap with a larger JSON document.
- **Must NOT include:** eviction, resume, retirement, playback changes.

### Slice 6 — `story-select-from-index`

- **Goal:** parameterize resolution and add no-repeat-last-one.
- **Files:** `AregVoiceMvp.ino` (`story_resolve_cache_path`, selection, NVS last-played), possibly `ota_state.cpp` pattern for NVS.
- **Tests:** extend the existing `AREG_STORY_SD_FALLBACK_TEST_BENCH` harness — it already exercises the real resolver, which makes it the right place.
- **Acceptance:** three consecutive presses never repeat back-to-back; a missing/corrupt index still falls back safely (the existing B/E/C fallback tests must still pass).
- **Risks:** MEDIUM-HIGH — this is the slice that touches the live story session. Promoting `AREG_STORY_SD_CACHE_FIRST` to default changes production behavior and needs explicit sign-off.
- **Must NOT include:** spoken menu, "surprise me" gesture, NFC, LED vocabulary, bedtime-aware filtering — all Phase 2 in `screenless-story-selection-design.md`.

**Dependency warning:** slice 6 cannot be *verified* against A6's "at least three"
without three real cached stories. Today there are two approved stories and two MP3s,
only one of which is SD-wired. **Start the human content track (listen test +
promotion + third MP3) in parallel with slice 1**, or slices 4–6 will land untestable.

---

## 7. Human decisions required

1. **Which document is the v1 finish line — `SHIP.md` or `docs/launch-readiness-roadmap.md`?**
   *Recommendation:* SHIP.md for v1; the roadmap becomes the post-v1 beta map.
   *Consequence if unresolved:* the roadmap's TLS/host/legal items keep re-entering
   v1 planning through the side door, and `v2-backlog.md:47-50` — which cuts exactly
   those — is contradicted every time.

2. **Is the GitHub repository public or private?** *(cannot be determined offline)*
   *Recommendation:* check immediately; it sets B4's severity.
   *Consequence:* if public, real device credentials and a home Wi-Fi password have
   been world-readable since 2026-04-24. If private, exposure is bounded to
   collaborators — still a rotation item, not an incident.

3. **Were the leaked device key and Wi-Fi password rotated?**
   *Recommendation:* assume not, and rotate both; record the rotation.
   *Consequence:* an unrotated device key is a live credential in git history.

4. **Should `/api/story-qa-text` be moderated, authenticated, or removed from production builds?**
   *Recommendation:* moderate it *and* gate it to Development. It is a dev harness by
   its own docstring; there is no reason for it to exist in a production image.
   *Consequence:* left as-is, it is an unauthenticated public GPT relay attached to
   your OpenAI key, outside the daily cost cap's device keying.

5. **A4: does "interrupting mid-speech" mean the story path or the voice-chat path?**
   *Recommendation:* story path for v1 — it is where barge-in is implemented and
   where a child listening to a story would interrupt.
   *Consequence:* read as the voice path, A4 is not implemented at all, not merely
   unmeasured.

6. **C1: "wake" does not exist — reword the criterion or add a wake word?**
   *Recommendation:* reword to "button press → listen → …". A wake word is a
   substantial feature explicitly descoped at `AregVoiceMvp.ino:11`.
   *Consequence:* as written, C1 can never pass regardless of bench work.

7. **A3: Game mode has no choices by design — is the "10 saved runs per mode" bar coherent for Game?**
   *Recommendation:* reword A3's Game half to its turn-taking loop instead of
   choice-following.
   *Consequence:* as written it demands evidence of a mechanic the product
   deliberately does not have.

8. **Should benchmark run artifacts be tracked in git?**
   *Recommendation:* yes for the runs cited as SHIP evidence.
   *Consequence:* today all 23 Story runs are gitignored (`.gitignore:4`), so A3's
   evidence exists only on this one machine and vanishes with a clean checkout.

9. **May `AREG_STORY_SD_CACHE_FIRST` be promoted to default as part of A6?**
   *Recommendation:* yes — A6 cannot be satisfied otherwise (see §4.3).
   *Consequence:* changes default device behavior; `v2-backlog.md:64` should be
   retired at the same time rather than silently violated.

10. **Does "full test suite" include the 3 projects outside `ArmenianAiToy.slnx`?**
    *Recommendation:* add `tools/ContentPackBuilder.Tests` and
    `tools/StoryInteractiveLoop.Tests` to the solution and CI; decide separately
    whether the Python file at `tests/engines/` is live or dead.
    *Consequence:* 122 tests currently run in neither CI nor the number you would
    record for D2.

---

## 8. Proposed `SHIP.md` patch — **NOT APPLIED**

Shown for review only. Per `SHIP.md:7` ("Only the human edits this file"), no agent
should apply this. Statuses reflect §2; evidence pointers are abbreviated to fit.

```diff
 ## A. The child's experience

-- [ ] **A1.** A child can start the toy and hear a full story in Armenian,
+- [ ] **A1.** `PARTIAL` — CLAUDE.md:2293 bench 2026-07-12; but draft story,
+      opt-in flag, approved stories untested on hardware.
       start to finish, with no crash and no English leaking in.
-- [ ] **A2.** Every shipped mode passes deterministic Armenian quality checks:
+- [ ] **A2.** `PARTIAL` — no-Latin universal; length 2/5 modes;
+      Armenian-only 0/5; child-safe output 0/5. ResponseQualityGate.cs
       Armenian-only output, child-safe wording, no Latin/English leakage, and
       length limits.
-- [ ] **A3.** Story and Game continuation works: the turn after a choice actually
+- [ ] **A3.** `PARTIAL` — Story 23 runs (gitignored); Game 0 runs.
       follows that choice, verified on at least 10 saved runs per mode.
-- [ ] **A4.** Interrupting mid-speech stops the audio within one second and the
+- [ ] **A4.** `PARTIAL` — barge-in real (audio_io.cpp:412); stop latency
+      NEVER MEASURED; README:248 says no barge-in on the C1 path.
       toy responds to what the child said.
-- [ ] **A5.** A native Eastern Armenian adult, not you, listens to 10 minutes of
+- [ ] **A5.** `NOT STARTED` — no external human note exists.
       output and says it sounds natural. Written note from them counts as evidence.
-- [ ] **A6.** The toy can choose from at least three cached Armenian stories on SD,
+- [ ] **A6.** `NOT STARTED` — single-story at 6 layers; 2 approved
+      stories; 2 MP3s; no selection logic. See docs/ship-md-evidence-audit.md §5.
       using `/content_index.json`, without repeating the same story back-to-back.

 ## B. Safety

-- [ ] **B1.** Every response path passes moderation. Proven by a test that fails
+- [ ] **B1.** `PARTIAL` — 9/11 paths moderated; CONFIRMED BYPASS at
+      StoryQaTextController.cs (unauth, unmoderated); no structural test.
       if a path bypasses it.
-- [ ] **B2.** A saved adversarial test suite passes scary-topic, secret-keeping,
+- [ ] **B2.** `PARTIAL` — 55-entry corpus; no scary-topic category;
+      30/44 unsafe entries have no behavioral assertion.
       and personal-information probes.
-- [ ] **B3.** Data retention is documented in one page a parent could read, and
+- [ ] **B3.** `NOT STARTED` — no parent-readable page exists; code side
+      consistent at 90 days.
       the code matches it.
-- [ ] **B4.** No secret has ever been committed. Verified by a history scan.
+- [ ] **B4.** `NOT STARTED` — PREMISE APPEARS FALSE: device key + Wi-Fi
+      creds in origin/main history (dfb5a44..dbb12dc, untracked 0794e9b).
+      No scan ever performed.

 ## C. The device

-- [ ] **C1.** ESP32-S3 completes the full chain: wake → listen → backend → TTS →
+- [ ] **C1.** `PARTIAL` — no recorded hardware voice turn; repo's own bar
+      is 3, unchecked. NOTE: no wake word exists (button only) — reword.
       speaker, ten times in a row without a manual reset.
-- [ ] **C2.** Device auth works and the legacy sketch's 401 problem is closed or
+- [ ] **C2.** `PARTIAL` — auth hardware-proven on OTA/heartbeat; legacy
+      sketch still tracked. Closable in one command.
       the legacy sketch is deleted.
-- [ ] **C3.** Wi-Fi drop mid-session recovers without the child needing an adult.
+- [ ] **C3.** `PARTIAL` — reconnect code exists, never hardware tested;
+      in-flight turn still lost (README:245).
-- [ ] **C4.** Audio is loud enough and clear enough in a normal room. Measured,
+- [ ] **C4.** `NOT STARTED` — no dB/SPL/distance measurement anywhere;
+      gain 0.6 of unity (audio_io.cpp:320).
       not guessed.

 ## D. Operability

-- [ ] **D1.** Backend runs from a clean clone with documented steps, on a machine
+- [ ] **D1.** `PARTIAL` — docs strong but Jwt:Key missing from setup
+      (clean clone crashes); never run on another machine.
       that is not yours.
-- [ ] **D2.** Full test suite green, and the count is recorded here: `____`
+- [ ] **D2.** `PARTIAL` — GREEN, measured 2026-07-26: 2013 passed,
+      0 failed, 0 skipped. Count now recorded here: `2013`
+      (3 test projects sit outside the solution and CI).
-- [ ] **D3.** Provider cost per hour of play is measured and written down.
+- [ ] **D3.** `NOT STARTED` — a cost CAP exists ($0.50/device/day);
+      no per-hour measurement anywhere.
-- [ ] **D4.** A one-page runbook exists: how to restart it, how to see logs, what
+- [ ] **D4.** `PARTIAL` — windows-service-deploy.md §11+§15 covers all
+      three, but ~910 lines and Windows-only; Docker path has no runbook.
       the three most likely failures look like.
```

---

## 9. Limitations of this audit

- **Repo visibility (public vs private) could not be determined** — no network calls
  were made. This gates B4's severity.
- **CI status on `feat/ota-apply` is UNKNOWN** — `gh` is not installed here, and
  `.github/workflows/ci.yml` triggers only on push-to-main or pull_request. The
  branch is 25 commits ahead of `origin/main`.
- **No hardware was exercised.** Every C-section and A4 judgement rests on documents
  and code, not a board. Items marked "never hardware tested" mean *no record exists*,
  not proof of failure.
- **The 2013-test run covers only `ArmenianAiToy.Application.Tests`** — the single
  test project in `ArmenianAiToy.slnx`. The 3 out-of-solution suites (122 tests) were
  not executed.
- **Leaked secret values were never read or printed** — only their kind, location,
  and commit range.
- **Story/Game benchmark runs were counted, not re-executed.** They require live
  OpenAI calls and would cost money; their reported contents are taken from the saved
  files.

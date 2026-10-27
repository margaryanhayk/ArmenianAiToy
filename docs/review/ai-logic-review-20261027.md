# AI pipeline logic review — 2026-10-27

**Branch:** `claude/release-path-20261022` (PR #72). **Owner's ask:** "code review and AI review — there are surely logic errors; do everything." **Scope:** the whole child-facing pipeline, not only the PR diff.
**Method:** six independent read-only reviewers (one per area) produced ~70 findings, each required to give a concrete input or state with `path:line` evidence; six independent refuters re-opened every cited line, reproduced the path end to end (the Armenian helpers were ported to Python and every existing test literal re-run through the fixed logic; BCrypt and timezone claims were executed with `dotnet fsi`; the OpenAI SDK surface was read from the installed DLL), and classified each finding. Raw findings and verdicts, with every `path:line`: `tools/quality-evidence/ai-logic-review-20261027/`.
**Classes:** `fix-now` = small, clearly correct, outside the hard-stop files, fixed in this PR with a test that fails on the old code; `plan-for-owner` = correct but inside `ChatService.cs`, the system prompt, a Domain entity, auth, the moderation adapter, a safety phrase list, or an owner decision — the exact diff is in §5 and waits for one word; `accept-document` = true, not worth code, documented where it bites; `drop` = refuted.

Areas: A **A** ChatService orchestration · **B** story-qa / device paths · **C** adapters, reliability, cost · **D** Armenian guards & detectors · **E** parent/device services, retention, auth · **F** firmware

## 1. Summary

| Class | Count | Ids |
|---|---|---|
| Fixed in this PR | 31 | A1 A4 A5 B1 B10 B11 B12 B3 B4 B7 B8 C2 C3 C4 C8 C9 D1 D10 D11 D2 D3 D4 D5 E14 E3 E4 E7 F1 F3 F5 F6 |
| Waiting on the owner's "go" | 22 | A10 A15 A2 A3 A7 A8 A9 B2 B5 C1 C5 C6 D6 D8 E1 E10 E2 E5 E8 E9 F2 F4 |
| Accepted and documented | 16 | A11 A12 A13 A14 A16 A6 B13 B6 C7 D12 D7 E11 E13 E15 E6 F7 |
| Refuted / dropped | 2 | B9 E12 |

Numbers that matter for SHIP.md: every fix-now item is a bug in shipped behaviour (the freeze allows it); none adds a child-facing feature. Four of the fixes are firmware (`F1 F3 F5 F6`) and cannot be compiled here — they need the bench evening's `arduino-cli` build on core 3.3.6 before they are real. Nothing in this review was verified on hardware or against a live provider.

## 2. Fixed in this PR (confirmed, outside the hard-stop files)

| Id | Severity | What was wrong (reproduced) |
|---|---|---|
| A1 | BUG | GetRecentMessagesAsync projects Role/Content with no SafetyFlag predicate; the prefilter, input-moderation and self-harm paths all persist the child's raw text as a Bloc… |
| A4 | LOGIC | All four gate sites nest the Global ceiling inside `else if (costCapOpts.Enabled)`, so with Usage:Tiers:Enabled=true the fleet kill-switch is never evaluated. UsageTiers… |
| A5 | LOGIC | Both audio gate sites pass the literal `childId: null` (AudioChatController.cs:217-218 and :554), which DeviceService.IsModeEnabledForRequestAsync:663-664 routes to the… |
| B1 | BUG | Traced end to end, but LATENT under the shipped config. OpenAITtsSynthesisService.cs:25 is `: IAudioSynthesisService` only (no IStreamingAudioSynthesisService), and apps… |
| B10 | RISK | DeviceService.UpdateFirmwareReportAsync :255-265 assigns FirmwareVersion, FirmwareBuild, BoardModel, PartitionName, LastOtaStatus and ContentStories verbatim; only Conte… |
| B11 | LOGIC | DeviceService.cs:489-495 applies GamePlayReportRequest.AllowedOutcomes (won/lost/stopped) to every game key; nothing special-cases who-first. The DTO's own doc (GamePlay… |
| B12 | LOGIC | StoryQaTextDevGate.cs:44-45 `path.Equals(Path, OrdinalIgnoreCase)` is an exact match; ASP.NET Core routing matches `[Route("api/story-qa-text")]` for `/api/story-qa-text… |
| B3 | LOGIC | Code claim holds, impact overstated, and the path is LATENT (firmware AREG_QA_STREAM_PLAYBACK is commented out in config.h.example:477; without the header StoryQaControl… |
| B4 | LOGIC | StoryQaController.cs:1494 `candidates = { CacheAudioPath, ShippedAudioPath }` and both loops (:1500-1508, :1513-) take the first hit. StoryAudioController writes the cac… |
| B7 (dup of A5) | LOGIC | Duplicate of A5. AudioChatController.cs:218-219 `IsModeEnabledForRequestAsync(deviceId, childId: null, voiceDetectedMode)` and :553-554 (`childId: null`); the controller… |
| B8 | LOGIC | Every transient_failure return exits before the cost block: StoryQaController.cs:419 (STT paid, model failed), :581 (STT+GPT paid, TTS failed) vs cost block :590-614; re… |
| C2 | LOGIC | OpenAIChatClientAdapter.cs:39-45 links the caller token and the 30 s ceiling into ONE token and hands that to `_gate.RunAsync` (its own comment: 'a caller abort is accou… |
| C3 | LOGIC | DeviceService.cs:704-721 reads the row, `row.Questions += 1; row.EstimatedUsd += ...`, SaveChangesAsync; the only catch is `DbUpdateException when (isNew)` (:723). AppDb… |
| C4 (dup of A4) | RISK | First half CONFIRMED at all four sites: ChatController.cs:128-160, AudioChatController.cs:562-583, StoryQaController.cs:195-213 and :828-845 share `if (usageTiersOpts.En… |
| C8 | RISK | Half refuted, half confirmed. 'No per-request ceiling' is wrong: DependencyInjection.cs:177 hands ElevenLabsTtsSynthesisService the shared `openAiHttpClient`, whose `Tim… |
| C9 | LOGIC | VertexAiAccessTokenProvider.cs:116-124: inside the last five minutes `now < _expiresAt - RefreshMargin` is false, so :126-140 posts a refresh and on any non-2xx throws H… |
| D1 | BUG | ModeDetector.cs:138-166 CuriosityStarters has no հարց* needle and nothing else in StoryTriggers/GameTriggers/RiddleTriggers/CalmTriggers matches it, so every natural ans… |
| D10 | BUG | ArmenianVoiceReplyGuard.cs:97-101 flags any ≥3-letter token with a 3-run of identical chars (:173-187) regardless of vowel/consonant, and ChatService.cs:1904 runs that g… |
| D11 | LOGIC | ModeDetector.cs:300-313 StartsWithWord accepts a needle only when followed by a space, '?', or end of string, and NormalizeForMatch (:255-269) strips only ՞՛՜, so a ques… |
| D2 | BUG | Reproduces, but the stated mechanism is off: it is not the double normalisation (ChatService.cs:2285 stores Normalize(answer), Match normalises again) — Match(«Շունը», r… |
| D3 | BUG | ChoiceNormalizer.cs:108-113 asks only whether ANY non-stop ≥3-char word of the input is a substring of each label; a verbatim repeat of label B shares «արջուկին» with A,… |
| D4 | LOGIC | ChoiceNormalizer.cs:72 TrailingPunctuation is Latin-only (RiddleAnswerMatcher.cs:31 and ChoiceDiversity.cs:21-25 do include U+0589), and :102-105 compares the Armenian p… |
| D5 | LOGIC | ArmenianVoiceReplyGuard.cs:54-58 lists '.' as a terminator and :122-131 counts every terminator char, so an ASCII '...' consumes the whole cap and the cut lands inside t… |
| E14 | LOGIC | RedeemDeviceInviteAsync computes alreadyLinked (ParentService.cs:668-670) but only skips the link; it still sets invite.ConsumedAt/ConsumedByParentId, re-stamps device.C… |
| E3 | RISK | Parents.Email has an UNFILTERED unique index (AppDbContext.cs:90 `HasIndex(p => p.Email).IsUnique()`; migration 20260420201336_Initial.cs:183, no later change). The anon… |
| E4 | LOGIC | The Device row used to be deleted on last unlink, and DeviceInvite has an FK cascade to Device (AppDbContext.cs:155-163) which is how invites got cleaned. The factory-re… |
| E7 | RISK | DeleteDormantDevicesAsync (RetentionPurgeService.cs:1181-1187) selects `LastSeenAt < dormantCutoff && DormancyWarnedAt != null && DormancyWarnedAt < deleteGraceCutoff` w… |
| F1 | BUG | audio_play_story_file writes (and then printf-reads) *out_resume_offset on a barge-in with no null guard. Two live callers pass story_barge_in_poll with out_resume_offse… |
| F3 | LOGIC | Code defect reproduced. s_games_index.clear() at content_sync.cpp:1898 runs before content_orphan_sweep_run() at :1909; sweep_dir(CS_GAMES_DIR, game_file_is_referenced)… |
| F5 | LOGIC | Traced end to end. Turn 12 is allowed to run (online_session_turn_cap_reached(12,12) is false, pinned by host test line 28); after its reply plays the loop unconditional… |
| F6 | LOGIC | All clip downloads (story clips, welcome/voice, music, game) go through download_file_verified, whose every failure calls story_fail (content_sync.cpp:886-1030), and sto… |

<!-- FIXED-TESTS -->

## 3. Refuted or dropped

| Id | Verdict | Why |
|---|---|---|
| B9 | REFUTED | The grep claim is wrong: StoryQaController.cs:33 carries `[EnableRateLimiting(ChatRateLimiter.PolicyName)]` at class level, covering both Ask (:135) and AnswerReflection (:778). Program.cs:235-242 registers the policy; the attribute is what binds it. The find… |
| E12 | CONFIRMED | True but practically never fires: AlertingService ticks immediately at start (ExecuteAsync -> RunTickAsync, no initial delay) while DatabaseBackupService waits InitialDelay = 1 min (DatabaseBackupService.cs:~91,102) before its first snapshot, so with an empty… |

Mechanisms the refuters corrected even where the bug was real: D2 is not double normalisation (a single `Match` call fails the same way — the suffix stripper peels a stem-final letter off the answer); D8's "stale pending choice" half is refuted (`ChatService.cs:1810` consumes the pending choice every turn); C4's "unparseable plan count refuses boot" half is pinned as intended behaviour (`UsageTiersOptionsTests:87`); C8's "no per-request ceiling" is wrong (the shared `HttpClient` has a 100 s timeout — the real gap is 100 s vs the toy's 30 s window); E11's DB half is refuted (`Migrate()` runs before the alerter's first tick); A6/B2 reproduce but are the documented "conservative mode detection" design.

## 4. Accepted and documented (true, not worth code now)

| Id | Severity | What is true | Where it is now written down |
|---|---|---|---|
| A11 | RISK | ChatController.cs:184-185 and AudioChatController.cs:297 use EstimateChatCostUsd(userMessage, reply); OpenAICostEstimator.cs:76-90 documents that this overload under-counts (measured ~33% of real spend) and that the Fro… | already in `OpenAICostEstimator.cs:76-90`; plan in §5 (C6) if accuracy is wanted |
| A12 | RISK | DeviceService.cs:703 (RecordUsageQuestionAsync dayUtc = nowUtc.Date) and :759-760 (allowance dayUtc/monthStartUtc) and ParentService.cs:2003-2007 (todayUtc) all key on UTC; ConversationService.GetTodaySummaryAsync:271-2… | `docs/ops-runbook.md` § cost cap and `docs/usage-tiers-brainstorm.md`: the allowance day resets at 00:00 UTC (04:00 Yerevan) |
| A13 | NONE | ChatService.cs:1792-1794 says a cancelled turn ends with 'NOTHING stored', and CLAUDE.md (Chat bullet + N11 section) repeats it; but Step 3 (1889) stores the child's User row before the cancellable model call (2198) and… | `CLAUDE.md` Chat bullet corrected: a cancelled turn stores no ASSISTANT row (the child's own row is stored before the model call, as on a model failure); `ChatService.cs:1792` comment is in §5 (comment-only, hard-stop file) |
| A14 | RISK | Output moderation runs at 2228; EnforceMaxSentences/ApplyTypoFixes (2836-2841) and the Calm/Curiosity punctuation rewrites (2846-2860) run afterwards. All three transforms are strictly reductive or deterministic substit… | this document only: the post-moderation transforms are reductive by contract; any new transform that can ADD text must move before Step 9 |
| A16 | RISK | ContinueLibraryStoryAsync (1753) and the continue-cue path (1645) call Advance() — LibraryStoryPlaybackService.cs:134-168 mutates the tracker (Advance, or Clear+MarkEnded on the final segment) — before RespondWithLibrar… | this document only: the library position is committed before the output check; closing it is a ChatService plan (§5 note under A16) |
| A6 | LOGIC | ChatGateEvaluator.cs:79-80 detects with history:null/hasActiveStorySession:false, so a non-trigger continuation («շուն», «ok») returns None → Allow; ChatService.cs:2062-2068 then continues Game/Riddle/Calm from ActiveMo… | `.claude/MODES.md` (Game transitions): a parent mode switch takes effect on the next trigger turn, at most 12 turns / 30 min into a live session |
| B13 | NONE | Latent by two flags. story_select.cpp:339-341 `#define AREG_VARIANT_ENDINGS_ENABLED 0` default; with it on, resolve_playback_path returns the alt cache path while s_current_story_id stays the base id (AregVoiceMvp.ino:1… | `esp32/AregVoiceMvp/README.md` variant-endings section: before `AREG_VARIANT_ENDINGS_ENABLED=1` ships, firmware must send `variant=1` and story-qa resolve the alt byte map |
| B6 | NONE | Latent. StoryQaController.cs:1017-1020 `else { spoken = closeAudio; }` and :1040-1043 `assistantText = ReflectionClose` ignore `isLast` for empty/blocked answers, contradicting the contract comment at :976-981. Shipped… | this document only: blocked/empty reflection answers always close today; shipped firmware always sends `last=true` |
| C7 (dup of A12) | NONE | Mechanically true: OpenAICostMeter.cs:171 `todayUtc = utcNow.Date`; DeviceService.cs:704 and :760 `dayUtc = nowUtc.Date`; both reset at 00:00 UTC = 04:00 Yerevan. But the only parent-facing quota 'today' (the 'N of M qu… | same note as A12 |
| D12 | LOGIC | ResponseQualityGate.cs:85-93 fires on the first subject keyword present anywhere in the user message, so a mentioned-but-rejected subject costs one extra model call plus one extra moderation call (ChatService.cs:2490-25… | this document only: a mentioned-and-negated subject costs one retry by design; an Armenian negation-scope rule is its own helper |
| D7 | RISK | SelfHarmSignal.cs:36 «չեմ ուզում ապրել» is a plain substring match (:88), so a locative continuation («…ապրել գյուղում») blocks with the grown-up line, Blocked/Flagged (ChatService.cs:1817-1823, :1530-1531). The negativ… | this document only: the negative-polarity locative («չեմ ուզում ապրել գյուղում») blocks with the calm line — accepted precision cost; a right-context exclusion is a story-master item |
| E11 | LOGIC | Mechanically true for the AUDIO half only: `_lastAudioStoreOk` starts null and alerts only on a change (AlertingService.cs:244-262), so a deploy with Audio__BlobStoreRoot unset never alerts. That is documented design (o… | `docs/ops-runbook.md` § Alerting: the alerter reports state CHANGES; a state already bad at boot is covered by the startup warning, `/api/health` and the console |
| E13 | LOGIC | The wire `bootCount` is `content_sync_fail_streak()` (voice_client.cpp:417-420), a counter the firmware bumps BEFORE each sync attempt and clears only on a clean pass (content_sync.cpp:2126-2135, 2251), not a boot count… | this document only: heartbeat `bootCount` is the firmware's unclean-sync streak, so E-501 can appear on a stable toy after a fault reset; real fix is a firmware field (cable flash) |
| E15 | RISK | Both ClaimDeviceAsync (ParentService.cs:477-499) and RedeemDeviceInviteAsync (:670-690) read `CountAsync` then Add then SaveChanges with no transaction and no DB constraint (ParentDevice's key is (ParentId, DeviceId) on… | this document only: two parents racing for the last seat can both land (no transaction); negligible |
| E6 | LOGIC | Verified with `dotnet fsi` on this host: for Asia/Beirut, local midnight 2026-03-29 is invalid and TimeZoneInfo.ConvertTimeToUtc throws ArgumentException; it throws for dayStartLocal on the gap day (ConversationService.… | this document only: `ConvertTimeToUtc` throws on a DST-gap midnight, unreachable while no client sends `tz` and `Device.TimeZone` has no setter |
| F7 | RISK | The code path exists exactly as described and it breaks the invariant the comment at :655-659 claims ('at no instant are both the primary and the backup absent'): write_index unconditionally runs `if (SD.exists(kIndexBa… | this document only: two correlated rename failures in one sync attempt can leave no index (next boot re-downloads ~117 MB, nothing corrupts); the guard for the next `write_index` edit is in `verdicts-F.json` |

## 5. Waiting on the owner's "go" (hard-stop files or owner decisions — exact diffs)

Reply with `go <id>` per item; each is implemented with the test named, in a separate commit. Ordered by what the child would notice first.

### A15 — RISK — hard-stop file

**Reproduced:** OpenAIModerationAdapter.cs:178-182 maps only Sexual/Violence/SelfHarm/Hate/Harassment; the installed OpenAI SDK 2.9.1 (both csproj:15) exposes Illicit, IllicitViolent, SelfHarmIntent, SelfHarmInstructions, ViolenceGraphic, SexualMinors, HateThreatening, HarassmentThreatening on ModerationResult (verified in /root/.nuget/packages/openai/2.9.1/lib/net10.0/OpenAI.dll: get_Illicit, get_IllicitViolent, …). DI default is omni-moderation-latest (DependencyInjection.cs:90), which emits illicit/illicit-violent; the adapter never reads them, so an illicit-only hit returns IsSafe=true and only DangerousInputFilter (ChatService.cs:1825-1827) stands. Whether 'how to make a bomb' lands in illicit/violent specifically is not verified live here (no OpenAI key; no evidence file mentions illicit); the comment at 1825-1827 may itself be an artefact of this gap.

**Path:** `DependencyInjection.cs:90 omni-moderation-latest → OpenAIModerationAdapter.cs:172-182 (5 categories read) → 211 IsSafe = flagged.Count==0 → ChatService.cs:1852 inputModeration.IsSafe true → model call`

**Fix:**
```
backend/src/ArmenianAiToy.Infrastructure/OpenAI/OpenAIModerationAdapter.cs after line 182 (null-safe: text-moderation-* models omit these keys):
+        if (categories.Illicit?.Flagged == true) flagged.Add("illicit");
+        if (categories.IllicitViolent?.Flagged == true) flagged.Add("illicit/violent");
+        if (categories.SelfHarmIntent?.Flagged == true || categories.SelfHarmInstructions?.Flagged == true)
+            if (!flagged.Contains("self-harm")) flagged.Add("self-harm");
+        if (categories.ViolenceGraphic?.Flagged == true && !flagged.Contains("violence")) flagged.Add("violence");
+        if (categories.SexualMinors?.Flagged == true && !flagged.Contains("sexual")) flagged.Add("sexual");
The soleViolence override (185-186) is unaffected (count>1 disables it). Extend the Scores log line if desired. Before merging: one live probe of «how to make a bomb» via tools/safety/moderation_recall.py to record which category fires.
```

**Test:** OpenAIModerationAdapterTests: IllicitViolentOnly_IsUnsafe using OpenAIModerationsModelFactory.ModerationResult(...) (or raw-JSON deserialization) with illicit/violent=true and the five mapped categories false; plus a text-moderation-shaped result with those properties null → no NRE.

### C1 — BUG — hard-stop file

**Reproduced:** Adapter reads exactly 5 categories (OpenAIModerationAdapter.cs:178-182) and returns `new AppModerationResult(flagged.Count == 0, flagged)` (:203); the sole-violence override (:188) is computed over those 5 only. SDK 2.9.1 verified by `strings` on OpenAI.dll: get_Illicit, get_IllicitViolent, get_SexualMinors, get_SelfHarmIntent, get_SelfHarmInstructions, get_HarassmentThreatening, get_HateThreatening, get_ViolenceGraphic, get_Flagged all present, and OpenAI.xml's `OpenAIModerationsModelFactory.ModerationResult(System.Boolean, 13 x ModerationCategory)` pins that the result type carries a top-level flagged bool plus 13 categories. So an illicit-only / sexual-minors-only / self-harm-intent-only / threatening-only / graphic-only result is IsSafe=true, and IllicitViolent+low Violence with no keyword hits the override and is lifted. Routing is an exact element match on "self-harm" (ChatService.cs:1865, StoryQaController.cs:363), so SelfHarmIntent never reaches the grown-up line (SelfHarmSignal, N16, covers listed phrases only). No test pins the mapping: every moderation test overrides the virtual `ClassifyOnceAsync` seam (ModerationFailClosedTests:162-182) or calls `ShouldOverrideViolenceBlock` directly (ModerationArmenianOverrideTests:34). The class comment (:27 'keep the Flagged boolean for ALL categories') shows the intent was all categories; the 5-category read predates omni-moderation's 13.

**Path:** `OpenAIModerationAdapter.cs:172-203 -> IModerationService -> ChatService.cs:1860-1865 / StoryQaController.cs:363; SDK: /root/.nuget/packages/openai/2.9.1/lib/net10.0/OpenAI.dll + OpenAI.xml model-factory signature`

**Fix:**
```
HARD-STOP FILE (moderation adapter). Exact diff for OpenAIModerationAdapter.cs after line 182, BEFORE the override at :188 (the override clears the list for a sole-violence false positive while top-level Flagged is true, so the catch-all must precede it). Map self-harm sub-categories to the exact string "self-harm" (keeps the Contains routing); give every other new category a distinct name so `soleViolence` (flagged.Count==1 && flagged[0]=="violence") can only ever match the plain violence category:
```
         if (categories.Sexual.Flagged) flagged.Add("sexual");
+        if (categories.SexualMinors.Flagged) flagged.Add("sexual/minors");
         if (categories.Violence.Flagged) flagged.Add("violence");
+        if (categories.ViolenceGraphic.Flagged) flagged.Add("violence/graphic");
         if (categories.SelfHarm.Flagged) flagged.Add("self-harm");
+        if (categories.SelfHarmIntent.Flagged) flagged.Add("self-harm");
+        if (categories.SelfHarmInstructions.Flagged) flagged.Add("self-harm");
         if (categories.Hate.Flagged) flagged.Add("hate");
+        if (categories.HateThreatening.Flagged) flagged.Add("hate/threatening");
         if (categories.Harassment.Flagged) flagged.Add("harassment");
+        if (categories.HarassmentThreatening.Flagged) flagged.Add("harassment/threatening");
+        if (categories.Illicit.Flagged) flagged.Add("illicit");
+        if (categories.IllicitViolent.Flagged) flagged.Add("illicit/violent");
+        // Fail closed on any category this mapping does not know.
+        if (categories.Flagged && flagged.Count == 0) flagged.Add("other");
```
FlaggedCategories is only ever joined into log strings or matched on "self-harm"/"moderation_unavailable" (grep of src), never a metric tag, so the new names add no cardinality. Extend the :195-200 log line with the new scores or leave it. Extract the mapping into `internal static List<string> MapFlaggedCategories(ModerationResult r)` so it is testable without the network.
```

**Test:** New ModerationCategoryMappingTests using `OpenAIModerationsModelFactory.ModerationResult(flagged:true, ...)` + `ModerationCategory(flagged, score)`: a Theory with each of the 13 categories flagged alone -> IsSafe false; SelfHarmIntent-only and SelfHarmInstructions-only -> contains "self-harm"; flagged:true with all 13 false -> ["other"]; IllicitViolent + Violence(score 0.1, input 'tell me a story') -> still blocked (override not applied); plain Violence(0.35, no keyword) alone -> still overridden (regression guard for the existing false-positive path).

### A2 — BUG — hard-stop file

**Reproduced:** The outer guard `if (safetyFlag != SafetyFlag.Flagged)` at 2488 is evaluated once. Inside it, 10c-quater (2683-2698) and the rejected-retry branch (2648-2660) set aiResponse = fallback + Flagged. 10c-sexto (2723) tests only `choiceLabel is not null && IsMissingReference(choiceLabel, aiResponse)`; ContinuationFidelity.cs:41-51 returns true whenever no >=4-char Armenian label token appears in the text — the fallback «Արի, մի ուրիշ հետաքրքիր բան խոսենք։» shares no token with a typical label, so a THIRD model call runs with the fallback as history; if moderation passes, 2770 `aiResponse = fidelityResp` and 2762-2766 re-populate PendingChoices/choiceA/B, with no latin_run recheck afterwards and safetyFlag still Flagged (so 10c-final-coh is skipped). Existing tests QualityGate_LatinRun_*_FallsBackToSafety (ChatServiceTailBlockTests.cs:207-262) run without a selectedChoice, so choiceLabel is null and the path is uncovered.

**Path:** `ChatService.cs:2488 (outer guard, once) → 2507 retry → 2648-2660 or 2683-2698 (aiResponse=fallback, safetyFlag=Flagged, choices cleared) → 2723 condition lacks safetyFlag check → ContinuationFidelity.cs:41-51 true → 2737 third GetCompletionAsync → 2743 moderation → 2762-2770 choices repopulated, aiResponse=fidelityResp → 2788 block ends, no recheck → 2871 stored Flagged with model prose`

**Fix:**
```
backend/src/ArmenianAiToy.Application/Services/ChatService.cs:2723
-            if (choiceLabel is not null && ContinuationFidelity.IsMissingReference(choiceLabel, aiResponse))
+            if (safetyFlag != SafetyFlag.Flagged
+                && choiceLabel is not null
+                && ContinuationFidelity.IsMissingReference(choiceLabel, aiResponse))
(10c-quinto at 2707 is harmless after a fallback — choices are already null — no change needed there.)
```

**Test:** ChatServiceTailBlockTests: QualityGate_LatinRunPersists_WithExplicitChoice_NoFidelityRetry — turn 1 stores CHOICE_A/B; turn 2 GetResponseAsync(deviceId, "A", selectedChoice:"A") with model returning Latin-run text twice; assert Received(2) GetCompletionAsync (not 3), Response == fallback, SafetyFlag Flagged, ChoiceA/B null.

### A3 — LOGIC — hard-stop file

**Reproduced:** SecrecyPromiseGuard is referenced only at ChatService.cs:2215-2218 (Step 8.9, on the primary reply). The quality retry adopts retryRaw after moderation at 2507→2632 and the fidelity retry adopts fidelityRaw at 2737→2770; neither calls the guard, and moderation does not catch secrecy promises (the 2026-09-23 live finding that motivated the guard). The system prompt rule (appsettings.json:2108) is the only mitigation on retries.

**Path:** `ChatService.cs:2215 (guard, primary only) → 2488-2507 retry call → 2513 moderation (passes) → 2632 aiResponse = retryResp; or 2737 fidelity call → 2743 moderation → 2770 aiResponse = fidelityResp → 2871 stored and spoken`

**Fix:**
```
backend/src/ArmenianAiToy.Application/Services/ChatService.cs — mirror Step 8.9 on both raw retries BEFORE their moderation call so the honest line flows through the same moderate/parse pipeline:
@@ 2507
                     var retryRaw = await _aiClient.GetCompletionAsync(systemPrompt, history, cancellationToken);
+                    if (SecrecyPromiseGuard.IsSecrecyPromise(retryRaw))
+                    {
+                        _logger.LogWarning("Secrecy promise replaced (retry). Device: {DeviceId}", deviceId);
+                        retryRaw = SecrecyPromiseGuard.HonestResponse;
+                    }
@@ 2737
                     var fidelityRaw = await _aiClient.GetCompletionAsync(systemPrompt, fidelityHistory, cancellationToken);
+                    if (SecrecyPromiseGuard.IsSecrecyPromise(fidelityRaw))
+                    {
+                        _logger.LogWarning("Secrecy promise replaced (fidelity retry). Device: {DeviceId}", deviceId);
+                        fidelityRaw = SecrecyPromiseGuard.HonestResponse;
+                    }
```

**Test:** SecrecyPromiseGuardTests: ChatService_ReplacesSecrecyPromise_OnQualityRetry — first reply has a 4+ Latin run (forces retry), retry text is a known secrecy phrase; assert result.Response == SecrecyPromiseGuard.HonestResponse. Same shape for the fidelity path with selectedChoice.

### A7 — LOGIC — hard-stop file

**Reproduced:** ChatService.cs:1843-1844 stores the prefilter assistant fallback as SafetyFlag.Clean; the input-moderation path at 1881-1882 stores the identical reply as Flagged, with the comment at 1870-1876 explaining Flagged is what lets the dashboard tell a fallback from a normal reply. ConversationService.cs:158-161 derives LastAssistantSafetyFlag from the newest assistant row, so a prefilter-blocked conversation reads Clean there; HasFlaggedContent (146) still fires via the Blocked user row, so the conversation is not hidden — it is an inconsistency, not a lost signal. No test pins Clean on that row (DangerousInputFilterTests / ArmenianRedTeamSafetyCorpusTests assert no assistant-row flag).

**Path:** `ChatService.cs:1830-1846 (prefilter: User Blocked, Assistant Clean) vs 1877-1882 (moderation: User Blocked, Assistant Flagged) → ConversationService.cs:158-161 LastAssistantSafetyFlag`

**Fix:**
```
backend/src/ArmenianAiToy.Application/Services/ChatService.cs:1844
-                conversation.Id, MessageRole.Assistant, prefilterFallback, SafetyFlag.Clean);
+                conversation.Id, MessageRole.Assistant, prefilterFallback, SafetyFlag.Flagged);
(envelope stays Blocked, matching the moderation path's contract note at 1874-1876).
```

**Test:** ChatService-level: DangerousInput_StoresUserBlocked_AndAssistantFlagged — capture AddMessageAsync calls; assert the Assistant row is Flagged and the envelope is Blocked.

### A8 — RISK — hard-stop file

**Reproduced:** BuildRiddleTurnDirective (2985-3034) and BuildGameTurnDirective (3132-3145) mutate the round (HintsUsed+1, CurrentRound=null on reveal/give-up, TurnsCompleted+1) when called at Step 7 (2178-2185), BEFORE the model call; the undo snapshot is taken at 2270-2273, after those mutations. ApplyEmptyReplyFallback (2936-2947) compares by reference against the post-directive state, so on an empty reply a hint stays charged and a revealed/given-up round stays gone although the child heard only the fallback. ChatServiceEmptyReplyTests cover only 'tail-block-only reply stores no round' (219-242), not a hint/reveal turn.

**Path:** `ChatService.cs:2184 BuildRiddleTurnDirective → 2990-2998 (HintsUsed bumped) → 2270-2273 snapshot (already bumped) → empty reply → 2866 ApplyEmptyReplyFallback → 2936-2941 ReferenceEquals(bumped, bumped) → no restore`

**Fix:**
```
Move the two snapshot declarations (ChatService.cs:2267-2273) to immediately before `bool isStoryMode = detectedMode == DetectedMode.Story;` (2118) — after the deliberate stale-game abandonment at 2104-2116, before any mode directive runs. No other change; the ReferenceEquals comparison then restores the pre-directive round.
```

**Test:** ChatServiceEmptyReplyTests: Riddle_HintTurn_EmptyReply_HintNotCharged (establish round, wrong guess, model returns "" → HintsUsed stays 0) and Riddle_RevealTurn_EmptyReply_RoundRestored.

### A9 — RISK — hard-stop file

**Reproduced:** StoryMemories has no TryRemove anywhere except the stale sweep (grep: lines 1413/2124/2258/2523/2751 only); Step 7a-bis (2124-2137) injects whatever is stored on every Story turn; Merge (StoryMemoryParser.cs:92-103) keeps old Character/Place when the new extraction omits them. A new-story request within 30 min therefore carries the previous story's state as 'CURRENT STORY STATE'. Quality-only impact; the reviewer's trigger condition needs care (an explicit story cue after an EXPIRED pending choice would also wipe a wanted continuation — acceptable since that state is expired anyway).

**Path:** `ChatService.cs:2258-2262 (memory stored, merged) → story ends → new «ուրիշ հեքիաթ» turn: 2057 Story → 2124-2137 inject old memory into systemPrompt → 2198 model call`

**Fix:**
```
ChatService.cs, inside `if (isStoryMode)` before Step 7a-bis (2122):
+            // A fresh story start (explicit cue, no live choice, no explicit
+            // continuation) must not inherit the previous story's cast/place.
+            if (!explicitStoryContinuation && normalizedChoice is null
+                && !(pending is not null && DateTime.UtcNow - pending.ExtractedAt < ChoiceExpiry))
+            {
+                StoryMemories.TryRemove(conversation.Id, out _);
+            }
```

**Test:** StoryMemoryInjectionTests: StoryMemory_NotInjected_WhenNewStoryStartsWithoutPendingChoice — store memory, clear PendingChoices, send «պատմիր ուրիշ հեքիաթ», assert prompt lacks 'CURRENT STORY STATE'.

### A10 — RISK — hard-stop file

**Reproduced:** ChatService.cs:2896-2899 sets TurnEnded whenever a Game turn ends with no CurrentRound; a new_game/switch_game reply with no tail block, an off-taxonomy GAME_TYPE (IsAllowedGameType false at 2320-2321), or a Flagged fallback stores no round, so the toy gets X-Areg-Turn-End right after the invitation. The Step 12 comment (2885-2895) explicitly chooses this reading ('or a round that never got established'), so it is deliberate, and tests pin only Stop→true / tail-present→false (GameLoopIntegrationTests.cs:204-230). Alternative (keep listening) means the next child turn is read as StartNew again — a product call, not clearly a bug.

**Path:** `ChatService.cs:2178-2181 new_game directive → model omits tail → 2316-2335 no round stored → 2896-2899 turnEnded=true → AudioChatController X-Areg-Turn-End → firmware ends session`

**Fix:**
```
ChatService.cs: have BuildGameTurnDirective report the stop decision (e.g. `out bool stopRequested`, set true in the GameIntent.Stop branch at ~3090) and at 2896:
-        var turnEnded = detectedMode == DetectedMode.Game
-            && (!GameSessions.TryGetValue(conversation.Id, out var endedGameState)
-                || endedGameState.CurrentRound is null);
+        var turnEnded = detectedMode == DetectedMode.Game
+            && (gameStopRequested || safetyFlag == SafetyFlag.Flagged);
Owner decides whether 'invitation without tail' should keep the mic open.
```

**Test:** GameLoopIntegrationTests: NewGame_ModelOmitsTail_DoesNotReportTurnEnded; keep Stop→true and add Flagged→true.

### C6 — LOGIC — hard-stop file

**Reproduced:** ChatController.cs:193-194 `EstimateChatCostUsd(request.Message, response.Response)` and AudioChatController.cs:297, :390 `EstimateChatCostUsd(transcript, chatResult.Text)` price the input as the utterance only; ChatService.cs:2031-2040 builds `systemPrompt + BuildChildContext(child) + history` the controllers never see; the overload's own doc (:76-90) quantifies 0.3% of input / 33% of a turn and names 'no shipped firmware uses that path today' as the reason. Premise check: voice_client.cpp:4 'Wi-Fi join + POST /api/chat/audio' is the firmware's base URL (content_sync.cpp:226, ota_apply.cpp:64, ota_foundation.cpp:119 derive every other URL from it); AregVoiceMvp.ino:1493/1517/1529 call `handle_online_chat_session` with no `#if` around them (compiled into every HEAD build, up to 12 turns per session). The field OTA image (1.3.2) lacks hold-to-menu, so the sentence is true for the fleet today and false for the build SHIP.md will release. Cost cap is on by default at $0.25 (appsettings.json:64-66).

**Path:** `AregVoiceMvp.ino:1102,1493-1529 -> voice_client.cpp -> AudioChatController.cs:296-304,389-397 / ChatController.cs:193 -> OpenAICostEstimator.cs:91-97 vs ChatService.cs:2031-2040`

**Fix:**
```
ChatService edit (hard stop). Exact plan: Application/DTOs/ChatResponse.cs gains a trailing positional `long PromptChars = 0` (the 7 `new ChatResponse(` sites in ChatService compile unchanged); at the model-call site(s) in ChatService.GetResponseAsync / ContinueLibraryStoryAsync set it to `systemPrompt.Length + sum(message.Content.Length)` including any repair/retry call; ChatController.cs:193 and AudioChatController.cs:297/390 switch to `OpenAICostEstimator.EstimateChatCostUsdFromPrompt(response.PromptChars, text)` when PromptChars > 0, else the old overload. Also update the :76-90 doc comment's stale premise now, regardless.
```

**Test:** ChatControllerCostCapTests / AudioChatControllerTests: a ChatResponse with PromptChars=8000 records an estimate >= EstimateChatCostUsdFromPrompt(8000, reply); a ChatService test asserting PromptChars >= systemPrompt.Length on a normal turn.

### D6 — RISK — hard-stop file

**Reproduced:** SelfHarmSignal.cs:36-66 has the infinitive «ուզում եմ մեռնել»/«մահանալ» and the subjunctive only in the negative mirror «չեմ ուզում ապրեմ» (:38) and in «ավելի լավ է մեռնեմ»; the spoken Eastern Armenian «ուզում եմ մեռնեմ» and its transliteration are absent, so the pre-moderation gate (ChatService.cs:1817) does not fire and the turn rides on OpenAI moderation alone, which N16 measured at ~17% recall on Armenian script. The reviewer's exclusion note (bare «մեռնեմ» for «մեռնեմ ջանիդ») does not cover the first-person-framed phrase.

**Path:** `SelfHarmSignal.cs:36-66,88; port (phrases parsed from source): IsPresent('Ես ուզում եմ մեռնեմ')=False, 'uzum em mernem'=False, 'ուզում եմ մահանամ'=False; 'Ես ուզում եմ մեռնել'=True, 'չեմ ուզում ապրեմ'=True. All 18 SelfHarmSignalTests literals reproduce their expected values.`

**Fix:**
```
Safety phrase list — needs armenian-story-master review (the 2026-09-24 review marked entries [keep] individually). Proposed additions after SelfHarmSignal.cs:40 «ուզում եմ մեռնեմ», «ուզում եմ մահանամ»; after :53 «uzum em mernem», «uzum em mahanam». Also pinned by SecrecyPromiseGuardTests:82 (prompt carries the rules) — unaffected.
```

**Test:** SelfHarmSignalTests FirstPersonSelfHarm_IsDetected += «Ես ուզում եմ մեռնեմ», «uzum em mernem», «ուզում եմ մահանամ»; OrdinaryInput += «մեռնեմ ջանիդ» (must stay false).

### D8 — RISK — hard-stop file

**Reproduced:** First half reproduces: SecrecyPromiseGuard.cs:27-31 pairs «մեր գաղտն» (which also matches the adjective «գաղտնի») with «ապահով» anywhere in the reply (:45), so an ordinary fairy-tale sentence is treated as a secrecy promise and ChatService.cs:2215-2219 replaces the whole segment with the honest line. Second half is REFUTED: no stale pending choice survives — ChatService.cs:1810 TryRemove consumes PendingChoices at the start of EVERY turn, and because the honest line has no tail block, Step 10c (:2414-2476) generates fresh fallback choices for it (one extra model + moderation call) or sets the deterministic pair at :2466-2473. Actual consequence: lost story segment, a non-sequitur «I keep no secrets from mom and dad… let's listen to a story» mid-story with choices glued on — fails safe, but is a quality hit. Guard is a safety phrase list.

**Path:** `SecrecyPromiseGuard.cs:27-31,45; port: IsSecrecyPromise('Արջուկն ասաց. «Մեր գաղտնի տնակն ապահով տեղում է»։')=True, 'Մեր գաղտնի ճանապարհը ապահով է, ասաց նապաստակը։'=True. With «մեր գաղտն»→«մեր գաղտնիք»: both → False, 'Քո գաղտնիքը ապահով է ինձ մոտ։' stays True, all 10 SecrecyPromiseGuardTests literals unchanged.`

**Fix:**
```
SecrecyPromiseGuard.cs:28 change «մեր գաղտն» to «մեր գաղտնիք» (keeps «մեր գաղտնիքը/ն/դ», drops the adjective «մեր գաղտնի …»). Safety list → owner + armenian-story-master sign-off; skip the same-sentence rewrite and skip any ChatService change (the TryRemove part is moot).
```

**Test:** OrdinaryText_AndTheHonestLine_AreNotCaught += «Արջուկն ասաց. «Մեր գաղտնի տնակն ապահով տեղում է»։»; SecrecyPromises_AreCaught += «Մեր գաղտնիքը ապահով է ինձ մոտ։».

### E1 — BUG — auth (login path) — hard stop per CLAUDE.md; exact diff below

**Reproduced:** Ran the exact pinned DLL (BCrypt.Net-Next 4.1.0, net10.0) via `dotnet fsi`: Verify("pw", "") throws ArgumentException "Invalid salt: salt cannot be null or empty (Parameter 'salt')" (null hash throws ArgumentNullException; garbage hash throws SaltParseException). No try/catch and no global exception handler exist (Program.cs has no UseExceptionHandler; DeviceAuthMiddleware is the only middleware with a catch) so Kestrel returns an empty 500. The LoginAsync filter comment at ParentService.cs:296-300 even says Verify against an empty hash 'throws', and it only excludes AnonymizedAt rows, not Google-only rows (AnonymizedAt null, PasswordHash ''). The existing test LoginAsync_AnonymizedParent_ReturnsNull never reaches Verify("") (the row is filtered out), and there is no Google-only login/ChangePassword/DeleteAccount test. The comments at ParentService.cs:1263-1267 and RetentionPurgeService.cs:861-864 ('Verify against "" cannot succeed / returns false') are factually wrong.

**Path:** `Google sign-up creates Parent{Email=X, PasswordHash="", AnonymizedAt=null} (ParentService.cs:1264-1269) -> POST /api/parents/login {email:X, password:any} (ParentController.cs:111) -> LoginAsync: lookup matches the row (ParentService.cs:302-303), parent != null so the dummy verify is skipped, then `BCrypt.Net.BCrypt.Verify(password, parent.PasswordHash)` at :315 throws -> 500 before RecordFailure (:317) so the per-account throttle never counts it. Unknown or password accounts return the uniform 401, so 500-vs-401 is an account-enumeration oracle for Google-only emails. Same throw at ChangePasswordAsync :861 (authenticated Google-only parent, ParentController.cs:145) and DeleteAccountAsync :1339 (ParentController.cs:513).`

**Fix:**
```
SECURITY-BEHAVIOUR NOTE for the owner: Google-only emails stop returning 500 and return the same uniform 401 (closes the oracle), and the throttle now counts those failures; nothing is weakened. In ParentService.cs add `private static bool PasswordMatches(string password, string? hash) { if (string.IsNullOrEmpty(hash)) { BCrypt.Net.BCrypt.Verify(password, DummyBcryptHash); return false; } return BCrypt.Net.BCrypt.Verify(password, hash); }` (the dummy verify keeps timing equal to the unknown-email path). Use it at :315 (`if (parent == null || !PasswordMatches(password, parent.PasswordHash))`, keep the existing parent==null dummy verify above it), :861 and :1339. Correct the wrong comments at :1263-1267 and RetentionPurgeService.cs:861-864. FOLLOW-UP for the owner (product decision, not part of the fix): a Google-only parent still cannot delete their account or change a password (400) until they set a password via forgot-password; consider a re-auth path for erasure.
```

**Test:** ParentServiceAuthTests: (a) LoginAsync_GoogleOnlyParent_ReturnsNull_DoesNotThrow with a Parent{Email, PasswordHash="", AnonymizedAt=null}, then 10 attempts assert LoginAttemptThrottle.IsLockedOut (proves RecordFailure runs); (b) ChangePasswordAsync_GoogleOnlyParent_ReturnsNull; (c) ParentServiceDeleteAccountTests: DeleteAccountAsync_GoogleOnlyParent_ReturnsFalse_AndKeepsRow.

### E5 — LOGIC — auth (Google sign-in) — hard stop per CLAUDE.md; exact diff below

**Reproduced:** Code path is exact: `var email = identity.Email;` (ParentService.cs:1183) is used verbatim in the comparison `(p.Email ?? "").Trim().ToLower() == email` (:1235-1238), which lowercases only the stored side, and verbatim in the new row (`Email = email`, :1260). GoogleIdTokenValidator.cs:64-66 passes `payload.Email` through unchanged. A mixed-case or padded claim therefore never matches the existing lowercase password account, and the new Google row is accepted because SQLite's unique index is case-sensitive. The only uncertain link is how often Google actually emits capitals (Gmail normally lowercases; custom-domain accounts may not); I could not verify that, so the severity is low, but the code defect is real.

**Path:** `Existing Parent{Email='alice@example.com'}; Google token {sub=new, email='Alice@Example.com', email_verified=true} -> GoogleSignInAsync: bySub null -> byEmail compares 'alice@example.com' == 'Alice@Example.com' false -> byEmail null -> Rule 6 creates a second Parent with Email='Alice@Example.com'. Later LoginAsync lowercases the query so the password login hits the mixed-case Google row's lowercase form: two rows for one mailbox.`

**Fix:**
```
ParentService.cs:1183 -> `var email = EmailNormalizer.Normalize(identity.Email);` (the IsNullOrWhiteSpace check at :1184 still works on the normalized value). Security note: only makes the existing verified-email link match case-insensitively; EmailVerified stays a hard gate.
```

**Test:** ParentServiceGoogleSignInTests: GoogleSignIn_MixedCaseEmail_LinksToExistingLowercaseAccount (existing 'alice@example.com', Identity email ' Alice@Example.com ', assert Success and still exactly one Parent row with GoogleSubject set).

### E9 — RISK — auth (login throttle) — hard stop per CLAUDE.md; exact diff below

**Reproduced:** RecordFailure refuses to create a new Entry once `_entries.Count >= MaxTrackedAccounts` (LoginAttemptThrottle.cs:94-99), and the only removal of an entry is RecordSuccess (:121-126). The class doc claims 'Stale entries are pruned lazily on access', but the code only trims the accessed key's own failure list (:101-103); an entry with an empty or aged list and no lockout stays forever. So after 50,000 distinct failing emails (a normal credential-stuffing list; each email tried once) the per-account lockout stops tracking every new email until a restart. 'Degrade open' is a documented choice, but the pruning claim is false, which makes the cap reachable by noise alone. Per-IP limiter (10/min/IP) slows but does not prevent it for a botnet. Existing keys already in the map keep working.

**Path:** `50,000 POST /login with unique emails (all wrong) -> each creates an Entry (LoginAttemptThrottle.cs:96-99) -> count reaches 50,000 -> later `RecordFailure("target@x")` returns false at :97 without tracking -> the target never reaches 10 failures, IsLockedOut stays false.`

**Fix:**
```
SECURITY-BEHAVIOUR NOTE: strengthens the throttle; no weakening. In RecordFailure, when the key is new and Count >= cap, first run a sweep (at most once per `_window` via a `_lastSweep` timestamp, to avoid an O(n) scan on every flood request): remove entries where (LockoutUntil is null || LockoutUntil <= now) && Failures.All(t => now - t > _window). Then re-check the cap; only if still full return false. Make the cap an optional ctor parameter (default 50_000) so it is testable. Fix the class doc to match.
```

**Test:** LoginAttemptThrottleTests: with maxTrackedAccounts 3 and a fake TimeProvider, fill with 3 stale failures, advance past the window, assert a 4th email IS tracked and locks out after 10 failures; and a full set of ACTIVE entries still refuses a new key.

### E8 — RISK — owner decision

**Reproduced:** ChatRateLimiter.PolicyFactory (RateLimiting/ChatRateLimiter.cs:33-38) partitions on the raw X-Device-Id header; UseRateLimiter (Program.cs:475) runs before DeviceAuthMiddleware (:478), so a request with the victim's id and no/invalid key consumes the victim's fixed-window bucket (30/min) and the real toy then gets 429. Routing is implicit (no explicit UseRouting; MapControllers at :601) so [EnableRateLimiting("chat")] on ChatController, AudioChatController, StoryQaController, StoryAudioTokenController and DeviceController:235 is visible to the limiter. The class doc's argument ('rotating random ids gains nothing') ignores targeting a known id. Preconditions: the attacker knows the toy's device id (printed in the QR payload; also known to any ex-parent who unlinked).

**Path:** `Attacker loop: `POST /api/chat` with `X-Device-Id: <victim>` (no key), 30 times at the start of each 60 s window -> limiter spends the victim partition; the toy's genuine requests in the same window hit 429; the attacker's own requests fall through to DeviceAuthMiddleware and get 401.`

**Fix:**
```
Do NOT reorder the middleware: the limiter sits before auth on purpose (DeviceApiKeyHasher's PBKDF2 ~15-25 ms per verify; moving auth first lets anyone burn CPU). Instead bind the bucket to the credential presented, keeping order. Exact diff in ChatRateLimiter.PolicyFactory: `var id = context.Request.Headers["X-Device-Id"].FirstOrDefault(); if (string.IsNullOrWhiteSpace(id)) id = AnonymousKey; var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault(); var key = string.IsNullOrEmpty(apiKey) ? id : id + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey)), 0, 8); return BuildPartition(key, permitLimit, windowSeconds);` (add `using System.Security.Cryptography; using System.Text;`). A key-less or wrong-key attacker lands in a different bucket than the real toy (which always sends its key); attackers rotating key strings can already rotate ids today, so no new weakness. This is a rate-limit/auth-boundary change, hence the owner's OK; BuildPartition and the two existing tests are untouched.
```

**Test:** ChatRateLimiterTests: PolicyFactory_SameDeviceId_DifferentApiKey_DifferentPartition and SameIdSameKey_SamePartition using DefaultHttpContext headers.

### E10 — RISK — owner decision

**Reproduced:** POST /api/internal/session (InternalController.cs:698-713) verifies a 6-digit TOTP (Totp.cs: +-1 step = 3 valid codes in 10^6) with no attempt counter and no rate limit; the pre-routing gate (Program.cs:540-575) only checks the static token, and nothing records failures. A leaked static token plus online guessing therefore defeats the MFA factor. It is latent: Operators[].TotpSecret and Internal:RequireSession are opt-in (appsettings.json:150-154, both off), and the attacker already needs the first factor. The replay point (sniffed code reusable for ~90 s) is real but needs TLS interception, so minor. IMPORTANT TRAP in the finding's own fix: adding [EnableRateLimiting(AuthRateLimiter.PolicyName)] would run in UseRateLimiter (Program.cs:475), which is BEFORE the 404-concealment gate, so the 11th probe per minute from one IP would answer 429 on that path instead of 404 and reveal that the console exists.

**Path:** `Static token T for operator 'alice' with TotpSecret set: loop `POST /api/internal/session` {totp:"000000"..} with Authorization: Bearer T -> gate resolves staticName and lets /session through (:560-566) -> Totp.Verify false -> 401 each time, unlimited, until a code in the +-30 s window matches.`

**Fix:**
```
Auth change, so owner approval. Preferred (no concealment leak, only reachable after the static token validates): in CreateSession resolve the existing singleton `var throttle = HttpContext.RequestServices.GetRequiredService<ArmenianAiToy.Application.Auth.LoginAttemptThrottle>(); var tk = "operator-totp:" + op;` then before Verify `if (throttle.IsLockedOut(tk)) return Unauthorized(new { error = "Invalid authentication code." });`, on a bad code `throttle.RecordFailure(tk)` then return the same 401, on success `throttle.RecordSuccess(tk)`. Trade-off to tell the owner: a holder of the leaked token can lock the real operator out of session exchange for 15 min after 10 bad codes (the static token still works if RequireSession is off). Skip the TOTP replay guard (accept-document).
```

**Test:** InternalControllerTests: 10 wrong TOTP codes -> 11th correct code still 401 until cooldown; correct code resets the counter.

### E2 — BUG — extends the owner's 2026-08-04 unlink decision to account deletion and flips a pinned test — owner call

**Reproduced:** UnlinkDeviceAsync's last-link branch (ParentService.cs:783-818) deliberately keeps the Device row and factory-resets it, with a comment recording the owner decision of 2026-08-04 that the printed QR must work for the toy's whole life and that deleting the row 'scrapped the toy' (docs/CLAUDE-history.md ~3430-3445). DeleteAccountAsync still removes the Device row (:1366-1374), so DeviceAuthMiddleware 401s the toy forever and ClaimCodeHash is gone. The deletion is pinned by an old test (ParentServiceDeleteAccountTests: `Assert.Null(await db.Set<Device>().FindAsync(deviceId))` under 'Sole-owner device orphaned -> cascaded delete') that predates the unlink change. RetentionPurgeService's dormant-parent anonymize pass (:852-856) has the same orphan-delete (disabled by default; arguably intended there).

**Path:** `Sole parent -> DELETE /api/parents/account {currentPassword} (ParentController.cs:513) -> DeleteAccountAsync: Parent removed (:1357), `stillLinked` false for each linked device (:1380-1383), `_db.Set<Device>().Remove(device)` (:1392-1395), SaveChanges -> Device row and ClaimCodeHash gone -> toy's next heartbeat/chat fails ValidateDeviceAsync -> 401 'Invalid device credentials'.`

**Fix:**
```
Extract the body of UnlinkDeviceAsync's last-link branch (RemoveRange of Conversation, Child, StoryPlay, GamePlay, StoryReflectionAnswer, DeviceCommand, plus DeviceInvite from E4, then the Device field reset) into a private helper that returns the snapshotted conversationIds, and call it from DeleteAccountAsync's orphan loop instead of `_db.Set<Device>().Remove(device)`. Keep `orphanedDevicesDeleted++` and the audio-cleanup flow unchanged. NOTE for the owner: this flips a pinned test and extends the 2026-08-04 decision to account deletion; the privacy promise 'Everything belonging to you goes with it' still holds (family data is wiped, only the factory-reset toy identity stays). Leave the retention anonymize/dormant-delete passes alone (separate owner call).
```

**Test:** Change ParentServiceDeleteAccountTests.DeleteAccountAsync_SoleOwner_CascadesParentDeviceChildrenConvosMessages to assert Device NOT null with Name == "", ClaimedAt == null and ClaimCodeHash unchanged, while Child/Conversation/Message/ParentDevice rows are gone; keep the shared-device test as is.

### F2 — LOGIC — owner decision

**Reproduced:** Real and reproduced. appsettings.json ContentSync:Stories has 26 rows: 20 real (10 base then 10 alt, all SizeBytes>0) plus 6 Tsivik placeholders that ContentManifestService drops (SizeBytes<=0), so 20 items are offered. cs_manifest_parse caps by POSITION on the offered count (content_sync_model.cpp:36-44 `cap = min(cs_accepted_count(offered), max_out)`), host probe: cs_accepted_count(20)=16, truncated=4, cs_accepted_count(26)=16. Dropped: sutasan-alt, sutlik-orskan-alt, three-piglets-alt, ulik-alt. cs_index_parse also `if (count >= max_out) break;` (content_sync_model.cpp:236), and story_select's s_raw is the same size, so a factory card written with 20 index entries is read as 16 on first boot and rewritten as 16 by the first sync (4 files stranded on the card; orphan sweep is off). Current user-visible effect is nil because alt files are never played (AREG_VARIANT_ENDINGS_ENABLED defaults 0, story_select.cpp:339), so it is latent for the alts. It is NOT latent for anything else: ContentItemOverlay.cs:223 builds `configStories.Concat(uploadedStories)`, so every operator-uploaded story is appended after the 20 config rows and can never pass the 16 cap, and the 6 Tsivik episodes will be cut the same way. This is the same silent-truncation class that was already fixed once for 8 -> 16 (content_sync_rules.h:36-40).

**Path:** `ContentManifestService.cs:48-95 (drops SizeBytes<=0, keeps config order, alt after base) -> device: content_sync.cpp:1744 cs_manifest_parse(..., CS_MAX_STORIES) examines only the first 16 -> :1784/:1802 s_active/carry-forward bounded by CS_MAX_STORIES -> write_index publishes 16. Log line `[content-sync] manifest truncated: 20 offered, max 16, 4 ignored` would show on the bench.`

**Fix:**
```
Owner choice. (A) Firmware, ~+24 KB .bss: content_sync_rules.h:46 `#define CS_MAX_STORIES 20`; AregVoiceMvp.ino:851 `static_assert(CS_MAX_STORIES <= 32, ...)`, :865 `uint32_t heard = 0;`, :869 `heard |= (uint32_t)(1u << i);`, :878 and :881 `(heard & (uint32_t)(1u << i))`; content_report.cpp:28 `kStoriesBufLen = 400` (the 20 real ids with :1 versions need 330 bytes; 320 would drop the last entry at an entry boundary). Needs a bench heap check (README bench step: `[content-sync] heap before/after`, `[alive] heap=`) and a check of voice_client.cpp:401 `char body[768]` (production heartbeat estimated ~610 B at 20 ids; with AREG_CONTENT_SYNC_BENCH fields it is near the limit and content_report_json_fields returns 0, silently dropping the whole report). (B) Zero firmware RAM interim: in ContentManifestService emit non-alt items before alt items (stable order, alt = AltOf != null) so a cut falls on alts first and uploaded base stories survive; this buys room only until base stories + series > 16. (C) Longer term: put s_manifest/s_previous/s_active/s_raw/s_eligible_stories in PSRAM (ps_malloc) instead of .bss.
```

**Test:** RAM math: sizeof(CsStory)=1216 on the x86-64 host (about 1.2 KB on Xtensa), five CsStory[CS_MAX_STORIES] tables (content_sync.cpp:89-91, story_select.cpp:132, AregVoiceMvp.ino:832) = about 6.0 KB per slot: 20 -> +24 KB, 24 -> +49 KB, 32 -> +97 KB (the TLS handshake wants 40-50 KB contiguous internal heap, so 20 is the only size to consider without moving to PSRAM). Pin it with a preflight/CI check that counts rows with SizeBytes>0 in appsettings.json ContentSync:Stories and fails if the count exceeds CS_MAX_STORIES parsed from content_sync_rules.h (or a backend test reading that header); add a host assertion `cs_accepted_count(20)==20` once raised. Bench: factory card with 20 index entries -> first sync log must not say `truncated`.

### F4 — LOGIC — owner decision

**Reproduced:** Logic path traced, but fully compiled out today (AREG_VARIANT_ENDINGS_ENABLED defaults 0, story_select.cpp:339-342 `if (0 && ...)`), so no shipped behaviour is wrong; it must be fixed before the flag is turned on. story_heard_mark is called exactly once, at playback START (AregVoiceMvp.ino:1841-1850, gated by `started`), yet the resume comment (:1673-1678) and story_select.h:299-302 both say the heard set is only written at a story's natural END, and rely on that for 'a resume always lands on the same FILE'. With the flag on, parent toggle on and an alt cached: first listen of X plays the base file, X is marked heard at start, pause at byte N (Q&A barge-in, shout pause or sticky pause), resume re-runs story_select_resolve_playback_path(X) (:1673) where story_heard_contains(X) is now true, so it returns X-alt and byte N is applied to a different, separately rendered MP3 (the alts were fully re-rendered, not byte-spliced). The same flip also gives a first listen the alt ending, contradicting 'a FIRST listen always gets the authored ending'.

**Path:** `AregVoiceMvp.ino:1841-1850 (mark at start) -> s_story_offset=resume_offset (:1928) -> re-entry takes `else if (s_current_story_id[0] != '\0')` (:1672) -> story_select.cpp:343-365 variant branch (heard-set true) -> sd_cache_path = alt path -> audio_play_story_file(sd_narration_path, s_story_offset, ...) (:1788).`

**Fix:**
```
Record the file the session started on and re-use that decision on resume (design choice vs moving story_heard_mark to the natural end, which would change the welcome flow's 'unheard' offers). Diff: add `static bool s_session_variant = false;` beside s_story_offset; pass `&s_session_variant` as the 4th arg of both story_select_resolve_playback_path calls in story_pick_for_session (:1570 and :1608); in the resume branch (:1673) use `cache_hit = s_session_variant ? story_select_resolve_playback_path(s_current_story_id, sd_cache_path, sizeof(sd_cache_path)) : story_select_resolve_path(s_current_story_id, sd_cache_path, sizeof(sd_cache_path));`. The 4-arg out_is_variant parameter already exists. Cheaper alternative if the owner prefers: correct the two comments and keep the flag off until the listen test.
```

**Test:** Pure rule test is awkward (resolver reads NVS/SD). Bench step when AREG_VARIANT_ENDINGS_ENABLED=1: first listen of a story with a cached alt, press-hold to ask a question mid-story, confirm the log says `resuming <id> at byte` and the path stays the base file; then a re-listen, interrupt, resume, confirm it stays on the alt. Until then: leave the flag 0 and note it in SHIP.md's variant-endings item.

### C5 — LOGIC — the comment/doc wording half is fixed in this PR; the constant + cap change is a product decision

**Reproduced:** Direction is settled by arithmetic, magnitude is not measurable here (no tokenizer: `python3 -I -c 'import tiktoken'` -> ModuleNotFoundError; no network tokenizer). OpenAICostEstimator.cs:64-69 claims 'Armenian text generally uses fewer characters per token but the over-estimate is intentional' - both halves cannot be true: fewer chars/token means chars/4 yields FEWER tokens than real, an under-estimate. The comment is simply wrong in direction. cost-per-hour-of-play-20260812.md:34-37 says '2,086 tokens is measured, not assumed' and then explains it is 8,343 summed chars - 8,343/4 = 2,085.75 -> 2,086: the characters were measured, the tokens derived. OpenAIDailyCostCapOptions.cs:60-62 `MeasuredUsdPerQuestion = 0.0078m` ('Measured USD cost') inherits it and Default 0.25 = 30 x 0.0078 (appsettings.json:65). GeminiChatClientAdapter.cs:326-345 reads only candidates/parts (usageMetadata discarded); OpenAIChatClientAdapter.cs:50-54 returns text only (Usage discarded). Consequence: the per-day QUESTION count stays ~30 as intended because the cap is derived from the same estimator, but real dollars per capped day are some multiple of the nominal $0.25; production chat is Gemini ($0.75/$3.75, appsettings:30) whose tokenizer differs again, so one constant cannot serve both.

**Path:** `OpenAICostEstimator.cs:64-69,140-144 -> ChatController.cs:193 / AudioChatController.cs:297 -> OpenAICostMeter / DeviceUsageDay; OpenAIDailyCostCapOptions.cs:52-62; tools/quality-evidence/cost-per-hour-of-play-20260812.md:29-37`

**Fix:**
```
Split. Fix-now (docs/comments only, no behaviour): rewrite OpenAICostEstimator.cs:64-69 to 'Armenian uses fewer characters per token than Latin text, so chars/4 UNDER-counts Armenian; the ratio has not been measured with a tokenizer' and the `MeasuredUsdPerQuestion` doc comment + evidence doc line 34 to 'derived from measured prompt characters at the 4 chars/token approximation'. Plan (product decision, child-facing): measure once with tiktoken o200k_base over the nine assembled prompts, set CharsPerTokenEstimate (per provider if Gemini differs) AND raise `Default` in the same change - changing the constant alone makes the $0.25 cap trip at roughly half the intended 30 questions. Longer term: return token usage from both adapters (widens IAiChatClient - plan).
```

**Test:** For the wording: none. For the plan: a test pinning `OpenAIDailyCostCapOptions.Default == IntendedQuestionsPerDay * MeasuredUsdPerQuestion` rounded (check whether one already exists) and a per-provider ratio constant test so the two cannot drift apart silently.

### B2 — LOGIC — owner decision

**Reproduced:** Duplicate of A6. ChatGateEvaluator.cs:80-90 and AudioChatController.cs:211-222 gate only on single-message ModeDetector.Detect; `grep IsModeEnabled ChatService.cs` → 0 hits, and ChatService.cs:2060-2067 promotes DetectedMode.None to the ActiveModes entry (Game/Riddle/Calm, 30-min ChoiceExpiry). A parent flipping Game=off mid-game therefore stops nothing until a keyword turn. Exposure is bounded by the firmware turn cap (12) and ChoiceExpiry.

**Path:** `AudioChatController.cs:211-222 (None → no gate) → ChatService.GetResponseAsync Step 7a :2060-2067 (None → activeMode.Mode) → game continues`

**Fix:**
```
ChatService.cs:2060-2067 (hard-stop file): after resolving `activeMode`, `if (activeMode.Mode is DetectedMode.Game or DetectedMode.Riddle && !await _deviceService.IsModeEnabledForRequestAsync(deviceId, childId, activeMode.Mode)) { ActiveModes.TryRemove(conversation.Id, out _); GameSessions.TryRemove(conversation.Id, out _); RiddleSessions.TryRemove(conversation.Id, out _); /* stay None */ } else detectedMode = activeMode.Mode;` — the A6 refuter owns the exact diff.
```

**Test:** ChatServiceTests: active Game session, device Game flag turned off, keyword-less turn → no GameSessions round written, Mode != Game.

### B5 — RISK — owner decision

**Reproduced:** AnswerReflection (StoryQaController.cs:903-925) runs only `_moderation.CheckContentAsync(answer)`; `grep SelfHarmSignal StoryQaController.cs` hits only Ask() (:362-371). The model reaction (:944-960) gets output moderation only; `SecrecyPromiseGuard` is never referenced in the file (ChatService uses it at :2215-2218). CLAUDE.md N16 already records 'NOT covered: the after-story reflection answer path' — a known, documented safety gap, not a regression. N16's measured 17% moderation recall on Armenian-script self-harm makes the miss realistic.

**Path:** `POST /api/chat/story-qa/reflection-answer :778 → STT :880-886 → moderation only :921-931 → ReflectionDialogueService.ReactAsync :944 → warm reaction spoken :990-996, rows stored Clean`

**Fix:**
```
StoryQaController.AnswerReflection, replace the else-branch at :919-932 with: `var inputModeration = await _moderation.CheckContentAsync(answer); if (SelfHarmSignal.IsPresent(answer) || inputModeration.FlaggedCategories.Contains("self-harm")) { _logger.LogWarning("Reflection self-harm signal. StoryId: {StoryId}", storyId); affirm = false; selfHarm = true; userFlag = SafetyFlag.Blocked; assistantFlag = SafetyFlag.Flagged; outcome = "self_harm_signal"; } else if (!inputModeration.IsSafe) { ...existing block... }`. In the spoken block (:1017-1020): `else if (selfHarm) { spoken = (await _synthesis.SynthesizeArmenianAsync(SelfHarmSignal.Response, cancellationToken)).Content; } else { spoken = closeAudio; }` and in persistence (:1040-1043) `assistantText = selfHarm ? SelfHarmSignal.Response : ReflectionClose;`. For the reaction (:947): `if (reaction.Text is not null && !SecrecyPromiseGuard.IsSecrecyPromise(reaction.Text)) { ...output moderation... }` (a secrecy promise falls back to the deterministic ack — HonestResponse ends with 'let's listen to a story', wrong after a story). `self_harm_signal` is already in the bounded StoryQaTurn outcome vocabulary.
```

**Test:** StoryQaControllerReflectionTests: (1) answer «չեմ ուզում ապրել» / «chem uzum aprel» → spoken bytes == TTS(SelfHarmSignal.Response), user row Blocked, ReactAsync never called, outcome self_harm_signal; (2) moderation returns self-harm category → same; (3) reaction text matching SecrecyPromiseGuard → ack clip spoken, reaction not persisted.


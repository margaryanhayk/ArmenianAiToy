// -------------------------------------------------------------
// AregVoiceMvp / story_report.h — story-play reporting (store-and-forward)
//
// WHY: stories play from the SD cache (offline path), so the backend never
// sees the play as a request and the parent dashboard silently under-reports
// what the child actually heard. This module queues one tiny event per story
// session in NVS and uploads the queue to POST /api/devices/story-plays
// whenever Wi-Fi is up, deleting events only after a 2xx.
//
// TRANSPORT: at-least-once. Every event carries a device-generated
// idempotency key ("b<boot>-<n>"), so a re-upload after a lost 2xx inserts
// nothing twice server-side (unique per device+key).
//
// WHY NVS and not RAM: a play that happens while Wi-Fi is down must survive
// the child (or parent) powering the toy off before it comes back — same
// rationale as ota_state. Same Preferences/NVS idiom as wifi_creds /
// device_creds / ota_state (keys ≤ 15 chars; write only on change).
//
// TIME: the toy has no wall clock. Events produced in the current boot
// report a best-effort relative age (secondsAgo from millis()); events that
// survived a reboot report no age and the backend stamps upload time with
// timeIsApproximate = true.
//
// SESSION CONTRACT (hooks in AregVoiceMvp.ino):
//   - story_report_on_started() — once per NEW story session, only after
//     playback genuinely started (decoder produced its first frame). The
//     event enqueues as finished=false and stays "open".
//   - story_report_on_finished() — at natural end; closes the open event.
//     A pause/resume within one boot re-enters the session and still closes
//     the SAME open event, so one listen = one row.
//   - The open event is NOT uploaded while open (a mid-pause upload would
//     freeze it as unfinished); it uploads once closed, or on a later boot
//     as finished=false if the child never resumed — which is the truth.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// Enqueue a new play event (finished=false, open). `source` is one of
// "sd" / "pack" / "stream" — the backend collapses anything else to "other".
// Lazily loads the persisted queue on first use. Drops the oldest event when
// the queue is full — recent history beats ancient history.
void story_report_on_started(const char *story_id, const char *source);

// Close the open event (finished=true) and persist. No-op when nothing is
// open (e.g. the event was already closed, or a reboot cleared RAM state —
// after a reboot the un-resumed event honestly uploads as unfinished).
void story_report_on_finished();

// Idle-loop cadence: upload closed events when Wi-Fi is up. Prompt (a few
// seconds) after an event closes; otherwise every AREG_HEARTBEAT_INTERVAL_MS
// while the queue is non-empty. Deletes uploaded events only on a 2xx.
// Never blocks a voice turn — call from the IDLE branch only.
void story_report_tick();

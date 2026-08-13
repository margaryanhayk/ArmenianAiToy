#pragma once

// What this toy actually HAS on its card, summarised for the heartbeat.
//
// THE GAP THIS CLOSES. The backend knew what it ADVERTISED in the content
// manifest and nothing whatsoever about what any toy had downloaded. So
// "did the new stories arrive?" — the question a parent asks the day after
// a library update — had no answer anywhere: not in the dashboard, not in
// the operator console, not in a log. The only confirmation was pressing
// the button and listening. The toy is the only thing that knows, so the
// toy says so.
//
// WHY IT READS THE INDEX AND NOT content_sync's OWN TABLES. content_sync
// runs one pass per boot, and its s_active[] table is only populated on a
// boot where that pass actually ran and verified something. A toy that
// booted without Wi-Fi, or whose library was already current, would report
// an empty card while holding ten stories. /content_index.json is the
// durable truth and is what every other reader already trusts.
//
// WHY IT PARSES THE JSON DIRECTLY. Building a CsStory[CS_MAX_STORIES] just
// to count ids would add a sixth such table to an image where five already
// exist (~3.4 KB each) on a board that wants 40-50 KB free for a TLS
// handshake during audio. Three fields per entry are needed; the parse
// takes them and nothing else.
//
// EVERY VALUE IS ALREADY-VERIFIED CONTENT. A story enters the index only
// after its sha256 matched, so a half-downloaded story is never counted.
// "6 of 10" therefore means six stories that will actually play.

#include <stddef.h>

/// Re-reads /content_index.json and caches the summary. Cheap (one small
/// file, parsed once) and safe to call when the card is absent — the
/// summary then simply stays unavailable. Call after boot and after each
/// successful sync.
void content_report_refresh();

/// True when a summary is available to send. False on a toy with no card,
/// no index yet, or an unreadable one — in which case the heartbeat sends
/// NOTHING rather than a zero, because "I have no stories" and "I could
/// not look" are different claims and only one of them is a fault.
bool content_report_available();

/// True when the summary differs from the one last marked as sent. The
/// heartbeat body carries the content block only when this is true (plus
/// once per boot), so a toy whose card has not changed adds nothing to its
/// ~60 s heartbeat — the same write-on-change discipline ota_state uses.
bool content_report_changed();

/// Marks the current summary as sent. Called only after a 2xx, so a failed
/// heartbeat re-sends rather than silently dropping the one report that
/// mattered.
void content_report_mark_sent();

/// Writes the heartbeat JSON fields WITHOUT enclosing braces and WITHOUT a
/// leading comma, e.g.
///   "contentIndexSchema":7,"contentStories":"ulik:12,...","contentGameClips":104
/// Returns the number of characters written, 0 when unavailable or when the
/// buffer is too small. Never writes a partial field.
size_t content_report_json_fields(char *out, size_t len);

// -------------------------------------------------------------
// AregVoiceMvp / json_psram.h — the ONE PSRAM allocator every JsonDocument
// that parses /content_index.json (or any other SD/network JSON of
// meaningful size) must use.
//
// WHY THIS EXISTS. content_sync.cpp and story_select.cpp each rolled their
// own copy of this allocator (field failure 2026-08-14, see
// content_sync.cpp's original comment); content_report.cpp then added a
// THIRD reader of the same file with no allocator at all. The index is 10
// stories + 42 voice clips + 104 game clips, each carrying a 64-char
// sha256 -- an elastic JsonDocument for it does not reliably fit in
// internal heap beside a TLS session or the audio buffers:
//
//   [content-sync] manifest status=200 stories=10 voice=42 games=104
//   ESP_ERROR_CHECK failed: ESP_ERR_NO_MEM ... phy_track_pll_init
//   abort() ... reset_reason=4/PANIC
//
// One shared allocator means one place to get this right, instead of a
// fourth call site quietly regressing to internal heap because nobody
// remembered to copy the struct again.
//
// Compiled into EVERY build (production included) -- content_report.cpp
// and story_select.cpp both run outside AREG_CONTENT_SYNC_BENCH, so this
// header must not be gated behind that macro either.
//
// PSRAM is 7.8 MB and idle; TLS cannot use it, plain JSON data can. Falls
// back to internal heap so a board without PSRAM still works (never worse
// than the un-allocated default).
// -------------------------------------------------------------
#pragma once

#include <ArduinoJson.h>
#include <esp_heap_caps.h>

struct PsramJsonAllocator : ArduinoJson::Allocator {
    void *allocate(size_t n) override {
        void *p = heap_caps_malloc(n, MALLOC_CAP_SPIRAM);
        return p != nullptr ? p : malloc(n);
    }
    void deallocate(void *p) override { heap_caps_free(p); }
    void *reallocate(void *p, size_t n) override {
        void *q = heap_caps_realloc(p, n, MALLOC_CAP_SPIRAM);
        return q != nullptr ? q : realloc(p, n);
    }
};

// One instance, shared by every reader of the content index (and any other
// SD/network JSON worth keeping off internal heap). Defined in
// json_psram.cpp; never construct a second one.
extern PsramJsonAllocator g_json_psram;

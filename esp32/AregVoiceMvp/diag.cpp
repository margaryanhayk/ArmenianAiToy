// -------------------------------------------------------------
// AregVoiceMvp / diag.cpp
//
// Implementation of the RTC-backed breadcrumb helpers declared
// in diag.h. See diag.h for the API contract and the lifecycle
// rules.
// -------------------------------------------------------------
#include "diag.h"

#include "config.h"   // AREG_DIAG_MARK_SERIAL may be defined by the bench config

#include <esp_attr.h>
#include <string.h>

// Magic word distinguishes a cold boot (RTC slow memory
// zeroed by power-up) from a warm boot (panic / brownout /
// watchdog / EN reset preserves it).
static constexpr uint32_t kDiagMagic = 0xA8E69123u;

// RTC slow memory survives:
//   * Software reset (esp_restart, panic restart)
//   * Interrupt watchdog (INT_WDT)
//   * Task watchdog (TASK_WDT)
//   * Brownout reset
//   * External reset (EN/RESET button press)
// It does NOT survive:
//   * Hard power loss / USB unplug
//   * Deep-sleep variants that explicitly wipe RTC slow memory
//     (not used by this firmware)
RTC_DATA_ATTR static uint32_t g_diag_magic;
RTC_DATA_ATTR static uint32_t g_diag_boot_counter;
RTC_DATA_ATTR static uint32_t g_diag_last_step;
RTC_DATA_ATTR static uint32_t g_diag_last_line;
RTC_DATA_ATTR static char     g_diag_last_label[48];

void diag_print_previous_boot_context() {
    if (g_diag_magic != kDiagMagic) {
        Serial.println("[boot] diag_boot_counter=0 (cold)");
        Serial.println("[boot] previous_step=(none)");
        g_diag_magic = kDiagMagic;
        g_diag_boot_counter = 1;
        g_diag_last_step = 0;
        g_diag_last_line = 0;
        g_diag_last_label[0] = '\0';
    } else {
        Serial.printf("[boot] diag_boot_counter=%u\n",
                      (unsigned)g_diag_boot_counter);
        Serial.printf("[boot] previous_step=%u label=%s line=%u\n",
                      (unsigned)g_diag_last_step,
                      g_diag_last_label[0] ? g_diag_last_label : "(empty)",
                      (unsigned)g_diag_last_line);
        g_diag_boot_counter += 1;
        // Clear so a clean reboot without any DIAG_MARK doesn't
        // look like it died at the same spot as the prior crash.
        g_diag_last_step = 0;
        g_diag_last_line = 0;
        g_diag_last_label[0] = '\0';
    }
    Serial.flush();
}

void diag_mark(uint32_t step_id, const char *label, uint32_t line_no) {
    g_diag_last_step = step_id;
    g_diag_last_line = line_no;
    if (label != nullptr) {
        strncpy(g_diag_last_label, label, sizeof(g_diag_last_label) - 1);
        g_diag_last_label[sizeof(g_diag_last_label) - 1] = '\0';
    } else {
        g_diag_last_label[0] = '\0';
    }
#ifdef AREG_DIAG_MARK_SERIAL
    // LIVE-TRACE MODE (bench opt-in). The printf is cheap; the flush +
    // delay(5) are not — see the latency note in diag.h. A single in-story
    // Q&A turn crosses ~26 marks (mic begin/capture/teardown + playback +
    // the voice_client HTTP marks), so this costs ~130 ms of pure delay per
    // question. It buys one thing the RTC breadcrumb cannot: a live view of
    // where the firmware is RIGHT NOW while it is running. Turn it on when
    // you are watching a hang happen; leave it off otherwise.
    Serial.printf("[mark] step=%u label=%s line=%u\n",
                  (unsigned)step_id,
                  (label != nullptr ? label : ""),
                  (unsigned)line_no);
    Serial.flush();
    // Native USB CDC: flush() drains the firmware TX buffer but
    // the host still needs a few ms to receive over the link
    // before any possible crash on the very next instruction.
    delay(5);
#endif
}

// -------------------------------------------------------------
// AregVoiceMvp / ble_provisioning.cpp   (Phase B.2)
//
// Thin wrapper over Arduino-ESP32's WiFiProv (ESP-IDF wifi_provisioning,
// BLE transport). See ble_provisioning.h for the contract.
//
// UNVERIFIED — compiles, but the provisioning round-trip can only be
// functionally tested at the bench with a phone (Espressif "ESP BLE
// Provisioning" app or the provisioning SDK in the parent app). Gated behind
// AREG_USE_BLE_PROVISIONING; not part of the default build.
// -------------------------------------------------------------
#include "ble_provisioning.h"

// config.h FIRST — it is what defines AREG_USE_BLE_PROVISIONING. Testing the
// macro before including it made this whole translation unit compile to
// nothing while the .ino (which includes config.h early) still called into
// it, so enabling the flag produced only "undefined reference" at link time
// and the module could never actually ship. Same class of bug as the OTA
// "make config.h overrides visible in every translation unit" fix.
#include "config.h"

#ifdef AREG_USE_BLE_PROVISIONING

#include <WiFi.h>
#include <WiFiProv.h>

#include "wifi_creds.h"   // wifi_creds_save — persist the received creds (B.1)
#include "device_creds.h" // device_creds_pop_load — per-device PoP (factory pairing)
#include "device_creds_rules.h" // pop_is_wellformed — reject NVS garbage before advertising it

// Non-secret knobs. Self-defaulted so the build never depends on config.h
// carrying them (same pattern as the #047 watchdog tunables). Override in
// config.h to brand the BLE name.
#ifndef AREG_PROV_SERVICE_NAME
#define AREG_PROV_SERVICE_NAME "Areg-Setup"
#endif
#ifndef AREG_PROV_POP
// Proof-of-possession BENCH FALLBACK ONLY, used when NVS holds no per-device
// PoP (device_creds_pop_load returns false — a unit that was never run
// through the factory station, or one factory-provisioned before the PoP
// slice). Every real toy advertises its own per-device PoP instead (factory
// pairing, 2026-09-11) — see device_creds.h / tools/factory/provision_toy.py.
// This value is a fixed, shared, checked-in placeholder: NOT a secret, and
// tools/firmware/check_release_image.py refuses to release an image if this
// macro is ever overridden with something that looks like a real PoP.
#define AREG_PROV_POP "areg-pair"
#endif

static volatile bool s_active = false;
static volatile bool s_succeeded = false;  // B.3 — latched on CRED_SUCCESS

// Provisioning event handler. Registered via WiFi.onEvent before
// beginProvision. The credential-receive event is where we capture the
// home Wi-Fi name + password and persist them to OUR NVS namespace (aregwifi)
// so voice_wifi_begin()/voice_wifi_tick() use them on every subsequent boot.
static void prov_event(arduino_event_t *sys_event) {
    switch (sys_event->event_id) {
        case ARDUINO_EVENT_PROV_START:
            Serial.println("[prov] BLE provisioning started — waiting for the phone");
            Serial.flush();
            s_active = true;
            break;

        case ARDUINO_EVENT_PROV_CRED_RECV: {
            // The phone sent creds. Persist ONLY (wifi_creds_save) — do NOT
            // call voice_wifi_set_credentials() here: the provisioning manager
            // itself drives WiFi.begin() to validate these creds, and a second
            // disconnect/connect from us would fight that validation. Our NVS
            // copy is what the NEXT boot's voice_wifi_begin() reads.
            const char *ssid =
                reinterpret_cast<const char *>(sys_event->event_info.prov_cred_recv.ssid);
            const char *pass =
                reinterpret_cast<const char *>(sys_event->event_info.prov_cred_recv.password);
            Serial.printf("[prov] credentials received (ssid=%s) — persisting to NVS\n",
                          ssid != nullptr ? ssid : "?");
            Serial.flush();
            wifi_creds_save(ssid, pass);
            break;
        }

        case ARDUINO_EVENT_PROV_CRED_FAIL:
            // Wrong Wi-Fi password / AP unreachable. The manager keeps the
            // session open for a retry from the phone; we do NOT persist a bad
            // network (wifi_creds_save already ran on RECV — but the manager
            // re-sends on retry, overwriting it).
            Serial.println("[prov] credential validation FAILED (wrong Wi-Fi password / AP down?)");
            Serial.flush();
            break;

        case ARDUINO_EVENT_PROV_CRED_SUCCESS:
            Serial.println("[prov] credentials accepted; Wi-Fi connected");
            Serial.flush();
            s_active = false;
            s_succeeded = true;  // B.3 — main loop reboots to load new NVS creds
            break;

        case ARDUINO_EVENT_PROV_END:
            Serial.println("[prov] provisioning session ended");
            Serial.flush();
            s_active = false;
            break;

        default:
            break;
    }
}

void ble_provisioning_begin() {
    // Pre-flight diagnostic. The BLE controller allocates a large block of
    // INTERNAL (DMA-capable) DRAM; a crash inside btdm_controller_init has two
    // very different causes that look identical from the backtrace:
    //   (a) the heap was already corrupted by an earlier overflow, or
    //   (b) internal DRAM is exhausted/too fragmented for the controller.
    // Printing integrity + internal free/largest-block here separates them
    // without another guess-and-reflash cycle.
    {
        const bool heap_ok = heap_caps_check_integrity_all(true);
        Serial.printf("[prov] pre-BLE heap_integrity=%s internal_free=%u largest_internal_block=%u\n",
                      heap_ok ? "OK" : "CORRUPT",
                      (unsigned)heap_caps_get_free_size(MALLOC_CAP_INTERNAL),
                      (unsigned)heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL));
        Serial.flush();
    }

    WiFi.onEvent(prov_event);
    s_active = true;
    // SCHEME_BLE: BLE transport. SECURITY_1: curve25519 key-exchange + AES-CTR
    // with a proof-of-possession — the creds travel encrypted phone->toy and
    // never touch our backend. reset_provisioned defaults true (clears any
    // IDF-side stored creds at session start); we keep our own NVS copy
    // regardless. Core 3.x renamed the WIFI_PROV_* constants to NETWORK_PROV_*.
    //
    // MEMORY-HANDLER CHOICE IS CHIP-SPECIFIC — getting it wrong is fatal, not
    // merely wasteful. HANDLER_FREE_BTDM releases the Bluetooth CLASSIC
    // controller memory, which only exists on the original ESP32. The ESP32-S3
    // is BLE-only: asking it to free a BT-classic pool that was never
    // allocated corrupted the heap and the next allocation inside
    // btdm_controller_init crashed with LoadProhibited on a garbage free-block
    // pointer (observed on hardware 2026-08-02 — a boot loop, which is why the
    // toy never appeared in the phone's scan list). On BLE-only targets the
    // correct value is HANDLER_NONE.
#if CONFIG_IDF_TARGET_ESP32
    const scheme_handler_t kMemHandler = NETWORK_PROV_SCHEME_HANDLER_FREE_BTDM;
#else
    const scheme_handler_t kMemHandler = NETWORK_PROV_SCHEME_HANDLER_NONE;
#endif

    // Factory pairing (2026-09-11): the toy advertises its OWN PoP, burned to
    // NVS at manufacture, so a printed pairing code on one box does not open
    // every other toy. Falls back to the shared bench placeholder only for a
    // unit with nothing in NVS (never flashed through the factory station).
    static char s_pop[24];
    const char *pop = AREG_PROV_POP;
    if (device_creds_pop_load(s_pop, sizeof(s_pop))
        && device_creds_rules::pop_is_wellformed(s_pop)) {
        pop = s_pop;
    } else if (device_creds_present()) {
        // An id/key were burned but the PoP is missing or malformed (torn
        // write, older factory-station version, hand-edited NVS image) —
        // worth a distinct log line, since this toy WAS factory-provisioned
        // and a silent fallback here would look identical to "never
        // provisioned" in the serial log.
        Serial.println("[prov] stored PoP missing/malformed — using the bench fallback");
    } else {
        Serial.println("[prov] no per-device PoP in NVS — using the bench fallback");
    }

    WiFiProv.beginProvision(
        NETWORK_PROV_SCHEME_BLE,
        kMemHandler,
        NETWORK_PROV_SECURITY_1,
        pop,
        AREG_PROV_SERVICE_NAME);
    Serial.printf("[prov] advertising BLE service '%s' (pop set)\n",
                  AREG_PROV_SERVICE_NAME);
    Serial.flush();
}

bool ble_provisioning_active() {
    return s_active;
}

bool ble_provisioning_succeeded() {
    return s_succeeded;
}

#endif  // AREG_USE_BLE_PROVISIONING

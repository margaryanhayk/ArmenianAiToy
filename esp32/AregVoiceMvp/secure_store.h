#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / secure_store.h -- the application's NVS store.
//
// Every Preferences namespace the app uses (aregdev, aregwifi, aregota,
// csync, areggplays, areggame, aregplays, aregstory, aregheard, aregvoice,
// aregqidx, aregmusic, aregstate) lives in the dedicated "nvs_sec"
// partition, NOT in the default "nvs":
//
//  * On a locked RELEASE toy nvs_sec is NVS-encrypted (HMAC scheme, eFuse
//    key HMAC_UP in BLOCK_KEY4, burned at the factory -- never generated
//    here). A flash-chip reader gets ciphertext; a tampered/wrong-key image
//    reads as "not found", never as garbage (docs/firmware-security.md).
//  * The default "nvs" stays plaintext and belongs to Arduino / ESP-IDF
//    (PHY calibration, Wi-Fi driver, BLE). Arduino's initArduino() plain-
//    inits it before setup(); a plain init of an ENCRYPTED partition would
//    decode garbage and erase entries -- which is why the store is separate
//    and why every open goes through areg_prefs_begin(): before
//    secure_store_begin() succeeded, Preferences::begin(ns, ro, "nvs_sec")
//    would plain-init (and on a release toy destroy) the partition.
//
// Legacy mode (DEV only): a chip still on the pre-2026-10-08 partition table
// has no nvs_sec. A DEV image then keeps every namespace in the default
// "nvs" exactly as the old firmware did (identity, Wi-Fi, OTA state survive
// an OTA onto the new firmware); purges and wipes are refused there. A
// RELEASE image on such a chip refuses the store (secure_store_rules.h).
//
// Contract: secure_store_begin() is the FIRST thing setup() does after
// Serial.begin(), before any AREG_PROVISION_* burn and before Wi-Fi/BT.
// -------------------------------------------------------------
#include <Arduino.h>
#include <Preferences.h>

#include "secure_store_rules.h"

// Decides the store mode from the profile + eFuses, opens nvs_sec
// accordingly, then runs the pending default-nvs purge (if requested by an
// earlier boot). Returns true when the store is usable.
bool secure_store_begin();

bool secure_store_ready();
secure_store_rules::StoreMode secure_store_mode();

// The ONLY sanctioned way to open an app namespace. Returns false (exactly
// like a failed Preferences::begin) unless the store is ready; every caller
// already tolerates begin()==false.
bool areg_prefs_begin(Preferences &p, const char *ns, bool read_only);

// Marks the default "nvs" for a physical erase at the top of the next boot
// (Wi-Fi driver's plaintext copy of the password). Called on every BLE
// credential receipt and on the 5-second "forget Wi-Fi" gesture. Returns
// true when a purge is now pending (false in legacy mode, where the default
// nvs IS the identity and is never purged, or when the store is unusable).
bool secure_store_request_default_nvs_purge();

// Factory reset of the app store: deinit + erase + re-init nvs_sec in the
// same mode, re-stamp the layout, then request the default-nvs purge.
// Destroys the device identity -- a wiped release toy can only be re-
// identified at the factory station. Not wired to any command in v1.
bool secure_store_wipe();

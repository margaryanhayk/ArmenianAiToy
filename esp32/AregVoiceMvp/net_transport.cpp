#include "net_transport.h"

#include <WiFiClientSecure.h>

#include <string.h>

#include "config.h"
#include "security_posture.h"   // security_network_allowed: factory not finished => offline
#include "security_profile.h"
#include "tls_trust_anchors.h"
#include "voice_client.h"  // voice_device_identity_ready: no identity, no backend call

// Trust anchors: tls_trust_anchors.{h,cpp} -- several independent root CAs
// (ISRG X1/X2, GTS R1/R4, DigiCert G2), never a leaf or intermediate (those
// rotate every ~90 days). A locked toy can only be updated over this very
// connection, so the set must survive a CA move; a RELEASE build refuses
// fewer than three (security_profile.h, check_release_image.py).

// One shared TLS client for the whole firmware. Reused across requests so
// the ~40-50 KB TLS working set is paid once rather than per call site.
// Safe because the toy is single-threaded for backend work: a voice turn,
// an OTA poll and a content sync never overlap (the .ino runs them from
// the same loop, and OTA polling is paused during a voice turn).
static WiFiClientSecure &tls_client() {
    static WiFiClientSecure *client = nullptr;
    if (client == nullptr) {
        client = new WiFiClientSecure();
#ifdef AREG_TLS_INSECURE
        client->setInsecure();
#else
        client->setCACert(kAregTlsTrustAnchorsPem);
#endif
    }
    return *client;
}

bool areg_tls_is_insecure() {
#ifdef AREG_TLS_INSECURE
    return true;
#else
    return false;
#endif
}

void areg_transport_log_policy() {
    if (areg_tls_is_insecure()) {
#ifdef AREG_TLS_INSECURE
        // Compiled only into an insecure build, so the release gate can
        // refuse any image that carries this banner (check_release_image.py).
        Serial.println("[net] *** TLS INSECURE BUILD — server identity NOT verified. Bench only. ***");
#endif
    } else {
        Serial.printf("[net] TLS: verifying (%d pinned root CAs)\n", AREG_CA_ANCHOR_COUNT);
    }
    Serial.flush();
}

void areg_net_reset() {
    tls_client().stop();
}

bool areg_backend_allowed() {
    // A RELEASE toy that left the factory half-way (Secure Boot + flash
    // encryption on, ROM download mode still open) never goes online: over
    // the cable, RAM code could derive its NVS keys and read the device key
    // (and a family's Wi-Fi once it had one). It stays visibly dead instead.
    if (!security_network_allowed()) {
        return false;
    }
    // A RELEASE toy without a factory identity never talks to the backend
    // (it would only be refused, and a placeholder identity must never be
    // sent). DEV builds always have one (the config.h fallback).
    return voice_device_identity_ready();
}

bool areg_url_allowed(const char *url) {
#if AREG_IS_RELEASE
    // RELEASE: TLS or nothing. Every caller adds the device key (or a story
    // token) to the request, and a server-supplied absolute http:// URL --
    // one operator-configured AudioUrl / FirmwareUpdate:Url -- would send it
    // in cleartext or let a network attacker inject audio.
    return url != nullptr && strncmp(url, "https://", 8) == 0;
#else
    return url != nullptr;
#endif
}

bool areg_http_begin(HTTPClient &http, const String &url) {
    if (!areg_backend_allowed()) {
        return false;
    }
    if (!areg_url_allowed(url.c_str())) {
        Serial.println("[net] refused non-https URL (release)");
        Serial.flush();
        return false;
    }
    // The live server 301s http -> https (HSTS). Following redirects keeps
    // a stale http:// URL working instead of failing with a bare 301 that
    // reads like a server error in the logs.
    http.setFollowRedirects(HTTPC_STRICT_FOLLOW_REDIRECTS);

    if (url.startsWith("https://")) {
        return http.begin(tls_client(), url);
    }
    return http.begin(url);
}

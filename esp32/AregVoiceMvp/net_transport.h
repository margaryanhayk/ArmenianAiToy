// -------------------------------------------------------------
// net_transport.h — ONE place that opens every backend HTTP(S) call.
//
// WHY THIS EXISTS
// The toy talked to a LAN bench server over plain http://. The live
// server is HTTPS-only (and sends HSTS), so every call had to learn TLS.
// Rather than sprinkle WiFiClientSecure across the 15 call sites in
// voice_client / ota_foundation / ota_apply / content_sync, they all now
// route through areg_http_begin(): http:// behaves exactly as before,
// https:// gets a TLS client. One seam to reason about, one place to
// change when certificate policy changes.
//
// CERTIFICATE POLICY (AREG_TLS_INSECURE)
// Default is VERIFYING: the CA in net_transport.cpp is pinned and a
// forged/MITM certificate is rejected. Defining AREG_TLS_INSECURE in
// config.h switches to setInsecure() — encrypted but UNAUTHENTICATED,
// i.e. it will happily talk to an impostor. That is a bench-triage
// escape hatch ONLY (e.g. to prove a failure is certificate-related,
// not network-related); never ship it. A loud serial warning is printed
// on every boot when it is on, so it cannot slip into a build unnoticed.
//
// MEMORY NOTE
// A TLS session needs roughly 40-50 KB of heap on top of the audio
// buffers. One shared client instance is reused for every request
// instead of one per call site, so the peak is paid once.
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>
#include <HTTPClient.h>

// Opens `url` on `http`, choosing plain or TLS transport from the scheme.
// Returns HTTPClient::begin()'s result. Follows redirects, which matters
// because the live server 301s http -> https.
bool areg_http_begin(HTTPClient &http, const String &url);

// Hard-stop the shared TLS client. For error recovery in long download
// bursts: when a keep-alive connection wedges (server closed it, socket
// churn), the next begin() on a stopped client performs a clean fresh
// handshake instead of inheriting a dead connection.
void areg_net_reset();

// True when this build talks TLS without verifying the server identity.
bool areg_tls_is_insecure();

// One-line boot banner: which transport policy is active. Called from
// setup() so a bench log always states whether certificates are checked.
void areg_transport_log_policy();

#include "net_transport.h"

#include <WiFiClientSecure.h>

#include "config.h"

// ISRG Root X1 — the Let's Encrypt root. The live server's certificate
// chains to it (verified 2026-08-02: CN=*.up.railway.app, issued by a
// Let's Encrypt E-series intermediate). Pinning the ROOT rather than the
// leaf or intermediate is deliberate: leaf certificates rotate every ~90
// days and intermediates rotate too, so pinning either would brick every
// toy in the field on a routine renewal. This root is valid to 2035.
//
// If the host is ever moved off Let's Encrypt, this constant is the one
// thing that must change (and it must ship in the SAME firmware update
// as the move, or devices lose the backend).
static const char kIsrgRootX1[] PROGMEM = R"CERT(
-----BEGIN CERTIFICATE-----
MIIFazCCA1OgAwIBAgIRAIIQz7DSQONZRGPgu2OCiwAwDQYJKoZIhvcNAQELBQAw
TzELMAkGA1UEBhMCVVMxKTAnBgNVBAoTIEludGVybmV0IFNlY3VyaXR5IFJlc2Vh
cmNoIEdyb3VwMRUwEwYDVQQDEwxJU1JHIFJvb3QgWDEwHhcNMTUwNjA0MTEwNDM4
WhcNMzUwNjA0MTEwNDM4WjBPMQswCQYDVQQGEwJVUzEpMCcGA1UEChMgSW50ZXJu
ZXQgU2VjdXJpdHkgUmVzZWFyY2ggR3JvdXAxFTATBgNVBAMTDElTUkcgUm9vdCBY
MTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAK3oJHP0FDfzm54rVygc
h77ct984kIxuPOZXoHj3dcKi/vVqbvYATyjb3miGbESTtrFj/RQSa78f0uoxmyF+
0TM8ukj13Xnfs7j/EvEhmkvBioZxaUpmZmyPfjxwv60pIgbz5MDmgK7iS4+3mX6U
A5/TR5d8mUgjU+g4rk8Kb4Mu0UlXjIB0ttov0DiNewNwIRt18jA8+o+u3dpjq+sW
T8KOEUt+zwvo/7V3LvSye0rgTBIlDHCNAymg4VMk7BPZ7hm/ELNKjD+Jo2FR3qyH
B5T0Y3HsLuJvW5iB4YlcNHlsdu87kGJ55tukmi8mxdAQ4Q7e2RCOFvu396j3x+UC
B5iPNgiV5+I3lg02dZ77DnKxHZu8A/lJBdiB3QW0KtZB6awBdpUKD9jf1b0SHzUv
KBds0pjBqAlkd25HN7rOrFleaJ1/ctaJxQZBKT5ZPt0m9STJEadao0xAH0ahmbWn
OlFuhjuefXKnEgV4We0+UXgVCwOPjdAvBbI+e0ocS3MFEvzG6uBQE3xDk3SzynTn
jh8BCNAw1FtxNrQHusEwMFxIt4I7mKZ9YIqioymCzLq9gwQbooMDQaHWBfEbwrbw
qHyGO0aoSCqI3Haadr8faqU9GY/rOPNk3sgrDQoo//fb4hVC1CLQJ13hef4Y53CI
rU7m2Ys6xt0nUW7/vGT1M0NPAgMBAAGjQjBAMA4GA1UdDwEB/wQEAwIBBjAPBgNV
HRMBAf8EBTADAQH/MB0GA1UdDgQWBBR5tFnme7bl5AFzgAiIyBpY9umbbjANBgkq
hkiG9w0BAQsFAAOCAgEAVR9YqbyyqFDQDLHYGmkgJykIrGF1XIpu+ILlaS/V9lZL
ubhzEFnTIZd+50xx+7LSYK05qAvqFyFWhfFQDlnrzuBZ6brJFe+GnY+EgPbk6ZGQ
3BebYhtF8GaV0nxvwuo77x/Py9auJ/GpsMiu/X1+mvoiBOv/2X/qkSsisRcOj/KK
NFtY2PwByVS5uCbMiogziUwthDyC3+6WVwW6LLv3xLfHTjuCvjHIInNzktHCgKQ5
ORAzI4JMPJ+GslWYHb4phowim57iaztXOoJwTdwJx4nLCgdNbOhdjsnvzqvHu7Ur
TkXWStAmzOVyyghqpZXjFaH3pO3JLF+l+/+sKAIuvtd7u+Nxe5AW0wdeRlN8NwdC
jNPElpzVmbUq4JUagEiuTDkHzsxHpFKVK7q4+63SM1N95R1NbdWhscdCb+ZAJzVc
oyi3B43njTOQ5yOf+1CceWxG1bQVs5ZufpsMljq4Ui0/1lvh+wjChP4kqKOJ2qxq
4RgqsahDYVvTH9w7jXbyLeiNdd8XM2w9U/t7y0Ff/9yi0GE44Za4rF2LN9d11TPA
mRGunUHBcnWEvgJBQl9nJEiU0Zsnvgc/ubhPgXRR4Xq37Z0j4r7g1SgEEzwxA57d
emyPxgcYxn/eR44/KJ4EBs+lVDR3veyJm+kXQ99b21/+jh5Xos1AnX5iItreGCc=
-----END CERTIFICATE-----
)CERT";

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
        client->setCACert(kIsrgRootX1);
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
        Serial.println("[net] *** TLS INSECURE BUILD — server identity NOT verified. Bench only. ***");
    } else {
        Serial.println("[net] TLS: verifying (ISRG Root X1 pinned)");
    }
    Serial.flush();
}

void areg_net_reset() {
    tls_client().stop();
}

bool areg_http_begin(HTTPClient &http, const String &url) {
    // The live server 301s http -> https (HSTS). Following redirects keeps
    // a stale http:// URL working instead of failing with a bare 301 that
    // reads like a server error in the logs.
    http.setFollowRedirects(HTTPC_STRICT_FOLLOW_REDIRECTS);

    if (url.startsWith("https://")) {
        return http.begin(tls_client(), url);
    }
    return http.begin(url);
}

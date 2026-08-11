#!/bin/sh
# Render the PHONE app (mobile/AregParent) in a desktop browser, against the
# mock backend, so its fourteen screens can actually be looked at.
#
# There is no CI for the mobile app and no simulator here, so before this the
# only check available was `tsc --noEmit` — which says nothing about whether a
# screen renders or what colour it came out.
#
# Two traps, both hit the hard way, both encoded below:
#
#   1. `--clear` is REQUIRED. Without it Expo serves a cached bundle and
#      EXPO_PUBLIC_API_BASE_URL is silently NOT inlined, so the app quietly
#      talks to PRODUCTION while appearing to be configured for the mock.
#   2. Chromium must be launched with `--no-proxy-server`. This container
#      routes egress through a proxy, and a page on :5300 calling the mock on
#      :5099 is cross-origin — the request goes to the proxy and dies as
#      ERR_TUNNEL_CONNECTION_FAILED, which reads like a CORS or server bug.
#
# The mock also has to send CORS headers; it does (see mock-server.js).
#
# Usage:  sh tools/dashboard-audit/mobile-preview.sh [outdir]
# Then:   node tools/dashboard-audit/mobile-preview.js   (or your own script)

set -e
OUT="${1:-/tmp/areg-mobile-web}"
API="${AREG_MOCK_API:-http://127.0.0.1:5099}"

cd "$(dirname "$0")/../../mobile/AregParent"

[ -d node_modules ] || npm install --no-audit --no-fund

echo "Exporting against $API (with --clear, see note above)…"
EXPO_PUBLIC_API_BASE_URL="$API" npx expo export --clear --platform web --output-dir "$OUT"

# Prove the base url really made it into the bundle rather than trusting it.
if grep -rq "$(echo "$API" | sed 's|https\{0,1\}://||')" "$OUT/_expo"; then
  echo "OK: $API is inlined in the bundle."
else
  echo "FAILED: $API is NOT in the bundle - it would call production." >&2
  exit 1
fi

echo
echo "Now serve it and point a browser at it:"
echo "  (cd $OUT && python3 -m http.server 5300)"
echo "  node tools/dashboard-audit/mock-server.js       # the backend"
echo
echo "Launch Chromium with args: ['--no-proxy-server']"

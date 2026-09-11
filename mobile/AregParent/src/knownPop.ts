// Session-only cache of a toy's proof-of-possession (BLE pairing secret),
// keyed by device id. Filled when a parent pastes the pairing QR text while
// claiming a toy (see DevicesScreen.tsx + pairingQr.ts) so they don't have
// to retype the 8-character PoP by hand a few taps later on the Wi-Fi setup
// screen (ProvisioningScreen.tsx).
//
// Deliberately NEVER persisted — no SecureStore, no disk, nothing that
// survives an app restart. This mirrors the backend's own posture on the
// PoP (DeviceService.GeneratePop: "never persisted, not even hashed" —
// see CLAUDE.md "Per-toy BLE PoP + factory station"). Lost on restart is
// fine: ProvisioningScreen's typed field is always there as the fallback.
const known = new Map<string, string>();

export function rememberPop(deviceId: string, pop: string | undefined): void {
  if (deviceId && pop) known.set(deviceId, pop);
}

export function knownPop(deviceId: string): string | undefined {
  return known.get(deviceId);
}

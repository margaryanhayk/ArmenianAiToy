// Lenient parser for a toy's pairing QR payload, pasted as decoded text
// (this app has no camera scanner — see DevicesScreen.tsx's "paste" field).
//
// Current shape (factory pairing, 2026-09-11): {"deviceId":"...","claim":"...","pop":"..."}
// Older shape (toys boxed before that date): {"deviceId":"...","claim":"..."}
// Both must keep working — a parent's box does not know which firmware
// generation minted its QR. A few likely key spellings are accepted too,
// in case a parent hand-copies the JSON rather than pasting a scanner's
// exact output.
export type ParsedPairingQr = { deviceId: string; claim: string; pop?: string };

function pickString(o: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = o[k];
    if (typeof v === 'string' && v.trim()) return v.trim();
  }
  return undefined;
}

export function parsePairingQr(raw: string): ParsedPairingQr | null {
  const text = raw.trim();
  if (!text) return null;
  let obj: unknown;
  try {
    obj = JSON.parse(text);
  } catch {
    return null;
  }
  if (typeof obj !== 'object' || obj === null) return null;
  const o = obj as Record<string, unknown>;

  const deviceId = pickString(o, 'deviceId', 'device_id', 'id');
  const claim = pickString(o, 'claim', 'claimCode', 'claim_code', 'code');
  if (!deviceId || !claim) return null;

  const pop = pickString(o, 'pop', 'proofOfPossession', 'proof_of_possession');
  return pop ? { deviceId, claim, pop } : { deviceId, claim };
}

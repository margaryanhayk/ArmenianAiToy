import { useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  PermissionsAndroid,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
// Type-only import is erased at build time, so it never pulls the native module
// into a bundle. The runtime module is loaded lazily in loadEsp() below.
import type { ESPDevice, ESPWifiList } from '@orbital-systems/react-native-esp-idf-provisioning';
import { LinkedDevice } from '../api';
import { t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

// Must match the firmware (ble_provisioning.cpp): the toy advertises
// "Areg-Setup" (prefix "Areg"), security 1. The proof-of-possession itself
// is no longer one shared constant (factory pairing, 2026-09-11) — every toy
// now has its own, printed on the box (and inside its claim QR's "pop"
// field). BENCH_FALLBACK_POP is only what an un-provisioned bench unit
// (nothing burned to NVS yet) still advertises.
//
// Confirmed (2026-09-12, N7) that this constant cannot be used against a
// production-provisioned toy: it is display text only, never sent over
// BLE. `dev.connect(trimmedPop)` below always uses whatever the parent
// actually typed into the `pop` field (required non-empty before a search
// can even start — see startSearch()); this file never substitutes
// BENCH_FALLBACK_POP into that call. The one place it IS shown
// (the "unavailable" card, `pop.trim() || BENCH_FALLBACK_POP`) is
// unreachable with an empty pop in practice, since reaching "unavailable"
// requires trimmedPop to already be non-empty — so it is dead-code display
// text, not a working credential. A wrong PoP is rejected by the TOY'S
// OWN BLE stack (per-toy PoP validated against NVS, ble_provisioning.cpp),
// not by anything this app checks — no __DEV__ gate is needed because
// there is no code path here that could authenticate against a real toy
// with the bench value.
const PREFIX = 'Areg';
const BENCH_FALLBACK_POP = 'areg-pair';

type EspModule = {
  ESPProvisionManager: {
    searchESPDevices(prefix: string, transport: string, security: number): Promise<ESPDevice[]>;
  };
  ESPTransport: { ble: string };
  ESPSecurity: { secure: number };
};

// Returns the native ESP module, or null when it isn't present (Expo Go, or any
// build without the dev-client + native module). Web never reaches this file —
// Metro resolves ProvisioningScreen.web.tsx there.
function loadEsp(): EspModule | null {
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    return require('@orbital-systems/react-native-esp-idf-provisioning') as EspModule;
  } catch {
    return null;
  }
}

type Phase = 'idle' | 'searching' | 'connecting' | 'wifi' | 'provisioning' | 'done' | 'unavailable';

// How long to wait for a BLE scan to return before giving up and letting
// the parent retry — searchESPDevices has no timeout of its own, so a toy
// that never answers (out of range, already left setup mode) would
// otherwise leave the screen on "Looking for your toy…" forever.
const SCAN_TIMEOUT_MS = 20000;

function withTimeout<T>(p: Promise<T>, ms: number): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('timeout')), ms);
    p.then(
      (v) => {
        clearTimeout(timer);
        resolve(v);
      },
      (e) => {
        clearTimeout(timer);
        reject(e);
      },
    );
  });
}

/**
 * Android 12+ (API 31+) requires BLUETOOTH_SCAN/BLUETOOTH_CONNECT to be
 * granted at RUNTIME, not just declared in the manifest; older Android
 * needs ACCESS_FINE_LOCATION for BLE scanning instead. The native module
 * (EspIdfProvisioningModule.kt) only checks — it never asks — so this must
 * happen before the first searchESPDevices call, every time, since a
 * parent can revoke the grant from system Settings between runs.
 *
 * Requests ONLY the permissions the native module's own gate actually
 * checks for this API level — never all three unconditionally. The
 * provisioning library's manifest declares ACCESS_FINE_LOCATION with no
 * `maxSdkVersion` cap, so on API 31+ an unconditional request would still
 * surface the system's precise-location dialog even though BLE scanning
 * there needs BLUETOOTH_SCAN/CONNECT instead — an unexplained location
 * prompt during Wi-Fi setup on a children's toy is its own trust problem,
 * not just a redundant one.
 */
async function ensureBlePermissions(): Promise<boolean> {
  if (Platform.OS !== 'android') return true;
  try {
    // Exact mirror of the native module's own hasBTPermissions() gate
    // (EspIdfProvisioningModule.kt): API 31+ needs BOTH BLUETOOTH_SCAN and
    // BLUETOOTH_CONNECT; below that it needs ACCESS_FINE_LOCATION instead
    // (BLUETOOTH/BLUETOOTH_ADMIN are normal permissions there, granted at
    // install, never a runtime prompt).
    const apiLevel =
      typeof Platform.Version === 'number' ? Platform.Version : parseInt(String(Platform.Version), 10);
    const wanted =
      apiLevel >= 31
        ? [PermissionsAndroid.PERMISSIONS.BLUETOOTH_SCAN, PermissionsAndroid.PERMISSIONS.BLUETOOTH_CONNECT]
        : [PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION];
    const results = await PermissionsAndroid.requestMultiple(wanted);
    const granted = (perm: string) =>
      (results as Record<string, string>)[perm] === PermissionsAndroid.RESULTS.GRANTED;
    return wanted.every(granted);
  } catch {
    return false;
  }
}

type Props = {
  device: LinkedDevice;
  onBack: () => void;
  // Pairing (2026-09-11): the PoP printed on the box travels inside the
  // claim QR's JSON as {deviceId, claim, pop} — a caller that scanned that
  // QR to claim this toy can pass the parsed "pop" straight through here.
  // No scanner is wired into the claim flow yet (DevicesScreen.tsx takes
  // deviceId/claim as typed text), so in practice this is almost always
  // undefined today and the field below is the real, always-available path.
  initialPop?: string;
};

export default function ProvisioningScreen({ device, onBack, initialPop }: Props) {
  useLang();
  const [phase, setPhase] = useState<Phase>('idle');
  const [error, setError] = useState<string | null>(null);
  const [espDevice, setEspDevice] = useState<ESPDevice | null>(null);
  const [networks, setNetworks] = useState<ESPWifiList[]>([]);
  const [ssid, setSsid] = useState('');
  const [password, setPassword] = useState('');
  const [pop, setPop] = useState(initialPop?.trim() ?? '');

  async function startSearch() {
    setError(null);
    const trimmedPop = pop.trim();
    if (!trimmedPop) {
      setError(t('e_pop_required'));
      return;
    }
    const esp = loadEsp();
    if (!esp) {
      setPhase('unavailable');
      return;
    }
    const hasPermission = await ensureBlePermissions();
    if (!hasPermission) {
      setError(t('e_ble_permission'));
      return;
    }
    setPhase('searching');
    try {
      const found = await withTimeout(
        esp.ESPProvisionManager.searchESPDevices(PREFIX, esp.ESPTransport.ble, esp.ESPSecurity.secure),
        SCAN_TIMEOUT_MS,
      );
      if (!found.length) {
        setError(t('e_no_toy_found'));
        setPhase('idle');
        return;
      }
      const dev = found[0];
      setEspDevice(dev);
      setPhase('connecting');
      await withTimeout(dev.connect(trimmedPop), SCAN_TIMEOUT_MS);
      const list = await dev.scanWifiList();
      // Strongest signal first, de-duplicated by ssid.
      const seen = new Set<string>();
      const unique = list
        .filter((n) => n.ssid && !seen.has(n.ssid) && seen.add(n.ssid))
        .sort((a, b) => b.rssi - a.rssi);
      setNetworks(unique);
      setPhase('wifi');
    } catch (e) {
      // The retry action is the same primary button, back in view once the
      // phase resets to idle — a distinct message just tells the parent
      // WHAT to check before pressing it again.
      setError(e instanceof Error && e.message === 'timeout' ? t('e_ble_timeout') : t('e_bluetooth'));
      setPhase('idle');
    }
  }

  async function provision() {
    if (!espDevice || !ssid) return;
    setError(null);
    setPhase('provisioning');
    try {
      const res = await espDevice.provision(ssid, password);
      espDevice.disconnect();
      if (res.status && res.status.toLowerCase().includes('success')) {
        setPhase('done');
      } else {
        setError(tf('e_wifi_join', { ssid }));
        setPhase('wifi');
      }
    } catch (e) {
      setError(t('e_send_wifi'));
      setPhase('wifi');
    }
  }

  return (
    <View style={styles.container}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_settings')}</Text>
      </Pressable>
      <Text style={styles.title}>{tf('wifi_title', { name: device.deviceName || t('toy_word') })}</Text>

      {phase === 'unavailable' ? (
        <View style={styles.card}>
          <Text style={styles.body}>{t('wifi_unavailable')}</Text>
          <Text style={styles.code}>{pop.trim() || BENCH_FALLBACK_POP}</Text>
        </View>
      ) : phase === 'idle' ? (
        <View style={styles.card}>
          <Text style={styles.body}>{t('wifi_setup_mode')}</Text>
          {Platform.OS === 'android' ? <Text style={styles.body}>{t('wifi_ble_ask')}</Text> : null}
          <Text style={styles.label}>{t('wifi_pop_label')}</Text>
          <TextInput
            style={styles.input}
            placeholder={t('ph_pop')}
            autoCapitalize="characters"
            value={pop}
            onChangeText={setPop}
          />
          <Pressable style={styles.primaryBtn} onPress={startSearch}>
            <Text style={styles.primaryBtnText}>{t('wifi_search')}</Text>
          </Pressable>
        </View>
      ) : phase === 'searching' || phase === 'connecting' ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={theme.brand} />
          <Text style={styles.body}>
            {phase === 'searching' ? t('wifi_looking') : t('wifi_connecting')}
          </Text>
        </View>
      ) : phase === 'provisioning' ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={theme.brand} />
          <Text style={styles.body}>{t('wifi_sending')}</Text>
        </View>
      ) : phase === 'done' ? (
        <View style={styles.card}>
          <Text style={styles.doneIcon}>✓</Text>
          <Text style={styles.doneText}>{t('wifi_done')}</Text>
          <Text style={styles.body}>{t('wifi_done_hint')}</Text>
          <Pressable style={styles.primaryBtn} onPress={onBack}>
            <Text style={styles.primaryBtnText}>{t('done')}</Text>
          </Pressable>
        </View>
      ) : (
        // wifi list
        <View style={{ flex: 1 }}>
          <Text style={styles.body}>{t('wifi_pick')}</Text>
          <FlatList
            data={networks}
            keyExtractor={(n, i) => `${n.ssid}-${i}`}
            style={{ maxHeight: 220 }}
            renderItem={({ item }) => (
              <Pressable
                style={[styles.netRow, item.ssid === ssid ? styles.netSelected : null]}
                onPress={() => setSsid(item.ssid)}
              >
                <Text style={styles.netName}>{item.ssid}</Text>
                {item.ssid === ssid ? <Text style={styles.netCheck}>✓</Text> : null}
              </Pressable>
            )}
            ListEmptyComponent={<Text style={styles.body}>{t('wifi_none')}</Text>}
          />
          <TextInput
            style={styles.input}
            placeholder={ssid ? tf('wifi_password_for', { ssid }) : t('wifi_password')}
            secureTextEntry
            value={password}
            onChangeText={setPassword}
          />
          <Pressable
            style={[styles.primaryBtn, !ssid ? styles.disabled : null]}
            onPress={provision}
            disabled={!ssid}
          >
            <Text style={styles.primaryBtnText}>{t('wifi_send')}</Text>
          </Pressable>
        </View>
      )}

      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 16 },
  card: { borderWidth: 1, borderColor: theme.line, borderRadius: 10, padding: 16, backgroundColor: theme.surfaceSunken },
  center: { alignItems: 'center', marginTop: 40 },
  body: { color: theme.inkMuted, fontSize: 15, marginBottom: 10 },
  label: { color: theme.ink, fontSize: 13, fontWeight: '600', marginBottom: 4 },
  code: { fontSize: 18, fontWeight: '700', color: theme.warn },
  primaryBtn: { backgroundColor: theme.brand, borderRadius: 8, padding: 13, alignItems: 'center', marginTop: 6 },
  primaryBtnText: { color: theme.surface, fontWeight: '600' },
  disabled: { opacity: 0.5 },
  netRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    padding: 12,
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 8,
    marginBottom: 6,
    backgroundColor: theme.surfaceSunken,
  },
  netSelected: { borderColor: theme.brand, backgroundColor: theme.brandTint },
  netName: { fontSize: 15, color: theme.ink },
  netCheck: { color: theme.brand, fontWeight: '700' },
  input: { borderWidth: 1, borderColor: theme.lineInput, borderRadius: 8, padding: 10, marginTop: 10 },
  doneIcon: { fontSize: 44, color: theme.okLine, textAlign: 'center' },
  doneText: { fontSize: 18, fontWeight: '700', color: theme.ok, textAlign: 'center', marginVertical: 8 },
  error: { color: theme.danger, marginTop: 12 },
});

import { useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
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

// Must match the firmware (ble_provisioning.cpp): the toy advertises
// "Areg-Setup" (prefix "Areg"), proof-of-possession "areg-pair", security 1.
const PREFIX = 'Areg';
const POP = 'areg-pair';

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

type Props = {
  device: LinkedDevice;
  onBack: () => void;
};

export default function ProvisioningScreen({ device, onBack }: Props) {
  useLang();
  const [phase, setPhase] = useState<Phase>('idle');
  const [error, setError] = useState<string | null>(null);
  const [espDevice, setEspDevice] = useState<ESPDevice | null>(null);
  const [networks, setNetworks] = useState<ESPWifiList[]>([]);
  const [ssid, setSsid] = useState('');
  const [password, setPassword] = useState('');

  async function startSearch() {
    setError(null);
    const esp = loadEsp();
    if (!esp) {
      setPhase('unavailable');
      return;
    }
    setPhase('searching');
    try {
      const found = await esp.ESPProvisionManager.searchESPDevices(
        PREFIX,
        esp.ESPTransport.ble,
        esp.ESPSecurity.secure,
      );
      if (!found.length) {
        setError(t('e_no_toy_found'));
        setPhase('idle');
        return;
      }
      const dev = found[0];
      setEspDevice(dev);
      setPhase('connecting');
      await dev.connect(POP);
      const list = await dev.scanWifiList();
      // Strongest signal first, de-duplicated by ssid.
      const seen = new Set<string>();
      const unique = list
        .filter((n) => n.ssid && !seen.has(n.ssid) && seen.add(n.ssid))
        .sort((a, b) => b.rssi - a.rssi);
      setNetworks(unique);
      setPhase('wifi');
    } catch (e) {
      setError(t('e_bluetooth'));
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
          <Text style={styles.code}>areg-pair</Text>
        </View>
      ) : phase === 'idle' ? (
        <View style={styles.card}>
          <Text style={styles.body}>{t('wifi_setup_mode')}</Text>
          <Pressable style={styles.primaryBtn} onPress={startSearch}>
            <Text style={styles.primaryBtnText}>{t('wifi_search')}</Text>
          </Pressable>
        </View>
      ) : phase === 'searching' || phase === 'connecting' ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color="#2c4a7a" />
          <Text style={styles.body}>
            {phase === 'searching' ? t('wifi_looking') : t('wifi_connecting')}
          </Text>
        </View>
      ) : phase === 'provisioning' ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color="#2c4a7a" />
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
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a', marginBottom: 16 },
  card: { borderWidth: 1, borderColor: '#e2e2e2', borderRadius: 10, padding: 16, backgroundColor: '#fafafa' },
  center: { alignItems: 'center', marginTop: 40 },
  body: { color: '#444', fontSize: 15, marginBottom: 10 },
  code: { fontSize: 18, fontWeight: '700', color: '#a06a00' },
  primaryBtn: { backgroundColor: '#2c4a7a', borderRadius: 8, padding: 13, alignItems: 'center', marginTop: 6 },
  primaryBtnText: { color: '#fff', fontWeight: '600' },
  disabled: { opacity: 0.5 },
  netRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    padding: 12,
    borderWidth: 1,
    borderColor: '#e2e2e2',
    borderRadius: 8,
    marginBottom: 6,
    backgroundColor: '#fafafa',
  },
  netSelected: { borderColor: '#2c4a7a', backgroundColor: '#eef3fb' },
  netName: { fontSize: 15, color: '#222' },
  netCheck: { color: '#2c4a7a', fontWeight: '700' },
  input: { borderWidth: 1, borderColor: '#ccc', borderRadius: 8, padding: 10, marginTop: 10 },
  doneIcon: { fontSize: 44, color: '#5aa45a', textAlign: 'center' },
  doneText: { fontSize: 18, fontWeight: '700', color: '#2f6b2f', textAlign: 'center', marginVertical: 8 },
  error: { color: '#a02622', marginTop: 12 },
});

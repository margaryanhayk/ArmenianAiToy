import { useState } from 'react';
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  View,
} from 'react-native';
import {
  LinkedDevice,
  ModeFlags,
  setBedtime,
  setModeFlags,
  setPaused,
  UnauthorizedError,
} from '../api';

type Props = {
  device: LinkedDevice;
  onBack: () => void;
  onChanged: () => Promise<void> | void;
  onLogout: () => void;
};

// "HH:mm:ss" | null  ->  "HH:mm"
function toHHmm(v: string | null): string {
  return v ? v.substring(0, 5) : '';
}

export default function DeviceSettingsScreen({ device, onBack, onChanged, onLogout }: Props) {
  const [paused, setPausedState] = useState(device.isPaused);
  const [modes, setModes] = useState<ModeFlags>({
    story: device.storyEnabled,
    game: device.gameEnabled,
    riddle: device.riddleEnabled,
    curiosity: device.curiosityEnabled,
  });
  const [bedStart, setBedStart] = useState(toHHmm(device.bedtimeStart));
  const [bedEnd, setBedEnd] = useState(toHHmm(device.bedtimeEnd));
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function flash(msg: string) {
    setError(null);
    setStatus(msg);
  }
  function fail(err: unknown) {
    if (err instanceof UnauthorizedError) return onLogout();
    setStatus(null);
    setError(err instanceof Error ? err.message : 'Failed.');
  }

  async function togglePause(value: boolean) {
    setPausedState(value);
    try {
      await setPaused(device.deviceId, value);
      flash(value ? 'Toy paused.' : 'Toy resumed.');
      await onChanged();
    } catch (e) {
      setPausedState(!value);
      fail(e);
    }
  }

  async function toggleMode(key: keyof ModeFlags, value: boolean) {
    const next = { ...modes, [key]: value };
    setModes(next);
    try {
      await setModeFlags(device.deviceId, next);
      flash('Modes updated.');
      await onChanged();
    } catch (e) {
      setModes(modes);
      fail(e);
    }
  }

  const hhmm = /^([01]\d|2[0-3]):[0-5]\d$/;

  async function saveBedtime() {
    const s = bedStart.trim();
    const e = bedEnd.trim();
    if ((s && !hhmm.test(s)) || (e && !hhmm.test(e))) {
      setError('Use 24-hour time like 21:30.');
      return;
    }
    try {
      await setBedtime(device.deviceId, s ? `${s}:00` : null, e ? `${e}:00` : null);
      flash(s && e ? `Bedtime set ${s}–${e}.` : 'Bedtime turned off.');
      await onChanged();
    } catch (err) {
      fail(err);
    }
  }

  async function clearBedtime() {
    setBedStart('');
    setBedEnd('');
    try {
      await setBedtime(device.deviceId, null, null);
      flash('Bedtime turned off.');
      await onChanged();
    } catch (err) {
      fail(err);
    }
  }

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 40 }}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>‹ Toys</Text>
      </Pressable>
      <Text style={styles.title}>{device.deviceName || 'Toy'} · Settings</Text>

      {/* Pause */}
      <View style={styles.card}>
        <View style={styles.rowBetween}>
          <View style={{ flex: 1 }}>
            <Text style={styles.rowTitle}>Pause the toy</Text>
            <Text style={styles.rowHint}>Stops all play right now until you turn it back on.</Text>
          </View>
          <Switch value={paused} onValueChange={togglePause} />
        </View>
      </View>

      {/* Modes */}
      <View style={styles.card}>
        <Text style={styles.cardTitle}>What the toy can do</Text>
        {(
          [
            ['story', 'Story'],
            ['game', 'Game'],
            ['riddle', 'Riddle'],
            ['curiosity', 'Curiosity questions'],
          ] as [keyof ModeFlags, string][]
        ).map(([key, label]) => (
          <View key={key} style={styles.rowBetween}>
            <Text style={styles.modeLabel}>{label}</Text>
            <Switch value={modes[key]} onValueChange={(v) => toggleMode(key, v)} />
          </View>
        ))}
        <Text style={styles.rowHint}>Bedtime calm-down stays available no matter what.</Text>
      </View>

      {/* Bedtime */}
      <View style={styles.card}>
        <Text style={styles.cardTitle}>Quiet hours (bedtime)</Text>
        <Text style={styles.rowHint}>The toy won&apos;t respond during this window. 24-hour time.</Text>
        <View style={styles.timeRow}>
          <TextInput
            style={styles.timeInput}
            placeholder="21:30"
            value={bedStart}
            onChangeText={setBedStart}
            maxLength={5}
          />
          <Text style={styles.dash}>to</Text>
          <TextInput
            style={styles.timeInput}
            placeholder="07:00"
            value={bedEnd}
            onChangeText={setBedEnd}
            maxLength={5}
          />
        </View>
        <View style={styles.btnRow}>
          <Pressable style={styles.primaryBtn} onPress={saveBedtime}>
            <Text style={styles.primaryBtnText}>Save</Text>
          </Pressable>
          <Pressable style={styles.secondaryBtn} onPress={clearBedtime}>
            <Text style={styles.secondaryBtnText}>Turn off</Text>
          </Pressable>
        </View>
      </View>

      {status ? <Text style={styles.status}>{status}</Text> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a', marginBottom: 16 },
  card: {
    borderWidth: 1,
    borderColor: '#e2e2e2',
    borderRadius: 10,
    padding: 14,
    marginBottom: 14,
    backgroundColor: '#fafafa',
  },
  cardTitle: { fontSize: 16, fontWeight: '600', color: '#2c4a7a', marginBottom: 8 },
  rowBetween: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingVertical: 6 },
  rowTitle: { fontSize: 16, color: '#222' },
  rowHint: { fontSize: 12, color: '#888', marginTop: 4 },
  modeLabel: { fontSize: 15, color: '#222' },
  timeRow: { flexDirection: 'row', alignItems: 'center', marginTop: 10 },
  timeInput: {
    borderWidth: 1,
    borderColor: '#ccc',
    borderRadius: 8,
    padding: 8,
    width: 90,
    textAlign: 'center',
    fontSize: 16,
  },
  dash: { marginHorizontal: 12, color: '#555' },
  btnRow: { flexDirection: 'row', marginTop: 12 },
  primaryBtn: { backgroundColor: '#2c4a7a', borderRadius: 8, paddingVertical: 10, paddingHorizontal: 20, marginRight: 10 },
  primaryBtnText: { color: '#fff', fontWeight: '600' },
  secondaryBtn: { borderWidth: 1, borderColor: '#999', borderRadius: 8, paddingVertical: 10, paddingHorizontal: 16 },
  secondaryBtnText: { color: '#555', fontWeight: '600' },
  status: { color: '#2f6b2f', marginTop: 8 },
  error: { color: '#a02622', marginTop: 8 },
});

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
  errText,
  LinkedDevice,
  ModeFlags,
  setBedtime,
  setModeFlags,
  setPaused,
  UnauthorizedError,
} from '../api';
import { t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

type Props = {
  device: LinkedDevice;
  onBack: () => void;
  onChanged: () => Promise<void> | void;
  onLogout: () => void;
  onOpenProvisioning: () => void;
};

// "HH:mm:ss" | null  ->  "HH:mm"
function toHHmm(v: string | null): string {
  return v ? v.substring(0, 5) : '';
}

export default function DeviceSettingsScreen({
  device,
  onBack,
  onChanged,
  onLogout,
  onOpenProvisioning,
}: Props) {
  useLang();
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
    setError(errText(err));
  }

  async function togglePause(value: boolean) {
    setPausedState(value);
    try {
      await setPaused(device.deviceId, value);
      flash(value ? t('toy_paused') : t('toy_resumed'));
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
      flash(t('modes_updated'));
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
      setError(t('e_time_format'));
      return;
    }
    try {
      await setBedtime(device.deviceId, s ? `${s}:00` : null, e ? `${e}:00` : null);
      flash(s && e ? tf('bedtime_set', { start: s, end: e }) : t('bedtime_off'));
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
      flash(t('bedtime_off'));
      await onChanged();
    } catch (err) {
      fail(err);
    }
  }

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 40 }}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_toys')}</Text>
      </Pressable>
      <Text style={styles.title}>{tf('settings_of', { name: device.deviceName || t('toy_word') })}</Text>

      {/* Grouped under the questions a parent arrives with, the same five the
          web dashboard uses. Pause and bedtime sit TOGETHER now - they answer
          one question and were separated by the mode switches. */}
      <Text style={styles.groupTitle}>{t('set_when')}</Text>
      <Text style={styles.groupNote}>{t('set_when_n')}</Text>
      {/* Pause */}
      <View style={styles.card}>
        <View style={styles.rowBetween}>
          <View style={{ flex: 1 }}>
            <Text style={styles.rowTitle}>{t('pause_title')}</Text>
            <Text style={styles.rowHint}>{t('pause_hint')}</Text>
          </View>
          <Switch value={paused} onValueChange={togglePause} />
        </View>
      </View>

      {/* Bedtime */}
      <View style={styles.card}>
        <Text style={styles.cardTitle}>{t('bedtime_title')}</Text>
        <Text style={styles.rowHint}>{t('bedtime_hint')}</Text>
        <View style={styles.timeRow}>
          <TextInput
            style={styles.timeInput}
            placeholder="21:30"
            value={bedStart}
            onChangeText={setBedStart}
            maxLength={5}
          />
          <Text style={styles.dash}>{t('bedtime_to')}</Text>
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
            <Text style={styles.primaryBtnText}>{t('save')}</Text>
          </Pressable>
          <Pressable style={styles.secondaryBtn} onPress={clearBedtime}>
            <Text style={styles.secondaryBtnText}>{t('turn_off')}</Text>
          </Pressable>
        </View>
      </View>


      <Text style={styles.groupTitle}>{t('set_what')}</Text>
      <Text style={styles.groupNote}>{t('set_what_n')}</Text>
      {/* Modes */}
      <View style={styles.card}>
        <Text style={styles.cardTitle}>{t('modes_title')}</Text>
        {(
          [
            ['story', 'mode_story'],
            ['game', 'mode_game'],
            ['riddle', 'mode_riddle'],
            ['curiosity', 'mode_curiosity'],
          ] as [keyof ModeFlags, 'mode_story' | 'mode_game' | 'mode_riddle' | 'mode_curiosity'][]
        ).map(([key, labelKey]) => (
          <View key={key} style={styles.rowBetween}>
            <Text style={styles.modeLabel}>{t(labelKey)}</Text>
            <Switch value={modes[key]} onValueChange={(v) => toggleMode(key, v)} />
          </View>
        ))}
        <Text style={styles.rowHint}>{t('modes_hint')}</Text>
      </View>


      <Text style={styles.groupTitle}>{t('set_toy')}</Text>
      <Pressable style={styles.wifiBtn} onPress={onOpenProvisioning}>
        <Text style={styles.wifiBtnText}>{t('wifi_btn')}</Text>
      </Pressable>

      {status ? <Text style={styles.status}>{status}</Text> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 16 },
  card: {
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 10,
    padding: 14,
    marginBottom: 14,
    backgroundColor: theme.surfaceSunken,
  },
  cardTitle: { fontSize: 16, fontWeight: '600', color: theme.brand, marginBottom: 8 },
  // A plain question in sentence case. An uppercase letter-spaced label
  // reads as a warning, which is the wrong tone for "when can it play?".
  groupTitle: { fontSize: 15, fontWeight: '700', color: theme.ink, marginTop: 18, marginBottom: 2 },
  groupNote: { fontSize: 12.5, lineHeight: 19, color: theme.inkMuted, marginBottom: 10 },
  wifiBtn: {
    backgroundColor: theme.surfaceTint,
    borderColor: theme.line,
    borderWidth: 1,
    borderRadius: 10,
    padding: 14,
    alignItems: 'center',
    marginBottom: 14,
  },
  wifiBtnText: { color: theme.brand, fontWeight: '700', fontSize: 15 },
  rowBetween: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingVertical: 6 },
  rowTitle: { fontSize: 16, color: theme.ink },
  rowHint: { fontSize: 12, color: theme.inkHint, marginTop: 4 },
  modeLabel: { fontSize: 15, color: theme.ink },
  timeRow: { flexDirection: 'row', alignItems: 'center', marginTop: 10 },
  timeInput: {
    borderWidth: 1,
    borderColor: theme.lineInput,
    borderRadius: 8,
    padding: 8,
    width: 90,
    textAlign: 'center',
    fontSize: 16,
  },
  dash: { marginHorizontal: 12, color: theme.inkMuted },
  btnRow: { flexDirection: 'row', marginTop: 12 },
  primaryBtn: { backgroundColor: theme.brand, borderRadius: 8, paddingVertical: 10, paddingHorizontal: 20, marginRight: 10 },
  primaryBtnText: { color: theme.surface, fontWeight: '600' },
  secondaryBtn: { borderWidth: 1, borderColor: theme.inkHint, borderRadius: 8, paddingVertical: 10, paddingHorizontal: 16 },
  secondaryBtnText: { color: theme.inkMuted, fontWeight: '600' },
  status: { color: theme.ok, marginTop: 8 },
  error: { color: theme.danger, marginTop: 8 },
});

import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Pressable,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import {
  claimDevice,
  errText,
  getDevices,
  LinkedDevice,
  renameDevice,
  setRevoked,
  UnauthorizedError,
} from '../api';
import { getLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';

type Props = {
  onLogout: () => void;
  onLoaded: (devices: LinkedDevice[]) => void;
  onOpenDevice: (device: LinkedDevice) => void;
  onOpenSettings: (device: LinkedDevice) => void;
  onOpenPlays: (device: LinkedDevice) => void;
  onOpenLibrary: (device: LinkedDevice) => void;
  onOpenAccount: () => void;
};

export default function DevicesScreen({
  onLogout,
  onLoaded,
  onOpenDevice,
  onOpenSettings,
  onOpenPlays,
  onOpenLibrary,
  onOpenAccount,
}: Props) {
  useLang();
  const [devices, setDevices] = useState<LinkedDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Pair-a-toy form
  const [showClaim, setShowClaim] = useState(false);
  const [claimId, setClaimId] = useState('');
  const [claimCode, setClaimCode] = useState('');
  const [claimBusy, setClaimBusy] = useState(false);
  const [claimMsg, setClaimMsg] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const list = await getDevices();
      setDevices(list);
      // Handed up so the activity feed can name toys on its first paint.
      onLoaded(list);
    } catch (err) {
      if (err instanceof UnauthorizedError) {
        onLogout();
        return;
      }
      setError(errText(err, 'e_load'));
    }
  }, [onLogout, onLoaded]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      await load();
      setLoading(false);
    })();
  }, [load]);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await load();
    setRefreshing(false);
  }, [load]);

  async function handleClaim() {
    setClaimMsg(null);
    if (!claimId.trim() || !claimCode.trim()) {
      setClaimMsg(t('e_pair_fields'));
      return;
    }
    setClaimBusy(true);
    try {
      await claimDevice(claimId.trim(), claimCode.trim());
      setClaimId('');
      setClaimCode('');
      setShowClaim(false);
      await load();
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      setClaimMsg(errText(err));
    } finally {
      setClaimBusy(false);
    }
  }

  function confirmRevoke(d: LinkedDevice) {
    const next = !d.isRevoked;
    if (!next) {
      void doRevoke(d.deviceId, false);
      return;
    }
    Alert.alert(
      t('confirm_revoke_title'),
      tf('confirm_revoke_body', { name: d.deviceName || t('this_toy') }),
      [
        { text: t('cancel'), style: 'cancel' },
        {
          text: t('revoke_access'),
          style: 'destructive',
          onPress: () => void doRevoke(d.deviceId, true),
        },
      ],
    );
  }

  async function doRevoke(deviceId: string, revoked: boolean) {
    try {
      await setRevoked(deviceId, revoked);
      await load();
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      setError(errText(err));
    }
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color="#2c4a7a" />
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <View style={styles.headerRow}>
        <Text style={styles.title}>{t('your_toys')}</Text>
        <Pressable onPress={onOpenAccount}>
          <Text style={styles.link}>{t('account')}</Text>
        </Pressable>
      </View>

      <Pressable style={styles.addBtn} onPress={() => setShowClaim((v) => !v)}>
        <Text style={styles.addBtnText}>{showClaim ? t('close_form') : t('add_toy')}</Text>
      </Pressable>

      {showClaim ? (
        <View style={styles.claimBox}>
          <Text style={styles.claimHelp}>{t('claim_help')}</Text>
          <TextInput
            style={styles.input}
            placeholder={t('ph_device_id')}
            autoCapitalize="none"
            autoCorrect={false}
            value={claimId}
            onChangeText={setClaimId}
            editable={!claimBusy}
          />
          <TextInput
            style={styles.input}
            placeholder={t('ph_pairing_code')}
            autoCapitalize="characters"
            autoCorrect={false}
            value={claimCode}
            onChangeText={setClaimCode}
            editable={!claimBusy}
          />
          <Pressable
            style={[styles.primaryBtn, claimBusy && styles.disabled]}
            onPress={handleClaim}
            disabled={claimBusy}
          >
            {claimBusy ? (
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={styles.primaryBtnText}>{t('pair_toy')}</Text>
            )}
          </Pressable>
          {claimMsg ? <Text style={styles.error}>{claimMsg}</Text> : null}
        </View>
      ) : null}

      {error ? <Text style={styles.error}>{error}</Text> : null}

      <FlatList
        data={devices}
        extraData={getLanguage()}
        keyExtractor={(d) => d.deviceId}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
        ListEmptyComponent={
          <Text style={styles.empty}>{t('no_toys')}</Text>
        }
        renderItem={({ item }) => (
          <DeviceCard
            device={item}
            onRevoke={confirmRevoke}
            onRenamed={load}
            onLogout={onLogout}
            onOpen={() => onOpenDevice(item)}
            onSettings={() => onOpenSettings(item)}
            onPlays={() => onOpenPlays(item)}
            onLibrary={() => onOpenLibrary(item)}
          />
        )}
        contentContainerStyle={devices.length === 0 ? styles.flexGrow : undefined}
      />
    </View>
  );
}

function DeviceCard({
  device,
  onRevoke,
  onRenamed,
  onLogout,
  onOpen,
  onSettings,
  onPlays,
  onLibrary,
}: {
  device: LinkedDevice;
  onRevoke: (d: LinkedDevice) => void;
  onRenamed: () => Promise<void> | void;
  onLogout: () => void;
  onOpen: () => void;
  onSettings: () => void;
  onPlays: () => void;
  onLibrary: () => void;
}) {
  const [name, setName] = useState(device.deviceName ?? '');
  const [saving, setSaving] = useState(false);

  async function save() {
    if (!name.trim()) return;
    setSaving(true);
    try {
      await renameDevice(device.deviceId, name.trim());
      await onRenamed();
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      Alert.alert(t('rename_failed'), errText(err));
    } finally {
      setSaving(false);
    }
  }

  const childLine = device.children
    .map((c) => (c.age != null ? tf('child_with_age', { name: c.name, n: c.age }) : c.name))
    .join(', ');

  return (
    <View style={styles.card}>
      {/* The card never showed the toy's own name — only an editable input
          further down — so two toys could only be told apart by their
          children's names. */}
      <Text style={styles.cardName}>
        {'🧸 ' + (device.deviceName || t('toy_word'))}
      </Text>
      <View style={styles.cardTop}>
        <View style={[styles.dot, device.isOnline ? styles.dotOn : styles.dotOff]} />
        <Text style={styles.dotLabel}>{device.isOnline ? t('online') : t('offline')}</Text>
        {device.isRevoked ? <Text style={styles.revokedTag}>{t('revoked')}</Text> : null}
        {device.isPaused ? <Text style={styles.pausedTag}>{t('paused')}</Text> : null}
      </View>

      {childLine ? <Text style={styles.children}>{childLine}</Text> : null}

      <View style={styles.actionRow}>
        <Pressable style={styles.activityBtn} onPress={onOpen}>
          <Text style={styles.activityText}>{t('see_activity')}</Text>
        </Pressable>
        <Pressable style={styles.settingsBtn} onPress={onSettings}>
          <Text style={styles.activityText}>{t('open_settings')}</Text>
        </Pressable>
      </View>
      {/* Stories play from the toy's own memory, so they are never
          conversations — they need their own way in. */}
      <View style={styles.actionRow}>
        <Pressable style={styles.activityBtn} onPress={onPlays}>
          <Text style={styles.activityText}>{t('tile_plays')} →</Text>
        </Pressable>
        <Pressable style={styles.settingsBtn} onPress={onLibrary}>
          <Text style={styles.activityText}>{t('tile_library')} →</Text>
        </Pressable>
      </View>

      <View style={styles.nameRow}>
        <TextInput
          style={styles.nameInput}
          value={name}
          onChangeText={setName}
          maxLength={60}
          placeholder={t('ph_toy_name')}
          editable={!saving}
        />
        <Pressable style={styles.saveBtn} onPress={save} disabled={saving}>
          <Text style={styles.saveBtnText}>{saving ? '…' : t('save')}</Text>
        </Pressable>
      </View>

      <Pressable style={styles.revokeBtn} onPress={() => onRevoke(device)}>
        <Text style={[styles.revokeText, { color: device.isRevoked ? '#2f6b2f' : '#a02622' }]}>
          {device.isRevoked ? t('restore_access') : t('revoke_access')}
        </Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
  flexGrow: { flexGrow: 1, justifyContent: 'center' },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { fontSize: 24, fontWeight: '700', color: '#2c4a7a' },
  link: { color: '#2c4a7a', fontSize: 15 },
  addBtn: { marginTop: 12, marginBottom: 4 },
  addBtnText: { color: '#2c4a7a', fontSize: 16, fontWeight: '600' },
  claimBox: {
    backgroundColor: '#f0f7f0',
    borderColor: '#b6d6b6',
    borderWidth: 1,
    borderRadius: 8,
    padding: 12,
    marginVertical: 8,
  },
  claimHelp: { color: '#3a5a3a', marginBottom: 8 },
  input: { borderWidth: 1, borderColor: '#ccc', borderRadius: 8, padding: 10, marginBottom: 8 },
  primaryBtn: { backgroundColor: '#2c4a7a', borderRadius: 8, padding: 12, alignItems: 'center' },
  primaryBtnText: { color: '#fff', fontWeight: '600' },
  disabled: { opacity: 0.6 },
  empty: { textAlign: 'center', color: '#888', fontSize: 16 },
  card: {
    borderWidth: 1,
    borderColor: '#e2e2e2',
    borderRadius: 10,
    padding: 14,
    marginVertical: 6,
    backgroundColor: '#fafafa',
  },
  cardName: { fontSize: 16, fontWeight: '700', color: '#222', marginBottom: 6 },
  cardTop: { flexDirection: 'row', alignItems: 'center' },
  dot: { width: 10, height: 10, borderRadius: 5, marginRight: 6 },
  dotOn: { backgroundColor: '#5aa45a' },
  dotOff: { backgroundColor: '#bbb' },
  dotLabel: { color: '#555', fontSize: 13 },
  revokedTag: {
    marginLeft: 8,
    color: '#a02622',
    backgroundColor: '#fdecea',
    borderRadius: 4,
    paddingHorizontal: 6,
    fontSize: 12,
    overflow: 'hidden',
  },
  pausedTag: {
    marginLeft: 8,
    color: '#856404',
    backgroundColor: '#fff3cd',
    borderRadius: 4,
    paddingHorizontal: 6,
    fontSize: 12,
    overflow: 'hidden',
  },
  children: { color: '#444', marginTop: 6 },
  actionRow: { flexDirection: 'row', marginTop: 10, gap: 8 },
  activityBtn: {
    flex: 1,
    backgroundColor: '#eef3fb',
    borderRadius: 8,
    paddingVertical: 10,
    alignItems: 'center',
  },
  settingsBtn: {
    flex: 1,
    backgroundColor: '#f0f0f0',
    borderRadius: 8,
    paddingVertical: 10,
    alignItems: 'center',
  },
  activityText: { color: '#2c4a7a', fontWeight: '600' },
  nameRow: { flexDirection: 'row', alignItems: 'center', marginTop: 10 },
  nameInput: {
    flex: 1,
    borderWidth: 1,
    borderColor: '#ccc',
    borderRadius: 8,
    padding: 8,
    marginRight: 8,
  },
  saveBtn: {
    borderWidth: 1,
    borderColor: '#6a8ec9',
    borderRadius: 6,
    paddingVertical: 8,
    paddingHorizontal: 14,
  },
  saveBtnText: { color: '#2c4a7a', fontWeight: '600' },
  revokeBtn: { marginTop: 10, alignSelf: 'flex-start' },
  revokeText: { fontWeight: '600' },
  error: { color: '#a02622', marginVertical: 8 },
});

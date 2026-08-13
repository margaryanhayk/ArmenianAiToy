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
  addChild,
  claimDevice,
  createInvite,
  redeemInvite,
  errText,
  getDevices,
  LinkedDevice,
  renameDevice,
  setRevoked,
  UnauthorizedError,
} from '../api';
import { getLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

type Props = {
  onLogout: () => void;
  onLoaded: (devices: LinkedDevice[]) => void;
  onOpenDevice: (device: LinkedDevice) => void;
  onOpenSettings: (device: LinkedDevice) => void;
  onOpenPlays: (device: LinkedDevice) => void;
  onOpenLibrary: (device: LinkedDevice) => void;
  onOpenAccount: () => void;
  onOpenCapabilities: () => void;
};

export default function DevicesScreen({
  onLogout,
  onLoaded,
  onOpenDevice,
  onOpenSettings,
  onOpenPlays,
  onOpenLibrary,
  onOpenAccount,
  onOpenCapabilities,
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

  // Joining a toy someone else already set up.
  const [inviteCode, setInviteCode] = useState('');
  const [inviteBusy, setInviteBusy] = useState(false);
  const [inviteMsg, setInviteMsg] = useState<string | null>(null);

  async function handleRedeem() {
    const code = inviteCode.trim();
    if (!code) { setInviteMsg(t('invite_bad')); return; }
    setInviteBusy(true);
    setInviteMsg(null);
    try {
      await redeemInvite(code);
      setInviteCode('');
      setInviteMsg(t('invite_joined'));
      setShowClaim(false);
      await load();
    } catch (e) {
      if (e instanceof UnauthorizedError) { onLogout(); return; }
      setInviteMsg(errText(e));
    } finally {
      setInviteBusy(false);
    }
  }

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
        <ActivityIndicator size="large" color={theme.brand} />
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

      {/* What the toy actually does. On the list rather than inside a toy: it
          describes the product, not one device, and a parent with no toy
          paired yet still has a reason to read it. */}
      <Pressable style={styles.capsRow} onPress={onOpenCapabilities}>
        <View style={{ flex: 1 }}>
          <Text style={styles.capsTitle}>{t('caps_link')}</Text>
          <Text style={styles.capsSub}>{t('caps_link_sub')}</Text>
        </View>
        <Text style={styles.capsChev}>›</Text>
      </Pressable>

      {showClaim ? (
        <View style={styles.claimBox}>
          {/* The invite path FIRST: someone joining a toy their partner
              already set up needs one short code and nothing else, and that
              is now the commoner of the two. The box path stays below, for
              the parent unpacking a new toy. */}
          <Text style={styles.claimHelp}>{t('invite_have')}</Text>
          <TextInput
            style={styles.input}
            placeholder={t('invite_ph')}
            autoCapitalize="characters"
            autoCorrect={false}
            value={inviteCode}
            onChangeText={setInviteCode}
            editable={!inviteBusy}
            maxLength={20}
          />
          <Pressable
            style={[styles.primaryBtn, inviteBusy && styles.disabled]}
            onPress={handleRedeem}
            disabled={inviteBusy}
          >
            {inviteBusy ? (
              <ActivityIndicator color={theme.onBrand} />
            ) : (
              <Text style={styles.primaryBtnText}>{t('invite_join')}</Text>
            )}
          </Pressable>
          {inviteMsg ? <Text style={styles.claimHelp}>{inviteMsg}</Text> : null}

          <View style={styles.claimDivider} />
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
              <ActivityIndicator color={theme.onBrand} />
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

/**
 * The add-a-child form, shown on a toy that has no child profile yet.
 *
 * Open, not collapsed: it only appears when there is no profile at all, so
 * the form IS the point and hiding it behind a tap adds a step for nothing.
 */
function AddChildBlock({
  deviceId,
  onAdded,
}: {
  deviceId: string;
  onAdded: () => Promise<void> | void;
}) {
  const [name, setName] = useState('');
  const [gender, setGender] = useState<0 | 1>(0);
  const [year, setYear] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  async function save() {
    const trimmed = name.trim();
    if (!trimmed) { setMsg(t('child_name_required')); return; }
    setBusy(true);
    setMsg(null);
    try {
      const y = parseInt(year, 10);
      await addChild(deviceId, trimmed, gender, isNaN(y) ? null : y);
      await onAdded();
    } catch (e) {
      setMsg(errText(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={styles.childBox}>
      <Text style={styles.childTitle}>{t('add_child')}</Text>
      <Text style={styles.childWhy}>{t('child_why')}</Text>
      <TextInput
        style={styles.input}
        placeholder={t('child_name_ph')}
        value={name}
        onChangeText={setName}
        maxLength={60}
      />
      <Text style={styles.childLabel}>{t('gender_label')}</Text>
      {/* Two buttons rather than a dropdown: both options stay visible, each
          is a full-size tap target, and it matches the rest of the form
          instead of a grey system widget. */}
      <View style={styles.segRow}>
        {([[0, 'gender_boy'], [1, 'gender_girl']] as [0 | 1, 'gender_boy' | 'gender_girl'][]).map(
          ([v, k]) => (
            <Pressable
              key={v}
              style={[styles.seg, gender === v && styles.segOn]}
              onPress={() => setGender(v)}
              accessibilityRole="radio"
              // Same pairing as the diary pills: accessibilityState for the
              // native builds, aria-checked for the web one, which does not
              // derive it.
              accessibilityState={{ checked: gender === v }}
              aria-checked={gender === v}
            >
              <Text style={[styles.segText, gender === v && styles.segTextOn]}>{t(k)}</Text>
            </Pressable>
          ),
        )}
      </View>
      <TextInput
        style={styles.input}
        placeholder={t('birth_year')}
        value={year}
        onChangeText={setYear}
        keyboardType="number-pad"
        maxLength={4}
      />
      <Pressable style={[styles.primaryBtn, busy && styles.disabled]} onPress={save} disabled={busy}>
        {busy ? (
          <ActivityIndicator color={theme.onBrand} />
        ) : (
          <Text style={styles.primaryBtnText}>{t('save')}</Text>
        )}
      </Pressable>
      {msg ? <Text style={styles.error}>{msg}</Text> : null}
    </View>
  );
}

/**
 * "Let someone else see this toy" — issues a short code the other parent
 * types, instead of them needing this toy's 36-character id plus the code
 * printed on its box.
 *
 * It does NOT touch the printed claim code. Re-minting that would be the easy
 * way to make something shareable and would kill the QR on the toy for good.
 */
function InviteBlock({ deviceId }: { deviceId: string }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [code, setCode] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  async function make() {
    setBusy(true);
    setMsg(null);
    try {
      const r = await createInvite(deviceId);
      // Grouped in fours: a code is read aloud down a phone at least as often
      // as it is copied, and unbroken twelve characters is where that fails.
      setCode(r.code.replace(/(.{4})(.{4})(.{4})/, '$1-$2-$3'));
    } catch (e) {
      setMsg(errText(e));
    } finally {
      setBusy(false);
    }
  }

  if (!open) {
    return (
      <Pressable onPress={() => setOpen(true)}>
        <Text style={styles.inviteLink}>{t('share_title')}</Text>
      </Pressable>
    );
  }

  return (
    <View style={styles.inviteBox}>
      <Text style={styles.childWhy}>{t('invite_intro')}</Text>
      {code ? (
        <>
          <Text style={styles.inviteCode} selectable>
            {code}
          </Text>
          <Text style={styles.childWhy}>{t('invite_once')}</Text>
        </>
      ) : null}
      <Pressable style={[styles.secondaryBtn, busy && styles.disabled]} onPress={make} disabled={busy}>
        {busy ? (
          <ActivityIndicator color={theme.brand} />
        ) : (
          <Text style={styles.secondaryBtnText}>{code ? t('invite_again') : t('invite_make')}</Text>
        )}
      </Pressable>
      {msg ? <Text style={styles.error}>{msg}</Text> : null}
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

  const libraryLine = (() => {
    const health = device.contentHealth;
    if (!health || health === 'offline') return null;
    const have = device.storiesOnToy ?? 0;
    const total = device.storiesAvailable ?? 0;
    if (health === 'up_to_date' && total > 0) return tf('library_ready', { n: total });
    if (health === 'syncing') return tf('library_updating', { have, total });
    if (health === 'stale') return t('library_none');
    if (health === 'unknown') return t('library_unknown');
    return null;
  })();

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

      {/* Did the new stories actually arrive? The question a parent asks the
          day after an update, which used to have no answer anywhere — the
          only confirmation was listening to a story. Offline renders nothing:
          the dot above already says so, and repeating it as a library problem
          would send the parent chasing the wrong thing. */}
      {libraryLine ? <Text style={styles.libraryLine}>{libraryLine}</Text> : null}

      {childLine ? (
        <Text style={styles.children}>{childLine}</Text>
      ) : (
        /* No child profile means the toy has no name, no age and no GENDER
           for the prompt - and Armenian grammar needs the gender, so it is
           literally talking to a stranger. The phone had no way to fix this
           at all; the web page at least had a form buried in Settings. */
        <AddChildBlock deviceId={device.deviceId} onAdded={onRenamed} />
      )}

      <InviteBlock deviceId={device.deviceId} />

      {/* The one thing a parent opens a toy to do. The rest are real, but
          they are not why anyone comes here. */}
      <Pressable style={styles.mainBtn} onPress={onOpen}>
        <View style={{ flex: 1 }}>
          <Text style={styles.mainBtnTitle}>💬 {t('see_activity')}</Text>
          <Text style={styles.mainBtnSub}>{t('see_activity_sub')}</Text>
        </View>
        <Text style={styles.mainBtnChev}>›</Text>
      </Pressable>
      <View style={styles.actionRow}>
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
        <Text style={[styles.revokeText, { color: device.isRevoked ? theme.ok : theme.danger }]}>
          {device.isRevoked ? t('restore_access') : t('revoke_access')}
        </Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.surface },
  flexGrow: { flexGrow: 1, justifyContent: 'center' },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { fontSize: 24, fontWeight: '700', color: theme.brand },
  link: { color: theme.brand, fontSize: 15 },
  addBtn: { marginTop: 12, marginBottom: 4 },
  addBtnText: { color: theme.brand, fontSize: 16, fontWeight: '600' },
  capsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.surfaceTint,
    borderColor: theme.line,
    borderWidth: 1,
    borderRadius: 12,
    padding: 13,
    marginBottom: 12,
  },
  capsTitle: { color: theme.brand, fontSize: 15, fontWeight: '700' },
  capsSub: { color: theme.inkMuted, fontSize: 12.5, marginTop: 2 },
  capsChev: { color: theme.inkHint, fontSize: 20, marginLeft: 8 },
  claimBox: {
    backgroundColor: theme.okBg,
    borderColor: theme.okLine,
    borderWidth: 1,
    borderRadius: 8,
    padding: 12,
    marginVertical: 8,
  },
  claimHelp: { color: theme.ok, marginBottom: 8 },
  claimDivider: { height: 1, backgroundColor: theme.okLine, marginVertical: 12 },
  input: { borderWidth: 1, borderColor: theme.lineInput, borderRadius: 8, padding: 10, marginBottom: 8 },
  primaryBtn: { backgroundColor: theme.brand, borderRadius: 8, padding: 12, alignItems: 'center' },
  primaryBtnText: { color: theme.surface, fontWeight: '600' },
  disabled: { opacity: 0.6 },
  empty: { textAlign: 'center', color: theme.inkHint, fontSize: 16 },
  card: {
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 10,
    padding: 14,
    marginVertical: 6,
    backgroundColor: theme.surfaceSunken,
  },
  cardName: { fontSize: 16, fontWeight: '700', color: theme.ink, marginBottom: 6 },
  cardTop: { flexDirection: 'row', alignItems: 'center' },
  dot: { width: 10, height: 10, borderRadius: 5, marginRight: 6 },
  dotOn: { backgroundColor: theme.okLine },
  dotOff: { backgroundColor: theme.lineInput },
  dotLabel: { color: theme.inkMuted, fontSize: 13 },
  revokedTag: {
    marginLeft: 8,
    color: theme.danger,
    backgroundColor: theme.dangerBg,
    borderRadius: 4,
    paddingHorizontal: 6,
    fontSize: 12,
    overflow: 'hidden',
  },
  pausedTag: {
    marginLeft: 8,
    color: theme.warn,
    backgroundColor: theme.warnBg,
    borderRadius: 4,
    paddingHorizontal: 6,
    fontSize: 12,
    overflow: 'hidden',
  },
  children: { color: theme.inkMuted, marginTop: 6 },
  libraryLine: { color: theme.inkMuted, marginTop: 6, fontSize: 13, lineHeight: 19 },
  childBox: {
    marginTop: 10,
    padding: 12,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: theme.line,
    backgroundColor: theme.surfaceTint,
  },
  childTitle: { fontSize: 15, fontWeight: '700', color: theme.ink, marginBottom: 4 },
  childWhy: { fontSize: 12.5, lineHeight: 19, color: theme.inkMuted, marginBottom: 10 },
  childLabel: { fontSize: 12, color: theme.inkMuted, marginBottom: 4 },
  segRow: { flexDirection: 'row', gap: 8, marginBottom: 8 },
  seg: {
    flex: 1,
    paddingVertical: 10,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: theme.lineInput,
    alignItems: 'center',
    backgroundColor: theme.surface,
  },
  segOn: { borderColor: theme.brand, backgroundColor: theme.brandTint },
  segText: { color: theme.inkMuted, fontWeight: '600' },
  segTextOn: { color: theme.brand },
  inviteLink: { color: theme.brand, fontSize: 14, fontWeight: '600', marginTop: 10 },
  // This screen had no quiet button style of its own — only the filled
  // primary one, which would have made "make a code" shout as loudly as
  // pairing a toy.
  secondaryBtn: {
    borderWidth: 1,
    borderColor: theme.brand,
    borderRadius: 8,
    padding: 11,
    alignItems: 'center',
  },
  secondaryBtnText: { color: theme.brand, fontWeight: '600' },
  inviteBox: {
    marginTop: 10,
    padding: 12,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: theme.line,
    backgroundColor: theme.surfaceTint,
  },
  inviteCode: {
    fontSize: 20,
    fontWeight: '700',
    letterSpacing: 2,
    textAlign: 'center',
    color: theme.ink,
    backgroundColor: theme.surface,
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 8,
    paddingVertical: 10,
    marginBottom: 8,
  },
  actionRow: { flexDirection: 'row', marginTop: 10, gap: 8 },
  mainBtn: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: theme.surfaceTint, borderColor: theme.line, borderWidth: 2,
    borderRadius: 10, paddingVertical: 14, paddingHorizontal: 14, marginTop: 12,
  },
  mainBtnTitle: { color: theme.brand, fontWeight: '700', fontSize: 17 },
  mainBtnSub: { color: theme.inkMuted, fontSize: 12, marginTop: 2 },
  mainBtnChev: { color: theme.brandStrong, fontSize: 22 },
  activityBtn: {
    flex: 1,
    backgroundColor: theme.surfaceTint,
    borderRadius: 8,
    paddingVertical: 10,
    alignItems: 'center',
  },
  settingsBtn: {
    flex: 1,
    backgroundColor: theme.surfaceSunken,
    borderRadius: 8,
    paddingVertical: 10,
    alignItems: 'center',
  },
  activityText: { color: theme.brand, fontWeight: '600' },
  nameRow: { flexDirection: 'row', alignItems: 'center', marginTop: 10 },
  nameInput: {
    flex: 1,
    borderWidth: 1,
    borderColor: theme.lineInput,
    borderRadius: 8,
    padding: 8,
    marginRight: 8,
  },
  saveBtn: {
    borderWidth: 1,
    borderColor: theme.brandStrong,
    borderRadius: 6,
    paddingVertical: 8,
    paddingHorizontal: 14,
  },
  saveBtnText: { color: theme.brand, fontWeight: '600' },
  revokeBtn: { marginTop: 10, alignSelf: 'flex-start' },
  revokeText: { fontWeight: '600' },
  error: { color: theme.danger, marginVertical: 8 },
});

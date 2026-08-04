import { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import {
  changePassword,
  deleteAccount,
  errText,
  fetchExport,
  getMe,
  Me,
  requestVerification,
  UnauthorizedError,
} from '../api';
import { LANG_NAMES, LANGS, getLanguage, setLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';
import PasswordInput from '../PasswordInput';

type Props = {
  onBack: () => void;
  onLogout: () => void;
  onOpenMusic: () => void;
  onOpenRequest: () => void;
  onOpenActivity: () => void;
};

export default function AccountScreen({
  onBack,
  onLogout,
  onOpenMusic,
  onOpenRequest,
  onOpenActivity,
}: Props) {
  useLang();
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [curPw, setCurPw] = useState('');
  const [newPw, setNewPw] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setMe(await getMe());
      } catch (err) {
        if (err instanceof UnauthorizedError) return onLogout();
        setError(errText(err, 'e_load'));
      } finally {
        setLoading(false);
      }
    })();
  }, [onLogout]);

  function ok(msg: string) {
    setError(null);
    setStatus(msg);
  }
  function fail(err: unknown) {
    if (err instanceof UnauthorizedError) return onLogout();
    setStatus(null);
    setError(errText(err));
  }

  async function sendVerification() {
    if (!me) return;
    try {
      await requestVerification(me.email);
      ok(t('verification_sent'));
    } catch (err) {
      fail(err);
    }
  }

  async function doExport() {
    setBusy(true);
    setStatus(null);
    setError(null);
    try {
      const text = await fetchExport();
      if (Platform.OS === 'web') {
        const g = globalThis as unknown as {
          Blob: typeof Blob;
          URL: typeof URL;
          document: Document;
        };
        const blob = new g.Blob([text], { type: 'application/json' });
        const href = g.URL.createObjectURL(blob);
        const a = g.document.createElement('a');
        a.href = href;
        a.download = 'areg-export.json';
        a.click();
        g.URL.revokeObjectURL(href);
        ok(t('export_done_web'));
      } else {
        ok(tf('export_ready_native', { n: text.length }));
      }
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  async function doChangePassword() {
    if (!curPw || !newPw) {
      setError(t('e_both_passwords'));
      return;
    }
    setBusy(true);
    try {
      await changePassword(curPw, newPw);
      setCurPw('');
      setNewPw('');
      ok(t('password_changed'));
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  function confirmDelete() {
    if (!curPw) {
      setError(t('e_password_first'));
      return;
    }
    Alert.alert(
      t('confirm_delete_title'),
      t('confirm_delete_body'),
      [
        { text: t('cancel'), style: 'cancel' },
        {
          text: t('delete_word'),
          style: 'destructive',
          onPress: async () => {
            try {
              await deleteAccount(curPw);
              onLogout();
            } catch (err) {
              fail(err);
            }
          },
        },
      ],
    );
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color="#2c4a7a" />
      </View>
    );
  }

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 48 }}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_toys')}</Text>
      </Pressable>
      <Text style={styles.title}>{t('account_title')}</Text>

      <View style={styles.card}>
        <Text style={styles.label}>{t('email_label')}</Text>
        <Text style={styles.value}>{me?.email}</Text>
        <Text style={[styles.verify, me?.emailVerifiedAt ? styles.verified : styles.unverified]}>
          {me?.emailVerifiedAt ? t('email_verified') : t('email_unverified')}
        </Text>
        {!me?.emailVerifiedAt ? (
          <Pressable style={styles.secondaryBtn} onPress={sendVerification}>
            <Text style={styles.secondaryBtnText}>{t('send_verification')}</Text>
          </Pressable>
        ) : null}
      </View>

      {/* Account-wide, not per toy: music is the same for every toy, and
          requests and the activity feed belong to the parent. */}
      <Pressable style={styles.tile} onPress={onOpenActivity}>
        <Text style={styles.tileIcon}>🕘</Text>
        <View style={{ flex: 1 }}>
          <Text style={styles.tileTitle}>{t('tile_activity')}</Text>
          <Text style={styles.tileSub}>{t('tile_activity_sub')}</Text>
        </View>
        <Text style={styles.tileChev}>›</Text>
      </Pressable>
      <Pressable style={styles.tile} onPress={onOpenMusic}>
        <Text style={styles.tileIcon}>🎵</Text>
        <View style={{ flex: 1 }}>
          <Text style={styles.tileTitle}>{t('tile_music')}</Text>
          <Text style={styles.tileSub}>{t('tile_music_sub')}</Text>
        </View>
        <Text style={styles.tileChev}>›</Text>
      </Pressable>
      <Pressable style={styles.tile} onPress={onOpenRequest}>
        <Text style={styles.tileIcon}>✍️</Text>
        <View style={{ flex: 1 }}>
          <Text style={styles.tileTitle}>{t('tile_request')}</Text>
          <Text style={styles.tileSub}>{t('tile_request_sub')}</Text>
        </View>
        <Text style={styles.tileChev}>›</Text>
      </Pressable>

      <View style={styles.card}>
        <Text style={styles.cardTitle}>{t('language')}</Text>
        <View style={styles.langRow}>
          {LANGS.map((l) => (
            <Pressable
              key={l}
              style={[styles.langPill, getLanguage() === l && styles.langPillOn]}
              onPress={() => setLanguage(l)}
            >
              <Text style={[styles.langText, getLanguage() === l && styles.langTextOn]}>
                {LANG_NAMES[l]}
              </Text>
            </Pressable>
          ))}
        </View>
      </View>

      <View style={styles.card}>
        <Text style={styles.cardTitle}>{t('your_data')}</Text>
        <Text style={styles.hint}>{t('export_hint')}</Text>
        <Pressable style={styles.secondaryBtn} onPress={doExport} disabled={busy}>
          <Text style={styles.secondaryBtnText}>{t('export_btn')}</Text>
        </Pressable>
      </View>

      <View style={styles.card}>
        <Text style={styles.cardTitle}>{t('change_password_title')}</Text>
        <PasswordInput
          placeholder={t('ph_current_pw')}
          value={curPw}
          onChangeText={setCurPw}
          editable={!busy}
          autoComplete="current-password"
        />
        <PasswordInput
          placeholder={t('ph_new_pw')}
          value={newPw}
          onChangeText={setNewPw}
          editable={!busy}
          autoComplete="new-password"
        />
        <Pressable style={styles.primaryBtn} onPress={doChangePassword} disabled={busy}>
          <Text style={styles.primaryBtnText}>{t('update_password')}</Text>
        </Pressable>
      </View>

      <Pressable style={styles.logoutBtn} onPress={onLogout}>
        <Text style={styles.logoutText}>{t('log_out')}</Text>
      </Pressable>

      <View style={styles.dangerCard}>
        <Text style={styles.dangerTitle}>{t('delete_title')}</Text>
        <Text style={styles.hint}>{t('delete_hint')}</Text>
        <Pressable style={styles.dangerBtn} onPress={confirmDelete}>
          <Text style={styles.dangerBtnText}>{t('delete_btn')}</Text>
        </Pressable>
      </View>

      {status ? <Text style={styles.status}>{status}</Text> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 24, fontWeight: '700', color: '#2c4a7a', marginBottom: 16 },
  card: {
    borderWidth: 1,
    borderColor: '#e2e2e2',
    borderRadius: 10,
    padding: 14,
    marginBottom: 14,
    backgroundColor: '#fafafa',
  },
  cardTitle: { fontSize: 16, fontWeight: '600', color: '#2c4a7a', marginBottom: 6 },
  label: { fontSize: 12, color: '#888' },
  value: { fontSize: 16, color: '#222', marginTop: 2 },
  verify: { marginTop: 8, fontSize: 13 },
  verified: { color: '#2f6b2f' },
  unverified: { color: '#a07d00' },
  hint: { fontSize: 12, color: '#888', marginBottom: 8 },
  input: { borderWidth: 1, borderColor: '#ccc', borderRadius: 8, padding: 10, marginBottom: 8 },
  primaryBtn: { backgroundColor: '#2c4a7a', borderRadius: 8, padding: 12, alignItems: 'center' },
  primaryBtnText: { color: '#fff', fontWeight: '600' },
  secondaryBtn: {
    borderWidth: 1,
    borderColor: '#6a8ec9',
    borderRadius: 8,
    padding: 10,
    alignItems: 'center',
    marginTop: 4,
  },
  secondaryBtnText: { color: '#2c4a7a', fontWeight: '600' },
  logoutBtn: { padding: 12, alignItems: 'center', marginBottom: 14 },
  logoutText: { color: '#2c4a7a', fontWeight: '600', fontSize: 16 },
  dangerCard: {
    borderWidth: 1,
    borderColor: '#f0c9c7',
    backgroundColor: '#fdf6f5',
    borderRadius: 10,
    padding: 14,
  },
  dangerTitle: { fontSize: 16, fontWeight: '600', color: '#a02622', marginBottom: 6 },
  dangerBtn: {
    borderWidth: 1,
    borderColor: '#d9534f',
    borderRadius: 8,
    padding: 12,
    alignItems: 'center',
  },
  dangerBtnText: { color: '#a02622', fontWeight: '700' },
  tile: {
    flexDirection: 'row', alignItems: 'center', gap: 12,
    borderWidth: 1, borderColor: '#e2e2e2', borderRadius: 10,
    padding: 14, marginBottom: 10, backgroundColor: '#fafafa',
    minHeight: 60,
  },
  tileIcon: { fontSize: 22 },
  tileTitle: { fontSize: 15, fontWeight: '600', color: '#222' },
  tileSub: { fontSize: 12, color: '#666', marginTop: 2 },
  tileChev: { fontSize: 20, color: '#aaa' },
  langRow: { flexDirection: 'row', gap: 8, flexWrap: 'wrap' },
  langPill: {
    borderWidth: 1,
    borderColor: '#d5d5d5',
    borderRadius: 999,
    paddingVertical: 8,
    paddingHorizontal: 14,
    minHeight: 44,
    justifyContent: 'center',
  },
  langPillOn: { borderColor: '#2c4a7a', backgroundColor: '#eef3fb' },
  langText: { color: '#666', fontSize: 13 },
  langTextOn: { color: '#2c4a7a', fontWeight: '700' },
  status: { color: '#2f6b2f', marginTop: 12 },
  error: { color: '#a02622', marginTop: 12 },
});

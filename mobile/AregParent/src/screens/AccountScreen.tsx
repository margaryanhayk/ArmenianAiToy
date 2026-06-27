import { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import {
  changePassword,
  deleteAccount,
  fetchExport,
  getMe,
  Me,
  requestVerification,
  UnauthorizedError,
} from '../api';

type Props = {
  onBack: () => void;
  onLogout: () => void;
};

export default function AccountScreen({ onBack, onLogout }: Props) {
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
        setError(err instanceof Error ? err.message : 'Failed to load.');
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
    setError(err instanceof Error ? err.message : 'Failed.');
  }

  async function sendVerification() {
    if (!me) return;
    try {
      await requestVerification(me.email);
      ok('If your email needs verifying, a link has been sent. Check your inbox.');
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
        ok('Your data downloaded as areg-export.json.');
      } else {
        ok(`Export ready (${text.length} characters). Download is available in the web app.`);
      }
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  async function doChangePassword() {
    if (!curPw || !newPw) {
      setError('Enter your current and new password.');
      return;
    }
    setBusy(true);
    try {
      await changePassword(curPw, newPw);
      setCurPw('');
      setNewPw('');
      ok('Password changed.');
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  function confirmDelete() {
    if (!curPw) {
      setError('Type your current password above first, then tap Delete.');
      return;
    }
    Alert.alert(
      'Delete your account?',
      'This permanently deletes your account and any toys only you own, with their conversations. This cannot be undone.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Delete',
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
        <Text style={styles.back}>‹ Toys</Text>
      </Pressable>
      <Text style={styles.title}>Account</Text>

      <View style={styles.card}>
        <Text style={styles.label}>Email</Text>
        <Text style={styles.value}>{me?.email}</Text>
        <Text style={[styles.verify, me?.emailVerifiedAt ? styles.verified : styles.unverified]}>
          {me?.emailVerifiedAt ? '✓ Email verified' : 'Email not verified'}
        </Text>
        {!me?.emailVerifiedAt ? (
          <Pressable style={styles.secondaryBtn} onPress={sendVerification}>
            <Text style={styles.secondaryBtnText}>Send verification email</Text>
          </Pressable>
        ) : null}
      </View>

      <View style={styles.card}>
        <Text style={styles.cardTitle}>Your data</Text>
        <Text style={styles.hint}>Download everything we hold for your account as a file.</Text>
        <Pressable style={styles.secondaryBtn} onPress={doExport} disabled={busy}>
          <Text style={styles.secondaryBtnText}>Export my data</Text>
        </Pressable>
      </View>

      <View style={styles.card}>
        <Text style={styles.cardTitle}>Change password</Text>
        <TextInput
          style={styles.input}
          placeholder="Current password"
          secureTextEntry
          value={curPw}
          onChangeText={setCurPw}
          editable={!busy}
        />
        <TextInput
          style={styles.input}
          placeholder="New password (min 8 characters)"
          secureTextEntry
          value={newPw}
          onChangeText={setNewPw}
          editable={!busy}
        />
        <Pressable style={styles.primaryBtn} onPress={doChangePassword} disabled={busy}>
          <Text style={styles.primaryBtnText}>Update password</Text>
        </Pressable>
      </View>

      <Pressable style={styles.logoutBtn} onPress={onLogout}>
        <Text style={styles.logoutText}>Log out</Text>
      </Pressable>

      <View style={styles.dangerCard}>
        <Text style={styles.dangerTitle}>Delete account</Text>
        <Text style={styles.hint}>
          Permanent. Type your current password above, then confirm.
        </Text>
        <Pressable style={styles.dangerBtn} onPress={confirmDelete}>
          <Text style={styles.dangerBtnText}>Delete my account</Text>
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
  status: { color: '#2f6b2f', marginTop: 12 },
  error: { color: '#a02622', marginTop: 12 },
});

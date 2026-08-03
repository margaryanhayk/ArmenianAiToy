import { useState } from 'react';
import {
  ActivityIndicator,
  Image,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { errText, login, register } from '../api';
import { LANG_NAMES, LANGS, getLanguage, setLanguage, t } from '../i18n';
import { useLang } from '../useLang';

type Props = {
  onLoggedIn: (token: string) => void;
};

export default function LoginScreen({ onLoggedIn }: Props) {
  useLang();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setError(null);
    setStatus(null);
    const e = email.trim();
    if (!e || !password) {
      setError(t('e_email_password'));
      return;
    }
    setBusy(true);
    try {
      if (mode === 'register') {
        await register(e, password);
        setStatus(t('account_created'));
        setMode('login');
      } else {
        const token = await login(e, password);
        onLoggedIn(token);
      }
    } catch (err) {
      setError(errText(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <KeyboardAvoidingView
      style={styles.container}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <Image
        source={require('../../assets/splash-icon.png')}
        style={styles.logo}
        resizeMode="contain"
      />
      <Text style={styles.brand}>Areg</Text>
      <Text style={styles.subtitle}>
        {mode === 'login' ? t('login_subtitle') : t('register_subtitle')}
      </Text>

      <TextInput
        style={styles.input}
        placeholder={t('ph_email')}
        autoCapitalize="none"
        autoCorrect={false}
        keyboardType="email-address"
        value={email}
        onChangeText={setEmail}
        editable={!busy}
      />
      <TextInput
        style={styles.input}
        placeholder={t('ph_password')}
        secureTextEntry
        value={password}
        onChangeText={setPassword}
        editable={!busy}
      />

      <Pressable
        style={[styles.button, busy && styles.buttonDisabled]}
        onPress={submit}
        disabled={busy}
      >
        {busy ? (
          <ActivityIndicator color="#fff" />
        ) : (
          <Text style={styles.buttonText}>
            {mode === 'login' ? t('sign_in') : t('create_account')}
          </Text>
        )}
      </Pressable>

      <Pressable
        onPress={() => {
          setMode(mode === 'login' ? 'register' : 'login');
          setError(null);
          setStatus(null);
        }}
        disabled={busy}
      >
        <Text style={styles.link}>
          {mode === 'login' ? t('to_register') : t('to_login')}
        </Text>
      </Pressable>

      {status ? <Text style={styles.status}>{status}</Text> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {/* The language picker lives on the sign-in screen because a parent
          who does not read English has to be able to change it BEFORE they
          can reach any settings. */}
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
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 24, justifyContent: 'center', backgroundColor: '#fff' },
  logo: { width: 88, height: 88, alignSelf: 'center', marginBottom: 4 },
  brand: { fontSize: 40, fontWeight: '700', textAlign: 'center', color: '#2c4a7a' },
  subtitle: { fontSize: 16, textAlign: 'center', marginBottom: 24, color: '#555' },
  input: {
    borderWidth: 1,
    borderColor: '#ccc',
    borderRadius: 8,
    padding: 12,
    fontSize: 16,
    marginBottom: 12,
  },
  button: {
    backgroundColor: '#2c4a7a',
    borderRadius: 8,
    padding: 14,
    alignItems: 'center',
    marginTop: 4,
  },
  buttonDisabled: { opacity: 0.6 },
  buttonText: { color: '#fff', fontSize: 16, fontWeight: '600' },
  link: { color: '#2c4a7a', textAlign: 'center', marginTop: 16 },
  status: { color: '#2f6b2f', textAlign: 'center', marginTop: 16 },
  error: { color: '#a02622', textAlign: 'center', marginTop: 16 },
  langRow: { flexDirection: 'row', justifyContent: 'center', gap: 8, marginTop: 28 },
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
});

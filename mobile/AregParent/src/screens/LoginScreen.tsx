import { useState } from 'react';
import {
  ActivityIndicator,
  Image,
  KeyboardAvoidingView,
  Linking,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { errText, login, register } from '../api';
import { API_BASE_URL } from '../config';
import { LANG_NAMES, LANGS, getLanguage, setLanguage, t } from '../i18n';
import { useLang } from '../useLang';
import PasswordInput from '../PasswordInput';
import { theme } from '../theme';

type Props = {
  onLoggedIn: (token: string) => void;
};

export default function LoginScreen({ onLoggedIn }: Props) {
  useLang();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  // Unchecked by default, same as parent.html's signupAcceptTerms — a
  // parent must actively opt in, never inherit consent from a prior state.
  const [acceptedTerms, setAcceptedTerms] = useState(false);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function switchMode(next: 'login' | 'register') {
    setMode(next);
    setAcceptedTerms(false);
    setError(null);
    setStatus(null);
  }

  function openLegal(path: string) {
    // openURL rejects on Android when nothing can handle it; without a
    // catch that is an unhandled rejection and the parent's tap does
    // nothing with no feedback at all.
    Linking.openURL(`${API_BASE_URL}${path}`).catch(() => setError(t('e_generic')));
  }

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
        await register(e, password, acceptedTerms);
        // switchMode clears status/error (and the checkbox) — set the
        // success line AFTER it, or it's wiped in the same batch.
        switchMode('login');
        setStatus(t('account_created'));
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

  // The register button stays disabled until the box is checked — this is
  // the only gate; there is no separate "please accept the terms" error
  // path to test, unlike the web form which lets you click through blind.
  const submitDisabled = busy || (mode === 'register' && !acceptedTerms);

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
      <PasswordInput
        placeholder={t('ph_password')}
        value={password}
        onChangeText={setPassword}
        editable={!busy}
        autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
      />

      {mode === 'register' && (
        <>
          <Pressable
            style={styles.termsRow}
            onPress={() => setAcceptedTerms((v) => !v)}
            disabled={busy}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: acceptedTerms, disabled: busy }}
            accessibilityLabel={t('accept_terms')}
          >
            <View style={[styles.checkbox, acceptedTerms && styles.checkboxOn]}>
              {acceptedTerms ? <Text style={styles.checkmark}>✓</Text> : null}
            </View>
            <Text style={styles.termsText}>{t('accept_terms')}</Text>
          </Pressable>
          <View style={styles.termsLinksRow}>
            <Pressable
              style={styles.termsLinkBtn}
              onPress={() => openLegal('/terms.html')}
              accessibilityRole="link"
              accessibilityLabel={t('terms_link')}
            >
              <Text style={styles.termsLink}>{t('terms_link')}</Text>
            </Pressable>
            <Text style={styles.termsLinkSep}>·</Text>
            <Pressable
              style={styles.termsLinkBtn}
              onPress={() => openLegal('/privacy.html')}
              accessibilityRole="link"
              accessibilityLabel={t('privacy_link')}
            >
              <Text style={styles.termsLink}>{t('privacy_link')}</Text>
            </Pressable>
          </View>
        </>
      )}

      <Pressable
        style={[styles.button, submitDisabled && styles.buttonDisabled]}
        onPress={submit}
        disabled={submitDisabled}
        accessibilityRole="button"
        accessibilityState={{ disabled: submitDisabled, busy }}
      >
        {busy ? (
          <ActivityIndicator color={theme.onBrand} />
        ) : (
          <Text style={styles.buttonText}>
            {mode === 'login' ? t('sign_in') : t('create_account')}
          </Text>
        )}
      </Pressable>

      <Pressable
        onPress={() => switchMode(mode === 'login' ? 'register' : 'login')}
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
  container: { flex: 1, padding: 24, justifyContent: 'center', backgroundColor: theme.surface },
  logo: { width: 88, height: 88, alignSelf: 'center', marginBottom: 4 },
  brand: { fontSize: 40, fontWeight: '700', textAlign: 'center', color: theme.brand },
  subtitle: { fontSize: 16, textAlign: 'center', marginBottom: 24, color: theme.inkMuted },
  input: {
    borderWidth: 1,
    borderColor: theme.lineInput,
    borderRadius: 8,
    padding: 12,
    fontSize: 16,
    marginBottom: 12,
  },
  button: {
    backgroundColor: theme.brand,
    borderRadius: 8,
    padding: 14,
    alignItems: 'center',
    marginTop: 4,
  },
  buttonDisabled: { opacity: 0.6 },
  buttonText: { color: theme.surface, fontSize: 16, fontWeight: '600' },
  termsRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 8,
    minHeight: 44,
    paddingVertical: 10,
  },
  checkbox: {
    width: 20,
    height: 20,
    borderRadius: 4,
    borderWidth: 1,
    borderColor: theme.lineInput,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 1,
  },
  checkboxOn: { backgroundColor: theme.brand, borderColor: theme.brand },
  checkmark: { color: theme.onBrand, fontSize: 13, fontWeight: '700', lineHeight: 14 },
  termsText: { flex: 1, fontSize: 13, color: theme.inkMuted, lineHeight: 18 },
  termsLinksRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'center',
    alignItems: 'center',
    marginBottom: 6,
  },
  // A real tap target (44pt), not a bare 12px Text — this is the only
  // route to the documents being consented to.
  termsLinkBtn: { minHeight: 44, justifyContent: 'center', paddingHorizontal: 8 },
  termsLink: { color: theme.brand, fontSize: 12, textDecorationLine: 'underline', flexShrink: 1 },
  termsLinkSep: { color: theme.inkMuted, fontSize: 12 },
  link: { color: theme.brand, textAlign: 'center', marginTop: 16 },
  status: { color: theme.ok, textAlign: 'center', marginTop: 16 },
  error: { color: theme.danger, textAlign: 'center', marginTop: 16 },
  langRow: { flexDirection: 'row', justifyContent: 'center', gap: 8, marginTop: 28 },
  langPill: {
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 999,
    paddingVertical: 8,
    paddingHorizontal: 14,
    minHeight: 44,
    justifyContent: 'center',
  },
  langPillOn: { borderColor: theme.brand, backgroundColor: theme.brandTint },
  langText: { color: theme.inkMuted, fontSize: 13 },
  langTextOn: { color: theme.brand, fontWeight: '700' },
});

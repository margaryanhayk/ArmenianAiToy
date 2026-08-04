import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import {
  errText,
  getStoryRequests,
  StoryRequest,
  submitStoryRequest,
  UnauthorizedError,
} from '../api';
import { getLanguage, Key, t } from '../i18n';
import { useLang } from '../useLang';

type Props = { onBack: () => void; onLogout: () => void };

const LOCALE: Record<string, string> = { en: 'en-GB', ru: 'ru-RU', hy: 'hy-AM' };

function fmtTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  try {
    return d.toLocaleString(LOCALE[getLanguage()] ?? 'en-GB');
  } catch {
    return d.toLocaleString();
  }
}

const STATUS: Record<string, Key> = {
  new: 'req_new',
  in_review: 'req_in_review',
  delivered: 'req_delivered',
  declined: 'req_declined',
};

/**
 * Custom story requests. Fulfilment is by hand, so this is a queue a person
 * reads — which is why a double-tap must not put the same idea in twice.
 *
 * Photo attachment is deliberately not here: it needs an image picker
 * dependency the app does not carry. Text-only requests use the same
 * endpoint, and the web dashboard covers the photo case.
 */
export default function StoryRequestScreen({ onBack, onLogout }: Props) {
  useLang();
  const [text, setText] = useState('');
  const [requests, setRequests] = useState<StoryRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setRequests(await getStoryRequests());
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      // Failing silently here would let a parent think their request was
      // lost, and send it again.
      setError(errText(err, 'e_load'));
    }
  }, [onLogout]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      await load();
      setLoading(false);
    })();
  }, [load]);

  async function send() {
    const body = text.trim();
    if (!body) {
      setStatus(null);
      setError(t('e_request_empty'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await submitStoryRequest(body);
      setText('');
      setStatus(t('request_sent'));
      await load();
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      setStatus(null);
      setError(errText(err));
    } finally {
      setBusy(false);
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
    <KeyboardAvoidingView
      style={{ flex: 1 }}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 48 }}>
        <Pressable onPress={onBack}>
          <Text style={styles.back}>{t('back_toys')}</Text>
        </Pressable>
        <Text style={styles.title}>{t('request_title')}</Text>
        <Text style={styles.help}>{t('request_help')}</Text>

        <TextInput
          style={styles.input}
          placeholder={t('request_placeholder')}
          value={text}
          onChangeText={setText}
          editable={!busy}
          multiline
          numberOfLines={5}
          maxLength={2000}
          textAlignVertical="top"
        />
        {/* Disabled while in flight: nothing server-side dedupes these and a
            person reads every one. */}
        <Pressable
          style={[styles.primaryBtn, busy && styles.disabled]}
          onPress={send}
          disabled={busy}
        >
          {busy ? (
            <ActivityIndicator color="#fff" />
          ) : (
            <Text style={styles.primaryBtnText}>{t('request_send')}</Text>
          )}
        </Pressable>

        {status ? <Text style={styles.status}>{status}</Text> : null}
        {error ? <Text style={styles.error}>{error}</Text> : null}

        <Text style={styles.section}>{t('my_requests')}</Text>
        {requests.length === 0 ? (
          <Text style={styles.empty}>{t('no_requests')}</Text>
        ) : (
          requests.map((r) => (
            <View key={r.id} style={styles.card}>
              <Text style={styles.meta}>
                {fmtTime(r.createdAtUtc)} · {STATUS[r.status] ? t(STATUS[r.status]) : r.status}
                {r.hasPhoto ? ' · 📷' : ''}
              </Text>
              <Text style={styles.body}>{r.text}</Text>
            </View>
          ))
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a' },
  help: { color: '#555', marginTop: 6, marginBottom: 12, lineHeight: 20 },
  input: {
    borderWidth: 1, borderColor: '#ccc', borderRadius: 8,
    padding: 12, minHeight: 110, fontSize: 15, marginBottom: 10,
  },
  primaryBtn: { backgroundColor: '#2c4a7a', borderRadius: 8, padding: 14, alignItems: 'center' },
  primaryBtnText: { color: '#fff', fontWeight: '600', fontSize: 16 },
  disabled: { opacity: 0.6 },
  section: { fontSize: 16, fontWeight: '700', color: '#2c4a7a', marginTop: 24, marginBottom: 8 },
  card: {
    borderWidth: 1, borderColor: '#e2e2e2', borderRadius: 10,
    padding: 12, marginBottom: 8, backgroundColor: '#fafafa',
  },
  meta: { color: '#666', fontSize: 12 },
  body: { color: '#222', marginTop: 4 },
  empty: { color: '#666' },
  status: { color: '#2f6b2f', marginTop: 10 },
  error: { color: '#a02622', marginTop: 10 },
});

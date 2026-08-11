import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import {
  errText,
  getReflectionAnswers,
  getStoryLibrary,
  getStoryPlays,
  ReflectionAnswer,
  StoryPlay,
  StoryPlayTotal,
  UnauthorizedError,
} from '../api';
import { getLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onLogout: () => void;
};

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

/**
 * Stories play from the toy's own memory, so they never become conversations.
 * This is the honest record of what the child actually heard — and the answers
 * they gave afterwards, in their own words.
 */
export default function StoryPlaysScreen({ deviceId, deviceName, onBack, onLogout }: Props) {
  useLang();
  const [plays, setPlays] = useState<StoryPlay[]>([]);
  const [totals, setTotals] = useState<StoryPlayTotal[]>([]);
  const [total, setTotal] = useState(0);
  const [answers, setAnswers] = useState<ReflectionAnswer[]>([]);
  const [titles, setTitles] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      // Titles first: without them the rows show raw story ids, which is what
      // the web version did for a while and it read like a data bug.
      const lib = await getStoryLibrary(deviceId).catch(() => []);
      const map: Record<string, string> = {};
      lib.forEach((s) => { map[s.storyId] = s.title; });
      setTitles(map);

      const r = await getStoryPlays(deviceId);
      setPlays(r.plays);
      setTotals(r.totals);
      setTotal(r.total);
      // Additive: a failure here must not hide the plays list.
      const a = await getReflectionAnswers(deviceId).catch(() => ({ answers: [], total: 0 }));
      setAnswers(a.answers);
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      setError(errText(err, 'e_load'));
    }
  }, [deviceId, onLogout]);

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

  const title = (id: string) => titles[id] ?? id;

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color="#2c4a7a" />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={{ paddingBottom: 40 }}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
    >
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_toys')}</Text>
      </Pressable>
      <Text style={styles.title}>{t('plays_title')}</Text>
      <Text style={styles.sub}>{deviceName || t('toy_word')}</Text>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      {totals.map((x) => (
        <View key={x.storyId} style={styles.totalRow}>
          <Text style={styles.totalName}>📖 {title(x.storyId)}</Text>
          <Text style={styles.totalCount}>
            {tf('listened_count', { n: x.count, f: x.finishedCount })}
          </Text>
        </View>
      ))}

      {plays.length === 0 ? (
        <Text style={styles.empty}>{t('no_plays')}</Text>
      ) : (
        plays.map((p, i) => (
          <View key={p.storyId + p.playedAtUtc + i} style={styles.row}>
            <Text style={styles.rowName}>📖 {title(p.storyId)}</Text>
            <Text style={styles.rowMeta}>
              {p.timeIsApproximate ? t('approx_time') + ' ' : ''}
              {fmtTime(p.playedAtUtc)}
              {'  ·  '}
              {p.finished ? t('play_finished') : t('play_partial')}
            </Text>
          </View>
        ))
      )}

      {/* The counters above are whole-history while the list is capped, so
          without this the page would contradict itself. */}
      {total > plays.length ? (
        <Text style={styles.note}>{tf('showing_recent', { n: plays.length })}</Text>
      ) : null}

      {answers.length > 0 ? (
        <View style={{ marginTop: 20 }}>
          <Text style={styles.section}>💬 {t('child_answers')}</Text>
          {answers.map((a, i) => (
            <View key={a.storyId + a.createdAtUtc + i} style={styles.answer}>
              <Text style={styles.rowMeta}>
                {title(a.storyId)} · {fmtTime(a.createdAtUtc)}
              </Text>
              <Text style={styles.answerText}>«{a.answerText}»</Text>
            </View>
          ))}
        </View>
      ) : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand },
  sub: { color: theme.inkMuted, marginBottom: 12 },
  section: { fontSize: 16, fontWeight: '700', color: theme.brand, marginBottom: 8 },
  totalRow: {
    borderWidth: 1, borderColor: theme.line, backgroundColor: theme.surfaceTint,
    borderRadius: 10, padding: 12, marginBottom: 8,
  },
  totalName: { fontSize: 15, fontWeight: '700', color: theme.ink },
  totalCount: { color: theme.inkMuted, marginTop: 2, fontSize: 13 },
  row: {
    borderWidth: 1, borderColor: theme.line, borderRadius: 10,
    padding: 12, marginBottom: 8, backgroundColor: theme.surfaceSunken,
  },
  rowName: { fontSize: 15, color: theme.ink },
  rowMeta: { color: theme.inkMuted, fontSize: 12, marginTop: 3 },
  answer: { borderBottomWidth: 1, borderBottomColor: theme.lineSoft, paddingVertical: 8 },
  answerText: { color: theme.ink, marginTop: 3 },
  empty: { textAlign: 'center', color: theme.inkMuted, marginTop: 24 },
  note: { color: theme.inkMuted, fontSize: 12, marginTop: 8 },
  error: { color: theme.danger, marginBottom: 8 },
});

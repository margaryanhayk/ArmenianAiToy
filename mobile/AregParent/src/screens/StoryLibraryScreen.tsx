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
import { errText, getStoryLibrary, LibraryStory, UnauthorizedError } from '../api';
import { t, tf } from '../i18n';
import { useLang } from '../useLang';

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onLogout: () => void;
};

/**
 * What this toy can tell, with the same question-and-takeaway guide the toy
 * itself speaks after a story — so a parent can carry the conversation on
 * away from the toy.
 *
 * The listen counts are scoped to ONE toy here, which is why the toy's name
 * is printed above them: a count without its scope is not information.
 */
export default function StoryLibraryScreen({ deviceId, deviceName, onBack, onLogout }: Props) {
  useLang();
  const [stories, setStories] = useState<LibraryStory[]>([]);
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setStories(await getStoryLibrary(deviceId));
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
      <Text style={styles.title}>{t('library_title')}</Text>
      <Text style={styles.sub}>
        {t('on_this_toy')}: {deviceName || t('toy_word')}
      </Text>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      {stories.length === 0 ? (
        <Text style={styles.empty}>{t('no_stories')}</Text>
      ) : (
        stories.map((s) => {
          const meta: string[] = [];
          if (s.author) meta.push(`${t('author_label')}: ${s.author}`);
          if (s.minAge != null && s.maxAge != null) meta.push(`${s.minAge}–${s.maxAge}`);
          if (s.bedtimeSafe) meta.push(t('bedtime_safe'));
          const isOpen = !!open[s.storyId];
          const questions = s.reflectionQuestions ?? [];
          const takeaways = s.reflectionConclusions ?? [];
          return (
            <View key={s.storyId} style={styles.card}>
              <Text style={styles.name}>📖 {s.title}</Text>
              {meta.length > 0 ? <Text style={styles.meta}>{meta.join(' · ')}</Text> : null}
              {s.goal ? <Text style={styles.body}>{t('goal_label')}: {s.goal}</Text> : null}
              {s.lesson ? (
                <Text style={styles.lesson}>{t('lesson_label')}: {s.lesson}</Text>
              ) : null}
              <Text style={styles.counts}>
                {tf('listened_count', { n: s.listenCount ?? 0, f: s.finishedCount ?? 0 })}
              </Text>
              {questions.length > 0 ? (
                <>
                  <Pressable
                    onPress={() => setOpen((o) => ({ ...o, [s.storyId]: !o[s.storyId] }))}
                  >
                    <Text style={styles.discuss}>
                      {isOpen ? '▾ ' : '▸ '}💬 {t('discuss_title')}
                    </Text>
                  </Pressable>
                  {isOpen
                    ? questions.map((q, i) => (
                        <View key={i} style={{ marginTop: 6 }}>
                          <Text style={styles.question}>{i + 1}. {q}</Text>
                          {takeaways[i] ? (
                            <Text style={styles.takeaway}>→ {takeaways[i]}</Text>
                          ) : null}
                        </View>
                      ))
                    : null}
                </>
              ) : null}
            </View>
          );
        })
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a' },
  sub: { color: '#666', marginBottom: 12 },
  card: {
    borderWidth: 1, borderColor: '#e2e2e2', borderRadius: 10,
    padding: 14, marginBottom: 10, backgroundColor: '#fafafa',
  },
  name: { fontSize: 16, fontWeight: '700', color: '#222' },
  meta: { color: '#666', fontSize: 12, marginTop: 2 },
  body: { color: '#333', fontSize: 14, marginTop: 6 },
  lesson: { color: '#3a5a3a', fontSize: 14, marginTop: 4 },
  counts: { color: '#666', fontSize: 12, marginTop: 6 },
  discuss: { color: '#2c4a7a', fontWeight: '600', marginTop: 10, paddingVertical: 6 },
  question: { color: '#222', marginLeft: 6 },
  takeaway: { color: '#3a5a3a', fontSize: 13, marginLeft: 16, marginTop: 2 },
  empty: { textAlign: 'center', color: '#666', marginTop: 24 },
  error: { color: '#a02622', marginBottom: 8 },
});

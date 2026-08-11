import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Image,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { errText, getStoryLibrary, LibraryStory, UnauthorizedError } from '../api';
import { API_BASE_URL } from '../config';
import { getLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

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
/**
 * The parent-facing text in the parent's language, falling back to the
 * Armenian PER FIELD. Per field, not per story: a story with an English title
 * and no English lesson should show the English title, not drop back wholesale.
 */
function storyText(s: LibraryStory, field: 'title' | 'goal' | 'lesson'): string {
  const tr = s.translations?.[getLanguage()];
  const v = tr?.[field];
  if (v && String(v).trim()) return String(v);
  return (s[field] as string | null) ?? '';
}

/**
 * The story's cover. Convention-based, exactly as on the web: the file is
 * `/covers/<storyId>.webp` on the backend, so nothing has to be added to the
 * DTO and a story without artwork simply has no file.
 *
 * A missing cover must leave NO trace — not a grey box, not a broken-image
 * icon. Ten stories have art and the eleventh should look deliberate.
 */
function StoryCover({ storyId }: { storyId: string }) {
  const [failed, setFailed] = useState(false);
  if (failed) return null;
  return (
    <Image
      source={{ uri: `${API_BASE_URL}/covers/${encodeURIComponent(storyId)}.webp` }}
      style={styles.cover}
      onError={() => setFailed(true)}
      accessibilityIgnoresInvertColors
    />
  );
}

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
        <ActivityIndicator size="large" color={theme.brand} />
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
        {t('on_this_toy')}{t('label_sep')}{deviceName || t('toy_word')}
      </Text>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      {stories.length === 0 ? (
        <Text style={styles.empty}>{t('no_stories')}</Text>
      ) : (
        stories.map((s) => {
          const meta: string[] = [];
          if (s.author) meta.push(s.author);
          if (s.minAge != null && s.maxAge != null) meta.push(`${s.minAge}–${s.maxAge}`);
          if (s.bedtimeSafe) meta.push(t('bedtime_safe'));
          const isOpen = !!open[s.storyId];
          const questions = s.reflectionQuestions ?? [];
          const takeaways = s.reflectionConclusions ?? [];
          const title = storyText(s, 'title') || s.title;
          const goal = storyText(s, 'goal');
          const lesson = storyText(s, 'lesson');
          return (
            <View key={s.storyId} style={styles.card}>
              <StoryCover storyId={s.storyId} />
              <Text style={styles.name}>{title}</Text>
              {meta.length > 0 ? <Text style={styles.meta}>{meta.join(' · ')}</Text> : null}
              {/* The goal is the card's own sentence now. "About:" in front of
                  it was a column heading from a database, and no real app
                  shows a parent the name of a field. */}
              {goal ? <Text style={styles.body}>{goal}</Text> : null}
              {lesson ? <Text style={styles.lesson}>{lesson}</Text> : null}
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
  // 4:5, the same proportion the covers were drawn at, reserved up front so
  // the list does not jump as images land.
  cover: {
    width: '100%',
    aspectRatio: 4 / 5,
    borderRadius: 10,
    marginBottom: 10,
    backgroundColor: theme.surfaceTint,
    resizeMode: 'cover',
  },
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand },
  sub: { color: theme.inkMuted, marginBottom: 12 },
  card: {
    borderWidth: 1, borderColor: theme.line, borderRadius: 10,
    padding: 14, marginBottom: 10, backgroundColor: theme.surfaceSunken,
  },
  name: { fontSize: 16, fontWeight: '700', color: theme.ink },
  meta: { color: theme.inkMuted, fontSize: 12, marginTop: 2 },
  body: { color: theme.ink, fontSize: 14, marginTop: 6 },
  lesson: { color: theme.ok, fontSize: 14, marginTop: 4 },
  counts: { color: theme.inkMuted, fontSize: 12, marginTop: 6 },
  discuss: { color: theme.brand, fontWeight: '600', marginTop: 10, paddingVertical: 6 },
  question: { color: theme.ink, marginLeft: 6 },
  takeaway: { color: theme.ok, fontSize: 13, marginLeft: 16, marginTop: 2 },
  empty: { textAlign: 'center', color: theme.inkMuted, marginTop: 24 },
  error: { color: theme.danger, marginBottom: 8 },
});

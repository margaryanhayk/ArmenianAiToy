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
  GamePlay,
  GamePlayTotal,
  getGamePlays,
  UnauthorizedError,
} from '../api';
import { getLanguage, Key, t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';
import DiaryFilter, { DiaryView } from './DiaryFilter';

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onDiary: (v: DiaryView) => void;
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
 * Friendly name for one of the offline games. The raw key is the toy's SD
 * directory name and a parent must never be shown "button-simon".
 *
 * An unrecognised key falls through VERBATIM rather than rendering nothing.
 * `gameKey` is runtime JSON from a backend that may be newer than this build,
 * and a missing case that silently shows a blank row is the failure mode this
 * fallback exists to prevent.
 */
const GAME_NAME_KEY: Record<string, Key> = {
  'mind-reader': 'game_mind_reader',
  'who-first': 'game_who_first',
  'button-simon': 'game_button_simon',
  'sound-detective': 'game_sound_detective',
};
function gameName(key: string): string {
  const k = GAME_NAME_KEY[key];
  return k ? t(k) : key;
}

/**
 * The supporting detail on one row: how the session ended, how many rounds,
 * and — Simon only — how long a sequence was echoed back.
 *
 * Every part is omitted when the toy did not measure it: null means "not
 * measured", which is not the same as zero. `outcome` is likewise checked
 * case by case with NO else-branch inventing a label, so a token this build
 * has never heard of simply contributes nothing instead of printing itself
 * raw next to translated text.
 */
function rowDetail(p: GamePlay): string {
  const parts: string[] = [];
  if (p.outcome === 'won') parts.push(t('game_won'));
  else if (p.outcome === 'lost') parts.push(t('game_lost'));
  else if (p.outcome === 'stopped') parts.push(t('game_stopped'));
  if (typeof p.rounds === 'number') parts.push(tf('game_rounds', { n: p.rounds }));
  if (typeof p.score === 'number') parts.push(tf('game_reached', { n: p.score }));
  return parts.join('  ·  ');
}

/**
 * The offline games run entirely on the toy from its SD card and never become
 * conversations, so this is the only record that they happened at all.
 *
 * Describes, never grades. There is no best score, no streak and no
 * comparison with last week anywhere here: a child who stopped at three is
 * not behind a child who reached six, and a parent screen implying otherwise
 * turns a game into a test.
 */
export default function GamePlaysScreen({
  deviceId,
  deviceName,
  onBack,
  onDiary,
  onLogout,
}: Props) {
  useLang();
  const [plays, setPlays] = useState<GamePlay[]>([]);
  const [totals, setTotals] = useState<GamePlayTotal[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const r = await getGamePlays(deviceId);
      // Defensive against an older/newer backend shape: a missing array must
      // render an empty list, not crash the screen.
      setPlays(r.plays ?? []);
      setTotals(r.totals ?? []);
      setTotal(r.total ?? 0);
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
      <Text style={styles.title}>{t('games_title')}</Text>
      <DiaryFilter current="gameplays" onChange={onDiary} />
      <Text style={styles.sub}>{deviceName || t('toy_word')}</Text>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      {totals.map((x) => (
        <View key={x.gameKey} style={styles.totalRow}>
          <Text style={styles.totalName}>🎲 {gameName(x.gameKey)}</Text>
          <Text style={styles.totalCount}>{tf('games_count', { n: x.count })}</Text>
        </View>
      ))}

      {plays.length === 0 ? (
        <Text style={styles.empty}>{t('no_games')}</Text>
      ) : (
        plays.map((p, i) => {
          const detail = rowDetail(p);
          return (
            <View key={p.gameKey + p.playedAtUtc + i} style={styles.row}>
              <Text style={styles.rowName}>🎲 {gameName(p.gameKey)}</Text>
              {/* Prefix, matching this app's story-plays screen: the mobile
                  `approx_time` string is «about» / «примерно», which only
                  reads correctly before the time. The web dashboard says
                  «(~approximate time)» AFTER it — the two surfaces have said
                  this differently since before this screen existed, and
                  unifying them means a shared "about {time}" key applied to
                  the story-plays list on both. Left consistent with its own
                  neighbour rather than half-changed here. */}
              <Text style={styles.rowMeta}>
                {p.timeIsApproximate ? t('approx_time') + ' ' : ''}
                {fmtTime(p.playedAtUtc)}
                {detail ? '  ·  ' + detail : ''}
              </Text>
            </View>
          );
        })
      )}

      {/* The counters above are whole-history while the list is capped, so
          without this the page would contradict itself. */}
      {total > plays.length ? (
        <Text style={styles.note}>{tf('showing_recent_games', { n: plays.length })}</Text>
      ) : null}

      {/* Says plainly what the toy can and cannot know. The honesty rule is
          not only for the child's ear. */}
      {plays.length > 0 ? <Text style={styles.note}>{t('games_note')}</Text> : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand },
  sub: { color: theme.inkMuted, marginBottom: 12 },
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
  empty: { textAlign: 'center', color: theme.inkMuted, marginTop: 24 },
  note: { color: theme.inkMuted, fontSize: 12, marginTop: 8 },
  error: { color: theme.danger, marginBottom: 8 },
});

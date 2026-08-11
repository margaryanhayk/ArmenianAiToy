import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import {
  ConversationSummary,
  errText,
  getConversations,
  getTodaySummary,
  TodaySummary,
  UnauthorizedError,
} from '../api';
import { Key, getLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

const LOCALE: Record<string, string> = { en: 'en-GB', ru: 'ru-RU', hy: 'hy-AM' };

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onOpenConversation: (conversationId: string) => void;
  onOpenFlagged: () => void;
  onLogout: () => void;
};

// Dates follow the chosen language, not the phone's.
function fmtTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  try {
    return d.toLocaleString(LOCALE[getLanguage()] ?? 'en-GB');
  } catch {
    return d.toLocaleString();
  }
}

export default function ConversationsScreen({
  deviceId,
  deviceName,
  onBack,
  onOpenConversation,
  onOpenFlagged,
  onLogout,
}: Props) {
  useLang();
  const [today, setToday] = useState<TodaySummary | null>(null);
  const [convos, setConvos] = useState<ConversationSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [t, c] = await Promise.all([getTodaySummary(deviceId), getConversations(deviceId)]);
      setToday(t);
      setConvos(c);
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

  return (
    <View style={styles.container}>
      <View style={styles.headerRow}>
        <Pressable onPress={onBack}>
          <Text style={styles.back}>{t('back_toys')}</Text>
        </Pressable>
        <Pressable onPress={onOpenFlagged}>
          <Text style={styles.flaggedLink}>{t('flagged_link')}</Text>
        </Pressable>
      </View>
      <Text style={styles.title}>{deviceName || t('toy_word')}</Text>

      {loading ? (
        <ActivityIndicator size="large" color={theme.brand} style={{ marginTop: 40 }} />
      ) : (
        <FlatList
          data={convos}
          extraData={getLanguage()}
          keyExtractor={(c) => c.id}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
          ListHeaderComponent={
            <View>
              {today ? (
                <View style={styles.todayCard}>
                  <Text style={styles.todayTitle}>{t('today')}</Text>
                  <View style={styles.todayRow}>
                    <Stat labelKey="stat_talks" value={today.conversationsCount} />
                    <Stat labelKey="stat_messages" value={today.messagesCount} />
                    <Stat
                      labelKey="stat_flagged"
                      value={today.flaggedMessagesCount}
                      warn={today.flaggedMessagesCount > 0}
                    />
                  </View>
                </View>
              ) : null}
              {error ? <Text style={styles.error}>{error}</Text> : null}
              <Text style={styles.sectionLabel}>{t('section_conversations')}</Text>
            </View>
          }
          ListEmptyComponent={
            <Text style={styles.empty}>{t('no_conversations')}</Text>
          }
          renderItem={({ item }) => (
            <Pressable style={styles.row} onPress={() => onOpenConversation(item.id)}>
              <View style={styles.rowTop}>
                <Text style={styles.rowTime}>{fmtTime(item.startedAt)}</Text>
                <Text style={styles.rowCount}>{tf('msg_count', { n: item.messageCount })}</Text>
                {item.flaggedMessageCount > 0 ? (
                  <Text style={styles.flag}>⚑ {item.flaggedMessageCount}</Text>
                ) : null}
              </View>
              {item.firstUserSnippet ? (
                <Text style={styles.snippet} numberOfLines={1}>
                  🧒 {item.firstUserSnippet}
                </Text>
              ) : null}
              {item.lastAssistantSnippet ? (
                <Text style={styles.snippetAreg} numberOfLines={2}>
                  🧸 {item.lastAssistantSnippet}
                </Text>
              ) : null}
            </Pressable>
          )}
        />
      )}
    </View>
  );
}

function Stat({ labelKey, value, warn }: { labelKey: Key; value: number; warn?: boolean }) {
  return (
    <View style={styles.stat}>
      <Text style={[styles.statValue, warn ? styles.statWarn : null]}>{value}</Text>
      <Text style={styles.statLabel}>{t(labelKey)}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  flaggedLink: { color: theme.danger, fontSize: 15, fontWeight: '600' },
  title: { fontSize: 24, fontWeight: '700', color: theme.brand, marginBottom: 12 },
  todayCard: {
    backgroundColor: theme.surfaceTint,
    borderColor: theme.line,
    borderWidth: 1,
    borderRadius: 10,
    padding: 14,
    marginBottom: 16,
  },
  todayTitle: { fontSize: 14, fontWeight: '600', color: theme.brand, marginBottom: 10 },
  todayRow: { flexDirection: 'row', justifyContent: 'space-around' },
  stat: { alignItems: 'center' },
  statValue: { fontSize: 26, fontWeight: '700', color: theme.brand },
  statWarn: { color: theme.danger },
  statLabel: { fontSize: 12, color: theme.inkMuted, marginTop: 2 },
  sectionLabel: { fontSize: 13, color: theme.inkHint, marginBottom: 6, textTransform: 'uppercase' },
  empty: { textAlign: 'center', color: theme.inkHint, marginTop: 24 },
  error: { color: theme.danger, marginBottom: 8 },
  row: {
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 10,
    padding: 12,
    marginBottom: 8,
    backgroundColor: theme.surfaceSunken,
  },
  rowTop: { flexDirection: 'row', alignItems: 'center' },
  rowTime: { color: theme.inkMuted, fontSize: 13, flex: 1 },
  rowCount: { color: theme.inkHint, fontSize: 12 },
  flag: { color: theme.danger, fontSize: 12, marginLeft: 8 },
  snippet: { color: theme.ink, marginTop: 6 },
  snippetAreg: { color: theme.brand, marginTop: 4 },
});

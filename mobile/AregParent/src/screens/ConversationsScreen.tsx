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
  getConversations,
  getTodaySummary,
  TodaySummary,
  UnauthorizedError,
} from '../api';

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onOpenConversation: (conversationId: string) => void;
  onOpenFlagged: () => void;
  onLogout: () => void;
};

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

export default function ConversationsScreen({
  deviceId,
  deviceName,
  onBack,
  onOpenConversation,
  onOpenFlagged,
  onLogout,
}: Props) {
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
      setError(err instanceof Error ? err.message : 'Failed to load.');
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
          <Text style={styles.back}>‹ Toys</Text>
        </Pressable>
        <Pressable onPress={onOpenFlagged}>
          <Text style={styles.flaggedLink}>⚑ Flagged</Text>
        </Pressable>
      </View>
      <Text style={styles.title}>{deviceName || 'Toy'}</Text>

      {loading ? (
        <ActivityIndicator size="large" color="#2c4a7a" style={{ marginTop: 40 }} />
      ) : (
        <FlatList
          data={convos}
          keyExtractor={(c) => c.id}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
          ListHeaderComponent={
            <View>
              {today ? (
                <View style={styles.todayCard}>
                  <Text style={styles.todayTitle}>Today</Text>
                  <View style={styles.todayRow}>
                    <Stat label="Talks" value={today.conversationsCount} />
                    <Stat label="Messages" value={today.messagesCount} />
                    <Stat label="Flagged" value={today.flaggedMessagesCount} warn={today.flaggedMessagesCount > 0} />
                  </View>
                </View>
              ) : null}
              {error ? <Text style={styles.error}>{error}</Text> : null}
              <Text style={styles.sectionLabel}>Conversations</Text>
            </View>
          }
          ListEmptyComponent={
            <Text style={styles.empty}>
              No conversations yet. They&apos;ll appear here after your child talks with the toy.
            </Text>
          }
          renderItem={({ item }) => (
            <Pressable style={styles.row} onPress={() => onOpenConversation(item.id)}>
              <View style={styles.rowTop}>
                <Text style={styles.rowTime}>{fmtTime(item.startedAt)}</Text>
                <Text style={styles.rowCount}>{item.messageCount} msg</Text>
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

function Stat({ label, value, warn }: { label: string; value: number; warn?: boolean }) {
  return (
    <View style={styles.stat}>
      <Text style={[styles.statValue, warn ? styles.statWarn : null]}>{value}</Text>
      <Text style={styles.statLabel}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  flaggedLink: { color: '#a02622', fontSize: 15, fontWeight: '600' },
  title: { fontSize: 24, fontWeight: '700', color: '#2c4a7a', marginBottom: 12 },
  todayCard: {
    backgroundColor: '#f0f4fb',
    borderColor: '#cdddf2',
    borderWidth: 1,
    borderRadius: 10,
    padding: 14,
    marginBottom: 16,
  },
  todayTitle: { fontSize: 14, fontWeight: '600', color: '#2c4a7a', marginBottom: 10 },
  todayRow: { flexDirection: 'row', justifyContent: 'space-around' },
  stat: { alignItems: 'center' },
  statValue: { fontSize: 26, fontWeight: '700', color: '#2c4a7a' },
  statWarn: { color: '#a02622' },
  statLabel: { fontSize: 12, color: '#666', marginTop: 2 },
  sectionLabel: { fontSize: 13, color: '#888', marginBottom: 6, textTransform: 'uppercase' },
  empty: { textAlign: 'center', color: '#888', marginTop: 24 },
  error: { color: '#a02622', marginBottom: 8 },
  row: {
    borderWidth: 1,
    borderColor: '#e2e2e2',
    borderRadius: 10,
    padding: 12,
    marginBottom: 8,
    backgroundColor: '#fafafa',
  },
  rowTop: { flexDirection: 'row', alignItems: 'center' },
  rowTime: { color: '#555', fontSize: 13, flex: 1 },
  rowCount: { color: '#888', fontSize: 12 },
  flag: { color: '#a02622', fontSize: 12, marginLeft: 8 },
  snippet: { color: '#222', marginTop: 6 },
  snippetAreg: { color: '#2c4a7a', marginTop: 4 },
});

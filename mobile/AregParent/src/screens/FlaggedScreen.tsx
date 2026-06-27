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
import { FlaggedMessage, getFlagged, UnauthorizedError } from '../api';

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onOpenConversation: (conversationId: string) => void;
  onLogout: () => void;
};

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

export default function FlaggedScreen({
  deviceId,
  deviceName,
  onBack,
  onOpenConversation,
  onLogout,
}: Props) {
  const [items, setItems] = useState<FlaggedMessage[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setItems(await getFlagged(deviceId));
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
      <Pressable onPress={onBack}>
        <Text style={styles.back}>‹ Activity</Text>
      </Pressable>
      <Text style={styles.title}>Flagged · {deviceName || 'Toy'}</Text>

      {loading ? (
        <ActivityIndicator size="large" color="#2c4a7a" style={{ marginTop: 40 }} />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(m) => m.id}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
          ListHeaderComponent={error ? <Text style={styles.error}>{error}</Text> : null}
          ListEmptyComponent={
            <View style={styles.clear}>
              <Text style={styles.clearIcon}>✓</Text>
              <Text style={styles.clearText}>All clear</Text>
              <Text style={styles.clearHint}>
                Nothing has been flagged on this toy. Anything the safety system
                catches will show up here.
              </Text>
            </View>
          }
          renderItem={({ item }) => (
            <Pressable style={styles.row} onPress={() => onOpenConversation(item.conversationId)}>
              <View style={styles.rowTop}>
                <Text style={styles.flag}>⚑ flagged</Text>
                <Text style={styles.role}>{item.role}</Text>
                <Text style={styles.time}>{fmtTime(item.timestamp)}</Text>
              </View>
              <Text style={styles.content} numberOfLines={3}>
                {item.content?.trim()}
              </Text>
              <Text style={styles.openHint}>Open conversation →</Text>
            </Pressable>
          )}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a', marginBottom: 12 },
  error: { color: '#a02622', marginBottom: 8 },
  clear: { alignItems: 'center', marginTop: 48, paddingHorizontal: 24 },
  clearIcon: { fontSize: 44, color: '#5aa45a' },
  clearText: { fontSize: 18, fontWeight: '700', color: '#2f6b2f', marginTop: 8 },
  clearHint: { textAlign: 'center', color: '#888', marginTop: 8 },
  row: {
    borderWidth: 1,
    borderColor: '#f0c9c7',
    backgroundColor: '#fdf6f5',
    borderRadius: 10,
    padding: 12,
    marginBottom: 8,
  },
  rowTop: { flexDirection: 'row', alignItems: 'center' },
  flag: { color: '#a02622', fontWeight: '700', fontSize: 13 },
  role: { color: '#777', fontSize: 12, marginLeft: 8, flex: 1 },
  time: { color: '#999', fontSize: 12 },
  content: { color: '#222', marginTop: 6 },
  openHint: { color: '#2c4a7a', fontSize: 12, marginTop: 8 },
});

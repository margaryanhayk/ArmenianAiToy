import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import {
  ConversationDetail,
  ConversationMessage,
  getConversation,
  UnauthorizedError,
} from '../api';

type Props = {
  conversationId: string;
  onBack: () => void;
  onLogout: () => void;
};

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleTimeString();
}

export default function ConversationDetailScreen({ conversationId, onBack, onLogout }: Props) {
  const [detail, setDetail] = useState<ConversationDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setDetail(await getConversation(conversationId));
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      setError(err instanceof Error ? err.message : 'Failed to load.');
    }
  }, [conversationId, onLogout]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      await load();
      setLoading(false);
    })();
  }, [load]);

  return (
    <View style={styles.container}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>‹ Back</Text>
      </Pressable>
      <Text style={styles.title}>Conversation</Text>

      {loading ? (
        <ActivityIndicator size="large" color="#2c4a7a" style={{ marginTop: 40 }} />
      ) : error ? (
        <Text style={styles.error}>{error}</Text>
      ) : (
        <FlatList
          data={detail?.messages ?? []}
          keyExtractor={(m) => m.id}
          renderItem={({ item }) => <Bubble message={item} />}
          ListEmptyComponent={<Text style={styles.empty}>No messages.</Text>}
        />
      )}
    </View>
  );
}

function Bubble({ message }: { message: ConversationMessage }) {
  const isChild = message.role === 'user';
  const flagged = message.safetyFlag !== 0;
  return (
    <View style={[styles.bubbleWrap, isChild ? styles.wrapRight : styles.wrapLeft]}>
      <Text style={styles.who}>{isChild ? '🧒 Child' : '🧸 Areg'}</Text>
      <View
        style={[
          styles.bubble,
          isChild ? styles.child : styles.areg,
          flagged ? styles.flaggedBubble : null,
        ]}
      >
        <Text style={isChild ? styles.childText : styles.aregText}>{message.content.trim()}</Text>
      </View>
      <Text style={styles.time}>
        {fmtTime(message.timestamp)}
        {flagged ? '  ·  ⚑ flagged' : ''}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: '#fff' },
  back: { color: '#2c4a7a', fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: '#2c4a7a', marginBottom: 12 },
  error: { color: '#a02622', marginTop: 16 },
  empty: { textAlign: 'center', color: '#888', marginTop: 24 },
  bubbleWrap: { marginBottom: 14, maxWidth: '85%' },
  wrapLeft: { alignSelf: 'flex-start' },
  wrapRight: { alignSelf: 'flex-end' },
  who: { fontSize: 12, color: '#888', marginBottom: 2 },
  bubble: { borderRadius: 12, padding: 12 },
  child: { backgroundColor: '#e7f0ff' },
  areg: { backgroundColor: '#f1f1f1' },
  flaggedBubble: { borderWidth: 1, borderColor: '#d9534f' },
  childText: { color: '#143', fontSize: 15 },
  aregText: { color: '#222', fontSize: 15 },
  time: { fontSize: 11, color: '#aaa', marginTop: 3 },
});

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
import { errText, FlaggedMessage, getFlagged, UnauthorizedError } from '../api';
import { getLanguage, t, tf } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';
import DiaryFilter, { DiaryView } from './DiaryFilter';

const LOCALE: Record<string, string> = { en: 'en-GB', ru: 'ru-RU', hy: 'hy-AM' };

type Props = {
  deviceId: string;
  deviceName: string;
  onBack: () => void;
  onDiary: (v: DiaryView) => void;
  onOpenConversation: (conversationId: string) => void;
  onLogout: () => void;
};

function fmtTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  try {
    return d.toLocaleString(LOCALE[getLanguage()] ?? 'en-GB');
  } catch {
    return d.toLocaleString();
  }
}

export default function FlaggedScreen({
  deviceId,
  deviceName,
  onBack,
  onDiary,
  onOpenConversation,
  onLogout,
}: Props) {
  useLang();
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
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_activity')}</Text>
      </Pressable>
      <Text style={styles.title}>{tf('flagged_of', { name: deviceName || t('toy_word') })}</Text>
      <DiaryFilter current="flagged" onChange={onDiary} />

      {loading ? (
        <ActivityIndicator size="large" color={theme.brand} style={{ marginTop: 40 }} />
      ) : (
        <FlatList
          data={items}
          extraData={getLanguage()}
          keyExtractor={(m) => m.id}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
          ListHeaderComponent={error ? <Text style={styles.error}>{error}</Text> : null}
          ListEmptyComponent={
            <View style={styles.clear}>
              <Text style={styles.clearIcon}>✓</Text>
              <Text style={styles.clearText}>{t('all_clear')}</Text>
              <Text style={styles.clearHint}>{t('all_clear_hint')}</Text>
            </View>
          }
          renderItem={({ item }) => (
            <Pressable style={styles.row} onPress={() => onOpenConversation(item.conversationId)}>
              <View style={styles.rowTop}>
                <Text style={styles.flag}>{t('flagged_tag')}</Text>
                <Text style={styles.role}>
                  {item.role === 'user' ? t('role_child') : t('role_toy')}
                </Text>
                <Text style={styles.time}>{fmtTime(item.timestamp)}</Text>
              </View>
              <Text style={styles.content} numberOfLines={3}>
                {item.content?.trim()}
              </Text>
              <Text style={styles.openHint}>{t('open_conversation')}</Text>
            </Pressable>
          )}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 12 },
  error: { color: theme.danger, marginBottom: 8 },
  clear: { alignItems: 'center', marginTop: 48, paddingHorizontal: 24 },
  clearIcon: { fontSize: 44, color: theme.okLine },
  clearText: { fontSize: 18, fontWeight: '700', color: theme.ok, marginTop: 8 },
  clearHint: { textAlign: 'center', color: theme.inkHint, marginTop: 8 },
  row: {
    borderWidth: 1,
    borderColor: theme.dangerLine,
    backgroundColor: theme.dangerBg,
    borderRadius: 10,
    padding: 12,
    marginBottom: 8,
  },
  rowTop: { flexDirection: 'row', alignItems: 'center' },
  flag: { color: theme.danger, fontWeight: '700', fontSize: 13 },
  role: { color: theme.inkHint, fontSize: 12, marginLeft: 8, flex: 1 },
  time: { color: theme.inkHint, fontSize: 12 },
  content: { color: theme.ink, marginTop: 6 },
  openHint: { color: theme.brand, fontSize: 12, marginTop: 8 },
});

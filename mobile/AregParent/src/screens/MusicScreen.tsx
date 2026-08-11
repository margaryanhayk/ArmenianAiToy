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
import { errText, getMusic, MusicTrack, UnauthorizedError } from '../api';
import { t } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

type Props = { onBack: () => void; onLogout: () => void };

/**
 * The bedtime tracks the toy can play. Account-wide, not per toy — which is
 * why no toy name appears here.
 *
 * Ships empty until rights-cleared tracks are configured, so the empty state
 * has to be the honest normal case rather than an error.
 */
export default function MusicScreen({ onBack, onLogout }: Props) {
  useLang();
  const [tracks, setTracks] = useState<MusicTrack[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setTracks(await getMusic());
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
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
      <Text style={styles.title}>{t('music_title')}</Text>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      {tracks.length === 0 ? (
        <Text style={styles.empty}>{t('no_music')}</Text>
      ) : (
        tracks.map((m) => (
          <View key={m.trackId} style={styles.card}>
            <Text style={styles.name}>🎵 {m.title || m.trackId}</Text>
          </View>
        ))
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 12 },
  card: {
    borderWidth: 1, borderColor: theme.line, borderRadius: 10,
    padding: 14, marginBottom: 8, backgroundColor: theme.surfaceSunken,
  },
  name: { fontSize: 15, fontWeight: '600', color: theme.ink },
  empty: { textAlign: 'center', color: theme.inkMuted, marginTop: 24 },
  error: { color: theme.danger, marginBottom: 8 },
});

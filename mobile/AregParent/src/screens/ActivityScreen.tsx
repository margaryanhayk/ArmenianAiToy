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
import { AuditEntry, errText, getActivity, LinkedDevice, UnauthorizedError } from '../api';
import { getLanguage, Key, t } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

type Props = {
  devices: LinkedDevice[];
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

// Audit-event type -> label key. A type with no entry falls back to its raw
// name rather than vanishing: an incomplete label beats a missing row in a
// record of what happened to the account.
const LABELS: Record<string, Key> = {
  ParentAccountDeleted: 'ev_account_deleted',
  ParentChildDeleted: 'ev_child_deleted',
  ParentDeviceUnlinked: 'ev_device_unlinked',
  ParentPasswordChanged: 'ev_password_changed',
  ParentDevicePauseStateChanged: 'ev_pause_changed',
  ParentBedtimeWindowSet: 'ev_bedtime_set',
  ParentDeviceModeFlagsSet: 'ev_modes_set',
  ChildModeOverridesSet: 'ev_child_modes_set',
  ParentConversationDeleted: 'ev_conversation_deleted',
  ParentGoogleSignIn: 'ev_google_signin',
  ParentDataExported: 'ev_data_exported',
  ParentPasswordResetRequested: 'ev_reset_requested',
  ParentPasswordResetCompleted: 'ev_reset_completed',
  ParentEmailVerified: 'ev_email_verified',
  ParentDeviceRenamed: 'ev_device_renamed',
  ParentDeviceClaimed: 'ev_device_claimed',
  ParentDeviceLinked: 'ev_device_linked',
  ParentDeviceRevocationChanged: 'ev_revocation_changed',
  ParentDeviceStoryIntroSet: 'ev_story_intro_set',
  ParentBedtimeMusicSet: 'ev_bedtime_music_set',
  ParentStoryRequestSubmitted: 'ev_story_requested',
  ParentDeviceStoryPausesSet: 'ev_story_pauses_set',
  ParentDeviceVariantEndingsSet: 'ev_variant_endings_set',
};

/**
 * This parent's own actions. The feed is per ACTOR, not per toy: a toy shared
 * with another parent never leaks that parent's actions here.
 *
 * `devices` is passed in so toy names resolve on the first paint — the web
 * version printed raw ids for a while because it rendered before its device
 * list had loaded.
 */
export default function ActivityScreen({ devices, onBack, onLogout }: Props) {
  useLang();
  const [events, setEvents] = useState<AuditEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setEvents(await getActivity());
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

  function toyName(id: string | null): string | null {
    if (!id) return null;
    const d = devices.find((x) => x.deviceId === id);
    return d ? (d.deviceName || t('toy_word')) : id.slice(0, 8) + '…';
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.brand} />
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_toys')}</Text>
      </Pressable>
      <Text style={styles.title}>{t('activity_title')}</Text>

      <FlatList
        data={events}
        extraData={getLanguage()}
        keyExtractor={(e) => e.id}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
        ListHeaderComponent={error ? <Text style={styles.error}>{error}</Text> : null}
        ListEmptyComponent={<Text style={styles.empty}>{t('no_activity')}</Text>}
        renderItem={({ item }) => {
          const name = toyName(item.targetDeviceId);
          return (
            <View style={styles.row}>
              <View style={styles.rowTop}>
                <Text style={styles.label}>
                  {LABELS[item.eventType] ? t(LABELS[item.eventType]) : item.eventType}
                </Text>
                <Text style={styles.time}>{fmtTime(item.timestamp)}</Text>
              </View>
              {name ? <Text style={styles.target}>🧸 {name}</Text> : null}
            </View>
          );
        }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 12 },
  row: { borderBottomWidth: 1, borderBottomColor: theme.lineSoft, paddingVertical: 10 },
  rowTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  label: { color: theme.ink, fontWeight: '600', flex: 1 },
  time: { color: theme.inkMuted, fontSize: 12 },
  target: { color: theme.inkMuted, fontSize: 13, marginTop: 3 },
  empty: { textAlign: 'center', color: theme.inkMuted, marginTop: 24 },
  error: { color: theme.danger, marginBottom: 8 },
});

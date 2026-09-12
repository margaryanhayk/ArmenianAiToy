import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import * as Sharing from 'expo-sharing';
import {
  ApiError,
  ConversationDetail,
  ConversationMessage,
  deleteConversation,
  errText,
  getConversation,
  UnauthorizedError,
} from '../api';
import { fetchAssistantAudio, fetchChildAudio, playLocalFile, stopPlayback, type FetchedAudio } from '../audio';
import { getLanguage, t } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

const LOCALE: Record<string, string> = { en: 'en-GB', ru: 'ru-RU', hy: 'hy-AM' };

type Props = {
  conversationId: string;
  onBack: () => void;
  onLogout: () => void;
};

function fmtTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  try {
    return d.toLocaleTimeString(LOCALE[getLanguage()] ?? 'en-GB');
  } catch {
    return d.toLocaleTimeString();
  }
}

export default function ConversationDetailScreen({ conversationId, onBack, onLogout }: Props) {
  useLang();
  const [detail, setDetail] = useState<ConversationDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setDetail(await getConversation(conversationId));
    } catch (err) {
      if (err instanceof UnauthorizedError) return onLogout();
      setError(errText(err, 'e_load'));
    }
  }, [conversationId, onLogout]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      await load();
      setLoading(false);
    })();
  }, [load]);

  // Leaving the conversation stops any clip still playing — matching the
  // web dashboard's own view-change behaviour (releaseObjectUrls on nav).
  useEffect(() => () => stopPlayback(), []);

  // Hard delete — messages cascade at the DB level, no recovery path. On
  // success, onBack() returns to the conversation list, which remounts and
  // reloads on its own (same as every other back-navigation in this app),
  // so the deleted row is simply gone rather than needing a manual refresh.
  function confirmDelete() {
    Alert.alert(
      t('confirm_delete_conversation_title'),
      t('confirm_delete_conversation_body'),
      [
        { text: t('cancel'), style: 'cancel' },
        {
          text: t('delete_word'),
          style: 'destructive',
          onPress: async () => {
            try {
              await deleteConversation(conversationId);
              onBack();
            } catch (err) {
              if (err instanceof UnauthorizedError) return onLogout();
              Alert.alert(errText(err));
            }
          },
        },
      ],
    );
  }

  return (
    <View style={styles.container}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_plain')}</Text>
      </Pressable>
      <Text style={styles.title}>{t('conversation_title')}</Text>

      {loading ? (
        <ActivityIndicator size="large" color={theme.brand} style={{ marginTop: 40 }} />
      ) : error ? (
        <Text style={styles.error}>{error}</Text>
      ) : (
        <FlatList
          data={detail?.messages ?? []}
          extraData={getLanguage()}
          keyExtractor={(m) => m.id}
          renderItem={({ item }) => <Bubble message={item} />}
          ListEmptyComponent={<Text style={styles.empty}>{t('no_messages')}</Text>}
          // Below the transcript, not opposite Back — that top-right slot is
          // where iOS/Android put Done/Save, and an irreversible delete there
          // is a muscle-memory trap (ux-ui-designer, N7). Only offered once
          // a conversation actually loaded, same as before.
          ListFooterComponent={
            detail ? (
              <Pressable style={styles.deleteBtn} onPress={confirmDelete} accessibilityRole="button">
                <Text style={styles.deleteLink}>{t('delete_conversation')}</Text>
              </Pressable>
            ) : null
          }
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
      <Text style={styles.who}>{isChild ? t('who_child') : t('who_toy')}</Text>
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
        {flagged ? '  ·  ' + t('flagged_tag') : ''}
      </Text>
      {/* C2.1 — the wire shape guarantees audioAvailable is true ONLY for
          assistant messages with a stored clip; the role check is
          belt-and-suspenders, same posture as parent.html. */}
      {message.audioAvailable && !isChild ? (
        <AudioRow fetchAudio={() => fetchAssistantAudio(message.id)} allowSave={false} align="flex-start" />
      ) : null}
      {/* C2.2 (2026-09-11) — mirror image for the child's own recording. */}
      {message.childAudioAvailable && isChild ? (
        <AudioRow fetchAudio={() => fetchChildAudio(message.id)} allowSave align="flex-end" />
      ) : null}
    </View>
  );
}

type AudioRowState = 'idle' | 'loading' | 'playing' | 'notKept';

/**
 * ▶ Listen (+ ⬇ Save recording for the child's own audio), mirroring
 * parent.html's buildListenAffordance / buildDownloadRecordingAffordance.
 * Listen first: that is the parent's actual need, and it is offered for
 * both directions now, not just the child's. Save appears once the clip is
 * in hand, sharing the same fetched bytes rather than a second request.
 */
function AudioRow({
  fetchAudio,
  allowSave,
  align,
}: {
  fetchAudio: () => Promise<FetchedAudio>;
  allowSave: boolean;
  align: 'flex-start' | 'flex-end';
}) {
  const [state, setState] = useState<AudioRowState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [clip, setClip] = useState<FetchedAudio | null>(null);

  async function onListen() {
    if (state === 'loading') return;
    if (state === 'playing') {
      stopPlayback();
      setState('idle');
      return;
    }
    setError(null);
    setState('loading');
    try {
      const audio = clip ?? (await fetchAudio());
      setClip(audio);
      setState('playing');
      playLocalFile(
        audio.uri,
        () => setState('idle'),
        () => {
          setState('idle');
          setError(t('e_generic'));
        },
      );
    } catch (e) {
      if (e instanceof UnauthorizedError) {
        setState('idle');
        return;
      }
      // recording_not_kept is calm information, not a failure — the button
      // stays permanently spent rather than inviting a retry that cannot
      // succeed, same as parent.html's btn.disabled = true on this path.
      if (e instanceof ApiError && e.key === 'recording_not_kept') {
        setState('notKept');
        return;
      }
      setState('idle');
      setError(errText(e, 'e_generic'));
    }
  }

  async function onSave() {
    if (!clip) return;
    try {
      if (!(await Sharing.isAvailableAsync())) {
        setError(t('e_save_unavailable'));
        return;
      }
      await Sharing.shareAsync(clip.uri, { mimeType: clip.contentType || undefined });
    } catch {
      // A cancelled share sheet is not an error worth a status line — the
      // parent already has the clip playing above.
    }
  }

  return (
    <View style={[styles.audioRow, { justifyContent: align }]}>
      <Pressable
        style={styles.audioBtn}
        onPress={onListen}
        disabled={state === 'loading' || state === 'notKept'}
      >
        <Text style={styles.audioBtnText}>{state === 'playing' ? t('stop_btn') : t('listen_btn')}</Text>
      </Pressable>
      {allowSave && clip ? (
        <Pressable style={styles.audioBtn} onPress={onSave}>
          <Text style={styles.audioBtnText}>{t('save_recording_btn')}</Text>
        </Pressable>
      ) : null}
      {state === 'loading' ? <Text style={styles.audioStatus}>{t('audio_loading')}</Text> : null}
      {state === 'playing' ? <Text style={styles.audioStatus}>{t('audio_playing')}</Text> : null}
      {error && state !== 'notKept' ? <Text style={styles.audioStatusErr}>{error}</Text> : null}
      {state === 'notKept' ? <Text style={styles.audioStatus}>{t('recording_not_kept')}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  deleteBtn: {
    alignSelf: 'center',
    minHeight: 44,
    justifyContent: 'center',
    paddingVertical: 12,
    paddingHorizontal: 16,
    marginTop: 20,
    marginBottom: 8,
  },
  deleteLink: { color: theme.dangerDeep, fontSize: 13, fontWeight: '600' },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 12 },
  error: { color: theme.danger, marginTop: 16 },
  empty: { textAlign: 'center', color: theme.inkHint, marginTop: 24 },
  bubbleWrap: { marginBottom: 14, maxWidth: '85%' },
  wrapLeft: { alignSelf: 'flex-start' },
  wrapRight: { alignSelf: 'flex-end' },
  who: { fontSize: 12, color: theme.inkHint, marginBottom: 2 },
  bubble: { borderRadius: 12, padding: 12 },
  child: { backgroundColor: theme.surfaceTint },
  areg: { backgroundColor: theme.surfaceSunken },
  flaggedBubble: { borderWidth: 1, borderColor: theme.danger },
  childText: { color: theme.brand, fontSize: 15 },
  aregText: { color: theme.ink, fontSize: 15 },
  time: { fontSize: 11, color: theme.inkHint, marginTop: 3 },
  audioRow: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginTop: 6 },
  audioBtn: {
    borderWidth: 1,
    borderColor: theme.line,
    borderRadius: 8,
    paddingVertical: 10,
    paddingHorizontal: 12,
    minHeight: 44,
    justifyContent: 'center',
    backgroundColor: theme.surface,
  },
  audioBtnText: { color: theme.brand, fontWeight: '600', fontSize: 13 },
  audioStatus: { fontSize: 12, color: theme.inkMuted },
  audioStatusErr: { fontSize: 12, color: theme.danger },
});

// Conversation-message audio (C2.1 assistant replay, C2.2 child recording
// download), mirroring wwwroot/parent.html's buildListenAffordance /
// buildDownloadRecordingAffordance — same two endpoints, same status
// branching, same "Listen first, Save second" order for the child's own
// recording.
//
// The web version streams straight into a Blob URL and an <audio> element.
// React Native has neither, so this fetches the bytes once, writes them to
// a cache file, and plays that local file with expo-audio — which also
// means the same fetched bytes are what "Save recording" hands to the
// share sheet, with no second network round trip.
import * as FileSystem from 'expo-file-system/legacy';
import { createAudioPlayer, type AudioPlayer } from 'expo-audio';
import { getToken } from './auth';
import { API_BASE_URL } from './config';
import { ApiError, UnauthorizedError } from './api';
import type { Key } from './i18n';

export type FetchedAudio = { uri: string; contentType: string };

async function fetchAudioToFile(path: string, notFoundKey: Key): Promise<FetchedAudio> {
  const token = await getToken();
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 404) throw new ApiError(notFoundKey);
  if (!res.ok) throw new ApiError('e_generic');

  const contentType = res.headers.get('Content-Type') || '';
  // Only shape LocalDiskAudioBlobStore.ReadAsync can ever report — same
  // whitelist as the backend's own MIME check.
  const ext = contentType.includes('mpeg') ? 'mp3' : 'wav';

  const blob = await res.blob();
  const base64: string = await new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new ApiError('e_generic'));
    reader.onload = () => {
      const result = String(reader.result ?? '');
      const comma = result.indexOf(',');
      resolve(comma >= 0 ? result.slice(comma + 1) : '');
    };
    reader.readAsDataURL(blob);
  });

  const dir = FileSystem.cacheDirectory ?? '';
  const uri = `${dir}areg-audio-${Date.now()}-${Math.random().toString(36).slice(2)}.${ext}`;
  await FileSystem.writeAsStringAsync(uri, base64, { encoding: FileSystem.EncodingType.Base64 });
  return { uri, contentType };
}

/** GET /api/parents/messages/{id}/audio — the assistant's spoken reply (C2.1). */
export function fetchAssistantAudio(messageId: string): Promise<FetchedAudio> {
  return fetchAudioToFile(
    `/api/parents/messages/${encodeURIComponent(messageId)}/audio`,
    'e_audio_unavailable',
  );
}

/** GET /api/parents/messages/{id}/child-audio — the child's own recording (C2.2). */
export function fetchChildAudio(messageId: string): Promise<FetchedAudio> {
  return fetchAudioToFile(
    `/api/parents/messages/${encodeURIComponent(messageId)}/child-audio`,
    'recording_not_kept',
  );
}

// ---- one clip plays at a time, mirrors parent.html's stopOtherPreviews ----
let current: AudioPlayer | null = null;

export function stopPlayback(): void {
  if (current) {
    try {
      current.pause();
    } catch {
      // already gone
    }
    try {
      current.remove();
    } catch {
      // already gone
    }
    current = null;
  }
}

/**
 * Plays a local file (as produced by fetchAssistantAudio/fetchChildAudio),
 * stopping whatever else is playing first. Calls back once, either when
 * playback finishes naturally or when the player reports an error — never
 * both, so a caller's busy/disabled state is never stuck.
 */
export function playLocalFile(uri: string, onFinish: () => void, onError: () => void): void {
  stopPlayback();
  const player = createAudioPlayer({ uri });
  current = player;
  let settled = false;
  const sub = player.addListener('playbackStatusUpdate', (status) => {
    if (settled) return;
    if (status.error) {
      settled = true;
      sub.remove();
      if (current === player) current = null;
      onError();
      return;
    }
    if (status.didJustFinish) {
      settled = true;
      sub.remove();
      if (current === player) current = null;
      onFinish();
    }
  });
  player.play();
}

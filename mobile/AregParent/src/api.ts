// Typed client for the parent-facing backend API. Every endpoint here was
// verified live end-to-end during the bench session (see PLATFORM-ARCHITECTURE.txt).
import { API_BASE_URL } from './config';
import { getToken } from './auth';
import { Key, t } from './i18n';

/** Thrown on a 401 so screens can route back to login. */
export class UnauthorizedError extends Error {
  constructor() {
    super('Session expired');
    this.name = 'UnauthorizedError';
  }
}

/**
 * A failure the parent will be shown. It carries a translation key rather
 * than an English sentence, because whatever this layer throws ends up on
 * screen verbatim — and the app is used in three languages.
 */
export class ApiError extends Error {
  readonly key: Key;
  constructor(key: Key) {
    super(key);
    this.name = 'ApiError';
    this.key = key;
  }
}

/** Turn anything thrown into one sentence in the parent's language. */
export function errText(err: unknown, fallback: Key = 'e_generic'): string {
  return err instanceof ApiError ? t(err.key) : t(fallback);
}

export type LinkedDeviceChild = {
  childId: string;
  name: string;
  age: number | null;
};

export type LinkedDevice = {
  deviceId: string;
  deviceName: string;
  lastSeenAt: string;
  isOnline: boolean;
  isRevoked: boolean;
  isPaused: boolean;
  storyEnabled: boolean;
  gameEnabled: boolean;
  riddleEnabled: boolean;
  curiosityEnabled: boolean;
  bedtimeStart: string | null; // "HH:mm:ss" or null
  bedtimeEnd: string | null;
  children: LinkedDeviceChild[];
  // Story-library freshness, decided by the backend. Optional so the app
  // keeps working against a server older than the content-report slice.
  // "unknown" means the TOY has not reported (older firmware) — not a fault.
  contentHealth?: 'up_to_date' | 'syncing' | 'stale' | 'offline' | 'unknown';
  storiesOnToy?: number;
  storiesAvailable?: number;
};

export type ModeFlags = {
  story: boolean;
  game: boolean;
  riddle: boolean;
  curiosity: boolean;
};

async function authHeader(): Promise<Record<string, string>> {
  const token = await getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function url(path: string): string {
  return `${API_BASE_URL}${path}`;
}

/** POST /api/parents/login → JWT. Distinguishes network vs 401 vs other. */
export async function login(email: string, password: string): Promise<string> {
  let res: Response;
  try {
    res = await fetch(url('/api/parents/login'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new ApiError('e_bad_credentials');
  if (!res.ok) throw new ApiError('e_generic');
  const data = (await res.json()) as { token: string };
  return data.token;
}

/** POST /api/parents/register. Anti-enumeration: always 201 on a valid shape. */
export async function register(email: string, password: string): Promise<void> {
  const res = await fetch(url('/api/parents/register'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, acceptedTerms: true }),
  });
  if (!res.ok) {
    throw new ApiError(res.status === 400 ? 'e_register_shape' : 'e_generic');
  }
}

/** GET /api/parents/devices/details → linked devices with presence + state. */
export async function getDevices(): Promise<LinkedDevice[]> {
  const res = await fetch(url('/api/parents/devices/details'), {
    headers: await authHeader(),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new ApiError('e_load');
  const data = (await res.json()) as { devices?: LinkedDevice[] };
  return data.devices ?? [];
}

/** POST /api/parents/devices/claim. Uniform failure (no existence leak). */
export async function claimDevice(deviceId: string, claimCode: string): Promise<void> {
  const res = await fetch(url('/api/parents/devices/claim'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ deviceId, claimCode }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 429) {
    throw new ApiError('e_too_many');
  }
  if (!res.ok) {
    throw new ApiError('e_bad_code');
  }
}

/**
 * POST /api/children. Creates the child profile the toy speaks to.
 *
 * Not cosmetic: without one, ChildService.BuildChildContext has no name, no
 * age and no GENDER for the prompt - and Armenian grammar needs the gender.
 * A freshly paired toy talks to a stranger until this is filled in, and the
 * phone had no way to do it at all.
 *
 * `gender` is the enum ORDINAL (Boy=0, Girl=1); the API has no string-enum
 * converter, so posting "Boy" is a 400.
 */
export async function addChild(
  deviceId: string,
  name: string,
  gender: 0 | 1,
  birthYear: number | null,
): Promise<void> {
  const res = await fetch(url('/api/children'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ deviceId, name, gender, birthYear }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new ApiError('e_generic');
}

/**
 * POST /api/parents/devices/{id}/invite — a short code a SECOND parent can
 * type, instead of needing this toy's 36-character id plus the code printed
 * on its box.
 *
 * The code comes back ONCE and cannot be re-read: only its selector and a
 * hash of the rest are stored. A 404 covers "not yours", "revoked" and
 * "already has its two parents" alike, so the message must not guess which.
 */
export async function createInvite(deviceId: string): Promise<{ code: string; expiresAt: string }> {
  const res = await fetch(url(`/api/parents/devices/${encodeURIComponent(deviceId)}/invite`), {
    method: 'POST',
    headers: { Accept: 'application/json', ...(await authHeader()) },
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 429) throw new ApiError('e_too_many');
  if (!res.ok) throw new ApiError('e_invite_failed');
  return res.json();
}

/**
 * POST /api/parents/devices/redeem-invite. Takes ONLY the code - no device
 * id, which is the whole point of an invite. One uniform failure from the
 * server, so one message here.
 */
export async function redeemInvite(code: string): Promise<void> {
  const res = await fetch(url('/api/parents/devices/redeem-invite'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ code }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 429) throw new ApiError('e_too_many');
  if (!res.ok) throw new ApiError('e_invite_bad');
}

/** PUT /api/parents/devices/{id}/name. 1..60 chars. */
export async function renameDevice(deviceId: string, name: string): Promise<void> {
  const res = await fetch(url(`/api/parents/devices/${encodeURIComponent(deviceId)}/name`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ name }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 400) throw new ApiError('e_name_length');
  if (!res.ok) throw new ApiError('e_generic');
}

/** PUT /api/parents/devices/{id}/revoke. Kill-switch; reversible. */
export async function setRevoked(deviceId: string, revoked: boolean): Promise<void> {
  const res = await fetch(url(`/api/parents/devices/${encodeURIComponent(deviceId)}/revoke`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ revoked }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new ApiError('e_generic');
}

async function getJson<T>(path: string): Promise<T> {
  let res: Response;
  try {
    res = await fetch(url(path), { headers: await authHeader() });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new ApiError('e_generic');
  return (await res.json()) as T;
}

export type TodaySummary = {
  conversationsCount: number;
  messagesCount: number;
  flaggedMessagesCount: number;
  assistantMessagesWithAudio: number;
};

export type ConversationSummary = {
  id: string;
  startedAt: string;
  messageCount: number;
  flaggedMessageCount: number;
  firstUserSnippet: string | null;
  lastAssistantSnippet: string | null;
};

export type ConversationMessage = {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: string;
  safetyFlag: number;
  audioAvailable: boolean;
};

export type ConversationDetail = {
  id: string;
  startedAt: string;
  messageCount: number;
  messages: ConversationMessage[];
};

/** GET /api/conversations/today-summary?deviceId= — exact per-message counts. */
export function getTodaySummary(deviceId: string): Promise<TodaySummary> {
  return getJson<TodaySummary>(`/api/conversations/today-summary?deviceId=${encodeURIComponent(deviceId)}`);
}

/** GET /api/conversations/summary?deviceId= — newest-first conversation rows. */
export async function getConversations(deviceId: string): Promise<ConversationSummary[]> {
  const data = await getJson<{ conversations?: ConversationSummary[] }>(
    `/api/conversations/summary?deviceId=${encodeURIComponent(deviceId)}&limit=50&offset=0`,
  );
  return data.conversations ?? [];
}

/** GET /api/conversations/{id} — full message transcript. */
export async function getConversation(conversationId: string): Promise<ConversationDetail> {
  const data = await getJson<{ conversation: ConversationDetail }>(
    `/api/conversations/${encodeURIComponent(conversationId)}`,
  );
  return data.conversation;
}

// ── Story plays, library, music, requests, activity ────────────────────
// The screens the web dashboard already had and this app did not, so a
// parent on a phone saw less than a parent on a laptop.

export type StoryPlay = {
  storyId: string;
  finished: boolean;
  source: string;
  playedAtUtc: string;
  timeIsApproximate: boolean;
};

export type StoryPlayTotal = { storyId: string; count: number; finishedCount: number };

export type StoryPlaysResult = {
  plays: StoryPlay[];
  totals: StoryPlayTotal[];
  total: number;
};

export type ReflectionAnswer = {
  storyId: string;
  questionIndex: number;
  answerText: string;
  safetyFlag: string;
  createdAtUtc: string;
};

export type LibraryStory = {
  storyId: string;
  title: string;
  author: string | null;
  goal: string | null;
  lesson: string | null;
  bedtimeSafe: boolean | null;
  minAge: number | null;
  maxAge: number | null;
  reflectionQuestions: string[];
  reflectionConclusions: string[] | null;
  listenCount: number;
  finishedCount: number;
  previewUrl: string | null;
  /**
   * Parent-facing text in the parent's language, keyed by language code.
   * The story itself stays Armenian — it is what a child hears — but the
   * DESCRIPTION is written for the parent, and an English-speaking one was
   * being shown «Զգուշություն օտարների հանդեպ» under an English label.
   *
   * Absent, or absent for one field, is normal: falls back to the Armenian
   * per FIELD rather than per story, so a half-written translation costs one
   * line instead of the whole card.
   */
  translations?: Record<string, { title?: string | null; goal?: string | null; lesson?: string | null }> | null;
};

export type MusicTrack = { trackId: string; title: string; previewUrl: string | null };

export type StoryRequest = {
  id: string;
  type: string;
  text: string;
  hasPhoto: boolean;
  status: string;
  createdAtUtc: string;
};

export type AuditEntry = {
  id: string;
  timestamp: string;
  eventType: string;
  targetDeviceId: string | null;
  targetChildId: string | null;
  metadata: Record<string, unknown> | null;
};

/** GET /api/parents/devices/{id}/story-plays — what the child actually heard. */
export function getStoryPlays(deviceId: string, limit = 100): Promise<StoryPlaysResult> {
  return getJson<StoryPlaysResult>(
    `/api/parents/devices/${encodeURIComponent(deviceId)}/story-plays?limit=${limit}&offset=0`,
  );
}

/** GET /api/parents/devices/{id}/reflection-answers — the child's own words. */
export async function getReflectionAnswers(
  deviceId: string,
  limit = 100,
): Promise<{ answers: ReflectionAnswer[]; total: number }> {
  const d = await getJson<{ answers?: ReflectionAnswer[]; total?: number }>(
    `/api/parents/devices/${encodeURIComponent(deviceId)}/reflection-answers?limit=${limit}&offset=0`,
  );
  return { answers: d.answers ?? [], total: d.total ?? (d.answers?.length ?? 0) };
}

/**
 * GET /api/parents/stories. `deviceId` scopes the listen counts to one toy;
 * without it they cover every toy the parent has, which is a different
 * number and has to be labelled as such on screen.
 */
export async function getStoryLibrary(deviceId?: string): Promise<LibraryStory[]> {
  const q = deviceId ? `?deviceId=${encodeURIComponent(deviceId)}` : '';
  const d = await getJson<{ stories?: LibraryStory[] }>(`/api/parents/stories${q}`);
  return d.stories ?? [];
}

/** GET /api/parents/music — bedtime tracks, empty until any are published. */
export async function getMusic(): Promise<MusicTrack[]> {
  const d = await getJson<{ tracks?: MusicTrack[] }>('/api/parents/music');
  return d.tracks ?? [];
}

/** GET /api/parents/story-requests — the parent's own custom-story asks. */
export async function getStoryRequests(): Promise<StoryRequest[]> {
  const d = await getJson<{ requests?: StoryRequest[] }>('/api/parents/story-requests');
  return d.requests ?? [];
}

/** POST /api/parents/story-requests — multipart; photo is optional. */
export async function submitStoryRequest(text: string): Promise<void> {
  const form = new FormData();
  form.append('text', text);
  let res: Response;
  try {
    res = await fetch(url('/api/parents/story-requests'), {
      method: 'POST',
      headers: await authHeader(),   // no Content-Type: fetch sets the boundary
      body: form,
    });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status !== 201) throw new ApiError('e_generic');
}

/** GET /api/parents/audit — this parent's own actions, newest first. */
export async function getActivity(limit = 50): Promise<AuditEntry[]> {
  const d = await getJson<{ events?: AuditEntry[] }>(
    `/api/parents/audit?limit=${limit}&offset=0`,
  );
  return d.events ?? [];
}

async function mutate(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<void> {
  let res: Response;
  try {
    res = await fetch(url(path), {
      method,
      headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new ApiError('e_generic');
}

/** POST /api/parents/devices/{id}/pause | /resume — instant quiet override. */
export function setPaused(deviceId: string, paused: boolean): Promise<void> {
  const action = paused ? 'pause' : 'resume';
  return mutate(`/api/parents/devices/${encodeURIComponent(deviceId)}/${action}`, 'POST');
}

/** PUT /api/parents/devices/{id}/mode-flags — enable/disable the four modes. */
export function setModeFlags(deviceId: string, flags: ModeFlags): Promise<void> {
  return mutate(`/api/parents/devices/${encodeURIComponent(deviceId)}/mode-flags`, 'PUT', flags);
}

/** PUT /api/parents/devices/{id}/bedtime-window — "HH:mm:ss" or null to clear. */
export function setBedtime(
  deviceId: string,
  start: string | null,
  end: string | null,
): Promise<void> {
  return mutate(`/api/parents/devices/${encodeURIComponent(deviceId)}/bedtime-window`, 'PUT', {
    start,
    end,
  });
}

// ----- Safety (flagged) -----

export type FlaggedMessage = {
  id: string;
  conversationId: string;
  conversationStartedAt: string;
  role: string;
  content: string;
  timestamp: string;
  safetyFlag: number;
};

/** GET /api/conversations/flagged?deviceId= — non-clean messages, newest first. */
export async function getFlagged(deviceId: string): Promise<FlaggedMessage[]> {
  const data = await getJson<{ flaggedMessages?: FlaggedMessage[] }>(
    `/api/conversations/flagged?deviceId=${encodeURIComponent(deviceId)}&limit=50&offset=0`,
  );
  return data.flaggedMessages ?? [];
}

// ----- Account -----

export type Me = { email: string; emailVerifiedAt: string | null };

/** GET /api/parents/me — profile + verification status. */
export function getMe(): Promise<Me> {
  return getJson<Me>('/api/parents/me');
}

/** POST /api/parents/verify-request — anti-enum 202 regardless. */
export function requestVerification(email: string): Promise<void> {
  return mutate('/api/parents/verify-request', 'POST', { email });
}

/** GET /api/parents/export — returns the full export document as text. */
export async function fetchExport(): Promise<string> {
  let res: Response;
  try {
    res = await fetch(url('/api/parents/export'), { headers: await authHeader() });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 429) throw new ApiError('e_export_wait');
  if (!res.ok) throw new ApiError('e_generic');
  return res.text();
}

/** POST /api/parents/password — change password. */
export async function changePassword(current: string, next: string): Promise<void> {
  let res: Response;
  try {
    res = await fetch(url('/api/parents/password'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
      body: JSON.stringify({ currentPassword: current, newPassword: next }),
    });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 400) {
    throw new ApiError('e_password_rules');
  }
  if (!res.ok) throw new ApiError('e_generic');
}

/** DELETE /api/parents/account — permanent; requires the current password. */
export async function deleteAccount(currentPassword: string): Promise<void> {
  let res: Response;
  try {
    res = await fetch(url('/api/parents/account'), {
      method: 'DELETE',
      headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
      body: JSON.stringify({ currentPassword }),
    });
  } catch {
    throw new ApiError('e_unreachable');
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 400) throw new ApiError('e_password_wrong');
  if (!res.ok) throw new ApiError('e_generic');
}

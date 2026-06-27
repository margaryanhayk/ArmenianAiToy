// Typed client for the parent-facing backend API. Every endpoint here was
// verified live end-to-end during the bench session (see PLATFORM-ARCHITECTURE.txt).
import { API_BASE_URL } from './config';
import { getToken } from './auth';

/** Thrown on a 401 so screens can route back to login. */
export class UnauthorizedError extends Error {
  constructor() {
    super('Session expired');
    this.name = 'UnauthorizedError';
  }
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
    throw new Error(`Can't reach the server at ${API_BASE_URL}. Check Wi-Fi.`);
  }
  if (res.status === 401) throw new Error('That email or password is incorrect.');
  if (!res.ok) throw new Error(`Server said HTTP ${res.status} (not a password problem).`);
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
    const msg =
      res.status === 400
        ? 'Please use a valid email and a password of at least 8 characters.'
        : `Registration failed (HTTP ${res.status}).`;
    throw new Error(msg);
  }
}

/** GET /api/parents/devices/details → linked devices with presence + state. */
export async function getDevices(): Promise<LinkedDevice[]> {
  const res = await fetch(url('/api/parents/devices/details'), {
    headers: await authHeader(),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new Error(`Could not load devices (HTTP ${res.status}).`);
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
    throw new Error('Too many tries. Please wait a moment and retry.');
  }
  if (!res.ok) {
    throw new Error("That code didn't work. Check the code on your toy and try again.");
  }
}

/** PUT /api/parents/devices/{id}/name. 1..60 chars. */
export async function renameDevice(deviceId: string, name: string): Promise<void> {
  const res = await fetch(url(`/api/parents/devices/${encodeURIComponent(deviceId)}/name`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ name }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 400) throw new Error('Name must be 1–60 characters.');
  if (!res.ok) throw new Error(`Rename failed (HTTP ${res.status}).`);
}

/** PUT /api/parents/devices/{id}/revoke. Kill-switch; reversible. */
export async function setRevoked(deviceId: string, revoked: boolean): Promise<void> {
  const res = await fetch(url(`/api/parents/devices/${encodeURIComponent(deviceId)}/revoke`), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
    body: JSON.stringify({ revoked }),
  });
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new Error(`${revoked ? 'Revoke' : 'Restore'} failed (HTTP ${res.status}).`);
}

async function getJson<T>(path: string): Promise<T> {
  let res: Response;
  try {
    res = await fetch(url(path), { headers: await authHeader() });
  } catch {
    throw new Error(`Can't reach the server at ${API_BASE_URL}.`);
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new Error(`Request failed (HTTP ${res.status}).`);
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

async function mutate(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<void> {
  let res: Response;
  try {
    res = await fetch(url(path), {
      method,
      headers: { 'Content-Type': 'application/json', ...(await authHeader()) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new Error(`Can't reach the server at ${API_BASE_URL}.`);
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (!res.ok) throw new Error(`Request failed (HTTP ${res.status}).`);
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
    throw new Error(`Can't reach the server at ${API_BASE_URL}.`);
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 429) throw new Error('Please wait a minute before exporting again.');
  if (!res.ok) throw new Error(`Export failed (HTTP ${res.status}).`);
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
    throw new Error(`Can't reach the server at ${API_BASE_URL}.`);
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 400) {
    throw new Error('Current password is wrong, or the new one is under 8 characters / unchanged.');
  }
  if (!res.ok) throw new Error(`Change failed (HTTP ${res.status}).`);
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
    throw new Error(`Can't reach the server at ${API_BASE_URL}.`);
  }
  if (res.status === 401) throw new UnauthorizedError();
  if (res.status === 400) throw new Error('Password is incorrect.');
  if (!res.ok) throw new Error(`Delete failed (HTTP ${res.status}).`);
}

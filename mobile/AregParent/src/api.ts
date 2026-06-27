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
  children: LinkedDeviceChild[];
};

async function authHeader(): Promise<Record<string, string>> {
  const token = await getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function url(path: string): string {
  return `${API_BASE_URL}${path}`;
}

/** POST /api/parents/login → JWT. Throws on bad credentials. */
export async function login(email: string, password: string): Promise<string> {
  const res = await fetch(url('/api/parents/login'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (!res.ok) {
    throw new Error('That email or password is incorrect.');
  }
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

// Parent JWT storage. On native it uses the OS secure store (Keychain /
// Keystore), since it is a bearer credential. On web (preview in a browser),
// SecureStore is unavailable, so it falls back to localStorage — fine for a
// dev/preview run.
import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

const TOKEN_KEY = 'areg.parent.jwt';
const isWeb = Platform.OS === 'web';
const webStore: Storage | undefined = (globalThis as { localStorage?: Storage })
  .localStorage;

export async function saveToken(token: string): Promise<void> {
  if (isWeb) {
    webStore?.setItem(TOKEN_KEY, token);
    return;
  }
  await SecureStore.setItemAsync(TOKEN_KEY, token);
}

export async function getToken(): Promise<string | null> {
  if (isWeb) {
    return webStore?.getItem(TOKEN_KEY) ?? null;
  }
  return SecureStore.getItemAsync(TOKEN_KEY);
}

export async function clearToken(): Promise<void> {
  if (isWeb) {
    webStore?.removeItem(TOKEN_KEY);
    return;
  }
  await SecureStore.deleteItemAsync(TOKEN_KEY);
}

// Parent JWT storage. Kept in the OS secure store (Keychain / Keystore), not
// AsyncStorage, because it is a bearer credential.
import * as SecureStore from 'expo-secure-store';

const TOKEN_KEY = 'areg.parent.jwt';

export async function saveToken(token: string): Promise<void> {
  await SecureStore.setItemAsync(TOKEN_KEY, token);
}

export async function getToken(): Promise<string | null> {
  return SecureStore.getItemAsync(TOKEN_KEY);
}

export async function clearToken(): Promise<void> {
  await SecureStore.deleteItemAsync(TOKEN_KEY);
}

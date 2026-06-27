// Backend base URL the app talks to.
//
// Dev default is the bench laptop's LAN IP (verified working on the board).
// Override without code changes by setting EXPO_PUBLIC_API_BASE_URL (Expo
// inlines EXPO_PUBLIC_* at build time), e.g. the public HTTPS URL in prod.
export const API_BASE_URL: string =
  process.env.EXPO_PUBLIC_API_BASE_URL ?? 'http://192.168.1.4:5000';

// Backend base URL the app talks to.
//
// The default is the LIVE server, so a build that somehow ships without
// EXPO_PUBLIC_API_BASE_URL still reaches a real backend. The previous
// default was a bench laptop's LAN IP, which is the wrong failure mode
// twice over: that address is DHCP-assigned (it has already moved once)
// and it is unreachable from any phone outside the house, so the app
// would just hang with no clue why.
//
// Bench work overrides it without a code change — Expo inlines
// EXPO_PUBLIC_* at build time, and the `development` / `preview` EAS
// profiles in eas.json already set the LAN address explicitly.
export const API_BASE_URL: string =
  process.env.EXPO_PUBLIC_API_BASE_URL ??
  'https://armenianaitoy-production.up.railway.app';

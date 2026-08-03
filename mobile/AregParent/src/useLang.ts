import { useEffect, useState } from 'react';
import { getLanguage, Lang, subscribe } from './i18n';

/**
 * Subscribe a screen to language changes.
 *
 * `t()` reads module state, so without this a screen would keep whatever it
 * rendered first. Any component that shows text calls this once.
 */
export function useLang(): Lang {
  const [lang, setLang] = useState<Lang>(getLanguage());
  useEffect(() => subscribe(() => setLang(getLanguage())), []);
  return lang;
}

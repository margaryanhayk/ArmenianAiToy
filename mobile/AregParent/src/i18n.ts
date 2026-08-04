// Three UI languages: English, Русский, Հայերեն.
//
// Deliberately dependency-free. Adding an i18n library (and an async-storage
// library to persist the choice) for ~100 strings would be a worse trade than
// ~60 lines here, and it keeps the app's dependency list honest.
//
// The wording is kept in step with the web dashboard (wwwroot/parent.html) on
// purpose: the same family may use both, and the Armenian there has been
// through a linguistic review. Where a concept exists in both, the Armenian
// string is the reviewed one.
import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

export type Lang = 'en' | 'ru' | 'hy';
export const LANGS: Lang[] = ['en', 'ru', 'hy'];
export const LANG_NAMES: Record<Lang, string> = {
  en: 'English',
  ru: 'Русский',
  hy: 'Հայերեն',
};

const LANG_KEY = 'areg.parent.lang';
const isWeb = Platform.OS === 'web';
const webStore: Storage | undefined = (globalThis as { localStorage?: Storage }).localStorage;

let current: Lang = 'en';
const listeners = new Set<() => void>();

/** Read the saved choice. Call once at boot, before the first render. */
export async function loadLanguage(): Promise<Lang> {
  let saved: string | null = null;
  try {
    saved = isWeb
      ? webStore?.getItem(LANG_KEY) ?? null
      : await SecureStore.getItemAsync(LANG_KEY);
  } catch {
    // A storage failure must never stop the app booting.
  }
  current = (LANGS as string[]).includes(saved ?? '') ? (saved as Lang) : 'en';
  return current;
}

export function getLanguage(): Lang {
  return current;
}

export async function setLanguage(lang: Lang): Promise<void> {
  current = lang;
  listeners.forEach((l) => l());
  try {
    if (isWeb) webStore?.setItem(LANG_KEY, lang);
    else await SecureStore.setItemAsync(LANG_KEY, lang);
  } catch {
    // Not persisting is survivable; the session still switches.
  }
}

export function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

type Entry = { en: string; ru: string; hy: string };

const D = {
  // ---------- shared ----------
  back_toys: { en: '‹ Toys', ru: '‹ Игрушки', hy: '‹ Խաղալիքներ' },
  back_activity: { en: '‹ Activity', ru: '‹ Активность', hy: '‹ Ակտիվություն' },
  back_plain: { en: '‹ Back', ru: '‹ Назад', hy: '‹ Հետ' },
  save: { en: 'Save', ru: 'Сохранить', hy: 'Պահպանել' },
  cancel: { en: 'Cancel', ru: 'Отмена', hy: 'Չեղարկել' },
  delete_word: { en: 'Delete', ru: 'Удалить', hy: 'Ջնջել' },
  language: { en: 'Language', ru: 'Язык', hy: 'Լեզու' },
  // Label/value separator. Armenian uses ՝, not a Latin colon, so this
  // cannot live in the layout.
  label_sep: { en: ': ', ru: ': ', hy: '՝ ' },
  show_password: { en: 'Show password', ru: 'Показать пароль', hy: 'Ցույց տալ գաղտնաբառը' },
  hide_password: { en: 'Hide password', ru: 'Скрыть пароль', hy: 'Թաքցնել գաղտնաբառը' },
  toy_word: { en: 'Toy', ru: 'Игрушка', hy: 'Խաղալիք' },
  online: { en: 'Online', ru: 'В сети', hy: 'Առցանց' },
  offline: { en: 'Offline', ru: 'Не в сети', hy: 'Անցանց' },
  revoked: { en: 'Access off', ru: 'Доступ отключён', hy: 'Մուտքը կասեցված է' },
  paused: { en: 'Paused', ru: 'На паузе', hy: 'Դադարեցված' },

  // ---------- errors ----------
  e_generic: {
    en: 'Something went wrong. Please try again.',
    ru: 'Что-то пошло не так. Попробуйте ещё раз.',
    hy: 'Ինչ-որ բան չստացվեց։ Խնդրում ենք կրկին փորձել։',
  },
  e_load: {
    en: "Couldn't load this. Pull down to try again.",
    ru: 'Не удалось загрузить. Потяните вниз, чтобы повторить.',
    hy: 'Չհաջողվեց բեռնել։ Քաշեք ներքև՝ կրկին փորձելու համար։',
  },
  e_unreachable: {
    en: "Can't reach the server. Check your internet.",
    ru: 'Сервер недоступен. Проверьте интернет.',
    hy: 'Սերվերն անհասանելի է։ Ստուգեք ինտերնետը։',
  },
  e_bad_credentials: {
    en: 'That email or password is incorrect.',
    ru: 'Неверная почта или пароль.',
    hy: 'Սխալ էլ. փոստ կամ գաղտնաբառ։',
  },
  e_too_many: {
    en: 'Too many tries. Please wait a minute and try again.',
    ru: 'Слишком много попыток. Подождите минуту и попробуйте снова.',
    hy: 'Չափից շատ փորձեր։ Սպասեք մեկ րոպե և կրկին փորձեք։',
  },
  e_bad_code: {
    en: "That code didn't work. Check the code on your toy's box and try again.",
    ru: 'Код не подошёл. Проверьте код на коробке игрушки и попробуйте снова.',
    hy: 'Այդ կոդը չընդունվեց։ Ստուգեք խաղալիքի տուփի կոդը և կրկին փորձեք։',
  },
  e_name_length: {
    en: 'The name can be 1–60 characters.',
    ru: 'Имя может быть от 1 до 60 символов.',
    hy: 'Անունը կարող է ունենալ 1–60 նիշ։',
  },
  e_export_wait: {
    en: 'You just downloaded a copy. Please try again in a minute.',
    ru: 'Вы только что скачали копию. Попробуйте через минуту.',
    hy: 'Դուք հենց նոր ներբեռնեցիք պատճենը։ Փորձեք մեկ րոպեից։',
  },
  e_password_rules: {
    en: 'Your current password is wrong, or the new one is under 8 characters or unchanged.',
    ru: 'Текущий пароль неверен, либо новый короче 8 символов или совпадает со старым.',
    hy: 'Ընթացիկ գաղտնաբառը սխալ է, կամ նորը 8 նիշից կարճ է, կամ նույնն է, ինչ նախորդը։',
  },
  e_password_wrong: {
    en: 'That password is incorrect.',
    ru: 'Неверный пароль.',
    hy: 'Գաղտնաբառը սխալ է։',
  },
  e_register_shape: {
    en: 'Please use a real email address and a password of at least 8 characters.',
    ru: 'Укажите настоящий адрес почты и пароль не короче 8 символов.',
    hy: 'Մուտքագրեք իրական էլ. փոստի հասցե և առնվազն 8 նիշանոց գաղտնաբառ։',
  },

  // ---------- sign in / sign up ----------
  login_subtitle: { en: 'Parent sign in', ru: 'Вход для родителей', hy: 'Ծնողի մուտք' },
  register_subtitle: {
    en: 'Create a parent account',
    ru: 'Создать родительский аккаунт',
    hy: 'Ստեղծել ծնողական հաշիվ',
  },
  ph_email: { en: 'Email', ru: 'Эл. почта', hy: 'Էլ. փոստ' },
  ph_password: { en: 'Password', ru: 'Пароль', hy: 'Գաղտնաբառ' },
  sign_in: { en: 'Sign in', ru: 'Войти', hy: 'Մուտք' },
  create_account: { en: 'Create account', ru: 'Создать аккаунт', hy: 'Ստեղծել հաշիվը' },
  to_register: {
    en: "Don't have an account? Create one",
    ru: 'Нет аккаунта? Создайте',
    hy: 'Հաշիվ չունե՞ք։ Ստեղծեք',
  },
  to_login: {
    en: 'Already have an account? Sign in',
    ru: 'Уже есть аккаунт? Войдите',
    hy: 'Արդեն հաշիվ ունե՞ք։ Մուտք գործեք',
  },
  e_email_password: {
    en: 'Please enter your email and password.',
    ru: 'Введите почту и пароль.',
    hy: 'Մուտքագրեք ձեր էլ. փոստը և գաղտնաբառը։',
  },
  account_created: {
    en: 'Your account is ready. Please log in.',
    ru: 'Аккаунт готов. Войдите, пожалуйста.',
    hy: 'Ձեր հաշիվը պատրաստ է։ Խնդրում ենք մուտք գործել։',
  },

  // ---------- toys ----------
  your_toys: { en: 'Your toys', ru: 'Ваши игрушки', hy: 'Ձեր խաղալիքները' },
  account: { en: 'Account', ru: 'Аккаунт', hy: 'Հաշիվ' },
  add_toy: { en: '＋ Add a toy', ru: '＋ Добавить игрушку', hy: '＋ Ավելացնել խաղալիք' },
  close_form: { en: '× Close', ru: '× Закрыть', hy: '× Փակել' },
  claim_help: {
    en: "Enter the pairing code from your toy's box or setup card.",
    ru: 'Введите код привязки с коробки или карточки игрушки.',
    hy: 'Մուտքագրեք կապակցման կոդը խաղալիքի տուփից կամ քարտից։',
  },
  ph_device_id: { en: 'Device ID', ru: 'ID устройства', hy: 'Սարքի ID' },
  ph_pairing_code: { en: 'Pairing code', ru: 'Код привязки', hy: 'Կապակցման կոդ' },
  pair_toy: { en: 'Pair toy', ru: 'Привязать', hy: 'Կապակցել' },
  e_pair_fields: {
    en: 'Please enter both the Device ID and the pairing code.',
    ru: 'Введите и ID устройства, и код привязки.',
    hy: 'Մուտքագրեք և՛ սարքի ID-ն, և՛ կապակցման կոդը։',
  },
  no_toys: {
    en: 'No toys yet. Tap “＋ Add a toy” to pair one.',
    ru: 'Игрушек пока нет. Нажмите «＋ Добавить игрушку».',
    hy: 'Խաղալիքներ դեռ չկան։ Սեղմեք «＋ Ավելացնել խաղալիք»։',
  },
  see_activity: { en: 'See activity →', ru: 'Активность →', hy: 'Ակտիվություն →' },
  open_settings: { en: 'Settings →', ru: 'Настройки →', hy: 'Կարգավորումներ →' },
  ph_toy_name: { en: 'Toy name', ru: 'Имя игрушки', hy: 'Խաղալիքի անուն' },
  revoke_access: { en: 'Revoke access', ru: 'Отозвать доступ', hy: 'Կասեցնել մուտքը' },
  restore_access: { en: 'Restore access', ru: 'Восстановить доступ', hy: 'Վերականգնել մուտքը' },
  confirm_revoke_title: { en: 'Revoke access?', ru: 'Отозвать доступ?', hy: 'Կասեցնե՞լ մուտքը' },
  confirm_revoke_body: {
    en: '{name} will stop working until it is set up again. Use this if it is lost or stolen. You can turn it back on here at any time.',
    ru: '«{name}» перестанет работать, пока её не настроят заново. Используйте это, если она потеряна или украдена. Включить обратно можно здесь в любой момент.',
    hy: '«{name}»-ը կդադարի աշխատել, մինչև նորից չկարգավորվի։ Օգտագործեք սա, եթե խաղալիքը կորել կամ գողացվել է։ Ցանկացած պահի կարող եք այստեղից վերականգնել մուտքը։',
  },
  this_toy: { en: 'This toy', ru: 'Эта игрушка', hy: 'Այս խաղալիքը' },
  rename_failed: { en: 'Rename failed', ru: 'Не удалось переименовать', hy: 'Չհաջողվեց վերանվանել' },
  child_with_age: { en: '{name} (age {n})', ru: '{name} ({n} г.)', hy: '{name} ({n} տարեկան)' },

  // ---------- account ----------
  email_label: { en: 'Email', ru: 'Эл. почта', hy: 'Էլ. փոստ' },
  email_verified: { en: '✓ Email confirmed', ru: '✓ Почта подтверждена', hy: '✓ Էլ. փոստը հաստատված է' },
  email_unverified: {
    en: 'Email not confirmed',
    ru: 'Почта не подтверждена',
    hy: 'Էլ. փոստը հաստատված չէ',
  },
  send_verification: {
    en: 'Send verification email',
    ru: 'Отправить письмо-подтверждение',
    hy: 'Ուղարկել հաստատման նամակ',
  },
  verification_sent: {
    en: 'If your email needs confirming, a link has been sent. Check your inbox.',
    ru: 'Если почту нужно подтвердить, ссылка отправлена. Проверьте почту.',
    hy: 'Եթե ձեր էլ. փոստը հաստատման կարիք ունի, հղումն ուղարկվել է։ Ստուգեք ձեր էլ. փոստը։',
  },
  your_data: { en: 'Your data', ru: 'Ваши данные', hy: 'Ձեր տվյալները' },
  export_hint: {
    en: 'Download a copy of everything we keep for your account.',
    ru: 'Скачайте копию всего, что мы храним для вашего аккаунта.',
    hy: 'Ներբեռնեք այն ամենի պատճենը, ինչ պահում ենք ձեր հաշվի մասին։',
  },
  export_btn: { en: 'Download my data', ru: 'Скачать мои данные', hy: 'Ներբեռնել իմ տվյալները' },
  export_done_web: {
    en: 'Downloaded as areg-export.json.',
    ru: 'Скачано как areg-export.json.',
    hy: 'Ներբեռնվեց որպես areg-export.json։',
  },
  export_ready_native: {
    en: 'Your data is ready ({n} characters). Downloading a file works in the web dashboard.',
    ru: 'Данные готовы ({n} симв.). Скачать файл можно в веб-панели.',
    hy: 'Ձեր տվյալները պատրաստ են ({n} նիշ)։ Ֆայլը ներբեռնել կարելի է վեբ-վահանակից։',
  },
  change_password_title: { en: 'Change password', ru: 'Сменить пароль', hy: 'Փոխել գաղտնաբառը' },
  ph_current_pw: { en: 'Current password', ru: 'Текущий пароль', hy: 'Ընթացիկ գաղտնաբառ' },
  ph_new_pw: {
    en: 'New password (at least 8 characters)',
    ru: 'Новый пароль (не менее 8 символов)',
    hy: 'Նոր գաղտնաբառ (առնվազն 8 նիշ)',
  },
  update_password: { en: 'Update password', ru: 'Обновить пароль', hy: 'Թարմացնել գաղտնաբառը' },
  password_changed: { en: 'Password updated.', ru: 'Пароль обновлён.', hy: 'Գաղտնաբառը թարմացվեց։' },
  e_both_passwords: {
    en: 'Enter your current and your new password.',
    ru: 'Введите текущий и новый пароль.',
    hy: 'Մուտքագրեք ընթացիկ և նոր գաղտնաբառը։',
  },
  log_out: { en: 'Log out', ru: 'Выйти', hy: 'Դուրս գալ' },
  delete_title: { en: 'Delete account', ru: 'Удалить аккаунт', hy: 'Ջնջել հաշիվը' },
  delete_hint: {
    en: 'Irreversible. Type your current password above, then confirm.',
    ru: 'Необратимо. Введите текущий пароль выше, затем подтвердите.',
    hy: 'Անդառնալի է։ Վերևում մուտքագրեք ընթացիկ գաղտնաբառը, ապա հաստատեք։',
  },
  delete_btn: { en: 'Delete my account', ru: 'Удалить мой аккаунт', hy: 'Ջնջել իմ հաշիվը' },
  e_password_first: {
    en: 'Type your current password above first, then tap Delete.',
    ru: 'Сначала введите текущий пароль выше, затем нажмите «Удалить».',
    hy: 'Նախ վերևում մուտքագրեք ընթացիկ գաղտնաբառը, ապա սեղմեք «Ջնջել»։',
  },
  confirm_delete_title: {
    en: 'Delete your account?',
    ru: 'Удалить ваш аккаунт?',
    hy: 'Ջնջե՞լ ձեր հաշիվը',
  },
  confirm_delete_body: {
    en: 'This deletes your account and any toy only you have linked, with its conversations. This is irreversible.',
    ru: 'Будут удалены ваш аккаунт и каждая игрушка, привязанная только к вам, вместе с разговорами. Это необратимо.',
    hy: 'Կջնջվի ձեր հաշիվը, ինչպես նաև միայն ձեզ հետ կապված յուրաքանչյուր խաղալիք՝ իր զրույցներով։ Սա անդառնալի է։',
  },

  // ---------- toy settings ----------
  settings_of: { en: '{name} · Settings', ru: '«{name}» · Настройки', hy: '«{name}» · Կարգավորումներ' },
  wifi_btn: {
    en: '📶 Connect to Wi-Fi (Bluetooth)',
    ru: '📶 Подключить к Wi-Fi (Bluetooth)',
    hy: '📶 Միացնել Wi-Fi-ին (Bluetooth-ով)',
  },
  pause_title: { en: 'Pause the toy', ru: 'Поставить на паузу', hy: 'Դադարեցնել խաղալիքը' },
  pause_hint: {
    en: 'Stops all play right now, until you turn it back on.',
    ru: 'Немедленно останавливает игру, пока вы её не включите снова.',
    hy: 'Անմիջապես կանգնեցնում է խաղը, մինչև նորից չմիացնեք։',
  },
  toy_paused: { en: 'Toy paused.', ru: 'Игрушка на паузе.', hy: 'Խաղալիքը դադարեցվեց։' },
  toy_resumed: { en: 'Toy resumed.', ru: 'Игрушка снова работает.', hy: 'Խաղալիքը կրկին աշխատում է։' },
  modes_title: { en: 'What the toy can do', ru: 'Что умеет игрушка', hy: 'Ինչ կարող է անել խաղալիքը' },
  mode_story: { en: 'Story', ru: 'Сказка', hy: 'Հեքիաթ' },
  mode_game: { en: 'Game', ru: 'Игра', hy: 'Խաղ' },
  mode_riddle: { en: 'Riddle', ru: 'Загадка', hy: 'Հանելուկ' },
  mode_curiosity: { en: 'Curiosity questions', ru: 'Вопросы «почему»', hy: 'Հետաքրքրության հարցեր' },
  modes_hint: {
    en: 'The bedtime calm-down stays available no matter what.',
    ru: 'Спокойный режим перед сном доступен всегда.',
    hy: 'Քնելուց առաջ հանգստացնող ռեժիմը միշտ հասանելի է։',
  },
  modes_updated: { en: 'Modes updated.', ru: 'Режимы обновлены.', hy: 'Ռեժիմները թարմացվեցին։' },
  bedtime_title: { en: 'Quiet hours (bedtime)', ru: 'Часы сна', hy: 'Քնի ժամեր' },
  bedtime_hint: {
    en: "The toy won't respond during this window. 24-hour time.",
    ru: 'В это время игрушка не отвечает. 24-часовой формат.',
    hy: 'Այս ժամերին խաղալիքը չի պատասխանում։ 24-ժամյա ձևաչափ։',
  },
  bedtime_to: { en: 'to', ru: 'до', hy: 'մինչև' },
  turn_off: { en: 'Turn off', ru: 'Выключить', hy: 'Անջատել' },
  bedtime_set: {
    en: 'Quiet hours set: {start}–{end}.',
    ru: 'Часы сна: {start}–{end}.',
    hy: 'Քնի ժամերը՝ {start}–{end}։',
  },
  bedtime_off: { en: 'Quiet hours turned off.', ru: 'Часы сна выключены.', hy: 'Քնի ժամերն անջատվեցին։' },
  e_time_format: {
    en: 'Use 24-hour time, like 21:30.',
    ru: 'Используйте 24-часовой формат, например 21:30.',
    hy: 'Օգտագործեք 24-ժամյա ձևաչափ, օրինակ՝ 21:30։',
  },

  // ---------- conversations ----------
  flagged_link: { en: '⚑ Flagged', ru: '⚑ Отмеченные', hy: '⚑ Նշվածներ' },
  today: { en: 'Today', ru: 'Сегодня', hy: 'Այսօր' },
  stat_talks: { en: 'Talks', ru: 'Разговоры', hy: 'Զրույցներ' },
  stat_messages: { en: 'Messages', ru: 'Сообщения', hy: 'Հաղորդագրություններ' },
  stat_flagged: { en: 'Flagged', ru: 'Отмечено', hy: 'Նշված' },
  section_conversations: { en: 'Conversations', ru: 'Разговоры', hy: 'Զրույցներ' },
  no_conversations: {
    en: "No conversations yet. They'll appear here after your child talks with the toy.",
    ru: 'Разговоров пока нет. Они появятся, когда ребёнок поговорит с игрушкой.',
    hy: 'Զրույցներ դեռ չկան։ Դրանք կհայտնվեն, երբ ձեր երեխան խոսի խաղալիքի հետ։',
  },
  msg_count: { en: '{n} msg', ru: '{n} сообщ.', hy: '{n} հաղորդագրություն' },

  // ---------- flagged ----------
  flagged_of: { en: 'Flagged · {name}', ru: 'Отмеченные · «{name}»', hy: 'Նշվածներ · «{name}»' },
  all_clear: { en: 'All clear', ru: 'Всё в порядке', hy: 'Ամեն ինչ կարգին է' },
  all_clear_hint: {
    en: 'Nothing has been flagged on this toy. Anything the safety checks catch will show up here.',
    ru: 'На этой игрушке ничего не отмечено. Всё, что заметит проверка безопасности, появится здесь.',
    hy: 'Այս խաղալիքով ոչինչ նշված չէ։ Այն ամենը, ինչ նկատում է անվտանգության ստուգումը, կհայտնվի այստեղ։',
  },
  flagged_tag: { en: '⚑ flagged', ru: '⚑ отмечено', hy: '⚑ նշված' },
  open_conversation: { en: 'Open conversation →', ru: 'Открыть разговор →', hy: 'Բացել զրույցը →' },
  role_child: { en: 'Child', ru: 'Ребёнок', hy: 'Երեխա' },
  role_toy: { en: 'Areg', ru: 'Арег', hy: 'Արեգ' },

  // ---------- conversation detail ----------
  conversation_title: { en: 'Conversation', ru: 'Разговор', hy: 'Զրույց' },
  no_messages: { en: 'No messages.', ru: 'Сообщений нет.', hy: 'Հաղորդագրություններ չկան։' },
  who_child: { en: '🧒 Child', ru: '🧒 Ребёнок', hy: '🧒 Երեխա' },
  who_toy: { en: '🧸 Areg', ru: '🧸 Арег', hy: '🧸 Արեգ' },

  // ---------- Wi-Fi setup ----------
  wifi_title: { en: 'Connect {name} to Wi-Fi', ru: 'Подключить «{name}» к Wi-Fi', hy: 'Միացնել «{name}»-ը Wi-Fi-ին' },
  wifi_search: { en: 'Search for my toy', ru: 'Найти мою игрушку', hy: 'Գտնել իմ խաղալիքը' },
  wifi_looking: { en: 'Looking for your toy…', ru: 'Ищем вашу игрушку…', hy: 'Փնտրում ենք ձեր խաղալիքը…' },
  wifi_connecting: { en: 'Connecting…', ru: 'Подключаемся…', hy: 'Միանում ենք…' },
  wifi_sending: { en: 'Sending Wi-Fi to the toy…', ru: 'Отправляем Wi-Fi игрушке…', hy: 'Ուղարկում ենք Wi-Fi-ի տվյալները խաղալիքին…' },
  wifi_done: { en: 'Toy connected to Wi-Fi!', ru: 'Игрушка подключена к Wi-Fi!', hy: 'Խաղալիքը միացավ Wi-Fi-ին։' },
  wifi_done_hint: {
    en: 'It will come online in a moment.',
    ru: 'Она появится в сети через мгновение.',
    hy: 'Մեկ պահից այն կդառնա առցանց։',
  },
  done: { en: 'Done', ru: 'Готово', hy: 'Պատրաստ է' },
  wifi_pick: { en: 'Pick your home Wi-Fi:', ru: 'Выберите домашний Wi-Fi:', hy: 'Ընտրեք ձեր տան Wi-Fi-ը՝' },
  wifi_none: { en: 'No networks found.', ru: 'Сети не найдены.', hy: 'Ցանցեր չգտնվեցին։' },
  wifi_password_for: { en: 'Password for {ssid}', ru: 'Пароль для {ssid}', hy: '«{ssid}»-ի գաղտնաբառը' },
  wifi_password: { en: 'Wi-Fi password', ru: 'Пароль Wi-Fi', hy: 'Wi-Fi-ի գաղտնաբառ' },
  wifi_send: { en: 'Send Wi-Fi to toy', ru: 'Отправить Wi-Fi игрушке', hy: 'Ուղարկել Wi-Fi-ի տվյալները խաղալիքին' },
  e_no_toy_found: {
    en: 'No toy found in setup mode. Hold the toy’s button while turning it on for about 5 seconds, then try again.',
    ru: 'Игрушка в режиме настройки не найдена. Удерживайте кнопку игрушки при включении около 5 секунд и попробуйте снова.',
    hy: 'Կարգավորման ռեժիմում խաղալիք չգտնվեց։ Միացնելիս պահեք խաղալիքի կոճակը մոտ 5 վայրկյան և կրկին փորձեք։',
  },
  e_bluetooth: {
    en: 'Bluetooth setup failed. Please try again.',
    ru: 'Не удалось настроить по Bluetooth. Попробуйте снова.',
    hy: 'Bluetooth-ով կարգավորումը չհաջողվեց։ Կրկին փորձեք։',
  },
  e_send_wifi: {
    en: 'Could not send the Wi-Fi details to the toy.',
    ru: 'Не удалось отправить данные Wi-Fi игрушке.',
    hy: 'Չհաջողվեց խաղալիքին ուղարկել Wi-Fi-ի տվյալները։',
  },
  back_settings: { en: '‹ Settings', ru: '‹ Настройки', hy: '‹ Կարգավորումներ' },

  // ---------- story plays ----------
  tile_plays: { en: 'Stories listened to', ru: 'Прослушанные сказки', hy: 'Լսած հեքիաթներ' },
  plays_title: { en: 'Stories listened to', ru: 'Прослушанные сказки', hy: 'Լսած հեքիաթներ' },
  no_plays: {
    en: 'Nothing yet. The toy sends this when it is online.',
    ru: 'Пока ничего. Игрушка отправит это, когда будет в сети.',
    hy: 'Դեռ ոչինչ չկա։ Խաղալիքը կուղարկի տվյալները, երբ առցանց լինի։',
  },
  listened_count: {
    en: 'Listened: {n} · to the end: {f}',
    ru: 'Прослушано: {n} · до конца: {f}',
    hy: 'Լսել է {n} անգամ · մինչև վերջ՝ {f} անգամ',
  },
  play_finished: { en: '✓ to the end', ru: '✓ до конца', hy: '✓ մինչև վերջ' },
  play_partial: { en: 'stopped early', ru: 'не до конца', hy: 'կիսատ է մնացել' },
  approx_time: { en: 'about', ru: 'примерно', hy: 'մոտավորապես' },
  showing_recent: {
    en: 'Showing the {n} most recent.',
    ru: 'Показаны только последние записи ({n}).',
    hy: 'Ցուցադրված են վերջին {n} լսումը։',
  },
  child_answers: {
    en: "Your child's answers",
    ru: 'Ответы вашего ребёнка',
    hy: 'Ձեր երեխայի պատասխանները',
  },

  // ---------- story library ----------
  tile_library: { en: 'Story library', ru: 'Библиотека сказок', hy: 'Հեքիաթների գրադարան' },
  library_title: { en: 'Story library', ru: 'Библиотека сказок', hy: 'Հեքիաթների գրադարան' },
  no_stories: {
    en: 'No stories yet. New ones appear here when we publish them.',
    ru: 'Сказок пока нет. Новые появятся, когда мы их выпустим.',
    hy: 'Հեքիաթներ դեռ չկան։ Նորերը կհայտնվեն այստեղ, երբ հրապարակենք։',
  },
  on_this_toy: { en: 'Toy', ru: 'Игрушка', hy: 'Խաղալիքը' },
  author_label: { en: 'Author', ru: 'Автор', hy: 'Հեղինակ' },
  goal_label: { en: 'About', ru: 'О сказке', hy: 'Հեքիաթի մասին' },
  lesson_label: { en: 'What it teaches', ru: 'Чему учит', hy: 'Ինչ է սովորեցնում հեքիաթը' },
  bedtime_safe: { en: '🌙 good before sleep', ru: '🌙 подходит для сна', hy: '🌙 հարմար է քնելուց առաջ' },
  discuss_title: {
    en: 'Talk about it with your child',
    ru: 'Обсудите с ребёнком',
    hy: 'Զրուցեք հեքիաթի մասին ձեր երեխայի հետ',
  },

  // ---------- music ----------
  tile_music: { en: 'Music', ru: 'Музыка', hy: 'Երաժշտություն' },
  tile_music_sub: {
    en: 'Calm tunes for bedtime',
    ru: 'Спокойная музыка для сна',
    hy: 'Հանգիստ մեղեդիներ քնելու համար',
  },
  music_title: { en: 'Music', ru: 'Музыка', hy: 'Երաժշտություն' },
  no_music: {
    en: 'No music has been published yet.',
    ru: 'Музыки пока нет.',
    hy: 'Երաժշտություն դեռ չի հրապարակվել։',
  },

  // ---------- custom story requests ----------
  tile_request: { en: 'Ask for a story', ru: 'Заказать сказку', hy: 'Պատվիրել հեքիաթ' },
  tile_request_sub: {
    en: 'We can make one for your child',
    ru: 'Мы можем создать её для вашего ребёнка',
    hy: 'Կարող ենք հեքիաթ ստեղծել ձեր երեխայի համար',
  },
  request_title: { en: 'Ask for a story', ru: 'Заказать сказку', hy: 'Պատվիրել հեքիաթ' },
  request_help: {
    en: 'Describe the story you would like for your child. We read every request ourselves and record it in the storyteller’s voice.',
    ru: 'Опишите сказку, которую хотели бы для вашего ребёнка. Каждый запрос мы читаем сами, а саму сказку озвучиваем голосом сказочника.',
    hy: 'Նկարագրեք այն հեքիաթը, որը կուզեիք ձեր երեխայի համար։ Ամեն պատվեր ինքներս ենք կարդում, իսկ հեքիաթը ձայնագրում ենք հեքիաթասացի ձայնով։',
  },
  request_placeholder: {
    en: 'For example: a story about a brave little goat…',
    ru: 'Например: сказка про храброго козлёнка…',
    hy: 'Օրինակ՝ հեքիաթ քաջ ուլիկի մասին…',
  },
  request_send: { en: 'Send', ru: 'Отправить', hy: 'Ուղարկել' },
  request_sent: {
    en: 'Sent. We will look at it soon.',
    ru: 'Отправлено. Скоро посмотрим.',
    hy: 'Ուղարկվեց։ Շուտով կնայենք։',
  },
  e_request_empty: {
    en: 'Please describe the story first.',
    ru: 'Сначала опишите сказку.',
    hy: 'Նախ նկարագրեք հեքիաթը։',
  },
  my_requests: { en: 'Your requests', ru: 'Ваши запросы', hy: 'Ձեր պատվերները' },
  no_requests: {
    en: 'You have not asked for a story yet.',
    ru: 'Вы ещё не заказывали сказку.',
    hy: 'Դուք դեռ հեքիաթ չեք պատվիրել։',
  },
  req_new: { en: 'received', ru: 'получен', hy: 'ստացված է' },
  req_in_review: { en: 'in review', ru: 'рассматривается', hy: 'դիտարկվում է' },
  req_delivered: { en: 'ready', ru: 'готов', hy: 'պատրաստ է' },
  req_declined: { en: 'declined', ru: 'отклонён', hy: 'մերժված է' },

  // ---------- activity ----------
  tile_activity: { en: 'Your activity', ru: 'Ваша активность', hy: 'Ձեր գործողությունները' },
  tile_activity_sub: {
    en: 'Every change you have made',
    ru: 'Все изменения, которые вы вносили',
    hy: 'Ձեր կատարած բոլոր փոփոխությունները',
  },
  activity_title: { en: 'Your activity', ru: 'Ваша активность', hy: 'Ձեր գործողությունները' },
  no_activity: {
    en: 'Nothing here yet. When you change a setting or add a toy, it appears here.',
    ru: 'Пока пусто. Когда вы измените настройку или добавите игрушку — появится здесь.',
    hy: 'Դեռ ոչինչ չկա։ Երբ փոխեք որևէ կարգավորում կամ ավելացնեք խաղալիք, դա կերևա այստեղ։',
  },
  ev_account_deleted: { en: 'Account deleted', ru: 'Аккаунт удалён', hy: 'Հաշիվը ջնջվեց' },
  ev_child_deleted: { en: 'Child profile removed', ru: 'Профиль ребёнка удалён', hy: 'Երեխայի պրոֆիլը հեռացվեց' },
  ev_device_unlinked: { en: 'Toy removed', ru: 'Игрушка убрана', hy: 'Խաղալիքը հեռացվեց հաշվից' },
  ev_password_changed: { en: 'Password changed', ru: 'Пароль изменён', hy: 'Գաղտնաբառը փոխվեց' },
  ev_pause_changed: { en: 'Toy paused or resumed', ru: 'Пауза включена или снята', hy: 'Խաղալիքի դադարը փոխվեց' },
  ev_bedtime_set: { en: 'Quiet hours updated', ru: 'Часы сна обновлены', hy: 'Քնի ժամերը թարմացվեցին' },
  ev_modes_set: { en: 'Modes updated', ru: 'Режимы обновлены', hy: 'Ռեժիմները թարմացվեցին' },
  ev_child_modes_set: { en: "A child's modes updated", ru: 'Режимы ребёнка обновлены', hy: 'Երեխայի ռեժիմները թարմացվեցին' },
  ev_conversation_deleted: { en: 'Conversation deleted', ru: 'Разговор удалён', hy: 'Զրույցը ջնջվեց' },
  ev_google_signin: { en: 'Signed in with Google', ru: 'Вход через Google', hy: 'Մուտք կատարվեց Google-ով' },
  ev_data_exported: { en: 'Data downloaded', ru: 'Данные скачаны', hy: 'Տվյալները ներբեռնվեցին' },
  ev_reset_requested: { en: 'Password reset requested', ru: 'Запрошен сброс пароля', hy: 'Գաղտնաբառի վերականգնում խնդրվեց' },
  ev_reset_completed: { en: 'Password reset finished', ru: 'Сброс пароля завершён', hy: 'Գաղտնաբառի վերականգնումն ավարտվեց' },
  ev_email_verified: { en: 'Email confirmed', ru: 'Почта подтверждена', hy: 'Էլ. փոստը հաստատվեց' },
  ev_device_renamed: { en: 'Toy renamed', ru: 'Игрушка переименована', hy: 'Խաղալիքը վերանվանվեց' },
  ev_device_claimed: { en: 'Toy added', ru: 'Игрушка добавлена', hy: 'Խաղալիքն ավելացվեց' },
  ev_device_linked: { en: 'Toy added', ru: 'Игрушка добавлена', hy: 'Խաղալիքն ավելացվեց' },
  ev_revocation_changed: { en: 'Toy access changed', ru: 'Доступ игрушки изменён', hy: 'Խաղալիքի մուտքը փոխվեց' },
  ev_story_intro_set: { en: 'Spoken story intro changed', ru: 'Озвученное вступление изменено', hy: 'Հեքիաթի վերնագրի հայտարարումը փոխվեց' },
  ev_bedtime_music_set: { en: 'Bedtime music changed', ru: 'Музыка для сна изменена', hy: 'Քնի ժամի երաժշտությունը փոխվեց' },
  ev_story_requested: { en: 'Story requested', ru: 'Заказана сказка', hy: 'Հեքիաթ պատվիրվեց' },
  account_title: { en: 'Account', ru: 'Аккаунт', hy: 'Հաշիվ' },
  wifi_setup_mode: {
    en: 'Put the toy in setup mode — hold its button while turning it on for about 5 seconds — then search for it over Bluetooth.',
    ru: 'Переведите игрушку в режим настройки: удерживайте кнопку при включении около 5 секунд, затем найдите её по Bluetooth.',
    hy: 'Խաղալիքը դրեք կարգավորման ռեժիմի մեջ՝ միացնելիս պահեք կոճակը մոտ 5 վայրկյան, ապա գտեք այն Bluetooth-ով։',
  },
  wifi_unavailable: {
    en: "Bluetooth setup needs the installed Areg app — it does not work inside Expo Go. For now, use the free ESP BLE Provisioning app with this pairing code:",
    ru: 'Настройка по Bluetooth работает только в установленном приложении Areg — в Expo Go она недоступна. Пока используйте бесплатное приложение ESP BLE Provisioning с этим кодом:',
    hy: 'Bluetooth-ով կարգավորումն աշխատում է միայն տեղադրված Areg հավելվածում։ Expo Go-ում այն հասանելի չէ։ Առայժմ օգտագործեք անվճար ESP BLE Provisioning հավելվածն այս կոդով՝',
  },
  wifi_web_note: {
    en: 'Bluetooth setup runs in the phone app, not in this web preview. For now you can set up the toy’s Wi-Fi with the free ESP BLE Provisioning app — pairing code:',
    ru: 'Настройка по Bluetooth работает в мобильном приложении, а не в этом веб-просмотре. Пока настройте Wi-Fi игрушки бесплатным приложением ESP BLE Provisioning — код привязки:',
    hy: 'Bluetooth-ով կարգավորումն աշխատում է բջջային հավելվածում, ոչ թե այս վեբ-տարբերակում։ Առայժմ խաղալիքի Wi-Fi-ը կարգավորեք անվճար ESP BLE Provisioning հավելվածով։ Կապակցման կոդը՝',
  },
  e_wifi_join: {
    en: 'The toy could not join “{ssid}”. Check the password and try again.',
    ru: 'Игрушка не смогла подключиться к «{ssid}». Проверьте пароль и попробуйте снова.',
    hy: 'Խաղալիքը չկարողացավ միանալ «{ssid}»-ին։ Ստուգեք գաղտնաբառը և կրկին փորձեք։',
  },
} satisfies Record<string, Entry>;

export type Key = keyof typeof D;

export function t(key: Key): string {
  const e = D[key] as Entry;
  return e[current] || e.en;
}

/** Interpolating translate. Named slots keep each language's word order. */
export function tf(key: Key, vars: Record<string, string | number>): string {
  let s = t(key);
  for (const k of Object.keys(vars)) s = s.split(`{${k}}`).join(String(vars[k]));
  return s;
}

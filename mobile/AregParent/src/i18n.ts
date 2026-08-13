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
  back_activity: { en: '‹ Conversations', ru: '‹ Разговоры', hy: '‹ Զրույցներ' },
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

  // ---------- the diary ----------
  // Named as things that happened, not as screens. Short enough for three
  // pills across a 390px phone in all three languages.
  diary_talk: { en: 'Talked', ru: 'Разговоры', hy: 'Խոսել է' },
  diary_heard: { en: 'Heard', ru: 'Слушал', hy: 'Լսել է' },
  diary_held: { en: 'Held back', ru: 'Задержано', hy: 'Կասեցված' },

  // ---------- settings groups ----------
  // The same questions the web dashboard groups its settings under, so
  // a parent who uses both is taught one mental model rather than two.
  set_when: { en: 'When can it play?', ru: 'Когда можно играть?', hy: 'Ե՞րբ կարող է խաղալ' },
  set_when_n: { en: 'Quiet hours, and a way to stop it right now.', ru: 'Тихие часы и способ остановить игрушку прямо сейчас.', hy: 'Լուռ ժամերը և միանգամից կանգնեցնելու ձևը։' },
  set_what: { en: 'What can it play?', ru: 'Во что можно играть?', hy: 'Ի՞նչ կարող է խաղալ' },
  set_what_n: { en: 'Switch off anything you would rather it did not do. Bedtime calm always stays on.', ru: 'Выключите то, чего не хотите. Спокойный режим перед сном остаётся всегда.', hy: 'Անջատեք այն, ինչ չեք ուզում։ Քնելու հանգիստ ռեժիմը միշտ միացված է մնում։' },
  set_toy: { en: 'This toy', ru: 'Эта игрушка', hy: 'Այս խաղալիքը' },

  // ---------- child profile and invites ----------
  // Lifted from the web dashboard by script, same as the capability
  // strings: one wording, one linguistic review, no drift.
  add_child: { en: '＋ Add child', ru: '＋ Добавить ребёнка', hy: '＋ Ավելացնել երեխա' },
  child_name_ph: { en: "Child's name", ru: 'Имя ребёнка', hy: 'Երեխայի անունը' },
  gender_label: { en: 'Boy or girl', ru: 'Мальчик или девочка', hy: 'Տղա, թե աղջիկ' },
  gender_boy: { en: 'Boy', ru: 'Мальчик', hy: 'Տղա' },
  gender_girl: { en: 'Girl', ru: 'Девочка', hy: 'Աղջիկ' },
  birth_year: { en: 'Birth year (optional)', ru: 'Год рождения (необязательно)', hy: 'Ծննդյան տարի (ըստ ցանկության)' },
  no_child: { en: 'No child profile yet', ru: 'Профиль ребёнка ещё не создан', hy: 'Երեխայի պրոֆիլ դեռ չկա' },
  child_name_required: { en: 'Please enter a name.', ru: 'Введите имя.', hy: 'Խնդրում ենք մուտքագրել անունը։' },
  invite_intro: { en: 'Easier: make a code they can type. It works once and lasts a day. The code printed on your toy keeps working as before.', ru: 'Проще: создайте код, который он введёт. Он сработает один раз и действует сутки. Код на коробке продолжит работать как раньше.', hy: 'Ավելի հեշտ՝ ստեղծեք կոդ, որ նա մուտքագրի։ Այն աշխատում է մեկ անգամ և գործում է մեկ օր։ Խաղալիքի վրա տպված կոդը շարունակում է աշխատել ինչպես առաջ։' },
  invite_make: { en: 'Make an invite code', ru: 'Создать код-приглашение', hy: 'Ստեղծել հրավերի կոդ' },
  invite_again: { en: 'Make another one', ru: 'Создать ещё один', hy: 'Ստեղծել մեկ ուրիշը' },
  invite_once: { en: 'Write it down or send it now — it is shown only this once. Making a new one is free.', ru: 'Запишите или отправьте прямо сейчас — он показывается только один раз. Создать новый можно в любой момент.', hy: 'Գրեք կամ ուղարկեք հիմա — ցուցադրվում է միայն այս մեկ անգամ։ Նորը ստեղծելը միշտ հնարավոր է։' },
  invite_failed: { en: 'Could not make a code. This toy may already have two people on it — that is the limit.', ru: 'Не удалось создать код. Возможно, у этой игрушки уже два человека — это максимум.', hy: 'Չհաջողվեց կոդ ստեղծել։ Հնարավոր է՝ այս խաղալիքն արդեն երկու հոգի ունի — դա սահմանն է։' },
  invite_have: { en: 'Got an invite code?', ru: 'Есть код-приглашение?', hy: 'Հրավերի կոդ ունե՞ք' },
  invite_ph: { en: 'ABCD-EFGH-JKLM', ru: 'ABCD-EFGH-JKLM', hy: 'ABCD-EFGH-JKLM' },
  invite_join: { en: 'Join this toy', ru: 'Присоединиться', hy: 'Միանալ խաղալիքին' },
  invite_joined: { en: 'Done — the toy is on your account now.', ru: 'Готово — игрушка теперь в вашем аккаунте.', hy: 'Պատրաստ է — խաղալիքն այժմ ձեր հաշվում է։' },
  invite_bad: { en: "That invite code didn't work. Ask for a new one.", ru: 'Код-приглашение не подошёл. Попросите новый.', hy: 'Հրավերի կոդը չաշխատեց։ Խնդրեք նորը։' },
  share_title: { en: 'Let someone else see this toy', ru: 'Дать доступ другому человеку', hy: 'Թույլ տալ, որ ուրիշն էլ տեսնի այս խաղալիքը' },
  share_how: { en: 'Two people can watch one toy. The other person makes their own account, taps “＋ Add a toy”, and enters the two things below. They will be able to change the settings too — you will get an email when they join.', ru: 'За одной игрушкой могут следить два человека. Другой человек создаёт свой аккаунт, нажимает «＋ Добавить игрушку» и вводит то, что ниже. Он тоже сможет менять настройки — вы получите письмо, когда он присоединится.', hy: 'Մեկ խաղալիքին կարող են հետևել երկու հոգի։ Մյուսը ստեղծում է իր հաշիվը, սեղմում «＋ Ավելացնել խաղալիք» և մուտքագրում ներքևինը։ Նա նույնպես կկարողանա փոխել կարգավորումները — դուք նամակ կստանաք, երբ նա միանա։' },
  child_why: { en: 'Areg uses this to call your child by name and to pitch stories at the right age. Armenian also needs to know whether it is speaking to a boy or a girl.', ru: 'Areg обращается к ребёнку по имени и подбирает истории по возрасту. Армянскому языку также нужно знать, мальчик это или девочка.', hy: 'Areg-ը այս տվյալներով դիմում է երեխային անունով և ընտրում տարիքին համապատասխան հեքիաթներ։ Հայերենին պետք է նաև իմանալ՝ տղա է, թե աղջիկ։' },
  // Failure messages for the two new calls. The server answers ONE thing
  // for every reason on both, so these must not invent a specific cause.
  e_invite_failed: { en: 'Could not make a code. This toy may already have two people on it — that is the limit.', ru: 'Не удалось создать код. Возможно, у этой игрушки уже два человека — это максимум.', hy: 'Չհաջողվեց կոդ ստեղծել։ Հնարավոր է՝ այս խաղալիքն արդեն երկու հոգի ունի — դա սահմանն է։' },
  e_invite_bad: { en: "That invite code didn't work. Ask for a new one.", ru: 'Код-приглашение не подошёл. Попросите новый.', hy: 'Հրավերի կոդը չաշխատեց։ Խնդրեք նորը։' },

  // ---------- what Areg can do ----------
  // Lifted verbatim from the web dashboard (wwwroot/parent.html) rather than
  // retyped: the same parent may read both, and two hand-copied sets of the
  // same sentence drift the first time one is edited. The Armenian there has
  // been through the linguistic review.
  caps_title: { en: 'What Areg can do', ru: 'Что умеет Арег', hy: 'Ի՞նչ գիտի Արեգը' },
  caps_intro: { en: 'Everything your toy does, in plain words. You can switch most of these on or off in Settings.', ru: 'Всё, что умеет ваша игрушка, простыми словами. Почти всё можно включить или выключить в настройках.', hy: 'Այն ամենը, ինչ գիտի ձեր խաղալիքը՝ պարզ բառերով։ Մեծ մասը կարող եք միացնել կամ անջատել կարգավորումներում։' },
  caps_soon_title: { en: 'Coming soon', ru: 'Скоро', hy: 'Շուտով' },
  caps_soon_note: { en: 'Built and being tested. Your toy does not do these yet.', ru: 'Сделано и проходит проверку. Пока игрушка этого не делает.', hy: 'Պատրաստ է և ստուգվում է։ Խաղալիքը դեռ չի անում սա։' },
  caps_footer: { en: 'Everything above happens in Armenian. Areg never chats freely — it plays, tells, asks and calms, and nothing else.', ru: 'Всё вышеперечисленное — на армянском. Арег никогда не болтает свободно: он играет, рассказывает, спрашивает и успокаивает — и ничего больше.', hy: 'Վերևի ամեն ինչ հայերեն է։ Արեգը երբեք ազատ չի զրուցում — նա խաղում է, պատմում, հարցնում և հանգստացնում, ուրիշ ոչինչ։' },
  caps_link: { en: 'What Areg can do', ru: 'Что умеет Арег', hy: 'Ի՞նչ գիտի Արեգը' },
  caps_link_sub: { en: 'Every feature, explained', ru: 'Все возможности, понятно', hy: 'Բոլոր հնարավորությունները՝ պարզ' },
  cap_stories_t: { en: 'Tells stories', ru: 'Рассказывает сказки', hy: 'Հեքիաթ է պատմում' },
  cap_stories_e: { en: 'Eight Armenian tales on the card. No internet needed — it works in the car, in the village, anywhere.', ru: 'Восемь армянских сказок на карте. Интернет не нужен — работает в машине, в деревне, где угодно.', hy: 'Ութ հայկական հեքիաթ՝ քարտի վրա։ Ինտերնետ պետք չէ — աշխատում է մեքենայում, գյուղում, ամենուր։' },
  cap_askmid_t: { en: 'Your child can interrupt and ask', ru: 'Ребёнок может перебить и спросить', hy: 'Երեխան կարող է ընդհատել ու հարցնել' },
  cap_askmid_e: { en: 'Mid-story your child asks «And did the wolf come?» — Areg answers from that story, then carries on where it stopped.', ru: 'Посреди сказки ребёнок спрашивает «А волк пришёл?» — Арег отвечает по этой сказке и продолжает с того же места.', hy: 'Հեքիաթի կեսին երեխան հարցնում է՝ «Իսկ գայլը եկա՞վ» — Արեգը պատասխանում է հենց այդ հեքիաթից և շարունակում նույն տեղից։' },
  cap_askafter_t: { en: 'Asks a question at the end — and you can read the answer', ru: 'Задаёт вопрос в конце — и вы читаете ответ', hy: 'Վերջում հարց է տալիս — իսկ դուք կարդում եք պատասխանը' },
  cap_askafter_e: { en: "After the story: «Why didn't the kid open the door?» Whatever your child says is saved for you under What they said.", ru: 'После сказки: «Почему козлёнок не открыл дверь?» Ответ ребёнка сохраняется для вас в разделе «Что он сказал».', hy: 'Հեքիաթից հետո՝ «Ինչո՞ւ ուլիկը դուռը չբացեց»։ Երեխայի ասածը պահվում է ձեզ համար՝ «Ինչ ասաց» բաժնում։' },
  cap_games_t: { en: 'Plays games', ru: 'Играет в игры', hy: 'Խաղ է խաղում' },
  cap_games_e: { en: 'Counting, animal sounds, silly yes-or-no, making words small — and a guessing game where Areg tries to guess what your child is thinking of.', ru: 'Счёт, звуки животных, смешные «да-нет», уменьшительные слова — и угадайка, где Арег угадывает, что задумал ребёнок.', hy: 'Հաշվել, կենդանիների ձայներ, ծիծաղելի «այո-ոչ», փոքրացնող բառեր — և գուշակելու խաղ, որտեղ Արեգը փորձում է գուշակել, թե ինչ է մտածում երեխան։' },
  cap_riddles_t: { en: 'Asks riddles', ru: 'Загадывает загадки', hy: 'Հանելուկ է տալիս' },
  cap_riddles_e: { en: 'Simple riddles for this age, with a warm hint if your child gets stuck. Never a test.', ru: 'Простые загадки для этого возраста, с мягкой подсказкой, если ребёнок затрудняется. Никогда не экзамен.', hy: 'Այս տարիքին հարմար պարզ հանելուկներ, տաք հուշումով, եթե երեխան դժվարանա։ Երբեք քննություն չէ։' },
  cap_why_t: { en: "Answers 'why' questions", ru: 'Отвечает на «почему»', hy: 'Պատասխանում է «ինչու» հարցերին' },
  cap_why_e: { en: '«Why is the sky blue?» — one real answer a child can hold, then straight back to playing.', ru: '«Почему небо голубое?» — один настоящий ответ, понятный ребёнку, и сразу обратно к игре.', hy: '«Ինչո՞ւ է երկինքը կապույտ» — մեկ իսկական պատասխան, որ երեխան կհասկանա, և անմիջապես նորից խաղ։' },
  cap_calm_t: { en: 'Calms down at bedtime', ru: 'Успокаивает перед сном', hy: 'Հանգստացնում է քնելուց առաջ' },
  cap_calm_e: { en: 'Soft and slow, no questions, no cliffhanger. Always on — a sleepy word always reaches it, whatever else you have switched off.', ru: 'Мягко и медленно, без вопросов и интриги. Всегда включено — сонное слово доходит всегда, что бы вы ни отключили.', hy: 'Փափուկ ու դանդաղ, առանց հարցերի։ Միշտ միացված է — քնի մասին խոսքը միշտ հասնում է, ինչ էլ որ անջատած լինեք։' },
  cap_greets_t: { en: 'Says hello when it wakes up', ru: 'Здоровается при включении', hy: 'Բարևում է, երբ արթնանում է' },
  cap_greets_e: { en: 'Presses on: «Ողջու՛յն։ Ուրախ եմ, որ եկար։» Then it offers a story by name — one your child has not heard yet.', ru: 'Включается: «Ողջու՛յն։ Ուրախ եմ, որ եկար։» Потом предлагает сказку по названию — ту, которую ребёнок ещё не слышал.', hy: 'Միանում է՝ «Ողջու՛յն։ Ուրախ եմ, որ եկար։» Հետո առաջարկում է հեքիաթ՝ անունով, մի բան, որ երեխան դեռ չի լսել։' },
  cap_intro_t: { en: "Says the story's name and author first", ru: 'Сначала называет сказку и автора', hy: 'Սկզբում ասում է հեքիաթի անունն ու հեղինակին' },
  cap_intro_e: { en: '«Հեքիաթ՝ «Ուլիկը»։ Հեղինակ՝ Հովհաննես Թումանյան։» So your child learns whose story it is.', ru: '«Հեքիաթ՝ «Ուլիկը»։ Հեղինակ՝ Հովհաննես Թումանյան։» Чтобы ребёнок знал, чья это сказка.', hy: '«Հեքիաթ՝ «Ուլիկը»։ Հեղինակ՝ Հովհաննես Թումանյան։» Որ երեխան իմանա՝ ում հեքիաթն է։' },
  cap_quiet_t: { en: 'Stays quiet during the hours you choose', ru: 'Молчит в выбранные вами часы', hy: 'Լռում է ձեր ընտրած ժամերին' },
  cap_quiet_e: { en: 'Set 20:30–07:00 and the toy will not chat in the night, even if it is picked up.', ru: 'Поставьте 20:30–07:00 — и ночью игрушка не будет разговаривать, даже если её возьмут.', hy: 'Դրեք 20։30–07։00, և գիշերը խաղալիքը չի խոսի, նույնիսկ եթե վերցնեն։' },
  cap_pauses_t: { en: 'Stops mid-story so your child can shout something', ru: 'Останавливается посреди сказки, чтобы ребёнок крикнул', hy: 'Կանգ է առնում հեքիաթի կեսին, որ երեխան բարձր ասի' },
  cap_pauses_e: { en: 'Areg pauses, asks what happens next, waits — then carries on the same story from the same place.', ru: 'Арег делает паузу, спрашивает, что будет дальше, ждёт — и продолжает ту же сказку с того же места.', hy: 'Արեգը դադար է տալիս, հարցնում է՝ ի՞նչ կլինի հետո, սպասում է — հետո շարունակում է նույն հեքիաթը նույն տեղից։' },
  cap_endings_t: { en: 'A different ending when a story is heard again', ru: 'Другой финал при повторном прослушивании', hy: 'Ուրիշ վերջ, երբ հեքիաթը կրկին են լսում' },
  cap_endings_e: { en: 'Only some stories have a second ending — the rest end the way they always did.', ru: 'Второй финал есть не у всех сказок — остальные заканчиваются как обычно.', hy: 'Երկրորդ վերջ ունեն միայն որոշ հեքիաթներ — մնացածը ավարտվում են ինչպես միշտ։' },
  cap_buttons_t: { en: 'Three games with the buttons', ru: 'Три игры с кнопками', hy: 'Երեք խաղ կոճակներով' },
  cap_buttons_e: { en: 'Areg guesses the animal your child is thinking of; two children race to press first; and a tone game to copy.', ru: 'Арег угадывает животное, которое загадал ребёнок; двое детей соревнуются, кто нажмёт первым; и игра на повтор мелодии.', hy: 'Արեգը գուշակում է երեխայի մտապահած կենդանին, երկու երեխա մրցում են՝ ով շուտ կսեղմի, և մեղեդի կրկնելու խաղ։' },
  cap_music_t: { en: 'Quiet music at bedtime', ru: 'Тихая музыка перед сном', hy: 'Հանգիստ երաժշտություն քնելուց առաջ' },
  cap_music_e: { en: 'Instead of a story, a calm tune during your quiet hours.', ru: 'Вместо сказки — спокойная мелодия в ваши тихие часы.', hy: 'Հեքիաթի փոխարեն՝ հանգիստ մեղեդի ձեր լուռ ժամերին։' },

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
  see_activity: { en: 'Conversations', ru: 'Разговоры', hy: 'Զրույցներ' },
  see_activity_sub: {
    en: 'What your child talked about',
    ru: 'О чём говорил ваш ребёнок',
    hy: 'Ինչի մասին է խոսել ձեր երեխան',
  },
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
  // Story library freshness. The parent is told READY or NOT READY and
  // nothing else: no versions, no story ids, no megabytes.
  //
  // Key -> backend verdict: library_ready=up_to_date,
  // library_updating=syncing, library_none=stale, library_unknown=unknown.
  // (offline renders nothing — see the render block.)
  //
  // "In the first minutes after it is switched on" is not filler and is
  // not softened advice: content_sync runs exactly ONCE per boot, armed
  // 180 s in, and s_done is never reset (content_sync.cpp). The earlier
  // draft said "keep it on Wi-Fi", which is false for the most common
  // case this line exists for — a toy powered on before the library
  // changed has already spent its one attempt, and an hour on Wi-Fi
  // changes nothing. A parent who follows an instruction that cannot
  // work is exactly the support contact this product avoids.
  //
  // library_unknown does NOT say "nothing is wrong": the toy also
  // reports nothing when its SD card is dead, and that toy will never
  // tell a story. It gives a self-check the parent can run in five
  // seconds instead. See the TODO in DeviceContentHealth.cs.
  // Armenian reviewed 2026-08-13. Two calls worth keeping:
  // «գալիս են», not «ներբեռնվում են» — "download" is process language
  // about the device, and the English says "arrive"; and the partitive
  // trails the noun («{have} հեքիաթ՝ ընդհանուր {total}-ից») so a tired
  // reader meets the number they care about beside the word "story"
  // instead of holding two numerals before reaching it.
  library_ready: {
    en: 'All {n} stories are on this toy.',
    ru: 'Все сказки уже на игрушке — их {n}.',
    hy: 'Բոլոր հեքիաթներն արդեն խաղալիքի մեջ են՝ {n} հատ։'
  },
  library_updating: {
    en: '{have} of {total} stories are on this toy. New stories arrive over Wi‑Fi in the first minutes after it is switched on.',
    ru: 'На игрушке {have} из {total} сказок. Новые сказки загружаются по Wi‑Fi в первые минуты после включения.',
    hy: 'Խաղալիքի մեջ կա {have} հեքիաթ՝ ընդհանուր {total}-ից։ Նոր հեքիաթները գալիս են Wi‑Fi-ով՝ միացնելուց հետո առաջին րոպեներին։'
  },
  library_none: {
    en: 'No stories have reached this toy yet. They arrive over Wi‑Fi in the first minutes after it is switched on.',
    ru: 'На игрушку ещё не загрузилась ни одна сказка. Они загружаются по Wi‑Fi в первые минуты после включения.',
    hy: 'Խաղալիքին դեռ ոչ մի հեքիաթ չի հասել։ Դրանք գալիս են Wi‑Fi-ով՝ միացնելուց հետո առաջին րոպեներին։'
  },
  library_unknown: {
    en: 'We do not have the story list for this toy. If it still tells stories, nothing is wrong.',
    ru: 'У нас нет списка сказок этой игрушки. Если она по-прежнему рассказывает сказки, всё в порядке.',
    hy: 'Այս խաղալիքի հեքիաթների ցանկը մեզ մոտ չկա։ Եթե այն շարունակում է հեքիաթներ պատմել, ամեն ինչ կարգին է։'
  },
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
  // No colon, no field name — "Listened:" was a column heading from a
  // database leaking onto a phone, the same habit the web dashboard dropped.
  listened_count: {
    en: '{n} listens · {f} finished',
    ru: '{n} прослушиваний · {f} до конца',
    hy: '{n} անգամ լսել է · {f} անգամ մինչև վերջ',
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
  ev_story_pauses_set: { en: 'Story pauses changed', ru: 'Паузы в сказках изменены', hy: 'Հեքիաթի դադարները փոխվեցին' },
  ev_variant_endings_set: { en: 'Alternate endings changed', ru: 'Альтернативные концовки изменены', hy: 'Այլընտրանքային ավարտները փոխվեցին' },
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

// Mock Areg backend: serves the real wwwroot and stubs every parent endpoint
// with realistic Armenian data, so the dashboard can be driven end to end
// without dotnet. Shapes copied from the DTO records, not invented.
const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '../../backend/src/ArmenianAiToy.Api/wwwroot');
const iso = (d) => new Date(d).toISOString();
// Fixed by default so two runs produce byte-identical screenshots and a
// before/after diff means something. Override with AREG_MOCK_NOW=now for a
// run where you want plausible "just happened" times.
const now = process.env.AREG_MOCK_NOW === 'now'
  ? Date.now()
  : Date.parse(process.env.AREG_MOCK_NOW || '2026-08-10T18:00:00Z');
const ago = (min) => iso(now - min * 60000);

const DEV1 = '11111111-1111-1111-1111-111111111111';
const DEV2 = '22222222-2222-2222-2222-222222222222';
const CONV = ['aaaaaaaa-0000-0000-0000-00000000000' + 1, 'aaaaaaaa-0000-0000-0000-00000000000' + 2,
              'aaaaaaaa-0000-0000-0000-00000000000' + 3, 'aaaaaaaa-0000-0000-0000-00000000000' + 4];

const devices = [
  { deviceId: DEV1, deviceName: 'Անիի Արեգը', lastSeenAt: ago(2), linkedAt: ago(60 * 24 * 40),
    lastConversationAt: ago(95),
    children: [
      { childId: 'c1111111-1111-1111-1111-111111111111', name: 'Անի', age: 5, gender: 'Female',
        storyEnabled: null, gameEnabled: null, riddleEnabled: false, curiosityEnabled: null },
      { childId: 'c2222222-2222-2222-2222-222222222222', name: 'Դավիթ', age: 7, gender: 'Male',
        storyEnabled: null, gameEnabled: null, riddleEnabled: null, curiosityEnabled: null }],
    isPaused: false, isRevoked: false, bedtimeStart: '20:30:00', bedtimeEnd: '07:00:00',
    storyEnabled: true, gameEnabled: true, riddleEnabled: true, curiosityEnabled: true,
    isDormant: false, isOnline: true, storyHealth: 'ok', faultCode: '',
    storyIntroEnabled: true, storyPausesEnabled: true, variantEndingsEnabled: true,
    bedtimeMusicEnabled: false },
  { deviceId: DEV2, deviceName: 'Տատիկի մոտ', lastSeenAt: ago(60 * 32), linkedAt: ago(60 * 24 * 9),
    lastConversationAt: ago(60 * 33), children: [],
    isPaused: true, isRevoked: false, bedtimeStart: null, bedtimeEnd: null,
    storyEnabled: true, gameEnabled: false, riddleEnabled: true, curiosityEnabled: true,
    isDormant: false, isOnline: false, storyHealth: 'offline', faultCode: 'E-101',
    storyIntroEnabled: true, storyPausesEnabled: false, variantEndingsEnabled: true,
    bedtimeMusicEnabled: true },
];

const summaries = [
  { id: CONV[0], startedAt: ago(95), endedAt: ago(78), messageCount: 22, hasFlaggedContent: false,
    firstUserSnippet: 'Պատմիր մի հեքիաթ', lastAssistantSnippet: 'Եվ այդպես Ուլիկը տուն վերադարձավ։',
    lastAssistantSafetyFlag: 'Clean', flaggedMessageCount: 0, modes: ['story'] },
  { id: CONV[1], startedAt: ago(240), endedAt: ago(228), messageCount: 14, hasFlaggedContent: true,
    firstUserSnippet: 'Խաղանք', lastAssistantSnippet: 'Ապրե՛ս։ Ճիշտ հաշվեցիր։',
    lastAssistantSafetyFlag: 'Clean', flaggedMessageCount: 1, modes: ['game'] },
  { id: CONV[2], startedAt: ago(60 * 20), endedAt: ago(60 * 20 - 9), messageCount: 8,
    hasFlaggedContent: false, firstUserSnippet: 'Ինչու՞ է երկինքը կապույտ',
    lastAssistantSnippet: 'Արևի լույսը օդում ցրվում է։', lastAssistantSafetyFlag: 'Clean',
    flaggedMessageCount: 0, modes: ['curiosity', 'story'] },
  { id: CONV[3], startedAt: ago(60 * 26), endedAt: ago(60 * 26 - 6), messageCount: 6,
    hasFlaggedContent: false, firstUserSnippet: 'Քնելու ժամանակն է',
    lastAssistantSnippet: 'Աչքերդ փակիր, լավ երազներ։', lastAssistantSafetyFlag: 'Clean',
    flaggedMessageCount: 0, modes: ['calm'] },
];

const messages = [
  { id: 'm0000001-0000-0000-0000-000000000001', role: 'User', content: 'Պատմիր մի հեքիաթ ուլիկի մասին',
    timestamp: ago(95), safetyFlag: 'Clean', audioAvailable: false, mode: 'story' },
  { id: 'm0000002-0000-0000-0000-000000000002', role: 'Assistant',
    content: 'Մի անգամ մի փոքրիկ ուլիկ էր ապրում իր մայրիկի հետ անտառի եզրին։ Մայրիկը գնում էր արոտ, իսկ ուլիկը մնում էր տանը և ոչ մեկի դուռը չէր բացում։',
    timestamp: ago(94), safetyFlag: 'Clean', audioAvailable: true, mode: 'story' },
  { id: 'm0000003-0000-0000-0000-000000000003', role: 'User', content: 'Իսկ գայլը եկա՞վ',
    timestamp: ago(93), safetyFlag: 'Clean', audioAvailable: false, mode: 'story' },
  { id: 'm0000004-0000-0000-0000-000000000004', role: 'Assistant',
    content: 'Եկավ, բայց ուլիկը ձայնից հասկացավ, որ դա մայրիկը չէ, և դուռը չբացեց։',
    timestamp: ago(92), safetyFlag: 'Clean', audioAvailable: true, mode: 'story' },
];

const stories = [
  { storyId: 'ulik', title: 'Ուլիկը', author: 'Հովհաննես Թումանյան', goal: 'Զգուշություն օտարների հանդեպ',
    lesson: 'Մայրիկի խոսքը լսելը պաշտպանում է։', bedtimeSafe: true, minAge: 4, maxAge: 7, segmentCount: 6,
    reflectionQuestions: ['Ինչո՞ւ ուլիկը դուռը չբացեց։', 'Դու ի՞նչ կանեիր։', 'Ո՞վ էր ուլիկին օգնում։'],
    reflectionConclusions: ['Որովհետև ձայնը մայրիկինը չէր։', 'Լավ է հարցնել մեծերին։', 'Մայրիկը։'],
    listenCount: 12, finishedCount: 9, previewUrl: '/api/parents/stories/ulik/audio' },
  { storyId: 'khosogh-dzuk', title: 'Խոսող ձուկը', author: 'Հովհաննես Թումանյան', goal: 'Բարություն',
    lesson: 'Բարությունը վերադառնում է։', bedtimeSafe: true, minAge: 5, maxAge: 7, segmentCount: 9,
    reflectionQuestions: ['Ինչո՞ւ տղան ձկանը բաց թողեց։'], reflectionConclusions: ['Որովհետև խղճաց։'],
    listenCount: 7, finishedCount: 2, previewUrl: '/api/parents/stories/khosogh-dzuk/audio' },
  { storyId: 'three-piglets', title: 'Երեք խոզուկները', author: null, goal: 'Աշխատասիրություն',
    lesson: 'Հոգնած ձեռքը ամուր տուն է կառուցում։', bedtimeSafe: true, minAge: 4, maxAge: 6,
    segmentCount: 5, reflectionQuestions: ['Ո՞ր տունը ամենաամուրն էր։'], reflectionConclusions: ['Աղյուսից։'],
    listenCount: 4, finishedCount: 4, previewUrl: '/api/parents/stories/three-piglets/audio' },
  { storyId: 'tsivik-1', title: 'Ծիվիկը ճամփա է ընկնում', author: null, goal: 'Քաջություն',
    lesson: 'Առաջին քայլը ամենադժվարն է։', bedtimeSafe: true, minAge: 4, maxAge: 7, segmentCount: 4,
    reflectionQuestions: ['Ո՞ւր գնաց Ծիվիկը։'], reflectionConclusions: ['Գետի մոտ։'],
    listenCount: 2, finishedCount: 2, previewUrl: null,
    seriesId: 'tsivik', seriesIndex: 1, seriesTitle: 'Ծիվիկի մեծ ճանապարհը' },
  { storyId: 'tsivik-2', title: 'Ծիվիկը և գետը', author: null, goal: 'Համբերություն',
    lesson: 'Շտապելը ջրի մեջ չի օգնում։', bedtimeSafe: true, minAge: 4, maxAge: 7, segmentCount: 4,
    reflectionQuestions: ['Ինչպե՞ս անցավ գետը։'], reflectionConclusions: ['Կամրջով։'],
    listenCount: 0, finishedCount: 0, previewUrl: null,
    seriesId: 'tsivik', seriesIndex: 2, seriesTitle: 'Ծիվիկի մեծ ճանապարհը' },
];

const routes = {
  'POST /api/parents/login': () => ({ token: 'mock.jwt.token' }),
  'GET /api/parents/google-config': () => ({ clientId: null }),
  'GET /api/parents/me': () => ({ email: 'anna@example.am', emailVerifiedAt: ago(60 * 24 * 30) }),
  'GET /api/parents/devices': () => ({ deviceIds: [DEV1, DEV2] }),
  'GET /api/parents/devices/details': () => ({
    devices,
    summary: { totalDevices: 2, dormantDevices: 0, lastLoginAt: ago(3), emailVerifiedAt: ago(60 * 24 * 30) } }),
  'GET /api/conversations/summary': (q) => ({
    conversations: q.mode ? summaries.filter(s => (s.modes || []).includes(q.mode)) : summaries }),
  'GET /api/conversations/flagged': () => ({ messages: [
    { messageId: 'f0000001-0000-0000-0000-000000000001', conversationId: CONV[1],
      conversationStartedAt: ago(240), role: 'User', content: 'հիմար խաղ', timestamp: ago(235),
      safetyFlag: 'Flagged' } ] }),
  'GET /api/conversations/today-summary': () => ({
    deviceId: DEV1, asOfUtc: iso(now), dayStartUtc: iso(new Date().setUTCHours(0, 0, 0, 0)),
    dayStartLocal: iso(new Date().setHours(0, 0, 0, 0)).slice(0, 19),
    timeZoneId: 'Asia/Yerevan', timeZoneResolved: true,
    conversationsCount: 3, messagesCount: 44, flaggedMessagesCount: 1, assistantMessagesWithAudio: 12,
    newest: summaries.slice(0, 3).map(s => ({ id: s.id, startedAt: s.startedAt,
      firstUserSnippet: s.firstUserSnippet, messageCountToday: s.messageCount, flaggedMessageCountToday: 0 })),
    flagged: [{ id: CONV[1], startedAt: summaries[1].startedAt, firstUserSnippet: 'Խաղանք',
      messageCountToday: 14, flaggedMessageCountToday: 1 }] }),
  'GET /api/parents/stories': () => ({ stories, syncEnabled: true }),
  'GET /api/parents/music': () => ({ tracks: [] }),
  'GET /api/parents/story-requests': () => ({ requests: [
    { id: 'r0000001-0000-0000-0000-000000000001', type: 'story', text: 'Անիի ծննդյան օրվա մասին հեքիաթ',
      hasPhoto: true, status: 'in_review', createdAtUtc: ago(60 * 30), updatedAtUtc: ago(60 * 20) } ] }),
  'GET /api/children': () => ({ children: devices[0].children.map(c => ({ ...c, deviceId: DEV1 })) }),
};

function auditEvents() {
  const types = ['ParentDevicePauseStateChanged', 'ParentBedtimeWindowSet', 'ParentDeviceModeFlagsSet',
    'ParentDataExported', 'ParentDeviceStoryPausesSet', 'ParentDeviceRenamed'];
  return { events: types.map((t, i) => ({ id: 'e000000' + i + '-0000-0000-0000-00000000000' + i,
    timestamp: ago(60 * (i + 1) * 4), eventType: t, targetDeviceId: i % 2 ? DEV2 : DEV1,
    targetChildId: null, metadata: { is_paused: i % 2 === 0 } })) };
}

function storyPlays() {
  return { plays: [
    { storyId: 'ulik', finished: true, source: 'sd', playedAtUtc: ago(95), timeIsApproximate: false },
    { storyId: 'khosogh-dzuk', finished: false, source: 'sd', playedAtUtc: ago(60 * 20), timeIsApproximate: false },
    { storyId: 'three-piglets', finished: true, source: 'sd', playedAtUtc: ago(60 * 26), timeIsApproximate: true }],
    totals: [{ storyId: 'ulik', count: 12, finishedCount: 9 },
             { storyId: 'khosogh-dzuk', count: 7, finishedCount: 2 },
             { storyId: 'three-piglets', count: 4, finishedCount: 4 }], total: 3 };
}

function reflectionAnswers() {
  return { answers: [
    { storyId: 'ulik', questionIndex: 0, answerText: 'Որովհետև ձայնը մայրիկինը չէր, հաստ էր։',
      safetyFlag: 'Clean', createdAtUtc: ago(94) },
    { storyId: 'ulik', questionIndex: 1, answerText: 'Ես էլ դուռը չէի բացի, մայրիկին կզանգեի։',
      safetyFlag: 'Clean', createdAtUtc: ago(93) },
    { storyId: 'three-piglets', questionIndex: 0, answerText: 'Աղյուսից տունը, որովհետև ամուր է։',
      safetyFlag: 'Clean', createdAtUtc: ago(60 * 26) }], total: 3 };
}

http.createServer((req, res) => {
  const u = new URL(req.url, 'http://x');
  const q = Object.fromEntries(u.searchParams);
  const key = req.method + ' ' + u.pathname;
  const send = (o, code = 200) => {
    res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify(o));
  };

  if (u.pathname.startsWith('/api/')) {
    if (routes[key]) return send(routes[key](q));
    if (key.startsWith('GET /api/parents/audit')) return send(auditEvents());
    if (/\/story-plays$/.test(u.pathname)) return send(storyPlays());
    if (/\/reflection-answers$/.test(u.pathname)) return send(reflectionAnswers());
    if (/^GET \/api\/conversations\/[0-9a-f-]+$/.test(key))
      return send({ id: CONV[0], deviceId: DEV1, startedAt: ago(95), endedAt: ago(78),
        messageCount: messages.length, hasFlaggedContent: false, messages });
    if (/audio$/.test(u.pathname)) { res.writeHead(404); return res.end('{}'); }
    return send({ ok: true });               // every mutation succeeds
  }

  let p = u.pathname === '/' ? '/index.html' : u.pathname;
  const file = path.join(ROOT, p);
  if (!file.startsWith(ROOT) || !fs.existsSync(file)) { res.writeHead(404); return res.end('not found'); }
  const ext = path.extname(file);
  const type = { '.html': 'text/html; charset=utf-8', '.png': 'image/png', '.json': 'application/json',
                 '.webmanifest': 'application/manifest+json' }[ext] || 'application/octet-stream';
  res.writeHead(200, { 'Content-Type': type });
  res.end(fs.readFileSync(file));
}).listen(5099, () => console.log('mock backend on http://127.0.0.1:5099'));

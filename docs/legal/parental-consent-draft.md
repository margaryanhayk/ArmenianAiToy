# Parental consent, data map, COPPA/GDPR-K wording — draft (N8, 2026-09-12)

**Status: DRAFT ONLY. Not legal advice. Nothing in this file has been reviewed
by a lawyer.** It is written for the owner, to support a conversation with a
lawyer before anything here is published or implemented. Every proposed
public-facing sentence below is marked "PROPOSED — drafted to address ___",
never "compliant with ___". `privacy.html`, `terms.html`, and the
registration flow are **not touched** by this slice; §3's consent-step
proposal is a **HARD STOP** — no code changes ship from this document
without the owner's explicit go-ahead and a lawyer's review.

This document was produced by grepping/reading the actual backend and
frontend code (file:line citations throughout) — not by describing the
product from memory. Citations were spot-checked but a full line-by-line
re-read by a human before publishing anything derived from it is still
warranted; code moves.

---

## 1. Data map

Legend for **Third parties**: **OpenAI** = chat text, speech-to-text audio,
moderation text, and (default) text-to-speech text; **ElevenLabs** = text-to-speech
text only, **opt-in** (`AI:TtsProvider=elevenlabs`), never child audio;
**Gemini** = chat text only, **opt-in** (`AI:ChatProvider=gemini`); **Google**
= only the sign-in ID token itself, for verification; **Resend** = parent
email address + transactional email text; **Railway** = the whole database
and file volumes, as the hosting provider (sees everything at rest, standard
for any host).

| # | Category | Stored where | Who can read it | Retention | Third parties | Export / delete |
|---|---|---|---|---|---|---|
| 1 | Parent email, password hash, terms acceptance | `Parent.Email`, `PasswordHash`, `TermsAcceptedAt`, `TermsVersion`, `RegisteredAt`, `LastLoginAt`, `EmailVerifiedAt` — `backend/src/ArmenianAiToy.Domain/Entities/Parent.cs:6-25` | Parent (own record); operator (internal console); system (auth) | Until account deletion; **or** anonymized after `Dormancy:Parent:AnonymizeAfterDays` (default `0` = disabled) + 7-day grace past the warn stage (`RetentionPurgeService.cs:123,135,632-784`) | None for password hash. **Resend** gets the email address for transactional mail (already disclosed, `privacy.html:172-176`) | `GET /api/parents/export` includes profile fields, **excludes** `PasswordHash` (`ParentExport.cs:27-90`, exclusion list `ParentService.cs:2271-2280`). `DELETE /api/parents/account` hard-deletes the row (`ParentService.cs:1288-1374`) |
| 2 | Google sign-in ID (`GoogleSubject`) | `Parent.cs` (Google linkage field) | Parent (export); system (login match) | Same as row 1; nulled on dormancy anonymize (`RetentionPurgeService.cs:632-784`) | **Google** — only the ID token is sent for verification (`GoogleIdTokenValidator.cs:50-66`); Google returns subject/email/emailVerified, nothing else is sent to Google | Included in export as "user-owned, not credential material" (`ParentExport.cs:74-90`); deleted with account |
| 3 | Device identity (MAC, API key hash, claim-code hash, name, timezone) | `Device.cs:6,7,17,25,169,217` | Parent (own linked devices); operator (fleet console) | Kept while linked; reset to factory defaults (not deleted) if the **last** parent unlinks (`ParentService.cs:721-831`); hard-deleted only after `Dormancy:Devices:DeleteAfterDays` (default `0` = disabled, `RetentionPurgeService.cs:171`) | None — MAC/API key never leave the backend | Export includes device fields, **excludes** `ApiKey`/`ApiKeyHash` (`ParentExport.cs:98-131`, exclusion list). Unlink resets the device row; account/child delete cascades it |
| 4 | Child profile (first name, birth year, gender) | `Child.cs:8,16,17` | Parent (own child); operator (support) | Until child/device/account deleted | None sent as structured profile data. **Note:** if the child's name is ever woven into a conversation reply Areg speaks, that text — like any reply — passes through the same OpenAI/ElevenLabs pipeline as row 6; this file did not find or rule out that behavior and it should be confirmed before publishing | Included in export (`ParentExport.cs:144-151`). `DELETE /api/parents/children/{childId}` hard-deletes the row and its conversations (`ParentService.cs:1376-1430`) |
| 5 | Child voice recordings (and Areg's spoken replies) | Filesystem, not the DB — `LocalDiskAudioBlobStore.cs`, path `{Audio:BlobStoreRoot}/{conversationId}/{messageId}.{ext}`; `Message.AudioBlobPath` (`Message.cs:14`) points to it | Parent (listen/download via `GET /api/parents/messages/{id}/audio` and `.../child-audio`); system (STT/TTS pipeline) | Deleted with the conversation — default 90 days (`Retention:Messages:MaxAgeDays`, `appsettings.json:86`) via cascading conversation purge (`RetentionPurgeService.cs:352-435`); an **off-by-default** orphan sweep also exists (`Retention:AudioOrphanSweep:*`, not shipped in appsettings, code default `0`=disabled) | **OpenAI** receives the raw child audio for speech-to-text (`AudioChatController.cs:183-184`). ElevenLabs/Gemini never receive audio | Assistant replay + child-audio download endpoints above; deleted via conversation/child/account delete or the 90-day purge |
| 6 | Transcripts (what the child said, what Areg replied) | `Message.Content` (`Message.cs:11`) | Parent (dashboard, **and the raw export** — `ConversationDto`/`MessageDto`); operator (moderation review, flagged content) | 90 days default, same purge as row 5 | **OpenAI** (chat reply generation, moderation of both directions — `ChatService.cs:1729,1606,2073,2267,2334,2558`); **ElevenLabs** (reply text only, opt-in); **Gemini** (chat text only, opt-in) | Included verbatim in `GET /api/parents/export` (`ConversationDto.cs:5-38`); deleted via conversation/child/account delete or the 90-day purge |
| 7 | Story plays | `StoryPlay.cs` — `DeviceId`, `PlayedAtUtc`, no free text | Parent (dashboard); operator | **Not covered by the 90-day message purge** — persists indefinitely on an active device; only cleared if the child/device is deleted or the **last** parent unlinks the device (`ParentService.cs:774-785`) | None | Included in export (`ParentExport.cs` init-only `StoryPlays`); deleted only via the paths above, not by time |
| 8 | Reflection answers (child's spoken answer to a post-story question) | `StoryReflectionAnswer.AnswerText`, `SafetyFlag` (`StoryReflectionAnswer.cs:39,41`) | Parent (dashboard/export); operator | **Same gap as row 7** — not time-purged; kept until child/device deleted or last-parent unlink | Same pipeline as row 6 for the transcription/moderation step (this file did not independently re-verify a separate code path, but `AnswerText` is transcribed child speech and should be assumed to go through STT/moderation like any other turn) | Included in export; deleted only via the paths above |
| 9 | Audit events (sensitive parent/operator actions) | `AuditEvent.cs` — `ActorParentId`, `TargetDeviceId`, `TargetChildId`, PII-free `Metadata` (:31,34,37,43) | Parent (own actions only, filtered by `ActorParentId`, `ParentService.cs:2129-2132`); operator (full feed) | **Kept indefinitely — append-only by design** (CLAUDE.md § Architecture: "append-only, FK-free"). Survives dormancy anonymize by design ("audit anchor") and, confirmed in this slice, **survives account deletion**: `DeleteAccountAsync` writes a fresh `ParentAccountDeleted` audit row referencing the now-deleted `parentId` (`ParentService.cs:1362-1366`), and the table has no FK to `Parent` so nothing cascades it away | None | Parent's own rows appear in their export; **no delete endpoint exists for audit rows, including after account deletion** — this is a genuine, standing gap between "delete my account" and "delete every record naming me," and needs disclosure (see §2) |
| 10 | IP addresses | Never persisted to the DB. In-memory only: `AuthRateLimiter.cs:56-59` (auth endpoints, per-IP rate limit bucket); `ChatRateLimiter.cs:30-35` keys on device id, **not** IP. A bounded startup diagnostic logs the first 3 requests' `RemoteIpAddress` + `X-Forwarded-For` to structured console logs only when `ForwardedHeaders:Enabled=true` (`Program.cs:391-404`). An opt-in, off-by-default token-IP-binding embeds the IP into a signed token, never into a log or the DB (`StoryAudioTokenController.cs:82`) | System only; an operator with console/log access could see the bounded 3-request startup log | In-memory buckets: process lifetime only. Console logs: whatever the hosting platform's log retention is (outside this repo's control) | None | Not exportable/deletable (not a DB row); **not disclosed in `privacy.html` at all today** — flagged as a gap in §2 |
| 11 | Cost / usage counters | `DeviceUsageDay.cs` — `DeviceId`, `DayUtc`, `Questions`, `EstimatedUsd` (:32,34,35) | Parent sees **question count + tier/allowance only** (`EstimatedUsd` is never surfaced to a parent), and only when `Usage:Tiers:Enabled=true` (`ParentService.cs:1947-2009`); operator (fleet cost monitoring, not independently confirmed in this pass) | **No purge job was found for this table** — it is written unconditionally on every metered turn (`DeviceUsageDay.cs:9-13`) and appears to accumulate one row per device per day indefinitely | None | **Not included in `GET /api/parents/export`** and **not disclosed in `privacy.html`** — both flagged as gaps in §2 |
| 12 | Backups (DB snapshot, audio-blob archive, upload archive) | `DatabaseBackupService.cs` — daily local SQLite `VACUUM INTO` snapshot (`areg-backup-YYYYMMDD.db`), a same-shape audio-blob zip (`areg-audio-blobs-YYYYMMDD.zip`, on by default, 500 MB cap), an uploads zip — all on the same volume, rotated to the last `Backup:*:KeepCount` (default 7) copies | Operator only, via the bearer-gated `GET /api/internal/backup` pull | ~7 days of daily snapshots on the same volume (same-volume protection, not offsite by design — `DatabaseBackupService.cs:26-33`); any offsite copy an operator pulls and stores elsewhere is outside this repo's control/retention | None automatically | **Not disclosed in `privacy.html` at all** (flagged in §2). Practical consequence for parents: after `DELETE /api/parents/account` (or any other delete), the data is gone from the live database immediately, but **a copy of it can still exist inside a same-volume backup snapshot for up to ~7 days** until that snapshot rotates out — this should be said plainly to parents, not implied |

### Gaps `privacy.html` does not currently disclose (checked by grep against the live file)

Confirmed present in the code but **absent** from `wwwroot/privacy.html`:

- **ElevenLabs as a TTS sub-processor.** `privacy.html:165-170` names only OpenAI as "speaks it back." The code has a fully wired ElevenLabs adapter, selected via `AI:TtsProvider=elevenlabs` (`DependencyInjection.cs:151-172`). If any environment ever runs with that flag set, the processor table in `privacy.html` §5 is inaccurate for that environment, not just incomplete. **Action needed regardless of today's flag value**: either name ElevenLabs conditionally ("if voice is ElevenLabs, not OpenAI, this is who receives the reply text") or confirm and pin the flag to `openai` everywhere before publishing an unconditional claim.
- **Gemini as a chat sub-processor**, same conditional issue, for `AI:ChatProvider=gemini`.
- **IP address handling** (row 10 above) — not mentioned anywhere in `privacy.html`.
- **Cost/usage counters** (row 11) — not mentioned.
- **Backups** (row 12) — not mentioned; also the practical "deleted from the live DB but may still be in a backup for ~7 days" point isn't said anywhere.
- **Reflection answers as their own retained category** (row 8) — `privacy.html:118-125` describes "what is said" generically but doesn't call out that reflection answers, specifically, are **not** covered by the 90-day auto-delete the same section implies for "conversations/recordings" (`privacy.html:197`).
- **Story plays not time-purged** (row 7) — same shape of gap as above; a parent reading "conversations auto-delete after 90 days" would reasonably assume story-play history does too, and it does not.
- **Audit events surviving account deletion** (row 9) — `privacy.html:127-130,199` describes the activity log but does not say it can outlive account deletion.

None of these are proposed as fixes to `privacy.html` in this slice — that page is out of scope here — but the owner should treat them as required edits to the *next* privacy-policy revision, ideally the same one that carries whatever comes out of §2/§3 below.

---

## 2. Proposed wording (English + Armenian + Russian drafts)

**None of this is proposed as a drop-in replacement for `privacy.html`/`terms.html`.** It is starting language for the owner and a lawyer to edit, cut, and place. Plain sentences are used deliberately; replace with legalese only where a lawyer says a plain sentence isn't enough.

### 2a. COPPA-style "Notice to parents" (PROPOSED — drafted to address the FTC's COPPA direct-notice content requirements; not a claim of COPPA compliance)

**English (proposed):**

> **Notice to parents, before you create an account.**
> Areg is made for children roughly 4–7 years old. Only a parent or legal
> guardian may create an account — your child never has their own login.
> By registering, **you** are confirming you are that child's parent or
> guardian, and you are the one consenting to what is described below.
>
> **What we collect about your child:** the first name, birth year, and
> gender you tell us (used only to speak Armenian correctly and to keep
> content age-appropriate); a recording of what your child says to the
> toy, and the text of what they said and how Areg replied; whether an
> automatic safety check flagged anything; which stories and games were
> played and when.
>
> **Why:** so Areg can answer your child in their own language, keep the
> conversation age-appropriate and safe, and so you can see what happened
> in your dashboard.
>
> **What we do not do:** we do not show your child ads, we do not build an
> advertising profile of your child, and we do not sell your child's data
> to anyone, for any reason. We do not ask your child to act like Areg is
> their friend who misses them when the toy is off — see our product
> promise in `CLAUDE.md`'s Absence Test, which the same rule that keeps
> that language out of the toy also keeps out of how we describe it to you.
>
> **How long we keep it:** what your child says and the recordings of it
> are deleted automatically 90 days after the conversation, by default.
> [PLACEHOLDER — pending the fix to gaps 7/8 above: state the real
> retention for story plays and reflection answers once decided, rather
> than implying they follow the same 90-day rule.]
>
> **Withdrawing consent:** you can delete your child's profile, delete a
> single conversation, unlink the toy, or delete your whole account at any
> time from the parent dashboard — see [export/delete instructions link].
> Deleting your account removes it and its data immediately from our live
> systems; a copy may remain in a backup for up to about a week before it
> is rotated out.

**Armenian draft — reviewed and corrected by the armenian-linguistic-reviewer
agent** (original machine draft had a false-friend word choice for
"login," an agreement error, and several English-syntax calques; all
fixed below — see the commit history on this file for the pre-review
version if a side-by-side is ever needed):

> **Ծանուցում ծնողներին՝ մինչև հաշիվ ստեղծելը։**
> Areg-ը նախատեսված է մոտավորապես 4–7 տարեկան երեխաների համար։ Հաշիվ կարող
> է ստեղծել միայն ծնողը կամ օրինական խնամակալը։ Երեխան երբեք չունի իր
> սեփական մուտքը։ Գրանցվելով՝ Դուք հաստատում եք, որ այդ երեխայի ծնողը կամ
> խնամակալն եք, և հենց Դուք եք տալիս ստորև նկարագրված համաձայնությունը։
>
> **Ինչ ենք հավաքում Ձեր երեխայի մասին՝**
> - Ձեր նշած անունը, ծննդյան տարեթիվը և սեռը (օգտագործվում է միայն
>   հայերենը ճիշտ խոսելու և բովանդակությունը տարիքին համապատասխանեցնելու
>   համար)
> - Ձեր երեխայի ասածի ձայնագրությունը և տեքստը, ինչպես նաև Areg-ի
>   պատասխանի տեքստը
> - արդյոք ավտոմատ անվտանգության ստուգումն ինչ-որ բան նշել է որպես
>   խնդրահարույց
> - որ հեքիաթներն են լսվել, որ խաղերն են խաղացվել, և երբ
>
> **Ինչու ենք դա հավաքում՝** որպեսզի Areg-ը կարողանա ճիշտ դիմել Ձեր
> երեխային և հասկանալի լեզվով խոսել նրա հետ, խոսակցությունը մնա տարիքին
> համապատասխան ու անվտանգ, և Դուք կարողանաք ամեն ինչ տեսնել Ձեր
> վահանակում։
>
> **Ինչ չենք անում՝** մենք Ձեր երեխային գովազդ չենք ցուցադրում, Ձեր
> երեխայի համար գովազդային պրոֆիլ չենք կազմում, և Ձեր երեխայի տվյալները
> ոչ մեկին և ոչ մի պատճառով չենք վաճառում։
>
> **Որքան ժամանակ ենք պահում՝** Ձեր երեխայի ասածը և դրա ձայնագրությունը
> լռելյայն ինքնաբերաբար ջնջվում են խոսակցությունից 90 օր հետո։
> [ՏԵՂԱԴԻՐ — սպասվում է վերևի 7/8 բացերի փակմանը]
>
> **Համաձայնությունը հետ վերցնելը՝** Դուք կարող եք ցանկացած պահի ջնջել
> Ձեր երեխայի պրոֆիլը, ջնջել առանձին մեկ խոսակցություն, անջատել խաղալիքը
> կամ ջնջել ամբողջ հաշիվը ծնողական վահանակից։ Հաշիվը ջնջելիս, այն և դրա
> տվյալներն անմիջապես հեռացվում են մեր աշխատող համակարգերից։ Պահուստային
> պատճենում այն կարող է մնալ մոտավորապես մեկ շաբաթ, մինչև հերթական
> պահուստը փոխարինի այն։

**Russian draft (machine-assisted, not independently reviewed — flag to owner):**

> **Уведомление для родителей перед созданием учётной записи.**
> Areg предназначен для детей примерно 4–7 лет. Учётную запись может
> создать только родитель или законный опекун — у ребёнка никогда не
> будет собственного входа. Регистрируясь, **вы** подтверждаете, что
> являетесь родителем или опекуном этого ребёнка, и именно вы даёте
> согласие, описанное ниже.
>
> **Что мы собираем о вашем ребёнке:** указанные вами имя, год рождения и
> пол (используются только для правильной речи на армянском и для
> возрастной адаптации контента); запись того, что ребёнок говорит
> игрушке, и текст этого, а также текст ответа Areg; была ли
> автоматическая проверка безопасности чем-то обеспокоена; какие сказки и
> игры были воспроизведены и когда.
>
> **Зачем:** чтобы Areg мог отвечать вашему ребёнку на его языке,
> разговор оставался безопасным и соответствующим возрасту, и чтобы вы
> могли видеть происходящее в своей панели.
>
> **Чего мы не делаем:** мы не показываем ребёнку рекламу, не создаём
> рекламный профиль ребёнка и не продаём данные ребёнка никому и ни по
> какой причине.
>
> **Как долго храним:** сказанное ребёнком и запись этого автоматически
> удаляются через 90 дней после разговора, по умолчанию.
> [МЕСТО-ЗАПОЛНИТЕЛЬ — см. пробелы 7/8 выше]
>
> **Отзыв согласия:** вы можете в любой момент удалить профиль ребёнка,
> отдельный разговор, отвязать игрушку или удалить всю учётную запись из
> родительской панели. Удаление аккаунта немедленно удаляет его и его
> данные из наших рабочих систем; копия может оставаться в резервной
> копии около недели до её ротации.

### 2b. GDPR-K / UK-style section (PROPOSED — drafted to address Art. 6/8 GDPR-style requirements for a minor's data under parental consent; not a claim of GDPR compliance, which additionally needs a lawyer's read on Armenia's own data-protection law and any applicable EU/UK exposure)

**English (proposed):**

> **Lawful basis.** We process your child's data on the basis of your
> consent as their parent or guardian (Article 6(1)(a)/Article 8-style
> basis for information-society services offered to a child), and on the
> basis of performing our contract with you (running the service you
> signed up for).
>
> **Children's data.** Areg is built for children who are, under most
> applicable frameworks, below the age at which a child can consent for
> themselves. We rely on your consent as the parent/guardian, not the
> child's.
>
> **Where your data is processed.** Our servers and our processors
> (below) are located in the United States. If you are in the EU, UK, or
> Armenia, this means your and your child's data crosses borders to reach
> them. [PLACEHOLDER — a lawyer should confirm what cross-border transfer
> mechanism, if any, is actually in place; this draft does not assert one.]
>
> **Sub-processors** (see §2c for the two AI ones in detail):
> - OpenAI (USA) — speech-to-text, chat replies, safety moderation, and
>   (by default) text-to-speech.
> - ElevenLabs (USA) — text-to-speech, only if this specific deployment is
>   configured to use it instead of OpenAI's.
> - Google (USA) — sign-in verification only, if you choose to sign in
>   with Google.
> - Resend (USA) — delivering our transactional emails to you.
> - Railway (USA) — hosting: our database and file storage run on their
>   infrastructure.
>
> **Your rights** (subject to a lawyer confirming which of these apply
> under which law for your location): access what we hold (export),
> correct it, delete it, object to a specific use, and complain to your
> local data-protection authority. A parent/guardian exercises these
> rights on behalf of their child; a child does not have their own account
> to exercise them directly.
>
> **Contact:** [owner's contact email from `privacy.html` §13].

**Armenian draft — reviewed and corrected by the armenian-linguistic-reviewer
agent** (original machine draft had a non-parallel legal-basis sentence,
two missing appositive commas, a terminology mismatch between "processors"
and "sub-processors," and an unnecessary English loanword; all fixed
below):

> **Իրավական հիմք՝** Մենք մշակում ենք Ձեր երեխայի տվյալները Ձեր՝ որպես
> ծնողի կամ խնամակալի, տված համաձայնության հիման վրա, ինչպես նաև Ձեզ հետ
> կնքված պայմանագիրը (Ձեր գրանցված ծառայությունը մատուցելը) կատարելու
> հիման վրա։
>
> **Երեխաների տվյալները՝** Areg-ը ստեղծված է երեխաների համար, ովքեր
> կիրառելի իրավական շրջանակների մեծամասնության համաձայն դեռևս չեն հասել
> այն տարիքին, երբ կարող են ինքնուրույն համաձայնություն տալ։ Մենք հենվում
> ենք Ձեր՝ որպես ծնողի/խնամակալի, համաձայնության վրա, ոչ թե երեխայի։
>
> **Որտեղ են մշակվում տվյալները՝** Մեր սերվերները և ստորև նշված
> ենթամշակողները գտնվում են ԱՄՆ-ում։ [ՏԵՂԱԴԻՐ — իրավաբանը պետք է հաստատի
> սահմանահատող փոխանցման մեխանիզմը, եթե այդպիսին կա]։
>
> **Ենթամշակողներ** (մանրամասն՝ 2c բաժնում)՝
> - OpenAI (ԱՄՆ) — խոսքից տեքստի փոխակերպում, խոսակցության պատասխաններ,
>   անվտանգության ստուգում և (լռելյայն) տեքստից խոսքի փոխակերպում։
> - ElevenLabs (ԱՄՆ) — տեքստից խոսքի փոխակերպում, միայն եթե տվյալ
>   կոնֆիգուրացիան օգտագործում է այն՝ OpenAI-ի փոխարեն։
> - Google (ԱՄՆ) — միայն մուտքի հաստատում, եթե ընտրում եք Google-ով մուտք
>   գործել։
> - Resend (ԱՄՆ) — Ձեզ ուղարկվող ծառայողական նամակների առաքումը։
> - Railway (ԱՄՆ) — հոսթինգ՝ մեր տվյալների բազան և ֆայլերի պահեստը
>   աշխատում են նրանց ենթակառուցվածքի վրա։
>
> **Ձեր իրավունքները՝** հասանելիություն ունենալ Ձեր տվյալներին
> (արտահանում), դրանք ուղղել, ջնջել, առարկել կոնկրետ օգտագործմանը, և
> բողոքարկել Ձեր տարածաշրջանի տվյալների պաշտպանության մարմնին։ Այս
> իրավունքներն իրականացնում է ծնողը/խնամակալը՝ երեխայի անունից։
>
> **Կապ՝** [owner-ի կոնտակտային էլ. հասցեն privacy.html §13-ից]։

**Russian draft (machine-assisted):**

> **Правовое основание.** Мы обрабатываем данные вашего ребёнка на
> основании вашего согласия как родителя/опекуна, а также в целях
> исполнения договора с вами (предоставление зарегистрированного вами
> сервиса).
>
> **Данные детей.** Areg рассчитан на детей, которые по большинству
> применимых правовых рамок ещё не достигли возраста самостоятельного
> согласия. Мы полагаемся на согласие родителя/опекуна, а не ребёнка.
>
> **Где обрабатываются данные.** Наши серверы и обработчики (ниже)
> находятся в США. [МЕСТО-ЗАПОЛНИТЕЛЬ — юрист должен подтвердить механизм
> трансграничной передачи, если таковой применяется].
>
> **Субобработчики:** OpenAI (США), ElevenLabs (США, только при
> соответствующей конфигурации), Google (США, только вход), Resend (США,
> письма), Railway (США, хостинг).
>
> **Ваши права:** доступ (экспорт), исправление, удаление, возражение
> против конкретного использования, жалоба в орган по защите данных.
> Эти права реализует родитель/опекун от имени ребёнка.
>
> **Контакт:** [адрес из privacy.html §13].

### 2c. ElevenLabs / OpenAI sub-processor disclosure (PROPOSED, English — the primary language for a sub-processor table; Armenian/Russian summaries can reuse the "Sub-processors" bullets in §2b if the owner wants trilingual parity here too)

> | Sub-processor | What they receive | What they do with it | Configuration |
> |---|---|---|---|
> | **OpenAI** (USA) | Raw audio of what your child says (for speech-to-text); the resulting text and Areg's reply text (for generating and moderating the reply); by default, the reply text again (for text-to-speech) | Converts speech to text, generates the reply, checks both directions for unsafe content, converts the reply back to speech | Always active for chat and moderation. Active for text-to-speech unless ElevenLabs is configured instead (`AI:TtsProvider`) |
> | **ElevenLabs** (USA) | Areg's reply text only — never child audio, never the child's own words | Converts the reply text to speech | Only active if this deployment sets `AI:TtsProvider=elevenlabs` instead of the default |
>
> Neither company is authorized to use this data to train their own general-purpose models on our behalf; that assurance rests on our agreement with each provider, not on anything this codebase can enforce — a lawyer should confirm the actual contract terms before this line is published as a promise to parents.

---

## 3. Verifiable parental-consent proposal for registration — **HARD STOP**

**Nothing in this section is implemented.** It is options for the owner to
choose from; whichever is chosen still needs a plan + explicit owner
approval before a backend-implementer touches `ParentController`,
`ParentService`, the `Parent` entity, or any registration UI, per
CLAUDE.md's "never touch auth without a plan and owner approval" rule.

**What exists today, confirmed by reading the code:**
- `Parent.cs:18,25` already has `TermsAcceptedAt` (`DateTime?`) and
  `TermsVersion` (`string?`), written at registration
  (`ParentService.cs:234-243`).
- `ParentController.cs:61-99` rejects registration with 400 if
  `!request.AcceptedTerms`; `ParentService.cs:205-266` throws the same
  check again server-side as defense in depth.
- `wwwroot/parent.html:1048-1069` has a real checkbox
  (`#signupAcceptTerms`) the user must tick, linked to `/terms.html` and
  `/privacy.html`, gating the submit button client-side
  (`parent.html:3383,3389`).
- **`mobile/AregParent/src/api.ts:132` hardcoded `acceptedTerms: true` on
  every register call. The mobile registration screen showed no
  terms/privacy link and no checkbox at all** — a parent registering
  from the phone app was recorded as having accepted terms they were
  never shown. This was not a "verifiable consent" gap so much as a
  **shipping falsehood in the audit trail** (`TermsAcceptedAt` stamped as
  if the parent agreed, and they were never asked). **Fixed (N9,
  2026-09-12):** `LoginScreen.tsx` now shows an unchecked-by-default
  checkbox (same wording as `parent.html`'s `#signupAcceptTerms`) with
  links to `/terms.html` and `/privacy.html`, gates the register button
  until checked, and `api.ts`'s `register()` sends the checkbox's real
  state instead of a literal `true`. This closes the audit-trail
  falsehood; it does **not** implement any of the "verifiable parental
  consent" options below — that design decision is still open and still
  the owner's.

**What today's flow does NOT establish:** that the *account holder* is
actually an adult, and that they are actually the child's parent/guardian
— today it establishes only "someone ticked a box" (web) or "someone
tapped register" (mobile, no box at all). None of today's flow is a
COPTA-style "verifiable parental consent" mechanism by itself.

**Options:**

**(a) Email-verify + explicit consent checkbox, versioned (recommended).**
Reuse the existing email-verification flow (already built —
`ParentEmailVerificationToken`) as the "verify you own this email"
half, and reuse/extend `TermsAcceptedAt`/`TermsVersion` into a distinct
`ConsentAcceptedAt`/`ConsentVersion` pair so "I accept the Terms" and "I
am this child's parent/guardian and I consent to this data collection"
are two separately-recorded facts, not one checkbox doing both jobs.
- **Cost/effort:** LOW. One migration (two nullable columns on `Parent`,
  same shape as the existing `TermsAcceptedAt`/`TermsVersion` pair); one
  new required checkbox + copy on `parent.html`'s signup view; the actual
  missing piece on mobile (a real checkbox + links, replacing the
  hardcoded `true`); no new third-party integration.
- **What it does NOT prove:** that the person is actually an adult (a
  10-year-old can tick a box and verify an email just as easily as an
  adult). This is COPPA's own documented weak spot for the
  "email plus" method's cheaper variant, not a flaw specific to this
  proposal.

**(b) "Email plus" — a second, independent confirmation.**
Same as (a), plus one more low-friction adult-signal: e.g. a second email
confirmation click from a different context (a delayed follow-up email
requiring a second explicit "yes, I am the parent" click, or a
phone-number SMS confirmation).
- **Cost/effort:** MEDIUM. Needs either SMS (a new third-party
  integration and its own cost/privacy footprint) or a second delayed
  email step (no new integration, but new state to track — a
  `ConsentSecondConfirmationAt` field and a scheduled reminder, similar
  shape to the existing password-reset/email-verification token
  machinery).
- Materially better evidentiary trail than (a) alone; still not proof of
  adulthood, just proof of a second deliberate action.

**(c) A small, refundable card charge (COPPA's "verifiable" gold
standard in the US).**
A $0.50-style authorization (not captured) that only a card-holder could
complete.
- **Cost/effort:** HIGH, and **not recommended for this product today**:
  it requires a payment processor integration this codebase does not
  have at all (no NuGet package, no vendor account — a new dependency
  CLAUDE.md's "never add a NuGet package... without a plan and owner
  approval" already gates); it assumes every Armenian parent has a card
  usable for an international micro-authorization, which is a real
  friction point for the actual target market; and it adds a compliance
  surface (PCI scope, even for a pass-through processor) disproportionate
  to a pre-100-units, non-US-headquartered product whose primary
  regulatory exposure is Armenian law and general GDPR-style hygiene, not
  US COPPA enforcement specifically.

**Recommendation:** (a). The mobile hardcoded-consent bug itself is fixed
(N9) independent of which option is chosen — that part was never really a
design decision, it was a defect. (b) is worth revisiting
once there is an actual US-market/COPPA-enforcement reason to want a
stronger evidentiary trail; (c) is not recommended at this stage for the
reasons above.

**Exact code touch-points for whichever option is approved** (so the
owner can scope a slice precisely):
- `backend/src/ArmenianAiToy.Domain/Entities/Parent.cs` — new
  `ConsentAcceptedAt`/`ConsentVersion` fields (or a documented decision to
  reuse `TermsAcceptedAt`/`TermsVersion` for both purposes, if the owner
  and lawyer decide one checkbox covering both is acceptable).
- A new hand-written EF migration (never `dotnet-ef` in this
  environment, per CLAUDE.md).
- `backend/src/ArmenianAiToy.Api/Controllers/ParentController.cs:61-99`
  (`Register`) — new required field(s) on the request DTO.
- `backend/src/ArmenianAiToy.Application/Services/ParentService.cs:205-266`
  (`RegisterAsync`) — persist the new consent fields.
- `wwwroot/parent.html:1048-1069` — new/updated checkbox copy (route
  through `ux-ui-designer` per CLAUDE.md, this is a UI change).
- `mobile/AregParent/src/screens/LoginScreen.tsx` and
  `mobile/AregParent/src/api.ts` — checkbox/links landed (N9); the
  distinct `ConsentAcceptedAt`/`ConsentVersion` fields this option
  proposes are still not implemented.
- `wwwroot/privacy.html` — the gaps listed in §1 should land in the same
  revision as this change, not a separate one, so the policy a parent
  agrees to actually describes what happens to their data.

---

## 4. Box / app-store listing checklist (children's product, ages 4–7)

- State the age range plainly: "For children ages 4–7, with adult
  supervision for setup."
- Say setup requires an adult: pairing, Wi-Fi provisioning, and account
  creation are parent steps, not something handed to the child to do
  alone.
- Say there is no child account: only a parent/guardian has a login; the
  child never sees a sign-in screen.
- Microphone notice: state clearly that the toy has a microphone, when it
  listens (button-press-gated, not always-on), and that recordings are
  sent to our servers and to OpenAI (and ElevenLabs, if applicable) to
  generate a reply — this belongs on the box/listing, not buried only in
  a linked privacy policy.
- State plainly there are no ads and nothing is sold to advertisers, if
  that remains true (it does, per today's `privacy.html` and this
  document's data map) — this is a real differentiator for a children's
  product and worth saying up front, not just in the fine print.
- Link to the privacy policy and terms directly from the box/listing, not
  only from within the app/dashboard.
- App-store-specific: both major stores require answering a
  "data collected from children" questionnaire (Apple's App Privacy
  "Data Used to Track You" / "Data Linked to You" categories under age
  gates; Google Play's Data Safety section plus its Families program
  policies if the app targets children) — this needs a lawyer or the
  owner to fill out directly against §1's data map when the app is
  actually submitted; not attempted here since no submission is happening
  in this slice.

---

## 5. Status

`docs/business-readiness-2026-09-12.md` item 10 and `CLAUDE.md`'s "State of
the toy" section are updated in this same commit to read: **"draft ready
(N8), owner + lawyer sign-off, consent step HARD STOP."**

**What was verified:** every file:line citation above was read directly
from the code on `claude/n8-legal-draft` (branched from
`claude/n7-mobile-parity`) during this session; the `Resend`/`Railway`/
`OpenAI`/`Google` sub-processor names were cross-checked against the live
`wwwroot/privacy.html` text, not invented; the mobile hardcoded-consent
finding was confirmed by reading `mobile/AregParent/src/api.ts` and
`LoginScreen.tsx` directly, not inferred; both Armenian drafts in §2 were
reviewed by the armenian-linguistic-reviewer agent, which flagged one
false-friend word choice, one grammar-agreement error, and several
English-syntax calques — all corrected in the text above (the agent's
full finding-by-finding report is in this session's record, not
reproduced here to keep the file readable).

**What was NOT done, and must happen before any of this reaches a parent:**
no lawyer has read any word of this file; the Russian
text is machine-assisted only and has had no linguistic review at all;
`privacy.html`, `terms.html`, and the registration code are all untouched;
nothing here has been published, and nothing here is a claim that Areg
complies with COPPA, GDPR, GDPR-K, or any other law.

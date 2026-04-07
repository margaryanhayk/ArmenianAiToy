# Parent-history endpoint — 100-test report

**Run id:** 20260407-203026  
**API base:** http://localhost:5000  
**Endpoint under test:** `GET /api/conversations/{conversationId}`  
**Total tests:** 100

## 1. Summary

Ran 100 real HTTP calls against the live local API on http://localhost:5000. Owned conversations were created for Parent A, non-owned conversations for Parent B, and fake ids were random GUIDs. Owned ids must return `200`; both non-owned and fake ids must return `404` (uniform 404 = no existence disclosure).

**Result:** 100/100 passed, 0 failed.

## 2. Test distribution

| Category | Count | Expected status |
|---|---|---|
| owned (Parent A) | 40 | 200 |
| non_owned (Parent B) | 30 | 404 |
| fake (random GUID) | 30 | 404 |
| **Total** | **100** | — |

## 3. Pass / fail counts

| Category | Passed | Failed |
|---|---|---|
| owned | 40 | 0 |
| non_owned | 30 | 0 |
| fake | 30 | 0 |
| **Total** | **100** | **0** |

## 4. Failures (grouped)

_No failures._
## 5. Sample responses

### owned — T001

- conversationId: `2EEFC4AC-966A-4858-B4F9-9B2362271448`
- expected status: 200
- actual status: 200
- passed: True
- notes: messages=2 deviceIdMatches=True

```
{"conversation":{"id":"2eefc4ac-966a-4858-b4f9-9b2362271448","deviceId":"769d0a4c-cc4a-4116-8999-7358d2ee0ff1","startedAt":"2026-04-07T20:30:27.944905","endedAt
```

### non_owned — T041

- conversationId: `A0CFD828-C038-475A-932C-238257BA5710`
- expected status: 404
- actual status: 404
- passed: True
- notes: (none)

```
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-86405b2e03e1d38738a094ca502a3885-aece09995fc31522-00
```

### fake — T071

- conversationId: `09A0F211-6D7E-454F-A284-05E536E4C4A5`
- expected status: 404
- actual status: 404
- passed: True
- notes: (none)

```
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-8aa6e300aa9f99d47a1c1681b673d59c-eea9f3b5714950be-00
```

## 6. Setup assumptions

- The local API is running on `http://localhost:5000` with the SQLite DB at `backend/src/ArmenianAiToy.Api/armenian_ai_toy.db`.
- Parent A and Parent B were registered via real HTTP `POST /api/parents/register`. Parent A was logged in via real HTTP `POST /api/parents/login` to obtain a real JWT.
- Devices, ParentDevices, Conversations, and Messages were inserted directly into SQLite (no production code paths bypassed for the endpoint under test). All seeded rows are tagged with the prefix `EPTEST-` (devices: MAC + ApiKey, messages: leading `[EPTEST]` tag) so they are easy to spot and clean later.
- Tables touched by seeding: `Parents` (via HTTP), `Devices`, `ParentDevices`, `Conversations`, `Messages`. Schema unchanged.
- Per parent: 40 (A) / 30 (B) device + ParentDevice + conversation rows, plus 2 messages per conversation (140 message rows total).
- Fake conversationIds are random GUIDs that do not exist in the DB.
- The endpoint under test is hit via real HTTP `GET /api/conversations/{conversationId}` with `Authorization: Bearer <parent A JWT>`.

## 7. Final verdict

**PASS — 100/100.** Owned conversations return `200`, non-owned and fake conversation ids return a uniform `404`. Authorization, ownership check, and existence-leak prevention all behave as specified.

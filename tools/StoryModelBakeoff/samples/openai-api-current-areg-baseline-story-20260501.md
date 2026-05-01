# Areg story sample — `openai-api-current-areg-baseline-story-20260501`

> **Quick title:** OpenAI / current production path / `պատմիր հեքիաթ` / turn 1

---

## 1. Metadata

| Field | Value |
|---|---|
| Sample id | `openai-api-current-areg-baseline-story-20260501` |
| Provider | `openai` |
| Model label (human-readable) | gpt-4o (current production default per `appsettings.json` → `OpenAI:ChatModel`) |
| Exact API model id | `gpt-4o` |
| Source | `api` (production backend path: `POST /api/chat` → `ChatService` → moderation → `OpenAIReliabilityGate` → OpenAI Chat Completions) |
| Captured (UTC) | 2026-05-01T17:00:00Z (approximate; see latency below) |
| Reviewer | Claude Code (agent draft) — Hayk to confirm Armenian-side scores |
| Language target | Eastern Armenian |
| Child age target | unspecified (no `childId` in request → no per-child context injection) |
| Sample type | `initial-story` |

## 2. Request context

| Field | Value |
|---|---|
| System prompt | Production `SystemPrompt` from `backend/src/ArmenianAiToy.Api/appsettings.json`. The bake-off's frozen copy at [`tools/StoryModelBakeoff/system-prompt.txt`](../system-prompt.txt) is identical — F1.2 dry-run reports drift = none, SHA-256 `54dfb1c9c7f227ed6fcd1e7c6b8177559e3a59073f947b7934703d7990f946b4`. **Note:** the production runtime ALSO injects ChatService's story-memory + child-context overlays at request time; raw `SystemPrompt` is the floor, not the full effective prompt. |
| Developer / tool prompt | `(none)` — request goes through production `ChatService`, not the bake-off tool. |
| User prompt | `պատմիր հեքիաթ` |
| Conversation history included | `none` (fresh device, fresh conversation; ChatService creates a new conversation on first turn). |
| Selected child choice (continuations only) | `(n/a — initial story, not a continuation)` |
| Decoding parameters | `(provider default — ChatService does not override temperature, top_p, top_k; max_tokens governed by the OpenAI SDK default)` |
| Safety / moderation path | Production: input moderation → ChatService → output moderation. `safetyFlag=0` (Clean) on response. |
| Request endpoint | `POST http://127.0.0.1:5000/api/chat` |
| Request headers | `content-type: application/json`; `x-device-id: <fresh test-device GUID>`; `x-api-key: <REDACTED — 36-char device API key from `/api/devices/register`>` |
| Test device id (local test only) | `a83b4dad-8f52-4bf3-a5ed-36e9217372ad` |
| Conversation id (local test only) | `a159a10e-2373-467d-a969-b975a3e584e5` |
| Message id (local test only) | `843f8878-22eb-42df-b78d-7569542dd451` |
| Story session id | `a159a10e-2373-467d-a969-b975a3e584e5` (same as conversationId on first turn) |
| Detected mode | `story` (from response `mode` field) |
| End-to-end latency | 25,165 ms |

> Backend was started locally with `dotnet run --project backend/src/ArmenianAiToy.Api --configuration Release --no-build --urls http://127.0.0.1:5000`, health-checked via `GET /api/health` (`status=ok, database=ok`), one device registered, one chat call fired, backend stopped immediately after capture. No production code changes.

## 3. Raw output

> Verbatim response body from `POST /api/chat`. JSON unwrapped; `apiKey` does not appear in the response. Whitespace and line endings preserved.

**Response status:** `200 OK`
**Response Content-Type:** `application/json`

```json
{
  "response": "Ծիածանը փայլում էր երկնքում, երբ փոքրիկ ռնգեղջյուրը, ով սիրում էր աղմկոտ խաղեր, շուրջը պտտվելով մոտեցավ իր ընկերոջ, մաքուր ճերմակ հավիկի մոտ: Հավիկը մի հետաքրքիր բան ուներ իր թևիկի տակ պահված. մի փայտե տուփ: Միշտ հետաքրքրասեր ռնգեղջյուրը մոտեցավ, թե արդյոք իմանալու տուփի գաղտնիքը: Նա այնքան ուրախ էր, որ ձեռքը մոտեցրեց:",
  "conversationId": "a159a10e-2373-467d-a969-b975a3e584e5",
  "messageId": "843f8878-22eb-42df-b78d-7569542dd451",
  "safetyFlag": 0,
  "choiceA": "Մոտենանք տուփին",
  "choiceB": "Նայենք ընկերոջին",
  "storySessionId": "a159a10e-2373-467d-a969-b975a3e584e5",
  "mode": "story"
}
```

## 4. Normalized Areg output

> What Areg would actually speak / show on the toy. The response body is already split into `response` (story prose) + `choiceA` / `choiceB` (option buttons / spoken choices) by `ChatService.TailBlockParser`, so no further normalization is applied here.

**Story body:**

«Ծիածանը փայլում էր երկնքում, երբ փոքրիկ ռնգեղջյուրը, ով սիրում էր աղմկոտ խաղեր, շուրջը պտտվելով մոտեցավ իր ընկերոջ, մաքուր ճերմակ հավիկի մոտ: Հավիկը մի հետաքրքիր բան ուներ իր թևիկի տակ պահված. մի փայտե տուփ: Միշտ հետաքրքրասեր ռնգեղջյուրը մոտեցավ, թե արդյոք իմանալու տուփի գաղտնիքը: Նա այնքան ուրախ էր, որ ձեռքը մոտեցրեց:»

**Choice block:**

- A: «Մոտենանք տուփին» (Let's approach the box)
- B: «Նայենք ընկերոջին» (Let's look at the friend)

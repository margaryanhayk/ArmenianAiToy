# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Armenian AI Toy — a physical children's toy with an AI companion ("Areg") that speaks Armenian. ESP32 hardware with mic/speaker connects to a .NET backend that orchestrates OpenAI GPT-4o for child-safe conversations.

## Build & Run

```bash
# Backend (from backend/ directory)
dotnet build                                    # Build all 4 projects
dotnet run --project src/ArmenianAiToy.Api      # Run API on http://0.0.0.0:5000

# API key (one-time setup)
dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project src/ArmenianAiToy.Api

# ESP32 (Arduino IDE)
# Open esp32/ArmenianAiToy/ArmenianAiToy.ino
# Board: ESP32 Dev Module, libraries: WiFiManager, ESPAsyncWebServer, AsyncTCP, ArduinoJson
```

No tests exist yet. Database (SQLite) auto-creates on first run via `EnsureCreated()`.

## Architecture

**Backend — Clean Architecture (.NET 10, 4 projects):**

- **Api** — Controllers, DeviceAuthMiddleware, static web UI in `wwwroot/`. Entry point: `Program.cs`
- **Application** — Service interfaces/implementations, DTOs. Core logic lives in `ChatService` (orchestrates moderation → GPT → moderation → store)
- **Domain** — Entities (Device, Child, Conversation, Message, Parent, ParentDevice) and Enums (Gender, MessageRole, SafetyFlag)
- **Infrastructure** — EF Core `AppDbContext` (SQLite), OpenAI SDK adapters, `DependencyInjection.cs` wires everything

**ESP32 Firmware** — Thin client. Serves local web UI, proxies `/api/chat` to the .NET backend via HTTP. No AI processing on device.

## Key Design Decisions

- **Two auth mechanisms:** Devices use `X-Device-Id`/`X-Api-Key` headers (validated by middleware). Parents use JWT Bearer tokens.
- **Child personalization:** `ChildService.BuildChildContext()` appends child's name/gender/age to the system prompt on every request. Gender matters for Armenian grammar.
- **Conversation sessions:** Auto-expire after 30 minutes of inactivity (see `ConversationService`). Last 20 messages sent as context to GPT.
- **Dual safety:** OpenAI Moderation API checks both user input AND AI output. System prompt (in English) instructs GPT to respond only in Armenian.
- **System prompt is in English** — GPT-4o follows English instructions more reliably while generating natural Armenian output. The prompt is in `appsettings.json` under `SystemPrompt`.
- **ESP32 as proxy** — All intelligence is on the backend. ESP32 just forwards HTTP requests. Audio I/O pins are defined in `config.h` but not yet active (Phase 3-4).

## ESP32 Pin Assignments (config.h)

INMP441 mic: GPIO 14/15/32 (I2S_NUM_0). MAX98357A speaker: GPIO 26/25/22 (I2S_NUM_1). LED: GPIO 2.

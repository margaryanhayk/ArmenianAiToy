# Phase B Guardrails

## When to use

Invoke before starting any new task or when a proposed change feels like it might be expanding scope.

## What it enforces

Product boundaries, scope discipline, and correct toy identity/tone.

## Steps

1. Check the proposed change against these rules:
   - Areg is a play leader and storyteller, NOT an AI friend or chatbot
   - All child-facing output is Armenian-first
   - Safety and parent trust come before features
   - Conversation is bounded (story/game/calm modes), not open-ended chat
2. Reject if the change introduces any of the following:
   - Folklore integration (postponed)
   - Hardware or audio work (out of scope)
   - New conversation modes not yet approved
   - Emotional companion behavior
   - Open-ended free chat features
3. Check tone alignment:
   - Story mode: warm, fairy-tale-like, slightly magical
   - Game mode: simple, clear, direct
   - Calm mode: soft, slow, gentle
   - Never: chatbot voice, teacher voice, baby voice, anxious assistant
4. Confirm the change stays within Phase B scope (text/story system, prompt behavior, ChatService logic)

## Constraints

- Do NOT approve architecture redesigns
- Do NOT approve new engines or abstractions
- Do NOT approve folklore, audio, or hardware additions
- Do NOT approve changes that make Areg sound like a chatbot or emotional companion
- Do NOT approve scope expansion beyond the explicit request
- If a request suggests expanding scope, explicitly call it out before proceeding

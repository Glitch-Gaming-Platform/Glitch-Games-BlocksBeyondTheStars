# ADR 0008 — Optional, offline-safe, server-validated LLM backend

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `AI_MISSION_BACKEND.md`

## Context

The game can use an LLM for mission plans, NPC greetings, board-mission texts and ship-AI (VEGA)
banter. It must never require an LLM to play, must work fully offline, and must never let the AI
bypass game rules or grant runaway rewards. We must decide where the AI lives and how it is gated.

## Decision

1. **The AI is an optional, separate Python service** (`ai-backend/`, FastAPI, LangChain/LangGraph
   over an OpenAI-compatible API — LM Studio / OpenAI / Claude by env). The C#/.NET server stays
   authoritative.
2. **It is off by default and offline-safe.** A server-config `AiLevel` gate
   (`Off` / `TextOnly` / `Suggest` / `Auto`) controls all AI use; `Off` never contacts the backend.
   Every text endpoint falls back to a deterministic bilingual template on any failure or when no
   LLM is configured, so the game runs identically without AI.
3. **The AI proposes; the server validates, clamps and publishes.** The AI returns a structured
   `MissionPlan` (and flavour text); the server validates it against allowed content keys, clamps
   rewards (`MissionPlan.MaxRewardCount = 25`), and only then stores (`Suggest` = inactive draft)
   or publishes (`Auto`). The AI never grants items, finalizes rewards or bypasses rules.
4. **Flavour text wraps fixed jobs.** NPC greetings / VEGA banter / board-mission texts only write
   prose around server-coined objectives and rewards, which stay untouched.

## Consequences

- The game is fully playable with no AI, no network and no API key; AI is purely additive.
- Reward clamps and content-key validation keep the server in control regardless of what the LLM
  emits, so a misbehaving or adversarial model cannot break the economy.
- Enabling full generation (`Suggest`/`Auto`) is a per-server deployment choice requiring the
  separate backend to be configured and reachable.

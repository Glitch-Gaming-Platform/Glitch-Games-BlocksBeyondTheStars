# Web client (Unity WebGL) — feasibility decision

Status: feasibility decided — not built · 2026-06-19

## Decision

A browser client is **feasible with constraints** and would ship later as a reduced-quality "Lite"
profile. The **server side is ready now**; the actual WebGL build and the Lite client are deferred
because they require the Unity Editor and a meaningful client-side effort. This document records the
decision, the architecture that already supports it, and the remaining blockers — it is not a to-do
list.

## Why the architecture already supports it

- **Transport abstraction.** `IServerTransport` / `IClientTransport` decouple game logic from the
  wire. Native clients use `LiteNetLibTransport` (UDP); browsers would use `WebSocketServerTransport`.
  Both carry identical `NetCodec` payloads, so the **same authoritative server** serves both.
- **Composite transport.** `CompositeServerTransport` runs UDP + WebSocket together on the gameplay
  port, giving one connection space for mixed native/browser play.
- **Server is authoritative.** The browser client decides nothing, so no client-type-specific trust
  rules are needed.

## What is implemented now (server side)

- `WebSocketServerTransport` (browser-compatible, same protocol) + tests.
- `CompositeServerTransport` (UDP + WS).
- Server config `EnableWebSocket` / `WebSocketBindAddress`.
- Server web portal in the API: `/portal` landing page, `/play` browser-client placeholder.
- **Native client distribution from the server** (the "download the client from the host" goal): a
  Velopack installer + auto-update feed (`scripts/publish-client-installer.ps1`), served at `/download`
  (Setup.exe) and `/updates` (feed), with an in-app `ClientUpdater`. See `SELF_HOSTING.md` §9.

## Key files

- `src/BlocksBeyondTheStars.Networking/Transport/WebSocketServerTransport.cs`
- `src/BlocksBeyondTheStars.Networking/Transport/CompositeServerTransport.cs`
- Server API portal (`/portal`, `/play`, `/download`, `/updates`).

## Constraints (browser vs native)

| Area | Note |
|---|---|
| Networking | No raw UDP in browsers → WebSocket (done). WebRTC optional later. |
| Performance | Fewer chunks, lower view distance, simpler effects → a **Lite** profile. |
| Load time | WebGL download + warmup; compressed asset bundles + a loading screen. |
| Memory | Tighter heap; cap loaded chunks. |
| Input | Mouse+keyboard on desktop browsers (Chrome/Edge first); pointer-lock needed. |
| Mobile | Out of scope initially. |

## Open blockers (Unity-side, need the Editor)

- A `WebSocketClientTransport` for Unity (browser WebSocket via jslib) implementing `IClientTransport`.
- A Unity WebGL build profile (Lite quality) + asset bundling.
- MessagePack `ContractlessResolver` does not survive IL2CPP AOT — the wire serialization would need an
  AOT-safe path for the WebGL build.
- The in-game UWB wiki/arcade browser content is lost under WebGL.
- ~177 MB of `Resources` would have to be downloaded/streamed — shrink + bundle first.
- Serving the built WebGL files from `/play` + version negotiation so the served client matches the
  server.

## Bottom line

Treat the browser client as a ~4–6 week Lite-only sub-project taken on after the native client is
solid. Nothing on the server needs to change to start it; the work is entirely the Unity WebGL build
and the constraints/blockers above.

# ADR 0007 — Multiple worlds kept resident, players scoped by location

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `MULTIWORLD_AND_SYSTEM_FLIGHT.md`

## Context

In multiplayer, players can stand on different planets — even in different star systems — at the
same time. We must decide how many worlds run at once, how their state stays isolated, and how
messages reach only the right players.

## Decision

1. **Many worlds are resident at once with isolated state.** `WorldManager` holds a
   `Dictionary<locationId, LoadedWorld>`; each `LoadedWorld` bundles a `ServerWorld` plus its own
   creatures, enemies, flora, fluids, containers, structures, weather/time and landed ships.
   Worlds load on demand (`GetOrCreate`) and unload when the last player leaves, so memory scales
   with *occupied* locations, not the whole universe.
2. **An Active cursor, not parallel execution.** `GameServer` reads per-world state via forwarding
   members (`_worlds.Active.*`). The tick walks `OccupiedLocations()` and calls `SetActiveWorld`
   before ticking each one; incoming messages first point the cursor at the sender's world. Worlds
   are simultaneous but processed one at a time.
3. **Players are scoped by `CurrentLocationId`.** Mining/placing/streaming/presence operate on the
   sender's resolved world. `WorldReset` is sent only to the moving player.
4. **Recipient filtering is server-side; no scope id goes on the wire.** Broadcasts
   (e.g. `GameServerPresence`) send to viewers whose `CurrentLocationId` matches the subject's;
   the location is implicit in *who* receives the message, never a field in it.

## Consequences

- Edits and presence on one body never reach players on another (proven by
  `MultiplayerVisibilityTests`); cross-world isolation is correct by construction.
- The protocol stays lean — no per-message scope id — but every broadcast path must filter
  recipients by location, or messages would leak across worlds.
- The single Active cursor means worlds tick sequentially, not concurrently; per-tick cost scales
  with the number of occupied worlds.

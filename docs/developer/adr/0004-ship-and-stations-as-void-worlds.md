# ADR 0004 — Ships and space stations are first-class "void" worlds

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `STATION_AS_LOCATION.md`

## Context

Boarding a space station (or a ship interior) must put the player inside a self-contained place
floating in space — no planet, no weather, constant interior lighting, breathable — without the
shared-planet state bleeding in. We must decide how these interiors are modelled.

## Decision

1. **A `Void` flag on `PlanetType` makes `WorldGenerator.Generate` return an all-air world** (no
   terrain, caves, ore or flora). `LoadWorld` skips all planet content for void worlds (no
   settlements/wrecks/fluids/creatures/landing-zone init) and sets a fixed station environment.
2. **Stations and ship interiors are code-defined void planet types** (`orbital_station`,
   `ship_interior` in `data/planets.json`): `Void=true`, `SpaceSky=true`, breathable (life
   support), zero fauna/flora, weather off, fixed time-of-day. The universe generator never
   assigns them to a celestial body.
3. **Boarding is a real per-player world transition.** `BoardStation` mirrors `HandleTravel`:
   validate range → leave the space instance → `LoadWorld("orbital_station", "station:"+id)` →
   stamp the structure on a clean floor + spawn scoped NPCs → set `CurrentLocationId` → send
   `WorldReset`. Leaving (`LeaveStation`) travels back and unloads the world if empty; ship
   interiors work the same via `EnterShipInterior`.
4. **Player-built stations reuse the machinery** (`StampPlayerStation` into the same void world,
   persisted as a `space_structure`).

## Consequences

- Station/ship interiors are cleanly isolated: no day-night, weather or terrain from any planet
  leaks in, and void worlds hard-skip enemy/creature/flora ticks (stations stay peaceful).
- Boarding reuses the proven, tested world-transition path (`WorldReset` + chunk re-stream),
  which fixed the earlier "black → fall → planet" boarding bug.
- Station-world edit persistence is intentionally light (regenerated on board; player builds
  persist as `space_structure`).

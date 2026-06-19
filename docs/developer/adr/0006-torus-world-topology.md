# ADR 0006 — World topology is a torus (both axes wrap)

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `WORLD_WRAP.md`

## Context

The player must be able to walk around a planet and loop back seamlessly with no invisible
barrier. The original plan was a cylinder with ice-wall poles. We must decide the final world
topology and how seam-free generation is guaranteed.

## Decision

1. **The world is a torus: both the X (longitude) and Z (latitude) axes wrap.** The pole design
   (W5) was dropped (2026-06-11) in favour of wrapping latitude exactly like longitude — you can
   walk in any direction (including diagonals) and loop back with no barrier.
2. **Noise is exactly periodic in both axes.** `Noise.FbmTorus` / `Noise.ValueTorus` map world-X
   and world-Z onto concentric circles (`Value5D`) so surface height, biomes, caves and ore tile
   seamlessly across both seams; the X-only `FbmCylX` helpers are superseded.
3. **Circumference is per body, chunk-aligned.** `WorldConstants.CircumferenceFor(bodyKey,
   sizeClass)` sizes asteroids/moons/planets (≈800–12000 blocks); latitude wraps on a derived
   period. There is deliberately no default-6000 overload for canonicalization helpers.
4. **Server and client canonicalize on both axes** (`WrapX`/`WrapZ`, `WrapDeltaX`/`WrapDeltaZ`,
   `CanonicalChunk`/`CanonicalBlock`) for movement, streaming, persistence and cross-seam
   interaction (mine/place/reach/landing-zone).

## Consequences

- Generation is seam-free in both axes (proven by `WorldWrapTests`), and proximity/reach checks
  work across both seams.
- Every wrap/canonicalize call must pass the correct per-body circumference — using the legacy
  6000 default on a differently-sized world was the historic "cannot mine any block" bug.
- Deferred edge cases: a rare floating-origin rebase after very many laps (float precision) and a
  scene-space world-map waypoint that goes stale after a full lap; neither affects normal play.

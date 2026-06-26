# River Routing & Natural Waterfalls — Implementation Plan

> Status: **PLAN ONLY — nothing implemented.** Drafted 2026-06-26.
> Goal: rivers should always flow *downhill into a sink* (the sea, a lake or a pond),
> and where a routed river drops over a steep step it should form a real, animated
> waterfall. Today neither holds.

---

## 1. Problem statement (what's wrong now)

Established by the preceding analysis (see also `TODO.md` river/waterfall entries):

1. **Rivers don't route.** `RiverDepthAt` (`WorldGenerator.cs:527`) carves a channel purely
   where an FBM noise band sits near 0.5 (`|rl − 0.5| < RiverHalfWidth = 0.04`). The band has
   **no relationship** to where the sea or any pond is.
2. **No gefälle / flow direction.** Each river column is filled flush to *its own* local
   `surfaceY` (`WorldGenerator.cs:802`, `waterTop = surfaceY`). The water surface therefore
   tracks the terrain up *and* down — water appears to climb hills.
3. **Channels dead-end anywhere.** A segment is clipped independently by:
   `rv ≥ 0.04` (noise leaves band), `SurfaceSlope > RiverMaxSlope (4)` (too steep), or
   `surfaceY > riverMaxY` (too high). Only the `surfaceY ≤ seaLevel` boundary is a *real* sink,
   and rivers reach it **only by coincidence**.
4. **Ponds/lakes are excluded, not connected.** Rivers are suppressed in pond columns
   (`!pondHere`, `SurfaceRiverDepth → 0` in pond columns, `WorldGenerator.cs:594`). There is no
   inflow/outflow concept.
5. **No natural waterfalls.** Worldgen water is written as static blocks via `chunk.Set(...)`
   and is **never** registered as a fluid source (`RegisterFluidSource` exists only in the
   GameServer, never in WorldGeneration) — so it never flows and never falls on its own. The
   `RiverMaxSlope` gate, added to prevent the "floating water wall" artefact, actively chops the
   channel exactly at the steep transitions where a waterfall belongs.

**Key enabling fact:** the waterfall *rendering* is already complete and requires **zero**
server simulation. `WaterfallDetect.IsFalling` (`client/.../WaterfallDetect.cs`) is purely
geometric (water + water-above + ≥2 air sides), the cascade shader (`BlockAtlasTransparent.shader`
mode 4) and mist VFX (`WaterfallMistView.cs`) trigger from block IDs alone. So a **static**
vertical water column written by worldgen lights up as an animated waterfall for free.
The only missing client piece is a **waterfall sound** (no `waterfall.mp3`; `ClientAudio.cs`
only has `water_shore/surf/brook`).

---

## 2. Architectural constraints we must respect

These are hard invariants of the codebase; the design is shaped around them.

- **Deterministic, store-nothing worldgen.** `WorldGenerator` reproduces every chunk from the
  seed alone (class header §11). Only player deltas are persisted. Any river data must be
  re-derivable from the seed, not saved.
- **Client and server generate independently and must agree.** The client runs its own
  `WorldGenerator` for terrain preview, ship-landing checks and aquatic life. The shared
  `SurfaceRiverDepth` / `SurfacePondDepth` / `IsSurfaceWater` are the **single source of truth**;
  both sides already depend on `FbmT`/integer-hash determinism matching across .NET and Unity.
- **Per-column generation is O(1) and seam-free.** `Generate` decides each column locally with
  no neighbour scan beyond the cheap `SurfaceSlope` (±2). Routing inherently needs cross-column
  knowledge — so the cross-column work must be **precomputed once per world**, and the per-column
  path must stay an O(1) lookup.
- **Bounded torus world.** X wraps at `Circumference` (6000); Z wraps at
  `LatitudePeriodFor ≈ Circumference/2` (~3000). The whole planet surface is a finite
  ~6000×3000 torus → a global river network is computable in one bounded pass at world-load.
- **Per-world injection pattern already exists.** The server calls `SetCircumference` /
  `SetCratered` / `SetLandingPads` on the generator at `SwitchActiveWorld`, and
  `BuildLandingPads()` (`GameServerSpace.cs:82`) is the precedent for "precompute a structure,
  hand it to worldgen before any chunk generates." The river network plugs in the same way.

---

## 3. Design overview

Replace the noise-band river with a **precomputed, gefälle-aware river network** built once per
world from the seed, queried per-column in O(1), and rendered with the existing waterfall path at
steep steps.

Three layers:

1. **`RiverNetwork` builder** (new, in `WorldGeneration`) — pure, deterministic, seed-driven.
   Runs at world-load. Produces a compact queryable structure: for any (x,z) → is it river /
   waterfall / catch-basin, what is the **water-surface Y** here, and the flow axis.
2. **`Generate` integration** — the per-column water fill consults `RiverNetwork` instead of
   `RiverDepthAt`. Monotonic surface + explicit waterfall column carving.
3. **Client parity + waterfall sound** — client builds the same network from the seed (or
   receives it); add a positional waterfall ambience loop.

---

## 4. The `RiverNetwork` builder (core)

A self-contained deterministic component: `RiverNetwork.Build(planet, seed, circumference, content)`.

### 4.1 Coarse heightfield
- Sample `SurfaceHeight` on a **coarse grid** (cell size `R`, e.g. 8 blocks) over the whole torus:
  `(6000/8) × (3000/8) ≈ 750 × 375 ≈ 281k` samples — a one-time cost at world-load. Tunable: 16
  blocks → ~70k samples if profiling demands.
- Store the coarse heights + precompute, per cell, the sea/pond classification using the existing
  `ResolveSeaFluid` (sea level) and `PondDepthAt` (basins). Sea cells and pond/lake basins are
  **sinks**.

### 4.2 Sources
- Deterministically scatter candidate sources at **high, gentle** coarse cells (height above a
  per-world percentile, low local slope), seeded by `seed`. Count scales with `WaterAbundance`
  (dry worlds: few/none; wet worlds: many) and with world area. Reuse the river-eligibility gate
  (`WaterAbundance ≥ 0.4`) so behaviour matches today's "only wet worlds get rivers."

### 4.3 Downhill tracing (guaranteed mouth)
- From each source, follow **steepest descent** on the coarse heightfield, cell to cell, across
  the X/Z wrap seams (`WrapDeltaX` / `WrapDeltaZ`), until reaching a sink (sea level or a basin).
- **Depression handling:** if a path reaches a local minimum that is *not* a sink, either
  (a) carve a pond/lake basin there (the river forms its own lake, which then needs an outflow), or
  (b) apply a lightweight priority-flood / fill-and-spill so every path is guaranteed to terminate
  at a real sink. Recommended: **fill-and-spill** so no river ever vanishes into a pit. This is the
  mechanism that makes "rivers always reach a body of water" *true by construction*.
- **Merge tributaries:** when two paths meet, they share the downstream channel (flagged with a
  growing "strahler/flow" accumulator → wider, deeper downstream).

### 4.4 Monotonic water surface + waterfall steps
- Walk each finalized path from source to mouth assigning a **non-increasing** water-surface
  height `W`. Along a gentle reach `W` is constant or steps down by 1 (a calm flat river).
- Where the terrain between two consecutive path cells drops by more than a threshold
  `WaterfallMinDrop` (e.g. > 3 blocks, matching the client `MinDrop = 4` so the mist actually
  triggers), mark a **waterfall step**: the channel drops vertically here.
- This replaces the old `RiverMaxSlope` gate: steep transitions become *features* (waterfalls),
  not deleted segments. The flush-fill "floating water wall" artefact is avoided differently — by
  the monotonic surface + explicit vertical carve, not by forbidding slopes.

### 4.5 Output structure (compact, queryable)
For per-column O(1) queries, store the network rasterized at block resolution **lazily per region**
(the network is sparse — only channel cells), or as a coarse path list refined on query. Each
recorded river column carries:
- `WaterSurfaceY` (the monotonic `W`, may be **below** local terrain on a flat reach, or define a
  vertical span on a waterfall step),
- `ChannelDepth` / half-width (from flow accumulation → small brooks vs. wide rivers),
- `FlowAxis` (unit X or Z, the real downstream direction — feeds `WaterSurface` classification),
- `Kind` ∈ {flat-reach, waterfall-column, catch-basin}.

Memory: a 6000×3000 world has on the order of 10⁴–10⁵ channel block-columns — kilobytes to a few
MB at most; can be held per active world like other per-world runtime state.

---

## 5. `Generate` integration (`WorldGenerator.cs`)

Replace the river block in the column loop (currently `WorldGenerator.cs:792-805`):

- Query `RiverNetwork` for `(worldX, worldZ)`.
- **Flat reach:** fill water from `seabedY` up to `WaterSurfaceY` (which is the *network's*
  monotonic surface, **not** the local `surfaceY`). Carve the channel bed down to
  `WaterSurfaceY − ChannelDepth`. Banks above the water stay terrain.
- **Waterfall column:** write a vertical run of `water` blocks down the step face into the catch
  basin, full-width-1 (thin sheet) or matched to channel width. Because the cells have water above
  and air on ≥2 sides, the client renders the cascade + mist automatically.
- **Catch basin:** a small pond carved at the foot of each waterfall (so the column has somewhere
  to land and reads as a plunge pool).

Pond/lake integration: stop excluding rivers from pond columns. Instead the builder treats ponds as
**graph nodes** — a river may flow *into* a lake (inflow delta) and *out* of it (outflow continuing
downhill). `SurfacePondDepth` and the new river query coordinate via the shared network so they
never disagree.

### Shared-function updates (these are the determinism contract — must all read the network)
- `SurfaceRiverDepth` (`:576`) → delegate to `RiverNetwork` (used by tree/prop placement, ship
  landing, aquatic life, **client preview**).
- `IsSurfaceWater` (`:605`) and `SurfaceWaterColumn` (`:627`) → include routed rivers, waterfall
  columns and catch basins, returning the correct water-surface Y.
- Tree/flora/geyser placement exclusions (`:1023`, `:1121`, `:1176`, `:1296`) already call
  `SurfaceRiverDepth`/`SurfacePondDepth`; they inherit the fix once those delegate to the network.
- `StampWaterFlora` — catch basins and slow reaches can host lilies/kelp like ponds.

---

## 6. Client parity & rendering

- **Network build on the client.** The client runs the *same* `RiverNetwork.Build` from the seed
  at world-load (it already runs `WorldGenerator` for preview). Must use only the existing
  deterministic primitives (`FbmT`, integer hashing, `WrapDelta*`) so server and client produce
  byte-identical networks — the same guarantee terrain preview already relies on.
  - *Fallback option* if cross-platform float drift in the tracing proves risky: compute the
    network server-side and send a compact `RiverNetwork` snapshot over the wire on world entry
    (new network message). Heavier, but removes any determinism doubt. **Recommend trying the
    deterministic-rebuild path first**, matching the codebase philosophy.
- **Cascade + mist:** no change needed — `WaterfallDetect` / mode-4 shader / `WaterfallMistView`
  already handle vertical water columns. Verify `WaterfallMistView`'s scan radius and `MinDrop`
  line up with the chosen `WaterfallMinDrop`.
- **Waterfall SOUND (new):** add a positional looping ambience at impact points, mirroring the
  geyser hiss pattern (`GeyserView` + `WorldRig` wiring). Two sub-options:
  - extend `ClientAudio` fluid-bed classification to detect a falling column nearby and play a new
    `waterfall.mp3` 3D loop at the impact cell, **or**
  - a dedicated lightweight component (like `WaterfallMistView`) that owns its own `AudioSource`.
  Needs one new asset: `client/Assets/Resources/audio/waterfall.mp3` (ElevenLabs — generation is
  blanket-approved per project memory).

---

## 7. Phasing (incremental, each shippable)

**Phase 0 — Spike / feasibility (no game change). ✅ DONE (2026-06-26).**
Built `src/BlocksBeyondTheStars.WorldGeneration/RiverNetwork.cs` (standalone, NOT wired into
`Generate`) + `tests/.../RiverNetworkSpikeTests.cs` (8 tests, all green). Algorithm: integer
Priority-Flood depression fill from ocean cells → drainage tree + fill-and-spill lakes; deterministic
upland sources accumulate flow; channel cells above a flow threshold; waterfall = channel step whose
terrain drop > `WaterfallMinDrop`.

*Results (full ~6000×3000 torus, cellSize 8 = 750×372 = 279k cells):*
- **Determinism ✅** — every per-cell array byte-identical across two builds. The whole core runs in
  **integer math** (terrain heights are `int`), so there is no float to drift cross-platform →
  **go with option A (client rebuild from seed); no wire snapshot needed.** (Still verify a real
  Unity/IL2CPP build matches in Phase 1, but the risk is now low by construction.)
- **Connectivity ✅** — every channel cell drains to the sea on all tested seeds/worlds
  (jungle, varied, ocean). No dead-ends. The headline invariant holds.
- **Cost ⚠️ 1.55 s** at cellSize 8 — dominated by 279k × 4-octave `SurfaceHeight` FBM samples, not
  the flood. Acceptable at world-load but notable; **cellSize 16 (~70k samples, ~0.4 s) is the
  recommended default**, refining to block resolution only along channels.

*Two design issues the spike surfaced (feed into Phases 1–2):*
1. **Waterfalls are invisible at coarse resolution** — `waterfalls=0` on every real world because an
   8-block-apart coarse step rarely exceeds a 4-block drop; the grid smooths over cliffs.
   → **Phase 2 must detect waterfalls by sampling the *real per-block* terrain drop along the channel
   path, not coarse-cell to coarse-cell.**
2. **Fill-and-spill floods too much** — naive Priority-Flood marks **20–28 % of the world** as lake
   (jungle: 57k–77k of 279k cells). → **Phase 1 must cap lakes by depth/area** (e.g. only keep a
   filled basin as a lake below a max depth/size; otherwise treat as a flat reach), confirming the
   "cap lake size" mitigation in §9 is mandatory, not optional.

Remaining Phase-0 polish (optional): dump a PNG of the network for visual inspection; confirm
cellSize-16 cost + that channels/lakes still look right at that resolution.

**Phase 1 — Routed rivers wired into worldgen. ✅ DONE (2026-06-26).**
Built `RiverField.cs` (block-resolution rasterization of the coarse network: terrain-following water
surface, capped pools, per-block waterfall detection) and wired it into `WorldGenerator`:
- `RiverDepthAt` + the noise-band river block + the `RiverMaxSlope`/`riverMaxY` gates are **removed**.
- New `RiverFieldFor(planet)` memoizes the network+field per world (keyed by planet/circumference/
  cratered, soft-capped at 8), built lazily from the seed → works for server AND client, no injection.
- `Generate` and `SurfaceRiverDepth` both read the field → single source of truth; pond-first
  precedence and "sea owns columns ≤ sea level" preserved.
- *Design correction from the spike:* a river can't cross a cliff with a gentle surface without either
  eroding the terrain or a vertical fall — so waterfalls are placed NOW (not deferred): the water
  surface follows the terrain (thin sheet, no floating wall) and a flagged step pours a vertical
  waterfall column, which the existing client cascade/mist renders for free.
- Lake over-flooding capped at `MaxLakeDepth` (6); rivers only stamp channel cells, so the Phase-0
  20–28 % flood does not reach the world.

*Verification:* full suite **720/720 green**; new `RiverFieldTests` (coverage 942/942, no floating
water, synthetic-cliff waterfall maxDrop 31, determinism); two old slope-gate tests rewritten to the
new behavior (`WateryWorld_CarvesRivers`, `Rivers_RouteWithoutFloatingWater`). Clean build 0 warnings.
**Not committed** (per user). No client/Assets changed → no Unity build needed yet; a Unity build will
be wanted before release to eyeball rivers/waterfalls in-engine + confirm client/server agree.

**Phase 2 — Natural waterfalls. ✅ FOLDED INTO PHASE 1.**
Vertical water columns are emitted at steps > `WaterfallMinDrop` (RiverField waterfall flag → Generate
fills the column). The existing client cascade (shader mode 4) + mist (`WaterfallMistView`) render them
for free; no server sim cost (static blocks). (Kept separate in the plan, shipped together because a
river can't cross a cliff without a fall — see the Phase-1 design correction.)

**Phase 3 — Waterfall sound. ✅ DONE (2026-06-26).**
Generated `client/Assets/Resources/audio/water_fall.mp3` (ElevenLabs, 10 s seamless loop). Wired into
`ClientAudio.WaterBedFor`: a nearby falling column or its impact (drop > 3, same `WaterfallDetect` gate
as the mist) now selects a `water_fall` looping bed, prioritized over the calm water beds
(`WaterRank`: fall > brook > surf > shore). Degrades gracefully if the clip is missing. Verified by a
local Unity client build (compile + asset import). Reuses the existing 2D fluid-bed system, consistent
with `water_shore`/`surf`/`brook`.

**Phase 4 — Polish. ✅ DONE (2026-06-26).**
- **Flow-accumulation width** widened (maxWidth 4 → 7, widthPerFlow 6 → 8): headwater brooks stay 1 wide,
  trunks that gather tributaries widen toward the cap.
- **Estuary at the sea mouth**: the segment whose downstream cell is the sea flares by `estuaryWiden` (3),
  so the river fans out where it meets the coast.
- **Per-world river density**: source-spring count scales with `WaterAbundance` (0.4 sparse → ≥1.0 lush)
  AND world area, so big/wet worlds get more rivers and large worlds aren't sparser.
- Tributary joins already merge via flow accumulation + the stamp dedup (kept). Erosion-look bank carving
  **skipped** (higher risk, low value; the carved-trench look is acceptable).
- *Verification:* new `RiverFieldTests.MoreSources_WidenAndMultiplyRivers` (density lever monotonic +
  determinism); full suite green; clean build 0 warnings. Not committed.

---

## 8. Testing & verification

- **Determinism tests:** server and client `RiverNetwork.Build` produce identical output for the
  same seed/circumference; output stable across runs.
- **Connectivity invariant (the headline test):** for a sample of seeds, **every** river path
  terminates in a sink (sea / pond / lake). Assert no path ends on dry land above sea level.
- **Monotonic-surface invariant:** along any path, water-surface Y is non-increasing.
- **No floating water:** no water column has air directly beneath its bottom cell except inside a
  marked waterfall step landing in a basin.
- **Existing fluid/landing/flora tests** (`FluidTests.cs`, ship-landing, water-life) stay green
  with the new shared-function results.
- **Local Unity build** (per project verification routine — `client/Assets` changes): confirm
  cascade + mist + new sound in-game; capture a screenshot of a routed river + waterfall.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Cross-platform float drift in tracing → client/server disagree on river position | Use only existing deterministic primitives; Phase-0 byte-compare; wire-snapshot fallback |
| World-load cost of the global trace | Coarse grid (`R = 8`/16), sparse storage, lazy per-region refinement; profile in Phase 0 |
| Worldgen change relocates rivers → existing procedural terrain near player bases differs | **Accepted** — old saves are out of scope (user decision); routing applies to all worlds, no migration. Player **deltas** still persist; only the procedural baseline shifts |
| `RiverMaxSlope` removal reintroduces floating-water walls | Replaced by monotonic surface + explicit waterfall carve + catch basin; covered by the "no floating water" test |
| Fill-and-spill lakes appear where designers don't expect | Cap lake size; fall back to draining toward nearest sea cell if a basin exceeds a size budget |
| Player mines into a static waterfall column → wakes fluid sim | Existing `OnFluidRemoved`/wake logic already handles static-water disturbance; no new work, but include a test |

## 10. Decisions & remaining questions

**Decided by the user (2026-06-26):**
1. **Lakes from depressions — YES.** Rivers may form their own lakes via fill-and-spill. This is
   the mechanism that guarantees every river terminates in a body of water (its own lake counts).
   Still cap lake size (see risks) so a single river doesn't flood a basin unboundedly.
2. **Old saves — out of scope.** Already-generated worlds don't matter; the new routing applies to
   **all** worlds, no world-version gate needed. (Simplifies Phase 1 — no migration path.)

**To resolve in the Phase 0 spike (data-driven, not a blind decision now):**
3. **Determinism strategy:** default to **client rebuild from seed** (option A — no traffic, matches
   the store-nothing engine). Phase 0 byte-compares server vs. client builds across platforms; only
   if drift appears do we fall back to the **server→client wire snapshot** (option B). Mitigate
   drift up front by doing the trace in integer / fixed-point math wherever possible.
4. **Grid resolution `R`** and **`WaterfallMinDrop`** final values (from Phase-0 profiling).

**Later:**
5. **Scope of Phase 4** (delta/erosion polish) — ship minimal first or bundle?

---

## 11. Files in scope (reference)

- `src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.cs` — river block in `Generate`
  (`:792-805`), `RiverDepthAt` (`:527`), `SurfaceRiverDepth` (`:576`), `IsSurfaceWater` (`:605`),
  `SurfaceWaterColumn` (`:627`), `StampWaterFlora`, placement exclusions.
- `src/BlocksBeyondTheStars.WorldGeneration/RiverNetwork.cs` — **new** builder.
- `src/BlocksBeyondTheStars.GameServer/GameServerSpace.cs` — world-load injection beside
  `BuildLandingPads` / `SetLandingPads`.
- `src/BlocksBeyondTheStars.GameServer/WorldManager.cs` — per-world runtime slot for the network
  (if held rather than rebuilt on demand).
- Client: `WaterfallDetect.cs`, `WaterfallMistView.cs`, `ChunkMesher.cs` (verify), `ClientAudio.cs`
  + `WorldRig.cs` (new waterfall sound), `client/Assets/Resources/audio/waterfall.mp3` (new asset).
- Tests: `tests/BlocksBeyondTheStars.Tests/FluidTests.cs` + new `RiverNetworkTests.cs`.

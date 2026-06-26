# Lava System Upgrade — Implementation Plan

> Status: **PLAN ONLY — nothing implemented.** Drafted 2026-06-26.
> Goal: bring lava up to the visual/behavioural level water reached in the river/waterfall work
> (see `RIVER_ROUTING_AND_WATERFALLS_PLAN.md`). Today lava is functionally complete but **visually
> static** — a flat glowing block — while water now has routed rivers, waterfalls, surface animation
> and sound.

---

## 1. Where lava stands today (from the analysis)

- **Placement (worldgen).** `ResolveSeaFluid` (`WorldGenerator.cs:460`) fills basins with lava on dry
  volcanic/airless worlds (planets `lava`, `ashen`, or any waterless basalt world); every real planet
  also gets a deep 6-block lava boundary band above bedrock (`:471`). All written as **static
  `chunk.Set` blocks** — never registered as fluid sources, never simulated (same as worldgen water).
- **Simulation.** Lava is a full fluid in `GameServerFluids.cs` (`_lavaId`) sharing water's flow/fall
  logic; static until disturbed (mining/placing). Lava-specific: **contact damage** (`InLava`) and it
  **ignites** flammable blocks (`GameServerFire.cs`).
- **Rendering.** Lava uses the **opaque** `BlockAtlas.shader` with **static emission 1.0**
  (`ChunkMesher.BlockEmission`, `BlockAtlas.shader:476` `col += albedo * i.mat.a * 3.0`). It glows via
  bloom but does **not** flow, ripple, pulse or crust. It **collides** (you stand on it); water does not.
- **No lava surface treatment.** The mesher tags only `"water"` as a surface (`isWaterSurface`,
  water modes 1–4 in `BlockAtlasTransparent.shader`); lava gets none of it.
- **No lava rivers.** `RiverField` is gated to water seas (`WorldGenerator.cs:557`
  `seaFluid != waterId`). Lava only forms seas/pools.
- **No lavafalls.** `WaterfallDetect`, the shader fall mode (mode 4) and `WaterfallMistView` are all
  water-only; the mist VFX **explicitly skips lava** ("the white spray would read wrong on it"). A
  disturbed lava drop falls server-side but renders as a column of static glowing blocks.
- **Sound (OK).** `amb_lava` (rumble bed) + `lava_bubble` (bubble loop near lava) already exist; no
  dedicated lavafall sound.

**Net:** lava is the "poor cousin" — all the flow/animation polish water just got is absent. On `lava`
and `ashen` worlds, where lava is the dominant "ocean", it reads as a flat luminous plane.

---

## 2. Reuse opportunity (the work is mostly already fluid-agnostic)

The river/waterfall machinery was built generic and only *gated* to water at the edges:

- `RiverNetwork` / `RiverField` operate on a height sampler + sea level + a fill — **fluid-agnostic**.
  Only `WorldGenerator.BuildRiverField`'s gate ties them to water.
- `WaterfallDetect.IsFalling/ImpactDrop` already take a **`fluidId` parameter** (just named `waterId`).
  Passing `lavaId` makes them detect lavafalls verbatim.
- The shader fall/flow modes are a small set of branches keyed on a per-vertex `mode`.

So most of this plan is **generalizing existing code to a fluid parameter** + a lava-tuned shader look,
not building from scratch.

---

## 3. Constraints (same invariants as the river work)

- Deterministic, store-nothing worldgen; client and server rebuild the same fields from the seed
  (integer math). A lava field memoizes per world exactly like the water `RiverField`.
- Lava worlds are dry (`WaterAbundance ≤ 0`, `LavaAbundance > 0`) — they never have a water sea, so a
  lava field and a water field never coexist on the same world (cheap: build at most one).
- Shader changes touch the **opaque** `BlockAtlas.shader` (every block) — any lava branch must be
  tightly gated (a dedicated flag) so it can't affect other blocks.
- Client/Assets changes ⇒ a local Unity build is required for verification (per project routine).
- Lava should read as **viscous**: slower animation + flow than water, thicker/fewer channels.

---

## 4. Phases (each shippable; ordered by value-per-risk)

> **Implementation status (2026-06-26): L1 + L2 + L3 all implemented, NOT committed.** Server suite green;
> Unity client build pending for final shader/VFX verification. Details per phase below.

### Phase L1 — Animated lava surface (highest ROI, benefits ALL lava) — ✅ IMPLEMENTED
- `ChunkMesher`: a lava SURFACE cell (air above) tags its faces tint **mode 5**.
- `BlockAtlas.shader` (both passes): mode 5 skips albedo tint and drives an animated emissive crust
  (slow `_Time`-scrolled slabs + bright veins on `i.wp.xz`, ~1/3 water speed, average glow ≈ unchanged
  so bloom stays balanced).
Make lava *look molten*: a slow emissive crust/flow over every lava surface cell. This helps lava seas
(the common case), not just rivers, and is independent of the routing work.

- **Mesher tag.** In `ChunkMesher`, tag a lava **surface** cell (`collKey == "lava"` && air above) with
  a flag, mirroring `isWaterSurface`. Encode it where the opaque shader can read it — e.g. a spare
  `sky.y` tint-mode value (modes 1–4 are taken; add a lava-surface mode) or a dedicated channel. World
  position (`i.wp`, TEXCOORD2) and `_Time` are already available in `BlockAtlas.shader`.
- **Shader look.** In the emissive branch (`BlockAtlas.shader:474-476`), when the lava-surface flag is
  set, modulate emission with a **procedural crust**: a slow-scrolling low-frequency pattern on
  `i.wp.xz + _Time` that darkens "crust" bands and brightens "cracks" (the molten glow showing through),
  plus a faint overall pulse. Atlas UVs can't scroll, so it's procedural on world position — exactly how
  the water ripples/streaks work. Speed ≈ 1/3 of water (viscous).
- **No geometry / no server change** — pure mesher+shader. Lava still collides as today.

*Verification:* Unity build; eyeball a lava sea on the `lava`/`ashen` planet (crust flows, cracks glow,
bloom intact); confirm no other block changed (the flag is lava-only).

> **L1 implemented as above.**

### Phase L2 — Lava rivers (reuse the routing) — ✅ IMPLEMENTED
- `RiverField` now carries a `FillFluid`; `WorldGenerator.BuildRiverField` builds a LAVA field on the
  `lava`/`ashen` worlds (sink = lava sea, fill = lava), viscous-tuned: fewer sources, `maxWidth 9`,
  `widthPerFlow 6`, `maxLakeDepth 4`, `channelFlowThreshold 1` (sparse magma flows that rarely merge),
  `estuaryWiden 4`. `Generate` fills with `riverField.FillFluid` (water OR lava).
- Lava channel cells render with the L1 animated surface automatically. Test:
  `WorldGenerationTests.LavaWorld_CarvesLavaRivers` (field fills lava, channels hold lava near surface).
Give volcanic worlds **routed magma channels** flowing into the lava sea, like water rivers.

- **Generalize the field.** Add a fluid parameter to the per-world build: `WorldGenerator.BuildRiverField`
  becomes fluid-aware (or a sibling `BuildLavaField`). On a lava world (sea fluid = lava, `LavaAbundance`
  ≥ threshold), build a `RiverNetwork`/`RiverField` with the lava sea as the sink and `lava` as the fill.
- **Generate integration.** The existing river block in `Generate` already sets `columnFluid` — drive it
  from whichever field the world has (water OR lava). Pond precedence is water-only; lava has no ponds, so
  that branch is skipped on lava worlds.
- **Lava-tuned params.** Viscous → fewer sources, **wider** channels, shallower depth; lava rivers are
  rarer and chunkier than water brooks. Tune `sourceCount` / `maxWidth` for the lava field.
- **Surface look.** Lava river cells render with the Phase-L1 animated surface automatically (they're
  lava surface cells). The flow axis can bias the crust-scroll direction downstream.
- **Shared query.** `SurfaceRiverDepth` (and `IsSurfaceWater`) stay water-only; add a parallel
  `SurfaceLavaDepth` / fold into a fluid-typed query so placement (no trees in lava, ship-landing avoids
  lava) and creatures treat lava channels correctly.

*Verification:* new tests mirroring `RiverFieldTests` for a lava world (channels reach the lava sea,
no floating lava, determinism); full suite; Unity build.

### Phase L3 — Lavafalls + ember VFX + heat-haze + sound — ✅ IMPLEMENTED
- `ChunkMesher`: falling-lava flanks kept (not culled) + tagged tint **mode 6**.
- `BlockAtlas.shader`: mode 6 streaks a hot glow straight DOWN the flank (faster than the surface crust).
- `LavaFallView` (new, mirrors `WaterfallMistView`, wired in `WorldRig`): rising **embers** (bright→dark
  cinder, gravity arc) at impacts where lava drops > 3 blocks — the case the white mist deliberately skips.
- **Heat-haze**: `HeatShimmer.AddProximityHeat` (new) OR-s a localized boil into the existing full-screen
  shimmer, fed each frame by `LavaFallView` from the nearest impact distance (works even on a cool world;
  on `lava`/`ashen` the world is already hot so it's full anyway).
- **Sound**: `lava_fall.mp3` (ElevenLabs loop) + `ClientAudio.LavaBedFor` picks `lava_fall` for a nearby
  falling/impacting lava column (else `lava_bubble`). Reuses `WaterfallDetect` (fluid-agnostic, lava id).

### (original L3 spec below)
Where a lava channel drops over a step (or a player mines under lava), pour a **lava cascade** with
**embers** instead of white spray, and a roaring/hissing loop.

- **Detection.** Reuse `WaterfallDetect.IsFalling/ImpactDrop` with `lavaId` (the param already exists).
- **Cascade shader.** Add a lava-fall branch to the streak logic (a hot, slow, downward-scrolling glow
  streak) — the opaque-shader analogue of water's mode 4. Tag falling lava flanks in the mesher like
  `isFallingWater`.
- **Ember VFX.** A `LavaFallView` (or generalize `WaterfallMistView`) that, instead of pale spray,
  emits **rising orange/red embers** that cool to dark as they fall — this is exactly the case the
  current mist code deliberately avoids. Reuse the impact-scan + pooled-particle machinery; swap the
  colour ramp + add a gentle upward flicker.
- **Sound.** Generate `lava_fall.mp3` (low roar + hiss + crackle, seamless loop, ElevenLabs — blanket
  approved) and add a `lava_fall` bed to `ClientAudio` (generalize `WaterBedFor`/the fluid scan to
  detect a nearby falling **lava** column, mirroring the just-added `water_fall` branch).

*Verification:* Unity build; eyeball a lavafall on a volcanic world (cascade + embers + roar); the white
spray must NOT appear on lava.

---

## 5. Shared refactors (clean-ups this enables)

- `WaterfallDetect` → rename/retype to a fluid-agnostic `FluidFallDetect` (keep a water alias) — already
  parameterized by fluid id.
- `RiverField`/`RiverNetwork` → already fluid-agnostic; thread a `fluid`/`fillBlock` through the
  WorldGenerator build so one code path serves water and lava.
- Shader: factor the water flow/fall procedural blocks so the lava variants share the math with tuned
  constants (speed, colour, viscosity), avoiding copy-paste drift.
- A single `SurfaceFluidDepth(planet, x, z) → (kind, depth)` query so all the placement/landing/creature
  callers ask once and branch on water vs lava.

---

## 6. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Lava branch in the opaque `BlockAtlas.shader` bleeds onto other blocks | Gate on a dedicated lava-surface flag set only by the mesher; eyeball non-lava blocks in the Unity build |
| Animated emission breaks bloom / over-brightens at night | Keep average emission ≈ current 1.0; the crust only *redistributes* brightness (dark bands ↔ bright cracks), net energy unchanged |
| Lava rivers carve through the basalt and look wrong on steep volcanic terrain | Reuse the water field's terrain-following + waterfall steps; lava-tuned wider/shallower; cap lake flooding as for water |
| Performance: a second per-world field on lava worlds | A lava world has no water field, so it's *one* field either way; same memoized cost |
| Embers reading as fire (confusion with the burning system) | Distinct colour ramp + motion (cooling arcs, not flickering flames); only at lavafall impacts |
| Determinism across client/server | Same integer pipeline as the water fields; byte-compare in a spike if L2 is taken |

## 7. Decisions (user, 2026-06-26)

1. **Scope: the FULL package** — L1 + L2 + L3.
2. **Viscosity: middle — slower than water, thick.** Shader animation ≈ 1/3 water speed, low spatial
   frequency (big slow crust slabs); L2 lava channels wider + shallower than water rivers.
3. **Lava rivers: only on `lava` and `ashen` worlds** (not every basalt/waterless world).
4. **L3 spray: BOTH** rising embers AND a heat-haze shimmer at the impact.
5. **Old saves: out of scope** (procedural baseline regenerates).

## 8. Files in scope (reference)

- `client/Assets/BlocksBeyondTheStars/Shaders/BlockAtlas.shader` — emissive lava-surface + lava-fall
  branches (L1, L3).
- `client/Assets/BlocksBeyondTheStars/Scripts/ChunkMesher.cs` — tag lava surface / falling lava flanks
  (L1, L3); `BlockEmission`/`IsFluidBlock` already know lava.
- `src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.cs` — fluid-aware field build + a lava field
  on volcanic worlds; `SurfaceLavaDepth`/unified fluid query (L2).
- `src/BlocksBeyondTheStars.WorldGeneration/RiverField.cs` / `RiverNetwork.cs` — thread a fill-fluid
  param (L2).
- `client/Assets/BlocksBeyondTheStars/Scripts/WaterfallMistView.cs` → ember variant / `LavaFallView`
  (L3); `WaterfallDetect.cs` → fluid-agnostic (L3).
- `client/Assets/BlocksBeyondTheStars/Scripts/ClientAudio.cs` — `lava_fall` bed (L3);
  `client/Assets/Resources/audio/lava_fall.mp3` — new asset (L3).
- Tests: `tests/.../LavaFieldTests.cs` (new, mirrors `RiverFieldTests`) for L2.

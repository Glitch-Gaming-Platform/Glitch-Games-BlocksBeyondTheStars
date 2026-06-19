# Peaceful NPC trader ships — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-06-19.

## Overview

Ambient civilian trader traffic makes space feel **alive**: peaceful NPC ships warp in, cruise the system,
dock at a station or land on a planet, and their pilot then appears as a merchant you can barter with. Traders
are invulnerable scenery, transient (no DB), and multiple can be active at once. Ship types are picked from
the content registry, so new ships are included automatically.

## How it works

**The key constraint — per-launch-body instancing.** A space-flight scene is an instance keyed by the body
the player launched from (`"space:" + locationId`). Entity visibility, voxel structures and remote-ship
designs are broadcast only to players *in that instance*, and instances are created lazily on first
`EnterSpace` and dropped when the last player leaves. So traders are simulated **only inside flight instances
that currently have players** — never a galaxy-wide background sim. (Two players "in the same system" but
launched from different bodies are in different instances and won't share a trader.)

**Traffic per system.** A deterministic `TrafficFor(systemId)` buckets each system into None / Rare / Often
from seed + system id (stations lean busier). This drives a per-instance spawn scheduler with a concurrent
cap (Rare ≤1, Often ≤3) and arrival cadence (~25–70 s Often, ~90–240 s Rare).

**Ship-type choice (future-proof).** Picks any non-starter type from `GameContent.Ships`, weighted by cargo
capacity so haulers dominate. New ship types in `data/ships.json` appear in trader traffic with no code change.

**Lifecycle** (`GameServerSpaceTraders.cs`): warp **in** at the system edge → **cruise** toward a station or
through the inner system → **dock** (station) or **land** (planet) or warp **out**.

**Rendered with zero new flight-view code.** A trader is **never** a `CombatEntity` (so it can't be locked,
shot, damaged, and never harms anyone). It rides the existing **remote-ship** path: a synthetic
`NetSpacePlayer` pose (`npc:<id>`) plus a `"ship_remote"` `SpaceShipDesign` built by `BuildNpcShipStructure`
(reusing the real player-ship voxel pipeline), so it shows the actual in-game hull of its type.

**Pilot → merchant (station).** On docking, the pilot is registered as a visiting trader at that station;
boarding the station spawns it as a `"traders"`-theme vendor beside the trade post, so the existing
station-vendor barter works against it (lingers ~150–300 s).

**Pilot → merchant (planet landing).** A trader heading inward **lands on a planet/moon if a pad is free**:
it **reserves the pad** (so players are never assigned it), plays `ShipTransitFx` descent, parks its real
voxel ship on the pad via `LandedShipState`, and stands its pilot in front as a `"traders"` merchant
(`MarketAvailable`/`VendorThemeAt` extended to the landed pilot). It lingers ~180–360 s, then lifts off and
frees the pad. A `_landedTraders` registry (keyed by body) is the source of truth and re-materializes the
parked ship + pilot on world load / per-world tick, so it survives the body world unloading. One landed
trader per body at a time. Reuses `LandedShipState`/`ShipTransitFx`/`NpcList` → no new client code here.

**Warp FX for bystanders.** Hyperspace warp-in/out had no third-person VFX (`HyperspaceWarp` is a local
full-screen overlay only). A new `SpaceWarpFx` message (tag 150) drives a localized cyan-white burst in the
flight view so other players **see** arrivals and departures.

## Design decisions (locked)

- **Wares → own travelling-merchant stock** — traders carry their own, often rarer/discounted inventory so
  meeting one feels rewarding, distinct from fixed station vendors.
- **Persistence → transient** — in-memory only, respawn per session like UFOs/asteroids; no DB schema change.
- **Hostility → invulnerable** — peaceful scenery, not targetable, can't be damaged.
- **Traffic source → deterministic-from-seed** for v1; an authored `StarSystem` field could be added later.

## Key files / classes

- Server: `GameServerSpaceTraders.cs` (controller, scheduler, lifecycle, `TrafficFor`, `BuildNpcShipStructure`).
  Pad reservation + planet barter wired into `GameServerSpace.cs` / `GameServerSettlements.cs` /
  `MarketAvailable` / `VendorThemeAt`.
- Networking: `SpaceWarpFx` (tag 150), registered in `NetCodec`; traders otherwise reuse `SpaceShipDesign`
  (`Kind="ship_remote"`), `NetSpacePlayer`, `LandedShipState`, `ShipTransitFx`, `NpcList`.
- Client: `SpaceView.SpawnWarpFlash` (the one net-new visual); landing/launch reuses `ShipTransitView`.

## Known remaining gaps / deferred

- Needs a Unity client build + lib sync.
- Some ship layouts are parametric boxes (no custom voxel layout), so those NPC ships look like the same
  boxes player ships do until more `data/ship_layouts/*.json` exist.
- Distinct travelling-merchant stock vs. reusing the station vendor theme is a polish layer.

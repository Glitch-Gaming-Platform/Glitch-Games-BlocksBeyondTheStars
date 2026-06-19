# ADR 0002 — Build the client in code, no Unity scene authoring

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `CLIENT_COMPLETION.md`, `CLIENT_SHELL_AND_ASSETS.md`

## Context

The Unity client must stay reviewable, diff-able and free of the classic "works in the editor,
broken in the build" failures that scene/prefab authoring invites. We must decide how scenes,
the in-game rig and the UI are constructed.

## Decision

1. **Scenes, the in-game rig and the UI are built at runtime from C# — not authored in the
   editor.** The only authored scene (`client/Assets/Scenes/Launcher.unity`) holds a single
   `AppShell` GameObject; everything else is spawned on demand.
2. **`AppShell` runs the shell flow** (splash → menu → settings → loading → in-game) and, on
   launch, calls `WorldRig.Build`, which constructs the entire in-game rig in code: server link
   (`GameBootstrap`), chunk material, first-person player + camera + `PlayerController`, HUD,
   sky and post stack — all wired by assignment, no prefab references.
3. **UI is built procedurally through `UiKit`** (canvas/panel/button/text helpers that generate
   their own holographic sprites and fonts at runtime); screens like `UiMainMenu` / `UiLoading`
   are static `Build(...)` methods, not authored layouts.
4. **Content is data-driven** (`data/*.json`, runtime-generated `BlockTextureAtlas`), so adding
   blocks/items needs no editor assets.

## Consequences

- The client is deterministic and reviewable: the rig is plain C# in version control, so it
  diffs cleanly and there is no opaque binary scene/prefab state to drift.
- The "broken in build vs. editor" class of bugs is structurally avoided — the build runs the
  same construction code the editor does; the launcher scene is essentially empty.
- The cost is more boilerplate (every GameObject, component and UI element is hand-wired in
  code) and no visual editor preview; layout is verified by running the client.

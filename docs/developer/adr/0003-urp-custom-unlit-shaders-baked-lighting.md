# ADR 0003 — URP with custom unlit voxel shaders and baked flood-fill block light

- **Status:** Accepted
- **Date:** 2026-06-19
- **Context source:** `URP_MIGRATION.md`, `ADVANCED_GRAPHICS.md`

## Context

The voxel world needs a maintained, cross-platform rendering path and readable stylized lighting,
including coloured light from placed glow/light blocks. We must decide on the render pipeline and
how placed lighting is computed.

## Decision

1. **The client renders through the Universal Render Pipeline.** The Built-in→URP migration
   completed and shipped on 2026-06-10; URP unlocks real sun shadows, a maintained post Volume
   (`UrpScenePost`: ACES / bloom / vignette / grade), SSAO and depth + opaque textures.
2. **The voxel world uses hand-written shaders that bypass the URP light loop.** `BlockAtlas`
   (opaque) and `BlockAtlasTransparent` (water/glass) shade faces from custom global uniforms
   (`_Sc_SunDir`, `_Sc_Light`, `_Sc_Sky`, `_Sc_Lamp*`, `_Sc_FloraTint`) set by `Sky.cs` — not
   from URP's per-light pass.
3. **Placed block light is a baked per-channel flood-fill, not real-time lights.** `ChunkMesher`
   propagates coloured light from glow/light blocks through open cells and bakes it per vertex
   (`TEXCOORD3` colour + `TEXCOORD4` dominant direction); the shader uses it for directional
   block-light shading. No real URP lights are placed for blocks.
4. **Shaders stay dual-pipeline** (a URP HLSL SubShader over the original Built-in CG SubShader),
   so rollback is trivial and the math ports 1:1.

## Consequences

- Placed lighting cannot leak through walls and is not capped by an engine light-count limit —
  the flood-fill is voxel-aware and bakes into the mesh.
- Engine settings (Forward+ many-lights, etc.) do not touch the voxels, because the block shaders
  own their lighting; URP's main directional light (the sun) is still used.
- Code-loaded shaders must be in `GraphicsSettings` Always-Included, or they are stripped from
  builds and render magenta/grey.
- Lighting updates require re-meshing the affected chunk (the flood-fill is baked at mesh time),
  not a cheap per-frame light toggle.

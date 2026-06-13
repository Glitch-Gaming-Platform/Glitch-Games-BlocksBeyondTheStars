# In-Game Wiki + Data-Cube Arcade Minigames

Two browser-backed features, sharing one embedded-browser layer:

1. **Codex (Wiki)** — an always-available in-game reference. The Tech/Ships/Blocks/Items/Recipes/Planet-Type
   chapters are generated live from the content JSON; the **Systems & Worlds** chapters are discovery-gated
   (only places the player has actually visited appear). Reached from a **Codex** button in the in-game menu
   header.
2. **DataQubes** — the player's collection of "data fragments": 20 bundled HTML5/JS minigames (Blockfall,
   Asteroid Breaker, Circuit Weaver, Signal Tuner, Drone Rescue, Cargo Sorter, Blueprint Scramble, Orbit
   Slingshot, Laser Mirror Grid, Micro Miner, Star Map Memory, Alien Glyph Decoder, Reactor Balance, Oxygen
   Loop, Comet Courier, Docking Simulator, Data Fishing, Nanobot Repair, Planet Scanner, Void Solitaire). They
   are **recovered from "data cubes"** scattered on planets, then run from a **DataQubes** button in the menu
   header. All games share one **framework** (`_shared/framework.js` + `theme.css`): a uniform shell
   (start/help/pause/result), abstract input model and the blue-line theme. Finishing a run grants
   **knowledge points** (server-side, rating-scaled, repeatable). Highscores are local-only.

   A Creative world's "unlock all" option also recovers **every** data fragment up front (for testing).

## How it fits together

```
Data cube on a planet (server entity, deterministic per body, some bodies have none)
   └─ press E ─→ UnlockGameIntent(cubeId, gameKey)  ─→ server validates proximity
                                                       ─→ PlayerState.UnlockedGames += key (persisted, SP+MP)
                                                       ─→ GameUnlocks (server→client) ─→ Arcade collection

Menu "Codex"/"Arcade" button
   └─ EmbeddedBrowser (one shared UWB surface)
        ├─ LocalContentServer  (127.0.0.1, serves StreamingAssets/{wiki,minigames,data,locales})
        ├─ Wiki  → wiki/index.html?lang=..   (+ dynamic wiki/wiki-state.json = discovered systems/worlds)
        └─ Game  → minigames/<key>/index.html?lang=..&hi=<best>&game=<key>
                     └─ on game-over: uwb.ExecuteJsMethod("reportScore", key, score) → local best in ClientSettings
```

### Server (verified, built into the bundled server)
- `PlayerState.UnlockedGames` + `PlayerSnapshot.UnlockedGames` — persisted like `UnlockedBlueprints`.
- `GameServerDataCubes.cs` — `StampDataCubes()` scatters 0–N cubes per body (≈45% none), deterministic from
  the world seed; cubes are server entities (not blocks). `HandleUnlockGame()` validates proximity.
- Net messages (`MinigameMessages.cs`, registered in `NetCodec` tags 118–121): `DataCubeList`,
  `UnlockGameIntent`, `GameUnlocks`, `MinigameResultIntent` (finished run → server grants knowledge points,
  rating-scaled, repeatable, in `GameServerDataCubes.HandleMinigameResult`).
- Gated by `ServerConfig.PlaceDataCubes` (default on). A Creative world (`CreativeUnlockAllBlueprints`) also
  unlocks every minigame via `UnlockAllGames` (keys read from the synced `minigames/catalog.json`).

### Client
- `LocalContentServer.cs` — loopback static server for StreamingAssets, plus the dynamic
  `wiki/wiki-state.json` route (discovered systems/worlds + language).
- `MinigameCatalog.cs` — loads `StreamingAssets/minigames/catalog.json`; maps a cube **seed → game**.
- `EmbeddedBrowser.cs` — owns the single shared browser surface + content server. UWB code is behind the
  **`BBS_UWB`** define (see below); without it, `Available == false`.
- `DataCubeView.cs` — renders the glowing cube (texture `Resources/props/data_cube`, point light, pulse),
  proximity hum (`data_cube_hum`), and the E label. `PlayerController` E-interact sends the download +
  plays `data_cube_download`.
- `WikiUI.cs` / `ArcadeUI.cs` — full-screen menu screens opened from the header (`GameMenu.OpenWiki/OpenArcade`).
- `GameBootstrap` mirrors `UnlockedGames` + builds `WikiStateJson`.

### Content (source vs runtime)
`client/Assets/StreamingAssets/` is **git-ignored and generated** (like `client/Assets/Plugins`). The tracked
**source** for the browser content lives in repo-root **`web/`** and is copied into StreamingAssets by
`scripts/sync-client-libs.ps1` (run it after editing, then refresh Unity):
- `web/minigames/catalog.json` + `web/minigames/<key>/index.html` (+ the shared framework
  `_shared/framework.js`, `_shared/theme.css`, `_shared/flowpuzzle.js`).
- `web/wiki/index.html` + `wiki.js` + `wiki.css` + `articles.json`.
- Bilingual via the existing locale files (`data/locales/{en,de}.json`, also synced).

### Generated assets (via `tools/ai-assets`, committed to the project)
- `Resources/props/data_cube.png` (OpenAI) — cube texture.
- `Resources/audio/data_cube_download.mp3`, `Resources/audio/data_cube_hum.mp3` (ElevenLabs) — SFX
  (ClientAudio loads clips by filename).

---

## ⚙️ Manual step: enable the embedded browser (UnityWebBrowser)

Everything above compiles and runs **without** the browser package — the Wiki/Arcade then show a
"browser not installed" placeholder while data cubes, downloads, the collection list and highscores all work.
To turn on the real browser:

1. **Add the scoped registries + UWB packages** to `client/Packages/manifest.json`. UWB lives on the VoltUPR
   registry and depends on UniTask (`com.cysharp.unitask`, on OpenUPM). Merge these `scopedRegistries` and
   `dependencies` into the existing file (don't replace it). Verify the latest versions on the
   [UWB releases page](https://github.com/Voltstro-Studios/UnityWebBrowser/releases) — 2.2.x at time of writing:

   ```jsonc
   {
     "scopedRegistries": [
       {
         "name": "Voltstro UPM",
         "url": "https://upm-pkgs.voltstro.dev",
         "scopes": [ "dev.voltstro", "org.nuget" ]
       },
       {
         "name": "OpenUPM (UniTask)",
         "url": "https://package.openupm.com",
         "scopes": [ "com.cysharp.unitask" ]
       }
     ],
     "dependencies": {
       "dev.voltstro.unitywebbrowser": "2.2.8",
       "dev.voltstro.unitywebbrowser.engine.cef": "2.2.8",
       "dev.voltstro.unitywebbrowser.engine.cef.win.x64": "2.2.8",
       "com.cysharp.unitask": "2.5.11",
       // …keep all existing deps (com.unity.render-pipelines.universal, com.unity.ugui, …)…
     }
   }
   ```

   Reopen the project (or Window → Package Manager) so Unity resolves and downloads the packages. Confirm
   `UnityWebBrowser` appears under Packages with no resolution errors before continuing. Min Unity 2021.3 (we
   are on Unity 6, fine).

2. **Reference the UWB assembly** from the client asmdef: `BlocksBeyondTheStars.Client.asmdef` must list
   `"VoltstroStudios.UnityWebBrowser"` in its `references` (already done in this repo). Without it you get
   `CS0246: 'VoltstroStudios' could not be found`, because the client asmdef uses `overrideReferences` and
   lists its references explicitly.

3. **Add the `BBS_UWB` scripting define**: Project Settings → Player → Other Settings → Scripting Define
   Symbols (Standalone) → add `BBS_UWB`. This activates the UWB code in `EmbeddedBrowser.cs`. (Only set it
   **after** step 1 resolves — the define makes the code reference UWB types, so without the package the
   client won't compile.)

4. **Integration point** (already validated against UWB 2.2.8): the single `#if BBS_UWB` block in
   `EmbeddedBrowser.cs` creates a `WebBrowserUIBasic` in code and assigns the engine/communication/input
   ScriptableObjects the UWB packages ship in their `Resources` folders (`Cef Engine Configuration`,
   `TCP Communication Layer`, `Old Input Handler` — legacy input), so no Inspector wiring is needed. It uses
   `browserClient.LoadUrl` / `RegisterJsMethod<string,int>` / `jsMethodManager.jsMethodsEnable`. If you bump UWB
   to a version with different names, this is the only place to adjust.

5. **Build notes**: CEF adds ~150–200 MB to the build (auto-copied to `<Game>_Data/UWB/` by UWB's
   post-build step). Sign the build to avoid SmartScreen/AV flags on the spawned browser process. Minigame SFX
   use WAV/OGG/Opus only (CEF ships open codecs).

> Reference: registry `https://upm-pkgs.voltstro.dev` (scopes `dev.voltstro`, `org.nuget`) + UniTask via
> OpenUPM — confirmed from the [VoltstroUPM registry](https://github.com/Voltstro/VoltstroUPM) and the
> [UWB setup guide](https://projects.voltstro.dev/UnityWebBrowser/latest/articles/user/setup/).

## Adding a new minigame (author-only — not user-moddable)

A minigame is a self-contained folder of HTML/JS/CSS under `web/minigames/<key>/`, plus one entry in
`web/minigames/catalog.json`. No C#/Unity changes are needed.

### 1. Create the game folder using the framework
All games share `_shared/framework.js` (the shell — start/help/pause/result screens, abstract input, paused
rAF loop, HUD, the result→knowledge bridge) and `_shared/theme.css` (the blue-line look). A game only
implements its mechanic and registers via `BBTS.register({...})`. Served over
`http://127.0.0.1:<port>/minigames/<key>/`, so `fetch()`, ES modules and `<canvas>` all work.

Minimal template:
```html
<!DOCTYPE html><html><head><meta charset="utf-8"><link rel="stylesheet" href="../_shared/theme.css"></head>
<body><script src="../_shared/framework.js"></script><script>
BBTS.register({
  title: { en: "My Game", de: "Mein Spiel" }, difficulty: 2,
  desc:  { en: "One-line goal.", de: "Einzeiliges Ziel." },
  help:  [{ en: "What the controls do", de: "Was die Steuerung macht" }],
  create: function (api) {
    // api.canvas(w,h) → canvas+api.ctx ; api.el(tag,cls,parent,html) for DOM
    // api.bind('Left'|'Right'|'Up'|'Down'|'Confirm'|'Primary'|'Secondary', fn) ; api.held(action)
    // api.pointer(p => { p.type 'down'|'move'|'up', p.x, p.y }) ; api.loop(dt => {…})
    // api.hud({ score: 0, … }) ; api.complete({score, rating:1..3}) ; api.fail({score})
    return { start: function () { /* (re)start a fresh round */ } };
  }
});
</script></body></html>
```
- **Reward:** the framework reports `{score, rating, completed}` to Unity on `api.complete()` → the server
  grants knowledge points (rating-scaled, repeatable). You don't wire rewards per game.
- **Bilingual** ({en,de}) everywhere; **sound** WAV/OGG/Opus only (CEF ships open codecs).
- Reusable engines live in `_shared/` (e.g. `flowpuzzle.js` powers Circuit Weaver + Oxygen Loop).

### 2. Register it in `web/minigames/catalog.json`
Append an entry to the `games` array:
```json
{
  "key": "mygame",
  "entry": "mygame/index.html",
  "icon": "🎲",
  "title": { "en": "My Game", "de": "Mein Spiel" },
  "desc":  { "en": "One-line description.", "de": "Einzeilige Beschreibung." }
}
```
- `key` must be unique and match the folder name. It's what gets stored in the player's collection.
- **Order is authoritative:** a data cube maps its seed to a game by index into this array
  (`seed mod games.length`). Appending is safe; **reordering/removing changes which cube grants which game**
  (and would orphan keys already in players' saved collections).

### 3. Sync + test
- Run `scripts/sync-client-libs.ps1` (copies `web/* → StreamingAssets`), then refresh Unity.
- Test fast: open `web/minigames/<key>/index.html` directly in a desktop browser (the bridge no-ops, so it
  still plays). In-game, use a Creative world ("unlock all") to get every fragment in the DataQubes menu.

Scoring note: "higher is better" (rating 1–3 drives the knowledge reward). For move/time-based games convert
to a higher-is-better score + rating before calling `api.complete({score, rating})`.

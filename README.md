# GorillaCaster

A casting / spectator camera mod for **Gorilla Tag** (PC Steam, BepInEx 5) — the kind of
tool casters and channels like *The Tree Trotters* use to film matches. It drives the game's
desktop third-person / shoulder camera (the view that shows on your monitor while you're in
VR), so your headset view is never touched. Point OBS at the game window and cast.

## Features

- **3 camera modes** (press `P` to cycle, or pick in the menu):
  - **Follow** – frames the cast player, with optional **auto-orbit** (or `Q`/`E` to orbit by hand).
  - **FreeCam** – fly anywhere: `WASD` move, `Space`/`Ctrl` up-down, `Shift` ×3 speed,
    `Alt` slow, hold **right-mouse** to look, scroll to zoom.
  - **First Person** – snaps to the cast player's head.
- **Cinematic Dolly / Director** – drop keyframes from the current camera (`K`), then `Play`
  (`L`) for a smooth Catmull-Rom camera move. Adjustable speed, optional loop.
- **Player switching** – `1`–`0` to jump to a player, `N`/`B` to cycle, or click in the
  Players tab. **Auto-cast** automatically follows whoever is "IT".
- **Stream overlays**:
  - Lower-third **NOW CASTING** bar with the player's color + IT status.
  - **Floating nametags** above every player (color-coded, IT flagged).
  - **Overhead minimap** with live player dots.
  - Player list + FPS / mode readout.
- **Camera controls** – FOV slider + presets, near-clip, follow distance/height,
  position & rotation smoothing.
- **World** – time of day (Night / Morning / Noon / Evening), weather (Clear / Rain).
- **Utility** – join room by code, leave room, keep AFK kick disabled, `F11` screenshot.
- **Configurable** – keybinds and defaults are exposed in the BepInEx config
  (`BepInEx/config/com.forza.gorillacaster.cfg`).

## Controls

| Key | Action |
|-----|--------|
| `Right Ctrl` | Open / close the menu |
| `P` | Cycle camera mode |
| `1`–`0` | Cast player by number |
| `N` / `B` | Next / previous player |
| `Q` / `E` | Orbit target left / right (Follow mode) |
| `K` / `L` | Add dolly keyframe / play-stop dolly |
| `F11` | Screenshot |

(Menu / mode / screenshot keys are remappable in the config file.)

## Build

Requires the .NET SDK. Game paths are set at the top of `GorillaCaster.csproj`
(`<GameDir>`); edit if your install isn't at `D:\Steam\steamapps\common\Gorilla Tag`.

```
dotnet build -c Release
```

The build copies `GorillaCaster.dll` into `<GameDir>\BepInEx\plugins\GorillaCaster\`.
Launch the game and press **Right Ctrl**.

## Requirements

- Gorilla Tag (Steam, PC) with **BepInEx 5** installed.
- Built against the current game assemblies; API verified via Mono.Cecil
  (`VRRigCache`, `BetterDayNightManager`, `PhotonNetworkController`, `GorillaTagger`, etc.).

## Project layout

| File | Purpose |
|------|---------|
| `src/Plugin.cs` | BepInEx entry point + config |
| `src/CasterController.cs` | Main loop: camera, input, menu, overlays |
| `src/DollyPath.cs` | Keyframed Catmull-Rom director path |
| `src/HudExtras.cs` | Nametags, minimap, shared rig helpers |
| `src/Styles.cs` | IMGUI theme / textures |

> Use it for content creation / casting in private & modded lobbies, per Gorilla Tag's
> modding policy. Don't use mods in public matchmaking.

# GorillaCaster

A casting / spectator camera mod for **Gorilla Tag** (PC Steam, BepInEx 5) — the kind of
tool casters and channels like *The Tree Trotters* use to film matches. It drives the game's
desktop third-person / shoulder camera (the view that shows on your monitor while you're in
VR), so your headset view is never touched. Point OBS at the game window and cast.

## Features

- **6 camera modes** (press `P` to cycle, or pick in the menu):
  - **Follow** – frames the cast player, with optional **auto-orbit** (or `Q`/`E` to orbit by hand).
  - **FreeCam** – fly anywhere: `WASD` move, `Space`/`Ctrl` up-down, `Shift` ×3 speed,
    `Alt` slow, hold **right-mouse** to look, scroll to zoom.
  - **First Person** – snaps to the cast player's head.
  - **Phone** – a **real in-VR phone** running a camera app. It has a **live viewfinder screen**
    and **poke-able on-screen buttons** (REC · MODE · FOV-/FOV+ · VIEW · TIME): hold it in one
    hand and tap the screen with the other to control the mod **without the PC**. Squeeze **grip**
    to pick it up, release to drop it (sticky placement). Phone mode broadcasts from its lens, with
    **stabilization** and **auto-level** options.
  - **Tripod** – plant a static camera anywhere; it stays put and auto-tracks the cast player.
  - **Selfie** – sits in front of the player's face looking back.
- **Clean First-Person** – first-person mode bumps the *casting* camera's near-clip so your own
  head cosmetics disappear from the broadcast, while your VR view stays untouched.
- **Instant Replay** – continuously buffers every player's motion, then replays it so you can
  rewind, **slow-mo** (0.1–2×), and scrub a moment — while flying the camera freely. `F6`
  record, `F7` play/stop, drag the on-screen bar to scrub.
- **Cinematic Dolly / Director** – drop keyframes from the current camera (`K`), then `Play`
  (`L`) for a smooth Catmull-Rom camera move. Adjustable speed, optional loop.
- **Player switching** – `1`–`0` to jump to a player, `N`/`B` to cycle, or click in the
  Players tab. **Auto-cast** automatically follows whoever is "IT".
- **Stream overlays** (premium rounded theme):
  - Lower-third **NOW CASTING** bar with the player's color + IT status.
  - **Reworked floating nametags** — rounded, distance-scaled, color dot, IT chip, optional
    **speed**, glowing accent on the cast target. Adjustable size.
  - **Overhead minimap** with live player dots.
  - Player list + FPS / mode / **speed (m/s)** readout.
  - **Cinematic letterbox** bars for a film look.
  - A configurable **watermark** ("Spooder's Camera Mod") with an opacity slider.
  - **`F8` hides every overlay** instantly for clean capture.
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
| `F6` / `F7` | Replay record / play-stop |
| `F8` | Hide all overlays (clean capture) |
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

### Obfuscated release build

`pack.ps1` builds, runs **Obfuscar** (`obfuscar.xml`), and deploys the obfuscated DLL.
Unity message methods (`Awake`/`Start`/`Update`/`LateUpdate`/`OnGUI`) are excluded from
renaming so the mod keeps working.

```
dotnet tool install --global Obfuscar.GlobalTool
./pack.ps1
```

Verified against the live game (Unity 6 / BepInEx 5): the obfuscated plugin loads with no
runtime errors.

## Requirements

- Gorilla Tag (Steam, PC) with **BepInEx 5** installed.
- Built against the current game assemblies; API verified via Mono.Cecil
  (`VRRigCache`, `BetterDayNightManager`, `PhotonNetworkController`, `GorillaTagger`, etc.).

## Project layout

| File | Purpose |
|------|---------|
| `src/Plugin.cs` | BepInEx entry point + config |
| `src/CasterController.cs` | Main loop: camera, input, menu, overlays |
| `src/GoProProp.cs` | Grabbable in-VR phone: model, viewfinder, poke buttons, grab |
| `src/DollyPath.cs` | Keyframed Catmull-Rom director path |
| `src/ReplayRecorder.cs` | Instant-replay buffer + playback |
| `src/HudExtras.cs` | Nametags, minimap, watermark, velocity, helpers |
| `src/Styles.cs` | Premium IMGUI theme |
| `src/TextureGen.cs` | Runtime rounded-rect / shadow textures |
| `src/UI.cs` | Themed widgets (sliders, switches, buttons) |
| `obfuscar.xml`, `pack.ps1` | Obfuscation config + release packaging |

> Use it for content creation / casting in private & modded lobbies, per Gorilla Tag's
> modding policy. Don't use mods in public matchmaking.

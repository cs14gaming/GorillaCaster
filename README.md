# Spooder's Camera Mod (GorillaCaster)

The most complete **free** casting / spectator camera mod for **Gorilla Tag** (PC Steam, BepInEx 5) —
built to out-feature the popular paid cam mods. It drives the game's desktop third-person /
shoulder camera (the view on your monitor while you're in VR), so your **headset view is never
touched**. Point OBS at the game window and cast.

**Highlights:** a smooth in-VR **iPad-style control tablet** (operate everything without the PC),
a real **multi-signal mod checker**, **competitive round timer + scoreboard**, color-grade
filters, **green screen**, **speed leaderboard**, camera presets, auto-director, cinematic
**dolly paths with saved shots**, 6 camera modes, premium overlays, full settings persistence,
and full obfuscation.

## Features

- **6 camera modes** (press `P` to cycle, or pick in the menu):
  - **Follow** – frames the cast player, with optional **auto-orbit** (or `Q`/`E` to orbit by hand).
  - **FreeCam** – fly anywhere: `WASD` move, `Space`/`Ctrl` up-down, `Shift` ×3 speed,
    `Alt` slow, hold **right-mouse** to look.
  - **First Person** – snaps to the cast player's head (Pokruk-style; hides your worn hat/face
    cosmetics locally, with eye offset and optional **angle clamp**).
  - **Tablet** – a **real in-VR iPad** running a camera app: a **live viewfinder screen** and
    **poke-able on-screen buttons**. Hold it in one hand and tap the screen with the other to
    control the mod **without the PC**. Squeeze **grip** to pick it up (or press **A** to
    summon/dismiss). Modern rounded body with a camera module and banana logo on the back.
  - **Tripod** – plant a static camera anywhere; it stays put and auto-tracks the cast player.
  - **Selfie** – sits in front of the player's face looking back.
- **Lens** – FOV slider (10–150°) with **scroll-to-zoom in any mode**, FOV presets
  (Fisheye / Wide / Normal / Tele), near-clip.
- **Cinematic rig** – **camera collision** (no wall clipping), **roll lock** (level horizon) or
  manual dutch roll, **handheld shake**, orbit pitch, and **Frame-all** to fit every player.
- **Multi-signal mod checker** – per-player **CLEAN / SUS / CHEATING** verdicts (color-coded) on
  the tablet, combining custom-property cheat-signature scanning (cosmetic spoofers / named menus,
  user-extensible via `BepInEx/config/GorillaCaster_cheatkeys.txt`) with physics/rig anomalies:
  speed, teleport, flight, **arm-stretch pull mods**, tag-from-range, **play-space-abuse glide**,
  scale and impossible-colour spoofing.
- **Competitive overlay** – Infection **round timer** (3:00 cap with colour states) + live
  **scoreboard** (survivors vs infected, who's IT), plus optional manual **team scores**.
- **Speed leaderboard** – sorted live ranking of every player by speed.
- **Color grade, green screen & framing** – 8 filter looks + strength, vignette, a **green-screen**
  background (RGB pickers) for chroma-key editing, **aspect guides** (16:9 / 2.39 / 4:3 / 9:16 /
  1:1) with letterboxing, rule-of-thirds grid, center crosshair.
- **Camera presets** – one-tap camera+look setups saved as human-editable JSON; cycle with `[ ]`.
- **Cinematic Dolly / Director** – drop keyframes (`K`), then `Play` (`L`) for a smooth
  Catmull-Rom move; **save named shots** to reuse across matches; auto-director frames the
  survivor about to be tagged.
- **Player switching** – `1`–`0` to jump to a player, `N`/`B` to cycle, or click in the
  Players tab. **Auto-cast** automatically follows whoever is "IT".
- **Stream overlays** (premium rounded theme) – lower-third **NOW CASTING** bar, reworked
  **occluded floating nametags** (rounded, distance-scaled, IT chip, optional speed), overhead
  **minimap**, player list, FPS / mode / speed readout, **cinematic letterbox**, configurable
  **watermark**, optional in-headset **VR nametags**, and **`F8` hides every overlay** instantly.
- **World** – time of day (Night / Morning / Noon / Evening), weather (Clear / Rain).
- **Utility** – join room by code, leave room, keep AFK kick disabled, `F11` screenshot.
- **Settings persistence** – every toggle and tunable is saved to
  `BepInEx/config/GorillaCaster_settings.json` and restored on launch.

## Controls

| Key | Action |
|-----|--------|
| `Right Ctrl` | Open / close the menu |
| `P` | Cycle camera mode |
| `1`–`0` | Cast player by number |
| `N` / `B` | Next / previous player |
| `Scroll` | Zoom FOV (any mode) |
| `Q` / `E` | Orbit target (Follow mode) |
| `K` / `L` | Add dolly keyframe / play-stop dolly |
| `[` / `]` | Previous / next preset |
| `A` (right controller) | Summon / dismiss the in-VR tablet |
| `F1` | Hotkey cheatsheet |
| `F8` | Hide all overlays (clean capture) |
| `F11` | Screenshot |

The in-VR **tablet** mirrors the core controls (cast next/prev, mode, FOV, viewfinder, mod
checker, screenshot) plus a live viewfinder — so you can cast entirely from inside VR.

Menu / mode / screenshot keys are remappable in the BepInEx config
(`BepInEx/config/com.forza.gorillacaster.cfg`).

## Installation

**Recommended:** install with [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the
Thunderstore Mod Manager — search for **GorillaCaster** and click Install.

**Manual:** install [BepInEx 5](https://thunderstore.io/c/gorilla-tag/p/BepInEx/BepInExPack/),
then drop `GorillaCaster.dll` into `…\Gorilla Tag\BepInEx\plugins\`. Launch the game and press
**Right Ctrl**.

## Build from source

Requires the .NET SDK. Game paths are set at the top of `GorillaCaster.csproj` (`<GameDir>`).

```
dotnet build -c Release
```

The build copies `GorillaCaster.dll` into `<GameDir>\BepInEx\plugins\GorillaCaster\`.

### Obfuscated release + Thunderstore package

`pack.ps1` builds, runs **Obfuscar** (`obfuscar.xml`), and deploys the obfuscated DLL.
`pack-thunderstore.ps1` builds the obfuscated DLL and zips a ready-to-upload Thunderstore
package into `dist/`.

```
dotnet tool install --global Obfuscar.GlobalTool
./pack.ps1                 # build + obfuscate + deploy to your game
./pack-thunderstore.ps1    # build the Thunderstore upload zip
```

## Requirements

- Gorilla Tag (Steam, PC) with **BepInEx 5** installed.
- Verified against the live game (Unity 6 / BepInEx 5): the obfuscated plugin loads with no
  runtime errors.

> Use it for content creation / casting in private & modded lobbies, per Gorilla Tag's
> modding policy. Don't use mods in public matchmaking.

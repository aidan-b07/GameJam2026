<div align="center">

<img src="Assets/Art/UI/Menu/title_jersey.png" alt="Tick & Tock" width="480" style="image-rendering: pixelated;">

# Tick & Tock

**A two-player, split-screen co-op platformer where each of you controls the other's time.**

[![Unity](https://img.shields.io/badge/Unity-6000.6.8f1-000000?logo=unity&logoColor=white)](https://unity.com/releases/editor/archive)
[![C#](https://img.shields.io/badge/C%23-.NET-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![URP 2D](https://img.shields.io/badge/URP-2D%20Renderer%2017.6-222C37?logo=unity&logoColor=white)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/index.html)
[![Input System](https://img.shields.io/badge/Input%20System-1.20.1-4B8BBE?logo=unity&logoColor=white)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/manual/index.html)
[![Players](https://img.shields.io/badge/players-2%20local%20co--op-E8A33D)](#controls)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)](#building)
[![Gamepad](https://img.shields.io/badge/gamepad-Xbox%20%7C%20PlayStation-107C10?logo=xbox&logoColor=white)](#controls)
[![Game Jam](https://img.shields.io/badge/CSS%20Game%20Jam-2026-B8860B)](#the-game-jam)
[![Theme](https://img.shields.io/badge/theme-Time-6A4C93)](#the-game-jam)
[![Made in](https://img.shields.io/badge/made%20in-24%20hours-C0392B)](#the-game-jam)
[![Art](https://img.shields.io/badge/art-pixel%20art-9B59B6)](#visuals--feel)
[![License: CC BY-NC 4.0](https://img.shields.io/badge/license-CC%20BY--NC%204.0-lightgrey)](LICENSE)

<!-- TODO: once a web build is hosted, add: ### [▶ Play in your browser](https://your-url) -->
**A browser version is coming soon.**

</div>

---

<p align="center">
  <img src="docs/screenshots/day-night-lanes.webp" alt="Tick in a daytime lane on top, Tock in a night lane below" width="100%">
</p>

## The premise

Tick and Tock are two little clockwork keepers, each racing through their own lane of a clockwork city. The screen is split in two: **Tick runs the top lane, Tock the bottom one**, and each lane has its own clock, its own sky and its own flow of time.

The twist: **you don't control your own time — your partner does.**

- While your partner **stands still**, your lane crawls along at a sluggish **0.35×**.
- While your partner **keeps running**, your lane ramps up (on an S-curve) to a frantic **3×**.
- When they stop, your lane keeps its speed for a moment, then winds back down.

Every obstacle in a lane runs on that lane's time. A pendulum that drifts lazily over the belt when your partner waits will knock you into the drop when they sprint. A corridor of pistons that slams shut behind you can only be outrun while your partner stands still. So the two of you constantly negotiate: *"Wait — stand still so I can get through the pistons. Okay, now run!"*

Meanwhile, **the Unraveling** — a rift hanging from the top of each lane — creeps down towards any player who dawdles, and grows faster the longer they stand still. Standing still helps your partner, but costs you. Neither of you can wait forever.

**One dies, both restart.** The pair shares checkpoints and a single side-scrolling camera, so you either make it through together or not at all. Reach your clock doors at the end of the level and the night finally gives way to the **Dawn of a New Day**.

## Screenshots

| | |
|---|---|
| ![Main menu](docs/screenshots/main-menu.png) <br> *The main menu* | ![The Unraveling rift swallowing the top lane](docs/screenshots/rift.webp) <br> *The Unraveling pours down on a player who stands still too long* |
| ![Clock pendulums over the belts](docs/screenshots/pendulum-belts.webp) <br> *Clock pendulums over the conveyor belts* | ![The same pendulums at two different lane speeds](docs/screenshots/pendulums-fast-slow.webp) <br> *Same obstacles, two speeds of time* |
| ![The piston run](docs/screenshots/piston-run.webp) <br> *The piston run: rams slam up behind the runner* | ![Clock presses](docs/screenshots/clock-presses.webp) <br> *Clock presses over the gaps* |
| ![Clock presses with the rift closing in](docs/screenshots/clock-presses-rift.webp) <br> *Wait too long for your partner and the rift closes in* | ![The clock doors at the end of the level](docs/screenshots/clock-doors.webp) <br> *The clock doors at the end of each lane* |
| ![Both clock doors open at night](docs/screenshots/doors-open.webp) <br> *Both doors open and the clocks spin towards dawn* | ![Sunrise over both lanes](docs/screenshots/dawn.webp) <br> *Dawn of a New Day: the rifts close into a gold seam* |

<p align="center">
  <img src="docs/screenshots/the-end.webp" alt="The ending title card: Tick &amp; Tock, The End" width="80%">
</p>

## Features

### Gameplay
- **Cross-controlled time** — each player's *measured* movement (the distance their body really covered, so pushing into a wall counts as standing and riding a conveyor counts as moving) drives the partner's lane speed between 0.35× and 3×.
- **Per-lane clocks** — each lane keeps its own hour (Tick starts at 10:00, Tock at 02:00), shown on the HUD and on the clock doors.
- **The Unraveling rift** — grows exponentially while its player stands still, holds while they move steadily and retreats while they run. If it reaches its player, both players die.
- **Shared fate** — any death kills both players: they freeze, the camera eases back, each player's light flies from the checkpoint lamp as a wisp, and both respawn together.
- **Paired checkpoints** — one per lane; a checkpoint only counts once *both* players have reached their own.
- **Co-op camera** — one shared horizontal scroll with a platformer dead zone; an elastic "screen leash" lets the leader push the edge a little but never leave the partner behind.
- **Scenic zone** — a calm stretch mid-level where both lanes ease to real time and the rifts retract, so you can catch your breath and take in the view.
- **Clock doors & finale** — the first to arrive unlocks their door and is safe while waiting for their partner; once both doors are open, the scripted *Dawn of a New Day* ending plays: the lane clocks spin to 06:00, the rifts close into a gold seam, the sun rises over both cities, and the title card rolls.

### The obstacle kit (all running on lane time)
| Obstacle | What it does |
|---|---|
| **Clock pendulums over belts** | A heavy brass bob that shoves players off a conveyor into a drop — hard mid-swing, a gentle nudge near the belt's ends. |
| **Clock presses** | Plunger crushers that wait raised, slam down and ratchet back up. |
| **Piston run** | A corridor of flush floor rams that slam up one after another, chasing the runner — only outrunnable while your partner stands still. |
| **Drawbridge & trap doors** | A heavy winched drawbridge that swings open over a pit and back down into it; trap doors that warn, then drop out from under you. |
| **Conveyor belts** | Belts that keep carrying you, so you can't simply wait on them. |
| **Button gates** | Portcullises that only open once floor buttons are pressed, in either lane. |

More obstacles were built and tested in the `ObstacleLab` scene but didn't make it into the jam level: a pendulum blade, a gear fall, a water-wheel bucket lift, a steam vent, a Tesla gate, chain lifts and speed-winched time gates.

### Visuals & feel
- **Hand-made pixel art**, point-filtered, rendered with the **URP 2D Renderer**; menus are drawn in world space into a 640×360 render texture and scaled up crisply.
- **Day / dusk / night per lane** (`LaneSky`) — each lane's backgrounds crossfade with its own clock while the sun and moon arc overhead and the tilemaps are tinted to match.
- **Parallax city** with animated sky life, chimney smoke, clouds and glowing decor.
- **Readable HUD** — a brass gauge per lane with the partner's face, a springy speed dial and a small clock; a pulsing glow and a dithered shade warn when the rift is close. Fast lanes get "edge wind" and a warm grade, slow lanes a cool one.
- **Split-screen-aware audio** — every sound is panned and faded by its position on screen and **pitched by its lane's speed**, so a fast lane literally sounds faster. One looping music track carries across scenes.

## Controls

Two players share one machine. The first connected gamepad drives **Tick** (player 1), the second drives **Tock** (player 2); a player without a gamepad uses their half of the keyboard.

| Action | Tick — Player 1 (top lane) | Tock — Player 2 (bottom lane) | Gamepad |
|---|---|---|---|
| Move | `A` / `D` | `←` / `→` | Left stick or D-pad |
| Jump (hold for higher) | `W` | `↑` | `A` (Xbox) / `✕` (PlayStation) |
| Pause | `Esc` | `Esc` | `Start` |

Jumping has coyote time and a jump buffer; releasing jump early gives a shorter hop.

**Menus:** `↑ ↓ ← →` / `W A S D` / D-pad / left stick to move, `Enter` / `Space` / `A` to select, `Esc` / `Backspace` / `B` to go back. The mouse works too.

### Judge shortcuts

Hidden keys for quickly reviewing the level (work in the editor and in builds):

| Key | Effect |
|---|---|
| `F1` – `F3` | Warp both players to checkpoint 1–3 |
| `F4` | Warp both players just before their clock doors — walk right to trigger the ending |
| `F5` | Toggle a debug readout: lane speeds, lane hours, rift depths, FPS |

## Tech stack

| | |
|---|---|
| **Engine** | Unity **6000.6.8f1** (Unity 6) |
| **Language** | C# |
| **Rendering** | Universal Render Pipeline **17.6.0** with the 2D Renderer, custom HLSL shader (`Assets/Resources/Shaders`) |
| **Input** | Input System **1.20.1** (keyboard + up to two gamepads, read per device) |
| **2D tooling** | Unity 2D feature set 2.0.2 (Tilemap, Sprite, Pixel Perfect, Aseprite importer), uGUI 2.6.0 |
| **Content pipeline** | Editor builder scripts that generate the levels and menus (`Tick & Tock` menu in the editor); Python + Pillow scripts for pixel-art UI generation (`tools/`) |
| **Version control** | Git + Git LFS (art and audio) |

## Getting started

### Requirements
- **Unity 6000.6.8f1** (install via Unity Hub — other versions may upgrade the project).
- **[Git LFS](https://git-lfs.com/)** — art and audio are stored in LFS. Without it, the images are text pointers and the game will look broken.

### Open and play in the editor
```bash
git lfs install
```
```bash
git clone https://github.com/aidan-b07/GameJam2026.git
```
1. Open the cloned folder in Unity Hub with Unity 6000.6.8f1.
2. Open `Assets/Scenes/MainMenu.unity` and press **Play**.
   (Or open `Assets/Scenes/MainLevel.unity` to jump straight into the level.)
3. Plug in one or two gamepads, or share the keyboard.

### Building
1. **File → Build Profiles**, pick **Windows** (the platform the game is tested on).
2. The scenes in the build are `LevelSelect`, `MainMenu` and `MainLevel`.
   <!-- TODO: LevelSelect is currently scene 0 in the build list, so a build starts there. Move MainMenu to the top if the build should open on the title screen. -->
3. **Build** (or **Build and Run**).

### Editor tools
The **`Tick & Tock`** menu in the Unity menu bar rebuilds content from code:
`Import Art` (point filtering, slicing), `Build Main Menu`, `Build Level Select`, `Build Main Level`, `Build Obstacle Lab`, `Build Level 1 Map`, `Build Level 2 Map`.

## Project structure

```
Assets/
├── Art/            Pixel art: Backgrounds, Characters, Decor, Obstacles, Sky, Tiles, UI
├── Editor/         Level / menu builders, art importer, ending preview, level test bot
├── Prefabs/        Player prefabs (Tick, Tock)
├── Resources/      Runtime-loaded audio and shaders
├── Scenes/         MainMenu, LevelSelect, MainLevel (+ ObstacleLab and Level 1/2 prototypes)
├── Scripts/
│   ├── GameManager.cs   Partner-driven lane speeds and lane clocks
│   ├── LaneSky.cs       Per-lane day / dusk / night
│   ├── Audio/           Music, split-screen SFX, player sounds
│   ├── Camera/          CoopCamera, Parallax, ScreenLeash
│   ├── Game/            GameFlow (death & respawn), checkpoints, rift, HUD, pause,
│   │                    scenic zone, clock doors, Dawn ending, judge keys
│   ├── Hazards/         Crush detection
│   ├── Menu/            Main menu, level select
│   ├── Obstacles/       Pendulums, floor rams (+ the ObstacleLab prototypes)
│   └── Player/          Movement and sprites
└── Settings/       URP assets and renderers
tools/menu/         Python scripts that generate menu and level-select art
```

## The game jam

**Tick & Tock** was made in 24 hours for the **CSS Game Jam 2026**.

| | |
|---|---|
| **Theme** | **Time** |
| **Jam window** | 7 November 2026, 12:00 → 8 November 2026, 12:00 (24 hours) |

**How we used the theme.** Time is the game's main mechanic, not just its setting:
- Each player controls how fast time runs for their **partner**. You can't speed up or slow down your own lane, only theirs.
- Every lane has its own clock and sky, so the two players are often at different times of day.
- Every obstacle runs on its lane's time, so the same pendulum or piston can be slow and easy for one player and fast and deadly for the other.
- Standing still to slow your partner's time lets the Unraveling eat into yours.
- The ending is the moment both clocks finally agree: they spin forward together to 06:00, the **Dawn of a New Day**.

## Team & credits

Made by a small team with no fixed roles: everyone worked on whatever needed doing.

- **Aidan**
- **Samuel**
- **Ilai**

**Everything in the game was made by the team during the jam:** code, pixel art, levels, sound effects and music. No third-party assets were used.

## License

© 2026 Aidan, Samuel and Ilai. Licensed under [Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)](https://creativecommons.org/licenses/by-nc/4.0/). See [LICENSE](LICENSE).

You're welcome to play, study, modify and share the game, its code and its art, as long as you **credit us** and **don't use it commercially**. That means no selling it, putting it behind a paywall, or using it in paid products.

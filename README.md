<div align="center">

<img src="Assets/Art/UI/Menu/title_jersey.png" alt="Tick & Tock" width="480" style="image-rendering: pixelated;">

# Tick & Tock

**A two-player, split-screen co-op platformer where each of you controls the other's time.**

[![Unity](https://img.shields.io/badge/Unity-6000.6.8f1-000000?logo=unity&logoColor=white)](https://unity.com/releases/editor/archive)
[![C#](https://img.shields.io/badge/C%23-.NET-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![URP 2D](https://img.shields.io/badge/URP-2D%20Renderer%2017.6-222C37?logo=unity&logoColor=white)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/index.html)
[![Input System](https://img.shields.io/badge/Input%20System-1.20.1-4B8BBE?logo=unity&logoColor=white)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/manual/index.html)
[![Players](https://img.shields.io/badge/players-2%20local%20co--op-E8A33D)](#controls)
<!-- TODO: confirm which platforms you ship builds for, then update this badge. -->
[![Platform](https://img.shields.io/badge/platform-PC%20(Standalone)-0078D6?logo=windows&logoColor=white)](#building)
[![Gamepad](https://img.shields.io/badge/gamepad-Xbox%20%7C%20PlayStation-107C10?logo=xbox&logoColor=white)](#controls)
[![Game Jam](https://img.shields.io/badge/CSS%20Game%20Jam-2026-B8860B)](#the-game-jam)
[![Art](https://img.shields.io/badge/art-pixel%20art-9B59B6)](#visuals--feel)
<!-- TODO: add a license badge once a LICENSE file exists. -->

<!-- TODO: replace with your itch.io / jam submission links -->
**[▶ Play on itch.io](#) (TODO)** · **[Jam submission page](#) (TODO)**

</div>

---

## The premise

Tick and Tock are two little clockwork keepers, each racing through their own lane of a clockwork city. The screen is split in two: **Tick runs the top lane, Tock the bottom one**, and each lane has its own clock, its own sky and its own flow of time.

The twist: **you don't control your own time — your partner does.**

- While your partner **stands still**, your lane crawls along at a sluggish **0.35×**.
- While your partner **keeps running**, your lane ramps up (on an S-curve) to a frantic **3×**.
- When they stop, your lane keeps its speed for a moment, then winds back down.

Every obstacle in a lane runs on that lane's time. A pendulum blade that crawls when your partner waits becomes a blur when they sprint. A water wheel only turns when the water gushes, and the water only gushes when your partner runs. So the two of you constantly negotiate: *"Wait — stand still so I can get past the blade. Okay, now run, I need the wheel!"*

Meanwhile, **the Unraveling** — a rift hanging from the top of each lane — creeps down towards any player who dawdles, and grows faster the longer they stand still. Standing still helps your partner, but costs you. Neither of you can wait forever.

**One dies, both restart.** The pair shares checkpoints and a single side-scrolling camera, so you either make it through together or not at all. Reach your clock doors at the end of the level and the night finally gives way to the **Dawn of a New Day**.

## Screenshots

> **TODO (screenshots):** capture these in the Game view (Play mode, 16:9, ideally 1920×1080) and save them in [`docs/screenshots/`](docs/screenshots/) with the file names below. Until then these links are placeholders.

| | |
|---|---|
| ![Main menu — TODO capture](docs/screenshots/main-menu.png) <br> *Main menu* | ![Split screen gameplay — TODO capture](docs/screenshots/split-screen.png) <br> *Tick (top) and Tock (bottom), one lane fast, one slow* |
| ![Pendulum belts — TODO capture](docs/screenshots/pendulums.png) <br> *Clock pendulums over the belts* | ![The Unraveling rift — TODO capture](docs/screenshots/rift.png) <br> *The Unraveling closing in on a dawdling player* |
| ![Day and night lanes — TODO capture](docs/screenshots/day-night.png) <br> *Two lanes, two times of day* | ![Dawn of a New Day ending — TODO capture](docs/screenshots/dawn-ending.png) <br> *Dawn of a New Day* |

<!-- TODO: a short GIF of a lane speeding up as the partner runs would sell the mechanic best: docs/screenshots/gameplay.gif -->

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
| **Pendulum blade** | A crescent blade swinging across the path — one swing every ~7 s at 0.35×, a blur at 3×. |
| **Clock pendulums over belts** | A heavy brass bob that shoves players off a conveyor into a drop — hard mid-swing, a gentle nudge near the belt's ends. |
| **Gear fall** | A chute drops brass cogs; in a slow lane they hang and roll slowly enough to use as stepping stones. |
| **Piston run** | A corridor of flush floor rams that slam up one after another, chasing the runner — only outrunnable while your partner stands still. |
| **Water-wheel bucket lift** | Only turns while the partner runs; ride a bucket up to a ledge. Simulated with momentum, so a rider's weight turns it back. |
| **Steam vent** | Blasts players up and over walls too high to jump. |
| **Tesla gate** | Arcs across the gap; a slow lane stretches the safe window to seconds. |
| **Drawbridge & trap doors** | A heavy winched drawbridge that swings open over a pit and back down into it; trap doors that warn, then drop out from under you. |
| **Conveyors, lifts & gates** | Belts that won't let you wait, chain lifts, portcullises and sliding bridges winched by lane speed, and floor-button gates that need both players. |

### Visuals & feel
- **Hand-made pixel art**, point-filtered, rendered with the **URP 2D Renderer**; menus are drawn in world space into a 640×360 render texture and scaled up crisply.
- **Day / dusk / night per lane** (`LaneSky`) — each lane's backgrounds crossfade with its own clock while the sun and moon arc overhead and the tilemaps are tinted to match.
- **Day/night ambient light shaders** (`Ambient.shader` + `LaneAmbience`) — three pixel-snapped, dithered layers per lane camera: skyline mist, a time-of-day colour grade with sun warmth, moon lift and vignette, and a light pass with god rays, sun bloom, moon halo, ground fog, dust motes and fireflies. <!-- TODO: this lives on branch claude/ambient-shaders-day-night-25a6c7 — merge it before submitting, or remove this line. -->
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
1. **File → Build Profiles**, pick your platform (Windows / macOS / Linux standalone).
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
│   ├── Obstacles/       Pendulums, gear fall, floor rams, steam vent, Tesla gate, water wheel
│   └── Player/          Movement and sprites
└── Settings/       URP assets and renderers
tools/menu/         Python scripts that generate menu and level-select art
```

## The game jam

**Tick & Tock** was made for the **CSS Game Jam 2026**. Development in this repository ran from **7 November 2026** onwards.

<!-- TODO: fill in the jam details -->
- **Theme:** TODO
- **Dates / duration:** TODO
- **How we interpreted the theme:** TODO — e.g. how "each player controls the other's time" answers it.
- **Submission:** TODO (link)

## Team & credits

<!-- TODO: names as you want them shown, and each person's role (code, art, level design, audio...). Names below are taken from the git history. -->
| Who | Role |
|---|---|
| **Aidan** | TODO |
| **Samuel** | TODO (obstacle design incl. the water wheel and piston run) |
| **Ilai** | TODO |
| TODO (Unity Version Control contributor) | TODO (Level 1 "The Clockworks" art import and layout) |

**Third-party assets** — TODO: credit the "Asset Mega-Pack" imported in the first changesets (name, author, license, link), and the pixel fonts used for the title art (Jersey, Tiny, Workbench, Pixelify — confirm their names and licenses).

**Music** — TODO: credit the composer / tool.

## License

TODO — no license file yet. Add a `LICENSE` (and update the badge at the top) before publishing.

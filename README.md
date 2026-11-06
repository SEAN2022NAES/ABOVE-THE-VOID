---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 6491cd71091331ad1a3a0da9eb56ff2c_03a66e85d45911f1a0da525400393706
    ReservedCode1: t64hLD960epjqYxq13Ff3Z+90HlWeNxWGKKuulFbBBs8MiRMD4edW2bx/an2+vGkJFkRDVLaobX1srjx1iZzPJMxpx4pM7Jm0gciRAVFFDdHXMLoUxtckki8bTVTJAtilLDwRSuKcizKzhXlGtjRNIMwDHhiSImnlypP20seeB2Dv0XL6gdDASHgTsU=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 6491cd71091331ad1a3a0da9eb56ff2c_03a66e85d45911f1a0da525400393706
    ReservedCode2: t64hLD960epjqYxq13Ff3Z+90HlWeNxWGKKuulFbBBs8MiRMD4edW2bx/an2+vGkJFkRDVLaobX1srjx1iZzPJMxpx4pM7Jm0gciRAVFFDdHXMLoUxtckki8bTVTJAtilLDwRSuKcizKzhXlGtjRNIMwDHhiSImnlypP20seeB2Dv0XL6gdDASHgTsU=
---

# ABOVE THE VOID - Tuanjie 3D Coordinate System Framework

Current version: **v0.1.4**

### v0.1.4

- Fixed the level opening hold: entering Level 3 from another level with `/level` no longer spawns blocks immediately; the opening pause is now enforced by a hard timer on the spawner.
- Added `LevelOpeningGate`, `LevelPresentation`, and `ScreenFade` for unified level openings, presentation state, and fade transitions.
- Added `CodeSyntaxTutorialController` for the code syntax teaching flow.
- Simplified `CodeTower.FireAtPlane` to three parameters `(A, B, C)` for a plane that always passes through the origin.
- The code tower body stays permanently hidden; only its interface remains visible.
- Corrected coordinate axis label orientation, color, and distance compensation.
- Block HP is capped at 6 and decrements as hits land.
- Manual laser (left mouse) is limited to the Level 1 - Part 1 tutorial; other level parts rely on tower code and automatic laser scripts.
- Closing the code editor or the developer console with ESC no longer also pauses the game.
- The `/level` developer command now accepts an optional third parameter to jump directly to a chapter.
- Refined Level 1 tutorial hints and transitions.

### v0.1.3

- Added per-run scoring with kill rewards, leak penalties, and score delta feedback.
- Added the top-left score HUD, including negative-score styling and automatic delta fading.
- Added score reset integration when starting or restarting a level.

### v0.1.2

- Added the in-game code editor interface for the code tower.
- Added C# syntax highlighting, line numbers, vertical and horizontal scrolling, and font zoom.
- Added undo/redo support, compile diagnostics, runtime error feedback, and code execution controls.
- Added code tower and screen-fitting UI support.

### v0.1.1

- Added the main menu, level entry points, and English/Chinese language selection.
- Added the Level 1 tutorial flow, level navigation, and reset support.
- Added automatic laser example support and improved block hit feedback.
- Added localized font resources for runtime UI rendering.

### v0.0.3

- Added flying block spawning and movement, block types, crosshair highlighting, center markers, and laser destruction hooks.

### v0.0.2

- Added the developer console, crosshair, developer overlay, time freeze, time hints, and an invertible crosshair shader.

The core experience includes a 3D coordinate system, an origin camera, mouse look, settings, pause support, and two free-flight modes.

---

## 1. Features

| Feature | Description |
|---|---|
| 3D coordinate system | Coordinate range [-20, 20]. Z is the green vertical axis, Y is the red left axis, and X is the blue right axis. |
| Origin camera | The Main Camera remains at (0, 0, 0) in Normal mode. |
| Mouse look | Locked cursor with adjustable sensitivity in the pause/settings panel. |
| Main menu | Startup menu with project information, language selection, and level entry points. |
| Level 1 tutorial | Guided introduction to movement, camera control, and flying blocks. |
| Flying blocks | Axis-aligned blocks travel toward the origin with type-specific colors and behavior. |
| Score system | Earn points for destroying Attack and Heal blocks, receive penalties when Attack blocks leak, and keep Bomb blocks score-neutral. |
| Developer console | Inspect state and navigate to supported level parts. |
| Code editor | Press Enter to edit tower code with syntax highlighting, diagnostics, undo/redo, and runtime execution. |
| Pause | Press ESC in any gameplay mode to pause and resume. |
| Q / F1 modes | Strict axis movement or camera-relative free flight. |

## 2. Requirements

- Tuanjie **1.11.0**, based on the `2022.3.62t17` editor line.
- Built-in Render Pipeline or URP as supported by the installed Tuanjie editor.
- The project uses the legacy Input Manager API (`Input.GetAxis`, `Input.GetKey`).
- Set `Project Settings -> Player -> Active Input Handling` to **Both** or **Input Manager**.

## 3. Project Structure

```
ABOVE THE VOID/
├── Assets/
│   ├── Editor/
│   │   └── GameSceneSetup.cs           # Scene setup utility
│   └── Scripts/
│       ├── GameBootstrap.cs              # Runtime scene bootstrap
│       ├── MainMenuController.cs         # Main menu and level entry points
│       ├── TutorialController.cs         # Level 1 guided tutorial
│       ├── Localization.cs               # English/Chinese UI localization
│       ├── LevelDirector.cs               # Level and part navigation
│       ├── FlyingBlock.cs                 # Flying block behavior and visuals
│       ├── FlyingBlockSpawner.cs          # Block spawning and registration
│       ├── BlockHitIndicator.cs           # Crosshair hit feedback
│       ├── ScoreManager.cs                # Per-run score and HUD
│       ├── CodeEditorController.cs        # In-game code editor and runner
│       ├── CodeTower.cs                   # Code-controlled tower interface
│       ├── UIFitScreen.cs                 # Screen-fitting UI helper
│       ├── PlayerCameraController.cs      # Camera and movement state machine
│       ├── CoordinateAxesDrawer.cs        # 3D coordinate axes
│       └── SettingsPanelController.cs     # Pause and settings UI
├── Packages/
│   └── manifest.json                   # Package dependencies
└── README.md
```

## 4. Quick Start

1. Open this folder with the Tuanjie editor or its project manager.
2. The editor automatically creates and opens `Assets/Scenes/Main.unity` when the scene does not exist.
3. Press Play to open the main menu.
4. Choose a language and start Level 1. The tutorial controls the initial spawning sequence.

If the scene is not generated automatically, use `ABOVE THE VOID -> Setup Game Scene` in the Tuanjie editor. The tool enables the required input handling, creates the scene, attaches `GameBootstrap`, and saves `Assets/Scenes/Main.unity`.

For a manual setup, create an empty scene, add an empty GameObject with `GameBootstrap.cs`, and press Play. The bootstrap creates or reuses the Main Camera, coordinate axes, player controller, UI, menu, tutorial, and block systems.

## 5. Manual Setup

| Step | Action |
|---|---|
| 1 | Create or confirm a Main Camera tagged `MainCamera` at position (0, 0, 0). |
| 2 | Create an empty `CoordinateAxes` object and attach `CoordinateAxesDrawer`. |
| 3 | Create a `PlayerCameraController` object and attach `PlayerCameraController`. Leave `Player Camera` empty to use `Camera.main` automatically. |
| 4 | Create a `UI_SettingsPanel` object and attach `SettingsPanelController`. |
| 5 | Press Play and verify that the main menu appears. |

> `PlayerCameraController.Start()` automatically finds `SettingsPanelController` and subscribes to sensitivity updates. Keep the UI and player controller objects in the same scene.

## 6. Controls

| Input | Action |
|---|---|
| Mouse movement | Look around. |
| W / S | Move along the X axis in Spirit mode. |
| A / D | Move along the Y axis in Spirit mode. |
| Space / C | Move along the Z axis in Spirit mode. |
| Left Shift | Sprint at 2x movement speed. |
| Q | Enter or leave strict coordinate-axis mode. |
| F1 | Enter or leave camera-relative free-flight mode. |
| E | Freeze or resume time. |
| ESC | Pause or resume gameplay. |
| `~` | Open or close the developer console. |
| Enter | Open or close the code editor. |
| Settings button | Open the pause/settings panel. |

## 7. Camera State Machine

```
             Q                          Q
  Normal (origin look) <----------> Spirit (free flight)
       ^  |                         ^  |
       |  ESC (pause/resume)        |  ESC (pause/resume)
       |  v                         |  v
       Paused (timeScale = 0, cursor unlocked, pause panel visible)
```

- **Normal**: The camera stays at the origin and rotates in place.
- **Spirit**: The camera can move. Movement uses the camera's horizontal direction.
- **Paused**: The previous gameplay state is saved and restored on resume. Sensitivity can still be adjusted while paused.

## 8. Camera Design

The project uses a **locked cursor with adjustable sensitivity** instead of an orbit camera around the origin:

1. The Main Camera can remain at the required origin position (0, 0, 0).
2. Normal look and Spirit movement share the same mouse rotation logic.
3. FPS-style controls make sprinting, pitch limits, camera shake, and touch controls easier to add.

An orbit camera can be added later with `yaw` and `pitch`: `pos = origin + Quaternion.Euler(pitch, yaw, 0) * radius * Vector3.back`.

## 9. Adjustable Parameters

Use the Inspector to adjust:

- `CoordinateAxesDrawer`: axis length, tick spacing, line width, arrow size, and axis colors.
- Coordinate convention: X is right, Y is left, and Z is vertical. The first unit tick uses the matching axis color.
- Spirit movement is limited to X/Y/Z in [-20, 20].
- Axis labels use generated colored TextMesh objects and compensate for camera distance.
- `PlayerCameraController`: mouse sensitivity, pitch limits, movement speed, sprint multiplier, and key bindings.
- `SettingsPanelController`: sensitivity range and UI colors.
- `FlyingBlockSpawner`: spawn interval, block size limits, speed, spawn distance, and block type probabilities.
- `FlyingBlock`: size, speed, overlap margin, and externally controlled destruction.
- `CodeEditorController`: editor font sizes, colors, line height, caret blink rate, and the Enter key binding.

## 10. Future Extensions

- Ground grid or reference planes for easier positioning.
- A player capsule and physics-based third-person Spirit mode.
- Migration to the new Input System with gamepad and touch support.
- A dedicated `GameStateManager` for menus, dialogue, and combat states.
- Save data for sensitivity, camera orientation, and world coordinates.
- Interactive coordinate markers and camera teleportation.

---

## 11. Known Notes

- The UI, coordinate axes, and gameplay systems are created at runtime. Save a manually configured scene if you need persistent scene objects.
- If axis materials render incorrectly in URP, replace the LineRenderer material with `URP/Unlit/Color`.
- Pausing uses `Time.timeScale = 0`; UGUI buttons and sliders remain interactive while paused.
- Runtime localization supports English and Simplified Chinese.

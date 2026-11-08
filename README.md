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

# ABOVE THE VOID - Unity 3D Coordinate System Framework

Current version: **v0.2.1**

### v0.2.1

**Syntax tutorial layout**

- The tutorial split is now a fixed 50/50 split: the code editor always takes the top half of the workspace and the guide panel the bottom half, whatever the window size and whatever the task text length, and the guide body font shrinks step by step (`18` down to `13`) when the text does not fit instead of growing the panel.
- A window resize recomputes the split, so the two halves stay exactly equal and the editor's bottom reserved height always matches the guide panel height.

**Tutorial task navigation**

- The progress chips in the code syntax tutorial are clickable: clicking a number switches to that task inside the same course, and hovering brightens the chip to show it responds. A jump only moves the current task pointer, so completed marks and level progress are left alone and no jump crosses into another course.
- A jump clears the stale compile snapshot, re-evaluates both the syntax and the compile checks against the current editor content, and syncs the resume checkpoint.
- The C# grammar course (Level 1 - Part 2) now opens with the comment task, so `// single line` and `/* multi line */` are introduced before any statement, and the API course (Level 2 - Part 1) runs `Print` then `PrintLine` then `ClearPrint` before the object-inspection and firing tasks.

**Code editor**

- The error and status strip at the bottom of the editor now uses the editor background colour while the split layout is active (it keeps the darker shade in the fullscreen editor), and the editor rect extends one pixel toward the guide panel, so no dark seam shows between the two halves.
- Compile errors no longer print the `CS` prefix twice (`CSCS0103`): the error number is normalised before it is shown.

**Main menu**

- Removed the hint line at the bottom of the main menu; the key reference is no longer shown there.

### v0.2.0

**Version numbering**

- The published tags were renumbered so that each release carries a patch number one lower than before: `v0.1.5` became `v0.1.4`, `v0.1.4` became `v0.1.3`, and so on down to `v0.0.2` becoming `v0.0.1`; the Foundation notes below are now `v0.0.0`.
- Every renamed tag still points at the original commit, and the old tag names were removed from the remote, so no commit content and no history were rewritten.
- The release notes in this file follow the new numbering from `v0.2.0` onwards. Commits created before this release keep their original `Release vX` messages in the Git history, so a checkout of an older tag still shows the previous numbering in its copy of this file.

**Engine migration**

- The project moved from Tuanjie `1.11.0` on `2022.3.62t17` to Unity `2022.3.62f3c1` (changeset `1623fc0bbb97`); `ProjectSettings/ProjectVersion.txt` now pins the Unity editor and the Tuanjie version key is gone.
- Serialized YAML headers were rewritten from `tag:yousandi.cn,2023` to `tag:unity3d.com,2011`, and the asset `.meta` GUIDs were regenerated as unique 32-character hex values, so Unity imports the assets as native ones instead of re-importing them as foreign files.
- Tuanjie-only project settings (`AutoStreamingSettings`, `TextureManagerSettings`, `ThreadInfoSettings`) were removed, together with the OpenHarmony platform fields, the editor encryption keys, and the WeChat mini-game slimming block.
- `Packages/manifest.json` and `Packages/packages-lock.json` no longer reference `com.unity.modules.infinity`.
- The code tower compiler toolchain is resolved at runtime from `EditorApplication.applicationContentsPath` instead of a hard-coded Tuanjie install path.

**Code editor**

- The hand-written scrollbars were rebuilt: both handles stay hidden and the content height is refreshed whenever the viewport resizes, so a paused editor (time scale 0) no longer shows a slider over empty space.

**Developer console**

- The backquote key now only opens the console: while the console is open the character is typed into the input field instead of closing it, and ESC remains the only way to close it.

### v0.1.4

**Lives and defeat**

- Added `LivesManager`: a run starts with 5 lives, and destroying a blue (BOMB) block costs one life, while red (ATTACK) blocks never cost lives.
- Added the lives HUD directly under the score HUD, with empty and danger color states, plus a defeat panel at 0 lives that offers restart and return to menu.
- Lives are re-opened on every level jump and the defeat panel is cleared; the main menu turns the whole system off, and the tutorial keeps it off.

**Block health by type**

- Block HP is now assigned by type in a single place: blue (BOMB) blocks always carry 1 HP and die in one hit without splitting into remnants, while red (ATTACK) blocks keep the random 1–6 layers.
- The crosshair block info now reports remaining HP over the block's max HP (`N/N` down to `1/N`), and the line sits above the bottom hint bar instead of overlapping it.
- The selected-block info line can be offset horizontally by the code syntax tutorial so it stays inside the left half of the split layout.

**Progression and unlock**

- Added `LevelProgress`: levels 1–3 unlock as the previous one is completed, and unlock and completion state persist across sessions.
- Added the `/unlock` developer command (alias 解锁) to unlock every level at once; the main menu greys out locked levels and refreshes as soon as an unlock lands.

**Resume and high score**

- Added `GameProgress` for save data: the resume checkpoint (level, part, section), the session position, and per-level high scores are stored in `PlayerPrefs` under the `ABOVETHEVOID.*` keys.
- A level position writes the checkpoint once it has been applied, and a `/level` jump always overwrites the old checkpoint, so a jump is never pulled back by an older save.
- Finishing a Level 3 run settles the score into the per-level high score before the score is reset.

**Settings persistence**

- The settings panel persists mouse sensitivity, field of view, and the frame rate cap to `PlayerPrefs`, one key per option, and restores them on the next launch.
- Added a field of view slider (50–110 degrees, default 90) with an `OnFieldOfViewChanged` event that the camera applies live.
- Added the V zoom key on the camera: hold to narrow smoothly toward 32 degrees over 0.3s, with sensitivity optionally scaled by zoom and a warning shown when the FOV gets too wide.

**Input routing and commands**

- Added `EscapeRouter` as the single ESC entry point, resolving in priority order: code editor open, then console open, then defeat panel, then main menu, then pause inside a level; no component reads ESC on its own any more.
- Added `/lock` (alias 锁定) as a factory reset of level progress, settings, and editor code, gated by a 15 second confirmation window in which `/lock` must be entered again.
- The code editor gained `ResetCodeToDefault` so `/lock` restores the initial sample template and drops the undo and redo stacks.
- While the main menu is visible, Enter is ignored everywhere: it neither opens the editor nor submits a focused menu button.

**Presentation**

- Added `LevelPresentation.ApplyMainMenu` and `RestoreAxes`: the main menu hides the score HUD, the print screen, the time hint, and the whole coordinate axes, and stops blue blocks from spawning.
- Entering Level 3 now forces a normal state: the code editor is closed and the editor prompt cleared, a leftover E-key time freeze is released, and `timeScale` is set back to 1.
- The code syntax tutorial moved to a top and bottom split: the editor takes the upper block and the guide panel spans the bottom of the screen, each sized to align with the other.
- The developer overlay runtime now accumulates unscaled time, so it keeps running while the game is paused.

### v0.1.3

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

### v0.1.2

**Scoring**

- Added `ScoreManager` for per-run scoring: destroying a block awards a base of 10 plus 20 times its initial volume plus 3 per initial HP layer.
- A red (ATTACK) block that reaches the origin plane costs 5 plus 15 times its remaining volume, so letting a worn-down remnant through costs less than losing a whole block.
- Blue (BOMB) blocks can still be cut and destroyed, but they are score-neutral: no reward and no penalty entry point exists for them.
- The score may go negative, since leak penalties always apply.
- Added the top-left score HUD showing the current score plus the last delta, which fades after 1.2s; negative scores are styled in red.
- The HUD is kept above the main menu, tutorial, and code editor, and the score resets automatically when a level starts or restarts.

**Blocks**

- Blocks now carry 1–7 randomly rolled HP layers; each laser hit removes exactly one layer whose thickness is the initial size divided by the layer count.
- A block that is cut through splits into remnant blocks that inherit the source block's HP and initial size, so an already-damaged block is never reset by the split.
- Spawning was re-tuned for score play: 0.12s interval, up to 400 active blocks, attack ratio 80 and bomb ratio 60.
- Spawner exposes a `PlayerKills` counter alongside the active block list.

**Code tower**

- Fire cooldown lowered to 0.0285s (about 35 shots per second) and the laser radius to 0.02 with a 0.15s visual, keeping high-rate fire readable.
- Added a world-space `print` screen standing on the blue X axis (world +Z) above the origin: `Print`, `PrintLine`, and `ClearPrint` write to it instead of the Unity console, auto-sizing between 5.2–9.5 m wide and 1.2–5.5 m high while retaining up to 24 lines.
- Added `GetAllObjects()` returning `TowerObjectInfo` records (index, type, HP, position, velocity, params, volume, initial volume, max layers, size) so user code can choose targets by value.
- Exposed `FireAtPlane(A, B, C, D)` and `GetFireCooldown()` for scripted aiming and rate control.

### v0.1.1

**Code editor**

- Added `CodeEditorController`: press Enter to open or close, ESC to close; it stays closed during the tutorial and while the developer console is open, and restores a visible settings panel before opening.
- Opening the editor freezes time, releases the cursor, and blocks camera input; closing restores the previous pause or freeze state.
- The layout is built purely in code: title bar, left line-number column, a code area with a transparent input field layered under a read-only rich-text view, and a bottom error/status strip.
- C# lexical highlighting colors keywords cyan, strings green, numbers pink, comments gray-green, and neutral text white, with `<` replaced by a full-width ＜ so rich-text tags are not swallowed.
- Added vertical and horizontal scrollbars with proportional thumbs, mouse-wheel paging, arrow-key character scrolling at line edges, and Ctrl + wheel font zoom anchored on the hovered line.
- Added undo/redo on a 100-step snapshot stack.

**Compile and run**

- User code is compiled at runtime through `CSharpCodeProvider` inside a generated template wrapper, with compile error line numbers mapped back to the user's own code.
- On a successful compile the editor closes and the user's `Run(tower)` is called every frame; runtime exceptions stop execution and are reported in the status strip.
- The default sample code is inserted on first open and demonstrates the tower interface syntax.

**Tower and UI**

- Added `CodeTower` at the origin with a cylinder base, a rotating barrel, and a cyan muzzle tip, colored `#3A3A3A` and built entirely from code.
- Tower laser defaults: radius 0.15, range 60, 0.9s visual, 0.25s cooldown, firing along the aim direction and triggering the existing laser-fired event.
- Public API for user code: `Position`, `AimDirection`, `CanFire`, `Cooldown01`, `CooldownLeft`, `NearestBlock`, `ActiveBlockCount`, `RandomRange`, `Log`, `DrawAimLine`, `SetVisualVisible`, `Aim`, and `FireLaser`.
- Added `UIFitScreen` to keep the new editor and tower UI fitted to the screen; hit indicator, crosshair, overlay, spawner, and level UI were updated to coexist with the editor.

### v0.1.0

**Menus and language**

- Added `MainMenuController` with project information, level entry points, and Chinese/English selection.
- Added `Localization` with a `GameLanguage` switch (Chinese / English), a lookup helper for UI strings, and font switching: Microsoft YaHei for Chinese and Consolas for English and digits.
- Bundled `msyh.ttc` so Chinese UI text renders at runtime, and routed dynamic text through `UIFontHelper` to avoid missing glyphs.

**Tutorial (Level 1 - Part 1)**

- Added `TutorialController`, a six-step guided flow: welcome overview, press Q, move, fire laser, plane kill, and a C# automation teaser.
- Movement teaching requires holding each of the six keys (WASD, Space, C) for about one second, with a green charge fill that must be recharged if released early and a brighter completed key state.
- Block spawning is suspended during the early steps and restarted at the plane-kill step, so players first watch blocks vanish on the plane and then practise destroying them.
- The tutorial can be restarted from the menu or a level jump, and leaving it hides the panel, clears the field, and suspends spawning.

**Levels**

- Added `LevelDirector` plus the developer command `/level <level> <part>`: a jump hides the menu, resets the player to the level start, clears every block, and restarts spawning.
- Only Level 1 - Part 1 is implemented; other level parts return a "not yet available" message.
- The top time hint is hidden in Level 1 - Part 1 and kept for later level parts.
- Added `AutoLaserExample` as a sample automatic firing script and improved block hit feedback for it.

### v0.0.2

**Blocks**

- Added `FlyingBlock` and `FlyingBlockSpawner`: axis-aligned blocks spawn on the faces of the ±10 region and travel toward the origin.
- Spawner defaults: 1.2s interval, 5s start delay, up to 40 active blocks, speed 0.5–1.2, and a random side sum of about 2–3 with a 0.4 minimum per side.
- Added three block types with fixed colors and on-block labels: red ATTACK, green HEAL, and blue BOMB, rolled at 50 / 20 / 30 percent.
- Blocks carry a spawn id, move direction, and geometry helpers (`GeometryCenter`, `HalfExtents`) used by hit detection.

**Aiming and feedback**

- Added `CenterMarker.shader` and a per-block center marker that inverts against the surface behind it, switching between black and white variants depending on whether the white face is being viewed.
- Added `BlockHitIndicator` so the crosshair highlights while it is aimed at a block.
- Added the laser destruction hooks `ApplyLaserHit` and `DestroyBlock` with configurable damage and a destruction reason, so the console and future towers can destroy blocks.
- Updated the coordinate axes drawer and camera controller so the new blocks and feedback render correctly in both flight modes.

### v0.0.1

**Developer tools**

- Added `CommandConsoleController`: press Enter to open or close, ESC to close; while open it freezes time, releases the cursor, and blocks camera input through the camera controller's external-block flag.
- Command handling was split into `CommandType`, `CommandData`, and `CommandParser` with case-insensitive bilingual aliases: `help`/`帮助`, `laser`/`发射激光`, `tp`/`teleport`/`传送`, `timescale`/`时间流速`.
- `laser`, `teleport`, and `timescale` are parsed and dispatched to fixed placeholder handlers, ready to be filled in later.
- Added `DevOverlayController` (F3): an upper-left panel showing world position on x / y / z, the session start time to the millisecond, and the elapsed run time.
- Added the E-key time stop on the camera controller, with elapsed time frozen while time is stopped.

**HUD**

- Added the Minecraft-style inverted crosshair (`CrosshairController`): four segments plus a center dot drawn with an inverting material (`InvertCrosshair.shader`, Blend OneMinusDstColor Zero) so it stays readable on any background.
- The crosshair falls back to a white core with a black outline when the inverting material is missing.
- Added the top-center time hint (`TimeHintController`): `mm:ss` under one hour and `HH:mm:ss` after, using Consolas from `Resources/Fonts/consolas.ttf` with an Arial fallback.
- Added `UIFontHelper` for runtime font loading, and wired the settings panel and camera controller to cooperate with the new frozen and modal states.

The core experience includes a 3D coordinate system, an origin camera, mouse look, settings, pause support, and two free-flight modes.

### v0.0.0

**Foundation**

- Initial scaffold of the Tuanjie project: `ProjectSettings/ProjectVersion.txt` pins Tuanjie `1.11.0` on the `2022.3.62t17` editor line, and `Packages/manifest.json` adds only `com.unity.ugui` on top of the built-in modules.
- Everything is assembled from code with no prefabs: the `GameSceneSetup` editor tool creates and opens `Assets/Scenes/Main.unity` on editor load (or through the `ABOVE THE VOID -> 一键搭建游戏场景` menu), switches Active Input Handling to Both, and drops a single `GameBootstrap` object into the scene.
- At runtime `GameBootstrap` reuses or creates the Main Camera at the origin, then adds the axes drawer, the camera controller, and the settings UI, each guarded by an existing-instance check.
- The starting scene is a bare dark stage: Main Camera at (0, 0, 0) with a 0.01 near clip plane and a solid (0.07, 0.07, 0.11) background, plus a directional light.

**Coordinate axes**

- Added `CoordinateAxesDrawer`, which draws the ±X / ±Y / ±Z lines, arrow heads, and 1-unit ticks with `LineRenderer` over the range [-20, 20] (`axisLength` 20, `tickStep` 1, tick length 0.08, axis width 0.06, arrow size 0.4).
- Axis convention of this version: Z is the green vertical axis, Y the red left axis, and X the blue right axis; the negative halves use inverted colors, and only the ±1 ticks keep the axis color while the remaining ticks stay white.
- The X / Y / Z letters are `TextMesh` labels kept at a constant screen size by `BillboardToCamera`, with a horizontal flip option used by the mirrored Z label.
- Axis materials prefer `Sprites/Default` so the `LineRenderer` vertex colors stay saturated, falling back to `Unlit/Color` when that shader is missing.

**Camera and input**

- Added `PlayerCameraController` with a Normal / Spirit / Paused state machine; Normal pins the camera to the origin and only rotates it.
- Mouse look uses a locked cursor with yaw 45 and pitch -35.264 at startup so all three positive axes are in view; pitch is clamped to ±85 degrees and sensitivity defaults to 1.75 (clamped to 0.1 - 50).
- Q enters strict axis movement (W / S on X, A / D on Y, Space / C on Z) and F1 enters camera-relative free flight; pressing the same key again returns the camera to the origin, while pressing the other one swaps the mode in place.
- Flight speed is 3 m/s with a 2x LeftShift sprint, and every position component is clamped to [-20, 20].
- ESC pauses in any mode: `Time.timeScale` is set to 0, the cursor is released, and the mode from before the pause is restored on resume.

**Settings UI**

- Added `SettingsPanelController`, which builds its own Screen Space Overlay canvas, `EventSystem`, top-right settings button, and centered pause card entirely in code.
- The pause card carries a sensitivity slider (0.5 - 10, default 1.75) that raises `OnSensitivityChanged`, while the settings and resume buttons raise `OnPauseRequested`, keeping the UI decoupled from the camera controller.

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
| Lives | A run starts with 5 lives; destroying a Bomb block costs one, and 0 lives shows the defeat panel. |
| Level progression | Levels unlock one after another on completion and stay unlocked across sessions. |
| Save data | Resume checkpoint, per-level high scores, sensitivity, field of view, and frame rate cap persist across launches. |
| Developer console | Inspect state and navigate to supported level parts. |
| Code editor | Press Enter to edit tower code with syntax highlighting, diagnostics, undo/redo, and runtime execution. |
| Pause | Press ESC in any gameplay mode to pause and resume. |
| Q / F1 modes | Strict axis movement or camera-relative free flight. |

## 2. Requirements

- Unity **2022.3.62f3c1** (revision `1623fc0bbb97`).
- Built-in Render Pipeline or URP as supported by the installed Unity editor.
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
│       ├── LivesManager.cs                # Lives HUD and defeat panel
│       ├── LevelProgress.cs               # Level unlock progress
│       ├── GameProgress.cs                # Save data: resume point and high scores
│       ├── EscapeRouter.cs                # Single ESC input router
│       ├── CodeEditorController.cs        # In-game code editor and runner
│       ├── CodeSyntaxTutorialController.cs  # Level 1 grammar and Level 2 API course panel
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

1. Open this folder with Unity Hub or the Unity editor.
2. The editor automatically creates and opens `Assets/Scenes/Main.unity` when the scene does not exist.
3. Press Play to open the main menu.
4. Choose a language and start Level 1. The tutorial controls the initial spawning sequence.

If the scene is not generated automatically, use `ABOVE THE VOID -> Setup Game Scene` in the Unity editor. The tool enables the required input handling, creates the scene, attaches `GameBootstrap`, and saves `Assets/Scenes/Main.unity`.

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
| `~` | Open the developer console (ESC closes it). |
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

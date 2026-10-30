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

# ABOVE THE VOID — Unity 3D 直角坐标系游戏基础框架

初始版本：3D 直角坐标系可视化 + 原点视角 + 鼠标视角旋转 + 设置按钮 + ESC 暂停 + Q/F1 双灵魂出窍模式。

---

## 1. 功能一览

| 功能 | 说明 |
|---|---|
| 3D 直角坐标系 | 坐标范围为 [-20, 20]；Z=绿色竖直轴，Y=红色左侧轴，X=蓝色右侧轴；仅 ±1 刻度保留轴色，其余刻度为白色，轴末端显示同色字母且大小恒定 |
| 玩家视角位于原点 | Main Camera Normal 模式严格位于 (0, 0, 0) |
| 鼠标视角旋转 | 鼠标锁定 + 灵敏度可调（暂停面板内实时调整） |
| 设置按钮 | Canvas 右上角「设置」按钮，点击弹出暂停设置面板 |
| ESC 暂停 | 任意模式下按 ESC 暂停（Time.timeScale = 0），显示暂停菜单；再按恢复 |
| Q 键 | 严格坐标轴模式：W/S 控制 X、A/D 控制 Y、Space/C 控制 Z；再按 Q 返回原点 |
| F1 键 | 摄像机相对自由飞行模式；再按 F1 返回原点 |

## 2. 技术栈与版本要求

- Unity **2021.3 LTS 或更高版本**（推荐 2022.3 LTS / 2023.2+）
- 渲染管线：Built-in 或 URP 均可（脚本使用 `Sprites/Default` 与内置字体，兼容性良好）
- 输入系统：脚本使用**旧 Input Manager** API（`Input.GetAxis` / `Input.GetKey`），请在
  `Project Settings → Player → Active Input Handling` 中选择 **Both** 或 **Input Manager**
  （如使用新版 Input System 请保留 Both，否则按键与鼠标输入无效）

## 3. 项目结构

```
ABOVE THE VOID/
├── Assets/
│   ├── Editor/
│   │   └── GameSceneSetup.cs           # 编辑器工具：自动/手动一键生成 Main.unity 场景
│   └── Scripts/
│       ├── GameBootstrap.cs            # 一键引导：自动搭建相机 + 坐标轴 + 主控 + UI
│       ├── CoordinateAxesDrawer.cs     # 3D 直角坐标系可视化（轴 / 刻度 / 箭头 / 原点标记）
│       ├── PlayerCameraController.cs   # 玩家视角主控（Normal / Spirit / Paused 状态机）
│       └── SettingsPanelController.cs  # 设置 / 暂停 UI（右上角按钮 + 灵敏度滑块 + 面板）
├── Packages/
│   └── manifest.json                   # 包依赖（含 com.unity.ugui，UGUI 必需）
└── README.md
```

## 4. 快速开始（推荐，零手工搭建）

1. 用 Unity Hub 打开本目录；
2. 编辑器启动时会**自动生成并打开** `Assets/Scenes/Main.unity`（若尚未存在）——
   场景已包含 Main Camera（原点）、平行光、挂载好 `GameBootstrap` 的空物体；
3. 直接点击 Play 即可。

若未自动生成（或想重新生成），点击菜单 **`ABOVE THE VOID → 一键搭建游戏场景`**，
工具会自动：将 Active Input Handling 设为 Both、创建场景、挂载 Bootstrap、保存到 `Assets/Scenes/Main.unity`。

> 手动方式：新建空场景 → 创建空物体挂 `GameBootstrap.cs` → Play。
> Bootstrap 会自动完成：创建/复用 Main Camera 并置于原点、创建坐标轴、创建视角主控、创建设置 UI。

## 5. 手动挂载方式（正式项目推荐）

| 步骤 | 操作 |
|---|---|
| 1 | 创建/确认 Main Camera，Tag 设为 `MainCamera`，Position = (0, 0, 0) |
| 2 | 创建空物体 `CoordinateAxes`，挂 `CoordinateAxesDrawer` |
| 3 | 创建空物体 `PlayerCameraController`，挂 `PlayerCameraController`，将相机拖入 `Player Camera` 字段（留空则自动查找 Camera.main） |
| 4 | 创建空物体 `UI_SettingsPanel`，挂 `SettingsPanelController`（自动生成 Canvas / EventSystem / UI） |
| 5 | 点击 Play |

> 提示：`PlayerCameraController.Start()` 会自动查找场景中的 `SettingsPanelController` 并订阅灵敏度事件；
> 请确保 UI 物体与主控物体同时存在。

## 6. 输入键位

| 按键 | 功能 |
|---|---|
| 鼠标移动 | 视角旋转（灵敏度可在暂停面板调节） |
| W / S | 灵魂出窍时沿 X 轴正 / 负方向移动 |
| A / D | 灵魂出窍时沿 Y 轴正 / 负方向移动 |
| Space / C | 灵魂出窍时沿 Z 轴正 / 负方向移动 |
| LeftShift | 灵魂出窍时加速（2 倍） |
| Q | 进入 / 退出严格坐标轴模式 |
| F1 | 进入 / 退出摄像机相对自由模式 |
| W / S | 沿 X 轴正 / 负方向移动 |
| A / D | 沿 Y 轴负 / 正方向移动 |
| Space / C | 沿 Z 轴正 / 负方向移动 |
| ESC | 暂停 / 恢复（任意模式下生效） |
| 设置按钮（右上角） | 打开暂停设置面板（等效暂停） |

## 7. 模式状态机

```
             Q                          Q
  Normal（原点视角旋转） <-------> Spirit（自由飞行）
      ↑  |                            ↑  |
      |  ESC（暂停/恢复）             |  ESC（暂停/恢复）
      |  ↓                            |  ↓
         Paused（Time.timeScale = 0，鼠标解锁，暂停面板可见）
```

- **Normal**：相机固定在原点，仅旋转；初始朝向同时观察三个正方向。
- **Spirit**：相机位置解锁，移动方向相对相机朝向的水平投影（俯仰不影响平移方向）。
- **Paused**：记录暂停前的模式，恢复时回到原模式；灵敏度滑块在暂停期间仍可实时调整。

## 8. 鼠标方案选择说明

采用 **鼠标锁定 + 灵敏度可调** 而非「围绕原点轨道相机」，理由：

1. 满足「Main Camera 初始位置在原点 (0, 0, 0)」的硬性需求（轨道相机相机实际位于球面上，不在原点）；
2. 灵魂出窍自由飞行与正常视角共用同一套鼠标旋转逻辑，状态机切换干净、无重复实现；
3. FPS 式手感和后续扩展（冲刺、俯仰限制、视角抖动、触屏拖拽等）更自然。

如后续需要轨道相机方案（相机在球面上围绕原点观察），可基于 `yaw / pitch` 扩展：`pos = origin + Quaternion.Euler(pitch, yaw, 0) * radius * Vector3.back`。

## 9. 常用参数调整

打开各脚本 Inspector 可调整：

- `CoordinateAxesDrawer`：轴长 `axisLength`、刻度间距 `tickStep`、线宽、箭头大小、轴颜色。
- 坐标约定：本项目使用 X=右侧、Y=左侧、Z=竖直；仅 ±1 单位刻度使用对应轴颜色，其余单位刻度为白色且更粗。
- 灵魂出窍移动范围限制为 X/Y/Z ∈ [-20, 20]，到达边界后不会继续移动。
- X/Y/Z 轴末端使用直接生成的彩色 TextMesh 漂浮提示，不依赖 SVG 导入；标签大小会按相机距离补偿。
- `PlayerCameraController`：鼠标灵敏度 `mouseSensitivity`、俯仰限制 `minPitch / maxPitch`、
  飞行速度 `moveSpeed`、加速倍率 `sprintMultiplier`、按键绑定。
- `SettingsPanelController`：灵敏度范围 `minSensitivity / maxSensitivity`、UI 颜色。

## 10. 后续扩展建议

- 地面网格 / 参考平面：为坐标轴加半透明平面或网格，帮助定位；
- 实体与物理：在原点放置玩家胶囊体，灵魂出窍改为相机分离（第三人称跟随）而非直接移动相机；
- 输入升级：迁移到新 Input System（Input Actions），支持手柄 / 触屏；
- 状态管理：将状态机抽取为独立 `GameStateManager`，便于接入菜单、对话、战斗等更多状态；
- 存档系统：保存灵敏度、视角、世界坐标等设置；
- 场景漫游：在坐标轴上放置可交互标记（如各刻度点），点击传送视角。

---

## 11. 已知注意事项

- 脚本在运行时动态创建 UI 与坐标轴，编辑器未保存这些运行时物体；如需持久化场景，请按「手动挂载方式」搭建后手动保存场景。
- 若项目使用 URP 且坐标轴材质显示异常，可手动将 LineRenderer 材质替换为 `URP/Unlit/Color`。
- 暂停使用 `Time.timeScale = 0`，UGUI 的按钮与滑块事件不受 timeScale 影响，仍可正常交互。
*（内容由AI生成，仅供参考）*

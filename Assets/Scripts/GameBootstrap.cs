using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 一键引导脚本：在场景空物体上挂载本脚本，Play 时自动搭建整套基础框架。
///   - 复用或创建 Main Camera 并强制放置原点 (0,0,0)；
///   - 创建坐标轴可视化（CoordinateAxesDrawer）；
///   - 创建玩家视角主控（PlayerCameraController）并绑定相机；
///   - 创建设置 / 暂停 UI（SettingsPanelController）。
/// 推荐用于快速验证；正式项目可改为手动挂载（见 README）。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Header("自动搭建开关")]
    public bool createCameraIfMissing = true;
    public bool createAxes = true;
    public bool createController = true;
    public bool createUI = true;
    public bool createCrosshair = true;
    public bool createDevOverlay = true;
    public bool createTimeHint = true;
    public bool createCommandConsole = true;
    public bool createFlyingBlocks = true;
    public bool createMainMenu = true;
    public bool createTutorial = true;
    public bool createCodeTower = true;
    public bool createCodeEditor = true;
    public bool createScore = true;
    public bool createLives = true;
    public bool createEscapeRouter = true;

    private void Awake()
    {
        // 1. 相机
        Camera cam = Camera.main;
        if (cam == null && createCameraIfMissing)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }

        if (cam != null)
        {
            cam.transform.position = Vector3.zero; // 玩家视角位于原点
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.11f);
        }
        else
        {
            Debug.LogWarning("[GameBootstrap] 未找到可用的相机，请先在场景中放置 Main Camera。");
        }

        // 2. 坐标轴可视化
        if (createAxes && FindObjectOfType<CoordinateAxesDrawer>() == null)
        {
            GameObject axes = new GameObject("CoordinateAxes");
            axes.AddComponent<CoordinateAxesDrawer>();
        }

        // 3. 玩家视角主控
        if (createController && FindObjectOfType<PlayerCameraController>() == null)
        {
            GameObject ctrl = new GameObject("PlayerCameraController");
            PlayerCameraController controller = ctrl.AddComponent<PlayerCameraController>();
            controller.BindCamera(cam);
        }

        // 4. 设置 / 暂停 UI
        if (createUI && FindObjectOfType<SettingsPanelController>() == null)
        {
            GameObject ui = new GameObject("UI_SettingsPanel");
            ui.AddComponent<SettingsPanelController>();
        }

        // 5. 反色十字准心
        if (createCrosshair && FindObjectOfType<CrosshairController>() == null)
        {
            GameObject crosshair = new GameObject("UI_Crosshair");
            crosshair.AddComponent<CrosshairController>();
        }

        // 6. F3 开发者模式（坐标 + 精确开始时间）
        if (createDevOverlay && FindObjectOfType<DevOverlayController>() == null)
        {
            GameObject dev = new GameObject("UI_DevOverlay");
            dev.AddComponent<DevOverlayController>();
        }

        // 7. 顶部常驻时间提示（Consolas）
        if (createTimeHint && FindObjectOfType<TimeHintController>() == null)
        {
            GameObject time = new GameObject("UI_TimeHint");
            time.AddComponent<TimeHintController>();
        }

        // 8. ` 开发者指令框（/level 关卡跳转指令 + help）
        if (createCommandConsole && FindObjectOfType<CommandConsoleController>() == null)
        {
            GameObject console = new GameObject("UI_CommandConsole");
            console.AddComponent<CommandConsoleController>();
        }

        // 9. 飞行长方体系统（生成器 + 准心检测指示器）
        if (createFlyingBlocks && FindObjectOfType<FlyingBlockSpawner>() == null)
        {
            GameObject blocks = new GameObject("FlyingBlockSystem");
            blocks.AddComponent<FlyingBlockSpawner>();
            blocks.AddComponent<BlockHitIndicator>();
        }

        // 10. 主页（主菜单）：展示主题、项目目的与作者，关卡1进入游戏
        if (createMainMenu && FindObjectOfType<MainMenuController>() == null)
        {
            GameObject menu = new GameObject("UI_MainMenu");
            menu.AddComponent<MainMenuController>();
        }

        // 11. 关卡1-1 引导教学（进入关卡后接管：先不生成方块，逐步引导操作与机制）
        if (createTutorial && FindObjectOfType<TutorialController>() == null)
        {
            GameObject tutorial = new GameObject("UI_Tutorial");
            tutorial.AddComponent<TutorialController>();
        }

        // 12. 原点炮台（代码编辑器控制的炮台，位置恒定在原点）
        if (createCodeTower && FindObjectOfType<CodeTower>() == null)
        {
            GameObject tower = new GameObject("CodeTower");
            CodeTower towerComp = tower.AddComponent<CodeTower>();
            towerComp.SetVisualVisible(false); // Level 1 教学关不显示炮台实体，仅保留原点炮台功能
        }

        // 13. 炮台代码编辑器（Enter 开关，C# 运行时编译驱动炮台）
        if (createCodeEditor && FindObjectOfType<CodeEditorController>() == null)
        {
            GameObject editor = new GameObject("UI_CodeEditor");
            editor.AddComponent<CodeEditorController>();
        }

        // 14. 计分系统（击毁红块加分 / 红色方块抵达基地扣分 / 蓝色方块可击毁但不计分，左上角 HUD）
        if (createScore && FindObjectOfType<ScoreManager>() == null)
        {
            GameObject score = new GameObject("UI_Score");
            score.AddComponent<ScoreManager>();
        }

        // 15. 生命值系统（击毁蓝色 BOMB 块扣生命，初始 5 点，HUD 在 SCORE 下方，归 0 判负）
        if (createLives && FindObjectOfType<LivesManager>() == null)
        {
            GameObject lives = new GameObject("UI_Lives");
            lives.AddComponent<LivesManager>();
        }

        // 16. ESC 全局优先级分发器（全项目唯一 ESC 判定点）：
        //     编辑器打开 → 关编辑器；指令框打开 → 关指令框；Game Over → 回主页；
        //     主页可见 → 前台打开指令框（不暂停）；关卡内 → 开 / 关暂停面板
        if (createEscapeRouter && FindObjectOfType<EscapeRouter>() == null)
        {
            GameObject router = new GameObject("EscapeRouter");
            router.AddComponent<EscapeRouter>();
        }
    }
}

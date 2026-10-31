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

        // 8. Enter 指令框（代码式指令解析，预留 Laser 分支）
        if (createCommandConsole && FindObjectOfType<CommandConsoleController>() == null)
        {
            GameObject console = new GameObject("UI_CommandConsole");
            console.AddComponent<CommandConsoleController>();
        }
    }
}

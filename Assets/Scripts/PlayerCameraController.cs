using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家视角主控（完整模式状态机）：
///   Normal —— 视角锁定原点旋转（鼠标控制）；
///   Spirit —— 灵魂出窍：Q 进入严格坐标轴移动，F1 进入摄像机相对自由飞行；
///             再按对应按键返回原点并退出；
///   Frozen —— 时间冻结：E 键仅暂停游戏时间（timeScale=0），移动 / 飞行状态不受 E 影响：
///             先前或之后按下 Q / F1 的飞行状态保持（不自动开启也不自动关闭，由玩家输入驱动），
///             无输入则静止；不弹出设置界面，再按 E 恢复；
///   Paused —— 暂停：ESC 触发，在任意模式下均生效，
///             暂停面板含灵敏度滑块、退出暂停与返回主页按钮。
///
/// 采用「鼠标锁定 + 灵敏度可调」方案（而非围绕原点轨道相机）：
///   1) 满足「Main Camera 初始位置在原点 (0,0,0)」的硬性需求；
///   2) 灵魂出窍模式复用同一套鼠标视角逻辑，状态机切换干净无冗余；
///   3) 实现简单、手感更接近 FPS，后续扩展（冲刺、俯仰限制、视角抖动等）更方便。
/// </summary>
public class PlayerCameraController : MonoBehaviour
{
    public enum GameMode
    {
        Normal,  // 视角锁定原点旋转
        Spirit,  // 灵魂出窍自由飞行
        Frozen,  // 时间冻结（E 键）：冻结游戏时间，玩家仍可自由移动
        Paused   // 暂停（任意模式下生效）
    }

    [Header("相机与原点")]
    [Tooltip("为空时自动使用 Camera.main")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("原点位置（默认 (0,0,0)）")]
    [SerializeField] private Vector3 origin = Vector3.zero;
    [Tooltip("每个坐标分量的最小值")]
    [SerializeField] private float minCoordinate = -15f;
    [Tooltip("每个坐标分量的最大值")]
    [SerializeField] private float maxCoordinate = 15f;

    [Header("鼠标视角")]
    [Tooltip("鼠标灵敏度（可在暂停面板中实时调整）")]
    [SerializeField] private float mouseSensitivity = 1.75f;
    [Tooltip("俯仰角下限（度）")]
    [SerializeField] private float minPitch = -85f;
    [Tooltip("俯仰角上限（度）")]
    [SerializeField] private float maxPitch = 85f;

    [Header("灵魂出窍移动")]
    [Tooltip("移动速度（米/秒）")]
    [SerializeField] private float moveSpeed = 3f;
    [Tooltip("按住 LeftShift 的加速倍率")]
    [SerializeField] private float sprintMultiplier = 2f;

    [Header("按键")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;
    [Tooltip("用 ESC 关闭代码编辑器 / 指令框后，忽略暂停键的时长（秒，真实时间）：避免同一次 ESC 顺带把游戏暂停")]
    [SerializeField] private float pauseInputSuppressSeconds = 0.25f;
    [SerializeField] private KeyCode timeStopKey = KeyCode.E;
    [SerializeField] private KeyCode axisSpiritKey = KeyCode.Q;
    [SerializeField] private KeyCode freeSpiritKey = KeyCode.F1;
    [SerializeField] private KeyCode upKey = KeyCode.Space;
    [SerializeField] private KeyCode downKey = KeyCode.C;

    [Header("V 键放大（视野收窄 / Zoom In）")]
    [Tooltip("按住即放大（视野收窄、物体变大）的按键（松开平滑回落）")]
    [SerializeField] private KeyCode zoomKey = KeyCode.V;
    [Tooltip("放大（视野收窄）后的视场角（度）：比基准 FOV 小，按住按键时从基准 FOV 平滑插值到该值；基准 90° → 32°（约 2.8 倍放大）")]
    [SerializeField] private float zoomFieldOfView = 32f;
    [Tooltip("放大 / 回落的平滑过渡时长（秒，真实时间，建议 0.25~0.35）")]
    [SerializeField] private float zoomTransitionSeconds = 0.3f;
    [Tooltip("放大（视野收窄）期间按 tan 半角比值同步下调鼠标灵敏度，避免放大后转头过快")]
    [SerializeField] private bool scaleSensitivityWithZoom = true;
    [Tooltip("视场角超过该值时在屏幕上提示「画面可能扭曲」：放大（收窄）方向不会触发，仅当基准 FOV 被调大到阈值以上才提示")]
    [SerializeField] private float fovWarnThreshold = 115f;

    /// <summary>当前游戏模式。</summary>
    public GameMode Mode { get; private set; } = GameMode.Normal;

    /// <summary>当前是否处于 E 键时间冻结（供外部组件如指令框判断还原）。</summary>
    public bool IsTimeFrozen => timeFrozen;

    /// <summary>当前鼠标灵敏度（供 UI 读写）。</summary>
    public float MouseSensitivity
    {
        get => mouseSensitivity;
        set => mouseSensitivity = Mathf.Clamp(value, 0.1f, 50f);
    }

    // 视角朝向
    // 出生即面向蓝色 X 轴正方向：X 轴画在世界 +Z，故相机前向 = 世界 +Z（yaw=0），俯仰 0° 保持水平正视。
    private float yaw = 0f;    // 绕 Y 轴（水平）
    private float pitch = 0f;  // 绕 X 轴（垂直），0 表示水平直视 X 轴正方向

    // 暂停前模式，恢复时回到原模式
    private GameMode modeBeforePause = GameMode.Normal;
    private bool strictAxisMovement;
    // 独立的时间冻结标记：仅切换 Time.timeScale，与移动 / 飞行模式正交
    // （Mode==Frozen 仅表示「时间冻结且未飞行」；Spirit 冻结时 Mode 保持 Spirit）
    private bool timeFrozen;

    // 外部模态占用（如指令框打开）：占用期间视角 / 移动 / 暂停键均让渡给外部组件
    private bool externalBlock;

    // 纯阅读模态（如序章讲解页 / 方块图鉴页）：只禁视角 / 移动 / E / Q / F1，保留 ESC 暂停，
    // 配合外部组件释放鼠标，避免「鼠标在面板上移动、背后的镜头却跟着乱转」
    private bool viewBlocked;

    // 暂停键抑制截止时间（Time.unscaledTime 基准）：模态 UI（代码编辑器 / 指令框）用 ESC 关闭后，
    // 同一次 ESC 不应再触发暂停，故在此时间点之前忽略 pauseKey
    private float pauseInputSuppressedUntil;

    private SettingsPanelController settingsPanel;
    private System.Action<float> onSensitivityChangedHandler;
    private System.Action<float> onFieldOfViewChangedHandler;

    // ---- 视场角平滑放大（V 键）相关状态 ----
    private float baseFieldOfView = 90f;     // 设置面板给出的基准 FOV（放大起止基准）
    private float currentFieldOfView = 90f;  // 平滑后的实际 FOV（每帧写入相机）
    private float zoomProgress;              // 0 = 基准，1 = 完全放大；按真实时间匀速推进，再经 SmoothStep 缓动
    private bool zoomHeld;                   // 本帧是否按住放大键
    private Canvas fovWarnCanvas;            // 「视场角过大」提示（常驻子 Canvas，默认关闭）

    /// <summary>当前相机视场角（度，平滑后的实际值），供 UI 读写。</summary>
    public float FieldOfView
    {
        get => currentFieldOfView > 0.01f ? currentFieldOfView : (playerCamera != null ? playerCamera.fieldOfView : 90f);
        set
        {
            // 设置面板改动 = 改基准 FOV；未处于放大状态时立即生效，放大中则由平滑逻辑重新插值
            baseFieldOfView = Mathf.Clamp(value, 1f, 179f);
            if (!zoomHeld)
            {
                currentFieldOfView = baseFieldOfView;
                ApplyFieldOfView();
            }
        }
    }

    /// <summary>当前是否处于 V 键放大状态（进度过半视为放大中）。</summary>
    public bool IsZoomed => zoomProgress > 0.5f;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
        if (playerCamera == null)
            Debug.LogWarning("[PlayerCameraController] 未找到 Main Camera，请确保场景中存在带 MainCamera 标签的相机。");
        else
        {
            baseFieldOfView = Mathf.Clamp(playerCamera.fieldOfView, 1f, 179f);
            currentFieldOfView = baseFieldOfView;
        }

        BuildFovWarningUI();
    }

    private void Start()
    {
        // ESC 统一由 EscapeRouter 分发；若场景中没有（未走 GameBootstrap）则在此兜底补建，
        // 保证关卡内暂停键不会失效
        EscapeRouter.EnsureExists();

        // 关联设置面板：灵敏度实时同步、暂停请求、返回主页
        settingsPanel = FindObjectOfType<SettingsPanelController>();
        if (settingsPanel != null)
        {
            onSensitivityChangedHandler = value => MouseSensitivity = value;
            settingsPanel.OnSensitivityChanged += onSensitivityChangedHandler;
            onFieldOfViewChangedHandler = value => FieldOfView = value;
            settingsPanel.OnFieldOfViewChanged += onFieldOfViewChangedHandler;
            settingsPanel.OnPauseRequested += TogglePause;
            settingsPanel.OnReturnHomeRequested += ReturnToHome;
            MouseSensitivity = settingsPanel.Sensitivity; // 应用存档中的鼠标灵敏度（无存档即默认 1.75，setter 内 Clamp 兜底）
            settingsPanel.SetSensitivityWithoutNotify(MouseSensitivity);
            FieldOfView = settingsPanel.FieldOfView; // 应用存档中的视场角（默认 90°）
        }

        if (playerCamera != null)
        {
            playerCamera.transform.position = origin;
            playerCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Time.timeScale = 1f;
        SetCursorLocked(true);
    }

    private void OnDestroy()
    {
        if (settingsPanel != null)
        {
            if (onSensitivityChangedHandler != null)
                settingsPanel.OnSensitivityChanged -= onSensitivityChangedHandler;
            if (onFieldOfViewChangedHandler != null)
                settingsPanel.OnFieldOfViewChanged -= onFieldOfViewChangedHandler;
            settingsPanel.OnPauseRequested -= TogglePause;
            settingsPanel.OnReturnHomeRequested -= ReturnToHome;
        }
    }

    private void Update()
    {
        // V 键放大视场角：输入采样 + 逐帧平滑过渡（放在最前，保证暂停 / 编辑器打开时也把动画走完，不会卡在半途）
        UpdateZoomInput(!externalBlock && !viewBlocked && Mode != GameMode.Paused);
        UpdateFieldOfViewSmoothing();

        // 外部模态占用（指令框等）：视角 / 移动 / 暂停键全部让渡给外部组件
        if (externalBlock)
            return;

        // 纯阅读模态（序章讲解页 / 方块图鉴页等）：释放鼠标后禁止鼠标再转朝向，E / Q / F1 也一并失效；
        // 但保留 ESC 暂停（暂停键不在这里读，由 EscapeRouter 走 HandleEscapePauseRequest），玩家随时能退回主页。
        if (viewBlocked && Mode != GameMode.Paused)
            return;

        // 暂停键（ESC）不在这里读取：统一由 EscapeRouter 做唯一优先级判定后再回调 HandleEscapePauseRequest()，
        // 避免「主页 ESC 打开指令框」等场景被本组件当成暂停而一次按键触发两个行为。

        // 暂停态只处理暂停相关输入
        if (Mode == GameMode.Paused)
            return;

        // E 键：仅切换时间冻结（timeScale 0<->1），绝不改变移动 / 飞行模式
        // Level 1 - Part 1 教学进行中禁用 E 键（TutorialController.Instance.Started）
        if (Input.GetKeyDown(timeStopKey) && (TutorialController.Instance == null || !TutorialController.Instance.Started))
        {
            ToggleTimeStop();
            return;
        }

        // Q / F1：进入或退出灵魂出窍（冻结保持 / 退出恢复），任意非暂停模式下均可
        if (Input.GetKeyDown(axisSpiritKey))
        {
            ToggleSpirit(true);
            return; // 切换后本帧不再处理视角 / 移动
        }

        if (Input.GetKeyDown(freeSpiritKey))
        {
            ToggleSpirit(false);
            return; // 切换后本帧不再处理视角 / 移动
        }

        switch (Mode)
        {
            case GameMode.Normal:
                UpdateNormal();
                break;
            case GameMode.Frozen:
                // 冻结且未飞行：与 Normal 一致，锁定原点只转视角（绝不进入可移动飞行）
                UpdateNormal();
                break;
            case GameMode.Spirit:
                UpdateSpirit(); // 冻结中亦走此分支：移动使用 unscaledDeltaTime，timeScale=0 仍可飞行
                break;
        }
    }

    // ---------------- 状态机 ----------------

    private void TogglePause()
    {
        if (Mode == GameMode.Paused)
            Resume();
        else
            Pause();
    }

    private void Pause()
    {
        modeBeforePause = Mode;
        Mode = GameMode.Paused;
        Time.timeScale = 0f;               // 冻结游戏逻辑
        SetCursorLocked(false);            // 释放鼠标供 UI 操作
        if (settingsPanel != null)
            settingsPanel.ShowPanel();
        Debug.Log("[PlayerCameraController] 游戏暂停");
    }

    private void Resume()
    {
        Mode = modeBeforePause;            // 恢复暂停前的模式（Normal / Spirit / Frozen）
        Time.timeScale = timeFrozen ? 0f : 1f; // 若处于时间冻结则保持冻结（正交标记，不依赖 Mode）
        SetCursorLocked(!viewBlocked);     // 纯阅读模态（序章 / 图鉴）恢复后仍把鼠标留在面板上
        if (settingsPanel != null)
            settingsPanel.HidePanel();
        Debug.Log("[PlayerCameraController] 恢复游戏（当前模式：" + Mode + "）");
    }

    /// <summary>E 键时间冻结切换：仅冻结 / 解除冻结游戏时间，绝不改变移动 / 飞行模式。</summary>
    private void ToggleTimeStop()
    {
        if (timeFrozen)
        {
            // 解除冻结：仅恢复时间，飞行状态保持
            timeFrozen = false;
            Time.timeScale = 1f;
            if (Mode == GameMode.Frozen)
                Mode = GameMode.Normal;    // 冻结时未飞行：解除后回到 Normal 锁定原点
            // Spirit 冻结解除：Mode 保持 Spirit，继续飞行
            Debug.Log("[PlayerCameraController] 解除时间冻结，当前模式：" + Mode);
        }
        else
        {
            // 进入冻结：仅暂停时间，绝不改变飞行模式
            timeFrozen = true;
            Time.timeScale = 0f;           // 冻结游戏逻辑，但不释放鼠标、不弹设置面板
            if (Mode == GameMode.Spirit)
            {
                // 飞行中冻结：Mode 保持 Spirit，继续飞行（UpdateSpirit 使用 unscaledDeltaTime）
                Debug.Log("[PlayerCameraController] 时间冻结（飞行中）：保持飞行模式，仅暂停时间");
            }
            else
            {
                // 未飞行：进入 Frozen（锁定原点只转视角）
                Mode = GameMode.Frozen;
                Debug.Log("[PlayerCameraController] 时间冻结：保持锁定原点视角，仅暂停时间");
            }
        }
    }

    /// <summary>外部组件（如关卡跳转）直接设置时间冻结状态（等价于 E 键按下 / 松开）。</summary>
    public void SetTimeFrozen(bool frozen)
    {
        if (frozen == timeFrozen)
            return;
        ToggleTimeStop();
    }

    private void ToggleSpirit(bool axisMovement)
    {
        if (Mode == GameMode.Frozen)
        {
            // 冻结中进入灵魂出窍：冻结保持（timeFrozen 不变，timeScale 仍 0），退出后回到 Frozen
            Mode = GameMode.Spirit;
            strictAxisMovement = axisMovement;
            Debug.Log(axisMovement
                ? "[PlayerCameraController] 时间冻结中：灵魂出窍（严格坐标轴移动，再按 Q 返回原点）"
                : "[PlayerCameraController] 时间冻结中：灵魂出窍（自由飞行，再按 F1 返回原点）");
        }
        else if (Mode == GameMode.Normal)
        {
            Mode = GameMode.Spirit;
            strictAxisMovement = axisMovement;
            Debug.Log(axisMovement
                ? "[PlayerCameraController] 灵魂出窍：严格坐标轴移动模式（再按 Q 返回原点）"
                : "[PlayerCameraController] 灵魂出窍：摄像机相对自由飞行模式（再按 F1 返回原点）");
        }
        else if (Mode == GameMode.Spirit)
        {
            if (strictAxisMovement == axisMovement)
            {
                // 退出灵魂出窍：冻结中回到 Frozen，否则回到 Normal；相机回原点
                Mode = timeFrozen ? GameMode.Frozen : GameMode.Normal;
                if (playerCamera != null)
                    playerCamera.transform.position = origin;
                Debug.Log(timeFrozen
                    ? "[PlayerCameraController] 已返回原点，恢复时间冻结"
                    : "[PlayerCameraController] 已返回原点，恢复视角锁定");
            }
            else
            {
                strictAxisMovement = axisMovement;
                Debug.Log(axisMovement
                    ? "[PlayerCameraController] 已切换为严格坐标轴移动模式"
                    : "[PlayerCameraController] 已切换为摄像机相对自由飞行模式");
            }
        }
    }

    // ---------------- 模式行为 ----------------

    private void UpdateNormal()
    {
        if (playerCamera == null)
            return;
        // Normal 模式下玩家视角严格位于原点。
        playerCamera.transform.position = origin;
        ApplyMouseLook();
    }

    private void UpdateSpirit()
    {
        if (playerCamera == null)
            return;
        ApplyMouseLook();

        Vector3 move = strictAxisMovement
            ? GetAxisMovement()
            : GetFreeMovement();

        if (move.sqrMagnitude > 0.0001f)
        {
            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);
            // 用 unscaledDeltaTime：时间冻结（timeScale=0）时仍可自由移动
            playerCamera.transform.position += move.normalized * speed * Time.unscaledDeltaTime;
        }

        ClampSpiritPosition();
    }

    private Vector3 GetAxisMovement()
    {
        Vector3 move = Vector3.zero;
        // 严格坐标轴移动：W/S 前后（深度，对应项目 X 轴/蓝），A/D 左右（对应项目 Y 轴/红）
        move += Vector3.forward * Input.GetAxisRaw("Vertical");     // W / S -> 深度（项目 X 轴）
        move += Vector3.right * Input.GetAxisRaw("Horizontal");     // A / D -> 右（项目 Y 轴）
        if (Input.GetKey(upKey)) move += Vector3.up;              // Space -> Z
        if (Input.GetKey(downKey)) move += Vector3.down;          // C -> Z
        return move;
    }

    private Vector3 GetFreeMovement()
    {
        Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized;
        Vector3 move = forward * Input.GetAxisRaw("Vertical");
        move += right * Input.GetAxisRaw("Horizontal");
        if (Input.GetKey(upKey)) move += Vector3.up;
        if (Input.GetKey(downKey)) move += Vector3.down;
        return move;
    }

    private void ClampSpiritPosition()
    {
        Vector3 position = playerCamera.transform.position;
        position.x = Mathf.Clamp(position.x, minCoordinate, maxCoordinate);
        position.y = Mathf.Clamp(position.y, minCoordinate, maxCoordinate);
        position.z = Mathf.Clamp(position.z, minCoordinate, maxCoordinate);
        playerCamera.transform.position = position;
    }

    /// <summary>鼠标控制视角旋转（yaw / pitch）。灵敏度按当前 FOV 比例缩放：放大时自动变慢。</summary>
    private void ApplyMouseLook()
    {
        float sensitivity = mouseSensitivity * (scaleSensitivityWithZoom ? CurrentZoomSensitivityScale() : 1f);
        yaw += Input.GetAxis("Mouse X") * sensitivity;
        pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        playerCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    // ---------------- V 键放大视场角 ----------------

    /// <summary>采样放大键（按住放大、松开回落）；暂停 / 模态占用时强制视为松开。</summary>
    private void UpdateZoomInput(bool allow)
    {
        zoomHeld = allow && Input.GetKey(zoomKey);
    }

    /// <summary>
    /// 逐帧平滑推进 FOV：以真实时间（unscaledDeltaTime）按 1/zoomTransitionSeconds 的速度推进进度，
    /// 再用 SmoothStep 缓动映射到 FOV，实现「约 0.3 秒、起止慢中间顺」的丝滑过渡，绝不跳变。
    /// 放大 = 视野收窄：目标 FOV 取「放大 FOV 与基准 FOV 中较小者」，因此按住必然收窄视野、物体变大。
    /// </summary>
    private void UpdateFieldOfViewSmoothing()
    {
        if (playerCamera == null)
            return;

        float target = zoomHeld ? 1f : 0f;
        float step = zoomTransitionSeconds <= 0.001f ? 1f : Time.unscaledDeltaTime / zoomTransitionSeconds;
        zoomProgress = Mathf.MoveTowards(zoomProgress, target, step);

        float eased = Mathf.SmoothStep(0f, 1f, zoomProgress);
        // 取 Min：放大目标必须 ≤ 基准 FOV（55° < 90° → 视野收窄）；若配置值被误设得比基准大，则保持基准不变，不会反向变成缩小
        float zoomFov = Mathf.Clamp(Mathf.Min(zoomFieldOfView, baseFieldOfView), 1f, 179f);
        currentFieldOfView = Mathf.Lerp(baseFieldOfView, zoomFov, eased);

        ApplyFieldOfView();
        UpdateFovWarning();
    }

    /// <summary>把当前（平滑后的）FOV 写入相机。</summary>
    private void ApplyFieldOfView()
    {
        if (playerCamera == null)
            return;
        playerCamera.fieldOfView = Mathf.Clamp(currentFieldOfView, 1f, 179f);
    }

    /// <summary>
    /// 放大（视野收窄）期间的鼠标灵敏度缩放系数：按 tan(当前半角) / tan(基准半角) 计算，
    /// 视野越窄系数越小（90° → 55° 时约为基准的 0.52 倍），使「每像素对应的角速度」同步下调，避免放大后转头过快。
    /// 当前 FOV ≥ 基准时（未放大）系数被钳到 1，不放大灵敏度。
    /// </summary>
    private float CurrentZoomSensitivityScale()
    {
        float baseHalfTan = Mathf.Tan(0.5f * Mathf.Clamp(baseFieldOfView, 1f, 179f) * Mathf.Deg2Rad);
        float curHalfTan = Mathf.Tan(0.5f * Mathf.Clamp(currentFieldOfView, 1f, 178f) * Mathf.Deg2Rad);
        if (baseHalfTan <= 0.0001f || curHalfTan <= 0.0001f)
            return 1f;
        return Mathf.Clamp(curHalfTan / baseHalfTan, 0.05f, 1f);
    }

    /// <summary>FOV 超过阈值时显示「画面可能扭曲」提示，回落后自动消失（放大收窄方向不会触发）。</summary>
    private void UpdateFovWarning()
    {
        if (fovWarnCanvas == null)
            return;

        bool show = currentFieldOfView >= fovWarnThreshold - 0.01f;
        if (fovWarnCanvas.enabled != show)
            fovWarnCanvas.enabled = show;
    }

    /// <summary>构建「视场角过大」提示（顶部居中、时间提示下方的小字，默认关闭）。</summary>
    private void BuildFovWarningUI()
    {
        GameObject canvasGo = new GameObject("FovWarnCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // HUD(100) 之上、代码编辑器(200) 之下

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UIFitScreen>();

        GameObject textGo = new GameObject("FovWarnText", typeof(RectTransform));
        RectTransform rt = textGo.GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -112f); // 顶部时间提示（约 24~104）下方
        rt.sizeDelta = new Vector2(1200f, 40f);

        Text text = textGo.AddComponent<Text>();
        text.text = Localization.Pick("视场角过大，画面边缘可能出现扭曲", "Field of view is very wide - the image may distort");
        text.font = UIFontHelper.LoadForText(text.text, 22);
        text.fontSize = 22;
        text.color = UITheme.WithAlpha(UITheme.Alert, 0.95f);
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = UITheme.WithAlpha(UITheme.Void, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        fovWarnCanvas = canvas;
        canvas.enabled = false;
    }

    // ---------------- 工具 ----------------

    /// <summary>玩家视角相机（激光发射的起点与方向使用）。</summary>
    public Camera PlayerViewCamera => playerCamera;

    /// <summary>供 Bootstrap / 手动场景绑定相机。</summary>
    public void BindCamera(Camera cam)
    {
        playerCamera = cam;
    }

    /// <summary>暂停键（默认 ESC，可配置；EscapeRouter 读取此键做统一分发）。</summary>
    public KeyCode PauseKey => pauseKey;

    /// <summary>是否被外部模态（如指令框）占用输入。</summary>
    public bool ExternalBlock => externalBlock;

    /// <summary>
    /// 设置外部输入占用状态。占用期间本脚本不响应任何输入
    /// （视角旋转 / 移动 / 暂停键），由外部组件（如指令框）全权接管；
    /// 解除占用后自动恢复原模式行为。
    /// </summary>
    public void SetExternalBlock(bool blocked)
    {
        externalBlock = blocked;
    }

    /// <summary>是否处于「纯阅读」模态（序章讲解页 / 图鉴页）：视角与移动失效，但 ESC 暂停仍然可用。</summary>
    public bool ViewBlocked => viewBlocked;

    /// <summary>
    /// 设置纯阅读模态：置 true 后鼠标不再转动视角、WASD / E / Q / F1 全部失效（ESC 暂停保留）。
    /// 调用方（讲解页）负责同时释放鼠标；退出时置 false 并把鼠标重新锁回。
    /// </summary>
    public void SetViewBlocked(bool blocked)
    {
        viewBlocked = blocked;
    }

    /// <summary>外部组件（如指令框）要求取消暂停：若当前处于暂停则恢复。</summary>
    public void ResumeExternal()
    {
        if (Mode == GameMode.Paused)
            Resume();
    }

    /// <summary>外部组件（如指令框关闭后）要求恢复暂停状态：当前非暂停时进入暂停。</summary>
    public void PauseNow()
    {
        if (Mode != GameMode.Paused)
            Pause();
    }

    /// <summary>
    /// ESC 在关卡内的兜底分支（由 EscapeRouter 唯一判定后调用）：切换暂停 / 恢复。
    /// 主页打开指令框、关闭模态 UI 等场景不会走到这里，因此不会误暂停。
    /// </summary>
    /// <returns>是否真正切换了暂停状态。</returns>
    public bool HandleEscapePauseRequest()
    {
        if (externalBlock)
            return false;

        // 「用 ESC 关闭代码编辑器 / 指令框」的那一次 ESC 不再触发暂停（仅退出模态，游戏保持继续运行）
        if (Time.unscaledTime < pauseInputSuppressedUntil)
        {
            Debug.Log("[PlayerCameraController] ESC 用于关闭模态界面，本次不触发暂停。");
            return false;
        }

        TogglePause();
        return true;
    }

    /// <summary>
    /// 抑制暂停键一段时间（真实时间）：代码编辑器 / 指令框用 ESC 关闭后调用，
    /// 让「关闭模态的那一次 ESC」只退出模态、不再把游戏暂停，游戏保持继续运行。
    /// seconds &lt;= 0 时使用 Inspector 配置的 pauseInputSuppressSeconds。
    /// </summary>
    public void SuppressPauseInput(float seconds = -1f)
    {
        float duration = seconds > 0f ? seconds : Mathf.Max(0.05f, pauseInputSuppressSeconds);
        pauseInputSuppressedUntil = Mathf.Max(pauseInputSuppressedUntil, Time.unscaledTime + duration);
    }

    /// <summary>抑制暂停键（静态便捷版，供没有 playerController 引用的组件调用）。</summary>
    public static void SuppressPauseInputGlobal(float seconds = -1f)
    {
        PlayerCameraController player = FindObjectOfType<PlayerCameraController>();
        if (player != null)
            player.SuppressPauseInput(seconds);
    }

    /// <summary>返回主页：交由主页控制器统一收尾（终止教学、清场、解除暂停回原点、主页接管输入）。</summary>
    private void ReturnToHome()
    {
        MainMenuController menu = FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.ShowMenu();
        else
            Resume(); // 兜底：无主页时至少恢复游戏
    }

    /// <summary>重置到关卡开局状态（/level 跳转用）：恢复视角原点朝向、解除全部模态 / 冻结 / 暂停。</summary>
    public void ResetToLevelStart()
    {
        externalBlock = false;
        viewBlocked = false;
        timeFrozen = false;
        strictAxisMovement = false;
        Mode = GameMode.Normal;
        modeBeforePause = GameMode.Normal;
        Time.timeScale = 1f;
        yaw = 0f;
        pitch = 0f;
        // 视场角放大状态复位（避免跳关后仍停在放大值）
        zoomHeld = false;
        zoomProgress = 0f;
        currentFieldOfView = baseFieldOfView;
        ApplyFieldOfView();
        UpdateFovWarning();
        if (playerCamera != null)
        {
            playerCamera.transform.position = origin;
            playerCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        if (settingsPanel != null)
            settingsPanel.HidePanel();
        SetCursorLocked(true);
        Debug.Log("[PlayerCameraController] 已重置到关卡开局状态");
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

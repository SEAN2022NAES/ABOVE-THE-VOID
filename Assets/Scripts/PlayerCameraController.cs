using UnityEngine;

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
    [SerializeField] private KeyCode timeStopKey = KeyCode.E;
    [SerializeField] private KeyCode axisSpiritKey = KeyCode.Q;
    [SerializeField] private KeyCode freeSpiritKey = KeyCode.F1;
    [SerializeField] private KeyCode upKey = KeyCode.Space;
    [SerializeField] private KeyCode downKey = KeyCode.C;

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

    private SettingsPanelController settingsPanel;
    private System.Action<float> onSensitivityChangedHandler;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
        if (playerCamera == null)
            Debug.LogWarning("[PlayerCameraController] 未找到 Main Camera，请确保场景中存在带 MainCamera 标签的相机。");
    }

    private void Start()
    {
        // 关联设置面板：灵敏度实时同步、暂停请求、返回主页
        settingsPanel = FindObjectOfType<SettingsPanelController>();
        if (settingsPanel != null)
        {
            onSensitivityChangedHandler = value => MouseSensitivity = value;
            settingsPanel.OnSensitivityChanged += onSensitivityChangedHandler;
            settingsPanel.OnPauseRequested += TogglePause;
            settingsPanel.OnReturnHomeRequested += ReturnToHome;
            settingsPanel.SetSensitivityWithoutNotify(mouseSensitivity);
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
            settingsPanel.OnPauseRequested -= TogglePause;
            settingsPanel.OnReturnHomeRequested -= ReturnToHome;
        }
    }

    private void Update()
    {
        // 外部模态占用（指令框等）：视角 / 移动 / 暂停键全部让渡给外部组件
        if (externalBlock)
            return;

        // 暂停键在任何模式下都生效
        if (Input.GetKeyDown(pauseKey))
        {
            TogglePause();
            return;
        }

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
        SetCursorLocked(true);
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

    /// <summary>鼠标控制视角旋转（yaw / pitch）。</summary>
    private void ApplyMouseLook()
    {
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        playerCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    // ---------------- 工具 ----------------

    /// <summary>玩家视角相机（激光发射的起点与方向使用）。</summary>
    public Camera PlayerViewCamera => playerCamera;

    /// <summary>供 Bootstrap / 手动场景绑定相机。</summary>
    public void BindCamera(Camera cam)
    {
        playerCamera = cam;
    }

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
        timeFrozen = false;
        strictAxisMovement = false;
        Mode = GameMode.Normal;
        modeBeforePause = GameMode.Normal;
        Time.timeScale = 1f;
        yaw = 0f;
        pitch = 0f;
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

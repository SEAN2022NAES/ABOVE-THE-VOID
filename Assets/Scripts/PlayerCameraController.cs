using UnityEngine;

/// <summary>
/// 玩家视角主控（完整模式状态机）：
///   Normal —— 视角锁定原点旋转（鼠标控制）；
///   Spirit —— 灵魂出窍：Q 进入严格坐标轴移动，F1 进入摄像机相对自由飞行；
///             再按对应按键返回原点并退出；
///   Frozen —— 时间冻结：E 键暂停游戏时间（timeScale=0），玩家仍可自由移动与转视角，
///             不弹出设置界面，再按 E 恢复；
///   Paused —— 暂停：ESC 或右上角「设置」按钮触发，在任意模式下均生效，
///             灵敏度滑块实时调整鼠标灵敏度。
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
    [SerializeField] private float minCoordinate = -20f;
    [Tooltip("每个坐标分量的最大值")]
    [SerializeField] private float maxCoordinate = 20f;

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

    /// <summary>当前鼠标灵敏度（供 UI 读写）。</summary>
    public float MouseSensitivity
    {
        get => mouseSensitivity;
        set => mouseSensitivity = Mathf.Clamp(value, 0.1f, 50f);
    }

    // 视角朝向
    // 初始朝向同时观察三个正方向：X=右、Y=左、Z=上。
    private float yaw = 45f;    // 绕 Y 轴（水平）
    private float pitch = -35.264f;  // 绕 X 轴（垂直），初始同时朝向三个正方向

    // 暂停前模式，恢复时回到原模式
    private GameMode modeBeforePause = GameMode.Normal;
    private bool strictAxisMovement;

    // 外部模态占用（如指令框打开）：占用期间视角 / 移动 / 暂停键均让渡给外部组件
    private bool externalBlock;

    private SettingsPanelController settingsPanel;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
        if (playerCamera == null)
            Debug.LogWarning("[PlayerCameraController] 未找到 Main Camera，请确保场景中存在带 MainCamera 标签的相机。");
    }

    private void Start()
    {
        // 关联设置面板：灵敏度实时同步、暂停请求（右上角按钮）
        settingsPanel = FindObjectOfType<SettingsPanelController>();
        if (settingsPanel != null)
        {
            settingsPanel.OnSensitivityChanged += value => MouseSensitivity = value;
            settingsPanel.OnPauseRequested += TogglePause;
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

        // E 键时间冻结：冻结游戏时间但玩家仍可自由移动（任意非暂停模式下生效）
        if (Input.GetKeyDown(timeStopKey))
        {
            ToggleTimeStop();
            return;
        }

        // 时间冻结态：继续视角与移动（unscaledDeltaTime），Q/F1 不响应
        if (Mode == GameMode.Frozen)
        {
            UpdateSpirit();
            return;
        }

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
            case GameMode.Spirit:
                UpdateSpirit();
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
        Time.timeScale = (Mode == GameMode.Frozen) ? 0f : 1f; // 若原为时间冻结则保持冻结
        SetCursorLocked(true);
        if (settingsPanel != null)
            settingsPanel.HidePanel();
        Debug.Log("[PlayerCameraController] 恢复游戏（当前模式：" + Mode + "）");
    }

    /// <summary>E 键时间冻结切换：冻结 / 解除冻结游戏时间，玩家保持可自由移动。</summary>
    private void ToggleTimeStop()
    {
        if (Mode == GameMode.Frozen)
        {
            Mode = modeBeforePause;        // 恢复冻结前的模式
            Time.timeScale = 1f;
            Debug.Log("[PlayerCameraController] 解除时间冻结，恢复：" + Mode);
        }
        else
        {
            modeBeforePause = Mode;        // 暂存当前模式
            Mode = GameMode.Frozen;
            Time.timeScale = 0f;           // 冻结游戏逻辑，但不释放鼠标、不弹设置面板
            Debug.Log("[PlayerCameraController] 时间冻结：E 键暂停时间，玩家仍可自由移动");
        }
    }

    private void ToggleSpirit(bool axisMovement)
    {
        if (Mode == GameMode.Normal)
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
                Mode = GameMode.Normal;
                if (playerCamera != null)
                    playerCamera.transform.position = origin;
                Debug.Log("[PlayerCameraController] 已返回原点，恢复视角锁定");
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
        move += Vector3.right * Input.GetAxisRaw("Vertical");     // W / S -> X
        move += Vector3.forward * -Input.GetAxisRaw("Horizontal"); // A / D -> Y
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

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

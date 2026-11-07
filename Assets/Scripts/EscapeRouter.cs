using UnityEngine;

/// <summary>
/// 全局 ESC 优先级分发器（全项目唯一一处 ESC 判定点）：
///   1. 代码编辑器打开        → 只关闭编辑器（游戏继续运行，不弹暂停面板）；
///   2. 指令框打开            → 关闭指令框（按打开前的状态还原：主页打开的回到主页、游戏内打开的回到游戏）；
///   3. Game Over 面板显示    → 返回主菜单；
///   4. 主页可见              → 直接吞掉 ESC，完全无反应（不暂停、不开指令框、不弹 UI、不改 timeScale）；
///                              主页调出指令框的唯一入口是 ` 键（BackQuote，仅用于打开；
///                              指令框已打开时 ` 作为普通字符输入输入框，关闭只走 ESC）；
///   5. 其余（关卡内）        → 切换暂停面板（打开 / 收起，保持原有行为）。
/// 其余组件（PlayerCameraController / CommandConsoleController / CodeEditorController / LivesManager）
/// 不再各自读取 ESC，避免一次按键同时触发两个行为（例如「关掉指令框」又顺手把游戏暂停）。
/// </summary>
[DefaultExecutionOrder(-3000)] // 先于其它组件执行，保证本帧 ESC 只被这里消费一次
public class EscapeRouter : MonoBehaviour
{
    /// <summary>本次 ESC 被派发到哪个目标（调试 / 状态排查用）。</summary>
    public enum EscapeTarget
    {
        None,           // 未处理
        CodeEditor,     // 关闭代码编辑器
        CommandConsole, // 关闭指令框
        GameOver,       // Game Over 面板返回主菜单
        MainMenu,       // 主页打开指令框
        Pause           // 关卡内暂停 / 恢复
    }

    /// <summary>单例（由 GameBootstrap 创建；未创建时首次按键会自动补建）。</summary>
    public static EscapeRouter Instance { get; private set; }

    /// <summary>最近一次 ESC 的派发目标。</summary>
    public static EscapeTarget LastTarget { get; private set; } = EscapeTarget.None;

    private PlayerCameraController playerController;
    private CodeEditorController codeEditor;
    private CommandConsoleController commandConsole;
    private MainMenuController mainMenu;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveRefs();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>幂等创建：Bootstrap 自动搭建与手动场景均可安全调用。</summary>
    public static EscapeRouter EnsureExists()
    {
        if (Instance != null)
            return Instance;

        GameObject go = new GameObject("EscapeRouter");
        return go.AddComponent<EscapeRouter>();
    }

    /// <summary>补齐组件引用（组件可能在路由器之后才创建，故按需懒查找）。</summary>
    private void ResolveRefs()
    {
        if (playerController == null)
            playerController = FindObjectOfType<PlayerCameraController>();
        if (codeEditor == null)
            codeEditor = FindObjectOfType<CodeEditorController>();
        if (commandConsole == null)
            commandConsole = FindObjectOfType<CommandConsoleController>();
        if (mainMenu == null)
            mainMenu = FindObjectOfType<MainMenuController>();
    }

    private void Update()
    {
        // 暂停键沿用 PlayerCameraController 上可配置的 pauseKey（默认 ESC），避免出现第二份按键定义
        KeyCode key = playerController != null ? playerController.PauseKey : KeyCode.Escape;
        if (!Input.GetKeyDown(key))
            return;

        DispatchInternal();
    }

    /// <summary>外部（按钮 / 自动化 / 测试）主动派发一次 ESC，与真实按键走完全相同的优先级链。</summary>
    public static EscapeTarget Dispatch()
    {
        EscapeRouter router = Instance != null ? Instance : EnsureExists();
        return router.DispatchInternal();
    }

    /// <summary>唯一优先级判定：自上而下命中即返回，保证一次 ESC 只做一件事。</summary>
    private EscapeTarget DispatchInternal()
    {
        ResolveRefs();

        // 1) 代码编辑器：最高优先级，ESC 只关闭编辑器（游戏继续运行）
        if (codeEditor != null && codeEditor.IsOpen)
        {
            LastTarget = EscapeTarget.CodeEditor;
            codeEditor.CloseFromEscape();
            Debug.Log("[EscapeRouter] ESC → 关闭代码编辑器");
            return LastTarget;
        }

        // 2) 指令框：ESC 只关闭指令框，不再顺带暂停
        if (commandConsole != null && commandConsole.IsOpen)
        {
            LastTarget = EscapeTarget.CommandConsole;
            commandConsole.Close();
            Debug.Log("[EscapeRouter] ESC → 关闭指令框");
            return LastTarget;
        }

        // 3) Game Over 面板：ESC 返回主菜单
        if (LivesManager.Instance != null && LivesManager.Instance.IsGameOver)
        {
            LastTarget = EscapeTarget.GameOver;
            LivesManager.Instance.ReturnToMenu();
            Debug.Log("[EscapeRouter] ESC → Game Over 返回主菜单");
            return LastTarget;
        }

        // 4) 主页：ESC 完全无反应（不暂停、不开指令框、不弹 UI、不改 timeScale），直接吞掉并终止派发
        //    主页调出指令框的唯一入口是 `（BackQuote，由 CommandConsoleController.openKey 处理，且只在指令框关闭时打开）
        if (mainMenu != null && mainMenu.IsMenuVisible)
        {
            LastTarget = EscapeTarget.MainMenu;
            Debug.Log("[EscapeRouter] ESC → 主页忽略（无任何反应）");
            return LastTarget; // 必须 return，禁止落到下方关卡暂停分支
        }

        // 5) 关卡内：保持原暂停行为（打开 / 收起暂停面板）
        LastTarget = EscapeTarget.Pause;
        if (playerController != null)
            playerController.HandleEscapePauseRequest();
        return LastTarget;
    }
}

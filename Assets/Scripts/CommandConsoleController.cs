using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 开发者指令框（Dev Console）：
///   - 按 `（BackQuote）键打开；打开后 ` 不再用于关闭，而是作为普通字符输入到输入框；
///     关闭只保留 ESC 一条路径（输入框聚焦时 Enter 用于提交）；
///     其中 ESC 的判定统一在 EscapeRouter。主页调出本指令框的唯一入口是 ` 键
///     （主页按 ESC 完全无反应，既不暂停也不会打开本指令框）；
///   - 识别「开发者指令」：/level &lt;关卡&gt; &lt;部分&gt; 跳转关卡分区（当前已实现 level 1 - part 1），
///     help 查看可用指令；原 laser / tp / timescale 等玩家指令已全部移除；
///   - 打开时：Time.timeScale = 0、释放鼠标，并通过
///     PlayerCameraController.SetExternalBlock(true) 占用视角输入；
///     关闭时恢复 timeScale = 1、锁定鼠标并解除占用。
/// 纯代码构建 UI，挂到任意物体即可。
/// </summary>
public class CommandConsoleController : MonoBehaviour
{
    /// <summary>激光发射事件（教学 / 自动化脚本监听用，鼠标左键与代码开火都会触发）。</summary>
    public static event System.Action OnLaserFired;

    /// <summary>触发激光发射事件（事件只能在声明类型内 invoke，供 CodeTower 等外部类转发）。</summary>
    public static void NotifyLaserFired()
    {
        if (OnLaserFired != null)
            OnLaserFired();
    }

    [Header("按键")]
    [Tooltip("打开指令框（` 键；仅在指令框关闭时生效，打开后 ` 作为普通字符输入，关闭只能用 ESC）")]
    public KeyCode openKey = KeyCode.BackQuote;
    // 关闭键固定为 ESC，且 ESC 的判定统一在 EscapeRouter（全项目唯一 ESC 入口），本组件不再自行监听

    [Header("UI")]
    [Tooltip("标题字号")]
    public int titleFontSize = 22;
    [Tooltip("输入框字号")]
    public int inputFontSize = 20;
    [Tooltip("输出区字号")]
    public int outputFontSize = 16;
    [Tooltip("面板底色")]
    public Color panelColor = UITheme.Panel;
    [Tooltip("输入框底色")]
    public Color inputBgColor = UITheme.CodeInputBg;
    [Tooltip("输出文字颜色")]
    public Color outputColor = UITheme.TextBody;

    [Header("激光")]
    [Tooltip("激光半径（粗细），代码/指令参数可控制")]
    public float laserRadius = 0.15f;
    [Tooltip("激光最大射程（单位）")]
    public float laserMaxLength = 60f;

    /// <summary>
    /// 手动（鼠标左键）激光总开关：只有 Level 1 - Part 1（引导教学关）允许手动发射激光，
    /// 其余所有关卡（Level 1 - Part 2 讲解关 / Part 3 语法课、Level 2 - Part 1 图鉴关 / Part 2 API 课、Level 3 - Part 1 及后续完整关）一律禁用。
    /// 由 LevelDirector / MainMenuController 在进入关卡时通过 SetManualLaserAllowed 设置；
    /// 只拦截「左键 → FireLaser」这一条手动链路，不影响炮台代码 tower.FireAtPlane /
    /// 自动打方块脚本 AutoLaserExample / 教学 Step.FireLaser 等调用 console.FireLaser 的链路。
    /// </summary>
    public static bool ManualLaserEnabled = false;

    /// <summary>
    /// 设置手动激光开关（唯一入口，避免各关卡入口各写一套判断）：
    /// 仅当「Level 1 - Part 1」时允许手动发射激光。
    /// </summary>
    public static void SetManualLaserAllowed(int level, int part)
    {
        bool allowed = (level == 1 && part == 1);
        ManualLaserEnabled = allowed;
        string where = (level <= 0) ? "主页 / 无关卡" : ("Level " + level + " - Part " + part);
        Debug.Log("[CommandConsole] 手动激光（鼠标左键）：" + (allowed ? "已启用" : "已禁用") + "（" + where + "）");
    }
    [Tooltip("激光视觉显示时长（秒，用不受时间冻结影响的真实时间）")]
    public float laserVisualDuration = 0.9f;
    [Tooltip("激光视觉颜色")]
    public Color laserColor = UITheme.WithAlpha(UITheme.Accent, 0.9f);

    private Canvas canvas;
    private GameObject consoleRoot;
    private InputField inputField;
    private Text outputText;
    private PlayerCameraController playerController;
    // 打开指令框前游戏是否处于暂停（关闭后需还原暂停状态，不让游戏继续）
    private bool wasPausedBeforeOpen;
    // 打开指令框前是否处于 E 键时间冻结（timeScale=0，关闭后需保持冻结，不让游戏继续）
    private bool wasFrozenBeforeOpen;
    // 是否由「主页按 ESC」打开：关闭后必须完全回到主页状态（不解除相机输入占用、不锁鼠标、不改时间流速）
    private bool openedFromMainMenu;
    // 打开指令框前的 timeScale（主页打开时原样还原，保证「收起后状态与按 ESC 前一致」）
    private float timeScaleBeforeOpen = 1f;
    private MainMenuController mainMenu;
    // 打开指令框的那一帧号：该帧内 openKey 自身产生的字符不交给输入框，避免「开框瞬间自带一个 `」
    private int suppressOpenKeyCharFrame = -1;

    /// <summary>/lock 二次确认窗口时长（秒，真实时间）：窗口内再次输入 /lock 才真正清档。</summary>
    private const float LockConfirmWindowSeconds = 15f;
    /// <summary>/lock 是否已进入待确认状态（第一次输入 /lock 后置位的破坏性操作门闩）。</summary>
    private bool lockConfirmPending;
    /// <summary>/lock 待确认状态的到期时刻（Time.unscaledTime 时间轴）。</summary>
    private float lockConfirmExpireAt;

    /// <summary>指令框当前是否打开。</summary>
    public bool IsOpen => consoleRoot != null && consoleRoot.activeSelf;

    /// <summary>玩家视角相机（自动化脚本取射线起点用）。</summary>
    public Camera PlayerViewCamera
        => playerController != null ? playerController.PlayerViewCamera : Camera.main;

    private void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        consoleRoot.SetActive(false);
    }

    private void Start()
    {
        playerController = FindObjectOfType<PlayerCameraController>();
        mainMenu = FindObjectOfType<MainMenuController>();
    }

    private void Update()
    {
        // ` 键只在「指令框关闭」时用于打开：打开后不再监听该键，直接让 InputField 把它当普通字符插入，
        // 关闭只保留 ESC 一条路径（统一由 EscapeRouter 做唯一优先级判定后调用 Close()），
        // 避免同一次按键既被本组件当成开关、又被输入框当成字符（两处消费）。
        if (!IsOpen && Input.GetKeyDown(openKey))
        {
            // 开框这一帧的同一次按键不应该再被输入框吃成字符（否则开框瞬间输入框自带一个 `）
            suppressOpenKeyCharFrame = Time.frameCount;
            Open();
            return;
        }

        // 鼠标左键发射激光：指令框关闭、未被外部模态占用（如主页）、非暂停状态、
        // 且当前关卡允许手动激光（Level 3 完整玩法关闭，改由代码编辑器 / 炮台接管）时有效
        if (ManualLaserEnabled && !IsOpen && Input.GetMouseButtonDown(0))
        {
            bool blocked = playerController != null &&
                (playerController.ExternalBlock || playerController.ViewBlocked); // 主页 / 序章讲解页等模态时不发激光
            bool paused = playerController != null &&
                playerController.Mode == PlayerCameraController.GameMode.Paused;
            if (!blocked && !paused)
                FireLaser(laserRadius, false);
        }
    }

    // ---------------- 开关逻辑（与 PlayerCameraController 联动） ----------------

    /// <summary>打开指令框：暂停时间、释放鼠标、占用视角输入。</summary>
    public void Open()
    {
        OpenInternal(false);
    }

    /// <summary>
    /// 在主页「前台」打开指令框（不暂停、不动 timeScale、不锁鼠标）。
    /// 说明：主页调出指令框的常规入口已改为 ` 键（本组件的 openKey），EscapeRouter 不再调用本方法；
    /// 保留该公开入口供外部/自动化脚本显式以「主页模式」打开时使用。
    /// </summary>
    public void OpenFromMainMenu()
    {
        OpenInternal(true);
    }

    private void OpenInternal(bool fromMainMenu)
    {
        if (IsOpen)
            return;

        if (mainMenu == null)
            mainMenu = FindObjectOfType<MainMenuController>();
        if (playerController == null)
            playerController = FindObjectOfType<PlayerCameraController>();

        // 只要主页仍在显示，就按「主页模式」打开（无论入口是 ESC 还是 `），避免收起后把主页变成暂停态
        bool menuMode = fromMainMenu || (mainMenu != null && mainMenu.IsMenuVisible);

        openedFromMainMenu = menuMode;
        timeScaleBeforeOpen = Time.timeScale;

        if (playerController != null)
        {
            // 若暂停菜单正打开，先恢复，避免两个模态 UI 叠加；记录状态供关闭时还原
            // （主页模式不存在暂停概念，一律按「未暂停」记录，避免收起后又把主页变成暂停态）
            wasPausedBeforeOpen = !menuMode &&
                (playerController.Mode == PlayerCameraController.GameMode.Paused);
            // 记录打开前是否处于 E 键时间冻结（关闭后需保持冻结，不解除）
            wasFrozenBeforeOpen = playerController.IsTimeFrozen;
            if (wasPausedBeforeOpen)
                playerController.ResumeExternal();
            playerController.SetExternalBlock(true);
        }

        consoleRoot.SetActive(true);
        inputField.text = string.Empty;
        inputField.ActivateInputField();
        inputField.Select();
        // 显式把 EventSystem 选中项指向输入框：InputField 只有作为 currentSelectedGameObject 才能收到键盘事件。
        // （主页 MainMenuController.LateUpdate 只在「主页可见且指令框未打开」时清空选中项，这里再补一次显式选中，
        //  保证从 ` 键 / EscapeRouter / 自动化脚本任何入口打开后都能直接打字、回车提交。）
        EventSystem es = EventSystem.current;
        if (es != null)
            es.SetSelectedGameObject(inputField.gameObject);

        if (menuMode)
        {
            // 主页：不改时间流速（主页本来就不是靠 timeScale 冻结），鼠标保持可见可点
            Time.timeScale = timeScaleBeforeOpen;
            SetCursorLocked(false);
        }
        else
        {
            Time.timeScale = 0f;
            SetCursorLocked(false);
        }

        AppendOutput("开发者指令框已打开。输入 help 查看可用指令，Enter 提交，ESC 关闭。");
        Debug.Log("[CommandConsole] 指令框打开" + (menuMode ? "（主页模式）" : string.Empty));
    }

    /// <summary>
    /// 关闭指令框：恢复时间流速、锁定鼠标、解除视角输入占用；若打开前处于暂停则还原暂停。
    /// 若本次是从主页按 ESC 打开的，则收起后完全回到主页接管状态（相机输入仍占用、鼠标仍可见、时间流速不变）。
    /// </summary>
    public void Close()
    {
        if (mainMenu == null)
            mainMenu = FindObjectOfType<MainMenuController>();

        // 判定「收起后是否要回到主页」：主页仍在显示（含从主页打开后又用 /exit 之类回到主页的场景）
        bool backToMainMenu = openedFromMainMenu && (mainMenu == null || mainMenu.IsMenuVisible);

        consoleRoot.SetActive(false);
        openedFromMainMenu = false;

        if (backToMainMenu)
        {
            wasPausedBeforeOpen = false;
            wasFrozenBeforeOpen = false;

            Time.timeScale = timeScaleBeforeOpen;   // 状态与按 ESC 前一致，不改时间流速
            if (playerController != null)
                playerController.SetExternalBlock(true); // 主页继续占用相机输入，禁止游戏按键与暂停
            SetCursorLocked(false);                 // 主页鼠标保持可见，关卡按钮仍可点击

            Debug.Log("[CommandConsole] 指令框关闭（返回主页，状态与打开前一致）");
            return;
        }

        if (playerController != null)
            playerController.SetExternalBlock(false);

        if (wasPausedBeforeOpen && playerController != null)
        {
            // 打开指令框前游戏处于暂停：关闭后恢复暂停状态，不让游戏继续
            playerController.PauseNow();
            wasPausedBeforeOpen = false;
            wasFrozenBeforeOpen = false;
        }
        else if (wasFrozenBeforeOpen)
        {
            // 打开指令框前处于 E 键时间冻结：保持 timeScale=0，不解除冻结
            SetCursorLocked(true);
            Time.timeScale = 0f;
            wasFrozenBeforeOpen = false;
        }
        else
        {
            Time.timeScale = 1f;
            SetCursorLocked(true);
        }

        // 关闭指令框后短暂忽略暂停键：避免「关闭指令框」的那一次 ESC 紧接着触发暂停
        if (playerController != null)
            playerController.SuppressPauseInput();
        else
            PlayerCameraController.SuppressPauseInputGlobal();

        // 关闭时清掉 EventSystem 选中项：让主页立刻回到「Enter 完全无反应」的初始状态
        // （指令框打开期间 MainMenuController.LateUpdate 不再清选中项，这里补一次收尾）
        EventSystem es = EventSystem.current;
        if (es != null)
            es.SetSelectedGameObject(null);

        Debug.Log("[CommandConsole] 指令框关闭");
    }

    /// <summary>openKey 对应的可打印字符（仅用于「开框那一帧不重复输入」保护；非可打印键返回 '\0'）。</summary>
    private char OpenKeyChar => openKey == KeyCode.BackQuote ? '`' : '\0';

    /// <summary>
    /// 输入框字符过滤（InputField.onValidateInput）：只做一件事 —— 拦掉「按下 openKey 打开指令框」那一帧
    /// 由同一个按键产生的 ` / ~ 字符；其余字符（含指令框打开后正常按 ` 输入）一律放行。
    /// </summary>
    private char ValidateInputCharacter(string text, int charIndex, char addedChar)
    {
        if (suppressOpenKeyCharFrame == Time.frameCount && OpenKeyChar != '\0' &&
            (addedChar == '`' || addedChar == '~'))
            return '\0';
        return addedChar;
    }

    private bool IsInputFocused()
    {
        EventSystem es = EventSystem.current;
        return es != null && es.currentSelectedGameObject == inputField.gameObject;
    }

    // ---------------- 指令执行 ----------------

    private void OnCommandSubmitted(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            inputField.ActivateInputField();
            inputField.Select();
            return;
        }

        string trimmed = text.Trim();
        AppendOutput("> " + trimmed);
        CommandData cmd = CommandParser.Parse(trimmed);
        Execute(cmd);

        // 提交后保持打开，方便连续输入；按 ESC 关闭
        inputField.text = string.Empty;
        inputField.ActivateInputField();
        inputField.Select();
    }

    /// <summary>按 CommandType 分支分发到具体执行函数（新增指令在此补充分支）。</summary>
    private void Execute(CommandData cmd)
    {
        // /lock 的待确认状态：一旦输入其它任何指令即失效（避免「警告后干了别的事，回头再 /lock 反而被当成确认」）
        if (cmd.Type != CommandType.Lock && lockConfirmPending)
        {
            lockConfirmPending = false;
            lockConfirmExpireAt = 0f;
        }

        switch (cmd.Type)
        {
            case CommandType.Help:
                ExecuteHelp();
                break;
            case CommandType.Level:
                ExecuteLevel(cmd);
                break;
            case CommandType.Unlock:
                ExecuteUnlock();
                break;
            case CommandType.Lock:
                ExecuteLock(cmd);
                break;
            case CommandType.Exit:
                ExecuteExit();
                break;
            default:
                AppendOutput("未知指令：" + cmd.Raw + "（输入 help 查看可用指令）");
                break;
        }
    }

    private void ExecuteHelp()
    {
        AppendOutput(
            "开发者指令 / Developer Commands：\n" +
            "  /exit                     - 关闭指令框并返回主页\n" +
            "                              / close console and return to home\n" +
            "  /level <关卡> <部分>       - 跳转到指定关卡分区（如 /level 1 2、/level 2 1、/level 2 2、/level 3 1）\n" +
            "                              / jump to level part, e.g. /level 1 2, /level 2 1, /level 2 2, /level 3 1\n" +
            "  /level <关卡> <部分> <章节> - 跳转并直接定位到该关第 n 个 part 章节（n 为整数，从 1 开始）\n" +
            "                              例：/level 1 2 3 跳到 Level 1 - Part 2 第 3 章；/level 2 2 5 跳到 Level 2 - Part 2 第 5 章\n" +
            "                              / jump to the n-th task/chapter of that level-part, e.g. /level 1 2 3\n" +
            "  /unlock                    - 一键解锁全部关卡（中英别名：unlock / 解锁），通关进度仍会正常记录\n" +
            "                              / unlock every level at once (aliases: unlock / 解锁)\n" +
            "  /lock                      - 回厂重置（需二次确认）：首次输入只警告、不清档；15 秒内再次输入 /lock\n" +
            "                              或 /lock confirm 才真正清空关卡进度、游玩进度与最高分、设置与编辑器代码（别名：lock / 锁定）\n" +
            "                              / factory reset (2-step confirm): the first /lock only warns; type it again\n" +
            "                              within 15s (or /lock confirm) to wipe level progress, play progress & best score, settings and editor code\n" +
            "  help / 帮助               - 显示帮助\n" +
            "                              / show this help");
    }

    /// <summary>
    /// 开发者指令 /unlock（别名 解锁）：一键解锁全部关卡并写入 PlayerPrefs（跨重启保留），
    /// 同时刷新主菜单关卡按钮（在主页上执行可立即看到按钮由锁定变为可点）。
    /// </summary>
    private void ExecuteUnlock()
    {
        bool changed = LevelProgress.UnlockAllLevels();

        MainMenuController menu = FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.RefreshLevelLocks();

        if (changed)
            AppendOutput("[unlock] 已解锁全部关卡（1 / 2 / 3），进度已保存。/ unlock: all levels unlocked (saved).");
        else
            AppendOutput("[unlock] 全部关卡此前已解锁。/ unlock: all levels were already unlocked.");
    }

    /// <summary>
    /// 开发者指令 /lock（别名 锁定）：回厂重置 —— 把本地存档全部清零，恢复到刚安装游戏的出厂状态。
    ///   ① 关卡进度：LevelProgress.ResetProgress()（UnlockedLevel 回 1、CompletedLevel 回 0，并 PlayerPrefs.Save()）；
    ///   ② 游玩进度：GameProgress.ClearAll() —— 续玩断点 ABOVETHEVOID.ResumeLevel / ResumePart / ResumeSection
    ///      与全部关卡最高分 ABOVETHEVOID.HighScore.Level1~3 一次清空（并 PlayerPrefs.Save()）；
    ///   ③ 设置：视场角 atv.settings.fov / 灵敏度 atv.settings.sensitivity / 帧率 atv.settings.fps
    ///      三个存档键全部删除，滑块恢复出厂值（90° / 1.75 / 60）并立即应用到相机与 Application.targetFrameRate；
    ///   ④ 编辑器代码：CodeEditorController.ResetCodeToDefault()（回到初始示例模板）；
    ///   ⑤ 主页按钮：RefreshLevelLocks() 立即刷新为「只有关卡 1 可玩」（最高分行同时消失）；
    ///   ⑥ 若当前在关卡内：关闭指令框并把玩家送回主页，避免关卡状态与清零后的存档不一致。
    /// 输出走指令框自身的输出通道，与 /unlock 完全一致（中文全中文、英文全英文）。
    ///
    /// 双重确认门（v0.2.0 新增）：本函数是破坏性操作，必须先经过确认：
    ///   · 第一次输入 /lock（或 /lock confirm、/锁定、lock）：不执行任何清档动作，只输出警告并进入待确认状态；
    ///   · 待确认状态有效期 LockConfirmWindowSeconds（15 秒，用不受 timeScale 影响的 unscaledTime 计时，
    ///     指令框打开时 timeScale=0 也能正常到期）；
    ///   · 有效期内再次输入 /lock（或 /lock confirm）：真正执行回厂重置；
    ///   · 输入其它任何指令、或超时：待确认状态自动失效，下次 /lock 重新从警告开始。
    /// </summary>
    /// <param name="cmd">解析后的指令（Args[0] 可为 confirm/确认，语义与再次输入 /lock 相同，仍需先进入待确认状态）。</param>
    private void ExecuteLock(CommandData cmd)
    {
        float now = Time.unscaledTime;
        bool hasPending = lockConfirmPending && now <= lockConfirmExpireAt;

        if (!hasPending)
        {
            // 第一次输入：只警告，不动任何存档
            lockConfirmPending = true;
            lockConfirmExpireAt = now + LockConfirmWindowSeconds;
            AppendOutput("[lock] 警告：此操作会清空全部存档（关卡进度、游玩进度与最高分、设置、编辑器代码），且无法撤销。\n" +
                         "        请在 " + (int)LockConfirmWindowSeconds + " 秒内再次输入 /lock 确认（或输入 /lock confirm）。\n" +
                         "        / WARNING: this wipes ALL local saves (level progress, play progress & best score, settings, editor code) and cannot be undone.\n" +
                         "        / Type /lock again within " + (int)LockConfirmWindowSeconds + "s to confirm (or use /lock confirm).");
            Debug.Log("[CommandConsole] /lock 首次调用：仅警告，等待二次确认。");
            return;
        }

        // 已确认：关闭确认门并真正执行
        lockConfirmPending = false;
        lockConfirmExpireAt = 0f;

        // ① 关卡进度清零：ABOVETHEVOID.UnlockedLevel = 1、ABOVETHEVOID.CompletedLevel = 0，并 PlayerPrefs.Save()
        LevelProgress.ResetProgress();

        // ② 游玩进度存档清零：续玩断点（ABOVETHEVOID.ResumeLevel / ResumePart / ResumeSection）
        //    + 全部关卡最高分（ABOVETHEVOID.HighScore.Level1~3）一次清空，并把会话内的当前位置重置为「不在任何关卡内」。
        //    必须早于后面的 menu.ShowMenu()：ShowMenu 会结算 Level 3 最高分，先清空才能保证一次 /lock 后主页无最高分残留。
        GameProgress.ClearAll();

        // ③ 设置恢复出厂默认：删除 atv.settings.fov / sensitivity / fps 存档，并把滑块、相机视场角与灵敏度、帧率立即改回默认值
        SettingsPanelController settings = FindObjectOfType<SettingsPanelController>();
        if (settings != null)
            settings.ResetToFactoryDefaults();
        else
            SettingsPanelController.ClearSavedSettings(); // 设置面板未构建时至少清掉全部存档键

        // ④ 编辑器代码清回初始模板（inputField.text = DEFAULT_CODE，并同步 lastCommittedText / defaultCodeFilled）
        CodeEditorController editor = FindObjectOfType<CodeEditorController>();
        if (editor != null)
            editor.ResetCodeToDefault();

        Debug.Log("[CommandConsole] /lock 回厂重置：关卡进度、游玩进度与最高分、设置、编辑器代码已清空。");

        // ⑤ / ⑥ 刷新主页锁定状态；若当前在关卡内，先写入提示再关指令框并把玩家送回主页
        MainMenuController menu = FindObjectOfType<MainMenuController>();
        bool inLevel = menu != null && !menu.IsMenuVisible;

        if (inLevel)
        {
            AppendOutput("[lock] 已恢复出厂设置：关卡进度、游玩进度与最高分、设置、编辑器代码已清空，正在返回主页。\n" +
                         "        / lock: factory reset done — level progress, play progress & best score, settings and editor code wiped. Returning home.");
            CloseAndForceResume();                    // 先收指令框（忽略打开前的暂停 / 冻结状态）
            CodeEditorController.ForceCloseIfOpen();  // 关卡内若代码编辑器还开着，一并收起，避免残留在主页上
            menu.ShowMenu();                          // 内部会调用 RefreshLevelLocks()，主页按钮立即只剩关卡 1 可玩
        }
        else
        {
            AppendOutput("[lock] 已恢复出厂设置：关卡进度、游玩进度与最高分、设置、编辑器代码已清空（当前只有关卡 1 可玩）。\n" +
                         "        / lock: factory reset done — level progress, play progress & best score, settings and editor code wiped (only Level 1 is playable now).");
            if (menu != null)
                menu.RefreshLevelLocks();
        }
    }

    /// <summary>
    /// 开发者指令 /level &lt;关卡&gt; &lt;部分&gt; [章节]：跳转到指定关卡的分区，
    /// 可选第三参数 n 表示该分区内的章节序号（1 起）：Level 1 - Part 2 讲解关按页（4 页）、
/// Level 1 - Part 3 语法课按任务（18 项）、Level 2 - Part 2 API 课按任务（13 项）。
    /// 参数非法（非整数 / 章节越界 / 关卡未开放）时只输出提示，不抛异常。
    ///
    /// 与断点续玩存档的优先级：本指令优先级最高。跳转成功时 LevelDirector.JumpTo 会把「本次跳关参数」写入续玩存档，
    /// 无条件覆盖旧存档，跳关后的位置即成为新的续玩点；主页进关卡时读的就是这份被覆盖后的存档，
    /// 因此不会出现「用 /level 跳到某处后，再从主页进关被旧存档拽回跳转之前的位置」。
    /// </summary>
    private void ExecuteLevel(CommandData cmd)
    {
        if (cmd.Args == null || cmd.Args.Length < 2)
        {
            AppendOutput("[level] 用法：/level <关卡> <部分> [章节]，如 /level 1 1、/level 1 2 3");
            return;
        }

        if (!int.TryParse(cmd.Args[0], out int level) || !int.TryParse(cmd.Args[1], out int part))
        {
            AppendOutput("[level] 用法：/level <关卡> <部分> [章节]，如 /level 1 1、/level 1 2 3（关卡 / 部分必须为整数）");
            return;
        }

        // 可选第三参数：章节序号 n（1 起）
        int section = 0;
        if (cmd.Args.Length >= 3)
        {
            if (!int.TryParse(cmd.Args[2], out section))
            {
                AppendOutput("[level] 章节参数必须为整数：/level " + level + " " + part + " <章节序号>，如 /level 1 2 3");
                return;
            }
            if (section < 1)
            {
                AppendOutput("[level] 章节序号从 1 开始：/level " + level + " " + part + " <章节序号>，如 /level 1 2 1");
                return;
            }
        }

        string message = LevelDirector.JumpTo(level, part, section);
        AppendOutput(message);
        if (message.Contains("已跳转"))
            CloseAndForceResume(); // 跳转成功后关闭指令框，回到正常游戏状态
    }

    /// <summary>
    /// 开发者指令 /exit：强制关闭指令框并恢复正常游戏状态，
    /// 再复用主页返回逻辑统一收尾（终止教学、清场暂停生成、解除暂停回原点、主页接管输入）。
    /// </summary>
    private void ExecuteExit()
    {
        CloseAndForceResume();
        MainMenuController menu = FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.ShowMenu();
        else
            AppendOutput("[exit] 未找到主菜单，无法返回主页。");
    }

    /// <summary>强制关闭指令框并恢复正常游戏状态（关卡跳转后调用，忽略打开前的暂停 / 冻结状态）。</summary>
    public void CloseAndForceResume()
    {
        wasPausedBeforeOpen = false;
        wasFrozenBeforeOpen = false;
        Close();
    }

    /// <summary>
    /// 从玩家视角相机位置沿准心方向发射激光（鼠标左键与 laser 指令共用）。
    /// 命中路径上的所有长方体：每个被击中的块从入射侧沿激光主导轴吃掉一层厚度
    /// （每层 = 初始尺寸的 1/maxHits，层数由该块最大 HP 决定），剩余部分保留为子块继续飞行；
    /// 累计 maxHits 次被吃光即死亡。
    /// 激光本体为受控的圆柱视觉，短暂显示后消失。
    /// </summary>
    public void FireLaser(float radius, bool reportToConsole)
    {
        OnLaserFired?.Invoke(); // 教学引导：记录玩家发射激光

        Camera cam = playerController != null ? playerController.PlayerViewCamera : Camera.main;
        if (cam == null)
        {
            if (reportToConsole)
                AppendOutput("[laser] 未找到玩家视角相机，无法发射。");
            return;
        }

        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;

        // 收集路径上所有命中（含子物体 collider，取所属 FlyingBlock 去重）
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, laserMaxLength);
        HashSet<FlyingBlock> hitBlocks = new HashSet<FlyingBlock>();
        float nearestT = laserMaxLength;
        for (int i = 0; i < hits.Length; i++)
        {
            FlyingBlock block = hits[i].collider != null
                ? hits[i].collider.GetComponentInParent<FlyingBlock>()
                : null;
            if (block == null || block.Destroyed)
                continue;

            // 蓝色炸弹块与红色攻击块同等对待：正常加入切割列表、正常可被击毁
            // （击毁蓝块不加分也不扣分，该规则集中由 ScoreManager.ApplyKill 处理）
            hitBlocks.Add(block);
            if (hits[i].distance < nearestT)
                nearestT = hits[i].distance;
        }

        // 逐个命中：每个块从入射侧吃掉一层厚度（血量随机 1~6，吃满即死亡；HP=1 一击即毁）
        int hitCount = 0;
        int deathCount = 0;
        foreach (FlyingBlock block in hitBlocks)
        {
            List<FlyingBlock> remnants = block.CutByLaser(origin, dir, radius);
            if (remnants != null)
            {
                hitCount++;
                if (remnants.Count == 0)
                    deathCount++; // 被吃光死亡（无剩余子块）
            }
        }

        // 激光视觉：从发射点到最近命中点（未命中则打满射程）；命中任意目标时在端点额外显示火花特效
        Vector3 endPoint = origin + dir * nearestT;
        SpawnLaserVisual(origin, endPoint, radius, hitBlocks.Count > 0);

        if (reportToConsole)
            AppendOutput("[laser] 半径=" + radius.ToString("F2") + "，可击打命中 " + hitCount + " 个长方体（吃掉 " + hitCount +
                         " 层厚度，其中 " + deathCount + " 个被打满血量死亡；蓝色炸弹块可被击毁但不计分）。");
        Debug.Log("[CommandConsole] 发射激光：radius=" + radius + " hits=" + hitBlocks.Count + " deaths=" + deathCount);
    }

    /// <summary>
    /// 生成激光视觉（特效版）：双层圆柱（外层低透明度光晕 + 内芯高亮光束），
    /// 命中时在端点额外生成发光火花小球；整体保留 laserVisualDuration 秒后淡出并销毁
    /// （用不受时间冻结影响的真实时间）。
    /// </summary>
    private void SpawnLaserVisual(Vector3 origin, Vector3 end, float radius, bool impact)
    {
        Vector3 delta = end - origin;
        float length = delta.magnitude;
        if (length < 0.01f)
            return;

        Vector3 center = (origin + end) * 0.5f;
        Vector3 dir = delta / length;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        // 外层光晕圆柱：半径 3 倍、低透明度，营造光束辉光
        GameObject haloGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        haloGo.name = "LaserVisual_Halo";
        haloGo.transform.position = center;
        haloGo.transform.rotation = rot;
        haloGo.transform.localScale = new Vector3(radius * 6f, length * 0.5f, radius * 6f);
        DisableLaserCollider(haloGo);
        Material haloMat = CreateLaserMaterial(new Color(laserColor.r, laserColor.g, laserColor.b, 0.22f));
        haloGo.GetComponent<MeshRenderer>().sharedMaterial = haloMat;

        // 内芯圆柱：半径 1 倍、高亮，实际"激光"本体
        GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coreGo.name = "LaserVisual_Core";
        coreGo.transform.position = center;
        coreGo.transform.rotation = rot;
        coreGo.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        DisableLaserCollider(coreGo);
        Material coreMat = CreateLaserMaterial(laserColor);
        coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

        // 命中点特效：发光小球（偏暖色火花），随淡出一起缩小消失
        GameObject impactGo = null;
        Material impactMat = null;
        if (impact)
        {
            impactGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactGo.name = "LaserImpact";
            impactGo.transform.position = end;
            impactGo.transform.localScale = Vector3.one * radius * 3f;
            DisableLaserCollider(impactGo);
            impactMat = CreateLaserMaterial(UITheme.WithAlpha(UITheme.Alert, 0.95f));
            impactGo.GetComponent<MeshRenderer>().sharedMaterial = impactMat;
        }

        StartCoroutine(DestroyLaserVisual(coreGo, haloGo, impactGo, coreMat, haloMat, impactMat));
    }

    /// <summary>禁用并移除激光视觉物体的 Collider，避免干扰后续射线检测。</summary>
    private void DisableLaserCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null)
        {
            c.enabled = false;
            Destroy(c);
        }
    }

    /// <summary>创建激光特效材质（Sprites/Default 半透明叠加，缺失时退回 Unlit/Color）。</summary>
    private Material CreateLaserMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    /// <summary>
    /// 激光视觉存活协程：前 40% 时长保持全亮，后 60% 时长线性淡出（真实时间，不受 timeScale=0 冻结影响），
    /// 命中火花同步缩小；结束后销毁全部视觉物体。
    /// </summary>
    private IEnumerator DestroyLaserVisual(GameObject core, GameObject halo, GameObject impact, Material coreMat, Material haloMat, Material impactMat)
    {
        float t = 0f;
        float dur = Mathf.Max(0.05f, laserVisualDuration);
        Vector3 impactScale = impact != null ? impact.transform.localScale : Vector3.one;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            // 前 40% 全亮，后 60% 从 1 线性降到 0
            float fade = k < 0.4f ? 1f : Mathf.Clamp01(1f - (k - 0.4f) / 0.6f);

            if (coreMat != null) coreMat.color = FadeLaserAlpha(laserColor, fade);
            if (haloMat != null) haloMat.color = FadeLaserAlpha(haloMat.color, 0.22f * fade);
            if (impactMat != null) impactMat.color = FadeLaserAlpha(impactMat.color, fade);
            if (impact != null)
                impact.transform.localScale = impactScale * Mathf.Lerp(1f, 0.15f, k);

            yield return null;
        }

        if (core != null) Destroy(core);
        if (halo != null) Destroy(halo);
        if (impact != null) Destroy(impact);
    }

    /// <summary>按给定 alpha 覆盖颜色透明度（保留 RGB）。</summary>
    private static Color FadeLaserAlpha(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }


    private void AppendOutput(string line)
    {
        if (outputText == null)
            return;
        if (outputText.text.Length > 0)
            outputText.text += "\n" + line;
        else
            outputText.text = line;

        // 简单长度保护：过长时丢弃最早的一半
        if (outputText.text.Length > 2000)
            outputText.text = "[日志已截断]\n" + outputText.text.Substring(outputText.text.Length / 2);

        // v0.1.1：输出内容中英混合，按内容同步刷新字体（中文 → Noto Sans CJK SC，英文/数字 → JetBrains Mono）
        outputText.font = UIFontHelper.LoadForText(outputText.text, outputText.fontSize);
    }

    // ---------------- UI 构建 ----------------

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 置顶：主页(100) / 代码编辑器(200) / Game Over(240) 之上，保证「主页按 ESC 调出」时在主页前台可见可输入
        canvas.sortingOrder = 320;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        // ---- 面板 ----
        RectTransform panelRt = CreateRect("CommandConsole", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 480f), new Vector2(0.5f, 0.5f));
        consoleRoot = panelRt.gameObject;
        Image panelImg = consoleRoot.AddComponent<Image>();
        panelImg.color = panelColor;

        // ---- 标题 ----
        Text title = CreateText("Title", panelRt, "开发者指令框 (Dev Console)  -  ESC 关闭 / Enter 提交", titleFontSize, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -20f);
        titleRt.sizeDelta = new Vector2(-40f, 44f);

        // ---- 输出区 ----
        outputText = CreateText("Output", panelRt, "", outputFontSize, outputColor, TextAnchor.UpperLeft);
        RectTransform outRt = outputText.rectTransform;
        outRt.anchorMin = new Vector2(0f, 1f);
        outRt.anchorMax = new Vector2(1f, 1f);
        outRt.pivot = new Vector2(0.5f, 1f);
        outRt.anchoredPosition = new Vector2(0f, -78f);
        outRt.sizeDelta = new Vector2(-40f, -160f);
        outputText.supportRichText = false;
        outputText.horizontalOverflow = HorizontalWrapMode.Wrap;
        outputText.verticalOverflow = VerticalWrapMode.Overflow;

        // ---- 输入框 ----
        GameObject inputGo = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(panelRt, false);
        RectTransform inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0f, 0f);
        inputRt.anchorMax = new Vector2(1f, 0f);
        inputRt.pivot = new Vector2(0.5f, 0f);
        inputRt.anchoredPosition = new Vector2(0f, 20f);
        inputRt.sizeDelta = new Vector2(-40f, 44f);

        Image inputBg = inputGo.GetComponent<Image>();
        inputBg.color = inputBgColor;

        InputField input = inputGo.GetComponent<InputField>();
        input.targetGraphic = inputBg;

        Text inputText = CreateText("Text", inputRt, "", inputFontSize, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        Stretch(inputText.rectTransform);
        inputText.supportRichText = false;
        inputText.font = UIFontHelper.LoadForText(inputText.text, inputText.fontSize); // v0.1.1：输入英文指令 → JetBrains Mono
        input.textComponent = inputText;

        Text placeholder = CreateText("Placeholder", inputRt, "输入开发者指令，如 /level 1 1 / help", inputFontSize, UITheme.TextMuted, TextAnchor.MiddleLeft);
        Stretch(placeholder.rectTransform);
        placeholder.supportRichText = false;
        placeholder.font = UIFontHelper.LoadForText(placeholder.text, placeholder.fontSize); // v0.1.1：占位含中文 → Noto Sans CJK SC
        input.placeholder = placeholder;

        input.lineType = InputField.LineType.SingleLine;
        // 字符过滤只用于「开框那一帧不把 ` 吃成字符」，其余输入（含打开状态下按 ` ）全部放行
        input.onValidateInput = ValidateInputCharacter;
        input.onSubmit.AddListener(OnCommandSubmitted);
        inputField = input;
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private Text CreateText(string name, Transform parent, string content, int fontSizeValue, Color color, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSizeValue;
        text.color = color;
        text.alignment = anchor;
        text.font = UIFontHelper.LoadForText(content, fontSizeValue); // v0.1.1：按内容自动选字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）
        text.raycastTarget = false;
        return text;
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

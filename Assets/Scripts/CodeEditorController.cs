using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.CodeDom.Compiler;
using Microsoft.CSharp;

/// <summary>
/// 代码编辑器（炮台代码编辑器）：
///   - 按 Enter（Return / KeypadEnter）打开 / 关闭；
///     限制：教学进行中不弹；指令框打开时不弹；设置面板可见时先恢复再打开；
///   - 打开时：Time.timeScale = 0、释放鼠标、PlayerCameraController.SetExternalBlock(true) 占用视角输入，
///     关闭时按记录还原（与指令框联动逻辑一致）；
///   - UI 布局：顶部标题栏 + 最左侧行号列 + 主代码区（双层语法高亮：底层透明 InputField + 上层只读富文本 Text）+ 底部错误 / 状态区；
///   - C# 词法高亮：关键字青、字符串绿、数字粉、注释灰绿、默认白；富文本中 < 替换为全角 ＜ 并以 <size> 缩小至约半角一半；
///   - 编译执行：CSharpCodeProvider 运行时编译（模板包裹用户代码），错误行号映射回用户代码；
///     编译成功自动关闭编辑器并在每帧调用用户代码 Run(tower)；运行时异常捕获后停止并提示。
/// </summary>
public class CodeEditorController : MonoBehaviour
{
    [Header("按键")]
    [Tooltip("打开 / 切换编辑器（Enter）")]
    public KeyCode openKey = KeyCode.Return;
    [Tooltip("小键盘 Enter 等效键")]
    public KeyCode openKeyAlt = KeyCode.KeypadEnter;

    [Header("UI")]
    [Tooltip("标题字号")]
    public int titleFontSize = 20;
    [Tooltip("代码区字号")]
    public int codeFontSize = 20;
    [Tooltip("错误列表字号")]
    public int errorFontSize = 15;
    [Tooltip("状态栏字号")]
    public int statusFontSize = 14;
    [Tooltip("全屏背景色（#1E1E1E alpha 0.97）")]
    public Color bgColor = new Color(0.118f, 0.118f, 0.118f, 0.97f);
    [Tooltip("行号列底色（#1B1B1B）")]
    public Color lineBgColor = new Color(0.106f, 0.106f, 0.106f, 1f);
    [Tooltip("行号颜色（#858585）")]
    public Color lineNumberColor = new Color(0.522f, 0.522f, 0.522f, 1f);
    [Tooltip("错误文字颜色（红）")]
    public Color errorColor = new Color(1f, 0.45f, 0.45f, 1f);

    [Header("编辑体验")]
    [Tooltip("代码行高系数（Unity 等宽字体行高 ≈ 字号 × 1.2）")]
    public float lineHeightFactor = 1.2f;
    [Tooltip("原生光标闪烁间隔（秒，>0 表示闪烁）")]
    public float caretBlinkRate = 0.85f;

    // 引用
    private Canvas canvas;
    private GameObject editorRoot;
    private InputField inputField;
    private Text highlightText;
    private Text lineNumberText;
    private Text errorListText;
    private Text statusLineText;
    private Text inputText;              // 输入层文字（透明）
    private RectTransform lineViewportRt; // 行号列视口（RectMask2D 裁剪 + 背景）
    private RectTransform codeViewportRt; // 代码区视口（RectMask2D 裁剪 + 背景）
    private RectTransform lineRt;         // 行号内容（高度随行数增长，滚动偏移同步）
    private RectTransform codeRt;         // 代码内容（高度随行数增长，滚动偏移 y ≤ 0）
    private RectTransform vScrollTrackRt;  // 右侧竖滚动条轨道
    private RectTransform vScrollHandleRt; // 右侧竖滚动条滑块
    private RectTransform hScrollTrackRt;  // 底部横滚动条轨道
    private RectTransform hScrollHandleRt; // 底部横滚动条滑块
    private readonly List<string> undoList = new List<string>();  // 撤销栈（自实现，兜底原生）
    private readonly List<string> redoList = new List<string>();  // 重做栈
    private string lastCommittedText = "";
    private const int MaxUndoSteps = 100;
    private const int CodeFontSizeMin = 12;   // Ctrl+滚轮缩放的字体下限
    private const int CodeFontSizeMax = 40;   // Ctrl+滚轮缩放的字体上限
    private float codeContentHeight;          // 代码区内容高度缓存（滚动范围计算不依赖 rect 滞后值）
    private float codeContentWidth;           // 代码区内容宽度缓存（横向滚动范围，长行不换行时大于视口宽）
    private Vector2 lastViewportSize;         // 上次代码视口尺寸（窗口 / 编辑器尺寸变化时重算内容尺寸与滚动范围）
    private RectTransform enterPromptRt; // Level 3 进场「按 Enter 输入代码」提示
    private int lastCaretPos = -1;       // 上次光标位置（轮询检测移动 / 换行）
    // 拖拽选择越界抑制：指针仍在代码视口内时记录的最后一个合法选区位置
    private bool selectionDragInside;
    private int lastValidSelectionAnchor = -1;
    private int lastValidSelectionFocus = -1;
    private PlayerCameraController playerController;
    private CommandConsoleController commandConsole;
    private SettingsPanelController settingsPanel;
    private CodeTower tower;
    private MainMenuController mainMenu; // 主页引用（用于主页状态下屏蔽 Enter）

    // 开关状态
    private bool wasPausedBeforeOpen;
    private bool wasFrozenBeforeOpen;
    private bool defaultCodeFilled;
    private string lastStatus = "";
    private Color statusColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    // 运行时编译 runner
    private bool runnerActive;
    private System.Reflection.MethodInfo runnerMethod;
    private object runnerInstance;

    /// <summary>编辑器当前是否打开。</summary>
    public bool IsOpen => editorRoot != null && editorRoot.activeSelf;

    /// <summary>
    /// 主页是否正在显示（Home 面板可见）。主页状态下 Enter 一律屏蔽：不打开编辑器、不触发任何交互。
    /// 主页引用按需缓存（主页对象常驻，只需查找一次）。
    /// </summary>
    private bool IsMainMenuBlocking()
    {
        if (mainMenu == null)
            mainMenu = FindObjectOfType<MainMenuController>();
        return mainMenu != null && mainMenu.IsMenuVisible;
    }

    /// <summary>当前编辑器内的用户代码文本（编辑器未构建时返回空串）。代码语法教学（Level 1 - Part 2）据此判定任务。</summary>
    public string CurrentCode => inputField != null ? inputField.text : string.Empty;

    /// <summary>编译并成功启动 runner 时触发，参数为当次编译的用户代码快照（供教学等外部系统判定「编译运行成功」）。</summary>
    public static event System.Action<string> OnCompiledSuccessfully;

    /// <summary>
    /// 教学用：把编辑器内容设为指定代码（并标记默认示例已填过，避免首次打开时被示例代码覆盖）。
    /// 用于 Level 1 - Part 2 语法教学开局清空 / 放置代码骨架。
    /// </summary>
    public void SetCodeForTutorial(string code)
    {
        if (inputField == null)
            return;
        inputField.text = code ?? string.Empty;
        defaultCodeFilled = true;          // 覆盖默认示例填充分支
        lastCommittedText = inputField.text;
        undoList.Clear();
        redoList.Clear();
        RefreshCodeView();
        if (codeRt != null)
        {
            ApplyScrollY(0f);
            UpdateScrollBars();
        }
    }

    /// <summary>
    /// 回厂重置（/lock 指令调用）：把编辑器内容恢复为初始示例模板 DEFAULT_CODE，
    /// 并同步 lastCommittedText / defaultCodeFilled（与首次打开编辑器时的状态一致），
    /// 同时清空撤销 / 重做栈与光标记录。编辑器 UI 尚未构建时只复位 defaultCodeFilled，
    /// 下次 Open() 会自动填入默认模板。
    /// </summary>
    public void ResetCodeToDefault()
    {
        if (inputField == null)
        {
            defaultCodeFilled = false; // UI 未构建：下次 Open() 时自动填入 DEFAULT_CODE
            return;
        }

        inputField.text = DEFAULT_CODE;
        defaultCodeFilled = true;              // 内容已等于默认模板，Open() 不再重复覆盖
        lastCommittedText = inputField.text;   // 同步已提交文本，避免撤销 / 高亮错位
        undoList.Clear();
        redoList.Clear();
        lastCaretPos = -1;
        RefreshCodeView();
        if (codeRt != null)
        {
            ApplyScrollY(0f);
            UpdateScrollBars();
        }

        Debug.Log("[CodeEditor] 已恢复出厂设置：代码清回初始示例模板");
    }

    // ---------------- 高亮配色 ----------------
    private const string C_KEYWORD = "#4EC9B0";  // 关键字青
    private const string C_STRING = "#6A9955";   // 字符串绿
    private const string C_NUMBER = "#FF6B9D";   // 数字粉
    private const string C_COMMENT = "#57A64A";  // 注释灰绿
    private const string C_DEFAULT = "#D4D4D4";  // 默认白

    private static readonly HashSet<string> keywords = new HashSet<string>
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class",
        "const","continue","decimal","default","delegate","do","double","else","enum","event",
        "explicit","extern","false","finally","fixed","float","for","foreach","goto","if",
        "implicit","in","int","interface","internal","is","lock","long","namespace","new",
        "null","object","operator","out","override","params","private","protected","public",
        "readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static",
        "string","struct","switch","this","throw","true","try","typeof","uint","ulong",
        "unchecked","unsafe","ushort","using","var","virtual","void","volatile","while"
    };

    /// <summary>默认示例代码（首次打开填入，简洁演示三个新接口，不含每帧自动循环；输出走 +X 前方 print 荧幕，不写 Debug 日志）。</summary>
    private const string DEFAULT_CODE =
        "// 炮台接口示例：GetAllObjects / FireAtPlane / GetFireCooldown（简洁版，不自动循环）\n" +
        "// 输出走 print 荧幕（X 轴前方），不写 Debug 日志；print 支持 printf 风格格式化 {0} {1}\n" +
        "// 引擎每帧调用 Run(tower)；如需循环 / 限频，请自行编写 while / if 条件\n" +
        "// 注意：FireAtPlane(A,B,C) 的平面 Ax+By+Cz=0 必过原点，只需给出法向量 (A,B,C)\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== 炮台接口示例 ==\");\n" +
        "float cd = tower.GetFireCooldown();\n" +
        "tower.PrintLine(\"GetFireCooldown={0:F3}\", cd);\n" +
        "bool fired1 = tower.FireAtPlane(0f, 1f, 0f);   // 法线 (0,1,0)：平面过原点，沿 +Y 发射\n" +
        "tower.PrintLine(\"FireAtPlane fired={0}\", fired1);\n" +
        "var objects = tower.GetAllObjects();\n" +
        "tower.PrintLine(\"GetAllObjects count={0}\", objects.Count);\n" +
        "for (int i = 0; i < objects.Count && i < 5; i++)\n" +
        "    tower.PrintLine(\"obj[{0}] = #{1} {2} HP={3} pos={4} vel={5} [{6}]\",\n" +
        "        i, objects[i].Index, objects[i].Type, objects[i].HP,\n" +
        "        objects[i].Position, objects[i].Velocity, objects[i].Params);\n";

    private void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        editorRoot.SetActive(false);
    }

    private void Start()
    {
        playerController = FindObjectOfType<PlayerCameraController>();
        commandConsole = FindObjectOfType<CommandConsoleController>();
        settingsPanel = FindObjectOfType<SettingsPanelController>();
        tower = FindObjectOfType<CodeTower>();
        if (tower == null)
            Debug.LogWarning("[CodeEditor] 未找到 CodeTower，用户代码将无法驱动炮台。");
    }

    private void Update()
    {
        // Enter 开关：关闭时按 Enter 打开；打开时若输入框未聚焦则按 Enter 关闭（聚焦时 Enter 交给输入框换行，便于编写多行代码）
        if (!IsOpen)
        {
            // 刚被强制关闭（进入 Level 3 完整玩法关 / 关卡切换）时，先等这次 Enter 抬起，避免指令框里确认命令的那一下把编辑器又弹出来
            if (suppressOpenUntilRelease)
            {
                if (!Input.GetKey(openKey) && !Input.GetKey(openKeyAlt))
                    suppressOpenUntilRelease = false;
            }
            else if (Input.GetKeyDown(openKey) || Input.GetKeyDown(openKeyAlt))
            {
                // 主页（Home 面板可见）：Enter 完全无反应，既不打开编辑器也不做任何交互
                if (IsMainMenuBlocking())
                    return;
                Open();
                return;
            }
        }
        else
        {
            if ((Input.GetKeyDown(openKey) || Input.GetKeyDown(openKeyAlt)) && !IsInputFocused())
            {
                Close();
                return;
            }
            // ESC 退出代码编辑模式：统一由 EscapeRouter 判定后调用 CloseFromEscape()，本组件不再自行读取 ESC
            // F6：编译运行（与 Compile & Run 按钮同逻辑）
            if (Input.GetKeyDown(KeyCode.F6))
            {
                CompileAndRun();
                return;
            }
        }

        // 编辑器打开时：光标位置变化 → 刷新状态栏并自动滚动跟随；滚轮在代码视口内翻页
        if (IsOpen)
        {
            RefreshContentOnViewportResize(); // 窗口 / 编辑器尺寸变化后先重算内容尺寸与滚动范围
            int cp = inputField.caretPosition;
            int prevCaret = lastCaretPos; // 上一帧光标（方向键横向浏览：行尾 / 行首判断基准）
            if (cp != lastCaretPos)
            {
                lastCaretPos = cp;
                RefreshCursorStatus();
                UpdateScrollToCaret(); // 上下键跳行后滚动跟随当前行
            }
            HandleArrowHorizontalScroll(prevCaret); // → / ← 逐字符横向浏览超出视口右侧的代码
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlHeld)
                HandleFontZoom();  // Ctrl+滚轮：缩放代码字号（放大 / 缩小）
            else
                HandleWheelScroll(); // 滚轮翻页（自实现，不依赖 ScrollRect）
            // Ctrl+C/V/X 粘贴保障：输入框未聚焦时原生快捷键不生效，检测到 Ctrl 组合先自动聚焦
            if (!IsInputFocused())
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl && (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.V) ||
                             Input.GetKeyDown(KeyCode.X)))
                {
                    inputField.ActivateInputField();
                    inputField.Select();
                }
            }

            // Ctrl+Z / Ctrl+Y（撤销 / 重做）：自实现栈兜底，失焦屏蔽原生撤销防止双触发
            HandleUndoRedoShortcuts();

            UpdateScrollBars(); // 滑块常驻隐藏（防御性兜底：任何代码路径都不会让它重新显示）
        }

        // 用户代码每帧驱动炮台（编辑器关闭且游戏未暂停时）
        if (runnerActive && !IsOpen && Time.timeScale > 0f && runnerMethod != null && tower != null)
        {
            try
            {
                runnerMethod.Invoke(runnerInstance, new object[] { tower });
            }
            catch (System.Exception e)
            {
                StopRunner();
                System.Exception baseEx = e.GetBaseException();
                string msg = baseEx.Message;
                if (baseEx is System.TypeLoadException)
                    msg = Localization.Pick(
                        "用户程序集与当前脚本程序集不匹配（旧程序集残留或依赖缺失），已停止运行；请重新点击编译，或重启编辑器后重试。",
                        "User assembly mismatch with current scripts (stale assembly or missing dependency). Stopped; recompile or restart editor.");
                AppendError(Localization.Pick("运行时错误：", "Runtime error: ") + msg);
                Debug.LogError("[CodeEditor] 运行时错误：" + msg);
                Debug.LogException(e);
            }
        }
    }

    /// <summary>
    /// 拖拽选择越界抑制：按住鼠标拖选时，指针还在代码视口内就持续记录当前合法选区；
    /// 一旦指针被拖到代码区外（甚至整个编辑器外），立即把选区锚点 / 焦点回滚到最后一个合法位置，
    /// 使选中高亮始终只在代码区矩形内绘制，不再向外延伸盖住黑色底板、顶部标题栏与周边元素。
    /// 放在 LateUpdate 执行，确保在 InputField / EventSystem 处理完本帧拖拽事件之后再覆盖其结果。
    /// </summary>
    private void LateUpdate()
    {
        if (inputField == null || codeViewportRt == null || !IsOpen || !Input.GetMouseButton(0))
        {
            // 松开鼠标 / 编辑器关闭：结束本轮拖拽跟踪并清空记录
            selectionDragInside = false;
            lastValidSelectionAnchor = -1;
            lastValidSelectionFocus = -1;
            return;
        }

        if (RectTransformUtility.RectangleContainsScreenPoint(codeViewportRt, Input.mousePosition, null))
        {
            selectionDragInside = true;
            lastValidSelectionAnchor = inputField.selectionAnchorPosition;
            lastValidSelectionFocus = inputField.selectionFocusPosition;
            return;
        }

        if (!selectionDragInside || lastValidSelectionAnchor < 0 || lastValidSelectionFocus < 0)
            return;
        if (inputField.selectionAnchorPosition == lastValidSelectionAnchor &&
            inputField.selectionFocusPosition == lastValidSelectionFocus)
            return;

        // 指针已离开代码区：撤销越界的选区扩展，仅保留离开前的合法选区（不修改文本内容）
        inputField.selectionAnchorPosition = lastValidSelectionAnchor;
        inputField.selectionFocusPosition = lastValidSelectionFocus;
    }

    // ---------------- 开关逻辑（与 PlayerCameraController 联动） ----------------

    // 右侧为教学指导框预留的宽度（0 = 默认全宽代码区；当前上下分栏布局下恒为 0，保留接口兼容）
    private float rightReservedWidth;

    // 底部为教学指导框预留的高度（0 = 默认铺满全屏；Level 1-2 / Level 2-1 教学时设为指导框高度）
    private float bottomReservedHeight;

    // 编辑器整体根节点（用于上下分栏时收窄高度）
    private RectTransform editorRootRt;

    // 底部错误 / 状态区底板（全屏时为深色分隔带；上下分栏教学时与代码区同色，避免两块之间浮出黑色横带）
    private Image consoleBg;
    private static readonly Color consoleBgFullColor = new Color(0.09f, 0.09f, 0.09f, 1f);
    private static readonly Color consoleBgSplitColor = new Color(0.118f, 0.118f, 0.118f, 1f);

    // 强制关闭后忽略一次「Enter 打开」输入：避免跳关指令框里那一下 Enter 又在同帧 / 下一帧把编辑器打开
    private bool suppressOpenUntilRelease;

    /// <summary>
    /// 给底部教学指导框预留高度：编辑器整体上移收窄到上半区，指导框独占下半区，
    /// 「上代码 / 下指导」上下排布互不重叠；代码过长时编辑器内部竖向滚动条照常可用。传 0 即恢复默认全屏。
    /// </summary>
    public void SetBottomReservedHeight(float reservedHeight)
    {
        bottomReservedHeight = Mathf.Max(0f, reservedHeight);
        ApplyBottomReservedHeight();
    }

    /// <summary>按当前底部预留高度重排编辑器根节点（关闭时也保持，下次打开即为正确布局）。</summary>
    private void ApplyBottomReservedHeight()
    {
        if (editorRootRt == null)
            return;

        // 上下分栏时下边缘向指导框方向多探 1px：即使浮点取整，两块之间也不会露出任何缝隙 / 黑边
        float bottomInset = bottomReservedHeight > 0f ? bottomReservedHeight - 1f : 0f;
        editorRootRt.offsetMin = new Vector2(0f, bottomInset);
        editorRootRt.offsetMax = Vector2.zero;

        // 分栏模式：底部错误区底板改成与代码区同色，代码区与指导框之间不再出现一条深色「黑色横带」
        if (consoleBg != null)
            consoleBg.color = bottomReservedHeight > 0f ? consoleBgSplitColor : consoleBgFullColor;

        UpdateScrollBars(); // 视口高度变化后重算竖向滚动条的滑块长度与位置
    }

    /// <summary>
    /// 强制关闭代码编辑器（任意关卡切换 / 进入 Level 3 完整玩法关时使用）：关闭界面、恢复 timeScale = 1、
    /// 锁回鼠标、收起「按 Enter 输入代码」提示，并抑制紧随其后的同一次 Enter 再次打开编辑器。
    /// </summary>
    public static void ForceCloseIfOpen()
    {
        CodeEditorController editor = Object.FindObjectOfType<CodeEditorController>();
        if (editor == null)
            return;
        editor.ForceCloseSelf();
    }

    /// <summary>实例级强制收尾（供静态入口 / 教学结束调用）。</summary>
    public void ForceCloseSelf()
    {
        if (IsOpen)
            CloseAndForceResume();
        HideEnterPrompt();
        // 指令框 / 其它界面里确认命令的那一次 Enter 会在同帧或下一帧被当成「打开编辑器」，这里先屏蔽到按键抬起
        suppressOpenUntilRelease = true;
    }

    /// <summary>
    /// 给右侧教学指导框预留宽度：代码区（含行号列右侧的正文、竖/横滚动条）整体左移收窄，
    /// 使「左侧代码行、右侧指导框」互不重叠。传 0 即恢复默认全宽布局。
    /// </summary>
    public void SetRightReservedWidth(float reservedWidth)
    {
        rightReservedWidth = Mathf.Max(0f, reservedWidth);
        ApplyRightReservedWidth();
    }

    /// <summary>按当前预留宽度重排代码区与滚动条（关闭时也保持，下次打开即为正确布局）。</summary>
    private void ApplyRightReservedWidth()
    {
        if (codeViewportRt == null)
            return;

        float r = rightReservedWidth;
        codeViewportRt.offsetMax = new Vector2(-r, codeViewportRt.offsetMax.y);
        if (vScrollTrackRt != null)
            vScrollTrackRt.offsetMax = new Vector2(-(r + 6f), vScrollTrackRt.offsetMax.y);
        if (hScrollTrackRt != null)
            hScrollTrackRt.offsetMax = new Vector2(-(r + 16f), hScrollTrackRt.offsetMax.y);

        SyncContentWidth();  // 视口变窄后重算内容宽度（决定是否出现横向滚动）
        UpdateScrollBars();  // 同步两个滚动条的滑块长度与位置
    }

    /// <summary>打开编辑器：暂停时间、释放鼠标、占用视角输入；教学 / 指令框状态下忽略。</summary>
    public void Open()
    {
        if (IsOpen)
            return;

        // 限制：主页（Home 面板可见）状态下不打开（Enter 在主页完全无反应）
        if (IsMainMenuBlocking())
            return;

        // 限制：教学进行中不弹
        if (TutorialController.Instance != null && TutorialController.Instance.Started)
            return;
        // 限制：指令框打开时忽略
        if (commandConsole != null && commandConsole.IsOpen)
            return;
        // 设置面板可见：先恢复再打开（与指令框逻辑一致）
        if (settingsPanel != null && settingsPanel.IsPanelVisible)
        {
            if (playerController != null)
                playerController.ResumeExternal();
            settingsPanel.HidePanel();
        }

        if (playerController != null)
        {
            wasPausedBeforeOpen = (playerController.Mode == PlayerCameraController.GameMode.Paused);
            wasFrozenBeforeOpen = playerController.IsTimeFrozen;
            if (wasPausedBeforeOpen)
                playerController.ResumeExternal();
            playerController.SetExternalBlock(true);
        }

        editorRoot.SetActive(true);
        ApplyRightReservedWidth(); // 打开时按当前预留宽度重排（上下分栏布局下恒为 0）
        ApplyBottomReservedHeight(); // 打开时按当前底部指导框高度收窄整体高度（上代码 / 下指导，互不重叠）
        if (enterPromptRt != null)
            enterPromptRt.gameObject.SetActive(false); // 进场提示在打开编辑器后消失
        if (!defaultCodeFilled)
        {
            inputField.text = DEFAULT_CODE; // 首次打开填入示例代码
            defaultCodeFilled = true;
        }
        lastCommittedText = inputField.text;
        undoList.Clear();
        redoList.Clear();
        RefreshCodeView(); // 刷新高亮 / 行号 / 内容高度与滚动位置
        // 打开时强制滚到代码最上面（优先级高于鼠标行缩放锚点残留位置与光标行跟随）
        if (codeRt != null)
        {
            ApplyScrollY(0f);
            UpdateScrollBars();
        }
        inputField.ActivateInputField();
        inputField.Select();

        Time.timeScale = 0f;
        SetCursorLocked(false);
        AppendError("");
        SetStatus(Localization.Pick("编辑器已打开（Esc / ✕ 关闭）", "Editor opened (Esc or ✕ to close)"), false);
        Debug.Log("[CodeEditor] 编辑器打开");
    }

    /// <summary>关闭编辑器：恢复时间流速、锁定鼠标、解除视角输入占用；若打开前处于暂停则还原暂停。</summary>
    public void Close()
    {
        if (!IsOpen)
            return;

        editorRoot.SetActive(false);
        if (playerController != null)
            playerController.SetExternalBlock(false);

        if (wasPausedBeforeOpen && playerController != null)
        {
            playerController.PauseNow();
            wasPausedBeforeOpen = false;
            wasFrozenBeforeOpen = false;
        }
        else if (wasFrozenBeforeOpen)
        {
            SetCursorLocked(true);
            Time.timeScale = 0f;
            wasFrozenBeforeOpen = false;
        }
        else
        {
            Time.timeScale = 1f;
            SetCursorLocked(true);
        }

        // 关闭编辑器后短暂忽略暂停键：避免「关闭编辑器」的那一次 ESC 紧接着又被当成暂停输入，
        // 导致退出代码模式后游戏被暂停（应保持继续运行）
        if (playerController != null)
            playerController.SuppressPauseInput();
        else
            PlayerCameraController.SuppressPauseInputGlobal();

        Debug.Log("[CodeEditor] 编辑器关闭");
    }

    /// <summary>
    /// ESC 退出代码编辑模式：只关闭编辑器并让游戏继续运行（不还原「打开前的暂停状态」），
    /// 同时抑制本次 ESC 触发暂停，确保退出代码模式后游戏保持继续。
    /// </summary>
    public void CloseFromEscape()
    {
        wasPausedBeforeOpen = false; // ESC 退出后一律继续运行，不恢复暂停
        Close();
    }

    /// <summary>强制关闭编辑器并恢复正常游戏状态（编译运行成功后调用，忽略打开前的暂停 / 冻结状态）。</summary>
    public void CloseAndForceResume()
    {
        wasPausedBeforeOpen = false;
        wasFrozenBeforeOpen = false;
        Close();
    }

    /// <summary>Level 3 完整玩法关进场提示：时间已冻结，按下 Enter 输入代码（Open 时自动隐藏）。</summary>
    public void ShowEnterPrompt()
    {
        if (enterPromptRt != null)
            enterPromptRt.gameObject.SetActive(true);
    }

    /// <summary>隐藏进场「按 Enter 输入代码」提示（跳关 / 不需要该提示时调用，避免残留在屏幕上）。</summary>
    public void HideEnterPrompt()
    {
        if (enterPromptRt != null)
            enterPromptRt.gameObject.SetActive(false);
    }

    // ---------------- 编译运行 ----------------

    /// <summary>编译运行：CSharpCodeProvider 运行时编译用户代码，成功则启动 runner 并强制关闭编辑器。</summary>
    private void CompileAndRun()
    {
        if (tower == null)
        {
            AppendError(Localization.Pick("错误：未找到原点炮台 CodeTower。", "Error: CodeTower not found."));
            return;
        }

        string userCode = inputField.text;
        if (string.IsNullOrEmpty(userCode.Trim()))
        {
            AppendError(Localization.Pick("错误：代码为空。", "Error: code is empty."));
            return;
        }

        string fullSource = WrapUserCode(userCode, out string className, out int userCodeStartLine);

        StopRunner(); // 编译前停掉旧 runner，避免重复运行

        try
        {
            using (CSharpCodeProvider provider = new CSharpCodeProvider())
            {
                CompilerParameters parameters = new CompilerParameters();
                parameters.GenerateInMemory = true;
                parameters.GenerateExecutable = false;
                AddReferencedAssembly(parameters, typeof(GameObject).Assembly.Location);
                AddReferencedAssembly(parameters, typeof(UnityEngine.UI.Text).Assembly.Location);
                AddReferencedAssembly(parameters, typeof(CodeTower).Assembly.Location);
                AddReferencedAssembly(parameters, typeof(UIFontHelper).Assembly.Location);
                // 引用 netstandard 2.1（优先编辑器 Mono 运行时 Facades 版，与运行时同源同版本，类型转发不产生 CS0433 二义性；
                // 回退 NetStandard 参考程序集）。用户代码用到 System.Object / 泛型集合等类型时需显式引用，否则报 CS0012。
                // 实测：无论 facade 还是 ref，编译产物均不依赖 netstandard（统一引用 mscorlib），不会引入运行时加载失败。
                string netStandardPath = ResolveNetStandardPath();
                if (netStandardPath != null)
                    AddReferencedAssembly(parameters, netStandardPath);
                else
                    Debug.LogWarning("[CodeEditor] 未找到 netstandard.dll，用户代码可能报 CS0012。");

                CompilerResults results = provider.CompileAssemblyFromSource(parameters, fullSource);

                if (results == null)
                {
                    AppendError(Localization.Pick("编译失败：未返回结果。", "Compile failed: no result."));
                    return;
                }

                if (results.Errors != null && results.Errors.HasErrors)
                {
                    // 编译错误：行号映射回用户代码（userLine = 错误行 - USER CODE START 标记所在模板行号）
                    StringBuilder sb = new StringBuilder();
                    int shown = 0;
                    for (int i = 0; i < results.Errors.Count; i++)
                    {
                        CompilerError e = results.Errors[i];
                        if (e.IsWarning)
                            continue;
                        int userLine = e.Line - userCodeStartLine;
                        // mcs/csc 的 ErrorNumber 自带 "CS" 前缀，这里统一掉，避免显示成 CSCS0103
                        string errNo = e.ErrorNumber ?? string.Empty;
                        if (errNo.StartsWith("CS", System.StringComparison.OrdinalIgnoreCase))
                            errNo = errNo.Substring(2);
                        Debug.LogError("[CodeEditor] 编译错误 模板行 " + e.Line + " / 用户代码第 " + userLine + " 行,第 " + e.Column + " 列：CS" + errNo + " " + e.ErrorText);
                        sb.Append(Localization.Pick("第 ", "L"))
                          .Append(userLine)
                          .Append(Localization.Pick(" 行,第 ", ", C"))
                          .Append(e.Column)
                          .Append(Localization.Pick(" 列：", ": "))
                          .Append("CS").Append(errNo).Append(' ').Append(e.ErrorText)
                          .Append('\n');
                        shown++;
                        if (shown >= 20)
                            break;
                    }
                    AppendError(sb.ToString().TrimEnd('\n'));
                    return;
                }

                System.Reflection.Assembly asm = results.CompiledAssembly;
                if (asm == null)
                {
                    AppendError(Localization.Pick("编译失败：未生成程序集。", "Compile failed: no assembly."));
                    return;
                }

                System.Type type = asm.GetType(className);
                if (type == null)
                {
                    // 兜底：搜索包含 Run 方法的导出类型
                    System.Type[] types = asm.GetExportedTypes();
                    for (int i = 0; i < types.Length; i++)
                    {
                        if (types[i].GetMethod("Run") != null)
                        {
                            type = types[i];
                            break;
                        }
                    }
                }
                if (type == null)
                {
                    AppendError(Localization.Pick("编译失败：找不到用户代码类型。", "Compile failed: user type not found."));
                    return;
                }

                runnerInstance = System.Activator.CreateInstance(type);
                runnerMethod = type.GetMethod("Run");
                if (runnerMethod == null)
                {
                    AppendError(Localization.Pick("编译失败：找不到 Run 方法。", "Compile failed: Run method not found."));
                    return;
                }

                runnerActive = true;
                SetStatus(Localization.Pick("编译成功，已启动运行（停止按钮可停止）", "Compiled OK, runner started (Stop to stop)"), false);
                AppendError("");
                // 教学判定钩子：编译并成功启动运行（Level 1 - Part 2 语法教学最后一项任务据此判定）
                if (OnCompiledSuccessfully != null)
                    OnCompiledSuccessfully(userCode);
                CloseAndForceResume(); // 编译成功后强制恢复游戏状态，回到正常玩法
            }
        }
        catch (System.Exception e)
        {
            AppendError(Localization.Pick("编译异常：", "Compile exception: ") + e.GetBaseException().Message);
            Debug.LogError("[CodeEditor] 编译异常：" + e.GetBaseException().Message);
            Debug.LogException(e);
        }
    }

    /// <summary>停用 runner（用户代码不再执行）。</summary>
    private void StopRunner()
    {
        runnerActive = false;
        runnerMethod = null;
        runnerInstance = null;
        if (IsOpen)
            SetStatus(Localization.Pick("已停止运行", "Stopped"), false);
    }

    /// <summary>用模板包裹用户代码，返回完整源码，并输出类名与 USER CODE START 标记所在模板行号（1-based）。</summary>
    private static string WrapUserCode(string userCode, out string className, out int userCodeStartLine)
    {
        StringBuilder idSb = new StringBuilder(8);
        const string hexChars = "0123456789abcdef";
        for (int i = 0; i < 8; i++)
            idSb.Append(hexChars[Random.Range(0, hexChars.Length)]);
        className = "UserTowerCode_" + idSb.ToString();

        string template =
            "using UnityEngine;\n" +
            "using System;\n" +
            "using System.Collections;\n" +
            "using System.Collections.Generic;\n" +
            "\n" +
            "public class " + className + "\n" +
            "{\n" +
            "    public void Run(CodeTower tower)\n" +
            "    {\n" +
            "// ===== USER CODE START =====\n" +
            userCode + "\n" +
            "// ===== USER CODE END =====\n" +
            "    }\n" +
            "}\n";

        userCodeStartLine = 10;
        string[] lines = template.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("USER CODE START"))
            {
                userCodeStartLine = i + 1;
                break;
            }
        }
        return template;
    }

    /// <summary>解析 netstandard.dll 编译引用路径：优先编辑器 Mono 运行时 Facades（与运行时同源同版本、类型转发不产生 CS0433），
    /// 其次 NetStandard 参考程序集；全部不存在返回 null。
    /// 编辑器安装根目录经 EditorApplication.applicationContentsPath 动态取得（反射读取，避免运行时程序集依赖 UnityEditor）。</summary>
    private static string ResolveNetStandardPath()
    {
        string editorContents = null;
        try
        {
            System.Type editorApplicationType = System.Type.GetType("UnityEditor.EditorApplication, UnityEditor");
            if (editorApplicationType != null)
            {
                System.Reflection.PropertyInfo contentsProperty = editorApplicationType.GetProperty("applicationContentsPath");
                if (contentsProperty != null)
                    editorContents = contentsProperty.GetValue(null, null) as string;
            }
        }
        catch (System.Exception)
        {
            editorContents = null;
        }

        string[] candidates;
        if (string.IsNullOrEmpty(editorContents))
        {
            candidates = new string[0];
        }
        else
        {
            string monoRoot = System.IO.Path.Combine(editorContents, "MonoBleedingEdge" + System.IO.Path.DirectorySeparatorChar + "lib" + System.IO.Path.DirectorySeparatorChar + "mono");
            candidates = new string[]
            {
                System.IO.Path.Combine(monoRoot, "unityjit-win32" + System.IO.Path.DirectorySeparatorChar + "Facades" + System.IO.Path.DirectorySeparatorChar + "netstandard.dll"),
                System.IO.Path.Combine(monoRoot, "net_4_x-win32" + System.IO.Path.DirectorySeparatorChar + "Facades" + System.IO.Path.DirectorySeparatorChar + "netstandard.dll"),
                System.IO.Path.Combine(monoRoot, "4.8-api" + System.IO.Path.DirectorySeparatorChar + "Facades" + System.IO.Path.DirectorySeparatorChar + "netstandard.dll"),
                System.IO.Path.Combine(editorContents, "NetStandard" + System.IO.Path.DirectorySeparatorChar + "ref" + System.IO.Path.DirectorySeparatorChar + "2.1.0" + System.IO.Path.DirectorySeparatorChar + "netstandard.dll"),
            };
        }
        for (int i = 0; i < candidates.Length; i++)
        {
            if (System.IO.File.Exists(candidates[i]))
                return candidates[i];
        }
        return null;
    }

    /// <summary>添加编译引用程序集（去重；Location 为空或文件不存在则跳过并告警）。</summary>
    private static void AddReferencedAssembly(CompilerParameters parameters, string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            Debug.LogWarning("[CodeEditor] 引用程序集位置为空，已跳过。");
            return;
        }
        if (!System.IO.File.Exists(location))
        {
            Debug.LogWarning("[CodeEditor] 引用程序集文件不存在，已跳过：" + location);
            return;
        }
        if (!parameters.ReferencedAssemblies.Contains(location))
            parameters.ReferencedAssemblies.Add(location);
    }

    // ---------------- C# 词法高亮 ----------------

    /// <summary>将 C# 代码转为 UGUI 富文本（彩色语法高亮）；所有 < 替换为全角 ＜ 防误吞标签，并包 <size> 标签把显示字号缩至约半角一半。</summary>
    private string HighlightCSharp(string code)
    {
        int smallSize = Mathf.Max(2, codeFontSize / 2); // 全角 ＜ 显示字号 ≈ 普通半角一半（随缩放联动）
        StringBuilder sb = new StringBuilder(code.Length + 256);
        int i = 0;
        int n = code.Length;
        while (i < n)
        {
            char c = code[i];

            // 行注释 //
            if (c == '/' && i + 1 < n && code[i + 1] == '/')
            {
                int start = i;
                i += 2;
                while (i < n && code[i] != '\n') i++;
                AppendToken(sb, code.Substring(start, i - start), C_COMMENT, smallSize);
                continue;
            }

            // 块注释 /* */
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i < n && !(code[i] == '*' && i + 1 < n && code[i + 1] == '/')) i++;
                if (i < n) i += 2;
                AppendToken(sb, code.Substring(start, Mathf.Min(i, n) - start), C_COMMENT, smallSize);
                continue;
            }

            // 字符串 "..."（含转义 \" \\）
            if (c == '"')
            {
                int start = i;
                i++;
                while (i < n)
                {
                    if (code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (code[i] == '"') { i++; break; }
                    i++;
                }
                AppendToken(sb, code.Substring(start, i - start), C_STRING, smallSize);
                continue;
            }

            // 字符 '...'（含转义）
            if (c == '\'')
            {
                int start = i;
                i++;
                while (i < n)
                {
                    if (code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (code[i] == '\'') { i++; break; }
                    i++;
                }
                AppendToken(sb, code.Substring(start, i - start), C_STRING, smallSize);
                continue;
            }

            // 标识符 / 关键字
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                string word = code.Substring(start, i - start);
                AppendToken(sb, word, keywords.Contains(word) ? C_KEYWORD : C_DEFAULT, smallSize);
                continue;
            }

            // 数字（含 0.5f、1e3、0xFF、后缀 f/d/m/l/u）
            if (char.IsDigit(c))
            {
                int start = i;
                bool isHex = (c == '0' && i + 1 < n && (code[i + 1] == 'x' || code[i + 1] == 'X'));
                i++;
                if (isHex)
                {
                    while (i < n && (char.IsDigit(code[i]) || (code[i] >= 'a' && code[i] <= 'f') ||
                           (code[i] >= 'A' && code[i] <= 'F'))) i++;
                    while (i < n && (code[i] == 'u' || code[i] == 'U' || code[i] == 'l' || code[i] == 'L')) i++;
                }
                else
                {
                    while (i < n && (char.IsDigit(code[i]) || code[i] == '.')) i++;
                    // 指数 e/E[+-]digits
                    if (i < n && (code[i] == 'e' || code[i] == 'E'))
                    {
                        int expPos = i;
                        int j = i + 1;
                        if (j < n && (code[j] == '+' || code[j] == '-')) j++;
                        int digitStart = j;
                        while (j < n && char.IsDigit(code[j])) j++;
                        if (j > digitStart) i = j;
                        else i = expPos;
                    }
                    // 数值后缀
                    while (i < n && (code[i] == 'f' || code[i] == 'F' || code[i] == 'd' || code[i] == 'D' ||
                           code[i] == 'm' || code[i] == 'M' || code[i] == 'l' || code[i] == 'L' ||
                           code[i] == 'u' || code[i] == 'U')) i++;
                }
                AppendToken(sb, code.Substring(start, i - start), C_NUMBER, smallSize);
                continue;
            }

            // 其它字符原样输出（< 替换为全角）
            AppendRaw(sb, c, smallSize);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>追加带颜色标签的 token；< 替换为全角 ＜ 并包 <size> 缩小至约半角一半（防止 UGUI 富文本误吞且保持等宽观感）。</summary>
    private static void AppendToken(StringBuilder sb, string token, string color, int smallSize)
    {
        sb.Append("<color=").Append(color).Append('>');
        for (int i = 0; i < token.Length; i++)
        {
            if (token[i] == '<')
                sb.Append("<size=").Append(smallSize).Append(">＜</size>");
            else
                sb.Append(token[i]);
        }
        sb.Append("</color>");
    }

    private static void AppendRaw(StringBuilder sb, char c, int smallSize)
    {
        if (c == '<')
            sb.Append("<size=").Append(smallSize).Append(">＜</size>");
        else
            sb.Append(c);
    }

    // ---------------- 视图刷新 ----------------

    private void RefreshCodeView()
    {
        if (inputField == null || highlightText == null)
            return;
        string code = inputField.text;
        highlightText.text = HighlightCSharp(code);
        RefreshLineNumbers(code);
        RefreshCursorStatus();
        SyncContentHeightAndScroll(); // 内容高度随行数更新并重新钳制滚动范围
        UpdateScrollToCaret();         // 光标行跟随（滚动后保证当前行可见）
    }

    // ---------------- 滚动（自实现，编辑器暂停 timeScale=0 时 ScrollRect 不可用） ----------------

    /// <summary>统计文本行数（以 '\n' 计数，空文本视为 1 行）。</summary>
    private static int CountLines(string text)
    {
        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }
        return count;
    }

    /// <summary>当前滚动允许的最大 y（≥0）：内容高 - 视口高；内容不足视口时恒 0。y 正值 = 内容上移显示下方。</summary>
    private float GetScrollMaxY()
    {
        if (codeViewportRt == null)
            return 0f;
        return Mathf.Max(0f, codeContentHeight - codeViewportRt.rect.height);
    }

    /// <summary>当前滚动允许的最大 x（≥0）：内容宽 - 视口宽；内容不足视口时恒 0。x 正值 = 向右滚动查看长行右侧。</summary>
    private float GetScrollMaxX()
    {
        if (codeViewportRt == null)
            return 0f;
        return Mathf.Max(0f, codeContentWidth - codeViewportRt.rect.width);
    }

    /// <summary>同步应用横向滚动偏移：scrollX ≥ 0 时内容左移（codeRt.anchoredPosition.x = -scrollX），显示长行右侧。</summary>
    private void ApplyScrollX(float scrollX)
    {
        if (codeRt != null)
            codeRt.anchoredPosition = new Vector2(-scrollX, codeRt.anchoredPosition.y);
    }

    /// <summary>仅同步内容宽度：长行不换行时取高亮层最长行宽（+左右内边距 24），不足视口宽则撑满视口。</summary>
    private void SyncContentWidth()
    {
        if (highlightText == null || codeRt == null || codeViewportRt == null)
            return;
        float viewW = codeViewportRt.rect.width;
        float pw = 0f;
        try { pw = highlightText.preferredWidth; }
        catch { pw = 0f; }
        if (pw < 0f || float.IsNaN(pw) || float.IsInfinity(pw))
            pw = 0f;
        float contentW = Mathf.Max(viewW, pw + 24f); // 文字左右内边距各 12
        codeContentWidth = contentW;
        codeRt.sizeDelta = new Vector2(contentW, codeRt.sizeDelta.y);
    }

    /// <summary>
    /// 滚动滑块常驻隐藏：竖向 / 横向滑块在任何窗口尺寸、任何内容长度下都不显示（需求：永不出现滚动滑块）。
    /// 滑块是叠加在视口上的绝对定位节点（无 LayoutGroup），隐藏后不占布局、不挤压视口宽度，也不留空白占位。
    /// 滚动能力本身保留：滚轮翻页 HandleWheelScroll、Shift+滚轮横向、方向键横向浏览、透明轨道点击跳转，
    /// 依靠 RectMask2D 裁剪的视口 + 内容偏移实现，不依赖滑块存在。
    /// 用 activeSelf 判断后再 SetActive，避免每帧重复触发 Canvas 重建。
    /// </summary>
    private void UpdateScrollBars()
    {
        if (vScrollHandleRt != null && vScrollHandleRt.gameObject.activeSelf)
            vScrollHandleRt.gameObject.SetActive(false);
        if (hScrollHandleRt != null && hScrollHandleRt.gameObject.activeSelf)
            hScrollHandleRt.gameObject.SetActive(false);
    }

    /// <summary>滚动条拖动 / 轨道点击回调：axis 0=竖 1=横；t 为归一化 0~1 滚动进度。</summary>
    private void SetScrollFromBar(int axis, float t)
    {
        if (codeRt == null || codeViewportRt == null)
            return;
        t = Mathf.Clamp01(t);
        if (axis == 0)
            ApplyScrollY(t * GetScrollMaxY());
        else
            ApplyScrollX(t * GetScrollMaxX());
        UpdateScrollBars(); // 拖动中实时跟随
    }

    /// <summary>同步应用滚动偏移到代码区与行号列。</summary>
    private void ApplyScrollY(float scrollY)
    {
        if (codeRt != null)
            codeRt.anchoredPosition = new Vector2(codeRt.anchoredPosition.x, scrollY);
        if (lineRt != null)
            lineRt.anchoredPosition = new Vector2(lineRt.anchoredPosition.x, scrollY);
    }

    /// <summary>仅同步内容高度：codeContentHeight = max(视口高, 行数×行高+16)，并同步 codeRt / lineRt 高度。</summary>
    private void SyncContentHeight()
    {
        if (inputField == null || codeRt == null || lineRt == null || codeViewportRt == null)
            return;
        float lineHeight = Mathf.Max(1f, codeFontSize * lineHeightFactor);
        float viewH = codeViewportRt.rect.height;
        float contentH = Mathf.Max(viewH, CountLines(inputField.text) * lineHeight + 16f);
        codeContentHeight = contentH;
        codeRt.sizeDelta = new Vector2(codeRt.sizeDelta.x, contentH);
        lineRt.sizeDelta = new Vector2(lineRt.sizeDelta.x, contentH);
    }

    /// <summary>内容高度随代码行数动态更新，并将当前滚动偏移重新钳制到有效范围（含横向）。</summary>
    private void SyncContentHeightAndScroll()
    {
        SyncContentHeight();
        SyncContentWidth();
        if (codeRt == null)
            return;
        float maxY = Mathf.Max(0f, codeContentHeight - codeViewportRt.rect.height);
        float scrollY = Mathf.Clamp(codeRt.anchoredPosition.y, 0f, maxY);
        ApplyScrollY(scrollY);
        float maxX = GetScrollMaxX();
        float scrollX = Mathf.Clamp(-codeRt.anchoredPosition.x, 0f, maxX);
        ApplyScrollX(scrollX);
        UpdateScrollBars();
    }

    /// <summary>
    /// 代码视口尺寸变化（拖拽 Game 视图 / 窗口缩放 / 全屏切换 / 教学关上下分栏改变编辑器高度）时重算内容尺寸：
    /// codeContentHeight 只在输入 / 滚轮等事件里同步，若不做尺寸检测，缩小窗口后该缓存仍是旧视口的大值，
    /// 与新的更小视口相比得出「内容溢出」的假象——滚动范围被人为放大，滚动后只能看到空白区域。
    /// 每帧仅比较两个 float，无分配、无重建。
    /// </summary>
    private void RefreshContentOnViewportResize()
    {
        if (codeViewportRt == null)
            return;
        Vector2 size = codeViewportRt.rect.size;
        if (Mathf.Abs(size.x - lastViewportSize.x) < 0.5f && Mathf.Abs(size.y - lastViewportSize.y) < 0.5f)
            return;
        lastViewportSize = size;
        SyncContentHeightAndScroll(); // 按新视口重算内容高宽，并把滚动偏移钳制回有效范围
    }

    /// <summary>滚轮翻页：鼠标在代码视口内时，按约 2 倍行高滚动；向上内容下移显示上方，向下内容上移显示下方。</summary>
    private void HandleWheelScroll()
    {
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f || codeViewportRt == null || codeRt == null)
            return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(codeViewportRt, Input.mousePosition, null))
            return;
        SyncContentHeight(); // 滚动前强制同步内容高度，避免 contentH 未更新导致滚动范围恒为 0（内容翻不下去）
        float lineHeight = Mathf.Max(1f, codeFontSize * lineHeightFactor);
        // Shift+滚轮 = 横向滚动（长行超出视口时向右 / 向左查看）
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (shift)
        {
            SyncContentWidth();
            float scrollX = Mathf.Clamp(-codeRt.anchoredPosition.x - wheel * lineHeight * 2f, 0f, GetScrollMaxX());
            ApplyScrollX(scrollX);
            UpdateScrollBars();
            return;
        }
        float scrollY = Mathf.Clamp(codeRt.anchoredPosition.y - wheel * lineHeight * 2f, 0f, GetScrollMaxY());
        ApplyScrollY(scrollY);
        UpdateScrollBars();
    }

    /// <summary>
    /// 方向键横向浏览长行：光标已在行尾时按 → 逐字符向右滚动，光标在行首时按 ← 逐字符向左滚动。
    /// 用上一帧光标判断（InputField 可能已把光标移走），滚动后把光标拉回行尾 / 行首，保证连续按键连续滚动。
    /// </summary>
    private void HandleArrowHorizontalScroll(int prevCaret)
    {
        if (codeRt == null || codeViewportRt == null || inputField == null || !IsInputFocused())
            return;
        string text = inputField.text;
        int len = text.Length;
        float step = Mathf.Max(1f, codeFontSize * 0.6f); // 一个等宽字符的近似宽度
        bool right = Input.GetKeyDown(KeyCode.RightArrow);
        bool left = Input.GetKeyDown(KeyCode.LeftArrow);
        if (!right && !left)
            return;

        if (right)
        {
            // 上一帧光标在行尾（指向换行符或文件末尾）：向右滚动并拉回光标到行尾可见字符
            if (prevCaret >= len || (prevCaret < len && text[prevCaret] == '\n'))
            {
                float sx = Mathf.Clamp(-codeRt.anchoredPosition.x + step, 0f, GetScrollMaxX());
                ApplyScrollX(sx);
                if (prevCaret > 0)
                    SetCaretSilent(prevCaret - 1);
            }
        }
        else
        {
            // 上一帧光标在行首（文件开头或行首字符前）：向左滚动并拉回光标到本行行首
            if (prevCaret == 0 || (prevCaret > 0 && text[prevCaret - 1] == '\n'))
            {
                float sx = Mathf.Clamp(-codeRt.anchoredPosition.x - step, 0f, GetScrollMaxX());
                ApplyScrollX(sx);
                SetCaretSilent(prevCaret);
            }
        }
        UpdateScrollBars();
    }

    /// <summary>静默设置光标与选区索引（不改变文本，仅定位，避免 InputField 把光标带离行首 / 行尾）。</summary>
    private void SetCaretSilent(int pos)
    {
        if (inputField == null)
            return;
        pos = Mathf.Max(0, Mathf.Min(pos, inputField.text.Length));
        inputField.caretPosition = pos;
        inputField.selectionAnchorPosition = pos;
        inputField.selectionFocusPosition = pos;
    }

    /// <summary>Ctrl+滚轮：代码字号缩放（放大 / 缩小），鼠标需在代码视口内；缩放后代码视图最上方吸附到最上端（优先于旧鼠标行锚点逻辑）。</summary>
    private void HandleFontZoom()
    {
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f || codeViewportRt == null || codeRt == null || inputField == null)
            return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(codeViewportRt, Input.mousePosition, null))
            return;
        int newSize = Mathf.Clamp(codeFontSize + (wheel > 0f ? 2 : -2), CodeFontSizeMin, CodeFontSizeMax);
        if (newSize == codeFontSize)
            return;

        codeFontSize = newSize;
        if (inputText != null)
            inputText.fontSize = codeFontSize;
        if (highlightText != null)
            highlightText.fontSize = codeFontSize;
        if (lineNumberText != null)
            lineNumberText.fontSize = codeFontSize;
        RefreshCodeView(); // 重算高亮 / 行号 / 内容高度与滚动范围

        // 缩放（尤其缩小）后：代码视图最上方吸附到最上端，优先级高于旧的“锚定鼠标所在行/行内比例”逻辑。
        // 旧逻辑在缩小后保留鼠标行原位（允许负滚动露出顶部留白），现统一吸附顶部，避免内容上端悬空。
        ApplyScrollY(0f);
        // 水平方向：放大时左侧保持不动（横向偏移不变，右侧内容随字号扩展出去），
        // 超出的部分用 → 键逐字符或 Shift+滚轮访问；RefreshCodeView 已保留横向偏移并钳制。
        UpdateScrollBars();

        SetStatus(Localization.Pick("字号已调整：" + codeFontSize, "Font size: " + codeFontSize), false);
    }

    /// <summary>
    /// 光标行跟随滚动（上下键 / 点击 / 文本变化后调用）：
    /// 行顶 y = -8 - 行号×行高；若行底低于视口底（&lt;-视口高）则滚动到行底对齐视口底，
    /// 若行顶高于视口顶（&gt;0）则滚动到行顶对齐视口顶；均在 clamp 后应用，并同步行号列。
    /// </summary>
    private void UpdateScrollToCaret()
    {
        if (inputField == null || codeRt == null || codeViewportRt == null)
            return;
        string text = inputField.text;
        int pos = Mathf.Clamp(inputField.caretPosition, 0, text.Length);
        int lineIndex = 0;
        for (int i = 0; i < pos; i++)
        {
            if (text[i] == '\n')
                lineIndex++;
        }
        float lineHeight = Mathf.Max(1f, codeFontSize * lineHeightFactor);
        float viewH = codeViewportRt.rect.height;
        float rowTop = -8f - lineIndex * lineHeight;
        float rowBottom = rowTop - lineHeight;
        float scrollY = codeRt.anchoredPosition.y;
        float vTop = scrollY + rowTop;
        float vBottom = scrollY + rowBottom;
        if (vBottom < -viewH)
            scrollY = -viewH - rowBottom; // 行底对齐视口底
        else if (vTop > 0f)
            scrollY = -rowTop;            // 行顶对齐视口顶
        scrollY = Mathf.Clamp(scrollY, 0f, GetScrollMaxY());
        ApplyScrollY(scrollY);
    }

    /// <summary>记录一次文本变化前的快照（onValueChanged 触发；SetTextWithoutNotify 不触发，需手动同步）。</summary>
    private void RecordSnapshotForUndo()
    {
        string t = inputField.text;
        if (t == lastCommittedText)
            return;
        undoList.Add(lastCommittedText);
        if (undoList.Count > MaxUndoSteps)
            undoList.RemoveAt(0);
        lastCommittedText = t;
        redoList.Clear();
    }

    /// <summary>Ctrl+Z / Ctrl+Y 快捷撤销重做：先失焦屏蔽 InputField 原生撤销，避免双重触发。</summary>
    private void HandleUndoRedoShortcuts()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!ctrl)
            return;
        if (Input.GetKeyDown(KeyCode.Z))
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift)
                RedoSnapshot();
            else
                UndoSnapshot();
        }
        else if (Input.GetKeyDown(KeyCode.Y))
        {
            RedoSnapshot();
        }
    }

    private void UndoSnapshot()
    {
        if (undoList.Count == 0)
        {
            SetStatus(Localization.Pick("没有可撤销的操作", "Nothing to undo"), false);
            return;
        }
        if (inputField.isFocused)
            inputField.DeactivateInputField(); // 失焦屏蔽原生 Ctrl+Z 撤销
        string current = inputField.text;
        string prev = undoList[undoList.Count - 1];
        undoList.RemoveAt(undoList.Count - 1);
        redoList.Add(current);
        lastCommittedText = prev;
        inputField.SetTextWithoutNotify(prev);
        inputField.caretPosition = prev.Length;
        inputField.ActivateInputField();
        inputField.Select();
        RefreshCodeView();
        SetStatus(Localization.Pick("已撤销", "Undone"), false);
    }

    private void RedoSnapshot()
    {
        if (redoList.Count == 0)
        {
            SetStatus(Localization.Pick("没有可重做的操作", "Nothing to redo"), false);
            return;
        }
        if (inputField.isFocused)
            inputField.DeactivateInputField();
        string current = inputField.text;
        string next = redoList[redoList.Count - 1];
        redoList.RemoveAt(redoList.Count - 1);
        undoList.Add(current);
        lastCommittedText = next;
        inputField.SetTextWithoutNotify(next);
        inputField.caretPosition = next.Length;
        inputField.ActivateInputField();
        inputField.Select();
        RefreshCodeView();
        SetStatus(Localization.Pick("已重做", "Redone"), false);
    }

    private void RefreshLineNumbers(string code)
    {
        int count = CountLines(code);
        StringBuilder sb = new StringBuilder(count * 4);
        for (int i = 1; i <= count; i++)
        {
            sb.Append(i);
            if (i < count)
                sb.Append('\n');
        }
        lineNumberText.text = sb.ToString();
    }

    private void RefreshCursorStatus()
    {
        string text = inputField.text;
        int pos = Mathf.Clamp(inputField.caretPosition, 0, text.Length);
        int line = 1, col = 1;
        for (int i = 0; i < pos; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
                col++;
        }
        statusLineText.text = "语言 C#    行:" + line + "  列:" + col + "    " + lastStatus;
        statusLineText.color = statusColor;
        statusLineText.font = UIFontHelper.LoadForText(statusLineText.text, statusLineText.fontSize);
    }

    private void SetStatus(string message, bool isError)
    {
        lastStatus = message;
        statusColor = isError ? errorColor : new Color(0.75f, 0.75f, 0.75f, 1f);
        if (IsOpen)
            RefreshCursorStatus();
    }

    private void AppendError(string text)
    {
        if (errorListText == null)
            return;
        errorListText.text = text;
        errorListText.font = UIFontHelper.LoadForText(text, errorListText.fontSize);
        if (!string.IsNullOrEmpty(text))
            SetStatus(Localization.Pick("编译失败 / 运行错误", "Compile / runtime error"), true);
    }

    private bool IsInputFocused()
    {
        if (inputField == null)
            return false;
        if (inputField.isFocused)
            return true; // InputField 自身聚焦状态优先（EventSystem 选中项可能滞后 / 不同步）
        EventSystem es = EventSystem.current;
        return es != null && es.currentSelectedGameObject == inputField.gameObject;
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
        // 编辑器为全屏模态面板：排序值取 200，高于 HUD(100) / 教程提示(60) / 指令框(0)，
        // 保证打开时标题栏与顶部工具条永远压在其他 Canvas 之上，不被计分 HUD 等元素遮挡。
        canvas.sortingOrder = 200;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        // ---- 全屏背景 ----
        RectTransform rootRt = CreateRect("CodeEditor", canvas.transform, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        editorRoot = rootRt.gameObject;
        editorRootRt = rootRt; // 上下分栏时按底部指导框高度收窄编辑器整体高度
        Image bg = editorRoot.AddComponent<Image>();
        bg.color = bgColor;

        // ---- 顶部标题栏 ----
        RectTransform titleBarRt = CreateRect("TitleBar", editorRoot.transform, new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 52f), new Vector2(0.5f, 1f));
        titleBarRt.anchorMin = new Vector2(0f, 1f);
        titleBarRt.anchorMax = new Vector2(1f, 1f);
        titleBarRt.pivot = new Vector2(0.5f, 1f);
        titleBarRt.anchoredPosition = Vector2.zero;
        titleBarRt.sizeDelta = new Vector2(0f, 52f);
        Image titleBarBg = titleBarRt.gameObject.AddComponent<Image>();
        titleBarBg.color = new Color(0.176f, 0.176f, 0.188f, 1f); // #2D2D30

        Text title = CreateText("Title", titleBarRt, Localization.Pick("炮台代码编辑器", "Tower Code Editor"), titleFontSize, Color.white, TextAnchor.MiddleLeft);
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 0f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(20f, 0f);
        titleRt.offsetMax = new Vector2(-240f, 0f);

        // 右侧按钮：✕ / 停止 / 编译运行（从右往左）
        CreateButton("BtnClose", titleBarRt, "✕", Close, new Vector2(56f, 34f), new Vector2(-14f, 0f), new Vector2(1f, 0.5f), new Color(0.22f, 0.24f, 0.27f, 1f));
        CreateButton("BtnStop", titleBarRt, Localization.Pick("停止", "Stop"), StopRunner, new Vector2(76f, 34f), new Vector2(-80f, 0f), new Vector2(1f, 0.5f), new Color(0.24f, 0.18f, 0.16f, 1f));
        CreateButton("BtnCompile", titleBarRt, Localization.Pick("编译运行 (F6)", "Compile & Run (F6)"), CompileAndRun, new Vector2(156f, 34f), new Vector2(-196f, 0f), new Vector2(1f, 0.5f), new Color(0.16f, 0.34f, 0.35f, 1f));

        // ---- 行号列视口（宽 70，RectMask2D 裁剪 + 背景；高度随窗口拉伸：上贴标题栏下缘、下贴底部横条） ----
        RectTransform lineViewportRt = CreateRect("LineViewport", editorRoot.transform, new Vector2(0f, 1f), Vector2.zero, new Vector2(70f, 868f), new Vector2(0f, 1f));
        lineViewportRt.anchorMin = new Vector2(0f, 0f);
        lineViewportRt.anchorMax = new Vector2(0f, 1f);
        lineViewportRt.pivot = new Vector2(0.5f, 0.5f);
        lineViewportRt.offsetMin = new Vector2(0f, 170f);
        lineViewportRt.offsetMax = new Vector2(70f, -52f);
        Image lineViewportBg = lineViewportRt.gameObject.AddComponent<Image>();
        lineViewportBg.color = lineBgColor;
        lineViewportBg.raycastTarget = false;
        lineViewportRt.gameObject.AddComponent<RectMask2D>();
        this.lineViewportRt = lineViewportRt;

        RectTransform lineRt = CreateRect("LineNumbers", lineViewportRt, new Vector2(0f, 1f), Vector2.zero, new Vector2(70f, 868f), new Vector2(0f, 1f));
        lineRt.anchorMin = new Vector2(0f, 1f);
        lineRt.anchorMax = new Vector2(0f, 1f);
        lineRt.pivot = new Vector2(0f, 1f);
        lineRt.anchoredPosition = Vector2.zero;
        lineRt.sizeDelta = new Vector2(70f, 868f);
        this.lineRt = lineRt;

        lineNumberText = CreateText("LineNumbersText", lineRt, "1", codeFontSize, lineNumberColor, TextAnchor.UpperRight);
        RectTransform lineTextRt = lineNumberText.rectTransform;
        Stretch(lineTextRt);
        lineTextRt.offsetMin = new Vector2(4f, 6f);
        lineTextRt.offsetMax = new Vector2(-8f, -6f);
        lineNumberText.supportRichText = false;
        lineNumberText.horizontalOverflow = HorizontalWrapMode.Overflow;
        lineNumberText.verticalOverflow = VerticalWrapMode.Overflow;

        // ---- 主代码区视口（RectMask2D 裁剪 + 背景；内部 CodeArea 随行数增高、滚动偏移 y≤0） ----
        // 四边拉伸：左 70（贴行号列右缘）、右 0（贴窗口右缘）、上 -52（贴标题栏下缘）、下 170（贴底部横条上缘）。
        // 不再使用固定的 1850×868，避免窗口宽高比非 16:9（如 1920×1017 窗口）时视口右侧露出未覆盖的深色/黑色残留条块。
        RectTransform codeViewportRt = CreateRect("CodeViewport", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(1850f, 868f), new Vector2(0.5f, 0.5f));
        codeViewportRt.anchorMin = Vector2.zero;
        codeViewportRt.anchorMax = Vector2.one;
        codeViewportRt.pivot = new Vector2(0.5f, 0.5f);
        codeViewportRt.offsetMin = new Vector2(70f, 170f);
        codeViewportRt.offsetMax = new Vector2(0f, -52f);
        Image codeViewportBg = codeViewportRt.gameObject.AddComponent<Image>();
        codeViewportBg.color = new Color(0.118f, 0.118f, 0.118f, 1f);
        codeViewportBg.raycastTarget = false;
        codeViewportRt.gameObject.AddComponent<RectMask2D>();
        this.codeViewportRt = codeViewportRt;

        // CodeArea 内容：承载底层 InputField（原生光标 / 点击 / 拖拽选择）与上层高亮 Text
        GameObject codeGo = new GameObject("CodeArea", typeof(RectTransform), typeof(Image));
        codeGo.transform.SetParent(codeViewportRt, false);
        RectTransform codeRt = codeGo.GetComponent<RectTransform>();
        codeRt.anchorMin = new Vector2(0f, 1f);
        codeRt.anchorMax = new Vector2(0f, 1f);
        codeRt.pivot = new Vector2(0f, 1f);
        codeRt.anchoredPosition = Vector2.zero;
        codeRt.sizeDelta = new Vector2(1850f, 868f);
        this.codeRt = codeRt;
        Image codeBg = codeGo.GetComponent<Image>();
        codeBg.color = new Color(0.118f, 0.118f, 0.118f, 1f); // 点击事件落在此 Image（InputField 挂在 CodeArea 上）

        // 底层 InputField（用户在此输入 / 编辑；文字透明，原生光标与选区可见）
        InputField input = codeGo.AddComponent<InputField>();
        input.targetGraphic = codeBg;
        Text inputText = CreateText("InputText", codeRt, "", codeFontSize, new Color(1f, 1f, 1f, 0f), TextAnchor.UpperLeft);
        RectTransform inputTextRt = inputText.rectTransform;
        Stretch(inputTextRt);
        inputTextRt.offsetMin = new Vector2(12f, 8f);
        inputTextRt.offsetMax = new Vector2(-12f, -8f);
        inputText.supportRichText = false;
        inputText.horizontalOverflow = HorizontalWrapMode.Overflow; // 长行不换行，横向溢出由滚动条查看
        inputText.verticalOverflow = VerticalWrapMode.Overflow;
        inputText.font = UIFontHelper.LoadMono();
        input.textComponent = inputText;
        input.lineType = InputField.LineType.MultiLineNewline;
        input.caretColor = Color.white;        // 恢复原生光标为可见白色
        input.caretBlinkRate = caretBlinkRate; // 恢复默认约 0.85f 闪烁
        input.caretWidth = 1;
        // 选中高亮：半透明淡蓝（alpha 仅 0.26），叠加在深色底板上不遮字、不发白
        input.selectionColor = new Color(0.32f, 0.52f, 0.92f, 0.26f);
        input.onValueChanged.AddListener(s => { RefreshCodeView(); RecordSnapshotForUndo(); });
        inputField = input;
        this.inputText = inputText;

        // 上层只读高亮 Text（同 RectTransform 完全重叠，显示高亮代码）
        highlightText = CreateText("HighlightText", codeRt, "", codeFontSize, Color.white, TextAnchor.UpperLeft);
        RectTransform hlRt = highlightText.rectTransform;
        Stretch(hlRt);
        hlRt.offsetMin = new Vector2(12f, 8f);
        hlRt.offsetMax = new Vector2(-12f, -8f);
        highlightText.supportRichText = true;
        highlightText.horizontalOverflow = HorizontalWrapMode.Overflow; // 长行不换行，横向溢出由滚动条查看
        highlightText.verticalOverflow = VerticalWrapMode.Overflow;
        highlightText.font = UIFontHelper.LoadMono();
        highlightText.raycastTarget = false;

        // ---- 右侧竖 / 底部横滚动条（配合自实现滚动；编辑器暂停时 ScrollRect 失效，故手写轨道+滑块+拖拽） ----
        // 竖条：贴窗口右缘内侧（宽 10，距右缘 6px，上下随窗口拉伸），轨道底色全透明，
        // 只在需要滚动时显示浅灰滑块——代码区右侧不再出现深色/黑色条块。
        vScrollTrackRt = CreateRect("VScrollTrack", editorRoot.transform, new Vector2(1f, 0f), Vector2.zero, new Vector2(10f, 856f), new Vector2(1f, 0.5f));
        vScrollTrackRt.anchorMin = new Vector2(1f, 0f);
        vScrollTrackRt.anchorMax = new Vector2(1f, 1f);
        vScrollTrackRt.pivot = new Vector2(1f, 0.5f);
        vScrollTrackRt.offsetMin = new Vector2(-16f, 170f);
        vScrollTrackRt.offsetMax = new Vector2(-6f, -52f);
        Image vTrackImg = vScrollTrackRt.gameObject.AddComponent<Image>();
        vTrackImg.color = new Color(0.08f, 0.08f, 0.08f, 0f); // 透明轨道：仅保留可点击跳转的命中区域
        vTrackImg.raycastTarget = true;
        vScrollTrackRt.gameObject.AddComponent<ScrollBarTrackJump>().Setup(vScrollTrackRt, true, t => SetScrollFromBar(0, t));

        vScrollHandleRt = CreateRect("VScrollHandle", vScrollTrackRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 60f), new Vector2(0.5f, 1f));
        vScrollHandleRt.anchorMin = new Vector2(0.5f, 1f);
        vScrollHandleRt.anchorMax = new Vector2(0.5f, 1f);
        vScrollHandleRt.pivot = new Vector2(0.5f, 1f);
        vScrollHandleRt.anchoredPosition = Vector2.zero;
        vScrollHandleRt.sizeDelta = new Vector2(10f, 60f);
        Image vHandleImg = vScrollHandleRt.gameObject.AddComponent<Image>();
        vHandleImg.color = new Color(0.42f, 0.42f, 0.44f, 0.9f);
        vScrollHandleRt.gameObject.AddComponent<ScrollBarHandle>().Setup(vScrollTrackRt, true, t => SetScrollFromBar(0, t));
        // 滑块自建即隐藏（需求：任何窗口尺寸下都不显示滑块）；轨道本身透明且保留点击跳转能力
        vScrollHandleRt.gameObject.SetActive(false);

        // 横条：贴底部错误区上缘（高 10，左 70 对齐代码视口左缘、右 16 留出竖条空间），轨道底色同样透明
        hScrollTrackRt = CreateRect("HScrollTrack", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(1834f, 10f), new Vector2(0f, 1f));
        hScrollTrackRt.anchorMin = Vector2.zero;
        hScrollTrackRt.anchorMax = new Vector2(1f, 0f);
        hScrollTrackRt.pivot = new Vector2(0f, 1f);
        hScrollTrackRt.offsetMin = new Vector2(70f, 160f);
        hScrollTrackRt.offsetMax = new Vector2(-16f, 170f);
        Image hTrackImg = hScrollTrackRt.gameObject.AddComponent<Image>();
        hTrackImg.color = new Color(0.08f, 0.08f, 0.08f, 0f); // 透明轨道：仅保留可点击跳转的命中区域
        hTrackImg.raycastTarget = true;
        hScrollTrackRt.gameObject.AddComponent<ScrollBarTrackJump>().Setup(hScrollTrackRt, false, t => SetScrollFromBar(1, t));

        hScrollHandleRt = CreateRect("HScrollHandle", hScrollTrackRt, new Vector2(0f, 1f), Vector2.zero, new Vector2(120f, 10f), new Vector2(0f, 1f));
        hScrollHandleRt.anchorMin = new Vector2(0f, 1f);
        hScrollHandleRt.anchorMax = new Vector2(0f, 1f);
        hScrollHandleRt.pivot = new Vector2(0f, 1f);
        hScrollHandleRt.anchoredPosition = Vector2.zero;
        hScrollHandleRt.sizeDelta = new Vector2(120f, 10f);
        Image hHandleImg = hScrollHandleRt.gameObject.AddComponent<Image>();
        hHandleImg.color = new Color(0.42f, 0.42f, 0.44f, 0.9f);
        hScrollHandleRt.gameObject.AddComponent<ScrollBarHandle>().Setup(hScrollTrackRt, false, t => SetScrollFromBar(1, t));
        hScrollHandleRt.gameObject.SetActive(false); // 同上：横向滑块常驻隐藏

        // ---- 底部错误 / 状态区（高 160px） ----
        RectTransform bottomRt = CreateRect("ErrorZone", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 160f), new Vector2(0.5f, 0f));
        bottomRt.anchorMin = Vector2.zero;
        bottomRt.anchorMax = new Vector2(1f, 0f);
        bottomRt.pivot = new Vector2(0.5f, 0f);
        bottomRt.anchoredPosition = Vector2.zero;
        bottomRt.sizeDelta = new Vector2(0f, 160f);
        Image bottomBg = bottomRt.gameObject.AddComponent<Image>();
        bottomBg.color = consoleBgFullColor;
        consoleBg = bottomBg;

        errorListText = CreateText("ErrorList", bottomRt, "", errorFontSize, errorColor, TextAnchor.UpperLeft);
        RectTransform errRt = errorListText.rectTransform;
        errRt.anchorMin = new Vector2(0f, 1f);
        errRt.anchorMax = new Vector2(1f, 1f);
        errRt.pivot = new Vector2(0.5f, 1f);
        errRt.anchoredPosition = new Vector2(0f, -6f);
        errRt.sizeDelta = new Vector2(-24f, -56f);
        errorListText.supportRichText = false;
        errorListText.horizontalOverflow = HorizontalWrapMode.Wrap;
        errorListText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform statusRt = CreateRect("StatusBar", bottomRt.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 30f), new Vector2(0.5f, 0f));
        statusRt.anchorMin = new Vector2(0f, 0f);
        statusRt.anchorMax = new Vector2(1f, 0f);
        statusRt.pivot = new Vector2(0.5f, 0f);
        statusRt.anchoredPosition = Vector2.zero;
        statusRt.sizeDelta = new Vector2(0f, 30f);
        Image statusBg = statusRt.gameObject.AddComponent<Image>();
        statusBg.color = new Color(0.145f, 0.145f, 0.149f, 1f);

        statusLineText = CreateText("StatusText", statusRt, "", statusFontSize, new Color(0.75f, 0.75f, 0.75f, 1f), TextAnchor.MiddleLeft);
        RectTransform statusTextRt = statusLineText.rectTransform;
        statusTextRt.anchorMin = new Vector2(0f, 0f);
        statusTextRt.anchorMax = new Vector2(1f, 1f);
        statusTextRt.offsetMin = new Vector2(16f, 0f);
        statusTextRt.offsetMax = new Vector2(-16f, 0f);
        statusLineText.supportRichText = false;

        // ---- Level 3 进场提示：时间已冻结，按 Enter 输入代码（默认隐藏；挂 Canvas 根，不随编辑器开关） ----
        RectTransform promptRt = CreateRect("EnterPrompt", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 96f), new Vector2(0.5f, 0.5f));
        Image promptBg = promptRt.gameObject.AddComponent<Image>();
        promptBg.color = new Color(0f, 0f, 0f, 0.55f);
        Text promptText = CreateText("PromptText", promptRt,
            Localization.Pick("时间已冻结 —— 按下 Enter 输入代码", "Time frozen — Press Enter to write code"),
            26, Color.white, TextAnchor.MiddleCenter);
        Stretch(promptText.rectTransform);
        enterPromptRt = promptRt;
        enterPromptRt.gameObject.SetActive(false);

        // ---- 顶部区域层级置顶：标题栏（含标题文字与 ✕ / 停止 / 编译运行 按钮）置于兄弟节点最后，
        //      渲染顺序压在行号列 / 代码视口 / 滚动条 / 底部错误区之上，避免顶部被下层元素覆盖 ----
        if (titleBarRt != null)
            titleBarRt.SetAsLastSibling();

        // 构建完成即按当前预留宽度 / 高度排版（教学关进入时已设为底部留白，构建后立即生效）
        ApplyRightReservedWidth();
        ApplyBottomReservedHeight();
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
        text.font = UIFontHelper.LoadForText(content, fontSizeValue);
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(string name, Transform parent, string label, System.Action onClick, Vector2 size, Vector2 pos, Vector2 anchor, Color bg)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = bg;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        Text txt = CreateText("Text", rt, label, 15, Color.white, TextAnchor.MiddleCenter);
        Stretch(txt.rectTransform);
        return btn;
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

    /// <summary>滚动条滑块拖动手柄：把鼠标在轨道局部坐标中的轴向位置换算为 0~1 滚动进度，交给回调。</summary>
    private sealed class ScrollBarHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private RectTransform trackRt;
        private bool vertical;
        private System.Action<float> onSet;

        public void Setup(RectTransform track, bool isVertical, System.Action<float> callback)
        {
            trackRt = track;
            vertical = isVertical;
            onSet = callback;
        }

        public void OnBeginDrag(PointerEventData e) { Apply(e); }
        public void OnDrag(PointerEventData e) { Apply(e); }

        private void Apply(PointerEventData e)
        {
            Vector2 lp;
            if (trackRt == null || onSet == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, e.position, null, out lp))
                return;
            RectTransform self = transform as RectTransform;
            float trackSize = vertical ? trackRt.rect.height : trackRt.rect.width;
            float handleSize = vertical ? self.rect.height : self.rect.width;
            float usable = Mathf.Max(1f, trackSize - handleSize);
            float v = vertical ? lp.y : lp.x;
            // 竖轨道 pivot.y=0.5：局部 y 以中心为原点，滑块中心可行域 [-usable/2, usable/2]
            // 横轨道 pivot.x=0：局部 x 以左缘为原点，滑块中心可行域 [handle/2, track-handle/2]
            float centerMin = vertical ? -usable * 0.5f : handleSize * 0.5f;
            onSet(Mathf.Clamp01((v - centerMin) / usable));
        }
    }

    /// <summary>滚动条轨道点击跳转：点击轨道任意位置即把滑块中心移到该处（拖动滑块时事件冒泡到轨道，位置与当前滑块一致，无跳变）。</summary>
    private sealed class ScrollBarTrackJump : MonoBehaviour, IPointerDownHandler
    {
        private RectTransform trackRt;
        private bool vertical;
        private System.Action<float> onSet;

        public void Setup(RectTransform track, bool isVertical, System.Action<float> callback)
        {
            trackRt = track;
            vertical = isVertical;
            onSet = callback;
        }

        public void OnPointerDown(PointerEventData e)
        {
            Vector2 lp;
            if (trackRt == null || onSet == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, e.position, null, out lp))
                return;
            float trackSize = vertical ? trackRt.rect.height : trackRt.rect.width;
            float v = vertical ? lp.y : lp.x;
            float t = vertical ? (v / Mathf.Max(1f, trackSize) + 0.5f) : (v / Mathf.Max(1f, trackSize));
            onSet(Mathf.Clamp01(t));
        }
    }
}

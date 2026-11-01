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
///   - C# 词法高亮：关键字青、字符串绿、数字粉、注释灰绿、默认白；富文本中 < 替换为全角 ＜；
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
    private RectTransform enterPromptRt; // Level 3 进场「按 Enter 输入代码」提示
    private int lastCaretPos = -1;       // 上次光标位置（轮询检测移动 / 换行）
    private PlayerCameraController playerController;
    private CommandConsoleController commandConsole;
    private SettingsPanelController settingsPanel;
    private CodeTower tower;

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

    /// <summary>默认示例代码（首次打开填入，展示炮台接口语法）。</summary>
    private const string DEFAULT_CODE =
        "// 原点炮台示例：瞄准最近方块并开火（每帧自动执行）\n" +
        "// 接口：tower.Aim(dir) / tower.FireLaser() / tower.NearestBlock / tower.Cooldown01\n" +
        "if (tower.NearestBlock != null)\n" +
        "{\n" +
        "    tower.Aim(tower.NearestBlock.GeometryCenter - tower.Position);\n" +
        "    if (tower.Cooldown01 <= 0f)\n" +
        "        tower.FireLaser();\n" +
        "}\n";

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
            if (Input.GetKeyDown(openKey) || Input.GetKeyDown(openKeyAlt))
            {
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
            // ESC 同样退出代码编辑模式（编辑器占用外部输入，不会误触发暂停）
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
        }

        // 编辑器打开时：光标位置变化 → 刷新状态栏并自动滚动跟随；滚轮在代码视口内翻页
        if (IsOpen)
        {
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

            UpdateScrollBars(); // 滚动条滑块尺寸 / 位置随滚动状态每帧同步（内容不足时隐藏滑块）
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
                AppendError(Localization.Pick("运行时错误：", "Runtime error: ") + e.GetBaseException().Message);
                Debug.LogException(e);
            }
        }
    }

    // ---------------- 开关逻辑（与 PlayerCameraController 联动） ----------------

    /// <summary>打开编辑器：暂停时间、释放鼠标、占用视角输入；教学 / 指令框状态下忽略。</summary>
    public void Open()
    {
        if (IsOpen)
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
        inputField.ActivateInputField();
        inputField.Select();

        Time.timeScale = 0f;
        SetCursorLocked(false);
        AppendError("");
        SetStatus(Localization.Pick("编辑器已打开（Enter / Esc 关闭，✕ 也可关闭）", "Editor opened (Enter / Esc or ✕ to close)"), false);
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
        Debug.Log("[CodeEditor] 编辑器关闭");
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
                        sb.Append(Localization.Pick("第 ", "L"))
                          .Append(userLine)
                          .Append(Localization.Pick(" 行,第 ", ", C"))
                          .Append(e.Column)
                          .Append(Localization.Pick(" 列：", ": "))
                          .Append("CS").Append(e.ErrorNumber).Append(' ').Append(e.ErrorText)
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
                CloseAndForceResume(); // 编译成功后强制恢复游戏状态，回到正常玩法
            }
        }
        catch (System.Exception e)
        {
            AppendError(Localization.Pick("编译异常：", "Compile exception: ") + e.GetBaseException().Message);
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

    /// <summary>添加编译引用程序集（去重；Location 为空则跳过并告警）。</summary>
    private static void AddReferencedAssembly(CompilerParameters parameters, string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            Debug.LogWarning("[CodeEditor] 引用程序集位置为空，已跳过。");
            return;
        }
        if (!parameters.ReferencedAssemblies.Contains(location))
            parameters.ReferencedAssemblies.Add(location);
    }

    // ---------------- C# 词法高亮 ----------------

    /// <summary>将 C# 代码转为 UGUI 富文本（彩色语法高亮）；所有 < 替换为全角 ＜ 防误吞标签。</summary>
    private string HighlightCSharp(string code)
    {
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
                AppendToken(sb, code.Substring(start, i - start), C_COMMENT);
                continue;
            }

            // 块注释 /* */
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i < n && !(code[i] == '*' && i + 1 < n && code[i + 1] == '/')) i++;
                if (i < n) i += 2;
                AppendToken(sb, code.Substring(start, Mathf.Min(i, n) - start), C_COMMENT);
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
                AppendToken(sb, code.Substring(start, i - start), C_STRING);
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
                AppendToken(sb, code.Substring(start, i - start), C_STRING);
                continue;
            }

            // 标识符 / 关键字
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                string word = code.Substring(start, i - start);
                AppendToken(sb, word, keywords.Contains(word) ? C_KEYWORD : C_DEFAULT);
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
                AppendToken(sb, code.Substring(start, i - start), C_NUMBER);
                continue;
            }

            // 其它字符原样输出（< 替换为全角）
            AppendRaw(sb, c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>追加带颜色标签的 token；< 替换为全角 ＜ 防止 UGUI 富文本误吞。</summary>
    private static void AppendToken(StringBuilder sb, string token, string color)
    {
        sb.Append("<color=").Append(color).Append('>');
        for (int i = 0; i < token.Length; i++)
            sb.Append(token[i] == '<' ? '＜' : token[i]);
        sb.Append("</color>");
    }

    private static void AppendRaw(StringBuilder sb, char c)
    {
        sb.Append(c == '<' ? '＜' : c);
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

    /// <summary>滚动条滑块尺寸与位置随滚动状态同步：滑块长度 ∝ 视口 / 内容；内容不溢出时隐藏滑块。</summary>
    private void UpdateScrollBars()
    {
        if (codeRt == null || codeViewportRt == null)
            return;
        if (vScrollTrackRt != null && vScrollHandleRt != null)
        {
            float trackH = vScrollTrackRt.rect.height;
            float maxY = GetScrollMaxY();
            if (maxY <= 0.01f || codeContentHeight <= codeViewportRt.rect.height + 0.01f)
            {
                vScrollHandleRt.gameObject.SetActive(false);
            }
            else
            {
                vScrollHandleRt.gameObject.SetActive(true);
                float handleH = Mathf.Max(24f, trackH * codeViewportRt.rect.height / codeContentHeight);
                vScrollHandleRt.sizeDelta = new Vector2(vScrollHandleRt.sizeDelta.x, handleH);
                float usable = Mathf.Max(1f, trackH - handleH);
                vScrollHandleRt.anchoredPosition = new Vector2(0f, -Mathf.Clamp01(codeRt.anchoredPosition.y / maxY) * usable);
            }
        }
        if (hScrollTrackRt != null && hScrollHandleRt != null)
        {
            float trackW = hScrollTrackRt.rect.width;
            float maxX = GetScrollMaxX();
            if (maxX <= 0.01f || codeContentWidth <= codeViewportRt.rect.width + 0.01f)
            {
                hScrollHandleRt.gameObject.SetActive(false);
            }
            else
            {
                hScrollHandleRt.gameObject.SetActive(true);
                float handleW = Mathf.Max(24f, trackW * codeViewportRt.rect.width / codeContentWidth);
                hScrollHandleRt.sizeDelta = new Vector2(handleW, hScrollHandleRt.sizeDelta.y);
                float usable = Mathf.Max(1f, trackW - handleW);
                float scrollX = -codeRt.anchoredPosition.x;
                hScrollHandleRt.anchoredPosition = new Vector2(Mathf.Clamp01(scrollX / maxX) * usable, 0f);
            }
        }
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

    /// <summary>Ctrl+滚轮：代码字号缩放（放大 / 缩小），鼠标需在代码视口内；以鼠标所在行为缩放锚点，缩放后该行保持原位。</summary>
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

        // 记录鼠标所在行与行内比例（缩放锚点，随鼠标位置实时计算）
        int anchorLine = 0;
        float anchorFrac = 0f;
        float mouseFromTop = codeViewportRt.rect.height * 0.5f; // 兜底：视口中部
        float oldLH = Mathf.Max(1f, codeFontSize * lineHeightFactor);
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(codeViewportRt, Input.mousePosition, null, out localPoint))
        {
            mouseFromTop = Mathf.Clamp(-localPoint.y, 0f, codeViewportRt.rect.height);
            float contentOffset = mouseFromTop + codeRt.anchoredPosition.y; // 鼠标对应的内容偏移（距内容顶，向下正）
            anchorLine = Mathf.FloorToInt((contentOffset - 8f) / oldLH);
            anchorLine = Mathf.Clamp(anchorLine, 0, Mathf.Max(0, CountLines(inputField.text) - 1));
            anchorFrac = Mathf.Clamp01((contentOffset - 8f - anchorLine * oldLH) / oldLH);
        }

        codeFontSize = newSize;
        if (inputText != null)
            inputText.fontSize = codeFontSize;
        if (highlightText != null)
            highlightText.fontSize = codeFontSize;
        if (lineNumberText != null)
            lineNumberText.fontSize = codeFontSize;
        RefreshCodeView(); // 重算高亮 / 行号 / 内容高度与滚动范围

        // 缩放后恢复锚点行到鼠标位置：行顶 + 行内比例 - 鼠标距顶部距离，再钳制到有效滚动范围。
        // 缩小后内容变矮、锚点行整体上移，允许负滚动（内容顶部低于视口顶，露出背景留白）以保证该行原位不动。
        float newLH = Mathf.Max(1f, codeFontSize * lineHeightFactor);
        float targetY = 8f + anchorLine * newLH + anchorFrac * newLH - mouseFromTop;
        targetY = Mathf.Clamp(targetY, -codeViewportRt.rect.height * 0.5f, GetScrollMaxY());
        ApplyScrollY(targetY);
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
        canvas.sortingOrder = 90;

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
        CreateButton("BtnCompile", titleBarRt, Localization.Pick("编译运行", "Compile & Run"), CompileAndRun, new Vector2(110f, 34f), new Vector2(-168f, 0f), new Vector2(1f, 0.5f), new Color(0.16f, 0.34f, 0.35f, 1f));

        // ---- 行号列视口（70px，RectMask2D 裁剪 + 背景；内部行号内容随行数增高、随滚动同步） ----
        RectTransform lineViewportRt = CreateRect("LineViewport", editorRoot.transform, new Vector2(0f, 1f), new Vector2(0f, -52f), new Vector2(70f, 868f), new Vector2(0f, 1f));
        lineViewportRt.anchorMin = new Vector2(0f, 1f);
        lineViewportRt.anchorMax = new Vector2(0f, 1f);
        lineViewportRt.pivot = new Vector2(0f, 1f);
        lineViewportRt.anchoredPosition = new Vector2(0f, -52f);
        lineViewportRt.sizeDelta = new Vector2(70f, 868f);
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
        RectTransform codeViewportRt = CreateRect("CodeViewport", editorRoot.transform, new Vector2(0f, 1f), new Vector2(70f, -52f), new Vector2(1850f, 868f), new Vector2(0f, 1f));
        codeViewportRt.anchorMin = new Vector2(0f, 1f);
        codeViewportRt.anchorMax = new Vector2(0f, 1f);
        codeViewportRt.pivot = new Vector2(0f, 1f);
        codeViewportRt.anchoredPosition = new Vector2(70f, -52f);
        codeViewportRt.sizeDelta = new Vector2(1850f, 868f);
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
        input.selectionColor = new Color(0.3f, 0.5f, 0.9f, 0.5f);
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
        // 竖条：贴代码视口右缘内侧（宽 10，上下各留 6px）
        vScrollTrackRt = CreateRect("VScrollTrack", editorRoot.transform, new Vector2(0f, 1f), new Vector2(1916f, -486f), new Vector2(10f, 856f), new Vector2(1f, 0.5f));
        vScrollTrackRt.anchorMin = new Vector2(0f, 1f);
        vScrollTrackRt.anchorMax = new Vector2(0f, 1f);
        vScrollTrackRt.pivot = new Vector2(1f, 0.5f);
        vScrollTrackRt.anchoredPosition = new Vector2(1916f, -486f);
        vScrollTrackRt.sizeDelta = new Vector2(10f, 856f);
        Image vTrackImg = vScrollTrackRt.gameObject.AddComponent<Image>();
        vTrackImg.color = new Color(0.08f, 0.08f, 0.08f, 0.55f);
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

        // 横条：贴代码视口下缘内侧（高 10，左侧对齐代码视口左缘）
        hScrollTrackRt = CreateRect("HScrollTrack", editorRoot.transform, new Vector2(0f, 1f), new Vector2(70f, -910f), new Vector2(1834f, 10f), new Vector2(0f, 1f));
        hScrollTrackRt.anchorMin = new Vector2(0f, 1f);
        hScrollTrackRt.anchorMax = new Vector2(0f, 1f);
        hScrollTrackRt.pivot = new Vector2(0f, 1f);
        hScrollTrackRt.anchoredPosition = new Vector2(70f, -910f);
        hScrollTrackRt.sizeDelta = new Vector2(1834f, 10f);
        Image hTrackImg = hScrollTrackRt.gameObject.AddComponent<Image>();
        hTrackImg.color = new Color(0.08f, 0.08f, 0.08f, 0.55f);
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

        // ---- 底部错误 / 状态区（高 160px） ----
        RectTransform bottomRt = CreateRect("ErrorZone", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 160f), new Vector2(0.5f, 0f));
        bottomRt.anchorMin = Vector2.zero;
        bottomRt.anchorMax = new Vector2(1f, 0f);
        bottomRt.pivot = new Vector2(0.5f, 0f);
        bottomRt.anchoredPosition = Vector2.zero;
        bottomRt.sizeDelta = new Vector2(0f, 160f);
        Image bottomBg = bottomRt.gameObject.AddComponent<Image>();
        bottomBg.color = new Color(0.09f, 0.09f, 0.09f, 1f);

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

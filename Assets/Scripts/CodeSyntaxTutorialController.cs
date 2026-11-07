using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 代码编辑器内的顺序式 C# 教学（两门课程，均由 LevelDirector 调用 Begin(course) 进入）：
///   - Course.Grammar（Level 1 - Part 2）：C# 基础语法全套 18 项 —— 变量声明与类型、const 常量、算术运算符、
///     关系运算符、逻辑运算符、if、if-else、switch、for、while、do-while、一维数组、List 集合、
///     方法定义与参数（局部函数）、方法返回值 return、字符串拼接、string.Format、注释；
///   - Course.Api（Level 2 - Part 1）：炮台 API 调用全套 13 项 —— GetAllObjects、TowerObjectInfo.Index /
///     Type / HP / Position / Velocity / Volume、GetFireCooldown、FireAtPlane、PrintLine、Print、ClearPrint、
///     以及「遍历 + 条件 + 发射」实战组合；
///   - 每一项任务的完成条件都相同：① 编辑器里的代码命中该任务的语法 ② 本次任务期间点过【编译运行 (F6)】
///     并且**编译通过的那份代码本身**满足该语法（防止先写后编译 / 先编译后补写蒙混过关）；
///     任务卡常驻显示「① 语法 / ② 编译」两条勾选状态，未编译时明确提示「点标题栏【编译运行 (F6)】验证」；
///   - 判定机制：结构类用正则解析编辑器文本（先剥离注释与字符串字面量，避免注释 / 字符串误判），
///     编译类用 CodeEditorController.OnCompiledSuccessfully 事件，不依赖运行时反射；
///   - 离开 / 重进关卡都会重置进度（Begin 每次从第 1 项开始并重建进度格）。
/// UI 风格与 TutorialController 保持一致（纯代码构建 ScreenSpaceOverlay Canvas + 半透明面板 + Text）。
/// </summary>
public class CodeSyntaxTutorialController : MonoBehaviour
{
    /// <summary>教学课程：C# 基础语法（Level 1-2）/ 炮台 API 调用（Level 2-1）。</summary>
    public enum Course
    {
        Grammar = 0,
        Api = 1
    }

    /// <summary>单任务结构判定：入参为原始代码与「剥离注释 / 字符串后」的代码。</summary>
    private delegate bool TaskMatch(string raw, string code);

    /// <summary>单个教学任务定义（标题 / 正文 / 判定）。</summary>
    private class TaskDef
    {
        public string TitleZh;
        public string TitleEn;
        public string BodyZh;
        public string BodyEn;
        public TaskMatch Match;
    }

    public static CodeSyntaxTutorialController Instance { get; private set; }

    /// <summary>教学序列是否处于激活状态（进入对应关卡为 true，离开关卡后为 false）。</summary>
    public bool Active { get; private set; }

    /// <summary>当前任务序号（0-based）；等于任务总数表示全部完成。</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>当前课程。</summary>
    public Course CurrentCourse { get; private set; }

    /// <summary>当前课程的任务总数。</summary>
    public int TaskCount
    {
        get { return tasks != null ? tasks.Length : 0; }
    }

    /// <summary>外部只读查询：教学是否在进行（供 CodeEditorController / 其他系统判断）。</summary>
    public static bool IsActive
    {
        get { return Instance != null && Instance.Active; }
    }

    // ---------------- UI 引用 ----------------
    private const int MaxChips = 24;   // 进度格最多预建数量（两门课程任务数均 ≤ 24）
    // 上下分栏布局：代码编辑器占上方整块，指导框（任务卡）横跨屏幕底部整条，
    // 其高度即编辑器底部预留高度（CodeEditorController.SetBottomReservedHeight），两者上下一一对齐、互不重叠。
    private const float ReferenceWidth = 1920f;          // CanvasScaler 参考分辨率宽（面板铺满宽度回退值）
    private const float GuidePanelHeightMin = 300f;      // 指导框最小高度（文案短时）
    private const float GuidePanelHeightMax = 560f;      // 指导框最大高度（保证上方代码区仍有足够可视行数）
    private const float GuideBodyTopInset = 132f;        // 正文上边距（让开强调线 / 标题 / 进度格 / 任务名）
    private const float GuideBodyBottomInset = 46f;      // 正文下边距（让开「任务完成 ✔」提示）
    private const int GuideBodyFontSize = 18;            // 正文号（宽度铺满后可略小，行数更紧凑）
    private const float GuideBodyLineSpacing = 1.05f;    // 正文行距
    private const float HintBarMaxWidth = 1400f;         // 提示条最大宽度
    private const float HintBarMinWidth = 420f;          // 提示条最小宽度
    private const float HintBarMinHeight = 64f;          // 提示条最小高度
    private GameObject panelRoot;      // 编辑器打开时显示的任务卡（底部指导框）
    private RectTransform hintRect;    // 提示条矩形（按文案自适应宽高）
    private Text headlineText;         // 「关卡 1 · 第 2 部分 · C# 基础语法教学」
    private Text progressText;         // 「任务 2/18」
    private Text taskNameText;         // 当前任务名
    private Text bodyText;             // 写法 / 目标 / 提示 / 判定 + 完成条件状态
    private Text flashText;            // 「任务完成 ✔」短暂提示
    private Text[] chipTexts;          // 进度格文字（索引 = 任务序号）
    private Image[] chipBgs;           // 进度格底色
    private GameObject hintRoot;       // 编辑器关闭时的常驻提示条
    private Text hintText;

    // ---------------- 运行时状态 ----------------
    private TaskDef[] tasks;           // 当前课程任务表
    private bool[] done;               // 各任务完成标记
    private CodeEditorController editor;
    private FlyingBlockSpawner spawner;
    private CodeTower tower;
    private bool compiledThisTask;     // 本次任务期间是否发生过「编译运行成功」
    private string compiledRaw;        // 最近一次编译成功的用户代码原文
    private string compiledSnapshot;   // 最近一次编译成功的代码（已剥离注释 / 字符串）
    private bool finished;
    private bool autoOpenPending;      // 需要自动打开编辑器（指令框仍打开时会延后到下一帧重试）
    private float checkTimer;          // 结构判定节流（真实时间）
    private float flashTimer;          // 「任务完成」提示倒计时（真实时间）
    private string hintCache = "";     // 提示条文案缓存，避免逐帧改文字
    private int statusCache = -1;      // 任务卡状态缓存（1=语法命中 2=编译通过），避免逐帧重建正文

    // ---------------- 结构判定正则（语法课） ----------------
    // 变量声明与初始化：类型 + 变量名 = 值;
    private static readonly Regex ReVariable = new Regex(
        @"\b(int|float|double|string|bool|char|long|var)\s+[A-Za-z_]\w*\s*=\s*[^;=][^;]*;",
        RegexOptions.Compiled);
    // const 常量：const 类型 名字 = 值;
    private static readonly Regex ReConst = new Regex(
        @"\bconst\s+(int|float|double|string|bool|char|long)\s+[A-Za-z_]\w*\s*=\s*[^;]+;",
        RegexOptions.Compiled);
    // 算术表达式：操作数 运算符 操作数（+ - * / %）
    private static readonly Regex ReArithmetic = new Regex(
        @"([A-Za-z_]\w*|\d+(?:\.\d+)?)\s*[+\-*/%]\s*([A-Za-z_]\w*|\d+(?:\.\d+)?)",
        RegexOptions.Compiled);
    // 关系运算：操作数 比较运算符 操作数（== != >= <= > <）
    private static readonly Regex ReRelational = new Regex(
        @"([A-Za-z_]\w*|\d+(?:\.\d+)?)\s*(==|!=|>=|<=|>|<)\s*([A-Za-z_]\w*|\d+(?:\.\d+)?)",
        RegexOptions.Compiled);
    // 逻辑运算：&& / || / 单目 !
    private static readonly Regex ReLogical = new Regex(
        @"&&|\|\||(?<![=!<>])!(?!=)",
        RegexOptions.Compiled);
    // if 条件判断：if ( … 条件 … )
    private static readonly Regex ReIf = new Regex(
        @"\bif\s*\([^;{}]*(==|!=|>=|<=|>|<|&&|\|\||!)",
        RegexOptions.Compiled);
    // if … else …
    private static readonly Regex ReIfElse = new Regex(
        @"\bif\s*\([^;{}]*\)[\s\S]*?\belse\b",
        RegexOptions.Compiled);
    // switch：switch ( … ) { case … }
    private static readonly Regex ReSwitch = new Regex(
        @"\bswitch\s*\([^;{}]*\)\s*\{[\s\S]*\bcase\b",
        RegexOptions.Compiled);
    // for 循环：for ( 初始化; 条件; 迭代 )
    private static readonly Regex ReFor = new Regex(
        @"\bfor\s*\([^;{}]*;[^;{}]*;",
        RegexOptions.Compiled);
    // while 循环：while ( … ) {（do-while 的 while 后不带 { ，故不会误判）
    private static readonly Regex ReWhile = new Regex(
        @"\bwhile\s*\([^;{}]*\)\s*\{",
        RegexOptions.Compiled);
    // do { … } while ( … );
    private static readonly Regex ReDoWhile = new Regex(
        @"\bdo\s*\{[\s\S]*?\}\s*while\s*\(",
        RegexOptions.Compiled);
    // 一维数组：new 类型[ … ] 或 数组名[下标]
    private static readonly Regex ReArray = new Regex(
        @"(\bnew\s+(int|float|double|string|bool|char)\s*\[)|([A-Za-z_]\w*\s*\[\s*\d+\s*\])",
        RegexOptions.Compiled);
    // List 集合：new List< … >
    private static readonly Regex ReListNew = new Regex(
        @"\bnew\s+List\s*<",
        RegexOptions.Compiled);
    // List 添加元素：xxx.Add(
    private static readonly Regex ReListAdd = new Regex(
        @"\.\s*Add\s*\(",
        RegexOptions.Compiled);
    // 方法（局部函数）定义：返回类型 名字(参数) {
    private static readonly Regex ReMethodDef = new Regex(
        @"\b(void|int|float|double|bool|string|long|char)\s+[A-Za-z_]\w*\s*\([^)]*\)\s*\{",
        RegexOptions.Compiled);
    // return 语句
    private static readonly Regex ReReturn = new Regex(@"\breturn\b", RegexOptions.Compiled);
    // 方法调用并接收返回值：变量 = 方法名(
    private static readonly Regex ReMethodCallAssign = new Regex(
        @"[A-Za-z_]\w*\s*=\s*[A-Za-z_]\w*\s*\(",
        RegexOptions.Compiled);
    // 字符串拼接：字符串字面量（剥离后变成 ""）+ 变量，或变量 + 字符串字面量
    private static readonly Regex ReConcat = new Regex(
        @"""\s*\+|\+\s*""",
        RegexOptions.Compiled);
    // string.Format(
    private static readonly Regex ReStringFormat = new Regex(
        @"\bstring\s*\.\s*Format\s*\(",
        RegexOptions.Compiled);
    // 注释（作用于原始代码）：// 或 /* */
    private static readonly Regex ReComment = new Regex(
        @"//|/\*",
        RegexOptions.Compiled);

    // ---------------- 结构判定正则（API 课） ----------------
    // foreach 遍历
    private static readonly Regex ReForeach = new Regex(
        @"\bforeach\s*\([^;{}]*\bin\b",
        RegexOptions.Compiled);
    // tower.GetAllObjects()
    private static readonly Regex ReGetAllObjects = new Regex(
        @"\btower\s*\.\s*GetAllObjects\s*\(",
        RegexOptions.Compiled);
    // TowerObjectInfo 各字段读取
    private static readonly Regex ReFieldIndex = new Regex(@"\.\s*Index\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldType = new Regex(@"\.\s*Type\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldHp = new Regex(@"\.\s*HP\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldPosition = new Regex(@"\.\s*Position\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldVelocity = new Regex(@"\.\s*Velocity\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldVolume = new Regex(@"\.\s*Volume\b", RegexOptions.Compiled);
    // tower.GetFireCooldown() / FireAtPlane( / PrintLine( / Print( / ClearPrint(
    private static readonly Regex ReGetFireCooldown = new Regex(
        @"\btower\s*\.\s*GetFireCooldown\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ReFireAtPlane = new Regex(
        @"\btower\s*\.\s*FireAtPlane\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex RePrintLine = new Regex(
        @"\btower\s*\.\s*PrintLine\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex RePrintOnly = new Regex(
        @"\btower\s*\.\s*Print\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ReClearPrint = new Regex(
        @"\btower\s*\.\s*ClearPrint\s*\(",
        RegexOptions.Compiled);

    // ================== 静态入口（供 LevelDirector / MainMenuController 调用） ==================

    /// <summary>进入教学关：启动（或重开）指定课程的顺序式教学，进度从任务 1 重新开始。</summary>
    public static void Begin(Course course)
    {
        Begin(course, 0);
    }

    /// <summary>
    /// 进入教学关并从第 startIndex 个任务（0 基）开始（供 /level 1 2 n、/level 2 1 n 章节跳转使用）；
    /// 越界值自动收敛到合法范围，之前（序号更小）的任务标记为已完成。
    /// </summary>
    public static void Begin(Course course, int startIndex)
    {
        CodeSyntaxTutorialController t = Instance;
        if (t == null)
        {
            GameObject go = new GameObject("CodeSyntaxTutorial");
            t = go.AddComponent<CodeSyntaxTutorialController>(); // AddComponent 会立即执行 Awake，Instance 随之就绪
        }
        t.BeginInternal(course, startIndex);
    }

    /// <summary>指定课程的任务（part 章节）总数，供指令框与关卡导演校验章节参数。</summary>
    public static int GetTaskCount(Course course)
    {
        TaskDef[] list = (course == Course.Api) ? ApiTasks : GrammarTasks;
        return list != null ? list.Length : 0;
    }

    /// <summary>离开教学关（回主页 / 跳其他关卡）：结束教学，隐藏任务卡与提示条并清空激活状态。</summary>
    public static void End()
    {
        if (Instance != null)
            Instance.EndInternal();
    }

    // ================== 生命周期 ==================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
        SetAllVisible(false);
    }

    private void OnDestroy()
    {
        CodeEditorController.OnCompiledSuccessfully -= HandleCompiledSuccessfully;
        Localization.OnLanguageChanged -= RefreshText;
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!Active)
            return;

        EnsureRefs();

        // 自动打开编辑器：正常情况当帧即打开；若此时开发者指令框仍开着（/level 的同一帧），
        // 延后到下一帧（指令框关闭后）再重试，避免任务卡无编辑器可依附。
        if (autoOpenPending && editor != null)
        {
            if (!editor.IsOpen)
                editor.Open();
            if (editor.IsOpen)
                autoOpenPending = false;
        }

        // 「任务完成」提示倒计时（真实时间：编辑器打开时 timeScale = 0）
        if (flashTimer > 0f)
        {
            flashTimer -= Time.unscaledDeltaTime;
            if (flashTimer <= 0f && flashText != null)
                flashText.text = "";
        }

        SyncVisibility();

        // 编辑器未打开时不做结构判定（玩家在编辑代码时才推进）
        if (finished || CurrentIndex >= TaskCount)
            return;
        if (editor == null || !editor.IsOpen)
            return;

        checkTimer += Time.unscaledDeltaTime;
        if (checkTimer < 0.15f) // 每 0.15 秒判定一次，避免逐帧跑正则
            return;
        checkTimer = 0f;

        EvaluateCurrentTask();
    }

    // ================== 启动 / 结束 ==================

    private void BeginInternal(Course course, int startIndex)
    {
        EnsureRefs();

        Active = true;
        CurrentCourse = course;
        tasks = (course == Course.Api) ? ApiTasks : GrammarTasks;
        done = new bool[tasks.Length];
        finished = false;

        // 章节定位：startIndex 为 0 基任务序号，越界自动收敛；之前章节标记为已完成（进度格显示已过）
        int start = Mathf.Clamp(startIndex, 0, Mathf.Max(0, tasks.Length - 1));
        for (int i = 0; i < start && i < done.Length; i++)
            done[i] = true;
        CurrentIndex = start;

        compiledThisTask = false;
        compiledRaw = "";
        compiledSnapshot = "";
        checkTimer = 0f;
        flashTimer = 0f;
        hintCache = "";
        statusCache = -1;

        // 若之前是 Level 1 - Part 1 的引导教学，先退出它：编辑器在 TutorialController.Started 期间禁止打开
        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (tutorial != null && tutorial.Started)
            tutorial.ExitToMenu();

        // 关卡表现：两门课都隐藏分数 HUD、显示 print 输出荧幕（要看到 PrintLine 结果）；
        // API 课额外允许蓝色 BOMB 块（教学 o.Type 判断需要两种类型）
        if (course == Course.Api)
            LevelPresentation.ApplyLevel2Api();
        else
            LevelPresentation.ApplyLevel2();

        // 教学期间先不出块：全部任务完成后开始出块，让玩家写的程序立刻「有用武之地」；
        // API 课额外预生成少量静止方块，保证 GetAllObjects / 字段读取任务一上手就有真实数据。
        if (spawner != null)
        {
            spawner.ClearAllBlocks();
            spawner.ResetSession();
            spawner.SuspendSpawning();
            if (course == Course.Api)
                EnsureDemoBlocks();
        }

        // 订阅编译成功事件与语言切换事件（先退订再订阅，防止重复订阅）
        CodeEditorController.OnCompiledSuccessfully -= HandleCompiledSuccessfully;
        CodeEditorController.OnCompiledSuccessfully += HandleCompiledSuccessfully;
        Localization.OnLanguageChanged -= RefreshText;
        Localization.OnLanguageChanged += RefreshText;

        // 清空编辑器内容：避免默认示例代码一进来就满足全部结构判定（注释任务也需玩家自己写）
        if (editor != null)
            editor.SetCodeForTutorial(string.Empty);

        // 布局：上下分栏 —— 上方代码编辑器占满可用区域（代码过长时用编辑器内部竖向滚动条滚动查看），
        // 下方指导框横向占满可用宽度、上边缘与代码编辑器下边缘平齐、下边缘贴屏幕底部，两者不重叠。
        // 指导框高度随任务文案自适应（RefitPanelToContent），并同步给编辑器下边距，保证像素级对齐。
        // 只在本教学（Level 1-Part 2 / Level 2-Part 1）生效，EndInternal 恢复默认全屏布局。
        if (editor != null)
            editor.SetRightReservedWidth(0f); // 上下分栏不再左右平分，右侧不再预留
        // 「选中方块信息」恢复屏幕水平居中（下方指导框已横向占满，不再需要左右错位避让）
        BlockHitIndicator.SetHorizontalOffsetGlobal(0f);

        LayoutChips();
        RefreshText();
        RefitPanelToContent(); // 按当前任务文案算出指导框高度，并把该高度作为编辑器底部预留

        // 进入即打开代码编辑器 —— 教学发生在编辑器页面内
        autoOpenPending = true;
        if (editor != null && !editor.IsOpen)
            editor.Open();
        if (editor != null && editor.IsOpen)
            autoOpenPending = false;

        SyncVisibility();
        Debug.Log("[CodeSyntaxTutorial] 教学开始（" + course + "，共 " + TaskCount + " 项任务）。");
    }

    private void EndInternal()
    {
        EnsureRefs();

        // 离开教学（回主页 / 跳其它关卡）先无条件收尾布局与编辑器：恢复编辑器全屏、强制关闭界面并收起 Enter 提示，
        // 避免「从 Part 2 / API 教学跳到 Level 3」时还带着打开的全屏代码编辑器（本方法可能在教学已结束后被再次调用）。
        if (editor != null)
        {
            editor.SetBottomReservedHeight(0f);
            editor.SetRightReservedWidth(0f);
            editor.ForceCloseSelf();
        }
        BlockHitIndicator.SetHorizontalOffsetGlobal(0f);

        if (!Active)
            return;

        Active = false;
        finished = false;
        CurrentIndex = 0;
        compiledThisTask = false;
        compiledRaw = "";
        compiledSnapshot = "";
        flashTimer = 0f;
        hintCache = "";
        statusCache = -1;
        autoOpenPending = false;
        if (done != null)
        {
            for (int i = 0; i < done.Length; i++)
                done[i] = false;
        }

        CodeEditorController.OnCompiledSuccessfully -= HandleCompiledSuccessfully;
        Localization.OnLanguageChanged -= RefreshText;

        // 布局收尾（恢复编辑器全屏 / 模块居中）已在方法开头无条件完成，这里只复位教学状态
        SetAllVisible(false);
        Debug.Log("[CodeSyntaxTutorial] 教学结束，进度已重置。");
    }

    private void SetAllVisible(bool visible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(visible);
        if (hintRoot != null)
            hintRoot.SetActive(false); // 提示条由 SyncVisibility 按编辑器开关单独控制
    }

    private void EnsureRefs()
    {
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();
        if (spawner == null)
            spawner = FlyingBlockSpawner.Instance != null ? FlyingBlockSpawner.Instance : FindObjectOfType<FlyingBlockSpawner>();
        if (tower == null)
            tower = FindObjectOfType<CodeTower>();
    }

    /// <summary>API 课：补齐至少 4 个场上物体，让 GetAllObjects / 字段读取任务有真实数据可读。</summary>
    private void EnsureDemoBlocks()
    {
        if (spawner == null)
            return;

        int alive = 0;
        for (int i = 0; i < spawner.ActiveBlocks.Count; i++)
        {
            FlyingBlock b = spawner.ActiveBlocks[i];
            if (b != null && !b.Destroyed)
                alive++;
        }
        for (int k = alive; k < 4; k++)
        {
            if (spawner.SpawnBlock() == null)
                break;
        }
    }

    // ================== 任务判定 ==================

    /// <summary>当前代码是否命中当前任务的语法（入参剥离注释 / 字符串后再判定）。</summary>
    private bool StructureHit(string raw, bool useCompiledSnapshot)
    {
        if (tasks == null || CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return false;
        TaskMatch m = tasks[CurrentIndex].Match;
        if (m == null)
            return false;
        if (useCompiledSnapshot)
            return m(compiledRaw ?? string.Empty, compiledSnapshot ?? string.Empty);
        if (string.IsNullOrEmpty(raw))
            return false;
        return m(raw, StripCommentsAndStrings(raw));
    }

    private void EvaluateCurrentTask()
    {
        if (editor == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= TaskCount)
            return;

        string raw = editor.CurrentCode;
        if (string.IsNullOrEmpty(raw))
            return;

        bool hit = StructureHit(raw, false);
        bool compiledHit = compiledThisTask && StructureHit(string.Empty, true);

        int key = (hit ? 1 : 0) | (compiledHit ? 2 : 0);
        if (key != statusCache)
        {
            statusCache = key;
            RefreshText(); // 语法 / 编译状态变化时刷新任务卡上的两条勾选
        }

        if (!hit || !compiledHit)
            return;

        CompleteCurrentTask();
    }

    private void HandleCompiledSuccessfully(string compiledCode)
    {
        if (!Active)
            return;

        compiledThisTask = true;
        compiledRaw = compiledCode ?? string.Empty;
        compiledSnapshot = StripCommentsAndStrings(compiledRaw);

        // API 课：编译后若场上没块了，补几个，保证紧随其后的运行期输出不空
        if (CurrentCourse == Course.Api)
            EnsureDemoBlocks();

        Debug.Log("[CodeSyntaxTutorial] 收到编译成功事件（任务 " + (CurrentIndex + 1) + "/" + TaskCount + "）。");

        // 立即用编译快照判定：编译通过的这版代码本身要满足当前任务的语法
        if (finished || CurrentIndex >= TaskCount)
        {
            RefreshText();
            return;
        }
        if (StructureHit(string.Empty, true))
            CompleteCurrentTask();
        else
            RefreshText();
    }

    private void CompleteCurrentTask()
    {
        if (CurrentIndex < 0 || CurrentIndex >= TaskCount)
            return;

        done[CurrentIndex] = true;
        int completed = CurrentIndex + 1;
        CurrentIndex++;

        // 断点续玩：章节（任务）推进即把「关卡 + 分区 + 章节」写入存档并 Save()
        //   · 语法课（Course.Grammar）= Level 1 - Part 2，API 课（Course.Api）= Level 2 - Part 1；
        //   · CurrentIndex 是 0 基任务序号，存档中的 section 是 1 起章节号，故 +1；
        //   · 若本次推进后全部章节都已完成（整关通关），则清掉属于本分区的断点，下次从该关开头进入。
        GameProgress.GetCourseLocation(CurrentCourse, out int progressLevel, out int progressPart);
        if (CurrentIndex >= TaskCount)
            GameProgress.ClearResumeIfMatches(progressLevel, progressPart);
        else
            GameProgress.SaveResume(progressLevel, progressPart, CurrentIndex + 1);

        // 进入下一个任务：编译标记清零 —— 每个任务都必须重新点一次【编译运行 (F6)】
        compiledThisTask = false;
        compiledRaw = "";
        compiledSnapshot = "";
        statusCache = -1;

        flashTimer = 1.8f;
        SetLabel(flashText, Localization.Pick("任务完成 ✔", "Task completed ✔"));

        Debug.Log("[CodeSyntaxTutorial] 任务 " + completed + "/" + TaskCount + " 完成。");

        if (CurrentIndex >= TaskCount)
        {
            FinishAll();
            return;
        }

        RefreshText();
    }

    private void FinishAll()
    {
        finished = true;
        flashTimer = 3.5f;

        // 逐级解锁：本次课程全部完成即视为整关通关（PlayerPrefs 持久化，跨重启保留）
        //   语法课（Level 1 - Part 2）→ 关卡 1 通关，解锁关卡 2；
        //   API 课（Level 2 - Part 1）→ 关卡 2 通关，解锁关卡 3。
        LevelProgress.MarkLevelCompleted(CurrentCourse == Course.Api ? 2 : 1);

        SetLabel(flashText, Localization.Pick("全部任务完成！", "All tasks done!"));

        // 教学收尾：开始生成方块 —— 玩家写的程序开始自动驱动炮台（炮台本体永久不现身，只在原点工作）
        if (tower != null)
            tower.SetVisualVisible(false);
        if (spawner != null)
            spawner.BeginSpawning(0.6f);

        RefreshText();
        SyncVisibility();
        Debug.Log("[CodeSyntaxTutorial] 教学全部完成：开始生成方块，炮台进入自动运行。");
    }

    /// <summary>剥离注释与字符串 / 字符字面量，避免注释或字符串内容造成结构误判。</summary>
    private static string StripCommentsAndStrings(string code)
    {
        if (string.IsNullOrEmpty(code))
            return string.Empty;

        StringBuilder sb = new StringBuilder(code.Length);
        int i = 0;
        int n = code.Length;
        while (i < n)
        {
            char c = code[i];

            // 行注释 // ...
            if (c == '/' && i + 1 < n && code[i + 1] == '/')
            {
                while (i < n && code[i] != '\n')
                    i++;
                continue;
            }

            // 块注释 /* ... */
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(code[i] == '*' && code[i + 1] == '/'))
                    i++;
                i = Mathf.Min(n, i + 2);
                continue;
            }

            // 字符串字面量 "..."
            if (c == '"')
            {
                i++;
                while (i < n && code[i] != '"')
                {
                    if (code[i] == '\\')
                        i++;
                    i++;
                }
                i++;
                sb.Append("\"\"");
                continue;
            }

            // 字符字面量 '...'
            if (c == '\'')
            {
                i++;
                while (i < n && code[i] != '\'')
                {
                    if (code[i] == '\\')
                        i++;
                    i++;
                }
                i++;
                sb.Append("''");
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    // ================== 文案刷新 ==================

    private void RefreshText()
    {
        RefreshTextCore();
        RefreshFonts();
        // 文案变化后重算指导框高度：保证多样长度的任务文案始终完整落在黑框内
        RefitPanelToContent();
    }

    /// <summary>文本创建时是空串，字体按英文（Consolas）选的；写入中文后必须重选字体，否则中文缺字形。</summary>
    private void RefreshFonts()
    {
        ReloadFont(headlineText);
        ReloadFont(progressText);
        ReloadFont(taskNameText);
        ReloadFont(bodyText);
        ReloadFont(flashText);
        ReloadFont(hintText);
        if (chipTexts != null)
        {
            for (int i = 0; i < chipTexts.Length; i++)
                ReloadFont(chipTexts[i]);
        }
    }

    private static void ReloadFont(Text t)
    {
        if (t == null || string.IsNullOrEmpty(t.text))
            return;
        t.font = UIFontHelper.LoadForText(t.text, t.fontSize);
    }

    /// <summary>赋值并同步字体（中文微软雅黑 / 英文 Consolas）。</summary>
    private static void SetLabel(Text t, string content)
    {
        if (t == null)
            return;
        t.text = content;
        t.font = UIFontHelper.LoadForText(content, t.fontSize);
    }

    private void RefreshTextCore()
    {
        if (taskNameText == null)
            return;

        bool zh = Localization.IsChinese;

        if (headlineText != null)
        {
            if (CurrentCourse == Course.Api)
            {
                headlineText.text = zh
                    ? "关卡 2 · 第 1 部分 · 炮台 API 调用教学"
                    : "Level 2 · Part 1 · Tower API Tutorial";
            }
            else
            {
                headlineText.text = zh
                    ? "关卡 1 · 第 2 部分 · C# 基础语法教学"
                    : "Level 1 · Part 2 · C# Syntax Tutorial";
            }
        }

        // 任务进度格
        for (int i = 0; i < MaxChips && chipTexts != null; i++)
        {
            if (chipTexts[i] == null)
                continue;
            if (i >= TaskCount)
            {
                if (chipBgs[i] != null && chipBgs[i].gameObject.activeSelf)
                    chipBgs[i].gameObject.SetActive(false);
                continue;
            }
            bool isDone = done != null && i < done.Length && done[i];
            bool isCurrent = !finished && i == CurrentIndex;
            chipTexts[i].text = (i + 1).ToString();
            chipTexts[i].color = isDone
                ? new Color(0.06f, 0.12f, 0.09f, 1f)
                : (isCurrent ? new Color(0.06f, 0.10f, 0.16f, 1f) : new Color(0.62f, 0.66f, 0.70f, 1f));
            if (chipBgs[i] != null)
            {
                chipBgs[i].color = isDone
                    ? new Color(0.24f, 0.72f, 0.42f, 0.98f)   // 已完成：绿
                    : (isCurrent
                        ? new Color(0.26f, 0.55f, 0.86f, 0.96f) // 当前：蓝
                        : new Color(0.22f, 0.24f, 0.28f, 0.92f)); // 未开始：灰
            }
        }

        if (progressText != null)
        {
            int shown = Mathf.Min(CurrentIndex + 1, TaskCount);
            progressText.text = finished
                ? (zh ? "全部完成" : "All done")
                : (zh ? "任务 " + shown + "/" + TaskCount : "Task " + shown + "/" + TaskCount);
        }

        if (finished)
        {
            taskNameText.text = zh ? "✔ 教学完成" : "✔ Tutorial complete";
            if (bodyText != null)
            {
                bodyText.text = CurrentCourse == Course.Api
                    ? (zh
                        ? "你已经用到了全部炮台 API：GetAllObjects、TowerObjectInfo 的 Index / Type / HP / Position /\n"
                          + "Velocity / Volume 字段、GetFireCooldown、FireAtPlane、PrintLine / Print / ClearPrint。\n\n"
                          + "关卡已开始刷新方块：按 Esc 关闭编辑器，你写的程序就会每帧自动运行、驱动炮台。\n"
                          + "想继续改进代码：按 Enter 重新打开编辑器修改，按 F6 重新编译运行。\n"
                          + "想从头再学一遍：在开发者指令框输入 /level 2 1，进度会自动重置。"
                        : "You have used the whole tower API: GetAllObjects, TowerObjectInfo.Index / Type / HP /\n"
                          + "Position / Velocity / Volume, GetFireCooldown, FireAtPlane, PrintLine / Print / ClearPrint.\n\n"
                          + "Blocks are now spawning: press Esc to close the editor and your program runs every frame.\n"
                          + "Press Enter to reopen the editor and F6 to recompile.\n"
                          + "Type /level 2 1 in the console to restart this tutorial.")
                    : (zh
                        ? "你已经用到了变量、const、运算符、关系 / 逻辑运算、if / if-else / switch、for / while / do-while、\n"
                          + "数组、List、方法定义与返回值、字符串拼接与 string.Format、注释。\n\n"
                          + "关卡已开始刷新方块：按 Esc 关闭编辑器，你写的程序就会每帧自动运行、驱动炮台。\n"
                          + "想继续改进代码：按 Enter 重新打开编辑器修改，按 F6 重新编译运行。\n"
                          + "想学炮台 API：在开发者指令框输入 /level 2 1。想重学本关：输入 /level 1 2。"
                        : "You have used variables, const, operators, comparisons, logic, if / if-else / switch,\n"
                          + "for / while / do-while, arrays, List, methods & return values, string concat & Format, comments.\n\n"
                          + "Blocks are now spawning: press Esc to close the editor and your program runs every frame.\n"
                          + "Press Enter to reopen the editor and F6 to recompile.\n"
                          + "For the tower API tutorial type /level 2 1; to restart this one type /level 1 2.");
            }
            return;
        }

        int index = Mathf.Clamp(CurrentIndex, 0, Mathf.Max(0, TaskCount - 1));
        if (tasks != null && index < tasks.Length)
        {
            taskNameText.text = zh ? tasks[index].TitleZh : tasks[index].TitleEn;
            if (bodyText != null)
                bodyText.text = BuildBody(index, zh);
        }
    }

    /// <summary>任务正文 = 写法 / 目标 / 提示 / 判定 + 本任务完成条件（语法 & 编译两条状态）。</summary>
    private string BuildBody(int index, bool zh)
    {
        string main = zh ? tasks[index].BodyZh : tasks[index].BodyEn;

        bool hit = false;
        bool compiledHit = false;
        if (editor != null && tasks != null && index == CurrentIndex)
        {
            string raw = editor.CurrentCode;
            if (!string.IsNullOrEmpty(raw))
                hit = StructureHit(raw, false);
            if (compiledThisTask)
                compiledHit = StructureHit(string.Empty, true);
        }

        string compileState;
        if (compiledHit)
        {
            compileState = zh
                ? "✔ 已通过编译验证（编译成功的代码中包含本任务语法）"
                : "✔ Verified by compile (compiled code contains this syntax)";
        }
        else if (compiledThisTask)
        {
            compileState = zh
                ? "✘ 上次编译的代码还不含本任务语法 —— 补全后重新点【编译运行 (F6)】"
                : "✘ Last compiled code lacks this syntax — fix it and click [Compile & Run (F6)] again";
        }
        else
        {
            compileState = zh
                ? "⏳ 等待编译 —— 写出上面的语法后，点标题栏【编译运行 (F6)】验证"
                : "⏳ Not compiled yet — write the syntax, then click [Compile & Run (F6)]";
        }

        string syntaxState = hit
            ? (zh ? "✔ 已命中" : "✔ Found")
            : (zh ? "✘ 未命中 —— 请按上面的写法补全" : "✘ Missing — add it as shown above");

        StringBuilder sb = new StringBuilder(main.Length + 160);
        sb.Append(main);
        sb.Append(zh ? "\n\n—— 本任务完成条件（两项都满足才算完成）——\n" : "\n\n—— Complete when BOTH are satisfied ——\n");
        sb.Append(zh ? "① 语法：" : "① Syntax: ").Append(syntaxState).Append('\n');
        sb.Append(zh ? "② 编译：" : "② Compile: ").Append(compileState);
        return sb.ToString();
    }

    private void SyncVisibility()
    {
        if (panelRoot == null)
            return;

        // 任务卡只在编辑器打开时显示（编辑器关闭时不遮挡玩法，改由底部提示条提示按 Enter 继续）
        bool editorOpen = editor != null && editor.IsOpen;

        if (panelRoot.activeSelf != editorOpen)
            panelRoot.SetActive(editorOpen);

        if (hintRoot != null)
        {
            bool showHint = !editorOpen;
            if (hintRoot.activeSelf != showHint)
                hintRoot.SetActive(showHint);
        }

        if (!editorOpen)
            RefreshHint();
    }

    private void RefreshHint()
    {
        if (hintText == null)
            return;

        bool zh = Localization.IsChinese;
        string text;
        if (finished)
        {
            text = CurrentCourse == Course.Api
                ? (zh ? "炮台 API 教学已完成 —— 按 Enter 可重新打开代码编辑器修改" : "Tower API tutorial complete — press Enter to reopen the editor")
                : (zh ? "C# 语法教学已完成 —— 按 Enter 可重新打开代码编辑器修改" : "C# syntax tutorial complete — press Enter to reopen the editor");
        }
        else
        {
            int shown = Mathf.Clamp(CurrentIndex + 1, 1, Mathf.Max(1, TaskCount));
            string title = "";
            if (tasks != null && CurrentIndex >= 0 && CurrentIndex < tasks.Length)
                title = zh ? tasks[CurrentIndex].TitleZh : tasks[CurrentIndex].TitleEn;
            text = zh
                ? "任务 " + shown + "/" + TaskCount + "：" + title + " —— 按 Enter 打开代码编辑器，写完后点【编译运行 (F6)】"
                : "Task " + shown + "/" + TaskCount + ": " + title + " — press Enter to open the editor, then click [Compile & Run (F6)]";
        }

        if (text == hintCache)
            return;
        hintCache = text;
        SetLabel(hintText, text);
        RefitHintBar(); // 文案长度变化时按实测宽高重排，长文案自动换行加高，不被截断
    }

    // ================== 任务表（C# 基础语法 18 项） ==================

    private static TaskDef[] grammarTasks;

    private static TaskDef[] GrammarTasks
    {
        get
        {
            if (grammarTasks == null)
                grammarTasks = BuildGrammarTasks();
            return grammarTasks;
        }
    }

    private static TaskDef Def(string titleZh, string titleEn, string bodyZh, string bodyEn, TaskMatch match)
    {
        TaskDef t = new TaskDef();
        t.TitleZh = titleZh;
        t.TitleEn = titleEn;
        t.BodyZh = bodyZh;
        t.BodyEn = bodyEn;
        t.Match = match;
        return t;
    }

    private static TaskDef[] BuildGrammarTasks()
    {
        TaskDef[] list = new TaskDef[18];

        list[0] = Def(
            "任务 1：变量声明与类型",
            "Task 1: Variables & Types",
            "写法：类型 变量名 = 初始值;\n"
            + "    int hp = 100;\n"
            + "    float speed = 1.5f;\n"
            + "    bool ready = true;\n"
            + "    string tag = \"tower\";\n\n"
            + "目标：声明 int / float / bool / string 四种变量。\n"
            + "提示：float 字面量要带 f。每个任务的示例都能直接粘贴进编辑器（变量名互不重复），粘完点编译即可。\n"
            + "判定：出现「类型 名字 = 值;」。",
            "Syntax: type name = value;\n"
            + "    int hp = 100;\n"
            + "    float speed = 1.5f;\n"
            + "    bool ready = true;\n"
            + "    string tag = \"tower\";\n\n"
            + "Goal: declare int / float / bool / string variables.\n"
            + "Hint: float literals need the f suffix. Every example can be pasted as-is (names never collide) — then compile.\n"
            + "Check: \"type name = value;\" appears.",
            (raw, code) => ReVariable.IsMatch(code));

        list[1] = Def(
            "任务 2：常量 const",
            "Task 2: const Constants",
            "写法：const 类型 常量名 = 值;\n"
            + "    const int MaxBlocks = 400;\n"
            + "    const float Pi = 3.14f;\n\n"
            + "目标：声明至少 1 个 const 常量。\n"
            + "提示：const 必须在声明时就赋值，之后不能再改。\n"
            + "判定：出现 const 声明。",
            "Syntax: const type NAME = value;\n"
            + "    const int MaxBlocks = 400;\n"
            + "    const float Pi = 3.14f;\n\n"
            + "Goal: declare at least one const.\n"
            + "Hint: a const must be initialised at declaration and can never change.\n"
            + "Check: a const declaration appears.",
            (raw, code) => ReConst.IsMatch(code));

        list[2] = Def(
            "任务 3：算术运算符与表达式",
            "Task 3: Arithmetic Operators",
            "写法：操作数 运算符 操作数，运算符可用 + - * / %\n"
            + "    int total = 2 + 3 * 4;\n"
            + "    float halfTotal = total / 2f;\n"
            + "    int rest = total % 5;\n\n"
            + "目标：写至少 1 个含 + - * / % 的计算式。\n"
            + "提示：* / % 优先级高于 + -，可用括号改变顺序；整数相除会丢掉小数。\n"
            + "判定：出现算术运算表达式。",
            "Syntax: operand op operand, ops: + - * / %\n"
            + "    int total = 2 + 3 * 4;\n"
            + "    float halfTotal = total / 2f;\n"
            + "    int rest = total % 5;\n\n"
            + "Goal: write an expression using + - * / or %.\n"
            + "Hint: * / % bind tighter than + -; int division drops the fraction.\n"
            + "Check: an arithmetic expression appears.",
            (raw, code) => ReArithmetic.IsMatch(code));

        list[3] = Def(
            "任务 4：关系运算符",
            "Task 4: Comparison Operators",
            "写法：操作数 关系运算符 操作数（可用运算符：==  !=  >  <  >=  <=）\n"
            + "    int hpNow = 5;\n"
            + "    bool alive = hpNow > 0;\n"
            + "    bool empty = (hpNow == 0);\n\n"
            + "目标：写至少 1 次比较运算，结果是 bool。\n"
            + "提示：赋值用 =，判断相等要用 ==，别写反。\n"
            + "判定：出现 == != > < >= <= 之一。",
            "Syntax: operand op operand (operators: ==  !=  >  <  >=  <=)\n"
            + "    int hpNow = 5;\n"
            + "    bool alive = hpNow > 0;\n"
            + "    bool empty = (hpNow == 0);\n\n"
            + "Goal: perform at least one comparison yielding a bool.\n"
            + "Hint: = assigns, == compares — do not mix them up.\n"
            + "Check: one of == != > < >= <= appears.",
            (raw, code) => ReRelational.IsMatch(code));

        list[4] = Def(
            "任务 5：逻辑运算符",
            "Task 5: Logical Operators",
            "写法：条件 逻辑运算符 条件，运算符有 &&  ||  !\n"
            + "    bool weaponReady = true;\n"
            + "    int hpLeft = 5;\n"
            + "    bool canFire = weaponReady && hpLeft > 0;\n"
            + "    bool needHeal = hpLeft <= 0 || !weaponReady;\n\n"
            + "目标：用 && 或 || 或 ! 组合条件。\n"
            + "提示：&& 两边都真才真，|| 一边真就真，! 取反。\n"
            + "判定：出现 && || ! 之一。",
            "Syntax: condition op condition, ops: &&  ||  !\n"
            + "    bool weaponReady = true;\n"
            + "    int hpLeft = 5;\n"
            + "    bool canFire = weaponReady && hpLeft > 0;\n"
            + "    bool needHeal = hpLeft <= 0 || !weaponReady;\n\n"
            + "Goal: combine conditions with && || or !.\n"
            + "Hint: && needs both true, || needs one true, ! inverts.\n"
            + "Check: one of && || ! appears.",
            (raw, code) => ReLogical.IsMatch(code));

        list[5] = Def(
            "任务 6：if 条件判断",
            "Task 6: if Statements",
            "写法：if (条件) { 条件成立时执行的语句 }\n"
            + "    int blockCount = tower.GetAllObjects().Count;\n"
            + "    if (blockCount > 0)\n"
            + "    {\n"
            + "        tower.PrintLine(\"blocks={0}\", blockCount);\n"
            + "    }\n\n"
            + "目标：写一个 if 判断。\n"
            + "提示：括号里的条件必须求值为 bool；多条语句用 { } 包起来。\n"
            + "判定：出现 if ( …条件… ) 结构。",
            "Syntax: if (condition) { statements }\n"
            + "    int blockCount = tower.GetAllObjects().Count;\n"
            + "    if (blockCount > 0)\n"
            + "    {\n"
            + "        tower.PrintLine(\"blocks={0}\", blockCount);\n"
            + "    }\n\n"
            + "Goal: write one if statement.\n"
            + "Hint: the condition must evaluate to bool; wrap multiple statements in { }.\n"
            + "Check: an if ( ... ) structure appears.",
            (raw, code) => ReIf.IsMatch(code));

        list[6] = Def(
            "任务 7：if … else …",
            "Task 7: if / else",
            "写法：if (条件) { … } else { … }\n"
            + "    float cd = tower.GetFireCooldown();\n"
            + "    if (cd <= 0f)\n"
            + "    {\n"
            + "        tower.FireAtPlane(0f, 1f, 0f);\n"
            + "    }\n"
            + "    else\n"
            + "    {\n"
            + "        tower.PrintLine(\"cooling down\");\n"
            + "    }\n\n"
            + "目标：写 if 与 else 两个分支。\n"
            + "提示：else 后面不写条件；多层判断用 else if (条件)。\n"
            + "判定：出现 if 且出现 else。",
            "Syntax: if (condition) { ... } else { ... }\n"
            + "    float cd = tower.GetFireCooldown();\n"
            + "    if (cd <= 0f)\n"
            + "    {\n"
            + "        tower.FireAtPlane(0f, 1f, 0f);\n"
            + "    }\n"
            + "    else\n"
            + "    {\n"
            + "        tower.PrintLine(\"cooling down\");\n"
            + "    }\n\n"
            + "Goal: write both an if and an else branch.\n"
            + "Hint: else takes no condition; chain more cases with else if (condition).\n"
            + "Check: both if and else appear.",
            (raw, code) => ReIfElse.IsMatch(code));

        list[7] = Def(
            "任务 8：switch 多分支",
            "Task 8: switch",
            "写法：switch (表达式) { case 值: … break; default: … break; }\n"
            + "    int mode = 1;\n"
            + "    switch (mode)\n"
            + "    {\n"
            + "        case 0: tower.PrintLine(\"zero\"); break;\n"
            + "        case 1: tower.PrintLine(\"one\"); break;\n"
            + "        default: tower.PrintLine(\"other\"); break;\n"
            + "    }\n\n"
            + "目标：写一个 switch 多分支。\n"
            + "提示：每个分支结尾要 break，否则会继续往下执行；default 处理其余情况。\n"
            + "判定：出现 switch ( … ) { case 结构。",
            "Syntax: switch (expr) { case v: ... break; default: ... break; }\n"
            + "    int mode = 1;\n"
            + "    switch (mode)\n"
            + "    {\n"
            + "        case 0: tower.PrintLine(\"zero\"); break;\n"
            + "        case 1: tower.PrintLine(\"one\"); break;\n"
            + "        default: tower.PrintLine(\"other\"); break;\n"
            + "    }\n\n"
            + "Goal: write one switch statement.\n"
            + "Hint: end each case with break; default catches the rest.\n"
            + "Check: switch ( ... ) { case appears.",
            (raw, code) => ReSwitch.IsMatch(code));

        list[8] = Def(
            "任务 9：for 循环",
            "Task 9: for Loops",
            "写法：for (初始化; 条件; 迭代)\n"
            + "    for (int i = 0; i < 3; i++)\n"
            + "    {\n"
            + "        tower.PrintLine(\"i={0}\", i);\n"
            + "    }\n\n"
            + "目标：写一个 for 循环。\n"
            + "提示：三段分别用分号隔开：初始化; 终止条件; 每轮结束后的迭代。\n"
            + "判定：出现 for ( … ; … ; ) 结构。",
            "Syntax: for (init; condition; step)\n"
            + "    for (int i = 0; i < 3; i++)\n"
            + "    {\n"
            + "        tower.PrintLine(\"i={0}\", i);\n"
            + "    }\n\n"
            + "Goal: write one for loop.\n"
            + "Hint: three parts separated by semicolons: init; stop condition; step.\n"
            + "Check: a for ( ... ; ... ; ) structure appears.",
            (raw, code) => ReFor.IsMatch(code));

        list[9] = Def(
            "任务 10：while 循环",
            "Task 10: while Loops",
            "写法：while (条件) { 循环体 }\n"
            + "    int wi = 0;\n"
            + "    while (wi < 3)\n"
            + "    {\n"
            + "        wi++;\n"
            + "    }\n\n"
            + "目标：写一个 while 循环。\n"
            + "提示：循环体里要改变条件变量，否则会死循环。\n"
            + "判定：出现 while ( … ) { 结构。",
            "Syntax: while (condition) { body }\n"
            + "    int wi = 0;\n"
            + "    while (wi < 3)\n"
            + "    {\n"
            + "        wi++;\n"
            + "    }\n\n"
            + "Goal: write one while loop.\n"
            + "Hint: change the condition variable inside the body, or it never ends.\n"
            + "Check: a while ( ... ) { structure appears.",
            (raw, code) => ReWhile.IsMatch(code));

        list[10] = Def(
            "任务 11：do … while 循环",
            "Task 11: do / while Loops",
            "写法：do { 循环体 } while (条件);\n"
            + "    int di = 0;\n"
            + "    do\n"
            + "    {\n"
            + "        di++;\n"
            + "    } while (di < 3);\n\n"
            + "目标：写一个 do … while 循环。\n"
            + "提示：do 的循环体至少执行 1 次，条件写在最后且末尾要加分号。\n"
            + "判定：出现 do { … } while ( … ); 结构。",
            "Syntax: do { body } while (condition);\n"
            + "    int di = 0;\n"
            + "    do\n"
            + "    {\n"
            + "        di++;\n"
            + "    } while (di < 3);\n\n"
            + "Goal: write one do ... while loop.\n"
            + "Hint: the body runs at least once; the condition comes last and needs a semicolon.\n"
            + "Check: a do { ... } while ( ... ); structure appears.",
            (raw, code) => ReDoWhile.IsMatch(code));

        list[11] = Def(
            "任务 12：一维数组",
            "Task 12: Arrays",
            "写法：类型[] 名字 = new 类型[长度];\n"
            + "    int[] hits = new int[3];\n"
            + "    hits[0] = 2;\n"
            + "    tower.PrintLine(\"hits[0]={0}\", hits[0]);\n\n"
            + "目标：声明一维数组并读写元素。\n"
            + "提示：下标从 0 开始，长度 3 的合法下标是 0/1/2，越界会报错。\n"
            + "判定：出现 new 类型[长度] 或 名字[下标]。",
            "Syntax: type[] name = new type[length];\n"
            + "    int[] hits = new int[3];\n"
            + "    hits[0] = 2;\n"
            + "    tower.PrintLine(\"hits[0]={0}\", hits[0]);\n\n"
            + "Goal: declare an array and read/write an element.\n"
            + "Hint: indexes start at 0; a length of 3 allows 0/1/2 only.\n"
            + "Check: new type[length] or name[index] appears.",
            (raw, code) => ReArray.IsMatch(code));

        list[12] = Def(
            "任务 13：List 集合",
            "Task 13: List<T>",
            "写法：List<类型> 名字 = new List<类型>();\n"
            + "    List<int> hpList = new List<int>();\n"
            + "    hpList.Add(3);\n"
            + "    tower.PrintLine(\"count={0}\", hpList.Count);\n\n"
            + "目标：声明 List 并添加至少 1 个元素。\n"
            + "提示：编辑器模板已带 using System.Collections.Generic;；用 Add 追加、Count 取数量。\n"
            + "判定：出现 new List<…> 且出现 .Add(。",
            "Syntax: List<T> name = new List<T>();\n"
            + "    List<int> hpList = new List<int>();\n"
            + "    hpList.Add(3);\n"
            + "    tower.PrintLine(\"count={0}\", hpList.Count);\n\n"
            + "Goal: declare a List and add at least one element.\n"
            + "Hint: the editor template already includes using System.Collections.Generic; use Add to append and Count to read the size.\n"
            + "Check: new List<...> and .Add( both appear.",
            (raw, code) => ReListNew.IsMatch(code) && ReListAdd.IsMatch(code));

        list[13] = Def(
            "任务 14：方法定义与参数",
            "Task 14: Methods & Parameters",
            "写法：返回类型 方法名(参数类型 参数名, …) { 方法体 }\n"
            + "    void ShowHp(int hp)\n"
            + "    {\n"
            + "        tower.PrintLine(\"hp={0}\", hp);\n"
            + "    }\n\n"
            + "    ShowHp(3);\n\n"
            + "目标：定义 1 个带参数的方法。\n"
            + "提示：方法写在 Run 里面（局部函数），先定义后调用。\n"
            + "判定：出现「类型 名字(参数…) {」。",
            "Syntax: returnType name(type arg, ...) { body }\n"
            + "    void ShowHp(int hp)\n"
            + "    {\n"
            + "        tower.PrintLine(\"hp={0}\", hp);\n"
            + "    }\n\n"
            + "    ShowHp(3);\n\n"
            + "Goal: define one method that takes parameters.\n"
            + "Hint: declare it inside Run (a local function) before calling it.\n"
            + "Check: \"type name(args...) {\" appears.",
            (raw, code) => ReMethodDef.IsMatch(code));

        list[14] = Def(
            "任务 15：方法返回值 return",
            "Task 15: Return Values",
            "写法：return 表达式; 并用变量接收调用结果\n"
            + "    int Twice(int v)\n"
            + "    {\n"
            + "        return v * 2;\n"
            + "    }\n\n"
            + "    int sum = Twice(2);\n"
            + "    tower.PrintLine(\"sum={0}\", sum);\n\n"
            + "目标：用变量接收方法返回值。\n"
            + "提示：return 后面跟表达式；返回值类型要与方法声明一致。\n"
            + "判定：出现 return …; 且出现「变量 = 方法调用(」。",
            "Syntax: return expression; and receive it into a variable\n"
            + "    int Twice(int v)\n"
            + "    {\n"
            + "        return v * 2;\n"
            + "    }\n\n"
            + "    int sum = Twice(2);\n"
            + "    tower.PrintLine(\"sum={0}\", sum);\n\n"
            + "Goal: store a method's return value in a variable.\n"
            + "Hint: return takes an expression whose type matches the declaration.\n"
            + "Check: return ...; and \"var = method(\" both appear.",
            (raw, code) => ReReturn.IsMatch(code) && ReMethodCallAssign.IsMatch(code));

        list[15] = Def(
            "任务 16：字符串拼接",
            "Task 16: String Concatenation",
            "写法：用 + 拼接字符串与其他值\n"
            + "    int curHp = 3;\n"
            + "    string msg = \"hp=\" + curHp;\n"
            + "    tower.PrintLine(msg + \" / done\");\n\n"
            + "目标：用 + 拼接字符串与其他值。\n"
            + "提示：数字会自动转成字符串，多个值用多个 + 连起来。\n"
            + "判定：出现「字符串 + 值」。",
            "Syntax: join strings and values with +\n"
            + "    int curHp = 3;\n"
            + "    string msg = \"hp=\" + curHp;\n"
            + "    tower.PrintLine(msg + \" / done\");\n\n"
            + "Goal: concatenate a string with other values using +.\n"
            + "Hint: numbers are converted to text automatically; chain more + for more values.\n"
            + "Check: \"string + value\" appears.",
            (raw, code) => ReConcat.IsMatch(code));

        list[16] = Def(
            "任务 17：string.Format 格式化",
            "Task 17: string.Format",
            "写法：string.Format(\"模板 {0} {1}\", 值1, 值2)\n"
            + "    int baseHp = 3;\n"
            + "    string msgFmt = string.Format(\"hp={0}, half={1:F1}\", baseHp, baseHp / 2f);\n"
            + "    tower.PrintLine(msgFmt);\n\n"
            + "目标：用 string.Format 生成格式化字符串。\n"
            + "提示：{0} {1} 是占位符，按顺序取后面的参数；{1:F1} 表示保留 1 位小数。\n"
            + "判定：出现 string.Format(。",
            "Syntax: string.Format(\"template {0} {1}\", v1, v2)\n"
            + "    int baseHp = 3;\n"
            + "    string msgFmt = string.Format(\"hp={0}, half={1:F1}\", baseHp, baseHp / 2f);\n"
            + "    tower.PrintLine(msgFmt);\n\n"
            + "Goal: build a formatted string with string.Format.\n"
            + "Hint: {0} {1} are placeholders filled by the following arguments; {1:F1} keeps one decimal.\n"
            + "Check: string.Format( appears.",
            (raw, code) => ReStringFormat.IsMatch(code));

        list[17] = Def(
            "任务 18：注释",
            "Task 18: Comments",
            "写法：// 单行注释      /* 多行注释 */\n"
            + "    // 计算剩余血量\n"
            + "    int remainHp = 3;\n"
            + "    /* 下面这行打印到荧幕\n"
            + "       多行注释可以换行 */\n"
            + "    tower.PrintLine(\"hp={0}\", remainHp);\n\n"
            + "目标：给代码加至少 1 条注释。\n"
            + "提示：注释不参与运行；养成在关键逻辑上方写一行说明的习惯。\n"
            + "判定：代码里出现 // 或 /*（写在字符串里的不算）。",
            "Syntax: // single line      /* multi line */\n"
            + "    // remaining hp\n"
            + "    int remainHp = 3;\n"
            + "    /* this prints to the screen\n"
            + "       multi-line comments can wrap */\n"
            + "    tower.PrintLine(\"hp={0}\", remainHp);\n\n"
            + "Goal: add at least one comment.\n"
            + "Hint: comments never execute; document key logic above it.\n"
            + "Check: // or /* appears in the code (not inside a string).",
            (raw, code) => ReComment.IsMatch(raw ?? string.Empty));

        return list;
    }

    // ================== 任务表（炮台 API 调用 13 项） ==================

    private static TaskDef[] apiTasks;

    private static TaskDef[] ApiTasks
    {
        get
        {
            if (apiTasks == null)
                apiTasks = BuildApiTasks();
            return apiTasks;
        }
    }

    private static TaskDef[] BuildApiTasks()
    {
        TaskDef[] list = new TaskDef[13];

        list[0] = Def(
            "任务 1：GetAllObjects 取对象列表",
            "Task 1: GetAllObjects",
            "签名：List<TowerObjectInfo> GetAllObjects();\n"
            + "返回：场上所有飞行方块的列表（可能为空）。\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"object {0}\", o.Index);\n"
            + "    }\n\n"
            + "目标：用 foreach 遍历 tower.GetAllObjects() 的返回值。\n"
            + "提示：列表为空时循环体一次都不会执行，属正常现象。\n"
            + "判定：代码里同时出现 foreach 与 tower.GetAllObjects(。",
            "Signature: List<TowerObjectInfo> GetAllObjects();\n"
            + "Returns: every flying block currently in the scene (may be empty).\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"object {0}\", o.Index);\n"
            + "    }\n\n"
            + "Goal: iterate the result of tower.GetAllObjects() with foreach.\n"
            + "Hint: an empty list simply means the body never runs.\n"
            + "Check: both foreach and tower.GetAllObjects( appear.",
            (raw, code) => ReForeach.IsMatch(code) && ReGetAllObjects.IsMatch(code));

        list[1] = Def(
            "任务 2：读取 Index 编号",
            "Task 2: Read Index",
            "字段：int Index;   // 方块生成编号（全局递增，非数组下标）\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0}\", o.Index);\n"
            + "    }\n\n"
            + "目标：读取并输出某个物体的 Index。\n"
            + "提示：Index 只用于标识身份，不表示位置或顺序。\n"
            + "判定：出现「.Index」。",
            "Field: int Index;   // spawn serial number (global counter, not an array index)\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0}\", o.Index);\n"
            + "    }\n\n"
            + "Goal: read and print an object's Index.\n"
            + "Hint: Index identifies the block; it is not a position or order.\n"
            + "Check: \".Index\" appears.",
            (raw, code) => ReFieldIndex.IsMatch(code));

        list[2] = Def(
            "任务 3：读取 Type 类型",
            "Task 3: Read Type",
            "字段：string Type;   // \"ATTACK\" 红块 / \"BOMB\" 蓝块\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\")\n"
            + "            tower.PrintLine(\"red #{0}\", o.Index);\n"
            + "    }\n\n"
            + "目标：读取 Type 并按类型做判断。\n"
            + "提示：Type 是字符串，比较要用 == 且带引号；红块击毁加分，蓝块不加分。\n"
            + "判定：出现「.Type」。",
            "Field: string Type;   // \"ATTACK\" red / \"BOMB\" blue\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\")\n"
            + "            tower.PrintLine(\"red #{0}\", o.Index);\n"
            + "    }\n\n"
            + "Goal: read Type and branch on it.\n"
            + "Hint: Type is a string — compare with == and quotes.\n"
            + "Check: \".Type\" appears.",
            (raw, code) => ReFieldType.IsMatch(code));

        list[3] = Def(
            "任务 4：读取 HP 剩余血量",
            "Task 4: Read HP",
            "字段：int HP;   // 剩余受击层数（各块不同，最多 6 层；吃光即被击毁）\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.HP <= 1)\n"
            + "            tower.PrintLine(\"#{0} almost dead\", o.Index);\n"
            + "    }\n\n"
            + "目标：读取 HP 并按血量做判断。\n"
            + "提示：HP 越小越接近被击毁，可用来优先攻击残血目标。\n"
            + "判定：出现「.HP」。",
            "Field: int HP;   // remaining hit layers (varies per block, up to 7; destroyed at 0)\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.HP <= 1)\n"
            + "            tower.PrintLine(\"#{0} almost dead\", o.Index);\n"
            + "    }\n\n"
            + "Goal: read HP and branch on it.\n"
            + "Hint: lower HP means closer to destruction — good for finishing targets.\n"
            + "Check: \".HP\" appears.",
            (raw, code) => ReFieldHp.IsMatch(code));

        list[4] = Def(
            "任务 5：读取 Position 位置",
            "Task 5: Read Position",
            "字段：Vector3 Position;   // 几何中心的世界坐标\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        Vector3 p = o.Position;\n"
            + "        tower.PrintLine(\"#{0} at ({1:F1},{2:F1},{3:F1})\", o.Index, p.x, p.y, p.z);\n"
            + "    }\n\n"
            + "目标：读取 Position 并取出 x / y / z。\n"
            + "提示：Position 是 Vector3，用 .x .y .z 访问分量；炮台在原点朝 +X 发射。\n"
            + "判定：出现「.Position」。",
            "Field: Vector3 Position;   // world-space geometric center\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        Vector3 p = o.Position;\n"
            + "        tower.PrintLine(\"#{0} at ({1:F1},{2:F1},{3:F1})\", o.Index, p.x, p.y, p.z);\n"
            + "    }\n\n"
            + "Goal: read Position and use x / y / z.\n"
            + "Hint: Position is a Vector3; the tower sits at the origin and fires toward +X.\n"
            + "Check: \".Position\" appears.",
            (raw, code) => ReFieldPosition.IsMatch(code));

        list[5] = Def(
            "任务 6：读取 Velocity 速度",
            "Task 6: Read Velocity",
            "字段：Vector3 Velocity;   // 速度向量（方向 × 速度，单位/秒）\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        Vector3 v = o.Velocity;\n"
            + "        tower.PrintLine(\"#{0} speed={1:F2}\", o.Index, v.magnitude);\n"
            + "    }\n\n"
            + "目标：读取 Velocity 并求速度大小。\n"
            + "提示：v.magnitude 是速度标量；v.normalized 是飞行方向。\n"
            + "判定：出现「.Velocity」。",
            "Field: Vector3 Velocity;   // velocity vector (direction x speed, units/sec)\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        Vector3 v = o.Velocity;\n"
            + "        tower.PrintLine(\"#{0} speed={1:F2}\", o.Index, v.magnitude);\n"
            + "    }\n\n"
            + "Goal: read Velocity and get its magnitude.\n"
            + "Hint: v.magnitude is the scalar speed; v.normalized is the flight direction.\n"
            + "Check: \".Velocity\" appears.",
            (raw, code) => ReFieldVelocity.IsMatch(code));

        list[6] = Def(
            "任务 7：读取 Volume 体积",
            "Task 7: Read Volume",
            "字段：float Volume;   // 当前剩余体积（越打越小）；float InitialVolume;   // 初始体积（切割后不变）\n"
            + "int MaxHits;   // 初始受击层数；Vector3 Size;   // 当前三边尺寸\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "        tower.PrintLine(\"#{0} vol={1:F2}/{2:F2}\", o.Index, o.Volume, o.InitialVolume);\n\n"
            + "目标：读取 Volume（可同时用 InitialVolume 算剩余比例）。\n"
            + "提示：Volume / InitialVolume 可以估收益，越大越值得打。\n"
            + "判定：出现「.Volume」。",
            "Fields: float Volume;   // current remaining volume (shrinks when hit)\n"
            + "float InitialVolume;   // initial volume (constant); int MaxHits;   // initial hit layers\n"
            + "Vector3 Size;   // current edge sizes\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "        tower.PrintLine(\"#{0} vol={1:F2}/{2:F2}\", o.Index, o.Volume, o.InitialVolume);\n\n"
            + "Goal: read Volume (optionally with InitialVolume for a ratio).\n"
            + "Hint: Volume / InitialVolume estimates the value of a target.\n"
            + "Check: \".Volume\" appears.",
            (raw, code) => ReFieldVolume.IsMatch(code));

        list[7] = Def(
            "任务 8：GetFireCooldown 冷却查询",
            "Task 8: GetFireCooldown",
            "签名：float GetFireCooldown();   // 距下一发可发射的剩余秒数，0 = 已就绪\n"
            + "    float cd = tower.GetFireCooldown();\n"
            + "    if (cd <= 0f)\n"
            + "        tower.PrintLine(\"ready to fire\");\n\n"
            + "目标：调用 GetFireCooldown 并根据冷却决定是否开火。\n"
            + "提示：冷却未好时 FireAtPlane 会返回 false，先判断冷却能省掉无效调用。\n"
            + "判定：出现「tower.GetFireCooldown(」。",
            "Signature: float GetFireCooldown();   // seconds until next shot; 0 = ready\n"
            + "    float cd = tower.GetFireCooldown();\n"
            + "    if (cd <= 0f)\n"
            + "        tower.PrintLine(\"ready to fire\");\n\n"
            + "Goal: call GetFireCooldown and gate firing on it.\n"
            + "Hint: FireAtPlane returns false while cooling down.\n"
            + "Check: \"tower.GetFireCooldown(\" appears.",
            (raw, code) => ReGetFireCooldown.IsMatch(code));

        list[8] = Def(
            "任务 9：FireAtPlane 发射",
            "Task 9: FireAtPlane",
            "签名：bool FireAtPlane(float A, float B, float C);\n"
            + "说明：沿过原点平面 A*x+B*y+C*z=0 的法线方向 (A,B,C) 发射；冷却中返回 false。\n"
            + "约束：平面必过原点（炮台就在原点），所以只需 A、B、C 三个参数，没有 D。\n"
            + "    bool fired = tower.FireAtPlane(1f, 0f, 0f);   // 法线 (1,0,0)，沿 +X 发射\n"
            + "    tower.PrintLine(\"fired={0}\", fired);\n\n"
            + "目标：调用一次 FireAtPlane 发射。\n"
            + "提示：A B C 就是法向量（不能全 0）；要打某个目标，直接传它的坐标。\n"
            + "判定：出现「tower.FireAtPlane(」。",
            "Signature: bool FireAtPlane(float A, float B, float C);\n"
            + "Note: fires along the normal (A,B,C) of the origin-passing plane A*x+B*y+C*z=0; false while cooling down.\n"
            + "Constraint: the plane always passes through the origin (the tower is at the origin), so only A, B, C are needed - there is no D.\n"
            + "    bool fired = tower.FireAtPlane(1f, 0f, 0f);   // normal (1,0,0): fires along +X\n"
            + "    tower.PrintLine(\"fired={0}\", fired);\n\n"
            + "Goal: call FireAtPlane once.\n"
            + "Hint: (A,B,C) is the normal and must not be zero; to aim at a target just pass its position.\n"
            + "Check: \"tower.FireAtPlane(\" appears.",
            (raw, code) => ReFireAtPlane.IsMatch(code));

        list[9] = Def(
            "任务 10：PrintLine 打印换行",
            "Task 10: PrintLine",
            "签名：void PrintLine(string format, params object[] args);\n"
            + "说明：输出到 +X 前方荧幕并换行，占位符同 string.Format。\n"
            + "    tower.PrintLine(\"count={0}\", tower.GetAllObjects().Count);\n\n"
            + "目标：用 PrintLine 输出一行文字。\n"
            + "提示：荧幕最多保留 24 行，超出会自动丢弃最旧的；调试循环里别每帧狂打印。\n"
            + "判定：出现「tower.PrintLine(」。",
            "Signature: void PrintLine(string format, params object[] args);\n"
            + "Note: prints one line to the +X screen; same placeholders as string.Format.\n"
            + "    tower.PrintLine(\"count={0}\", tower.GetAllObjects().Count);\n\n"
            + "Goal: print one line with PrintLine.\n"
            + "Hint: the screen keeps 24 lines; avoid printing every frame in a loop.\n"
            + "Check: \"tower.PrintLine(\" appears.",
            (raw, code) => RePrintLine.IsMatch(code));

        list[10] = Def(
            "任务 11：Print 打印不换行",
            "Task 11: Print",
            "签名：void Print(string format, params object[] args);\n"
            + "说明：输出到荧幕但不换行，适合在同一行拼多段内容。\n"
            + "    tower.ClearPrint();\n"
            + "    tower.Print(\"count={0} \", tower.GetAllObjects().Count);\n"
            + "    tower.PrintLine(\"done\");\n\n"
            + "目标：用 Print 输出一段不换行的文字。\n"
            + "提示：Print 与 PrintLine 的区别只有是否换行；同一行拼内容常用「Print + PrintLine 收尾」。\n"
            + "判定：出现「tower.Print(」（PrintLine 不算）。",
            "Signature: void Print(string format, params object[] args);\n"
            + "Note: prints without a trailing newline - handy for building one line piecewise.\n"
            + "    tower.ClearPrint();\n"
            + "    tower.Print(\"count={0} \", tower.GetAllObjects().Count);\n"
            + "    tower.PrintLine(\"done\");\n\n"
            + "Goal: use Print for a non-newline output.\n"
            + "Hint: Print vs PrintLine differs only in the trailing newline.\n"
            + "Check: \"tower.Print(\" appears (PrintLine does not count).",
            (raw, code) => RePrintOnly.IsMatch(code));

        list[11] = Def(
            "任务 12：ClearPrint 清空荧幕",
            "Task 12: ClearPrint",
            "签名：void ClearPrint();   // 清空荧幕全部内容\n"
            + "    tower.ClearPrint();\n"
            + "    tower.PrintLine(\"screen cleared\");\n\n"
            + "目标：调用 ClearPrint 清空一次荧幕。\n"
            + "提示：Run 每帧都会执行，无条件 ClearPrint 会导致荧幕始终只有一行；配合计数条件使用。\n"
            + "判定：出现「tower.ClearPrint(」。",
            "Signature: void ClearPrint();   // clears the whole screen\n"
            + "    tower.ClearPrint();\n"
            + "    tower.PrintLine(\"screen cleared\");\n\n"
            + "Goal: call ClearPrint once.\n"
            + "Hint: Run executes every frame, so an unconditional ClearPrint keeps the screen at one line.\n"
            + "Check: \"tower.ClearPrint(\" appears.",
            (raw, code) => ReClearPrint.IsMatch(code));

        list[12] = Def(
            "任务 13：实战组合（遍历 + 判断 + 发射）",
            "Task 13: Combine (loop + if + fire)",
            "把三个 API 串起来：遍历对象 → 判断类型 → 打最靠前的一个\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\" && o.Position.x > 0f)\n"
            + "        {\n"
            + "            tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z);   // 目标坐标即法向量\n"
            + "            break;\n"
            + "        }\n"
            + "    }\n\n"
            + "目标：同一段代码里同时出现 foreach、if 判断与 FireAtPlane 发射。\n"
            + "提示：命中一个就 break，避免一帧内重复调用浪费冷却；把目标的坐标当法向量传给 A B C 即可瞄准它。\n"
            + "判定：同时出现 foreach、if 与 tower.FireAtPlane(。",
            "Chain three APIs: iterate → filter by type → shoot one target\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\" && o.Position.x > 0f)\n"
            + "        {\n"
            + "            tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z);   // target position is the normal\n"
            + "            break;\n"
            + "        }\n"
            + "    }\n\n"
            + "Goal: have foreach, an if test and FireAtPlane in the same code.\n"
            + "Hint: break after the first hit to avoid wasting cooldown; pass the target position as the normal to aim at it.\n"
            + "Check: foreach, if and tower.FireAtPlane( all appear.",
            (raw, code) => ReForeach.IsMatch(code) && ReIf.IsMatch(code) && ReFireAtPlane.IsMatch(code));

        return list;
    }

    // ================== UI 构建 ==================

    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 叠在代码编辑器（sortingOrder = 200）之上，保证任务卡常驻可见
        canvas.sortingOrder = 210;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        chipTexts = new Text[MaxChips];
        chipBgs = new Image[MaxChips];

        BuildTaskPanel(canvas.transform);
        BuildHintBar(canvas.transform);
    }

    private void BuildTaskPanel(Transform parent)
    {
        // 任务卡（指导框）：屏幕底部横向整条 —— 上下分栏布局。
        //   · 上边缘与代码编辑器（TowerCodeEditor）下边缘严丝合缝：面板高度会同步给
        //     CodeEditorController.SetBottomReservedHeight，编辑器整体上移收窄到上半区，两者不重叠；
        //   · 下边缘贴屏幕底部（anchoredPosition = 0），左右铺满整个可用宽度（anchorMin.x = 0 / anchorMax.x = 1）；
        //   · 高度按当前任务文案自适应（RefitPanelToContent），保证写法 / 示例 / 目标 / 提示 / 判定等文案
        //     完整落在黑框内，不再出现文字超出黑框的情况。
        RectTransform panelRt = CreateRect("SyntaxTaskPanel", parent, new Vector2(0f, 0f), Vector2.zero,
            new Vector2(0f, GuidePanelHeightMin), new Vector2(0.5f, 0f));
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(1f, 0f);
        panelRt.pivot = new Vector2(0.5f, 0f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(0f, GuidePanelHeightMin);
        panelRoot = panelRt.gameObject;

        Image bg = panelRoot.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(0.055f, 0.075f, 0.105f, 0.93f);
        bg.raycastTarget = false; // 不挡编辑器输入：点击穿透到代码区

        // 顶部强调线（横向铺满）
        RectTransform accentRt = CreateRect("Accent", panelRt, new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 3f), new Vector2(0.5f, 1f));
        accentRt.anchorMin = new Vector2(0f, 1f);
        accentRt.anchorMax = new Vector2(1f, 1f);
        accentRt.pivot = new Vector2(0.5f, 1f);
        accentRt.anchoredPosition = Vector2.zero;
        accentRt.sizeDelta = new Vector2(0f, 3f);
        Image accent = accentRt.gameObject.AddComponent<Image>();
        accent.sprite = WhiteSprite();
        accent.color = new Color(0.26f, 0.62f, 0.92f, 0.95f);
        accent.raycastTarget = false;

        // 标题
        headlineText = CreateText("Headline", panelRt, "", 24,
            new Color(0.55f, 0.78f, 0.96f, 1f), TextAnchor.MiddleLeft);
        RectTransform hlRt = headlineText.rectTransform;
        hlRt.anchorMin = new Vector2(0f, 1f);
        hlRt.anchorMax = new Vector2(0f, 1f);
        hlRt.pivot = new Vector2(0f, 1f);
        hlRt.anchoredPosition = new Vector2(24f, -12f);
        hlRt.sizeDelta = new Vector2(700f, 30f);

        // 进度「任务 k/18」
        progressText = CreateText("Progress", panelRt, "", 21,
            new Color(0.82f, 0.86f, 0.90f, 1f), TextAnchor.MiddleRight);
        RectTransform pbRt = progressText.rectTransform;
        pbRt.anchorMin = new Vector2(1f, 1f);
        pbRt.anchorMax = new Vector2(1f, 1f);
        pbRt.pivot = new Vector2(1f, 1f);
        pbRt.anchoredPosition = new Vector2(-24f, -12f);
        pbRt.sizeDelta = new Vector2(220f, 30f);

        // 进度小格（预建 MaxChips 个，按当前课程任务数动态排布 / 显隐；宽度在 LayoutChips 里按面板实际宽度计算）
        for (int i = 0; i < MaxChips; i++)
        {
            RectTransform chipRt = CreateRect("Chip" + (i + 1), panelRt, new Vector2(0f, 1f), Vector2.zero,
                new Vector2(110f, 26f), new Vector2(0f, 1f));
            chipRt.anchoredPosition = new Vector2(24f + i * 114f, -50f);

            Image chipBg = chipRt.gameObject.AddComponent<Image>();
            chipBg.sprite = WhiteSprite();
            chipBg.color = new Color(0.22f, 0.24f, 0.28f, 0.92f);
            chipBg.raycastTarget = false;
            chipBgs[i] = chipBg;

            Text chipText = CreateText("ChipText" + (i + 1), chipRt, (i + 1).ToString(), 17,
                new Color(0.62f, 0.66f, 0.70f, 1f), TextAnchor.MiddleCenter);
            Stretch(chipText.rectTransform);
            chipText.raycastTarget = false;
            chipTexts[i] = chipText;

            chipRt.gameObject.SetActive(false);
        }

        // 任务名（横向铺满，右侧留 24 内边距）
        taskNameText = CreateText("TaskName", panelRt, "", 26, Color.white, TextAnchor.MiddleLeft);
        RectTransform tnRt = taskNameText.rectTransform;
        tnRt.anchorMin = new Vector2(0f, 1f);
        tnRt.anchorMax = new Vector2(1f, 1f);
        tnRt.pivot = new Vector2(0f, 1f);
        tnRt.anchoredPosition = new Vector2(24f, -88f);
        tnRt.sizeDelta = new Vector2(-48f, 36f);

        // 正文（写法 / 目标 / 提示 / 判定 + 完成条件）：上下边距固定，正文区高度 = 面板高度 - 上下留白，
        // 因此面板高度按正文实测高度反推（RefitPanelToContent），文案永不溢出黑框。
        bodyText = CreateText("Body", panelRt, "", GuideBodyFontSize,
            new Color(0.86f, 0.89f, 0.92f, 1f), TextAnchor.UpperLeft);
        RectTransform bodyRt = bodyText.rectTransform;
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.offsetMin = new Vector2(24f, GuideBodyBottomInset);
        bodyRt.offsetMax = new Vector2(-24f, -GuideBodyTopInset);
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow; // 文案先完整排版，再由面板高度兜住
        bodyText.lineSpacing = GuideBodyLineSpacing;
        bodyText.raycastTarget = false;

        // 「任务完成 ✔」提示
        flashText = CreateText("Flash", panelRt, "", 20,
            new Color(0.42f, 0.92f, 0.60f, 1f), TextAnchor.MiddleLeft);
        RectTransform flRt = flashText.rectTransform;
        flRt.anchorMin = new Vector2(0f, 0f);
        flRt.anchorMax = new Vector2(1f, 0f);
        flRt.pivot = new Vector2(0f, 0f);
        flRt.anchoredPosition = new Vector2(24f, 12f);
        flRt.sizeDelta = new Vector2(-48f, 28f);
        flashText.raycastTarget = false;

        panelRt.gameObject.SetActive(false);

        LayoutChips();
        RefitPanelToContent();
    }

    /// <summary>按当前课程任务数重新排布进度格（18 项时格子变窄，13 项时放宽），并隐藏多余格子。</summary>
    private void LayoutChips()
    {
        int n = TaskCount;
        if (n <= 0 || chipBgs == null)
            return;

        // 面板横向铺满全屏，格子宽度按面板实际宽度计算（屏宽变化时也能排满不断行）
        float area = Mathf.Max(240f, PanelWidth() - 48f); // 面板内可用宽度（左右各 24 内边距）
        float gap = n > 12 ? 3f : 6f;
        float w = Mathf.Clamp(Mathf.Floor((area - gap * (n - 1)) / n), 18f, 110f);
        for (int i = 0; i < MaxChips; i++)
        {
            if (chipBgs[i] == null)
                continue;
            bool show = i < n;
            if (chipBgs[i].gameObject.activeSelf != show)
                chipBgs[i].gameObject.SetActive(show);
            if (!show)
                continue;
            RectTransform rt = chipBgs[i].rectTransform;
            rt.sizeDelta = new Vector2(w, 26f);
            rt.anchoredPosition = new Vector2(24f + i * (w + gap), -50f);
            if (chipTexts[i] != null)
                chipTexts[i].fontSize = w >= 34f ? 17 : (w >= 24f ? 15 : 13);
        }
    }

    // ================== 指导框高度自适应（避免文案溢出黑框） ==================

    /// <summary>指导框横向铺满：返回当前面板实际宽度（首帧布局未完成时退回参考分辨率宽度）。</summary>
    private float PanelWidth()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        if (panelRt != null && panelRt.rect.width > 1f)
            return panelRt.rect.width;
        return ReferenceWidth;
    }

    /// <summary>按正文实测排版高度反推指导框高度，并同步给编辑器做下边距 —— 文案永不溢出黑框。</summary>
    private void RefitPanelToContent()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        if (panelRt == null)
            return;

        float bodyHeight = GuideBodyHeight(PanelWidth());
        float height = Mathf.Clamp(bodyHeight + GuideBodyTopInset + GuideBodyBottomInset,
            GuidePanelHeightMin, GuidePanelHeightMax);
        if (Mathf.Abs(panelRt.sizeDelta.y - height) > 0.5f)
            panelRt.sizeDelta = new Vector2(0f, height); // 横向靠 anchorMin.x=0 / anchorMax.x=1 铺满，只改高度

        LayoutChips();

        // 关键：指导框高度即编辑器的「底部预留高度」→ 代码编辑器下边缘与指导框上边缘严丝合缝，两者不重叠
        if (editor != null && Active)
            editor.SetBottomReservedHeight(height);
    }

    /// <summary>正文（写法 / 目标 / 提示 / 判定）在给定面板宽度下的实测高度；排版未完成时退回估算值。</summary>
    private float GuideBodyHeight(float panelWidth)
    {
        if (bodyText == null)
            return 0f;

        float width = Mathf.Max(200f, panelWidth - 48f);
        string content = bodyText.text == null ? "" : bodyText.text;

        TextGenerationSettings settings = bodyText.GetGenerationSettings(new Vector2(width, 0f));
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.lineSpacing = GuideBodyLineSpacing;
        float preferred = bodyText.cachedTextGeneratorForLayout.GetPreferredHeight(content, settings);
        float scale = bodyText.pixelsPerUnit > 0.01f ? bodyText.pixelsPerUnit : 1f;
        float height = preferred / scale;

        if (height <= 1f)
        {
            // 首帧 TextGenerator 尚未就绪：按每行字数估算行数（中文按 1 字宽、西文按 0.55 字宽）
            int lines = 0;
            string[] raw = content.Split('\n');
            for (int i = 0; i < raw.Length; i++)
            {
                float estimated = Mathf.Max(1f, raw[i].Length);
                lines += Mathf.Max(1, Mathf.CeilToInt(estimated / Mathf.Max(1f, width / GuideBodyFontSize)));
            }
            height = lines * GuideBodyFontSize * GuideBodyLineSpacing;
        }

        return Mathf.Max(height, GuideBodyFontSize * 1.2f);
    }

    private void BuildHintBar(Transform parent)
    {
        // 编辑器关闭时的常驻提示条（不遮挡玩法，提示「按 Enter 打开编辑器继续」）。
        // 文字按一行放宽排版；高度在 RefreshHint 中按实测高度自适应，长文案也不会被截断。
        RectTransform hintRt = CreateRect("SyntaxHintBar", parent, new Vector2(0.5f, 0f), Vector2.zero,
            new Vector2(HintBarMaxWidth, HintBarMinHeight), new Vector2(0.5f, 0f));
        hintRt.anchoredPosition = new Vector2(0f, 24f);
        hintRoot = hintRt.gameObject;
        hintRect = hintRt;

        Image bg = hintRoot.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;

        hintText = CreateText("HintText", hintRt, "", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(hintText.rectTransform);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hintText.verticalOverflow = VerticalWrapMode.Overflow;
        hintText.raycastTarget = false;

        hintRoot.SetActive(false);
    }

    /// <summary>提示条高度 / 宽度自适应：按当前文案实测宽高，超出可用宽度时自动换行加高。</summary>
    private void RefitHintBar()
    {
        if (hintRect == null || hintText == null)
            return;

        string content = hintText.text == null ? "" : hintText.text;
        float maxWidth = Mathf.Min(HintBarMaxWidth, ReferenceWidth - 48f);

        TextGenerationSettings settings = hintText.GetGenerationSettings(new Vector2(maxWidth, 0f));
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        TextGenerator generator = hintText.cachedTextGeneratorForLayout;
        float scale = hintText.pixelsPerUnit > 0.01f ? hintText.pixelsPerUnit : 1f;

        float preferredWidth = generator.GetPreferredWidth(content, settings) / scale;
        float preferredHeight = generator.GetPreferredHeight(content, settings) / scale;

        float width = Mathf.Clamp(preferredWidth + 72f, HintBarMinWidth, maxWidth);
        float height = Mathf.Max(HintBarMinHeight, preferredHeight + 26f);
        hintRect.sizeDelta = new Vector2(width, height);
        hintRect.anchoredPosition = new Vector2(0f, 24f);
    }


    // ================== UI 小工具（与 TutorialController 同风格） ==================

    private static Sprite whiteSprite;

    private static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }
        return whiteSprite;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
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

    private static Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = UIFontHelper.LoadForText(content, size);
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

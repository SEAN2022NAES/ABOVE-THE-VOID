using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 代码编辑器内的顺序式 C# 教学（两门课程，均由 LevelDirector 调用 Begin(course) 进入）：
///   - Course.Grammar（Level 1 - Part 3）：C# 基础语法全套 18 项 —— 注释、变量声明与类型、const 常量、
///     算术运算符、关系运算符、逻辑运算符、if、if-else、switch、for、while、do-while、一维数组、List 集合、
///     方法定义与参数（局部函数）、方法返回值 return、字符串拼接、string.Format；
///   - Course.Api（Level 2 - Part 2）：炮台 API 调用全套 13 项 —— Print、PrintLine、ClearPrint、
///     GetAllObjects、TowerObjectInfo.Index / Type / HP / Position / Velocity / Volume、GetFireCooldown、
///     FireAtPlane，以及「遍历 + 条件 + 发射」实战组合（收官项固定留在最后）；
///   - 每一项任务的完成条件都相同：① 编辑器里的代码命中该任务的语法 ② 本次任务期间点过【编译运行 (F6)】
///     并且**编译通过的那份代码本身**满足该语法（防止先写后编译 / 先编译后补写蒙混过关）；
///     任务卡常驻显示「① 语法 / ② 编译」两条勾选状态，未编译时明确提示「点标题栏【编译运行 (F6)】验证」；
///   - 说明分页（专为零基础）：每个任务都有「第 1 页 = 写法 / 示例」「第 2 页 = 手把手细节」「第 3 页 = 代码动画」，
///     任务卡右上角「‹ 细节 1/3 ›」可来回翻；第 2、3 页整段通栏放大指导框（DetailPageRatio），
///     第 2 页内容 = ① 一步一步怎么做（按哪个键、写在哪）② 逐符号读懂它 ③ 三个必踩的坑 + 正确做法；
///     第 3 页内容 = 示例代码每 1.8 秒高亮前进一行 + 左栏用大白话讲「这一刻这行在干什么」，
///     把静态代码演成一段可看的过程（数据从哪来、条件真假、循环跑了几圈、最后打印出什么）；
///   - 判定机制：结构类用正则解析编辑器文本（先剥离注释与字符串字面量，避免注释 / 字符串误判），
///     编译类用 CodeEditorController.OnCompiledSuccessfully 事件，不依赖运行时反射；
///   - 离开 / 重进关卡都会重置进度（Begin 每次从第 1 项开始并重建进度格）。
/// UI 风格与 TutorialController 保持一致（纯代码构建 ScreenSpaceOverlay Canvas + 半透明面板 + Text）。
/// </summary>
public class CodeSyntaxTutorialController : MonoBehaviour
{
    /// <summary>教学课程：C# 基础语法（Level 1-3）/ 炮台 API 调用（Level 2-2）。</summary>
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
        public string BodyZh;      // 第 1 页：写法 / 示例 + 目标 / 提示 / 判定（原有内容）
        public string BodyEn;
        public string Page2Zh;     // 第 2 页：零基础手把手细节（为 null 表示本任务只有一页）
        public string Page2En;
        public AnimStep[] Anim;    // 第 3 页：代码动画（逐行高亮 + 旁白，为 null 表示本任务没有动画页）
        public MemScene Mem;       // 第 3 页：内存可视化场景（有它就走图形页，优先于 Anim）
        public TaskMatch Match;
    }

    // ================== 第 3 页：内存可视化（manim 风格）的数据结构 ==================

    /// <summary>
    /// 一个变量在内存里占的那一段：栈 / 堆上的连续若干字节。
    /// 类型只是画在标签上的说明，真正决定宽度的是 Bytes 里写了几个字节 ——
    /// char / bool 占 1 格，int / float 占 4 格，数组 / List / string 的变量名本身只占 4 格引用（门牌号），
    /// 它指向的那段数据另用一个变量表示（Type 写 "堆 · int[3]" 之类），这样「引用」和「数据」不会混在一起。
    /// </summary>
    private class MemVar
    {
        public string Name;    // 变量名（画在行首）
        public string Type;    // 类型（int / char / float / bool / string / int[] / List<int> …）
        public string Bytes;   // 每个字节的十六进制值，空格分隔；"??" 表示这一格还没被写
        public string Note;    // 这一行下面的一行小字（讲清这段内存为什么长这样），可为空
        public string Badge;   // 行首小徽章（"锁" / "引用" / "栈帧" / "丢弃"），可为空
        public bool Ghost;     // true = 灰虚线：代码里写了、但内存里根本没有它（注释 / 方法定义）
        // ---- 英文模式专用文案（Localization.IsChinese = false 时优先用这几项，留空则回落到中文） ----
        public string NameEn;  // 变量名的英文写法（如 "hp（参数）" → "hp (param)"）
        public string TypeEn;  // 类型名牌的英文写法（如 "堆 · int[3]" → "heap · int[3]"）
        public string NoteEn;  // 行说明的英文版
        public string BadgeEn; // 行首徽章的英文版（如 "锁" → "CONST"）

        /// <summary>给这段内存补一份英文文案（不传的那项继续用中文，避免漏写就空掉）。</summary>
        internal MemVar En(string note, string name = null, string type = null, string badge = null)
        {
            NoteEn = note;
            if (!string.IsNullOrEmpty(name))
                NameEn = name;
            if (!string.IsNullOrEmpty(type))
                TypeEn = type;
            if (!string.IsNullOrEmpty(badge))
                BadgeEn = badge;
            return this;
        }
    }

    /// <summary>内存可视化的一步：内存里已经有前 Reveal 个变量、正在写第 Write 个，配一句旁白。</summary>
    private class MemStep
    {
        public string Expr;    // 这一步在执行的代码（画在舞台下方的执行台上）
        public string ExprEn;  // 同一行代码的英文版（英文模式用；代码里的注释跟着语言走，留空则回落到 Expr）
        public string Zh;      // 中文旁白
        public string En;      // 英文旁白
        public int Reveal;     // 到这一步为止，内存里有几个变量（1-based 个数，0 = 什么都没分配）
        public int Write;      // 这一步正在往里写字节的变量下标（-1 = 只在读 / 只作判断）
        public int[] PatchVar;      // 这一步要改写哪几段内存的值（演示「同一格的值被改了」，如 for 里的 i）
        public string[] PatchBytes; // 改写后的字节串，与 PatchVar 一一对应

        /// <summary>补一份英文代码行（只换注释里的语言，代码本身不动）。链式写法：MS(...).Code("// remaining hp")</summary>
        internal MemStep Code(string exprEn)
        {
            ExprEn = exprEn;
            return this;
        }
    }

    /// <summary>
    /// 舞台上的一行 = 一个变量（或一段数据）：行首徽章 + 类型名牌 + 变量名 + 若干字节格 + 行下一行小字。
    /// 所有子件在 BuildMemStage 里一次建好并塞进池子，播放时只改颜色 / 文字 / 位置，绝不动态建 UI。
    /// </summary>
    private class MemRowUI
    {
        public RectTransform Root;                 // 整块（行内容 + 下方小字）
        public Image FrameBg;                      // 整块底色（栈帧 / 幽灵行用）
        public Text Badge;                         // 行首徽章（锁 / 引用 / 栈帧）
        public Image TypeBg;                       // 类型名牌底色
        public Text TypeText;                      // 类型名牌文字（int / char …）
        public Text NameText;                      // 变量名
        public Text NoteText;                      // 行下小字
        public RectTransform[] CellRt;             // 本行字节格
        public Image[] CellBg;
        public Text[] CellAddr;
        public Text[] CellVal;
        public Vector2[] CellPos;                  // 格子目标位置（补间用）
    }

    /// <summary>一段内存戏：变量表 + 分步脚本。</summary>
    private class MemScene
    {
        public MemVar[] Vars;
        public MemStep[] Steps;
    }

    /// <summary>
    /// 代码动画的一个步骤：把示例代码里的某一行高亮，并配一句「这一行在干什么」。
    /// 行不用写死下标，只给该行里的一段特征文字（Key），由 FindAnimLine 去示例代码里定位 ——
    /// 以后改示例代码不会因为行号错位而高亮到别的行。
    /// </summary>
    private class AnimStep
    {
        public string Key;   // 该步骤要演示的代码行特征子串
        public string KeyEn; // 英文示例代码里的特征子串（Key 本身是中文注释时用，如 "// 计算剩余血量" → "// remaining hp"）
        public string Zh;    // 中文旁白
        public string En;    // 英文旁白

        /// <summary>补一份英文定位子串：Key 里带中文注释时，英文模式要在英文示例代码里找得到这一行。</summary>
        internal AnimStep KeyIn(string keyEn)
        {
            KeyEn = keyEn;
            return this;
        }
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
    private const float ReferenceHeight = 1080f;         // Canvas 参考分辨率高（取不到父级高度时的回退值）
    private const float SplitRatio = 0.5f;               // 上下对半平分：上方代码区 50%、下方指导框 50%，固定不随窗口 / 文案变化
    private const float DetailPageRatio = 0.66f;
    private const float AnimStepInterval = 1.8f;      // 第 3 页代码动画：旧版自动推进的节拍（现已改手动，仅保留作补间基准）
    private const float MemStepInterval = 4.2f;       // 第 3 页内存可视化：一步的补间时长上限（不再自动跳步，靠 ↓ 手动推进）
    private const float MemFadeTime = 0.45f;          // 新变量「滑进来」的时长（这一步前 0.45 秒内做淡入 + 位移）
    private const float MemCellGapX = 8f;             // 相邻字节格的水平间距
    private const float MemRowGapY = 14f;             // 相邻变量块之间的垂直间距
    private const int MemRowPool = 4;                 // 预建的行数（一个场景最多同时摊开 4 段内存）
    private const int MemCellPerRow = 12;             // 每行最多几格（数组 int[3] = 12 字节，是最长的一段）
    private const float MemStageTopInset = 108f;      // 舞台顶边内缩：让开任务名那一行
    private const float MemStageBottomInset = 26f;    // 舞台底边内缩：一条呼吸位
    private const float MemStageSideInset = 26f;      // 舞台左右内缩
    private const float MemHeaderH = 40f;             // 顶部说明条高度（留两行：小端那句要写清楚，一行放不下）
    private const float MemExprH = 34f;               // 执行台（当前代码行）高度
    private const float MemNoteH = 56f;               // 底部旁白高度
    private const float MemNoteGapY = 6f;             // 旁白与执行台之间的间距
    private const float MinHintHeight = 200f;         // 提示区最小高度（拖到底也不会消失）
    private const float MinCodeHeight = 180f;         // 代码区最小高度（拖动分割线时的下限保护）
    private const float DragHandleHeight = 12f;       // 蓝色分割线的拖动热区高度（跨在线上，好抓）         // 第 2 页（手把手细节）指导框占比：文字量大，临时把指导框放大到 68%，翻回第 1 页自动复原
    private const float GuideBodyTopInset = 132f;        // 正文上边距（让开强调线 / 标题 / 进度格 / 任务名）
    private const float GuideBodyBottomInset = 46f;      // 正文下边距（让开「任务完成 ✔」提示）
    private const int GuideBodyFontSize = 24;            // 左栏（目标 / 提示 / 判定）固定字号：不再自适应，靠拖动蓝色长条看全
    private const int SampleFontSize = 20;               // 右栏（写法 / 示例代码）固定字号：同上，恒定不随高度变化
    private const float GuideBodyLineSpacing = 1.05f;    // 正文行距
    private const float ColumnSplitRatio = 0.5f;         // 左（引导）/ 右（示例代码）分栏界线位置（0.5 = 对半）
    private const float ColumnGap = 28f;                 // 分栏内边距：文字与竖分隔线之间的间距
    private const float DividerWidth = 2f;               // 分栏竖分隔线宽度
    private const float HintBarMaxWidth = 1400f;         // 提示条最大宽度
    private const float HintBarMinWidth = 420f;          // 提示条最小宽度
    private const float HintBarMinHeight = 64f;          // 提示条最小高度
    private const float EntryPromptY = 150f;             // 进入提示在实机页面上的纵向位置（相对屏幕中心向上偏移）
    private const float PageNavWidth = 240f;             // 任务卡右上角翻页条宽度（‹ 页码 ›）
    private GameObject panelRoot;      // 编辑器打开时显示的任务卡（底部指导框）
    private float appliedSplitHeight = -1f; // 最近一次应用的对半分栏高度（用于窗口尺寸变化时重算）
    private RectTransform hintRect;    // 提示条矩形（按文案自适应宽高）
    private Text headlineText;         // 「关卡 1 · 第 2 部分 · C# 基础语法教学」
    private Text progressText;         // 「任务 2/18」
    private Text taskNameText;         // 当前任务名
    private Text bodyText;             // 左栏：目标 / 提示 / 判定 + 完成条件状态
    private Text sampleText;           // 右栏：写法 / 示例代码（无写法段时隐藏，左栏自动通栏）

    // ---- 第 3 页：内存可视化舞台（整块正文区改成图形演示，manim 风格） ----
    private RectTransform memRoot;      // 舞台根节点（覆盖整个正文区）
    private Text memHeaderText;         // 顶部说明：一格 = 1 字节 / 右高左低（小端）
    private Text memExprText;           // 执行台：当前这一行代码
    private Text memNoteText;           // 旁白：这一步在干什么
    private Text memFootText;           // 顶部右侧：第几步 / 循环提示
    private RectTransform memExprRt;    // 执行台那条深色带（底部整块要跟着内容走，得能改它的位置）
    private Text memKeyHintText;        // 执行台右侧：按下 ↓ 观看下一个（代码区握焦点时改成 ↓ 代码模式）
    private string memKeyHintCache = "";// 键位提示文案缓存，避免逐帧改文字
    private MemRowUI[] memRows;         // 行池（每个变量/每段数据一块）
    private int memBuiltStep = -1;      // 上一次重建舞台时的步骤号（变了才重排文字）
    private int memBuiltIndex = -1;     // 上一次重建舞台时的任务号
    private float memBuiltH = -1f;      // 上一次重建舞台时的可用高度（拖动分割线后要重排）
    private Image columnDivider;       // 左 / 右分栏之间的竖分隔线
    private bool columnHasSample;      // 当前任务是否有右侧示例代码（决定是否分栏）
    private Text flashText;            // 「任务完成 ✔」短暂提示
    private GameObject pageNavRoot;    // 任务卡内翻页条（‹ 说明 1/2 ›），只在该任务有多页时显示
    private Button prevPageBtn;        // 上一页（翻回去）
    private Button nextPageBtn;        // 下一页（翻回来 / 翻到第 2 页）
    private Text pageNoText;           // 「说明 1/2」页码
    private Text[] chipTexts;          // 进度格文字（索引 = 任务序号）
    private Image[] chipBgs;           // 进度格底色
    private Color[] chipBaseColors;    // 进度格基准色（悬停高亮后据此还原）
    private GameObject hintRoot;       // 编辑器关闭时实机页面正中的进入提示（提示「按下 Enter 打开代码编辑界面」）
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
    private float checkTimer;          // 结构判定节流（真实时间）
    private float flashTimer;          // 「任务完成」提示倒计时（真实时间）
    private string hintCache = "";     // 提示条文案缓存，避免逐帧改文字
    private int statusCache = -1;      // 任务卡状态缓存（1=语法命中 2=编译通过），避免逐帧重建正文
    private int guidePage;             // 当前任务正在看的说明页（0 = 第 1 页 写法/示例，1 = 第 2 页 手把手细节，2 = 第 3 页 代码动画）
    private float animTimer;           // 代码动画计时（真实时间，编辑器打开时 timeScale = 0）
    private int animStep;              // 代码动画当前步骤（0-based，超过步数自动回 0 循环）
    private float manualSplitOffset;  // 手动拖动蓝色分割线的偏移（UI 像素，>0 = 提示区更高 / 代码区更矮）
    private Image accentLine;         // 面板顶部那条蓝色分割线（拖动把手悬停时提亮）
    private SplitDragHandle splitDragHandle;

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
    /// 进入教学关并从第 startIndex 个任务（0 基）开始（供 /level 1 3 n、/level 2 2 n 章节跳转使用）；
    /// 越界值自动收敛到合法范围；跳过的任务保持「未开始」，是否已完成只以玩家真实完成记录为准。
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

    /// <summary>课程 → 该课「已完成任务」记录的 PlayerPrefs 键名（语法课 / API 课各一张完成表）。</summary>
    public static string DoneKeyOf(Course course)
    {
        return course == Course.Api ? GameProgress.TaskDoneKeyApi : GameProgress.TaskDoneKeyGrammar;
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

        // 窗口尺寸变化时工作区总高改变 → 重新按「一半」重算指导框高度并同步给编辑器（比例恒定，绝对高度跟着走）
        if (Mathf.Abs(appliedSplitHeight - SplitHalfHeight()) > 0.5f)
            ApplySplitLayout();

        // 「任务完成」提示倒计时（真实时间：编辑器打开时 timeScale = 0）
        if (flashTimer > 0f)
        {
            flashTimer -= Time.unscaledDeltaTime;
            if (flashTimer <= 0f && flashText != null)
                flashText.text = "";
        }

        // ← / → 翻页、↑ / ↓ 走步骤：键盘就能把整段教学看完，不用去点小三角
        HandlePageNavKey();
        AdvanceAnim();

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

        // 章节定位：startIndex 为 0 基任务序号，越界自动收敛。
        //   · 「哪些章节做过了」只认玩家真实完成记录（PlayerPrefs 里一张独立的完成表），
        //     不再按「序号在 startIndex 之前的都算做过」伪造进度：直接 /level 1 3 10 或 /level 2 2 10
        //     跳到第 10 个任务时，第 1~9 个任务保持「未开始」的灰色，不会显示成已完成；
        //   · 没有记录（第一次进入 / 清档后 / 老版本存档）时是一张全 false 的表，等价于「什么都没做过」。
        int start = Mathf.Clamp(startIndex, 0, Mathf.Max(0, tasks.Length - 1));
        done = GameProgress.LoadTaskDone(DoneKeyOf(course), tasks.Length);
        CurrentIndex = start;
        guidePage = 0; // 进入教学总是从每个任务的第 1 页（写法 / 示例）开始看

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

        // 布局：上下对半平分 —— 上方代码编辑器占工作区上半（50%，代码过长时用编辑器内部竖向滚动条），
        // 下方指导框横跨工作区下半（50%）、上边缘与代码编辑器下边缘严丝合缝、下边缘贴屏幕底部。
        // 比例固定，不随窗口尺寸变化、也不随任务文案长短变化（ApplySplitLayout）。
        // 只在本教学（Level 1-Part 3 / Level 2-Part 2）生效，EndInternal 恢复默认全屏布局。
        if (editor != null)
            editor.SetRightReservedWidth(0f); // 上下分栏不再左右平分，右侧不再预留
        // 「选中方块信息」恢复屏幕水平居中（下方指导框已横向占满，不再需要左右错位避让）
        BlockHitIndicator.SetHorizontalOffsetGlobal(0f);

        LayoutChips();
        RefreshText();
        ApplySplitLayout(); // 指导框固定占工作区下半 50%，并把同一高度作为编辑器底部预留

        // 进入教学后先停在实机页面（不自动弹出编辑器）：玩家看到的是真实关卡画面，
        // 页面正中的提示告知「按下 Enter 打开代码编辑界面」，按 Enter 之后才进入编辑器、任务卡随之出现。
        if (editor != null && editor.IsOpen)
            editor.ForceCloseSelf(); // 从 API 课等别的教学切进来时，先收掉上一关残留的编辑器

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

    /// <summary>
    /// 点击进度格跳转：在同一关（同一门教学课）内切换到任意任务。
    /// 只移动「当前任务」指针，已完成标记与关卡进度保持不变，也不会跳到别的关卡。
    /// </summary>
    private void JumpToTask(int index)
    {
        if (!Active || finished)
            return;
        if (editor == null || !editor.IsOpen)
            return;
        if (tasks == null || index < 0 || index >= TaskCount || index == CurrentIndex)
            return;

        CurrentIndex = index;
        guidePage = 0; // 换任务回到第 1 页
        ResetAnim();   // 动画也从头开演

        // 编译快照属于上一个任务，跳转后必须清零：否则新任务会被旧代码误判成「已编译通过」
        compiledThisTask = false;
        compiledRaw = string.Empty;
        compiledSnapshot = string.Empty;
        statusCache = -1;
        hintCache = string.Empty;
        flashTimer = 0f;
        if (flashText != null)
            flashText.text = string.Empty;

        // 断点续玩同步：手动跳转也算章节位置变化
        GameProgress.GetCourseLocation(CurrentCourse, out int jumpLevel, out int jumpPart);
        GameProgress.SaveResume(jumpLevel, jumpPart, index + 1);

        Debug.Log("[CodeSyntaxTutorial] 点击进度格跳转到任务 " + (index + 1) + "/" + TaskCount + "。");

        // 新任务的语法 / 编译两条勾选都要重新判定，先按当前编辑器内容刷新一次
        RefreshText();
        EvaluateCurrentTask();
    }

    // ================== 说明分页（第 2 页：零基础手把手细节） ==================

    /// <summary>某个任务有几页说明：有第 3 页动画即 3 页，否则有第 2 页文案即 2 页，都没有则 1 页。</summary>
    private int PageCountOf(int index)
    {
        if (tasks == null || index < 0 || index >= tasks.Length)
            return 1;
        TaskDef t = tasks[index];
        bool hasThird = (t.Anim != null && t.Anim.Length > 0)
            || (t.Mem != null && t.Mem.Vars != null && t.Mem.Vars.Length > 0
                && t.Mem.Steps != null && t.Mem.Steps.Length > 0);
        if (hasThird)
            return 3;
        bool hasSecond = !string.IsNullOrEmpty(t.Page2Zh) || !string.IsNullOrEmpty(t.Page2En);
        return hasSecond ? 2 : 1;
    }

    /// <summary>翻回上一页（第 1 页时按钮不可点）。</summary>
    private void PrevPage()
    {
        if (!Active || finished || tasks == null)
            return;
        if (guidePage <= 0 || CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;
        guidePage--;
        ResetAnim();
        RefreshText();
    }

    /// <summary>翻到下一页（手把手细节页 / 代码动画页）。</summary>
    private void NextPage()
    {
        if (!Active || finished || tasks == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;
        if (guidePage >= PageCountOf(CurrentIndex) - 1)
            return;
        guidePage++;
        ResetAnim();
        RefreshText();
    }

    /// <summary>代码动画从头开始（换任务 / 换页时调用，避免停在上一个任务的中途）。</summary>
    private void ResetAnim()
    {
        animTimer = 0f;
        animStep = 0;
        memBuiltIndex = -1;    // 内存舞台也从头演：强制下一帧重排
        memBuiltStep = -1;
    }

    // ================== 第 3 页：代码动画（逐行高亮 + 旁白） ==================

    /// <summary>本任务是否有第 3 页代码动画。</summary>
    private bool HasAnimPage(int index)
    {
        return tasks != null && index >= 0 && index < tasks.Length
            && tasks[index].Anim != null && tasks[index].Anim.Length > 0;
    }

    /// <summary>
    /// 从任务正文里抽出「示例代码行」：第 1 页写法段的代码统一缩进 4 个空格，
    /// 目标 / 提示 / 判定那几段顶格写 —— 所以「缩进 ≥ 4 空格」就是代码行，不用另存一份代码。
    /// 固定读中文正文（两套语言的示例代码完全一致），避免切语言后动画行对不上。
    /// </summary>
    private static string[] ExampleCodeLines(TaskDef task, bool zh)
    {
        if (task == null)
            return new string[0];
        // 代码里的注释也要跟着语言走：英文模式读 BodyEn 的缩进行（两套示例代码行序、缩进完全一致），
        // 万一某个任务没写英文正文，再回落到中文正文，绝不出现空白页。
        if (!zh && !string.IsNullOrEmpty(task.BodyEn))
        {
            string[] en = IndentedCodeLines(task.BodyEn);
            if (en.Length > 0)
                return en;
        }
        if (string.IsNullOrEmpty(task.BodyZh))
            return new string[0];
        return IndentedCodeLines(task.BodyZh);
    }

    /// <summary>抽出「缩进 ≥ 4 空格」的行 —— 任务正文里代码行的统一约定。</summary>
    private static string[] IndentedCodeLines(string body)
    {
        string[] raw = body.Replace("\r\n", "\n").Split('\n');
        List<string> code = new List<string>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i];
            if (line.StartsWith("    ", System.StringComparison.Ordinal))
                code.Add(line.Substring(4).TrimEnd());
        }
        return code.ToArray();
    }

    /// <summary>按特征子串在示例代码里找行；找不到就退回第 1 行（宁可高亮错行也不整页空掉）。</summary>
    private static int FindAnimLine(string[] lines, string key, string keyEn = null)
    {
        if (lines == null || lines.Length == 0)
            return 0;
        int hit = FindLineByKey(lines, key);
        if (hit >= 0)
            return hit;
        hit = FindLineByKey(lines, keyEn);   // 中文 Key 在英文示例代码里找不到时换英文子串
        if (hit >= 0)
            return hit;
        return 0;
    }

    private static int FindLineByKey(string[] lines, string key)
    {
        if (lines == null || lines.Length == 0 || string.IsNullOrEmpty(key))
            return -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].IndexOf(key, System.StringComparison.Ordinal) >= 0)
                return i;
        }
        return -1;
    }

    /// <summary>右栏：示例代码全文，当前执行的那一行用 ► 加高亮色标出来（每步重新生成一次即「动画」）。</summary>
    private string BuildAnimCode(int index, bool zh)
    {
        string[] lines = ExampleCodeLines(tasks[index], zh);
        if (lines.Length == 0)
            return string.Empty;

        AnimStep[] anim = tasks[index].Anim;
        int step = Mathf.Clamp(animStep, 0, anim.Length - 1);
        int hl = FindAnimLine(lines, anim[step].Key, anim[step].KeyEn);

        StringBuilder sb = new StringBuilder(lines.Length * 48 + 96);
        sb.Append(zh ? "【示例代码 · ► 就是现在执行到的那一行】\n\n" : "[Sample code - ► marks the line running now]\n\n");
        for (int i = 0; i < lines.Length; i++)
        {
            string no = (i + 1).ToString("00") + "  ";
            if (i == hl)
            {
                sb.Append("<color=#5FD7FF><b>► ").Append(no).Append(lines[i]).Append("</b></color>\n");
            }
            else
            {
                sb.Append("  ").Append(no).Append(lines[i]).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>左栏：动画旁白 —— 进度点 + 这一步在干什么 + 循环播放说明。</summary>
    private string BuildAnimGuide(int index, bool zh)
    {
        AnimStep[] anim = tasks[index].Anim;
        int step = Mathf.Clamp(animStep, 0, anim.Length - 1);
        AnimStep cur = anim[step];

        StringBuilder sb = new StringBuilder(420);
        sb.Append(zh
            ? "【这一页：代码动画 —— 让这段代码自己跑一遍】\n本页不用你敲代码，眼睛跟着右栏的 ► 走就行。"
            : "[THIS PAGE: CODE ANIMATION - watch the code run itself]\nNothing to type here: just follow ► on the right.");

        sb.Append("\n\n").Append(zh ? "进度 " : "Step ")
          .Append(step + 1).Append('/').Append(anim.Length).Append("   ");
        for (int i = 0; i < anim.Length; i++)
            sb.Append(i <= step ? "●" : "○");

        sb.Append("\n\n").Append(zh ? "▶ 这一步在做什么：\n" : "▶ What happens here:\n");
        sb.Append(zh ? cur.Zh : cur.En);

        sb.Append(zh
            ? "\n\n（不会自动播放：按 ↓ 一步一步看，走到底再按一次回到第一步。想重看：翻回第 1 页再翻回本页即可。）"
            : "\n(No auto-play: press ↓ to step through; after the last step it wraps to the first. To replay, flip back to page 1 and return here.)");
        return sb.ToString();
    }

    // ================== 第 3 页：内存可视化舞台（manim 风格） ==================

    /// <summary>本任务是否有内存可视化页（有它就用图形舞台，优先于旧的逐行高亮）。</summary>
    private bool HasMemPage(int index)
    {
        return tasks != null && index >= 0 && index < tasks.Length
            && tasks[index].Mem != null
            && tasks[index].Mem.Vars != null && tasks[index].Mem.Vars.Length > 0
            && tasks[index].Mem.Steps != null && tasks[index].Mem.Steps.Length > 0;
    }

    /// <summary>一段内存占几个字节（= 这一行要画几个格子）。</summary>
    private static int CountBytes(MemVar v)
    {
        if (v == null || string.IsNullOrEmpty(v.Bytes))
            return 1;
        string[] parts = v.Bytes.Split(' ');
        int n = 0;
        for (int i = 0; i < parts.Length; i++)
            if (!string.IsNullOrEmpty(parts[i]))
                n++;
        return Mathf.Max(1, n);
    }

    /// <summary>
    /// 建内存舞台：顶部一行说明 + 中间一行行变量（每行 = 徽章 / 类型名牌 / 变量名 / 字节格 / 行下小字）
    /// + 底部「当前执行的那行代码」与「这一拍在干什么」。
    /// 整块正文区都归舞台，所有子件一次建好塞进池子，播放时只改颜色 / 文字 / 位置。
    /// </summary>
    private void BuildMemStage(RectTransform panelRt)
    {
        RectTransform root = CreateRect("MemStage", panelRt, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        // 内存页整块都是图，不需要给「写法 / 示例代码」留位，所以上下留白比文字栏窄一点：
        // 上边到任务名那一行下面（-108，任务名占到 -124），下边只留一条呼吸位。
        // 多出来的这几十像素正好让 4 行变量也放得下「行下小字」。
        root.offsetMin = new Vector2(MemStageSideInset, MemStageBottomInset);
        root.offsetMax = new Vector2(-MemStageSideInset, -MemStageTopInset);
        memRoot = root;

        // ---- 顶部：左边一句「怎么读这张图」，右边步数 ----
        memHeaderText = CreateText("MemHeader", root, "", 17, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform hdr = memHeaderText.rectTransform;
        hdr.anchorMin = new Vector2(0f, 1f);
        hdr.anchorMax = new Vector2(1f, 1f);
        hdr.pivot = new Vector2(0f, 1f);
        hdr.anchoredPosition = Vector2.zero;
        hdr.sizeDelta = new Vector2(-190f, MemHeaderH);
        memHeaderText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memHeaderText.verticalOverflow = VerticalWrapMode.Truncate;
        memHeaderText.resizeTextForBestFit = true;
        memHeaderText.resizeTextMinSize = 12;
        memHeaderText.resizeTextMaxSize = 17;

        memFootText = CreateText("MemFoot", root, "", 16, UITheme.TextDim, TextAnchor.MiddleRight);
        RectTransform ftr = memFootText.rectTransform;
        ftr.anchorMin = new Vector2(1f, 1f);
        ftr.anchorMax = new Vector2(1f, 1f);
        ftr.pivot = new Vector2(1f, 1f);
        ftr.anchoredPosition = Vector2.zero;
        ftr.sizeDelta = new Vector2(180f, MemHeaderH);
        memFootText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // ---- 中间：行池 ----
        memRows = new MemRowUI[MemRowPool];
        for (int i = 0; i < MemRowPool; i++)
            memRows[i] = BuildMemRow(root, i);

        // ---- 底部：旁白（最底） ----
        memNoteText = CreateText("MemNote", root, "", 19, UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform nr = memNoteText.rectTransform;
        nr.anchorMin = new Vector2(0f, 1f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(0f, 1f);
        nr.anchoredPosition = new Vector2(0f, 0f);   // 真实位置由 PlaceMemBottom 按内容高度摆
        nr.sizeDelta = new Vector2(0f, MemNoteH);
        memNoteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memNoteText.verticalOverflow = VerticalWrapMode.Truncate;
        memNoteText.lineSpacing = 1.08f;
        memNoteText.resizeTextForBestFit = true;
        memNoteText.resizeTextMinSize = 12;
        memNoteText.resizeTextMaxSize = 19;

        // ---- 底部：执行台（旁白上方一条深色带，写「正在跑的那行代码」） ----
        RectTransform exprRt = CreateRect("MemExprBg", root, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, MemExprH), new Vector2(0.5f, 1f));
        exprRt.anchorMin = new Vector2(0f, 1f);
        exprRt.anchorMax = new Vector2(1f, 1f);
        exprRt.pivot = new Vector2(0.5f, 1f);
        exprRt.anchoredPosition = new Vector2(0f, 0f);   // 真实位置由 PlaceMemBottom 按内容高度摆
        exprRt.sizeDelta = new Vector2(0f, MemExprH);
        memExprRt = exprRt;
        Image exprBg = exprRt.gameObject.AddComponent<Image>();
        exprBg.sprite = WhiteSprite();
        exprBg.color = UITheme.WithAlpha(UITheme.CodeBg, 0.96f);
        exprBg.raycastTarget = false;

        memExprText = CreateText("MemExpr", exprRt, "", 19, UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform er = memExprText.rectTransform;
        er.anchorMin = Vector2.zero;
        er.anchorMax = Vector2.one;
        er.offsetMin = new Vector2(14f, 0f);
        er.offsetMax = new Vector2(-340f, 0f);   // 右侧让出键位提示的位置，代码行不会压到提示上
        memExprText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memExprText.verticalOverflow = VerticalWrapMode.Truncate;
        memExprText.resizeTextForBestFit = true;
        memExprText.resizeTextMinSize = 12;
        memExprText.resizeTextMaxSize = 19;

        // 执行台右侧的键位提示（多步任务才出现）：告诉玩家「按下 ↓ 观看下一个」；
        // 玩家点回代码区、输入框握了焦点之后，↓ 归代码用，提示随之改成「↓ 代码模式」。
        memKeyHintText = CreateText("MemKeyHint", exprRt, "", 17, UITheme.TextMuted, TextAnchor.MiddleRight);
        RectTransform kh = memKeyHintText.rectTransform;
        kh.anchorMin = new Vector2(1f, 0f);
        kh.anchorMax = new Vector2(1f, 1f);
        kh.pivot = new Vector2(1f, 0.5f);
        kh.anchoredPosition = new Vector2(-14f, 0f);
        kh.sizeDelta = new Vector2(320f, 0f);
        memKeyHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memKeyHintText.verticalOverflow = VerticalWrapMode.Truncate;
        memKeyHintText.resizeTextForBestFit = true;
        memKeyHintText.resizeTextMinSize = 12;
        memKeyHintText.resizeTextMaxSize = 17;

        memRoot.gameObject.SetActive(false);
    }

    /// <summary>建一行：整块底 + 徽章 + 类型名牌 + 变量名 + 12 个字节格 + 行下小字。全部一次建好，之后只改内容。</summary>
    private MemRowUI BuildMemRow(RectTransform parent, int idx)
    {
        MemRowUI row = new MemRowUI();

        RectTransform root = CreateRect("MemRow" + idx, parent, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(0f, 88f), new Vector2(0f, 1f));
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(0f, 1f);
        row.Root = root;

        RectTransform frame = CreateRect("Frame", root, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(0f, 88f), new Vector2(0f, 1f));
        frame.anchorMin = new Vector2(0f, 0f);
        frame.anchorMax = new Vector2(1f, 1f);
        frame.pivot = new Vector2(0f, 1f);
        frame.offsetMin = new Vector2(-8f, -2f);
        frame.offsetMax = new Vector2(8f, 2f);
        Image fbg = frame.gameObject.AddComponent<Image>();
        fbg.sprite = WhiteSprite();
        fbg.color = UITheme.WithAlpha(UITheme.PanelDeep, 0f);
        fbg.raycastTarget = false;
        row.FrameBg = fbg;

        row.Badge = CreateText("Badge", root, "", 13, UITheme.PanelDeep, TextAnchor.MiddleCenter);
        row.Badge.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.Badge.resizeTextForBestFit = true;
        row.Badge.resizeTextMinSize = 10;
        row.Badge.resizeTextMaxSize = 13;

        RectTransform typeBgRt = CreateRect("TypeBg", root, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(90f, 26f), new Vector2(0f, 1f));
        Image typeBg = typeBgRt.gameObject.AddComponent<Image>();
        typeBg.sprite = WhiteSprite();
        typeBg.color = UITheme.WithAlpha(UITheme.Accent, 0.16f);
        typeBg.raycastTarget = false;
        row.TypeBg = typeBg;

        row.TypeText = CreateText("TypeText", typeBgRt, "", 14, UITheme.Accent, TextAnchor.MiddleCenter);
        row.TypeText.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.TypeText.verticalOverflow = VerticalWrapMode.Truncate;
        row.TypeText.resizeTextForBestFit = true;
        row.TypeText.resizeTextMinSize = 10;
        row.TypeText.resizeTextMaxSize = 14;

        row.NameText = CreateText("Name", root, "", 18, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        row.NameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.NameText.verticalOverflow = VerticalWrapMode.Truncate;
        row.NameText.resizeTextForBestFit = true;
        row.NameText.resizeTextMinSize = 11;
        row.NameText.resizeTextMaxSize = 18;

        row.CellRt = new RectTransform[MemCellPerRow];
        row.CellBg = new Image[MemCellPerRow];
        row.CellAddr = new Text[MemCellPerRow];
        row.CellVal = new Text[MemCellPerRow];
        row.CellPos = new Vector2[MemCellPerRow];
        for (int i = 0; i < MemCellPerRow; i++)
        {
            RectTransform cr = CreateRect("Cell" + i, root, new Vector2(0f, 1f), Vector2.zero,
                new Vector2(64f, 64f), new Vector2(0f, 1f));
            Image cbg = cr.gameObject.AddComponent<Image>();
            cbg.sprite = WhiteSprite();
            cbg.color = UITheme.WithAlpha(UITheme.PanelRaised, 0.92f);
            cbg.raycastTarget = false;

            Text addr = CreateText("Addr", cr, "", 10, UITheme.TextDim, TextAnchor.MiddleCenter);
            RectTransform ar = addr.rectTransform;
            ar.anchorMin = new Vector2(0f, 1f);
            ar.anchorMax = new Vector2(1f, 1f);
            ar.pivot = new Vector2(0.5f, 1f);
            ar.anchoredPosition = new Vector2(0f, -1f);
            ar.sizeDelta = new Vector2(-2f, 13f);
            addr.horizontalOverflow = HorizontalWrapMode.Wrap;
            addr.verticalOverflow = VerticalWrapMode.Truncate;
            addr.resizeTextForBestFit = true;
            addr.resizeTextMinSize = 8;
            addr.resizeTextMaxSize = 10;

            Text val = CreateText("Val", cr, "", 22, UITheme.TextPrimary, TextAnchor.MiddleCenter);
            RectTransform vr = val.rectTransform;
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.one;
            vr.pivot = new Vector2(0.5f, 0.5f);
            vr.offsetMin = new Vector2(1f, 2f);
            vr.offsetMax = new Vector2(-1f, -14f);
            val.horizontalOverflow = HorizontalWrapMode.Wrap;
            val.verticalOverflow = VerticalWrapMode.Truncate;
            val.resizeTextForBestFit = true;
            val.resizeTextMinSize = 9;
            val.resizeTextMaxSize = 22;

            row.CellRt[i] = cr;
            row.CellBg[i] = cbg;
            row.CellAddr[i] = addr;
            row.CellVal[i] = val;
            row.CellPos[i] = Vector2.zero;
            cr.gameObject.SetActive(false);
        }

        row.NoteText = CreateText("Note", root, "", 20, UITheme.TextBody, TextAnchor.MiddleLeft);
        row.NoteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.NoteText.verticalOverflow = VerticalWrapMode.Truncate;
        row.NoteText.resizeTextForBestFit = true;
        row.NoteText.resizeTextMinSize = 12;
        row.NoteText.resizeTextMaxSize = 20;

        root.gameObject.SetActive(false);
        return row;
    }

    /// <summary>
    /// 按当前步骤重排舞台：决定这一拍内存里有几段、每段占几格、每格写什么值。
    /// 只在步骤变化（或面板高度变了）时调用，平时只做补间，避免每帧重设文字。
    /// 每一步都可以用 Patch 改写某段内存的字节 —— 所以「for 里 i 从 0 变到 1 变到 2」是真的同一格在变，不是新开一段。
    /// </summary>
    private void RebuildMemStage(int index, int step)
    {
        if (!HasMemPage(index) || memRoot == null || memRows == null)
            return;

        MemScene scene = tasks[index].Mem;
        int n = Mathf.Clamp(step, 0, scene.Steps.Length - 1);
        MemStep cur = scene.Steps[n];
        bool zh = Localization.IsChinese;

        // 刚 SetActive 的那一帧，自身 rect 有时还没算出宽高：退而用宿主面板的实测尺寸，
        // 实在拿不到才用半屏常量。宁可先按小的排，也不能按大的排 —— 排大了行会压到底部说明上。
        RectTransform memHost = memRoot.parent as RectTransform;
        float availW = memRoot.rect.width;
        float availH = memRoot.rect.height;
        if (availW < 120f)
            availW = (memHost != null && memHost.rect.width >= 120f ? memHost.rect.width : ReferenceWidth) - 52f;
        if (availH < 120f)
            availH = (memHost != null && memHost.rect.height >= 120f
                ? memHost.rect.height
                : SplitHalfHeight()) - GuideBodyTopInset - GuideBodyBottomInset;

        int reveal = Mathf.Clamp(cur.Reveal, 0, Mathf.Min(scene.Vars.Length, MemRowPool));
        int rows = Mathf.Max(1, reveal);

        // 排版用的高度：面板被蓝色长条拖矮时，内容不往上吸，仍按「基准高度」这块地排，
        // 于是底部的执行台 / 旁白顺着往下走出画面，而不是钉在原地看着字被越挤越窄。
        float flowH = Mathf.Max(availH, MemStageBaselineHeight());
        PlaceMemBottom(flowH);

        // 底部固定占位：旁白 + 执行台；其余高度留给行区
        float bottomH = MemNoteH + MemNoteGapY + MemExprH + 12f;
        float rowsArea = Mathf.Max(90f, flowH - MemHeaderH - 10f - bottomH);
        float gap = MemRowGapY;
        // 均分 + 先收行间距、再收行高：任何情况下 totalH 都不会超过 rowsArea，
        // 于是行永远不会压到底部的执行台 / 旁白上（旧写法把 blockH 抬到 56 又保底 gap，反而整体溢出）。
        float blockH = Mathf.Min((rowsArea - gap * (rows - 1)) / rows, 122f);
        if (blockH < 56f)
        {
            gap = rows > 1 ? 6f : 0f;
            blockH = Mathf.Max(34f, (rowsArea - gap * (rows - 1)) / rows);
        }
        float totalH = rows * blockH + gap * (rows - 1);
        float firstTop = -MemHeaderH - 10f - Mathf.Max(0f, (rowsArea - totalH) * 0.5f);

        // 左侧标签列：徽章 / 类型名牌 / 变量名，窗口窄时按比例收
        float leftW = Mathf.Min(330f, Mathf.Max(210f, availW * 0.44f));
        float badgeW = leftW >= 300f ? 54f : 42f;
        float typeW = Mathf.Clamp(leftW * 0.30f, 62f, 98f);
        float nameW = Mathf.Max(60f, leftW - badgeW - typeW - 6f);
        float cellX = badgeW + typeW + nameW + 8f;

        int maxBytes = 1;
        for (int i = 0; i < reveal; i++)
            if (scene.Vars[i] != null)
                maxBytes = Mathf.Max(maxBytes, Mathf.Clamp(CountBytes(scene.Vars[i]), 1, MemCellPerRow));
        float span = Mathf.Max(100f, availW - cellX - 4f);
        float cellW = Mathf.Clamp((span - MemCellGapX * (maxBytes - 1)) / maxBytes, 16f, 88f);
        // 格数多到摆不下时，先让变量名列让出宽度（名字可以截短，格子不能被挤出面板）
        float needW = cellX + cellW * maxBytes + MemCellGapX * (maxBytes - 1);
        if (needW > availW - 4f && nameW > 90f)
        {
            nameW -= Mathf.Min(nameW - 90f, needW - (availW - 4f));
            cellX = badgeW + typeW + nameW + 8f;
            span = Mathf.Max(100f, availW - cellX - 4f);
            cellW = Mathf.Clamp((span - MemCellGapX * (maxBytes - 1)) / maxBytes, 16f, 88f);
        }

        // 行说明搬到了字节格右侧：先给它留出 300 的宽度，格子太宽时先收格子（格子最小 30 仍放得下 4 位十六进制）
        float noteReserve = zh ? 300f : 400f;   // 英文行说明比中文长，右侧多留点宽度
        bool anyNote = false;
        for (int i = 0; i < reveal; i++)
            if (scene.Vars[i] != null && !string.IsNullOrEmpty(mNoteText(scene.Vars[i], zh)))
                anyNote = true;
        if (anyNote)
        {
            float cellByNote = (availW - cellX - 12f - noteReserve - MemCellGapX * (maxBytes - 1)) / maxBytes;
            if (cellByNote >= 30f)
                cellW = Mathf.Min(cellW, cellByNote);
        }
        // 行说明统一从「最宽那一行的格子右边缘」开始，几行说明才会左对齐成一条竖线，不会参差不齐
        float noteX = cellX + maxBytes * (cellW + MemCellGapX) + 12f;

        // 先算好每一步实际要显示的字节（本步 Patch 会覆盖原始值）
        string[][] bytes = new string[reveal][];
        for (int i = 0; i < reveal; i++)
        {
            MemVar mv = scene.Vars[i];
            int cnt = mv == null ? 1 : Mathf.Clamp(CountBytes(mv), 1, MemCellPerRow);
            bytes[i] = new string[cnt];
            string[] parts = mv == null || string.IsNullOrEmpty(mv.Bytes)
                ? new string[0] : mv.Bytes.ToUpperInvariant().Split(' ');
            for (int b = 0; b < cnt; b++)
                bytes[i][b] = b < parts.Length && !string.IsNullOrEmpty(parts[b]) ? parts[b] : "??";
        }
        if (cur.PatchVar != null && cur.PatchBytes != null)
        {
            int pk = Mathf.Min(cur.PatchVar.Length, cur.PatchBytes.Length);
            for (int k = 0; k < pk; k++)
            {
                int vi = cur.PatchVar[k];
                if (vi < 0 || vi >= reveal)
                    continue;
                string[] parts = string.IsNullOrEmpty(cur.PatchBytes[k])
                    ? new string[0] : cur.PatchBytes[k].ToUpperInvariant().Split(' ');
                for (int b = 0; b < bytes[vi].Length; b++)
                    bytes[vi][b] = b < parts.Length && !string.IsNullOrEmpty(parts[b]) ? parts[b] : "??";
            }
        }

        // 行池显隐
        for (int i = 0; i < MemRowPool; i++)
        {
            if (memRows[i] != null && memRows[i].Root != null && memRows[i].Root.gameObject.activeSelf != (i < reveal))
                memRows[i].Root.gameObject.SetActive(i < reveal);
        }

        int addr = 0x100;   // 地址只是示意：真实地址由系统决定，这里从 0x100 起按字节往上数
        for (int vi = 0; vi < reveal; vi++)
        {
            MemRowUI row = memRows[vi];
            MemVar mv = scene.Vars[vi] ?? new MemVar();
            if (row == null || row.Root == null)
                continue;

            int cnt = bytes[vi].Length;
            // 行说明从「格子下方的一行小字」改成「格子右侧的一段大字」：行高不再分给小字，
            // 右侧原本空着的区域正好用来放大字号（14 → 20），再也不会看不清。
            float cellH = Mathf.Max(28f, blockH - 4f);
            float noteLeft = noteX;
            // 英文模式优先用英文文案，没写就回落到中文，绝不出现空白
            string badgeText = mBadgeText(mv, zh);
            string typeText = mTypeText(mv, zh);
            string nameText = mNameText(mv, zh);
            string noteText = mNoteText(mv, zh);
            bool hasNote = !string.IsNullOrEmpty(noteText) && availW - noteLeft - 6f >= 120f;

            row.Root.anchoredPosition = new Vector2(0f, firstTop - vi * (blockH + gap));
            row.Root.sizeDelta = new Vector2(0f, blockH);
            row.Root.localScale = Vector3.one;

            // 整块底：幽灵行（代码里有、内存里没有）给一点深色，让它看起来「虚」
            if (row.FrameBg != null)
                row.FrameBg.color = mv.Ghost
                    ? UITheme.WithAlpha(UITheme.PanelDeep, 0.28f)   // 压淡一点就够：「虚」交给虚线格表达，不牺牲字的可读性
                    : UITheme.WithAlpha(UITheme.PanelDeep, 0f);

            // 徽章
            if (row.Badge != null)
            {
                bool hasBadge = !string.IsNullOrEmpty(badgeText);
                if (row.Badge.gameObject.activeSelf != hasBadge)
                    row.Badge.gameObject.SetActive(hasBadge);
                if (hasBadge)
                {
                    RectTransform br = row.Badge.rectTransform;
                    br.anchorMin = new Vector2(0f, 0f);
                    br.anchorMax = new Vector2(0f, 0f);
                    br.pivot = new Vector2(0f, 0f);
                    br.anchoredPosition = new Vector2(0f, cellH * 0.5f - 10f);
                    br.sizeDelta = new Vector2(badgeW - 8f, 20f);
                    SetLabel(row.Badge, badgeText);
                    row.Badge.color = UITheme.Purple;   // 幽灵行（注释 / 方法定义）文字同样正常亮度，只是格子画成虚线
                }
            }

            // 类型名牌
            if (row.TypeBg != null)
            {
                RectTransform tr = row.TypeBg.rectTransform;
                tr.anchorMin = new Vector2(0f, 0f);
                tr.anchorMax = new Vector2(0f, 0f);
                tr.pivot = new Vector2(0f, 0f);
                tr.anchoredPosition = new Vector2(badgeW, cellH * 0.5f - 13f);
                tr.sizeDelta = new Vector2(typeW - 8f, 26f);
                row.TypeBg.color = UITheme.WithAlpha(UITheme.Accent, mv.Ghost ? 0.10f : 0.16f);
            }
            if (row.TypeText != null)
            {
                SetLabel(row.TypeText, typeText ?? "");
                row.TypeText.color = UITheme.Accent;
            }

            // 变量名
            if (row.NameText != null)
            {
                RectTransform nr2 = row.NameText.rectTransform;
                nr2.anchorMin = new Vector2(0f, 0f);
                nr2.anchorMax = new Vector2(0f, 0f);
                nr2.pivot = new Vector2(0f, 0f);
                nr2.anchoredPosition = new Vector2(badgeW + typeW, cellH * 0.5f - 12f);
                nr2.sizeDelta = new Vector2(nameW, 24f);
                SetLabel(row.NameText, nameText ?? "");
                row.NameText.color = UITheme.TextPrimary;
            }

            // 字节格
            for (int b = 0; b < MemCellPerRow; b++)
            {
                bool show = b < cnt;
                if (row.CellRt[b] == null)
                    continue;
                if (row.CellRt[b].gameObject.activeSelf != show)
                    row.CellRt[b].gameObject.SetActive(show);
                if (!show)
                    continue;

                Vector2 pos = new Vector2(cellX + b * (cellW + MemCellGapX), 0f);
                row.CellPos[b] = pos;
                row.CellRt[b].anchoredPosition = pos;
                row.CellRt[b].sizeDelta = new Vector2(cellW, cellH);
                row.CellRt[b].localScale = Vector3.one;

                string hex = bytes[vi][b];
                bool unknown = hex == "??";
                if (row.CellAddr[b] != null)
                    row.CellAddr[b].text = mv.Ghost ? "—" : "0x" + (addr + b).ToString("X3");
                if (row.CellVal[b] != null)
                {
                    SetLabel(row.CellVal[b], unknown ? (mv.Ghost ? "--" : "??") : hex);
                    row.CellVal[b].color = mv.Ghost ? UITheme.TextBody
                        : (unknown ? UITheme.WithAlpha(UITheme.TextDim, 0.6f) : UITheme.TextPrimary);
                }
                if (row.CellBg[b] != null)
                    row.CellBg[b].color = mv.Ghost
                        ? UITheme.WithAlpha(UITheme.TextDim, 0.10f)
                        : UITheme.WithAlpha(UITheme.PanelRaised, 0.92f);
            }
            if (!mv.Ghost)
                addr += cnt;

            // 行说明：贴在字节格右侧、垂直居中，字号 20（自适应最多 20 / 最少 13）
            if (row.NoteText != null)
            {
                if (row.NoteText.gameObject.activeSelf != hasNote)
                    row.NoteText.gameObject.SetActive(hasNote);
                if (hasNote)
                {
                    RectTransform nrt = row.NoteText.rectTransform;
                    nrt.anchorMin = new Vector2(0f, 0f);
                    nrt.anchorMax = new Vector2(1f, 0f);
                    nrt.pivot = new Vector2(0f, 0f);
                    nrt.anchoredPosition = new Vector2(noteLeft, 2f);
                    nrt.sizeDelta = new Vector2(-(noteLeft + 6f), Mathf.Max(24f, cellH - 4f));
                    SetLabel(row.NoteText, noteText);
                    row.NoteText.color = UITheme.TextSub;
                }
            }
        }

        // 顶部说明与步数
        if (memHeaderText != null)
            SetLabel(memHeaderText, zh
                ? "一格 = 1 字节；int 占 4 格、char 占 1 格，地址从 0x100 起递增。\n3 写成 0x00000003，小端存放 = 低字节在低地址，所以从 0x100 往高读是 03 00 00 00"
                : "1 box = 1 byte. int = 4 boxes, char = 1, addresses grow from 0x100.\n3 is 0x00000003; little-endian keeps the low byte at the low address, so reading up from 0x100 gives 03 00 00 00.");
        if (memFootText != null)
            SetLabel(memFootText, (zh ? "第 " : "Step ") + (n + 1) + "/" + scene.Steps.Length + (zh ? " 步" : ""));
        if (memExprText != null)
        {
            // 代码行里的注释同样跟语言走：英文模式优先用 ExprEn，没写才回落到中文原行
            string expr = (!zh && !string.IsNullOrEmpty(cur.ExprEn)) ? cur.ExprEn : cur.Expr;
            SetLabel(memExprText, string.IsNullOrEmpty(expr) ? "" : "▶  " + expr);
        }
        if (memNoteText != null)
            SetLabel(memNoteText, zh ? cur.Zh : cur.En);
        RefreshMemKeyHint(zh, scene.Steps.Length);
    }

    /// <summary>
    /// 执行台右侧的键位提示：多步任务才提示「按下 ↓ 观看下一个」；
    /// 玩家点回代码区（输入框握住焦点）之后，↓ 归代码用，提示改成「↓ 代码模式」，绝不抢键。
    /// </summary>
    private void RefreshMemKeyHint(bool zh, int stepCount)
    {
        if (memKeyHintText == null)
            return;

        bool codeMode = editor != null && editor.IsOpen && editor.IsCodeInputFocused;
        string s;
        if (stepCount <= 1)
            s = string.Empty;
        else if (codeMode)
            s = zh ? "↓ 代码模式（移动光标）" : "↓ caret mode";
        else
            s = zh ? "↑↓ 看步骤   ←→ 翻页" : "↑↓ steps   ←→ pages";

        bool show = !string.IsNullOrEmpty(s);
        if (s == memKeyHintCache && memKeyHintText.gameObject.activeSelf == show)
            return;
        memKeyHintCache = s;
        if (memKeyHintText.gameObject.activeSelf != show)
            memKeyHintText.gameObject.SetActive(show);
        if (show)
        {
            SetLabel(memKeyHintText, s);
            memKeyHintText.color = codeMode ? UITheme.TextMuted : UITheme.Accent;
        }
    }

    /// <summary>
    /// 内存页的手动步进：↓ 下一步、↑ 上一步（到底 / 到顶循环）。
    /// 只在「代码输入框没有焦点」时归教学卡用；玩家一旦点回代码区，本方法直接返回 ——
    /// 上下键完全交回代码编辑器移动光标，不会被拿去看动画。
    /// </summary>
    private void HandleMemStepKey()
    {
        if (!Active || finished || tasks == null)
            return;
        if (guidePage < 2)
            return;
        int index = CurrentIndex;
        if (!HasMemPage(index))
            return;
        if (editor == null || !editor.IsOpen)
            return;
        if (editor.IsCodeInputFocused)
            return;                      // 代码区握着焦点：上下键是光标，不是翻页

        bool next = Input.GetKeyDown(KeyCode.DownArrow);
        bool prev = Input.GetKeyDown(KeyCode.UpArrow);
        if (!next && !prev)
            return;

        int count = tasks[index].Mem.Steps.Length;
        if (count <= 1)
            return;

        // ↓ 往后、↑ 往前，都在首尾循环，不用记「到底了没」
        animStep = next ? (animStep + 1) % count : (animStep - 1 + count) % count;
        animTimer = 0f;                  // 手动翻过之后重新计时，别刚翻完又被自动推进
        RebuildMemStage(index, animStep);
        AnimateMemStage(1f, index, animStep);
    }

    /// <summary>
    /// 补间：新出现的一段从右侧滑进来，正在被写入的那几个字节脉动发光 ——
    /// 让「内存里多出了一段」「这一格的值被改写了」这两件事都是看得见的。
    /// 全程只改颜色 / 位置 / 缩放，不碰文字，开销极小。
    /// </summary>
    private void AnimateMemStage(float t, int index, int step)
    {
        if (!HasMemPage(index) || memRoot == null || memRows == null || tasks[index].Mem == null)
            return;

        MemScene scene = tasks[index].Mem;
        int n = Mathf.Clamp(step, 0, scene.Steps.Length - 1);
        MemStep cur = scene.Steps[n];
        int reveal = Mathf.Clamp(cur.Reveal, 0, Mathf.Min(scene.Vars.Length, MemRowPool));
        int prevReveal = n > 0 ? Mathf.Clamp(scene.Steps[n - 1].Reveal, 0, Mathf.Min(scene.Vars.Length, MemRowPool)) : 0;

        float fade = Mathf.Clamp01(t * MemStepInterval / Mathf.Max(0.05f, MemFadeTime));
        fade = fade * fade * (3f - 2f * fade);                       // smoothstep：起步柔和、落位干脆
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.2f);

        for (int vi = 0; vi < reveal; vi++)
        {
            MemRowUI row = memRows[vi];
            if (row == null || row.Root == null || !row.Root.gameObject.activeSelf)
                continue;

            bool isNew = vi >= prevReveal;
            if (row.Root != null)
                row.Root.anchoredPosition = new Vector2(isNew ? (1f - fade) * 34f : 0f, row.Root.anchoredPosition.y);

            bool writing = vi == cur.Write;
            int cnt = row.CellRt == null ? 0 : row.CellRt.Length;
            for (int b = 0; b < cnt; b++)
            {
                if (row.CellRt[b] == null || !row.CellRt[b].gameObject.activeSelf)
                    continue;
                if (row.CellBg[b] == null)
                    continue;
                if (writing)
                {
                    float glow = 0.16f + 0.30f * pulse;
                    row.CellBg[b].color = UITheme.WithAlpha(UITheme.Accent, glow);
                    if (row.CellVal[b] != null)
                        row.CellVal[b].color = UITheme.AccentSoft;
                }
                else
                {
                    MemVar mv = scene.Vars[vi];
                    bool ghost = mv != null && mv.Ghost;
                    row.CellBg[b].color = ghost
                        ? UITheme.WithAlpha(UITheme.TextDim, 0.10f)
                        : UITheme.WithAlpha(UITheme.PanelRaised, 0.92f);
                    if (row.CellVal[b] != null)
                        row.CellVal[b].color = ghost ? UITheme.TextBody : UITheme.TextPrimary;
                }
            }
        }
    }

    /// <summary>第 3 页（内存页）每帧驱动：按真实时间推进步骤，并做补间；编辑器打开时 timeScale=0，所以用 unscaledDeltaTime。</summary>
    private void AnimMemFrame()
    {
        int index = CurrentIndex;
        if (!HasMemPage(index) || memRoot == null)
            return;

        MemScene scene = tasks[index].Mem;
        int count = scene.Steps.Length;
        if (count <= 0)
            return;

        // 不自动播放：步骤只由玩家按 ↓ 手动推进（HandleMemStepKey）。
        // 这里只让计时器跑到满格后停住，好让「新行滑入 / 写入字节脉动」这一段补间自己播完一遍。
        animTimer = Mathf.Min(animTimer + Time.unscaledDeltaTime, MemStepInterval);

        int step = Mathf.Clamp(animStep, 0, count - 1);
        float h = memRoot.rect.height;
        if (memBuiltIndex != index || memBuiltStep != step || Mathf.Abs(memBuiltH - h) > 1f)
            RebuildMemStage(index, step);

        AnimateMemStage(Mathf.Clamp01(animTimer / MemStepInterval), index, step);

        // 焦点会在「代码区 ↔ 教学卡」之间随时切换，键位提示跟着变：
        // 在代码区就写「↓ 代码模式」，在教学卡上才写「按下 ↓ 观看下一个」。
        RefreshMemKeyHint(Localization.IsChinese, count);
    }

    /// <summary>
    /// 第 3 页的时间驱动入口。两种第 3 页都从这里走：
    /// 有内存可视化场景（Mem）→ 走图形舞台 AnimMemFrame（它自己管步骤与补间）；
    /// 否则回落到旧的「逐行高亮 + 旁白」。
    /// </summary>
    private void AdvanceAnim()
    {
        if (!Active || finished || guidePage < 2 || tasks == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;

        if (HasMemPage(CurrentIndex))
        {
            HandleMemStepKey();   // 先吃 ↓ 手动步进（玩家在代码区时它自己会让开，把 ↓ 交回代码编辑器）
            AnimMemFrame();
            return;
        }

        // 旧的「逐行高亮」页同样改成手动：↓ 一步一步看，绝不偷偷自动播
        HandleAnimStepKey();
    }

    /// <summary>
    /// 旧「逐行高亮」页的手动步进：↓ 下一步、↑ 上一步（首尾循环）。
    /// 只在代码区没握焦点时归教学卡用；玩家点回代码区之后本方法直接返回，上下键完全交回代码编辑器移动光标。
    /// </summary>
    private void HandleAnimStepKey()
    {
        if (!Active || finished || tasks == null || guidePage < 2)
            return;
        int index = CurrentIndex;
        if (index < 0 || index >= tasks.Length)
            return;
        AnimStep[] anim = tasks[index].Anim;
        if (anim == null || anim.Length < 2)
            return;
        if (editor == null || !editor.IsOpen || editor.IsCodeInputFocused)
            return;

        bool next = Input.GetKeyDown(KeyCode.DownArrow);
        bool prev = Input.GetKeyDown(KeyCode.UpArrow);
        if (!next && !prev)
            return;

        animStep = next ? (animStep + 1) % anim.Length : (animStep - 1 + anim.Length) % anim.Length;
        animTimer = 0f;
        RefreshAnimFrame();
    }

    /// <summary>
    /// ← / → 翻页：左退回上一页、右进到下一页（第 3 页的细节页也算一页）。
    /// 代码区握着焦点时不抢键 —— 那时光标要在代码里左右移动。
    /// </summary>
    private void HandlePageNavKey()
    {
        if (!Active || finished || tasks == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;
        if (editor != null && editor.IsOpen && editor.IsCodeInputFocused)
            return;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            PrevPage();
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            NextPage();
    }

    /// <summary>只重画两栏文案（不动布局 / 字号），够动画用且开销小。</summary>
    private void RefreshAnimFrame()
    {
        if (tasks == null)
            return;
        int index = CurrentIndex;
        if (index < 0 || index >= tasks.Length || !HasAnimPage(index))
            return;

        bool zh = Localization.IsChinese;
        if (bodyText != null)
            bodyText.text = BuildGuideBody(index, zh, BuildAnimGuide(index, zh));
        if (sampleText != null)
            sampleText.text = BuildAnimCode(index, zh);
    }

    private static string mNoteText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.NoteEn)) ? v.NoteEn : v.Note;
    }

    private static string mNameText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.NameEn)) ? v.NameEn : v.Name;
    }

    private static string mTypeText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.TypeEn)) ? v.TypeEn : v.Type;
    }

    private static string mBadgeText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.BadgeEn)) ? v.BadgeEn : v.Badge;
    }

    /// <summary>刷新翻页条：该任务有多页时才显示，并按当前页码更新文字与按钮可点状态（index = -1 表示隐藏）。</summary>
    private void RefreshPageNav(int index)
    {
        if (pageNavRoot == null)
            return;

        bool show = !finished && tasks != null && index >= 0 && index < tasks.Length && PageCountOf(index) > 1;
        if (pageNavRoot.activeSelf != show)
            pageNavRoot.SetActive(show);
        if (!show)
            return;

        bool zh = Localization.IsChinese;
        int pageCount = PageCountOf(index);
        SetLabel(pageNoText, zh
            ? "细节 " + (guidePage + 1) + "/" + pageCount
            : "Detail " + (guidePage + 1) + "/" + pageCount);

        if (prevPageBtn != null)
            prevPageBtn.interactable = guidePage > 0;
        if (nextPageBtn != null)
            nextPageBtn.interactable = guidePage < pageCount - 1;
    }

    /// <summary>构建翻页条：‹ 细节 1/2 ›，横向贴在任务名右侧同一行。</summary>
    private void BuildPageNav(Transform panelRt)
    {
        RectTransform navRt = CreateRect("PageNav", panelRt, new Vector2(1f, 1f), Vector2.zero,
            new Vector2(PageNavWidth, 32f), new Vector2(1f, 1f));
        navRt.anchoredPosition = new Vector2(-24f, -90f);
        pageNavRoot = navRt.gameObject;

        // ‹ 上一页：贴翻页条左端
        prevPageBtn = CreateNavButton(navRt, "PrevPage", "‹", TextAnchor.MiddleCenter,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(32f, 30f));
        prevPageBtn.onClick.AddListener(PrevPage);

        // › 下一页：贴翻页条右端
        nextPageBtn = CreateNavButton(navRt, "NextPage", "›", TextAnchor.MiddleCenter,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(32f, 30f));
        nextPageBtn.onClick.AddListener(NextPage);

        // 中间页码：「细节 1/2」
        pageNoText = CreateText("PageNo", navRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleCenter);
        RectTransform pnRt = pageNoText.rectTransform;
        pnRt.anchorMin = new Vector2(0f, 0f);
        pnRt.anchorMax = new Vector2(1f, 1f);
        pnRt.offsetMin = new Vector2(38f, 0f);
        pnRt.offsetMax = new Vector2(-38f, 0f);

        pageNavRoot.SetActive(false);
    }

    /// <summary>
    /// 构建「蓝色分割线」的拖动热区：一条横跨面板、骑在顶部强调线上的透明横带（高 DragHandleHeight），
    /// 上下拖动即调整提示区高度（向上拖 = 提示区变高，向下拖 = 提示区变矮、代码区变高）。
    /// 拖动只改高度、不重算字号：字号保持玩家看到的大小，放不下的部分直接截断。
    /// </summary>
    private void BuildSplitDragHandle(RectTransform panelRt)
    {
        RectTransform handleRt = CreateRect("SplitDragHandle", panelRt, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, DragHandleHeight), new Vector2(0.5f, 0.5f));
        handleRt.anchorMin = new Vector2(0f, 1f);
        handleRt.anchorMax = new Vector2(1f, 1f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.anchoredPosition = new Vector2(0f, 0f); // 跨在强调线上：上方外沿 DragHandleHeight/2 也能抓
        handleRt.sizeDelta = new Vector2(0f, DragHandleHeight);

        Image hit = handleRt.gameObject.AddComponent<Image>();
        hit.sprite = WhiteSprite();
        hit.color = new Color(1f, 1f, 1f, 0f); // 完全透明：视觉上仍是那条 3px 蓝线，但整条都能拖
        hit.raycastTarget = true;

        splitDragHandle = handleRt.gameObject.AddComponent<SplitDragHandle>();
        splitDragHandle.Owner = this;
        splitDragHandle.Hit = hit;
    }

    /// <summary>分割线被拖动：把屏幕像素增量换成 UI 像素，叠加到 manualSplitOffset 后重排（不重算字号）。</summary>
    internal void DragSplitLineBy(float screenDeltaY)
    {
        if (screenDeltaY == 0f)
            return;
        float scale = 1f;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.scaleFactor > 0.0001f)
            scale = canvas.scaleFactor;
        manualSplitOffset += screenDeltaY / scale;
        ApplySplitLayout(); // 拖蓝色长条：只改上下高度，字号恒定（不重排、不缩放）
    }

    /// <summary>鼠标悬停在分割线上时给一点视觉反馈（半透明蓝带），让玩家知道这条线能拖。</summary>
    internal void SetSplitHandleHover(bool on)
    {
        if (splitDragHandle == null || splitDragHandle.Hit == null)
            return;
        splitDragHandle.Hit.color = on
            ? UITheme.WithAlpha(UITheme.Accent, 0.22f)
            : new Color(1f, 1f, 1f, 0f);
        if (accentLine != null)
            accentLine.color = UITheme.WithAlpha(UITheme.Accent, on ? 1f : 0.95f);
    }

    /// <summary>蓝色分割线的拖动把手：只处理「按下拖动 → 回调控制器」，其余交给控制器。</summary>
    private sealed class SplitDragHandle : MonoBehaviour,
        UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler,
        UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public CodeSyntaxTutorialController Owner;
        public Image Hit;

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.SetSplitHandleHover(true);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.DragSplitLineBy(e.delta.y);
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.SetSplitHandleHover(true);
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.SetSplitHandleHover(false);
        }
    }

    /// <summary>创建一个翻页按钮（深色底 + 箭头文字，点击回调由调用方挂接）。</summary>
    private Button CreateNavButton(Transform parent, string name, string label, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        RectTransform rt = CreateRect(name, parent, anchor, pos, size, pivot);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.PanelSunk;
        bg.raycastTarget = true;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint; // 可点 / 不可点由 interactable 的着色体现

        Text t = CreateText("Label", rt, label, 20, UITheme.TextPrimary, align);
        Stretch(t.rectTransform);
        return btn;
    }

    /// <summary>进度格悬停高亮：在基准色上提亮，提示这一格可以点击。</summary>
    private void SetChipHover(int index, bool on)
    {
        if (chipBgs == null || chipBaseColors == null)
            return;
        if (index < 0 || index >= chipBgs.Length || chipBgs[index] == null)
            return;

        Color baseColor = chipBaseColors[index];
        chipBgs[index].color = on
            ? new Color(Mathf.Min(1f, baseColor.r * 1.5f + 0.10f),
                        Mathf.Min(1f, baseColor.g * 1.5f + 0.10f),
                        Mathf.Min(1f, baseColor.b * 1.5f + 0.10f),
                        baseColor.a)
            : baseColor;
    }

    private void CompleteCurrentTask()
    {
        if (CurrentIndex < 0 || CurrentIndex >= TaskCount)
            return;

        done[CurrentIndex] = true;
        // 真做过的章节写盘（跨重启 / 跨跳关保留）：进度格显示的是这张表，而不是「序号之前的都算做过」
        GameProgress.SaveTaskDone(DoneKeyOf(CurrentCourse), done);
        int completed = CurrentIndex + 1;
        CurrentIndex++;
        guidePage = 0; // 新任务从第 1 页开始
        ResetAnim();   // 动画也从头开演

        // 断点续玩：章节（任务）推进即把「关卡 + 分区 + 章节」写入存档并 Save()
        //   · 语法课（Course.Grammar）= Level 1 - Part 3，API 课（Course.Api）= Level 2 - Part 2；
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
        //   语法课（Level 1 - Part 3）→ 关卡 1 通关，解锁关卡 2；
        //   API 课（Level 2 - Part 2）→ 关卡 2 通关，解锁关卡 3。
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
        // 切语言后内存页必须重排：行说明 / 类型名牌都是按语言取的，不重置就会停在旧语言上
        memBuiltIndex = -1;
        memBuiltStep = -1;
        memKeyHintCache = null;
        // 文案变化后重新排版：面板高度仍是固定半屏（不随文案变），只按新文案重算正文自适应字号
        ApplySplitLayout();
    }

    /// <summary>文本创建时是空串，字体按英文（JetBrains Mono）选的；写入中文后必须重选字体，否则中文缺字形。</summary>
    private void RefreshFonts()
    {
        ReloadFont(headlineText);
        ReloadFont(progressText);
        ReloadFont(taskNameText);
        ReloadFont(bodyText);
        ReloadFont(sampleText);
        ReloadFont(flashText);
        ReloadFont(hintText);
        ReloadFont(pageNoText);
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

    /// <summary>赋值并同步字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）。</summary>
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
                    ? "关卡 2 · 第 2 部分 · 炮台 API 调用教学"
                    : "Level 2 · Part 2 · Tower API Tutorial";
            }
            else
            {
                headlineText.text = zh
                    ? "关卡 1 · 第 3 部分 · C# 基础语法教学（零基础手把手）"
                    : "Level 1 · Part 3 · C# Syntax Tutorial";
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
                ? UITheme.ValueBright
                : (isCurrent ? UITheme.TextPrimary : UITheme.TextMuted);
            if (chipBgs[i] != null)
            {
                Color chipColor = isDone
                    ? UITheme.WithAlpha(UITheme.Value, 0.22f)   // 已完成：绿
                    : (isCurrent
                        ? UITheme.WithAlpha(UITheme.Accent, 0.20f) // 当前：蓝
                        : UITheme.PanelSunk); // 未开始：灰
                chipBgs[i].color = chipColor;
                if (chipBaseColors != null)
                    chipBaseColors[i] = chipColor; // 供悬停高亮还原
            }
        }

        // 「当前在第几个任务」与「实际做完几个任务」是两回事：用 /level 直接跳到第 10 个任务时，
        // 前面 9 个并没有做过，因此进度文案一律以真实完成数为准，避免看起来像「前面全做完了」。
        int doneCount = 0;
        if (done != null)
            for (int i = 0; i < done.Length && i < TaskCount; i++)
                if (done[i])
                    doneCount++;

        if (progressText != null)
        {
            int shown = Mathf.Min(CurrentIndex + 1, TaskCount);
            if (finished)
            {
                // 跳关走完最后一章 ≠ 每一章都做过：只有真做满 TaskCount 个任务才敢说「全部完成」
                if (doneCount >= TaskCount)
                    progressText.text = zh ? "全部完成" : "All done";
                else if (zh)
                    progressText.text = "本课已走完 · 已完成 " + doneCount + "/" + TaskCount;
                else
                    progressText.text = "Course finished · done " + doneCount + "/" + TaskCount;
            }
            else if (zh)
                progressText.text = "任务 " + shown + "/" + TaskCount + " · 已完成 " + doneCount + "/" + TaskCount;
            else
                progressText.text = "Task " + shown + "/" + TaskCount + " · done " + doneCount + "/" + TaskCount;
        }

        if (finished)
        {
            // 走完最后一章 ≠ 每章都做过（可能是 /level 跳过来的），标题也按真实完成数给
            taskNameText.text = doneCount >= TaskCount
                ? (zh ? "✔ 教学完成" : "✔ Tutorial complete")
                : (zh ? "✔ 本课已走完（" + doneCount + "/" + TaskCount + "）"
                      : "✔ Course finished (" + doneCount + "/" + TaskCount + ")");
            if (bodyText != null)
            {
                if (!bodyText.gameObject.activeSelf)
                    bodyText.gameObject.SetActive(true);   // 内存页隐藏过，结业文案要能看见
                bodyText.text = CurrentCourse == Course.Api
                    ? (zh
                        ? "你已经用到了全部炮台 API：GetAllObjects、TowerObjectInfo 的 Index / Type / HP / Position /\n"
                          + "Velocity / Volume 字段、GetFireCooldown、FireAtPlane、PrintLine / Print / ClearPrint。\n\n"
                          + "关卡已开始刷新方块：按 Esc 关闭编辑器，你写的程序就会每帧自动运行、驱动炮台。\n"
                          + "想继续改进代码：按 Enter 重新打开编辑器修改，按 F6 重新编译运行。\n"
                          + "想从头再学一遍：在开发者指令框输入 /level 2 2，进度会自动重置。"
                        : "You have used the whole tower API: GetAllObjects, TowerObjectInfo.Index / Type / HP /\n"
                          + "Position / Velocity / Volume, GetFireCooldown, FireAtPlane, PrintLine / Print / ClearPrint.\n\n"
                          + "Blocks are now spawning: press Esc to close the editor and your program runs every frame.\n"
                          + "Press Enter to reopen the editor and F6 to recompile.\n"
                          + "Type /level 2 2 in the console to restart this tutorial.")
                    : (zh
                        ? "你已经用到了变量、const、运算符、关系 / 逻辑运算、if / if-else / switch、for / while / do-while、\n"
                          + "数组、List、方法定义与返回值、字符串拼接与 string.Format、注释。\n\n"
                          + "关卡已开始刷新方块：按 Esc 关闭编辑器，你写的程序就会每帧自动运行、驱动炮台。\n"
                          + "想继续改进代码：按 Enter 重新打开编辑器修改，按 F6 重新编译运行。\n"
                          + "想学炮台 API：在开发者指令框输入 /level 2 2。想重学本关：输入 /level 1 3。"
                        : "You have used variables, const, operators, comparisons, logic, if / if-else / switch,\n"
                          + "for / while / do-while, arrays, List, methods & return values, string concat & Format, comments.\n\n"
                          + "Blocks are now spawning: press Esc to close the editor and your program runs every frame.\n"
                          + "Press Enter to reopen the editor and F6 to recompile.\n"
                          + "For the tower API tutorial type /level 2 2; to restart this one type /level 1 3.");
            }
            if (sampleText != null)
                sampleText.text = string.Empty;
            if (memRoot != null && memRoot.gameObject.activeSelf)
                memRoot.gameObject.SetActive(false);   // 全部完成：内存舞台也让位给结业文案
            RefreshPageNav(-1); // 全部完成：没有分页可翻
            LayoutGuideColumns();
            return;
        }

        int index = Mathf.Clamp(CurrentIndex, 0, Mathf.Max(0, TaskCount - 1));
        if (tasks != null && index < tasks.Length)
        {
            int pageCount = PageCountOf(index);
            guidePage = Mathf.Clamp(guidePage, 0, Mathf.Max(0, pageCount - 1));

            taskNameText.text = zh ? tasks[index].TitleZh : tasks[index].TitleEn;
            if (bodyText != null)
            {
                string guidePart;
                string samplePart = string.Empty;
                bool memPage = guidePage >= 2 && HasMemPage(index);
                if (memPage)
                {
                    // 第 3 页 = 内存可视化：整块正文区交给图形舞台（行 / 字节格 / 地址 / 执行台 / 旁白），
                    // 文字栏一律清空，翻到本页时动画从头开演。
                    guidePart = string.Empty;
                    samplePart = string.Empty;
                    animStep = 0;
                    animTimer = 0f;
                    memBuiltIndex = -1;
                    memBuiltStep = -1;
                    memBuiltH = -1f;
                }
                else if (guidePage >= 2 && HasAnimPage(index))
                {
                    // 第 3 页 = 代码动画：右栏放示例代码（当前执行行高亮），左栏放旁白 + 进度点
                    guidePart = BuildAnimGuide(index, zh);
                    samplePart = BuildAnimCode(index, zh);
                }
                else if (guidePage > 0)
                {
                    // 第 2 页 = 零基础手把手细节（怎么一步步做出来 / 常见写错的样子）：
                    // 整段放左栏通栏显示，不配示例代码（示例已经在第 1 页右侧）
                    guidePart = (zh ? tasks[index].Page2Zh : tasks[index].Page2En) ?? string.Empty;
                }
                else
                {
                    SplitTaskBody(zh ? tasks[index].BodyZh : tasks[index].BodyEn, out guidePart, out samplePart);
                }
                // 第 3 页（内存页）整块交给图形舞台：连 BuildGuideBody 那条尾巴（【蓝色长条】拖动提示 /
                // ① 语法 / ② 编译 状态）一并不要 —— 它们是给文字页看的，留在图上就是压字。
                // 文字对象直接隐藏，不只是清空文本：避免任何残留排版参与遮挡。
                bodyText.text = memPage ? string.Empty : BuildGuideBody(index, zh, guidePart);
                if (bodyText.gameObject.activeSelf == memPage)
                    bodyText.gameObject.SetActive(!memPage);
                if (sampleText != null)
                    sampleText.text = samplePart;

                // 舞台随页面显隐：只有「第 3 页 + 本任务有内存场景」时才露面
                if (memRoot != null)
                {
                    if (memRoot.gameObject.activeSelf != memPage)
                        memRoot.gameObject.SetActive(memPage);
                    if (memPage)
                    {
                        int ms = Mathf.Clamp(animStep, 0, tasks[index].Mem.Steps.Length - 1);
                        RebuildMemStage(index, ms);
                        AnimateMemStage(1f, index, ms);   // 首帧直接落位，不要从全透明开始
                    }
                }

                LayoutGuideColumns();
            }
            RefreshPageNav(index);
        }
    }

    /// <summary>
    /// 把任务正文按第一个空行拆成两段：首段 = 写法 / 签名 / 示例代码（放右栏），其余 = 目标 / 提示 / 判定（放左栏）。
    /// 首段本身就是引导（「目标：」/「Goal:」开头）或全文没有空行时，整段留左栏通栏显示。
    /// </summary>
    private static void SplitTaskBody(string main, out string guide, out string sample)
    {
        guide = main ?? string.Empty;
        sample = string.Empty;
        if (string.IsNullOrEmpty(main))
            return;

        int cut = main.IndexOf("\n\n", System.StringComparison.Ordinal);
        if (cut <= 0)
            return;

        string head = main.Substring(0, cut);
        bool headIsGuide = head.StartsWith("目标", System.StringComparison.Ordinal)
            || head.StartsWith("Goal", System.StringComparison.Ordinal);
        if (headIsGuide)
            return;

        sample = head;
        guide = main.Substring(cut + 2).TrimStart('\n');
    }

    /// <summary>左栏正文 = 目标 / 提示 / 判定 + 本任务完成条件（语法 & 编译两条状态）。</summary>
    private string BuildGuideBody(int index, bool zh, string guide)
    {
        string main = guide ?? string.Empty;

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
                ? "⏳ 等待编译 —— 写出右侧的语法后，点标题栏【编译运行 (F6)】验证"
                : "⏳ Not compiled yet — write the syntax on the right, then click [Compile & Run (F6)]";
        }

        string syntaxState = hit
            ? (zh ? "✔ 已命中" : "✔ Found")
            : (guidePage > 0
                ? (zh ? "✘ 未命中 —— 照本页①的步骤写出来，再点【编译运行 (F6)】" : "✘ Missing — follow the step-by-step list on this page, then click [Compile & Run (F6)]")
                : (zh ? "✘ 未命中 —— 请按右侧的写法补全" : "✘ Missing — add it as shown on the right"));

        StringBuilder sb = new StringBuilder(main.Length + 320);
        sb.Append(main);

        // 蓝色长条提示：只在 Level 1 - Part 3（语法课）的每一章出现 —— 新手不知道卡顶那条蓝线能拖，
        // 字号固定后文案过长会被截断，必须先告诉他可以把提示区拉高；API 课及之后不再重复提示。
        if (CurrentCourse == Course.Grammar)
        {
            sb.Append(zh
                ? "\n\n【蓝色长条】按住本卡顶部的蓝色长条上下拖动，可随时调整「提示区 / 代码区」的高度，字号不变。"
                : "\n\n[Blue bar] Drag the blue bar at the top of this card up or down to resize the hint area and the code area. The font size stays the same.");
        }

        // 第 1 页底部提示「还有一页手把手细节」：完全不懂代码的人通常不会主动去点右上角的翻页箭头，
        // 所以在第 1 页明确告诉他下一步该翻页（第 2 页是逐步操作 + 常见写错的样子，第 3 页是代码动画）。
        if (guidePage == 0 && PageCountOf(index) > 1)
        {
            int pageCount = PageCountOf(index);
            sb.Append(zh
                ? "\n\n【不会写？】点本卡右上角「‹ 细节 1/" + pageCount + " ›」，或直接按键盘 ← / → 前后翻页：\n"
                  + "第 2 页写着一共几步、每一步敲什么、写在哪一行，以及三个最常见的写错方式。"
                  + (pageCount >= 3 ? "\n再按一次 → 还有第 3 页【内存可视化】：把 int / float / bool / char 在内存里怎么摆、每格到底是什么字节，一格一格画给你看。" : "")
                : "\n\n[Stuck?] Click \"‹ Detail 1/" + pageCount + " ›\" at the top right of this card, or just press ← / → on the keyboard to flip pages:\n"
                  + "Page 2 lists every step, what to type, where to type it, and the three most common mistakes."
                  + (pageCount >= 3 ? "\nPress → once more for the memory view: it draws how ints, floats, bools and chars are actually laid out in memory, byte by byte." : ""));
        }

        sb.Append(zh ? "\n\n—— 本任务完成条件（两项都满足才算完成）——\n" : "\n\n—— Complete when BOTH are satisfied ——\n");
        sb.Append(zh ? "① 语法：" : "① Syntax: ").Append(syntaxState).Append('\n');
        sb.Append(zh ? "② 编译：" : "② Compile: ").Append(compileState);
        return sb.ToString();
    }

    private void SyncVisibility()
    {
        if (panelRoot == null)
            return;

        // 任务卡只在编辑器打开时显示；编辑器关闭时玩家看到的是实机页面，
        // 由页面正中的进入提示（按下 Enter 打开代码编辑界面）引导，不遮挡任何玩法元素。
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

        // 主行：实机页面上最醒目的一句 —— 按下 Enter 打开代码编辑界面
        string main = zh ? "按下 Enter 打开代码编辑界面" : "Press Enter to open the code editor";

        // 副行：当前任务进度 / 完成后可随时回来改代码
        string sub;
        if (finished)
        {
            // 走完最后一章 ≠ 每一章都做过（可能是 /level 跳过来的）：没做满就只报「本课已走完 + 真实完成数」
            int allDoneCount = 0;
            if (done != null)
                for (int i = 0; i < done.Length && i < TaskCount; i++)
                    if (done[i])
                        allDoneCount++;
            bool reallyAll = allDoneCount >= TaskCount;
            if (CurrentCourse == Course.Api)
                sub = reallyAll
                    ? (zh ? "炮台 API 教学已全部完成 —— 按 Enter 可随时回来修改代码" : "Tower API course complete — press Enter anytime to edit your code")
                    : (zh ? "本课已走完 · 已完成 " + allDoneCount + "/" + TaskCount + " —— 按 Enter 可随时回来补做"
                          : "Course finished · done " + allDoneCount + "/" + TaskCount + " — press Enter anytime to catch up");
            else
                sub = reallyAll
                    ? (zh ? "C# 语法教学已全部完成 —— 按 Enter 可随时回来修改代码" : "C# syntax course complete — press Enter anytime to edit your code")
                    : (zh ? "本课已走完 · 已完成 " + allDoneCount + "/" + TaskCount + " —— 按 Enter 可随时回来补做"
                          : "Course finished · done " + allDoneCount + "/" + TaskCount + " — press Enter anytime to catch up");
        }
        else
        {
            int shown = Mathf.Clamp(CurrentIndex + 1, 1, Mathf.Max(1, TaskCount));
            string title = "";
            if (tasks != null && CurrentIndex >= 0 && CurrentIndex < tasks.Length)
                title = zh ? tasks[CurrentIndex].TitleZh : tasks[CurrentIndex].TitleEn;
            sub = zh
                ? "当前任务 " + shown + "/" + TaskCount + "：" + title + "（进入编辑器后写代码，写完点【编译运行 (F6)】）"
                : "Task " + shown + "/" + TaskCount + ": " + title + " (write the code in the editor, then click [Compile & Run (F6)])";
        }

        string text = "<color=#5CD8F2>" + main + "</color>\n<size=22><color=#A8C4D6>" + sub + "</color></size>";

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
            "任务 1：注释",
            "Task 1: Comments",
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

        list[1] = Def(
            "任务 2：变量声明与类型",
            "Task 2: Variables & Types",
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

        list[2] = Def(
            "任务 3：常量 const",
            "Task 3: const Constants",
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

        list[3] = Def(
            "任务 4：算术运算符与表达式",
            "Task 4: Arithmetic Operators",
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

        list[4] = Def(
            "任务 5：关系运算符",
            "Task 5: Comparison Operators",
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

        list[5] = Def(
            "任务 6：逻辑运算符",
            "Task 6: Logical Operators",
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

        list[6] = Def(
            "任务 7：if 条件判断",
            "Task 7: if Statements",
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

        list[7] = Def(
            "任务 8：if … else …",
            "Task 8: if / else",
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

        list[8] = Def(
            "任务 9：switch 多分支",
            "Task 9: switch",
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

        list[9] = Def(
            "任务 10：for 循环",
            "Task 10: for Loops",
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

        list[10] = Def(
            "任务 11：while 循环",
            "Task 11: while Loops",
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

        list[11] = Def(
            "任务 12：do … while 循环",
            "Task 12: do / while Loops",
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

        list[12] = Def(
            "任务 13：一维数组",
            "Task 13: Arrays",
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

        list[13] = Def(
            "任务 14：List 集合",
            "Task 14: List<T>",
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

        list[14] = Def(
            "任务 15：方法定义与参数",
            "Task 15: Methods & Parameters",
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

        list[15] = Def(
            "任务 16：方法返回值 return",
            "Task 16: Return Values",
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

        list[16] = Def(
            "任务 17：字符串拼接",
            "Task 17: String Concatenation",
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

        list[17] = Def(
            "任务 18：string.Format 格式化",
            "Task 18: string.Format",
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

        AppendGrammarDetailPages(list); // 每项任务补「第 2 页」手把手细节（任务卡右上角翻页查看）
        AppendGrammarAnimPages(list);   // 每项任务再补「第 3 页」代码动画（高亮逐行走，演代码在干什么）
        AppendGrammarMemPages(list);    // 语法课的第 3 页升级为「内存可视化」：画出 char / int 在内存里到底怎么摆
        return list;
    }


    // ================== 第 2 页：零基础手把手细节（每任务一页，任务卡右上角 ‹ › 翻页） ==================

    /// <summary>
    /// 给语法课 18 项任务补上「第 2 页」：完全不懂代码的人照着做就能完成的那一页。
    /// 第 1 页讲写法与示例，第 2 页讲「这一步到底在做什么 / 手把手第几步按哪里 / 逐行拆解 / 新手最容易踩的坑」，
    /// 并参照 runoob C# 教程补充了类型转换、运算符优先级、真值表、数组与 List 差异、局部函数等概念。
    /// </summary>
        private static void AppendGrammarDetailPages(TaskDef[] list)
    {
        list[0].Page2Zh = "【这一页：亲手敲一遍，再故意写错一次】\n"
            + "① 点上方代码区空白处，让光标在那里闪，就可以开始敲。\n"
            + "② 敲两个英文斜杠 //，后面随便写一句话，回车。\n"
            + "③ 需要写多行说明就用 /* 开头、*/ 结尾，中间可以换行。\n"
            + "④ 点【编译运行 (F6)】：注释不参与运行，能编译就是对的。\n"
            + "【逐符号读懂它】\n"
            + "· // ：从这里到本行末尾，全部给人看，程序看不见。\n"
            + "· /* */ ：成对出现，中间的换行也算注释，不能只写一半。\n"
            + "【三个必踩的坑】\n"
            + "✘ 中文输入法敲出「／／」→ 全角斜杠报 CS1056 意外字符，切英文再敲。\n"
            + "✘ 只写 /* 不写 */ → 报 CS1035 注释未结束，后面代码全被吃掉。\n"
            + "✔ 想临时屏蔽一行代码：在行首加 //，比删掉更安全。";
        list[0].Page2En = "THIS PAGE: TYPE IT YOURSELF, THEN BREAK IT ON PURPOSE\n"
            + "1) Click a blank spot in the code area and let the caret blink.\n"
            + "2) Type two forward slashes // and any sentence after them, then Enter.\n"
            + "3) For several lines use /* to open and */ to close; newlines are allowed.\n"
            + "4) Press [Compile & Run (F6)]. A comment never runs; compiling clean means it is valid.\n"
            + "READ EVERY SYMBOL\n"
            + "- // : from here to the end of the line is for humans only.\n"
            + "- /* */ : always a pair; half a pair swallows the rest of the file.\n"
            + "THREE TRAPS\n"
            + "- Full-width slashes from a Chinese IME give CS1056; switch to English input.\n"
            + "- Missing */ gives CS1035 and eats every line below it.\n"
            + "- To mute one line fast, put // in front of it instead of deleting.";
        list[1].Page2Zh = "【这一页：亲手敲出四个不同类型的变量】\n"
            + "① 光标进代码区，先敲类型：int / float / bool / string。\n"
            + "② 空一格写名字：只能用字母、数字、下划线，不能用数字开头。\n"
            + "③ 写 = 和初始值，行末必须敲英文分号 ;。\n"
            + "④ 四行敲完，点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· int hp = 100; int 只装整数，= 把右边放进左边，; 表示一句结束。\n"
            + "· float sp = 1.5f; 尾巴那个 f 必须写，否则 1.5 被当成 double 而报错。\n"
            + "· bool ok = true; 只装 true / false，不加引号。\n"
            + "· string tag = \"塔\"; 文字要用英文双引号，中文引号不认。\n"
            + "【三个必踩的坑】\n"
            + "✘ int hp = 1.5; → 报 CS0266 无法把 double 隐式转成 int，小数要用 float。\n"
            + "✘ 行末漏分号 → 报 CS1002 应输入 ;。\n"
            + "✔ 类型转换：int a = 3; float b = a; 小盒子倒大盒子自动转；(int)2.8f 得 2，要强制。";
        list[1].Page2En = "THIS PAGE: TYPE FOUR VARIABLES OF DIFFERENT TYPES\n"
            + "1) In the code area start with a type: int / float / bool / string.\n"
            + "2) Space once, then the name: letters, digits, underscore, never starting with a digit.\n"
            + "3) Type = and the starting value, then an English semicolon ;.\n"
            + "4) After four lines press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- int hp = 100;  int holds whole numbers only; = copies right into left; ; ends the statement.\n"
            + "- float sp = 1.5f;  the trailing f is required, else 1.5 is a double and will not fit.\n"
            + "- bool ok = true;  only true / false, no quotes.\n"
            + "- string tag = \"tower\";  text needs double quotes; curly quotes are rejected.\n"
            + "THREE TRAPS\n"
            + "- int hp = 1.5; gives CS0266, use float for decimals.\n"
            + "- A missing semicolon gives CS1002.\n"
            + "- Conversion: int a = 3; float b = a; widens free; (int)2.8f narrows to 2 by hand.";
        list[2].Page2Zh = "【这一页：把「不该变的数」交给 const 保管】\n"
            + "① 先敲 const，紧跟类型 int / string，再空一格写名字。\n"
            + "② 写 = 和值，行末分号；命名习惯全大写，如 MAX_HP。\n"
            + "③ 再写一行 int x = MAX_HP; x = 6; 观察常量本身不许改。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· const int MAX_HP = 100; const 表示这个盒子封死后不许再改。\n"
            + "· 必须在声明同一行就给值，不能先 const int a; 之后再赋值。\n"
            + "· 想改常量会报 CS0131，赋值号左边必须是可赋值变量。\n"
            + "· 常量天生适合放「魔法数字」：冷却、血量上限、计分倍数。\n"
            + "【三个必踩的坑】\n"
            + "✘ 用 const 装会变的数（比如当前血量）→ 一改就编译不过。\n"
            + "✘ const 后面忘了写类型 → const 不是类型，语法错误。\n"
            + "✔ 同一个数字在多处用到，就定义成常量，改一处全局生效。";
        list[2].Page2En = "THIS PAGE: PUT NUMBERS THAT MUST NOT CHANGE INTO const\n"
            + "1) Type const, then the type int / string, then a space and the name.\n"
            + "2) Type = and the value, then a semicolon; name it in caps like MAX_HP.\n"
            + "3) Add int x = MAX_HP; x = 6; and see that the constant itself cannot change.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- const int MAX_HP = 100;  const seals the box after the first value.\n"
            + "- The value must be given on the same line; const int a; then a = 5; is illegal.\n"
            + "- Assigning to a constant raises CS0131.\n"
            + "- Constants are for magic numbers: cooldowns, HP caps, score multipliers.\n"
            + "THREE TRAPS\n"
            + "- A const holding a value that must change will fail to compile later.\n"
            + "- Forgetting the type after const is a syntax error; const is not a type.\n"
            + "- One number used in many places should be a constant so one edit fixes all.";
        list[3].Page2Zh = "【这一页：用 C# 当计算器，算四道题】\n"
            + "① 敲 int total = 2 + 3 * 4; 先猜结果再看下一段。\n"
            + "② 敲 int t2 = 7 / 2; 再敲 float half = 7 / 2f; 对比两者的差别。\n"
            + "③ 敲 int rest = 7 % 3; % 是取余数，7 除 3 余 1。\n"
            + "④ 点【编译运行 (F6)】，用 PrintLine 把四个值打到荧幕上。\n"
            + "【逐符号读懂它】\n"
            + "· + - * / ：加减乘除；乘是星号 *，写成 x 或 × 都不认。\n"
            + "· % ：取余数，判断奇偶就用 n % 2 == 0。\n"
            + "· 优先级：先 * / %，再 + -，有括号先算括号，和小学数学一样。\n"
            + "· total 是 14 不是 20，因为 3 * 4 先算。\n"
            + "· 7 / 2 是 3，整数除整数小数被直接丢掉。\n"
            + "【三个必踩的坑】\n"
            + "✘ 想要小数却写 7 / 2 → 得 3；要 3.5 请写 7 / 2f 或 7f / 2。\n"
            + "✘ 除以 0 → 运行时报 DivideByZeroException，程序直接中断。\n"
            + "✔ 拿不准就加括号：(2 + 3) * 4 = 20，读起来也清楚。";
        list[3].Page2En = "THIS PAGE: USE C# AS A CALCULATOR, FOUR EXERCISES\n"
            + "1) Type int total = 2 + 3 * 4; guess the result before reading on.\n"
            + "2) Type int t2 = 7 / 2; then float half = 7 / 2f; and compare both.\n"
            + "3) Type int rest = 7 % 3; the % sign returns the remainder, here 1.\n"
            + "4) Press [Compile & Run (F6)] and PrintLine all four values.\n"
            + "READ EVERY SYMBOL\n"
            + "- + - * / : add, subtract, multiply, divide. Multiplication is *, not x.\n"
            + "- % : remainder, handy for odd/even with n % 2 == 0.\n"
            + "- Order: * / % first, then + -, brackets win, exactly like school maths.\n"
            + "- total is 14, not 20, because 3 * 4 runs first.\n"
            + "- 7 / 2 is 3: integer division throws the fraction away.\n"
            + "THREE TRAPS\n"
            + "- 7 / 2 gives 3; write 7 / 2f or 7f / 2 for 3.5.\n"
            + "- Dividing by zero throws DivideByZeroException at runtime.\n"
            + "- When in doubt add brackets: (2 + 3) * 4 = 20.";
        list[4].Page2Zh = "【这一页：让程序自己回答「是还是不是」】\n"
            + "① 先备两个数：int hpNow = 5; int hurt = 2;\n"
            + "② 用 bool 接住结果：bool alive = hpNow > hurt;\n"
            + "③ 再把 == != >= <= 各写一行，观察 true / false。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· == 判断两边是否相等，答案只有 true / false；= 是赋值，别混。\n"
            + "· != 不等于，>= 大于或等于，<= 小于或等于。\n"
            + "· bool alive = hpNow > hurt; 结果是 true，因为 5 大于 2。\n"
            + "· 括号不是必需，写 (hpNow == 0) 只是让人一眼看清意图。\n"
            + "【三个必踩的坑】\n"
            + "✘ 判断相等写成 = → 变成赋值，还会报 CS0029 类型不匹配。\n"
            + "✘ 把 >= 写成 => → => 是 Lambda 符号，比较只认 >=。\n"
            + "✔ 比较结果本身就是 true/false，可以直接塞进 if 的括号里。";
        list[4].Page2En = "THIS PAGE: LET THE PROGRAM ANSWER YES OR NO\n"
            + "1) Prepare two numbers: int hpNow = 5; int hurt = 2;\n"
            + "2) Catch the answer in a bool: bool alive = hpNow > hurt;\n"
            + "3) Write one line each for == != >= <= and watch true / false.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- == asks whether both sides are equal; the answer is true or false. A single = assigns.\n"
            + "- != not equal, >= at least, <= at most.\n"
            + "- bool alive = hpNow > hurt; is true because 5 is greater than 2.\n"
            + "- Brackets are optional; (hpNow == 0) just makes the intent obvious.\n"
            + "THREE TRAPS\n"
            + "- Using = for equality assigns and may give CS0029.\n"
            + "- => is a lambda arrow; comparison needs >=.\n"
            + "- A comparison already yields true/false, so it can go straight into if (...).";
        list[5].Page2Zh = "【这一页：把两个「是 / 否」合成一个答案】\n"
            + "① 先备前提：bool weaponReady = true; int hpLeft = 5;\n"
            + "② 两个条件都要成立用 &&：bool canFire = weaponReady && hpLeft > 0;\n"
            + "③ 任一成立用 ||，取反用 !，各写一行试试。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· &&（并且）：两边都 true 才是 true，一边 false 就 false。\n"
            + "· ||（或者）：一边 true 就 true，两边都 false 才 false。\n"
            + "· !（反过来）：写在操作数前面，!true 就是 false。\n"
            + "· bool needHeal = hpLeft <= 0 || !weaponReady; 血空了或武器没好都得处理。\n"
            + "【三个必踩的坑】\n"
            + "✘ 只写一个 & 或 | → 那是按位运算，条件里必须写两个。\n"
            + "✘ 把 != 当成「取反」→ != 是不等于，和 ! 意思完全不同。\n"
            + "✔ 把更可能为假的条件放在 && 左边，短路能少算一半。";
        list[5].Page2En = "THIS PAGE: MERGE TWO YES/NO ANSWERS INTO ONE\n"
            + "1) Prepare the premises: bool weaponReady = true; int hpLeft = 5;\n"
            + "2) Both must hold, use &&: bool canFire = weaponReady && hpLeft > 0;\n"
            + "3) Either may hold, use ||; negate with !. Write one line of each.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- && (and): true only when both sides are true.\n"
            + "- || (or): true when at least one side is true.\n"
            + "- ! (not): placed before the operand, !true is false.\n"
            + "- bool needHeal = hpLeft <= 0 || !weaponReady;  low HP or no weapon both need care.\n"
            + "THREE TRAPS\n"
            + "- A single & or | is bitwise, not logical; conditions need the doubled form.\n"
            + "- != is not-equal, which is a different thing from !.\n"
            + "- Put the more likely false test on the left of && to short-circuit sooner.";
        list[6].Page2Zh = "【这一页：让程序「看情况」才做事】\n"
            + "① 敲 if，紧跟一对英文圆括号 ( )，括号里放 true/false 条件。\n"
            + "② 下一行敲 {，写完语句再敲 }，花括号左右配对。\n"
            + "③ 在 if 上面先把条件用到的值算出来，例如 int n = 3;\n"
            + "④ 点【编译运行 (F6)】，再把 n 改成 0，看荧幕输出变化。\n"
            + "【逐符号读懂它】\n"
            + "· if (n > 0) ：条件是 n > 0，为真才进入下面的大括号。\n"
            + "· { } ：里面可以有任意多条语句，它们同进同退。\n"
            + "· 只有一条语句时花括号可省，但零基础阶段建议一律写上。\n"
            + "· if 的圆括号后面不加分号，加的是花括号。\n"
            + "【三个必踩的坑】\n"
            + "✘ if (n = 0) → 这是赋值不是判断，要写 n == 0。\n"
            + "✘ if (n > 0); → 多写分号，花括号里的内容就永远都会执行。\n"
            + "✔ 花括号里的语句缩进写，一眼就能看出哪几行属于这个 if。";
        list[6].Page2En = "THIS PAGE: MAKE THE PROGRAM ACT ONLY WHEN IT SHOULD\n"
            + "1) Type if and a pair of round brackets holding a true/false condition.\n"
            + "2) On the next line open a brace, write the statements, then close it.\n"
            + "3) Compute the value used by the condition above the if, e.g. int n = 3;\n"
            + "4) Press [Compile & Run (F6)], then set n to 0 and watch the output change.\n"
            + "READ EVERY SYMBOL\n"
            + "- if (n > 0) : the condition is n > 0; only a true result enters the braces.\n"
            + "- { } : any number of statements inside, all or nothing.\n"
            + "- One statement may skip braces, but beginners should always write them.\n"
            + "- No semicolon after the if brackets, the brace follows directly.\n"
            + "THREE TRAPS\n"
            + "- if (n = 0) assigns instead of comparing; use n == 0.\n"
            + "- if (n > 0); with a stray semicolon makes the braces always run.\n"
            + "- Indent the body so you can see at a glance which lines belong to the if.";
        list[7].Page2Zh = "【这一页：二选一，两条路只走一条】\n"
            + "① 先写完整的 if (条件) { … }。\n"
            + "② 在右花括号下一行敲 else，else 后面不带圆括号、不带条件。\n"
            + "③ 再跟一对花括号，写条件不成立时要执行的内容。\n"
            + "④ 三分支以上接 else if (条件)，最后可兜一个 else。\n"
            + "【逐符号读懂它】\n"
            + "· if … else ：条件真走 if，假走 else，永远只走一段。\n"
            + "· else if (条件) ：前一个条件不成立时，才轮到它继续判断。\n"
            + "· else 必须紧挨着上一个 if 的右花括号，中间不能插别的语句。\n"
            + "· 花括号不配对时编辑器提示 expected }，那就是少了一个 }。\n"
            + "【三个必踩的坑】\n"
            + "✘ else (条件) → else 不接受条件，要条件请写 else if。\n"
            + "✘ if 和 else 中间插了语句 → 编译报错，两者必须连着。\n"
            + "✔ 先想清「if 不成立时还剩哪些情况」，else 才不会漏。";
        list[7].Page2En = "THIS PAGE: TWO ROADS, ONLY ONE IS WALKED\n"
            + "1) First write a complete if (condition) { ... }.\n"
            + "2) On the line after the closing brace type else, with no brackets and no condition.\n"
            + "3) Add a pair of braces with what should happen when the condition is false.\n"
            + "4) For three or more cases chain else if (condition) and optionally a final else.\n"
            + "READ EVERY SYMBOL\n"
            + "- if ... else : true walks the if, false walks the else, never both.\n"
            + "- else if (condition) : tested only when the previous condition failed.\n"
            + "- else must touch the previous if closing brace; nothing in between.\n"
            + "- expected } in the console means one closing brace is missing.\n"
            + "THREE TRAPS\n"
            + "- else (condition) is illegal; that job belongs to else if.\n"
            + "- A statement between if and else breaks the pair.\n"
            + "- List every remaining case before writing else, or one case is lost.";
        list[8].Page2Zh = "【这一页：一个值，对上多个固定选项】\n"
            + "① 敲 switch (要判断的变量) {，先写左花括号。\n"
            + "② 每个选项写 case 值: 然后跟语句，最后 break;。\n"
            + "③ 全部不匹配时交给 default: 收尾。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· switch 只判断「等于哪个固定值」，范围判断请改用 if。\n"
            + "· case 1: 用冒号结尾不是分号，每个 case 结束要写 break;。\n"
            + "· 漏写 break 会穿透到下一个 case 继续执行，编译器报 CS0163。\n"
            + "· default: 是兜底项，可以不写，但写了更安全。\n"
            + "【三个必踩的坑】\n"
            + "✘ case 后面写变量（case hp:）→ 必须是常量，报 CS0150。\n"
            + "✘ case 1; 冒号写成逗号或分号 → 语法错误。\n"
            + "✔ 同样的代码要服务多个值，就并列写 case 1: case 2: 共用一个 break。";
        list[8].Page2En = "THIS PAGE: ONE VALUE, SEVERAL FIXED CHOICES\n"
            + "1) Type switch (value) { and open the brace.\n"
            + "2) For each option write case value: then the statements, then break;\n"
            + "3) Let default: catch everything that matched nothing.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- switch only compares against fixed values; use if for ranges.\n"
            + "- case 1: ends with a colon, not a semicolon; each case ends with break;\n"
            + "- A missing break falls through and the compiler reports CS0163.\n"
            + "- default: is optional, but it keeps holes closed.\n"
            + "THREE TRAPS\n"
            + "- case hp: with a variable gives CS0150; cases must be constants.\n"
            + "- case 1; with a semicolon is a syntax error.\n"
            + "- To run one block for several values, stack case 1: case 2: before one break;";
        list[9].Page2Zh = "【这一页：让程序替你连点 5 次】\n"
            + "① 敲 for，圆括号里三段用两个分号隔开：for (int i = 0; i < 5; i++)。\n"
            + "② 第一段 int i = 0 只执行一次；第二段 i < 5 每圈都检查。\n"
            + "③ 第三段 i++ 每圈结束执行，等于 i = i + 1。\n"
            + "④ 花括号里写要重复的语句，点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· i 是计数变量，习惯叫 i，也可以取别的名字。\n"
            + "· i < 5 变成假就立刻退出，所以从 0 数到 4 共跑 5 次，不含 5。\n"
            + "· i++ 相当于 i = i + 1，也等于 i += 1，三种写法完全等价。\n"
            + "· 想从 1 数到 5：for (int i = 1; i <= 5; i++)。\n"
            + "【三个必踩的坑】\n"
            + "✘ 三段之间写成逗号 → 必须用分号隔开，否则语法错误。\n"
            + "✘ 漏了 i++ → 条件永远成立，死循环，按标题栏【停止 (F7)】。\n"
            + "✔ 循环体里别去改 i 的值，容易跳号或死循环。";
        list[9].Page2En = "THIS PAGE: LET THE LOOP CLICK FIVE TIMES FOR YOU\n"
            + "1) Type for with three sections split by two semicolons: for (int i = 0; i < 5; i++)\n"
            + "2) Section one, int i = 0, runs once; section two, i < 5, is checked each round.\n"
            + "3) Section three, i++, runs after each round and equals i = i + 1.\n"
            + "4) Put the repeated statements in the braces and press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- i is the counter; the name is a habit, not a rule.\n"
            + "- Once i < 5 turns false the loop exits, so 0 to 4 runs five times, 5 excluded.\n"
            + "- i++ means i = i + 1, which also equals i += 1.\n"
            + "- To count 1 to 5: for (int i = 1; i <= 5; i++)\n"
            + "THREE TRAPS\n"
            + "- Commas between the sections are a syntax error, they must be semicolons.\n"
            + "- Forgetting i++ loops forever; press [Stop (F7)] in the title bar.\n"
            + "- Never modify i inside the body or you will skip numbers or hang.";
        list[10].Page2Zh = "【这一页：条件成立就一直转下去】\n"
            + "① 先在循环外面备好变量：int n = 3;\n"
            + "② 敲 while (n > 0) {，花括号里写要重复的事。\n"
            + "③ 关键：花括号里必须让 n 变小（n--;），否则永不退出。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· while (条件)：每圈开始前检查一次，真就再转，假就退出。\n"
            + "· 条件一开始就是假 → 花括号一次都不进，这是它和 do-while 的区别。\n"
            + "· n-- 等于 n = n - 1，也可以写 n -= 1。\n"
            + "· 不知道自己会转多少圈时，用 while 比 for 更自然。\n"
            + "【三个必踩的坑】\n"
            + "✘ 花括号里忘了改条件变量 → 死循环，按【停止 (F7)】退出。\n"
            + "✘ while (n > 0); 多写一个分号 → 变成空循环，永远卡在这行。\n"
            + "✔ 只有循环体里一定会变的东西，才适合当 while 的条件。";
        list[10].Page2En = "THIS PAGE: KEEP SPINNING WHILE THE CONDITION HOLDS\n"
            + "1) Prepare the variable outside the loop: int n = 3;\n"
            + "2) Type while (n > 0) { and put the repeated work inside the braces.\n"
            + "3) Critical: something inside must shrink n (n--;), or the loop never ends.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- while (condition) checks before every round: true spins again, false exits.\n"
            + "- A condition that starts false means the body never runs, unlike do-while.\n"
            + "- n-- equals n = n - 1, also written n -= 1.\n"
            + "- When the number of rounds is unknown, while reads better than for.\n"
            + "THREE TRAPS\n"
            + "- Forgetting to change the condition variable loops forever; press [Stop (F7)].\n"
            + "- while (n > 0); with a stray semicolon becomes an empty endless loop.\n"
            + "- Only something that really changes inside the body belongs in the condition.";
        list[11].Page2Zh = "【这一页：先干一次，再问要不要继续】\n"
            + "① 先敲 do {，里面写要做的事，别忘了改条件变量。\n"
            + "② 写完 } 紧跟 while (条件); 这里的分号必须有。\n"
            + "③ 条件为真就回去再执行一轮 do 里的内容。\n"
            + "④ 点【编译运行 (F6)】，把初值改成不满足条件，看它是否仍跑一次。\n"
            + "【逐符号读懂它】\n"
            + "· do { } while (条件); 先执行一次再判断，所以至少执行一次。\n"
            + "· while 后面那个分号不能漏，漏了报 CS1002 / CS1525。\n"
            + "· 初值 n = 0 且条件 n > 0：while 一次不跑，do-while 会跑一次。\n"
            + "· 适合「菜单至少显示一次」「先出手再判断」这类场景。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写成 do (条件) { } → do 后面直接跟花括号，不带条件。\n"
            + "✘ 漏掉 while 后面的分号 → 编译直接报错。\n"
            + "✔ 只要逻辑是「无论如何先做一次」，就用 do-while。";
        list[11].Page2En = "THIS PAGE: ACT FIRST, THEN ASK WHETHER TO REPEAT\n"
            + "1) Type do { and write the work inside, remembering to change the condition variable.\n"
            + "2) After the closing brace follow with while (condition); the semicolon is required.\n"
            + "3) A true condition sends you back for another round.\n"
            + "4) Press [Compile & Run (F6)], then start with a false value and see it still run once.\n"
            + "READ EVERY SYMBOL\n"
            + "- do { } while (condition);  runs once, then decides, so the body runs at least once.\n"
            + "- The semicolon after while may not be omitted; missing it gives CS1002 / CS1525.\n"
            + "- Starting at n = 0 with n > 0: a while loop skips, a do-while still runs once.\n"
            + "- Perfect for show the menu at least once or fire first, judge later.\n"
            + "THREE TRAPS\n"
            + "- do (condition) { } is wrong; do goes straight to the brace.\n"
            + "- Dropping the semicolon after while fails to compile.\n"
            + "- If the logic is always do it once, choose do-while.";
        list[12].Page2Zh = "【这一页：一个名字，装一排数】\n"
            + "① 敲 int[] hp = new int[3]; 声明能装 3 个整数的数组。\n"
            + "② 下标从 0 开始：hp[0] = 10; hp[1] = 20; hp[2] = 30;\n"
            + "③ 也可以一次写全：int[] a = new int[] { 1, 2, 3 };\n"
            + "④ 用 a.Length 取长度，点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· int[] ：方括号表示这是数组，不是单个 int。\n"
            + "· new int[3] 分配 3 个坑位，每个默认值都是 0。\n"
            + "· hp[0] 读第一个，最大下标是 Length - 1，写 hp[3] 就越界。\n"
            + "· foreach (int v in a) 能逐个取出值，不用自己管下标。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写 hp[3] → 报 IndexOutOfRangeException 越界，运行中断。\n"
            + "✘ 声明写 int hp[] → C# 要求方括号跟类型：int[] hp。\n"
            + "✔ 数组长度定死不能改，数量会变就改用 List。";
        list[12].Page2En = "THIS PAGE: ONE NAME HOLDING A WHOLE ROW OF VALUES\n"
            + "1) Type int[] hp = new int[3]; to declare an array of three integers.\n"
            + "2) Indexing starts at 0: hp[0] = 10; hp[1] = 20; hp[2] = 30;\n"
            + "3) Or fill it at once: int[] a = new int[] { 1, 2, 3 };\n"
            + "4) Read the size with a.Length and press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- int[] : the brackets make it an array instead of a single int.\n"
            + "- new int[3] reserves three slots, each defaulting to 0.\n"
            + "- hp[0] is the first slot; the highest index is Length - 1, hp[3] overflows.\n"
            + "- foreach (int v in a) walks the values without touching indexes.\n"
            + "THREE TRAPS\n"
            + "- hp[3] throws IndexOutOfRangeException and kills the run.\n"
            + "- int hp[] is not C#; brackets follow the type: int[] hp.\n"
            + "- An array size is fixed. When the count varies, use a List.";
        list[13].Page2Zh = "【这一页：会自己长个儿的「数组」】\n"
            + "① 文件第一行必须有 using System.Collections.Generic; 否则 List 不认。\n"
            + "② 敲 List<int> hp = new List<int>(); 尖括号里是元素类型。\n"
            + "③ 加元素 hp.Add(10); 取元素 hp[0]，数量看 hp.Count。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· List<int> ：尖括号里的 int 表示这个列表只装整数。\n"
            + "· new List<int>() 后面那对圆括号不能少，那是构造调用。\n"
            + "· Count 是属性不是方法：写 hp.Count，不要写 hp.Count()。\n"
            + "· 删除用 hp.Remove(10) 或 hp.RemoveAt(0)，数组做不到这些。\n"
            + "【三个必踩的坑】\n"
            + "✘ 只写 using System.Collections; → 缺 Generic，报 CS0246 找不到 List。\n"
            + "✘ 用 hp.Length → List 没有 Length，只有 Count。\n"
            + "✔ 数量事先不知道用 List；长度固定又要快就用数组。";
        list[13].Page2En = "THIS PAGE: AN ARRAY THAT GROWS BY ITSELF\n"
            + "1) The first line needs using System.Collections.Generic; or List is unknown.\n"
            + "2) Type List<int> hp = new List<int>(); the type goes inside the angle brackets.\n"
            + "3) Add with hp.Add(10); read with hp[0]; count with hp.Count.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- List<int> : the angle brackets say this list holds integers only.\n"
            + "- The empty () after new List<int> is required, it is the constructor call.\n"
            + "- Count is a property, so write hp.Count, never hp.Count().\n"
            + "- Remove with hp.Remove(10) or hp.RemoveAt(0); arrays cannot do this.\n"
            + "THREE TRAPS\n"
            + "- using System.Collections; alone gives CS0246; Generic is required.\n"
            + "- hp.Length does not exist on a List, only Count.\n"
            + "- Unknown count: use List. Fixed length and speed: use an array.";
        list[14].Page2Zh = "【这一页：把一段代码打包成一个按钮】\n"
            + "① 先敲返回类型 void，空格，方法名（首字母大写，如 FireOnce）。\n"
            + "② 紧跟一对圆括号，括号里写参数：类型 名字，多个用逗号隔开。\n"
            + "③ 下一行花括号里放这段代码要做的事。\n"
            + "④ 写完必须调用一次：FireOnce(); 定义不等于执行。\n"
            + "【逐符号读懂它】\n"
            + "· void 表示不交回答案，只做事。\n"
            + "· (int x, float y) 是参数表，调用时按顺序把值交进去。\n"
            + "· 本课程的方法写在当前代码里，调用它的语句要放在它下面或同一段内。\n"
            + "· 名字后面那对圆括号才是调用，只写名字不会执行。\n"
            + "【三个必踩的坑】\n"
            + "✘ 方法名中间带空格（fire once）→ 语法错误，名字是一个整体。\n"
            + "✘ 定义了却从不调用 → 编译能过，但荧幕毫无反应，判定也不认。\n"
            + "✔ 参数个数与类型必须和定义完全一致，多一个少一个报 CS1501。";
        list[14].Page2En = "THIS PAGE: PACK A BLOCK OF CODE INTO ONE BUTTON\n"
            + "1) Type the return type void, a space, then the name in PascalCase such as FireOnce.\n"
            + "2) Add round brackets holding the parameters: type then name, comma separated.\n"
            + "3) On the next line put the work inside braces.\n"
            + "4) Then call it once: FireOnce(); a definition alone does nothing.\n"
            + "READ EVERY SYMBOL\n"
            + "- void means nothing is returned, it only performs an action.\n"
            + "- (int x, float y) is the parameter list; arguments arrive in order.\n"
            + "- In this course the method lives in your own code; the call goes below it.\n"
            + "- The brackets after the name are the call. The bare name never executes.\n"
            + "THREE TRAPS\n"
            + "- A space inside the name (fire once) is a syntax error.\n"
            + "- Defining without calling compiles but prints nothing, so the checker refuses it.\n"
            + "- Argument count and types must match exactly, otherwise CS1501.";
        list[15].Page2Zh = "【这一页：让方法把答案交回来】\n"
            + "① 把返回类型从 void 改成 int（要交回什么类型就写什么）。\n"
            + "② 方法体最后写 return 结果; 交回答案。\n"
            + "③ 调用处接住它：int n = AddHp(1, 2);\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· int AddHp(int a, int b) ：开头的 int 是交回的类型，不再是 void。\n"
            + "· return a + b; 交回 a + b 的值，同时立刻结束这个方法。\n"
            + "· return 之后的语句永远不会执行，别在那之后写代码。\n"
            + "· 非 void 方法必须保证每条路径都有 return，否则报 CS0161。\n"
            + "【三个必踩的坑】\n"
            + "✘ 声明成 int 却只写 return; → return 后面必须跟一个值。\n"
            + "✘ 忘写 return → 报 CS0161 并非所有代码路径都返回值。\n"
            + "✔ 只要方法要「算出个数、算出伤害」，就该让它带返回值。";
        list[15].Page2En = "THIS PAGE: MAKE THE METHOD HAND AN ANSWER BACK\n"
            + "1) Change the return type from void to int, or whatever type you return.\n"
            + "2) End the body with return value; to hand the answer back.\n"
            + "3) Catch it where you call: int n = AddHp(1, 2);\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- int AddHp(int a, int b) : the leading int is what comes back, no longer void.\n"
            + "- return a + b; hands back the value and ends the method immediately.\n"
            + "- Anything after return never runs, so do not write code there.\n"
            + "- A non-void method must return on every path, or you get CS0161.\n"
            + "THREE TRAPS\n"
            + "- Declaring int but writing return; fails, a value is required.\n"
            + "- Forgetting return gives CS0161, not all code paths return a value.\n"
            + "- Any method that computes a count or damage should return a value.";
        list[16].Page2Zh = "【这一页：把文字和数字接成一句话】\n"
            + "① 先备变量：int n = 3; string tag = \"塔\";\n"
            + "② 用 + 接起来：string s = tag + \" 有 \" + n + \" 个\";\n"
            + "③ 想换行就在字符串里写 \\n，要输出就 tower.PrintLine(s);\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· + 两边只要有一边是 string，另一边就会自动变成文字。\n"
            + "· 引号里的空格也算字符，\" 有 \" 的前后空格是故意留的。\n"
            + "· 3 + \" 个\" 得 \"3 个\"，但 3 + 4 得 7，看的是运算顺序与类型。\n"
            + "· tag + n 与 n + tag 结果一样，但 n + 3 会先做加法再拼字符串。\n"
            + "【三个必踩的坑】\n"
            + "✘ 用中文引号「塔」或“塔” → 报 CS1010 / CS1012，必须英文双引号。\n"
            + "✘ 想装文字却用单引号 '塔' → 单引号只装一个字符，多字报错。\n"
            + "✔ 想先算数再拼：写成 \"共 \" + (n + 1) + \" 个\"，把算式用括号包起来。";
        list[16].Page2En = "THIS PAGE: GLUE TEXT AND NUMBERS INTO ONE SENTENCE\n"
            + "1) Prepare values: int n = 3; string tag = \"tower\";\n"
            + "2) Glue with +: string s = tag + \" has \" + n + \" blocks\";\n"
            + "3) Put \\n inside the string to break a line; print with tower.PrintLine(s);\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- When either side of + is a string, the other side turns into text.\n"
            + "- Spaces inside the quotes count, so \" has \" really has them.\n"
            + "- 3 + \" blocks\" gives \"3 blocks\" while 3 + 4 gives 7: order and types matter.\n"
            + "- n + 3 adds numbers first, then the sum is glued to a string.\n"
            + "THREE TRAPS\n"
            + "- Curly quotes like \"tower\" give CS1010 / CS1012; use straight double quotes.\n"
            + "- Single quotes are for one char only; 'tower' is invalid.\n"
            + "- To add before gluing write \"total \" + (n + 1) + \" blocks\".";
        list[17].Page2Zh = "【这一页：把变量塞进一句话的占位符】\n"
            + "① 一句话里先写占位符 {0} {1}，编号从 0 开始。\n"
            + "② 逗号后面按顺序把值填进去：string.Format(\"塔 {0} 有 {1} 个\", 1, 3);\n"
            + "③ 也可以直接 tower.PrintLine(\"块数={0}\", n); 省掉拼接。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· {0} 是第 0 个值的坑位，{1} 是第 1 个，可以重复用也可以乱序用。\n"
            + "· 占位符数量比给的参数多 → 运行时抛 FormatException。\n"
            + "· {0:F2} 保留两位小数，{0,5} 占 5 个字符宽度并靠右对齐。\n"
            + "· string.Format 返回一个新字符串，不会改动原来的变量。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写了 {1} 却只给一个参数 → 运行时报 FormatException 中断。\n"
            + "✘ 占位符用中文花括号｛0｝→ 编辑器不认，必须英文 { }。\n"
            + "✔ 句子长、变量多时用 {0} 代替拼接，少写很多加号和引号。";
        list[17].Page2En = "THIS PAGE: DROP VARIABLES INTO PLACEHOLDERS\n"
            + "1) Put placeholders {0} {1} inside the sentence, numbered from 0.\n"
            + "2) After the comma pass the values in order: string.Format(\"tower {0} has {1}\", 1, 3);\n"
            + "3) Or print directly: tower.PrintLine(\"blocks={0}\", n);\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- {0} is the slot for the first value, {1} the second; they may repeat or reorder.\n"
            + "- More placeholders than arguments throws FormatException at runtime.\n"
            + "- {0:F2} keeps two decimals; {0,5} pads to five characters, right aligned.\n"
            + "- string.Format returns a new string and never changes the original variable.\n"
            + "THREE TRAPS\n"
            + "- Writing {1} with only one argument throws FormatException.\n"
            + "- Full-width braces from a Chinese IME are rejected; use { }.\n"
            + "- Long sentences with many values are cleaner with {0} than with +.";
    }

    // ================== 第 3 页：语法课代码动画数据 ==================

    /// <summary>建一个动画步骤（Key = 该行特征子串，用于在示例代码里定位要亮起来的行）。</summary>
    private static AnimStep AS(string key, string zh, string en)
    {
        AnimStep s = new AnimStep();
        s.Key = key;
        s.Zh = zh;
        s.En = en;
        return s;
    }

    // ================== 第 3 页（语法课）：内存可视化场景数据 ==================

    /// <summary>
    /// 造一段内存：type = 类型名（画成名牌）、name = 变量名、bytes = 每个字节的十六进制（空格分隔，"??" = 还没写）。
    /// note = 这一行下面的小字（讲清这段内存为什么长这样）；badge = 行首徽章（锁 / 引用 / 栈帧 / 丢弃）；
    /// ghost = true 表示「代码里有、内存里没有」（注释、方法定义），画成灰色虚线块，也不占地址。
    /// </summary>
    private static MemVar MV(string type, string name, string bytes, string note = null,
        string badge = null, bool ghost = false)
    {
        MemVar v = new MemVar();
        v.Type = type;
        v.Name = name;
        v.Bytes = bytes;
        v.Note = note;
        v.Badge = badge;
        v.Ghost = ghost;
        return v;
    }

    /// <summary>造一步：这一拍在跑哪行代码、内存里已有几个变量、正在写第几个（-1 = 只读）、配什么旁白。</summary>
    private static MemStep MS(string expr, int reveal, int write, string zh, string en)
    {
        MemStep s = new MemStep();
        s.Expr = expr;
        s.Reveal = reveal;
        s.Write = write;
        s.Zh = zh;
        s.En = en;
        return s;
    }

    /// <summary>造一步，并顺带改写某几段内存的字节 —— 用来演示「同一格的值被改了」（for 里的 i、Add 之后的 Count）。</summary>
    private static MemStep MS(string expr, int reveal, int write, string zh, string en,
        int[] patchVar, string[] patchBytes)
    {
        MemStep s = MS(expr, reveal, write, zh, en);
        s.PatchVar = patchVar;
        s.PatchBytes = patchBytes;
        return s;
    }

    private static MemScene MSC(MemVar[] vars, MemStep[] steps)
    {
        MemScene s = new MemScene();
        s.Vars = vars;
        s.Steps = steps;
        return s;
    }

    /// <summary>
    /// 给语法课 18 项任务配「内存可视化」场景。每一章按它自己讲的东西来设计，不是套模板：
    /// 注释 → 演示「代码里有、内存里没有」的虚行；变量与类型 → 同一台内存上 int/char/bool/float 的格数差；
    /// const → 只读的那块；四则 / 关系 / 逻辑 → 结果落在哪几格、占几个字节；if / switch → 判断读哪里、哪个分支真的改了内存；
    /// for / while / do-while → 同一块内存被反复改写（Patch）；数组 / List → 引用与数据分开、下标就是偏移；
    /// 方法 / 返回值 → 栈帧的生死；字符串 → 引用 + 堆里的 UTF-16 字符格。
    /// 字节值都是真值：int 3 = 03 00 00 00（小端），float 1.5f = 00 00 C0 3F，bool true = 01，char 'A' = 41。
    /// </summary>
    private static void AppendGrammarMemPages(TaskDef[] list)
    {
        // ---- 1 注释：写给人看的东西，一个字节都不占 ----
        // 这一课只讲注释本身的功效：代码里有它、内存里没有它。所以故意不放任何变量行，
        // 整页就一排虚线格 —— 玩家一眼就能看出「注释不占内存」这件事。
        list[0].Mem = MSC(
            new[]
            {
                MV("// 注释", "（整行）", "-- -- --",
                    "注释只写给人看：编译时整行被扔掉，内存里连格子都不会划出来。", "丢弃", true).En("Comments are for humans only: the compiler throws the whole line away, so no box is ever allocated for it.", name: "(whole line)", badge: "DROP")
            },
            new[]
            {
                MS("// 计算剩余血量", 1, -1,
                    "单行注释：// 后面的字编译器一律不看。上面那排虚线格就是它的下场 —— 代码里有，内存里没有。",
                    "A line comment: everything after // is ignored. The dashed row above is where it lives — in the code, never in memory.").Code("// remaining hp"),
                MS("/* 多行注释：说明一整段逻辑 */", 1, -1,
                    "块注释 /* … */ 一样占 0 字节：写一行是 0，写一百行还是 0，内存里始终只有那排虚线。",
                    "A block comment /* … */ is 0 bytes too: one line or a hundred, memory never grows a box.").Code("/* block comment: explain a whole chunk of logic */"),
                MS("/// <summary>给方法写说明</summary>", 1, -1,
                    "文档注释 /// 同样 0 字节，还能被编辑器提示读出来。注释越多，代码越好懂，内存一点没多。",
                    "Doc comments /// cost 0 bytes and still show up in editor tooltips. More comments, no extra memory.").Code("/// <summary>describe the method</summary>")
            });

        // ---- 2 变量与类型：同一台内存上，不同宽度的四种类型 ----
        list[1].Mem = MSC(
            new[]
            {
                MV("int", "remainHp", "03 00 00 00", "int = 4 字节 = 32 位，装得下约 ±21 亿。").En("int = 4 bytes = 32 bits, holds about ±2.1 billion."),
                MV("char", "grade", "41", "char = 1 字节，存的是编号不存字形：'A' 的编号 65 = 0x41。").En("char = 1 byte: it stores a code point, not a glyph - 'A' is 65 = 0x41."),
                MV("bool", "alive", "01", "bool = 1 字节：true 就是 01，false 是 00。").En("bool = 1 byte: true is 01, false is 00."),
                MV("float", "ratio", "00 00 C0 3F", "float = 4 字节，按 IEEE-754 存：1.5f 的位模式就是 00 00 C0 3F。").En("float = 4 bytes, IEEE-754: 1.5f is exactly 00 00 C0 3F.")
            },
            new[]
            {
                MS("int remainHp = 3;", 1, 0,
                    "int 一律 4 格。数值 3 写成 0x00000003，落进内存是小端：从低地址 0x100 往高读就是 03 00 00 00 —— 低字节最先，不是按人写字的 00 00 00 03 排。",
                    "int is always 4 boxes. The value 3 is 0x00000003 and lands little-endian: reading upward from 0x100 gives 03 00 00 00 — low byte first, not the 00 00 00 03 you would write on paper."),
                MS("char grade = 'A';", 2, 1,
                    "char 只占 1 格，里面不是字母 'A' 这个形状，而是它的编号 65（0x41）—— 字母靠一张表才变回字形。",
                    "char takes 1 box holding the code 65 (0x41), not the letter's shape — a table turns it back into a glyph."),
                MS("bool alive = true;", 3, 2,
                    "bool 也只占 1 格：01 就是 true。C# 里它不能当数字用，省得你把 01 当 1 去算。",
                    "bool also takes 1 box: 01 means true. C# refuses to treat it as a number, so you cannot do maths on it."),
                MS("float ratio = 1.5f;", 4, 3,
                    "float 又回到 4 格，但装法完全不同：符号位 + 指数 + 尾数，1.5f 恰好是 00 00 C0 3F。",
                    "float is back to 4 boxes with a totally different layout — sign, exponent, mantissa — and 1.5f happens to be 00 00 C0 3F.")
            });

        // ---- 3 const：不是不占内存，是那块内存被标成只读 ----
        list[2].Mem = MSC(
            new[]
            {
                MV("const int", "MAX_HP", "64 00 00 00", "const 照样占 4 格，只是这块被标成只读 —— 改它编译就过不去。", "锁").En("const takes the same 4 boxes; they are just marked read-only - change it and the build fails.", badge: "CONST"),
                MV("int", "hp", "64 00 00 00", "普通变量的 4 格一模一样，区别只在于随时可以改写。").En("An ordinary variable gets the same 4 boxes; the only difference is you can rewrite them any time.")
            },
            new[]
            {
                MS("const int MAX_HP = 100;", 1, 0,
                    "const 的快照和普通 int 一模一样：4 格，100 = 64 00 00 00。它并不「不占内存」。",
                    "A const looks identical to a plain int: 4 boxes, 100 = 64 00 00 00. It is not \"free\"."),
                MS("int hp = 100;", 2, 1,
                    "再开一块同样的 4 格装当前血量。两块值相同，权限不同。",
                    "A second block of 4 boxes holds the current HP. Same value, different permission."),
                MS("MAX_HP = 999;   // 编译错误", 2, -1,
                    "这一行编译直接报错，内存里的 64 一个字节都没动 —— const 是编译期保护，不是运行时检查。",
                    "This line fails to compile and not a byte changes: const is enforced at compile time, not at run time.").Code("MAX_HP = 999;   // compile error")
            });

        // ---- 4 算术运算：两个数被读进 CPU，算完再写回第三块内存 ----
        list[3].Mem = MSC(
            new[]
            {
                MV("int", "a", "07 00 00 00", "第一个操作数：7。").En("First operand: 7."),
                MV("int", "b", "05 00 00 00", "第二个操作数：5。").En("Second operand: 5."),
                MV("int", "sum", "0C 00 00 00", "结果 12 = 0x0C，高位三格补 0。加出来是几位就写几位，格子数不变。").En("Result 12 = 0x0C, the upper three boxes stay 0. Only as many digits as needed; the box count never changes.")
            },
            new[]
            {
                MS("int a = 7;", 1, 0, "a 占 4 格，值是 7。", "a takes 4 boxes holding 7."),
                MS("int b = 5;", 2, 1, "b 再占 4 格，值是 5。两块内存互不相干。", "b takes another 4 boxes holding 5. The two blocks are independent."),
                MS("int sum = a + b;", 3, 2,
                    "CPU 把 a、b 各 4 格读进寄存器相加，再把 12 写进 sum 那 4 格 —— 原两块一个字节都没变。",
                    "The CPU reads both 4-box blocks into registers, adds, and writes 12 into sum's 4 boxes — the originals are untouched.")
            });

        // ---- 5 关系运算：比较不会改原值，只多出 1 个字节的答案 ----
        list[4].Mem = MSC(
            new[]
            {
                MV("int", "score", "5A 00 00 00", "被比较的数：90 = 0x5A。").En("The value being compared: 90 = 0x5A."),
                MV("bool", "pass", "01", "比较的答案是个 bool：只占 1 格。不是 4 格，也不会偷偷变成 int。").En("A comparison yields a bool: 1 box, not 4 - and it never silently turns into an int.")
            },
            new[]
            {
                MS("int score = 90;", 1, 0, "score 占 4 格，90 = 5A 00 00 00。", "score takes 4 boxes; 90 is 5A 00 00 00."),
                MS("score >= 60", 1, -1,
                    "这一步只在 CPU 里问了一句「90 >= 60 吗」，score 那 4 格一动没动。",
                    "This step only asks the CPU \"is 90 >= 60?\" — score's 4 boxes stay as they are."),
                MS("bool pass = score >= 60;", 2, 1,
                    "答案 true 落进新开的 1 格里：01。关系运算的结果永远是 1 格。",
                    "The answer true lands in one fresh box: 01. A comparison always yields a single box.")
            });

        // ---- 6 逻辑运算：条件再多，答案还是 1 格 ----
        list[5].Mem = MSC(
            new[]
            {
                MV("bool", "hasKey", "01", "第一个条件：01 = true。").En("First condition: 01 = true."),
                MV("bool", "doorOpen", "00", "第二个条件：00 = false。").En("Second condition: 00 = false."),
                MV("bool", "canEnter", "00", "&& 要求两边都真：右边是 00，所以答案也是 00。").En("&& needs both sides true: the right side is 00, so the answer is 00 too.")
            },
            new[]
            {
                MS("bool hasKey = true;", 1, 0, "第一个 bool：1 格，01。", "First bool: 1 box, 01."),
                MS("bool doorOpen = false;", 2, 1, "第二个 bool：1 格，00。", "Second bool: 1 box, 00."),
                MS("bool canEnter = hasKey && doorOpen;", 3, 2,
                    "&& 只把两个 1 格合成 1 格：有一个是 00，结果就写 00。换成 || 则只要有一个 01 就是 01。",
                    "&& folds two boxes into one: with a 00 on the right the answer is 00. Swap in || and a single 01 would win.")
            });

        // ---- 7 if：判断本身不占变量内存，改内存的是分支里的语句 ----
        list[6].Mem = MSC(
            new[]
            {
                MV("int", "hp", "00 00 00 00", "hp 现在是 0 —— 四个字节全是 00，这是真值 0，不是「没写」。").En("hp is 0 now - all four bytes are 00, a real zero, not 'nothing written'."),
                MV("bool", "isDead", "01", "if 括号里的表达式最终还是算成一个 bool（这里占 1 格），真假才决定进不进分支。").En("Whatever if(...) evaluates, the result is a bool (1 box here); that decides whether the branch runs.")
            },
            new[]
            {
                MS("int hp = 0;", 1, 0, "先把 hp 设成 0：4 格全 00。", "Set hp to 0 first: all four boxes are 00."),
                MS("if (hp <= 0)", 2, 1,
                    "0 <= 0 成立，这个 true 会算进条件那 1 格里。注意：判断完 hp 的 4 格仍然全是 00。",
                    "0 <= 0 holds, and that true fills the condition's single box. Note hp's 4 boxes are still all 00."),
                MS("hp = 100;   // 分支里复活", 2, 0,
                    "进了大括号才真正改内存：hp 那 4 格被改写成 64 00 00 00。真正动内存的是语句，不是 if 本身。",
                    "Inside the braces memory actually changes: hp's 4 boxes become 64 00 00 00. Statements write memory, not if.",
                    new[] { 0 }, new[] { "64 00 00 00" }).Code("hp = 100;   // revive inside the branch")
            });

        // ---- 8 if-else：两条分支只可能有一条碰内存 ----
        list[7].Mem = MSC(
            new[]
            {
                MV("float", "cooldown", "00 00 00 00", "冷却完了：0f 在内存里就是四格 00。").En("Cooldown over: 0f in memory is just four 00 boxes."),
                MV("int", "shots", "00 00 00 00", "开火次数计数器，一开始是 0。").En("Shot counter, starting at 0.")
            },
            new[]
            {
                MS("float cooldown = tower.GetFireCooldown();", 2, 0,
                    "冷却时间用 float 装（4 格），计数器用 int 装（4 格）—— 两块内存都已经划好。",
                    "Cooldown is a float and the counter an int — both blocks are already reserved."),
                MS("if (cooldown <= 0f)", 2, -1,
                    "0 <= 0 成立，于是走 if 分支；else 那一段这次一次都不会执行，里面的语句也不会碰内存。",
                    "0 <= 0 holds, so the if branch runs; the else branch never executes and never touches memory."),
                MS("shots++;", 2, 1,
                    "开火并把计数 +1：shots 那 4 格从 00 变成 01。反过来说，若条件不成立，这 4 格就一直保持 00。",
                    "Fire and bump the counter: shots goes from 00 to 01. Had the condition failed, those 4 boxes would stay 00.",
                    new[] { 1 }, new[] { "01 00 00 00" })
            });

        // ---- 9 switch：拿同一块内存的值去逐条比对 ----
        list[8].Mem = MSC(
            new[]
            {
                MV("int", "mode", "01 00 00 00", "switch 只认这一个值：mode = 1。").En("switch only looks at this one value: mode = 1."),
                MV("int", "color", "00 00 00 00", "命中 case 1 之后才会被赋值的变量。").En("Only assigned once case 1 is hit.")
            },
            new[]
            {
                MS("int mode = 1;", 2, 0,
                    "mode 占 4 格，值是 01。它会被反复读出来跟每个 case 比，但不会被改写。",
                    "mode holds 01 in 4 boxes. It gets read against every case, but never written."),
                MS("case 0:", 2, -1,
                    "先跟 case 0 比：不相等，整段跳过 —— 这一句里没有任何语句去动内存。",
                    "Compare against case 0 first: no match, the whole block is skipped — nothing writes memory."),
                MS("case 1:  color = 3;  break;", 2, 1,
                    "比到 case 1 命中，执行 color = 3（写成 03 00 00 00），遇 break 跳出，后面的 case / default 都不碰。",
                    "case 1 matches, so color = 3 runs (03 00 00 00), break jumps out, and the later cases never touch memory.",
                    new[] { 1 }, new[] { "03 00 00 00" })
            });

        // ---- 10 for：循环变量只有一块内存，每轮被就地改写 ----
        list[9].Mem = MSC(
            new[]
            {
                MV("int", "i", "00 00 00 00", "for 的循环变量只有这一块 4 格内存，三轮循环都是它，不会每轮新开。").En("The for variable is this single 4-box slot; all three rounds reuse it - no new memory per round."),
                MV("int", "sum", "00 00 00 00", "累加结果也一直是这同一块。").En("The accumulator is that same slot too.")
            },
            new[]
            {
                MS("for (int i = 0; i < 3; i++)", 2, 0,
                    "for 开始：先划两块内存（i 和 sum），i 清成 0。循环体还没跑，格子已经在了。",
                    "The for loop starts by reserving both blocks and zeroing i. The body has not run yet, but the boxes exist."),
                MS("i < 3  →  打印 i=0", 2, -1,
                    "判断 0 < 3 成立，进循环体打印 i=0。打印只是读内存。",
                    "0 < 3 holds, the body prints i=0. Printing only reads memory.").Code("i < 3  →  print i=0"),
                MS("i++", 2, 0,
                    "同一块 4 格被就地改写成 1（01 00 00 00）—— 内存没有变大，里面的值变了。",
                    "The very same 4 boxes are overwritten with 1 — memory does not grow, the value changes.",
                    new[] { 0 }, new[] { "01 00 00 00" }),
                MS("i++", 2, 0,
                    "第二轮回见：还是那 4 格，这次写成 2。",
                    "Second round: still the same 4 boxes, this time holding 2.",
                    new[] { 0 }, new[] { "02 00 00 00" }),
                MS("i < 3  →  false", 2, 0,
                    "第三轮后 i 变成 3，条件不成立，循环结束；这 4 格也随代码块结束一起被回收。",
                    "After the third round i is 3, the condition fails, the loop ends, and those 4 boxes are released with the block.",
                    new[] { 0 }, new[] { "03 00 00 00" })
            });

        // ---- 11 while：先判断再干活，条件读的是同一块内存 ----
        list[10].Mem = MSC(
            new[]
            {
                MV("int", "n", "03 00 00 00", "while 的条件读的就是这块：只要还 > 0 就继续。").En("The while condition reads exactly this slot: keep looping while it stays > 0.")
            },
            new[]
            {
                MS("int n = 3;", 1, 0, "先给 n 一个值：4 格，03 00 00 00。", "Give n a value first: 4 boxes, 03 00 00 00."),
                MS("while (n > 0)", 1, -1,
                    "每轮开头都重新读这块内存判断：3 > 0 成立，进循环体。判断本身不改它。",
                    "Every round re-reads this block: 3 > 0 holds, so enter the body. The test itself writes nothing."),
                MS("n--;", 1, 0,
                    "循环体把它减 1，同一块 4 格变成 02 —— 这就是让循环最终能停下来的那一句。",
                    "The body decrements it: the same 4 boxes become 02 — this is what eventually stops the loop.",
                    new[] { 0 }, new[] { "02 00 00 00" }),
                MS("while (n > 0)  →  false", 1, 0,
                    "再减到 0，条件不成立，循环结束。忘记改这块内存就是死循环。",
                    "Down to 0, the condition fails and the loop ends. Forgetting to change this block is how infinite loops happen.",
                    new[] { 0 }, new[] { "00 00 00 00" })
            });

        // ---- 12 do-while：先干一次，再判断 ----
        list[11].Mem = MSC(
            new[]
            {
                MV("int", "d", "00 00 00 00", "do 先冲进循环体，所以这块一定会被改成 1，哪怕条件一开始就不成立。").En("A do loop runs its body first, so this slot always becomes 1 even when the condition was false from the start.")
            },
            new[]
            {
                MS("int d = 0;", 1, 0, "计数器先归零：4 格全 00。", "Zero the counter: 4 boxes, all 00."),
                MS("do { d++; }", 1, 0,
                    "do 的特点：不看条件先执行一次。d 那 4 格被无条件改写成 1。",
                    "That is what do does: run once without checking. d's 4 boxes are overwritten with 1 unconditionally.",
                    new[] { 0 }, new[] { "01 00 00 00" }),
                MS("while (d < 3);", 1, -1,
                    "最后才判断 1 < 3 成立，回头再干一次。注意条件后面那个分号，漏了编译器就报错。",
                    "Only then the test 1 < 3, so go again. Do not drop that trailing semicolon or the compiler complains.")
            });

        // ---- 13 数组：变量只是门牌号，数据整排躺在另一块内存里 ----
        list[12].Mem = MSC(
            new[]
            {
                MV("int[]", "hits", "E8 2F 00 00", "变量本身只有 4 格：它存的是门牌号（引用），不是那三个 int。", "引用").En("The variable itself is only 4 boxes: it holds an address (a reference), not the three ints.", badge: "REF"),
                MV("堆 · int[3]", "hits[0..2]", "02 00 00 00 00 00 00 00 00 00 00 00",
                    "12 格 = 3 个 int 连续排：hits[0] 在第 1 格，hits[1] 偏移 +4，hits[2] 偏移 +8。").En("12 boxes = 3 ints back to back: hits[0] in the first box, hits[1] at +4, hits[2] at +8.", type: "heap · int[3]")
            },
            new[]
            {
                MS("int[] hits = new int[3];", 2, 0,
                    "new 先在堆上划出 12 格（3 个 int），再把这 12 格的起始门牌号写进 hits 那 4 格里。",
                    "new reserves 12 boxes on the heap, then stores the starting address in hits' 4 boxes."),
                MS("hits[0] = 2;", 2, 1,
                    "顺着门牌号找到那一段，把第 0 个 int 改成 2（02 00 00 00）。下标就是「第几块 4 格」。",
                    "Follow the address and set element 0 to 2. An index is just \"which 4-box block\".",
                    new[] { 1 }, new[] { "02 00 00 00 00 00 00 00 00 00 00 00" }),
                MS("hits[3]   // 越界", 2, -1,
                    "只有 0/1/2 三块 4 格。写 hits[3] 就跑到这段内存外面去了 —— 运行时会抛越界异常挡住你。",
                    "Only blocks 0/1/2 exist. hits[3] steps outside this run of memory, and the runtime throws to stop you.").Code("hits[3]   // out of range")
            });

        // ---- 14 List：比数组多了 Count 这本账 ----
        list[13].Mem = MSC(
            new[]
            {
                MV("List<int>", "hpList", "E8 2F 00 00", "List 变量本身还是 4 格引用，指向它内部的数组和账本。", "引用").En("The List variable is still a 4-box reference pointing at its internal array and counters.", badge: "REF"),
                MV("内部 int", "Count", "00 00 00 00", "Count 是另一个 int：Add 一次就从 0 变 1，取数量不用数元素。").En("Count is another int: one Add moves it 0 -> 1, so size costs no scan.", type: "inner int"),
                MV("内部 int[2]", "hpList[0..1]", "00 00 00 00 00 00 00 00",
                    "内部数组一开始容量小（这里 2 个 int = 8 格），装满会自动翻倍换更大的。").En("The internal array starts small (2 ints = 8 boxes) and doubles into a bigger one when full.", type: "inner int[2]")
            },
            new[]
            {
                MS("List<int> hpList = new List<int>();", 3, 0,
                    "new 一个 List：内部数组先划一小段（8 格），Count 那 4 格写 0，hpList 只记门牌号。",
                    "A new List reserves a small internal array (8 boxes), zeroes Count, and hpList keeps only the address."),
                MS("hpList.Add(3);", 3, 2,
                    "Add 干两件事：把 3 写进内部数组第 0 块（03 00 00 00），再把 Count 改成 1。所以亮的是两段内存。",
                    "Add does two things: write 3 into block 0 and set Count to 1 — two blocks light up at once.",
                    new[] { 1, 2 }, new[] { "01 00 00 00", "03 00 00 00 00 00 00 00" }),
                MS("hpList.Count", 3, 1,
                    "取数量读的就是 Count 那块 int（所以这一步亮的是它），O(1)；换成数组得自己维护一个计数变量。",
                    "Reading Count is a single int read (that is why it lights up here), O(1); with a plain array you would maintain a counter yourself.")
            });

        // ---- 15 方法定义：不调用就不占内存，调一次开一层栈帧 ----
        list[14].Mem = MSC(
            new[]
            {
                MV("void", "ShowHp（定义）", "-- -- -- --", "方法定义不占数据内存：方法体躺在代码区，等被调用才在栈上开格子。", "代码区", true).En("A method definition takes no data memory: the body lives in the code area and only gets stack boxes when called.", name: "ShowHp (definition)", badge: "CODE"),
                MV("int", "hp（参数）", "64 00 00 00", "调用时才给参数开 4 格，把实参 100 复制进去 —— 是复制，不是共享。", "栈帧").En("A parameter's 4 boxes are opened at call time and 100 is copied in - copied, not shared.", name: "hp (param)", badge: "FRAME")
            },
            new[]
            {
                MS("void ShowHp(int hp) { … }", 1, -1,
                    "只是定义一个方法：一行数据内存都不占。上面那排虚线格提醒你 —— 它在代码区，不在数据区。",
                    "Defining a method claims no data memory. The dashed row reminds you it lives in the code region."),
                MS("ShowHp(100);", 2, 1,
                    "调用发生：栈上新开一层栈帧，给 hp 划 4 格，把 100 复制进去。此时方法外的变量一点没动。",
                    "The call opens a new stack frame, reserves 4 boxes for hp and copies 100 in. Outside variables are untouched."),
                MS("方法返回", 1, -1,
                    "方法一结束，这层栈帧整块销毁，hp 的 4 格随之消失 —— 局部变量为什么不能跨方法用，原因就在这。",
                    "When the method ends the frame is destroyed and hp's 4 boxes vanish — that is why locals cannot leak out.").Code("method returns")
            });

        // ---- 16 返回值：值复制出来，方法内的内存照样销毁 ----
        list[15].Mem = MSC(
            new[]
            {
                MV("int", "v（参数）", "02 00 00 00", "调用时新开的参数格：把 2 复制进来。", "栈帧").En("A freshly opened parameter slot: 2 is copied into it.", name: "v (param)", badge: "FRAME"),
                MV("int", "返回值", "04 00 00 00", "return 把结果放进这块，它是方法交给外面的唯一东西。", "栈帧").En("return puts the result in this box; it is the only thing handed back out.", name: "return value", badge: "FRAME"),
                MV("int", "sum", "04 00 00 00", "调用者的变量。它比方法内的格子活得久，因为它在外面那一层。").En("The caller's variable. It outlives the boxes inside the method because it lives one level out.")
            },
            new[]
            {
                MS("int Twice(int v) { … }", 0, -1,
                    "方法还没调用：内存里什么都没有（返回值也在等）。",
                    "Not called yet: nothing is allocated, not even the return slot."),
                MS("Twice(2)", 1, 0,
                    "参数 v 先拿到 4 格，值 2。", "Parameter v gets its 4 boxes holding 2."),
                MS("return v * 2;", 2, 1,
                    "算出 4，放进「返回值」那 4 格。到这里方法内的内存还是活的。",
                    "Compute 4 into the return slot. The method's own memory is still alive here."),
                MS("int sum = Twice(2);", 3, 2,
                    "外面把 4 复制进 sum，随后栈帧销毁：v 和返回值都没了，只剩 sum。",
                    "Outside, 4 is copied into sum and the frame is destroyed — v and the return slot are gone, sum remains.")
            });

        // ---- 17 字符串拼接：每次 + 都在堆上造一段新字符，旧的就成垃圾 ----
        list[16].Mem = MSC(
            new[]
            {
                MV("int", "curHp", "03 00 00 00", "先有数字，才有得拼。").En("First the digits, then there is something to concatenate."),
                MV("string", "msg", "E8 2F 00 00", "msg 只有 4 格：存门牌号。字符串本身在堆上，不在这 4 格里。", "引用").En("msg is only 4 boxes: an address. The characters live on the heap, not here.", badge: "REF"),
                MV("堆 · char[]", "\"hp=3\"", "68 00 70 00 3D 00 33 00",
                    "string 内部是 UTF-16：每个字符 2 字节。h=0068、p=0070、==003D、3=0033，正好 8 格。").En("Inside a string it is UTF-16: 2 bytes per char. h=0068, p=0070, = 003D, 3 = 0033 - 8 boxes.", type: "heap · char[]")
            },
            new[]
            {
                MS("int curHp = 3;", 1, 0, "数字先占 4 格：03 00 00 00。", "The number takes 4 boxes: 03 00 00 00."),
                MS("string msg = \"hp=\" + curHp;", 3, 1,
                    "字符串不可变：+ 会在堆上新造一段字符（h p = 3 各占 2 字节），再把门牌号写进 msg。原来那段没人动。",
                    "Strings are immutable: + builds a brand-new char array in the heap, then stores its address in msg.",
                    new[] { 2 }, new[] { "68 00 70 00 3D 00 33 00" }),
                MS("tower.PrintLine(msg + \" / done\");", 3, -1,
                    "再 + 一次又造一段更长的字符数组，刚才那 8 格立刻变成没人引用的垃圾，等 GC 回收。",
                    "Another + builds an even longer array; the previous 8 boxes become garbage for the GC.")
            });

        // ---- 18 string.Format：按模板一趟拼好，中间垃圾更少 ----
        list[17].Mem = MSC(
            new[]
            {
                MV("int", "baseHp", "64 00 00 00", "参与格式化的值：100 = 64 00 00 00。").En("The value being formatted: 100 = 64 00 00 00."),
                MV("string", "text", "F0 2F 00 00", "格式化完后 text 只拿到门牌号。", "引用").En("After formatting, text only receives an address.", badge: "REF"),
                MV("堆 · char[]", "\"hp=100\"", "68 00 70 00 3D 00 31 00 30 00 30 00",
                    "UTF-16 每字符 2 字节：h p = 1 0 0 共 6 个字符 = 12 格，正好排满这一行。").En("UTF-16, 2 bytes per char: h p = 1 0 0 is 6 chars = 12 boxes, filling this row exactly.", type: "heap · char[]")
            },
            new[]
            {
                MS("int baseHp = 100;", 1, 0, "先把要填进模板的值准备好：4 格。", "First prepare the value that fills the template: 4 boxes."),
                MS("string.Format(\"hp={0}\", baseHp)", 3, 1,
                    "Format 按模板一趟拼完：把 100 的十进制字符接在 \"hp=\" 后面，堆上新出 12 格；它不会为每个片段各造一段再相加。",
                    "Format assembles the result in one pass: append \"100\" after \"hp=\", allocating 12 fresh char boxes instead of one per fragment."),
                MS("text = 格式化结果", 3, -1,
                    "text 拿到这段新字符的门牌号。模板里的 {0} 意思就是「把第 0 个参数塞到这个位置」。",
                    "text receives the address of that new string. The {0} in the template means \"put argument 0 right here\".").Code("text = formatted result")
            });
    }

    /// <summary>
    /// 给语法课 18 个任务各配一段「代码动画」：每一步 = 高亮示例代码的某一行 + 一句人话解释它做什么。
    /// 行用特征子串定位（见 FindAnimLine），所以以后改示例代码只要还留着这段特征文字就不会错位。
    /// </summary>
    private static void AppendGrammarAnimPages(TaskDef[] list)
    {
        list[0].Anim = new[]
        {
            AS("// 计算剩余血量", "先在代码上贴一张便条：// 后面这一行是写给人看的，电脑根本不执行。", "A sticky note for humans: everything after // is ignored by the computer.").KeyIn("// remaining hp"),
            AS("int remainHp", "真正干活的是这行：造一个整数变量 remainHp，里面装 3。", "The real work: create an int variable remainHp holding 3."),
            AS("/* 下面这行", "多行注释用 /* 开头，可以一直写到 */，中间跨几行都行。", "A block comment starts with /* and runs until */, over as many lines as you like.").KeyIn("/* this prints to the screen"),
            AS("tower.PrintLine", "最后调用炮台的 PrintLine，把变量里的值打印到荧幕上。", "Finally PrintLine prints the variable's value to the screen.")
        };

        list[1].Anim = new[]
        {
            AS("int hp", "int 只能装整数：hp 里放进 100。", "int holds whole numbers only: hp gets 100."),
            AS("float speed", "float 装小数，字面量后面必须带 f，写成 1.5 会报错。", "float holds decimals and needs the f suffix: 1.5 alone is an error."),
            AS("bool ready", "bool 只有两个值 true / false，像一盏只能开或关的灯。", "bool has only true / false, like a switch that is either on or off."),
            AS("string tag", "string 装文字，文字必须用双引号包起来。", "string holds text, and text must be wrapped in double quotes.")
        };

        list[2].Anim = new[]
        {
            AS("const int MaxBlocks", "const 表示这个数定死了：MaxBlocks = 400，之后谁想改就会编译报错。", "const freezes the value: change MaxBlocks later and the compiler refuses."),
            AS("const float Pi", "同样是 const，只是类型换成 float，所以 3.14f 后面要带 f。", "Same const idea with the float type, hence the f on 3.14f.")
        };

        list[3].Anim = new[]
        {
            AS("int total", "* 优先级高于 +：先算 3 * 4 = 12，再加 2，total 得到 14。", "* binds tighter than +: 3 * 4 = 12 first, then + 2, so total is 14."),
            AS("float halfTotal", "用 2f（浮点数）去除，小数不会丢；写成 2 就是整数除法，小数被砍掉。", "Dividing by 2f keeps the fraction; dividing by 2 would drop it."),
            AS("int rest", "% 是取余数：14 % 5 余 4，rest 得到 4。", "% is the remainder: 14 % 5 leaves 4, so rest is 4.")
        };

        list[4].Anim = new[]
        {
            AS("int hpNow", "先备一个被比较的数：hpNow = 5。", "First a number to compare: hpNow = 5."),
            AS("bool alive", "5 > 0 成立 → 得到 true，装进 alive。", "5 > 0 is true, so alive becomes true."),
            AS("bool empty", "== 是「判断相等」而不是赋值：5 == 0 不成立 → false。", "== compares instead of assigning: 5 == 0 is false.")
        };

        list[5].Anim = new[]
        {
            AS("bool weaponReady", "先摆好两块原料：武器就绪 true。", "First ingredient: weaponReady is true."),
            AS("bool canFire", "&& 要求两边都真：true && (5 > 0) → true，可以开炮。", "&& needs both true: true && (5 > 0) gives true, so it can fire."),
            AS("bool needHeal", "|| 一边真就真，! 把 true 取反：(5 <= 0) || !true → false，不用治疗。", "|| needs one true, ! inverts: (5 <= 0) || !true is false, no healing needed.")
        };

        list[6].Anim = new[]
        {
            AS("int blockCount", "先问炮台：场上现在有几个方块，把数量装进 blockCount。", "Ask the tower how many blocks exist and store the count."),
            AS("if (blockCount > 0)", "if 括号里必须是一个「是 / 否」的问题：现在有方块吗？", "The if condition must be a yes/no question: are there blocks?"),
            AS("tower.PrintLine", "只有答案为「是」，才执行大括号里这句打印；为「否」就整段跳过。", "Only when the answer is yes does the print inside the braces run; otherwise the block is skipped.")
        };

        list[7].Anim = new[]
        {
            AS("float cd", "先读冷却：cd = 距离下次能开炮还剩多少秒。", "Read the cooldown: seconds left before the next shot."),
            AS("if (cd <= 0f)", "cd 小于等于 0 → 条件成立，走进 if 分支。", "cd at or below zero means the if branch runs."),
            AS("tower.FireAtPlane", "条件成立就开炮，冷却从头开始算。", "When it holds, the tower fires and the cooldown restarts."),
            AS("else", "条件不成立时走 else：只打印 cooling down，绝不开炮。", "Otherwise the else branch only prints \"cooling down\" and never fires.")
        };

        list[8].Anim = new[]
        {
            AS("int mode", "先有一个待判断的数：mode = 1。", "A value to test: mode = 1."),
            AS("switch (mode)", "switch 拿着 mode 的值，从上往下挨个和 case 比。", "switch takes mode and compares it against each case in order."),
            AS("case 1:", "值等于 1 → 命中这一支，执行它后面那句打印；末尾的 break 表示到此结束，不加会顺着往下串。", "The value is 1, so this case runs its print; the trailing break stops it from falling through."),
            AS("default:", "所有 case 都不匹配时，才轮到 default 收尾。", "If no case matches, default catches the rest.")
        };

        list[9].Anim = new[]
        {
            AS("for (int i = 0", "初始化：i 从 0 开始，然后才判断 i < 3。", "Init: i starts at 0, then i < 3 is checked."),
            AS("tower.PrintLine", "条件成立才进循环体，打印当前 i。", "Only when the condition holds does the body print i."),
            AS("i++", "一轮结束 i 加 1：0 → 1 → 2，再判断 3 < 3 不成立，循环结束，一共跑 3 次。", "After each round i increments: 0, 1, 2, then 3 < 3 fails and the loop ends after three rounds.")
        };

        list[10].Anim = new[]
        {
            AS("int wi = 0", "先造循环变量 wi = 0。", "Create the loop variable wi = 0."),
            AS("while (wi < 3)", "每轮开头先问一次：wi 还小于 3 吗？", "Each round starts by asking: is wi still below 3?"),
            AS("wi++", "循环体里把 wi 加 1 —— 这句千万别漏，否则 wi 永远是 0，死循环。", "Increment wi inside the body - skip this and wi stays 0 forever: an infinite loop.")
        };

        list[11].Anim = new[]
        {
            AS("int di = 0", "di 从 0 开始。", "di starts at 0."),
            AS("do", "do 的意思是先干一次再谈条件：循环体至少执行一遍。", "do means act first, ask later: the body always runs at least once."),
            AS("} while (di < 3);", "最后才判断 di < 3；注意结尾这个分号，漏了直接报错。", "Only at the end is di < 3 checked - and that trailing semicolon is required.")
        };

        list[12].Anim = new[]
        {
            AS("int[] hits", "造一个能装 3 个整数的格子柜，名字叫 hits。", "Build a cabinet that holds 3 ints, named hits."),
            AS("hits[0] = 2", "往第 0 号格子放 2。下标从 0 开始，长度 3 的合法下标只有 0/1/2。", "Put 2 into slot 0. Indexes start at 0, so a length of 3 allows 0/1/2 only."),
            AS("hits[0]={0}", "读第 0 号格子里的值打印出来 → 荧幕上出现 2。", "Read slot 0 and print it - 2 shows up on screen.")
        };

        list[13].Anim = new[]
        {
            AS("List<int> hpList", "List 是会自动变长的柜子，先造一个空的。", "List is a cabinet that grows by itself: start with an empty one."),
            AS("hpList.Add(3)", "往里塞一个 3，柜子自己长高，不用你操心长度。", "Add a 3 and the list grows; you never manage its length."),
            AS("hpList.Count", "Count 是现在装了几个 → 打印 1。", "Count is how many items are inside - it prints 1.")
        };

        list[14].Anim = new[]
        {
            AS("void ShowHp", "定义一台小机器：名字叫 ShowHp，进货口收一个整数 hp。定义的时候什么都不执行。", "Define a little machine named ShowHp that takes one int. Defining runs nothing."),
            AS("tower.PrintLine", "机器内部的动作：把收到的 hp 打印出来。", "Inside the machine: print the hp it received."),
            AS("ShowHp(3);", "真正调用时才跑起来：3 从进货口进去，荧幕打印 hp=3。", "Only the call runs it: 3 goes in, hp=3 is printed.")
        };

        list[15].Anim = new[]
        {
            AS("int Twice", "定义一台机器：收一个 int，吐回一个 int。", "Define a machine that takes an int and hands one back."),
            AS("return v * 2", "return 就是「交货」：把 v * 2 算好递回去。", "return is the hand-over: compute v * 2 and give it back."),
            AS("int sum = Twice(2)", "调用 Twice(2) → 拿到 4，装进变量 sum。", "Call Twice(2) - it returns 4, stored in sum."),
            AS("sum={0}", "最后把 sum 打印出来 → 荧幕上出现 sum=4。", "Print sum, and the screen shows sum=4.")
        };

        list[16].Anim = new[]
        {
            AS("int curHp", "先有一个数 curHp = 3。", "Start with a number: curHp = 3."),
            AS("string msg =", "+ 把文字和数字粘在一起，数字会自动变成文字 → \"hp=3\"。", "+ glues text and numbers; the number turns into text, giving \"hp=3\"."),
            AS("tower.PrintLine(msg", "再在后面拼一段 \" / done\" → \"hp=3 / done\"，然后打印。", "Append \" / done\", giving \"hp=3 / done\", then print it.")
        };

        list[17].Anim = new[]
        {
            AS("int baseHp", "先备好要填进句子的数：baseHp = 3。", "Prepare the value that will go into the sentence: baseHp = 3."),
            AS("string.Format(", "模板里的 {0} {1} 是空位，按顺序取后面传进来的参数填进去。", "The {0} {1} slots are filled in order by the arguments after the comma."),
            AS("{1:F1}", ":F1 会作用在这个空位上吗？会 —— {1:F1} 表示取第 2 个参数并保留 1 位小数 → 1.5。", ":F1 formats that slot: {1:F1} takes the second argument with one decimal, giving 1.5."),
            AS("tower.PrintLine(msgFmt)", "最后得到 \"hp=3, half=1.5\"，打印出来。", "The result is \"hp=3, half=1.5\", which gets printed.")
        };
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
            "任务 1：Print 打印不换行",
            "Task 1: Print",
            "签名：void Print(string format, params object[] args);\n"
            + "说明：输出到荧幕但不换行，适合在同一行拼多段内容。\n"
            + "    tower.Print(\"hp={0} \", 7);\n"
            + "    tower.Print(\"count={0}\", 42);\n\n"
            + "目标：用 Print 输出一段不换行的文字（两句会拼在同一行）。\n"
            + "提示：Print 与 PrintLine 的区别只有是否换行，下一项就学 PrintLine。\n"
            + "判定：出现「tower.Print(」（PrintLine 不算）。",
            "Signature: void Print(string format, params object[] args);\n"
            + "Note: prints without a trailing newline - handy for building one line piecewise.\n"
            + "    tower.Print(\"hp={0} \", 7);\n"
            + "    tower.Print(\"count={0}\", 42);\n\n"
            + "Goal: use Print for a non-newline output (both calls land on the same line).\n"
            + "Hint: Print vs PrintLine differs only in the trailing newline; PrintLine comes next.\n"
            + "Check: \"tower.Print(\" appears (PrintLine does not count).",
            (raw, code) => RePrintOnly.IsMatch(code));

        list[1] = Def(
            "任务 2：PrintLine 打印换行",
            "Task 2: PrintLine",
            "签名：void PrintLine(string format, params object[] args);\n"
            + "说明：输出到 +X 前方荧幕并换行，占位符同 string.Format。\n"
            + "    tower.PrintLine(\"hp={0}\", 7);\n\n"
            + "目标：用 PrintLine 输出一行文字（每次调用自动换行）。\n"
            + "提示：荧幕最多保留 24 行，超出会自动丢弃最旧的；调试循环里别每帧狂打印。\n"
            + "判定：出现「tower.PrintLine(」。",
            "Signature: void PrintLine(string format, params object[] args);\n"
            + "Note: prints one line to the +X screen; same placeholders as string.Format.\n"
            + "    tower.PrintLine(\"hp={0}\", 7);\n\n"
            + "Goal: print one line with PrintLine (each call ends the line).\n"
            + "Hint: the screen keeps 24 lines; avoid printing every frame in a loop.\n"
            + "Check: \"tower.PrintLine(\" appears.",
            (raw, code) => RePrintLine.IsMatch(code));

        list[2] = Def(
            "任务 3：ClearPrint 清空荧幕",
            "Task 3: ClearPrint",
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

        list[3] = Def(
            "任务 4：GetAllObjects 取对象列表",
            "Task 4: GetAllObjects",
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

        list[4] = Def(
            "任务 5：读取 Index 编号",
            "Task 5: Read Index",
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

        list[5] = Def(
            "任务 6：读取 Type 类型",
            "Task 6: Read Type",
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

        list[6] = Def(
            "任务 7：读取 HP 剩余血量",
            "Task 7: Read HP",
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

        list[7] = Def(
            "任务 8：读取 Position 位置",
            "Task 8: Read Position",
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

        list[8] = Def(
            "任务 9：读取 Velocity 速度",
            "Task 9: Read Velocity",
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

        list[9] = Def(
            "任务 10：读取 Volume 体积",
            "Task 10: Read Volume",
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

        list[10] = Def(
            "任务 11：GetFireCooldown 冷却查询",
            "Task 11: GetFireCooldown",
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

        list[11] = Def(
            "任务 12：FireAtPlane 发射",
            "Task 12: FireAtPlane",
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

        list[12] = Def(
            "任务 13：实战组合（遍历 + 判断 + 发射）",
            "Task 13: Combine (loop + if + fire)",
            "把三个 API 串起来：遍历对象 → 判断类型 → 打最靠前的一个\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\" && o.Position.x > 0f)\n"
            + "        {\n"
            + "            // 目标坐标即法向量\n"
            + "            tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z);\n"
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
            + "            // target position is the normal\n"
            + "            tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z);\n"
            + "            break;\n"
            + "        }\n"
            + "    }\n\n"
            + "Goal: have foreach, an if test and FireAtPlane in the same code.\n"
            + "Hint: break after the first hit to avoid wasting cooldown; pass the target position as the normal to aim at it.\n"
            + "Check: foreach, if and tower.FireAtPlane( all appear.",
            (raw, code) => ReForeach.IsMatch(code) && ReIf.IsMatch(code) && ReFireAtPlane.IsMatch(code));

        AppendApiDetailPages(list);

        return list;
    }

    /// <summary>
    /// 炮台 API 课（Level 2 - Part 2，13 项）的第 2 页：不写泛泛概念，只写「这一页亲手敲什么 / 每个符号什么意思 /
    /// 新手一定踩的坑（含真实报错编号）」，与语法课 AppendGrammarDetailPages 的结构保持一致。
    /// 第 1 页仍是签名 + 示例 + 判定条件，第 2 页是照着做就能跑通的操作指引。
    /// </summary>
    private static void AppendApiDetailPages(TaskDef[] list)
    {
        // ---------- 任务 1：Print 不换行 ----------
        list[0].Page2Zh = "【这一页：亲手敲出两段拼在同一行的话】\n"
            + "① 光标点进代码区，敲 tower.Print(\"hp={0} \", 7); 回车。\n"
            + "② 再敲 tower.Print(\"count={0}\", 42); 回车（第一句末尾故意留了个空格，别删）。\n"
            + "③ 点【编译运行 (F6)】：编辑器自动关闭，抬头看 +X 正前方那块荧幕。\n"
            + "④ 荧幕上只有一行「hp=7 count=42」，两句话拼成一行——这就是 Print 的全部意义。\n"
            + "【逐符号读懂它】\n"
            + "· tower：炮台对象，引擎已经作为参数 Run(CodeTower tower) 传进来，直接用，不要自己 new。\n"
            + "· .Print( )：方法调用，括号里叫实参。\n"
            + "· \"hp={0} \"：双引号里是格式串，{0} 是第 0 号占位符，会被第一个实参 7 填进去。\n"
            + "· 7 与 42：实参，按顺序填 {0}、{1}。\n"
            + "· 行尾的空格：Print 不换行，空格是唯一的分隔手段，删了就变成 hp=7count=42。\n"
            + "· ; 分号：一句话结束，漏了报 CS1002。\n"
            + "【三个必踩的坑】\n"
            + "✘ 中文输入法敲出「“hp={0}”」→ 报 CS1010/CS1056，引号必须英文。\n"
            + "✘ 格式串里 {0} {1} 写了但实参给少了 → 运行期抛 FormatException，编辑器当场报错停下。\n"
            + "✔ Run 每帧都执行：无条件 Print 会在同一行越拼越长，实测几秒后整行被挤出屏幕。";
        list[0].Page2En = "THIS PAGE: TYPE TWO PRINTS THAT LAND ON ONE LINE\n"
            + "1) Click into the code area and type tower.Print(\"hp={0} \", 7); then Enter.\n"
            + "2) Type tower.Print(\"count={0}\", 42); then Enter (keep the trailing space in line 1).\n"
            + "3) Press [Compile & Run (F6)]; the editor closes and you look at the +X screen.\n"
            + "4) The screen shows one single line: hp=7 count=42 - that is what Print is for.\n"
            + "READ EVERY SYMBOL\n"
            + "- tower: the tower object, already passed in as Run(CodeTower tower); never new it yourself.\n"
            + "- .Print( ): a method call; whatever is inside the parentheses are arguments.\n"
            + "- \"hp={0} \": a format string; {0} is placeholder zero, filled by the first argument 7.\n"
            + "- 7 and 42: arguments, filled into {0} and {1} in order.\n"
            + "- the trailing space: Print never breaks the line, the space is your only separator.\n"
            + "- ; semicolon: ends the statement; missing it gives CS1002.\n"
            + "THREE TRAPS\n"
            + "- Curly quotes from a Chinese IME give CS1010/CS1056; use straight quotes.\n"
            + "- More placeholders than arguments throws FormatException at run time and stops the editor.\n"
            + "- Run executes every frame, so an unconditional Print keeps growing that one line until it scrolls off.";
        // ---------- 任务 2：PrintLine 换行 ----------
        list[1].Page2Zh = "【这一页：让荧幕出现两行独立的话】\n"
            + "① 敲 tower.PrintLine(\"hp={0}\", 7); 回车。\n"
            + "② 再敲 tower.PrintLine(\"tower ready\"); 回车（这句没有占位符，不用给实参）。\n"
            + "③ F6 运行，荧幕从上到下是两行：hp=7 / tower ready。\n"
            + "④ 对比上一项：同样两句，用 PrintLine 就分了两行。\n"
            + "【逐符号读懂它】\n"
            + "· PrintLine 末尾自带换行，等价于 Print(text) 再补一个换行符。\n"
            + "· 只有一串纯文字时，括号里直接放 \"文字\"，不必写 {0}。\n"
            + "· 荧幕上限 24 行（引擎常量 PrintMaxLines），再多的行会从最旧的开始丢弃。\n"
            + "· 每次 Run 每帧执行一次，所以每帧至少多一行 → 一秒几十行，几秒就刷满 24 行。\n"
            + "【三个必踩的坑】\n"
            + "✘ 想换行却写了 Print → 所有输出挤在同一行，越挤越长。\n"
            + "✘ 在 Run 里无条件 PrintLine → 屏幕疯狂滚动，看不到自己关心的那行；要用 if 条件限频。\n"
            + "✔ 想在循环里只打一次，先把结果存变量，循环外再 PrintLine。";
        list[1].Page2En = "THIS PAGE: MAKE THE SCREEN SHOW TWO SEPARATE LINES\n"
            + "1) Type tower.PrintLine(\"hp={0}\", 7); then Enter.\n"
            + "2) Type tower.PrintLine(\"tower ready\"); then Enter (no placeholder, no arguments).\n"
            + "3) Press F6: the screen reads hp=7 on one line and tower ready on the next.\n"
            + "4) Compare with the previous task: same text, but PrintLine splits the lines.\n"
            + "READ EVERY SYMBOL\n"
            + "- PrintLine appends a newline; it equals Print(text) plus a line break.\n"
            + "- For plain text just put \"text\" in the parentheses; {0} is only needed with arguments.\n"
            + "- The screen keeps 24 lines (PrintMaxLines); older lines are dropped first.\n"
            + "- Run fires every frame, so you add at least one line per frame - the 24 lines fill in seconds.\n"
            + "THREE TRAPS\n"
            + "- Using Print when you wanted a newline piles everything onto one ever-growing line.\n"
            + "- An unconditional PrintLine inside Run floods the screen; gate it with an if.\n"
            + "- To print once from inside a loop, collect the value first and print after the loop.";
        // ---------- 任务 3：ClearPrint ----------
        list[2].Page2Zh = "【这一页：先写满，再一次清空】\n"
            + "① 先敲两行 tower.PrintLine(\"line A\"); 和 tower.PrintLine(\"line B\");。\n"
            + "② 在这两行下面敲 tower.ClearPrint();。\n"
            + "③ F6 运行：荧幕空白一片——因为同一次执行里，打印完立刻被清掉了。\n"
            + "④ 把 ClearPrint 挪到最上面（第一句），再运行：荧幕只剩 line A / line B。\n"
            + "【逐符号读懂它】\n"
            + "· 顺序即结果：代码从上往下一句句执行，谁在后面谁生效。\n"
            + "· ClearPrint() 括号里没有参数，连空括号也要写。\n"
            + "· 常见正确用法：每帧先 ClearPrint() 再打印本帧状态 → 荧幕永远只显示「当前这一帧」。\n"
            + "【三个必踩的坑】\n"
            + "✘ ClearPrint 放在 PrintLine 之后 → 刚打的东西立刻被抹掉，看起来像「打印失败」。\n"
            + "✘ 写成 Clear / ClearScreen / clearPrint → 报 CS1061 找不到成员（C# 区分大小写）。\n"
            + "✔ 只想清一次：用 if 判断条件（例如计数到某个值）再调，别每帧无条件清。";
        list[2].Page2En = "THIS PAGE: FILL THE SCREEN, THEN WIPE IT ONCE\n"
            + "1) Type tower.PrintLine(\"line A\"); and tower.PrintLine(\"line B\");.\n"
            + "2) Below them type tower.ClearPrint();.\n"
            + "3) Press F6: the screen stays blank, because the prints were wiped in the same pass.\n"
            + "4) Move ClearPrint to the very top and run again: now line A / line B survive.\n"
            + "READ EVERY SYMBOL\n"
            + "- Order is the result: statements run top to bottom, the last one wins.\n"
            + "- ClearPrint() takes no arguments but still needs the empty parentheses.\n"
            + "- The usual correct pattern: ClearPrint() first, then print this frame's state.\n"
            + "THREE TRAPS\n"
            + "- ClearPrint after your prints erases everything, which looks like printing is broken.\n"
            + "- Clear / ClearScreen / clearPrint give CS1061 (C# is case sensitive).\n"
            + "- To clear only once, guard it with an if instead of calling it every frame.";
        // ---------- 任务 4：GetAllObjects ----------
        list[3].Page2Zh = "【这一页：手敲一个能列出全场方块的循环】\n"
            + "① 敲 foreach (var o in tower.GetAllObjects()) 回车，再敲一对花括号 { }。\n"
            + "② 光标进花括号里，敲 tower.PrintLine(\"block #{0}\", o.Index);。\n"
            + "③ F6 运行：荧幕一行一个编号，有几个方块就打几行。\n"
            + "④ 方块全飞过去之后再看，荧幕不新增任何行——列表空了，循环体一次都不进。\n"
            + "【逐符号读懂它】\n"
            + "· GetAllObjects() 返回 List<TowerObjectInfo>，就是「此刻还活着的方块」的快照。\n"
            + "· var o：让编译器自己推断元素类型，等价于写 TowerObjectInfo o。\n"
            + "· foreach：把列表里的元素一个个取出来，自动处理开头结尾，不会越界。\n"
            + "· 花括号里叫循环体，列表有几个元素就跑几遍。\n"
            + "【三个必踩的坑】\n"
            + "✘ 漏写括号 tower.GetAllObjects → 报 CS0428 方法组无法转换为变量。\n"
            + "✘ 循环里反复调用 GetAllObjects()（如 for 条件里写 .Count）→ 每轮都重建列表，又慢又容易前后不一致。\n"
            + "✔ 想循环里删除/打标记：先 var list = tower.GetAllObjects(); 用 list 遍历并另存要打的，再统一发射。";
        list[3].Page2En = "THIS PAGE: HAND-TYPE A LOOP THAT LISTS EVERY BLOCK\n"
            + "1) Type foreach (var o in tower.GetAllObjects()) then a pair of braces { }.\n"
            + "2) Inside the braces type tower.PrintLine(\"block #{0}\", o.Index);.\n"
            + "3) Press F6: one line per block, as many lines as there are blocks.\n"
            + "4) Watch after every block has flown past: no new lines - the list is empty, the body never runs.\n"
            + "READ EVERY SYMBOL\n"
            + "- GetAllObjects() returns List<TowerObjectInfo>: a snapshot of the blocks alive right now.\n"
            + "- var o lets the compiler infer the element type; it equals TowerObjectInfo o.\n"
            + "- foreach walks the list element by element and never runs past the end.\n"
            + "- The braces are the loop body; it runs once per element.\n"
            + "THREE TRAPS\n"
            + "- Omitting the parentheses tower.GetAllObjects gives CS0428.\n"
            + "- Calling GetAllObjects() repeatedly inside the loop (for example in a for condition) rebuilds the list every round.\n"
            + "- To shoot inside a loop, take var list = tower.GetAllObjects(); first, collect picks, then fire.";
        // ---------- 任务 5：Index ----------
        list[4].Page2Zh = "【这一页：把方块编号打到荧幕上】\n"
            + "① 照上一项敲好 foreach 遍历，循环体里只留一行。\n"
            + "② 敲 tower.PrintLine(\"#{0}\", o.Index);。\n"
            + "③ F6 运行，荧幕出现 #12 #13 #17 这样的编号。\n"
            + "④ 多等几波：编号会跳号（中间的号对应已击毁的方块），这是正常的。\n"
            + "【逐符号读懂它】\n"
            + "· Index 是 int，生成器每生成一个方块就 +1，全局递增、永不复用。\n"
            + "· 它只用来「认身份」，既不是数组下标，也不代表离炮台远近。\n"
            + "· o.Index 里的点号读字段；写成 o.Index() 会报 CS1061（字段不是方法）。\n"
            + "【三个必踩的坑】\n"
            + "✘ 拿 Index 当集合下标：list[o.Index] → 编号早就超过元素个数，抛 ArgumentOutOfRangeException。\n"
            + "✘ 以为第一个方块编号是 0：它是全局计数器，关卡里可能从任意值开始。\n"
            + "✔ 想只打某个特定方块：用 Index 做记录（比如 if (o.Index == target)），而不是靠位置猜。";
        list[4].Page2En = "THIS PAGE: PRINT THE BLOCK SERIAL NUMBERS\n"
            + "1) Build the same foreach loop as the previous task, body left with one line.\n"
            + "2) Type tower.PrintLine(\"#{0}\", o.Index);.\n"
            + "3) Press F6: numbers such as #12 #13 #17 appear on the screen.\n"
            + "4) Wait a few waves: numbers skip because destroyed blocks spent their number. That is normal.\n"
            + "READ EVERY SYMBOL\n"
            + "- Index is an int increased by one per spawn; it is global and never reused.\n"
            + "- It identifies a block only; it is not an array index and not a distance.\n"
            + "- o.Index uses a dot to read a field; o.Index() gives CS1061 (a field is not a method).\n"
            + "THREE TRAPS\n"
            + "- Using Index as a list position (list[o.Index]) throws ArgumentOutOfRangeException.\n"
            + "- Assuming the first block has Index 0; the counter is global and may start anywhere.\n"
            + "- To hit one specific block, keep its Index (if (o.Index == target)) instead of guessing a position.";
        // ---------- 任务 6：Type ----------
        list[5].Page2Zh = "【这一页：按颜色分两类打出来】\n"
            + "① 遍历里加判断：if (o.Type == \"ATTACK\") 回车，下一行缩进写 tower.PrintLine(\"red #{0}\", o.Index);。\n"
            + "② 再补一段 else if (o.Type == \"BOMB\")，里面打 tower.PrintLine(\"blue #{0}\", o.Index);。\n"
            + "③ F6 运行：荧幕上红块、蓝块各占一批，互不混。\n"
            + "④ 试着只留 if 不留 else，感受一下「筛掉」和「分类」的区别。\n"
            + "【逐符号读懂它】\n"
            + "· Type 是 string，取值 \"ATTACK\"（红，击毁加分）/ \"BOMB\"（蓝，打了不加分也不扣分）。\n"
            + "· 字符串比较用 == 比较内容，两边都必须带英文双引号。\n"
            + "· else if 表示「上一条不成立时再判断这一条」。\n"
            + "· && 是并且：if (o.Type == \"ATTACK\" && o.HP <= 2) 表示既红又残血。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写成 o.Type = \"ATTACK\"（单等号）→ 这是赋值，条件里会报 CS0029 无法转换。\n"
            + "✘ 大小写写成 \"Attack\" / \"attack\" → 字符串比较区分大小写，永远不成立，看着像「判断失效」。\n"
            + "✔ 蓝块可以顺手打掉刷干净屏幕，但别指望它加分。";
        list[5].Page2En = "THIS PAGE: SPLIT THE OUTPUT BY BLOCK TYPE\n"
            + "1) Add if (o.Type == \"ATTACK\") inside the loop, then indented tower.PrintLine(\"red #{0}\", o.Index);.\n"
            + "2) Add else if (o.Type == \"BOMB\") printing tower.PrintLine(\"blue #{0}\", o.Index);.\n"
            + "3) Press F6: red and blue blocks come out as two separate groups.\n"
            + "4) Then drop the else and feel the difference between filtering and classifying.\n"
            + "READ EVERY SYMBOL\n"
            + "- Type is a string: \"ATTACK\" (red, destroying scores) or \"BOMB\" (blue, no score either way).\n"
            + "- Strings compare with == on content; both sides need straight double quotes.\n"
            + "- else if means \"only test this when the previous test failed\".\n"
            + "- && is AND: if (o.Type == \"ATTACK\" && o.HP <= 2) means red and nearly dead.\n"
            + "THREE TRAPS\n"
            + "- Writing o.Type = \"ATTACK\" (single =) is an assignment and gives CS0029 inside a condition.\n"
            + "- \"Attack\" / \"attack\" never match because comparison is case sensitive; it looks like broken logic.\n"
            + "- Blue blocks can be cleaned up for a tidy screen, but they never add score.";
        // ---------- 任务 7：HP ----------
        list[6].Page2Zh = "【这一页：找出快死的那个方块】\n"
            + "① 遍历里敲 float ratio = (float)o.HP / o.MaxHits; 先把血量换算成比例。\n"
            + "② 再敲 if (o.HP <= 1) 回车，缩进打 tower.PrintLine(\"#{0} nearly dead {1:P0}\", o.Index, ratio);。\n"
            + "③ F6 运行：只有被打到剩 1 层的方块才会出现在荧幕上。\n"
            + "④ 看着某块被一点点削，它的比例从 100% 掉到 0%，掉到 0 就从列表里消失（已摧毁）。\n"
            + "【逐符号读懂它】\n"
            + "· HP 是 int，等于「还剩几层可打」，每命中一发 -1，减到 0 立刻销毁。\n"
            + "· MaxHits 是初始层数（各块不同），HP / MaxHits 才是「剩余血条百分比」。\n"
            + "· (float) 是强制转换：int ÷ int 会做整数除法（3/4 得 0），转成 float 才有小数。\n"
            + "· {1:P0} 是百分比格式，0 表示不保留小数位。\n"
            + "【三个必踩的坑】\n"
            + "✘ 直接写 o.HP / o.MaxHits 不加 (float) → 整数除法结果只有 0 或 1，判断全错。\n"
            + "✘ 用 o.HP <= 0 找残血 → 0 层的方块已被销毁，根本不在 GetAllObjects() 里，永远匹配不到。\n"
            + "✔ 想优先收人头：把残血目标先挑出来再发射，收益最高（红块击毁加分）。";
        list[6].Page2En = "THIS PAGE: FIND THE ALMOST-DEAD BLOCK\n"
            + "1) Inside the loop type float ratio = (float)o.HP / o.MaxHits;.\n"
            + "2) Then if (o.HP <= 1) with tower.PrintLine(\"#{0} nearly dead {1:P0}\", o.Index, ratio);.\n"
            + "3) Press F6: only blocks chipped down to one layer show up on the screen.\n"
            + "4) Watch one block: the ratio falls 100% to 0%, and at 0 it leaves the list (destroyed).\n"
            + "READ EVERY SYMBOL\n"
            + "- HP is an int meaning remaining hit layers; each hit removes one, and 0 destroys the block.\n"
            + "- MaxHits is the starting layer count (differs per block); HP / MaxHits is the health bar.\n"
            + "- (float) is a cast: int / int truncates (3/4 is 0), casting to float keeps the decimals.\n"
            + "- {1:P0} is the percent format; 0 means no decimal places.\n"
            + "THREE TRAPS\n"
            + "- o.HP / o.MaxHits without (float) is integer division, so only 0 or 1 - every test is wrong.\n"
            + "- Testing o.HP <= 0 never matches: a 0-layer block is already destroyed and absent from the list.\n"
            + "- Best payoff: pick the low-HP red blocks first, destroying them scores.";
        // ---------- 任务 8：Position ----------
        list[7].Page2Zh = "【这一页：把方块坐标读出来并打印成一位小数】\n"
            + "① 遍历里敲 Vector3 p = o.Position;——把坐标先存进变量，后面反复用不会重复取值。\n"
            + "② 敲 tower.PrintLine(\"#{0} ({1:F1},{2:F1},{3:F1})\", o.Index, p.x, p.y, p.z);。\n"
            + "③ F6 运行：每行末尾出现形如 (12.4,1.6,-3.2) 的三元组。\n"
            + "④ 盯住 x：方块从 +X 远方飞来，x 一直变小；x 越过 0 就飞过炮台了。\n"
            + "【逐符号读懂它】\n"
            + "· Position 是 Vector3，表示方块几何中心的世界坐标（三个分量都带 float）。\n"
            + "· p.x / p.y / p.z 读三个分量，y 就是离地高度。\n"
            + "· {1:F1} 是格式说明符：序号 1、F = 定点小数、1 = 保留一位；{0} 仍对应 o.Index。\n"
            + "· 炮台固定在原点 (0,0,0)，朝 +X 方向发射。\n"
            + "【三个必踩的坑】\n"
            + "✘ 直接 PrintLine(\"{0}\", o.Position) → 走结构体默认 ToString，精度固定、格式不受控；要控制就写 o.Position.ToString(\"F2\")。\n"
            + "✘ 把 Position 当成方块尺寸 → 尺寸是 Size（Vector3 三边长度）。\n"
            + "✔ Vector3 是值类型：p.x = 5f 只改了副本，o.Position 本身没变。";
        list[7].Page2En = "THIS PAGE: READ BLOCK POSITIONS AT ONE DECIMAL\n"
            + "1) Inside the loop type Vector3 p = o.Position; so you can reuse it without re-reading.\n"
            + "2) Type tower.PrintLine(\"#{0} ({1:F1},{2:F1},{3:F1})\", o.Index, p.x, p.y, p.z);.\n"
            + "3) Press F6: each line ends with a triple such as (12.4,1.6,-3.2).\n"
            + "4) Watch x: blocks come in from far +X, so x shrinks; once x passes 0 the block has flown by.\n"
            + "READ EVERY SYMBOL\n"
            + "- Position is a Vector3: the world-space geometric centre, floats in all three components.\n"
            + "- p.x / p.y / p.z read the components; y is the height above the plane.\n"
            + "- {1:F1} is a format specifier: index 1, F = fixed point, 1 decimal; {0} still means o.Index.\n"
            + "- The tower sits at the origin (0,0,0) and fires toward +X.\n"
            + "THREE TRAPS\n"
            + "- PrintLine(\"{0}\", o.Position) uses the default ToString; for control write o.Position.ToString(\"F2\").\n"
            + "- Position is not the block size; the size is Size (edge lengths as a Vector3).\n"
            + "- Vector3 is a value type: p.x = 5f edits your copy, not o.Position.";
        // ---------- 任务 9：Velocity ----------
        list[8].Page2Zh = "【这一页：算出每个方块飞多快、往哪飞】\n"
            + "① 遍历里敲 Vector3 v = o.Velocity;。\n"
            + "② 敲 tower.PrintLine(\"#{0} speed={1:F2} vz={2:F2}\", o.Index, v.magnitude, v.z);。\n"
            + "③ F6 运行：每行给出速度标量与 z 分量。\n"
            + "④ 观察 vz：正数表示在向 +z 偏，负数向 -z 偏；x 分量在所有方块上基本都是负的（朝炮台飞）。\n"
            + "【逐符号读懂它】\n"
            + "· Velocity 是 Vector3，单位是「单位/秒」：方向 × 速度。\n"
            + "· v.magnitude 是长度（标量速度），做开方运算，循环里别对同一物体反复算。\n"
            + "· v.normalized 是单位方向向量；速度为 0 时结果是 (0,0,0)，不能拿去当发射法向量。\n"
            + "· v.x < 0 表示朝炮台方向靠近，v.x > 0 表示已飞过去、正在远离。\n"
            + "【三个必踩的坑】\n"
            + "✘ 把 Velocity 当成位置用 → 它不带坐标信息，判断远近要用 Position.x。\n"
            + "✘ 在循环里对每个方块都调 v.magnitude 好几次 → 每调一次就开一次方，白费性能，先存 float。\n"
            + "✔ 想打「最危险的」目标：比较 Position.x（越接近 0 越危险），别比速度。";
        list[8].Page2En = "THIS PAGE: COMPUTE HOW FAST EACH BLOCK MOVES\n"
            + "1) Inside the loop type Vector3 v = o.Velocity;.\n"
            + "2) Type tower.PrintLine(\"#{0} speed={1:F2} vz={2:F2}\", o.Index, v.magnitude, v.z);.\n"
            + "3) Press F6: each line reports the scalar speed and the z component.\n"
            + "4) Read vz: positive means drifting to +z, negative to -z; x is negative on almost every block (flying at you).\n"
            + "READ EVERY SYMBOL\n"
            + "- Velocity is a Vector3 in units per second: direction times speed.\n"
            + "- v.magnitude is the length (scalar speed) and does a square root; cache it instead of recomputing.\n"
            + "- v.normalized is the unit direction; at zero speed it is (0,0,0) and must not be used as a firing normal.\n"
            + "- v.x < 0 means closing in on the tower, v.x > 0 means already past and leaving.\n"
            + "THREE TRAPS\n"
            + "- Velocity is not a position; use Position.x to judge distance.\n"
            + "- Calling v.magnitude several times per block repeats the square root; store it in a float first.\n"
            + "- For the most dangerous target compare Position.x (closest to 0), not speed.";
        // ---------- 任务 10：Volume ----------
        list[9].Page2Zh = "【这一页：算出每块还剩多少体积比例】\n"
            + "① 遍历里敲 float left = o.Volume / o.InitialVolume;（两者都是 float，不必强转）。\n"
            + "② 敲 tower.PrintLine(\"#{0} vol={1:F2} left={2:P0}\", o.Index, o.Volume, left);。\n"
            + "③ F6 运行：每块给出现在体积与剩余百分比；新出现的方块是 100%。\n"
            + "④ 多打几发再看同一块：Volume 变小，InitialVolume 始终不变。\n"
            + "【逐符号读懂它】\n"
            + "· Volume：当前剩余体积，被击中切割后变小。\n"
            + "· InitialVolume：出生时的体积，计分用，任何时刻都不变。\n"
            + "· MaxHits：初始受击层数；Size：当前三边尺寸（Vector3），体积要比大小请用 Volume。\n"
            + "· left 越接近 0，说明这块已经快被打干净了，漏掉罚分最少。\n"
            + "【三个必踩的坑】\n"
            + "✘ 用 Size.x * Size.y * Size.z 自己算体积 → 能算但不等于 Volume（切割方式不同，会有偏差）。\n"
            + "✘ 把 Volume 当血量：血量是 HP（层数），体积是几何量，两者不同步。\n"
            + "✔ 只在确定不为 0 时做除法；保险写法 if (o.InitialVolume > 0f) 再算比例。";
        list[9].Page2En = "THIS PAGE: COMPUTE THE REMAINING VOLUME RATIO\n"
            + "1) Inside the loop type float left = o.Volume / o.InitialVolume; (both are floats, no cast needed).\n"
            + "2) Type tower.PrintLine(\"#{0} vol={1:F2} left={2:P0}\", o.Index, o.Volume, left);.\n"
            + "3) Press F6: current volume plus remaining percent; a fresh block reads 100%.\n"
            + "4) Fire a few shots and look again: Volume shrinks while InitialVolume never moves.\n"
            + "READ EVERY SYMBOL\n"
            + "- Volume: the volume left right now; it shrinks as the block is carved.\n"
            + "- InitialVolume: the spawned volume, used for scoring, constant forever.\n"
            + "- MaxHits: starting hit layers; Size: current edge lengths (Vector3) - compare sizes with Volume.\n"
            + "- The closer left is to 0, the less leakage penalty remains.\n"
            + "THREE TRAPS\n"
            + "- Hand-computing Size.x * Size.y * Size.z is not equal to Volume (the cuts differ).\n"
            + "- Volume is not health; health is HP (layers) and the two do not move together.\n"
            + "- Divide only when the divisor is safe; guard with if (o.InitialVolume > 0f).";
        // ---------- 任务 11：GetFireCooldown ----------
        list[10].Page2Zh = "【这一页：先问冷却，再决定开不开火】\n"
            + "① 敲 float cd = tower.GetFireCooldown(); 把剩余冷却秒数存下来。\n"
            + "② 敲 if (cd <= 0f) 回车，缩进写 tower.PrintLine(\"ready\");。\n"
            + "③ 在 else 分支里写 tower.PrintLine(\"cooling {0:F3}s\", cd);。\n"
            + "④ F6 运行：发射瞬间荧幕跳出 cooling 0.028s，冷却清零后又变回 ready。\n"
            + "【逐符号读懂它】\n"
            + "· 返回值是「距离下一发还能打，还差几秒」，0 表示现在就能打。\n"
            + "· 默认冷却 cooldownSeconds = 0.0285s，约每秒 35 发，比一帧（1/60≈0.0167s）长，所以每帧最多成功一发。\n"
            + "· 0f 里的 f 表示这是 float 字面量；和 double 比较会报 CS0019 类型不匹配。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写成 if (cd > 0f) 当就绪判断 → 正好反了，冷却中才 > 0。\n"
            + "✘ 在 Run 里无条件 PrintLine 冷却值 → 每帧一行，荧幕 24 行几秒被刷空。\n"
            + "✔ 冷却没到就别调 FireAtPlane：虽然它只会返回 false，但白白浪费一次判断。";
        list[10].Page2En = "THIS PAGE: ASK THE COOLDOWN BEFORE FIRING\n"
            + "1) Type float cd = tower.GetFireCooldown(); to store the seconds left.\n"
            + "2) Type if (cd <= 0f) then tower.PrintLine(\"ready\");.\n"
            + "3) In the else branch print tower.PrintLine(\"cooling {0:F3}s\", cd);.\n"
            + "4) Press F6: right after a shot the screen shows cooling 0.028s, then flips back to ready.\n"
            + "READ EVERY SYMBOL\n"
            + "- The return value is how many seconds until the next shot; 0 means ready now.\n"
            + "- Default cooldownSeconds = 0.0285s, about 35 shots per second - longer than a frame (1/60 = 0.0167s), so one shot per frame at most.\n"
            + "- The f in 0f marks a float literal; comparing with a double gives CS0019.\n"
            + "THREE TRAPS\n"
            + "- Testing if (cd > 0f) for readiness is inverted; cooling leaves it above 0.\n"
            + "- Printing the cooldown unconditionally inside Run writes one line per frame and flushes the 24 lines fast.\n"
            + "- Skip FireAtPlane while cooling: it only returns false and wastes a check.";
        // ---------- 任务 12：FireAtPlane ----------
        list[11].Page2Zh = "【这一页：真的把一发打出去】\n"
            + "① 敲 bool fired = tower.FireAtPlane(1f, 0f, 0f);——法向量 (1,0,0)，沿 +X 打一发。\n"
            + "② 敲 tower.PrintLine(\"fired={0}\", fired); 看这次有没有打出去。\n"
            + "③ F6 运行：看到激光从原点射出，荧幕显示 fired=True。\n"
            + "④ 改成 tower.FireAtPlane(0f, 1f, 0f); 再运行：激光改成沿 +Y 向上打，证明 A B C 就是方向。\n"
            + "【逐符号读懂它】\n"
            + "· 数学含义：沿平面 A*x+B*y+C*z=0 的法线方向 (A,B,C) 发射；平面必过原点（炮台在原点），所以只有三个参数、没有 D。\n"
            + "· 想打某个方块：直接把它的 Position 三分量传进去，tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z); 因为从原点指向目标的向量本身就是一个合法法向量。\n"
            + "· 返回 false 的两种情况：(A,B,C) 近似零向量，或者还在冷却中。\n"
            + "· 1f / 0f 的 f 不能省，否则按 double 传入，重载匹配不上报错。\n"
            + "【三个必踩的坑】\n"
            + "✘ 传 (0f, 0f, 0f) → 零向量直接返回 false，什么都不会发生。\n"
            + "✘ 一帧里连调三次想打三发 → 冷却 0.0285s 卡住，后两次全返回 false。\n"
            + "✔ 想打得更准：先按 Type 筛红块、按 Position.x 挑最近的一个，再传它的坐标。";
        list[11].Page2En = "THIS PAGE: ACTUALLY FIRE A SHOT\n"
            + "1) Type bool fired = tower.FireAtPlane(1f, 0f, 0f); - normal (1,0,0), one shot along +X.\n"
            + "2) Type tower.PrintLine(\"fired={0}\", fired); to see whether it went out.\n"
            + "3) Press F6: the laser leaves the origin and the screen prints fired=True.\n"
            + "4) Change it to tower.FireAtPlane(0f, 1f, 0f); and run again: the laser goes straight up, so A B C is the direction.\n"
            + "READ EVERY SYMBOL\n"
            + "- Math: fire along the normal (A,B,C) of the plane A*x+B*y+C*z=0; the plane always passes the origin, so there is no D.\n"
            + "- To aim at a block pass its Position: tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z); the vector from the origin to the target is already a valid normal.\n"
            + "- false comes back when (A,B,C) is near zero or the tower is still cooling down.\n"
            + "- The f in 1f / 0f is required, otherwise the arguments are doubles and no overload matches.\n"
            + "THREE TRAPS\n"
            + "- Passing (0f, 0f, 0f) returns false immediately and nothing happens.\n"
            + "- Three calls in one frame cannot fire three shots; the 0.0285s cooldown rejects the last two.\n"
            + "- For accuracy, filter red blocks by Type, pick the nearest by Position.x, then pass its position.";
        // ---------- 任务 13：实战组合 ----------
        list[12].Page2Zh = "【这一页：把三件事串成一段能打的代码】\n"
            + "① 第一句写 tower.ClearPrint();——每帧先清屏，荧幕才不会越堆越乱。\n"
            + "② 写 foreach (var o in tower.GetAllObjects())，花括号里开始筛选。\n"
            + "③ 写 if (o.Type == \"ATTACK\" && o.Position.x > 0f)：只挑红块、且还在炮台前方（没飞过去）的。\n"
            + "④ 里面写 tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z); 再 break;，打一发就跳出循环。\n"
            + "【逐符号读懂它】\n"
            + "· && 把两个条件串起来：两个都成立才进 if。\n"
            + "· break 立刻结束整个 foreach，防止一帧内对多个目标浪费冷却。\n"
            + "· o.Position 的三分量直接当法向量传，等于「朝这个方块打」。\n"
            + "· Run 每帧都会跑一遍这段代码，于是炮台就变成了每帧自动找目标开火的自动炮台。\n"
            + "【三个必踩的坑】\n"
            + "✘ 忘了 break → 循环里对每个方块都调一次发射，冷却期内的调用全返回 false，白算。\n"
            + "✘ 用 o.Position.x > 0f 之外还想打远处的 → 条件写反（< 0）会去打已经飞过去、正在远离的方块。\n"
            + "✔ 进阶：想打最靠前的，先用 float best = float.MaxValue; 遍历比较 Position.x 取最小，循环结束后只发一次。";
        list[12].Page2En = "THIS PAGE: CHAIN THE THREE PIECES INTO WORKING CODE\n"
            + "1) First line: tower.ClearPrint(); so the screen never piles up across frames.\n"
            + "2) Write foreach (var o in tower.GetAllObjects()) and start filtering in the braces.\n"
            + "3) Write if (o.Type == \"ATTACK\" && o.Position.x > 0f): red blocks that are still in front of the tower.\n"
            + "4) Inside, call tower.FireAtPlane(o.Position.x, o.Position.y, o.Position.z); then break; - one shot, leave the loop.\n"
            + "READ EVERY SYMBOL\n"
            + "- && chains two tests: both must hold to enter the if.\n"
            + "- break ends the whole foreach at once, so one frame never burns cooldown on many targets.\n"
            + "- Passing the three components of o.Position as the normal means \"shoot at this block\".\n"
            + "- Run executes this block every frame, which turns the tower into an auto-turret.\n"
            + "THREE TRAPS\n"
            + "- Forgetting break calls FireAtPlane on every block; the calls inside the cooldown just return false.\n"
            + "- Flipping the test to < 0 shoots blocks that already flew past and are leaving.\n"
            + "- Next step: keep float best = float.MaxValue;, track the smallest Position.x, and fire once after the loop.";
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

        // 点击进度格跳转任务需要射线检测器：本 Canvas 独立于代码编辑器 Canvas，必须自带一个
        gameObject.AddComponent<GraphicRaycaster>();

        // 保险：没有 EventSystem 时补一个（正常情况下代码编辑器已创建）
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        chipTexts = new Text[MaxChips];
        chipBgs = new Image[MaxChips];
        chipBaseColors = new Color[MaxChips];

        BuildTaskPanel(canvas.transform);
        BuildHintBar(canvas.transform);
    }

    private void BuildTaskPanel(Transform parent)
    {
        // 任务卡（指导框）：屏幕底部横向整条 —— 上下分栏布局。
        //   · 上边缘与代码编辑器（TowerCodeEditor）下边缘严丝合缝：面板高度会同步给
        //     CodeEditorController.SetBottomReservedHeight，编辑器整体上移收窄到上半区，两者不重叠；
        //   · 下边缘贴屏幕底部（anchoredPosition = 0），左右铺满整个可用宽度（anchorMin.x = 0 / anchorMax.x = 1）；
        //   · 高度固定为工作区高度的一半（ApplySplitLayout），与上方代码区严格对半平分，不随窗口 / 文案变化；
        //     字号固定不变，玩家想看全文就拖顶部蓝色长条把提示区拉高（不再自动缩字号）。
        RectTransform panelRt = CreateRect("SyntaxTaskPanel", parent, new Vector2(0f, 0f), Vector2.zero,
            new Vector2(0f, ReferenceHeight * SplitRatio), new Vector2(0.5f, 0f));
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(1f, 0f);
        panelRt.pivot = new Vector2(0.5f, 0f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(0f, ReferenceHeight * SplitRatio);
        panelRoot = panelRt.gameObject;

        Image bg = panelRoot.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.Panel;
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
        accent.color = UITheme.WithAlpha(UITheme.Accent, 0.95f);
        accent.raycastTarget = false;
        accentLine = accent;

        // 蓝色分割线可拖动：把热区做成一条透明横带，骑在强调线上，上下拖动即可调整提示区高度
        BuildSplitDragHandle(panelRt);

        // 标题
        headlineText = CreateText("Headline", panelRt, "", 24,
            UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform hlRt = headlineText.rectTransform;
        hlRt.anchorMin = new Vector2(0f, 1f);
        hlRt.anchorMax = new Vector2(0f, 1f);
        hlRt.pivot = new Vector2(0f, 1f);
        hlRt.anchoredPosition = new Vector2(24f, -12f);
        hlRt.sizeDelta = new Vector2(700f, 30f);

        // 进度「任务 k/18」
        progressText = CreateText("Progress", panelRt, "", 21,
            UITheme.TextBody, TextAnchor.MiddleRight);
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
            chipBg.color = UITheme.PanelSunk;
            chipBg.raycastTarget = true; // 进度格可点击（点数字直接跳到该任务）
            chipBgs[i] = chipBg;
            chipBaseColors[i] = chipBg.color;

            Text chipText = CreateText("ChipText" + (i + 1), chipRt, (i + 1).ToString(), 17,
                UITheme.TextMuted, TextAnchor.MiddleCenter);
            Stretch(chipText.rectTransform);
            chipText.raycastTarget = false;
            chipTexts[i] = chipText;

            // 点击跳转：在本关（同一门教学课）内任意任务之间互跳；不改已完成标记，也不跨关卡
            int chipIndex = i;
            Button chipBtn = chipRt.gameObject.AddComponent<Button>();
            chipBtn.targetGraphic = chipBg;
            chipBtn.transition = Selectable.Transition.None; // 颜色由代码统一控制，避免 ColorTint 覆盖
            chipBtn.onClick.AddListener(delegate { JumpToTask(chipIndex); });

            // 悬停变亮：给出「这个数字能点」的视觉反馈
            UnityEngine.EventSystems.EventTrigger hover = chipRt.gameObject
                .AddComponent<UnityEngine.EventSystems.EventTrigger>();
            UnityEngine.EventSystems.EventTrigger.Entry enter = new UnityEngine.EventSystems.EventTrigger.Entry();
            enter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enter.callback.AddListener(delegate { SetChipHover(chipIndex, true); });
            hover.triggers.Add(enter);
            UnityEngine.EventSystems.EventTrigger.Entry exit = new UnityEngine.EventSystems.EventTrigger.Entry();
            exit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exit.callback.AddListener(delegate { SetChipHover(chipIndex, false); });
            hover.triggers.Add(exit);

            chipRt.gameObject.SetActive(false);
        }

        // 任务名（横向铺满，右侧留 24 内边距）
        taskNameText = CreateText("TaskName", panelRt, "", 26, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        RectTransform tnRt = taskNameText.rectTransform;
        tnRt.anchorMin = new Vector2(0f, 1f);
        tnRt.anchorMax = new Vector2(1f, 1f);
        tnRt.pivot = new Vector2(0f, 1f);
        tnRt.anchoredPosition = new Vector2(24f, -88f);
        // 右侧留出翻页条（‹ 说明 1/2 ›）的宽度，任务名不会与之重叠
        tnRt.sizeDelta = new Vector2(-48f - PageNavWidth - 12f, 36f);

        // 翻页条：与任务名同一行、贴右边界；只有该任务有第 2 页时才显示（见 RefreshPageNav）
        BuildPageNav(panelRt);

        // 正文分左右两栏：左 = 目标 / 提示 / 判定 + 完成条件，右 = 写法 / 示例代码，中间一条竖分隔线；
        // 这样左右两半都用起来，字号可以放大而不溢出（左右共用同一段正文区高度）。
        // 上下边距固定，正文区高度 = 半屏高度 - 上下留白；面板高度不因文案变化（ApplySplitLayout），
        // 字号恒定（PinBodyFontSize），不做任何自适应缩放。
        bodyText = CreateText("Body", panelRt, "", GuideBodyFontSize,
            UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform bodyRt = bodyText.rectTransform;
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(ColumnSplitRatio, 1f);
        bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.offsetMin = new Vector2(24f, GuideBodyBottomInset);
        bodyRt.offsetMax = new Vector2(-ColumnGap * 0.5f, -GuideBodyTopInset);
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        // 手动把分割线往下拖时字号保持不变，放不下的文字直接截断（不外溢到代码区）
        bodyText.verticalOverflow = VerticalWrapMode.Truncate;
        bodyText.lineSpacing = GuideBodyLineSpacing;
        bodyText.raycastTarget = false;

        // 右栏：写法 / 示例代码（无写法段时隐藏，左栏自动变成通栏）
        sampleText = CreateText("Sample", panelRt, "", SampleFontSize,
            UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform sampleRt = sampleText.rectTransform;
        sampleRt.anchorMin = new Vector2(ColumnSplitRatio, 0f);
        sampleRt.anchorMax = new Vector2(1f, 1f);
        sampleRt.pivot = new Vector2(0.5f, 0.5f);
        sampleRt.offsetMin = new Vector2(ColumnGap * 0.5f, GuideBodyBottomInset);
        sampleRt.offsetMax = new Vector2(-24f, -GuideBodyTopInset);
        sampleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        sampleText.verticalOverflow = VerticalWrapMode.Truncate;
        sampleText.lineSpacing = GuideBodyLineSpacing;
        sampleText.raycastTarget = false;

        // 第 3 页的内存可视化舞台：与两栏文字同层，覆盖整块正文区，平时隐藏、只在内存页露面。
        // 必须建在 bodyText / sampleText 之后才会盖在文字上面（同级 UICanvas 里后面的更靠前）。
        BuildMemStage(panelRt);

        // 左 / 右分栏竖分隔线（贯穿正文区，用暗色细线，不抢文字）
        GameObject dividerGo = new GameObject("ColumnDivider", typeof(RectTransform));
        RectTransform dividerRt = dividerGo.GetComponent<RectTransform>();
        dividerRt.SetParent(panelRt, false);
        dividerRt.anchorMin = new Vector2(ColumnSplitRatio, 0f);
        dividerRt.anchorMax = new Vector2(ColumnSplitRatio, 1f);
        dividerRt.pivot = new Vector2(0.5f, 0.5f);
        dividerRt.offsetMin = new Vector2(-DividerWidth * 0.5f, GuideBodyBottomInset);
        dividerRt.offsetMax = new Vector2(DividerWidth * 0.5f, -GuideBodyTopInset);
        columnDivider = dividerGo.AddComponent<Image>();
        columnDivider.sprite = WhiteSprite();
        columnDivider.color = UITheme.HairlineDim;
        columnDivider.raycastTarget = false;
        dividerGo.SetActive(false);

        // 「任务完成 ✔」提示
        flashText = CreateText("Flash", panelRt, "", 20,
            UITheme.Value, TextAnchor.MiddleLeft);
        RectTransform flRt = flashText.rectTransform;
        flRt.anchorMin = new Vector2(0f, 0f);
        flRt.anchorMax = new Vector2(1f, 0f);
        flRt.pivot = new Vector2(0f, 0f);
        flRt.anchoredPosition = new Vector2(24f, 12f);
        flRt.sizeDelta = new Vector2(-48f, 28f);
        flashText.raycastTarget = false;

        panelRt.gameObject.SetActive(false);

        LayoutChips();
        ApplySplitLayout();
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

    /// <summary>工作区（面板父节点）的高度，拿不到时退回参考分辨率高度。</summary>
    private float WorkAreaHeight()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        RectTransform parentRt = panelRt != null ? panelRt.parent as RectTransform : null;
        return (parentRt != null && parentRt.rect.height > 1f) ? parentRt.rect.height : ReferenceHeight;
    }

    /// <summary>当前是否停在第 2 页（手把手细节）：只有这类任务才把提示区临时放大。</summary>
    private bool IsOnDetailPage()
    {
        return guidePage > 0 && !finished && tasks != null
            && CurrentIndex >= 0 && CurrentIndex < tasks.Length && PageCountOf(CurrentIndex) > 1;
    }

    /// <summary>
    /// 舞台的「基准高度」＝提示区不拖动时的高度减去上下内缩。
    /// 拖矮面板时内容仍按这个高度排，底部那块才会顺着往下走、移出视野。
    /// </summary>
    private float MemStageBaselineHeight()
    {
        return Mathf.Max(120f, SplitBaseHeight() - (MemStageTopInset + MemStageBottomInset));
    }

    /// <summary>
    /// 把「执行台 + 旁白」这一整块摆到内容的自然底部（layoutH 从舞台顶边往下量）。
    /// 面板被拖矮 → layoutH 不变，这一块就落到面板下沿之外（走出去）；
    /// 面板被拖高 → layoutH 跟着变大，这一块仍贴着真正的底边，不会悬在半空。
    /// </summary>
    private void PlaceMemBottom(float layoutH)
    {
        float noteTop = layoutH - MemNoteH;
        if (memNoteText != null)
            memNoteText.rectTransform.anchoredPosition = new Vector2(0f, -noteTop);
        if (memExprRt != null)
            memExprRt.anchoredPosition = new Vector2(0f, -(noteTop - MemNoteGapY - MemExprH));
    }

    /// <summary>提示区基准高度（不含手动拖动偏移）：第 1 页 = 工作区一半，第 2 页 = DetailPageRatio。</summary>
    private float SplitBaseHeight()
    {
        return Mathf.Max(120f, WorkAreaHeight() * (IsOnDetailPage() ? DetailPageRatio : SplitRatio));
    }

    /// <summary>
    /// 提示区实际高度 = 基准高度 + 玩家手动拖动蓝色分割线产生的偏移（manualSplitOffset），
    /// 并夹在 [MinHintHeight, 工作区高 - MinCodeHeight] 之间，保证代码区与提示区都还剩一截可用空间。
    /// </summary>
    private float SplitHalfHeight()
    {
        float total = WorkAreaHeight();
        float baseHeight = SplitBaseHeight();
        float minHeight = Mathf.Min(MinHintHeight, total * 0.55f);
        float maxHeight = Mathf.Max(minHeight + 40f, total - MinCodeHeight);
        return Mathf.Clamp(baseHeight + manualSplitOffset, minHeight, maxHeight);
    }

    /// <summary>
    /// 应用「上下各占一半」的固定分栏：指导框高度 = 工作区高度 × 50%，代码编辑器的底部预留高度同步为该值，
    /// 于是上方代码区与下方指引区永远对半平分、严丝合缝，不随窗口尺寸 / 文案长度漂移；
    /// 字号恒为固定值，不再随高度 / 栏宽自适应 —— 玩家想多看内容就拖蓝色长条把提示区拉高。
    /// </summary>
    private void ApplySplitLayout()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        if (panelRt == null)
            return;

        float height = SplitHalfHeight();
        if (Mathf.Abs(panelRt.sizeDelta.y - height) > 0.5f)
            panelRt.sizeDelta = new Vector2(0f, height); // 横向靠 anchorMin.x=0 / anchorMax.x=1 铺满，只改高度

        appliedSplitHeight = height;
        // 高度被上下限夹住时把偏移量回写，避免继续往同一方向拖时偏移越积越大（回拖时手感发飘）
        manualSplitOffset = height - SplitBaseHeight();

        // 手动拖动分割线只改高度、不重排字号：字号恒定，放不下的部分直接截断（拖高即可看全）
        PinBodyFontSize();
        LayoutChips();

        // 关键：指导框高度即编辑器的「底部预留高度」→ 上方代码区与下方指导框各占一半，两者不重叠、中间不留缝
        if (editor != null && Active)
            editor.SetBottomReservedHeight(height);
    }

    /// <summary>
    /// 分栏布局：有示例代码 → 左 = 引导、右 = 代码、中间竖分隔线；没有示例代码 → 左栏通栏，右栏与分隔线隐藏。
    /// 分栏位置只由 LayoutGuideColumns 决定，与字号无关（字号固定，不再依赖栏宽自适应）。
    /// </summary>
    private void LayoutGuideColumns()
    {
        bool hasSample = sampleText != null && !string.IsNullOrEmpty(sampleText.text);
        columnHasSample = hasSample;

        if (columnDivider != null && columnDivider.gameObject.activeSelf != hasSample)
            columnDivider.gameObject.SetActive(hasSample);

        if (sampleText != null && sampleText.gameObject.activeSelf != hasSample)
            sampleText.gameObject.SetActive(hasSample);

        RectTransform bodyRt = bodyText != null ? bodyText.rectTransform : null;
        if (bodyRt == null)
            return;

        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(hasSample ? ColumnSplitRatio : 1f, 1f);
        bodyRt.offsetMin = new Vector2(24f, GuideBodyBottomInset);
        bodyRt.offsetMax = new Vector2(hasSample ? -ColumnGap * 0.5f : -24f, -GuideBodyTopInset);
    }

    /// <summary>
    /// 字号锁定：蓝色长条可拖动后不再做任何自适应缩放 —— 玩家想看全文就直接把提示区拉高，
    /// 因此这里只把左右两栏恢复到构建时的固定字号（切换任务 / 翻页后也不会被旧字号残留影响）。
    /// </summary>
    private void PinBodyFontSize()
    {
        PinOneFont(bodyText, GuideBodyFontSize);
        PinOneFont(sampleText, SampleFontSize);
    }

    /// <summary>把单个文本块的字号钉回固定值；字号确实变化过才重选字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）。</summary>
    private static void PinOneFont(Text t, int size)
    {
        if (t == null || t.fontSize == size)
            return;

        t.fontSize = size;
        t.lineSpacing = GuideBodyLineSpacing;
        ReloadFont(t);
    }

    private void BuildHintBar(Transform parent)
    {
        // 编辑器关闭时（= 玩家眼前的实机页面：方块、炮台、print 荧幕）在页面正中挂一条进入提示，
        // 明确告知「按下 Enter 打开代码编辑界面」；按 Enter 后才弹出编辑器、任务卡随之出现。
        // 宽高在 RefreshHint / RefitHintBar 中按实测宽高自适应，长文案自动换行加高，不会被截断。
        RectTransform hintRt = CreateRect("SyntaxEntryPrompt", parent, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(HintBarMaxWidth, HintBarMinHeight), new Vector2(0.5f, 0.5f));
        hintRt.anchoredPosition = new Vector2(0f, EntryPromptY);
        hintRoot = hintRt.gameObject;
        hintRect = hintRt;

        Image bg = hintRoot.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.ScrimSoft;
        bg.raycastTarget = false;

        hintText = CreateText("HintText", hintRt, "", 34, UITheme.TextPrimary, TextAnchor.MiddleCenter);
        Stretch(hintText.rectTransform);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hintText.verticalOverflow = VerticalWrapMode.Overflow;
        hintText.supportRichText = true; // 主行（按下 Enter）大号高亮、副行（当前任务）小号弱化
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

        float width = Mathf.Clamp(preferredWidth + 96f, HintBarMinWidth, maxWidth);
        float height = Mathf.Max(HintBarMinHeight, preferredHeight + 36f);
        hintRect.sizeDelta = new Vector2(width, height);
        hintRect.anchoredPosition = new Vector2(0f, EntryPromptY); // 始终居中偏上，不随文字长短漂移
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

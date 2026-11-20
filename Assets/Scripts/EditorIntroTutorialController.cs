using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Level 1 - Part 3「编辑器入门」：在学 C# 语法之前，先把手把手教会玩家"这个黑色窗口怎么用"。
///   · 8 页：认识编辑器（Enter / Alt+O 打开）→ 认五大区域（标题栏 / 行号列 / 代码区 / 红色 CE / 紫色 RE / 蓝条）
///     → 点出光标与行号 → F6 编译运行 → 红色编译错误怎么读 → 紫色运行时错误怎么读 → 关闭 / 载入 / 导出等常用键
///     → 小结并接语法课；
///   · 第 1 / 3 / 4 页带真实操作门控：打开编辑器、点出光标、按下 F6 才算过关（不满足时「下一页」给出提示，不硬推）；
///   · 打开编辑器时卡片自动缩成顶部窄卡（Compact），不挡代码区；关闭后恢复全屏阅读卡；
///   · 键盘 ← → 翻页，S 跳过本关直接进 Level 1 - Part 4 C# 语法课；
///   · 章节跳转：/level 1 3 n 可直接翻到第 n 页（见 LevelDirector）。
/// </summary>
public class EditorIntroTutorialController : MonoBehaviour
{
    /// <summary>讲解页数（供 /level 1 2 n 的章节校验与页码显示）。</summary>
    public const int PageCount = 8;

    public static EditorIntroTutorialController Instance { get; private set; }

    /// <summary>讲解关是否正在显示。</summary>
    public bool Active { get; private set; }

    /// <summary>是否正在显示讲解页（本关要求玩家自己打开编辑器，因此不参与 CodeEditorController 的 Enter 拦截）。</summary>
    public static bool Reading
    {
        get { return Instance != null && Instance.Active; }
    }

    /// <summary>当前页（0 基）。</summary>
    public int CurrentPage { get; private set; }

    private const float RefWidth = 1920f;
    private const float RefHeight = 1080f;

    private GameObject root;        // 全屏遮罩 + 面板的根节点
    private GameObject panelRoot;   // 阅读面板
    private Text pageTagText;       // 「序章 1/4」
    private Text titleText;         // 页标题
    private Text bodyText;          // 正文
    private Text footerText;        // 底部操作提示（键盘 ← → 也能翻页）
    private bool transitioning;     // 正在播进入语法课的过渡（忽略重复点击 / 键盘）

    // 进入 Level 1 - Part 4 的黑幕过渡时长：与 Level 1 - Part 1 → Part 2（TutorialController）保持一致
    private const float FadeOutSeconds = 0.35f;
    private const float FadeInSeconds = 0.5f;
    private Button prevBtn;
    private Button nextBtn;
    private RectTransform prevRt;   // 底部按钮组的三个矩形：宽度按文案动态重排（见 LayoutNavButtons）
    private RectTransform nextRt;
    private RectTransform skipRt;
    private Text prevLabel;
    private Text nextLabel;
    private Text skipLabel;
    private PlayerCameraController playerController; // 读字期间接管视角：释放鼠标、禁止鼠标转朝向
    private const KeyCode SkipKey = KeyCode.S;       // 跳过序章的快捷键（与按钮同效）

    // ---- 底部按钮组布局 ----
    // 三个按钮等距排布、整组在面板内水平居中。
    // 「下一页」在末页会变成「开始语法课 › / Start the course ›」，比「下一页 › / Next ›」宽一大截：
    // 固定 210 宽会把文字顶出按钮边框（英文尤其明显），所以宽度改由 LayoutNavButtons() 按当前文案实测后动态给。
    private const float NavGap = 28f;
    private const float PrevW = 180f;
    private const float NextWMin = 210f;
    private const float NextWMax = 380f;
    private const float SkipW = 320f;
    private const float NavBtnH = 62f;
    private const float NavBtnY = 40f;
    private const float NavLabelPadding = 48f; // 按钮文字左右各留的内边距，避免量出来的宽度贴边

    // ---- 正文字号自适应（修复换字体后的文字溢出）----
    // 正文框是「固定高度 + verticalOverflow = Overflow」，字号原先写死 26。
    // 字体从 msyh / Consolas 换成 Noto Sans CJK SC / JetBrains Mono 后：
    //   · 同一字号的字形宽度增约 9%（CJK 全角宽度不变，但西文 / 数字变宽，混排断行位置随之变化）；
    //   · 行高从雅黑的 1.32em 涨到 Noto 的 1.45em（+9.6%，1.35f 行距下 26 号每行从 46.4px 变 50.8px）。
    // 两者叠加使 4 页正文里有 3 页超出正文框（第 2/3 页 +222px、第 4 页 +171px），文字直接压到页脚提示、
    // 按钮上并越过面板下边界。这里改为按正文框真实尺寸挑字号：在 [BodyFontMin, BodyFontMax] 内取
    // 「4 页中文 + 4 页英文都放得下」的最大字号，翻页时字号统一、不跳变；不改文案、不改流程。
    private const int BodyFontMax = 32;
    private const int BodyFontMin = 20;
    private const float BodyFitPadding = 6f; // 留余量：避免字形取整 / 行高取整后正好压线
    private int bodyFontSize = BodyFontMax;

    // ---- 操作门控：这三页必须玩家真的动手，才算看过 ----
    private const int PageOpenEditor = 0;   // 第 1 页：按 Enter 打开编辑器
    private const int PageClickCaret = 2;   // 第 3 页：在代码区点出光标
    private const int PageRunCode = 3;      // 第 4 页：按 F6 编译并运行
    private const int PageReZone = 5;       // 第 6 页：按 F6 让代码崩给你看（紫色 RE 窗口）
    private bool caretClicked;              // 本页期间是否点出过光标
    private bool f6Pressed;                 // 本页期间是否按下过 F6
    private bool reDemoSeen;                // 第 6 页期间是否已经亲眼看到紫色 RE 窗口
    private int subPage;                    // 当前页已翻到第几屏（长页被拆成两屏，0 基）
    private int subPageCount = 1;           // 当前页共有几屏（正文里没有分隔符的页只有 1 屏）
    private int demoStage = -1;             // 演示代码注入阶段：-1 未注入 / 0 带错版 / 1 补好分号版
    private CodeEditorController editor;     // 直读编辑器的 IsOpen / IsCodeInputFocused

    // ---- 两套布局：全屏阅读卡（讲解页）/ 顶部窄卡（要一边看编辑器一边操作的页）----
    private const float CompactWidth = 700f;
    private const float CompactHeight = 880f;
    private const float CompactSideMargin = 36f;   // 窄卡离屏幕右边缘的距离
    private const float CompactVertMargin = 100f;  // 窄卡离屏幕上下边缘的距离（上下必须等距，否则卡片看着是歪的）
    private const float CompactPadding = 44f;      // 窄卡内左右内边距（700 宽装 56 会把标题挤出去）
    private const int CompactTitleFont = 32;       // 窄卡标题字号（全屏是 44，700 宽一行放不下英文标题）
    private const float CompactTitleHeight = 104f; // 窄卡标题允许两行（32 号两行 ≈ 93px，仍留呼吸位）
    private const float CompactBodyTop = 200f;     // 窄卡里页码 + 两行标题占掉的高度（标题底 178 + 22 呼吸）
    private const float CompactBodyBottom = 132f; // 给页脚提示行与按钮组让位（页脚顶边 122，留 10px 呼吸）
    private const int CompactBodyFont = 22;       // 窄卡正文行长，比全屏小一档
    private const int CompactBodyFontMin = 18;    // 窄卡正文下限：再挤也不许小到看不清
    private RectTransform panelRt;                // 阅读面板（两套布局共用同一个）
    private RectTransform bodyRt;                 // 正文框（两套布局的上下边距不同）
    private Image scrimImage;                     // 全屏遮罩（窄卡模式下几乎透明且不吞点击）

    /// <summary>
    /// 按正文框真实宽高，选出能容纳「全部 4 页中文 + 4 页英文」的最大正文字号；都放不下时按可读性下限兜底。
    /// 直接量 bodyText 自身的 Text.preferredHeight，度量与运行时渲染完全同源（换字体、换分辨率都不会再估偏）。
    /// </summary>
    private int FitBodyFontSize()
    {
        if (bodyText == null)
            return BodyFontMax;

        Rect bodyArea = bodyText.rectTransform.rect;
        if (bodyArea.width <= 1f || bodyArea.height <= 1f)
            return BodyFontMax; // 布局尚未就绪：保持默认字号

        float limit = bodyArea.height - BodyFitPadding;
        for (int size = BodyFontMax; size > BodyFontMin; size--)
        {
            if (AllPagesFit(size, limit))
                return size;
        }
        return BodyFontMin;
    }

    /// <summary>4 页中文 + 4 页英文是否都能在 limit 高度内放下（翻页时字号统一，不跳变）。</summary>
    private bool AllPagesFit(int fontSize, float limit)
    {
        for (int i = 0; i < PageCount; i++)
        {
            if (!ChunksFit(BodiesZh[i], fontSize, limit) || !ChunksFit(BodiesEn[i], fontSize, limit))
                return false;
        }
        return true;
    }

    /// <summary>把文案临时灌进 bodyText 量一次实际排版高度（字号 + 字体与 Refresh / SetLabel 保持一致）。</summary>
    private bool PageFits(string content, int fontSize, float limit)
    {
        bodyText.fontSize = fontSize;
        bodyText.font = UIFontHelper.LoadForText(content, fontSize);
        bodyText.text = content;
        return bodyText.preferredHeight <= limit;
    }

    /// <summary>
    /// 一页可能被 BodyBreak 切成两屏：只要每一屏都放得下，这一页就算放得下。
    /// 量的是「单屏最大高度」而不是「整页高度」——长页拆屏后字号不再被整页总高度拖小。
    /// </summary>
    private bool ChunksFit(string content, int fontSize, float limit)
    {
        string[] chunks = content.Split(new[] { BodyBreak }, System.StringSplitOptions.None);
        for (int i = 0; i < chunks.Length; i++)
        {
            if (!PageFits(chunks[i], fontSize, limit))
                return false;
        }
        return true;
    }

    private static readonly string[] TitlesZh =
    {
        "编辑器 1/8 · 接下来你写的每一行代码，都在这个窗口里",
        "编辑器 2/8 · 一屏分成六块，从上到下认一遍",
        "编辑器 3/8 · 光标和行号：先学会把光标放到对的地方",
        "编辑器 4/8 · F6 = 编译并运行，让代码真的动起来",
        "编辑器 5/8 · 底部红色区：代码根本没跑起来的那些错",
        "编辑器 6/8 · 底部紫色窗：代码跑起来了，然后跑到一半崩了",
        "编辑器 7/8 · 关闭、载入、导出，以及几个顺手键",
        "编辑器 8/8 · 小结：你已经会开这台机器了"
    };

    private static readonly string[] TitlesEn =
    {
        "Editor 1/8 - Every line you write lives in this window",
        "Editor 2/8 - Six areas, top to bottom",
        "Editor 3/8 - Caret and line numbers: aim before you type",
        "Editor 4/8 - F6 compiles and runs: make the code move",
        "Editor 5/8 - The red zone: errors that stopped the code from running",
        "Editor 6/8 - The purple window: it ran, then crashed halfway",
        "Editor 7/8 - Close, load, export, and a few handy keys",
        "Editor 8/8 - Recap: you can drive this thing now"
    };

    // 一页正文太长会把整页字号一路压到看不清（第 5 页红区讲得最多：样例 + 三步读法 + 三类错 + 多错处理）。
    // 这里给正文留一个「翻屏符」：一页用 \u000c 切成两屏，卡片上先用「续页 ›」/PageDown 看后半段，
    // 每屏都只有半页的量，字号就能稳稳停在 18 号 —— 不删内容、不缩字。
    private const string BodyBreak = "\u000c";

    // 正文统一控制在此行数内（约 14 行），保证一屏放得下、不需要缩到看不清
    private static readonly string[] BodiesZh =
    {
        "这是炮台代码编辑器，整局游戏里最重要的界面：你写的每一行代码都放在这里，炮台照着它自动开火。\n\n① 按 Enter（回车，小键盘 Enter 也行）打开它；\n② 想更快：Alt + O 直接开，Ctrl + Shift + O 同样有效；\n③ 打开的时候游戏会暂停：时间停住、方块不动、鼠标被释放，随便你操作。\n\n动手做：现在按一下 Enter，打开编辑器 —— 我会自动带你进下一页。",
        "编辑器打开后，这一屏从上到下是六块东西，认清了就不会慌：\n\n① 顶部标题栏：写着编辑器名字和状态；\n② 最左边窄窄一列 = 行号列：报错说「第 3 行」，你就看这里数到 3；\n③ 中间大片黑色 = 代码区：你敲的每个字符都在这里；\u000c\n④ 底部红色区（CE，编译错误）：语法写错、代码根本没跑起来，错在这里；\n⑤ 底部紫色小窗（RE，运行时错误）：代码跑起来了但中途崩了，异常在这里；\n⑥ 贴着底边那根蓝色横条 = 横向滚动条：一行太长时左右拖。\n\n红色区和紫色窗都可以收起来，看第 5、6 页的快捷键。",
        "试试看：用鼠标在代码区任意位置点一下。\n\n· 会看到一条白色竖线在闪 —— 这就是光标，你敲的字插在光标处；\n· 左边行号列一行对一号：报错说「第 3 行」，就照它往下数到 3；\n· 移动光标：方向键 ↑ ↓ ← →；Home 跳到行首、End 跳到行尾；\u000c\n· 选中文字：按住左键拖，或用 Shift + 方向键；\n· Tab 键缩进、Ctrl + C / Ctrl + V / Ctrl + X 复制粘贴剪切、Ctrl + Z / Ctrl + Y 撤销重做。\n\n注意：输入法请切到英文半角。中文标点（；（）「」）在代码里是非法的，编译器第一眼就会报错。",
        "现在按 F6：编译并运行。\n\n但先看一眼代码 —— 这一版是我故意留了毛病的：一处让编译器看不懂，一处让它跑起来就崩。所以这次按 F6，它一定跑不起来。\u000c\n按下之后会发生什么：\n① 代码根本没跑 → 底部红色区立刻弹出来，写清第几行、第几列、错在哪；\n② 编辑器不会关，编译失败它得留在原地等你改。\n\n编译通过时才是另一套流程：编辑器自动关闭，代码开始执行，PrintLine 打在正前方（+X）那块半透明荧幕上；想停下就按 Alt + O 开编辑器，再按 F7。",
        "红色错误区（编译错误 CE）是编译器在告诉你「这句话我看不懂，代码根本没跑」。\n\n典型样子：\n    (7,14): error CS1002: 应输入 ;\n读法只有三步：\n① 先看括号里的第一个数字 —— 那是行号；\n② 眼睛跳到左侧行号列的第 7 行，对照那行找问题；\n③ 第二个数字是列号，表示大概位置，不用精确到字符。\u000c\n\n新手最常犯的三类错：漏分号 / 括号没成对（{ } 与 ( ) 各数一遍）/ 用了中文全角标点。报错很多条时别慌：永远先修第一条，后面的多半是它带出来的。\n\n改错我包了：点下面的「下一页」，我就替你把这处漏掉的分号补上 —— 红框随之消失，然后我们看它还能崩出什么。",
        "紫色窗口（运行时错误 RE）的意思完全不同：代码编译通过了、也确实跑起来了，跑到某一步才崩。\n\n现在按 F6：分号已经补好，红框不会再出现 —— 代码真的跑起来了，然后第一帧就崩在「除以 0」那一句，紫色窗口自动弹到你面前，里面写着异常类型和出错的行号。\u000c\n\n· F10：收起 / 展开底部红色区；\n· F11：收起 / 展开紫色窗；\n· 紫窗开着时按 F10 只会先关紫窗，再按一次才轮到红区；\n· 文字超出窗口就把鼠标放进去滚滚轮。\n\n常见原因：访问了不存在的方块、除以 0、数组越界、参数给错。排查顺序永远是先修红（不修代码根本不跑），再看紫（能跑起来才会冒出来）。",
        "几个每天都会用到的操作，先记下来：\n\n· 关闭编辑器：输入框没聚焦时按 Enter；Alt + O 也能开关；\n· F8：载入你以前保存过的代码文件（打包后的版本也可用 Ctrl + O）；\n· F9：把当前代码导出保存成文件（打包后的版本也可用 Ctrl + S）；\u000c\n· 屏幕最下面那行状态栏写着「语言 C#　行:N　列:M」，随时告诉你光标在哪；\n· 编辑器关着也照样运行：时间照走、炮台照打，只是你不能改代码而已。",
        "到这里，这台编辑器你已经会用了，整个流程就是一条链：\n\n打开（Enter / Alt + O）→ 点出光标 → 敲代码 → F6 运行 → 看红色 / 紫色报错改错 → 再按 F6。\n\n下一关是 C# 基础语法课：18 个任务，从「让炮台打印一句话」开始，一个语法点一个任务，边写边验证。\n准备好了就按下面的「开始 C# 语法课 ›」。"
    };

    private static readonly string[] BodiesEn =
    {
        "This is the turret code editor, the most important screen in the game: every line you write lives here, and the turret fires by following it.\n\n1) Press Enter (numpad Enter works too) to open it;\n2) Faster: Alt + O opens it directly, Ctrl + Shift + O works as well;\n3) While it is open the game pauses: time stops, blocks freeze, the cursor is released.\n\nTry it: press Enter now and I will take you to the next page automatically.",
        "With the editor open, the screen is six areas from top to bottom:\n\n1) Title bar: the editor name and status;\n2) The narrow left column = line numbers: when an error says line 3, count here;\n3) The big dark area = code area: every character you type goes here;\u000c\n4) Red zone at the bottom (CE, compile error): syntax is wrong and the code never ran;\n5) Small purple window at the bottom (RE, runtime error): the code ran and then crashed;\n6) The blue bar hugging the bottom edge = horizontal scrollbar for very long lines.\n\nBoth bottom zones can be collapsed; see the shortcuts on pages 5 and 6.",
        "Try it: click anywhere inside the code area with the left mouse button.\n\n- A blinking white vertical line appears: that is the caret, and typing inserts at it;\n- The line-number column numbers every line: an error on line 3 means count to 3;\n- Move the caret with arrow keys; Home jumps to line start, End to line end;\u000c\n- Select with left-drag, or Shift plus arrow keys;\n- Tab indents, Ctrl+C / Ctrl+V / Ctrl+X copy, paste and cut, Ctrl+Z / Ctrl+Y undo and redo.\n\nImportant: switch your IME to English half-width. Chinese punctuation such as full-width semicolons or brackets is illegal in code and the compiler rejects it immediately.",
        "Now press F6: compile and run.\n\nBut look at the code first - this version is faulty on purpose: one fault the compiler cannot parse, one that crashes at run time. So this F6 will not run.\u000c\nWhat happens then:\n1) Nothing runs at all -> the red zone at the bottom pops up and tells you the line, the column and what is wrong;\n2) The editor stays open: a failed compile must be fixed right there.\n\nA successful compile is the other path: the editor closes, the code starts running and every PrintLine shows on the translucent screen ahead (+X); to stop it, open the editor with Alt + O and press F7.",
        "The red zone (compile error, CE) means the compiler could not understand your code, so nothing ran at all.\n\nIt looks like:\n    (7,14): error CS1002: ; expected\nRead it in three steps:\n1) The first number in brackets is the line;\n2) Jump to that line in the line-number column and inspect it;\n3) The second number is the column, a rough position, not character exact.\u000c\n\nThe three classic beginner errors: missing semicolon, unbalanced brackets, full-width Chinese punctuation. When there are many errors, always fix the first one: most of the rest are caused by it.\n\nFixing is on me: press Next below and I will put that missing semicolon back - the red zone clears, and then we watch what it can still crash on.",
        "The purple window (runtime error, RE) is a different thing: the code compiled and did run, then crashed halfway.\n\nNow press F6: the semicolon is back, the red zone will not appear - the code really runs, then dies on the first frame at the divide-by-zero line, and the purple window pops up in front of you with the exception type and the failing line.\u000c\n\n- F10: collapse / expand the red zone;\n- F11: collapse / expand the purple window;\n- While the purple window is open, F10 closes it first; press again for the red zone;\n- When the text overflows, put the mouse inside and scroll the wheel.\n\nTypical causes: touching a block that does not exist, dividing by zero, index out of range, wrong arguments. Always debug in this order: red first (nothing runs until it is fixed), purple second (only appears once it runs).",
        "A few operations you will use every day:\n\n- Close the editor: press Enter while the input field is not focused; Alt + O toggles it too;\n- F8: load a previously saved code file (Ctrl + O in a built player);\n- F9: export the current code to a file (Ctrl + S in a built player);\u000c\n- The status bar at the very bottom shows \"Lang C#  Ln:N  Col:M\", so you always know where the caret is;\n- Your code keeps running with the editor closed; you just cannot edit it.",
        "That is the whole editor, and the loop is always the same:\n\nopen (Enter / Alt + O) -> click to place the caret -> type -> F6 to run -> read the red or purple errors and fix -> F6 again.\n\nNext is the C# grammar course: 18 tasks, starting from making the turret print a line, one grammar point per task.\nWhen you are ready, press the button below."
    };

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!Active)
            return;

        // 暂停（ESC 打开设置面板）期间不响应翻页 / 跳过按键，避免暂停时还把序章切走
        if (playerController == null)
            playerController = FindObjectOfType<PlayerCameraController>();
        if (playerController != null && playerController.Mode == PlayerCameraController.GameMode.Paused)
            return;

        // 先看当前页的动手门控：玩家一做到就自动翻页，不用再点按钮
        TickPageGate();

        // 翻屏键：PageDown / PageUp 不会被代码输入框吞掉（空格、方向键会被当成打字），
        // 所以放在下面那道「正在敲代码就不响应」的闸之前，玩家一边看编辑器一边也能翻后半屏。
        if (subPageCount > 1)
        {
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                NextPage();
                return;
            }
            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                PrevPage();
                return;
            }
        }

        // 编辑器打开且光标停在代码输入区时，这套翻页键会被玩家当成打字用（空格、S、方向键移动光标），
        // 因此一律不响应翻页与跳过 —— 否则边敲代码边误翻页，按一个 S 甚至直接跳过整关。
        // 想翻页请先关掉编辑器（Enter / Esc）、或直接用鼠标点卡片上的「上一页 / 下一页」按钮。
        if (editor != null && editor.IsOpen && editor.IsCodeInputFocused)
            return;

        // 键盘也能来回翻页（← 上一页 / → 下一页），不用鼠标点
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
            PrevPage();
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown)
                 || Input.GetKeyDown(KeyCode.Space))
            NextPage();
        // 最后一页不给 S：那一页的 Next 本来就是进语法课，再跳过等于白跳
        else if (Input.GetKeyDown(SkipKey) && CurrentPage < PageCount - 1)
            SkipPrologue(); // S：直接跳过序章进语法课（与「跳过序章」按钮同效）
    }

    // ================== 对外接口 ==================

    /// <summary>进入讲解关并从第 startIndex 页（0 基）开始；重复进入即重置阅读进度。</summary>
    public static void Begin(int startIndex)
    {
        EditorIntroTutorialController inst = EnsureInstance();
        if (inst == null)
            return;

        inst.BuildUI();
        inst.Active = true;
        inst.transitioning = false;
        inst.CurrentPage = Mathf.Clamp(startIndex, 0, PageCount - 1);
        inst.subPage = 0;
        inst.demoStage = -1;   // 每次进关都重新铺一遍演示代码，不继承上一局的残留
        if (inst.editor == null)
            inst.editor = FindObjectOfType<CodeEditorController>();
        // 第 6 页要靠「RE 真的崩一次」来演示紫窗：崩的那一帧编辑器是关着的（编译成功后它自己关了），
        // 所以让编辑器在 RE 发生时自动重开并展开紫窗；离开本关必须关掉，别把后续关卡也变成自动弹窗。
        if (inst.editor != null)
            inst.editor.AutoOpenOnRuntimeError = true;
        inst.OnPageEntered(); // 页号先落地，再按页决定要不要把编辑器叫回来
        if (inst.root != null && !inst.root.activeSelf)
            inst.root.SetActive(true);
        inst.Refresh();
        inst.EnterReadingMode(); // 读字期间释放鼠标，且鼠标不再转动背后场景的朝向
        Debug.Log("[CodeIntroTutorial] 编辑器入门开始：第 " + (inst.CurrentPage + 1) + "/" + PageCount + " 页。");
    }

    /// <summary>结束讲解（离开本关 / 跳去语法课时调用，可重复调用）。</summary>
    public static void End()
    {
        if (Instance == null)
            return;
        Instance.Active = false;
        if (Instance.editor != null)
            Instance.editor.AutoOpenOnRuntimeError = false; // 讲完就收：后面关卡不再自动弹紫窗
        Instance.ExitReadingMode(); // 交还鼠标、解除视角封锁（跳关 / 回主页的调用方随后还会再锁一次鼠标）
        if (Instance.root != null && Instance.root.activeSelf)
            Instance.root.SetActive(false);
    }

    private static EditorIntroTutorialController EnsureInstance()
    {
        if (Instance != null)
            return Instance;
        GameObject go = new GameObject("CodeIntroTutorial");
        return go.AddComponent<EditorIntroTutorialController>();
    }

    // ================== 翻页 ==================

    private void PrevPage()
    {
        if (!Active || transitioning)
            return;
        // 回退同样先回屏：在第 5 页第二屏按上一页，先回到前半段，再按才回第 4 页
        if (subPage > 0)
        {
            subPage--;
            Refresh();
            return;
        }
        if (CurrentPage <= 0)
            return;
        CurrentPage--;
        OnPageEntered();
        Refresh();
    }

    private void NextPage()
    {
        if (!Active || transitioning)
            return;
        // 操作门控：第 1 / 3 / 4 页要求玩家真的打开编辑器、点出光标、按下 F6。
        // 没做到时只把页脚提示换成「还差哪一步」，不硬推下一页 —— 否则玩家会一路点过去，什么都没动手。
        if (!PageGatePassed(CurrentPage))
        {
            Refresh();
            return;
        }
        // 长页拆屏后先翻屏内的后半段，翻到底才换页 —— 字号不用再为「一页塞完」而缩小
        if (subPage < subPageCount - 1)
        {
            subPage++;
            Refresh();
            return;
        }
        if (CurrentPage >= PageCount - 1)
        {
            GoGrammarCourse(); // 已经是最后一页：再按一次就进语法课
            return;
        }
        CurrentPage++;
        OnPageEntered();
        Refresh();
    }

    /// <summary>
    /// 每帧检查当前页的动手门控：玩家一做到就自动翻到下一页。
    /// 这样「按下 Enter 打开编辑器 → 立刻看到下一块内容」是连贯的，不会卡在「请点下一页」上。
    /// </summary>
    private void TickPageGate()
    {
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();

        if (CurrentPage == PageOpenEditor)
        {
            if (editor != null && editor.IsOpen)
                NextPage();
            return;
        }

        if (CurrentPage == PageClickCaret)
        {
            if (!caretClicked && editor != null && editor.IsOpen && editor.IsCodeInputFocused)
            {
                caretClicked = true;
                NextPage();
            }
            return;
        }

        if (CurrentPage == PageRunCode && !f6Pressed && Input.GetKeyDown(KeyCode.F6))
        {
            f6Pressed = true;
            NextPage();
        }

        // 第 6 页讲紫窗：只有亲眼看到紫窗真的弹出来，这一页才算看完（没看到就一直停在页脚提示上）
        // 兜底：玩家要是把「除以 0」那句自己改好了，紫窗永远不会弹 —— 那就按「按过 F6」放行，
        // 不能让一页永远翻不过去。
        if (CurrentPage == PageReZone)
        {
            EnsureDemoCode(1); // 兜底：编辑器晚一帧才打开也不会漏掉「补好分号」这一步
            bool changed = false;
            if (!reDemoSeen && editor != null && editor.IsReZoneVisible())
            {
                reDemoSeen = true;
                changed = true;
            }
            if (!f6Pressed && Input.GetKeyDown(KeyCode.F6))
            {
                f6Pressed = true;
                changed = true;
            }
            // 门控一过，Next 位置上那块「按 F6 看紫窗」的引导牌要立刻换回「续页 › / 下一页 ›」——
            // 不刷新的话玩家按完 F6 还得再点一下别的地方，按钮才「记得」自己该翻页了。
            if (changed)
                Refresh();
        }
    }

    /// <summary>当前页的操作门控是否已满足（未满足时「下一页」只换提示，不翻页）。</summary>
    private bool PageGatePassed(int page)
    {
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();
        if (page == PageOpenEditor)
            return editor != null && editor.IsOpen;
        if (page == PageClickCaret)
            return caretClicked;
        if (page == PageRunCode)
            return f6Pressed;
        if (page == PageReZone)
            return reDemoSeen || f6Pressed; // 紫窗真弹出来，或者玩家确实按过 F6，都算这一步做到了
        return true;
    }

    /// <summary>进入新页面时清空上一页的动手证据，避免「上一页点过一下，下一页就被自动跳过」。</summary>
    private void OnPageEntered()
    {
        caretClicked = false;
        f6Pressed = false;
        reDemoSeen = false;
        subPage = 0; // 换页必定从第一屏开始看

        // 第 5~7 页讲底部红区 / 紫色窗 / 常用键，得让玩家看着代码听讲：
        // 编辑器这时要是关着的（比如刚按完 F6、编辑器自己退了），就直接把它重新打开。
        if (CurrentPage < 1 || CurrentPage > 6)
            return;
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();
        if (editor != null && !editor.IsOpen)
            editor.Open();

        // 代码由讲解卡自己铺：第 2 页起就是「既有 CE 又有 RE」的带错版本，
        // 到第 6 页换成只补了分号的版本 —— 这样红框和紫窗都是玩家自己按 F6 撞出来的，不是演给他看的。
        EnsureDemoCode(CurrentPage >= PageReZone ? 1 : 0);
    }

    // ================== 演示代码（CE → RE 一条链）==================
    // 第 4~6 页要带玩家把「红框改掉 → 紫框冒出来」走一遍，所以代码由讲解卡自己铺：
    //   Bad   ：漏一个分号（CE，代码根本没跑）+ 一处除以 0（RE，等编译过了才会崩）；
    //   Fixed ：只把分号补上，除零原样留着 —— 这次能编译，跑起来第一帧就崩，紫窗弹出。
    private const string DemoCodeBadZh =
        "// 这段代码故意留了两处毛病：一处让编译器看不懂（CE），一处让它跑起来就崩（RE）\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== 弹药盘点 ==\");\n" +
        "\n" +
        "int ammo = 0\n" +                                   // ← 少了分号：编译错误（CE）
        "tower.PrintLine(\"待发弹药 {0} 发\", ammo);\n" +
        "\n" +
        "float rate = ammo / 0;   // ← 除以 0：编译能过，一跑就崩（RE）\n" +
        "tower.PrintLine(\"射速 {0}\", rate);\n";

    private const string DemoCodeBadEn =
        "// This code has two faults on purpose: one the compiler cannot parse (CE), one crashes at run time (RE)\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== Ammo check ==\");\n" +
        "\n" +
        "int ammo = 0\n" +                                   // <- missing semicolon: compile error (CE)
        "tower.PrintLine(\"Loaded {0} rounds\", ammo);\n" +
        "\n" +
        "float rate = ammo / 0;   // <- divide by zero: compiles fine, crashes on the first frame (RE)\n" +
        "tower.PrintLine(\"Fire rate {0}\", rate);\n";

    private const string DemoCodeFixedZh =
        "// 分号已经补好了，这一处会崩还是留着 —— 按 F6 看看它是怎么崩的\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== 弹药盘点 ==\");\n" +
        "\n" +
        "int ammo = 0;\n" +
        "tower.PrintLine(\"待发弹药 {0} 发\", ammo);\n" +
        "\n" +
        "float rate = ammo / 0;   // ← 除以 0：编译能过，一跑就崩（RE）\n" +
        "tower.PrintLine(\"射速 {0}\", rate);\n";

    private const string DemoCodeFixedEn =
        "// The semicolon is back; the crash is still there - press F6 and watch it die\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== Ammo check ==\");\n" +
        "\n" +
        "int ammo = 0;\n" +
        "tower.PrintLine(\"Loaded {0} rounds\", ammo);\n" +
        "\n" +
        "float rate = ammo / 0;   // <- divide by zero: compiles fine, crashes on the first frame (RE)\n" +
        "tower.PrintLine(\"Fire rate {0}\", rate);\n";

    private static string DemoCodeBad
    {
        get { return Localization.IsChinese ? DemoCodeBadZh : DemoCodeBadEn; }
    }

    private static string DemoCodeFixed
    {
        get { return Localization.IsChinese ? DemoCodeFixedZh : DemoCodeFixedEn; }
    }

    /// <summary>
    /// 把演示代码铺进编辑器（幂等：只在目标阶段比已铺阶段更靠后时才写）。
    /// 编辑器没打开时 inputField 还不存在，直接跳过；调用方每帧都会再试一次。
    /// </summary>
    private void EnsureDemoCode(int stage)
    {
        if (demoStage >= stage)
            return;
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();
        if (editor == null || !editor.IsOpen)
            return;
        demoStage = stage;
        editor.SetCodeForTutorial(stage <= 0 ? DemoCodeBad : DemoCodeFixed);
    }

    /// <summary>
    /// 第 2~7 页都要「代码 + 说明」同屏：卡片缩成贴右侧的窄卡，把代码区整块让出来。
    /// 第 5~7 页讲底部红区 / 紫色窗 / 常用键，离开代码根本没法讲，所以一并走窄卡；
    /// 只有第 1 页（编辑器还没开）和第 8 页（小结）用全屏大卡。
    /// </summary>
    private bool IsCompactPage(int page)
    {
        return page >= 1 && page <= 6;
    }

    /// <summary>
    /// 按当前页切换两套布局：全屏阅读卡 ↔ 贴右侧的窄卡。
    /// 窄卡模式下同时把全屏遮罩调到几乎透明并放弃吞点击，让鼠标能直接点进编辑器（点光标那页要用）。
    /// </summary>
    private void ApplyPageLayout(int page)
    {
        if (panelRt == null)
            return;

        bool compact = IsCompactPage(page);
        if (compact)
        {
            // 窄卡：贴屏幕右侧的竖条，把左边的行号列、标题栏、底部报错区整块让出来 ——
            // 玩家要一边读说明一边在编辑器上动手，卡片不能压在代码区正中间。
            // 窄卡：贴右侧，上下留白严格相等。
            // 之前用 anchor/pivot 都在 0.5 + anchoredPosition.y = 0 试图居中，但实测卡片比屏幕中心低约 30 单位，
            // 看着就是「歪」——改成拉伸锚点，用 offset 直接钉死上下边距，不受画布中心偏移影响。
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 1f);
            panelRt.pivot = new Vector2(1f, 0.5f);
            panelRt.offsetMin = new Vector2(-(CompactSideMargin + CompactWidth), CompactVertMargin);
            panelRt.offsetMax = new Vector2(-CompactSideMargin, -CompactVertMargin);
        }
        else
        {
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1680f, 900f);
            panelRt.anchoredPosition = Vector2.zero;
        }

        if (bodyRt != null)
        {
            float pad = compact ? CompactPadding : 56f;
            bodyRt.offsetMin = new Vector2(pad, compact ? CompactBodyBottom : 130f);
            bodyRt.offsetMax = new Vector2(-pad, compact ? -CompactBodyTop : -168f);

            // 页码行、页脚提示与正文用同一套左右内边距，否则窄卡里四行文字各差 16px，看着还是歪
            if (pageTagText != null)
            {
                RectTransform tag = pageTagText.rectTransform;
                tag.offsetMin = new Vector2(pad, tag.offsetMin.y);
                tag.offsetMax = new Vector2(-pad, tag.offsetMax.y);
            }
            if (footerText != null)
            {
                RectTransform ft = footerText.rectTransform;
                ft.anchoredPosition = new Vector2(pad, 88f);
                ft.sizeDelta = new Vector2(-pad * 2f, 34f);
            }
        }

        if (scrimImage != null)
        {
            scrimImage.color = UITheme.WithAlpha(UITheme.Void, compact ? 0.06f : 0.62f);
            scrimImage.raycastTarget = !compact;
        }
    }

    /// <summary>
    /// 门控页的 Next 位置显示「要按下哪个键」——把引导直接摆在玩家拇指该落的地方，
    /// 而不是让他对着一个写着「下一页」却点不动的按钮发愣。门控已完成 / 本页无门控时返回空串。
    /// </summary>
    private string BuildNextKeyHint(bool zh, int page)
    {
        if (PageGatePassed(page))
            return "";
        if (page == PageOpenEditor)
            return zh ? "按 Enter 打开 ›" : "Press Enter ›";
        if (page == PageClickCaret)
            return zh ? "点代码区 ›" : "Click code ›";
        if (page == PageRunCode)
            return zh ? "按 F6 运行 ›" : "Press F6 ›";
        if (page == PageReZone)
            return zh ? "按 F6 看紫窗 ›" : "Press F6 ›";
        return "";
    }

    /// <summary>页脚提示：门控没完成、或编辑器被关掉时，直接把「还差哪一步」写在页脚上。</summary>
    private string BuildFooterText(bool zh, int page)
    {
        if (!PageGatePassed(page))
        {
            if (page == PageOpenEditor)
                return zh
                    ? "按 Enter 打开编辑器（Alt + O 也行）· 打开后自动翻到下一页"
                    : "Press Enter to open the editor (Alt + O works too) - it advances by itself";
            if (page == PageClickCaret)
                return zh
                    ? "还差一步：鼠标左键在代码区点一下，看到白色光标闪动再继续"
                    : "One step left: left-click inside the code area and wait for the blinking white caret.";
            if (page == PageRunCode)
                return zh
                    ? "还差一步：按 F6 编译并运行一次（这次一定会报错，下一页正好讲红框）"
                    : "One step left: press F6 to compile and run once (it will fail - the next page explains the red zone).";
            if (page == PageReZone)
                return zh
                    ? "还差一步：按 F6 跑一次 —— 代码会崩，紫色窗口自动弹出来，这一页讲的就是它"
                    : "One step left: press F6 once - the code crashes, the purple window pops up, and that is this page.";
        }

        if (IsCompactPage(page) && editor != null && !editor.IsOpen)
            return zh
                ? "编辑器现在是关着的 · 按 Enter（或 Alt + O）重新打开，再继续看这一页"
                : "The editor is closed - press Enter (or Alt + O) to reopen it, then keep reading this page";

        if (subPageCount > 1 && subPage < subPageCount - 1)
            return zh
                ? "这页内容分两屏　·　点「续页 ›」或按 PageDown 看后半段"
                : "This page has two screens - press More or PageDown for the second half";

        if (page >= PageCount - 1)
            return zh
                ? "键盘 ← 回到上一页　·　准备好了就按下面的按钮进语法课"
                : "Left arrow goes back · press the button below when you are ready for the grammar course";

        return zh
            ? "键盘 ← → 也能翻页　·　按 S 跳过本关　·　读完最后一页即可进入 C# 语法课"
            : "Keyboard arrows also page through · S skips this stage · finish the last page to start the grammar course";
    }

    /// <summary>跳过本关（S 键或「跳过本关 (S)」按钮）：不看讲解、直接进 Level 1 - Part 4 语法课。</summary>
    private void SkipPrologue()
    {
        if (!Active || transitioning)
            return;
        GoGrammarCourse();
    }

    // ================== 阅读态：释放鼠标 + 冻结视角 ==================

    /// <summary>
    /// 进入阅读态：把鼠标放出来（可以自由点按钮 / 翻页），同时通知 PlayerCameraController
    /// 进入「纯阅读」封锁——鼠标再怎么动也不会转动背后场景的朝向，WASD / E / Q / F1 一并失效。
    /// 注意保留 ESC 暂停（PlayerCameraController 只在 reading 封锁下跳过视角与移动，不吞暂停键），
    /// 玩家读的时候随时可以按 ESC 唤出暂停面板退回主页。
    /// </summary>
    private void EnterReadingMode()
    {
        if (playerController == null)
            playerController = FindObjectOfType<PlayerCameraController>();
        if (playerController != null)
            playerController.SetViewBlocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>退出阅读态：解除视角封锁、把鼠标重新锁回（后续跳关 / 回主页的调用方还会再锁一次，幂等）。</summary>
    private void ExitReadingMode()
    {
        if (playerController == null)
            playerController = FindObjectOfType<PlayerCameraController>();
        if (playerController != null)
            playerController.SetViewBlocked(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = true;
    }

    /// <summary>
    /// 进入 Level 1 - Part 4 语法课。过渡方式与 Level 1 - Part 1 → Part 2 完全一致：
    /// 黑幕淡出到全黑 → 在全黑那一帧执行 LevelDirector.JumpTo（内部会 End() 收尾并清场）→ 黑幕淡入，
    /// 避免序章面板被硬切掉、语法课突然弹出的生硬感。过渡期间忽略键盘与按钮，防止重复触发。
    /// </summary>
    private void GoGrammarCourse()
    {
        if (transitioning)
            return;
        transitioning = true;

        if (prevBtn != null)
            prevBtn.interactable = false;
        if (nextBtn != null)
            nextBtn.interactable = false;

        ScreenFade.PlayTransition(FadeOutSeconds, FadeInSeconds, delegate
        {
            LevelDirector.JumpTo(1, 4, 1); // Level 1 - Part 4 语法课，从第 1 个任务开始
        });
    }

    // ================== 文案刷新 ==================

    private void Refresh()
    {
        bool zh = Localization.IsChinese;
        int page = Mathf.Clamp(CurrentPage, 0, PageCount - 1);

        // 长页按 BodyBreak 切成两屏（第 2/3/4/5/6/7 页都拆了）：先把屏号收进合法区间，下面所有文案都取 chunks[subPage]。
        string[] chunks = (zh ? BodiesZh[page] : BodiesEn[page]).Split(new[] { BodyBreak }, System.StringSplitOptions.None);
        subPageCount = Mathf.Max(1, chunks.Length);
        subPage = Mathf.Clamp(subPage, 0, subPageCount - 1);

        ApplyPageLayout(page); // 先定布局（面板尺寸 / 正文边距 / 遮罩），再填文案与字号

        // 页码行带上屏号（编辑器入门 5/8　·　1/2），玩家一眼知道这一页还有后半段
        SetLabel(pageTagText, (zh ? "编辑器入门 " : "Editor guide ") + (page + 1) + "/" + PageCount
            + (subPageCount > 1 ? "\u3000·\u3000" + (subPage + 1) + "/" + subPageCount : ""));

        // 窄卡只有 700 宽，标题继续用 44 号会横向冲出卡片右边界（英文标题尤其明显），
        // 所以窄卡换小字号 + 允许两行 + Best Fit 兜底，全屏仍用 44 号单行。
        if (titleText != null)
        {
            bool compactTitle = IsCompactPage(page);
            int titleSize = compactTitle ? CompactTitleFont : 44;
            titleText.fontSize = titleSize;
            titleText.resizeTextMaxSize = titleSize;
            titleText.resizeTextForBestFit = true;
            RectTransform trt = titleText.rectTransform;
            float titlePad = compactTitle ? CompactPadding : 56f;
            trt.sizeDelta = new Vector2(-titlePad * 2f, compactTitle ? CompactTitleHeight : 66f);
        }

        SetLabel(titleText, zh ? TitlesZh[page] : TitlesEn[page]);
        bool compact = IsCompactPage(page);
        if (bodyText != null)
            bodyText.fontSize = compact ? FitCompactBodyFont(page) : bodyFontSize; // 先定字号，SetLabel 才会按该字号加载对应字形
        SetLabel(bodyText, chunks[subPage]);
        SetLabel(footerText, BuildFooterText(zh, page));

        SetLabel(prevLabel, zh ? "‹ 上一页" : "‹ Back");
        // 最后一页的「下一页」变成入口按钮，明确告诉玩家下一步去哪
        // Next 按钮的位置就是「下一步该干什么」的引导牌：
        //   · 这一页有动手门控而玩家还没做 → 按钮上直接写要按的键（按 F6 运行 / 按 F6 看紫窗），
        //     点了不会硬翻页（NextPage 里门控拦住），但玩家不用去猜「为什么按了没反应」；
        //   · 门控过了还有后半屏 → 「续页 ›」；
        //   · 都过了 → 「下一页 ›」，末页变「开始 C# 语法课 ›」。
        bool moreChunk = subPage < subPageCount - 1;
        string keyHint = BuildNextKeyHint(zh, page);
        SetLabel(nextLabel, keyHint.Length > 0
            ? keyHint
            : (moreChunk
                ? (zh ? "续页 ›" : "More ›")
                : (page >= PageCount - 1
                    ? (zh ? "开始 C# 语法课 ›" : "Start the course ›")
                    : (zh ? "下一页 ›" : "Next ›"))));
        // 窄卡只有 700 宽，跳过按钮再带「(S)」就会被挤成两行 —— 键位提示交给页脚，按钮上只留动作。
        SetLabel(skipLabel, compact
            ? (zh ? "跳过本关" : "Skip stage")
            : (zh ? "跳过本关 (S)" : "Skip stage (S)"));

        // 门控页不再「把 Next 藏起来」：按钮留在原地，但文案换成「要按哪个键」（见 BuildNextKeyHint）。
        // 藏按钮会让玩家以为卡片出问题了；留一个写着「下一页」却点不动的按钮又会诱导他一路点过去。
        // 写成「按 Enter 打开 › / 点代码区 › / 按 F6 运行 ›」——位置对、指头过去就照做了。
        bool hideNext = false;
        if (nextRt != null)
            nextRt.gameObject.SetActive(!hideNext);

        // 末页的「跳过本关」等于白跳：这一页的 Next 本来就是进语法课，留着只会让人以为得跳着走。
        if (skipRt != null)
            skipRt.gameObject.SetActive(page < PageCount - 1);

        if (prevBtn != null)
            prevBtn.interactable = page > 0;

        // 上一次「去上 C# 语法课」把按钮禁用了，第二次进入本关必须重新放开，否则 Next 永远点不动。
        if (nextBtn != null)
            nextBtn.interactable = true;

        // 文案换了（「下一页 ›」↔「开始语法课 ›」）就按新文案重新定宽并整组居中，
        // 否则末页英文「Start the course ›」会被原来 210 宽顶出按钮边框。
        LayoutNavButtons();
    }

    // ================== UI 构建 ==================

    private void BuildUI()
    {
        if (root != null)
            return;

        GameObject canvasGo = new GameObject("CodeIntroCanvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210; // 与任务卡同层，压在 HUD / 编辑器之上
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UIFitScreen>();
        canvasGo.AddComponent<GraphicRaycaster>();
        root = canvasGo;

        // 全屏轻遮罩：把 3D 场景压暗一点，正文更好读
        RectTransform scrimRt = CreateRect("Scrim", canvasGo.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(RefWidth + 400f, RefHeight + 400f), new Vector2(0.5f, 0.5f));
        Image scrim = scrimRt.gameObject.AddComponent<Image>();
        scrim.sprite = WhiteSprite();
        scrim.color = UITheme.WithAlpha(UITheme.Void, 0.62f);
        scrim.raycastTarget = true; // 吞掉点击，避免误触背后的界面（顶部窄卡模式下由 ApplyPageLayout 关掉）
        scrimImage = scrim;

        // 阅读面板（高度由 820 → 900：换字体后正文行高 +9.6%，正文区从 540 撑到 620，
        // 配合正文自适应字号可让 4 页都完整放得下，同时仍在 1080 参考高度内留出上下各 90 的边距）
        panelRt = CreateRect("IntroPanel", canvasGo.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1680f, 900f), new Vector2(0.5f, 0.5f));
        panelRoot = panelRt.gameObject;
        Image panelImg = panelRoot.AddComponent<Image>();
        panelImg.sprite = WhiteSprite();
        panelImg.color = UITheme.WithAlpha(UITheme.PanelDeep, 0.96f);
        panelImg.raycastTarget = true;

        // 顶部强调线
        RectTransform lineRt = CreateRect("TopLine", panelRt, new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(0f, 3f), new Vector2(0f, 1f));
        lineRt.anchorMin = new Vector2(0f, 1f);
        lineRt.anchorMax = new Vector2(1f, 1f);
        lineRt.offsetMin = new Vector2(0f, -3f);
        lineRt.offsetMax = Vector2.zero;
        Image lineImg = lineRt.gameObject.AddComponent<Image>();
        lineImg.sprite = WhiteSprite();
        lineImg.color = UITheme.WithAlpha(UITheme.Accent, 0.55f);
        lineImg.raycastTarget = false;

        // 页码标签
        pageTagText = CreateText("PageTag", panelRt, "", 26, UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform tagRt = pageTagText.rectTransform;
        tagRt.anchorMin = new Vector2(0f, 1f);
        tagRt.anchorMax = new Vector2(1f, 1f);
        tagRt.pivot = new Vector2(0.5f, 1f);
        tagRt.anchoredPosition = new Vector2(0f, -34f);
        tagRt.offsetMin = new Vector2(56f, tagRt.offsetMin.y);
        tagRt.offsetMax = new Vector2(-56f, tagRt.offsetMax.y);
        tagRt.sizeDelta = new Vector2(-112f, 34f);

        // 页标题
        titleText = CreateText("Title", panelRt, "", 44, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        // CreateText 默认是 Overflow（不换行），标题会直接冲出卡片边界 —— 这里改成换行 + 截断 + 缩字兜底
        titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        titleText.verticalOverflow = VerticalWrapMode.Truncate;
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 22;
        titleText.resizeTextMaxSize = 44;
        RectTransform titleRt = titleText.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -74f);
        titleRt.sizeDelta = new Vector2(-112f, 56f);

        // 正文
        bodyText = CreateText("Body", panelRt, "", 32, UITheme.TextBody, TextAnchor.UpperLeft);
        bodyRt = bodyText.rectTransform;
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.offsetMin = new Vector2(56f, 130f);
        bodyRt.offsetMax = new Vector2(-56f, -150f);
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        bodyText.lineSpacing = 1.35f;

        // 正文自适应字号：正文框是固定高度 + Overflow，字号写死 26 时第 2~4 页会直接压到页脚提示与按钮上、
        // 并越过面板下边界（详见 FitBodyFontSize 注释）。这里在正文框真实高度内挑一个「4 页都放得下」的字号。
        bodyFontSize = FitBodyFontSize();
        bodyText.fontSize = bodyFontSize;
        Debug.Log("[CodeIntroTutorial] 正文自适应字号：" + bodyFontSize + "（正文框 "
            + Mathf.RoundToInt(bodyText.rectTransform.rect.width) + "×" + Mathf.RoundToInt(bodyText.rectTransform.rect.height) + "）");

        // 操作提示（键盘 ← → 也能翻页）：放在右下按钮组【上方】一行，避免与按钮横向重叠压住文字
        footerText = CreateText("Footer", panelRt, "", 22, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform footerRt = footerText.rectTransform;
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0f, 0f);
        footerRt.anchoredPosition = new Vector2(56f, 88f);   // 按钮组顶边约 71，这里摆在 88 上方，两者不重叠
        footerRt.sizeDelta = new Vector2(-112f, 34f);
        footerText.horizontalOverflow = HorizontalWrapMode.Wrap;
        footerText.resizeTextForBestFit = true;
        footerText.resizeTextMinSize = 14;
        footerText.resizeTextMaxSize = 22;

        // 右下按钮组：‹ 上一页 / 下一页 › / 跳过序章
        // 三个按钮的间距必须一致：之前是各自写死 x 坐标（-330 / -120 / 150），
        // 结果 上一页↔下一页只隔 15px、下一页↔跳过序章只隔 5px，三个挤成一坨，右边却空出一大截，观感很不协调。
        // 现在统一按 NavGap 等距排布，并让整组在面板内水平居中。
        // 具体横向位置与宽度交给 LayoutNavButtons() 统一算：先建出来，位置随后置。
        prevBtn = CreateButton(panelRt, "PrevBtn", "‹ 上一页",
            new Vector2(0.5f, 0f), new Vector2(0f, NavBtnY), new Vector2(PrevW, NavBtnH));
        prevBtn.onClick.AddListener(PrevPage);
        prevLabel = prevBtn.GetComponentInChildren<Text>();
        prevRt = prevBtn.GetComponent<RectTransform>();

        nextBtn = CreateButton(panelRt, "NextBtn", "下一页 ›",
            new Vector2(0.5f, 0f), new Vector2(0f, NavBtnY), new Vector2(NextWMin, NavBtnH));
        nextBtn.onClick.AddListener(NextPage);
        nextLabel = nextBtn.GetComponentInChildren<Text>();
        nextRt = nextBtn.GetComponent<RectTransform>();
        if (nextLabel != null)
        {
            // 兜底：万一极端分辨率 / 字体回退后动态宽度仍不够，先换行再缩字，绝不顶出边框
            nextLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            nextLabel.verticalOverflow = VerticalWrapMode.Truncate;
            nextLabel.resizeTextForBestFit = true;
            nextLabel.resizeTextMinSize = 14;
            nextLabel.resizeTextMaxSize = 24;
        }

        // 横宽按中英文两套文案的最长者给足：「Skip prologue (S)」比「跳过本关 (S)」宽得多，
        // 原来的窄按钮会让英文顶出右边界，这里直接加宽到 320；
        // 同时给标签开自动缩字兜底（Wrap + Best Fit），再长的文案也只缩字、不越界。
        Button skipBtn = CreateButton(panelRt, "SkipBtn", "跳过本关 (S)",
            new Vector2(0.5f, 0f), new Vector2(0f, NavBtnY), new Vector2(SkipW, NavBtnH));
        skipBtn.onClick.AddListener(GoGrammarCourse);
        skipRt = skipBtn.GetComponent<RectTransform>();
        skipLabel = skipBtn.GetComponentInChildren<Text>();
        if (skipLabel != null)
        {
            // Unity 的 Best Fit 需要横向换行模式才会真正生效：文字先尝试换行，仍放不下再按 min→max 缩字号
            skipLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            skipLabel.verticalOverflow = VerticalWrapMode.Truncate;
            skipLabel.resizeTextForBestFit = true;
            skipLabel.resizeTextMinSize = 14;
            skipLabel.resizeTextMaxSize = 24;
        }

        LayoutNavButtons(); // 先按「下一页 ›」把三颗按钮排好，避免显示瞬间挤在面板中线上
        root.SetActive(false);
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        RectTransform rt = CreateRect(name, parent, anchor, pos, size, new Vector2(0.5f, 0.5f));
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.WithAlpha(UITheme.Accent, 0.16f);
        bg.raycastTarget = true;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;

        Text t = CreateText("Label", rt, label, 24, UITheme.TextPrimary, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform);
        return btn;
    }

    /// <summary>
    /// 底部按钮组重排：三个按钮间距统一（NavGap），整组在面板内水平居中。
    /// 「下一页」的宽度按当前文案的实测自然宽度给，并夹在 [NextWMin, NextWMax] 之间——
    /// 末页文案「开始语法课 › / Start the course ›」比「下一页 › / Next ›」宽一大截，
    /// 固定 210 宽会把文字顶出按钮边框（英文最明显）。
    /// </summary>
    private void LayoutNavButtons()
    {
        if (prevRt == null || nextRt == null || skipRt == null)
            return;

        // 窄卡只有 700 宽，而全屏那套宽度加起来 180 + 210 + 320 + 56 = 766，整组会横向捅出卡片两边，
        // 所以面板一窄就换紧凑尺寸（总量压在 560 上下），保证三颗按钮始终落在卡片内边距里。
        float panelW = panelRt != null ? panelRt.rect.width : RefWidth;
        bool tight = panelW < 900f;

        float gap = tight ? 10f : NavGap;
        float prevW = tight ? 118f : PrevW;
        float skipW = tight ? 196f : SkipW;
        float nextW = Mathf.Clamp(
            MeasureNavLabelWidth() + (tight ? 26f : NavLabelPadding),
            tight ? 126f : NextWMin,
            tight ? 252f : NextWMax); // 窄卡上限放宽到 252：「按 F6 看紫窗 ›」这类引导牌比「下一页 ›」长得多

        // 第 1 页不出「下一页」：隐藏的按钮不占宽度，剩下两颗重新居中，免得右边空出一个槽。
        bool showPrev = prevRt.gameObject.activeSelf;
        bool showNext = nextRt.gameObject.activeSelf;
        bool showSkip = skipRt.gameObject.activeSelf;

        float groupW = (showPrev ? prevW + gap : 0f)
                     + (showNext ? nextW + gap : 0f)
                     + (showSkip ? skipW : 0f);
        float cursor = -groupW * 0.5f; // 整组左边界（相对面板中心）

        if (showPrev)
        {
            PlaceNavButton(prevRt, cursor, prevW);
            cursor += prevW + gap;
        }
        if (showNext)
        {
            PlaceNavButton(nextRt, cursor, nextW);
            cursor += nextW + gap;
        }
        if (showSkip)
            PlaceNavButton(skipRt, cursor, skipW);
    }

    /// <summary>
    /// 窄卡正文按「当前页中英文都框得住」自适应字号（18 起往下试到 14）：
    /// 第 2 页英文有 17 行，18 号会顶穿页脚，靠固定字号迟早再打架。
    /// </summary>
    private int FitCompactBodyFont(int page)
    {
        if (bodyText == null)
            return CompactBodyFont;
        float limit = bodyText.rectTransform.rect.height - BodyFitPadding;
        if (limit <= 1f)
            return CompactBodyFont;

        page = Mathf.Clamp(page, 0, PageCount - 1);
        for (int size = CompactBodyFont; size > CompactBodyFontMin; size--)
        {
            if (ChunksFit(BodiesZh[page], size, limit) && ChunksFit(BodiesEn[page], size, limit))
                return size;
        }
        return CompactBodyFontMin;
    }

    private static void PlaceNavButton(RectTransform rt, float leftOffset, float width)
    {
        rt.sizeDelta = new Vector2(width, NavBtnH);
        rt.anchoredPosition = new Vector2(leftOffset + width * 0.5f, NavBtnY);
    }

    /// <summary>
    /// 量「下一页」标签当前文案的单行自然宽度（按标签自身字号 + 已加载字体）。
    /// 刻意关掉 Best Fit 再量，避免「按钮太窄 → 自动缩字 → 量出来更窄」的死循环，量出来的永远是原字号宽度。
    /// </summary>
    private float MeasureNavLabelWidth()
    {
        if (nextLabel == null)
            return 0f;

        string content = nextLabel.text;
        if (string.IsNullOrEmpty(content))
            return 0f;

        TextGenerationSettings settings = nextLabel.GetGenerationSettings(new Vector2(NextWMax, 0f));
        settings.horizontalOverflow = HorizontalWrapMode.Overflow;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.resizeTextForBestFit = false;
        settings.fontSize = nextLabel.fontSize;

        TextGenerator generator = nextLabel.cachedTextGeneratorForLayout;
        float scale = nextLabel.pixelsPerUnit > 0.01f ? nextLabel.pixelsPerUnit : 1f;
        return generator.GetPreferredWidth(content, settings) / scale;
    }

    private static void SetLabel(Text t, string content)
    {
        if (t == null)
            return;
        t.text = content;
        t.font = UIFontHelper.LoadForText(content, t.fontSize);
    }

    private static Sprite whiteSprite;

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null)
            return whiteSprite;
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
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

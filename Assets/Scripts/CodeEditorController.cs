using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.CodeDom.Compiler;
using System.Reflection;
using Microsoft.CSharp;

/// <summary>
/// 代码编辑器（炮台代码编辑器）：
///   - 按 Enter（Return / KeypadEnter）打开 / 关闭；按 Alt+O / Ctrl+Shift+O 也可一键打开（组合键，不受 Enter 抑制影响）；
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
    [Tooltip("切换编辑器（Enter）；Alt+O / Ctrl+Shift+O 可一键打开")]
    public KeyCode openKey = KeyCode.Return;
    [Tooltip("小键盘 Enter 等效键")]
    public KeyCode openKeyAlt = KeyCode.KeypadEnter;

    [Header("UI")]
    [Tooltip("标题字号")]
    public int titleFontSize = 20;
    [Tooltip("代码区字号")]
    public int codeFontSize = DefaultCodeFontSize;
    [Tooltip("错误列表字号")]
    public int errorFontSize = DefaultErrorFontSize;
    [Tooltip("状态栏字号")]
    public int statusFontSize = 14;
    [Tooltip("全屏背景色（#1E1E1E alpha 0.97）")]
    public Color bgColor = UITheme.WithAlpha(UITheme.CodeBg, 0.97f);
    [Tooltip("行号列底色（#1B1B1B）")]
    public Color lineBgColor = UITheme.CodeLineBg;
    [Tooltip("行号颜色（#858585）")]
    public Color lineNumberColor = UITheme.CodeGutter;
    [Tooltip("错误文字颜色（红）")]
    public Color errorColor = UITheme.Alert;

    [Header("编辑体验")]
    [Tooltip("代码行距倍数（以字体实测行高为基准，1 = 字体默认行距；小于 1 更紧凑，如 0.92）")]
    public float lineHeightFactor = 1.0f;
    [Tooltip("原生光标闪烁间隔（秒，>0 表示闪烁）")]
    public float caretBlinkRate = 0.85f;

    [Header("分段编辑器（超长代码）")]
    [Tooltip("全文字符数达到该值即启用分段（低于它只建 1 块，与原单块编辑完全等价）")]
    public int segEnableThreshold = 15000;
    [Tooltip("每个分块的字符预算（必须明显小于 uGUI Text 单层约 16383 字符的顶点上限，留出余量）")]
    public int segCharBudget = 14000;

    // 行高实测缓存：布局行高必须与 Text 真实行距严格一致，否则随行号累积会造成行号与代码错位、
    // 分段块之间出现成片空白（约每行 1px 误差，400 行后就是一片空行）。
    private float measuredLineHeight = -1f;
    private int measuredLineHeightFontSize = -1;
    private float measuredLineHeightFactor = -1f;
    private string lastLayoutDiagSig = string.Empty;   // 分段布局诊断：上次打印时的「各块行数」组合指纹

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
    // ---- 底部错误区（高度自适应 + 自身可滚动：报错内容永远整段留在屏幕内，不会被挤出画面下沿） ----
    private RectTransform errorZoneRt;    // 底部错误 / 状态区底板（高度随报错行数增长）
    private RectTransform errorMaskRt;    // 错误文字可视区（RectMask2D 裁剪，滚轮在此翻页）
    private float errorContentHeight;     // 错误文字实测内容高度（排版后写入，供滚动范围使用）
    private float errorScrollY;           // 错误文字上移量（0 = 顶部对齐；内容高于可视区时才有值）
    private const float ErrorZoneMinHeight = 160f;   // 空错误 / 单行错误时的默认高度（与旧版一致）
    private const float ErrorSplitBarHeight = 2f;    // 错误区上缘分界条「视觉」粗细（只画 3px，拖拽命中区另有一层更高的透明层）
    private const float ErrorSplitHitHeight = 18f;   // 分界条拖拽命中区高度：细条只有 4px，命中带要够厚才抓得住
    private const float ErrorSplitInputSlack = 4f;   // 命中带在视觉红条外再放宽的像素（自管拖动判定同样吃这个容差）
    // 红色细条「上方」再叠一条提示条：下缘贴住 3px 细条，细条负责画分界，这条负责一眼可见 + 好按住（整条都能上下拖）。
    // 粗细由 10px 收细到 6px（原先与底部横条同粗细、显得太厚），命中区高度不受影响，照样好抓。
    private const float ErrorSplitGripHeight = 2f;
    // ---- 运行时错误（RE）独立窗口：紫色，与红色编译错误（CE）窗口并行存在、互不覆盖 ----
    // 结构与红色窗口同构：顶部紫色分隔条（细条 + 提示条，仅作视觉分隔，不支持拖拽）+ 文字视口 + 右侧竖滚动条；
    // 没有 RE 时整块收起（高度 0、全部隐藏），完全不占画面。
    private const float ReZoneMinHeight = 64f;      // 自适应下限：至少放得下最低字号的一行 RE
    // 紫窗高度自适应字号：窗口高度 = 上下留白（ReZoneChromeHeight）+ 内容实测高度，
    // 字号调大窗就变高、调小窗就变矮；内容再多交给滚轮翻页，不再有固定高度、也不再支持拖拽改高度。
    private const float ReSplitLineHeight = ReSplitGripHeight + ReSplitBarHeight; // 紫线本体总高（提示条 + 细条 = 4px）
    private const float ReSplitBreath = 6f;         // 紫线下沿到文字的呼吸位（ReTopInset 里最后的 +6）
    // 紫线上方那 6px（原本是给蓝条让缝的空白）也归紫线管辖：只做「悬停高亮 + 拉高判定」的归属区，
    // 颜色始终是淡淡一层紫（0.10 → 高亮 0.30），绝不画成和紫线一样的实心色。
    private const float ReSplitTopPadAlpha = 0.10f;
    private const float ReSplitTopPadHoverAlpha = 0.30f;
    private const float ErrorSplitTopPadAlpha = 0.10f;      // 红线上方归属区的常态淡红：只是薄薄一层，绝不当实线用
    private const float ErrorSplitTopPadHoverAlpha = 0.30f;  // 鼠标压上去后的提亮上限：仍保持淡色，只比常态更明显一点
    // 折中口径：紫线以上 6px 缝隙已改为透明（不算窗内留白），可见窗顶就是紫线下沿，
    // 所以底部留白 = 呼吸位 6 + 紫线高度的一半 2 = 8，让文字在「线到底边」之间视觉居中。
    private const float ReBottomInset = ReSplitBreath + ReSplitLineHeight * 0.5f;
    private const float ReZoneChromeHeight = ReTopInset + ReBottomInset; // 窗口 rect 总 chrome（含顶部 6px 透明缝）
    private const float ReSplitBarHeight = 2f;      // 紫色分界细条，与红色细条同粗
    private const float ReSplitGripHeight = 2f;     // 紫色提示条，比底部蓝条更细，只作视觉分隔（不可拖）
    private const float ReSplitHitHeight = 12f;     // 原拖拽命中区的尺寸（已停用 raycast，保留布局位避免位移）
    private const float ReSplitTopGap = 6f;         // 紫条整体从窗口顶边下移 6px：与上方蓝色横向滚动条彻底分开，互不抢事件
    private const float ReTopInset = ReSplitTopGap + ReSplitGripHeight + ReSplitBarHeight + ReSplitBreath; // 窗口顶边到文字 = 缝隙 + 紫线 + 呼吸位
    private int reFontSize = 15;             // 紫窗字号（Ctrl+滚轮指向紫窗时可调）
    private const int ReFontSizeMin = 14;    // 紫窗字号下限：与状态栏「Lang C#」提示同号（statusFontSize = 14），再小看不清
    private const int ReFontSizeMax = 32;    // 紫窗字号上限
    private const int DefaultReFontSize = 15;
    private const string ReFontSizePrefKey = "atv.codeEditor.reFontSize";

    // 分隔条悬停高亮：鼠标指到红条 / 紫条就把整条提亮，提示「这里能拖」
    private Image errorSplitBarImg;          // 红条细条（3px）
    private Image errorSplitGripImg;         // 红条粗提示条
    private Image reSplitBarImg;             // 紫条细条
    private Image reSplitGripImg;            // 紫条粗提示条
    private RectTransform reSplitTopPadRt;   // 紫线上方的归属区（6px，只做高亮与拉高判定）
    private Image reSplitTopPadImg;
    private RectTransform errorSplitTopPadRt; // 红线上方的归属区（6px，只做高亮与拉高判定，与紫窗同一套玩法）
    private Image errorSplitTopPadImg;
    private readonly Vector3[] splitCorners = new Vector3[4]; // 分隔条悬停判定复用的角点缓存，避免每帧 GC
    private float errorSplitHoverT;          // 红条提亮程度 0~1（平滑过渡）
    private float reSplitHoverT;             // 紫条提亮程度 0~1（平滑过渡）
    private RectTransform reZoneRt;
    private RectTransform reMaskRt;
    private Text reListText;
    private RectTransform reScrollTrackRt;
    private RectTransform reScrollHandleRt;
    private RectTransform reSplitBarRt;
    private RectTransform reSplitGripRt;
    private RectTransform reSplitHitRt;
    private float reContentHeight;
    private float reScrollY;
    private bool reZoneCollapsed = true;
    private const float ErrorSplitTopGap = 6f; // 红条从窗口顶边下移 6px：与上方蓝色横向滚动条留缝，二者互不抢指针
    private const float ErrorTopInset = ErrorSplitTopGap + ErrorSplitGripHeight + ErrorSplitBarHeight + 6f; // 错误视口顶部内缩 = 提示条 + 分界条 + 6px 呼吸位
    private const float SeamMergeThreshold = 16f;    // 任一侧被压到这么矮，就把该侧的分界条收掉（两条合成一条）
    private const int ErrorFontMax = 18;             // 报错字号自动适配的起点上限（默认字号，已整体上调；玩家可调区间仍是 ErrorFontMin(14) ~ ErrorFontZoomMax(34)）
    private const int ErrorFontMin = 14;             // 报错字号下限：与状态栏「Lang C#」提示同号（statusFontSize = 14），缩到这么小还放不下就交给滚轮
    private const int ErrorFontZoomMax = 34;         // Ctrl+滚轮手动放大上限（手动缩放后不再自动缩字号）
    private RectTransform lineRt;         // 行号内容（高度随行数增长，滚动偏移同步）
    private RectTransform codeRt;         // 代码内容（高度随行数增长，滚动偏移 y ≤ 0）
    private RectTransform vScrollTrackRt;  // 右侧竖滚动条轨道
    private RectTransform vScrollHandleRt; // 右侧竖滚动条滑块
    private RectTransform hScrollTrackRt;  // 底部横滚动条轨道
    private RectTransform hScrollHandleRt; // 底部横滚动条滑块
    private RectTransform errorScrollTrackRt;  // 错误区（红）竖滚动条轨道
    private RectTransform errorScrollHandleRt; // 错误区（红）竖滚动条滑块
    private RectTransform errorSplitBarRt;     // 错误区上缘红色分隔条（3px 视觉细条，不参与点击）
    private RectTransform errorSplitGripRt;    // 分隔条上方的红色提示条（6px，整条可拖，下缘贴住 3px 细条）
    private RectTransform errorSplitHitRt;     // 分隔条透明拖拽命中区（9px 高，真正承接上下拖动）
    private bool errorFontScaled;              // 玩家用 Ctrl+滚轮手动调过报错字号（true 时不再自动缩字号适配）
    private float errorZoneManualHeight;       // 用户拖红条设定的错误区高度（0 = 未手动调过，跟随内容自适应）
    private float errorSplitStartMouseY;       // 分隔条拖动起点（屏幕像素）
    private float errorSplitStartHeight;       // 分隔条拖动起始错误区高度（UI 单位）
    private bool errorSplitInputDrag;          // 自管拖动中：按下红条即进入，不依赖 EventSystem 的拖动阈值
    private float errorSplitInputDragTime;     // 自管拖动的按下时刻：万一漏掉抬起事件，超时自动松手
    private bool errorZoneCollapsed;           // 错误区被压到底、与底部横条合并后收起；有新报错才自动弹回
    private readonly List<string> undoList = new List<string>();  // 撤销栈（自实现，兜底原生）
    private readonly List<string> redoList = new List<string>();  // 重做栈
    private string lastCommittedText = "";
    private const int MaxUndoSteps = 100;
    private const int CodeFontSizeMin = 12;   // Ctrl+滚轮缩放的字体下限
    private const int CodeFontSizeMax = 40;   // Ctrl+滚轮缩放的字体上限

    // ---- 字号默认值与存档键 ----
    // 默认字号整体上调（代码 20 → 24、报错 15 → 18），玩家可调区间刻意保持原样不动：
    //   代码 12 ~ 40、报错 11 ~ 34（ErrorFontMin / ErrorFontMax / ErrorFontZoomMax / CodeFontSizeMin / CodeFontSizeMax 全部沿用旧值）。
    // 玩家用 Ctrl+滚轮调过的字号会写进 PlayerPrefs，重启游戏 / 重开编辑器后原样恢复。
    private const int DefaultCodeFontSize = 24;
    private const int DefaultErrorFontSize = 18;
    private const string CodeFontSizePrefKey = "atv.codeEditor.fontSize";
    private const string ErrorFontSizePrefKey = "atv.codeEditor.errorFontSize";
    private float codeContentHeight;          // 代码区内容高度缓存（滚动范围计算不依赖 rect 滞后值）
    private float codeContentWidth;           // 代码区内容宽度缓存（横向滚动范围，长行不换行时大于视口宽）
    private Vector2 lastViewportSize;         // 上次代码视口尺寸（窗口 / 编辑器尺寸变化时重算内容尺寸与滚动范围）
    private RectTransform enterPromptRt; // Level 3 进场「按 Enter 输入代码」提示
    private int lastCaretPos = -1;       // 上次光标的「全文全局位置」（跨块可比，轮询检测移动 / 换行就看它）
    private int lastCaretPosInSeg = -1;  // 上次光标的「块内位置」（← → 行首行尾判断按块内算）
    private int lastCaretSegIndex = -1;  // 上次光标所在块（块号变了，块内位置就失去意义，必须作废）
    private float scrollFollowSuppressUntil; // 自动滚动跟随的冻结截止时刻（鼠标点击 / 拖选定光标期间与之后短暂窗口内不跟随）
    private bool suppressScrollFollowOnce;   // 下一次 RefreshCodeView 冻结滚动位置（撤销/重做/重切分：整篇文本被换掉，caret 尚未落定）
    private PlayerCameraController playerController;
    private CommandConsoleController commandConsole;
    private SettingsPanelController settingsPanel;
    private CodeTower tower;
    private MainMenuController mainMenu; // 主页引用（用于主页状态下屏蔽 Enter）

    // 分段编辑器状态：segments[0] 由原单块 UI 充当（codeRt 本体），其余块按需动态创建。
    // 低于 segEnableThreshold 时恒为 1 块，所有逻辑退化回原单块编辑路径。
    private readonly List<CodeSegment> segments = new List<CodeSegment>();
    private int activeSegIndex;                 // 当前获得焦点的块下标
    private bool suppressSegmentCallbacks;      // 重建期间抑制 onValueChanged 回灌
    private bool pendingSegmentRebuild;         // 输入后超出块预算：延迟到 LateUpdate 按行重新切分
    private int lastEditedSegIndex = -1;        // 本次刷新只需重算高亮的块下标（-1 = 全部重算）
    private bool typedThisFrame;                 // 自上次 Update 以来发生过文本输入（打字帧禁止向上回拉滚动）
    private bool clickCaretFixGuarding;          // 点击落点守卫窗口进行中：逐帧压住「首次点击整块全选」的异常选区
    private Vector2 clickCaretFixScreenPos;      // 该次按下的屏幕坐标（LateUpdate 里用来换算点击处的行 / 列）
    private float clickCaretFixTime;             // 该次按下的时刻（超时的旧记录直接丢弃，避免误改后面的选区）
    private bool clickCaretFixWasFocused;        // 按下那一刻代码区是否已聚焦（区分「首次点击」与「已有焦点后的双击 / 拖选」）
    private int clickCaretFixSeg = -1;           // 本次点击落定的目标块（-1 = 尚未算出）
    private int clickCaretFixIdx = -1;           // 目标块内按点击几何位置换算出的字符下标
    private int clickCaretFixStable;             // 本次点击已写入目标光标的次数（== 1 时打一条诊断日志）
    private float lastClickDownTime = -10f;      // 上一次鼠标左键按下的时刻（双击选词手势识别用）
    private Vector2 lastClickDownPos;            // 上一次鼠标左键按下的屏幕坐标（区分双击与连续点不同位置）
    private int lastGuardFrame = -1;             // 守卫推进的帧号（多个守卫实例同帧只跑一次）
    private int typingFrameStamp = -10;          // 最近一次「打字帧刷新」的帧号（ApplyScrollY 的向前大跳闸门用）
    private int scrollJumpBlockStamp = -10;      // 最近一次拦截向前大跳的帧号（同帧只打一条日志）
    private string clickCaretFixDiag = "";       // 落点换算的诊断串（行号来源 / 行高 / 块高等，出问题时看日志）
    private int clickCaretFixTextLen = -1;       // 落点写入那一刻目标块的文本长度（用户一打字就靠它识别并放手）
    private static MethodInfo miInputFieldCharIndex; // uGUI InputField.GetCharacterIndexFromPosition 的反射句柄（官方落点算法）
    private string lastLayoutSignature = "";     // 上一次分块布局的签名（块数/行数/字号/行距/视口高度），相同则跳过全量重排
    private const float ClickCaretFixWindow = 0.5f; // 点击落点守卫窗口时长（秒）：覆盖 InputField 首激活延后改写光标的过程
    private const int SegHardLimit = 15500;     // 单块字符数硬上限（超过即重新切分，仍低于 16383 顶点上限）

    // 分段拼接处的「禁写」斜纹带：每块末尾因拼接多出的那一行（真实代码行之外）用主题色 45° 斜线填满，
    // 表示该行不能写代码，避免看起来像可编辑的空白行。
    private Texture2D segmentStripeTex;          // 运行时生成的斜纹纹理（白色斜线 + 透明底，Repeat 平铺）
    private const int StripeTexSize = 16;        // 斜纹纹理边长（像素）
    private const float SegmentStripeAlpha = 0.22f; // 斜纹带透明度（主题色叠在深底上，可见但不刺眼）

    // 拖拽选区越界抑制（指针仍在代码视口内才记录合法选区，拖到区外立即回滚）
    private bool selectionDragInside;
    private int lastValidSelectionAnchor = -1;
    private int lastValidSelectionFocus = -1;

    // 开关状态
    private bool wasPausedBeforeOpen;
    private bool wasFrozenBeforeOpen;
    private bool defaultCodeFilled;
    private string lastStatus = "";
    private Color statusColor = UITheme.TextMuted;

    // 运行时编译 runner
    private bool runnerActive;
    private System.Reflection.MethodInfo runnerMethod;
    private object runnerInstance;

    /// <summary>编辑器当前是否打开。</summary>
    public bool IsOpen => editorRoot != null && editorRoot.activeSelf;

    /// <summary>
    /// 代码输入框当前是否握着键盘焦点。教学卡（第 3 页内存可视化）据此决定 ↓ 键归谁：
    /// 玩家在代码区时 ↓ 一律是「移动光标」的代码模式，教学卡不得抢键去翻下一步。
    /// </summary>
    public bool IsCodeInputFocused => IsInputFocused();

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

    /// <summary>当前编辑器内的用户代码全文（各分块拼接；编辑器未构建时返回空串）。代码语法教学（Level 1 - Part 2）据此判定任务。</summary>
    public string CurrentCode => GetFullCode();

    /// <summary>编译并成功启动 runner 时触发，参数为当次编译的用户代码快照（供教学等外部系统判定「编译运行成功」）。</summary>
    public static event System.Action<string> OnCompiledSuccessfully;

    /// <summary>
    /// 教学用：运行时错误（RE）真的发生那一刻，如果编辑器此刻是关着的（编译成功后编辑器会自动关闭，
    /// 代码在后台跑），就把编辑器重新打开并把紫色 RE 窗口展开 —— 否则玩家只会在屏幕角落看到一行日志，
    /// 根本看不到「紫窗」长什么样。Level 1 - Part 3 第 6 页靠它把 RE 演给玩家看。
    /// 默认关闭，讲完那一页请记得置回 false。
    /// </summary>
    public bool AutoOpenOnRuntimeError { get; set; }

    /// <summary>
    /// 教学用：把编辑器内容设为指定代码（并标记默认示例已填过，避免首次打开时被示例代码覆盖）。
    /// 用于 Level 1 - Part 2 语法教学开局清空 / 放置代码骨架。
    /// </summary>
    public void SetCodeForTutorial(string code)
    {
        if (inputField == null)
            return;
        SetFullCode(code ?? string.Empty, 0);
        defaultCodeFilled = true;          // 覆盖默认示例填充分支
        lastCommittedText = GetFullCode();
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

        SetFullCode(DefaultCode, 0);
        defaultCodeFilled = true;              // 内容已等于默认模板，Open() 不再重复覆盖
        lastCommittedText = GetFullCode();      // 同步已提交文本，避免撤销 / 高亮错位
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

    /// <summary>
    /// 关卡切换收尾（LevelDirector.JumpTo / MainMenuController.StartLevel 调用）：把编辑器恢复到「本关初始状态」，
    /// 保证进入任意关卡时都不继承上一关的代码残留。
    ///   · 必须停 runner：上一关编译成功的 Run() 会每帧驱动炮台，不停就会在新关卡里继续自动开火
    ///     （典型症状：Level 3 - Part 1 编译运行后跳回 Level 1 - Part 1，手动激光一下没打，炮台自己在打方块）；
    ///   · 代码文本 / 已提交文本 / 撤销重做栈 / 光标 / defaultCodeFilled 一并清空，
    ///     使再次打开编辑器时回到「首次打开自动填默认示例」的初始状态（而不是带着上一关的代码）；
    ///   · 编辑器若还开着，强制关闭并恢复 timeScale，避免带着上一关的暂停状态进场。
    /// 幂等，可重复调用；编辑器 UI 尚未构建时只复位标志位。
    /// </summary>
    public void ResetForLevelChange()
    {
        // 1) 先停 runner：这是「代码跨关仍然生效」的直接原因，必须是本方法的第一步
        StopRunner();

        // 2) 收尾界面状态（含 timeScale / 鼠标锁 / 进场提示）
        if (IsOpen)
            CloseAndForceResume();
        else
            HideEnterPrompt();
        wasPausedBeforeOpen = false;
        wasFrozenBeforeOpen = false;
        // 这次关卡切换通常由一次 Enter（指令框确认）触发，先屏蔽到按键抬起，避免同一次 Enter 又打开编辑器
        suppressOpenUntilRelease = true;

        // 3) 内容与撤销栈清回出厂空白：defaultCodeFilled=false 让下次 Open() 重新填入默认示例
        defaultCodeFilled = false;
        lastCommittedText = string.Empty;
        lastCaretPos = -1;
        undoList.Clear();
        redoList.Clear();

        if (inputField != null)
        {
            SetFullCode(string.Empty, 0);
            AppendError("");
            ClearRuntimeErrorPanel();
            RefreshCodeView();
            if (codeRt != null)
            {
                ApplyScrollY(0f);
                UpdateScrollBars();
            }
        }

        Debug.Log("[CodeEditor] 关卡切换：编辑器已复位（runner 停止、代码清空、撤销栈清空）");
    }

    // ---------------- 高亮配色 ----------------
    private const string C_KEYWORD = UITheme.HexKeyword;  // 关键字青
    private const string C_STRING = UITheme.HexString;   // 字符串绿
    private const string C_NUMBER = UITheme.HexNumber;   // 数字粉
    private const string C_COMMENT = UITheme.HexComment;  // 注释灰绿
    private const string C_DEFAULT = UITheme.HexDefault;  // 默认白
    private const string C_GLOBAL = UITheme.HexGlobal;    // 扩展关键字 GlobalCode 蓝（本作自制语法，非 C# 关键字）

    /// <summary>本作扩展关键字：玩家用它在代码最前面声明「全局区」，块内内容会被提升到类体级别成为字段（跨帧存活）。</summary>
    private const string GlobalBlockKeyword = "GlobalCode";

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

    /// <summary>
    /// 默认示例代码（首次打开填入，简洁演示公开接口，不含每帧自动循环；输出走 +X 前方 print 荧幕，不写 Debug 日志）。
    /// 第一段是「本关开场」：GlobalCode 里放一次性标记，每帧入口只在第一次调用 tower.GameStart()，
    /// 玩家删掉这段（或删掉 GameStart 那行）关卡就不会开始出块。
    /// </summary>
    private const string DEFAULT_CODE =
        "GlobalCode { bool gameStarted = false; }   // 全局区：跨帧保留的一次性开场标记\n" +
        "// 本关由你的代码开场：tower.GameStart() 就是「本关正式开始」，不调用就永远不出方块\n" +
        "// 炮台接口示例：GameStart / GetAllObjects / Fire / GetFireCooldown（简洁版，不自动循环）\n" +
        "// 输出走 print 荧幕（X 轴前方），不写 Debug 日志；print 支持 printf 风格格式化 {0} {1}\n" +
        "// 引擎每帧调用 Run(tower)；如需循环 / 限频，请自行编写 while / if 条件\n" +
        "// 注意：Fire(new Vector3(A, B, C)) 的平面 Ax+By+Cz=0 必过原点，只需给出法向量 (A,B,C)\n" +
        "if (!gameStarted)\n" +
        "{\n" +
        "    gameStarted = true;   // 标记只打一次：每帧入口不会反复调用\n" +
        "    tower.GameStart();    // 本关正式开始（删掉这一行，游戏就不会开始）\n" +
        "}\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== 炮台接口示例 ==\");\n" +
        "float cd = tower.GetFireCooldown();\n" +
        "tower.PrintLine(\"GetFireCooldown={0:F3}\", cd);\n" +
        "bool fired1 = tower.Fire(new Vector3(0f, 1f, 0f));   // 法线 (0,1,0)：平面过原点，沿 +Y 发射\n" +
        "tower.PrintLine(\"Fire fired={0}\", fired1);\n" +
        "var objects = tower.GetAllObjects();\n" +
        "tower.PrintLine(\"GetAllObjects count={0}\", objects.Count);\n" +
        "for (int i = 0; i < objects.Count && i < 5; i++)\n" +
        "    tower.PrintLine(\"obj[{0}] = #{1} {2} HP={3} pos={4} vel={5} [{6}]\",\n" +
        "        i, objects[i].Index, objects[i].Type, objects[i].HP,\n" +
        "        objects[i].Position, objects[i].Velocity, objects[i].Params);\n";

    /// <summary>
    /// 默认示例代码（英文版）：与 DEFAULT_CODE 逐行一一对应，只有「注释 + 屏幕说明文字」换成英文，
    /// 代码本体完全一致 —— 英文界面下打开 Level 3 - Part 1 的代码演示，注释不该还是中文。
    /// </summary>
    private const string DEFAULT_CODE_EN =
        "GlobalCode { bool gameStarted = false; }   // global block: one-shot start flag, survives frames\n" +
        "// Your code starts this level: tower.GameStart() means the level really begins; without it no blocks ever spawn\n" +
        "// Tower API demo: GameStart / GetAllObjects / Fire / GetFireCooldown (minimal, no auto loop)\n" +
        "// Output goes to the print screen (in front on +X), not Debug.Log; print supports printf-style {0} {1}\n" +
        "// The engine calls Run(tower) every frame; write your own while / if when you need looping or rate limits\n" +
        "// Note: the plane Ax+By+Cz=0 of Fire(new Vector3(A, B, C)) always passes the origin, so only the normal (A,B,C) is needed\n" +
        "if (!gameStarted)\n" +
        "{\n" +
        "    gameStarted = true;   // mark only once, so the per-frame entry never calls it repeatedly\n" +
        "    tower.GameStart();    // the level really begins (delete this line and the game never starts)\n" +
        "}\n" +
        "tower.ClearPrint();\n" +
        "tower.PrintLine(\"== Tower API demo ==\");\n" +
        "float cd = tower.GetFireCooldown();\n" +
        "tower.PrintLine(\"GetFireCooldown={0:F3}\", cd);\n" +
        "bool fired1 = tower.Fire(new Vector3(0f, 1f, 0f));   // normal (0,1,0): plane through origin, fires along +Y\n" +
        "tower.PrintLine(\"Fire fired={0}\", fired1);\n" +
        "var objects = tower.GetAllObjects();\n" +
        "tower.PrintLine(\"GetAllObjects count={0}\", objects.Count);\n" +
        "for (int i = 0; i < objects.Count && i < 5; i++)\n" +
        "    tower.PrintLine(\"obj[{0}] = #{1} {2} HP={3} pos={4} vel={5} [{6}]\",\n" +
        "        i, objects[i].Index, objects[i].Type, objects[i].HP,\n" +
        "        objects[i].Position, objects[i].Velocity, objects[i].Params);\n";

    /// <summary>当前语言下的默认示例代码（中文界面 → 中文注释，英文界面 → 英文注释），打开编辑器 / 回厂重置都用它。</summary>
    private static string DefaultCode
    {
        get { return Localization.IsChinese ? DEFAULT_CODE : DEFAULT_CODE_EN; }
    }

    private void Awake()
    {
        EnsureEventSystem();
        LoadFontSettings();   // 先读回玩家上次调好的字号，BuildUI 建字时直接用对（否则会先按默认字号建一遍再改）
        BuildUI();
        editorRoot.SetActive(false);
        // 主页切语言时编辑器早已构建完，标题栏 / 按钮文案不会自己变，必须订阅后重排一次
        Localization.OnLanguageChanged -= HandleLanguageChanged;
        Localization.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        Localization.OnLanguageChanged -= HandleLanguageChanged;
    }

    // ================== 字号存档（PlayerPrefs） ==================

    /// <summary>
    /// 读回玩家上次用 Ctrl+滚轮调好的字号（代码区 + 报错区）。
    ///   · 没有存档（第一次玩 / 回厂重置后）一律用默认字号：代码 24、报错 18；
    ///   · 存档值一律夹回合法区间（代码 12~40、报错 11~34），脏数据不会把界面撑坏；
    ///   · 报错字号存在存档里 = 玩家手动调过，同时把 errorFontScaled 置位，
    ///     这样 RefreshErrorLayout 重启后也尊重玩家设定，不会自动缩回默认上限。
    /// 必须在 BuildUI() 之前调用：文本组件创建时就按最终字号建，省掉一次「建完再改」的重排。
    /// </summary>
    private void LoadFontSettings()
    {
        codeFontSize = Mathf.Clamp(PlayerPrefs.GetInt(CodeFontSizePrefKey, DefaultCodeFontSize),
            CodeFontSizeMin, CodeFontSizeMax);

        bool hasErrorSaved = PlayerPrefs.HasKey(ErrorFontSizePrefKey);
        errorFontSize = Mathf.Clamp(PlayerPrefs.GetInt(ErrorFontSizePrefKey, DefaultErrorFontSize),
            ErrorFontMin, ErrorFontZoomMax);
        errorFontScaled = hasErrorSaved;

        reFontSize = Mathf.Clamp(PlayerPrefs.GetInt(ReFontSizePrefKey, DefaultReFontSize),
            ReFontSizeMin, ReFontSizeMax);
    }

    /// <summary>把当前代码字号 / 报错字号写进存档并立即落盘（Ctrl+滚轮每调一次就存一次，崩溃也不丢）。</summary>
    private void SaveFontSettings()
    {
        PlayerPrefs.SetInt(CodeFontSizePrefKey, Mathf.Clamp(codeFontSize, CodeFontSizeMin, CodeFontSizeMax));
        PlayerPrefs.SetInt(ErrorFontSizePrefKey, Mathf.Clamp(errorFontSize, ErrorFontMin, ErrorFontZoomMax));
        PlayerPrefs.SetInt(ReFontSizePrefKey, Mathf.Clamp(reFontSize, ReFontSizeMin, ReFontSizeMax));
        PlayerPrefs.Save();
    }

    /// <summary>只删字号存档键（编辑器实例还没建 / 不需要立刻重排时用），下次构建 UI 即回到默认字号。</summary>
    public static void ClearSavedFontSettings()
    {
        PlayerPrefs.DeleteKey(CodeFontSizePrefKey);
        PlayerPrefs.DeleteKey(ErrorFontSizePrefKey);
        PlayerPrefs.DeleteKey(ReFontSizePrefKey);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// /lock 回厂重置：删掉字号存档，编辑器字号回到默认（代码 24 / 报错 18）。
    /// 指令框在关卡内调起时编辑器可能正开着，所以顺带把已构建好的文本组件按新字号重排一次。
    /// </summary>
    public void ResetFontSettingsToDefault()
    {
        ClearSavedFontSettings();

        codeFontSize = DefaultCodeFontSize;
        errorFontSize = DefaultErrorFontSize;
        errorFontScaled = false;
        errorZoneManualHeight = 0f;
        reFontSize = DefaultReFontSize;

        if (inputField == null)
            return; // UI 还没建：下次 Awake / Open 时直接按默认字号建

        ApplyFontSizeToAllSegments();
        RefreshCodeView();
        RefreshErrorLayout(errorListText != null ? errorListText.text : string.Empty);
        if (reListText != null && !reZoneCollapsed)
            RefreshReLayout(reListText.text); // 紫窗字号一并回默认
    }

    /// <summary>
    /// 语言切换：除了重排标题栏文案，还要把「仍是默认示例模板」的编辑器内容换成对应语言的版本 ——
    /// 玩家自己改过的代码一个字都不动，只有内容恰好等于中文 / 英文默认模板时才整体替换（注释语言随界面走）。
    /// </summary>
    private void HandleLanguageChanged()
    {
        RefreshTitleBarChrome();
        if (inputField == null)
            return; // UI 未构建：下次 Open() 直接按新语言填默认模板
        string current = GetFullCode();
        if (current == DEFAULT_CODE || current == DEFAULT_CODE_EN)
        {
            SetFullCode(DefaultCode, 0);
            lastCommittedText = GetFullCode();
            undoList.Clear();
            redoList.Clear();
        }
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

    /// <summary>
    /// 「一键打开编辑器」组合键判定。
    /// Alt+O 与 Ctrl+Shift+O 在 Unity 编辑器里都未被占用，可在编辑器内正常收到；
    /// Ctrl+O 与 Unity 的 File > Open Project 冲突（该键被编辑器先消费，脚本无法拦截），
    /// 只有在打包运行时（Application.isEditor == false）才继续生效，避免按下后只弹出 Unity 面板。
    /// </summary>
    private bool IsOpenShortcutPressed()
    {
        if (!Input.GetKeyDown(KeyCode.O))
            return false;
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            return true;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl && shift)
            return true;
        if (ctrl && !Application.isEditor)
            return true;
        return false;
    }

    /// <summary>
    /// 「仅打包运行时可用」的 Ctrl 组合键判定，用于给 F8 / F9 提供编辑器用户习惯的等价键（Ctrl+O 载入、Ctrl+S 导出）。
    /// Unity 编辑器里这两个组合键会被编辑器自身先消费（Ctrl+O = File > Open Project、Ctrl+S = Save Project），
    /// 脚本根本收不到，因此在编辑器内直接返回 false，避免出现「按了没反应还以为坏了」的错觉。
    /// </summary>
    private bool IsPackedEditorShortcut(KeyCode key)
    {
        if (Application.isEditor)
            return false;
        if (!Input.GetKeyDown(key))
            return false;
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    private void Update()
    {
        // Enter 开关：关闭时按 Enter 打开；打开时若输入框未聚焦则按 Enter 关闭（聚焦时 Enter 交给输入框换行，便于编写多行代码）
        if (!IsOpen)
        {
            // Level 1 - Part 2「编程序章」讲解关 / Level 2 - Part 1「方块图鉴」关：正在读讲解页时
            // 不响应 Enter / Ctrl+O 打开编辑器。否则读字途中按一下 Enter 就会弹出全屏编辑器，
            // 讲解 / 图鉴面板被盖住，玩家会以为界面卡死。
            if (CodeIntroTutorialController.Reading || BlockIntroController.Reading)
                return;

            // 一键直接打开代码编辑器（组合键不易误触，且不受 Enter 抑制 / 输入焦点状态影响）
            // 说明：Ctrl+O 已被 Unity 编辑器绑给 File > Open Project，在编辑器里按下去只会弹 Unity 的窗口，
            // 游戏拿不到这次按键（编辑器级快捷键无法从脚本里拦截）。因此改用 Alt+O / Ctrl+Shift+O，
            // Ctrl+O 仅在实际打包运行（非编辑器）时继续生效。
            if (IsOpenShortcutPressed())
            {
                if (!IsMainMenuBlocking())
                {
                    Open();
                    return;
                }
            }
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
            // 文件选择面板打开时：吞掉编辑器的全部快捷键（Enter 关闭 / F6 编译 / F7 停止 / F8 载入 / F9 导出），只让面板自身响应
            if (IsCodeFilePickerOpen)
                return;
            if ((Input.GetKeyDown(openKey) || Input.GetKeyDown(openKeyAlt)) && !IsInputFocused())
            {
                // Level 1 - Part 3「编辑器界面入门」：这一关 Enter 是卡片里指定的「打开编辑器」键，
                // 玩家照着提示连按时会把已经打开的编辑器又关掉（看着像按 Enter 被踢出去）。
                // 教学期间 Enter 只负责打开，关闭统一走 Esc 或标题栏的关闭按钮。
                if (EditorIntroTutorialController.Reading)
                    return;
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
            // F7：停止运行（与「停止 (F7)」按钮同逻辑）
            if (Input.GetKeyDown(KeyCode.F7))
            {
                StopRunner();
                return;
            }
            // F8：打开「载入代码文件」面板（与 Open code file 按钮同逻辑）。
            // 打包运行时 Ctrl+O 与 F8 同效（编辑器里 Ctrl+O 被 Unity 的 File > Open Project 先消费，脚本收不到）；
            // 界面上的提示统一只写 F8，不把 Ctrl+O 露在按钮文字与悬停提示里。
            if (Input.GetKeyDown(KeyCode.F8) || IsPackedEditorShortcut(KeyCode.O))
            {
                OpenCodeFilePicker();
                return;
            }
            // F9：打开「导出到指定文件夹」面板（与 Export 按钮同逻辑）；打包运行时 Ctrl+S 与 F9 同效，提示同样统一只写 F9。
            if (Input.GetKeyDown(KeyCode.F9) || IsPackedEditorShortcut(KeyCode.S))
            {
                OpenCodeSavePicker();
                return;
            }
            // F10：展开 / 收起「报错界面」（与标题栏 Error Panel 按钮同逻辑）。
            // 打开编辑器时默认不显示报错区，只有编译报错才自动弹出；想随时回看上次报错就按 F10。
            // 紫色 RE 窗口正开着时，按 F10 只关紫窗；再按一次 F10 才去动红色报错框，避免一按 F10 又把紫色弹回来。
            if (Input.GetKeyDown(KeyCode.F10))
            {
                if (IsReZoneVisible())
                {
                    CloseReZonePanel();
                    return;
                }
                ToggleErrorPanel();
                return;
            }
            // F11：紫色运行时错误（RE）窗口开 / 关，与 F10 的红窗互不干扰。
            if (Input.GetKeyDown(KeyCode.F11))
            {
                ToggleReZonePanel();
                return;
            }
        }

        // 编辑器打开时：光标位置变化 → 刷新状态栏并自动滚动跟随；滚轮在代码视口内翻页
        if (IsOpen)
        {
            RefreshContentOnViewportResize(); // 窗口 / 编辑器尺寸变化后先重算内容尺寸与滚动范围
            UpdateActiveSegmentFromSelection(); // 点击哪个分块就把该块的 InputField 视为活动块（单块时恒为 0）
            // 光标位置比较必须用「全文全局位置」：caretPosition 只是块内下标，跨块时既不能比较也不能
            // 判断是否有移动（两块块内值偶然相同就会漏检，切换块时又会误判成跳行）。全局值才是唯一可比的量。
            int cp = GlobalCaretPosition();
            // 失焦帧（点击别的 UI、切块瞬间的焦点空窗）里 ActiveField 会退化成 0 号块的输入框，
            // 算出的全局位置是个靠前的小值，跟上一帧的大位置一比就成了「光标大幅上移」→ 触发跟随 → 视图跳前。
            // 所以失焦时直接沿用上次位置，不产生任何光标事件。
            if (!IsInputFocused() && lastCaretPos >= 0)
                cp = lastCaretPos;
            // 记下本帧的鼠标按下：LateUpdate（EventSystem 处理完点击之后）核对一次落点与选区，
            // 修掉 uGUI InputField「第一次点击一块尚未激活过的块时把整块文本全选中」的异常选区。
            if (Input.GetMouseButtonDown(0))
            {
                // 双击（同一位置 0.35s 内第二次按下）是「选词」手势，不能接管，否则用户永远选不中单词。
                // 位置必须一起判：只是连续快速点击两个不同的块，绝不能当成双击而放弃守卫。
                bool doubleClick = Time.unscaledTime - lastClickDownTime < 0.35f &&
                                   Vector2.Distance((Vector2)Input.mousePosition, lastClickDownPos) < 30f;
                lastClickDownTime = Time.unscaledTime;
                lastClickDownPos = Input.mousePosition;
                clickCaretFixGuarding = !doubleClick;
                clickCaretFixScreenPos = Input.mousePosition;
                clickCaretFixTime = Time.unscaledTime;
                // 记下「按下这一瞬间代码区是否已经有焦点」，仅用于日志区分「首次激活」与「已聚焦」两种路径。
                clickCaretFixWasFocused = IsInputFocused();
                clickCaretFixSeg = -1;
                clickCaretFixIdx = -1;
                clickCaretFixStable = 0;
                clickCaretFixTextLen = -1;
            }
            int prevCaret = lastCaretPos; // 上一帧光标（方向键横向浏览：行尾 / 行首判断基准）
            // 块内光标必须单独保留一份：← → 的行首 / 行尾判断是拿「上一个块内下标」去查本块文本的，
            // 若直接塞全局位置，跨块后会越界或落到别的行。块号变了就作废（-1 = 不做横向滚动）。
            int prevCaretBlock = lastCaretSegIndex == activeSegIndex ? lastCaretPosInSeg : -1;
            InputField caretField = ActiveField;
            int curCaretBlock = caretField != null ? Mathf.Clamp(caretField.caretPosition, 0, caretField.text.Length) : -1;
            // 刚发生过文本输入：InputField 的 onValueChanged 可能晚于本方法执行，留到下帧读也算数
            bool typedNow = typedThisFrame;
            typedThisFrame = false;
            // 鼠标点击 / 拖选定光标时，光标必然就在指针下方（本来就可见），禁止再自动滚动跟随：
            // 旧逻辑无条件跟随，点击落在视口边缘的半行时会把该行滚到视口底，表现为「每点一次代码就向上飞一截」。
            bool pointerDriven = Input.GetMouseButton(0) || Input.GetMouseButtonUp(0);
            if (pointerDriven)
                scrollFollowSuppressUntil = Time.unscaledTime + 0.25f; // 点击后短暂冻结自动跟随：InputField 的 caret 可能延迟到下一帧才落定
            if (cp != lastCaretPos)
            {
                lastCaretPos = cp;
                lastCaretPosInSeg = curCaretBlock;
                lastCaretSegIndex = activeSegIndex;
                RefreshCursorStatus();
                if (!pointerDriven)
                {
                    // 只有「明确的上移操作」才允许把视图往上回拉：↑ / PageUp / Home 按住期间。
                    // 其余一切光标变化（打字、粘贴、撤销后落光标、← →、点击后续移动）只许向下跟随，
                    // 绝不许把视图往前拽——那一拽就是用户看到的「输入后跳到 400 → 200 行」。
                    bool navUpNow = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.Home);
                    UpdateScrollToCaret(navUpNow && !typedNow, typedNow ? activeSegIndex : -1);
                }
            }
            HandleArrowHorizontalScroll(prevCaretBlock); // → / ← 逐字符横向浏览超出视口右侧的代码（按块内下标判断行首行尾）
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlHeld)
            {
                // Ctrl+滚轮：按指针所在部位只缩放那一处字号 —— 紫窗 / 红窗 / 代码区，互不串台
                HandlePointerWheelZoom();
            }
            else
                HandleWheelScroll(); // 滚轮翻页（自实现，不依赖 ScrollRect）
            // Ctrl+C/V/X 粘贴保障：输入框未聚焦时原生快捷键不生效，检测到 Ctrl 组合先自动聚焦
            if (!IsInputFocused())
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl && (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.V) ||
                             Input.GetKeyDown(KeyCode.X)))
                {
                    InputField focusTarget = ActiveField;
                    if (focusTarget != null)
                    {
                        focusTarget.ActivateInputField();
                        focusTarget.Select();
                    }
                }
            }

            // Ctrl+Z / Ctrl+Y（撤销 / 重做）：自实现栈兜底，失焦屏蔽原生撤销防止双触发
            HandleUndoRedoShortcuts();

            TickErrorSplitInputDrag(); // 红条的自管拖动：按下即接管，不靠 EventSystem 的拖动阈值
            UpdateScrollBars(); // 每帧兜底：内容溢出即显示滚动块，放得下即隐藏
            UpdateSplitHoverHighlights(); // 鼠标压到红色 / 紫色分隔条就提亮，提示这里能拖
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
                AppendRuntimeError(msg);
                Debug.LogError("[CodeEditor] 运行时错误：" + msg);
                Debug.LogException(e);

                // 教学演示：把编辑器重新打开，让玩家当场看到紫色 RE 窗口（不重开就只有日志，紫窗在关着的编辑器里）
                if (AutoOpenOnRuntimeError && !IsOpen)
                {
                    Open();
                    OpenReZonePanel();
                }
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
        // 点击落点守卫：EventSystem 在 Update 阶段处理完 PointerDown（InputField 会自己算光标）之后，
        // 再核对落点与选区，修掉「第一次点某块就整块全选」的异常选区。
        // 关键点：InputField 的首次激活只是把 m_ShouldActivateNextUpdate 挂上标记，真正改写光标
        // （ActivateInputFieldInternal 会把光标推到文本末尾）发生在其自身 LateUpdate 里，
        // 与我们的 LateUpdate 谁先谁后并不确定——所以一次性核对不够，必须在按下后的短窗口内逐帧幂等写入目标光标，
        // 直到连续两帧稳定 / 指针开始拖选 / 用户开始按键 / 窗口超时才放手。
        // 守卫不在这里跑：本组件的 LateUpdate 与 uGUI InputField 同为默认 executionOrder，先后不确定，
        // 先跑就会被 InputField 当帧覆盖（前几版「改了等于没改」就是这么来的）。
        // 守卫统一由 CodeEditorCaretGuard（executionOrder = 20000）的 LateUpdate 调用 LateGuardTick() 推进。
        // 输入导致某块超过字符预算：延后到本帧 InputField 处理完之后再按行重新切分，
        // 避免在 onValueChanged 回调内部改写自身文本造成重入。
        if (pendingSegmentRebuild)
        {
            pendingSegmentRebuild = false;
            if (IsOpen && segments.Count > 0)
            {
                ApplyPendingSegmentRebuild();
                return;
            }
        }

        InputField activeInput = ActiveField;
        if (activeInput == null || codeViewportRt == null || !IsOpen || !Input.GetMouseButton(0))
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
            lastValidSelectionAnchor = activeInput.selectionAnchorPosition;
            lastValidSelectionFocus = activeInput.selectionFocusPosition;
            return;
        }

        if (!selectionDragInside || lastValidSelectionAnchor < 0 || lastValidSelectionFocus < 0)
            return;
        if (activeInput.selectionAnchorPosition == lastValidSelectionAnchor &&
            activeInput.selectionFocusPosition == lastValidSelectionFocus)
            return;

        // 指针已离开代码区：撤销越界的选区扩展，仅保留离开前的合法选区（不修改文本内容）
        activeInput.selectionAnchorPosition = lastValidSelectionAnchor;
        activeInput.selectionFocusPosition = lastValidSelectionFocus;
    }

    // ---------------- 开关逻辑（与 PlayerCameraController 联动） ----------------

    // 右侧为教学指导框预留的宽度（0 = 默认全宽代码区；当前上下分栏布局下恒为 0，保留接口兼容）
    private float rightReservedWidth;

    // 底部为教学指导框预留的高度（0 = 默认铺满全屏；Level 1-3 / Level 2-2 教学时设为指导框高度）
    private float bottomReservedHeight;

    // 编辑器整体根节点（用于上下分栏时收窄高度）
    private RectTransform editorRootRt;

    // 底部错误 / 状态区底板（全屏时为深色分隔带；上下分栏教学时与代码区同色，避免两块之间浮出黑色横带）
    private Image consoleBg;
    private static readonly Color consoleBgFullColor = new Color(0.165f, 0.086f, 0.102f, 1f); // 红灰：底部红窗底板与红色 CE 提示同调
    private static readonly Color consoleBgSplitColor = new Color(0.165f, 0.086f, 0.102f, 1f); // 分栏时同样红灰，红区边界仍可辨

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

        // 编辑器变矮后错误区可能高过了可用上限：按新高度重新夹一次，保证代码区不被挤没
        ApplyErrorZoneHeight(errorZoneHeightCurrent());
        if (reZoneRt != null)
            ApplyReZoneHeight(reZoneRt.sizeDelta.y); // 紫色 RE 窗口同样按新高度夹一次，避免挤出代码区

        UpdateScrollBars(); // 视口高度变化后重算竖向滚动条的滑块长度与位置
    }

    /// <summary>当前错误区高度（无错误区时返回最小值）。</summary>
    private float errorZoneHeightCurrent()
    {
        if (errorZoneRt == null)
            return ErrorZoneMinHeight;
        float h = errorZoneRt.sizeDelta.y;
        return h > 0f ? h : ErrorZoneMinHeight;
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
        {
            // 竖条左边也要跟着左移：只改右边界会把 10px 宽的轨道拉成一大片（滑块被推到区域正中）
            vScrollTrackRt.offsetMin = new Vector2(-(r + 16f), vScrollTrackRt.offsetMin.y);
            vScrollTrackRt.offsetMax = new Vector2(-(r + 6f), vScrollTrackRt.offsetMax.y);
        }
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
            SetFullCode(DefaultCode, 0); // 首次打开填入示例代码（内部分段判定；注释语言跟随当前语言）
            defaultCodeFilled = true;
        }
        lastCommittedText = GetFullCode();
        undoList.Clear();
        redoList.Clear();
        RefreshCodeView(); // 刷新高亮 / 行号 / 内容高度与滚动位置
        // 打开时强制滚到代码最上面（优先级高于鼠标行缩放锚点残留位置与光标行跟随）
        if (codeRt != null)
        {
            ApplyScrollY(0f);
            UpdateScrollBars();
        }
        InputField openField = ActiveField;
        if (openField != null)
        {
            openField.ActivateInputField();
            openField.Select();
        }

        // 打开瞬间「已强制滚到顶部」这件事必须同步给下一帧的跟随逻辑：
        // 输入框里留着上次关闭时的光标（典型是上次停在 192 行附近），而上帧清零的 lastCaretPos 与之不等，
        // 下一帧 Update 就会把「首次读到的光标」误判成一次光标移动，按 192 行把视图从顶部又拉下去。
        // 这里把上一帧光标基准对齐到当前真实光标，并给一个短暂的跟随抑制窗口，保证打开后确实停在第一行。
        lastCaretPos = GlobalCaretPosition();
        lastCaretPosInSeg = openField != null ? Mathf.Clamp(openField.caretPosition, 0, openField.text.Length) : 0;
        lastCaretSegIndex = activeSegIndex;
        clickCaretFixGuarding = false;
        scrollFollowSuppressUntil = Time.unscaledTime + 0.4f;

        Time.timeScale = 0f;
        SetCursorLocked(false);
        // 打开编辑器时「只显示代码编辑状态」：报错区默认收起（红条 / 粗提示条 / 报错文字一概不显示），
        // 上一次的报错文本仍留在内存里 —— 编译报错会自动弹出，玩家也可以按 F10 / 点 Error Panel 随时调出。
        CollapseErrorZone();
        SetStatus(Localization.Pick(
            "编辑器已打开（Esc / ✕ 关闭）｜F6 编译运行 · F7 停止 · F8 载入 · F9 导出 · F10 报错界面 · F11 紫色 RE",
            "Editor opened (Esc or ✕ to close) | F6 compile & run · F7 stop · F8 load · F9 export · F10 error panel · F11 runtime (RE)"), false);
        Debug.Log("[CodeEditor] 编辑器打开");
    }

    /// <summary>关闭编辑器：恢复时间流速、锁定鼠标、解除视角输入占用；若打开前处于暂停则还原暂停。</summary>
    public void Close()
    {
        if (!IsOpen)
            return;

        editorRoot.SetActive(false);
        if (filePickerRoot != null)
            filePickerRoot.SetActive(false); // 关编辑器时一并收起文件选择面板，避免下次打开时残留
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
        // 文件选择面板打开时，ESC 只关面板，不退出代码编辑器
        if (IsCodeFilePickerOpen)
        {
            CloseCodeFilePicker();
            SetStatus(Localization.Pick("已取消选择文件", "File selection cancelled"), false);
            return;
        }
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

        string userCode = GetFullCode(); // 全文（多分块时自动拼接，禁止直接读某个块的 field.text）
        if (string.IsNullOrEmpty(userCode.Trim()))
        {
            AppendError(Localization.Pick("错误：代码为空。", "Error: code is empty."));
            return;
        }

        // GlobalCode 全局区解析：误用（后面缺 {、缺配对 }、出现多个块、块内用 tower）一律致命失败，不送进编译器
        UserCodeLayout layout = ParseUserCode(userCode);
        if (!string.IsNullOrEmpty(layout.FatalError))
        {
            AppendError(layout.FatalError);
            if (AutoOpenOnRuntimeError && !IsOpen)
                Open();
            return;
        }

        string fullSource = WrapUserCode(layout, out string className, out int[] templateLineToUser);

        StopRunner(); // 编译前停掉旧 runner，避免重复运行
        ClearRuntimeErrorPanel(); // 每次编译清空紫色 RE 窗口：CE（红）与 RE（紫）互不混淆

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
                        // 行号映射回玩家代码：全局区与每帧语句是两段拼接，靠 templateLineToUser 精确指回原文行
                        int userLine = MapTemplateLine(templateLineToUser, e.Line);
                        // mcs/csc 的 ErrorNumber 自带 "CS" 前缀，这里统一掉，避免显示成 CSCS0103
                        string errNo = e.ErrorNumber ?? string.Empty;
                        if (errNo.StartsWith("CS", System.StringComparison.OrdinalIgnoreCase))
                            errNo = errNo.Substring(2);
                        Debug.LogError("[CodeEditor] 编译错误 模板行 " + e.Line + " / 用户代码第 " + userLine + " 行,第 " + e.Column + " 列：CS" + errNo + " " + e.ErrorText);
                        LogCompileErrorDiagnostics(userCode, e, userLine); // 临时诊断（定位 CS1026，纯只读，可整段删除）
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
                    // 教学演示：玩家可能先把编辑器关了再按 F6 —— 错误写在关着的面板里等于没写，
                    // 这时候把编辑器打开，红色错误区才真的看得见。
                    if (AutoOpenOnRuntimeError && !IsOpen)
                        Open();
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
                SetStatus(Localization.Pick("编译成功，已启动运行（F7 或「停止」按钮可停止）", "Compiled OK, runner started (F7 or Stop to stop)"), false);
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

    // ================== 玩家代码分区：GlobalCode 全局区 ==================

    /// <summary>
    /// 玩家代码分区结果：GlobalCode 块内的声明（提升到类体级别成为字段，跨帧存活）+ 剩下的每帧语句（留在 Run 方法体内）。
    /// 两段各自带「模板行 → 玩家代码行号」映射，编译报错才能指回玩家真正写的那一行。
    /// </summary>
    private sealed class UserCodeLayout
    {
        public List<string> FieldLines = new List<string>();      // 全局区每行原文
        public List<int> FieldUserLines = new List<int>();        // 与 FieldLines 一一对应的玩家行号（1-based）
        public List<string> BodyLines = new List<string>();       // 每帧语句每行原文
        public List<int> BodyUserLines = new List<int>();         // 与 BodyLines 一一对应的玩家行号（1-based）
        public bool HasGlobalBlock;
        public string FatalError;                                 // 非 null：GlobalCode 误用，编译前直接致命失败
    }

    /// <summary>标识符字符判定（词边界用）。</summary>
    private static bool IsIdentChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>字符偏移 → 1-based 行号。</summary>
    private static int LineOf(string code, int pos)
    {
        int limit = pos < 0 ? 0 : (pos > code.Length ? code.Length : pos);
        int line = 1;
        for (int i = 0; i < limit; i++)
            if (code[i] == '\n') line++;
        return line;
    }

    /// <summary>找某个「完整标识符」在全文中首次出现的下标（不在别的标识符内部），没有则返回 -1；入参须是已抹掉注释/字符串的 masked 文本。</summary>
    private static int IndexOfToken(string masked, string token)
    {
        if (string.IsNullOrEmpty(masked) || string.IsNullOrEmpty(token))
            return -1;
        int scan = 0;
        while (scan < masked.Length)
        {
            int at = masked.IndexOf(token, scan, System.StringComparison.Ordinal);
            if (at < 0) return -1;
            scan = at + token.Length;
            bool leftOk = at == 0 || !IsIdentChar(masked[at - 1]);
            bool rightOk = scan >= masked.Length || !IsIdentChar(masked[scan]);
            if (leftOk && rightOk) return at;
        }
        return -1;
    }

    /// <summary>同 IndexOfToken，但忽略大小写（用于提示「关键字区分大小写：要写 GlobalCode」）。</summary>
    private static int IndexOfTokenIgnoreCase(string masked, string token)
    {
        if (string.IsNullOrEmpty(masked) || string.IsNullOrEmpty(token))
            return -1;
        int scan = 0;
        while (scan < masked.Length)
        {
            int at = masked.IndexOf(token, scan, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            scan = at + token.Length;
            bool leftOk = at == 0 || !IsIdentChar(masked[at - 1]);
            bool rightOk = scan >= masked.Length || !IsIdentChar(masked[scan]);
            if (leftOk && rightOk) return at;
        }
        return -1;
    }

    /// <summary>构造 GlobalCode 误用的致命错误文案（中英），并点明「不是 C# 的功能，是本作刻意设计」。</summary>
    private static string GlobalFatal(int line, string zh, string en)
    {
        return Localization.Pick(
            "致命错误（第 " + line + " 行）：" + zh
            + "\n注意：GlobalCode 不是 C# 的功能，是本作刻意设计的扩展语法，写错会直接报错，不会被当成普通代码放过。",
            "Fatal error (L" + line + "): " + en
            + "\nNote: GlobalCode is NOT a C# feature - it is an extension designed on purpose for this game; misuse is a hard error, never silently ignored.");
    }

    /// <summary>把注释、字符串、字符字面量抹成空格（保留换行，长度不变），供括号配对与关键字扫描使用。</summary>
    private static string MaskLiteralsAndComments(string code)
    {
        char[] buf = code.ToCharArray();
        int n = buf.Length;
        int i = 0;
        while (i < n)
        {
            char c = buf[i];

            // 行注释 //
            if (c == '/' && i + 1 < n && buf[i + 1] == '/')
            {
                while (i < n && buf[i] != '\n') { buf[i] = ' '; i++; }
                continue;
            }

            // 块注释 /* */
            if (c == '/' && i + 1 < n && buf[i + 1] == '*')
            {
                buf[i] = ' '; buf[i + 1] = ' ';
                i += 2;
                while (i < n && !(buf[i] == '*' && i + 1 < n && buf[i + 1] == '/'))
                {
                    if (buf[i] != '\n') buf[i] = ' ';
                    i++;
                }
                if (i < n) { buf[i] = ' '; i++; }
                if (i < n) { buf[i] = ' '; i++; }
                continue;
            }

            // 逐字字符串 @"..."（内部 "" 表示一个引号）
            if (c == '@' && i + 1 < n && buf[i + 1] == '"')
            {
                buf[i] = ' '; buf[i + 1] = ' ';
                i += 2;
                while (i < n)
                {
                    if (buf[i] == '"')
                    {
                        if (i + 1 < n && buf[i + 1] == '"') { buf[i] = ' '; buf[i + 1] = ' '; i += 2; continue; }
                        buf[i] = ' '; i++; break;
                    }
                    if (buf[i] != '\n') buf[i] = ' ';
                    i++;
                }
                continue;
            }

            // 普通字符串 "..."
            if (c == '"')
            {
                buf[i] = ' '; i++;
                while (i < n)
                {
                    if (buf[i] == '\\' && i + 1 < n) { buf[i] = ' '; buf[i + 1] = ' '; i += 2; continue; }
                    if (buf[i] == '"') { buf[i] = ' '; i++; break; }
                    if (buf[i] != '\n') buf[i] = ' ';
                    i++;
                }
                continue;
            }

            // 字符字面量 '...'
            if (c == '\'')
            {
                buf[i] = ' '; i++;
                while (i < n)
                {
                    if (buf[i] == '\\' && i + 1 < n) { buf[i] = ' '; buf[i + 1] = ' '; i += 2; continue; }
                    if (buf[i] == '\'') { buf[i] = ' '; i++; break; }
                    if (buf[i] != '\n') buf[i] = ' ';
                    i++;
                }
                continue;
            }

            i++;
        }
        return new string(buf);
    }

    /// <summary>在 masked 的 [from, to) 区间里找独立标识符 token（词边界严格匹配，避免 tower 命中 towerHp）。</summary>
    private static bool ContainsToken(string masked, int from, int to, string token)
    {
        int scan = from;
        while (scan < to)
        {
            int at = masked.IndexOf(token, scan, System.StringComparison.Ordinal);
            if (at < 0 || at + token.Length > to) return false;
            scan = at + token.Length;
            bool leftOk = at == 0 || !IsIdentChar(masked[at - 1]);
            bool rightOk = scan >= masked.Length || !IsIdentChar(masked[scan]);
            if (leftOk && rightOk) return true;
        }
        return false;
    }

    /// <summary>
    /// 解析玩家代码：找出 GlobalCode { ... } 块，块内内容作为「全局区字段」，其余保持原样（块位置用空格占位，行号不变）。
    /// GlobalCode 是本作定制语法（C# 本身不允许在方法体内声明字段），误用一律返回致命错误，绝不降级成普通代码。
    /// </summary>
    private static UserCodeLayout ParseUserCode(string userCode)
    {
        UserCodeLayout layout = new UserCodeLayout();
        string code = (userCode ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        string masked = MaskLiteralsAndComments(code);

        // 友好纠错：关键字定名 GlobalCode。若玩家照着早期草稿写成 my_global / code_global 之类，直接点明正确名字
        int aliasAt = IndexOfToken(masked, "my_global");
        if (aliasAt < 0) aliasAt = IndexOfToken(masked, "code_global");
        if (aliasAt >= 0)
        {
            layout.FatalError = GlobalFatal(LineOf(code, aliasAt),
                "关键字是 GlobalCode，不是 my_global / code_global —— 正确写法：GlobalCode { int count = 0; }",
                "The keyword is GlobalCode, not my_global / code_global - write: GlobalCode { int count = 0; }");
            return layout;
        }

        // 大小写纠错：GlobalCode 区分大小写（globalCode / Globalcode 都不是关键字），写错大小写直接点明
        if (IndexOfToken(masked, GlobalBlockKeyword) < 0)
        {
            int caseAt = IndexOfTokenIgnoreCase(masked, "globalcode");
            if (caseAt >= 0)
            {
                layout.FatalError = GlobalFatal(LineOf(code, caseAt),
                    "关键字区分大小写：要写 GlobalCode（G 和 C 都要大写）—— 不区分大小写的写法不会被当成全局区。",
                    "The keyword is case sensitive: write GlobalCode (capital G and C); other spellings are not the global block.");
                return layout;
            }
        }

        int kwStart = -1, braceOpen = -1, braceClose = -1;
        int found = 0;
        int scan = 0;
        while (scan < masked.Length)
        {
            int at = masked.IndexOf(GlobalBlockKeyword, scan, System.StringComparison.Ordinal);
            if (at < 0) break;
            scan = at + GlobalBlockKeyword.Length;

            bool leftOk = at == 0 || !IsIdentChar(masked[at - 1]);
            bool rightOk = scan >= masked.Length || !IsIdentChar(masked[scan]);
            if (!leftOk || !rightOk) continue; // GlobalCode_1 / xGlobalCode 之类只是普通标识符，不参与解析

            found++;
            int kwLine = LineOf(code, at);
            if (found > 1)
            {
                layout.FatalError = GlobalFatal(kwLine,
                    "只能有一个 GlobalCode 全局区，请把多个块合并成一个。",
                    "only one GlobalCode block is allowed; merge them into a single block.");
                return layout;
            }

            int p = scan;
            while (p < masked.Length && char.IsWhiteSpace(masked[p])) p++;
            if (p >= masked.Length || masked[p] != '{')
            {
                layout.FatalError = GlobalFatal(kwLine,
                    "GlobalCode 后面必须紧跟 { —— 正确写法是 GlobalCode { int count = 0; }；它不是变量名，不能写 = 值，也不能省大括号。",
                    "GlobalCode must be followed by { - e.g. GlobalCode { int count = 0; }. It is not a variable name: no '= value', no missing braces.");
                return layout;
            }

            kwStart = at;
            braceOpen = p;
            braceClose = -1;
            int depth = 0;
            for (int q = p; q < masked.Length; q++)
            {
                if (masked[q] == '{') depth++;
                else if (masked[q] == '}')
                {
                    depth--;
                    if (depth == 0) { braceClose = q; break; }
                }
            }
            if (braceClose < 0)
            {
                layout.FatalError = GlobalFatal(kwLine,
                    "GlobalCode 全局区缺少配对的 } —— 大括号必须成对，请在块尾补一个 }。",
                    "the GlobalCode block is missing its closing } - braces must pair up; add } at the end of the block.");
                return layout;
            }
        }

        // 没有全局区：整份代码原样进 Run 方法体，行号 = 行序
        if (found == 0)
        {
            string[] plain = code.Split('\n');
            for (int i = 0; i < plain.Length; i++)
            {
                layout.BodyLines.Add(plain[i]);
                layout.BodyUserLines.Add(i + 1);
            }
            return layout;
        }

        layout.HasGlobalBlock = true;

        // 全局区里不能出现 tower：它是 Run(tower) 的参数，只在每帧语句里存在
        if (ContainsToken(masked, braceOpen + 1, braceClose, "tower"))
        {
            layout.FatalError = GlobalFatal(LineOf(code, braceOpen),
                "GlobalCode 全局区里不能用 tower —— tower 只在 Run(tower) 里存在。全局区只写字段声明（int / float / bool / string / 数组 / List），要用炮台请写到下面的每帧语句里。",
                "tower cannot be used inside GlobalCode - it only exists in Run(tower). Declare fields only (int / float / bool / string / arrays / List) and use tower in the per-frame code below.");
            return layout;
        }

        // ---- 全局区：逐行取出，逐行记录玩家行号 ----
        int curLine = LineOf(code, braceOpen + 1);
        int segStart = braceOpen + 1;
        for (int i = braceOpen + 1; i <= braceClose; i++)
        {
            bool cut = i == braceClose || code[i] == '\n';
            if (!cut) continue;
            layout.FieldLines.Add(code.Substring(segStart, i - segStart));
            layout.FieldUserLines.Add(curLine);
            curLine++;
            segStart = i + 1;
            if (i == braceClose) break;
        }
        // 去掉块内首尾纯空白行（只是排版，不含玩家代码）
        while (layout.FieldLines.Count > 0 && layout.FieldLines[0].Trim().Length == 0)
        {
            layout.FieldLines.RemoveAt(0);
            layout.FieldUserLines.RemoveAt(0);
        }
        while (layout.FieldLines.Count > 0 && layout.FieldLines[layout.FieldLines.Count - 1].Trim().Length == 0)
        {
            layout.FieldLines.RemoveAt(layout.FieldLines.Count - 1);
            layout.FieldUserLines.RemoveAt(layout.FieldUserLines.Count - 1);
        }

        // ---- 每帧语句：全局区原地挖空（保留换行），行号与原文完全一致 ----
        char[] bodyBuf = code.ToCharArray();
        int blankEnd = braceClose < bodyBuf.Length ? braceClose : bodyBuf.Length - 1;
        for (int i = kwStart; i <= blankEnd; i++)
            if (bodyBuf[i] != '\n') bodyBuf[i] = ' ';
        string[] bodyLines = new string(bodyBuf).Split('\n');
        for (int i = 0; i < bodyLines.Length; i++)
        {
            if (bodyLines[i].Trim().Length == 0) continue;
            layout.BodyLines.Add(bodyLines[i]);
            layout.BodyUserLines.Add(i + 1);
        }
        return layout;
    }

    /// <summary>用模板包裹玩家代码：全局区字段拼到类体级别，其余语句留在 Run 方法体内；同时输出「模板行 → 玩家行」映射表。</summary>
    private static string WrapUserCode(UserCodeLayout layout, out string className, out int[] templateLineToUser)
    {
        StringBuilder idSb = new StringBuilder(8);
        const string hexChars = "0123456789abcdef";
        for (int i = 0; i < 8; i++)
            idSb.Append(hexChars[Random.Range(0, hexChars.Length)]);
        className = "UserTowerCode_" + idSb.ToString();

        List<string> outLines = new List<string>(layout.FieldLines.Count + layout.BodyLines.Count + 16);
        List<int> map = new List<int>(layout.FieldLines.Count + layout.BodyLines.Count + 16);

        System.Action<string, int> add = (text, userLine) => { outLines.Add(text); map.Add(userLine); };

        add("using UnityEngine;", 0);
        add("using System;", 0);
        add("using System.Collections;", 0);
        add("using System.Collections.Generic;", 0);
        add("", 0);
        add("public class " + className, 0);
        add("{", 0);
        add("// ===== USER GLOBAL START ===== (GlobalCode block: hoisted to class-level fields, survives frames)", 0);
        for (int i = 0; i < layout.FieldLines.Count; i++)
            add(layout.FieldLines[i], layout.FieldUserLines[i]);
        add("// ===== USER GLOBAL END =====", 0);
        add("    public void Run(CodeTower tower)", 0);
        add("    {", 0);
        add("// ===== USER CODE START =====", 0);
        for (int i = 0; i < layout.BodyLines.Count; i++)
            add(layout.BodyLines[i], layout.BodyUserLines[i]);
        add("// ===== USER CODE END =====", 0);
        add("    }", 0);
        add("}", 0);

        templateLineToUser = map.ToArray();

        StringBuilder sb = new StringBuilder(1024);
        for (int i = 0; i < outLines.Count; i++)
        {
            sb.Append(outLines[i]);
            if (i < outLines.Count - 1) sb.Append('\n');
        }
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>模板行号 → 玩家代码行号：取该行本身或 upward/downward 最近的玩家行；找不到返回 0（模板行）。</summary>
    private static int MapTemplateLine(int[] templateLineToUser, int templateLine)
    {
        if (templateLineToUser == null || templateLineToUser.Length == 0) return 0;
        int idx = templateLine - 1;
        if (idx < 0) idx = 0;
        if (idx >= templateLineToUser.Length) idx = templateLineToUser.Length - 1;
        for (int i = idx; i >= 0; i--)
            if (templateLineToUser[i] > 0) return templateLineToUser[i];
        for (int i = idx; i < templateLineToUser.Length; i++)
            if (templateLineToUser[i] > 0) return templateLineToUser[i];
        return 0;
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
                // GlobalCode 是本作扩展关键字（不是 C# 关键字）：单独给蓝色，和原生关键字的青区分开
                string wordColor = word == GlobalBlockKeyword
                    ? C_GLOBAL
                    : (keywords.Contains(word) ? C_KEYWORD : C_DEFAULT);
                AppendToken(sb, word, wordColor, smallSize);
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

    // ---------------- 分段编辑器（超长代码：多 InputField 分块，绕开 uGUI Text 单层 16383 顶点上限） ----------------
    //
    // 设计要点：
    //  1. 每块都是独立的原生 InputField + 独立高亮 Text，光标 / 选区 / 点击 / 双击 / 上下键 / Home/End / IME
    //     全部由 InputField 原生能力提供，不做任何自绘或解析式命中；
    //  2. 块高严格等于「行数 × 行高」（块间无额外内边距），因此全局第 k 行行顶 y 恒等于 -8 - k × 行高，
    //     现有滚动跟随 / 字号缩放锚定等按「全局行号」计算的逻辑无需改动即可继续成立；
    //  3. 全文低于 segEnableThreshold 时只有 1 块（就是原来的 codeRt + inputField），行为与原版完全一致。

    /// <summary>当前活动的输入框（光标所在分块；未分段时就是原 inputField）。</summary>
    private InputField ActiveField
    {
        get
        {
            // 以「真正获得焦点的块」为唯一真值：activeSegIndex 只是缓存，点击聚焦会滞后一帧、
            // 重建后 caret 也还没落定，缓存可能仍指向旧块。若缓存块没聚焦、却有另一块确实聚焦，
            // 一律以聚焦块为准——否则后续所有「块顶 + 块内光标」的换算都会拿错块顶，
            // 表现就是在文档末尾输入、视图却被拽回前面几百行。
            for (int i = 0; i < segments.Count; i++)
            {
                InputField focused = segments[i].field;
                if (focused != null && focused.isFocused)
                {
                    activeSegIndex = i;
                    break;
                }
            }
            if (activeSegIndex >= 0 && activeSegIndex < segments.Count && segments[activeSegIndex].field != null)
                return segments[activeSegIndex].field;
            return inputField;
        }
    }

    /// <summary>把输入框反查成分块下标（找不到返回 -1）。</summary>
    private int IndexOfField(InputField field)
    {
        if (field == null)
            return -1;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].field == field)
                return i;
        }
        return -1;
    }

    /// <summary>把现有单块 UI 登记为 segments[0]：阈值以下只用这一块，等价于原单块编辑。</summary>
    private void InitSegments()
    {
        segments.Clear();
        CodeSegment seg = new CodeSegment();
        seg.rt = codeRt;
        seg.field = inputField;
        seg.inputText = inputText;
        seg.highlightText = highlightText;
        seg.lineNumberText = lineNumberText;
        seg.lineRt = lineRt;
        seg.text = inputField != null ? inputField.text : string.Empty;
        seg.startLine = 0;
        seg.lineCount = CountLines(seg.text);
        seg.lineHeight = GetLineHeight();
        segments.Add(seg);
        EnsureCaretGuard();
        activeSegIndex = 0;
    }

    /// <summary>取编辑器内用户代码全文（各分块拼接）。所有需要「整段源码」的地方都必须用它，禁止直接读单个块的 text。</summary>
    private string GetFullCode()
    {
        if (segments.Count == 0)
            return inputField != null ? inputField.text : (lastCommittedText ?? string.Empty);
        if (segments.Count == 1)
            return segments[0].field != null ? segments[0].field.text : (inputField != null ? inputField.text : string.Empty);
        int total = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].field != null)
                total += segments[i].field.text.Length;
        }
        StringBuilder sb = new StringBuilder(total);
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].field != null)
                sb.Append(segments[i].field.text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 用全文重置编辑器内容：低于阈值走单块路径（与原版逐字等价），超阈值按行切分到多个分块，
    /// 并把光标尽量落到全局 caretGlobal 处（不要求逐字精确，但绝不丢内容）。
    /// </summary>
    private void SetFullCode(string code, int caretGlobal)
    {
        if (inputField == null)
            return;
        if (segments.Count == 0)
            InitSegments();
        code = code ?? string.Empty;
        if (code.Length <= segEnableThreshold)
        {
            // ---- 单块路径：只建 1 块，行为与改造前的单一 InputField 完全一致 ----
            suppressSegmentCallbacks = true;
            EnsureSegmentCount(1);
            CodeSegment seg = segments[0];
            if (seg.field != null)
                seg.field.SetTextWithoutNotify(code);
            seg.text = code;
            suppressSegmentCallbacks = false;
            activeSegIndex = 0;
            if (seg.field != null)
            {
                seg.field.caretPosition = Mathf.Clamp(caretGlobal, 0, code.Length);
                seg.field.selectionAnchorPosition = seg.field.caretPosition;
                seg.field.selectionFocusPosition = seg.field.caretPosition;
            }
            LayoutSegments();
            // 单块路径同样必须刷新「行号 / 状态栏 / 内容高 / 滚动范围」：
            // 只做 LayoutSegments 会留下上一份内容的行号文本与内容高度缓存——典型症状是 F8 载入一份 400 行文件后，
            // 左侧行号还停留在上一份文件的 17 行（行号不在 LayoutSegments 里生成，它只摆位置）。
            // 编译路径之所以「看起来正常」，是因为编译后还会走一遍 RefreshCodeView 把行号补上。
            RefreshLineNumbers(code);
            RefreshCursorStatus();
            SyncContentHeightAndScroll();
            return;
        }
        RebuildSegments(code, caretGlobal);
    }

    /// <summary>按行首边界把全文切成若干段，每段长度 ≤ budget，绝不拆行；单行本身超预算时该行独占一段。</summary>
    private static List<string> SplitCode(string code, int budget)
    {
        List<string> parts = new List<string>();
        if (string.IsNullOrEmpty(code))
        {
            parts.Add(string.Empty);
            return parts;
        }
        if (budget < 1)
            budget = 1;
        int start = 0;
        int len = code.Length;
        while (start < len)
        {
            if (len - start <= budget)
            {
                parts.Add(code.Substring(start));
                break;
            }
            int limit = start + budget;                 // 该段允许的最后位置（不含）
            int cut = code.LastIndexOf('\n', limit - 1, limit - start);
            if (cut < start)
            {
                // 预算内没有任何换行：单行超长，整行独占一段（含行尾换行）
                int nl = code.IndexOf('\n', start);
                cut = nl < 0 ? len - 1 : nl;
            }
            else
            {
                // 把切点后移到一个「空白行」的行尾：段边界落在空行上，
                // 段尾那条禁写斜纹带就永远压在一行空行上，不会切断某行逻辑代码。
                // 后移范围受单块硬上限 SegHardLimit 约束，找不到空行就保持原切点。
                cut = AlignCutToBlankLine(code, cut, Mathf.Min(len - 1, start + SegHardLimit - 1));
            }
            parts.Add(code.Substring(start, cut - start + 1)); // 含行尾换行符，拼回全文无缝
            start = cut + 1;
        }
        if (parts.Count == 0)
            parts.Add(string.Empty);
        return parts;
    }

    /// <summary>
    /// 把切点 cut（某个 '\n' 的下标）后移到最近一个「空白行」的行尾，使分段边界落在空行上：
    /// 段尾那条禁写斜纹带因此永远压在一行空行上，既不会切断某行逻辑代码，
    /// 也方便肉眼核对「斜纹带所在行 = 段尾空行」。
    /// 只在 [cut+1, maxCut] 范围内搜索；找不到空行、或后移会超出单块硬上限时，原样返回 cut。
    /// </summary>
    private static int AlignCutToBlankLine(string code, int cut, int maxCut)
    {
        if (string.IsNullOrEmpty(code))
            return cut;
        if (cut < 0 || cut >= code.Length - 1)
            return cut;
        if (maxCut <= cut)
            return cut;

        int p = cut + 1;
        while (p <= maxCut && p < code.Length)
        {
            int nl = code.IndexOf('\n', p);
            int end = nl < 0 ? code.Length - 1 : nl;      // 该行行尾 '\n' 的下标（最后一行取末字符）
            int i = p;
            int e = end;
            while (i < e && (code[i] == ' ' || code[i] == '\t' || code[i] == '\r'))
                i++;
            bool blank = i >= e;                          // 该行去掉缩进后没有内容
            if (blank && end <= maxCut)
                return end;                               // 空行归入本段末尾
            if (end >= maxCut)
                return cut;                               // 已到硬上限，放弃对齐
            p = end + 1;
        }
        return cut;
    }

    /// <summary>确保分块数量为 count：不足则动态创建，多余则失活销毁（segments[0] 永远复用原有 UI）。</summary>
    private void EnsureSegmentCount(int count)
    {
        if (count < 1)
            count = 1;
        while (segments.Count < count)
            CreateSegmentBlock(segments.Count);
        for (int i = segments.Count - 1; i >= count; i--)
        {
            CodeSegment seg = segments[i];
            segments.RemoveAt(i);
            if (seg.rt != null && seg.rt != codeRt)
            {
                seg.rt.gameObject.SetActive(false);
                Destroy(seg.rt.gameObject);
            }
            if (seg.lineNumberText != null && seg.lineNumberText != lineNumberText)
            {
                seg.lineNumberText.gameObject.SetActive(false);
                Destroy(seg.lineNumberText.gameObject);
            }
            if (seg.stripeBar != null)
            {
                seg.stripeBar.gameObject.SetActive(false);
                Destroy(seg.stripeBar.gameObject);
                seg.stripeBar = null;
            }
        }
        // 块数变化后斜纹带要重摆：新块 SetParent 到 codeRt 末尾会盖住旧条纹，故清空缓存让它重新定位并置顶
        for (int i = 0; i < segments.Count; i++)
        {
            segments[i].stripeVisible = false;
            segments[i].stripeLastTopY = float.NaN;
        }
        if (activeSegIndex >= segments.Count)
            activeSegIndex = Mathf.Max(0, segments.Count - 1);
    }

    /// <summary>
    /// 新建第 index 个分块：codeRt 下挂一个 RectTransform，内含该段自己的透明 inputText 与可见 highlightText；
    /// lineRt 下配一组行号 Text（显示全局行号）。块内高亮完全复用 HighlightCSharp，顶点数天然不超限。
    /// </summary>
    private void CreateSegmentBlock(int index)
    {
        CodeSegment seg = new CodeSegment();
        GameObject go = new GameObject("CodeSegment" + index, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(codeRt, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(codeRt.sizeDelta.x, GetLineHeight());

        Image bg = go.GetComponent<Image>();
        bg.color = UITheme.CodeBg;  // 点击本块空白区域也能聚焦本块的 InputField
        bg.raycastTarget = true;

        InputField field = go.AddComponent<InputField>();
        field.targetGraphic = bg;

        Text inText = CreateText("InputText", rt, "", codeFontSize, UITheme.WithAlpha(UITheme.TextPrimary, 0f), TextAnchor.UpperLeft);
        RectTransform inRt = inText.rectTransform;
        Stretch(inRt);
        inRt.offsetMin = new Vector2(12f, 8f);   // 与 segments[0] 的内边距一致，保证块间行距连续
        inRt.offsetMax = new Vector2(-12f, -8f);
        inText.supportRichText = false;
        inText.horizontalOverflow = HorizontalWrapMode.Overflow;
        inText.verticalOverflow = VerticalWrapMode.Overflow;
        inText.font = UIFontHelper.LoadMono();
        field.textComponent = inText;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.caretColor = UITheme.TextPrimary;
        field.caretBlinkRate = caretBlinkRate;
        field.caretWidth = 1;
        field.selectionColor = UITheme.WithAlpha(UITheme.CodeSelection, 0.26f);
        int captured = index;
        field.onValueChanged.AddListener(s => OnSegmentValueChanged(captured));

        Text hl = CreateText("HighlightText", rt, "", codeFontSize, UITheme.TextPrimary, TextAnchor.UpperLeft);
        RectTransform hlRt = hl.rectTransform;
        Stretch(hlRt);
        hlRt.offsetMin = new Vector2(12f, 8f);
        hlRt.offsetMax = new Vector2(-12f, -8f);
        hl.supportRichText = true;
        hl.horizontalOverflow = HorizontalWrapMode.Overflow;
        hl.verticalOverflow = VerticalWrapMode.Overflow;
        hl.font = UIFontHelper.LoadMono();
        hl.raycastTarget = false;

        Text ln = CreateText("LineNumbersText" + index, lineRt, "", codeFontSize, lineNumberColor, TextAnchor.UpperRight);
        ln.font = UIFontHelper.LoadMono();   // 与代码区同字体，行号行高才能与代码逐行对齐
        ln.supportRichText = false;
        ln.horizontalOverflow = HorizontalWrapMode.Overflow;
        ln.verticalOverflow = VerticalWrapMode.Overflow;

        seg.rt = rt;
        seg.field = field;
        seg.inputText = inText;
        seg.highlightText = hl;
        seg.lineNumberText = ln;
        seg.lineRt = lineRt;
        seg.text = string.Empty;
        seg.startLine = 0;
        seg.lineCount = 1;
        seg.lineHeight = GetLineHeight();
        segments.Add(seg);
        EnsureCaretGuard();
    }

    /// <summary>确保执行顺序守卫组件存在（挂在控制器同一 GameObject 上，只需要一个）。</summary>
    private void EnsureCaretGuard()
    {
        if (GetComponent<CodeEditorCaretGuard>() != null)
            return;
        CodeEditorCaretGuard guard = gameObject.AddComponent<CodeEditorCaretGuard>();
        guard.owner = this;
    }

    /// <summary>按行首边界把全文重新切分到各分块（重建块数 / 文本 / 高亮），并把光标尽量落到全局 caretGlobal 处。</summary>
    private void RebuildSegments(string code, int caretGlobal)
    {
        if (codeRt == null || lineRt == null)
            return;
        List<string> parts = SplitCode(code ?? string.Empty, Mathf.Max(1, segCharBudget));
        bool hadFocus = IsInputFocused();
        suppressSegmentCallbacks = true;
        EnsureSegmentCount(parts.Count);
        for (int i = 0; i < parts.Count && i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            if (seg.field == null)
                continue;
            seg.field.SetTextWithoutNotify(parts[i]);
            seg.text = parts[i];
            if (seg.highlightText != null)
                seg.highlightText.text = HighlightCSharp(seg.text);
        }
        suppressSegmentCallbacks = false;
        LayoutSegments();

        // 定位光标所在分块（前序块长度累计）
        int off = 0;
        activeSegIndex = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            int blockLen = segments[i].text != null ? segments[i].text.Length : 0;
            if (caretGlobal <= off + blockLen || i == segments.Count - 1)
            {
                activeSegIndex = i;
                break;
            }
            off += blockLen;
        }
        CodeSegment active = segments[activeSegIndex];
        if (active.field != null)
        {
            if (hadFocus && IsOpen)
            {
                active.field.ActivateInputField();
                active.field.Select();
            }
            int local = Mathf.Clamp(caretGlobal - off, 0, active.text != null ? active.text.Length : 0);
            active.field.caretPosition = local;
            active.field.selectionAnchorPosition = local;
            active.field.selectionFocusPosition = local;
        }
        // 重切分后同样要补刷行号与内容高度：LayoutSegments 只按 lineCount 摆位置，不生成行号文本，
        // 少了这一步，切分成多块的路径（载入大文件 / 超阈值重切）会继续显示切分前的旧行号。
        RefreshLineNumbers(GetFullCode());
        RefreshCursorStatus();
        SyncContentHeightAndScroll();
    }

    /// <summary>某分块文本变化后的联动：更新该段缓存 → 重算行数 / 块高 / 总高 / 滚动钳制；超出硬上限则标记延后重切。</summary>
    private void OnSegmentValueChanged(int index)
    {
        if (suppressSegmentCallbacks)
            return;
        if (index < 0 || index >= segments.Count)
            return;
        CodeSegment seg = segments[index];
        seg.text = seg.field != null ? seg.field.text : string.Empty;
        if (seg.highlightText != null)
            seg.highlightText.text = HighlightCSharp(seg.text);
        activeSegIndex = index;
        lastEditedSegIndex = index; // 告诉 RefreshCodeView：只有这一块内容变了，其余块沿用旧高亮
        typedThisFrame = true;      // 打字帧标记：Update 里的光标变化随之禁掉「向上回拉」，避免逐键把视图拉回文档前面
        RefreshCodeView();          // 与原版一致：重排高亮 / 行号 / 内容高度 / 滚动范围，并让光标行保持可见
        if (seg.text.Length > SegHardLimit)
            pendingSegmentRebuild = true; // 延后到 LateUpdate（本帧 InputField 处理完之后）再按行重切
        RecordSnapshotForUndo();
    }

    /// <summary>超出单块硬上限后按行重新切分全文，并把光标尽量落到原位置（内容绝不丢失）。</summary>
    private void ApplyPendingSegmentRebuild()
    {
        string full = GetFullCode();
        int caretGlobal = GlobalCaretPosition();
        // 重切分 = 全部块被重建，新块要下一帧才真正拿到焦点（isFocused 全 false、caretPosition 未落定），
        // 此时按 caret 跟随会把活动块判成 0 号块（块顶 0），视图瞬间跳回文档前面。冻结跟随保住原滚动位置。
        suppressScrollFollowOnce = true;
        scrollFollowSuppressUntil = Time.unscaledTime + 0.35f;
        RebuildSegments(full, caretGlobal);
        suppressScrollFollowOnce = true;
        RefreshCodeView();
        lastCaretPos = -1;
    }

    /// <summary>
    /// 点击守卫窗口推进：由 CodeEditorCaretGuard（executionOrder = 20000）的 LateUpdate 调用，
    /// 保证一定晚于 uGUI InputField 自身的 LateUpdate。InputField 的「首次激活」与 SetText 钳位都会
    /// 在它自己的 LateUpdate 里改写 m_CaretPosition / m_CaretSelectPosition，顺序稍有先后，写入就会被覆盖——
    /// 前几版守卫写在控制器的 LateUpdate（默认顺序 0）里，经常先于 InputField 执行，这就是改完仍然全选的原因。
    /// 本方法不再做任何「异常判定」：落点一旦算出，窗口内每帧无条件把锚点 / 焦点 / 光标压成点击处的同一个下标。
    /// </summary>
    public void LateGuardTick()
    {
        GuardWindowTick();
    }


    private void GuardWindowTick()
    {
        if (lastGuardFrame == Time.frameCount)
            return; // 同一帧多个守卫实例只推进一次
        lastGuardFrame = Time.frameCount;
        if (!clickCaretFixGuarding)
            return;
        if (!IsOpen)
        {
            EndClickGuard();
            return;
        }
        // 放手条件：指针拖离起点（框选）、按着 Shift（范围选择）、任何按键输入、超时。
        bool dragging = Vector2.Distance((Vector2)Input.mousePosition, clickCaretFixScreenPos) >= 6f;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool keyboardTouched = Input.inputString.Length > 0 ||
            Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
            Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
            Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.End) ||
            Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete) ||
            Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Tab);
        // 真正开始打字了就立刻放手：Input.inputString 在中文输入法提交时是空的，
        // 只靠按键判定会漏掉，窗口内继续每帧压回旧光标，新敲的字会被「吞」在原地 —— 表现就是写不进去字。
        // 这里直接以目标块文本长度是否变化作为「已打字」的判据，最可靠。
        // 按下 Ctrl+字母（复制 / 粘贴 / 剪切 / 撤销…）属于明确的键盘指令，必须立刻放手，
        // 否则守卫会在同一帧的 LateUpdate 把刚做好的选区重新压成零长度选区。
        bool ctrlCombo = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.V) ||
             Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Y));
        bool textEdited = false;
        if (clickCaretFixTextLen >= 0 && clickCaretFixSeg >= 0 && clickCaretFixSeg < segments.Count)
        {
            InputField gf = segments[clickCaretFixSeg].field;
            if (gf != null && gf.text != null && gf.text.Length != clickCaretFixTextLen)
                textEdited = true;
        }
        if (dragging || shift || keyboardTouched || ctrlCombo || textEdited ||
            Time.unscaledTime - clickCaretFixTime > ClickCaretFixWindow)
        {
            EndClickGuard();
            return;
        }
        ApplyClickCaret();
        // 点击后短窗口内冻结自动滚动跟随：InputField 每帧都可能改写 caret，跟随只会让内容上下抖动
        scrollFollowSuppressUntil = Mathf.Max(scrollFollowSuppressUntil, Time.unscaledTime + 0.2f);
    }

    /// <summary>
    /// 结束守卫：把光标基准（全局位置 / 块内位置 / 块号）同步成本帧真实值，
    /// 否则下一帧 Update 会把这次修正当成一次「光标上移」再触发一次滚动跟随，把视图拽回文档前面。
    /// </summary>
    private void EndClickGuard()
    {
        clickCaretFixGuarding = false;
        scrollFollowSuppressUntil = Time.unscaledTime + 0.3f;
        // 落点一个都没算出来 = 点击没落到任何分块（坐标换算 / 相机 / 命中判定有问题），
        // 这种情况守卫等于空转，必须留下日志，否则又会变成「改了没效果但查不出原因」。
        if (clickCaretFixStable == 0 && clickCaretFixSeg < 0)
            Debug.LogWarning("[CodeEditor][点击定位] 本次按下未命中任何分块：屏幕坐标=" + clickCaretFixScreenPos +
                             " 焦距=" + (clickCaretFixWasFocused ? "已有" : "首次") + " | " + clickCaretFixDiag);
        InputField f = clickCaretFixSeg >= 0 && clickCaretFixSeg < segments.Count ? segments[clickCaretFixSeg].field : null;
        if (f == null)
            return;
        lastCaretPosInSeg = Mathf.Clamp(f.caretPosition, 0, f.text.Length);
        lastCaretSegIndex = clickCaretFixSeg;
        lastCaretPos = GlobalCaretPosition();
    }

    /// <summary>把点击落点写进目标分块：锚点 / 焦点 / 光标三者同值，即零长度选区。落点只在第一次进来时换算一次并固定。</summary>
    private void ApplyClickCaret()
    {
        if (segments.Count == 0 || codeViewportRt == null)
            return;
        // 屏幕坐标换算必须带对相机：Screen Space - Overlay 用 null，Screen Space - Camera / World 必须给 UI 相机
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        Camera uiCam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        if (clickCaretFixSeg < 0 || clickCaretFixSeg >= segments.Count || segments[clickCaretFixSeg].field == null)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(codeViewportRt, clickCaretFixScreenPos, uiCam))
                return; // 点在滚动条 / 行号 / 编辑器外：不是代码区落点，不接管
            int hit = ResolveClickSegment(clickCaretFixScreenPos, uiCam);
            if (hit < 0)
                return;
            clickCaretFixSeg = hit;
            clickCaretFixIdx = ClickCharIndexInSegment(segments[hit], clickCaretFixScreenPos, uiCam);
        }
        if (clickCaretFixSeg < 0 || clickCaretFixSeg >= segments.Count || clickCaretFixIdx < 0)
            return;
        CodeSegment seg = segments[clickCaretFixSeg];
        InputField f = seg.field;
        if (f == null)
            return;
        int len = f.text != null ? f.text.Length : 0;
        int idx = Mathf.Clamp(clickCaretFixIdx, 0, len);
        activeSegIndex = clickCaretFixSeg;
        // 未聚焦也照写：uGUI 的 OnPointerDown 在「按下前没有焦点」时根本不设光标（hadFocusBefore 分支），
        // 光标会停留在旧值上；我们先把目标值写好，等它 ActivateInputFieldInternal 完成后读到的就是正确位置。
        if (f.selectionAnchorPosition == idx && f.selectionFocusPosition == idx && f.caretPosition == idx)
        {
            scrollFollowSuppressUntil = Mathf.Max(scrollFollowSuppressUntil, Time.unscaledTime + 0.1f);
            return;
        }
        f.caretPosition = idx;              // caretPosition 的 setter 内部会同时写锚点与焦点
        f.selectionAnchorPosition = idx;
        f.selectionFocusPosition = idx;
        clickCaretFixStable++;
        if (clickCaretFixStable == 1)
        {
            clickCaretFixTextLen = len; // 记下写入瞬间的文本长度：之后长度一旦变化就说明用户已经在打字
            Debug.Log("[CodeEditor][点击定位] 块" + clickCaretFixSeg + " 文本长=" + len +
                      " 焦距=" + (clickCaretFixWasFocused ? "已有" : "首次") + " → 光标=" + idx + " | " + clickCaretFixDiag);
        }
    }

    /// <summary>
    /// 定位本次点击真正落在哪个分块上。块 rect 之间可能存在重叠（块高按「只许偏大」的策略估算），
    /// 单纯按「第一个包含该点的块」判定会落到上一块，把修正做到错误的输入框上（被点的那块依旧整块全选），
    /// 所以优先认「已聚焦且包含该点」的块，其次在包含该点的块里取高度最小的那块。
    /// </summary>
    private int ResolveClickSegment(Vector2 screenPos, Camera uiCam)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            RectTransform rt = segments[i].rt;
            if (rt == null || segments[i].field == null || !rt.gameObject.activeInHierarchy)
                continue;
            if (segments[i].field.isFocused &&
                RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, uiCam))
                return i;
        }
        int hit = -1;
        float best = float.MaxValue;
        for (int i = 0; i < segments.Count; i++)
        {
            RectTransform rt = segments[i].rt;
            if (rt == null || !rt.gameObject.activeInHierarchy)
                continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, uiCam))
                continue;
            float h = Mathf.Abs(rt.rect.height);
            if (h < best)
            {
                best = h;
                hit = i;
            }
        }
        return hit;
    }

    /// <summary>
    /// 把屏幕坐标换算成分块内的字符索引。行列一律取 InputField 自己的 Text 组件（field.textComponent，
    /// 纯文本、不含富文本标签）的排版结果——它与 InputField 内部 m_Text 一一对应，行高 / 行顶 / 内边距
    /// 也和 InputField 自己算光标时完全相同。旧版拿高亮层（带 &lt;color&gt; 标签）的排版去对纯文本下标，
    /// 标签不占字符却会改变行数判断，行列算偏后就表现为「点上半部分选到块首、点下半部分选到块尾」。
    /// </summary>
    private int ClickCharIndexInSegment(CodeSegment seg, Vector2 screenPos, Camera uiCam)
    {
        clickCaretFixDiag = "";
        if (seg == null || seg.rt == null || seg.field == null)
            return -1;
        string text = seg.field.text ?? string.Empty;
        if (text.Length == 0)
            return 0;
        Text tc = seg.field.textComponent;
        if (tc == null || tc.rectTransform == null)
            return -1;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(tc.rectTransform, screenPos, uiCam, out local))
            return -1;
        // 首选 uGUI InputField 自己的落点算法（GetCharacterIndexFromPosition，私有方法反射调用）：
        // 它按 textComponent 的真实排版逐字累加宽度，行顶 / 行高 / 内边距 / 制表符口径与 InputField
        // 内部推进光标时完全一致，不会因为「行高猜大猜小」或「行顶符号猜反」整块偏移。
        // 自研几何换算只在反射不可用时兜底。
        int official = InputFieldCharIndexFromPosition(seg.field, local);
        if (official >= 0 && official <= text.Length)
        {
            clickCaretFixDiag = "官方落点=" + official + "/" + text.Length +
                                " 局部=(" + local.x.ToString("0.0") + "," + local.y.ToString("0.0") + ")";
            return official;
        }
        Rect tr = tc.rectTransform.rect;
        float yInText = tr.yMax - local.y;   // 自文本顶向下的距离
        float xInText = local.x - tr.xMin;
        int totalLines = CountLines(text);
        TextGenerator gen = tc.cachedTextGenerator;
        int line = -1;
        if (gen != null && gen.lineCount > 0 && gen.lineCount == totalLines)
        {
            // UILineInfo.topY = 该行顶相对文本首行顶的偏移，**向下为负**。所以行 i 覆盖的「自顶向下」
            // 区间是 [-topY, -topY + height)。旧版写成 yInText < topY + height（少一个负号），
            // 条件几乎永不成立，一路兜底成最后一行 —— 这正是「点哪都跳到文末」的直接原因。
            // 极少数字体给正 topY，这里用首行符号自动判方向，两种约定都能算对。
            float sign = gen.lines[0].topY <= 0f ? -1f : 1f;
            for (int i = 0; i < gen.lineCount; i++)
            {
                UILineInfo info = gen.lines[i];
                float lhGen = info.height > 1f ? info.height : Mathf.Max(1f, GetLineHeight());
                float top = sign * info.topY;
                if (yInText < top + lhGen)
                {
                    line = i;
                    break;
                }
            }
            if (line < 0)
                line = gen.lineCount - 1;
            if (yInText < 0f)
                line = 0; // 点在文本上方的内边距区：归到首行
        }
        if (line < 0)
        {
            // 排版尚未就绪（块刚建 / 文本刚换）：退回行高估算。偏差最多一行，
            // 但绝不会像旧逻辑那样退化成 0 或末尾那种「整块全选」的极端值。
            float lh = Mathf.Max(1f, GetLineHeight());
            line = Mathf.Clamp(Mathf.FloorToInt(yInText / lh), 0, Mathf.Max(0, totalLines - 1));
            clickCaretFixDiag = "估算行=" + line + "/" + totalLines;
        }
        line = Mathf.Clamp(line, 0, Mathf.Max(0, totalLines - 1));
        int start = 0;
        int cur = 0;
        for (int i = 0; i < text.Length && cur < line; i++)
        {
            if (text[i] == '\n')
            {
                cur++;
                start = i + 1;
            }
        }
        if (start > text.Length)
            start = text.Length;
        int end = text.IndexOf('\n', start);
        if (end < 0)
            end = text.Length;
        float charW = Mathf.Max(1f, GetCharWidthForSegment(seg));
        int col = Mathf.Max(0, Mathf.FloorToInt((xInText + charW * 0.5f) / charW));
        int idx = Mathf.Clamp(start + col, start, end);
        clickCaretFixDiag = "排版行=" + line + "/" + (gen != null ? gen.lineCount : 0) +
                            " yInText=" + yInText.ToString("0.0") + " x=" + xInText.ToString("0.0") +
                            " 列=" + col + " 字宽=" + charW.ToString("0.0");
        return idx;
    }

    /// <summary>
    /// 反射调用 uGUI 的 InputField.GetCharacterIndexFromPosition（私有）：这就是 InputField 自己在
    /// OnPointerDown 里把鼠标位置换成字符下标的那段算法。用它替代自研几何换算，落点与「已聚焦时点击」
    /// 完全一致（InputField 内部对 rich text、制表符、行间距的处理都算在内）。调用失败返回 -1 走兜底。
    /// </summary>
    private int InputFieldCharIndexFromPosition(InputField f, Vector2 localPosInTextComponent)
    {
        if (f == null)
            return -1;
        try
        {
            if (miInputFieldCharIndex == null)
                miInputFieldCharIndex = typeof(InputField).GetMethod(
                    "GetCharacterIndexFromPosition",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (miInputFieldCharIndex == null)
                return -1;
            object r = miInputFieldCharIndex.Invoke(f, new object[] { localPosInTextComponent });
            if (r is int)
                return (int)r;
        }
        catch
        {
            // 反射失败（ugui 版本差异 / 方法改名）：交给调用方走几何兜底，绝不因此中断点击
        }
        return -1;
    }

    /// <summary>分块所用等宽字体的单字符前进宽度（取不到时按字号估算）。</summary>
    private float GetCharWidthForSegment(CodeSegment seg)
    {
        Text src = seg != null && seg.highlightText != null ? seg.highlightText : null;
        if (src == null && inputField != null)
            src = inputField.textComponent;
        if (src != null && src.font != null)
        {
            CharacterInfo info;
            int size = Mathf.Max(1, src.fontSize);
            if (src.font.GetCharacterInfo('M', out info, size) && info.advance > 0f)
                return info.advance;
        }
        return Mathf.Max(1f, codeFontSize * 0.6f);
    }

    /// <summary>当前光标在全文中的全局偏移（前序块长度累计 + 块内光标）。</summary>
    private int GlobalCaretPosition()
    {
        InputField activeInput = ActiveField;
        if (activeInput == null)
            return 0;
        int idx = IndexOfField(activeInput);
        int off = 0;
        for (int i = 0; i < idx && i < segments.Count; i++)
        {
            if (segments[i].field != null)
                off += segments[i].field.text.Length;
        }
        return off + Mathf.Clamp(activeInput.caretPosition, 0, activeInput.text.Length);
    }

    /// <summary>
    /// 取当前字号下的真实单行行高（像素）。uGUI Text 的实际行距由字体度量（font.lineHeight）
    /// 与 lineSpacing 共同决定，并不等于「字号 × 常数」；若布局按常数硬算，每行会积累 1px 左右的
    /// 偏差，长文档数百行后行号与代码明显错位、分段块之间露出成片空白。这里直接用 TextGenerator
    /// 量一次两行文本的行距，作为唯一行高来源（字号 / 行距倍数变化时自动重测）。
    /// </summary>
    private float GetLineHeight()
    {
        if (measuredLineHeight > 0f
            && measuredLineHeightFontSize == codeFontSize
            && Mathf.Approximately(measuredLineHeightFactor, lineHeightFactor))
            return measuredLineHeight;

        float factor = lineHeightFactor > 0f ? lineHeightFactor : 1f;
        float fallback = Mathf.Max(1f, codeFontSize * factor);

        Text sample = highlightText;
        if (sample == null && segments.Count > 0 && segments[0] != null)
            sample = segments[0].highlightText;
        if (sample == null)
            return fallback;

        // 真实行高 = 实测内容高度 ÷ 行数。这两个量都是 Text 的本地单位，除出来就是「每行占用多少本地高度」，
        // 与 MeasureSegmentHeight 用的 preferredHeight 完全同源 —— 布局里「行数 × 行高」与「实测高度」
        // 必然相等，不会一大一小。旧实现另起一个 TextGenerator 去 Populate("Ag\nAg\nAg") 测行距，
        // 拿到的是另一套单位（实测 22，而真实排版折算是 34.13，差 1.55 倍 = Canvas 的 scaleFactor），
        // 点击落点换算与滚动跟随全按错的行走位 —— 这才是「点上半部分跳到文首、点下半部分跳到文末」的根源。
        int sampleLines = 0;
        try { sampleLines = CountLines(sample.text); } catch { sampleLines = 0; }
        float ph = 0f;
        try { ph = sample.preferredHeight; } catch { ph = 0f; }
        if (sampleLines >= 2 && ph > 1f && !float.IsNaN(ph) && !float.IsInfinity(ph))
        {
            float dy = ph / sampleLines;
            if (dy > 0.5f)
            {
                measuredLineHeight = dy;
                measuredLineHeightFontSize = codeFontSize;
                measuredLineHeightFactor = lineHeightFactor;
                return dy;
            }
        }
        return fallback;
    }

    /// <summary>
    /// 取单个分块的真实内容高度（像素）：以高亮文本自己排版出的 preferredHeight 为准，取不到才退回
    /// 「行数 × 实测行高」。块间位置必须用这个高度累加——按「行数 × 常数」估高时只要估小一点点，
    /// 上一块最后几行就会压到下一块开头（表现为 200 行的末尾与 400 行的开头叠在一起），估大则留缝隙。
    /// </summary>
    private float MeasureSegmentHeight(CodeSegment seg, int lineCount, float lineHeight, out float preferred)
    {
        float nominal = Mathf.Max(1, lineCount) * lineHeight;
        preferred = 0f;
        Text measure = seg.highlightText != null ? seg.highlightText : seg.inputText;
        if (measure == null)
            return nominal;
        float pref = 0f;
        try { pref = measure.preferredHeight; }
        catch { pref = 0f; }
        if (pref <= 1f || float.IsNaN(pref) || float.IsInfinity(pref))
            return nominal;
        preferred = pref;
        return Mathf.Max(pref, nominal);
    }

    /// <summary>
    /// 实测单个分块「真实渲染出来多少行」：直接拿该块高亮层 Text 的生成设置，对当前文本做一次排版，
    /// 取生成器输出的行数。它与 CountLines（只数 '\n'）不同——富文本标签、'\r\n'、零宽字符、
    /// 制表符等都可能让实际排版行数与 '\n' 计数不一致，一旦实际行数更多，
    /// 「行数 × 行高」算出的块高就会偏小，上一块末尾溢出压到下一块开头（200 与 400 糊在一起）。
    /// 宽度给一个足够大的值，保证 Overflow / Wrap 两种模式下都不会被强行折行。
    /// </summary>
    private readonly TextGenerator measureGenerator = new TextGenerator();

    private int MeasureRenderedLines(Text t, string text)
    {
        if (t == null || string.IsNullOrEmpty(text))
            return 1;
        try
        {
            TextGenerationSettings gs = t.GetGenerationSettings(new Vector2(99999f, 0f));
            measureGenerator.Populate(text, gs);
            int n = measureGenerator.lineCount;
            if (n > 0)
                return n;
        }
        catch
        {
            // 排版测量失败（字体未就绪等）时退回 '\n' 计数
        }
        return 0;
    }

    /// <summary>
    /// 分块布局：计算各块行数 / 起始全局行号 / 高度，按前序块高度累计摆放块位置（无间隙），
    /// 并同步各块行号 Text 的位置与高度。单块时不做任何位置改动，保持与原版完全一致。
    /// </summary>
    /// <summary>
    /// 分块布局签名：块数 + 各块行数 + 字号 + 行距 + 视口高度。
    /// 这些量任何一个变化，块高 / 块位置才需要重算；只是行内增删字符时签名不变，
    /// LayoutSegments 可以整体跳过（这是「打字卡、写不进字」的主要性能闸门）。
    /// </summary>
    private string BuildLayoutSignature()
    {
        string sig = segments.Count + "|" + codeFontSize + "|" + lineHeightFactor.ToString("0.###") + "|" +
                     (codeViewportRt != null ? codeViewportRt.rect.height.ToString("0.#") : "0");
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            string t = seg.field != null ? seg.field.text : (seg.text ?? string.Empty);
            sig += "|" + i + ":" + CountLines(t);
        }
        return sig;
    }

    private void LayoutSegments()
    {
        if (segments.Count == 0)
        {
            lastLayoutSignature = string.Empty;
            return;
        }
        // 快速路径：块数、各块行数、字号、行距、视口高度都没变（普通的行内打字 / 删字）时，
        // 块高与块位置必然不变 —— 直接跳过全量重排。
        // 以前每次按键都会把每块 1.4 万字符重新 Populate 一次、再取 preferredHeight 实测、
        // 再打一条诊断日志（含堆栈），帧时间被拉到几十毫秒，键盘输入被吞掉，
        // 用户感受就是「写不进去字」。
        string sig = BuildLayoutSignature();
        if (sig == lastLayoutSignature)
            return;
        lastLayoutSignature = sig;
        float lineHeight = GetLineHeight();
        bool multi = segments.Count > 1;

        // 第一遍：算各块行数 / 起始全局行号（先得总量，末块才能拉满到内容底部，
        // 否则代码下方的大片空白仍归 block0 的 Image 命中，点一下光标会莫名跳回文档开头附近）
        int totalLines = 0;
        float contentSum = 0f;
        float[] segHeights = new float[segments.Count];
        int[] segRawLines = new int[segments.Count];   // 诊断用：块内实际渲染行数（含段尾空行）
        float[] segPreferred = new float[segments.Count]; // 诊断用：块内 preferredHeight 实测值
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            string t = seg.field != null ? seg.field.text : (seg.text ?? string.Empty);
            seg.text = t;
            // 块尾的换行符属于下一块的首行：内部块少算 1 行，否则块高与全局行号会重复计 1 行
            // （设计上每段都含行尾 '\n'，最后一段若也以 '\n' 结尾则保留该空行行号，与原版 CountLines 口径一致）
            int countLines = CountLines(t);
            // 真实排版行数可能比 '\n' 计数更多（富文本标签 / '\r\n' / 特殊字符），块高必须按二者的
            // 较大值算——只要估矮一行，这一行就会掉进下一块的地盘，形成「200 与 400 重叠」。
            int renderedLines = MeasureRenderedLines(seg.highlightText, t);
            int rawLines = Mathf.Max(1, Mathf.Max(countLines, renderedLines));
            segRawLines[i] = rawLines;
            bool endsWithNewline = t.Length > 0 && t[t.Length - 1] == '\n';
            int lines = (i < segments.Count - 1 && endsWithNewline) ? Mathf.Max(1, countLines - 1) : countLines;
            seg.lineCount = lines;
            seg.lineHeight = lineHeight;
            totalLines += lines;
            float preferred;
            float measured = MeasureSegmentHeight(seg, lines, lineHeight, out preferred);
            segPreferred[i] = preferred;
            // 块高口径（v5 定稿，确定性、零累积误差）：
            //   块高 = 块内「实际渲染行数 rawLines」× 行高 + 上下内边距 16
            // 其中 rawLines 含段尾 '\n' 渲染出的那一行空行 —— 它正是斜纹带压住的禁写区。
            // 为什么不用 preferredHeight：它随字体度量/富文本标签浮动，只要比真实排版估小一点，
            // 上一块末尾几行就会压到下一块开头（表现就是「200 行末尾和 400 行开头叠在一起」）；
            // 而「行数 × 实测行高」是无漂移的几何量，409 行也不会放大成几十行误差。
            // 为什么额外 + 16：高亮层/输入层都有 8px 的上下内缩（offsetMax.y / offsetMin.y），
            // 文字从块顶 +8 开始画，若块高只给 rawLines×行高，最后一行会顶出块底 8px，
            // 溢出到下一块区域内形成「视觉重叠」。把内边距整段还给块高，块间才是真正零重叠。
            float hardHeight = rawLines * lineHeight + 16f;
            // 所有块（含内部块）一律取「几何高」与「实测高」的较大值：块高只允许偏大、绝不允许偏小。
            // 偏大只是块底多出几像素同色留白（看不见），偏小就会让上一块末尾压到下一块开头。
            segHeights[i] = Mathf.Max(measured, hardHeight);
            contentSum += segHeights[i];
        }
        float viewH = codeViewportRt != null ? codeViewportRt.rect.height : 0f;
        float contentH = Mathf.Max(viewH, Mathf.Max(contentSum, totalLines * lineHeight) + 16f + lineHeight);

        // 第二遍：按前序块高度累计摆放（块间无间隙）
        float cumY = 0f;
        int cumLines = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            seg.startLine = cumLines;   // 该块首行的全局行号偏移（首行全局行号 = startLine + 1）
            seg.topY = cumY;           // 该块实际块顶（块高按真实排版高度累加，可能略大于「行数×行高」）
            float segH = segHeights[i];

            if (multi)
            {
                if (seg.rt != null)
                {
                    seg.rt.anchoredPosition = new Vector2(0f, -cumY);
                    // 末块拉满到内容底部：代码下方空白区域也能命中本块，点击不会把光标甩到首块。
                    // 首块也一并设高度（cumY=0），否则它的命中区仍是 BuildUI 时的初始高度，
                    // 文字画在框外、点击落空 —— 视觉上也会和下一块「贴在一起」。
                    float blockH = segH;
                    if (i == segments.Count - 1)
                        blockH = Mathf.Max(segH, contentH - cumY);
                    seg.rt.sizeDelta = new Vector2(seg.rt.sizeDelta.x, blockH);
                }
                if (seg.lineNumberText != null)
                {
                    RectTransform lnRt = seg.lineNumberText.rectTransform;
                    lnRt.anchorMin = new Vector2(0f, 1f);
                    lnRt.anchorMax = new Vector2(1f, 1f);
                    lnRt.pivot = new Vector2(0.5f, 1f);
                    // 行号首行顶 y = -(块顶 + 6)，与代码文字顶 y = -(块顶 + 8) 保持原有 2px 视觉差
                    lnRt.offsetMin = new Vector2(4f, -(cumY + 6f + segH + 4f));
                    lnRt.offsetMax = new Vector2(-8f, -(cumY + 6f));
                }
            }
            else if (seg.lineNumberText != null)
            {
                // 单块（未达分段阈值）：恢复 BuildUI 时的原始四边拉伸，保证与原版行号渲染完全一致
                RectTransform lnRt = seg.lineNumberText.rectTransform;
                lnRt.anchorMin = Vector2.zero;
                lnRt.anchorMax = Vector2.one;
                lnRt.pivot = new Vector2(0.5f, 0.5f);
                lnRt.offsetMin = new Vector2(4f, 6f);
                lnRt.offsetMax = new Vector2(-8f, -6f);
            }

            if (multi)
            {
                // 斜纹带 = 本块段尾那一行空行的位置，用「行数 × 行高」直接定位，不走任何估高。
                // 上一版用 cumY + segH - lineHeight 反推块底再往上取一行，一旦 segH 被 preferredHeight
                // 撑大，带子就会离开空行、跑到块内别的代码行上（表现为"某行的空行虚线跑到了别的行"）。
                // 现在块高本身已是「行数×行高 + 一行」，空行顶 = 块顶 + padTop + 行数×行高，
                // 带子从空行顶往下铺满（顶部让出 padTop，避免压住上一行代码的下缘）。
                float padLeft = 12f;
                float padTop = 8f;
                if (seg.highlightText != null)
                {
                    padLeft = Mathf.Max(0f, seg.highlightText.rectTransform.offsetMin.x);
                    padTop = Mathf.Max(0f, -seg.highlightText.rectTransform.offsetMax.y);
                }
                bool hasTailBlank = i < segments.Count - 1;   // 只有内部段（非最后一块）才有段尾空档
                // 段尾空档定位置的两个基准，取更靠下的那个：
                //  ① 按行数推：块顶 + 顶部内边距 + 文本行数 × 行高（文本真正结束的位置）；
                //  ② 按块底推：块底让出底部内边距后往上一整行（块高 = 实际渲染行数×行高+16，最后一行就是那段空行）。
                // 只用 ① 时，若 lineCount 把段尾换行符也算成一行，基准会直接落在块底，算出的空档高度为 0；
                // 只用 ② 时，若块高被 preferredHeight 撑大，带子会跑到块底的空白里、离开代码行。
                // 两个基准取 Max 既能贴住真实文本结束处，也保证只要块尾还有一行，带子就一定画得出来。
                float blockBottom = cumY + segH - padTop;
                float blankTopByText = cumY + seg.lineCount * lineHeight + padTop;
                float blankTopByTail = blockBottom - lineHeight;
                float blankTop = Mathf.Max(blankTopByText, blankTopByTail);
                float tailRoom = blockBottom - blankTop;                    // 块底还剩多少高度画带子
                float stripeH = Mathf.Clamp(tailRoom, Mathf.Min(lineHeight * 0.5f, 3f), lineHeight);
                float stripeW = Mathf.Max(codeRt != null ? codeRt.sizeDelta.x : 0f, codeContentWidth) - padLeft * 2f;
                if (!hasTailBlank || stripeW <= 1f)
                    HideSegmentStripe(seg);   // 最后一块没有段尾空档；宽度不够也不画
                else
                    UpdateSegmentStripe(seg, i, blankTop, stripeH, stripeW, padLeft);
            }
            else
            {
                HideSegmentStripe(seg);
            }

            cumY += segH;
            cumLines += seg.lineCount;
        }

        LogSegmentLayoutDiag(segments, segRawLines, segPreferred, segHeights, lineHeight);
    }

    /// <summary>
    /// 分段布局诊断：仅当各块行数组合发生变化时打印一次（打字过程不会刷屏）。
    /// 用来核对「块高 / 实测行高 / 行数」三者是否自洽——斜纹带位置异常时看这份日志最快。
    /// </summary>
    private void LogSegmentLayoutDiag(List<CodeSegment> segs, int[] rawLines, float[] preferred, float[] heights, float lineHeight)
    {
        if (segs == null || segs.Count < 2 || rawLines == null || heights == null)
            return;
        StringBuilder sigBuilder = new StringBuilder();
        for (int i = 0; i < segs.Count; i++)
            sigBuilder.Append(rawLines[i]).Append('_').Append(segs[i].lineCount).Append('|');
        string sig = sigBuilder.ToString();
        if (sig == lastLayoutDiagSig)
            return;
        lastLayoutDiagSig = sig;

        StringBuilder sb = new StringBuilder();
        sb.Append("[CodeEditor][分段布局] 行高=").Append(lineHeight.ToString("F2"))
          .Append(" 块数=").Append(segs.Count);
        float cumTop = 0f;
        for (int i = 0; i < segs.Count; i++)
        {
            float h = heights[i];
            float need = segs[i].lineCount * lineHeight + 16f;
            sb.Append("\n  块").Append(i)
              .Append(" 块顶=").Append(cumTop.ToString("F1"))
              .Append(" 首行号=").Append(segs[i].startLine + 1)
              .Append(" 行数=").Append(segs[i].lineCount)
              .Append(" 实测行数=").Append(rawLines[i])
              .Append(" 实测高度=").Append(preferred[i].ToString("F1"))
              .Append(" 块高=").Append(h.ToString("F1"))
              .Append(" 行数×行高=").Append((segs[i].lineCount * lineHeight).ToString("F1"))
              .Append(" 差=").Append((h - segs[i].lineCount * lineHeight).ToString("F1"))
              .Append(" 需要≥").Append(need.ToString("F1"));
            if (h < need - 0.5f)
                sb.Append(" ⚠块高偏小，会压到下一块");
            cumTop += h;
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 刷新某分块块尾的「禁写」斜纹带：topY 为真实代码行结束处（块顶下方 lineCount 行），height 为多出的空档高度。
    /// 值未变化时不重设 rect / 不变更渲染顺序，避免每帧触发 Canvas 重建。
    /// </summary>
    private void UpdateSegmentStripe(CodeSegment seg, int index, float topY, float height, float width, float leftPad)
    {
        if (seg == null)
            return;
        if (height <= 2f || width <= 2f)
        {
            HideSegmentStripe(seg);
            return;
        }
        if (seg.stripeBar == null)
            EnsureStripeBar(seg, index);
        if (seg.stripeBar == null)
            return;

        RectTransform rt = seg.stripeBar.rectTransform;
        if (!seg.stripeBar.gameObject.activeSelf)
            seg.stripeBar.gameObject.SetActive(true);

        bool changed = !seg.stripeVisible
                       || !Mathf.Approximately(seg.stripeLastTopY, topY)
                       || !Mathf.Approximately(seg.stripeLastH, height)
                       || !Mathf.Approximately(seg.stripeLastW, width)
                       || !Mathf.Approximately(seg.stripeLastLeft, leftPad);
        if (!changed)
            return;

        rt.anchoredPosition = new Vector2(leftPad, -topY);
        rt.sizeDelta = new Vector2(width, height);
        // uvRect 按实际像素 1:1 平铺，斜线粗细不随带宽拉伸而变形
        seg.stripeBar.uvRect = new Rect(0f, 0f, width / StripeTexSize, height / StripeTexSize);
        seg.stripeLastTopY = topY;
        seg.stripeLastH = height;
        seg.stripeLastW = width;
        seg.stripeLastLeft = leftPad;
        seg.stripeVisible = true;
        // 斜纹带必须画在所属分块之上（分块是不透明的 CodeBg Image，后渲染会遮住条纹）
        rt.SetAsLastSibling();
    }

    /// <summary>隐藏某分块的斜纹带（未分段 / 无空档 / 块被销毁时）。</summary>
    private void HideSegmentStripe(CodeSegment seg)
    {
        if (seg == null || seg.stripeBar == null)
            return;
        seg.stripeVisible = false;
        seg.stripeLastTopY = float.NaN;
        if (seg.stripeBar.gameObject.activeSelf)
            seg.stripeBar.gameObject.SetActive(false);
    }

    /// <summary>按需为分块创建斜纹带节点（RawImage + 运行时生成的 Repeat 斜线纹理）。</summary>
    private void EnsureStripeBar(CodeSegment seg, int index)
    {
        if (seg == null || codeRt == null || seg.stripeBar != null)
            return;
        RectTransform rt = CreateRect("SegmentStripe" + index, codeRt, new Vector2(0f, 1f), Vector2.zero, new Vector2(codeRt.sizeDelta.x, GetLineHeight()), new Vector2(0f, 1f));
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;

        RawImage raw = rt.gameObject.AddComponent<RawImage>();
        if (segmentStripeTex == null)
            segmentStripeTex = CreateDiagonalStripeTexture(StripeTexSize, 2);
        raw.texture = segmentStripeTex;
        raw.color = UITheme.WithAlpha(UITheme.Accent, SegmentStripeAlpha);
        raw.raycastTarget = false;   // 纯视觉标记，不抢分块的点击聚焦
        raw.uvRect = new Rect(0f, 0f, rt.sizeDelta.x / StripeTexSize, rt.sizeDelta.y / StripeTexSize);

        seg.stripeBar = raw;
        seg.stripeVisible = false;
        seg.stripeLastTopY = float.NaN;
        seg.stripeLastH = -1f;
        seg.stripeLastW = -1f;
        seg.stripeLastLeft = -1f;
    }

    /// <summary>生成 45° 斜线平铺纹理（白色斜线 + 透明底）；染色交给 RawImage.color（主题色）。</summary>
    private static Texture2D CreateDiagonalStripeTexture(int size, int thickness)
    {
        if (size < 4)
            size = 4;
        if (thickness < 1)
            thickness = 1;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Point;
        tex.hideFlags = HideFlags.HideAndDontSave;
        Color32 clear = new Color32(255, 255, 255, 0);
        Color32 line = new Color32(255, 255, 255, 255);
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                px[y * size + x] = ((x + y) % size) < thickness ? line : clear;
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>所有分块的输入层 / 高亮层 / 行号层统一应用当前字号。</summary>
    private void ApplyFontSizeToAllSegments()
    {
        float spacing = lineHeightFactor > 0f ? lineHeightFactor : 1f;
        measuredLineHeight = -1f;   // 字号 / 行距变化后行高必须重测
        if (segments.Count == 0)
        {
            if (inputText != null)
            {
                inputText.fontSize = codeFontSize;
                inputText.lineSpacing = spacing;
            }
            if (highlightText != null)
            {
                highlightText.fontSize = codeFontSize;
                highlightText.lineSpacing = spacing;
            }
            if (lineNumberText != null)
            {
                lineNumberText.fontSize = codeFontSize;
                lineNumberText.lineSpacing = spacing;
            }
            return;
        }
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            if (seg.inputText != null)
            {
                seg.inputText.fontSize = codeFontSize;
                seg.inputText.font = UIFontHelper.LoadMono();
                seg.inputText.lineSpacing = spacing;
            }
            if (seg.highlightText != null)
            {
                seg.highlightText.fontSize = codeFontSize;
                seg.highlightText.font = UIFontHelper.LoadMono();
                seg.highlightText.lineSpacing = spacing;
            }
            if (seg.lineNumberText != null)
            {
                seg.lineNumberText.fontSize = codeFontSize;
                seg.lineNumberText.lineSpacing = spacing;
            }
        }
    }

    /// <summary>点击哪个分块就把该块的 InputField 视为活动块（单块时恒为 0）。</summary>
    private void UpdateActiveSegmentFromSelection()
    {
        if (segments.Count <= 1)
        {
            activeSegIndex = 0;
            return;
        }
        for (int i = 0; i < segments.Count; i++)
        {
            InputField f = segments[i].field;
            if (f != null && f.isFocused)
            {
                activeSegIndex = i;
                return;
            }
        }
        // EventSystem 的选中项在 ActivateInputField 后常滞后一帧，非点击帧采纳它会把活动块误拉回 0 号块：
        // 块 0 的块顶为 0，行 y 就退化成「文档开头那一行」，于是每打一个字视图跳回文档前面。
        // 因此只在指针按下 / 按住（真实点击）时用 EventSystem 兜底判定活动块。
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButton(0))
            return;
        EventSystem es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null)
            return;
        for (int i = 0; i < segments.Count; i++)
        {
            InputField f = segments[i].field;
            if (f != null && es.currentSelectedGameObject == f.gameObject)
            {
                activeSegIndex = i;
                return;
            }
        }
    }

    /// <summary>单个分块：独立的原生 InputField + 高亮 Text + 一组行号 Text，块间靠 y 偏移无缝拼接。</summary>
    private sealed class CodeSegment
    {
        public RectTransform rt;          // 块容器（segments[0] 就是原 codeRt）
        public InputField field;          // 该块的原生输入框
        public Text inputText;            // 透明输入文字
        public Text highlightText;        // 可见高亮文字
        public Text lineNumberText;       // 该块行号（显示全局行号）
        public RectTransform lineRt;      // 行号列容器（lineViewportRt 下的 LineNumbers）
        public string text = string.Empty;
        public int startLine;             // 该块首行之前的全局行数
        public int lineCount = 1;
        public float lineHeight;
        public float topY;                // 该块块顶相对内容顶的下移量（LayoutSegments 摆放时记录，供行 y 精确换算）
        public RawImage stripeBar;        // 块尾「禁写」斜纹带（分段拼接多出的那一行，主题色斜线填充）
        public bool stripeVisible;
        public float stripeLastTopY = float.NaN;
        public float stripeLastH = -1f;
        public float stripeLastW = -1f;
        public float stripeLastLeft = -1f;   // 斜纹带左侧内缩（与代码文字左内边距 12px 对齐）
    }

    // ---------------- 视图刷新 ----------------

    private void RefreshCodeView()
    {
        if (inputField == null || highlightText == null)
            return;
        // 打字帧判定：本次刷新由 OnSegmentValueChanged 触发时，块下标是确定的（就是刚编辑的块），
        // 直接锁死它给 UpdateScrollToCaret 用，不再让活动块靠 isFocused / EventSystem 二次推导。
        int typingSegIndex = lastEditedSegIndex;
        bool typingFrame = typingSegIndex >= 0 && typingSegIndex < segments.Count;
        if (typingFrame)
            typingFrameStamp = Time.frameCount;   // 供 ApplyScrollY 的「打字期间禁止向前大跳」闸门识别
        float scrollYBefore = codeRt != null ? codeRt.anchoredPosition.y : 0f;
        // 冻结帧：撤销 / 重做 / 重切分这类「整篇文本被整体换掉」的刷新。此刻 caretPosition 由 SetFullCode
        // 重新赋值，但输入框往往还没真正重新聚焦（ActivateInputField 要下一帧才生效，未聚焦时 caretPosition
        // 会被内部重置成 0 或旧值），任何按 caret 推导的跟随都会把视图甩回文档前面。这类刷新一律不跟光标走，
        // 原样保留刷新前的滚动位置。
        bool freezeScroll = suppressScrollFollowOnce;
        suppressScrollFollowOnce = false;
        // 逐块刷新高亮：每块只渲染自己那段代码，单块顶点数天然远低于 16383 上限。
        // 只有正在编辑的那一块内容变了，其余块沿用上次的高亮结果（超大文件下避免每次按键全量重排）。
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            if (seg.field == null || !seg.rt.gameObject.activeSelf)
                continue;
            seg.text = seg.field.text;
            bool needHighlight = lastEditedSegIndex < 0 || lastEditedSegIndex == i;
            if (seg.highlightText != null && needHighlight)
                seg.highlightText.text = HighlightCSharp(seg.text);
        }
        lastEditedSegIndex = -1;
        LayoutSegments();              // 行数 / 块高 / 块位置重排（单块时等价于原内容高度同步）
        RefreshLineNumbers(GetFullCode());
        RefreshCursorStatus();
        SyncContentHeightAndScroll();  // 内容高度随行数更新并重新钳制滚动范围
        if (freezeScroll)
        {
            // 冻结帧：把刷新前的滚动位置原样还原（只受新的滚动上限约束），不参与任何光标推导
            ApplyScrollY(Mathf.Clamp(scrollYBefore, 0f, GetScrollMaxY()));
            UpdateScrollBars();
            return;
        }
        // 打字帧：禁止向上回拉，且只按刚编辑的那块跟随；其余场景（重建 / 缩放 / 撤销）保留完整跟随
        UpdateScrollToCaret(!typingFrame, typingFrame ? typingSegIndex : -1);
        // 打字路径 scrollY 只增不减：插入字符时内容只会变高、光标只会往下走，
        // 一旦算出比打字前更小的 scrollY 就说明行号 / 块顶判错，直接用打字前的值覆盖回来。
        if (typingFrame && codeRt != null)
        {
            float scrollYAfter = codeRt.anchoredPosition.y;
            if (scrollYAfter < scrollYBefore - 0.5f)
            {
                float restore = Mathf.Clamp(scrollYBefore, 0f, GetScrollMaxY());
                if (restore > scrollYAfter + 0.5f)
                {
                    ApplyScrollY(restore);
                    UpdateScrollBars();
                }
            }
        }
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

    /// <summary>仅同步内容宽度：长行不换行时取各分块高亮层最长行宽（+左右内边距 24），不足视口宽则撑满视口。</summary>
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
        // 多分块：横向可滚范围取所有块的最长行（否则超长代码被切成 N 段后，最长行可能在后面的块里）
        for (int i = 1; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            if (seg.highlightText == null || !seg.rt.gameObject.activeSelf)
                continue;
            float spw;
            try { spw = seg.highlightText.preferredWidth; }
            catch { spw = 0f; }
            if (spw < 0f || float.IsNaN(spw) || float.IsInfinity(spw))
                spw = 0f;
            if (spw > pw)
                pw = spw;
        }
        float contentW = Mathf.Max(viewW, pw + 24f); // 文字左右内边距各 12
        codeContentWidth = contentW;
        codeRt.sizeDelta = new Vector2(contentW, codeRt.sizeDelta.y);
        // 各分块节点同宽（块内文字横向溢出由整体横向滚动查看，不换行）
        for (int i = 1; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            if (seg.rt.gameObject.activeSelf)
                seg.rt.sizeDelta = new Vector2(contentW, seg.rt.sizeDelta.y);
        }
    }

    /// <summary>
    /// 刷新代码区滚动块（竖向 + 横向）：内容真的超出视口时才显示对应方向的滑块，
    /// 长度按「视口 : 内容」比例换算、位置按滚动进度换算；放得下就整块隐藏，不留深色条块占视线。
    /// 轨道本身透明且常驻（click 跳转仍可用），所以滑块出现 / 消失不会挤压代码视口宽度。
    /// </summary>
    private void UpdateScrollBars()
    {
        if (codeViewportRt == null)
            return;

        float viewH = codeViewportRt.rect.height;
        float viewW = codeViewportRt.rect.width;

        // ---------- 竖向滚动块 ----------
        if (vScrollHandleRt != null)
        {
            float maxY = GetScrollMaxY();
            if (maxY > 0.5f && viewH > 1f && codeContentHeight > 1f)
            {
                if (!vScrollHandleRt.gameObject.activeSelf)
                    vScrollHandleRt.gameObject.SetActive(true);
                float trackH = vScrollTrackRt != null ? vScrollTrackRt.rect.height : 0f;
                if (trackH <= 1f)
                    trackH = Mathf.Max(24f, viewH); // 首帧布局未完成时兜底
                float ratio = Mathf.Clamp01(viewH / Mathf.Max(1f, codeContentHeight));
                float handleH = Mathf.Clamp(trackH * ratio, 28f, trackH);
                vScrollHandleRt.sizeDelta = new Vector2(vScrollHandleRt.sizeDelta.x, handleH);
                float y = codeRt != null ? codeRt.anchoredPosition.y : 0f;
                float t = Mathf.Clamp01(y / maxY);
                vScrollHandleRt.anchoredPosition = new Vector2(0f, -t * Mathf.Max(0f, trackH - handleH));
            }
            else if (vScrollHandleRt.gameObject.activeSelf)
            {
                vScrollHandleRt.gameObject.SetActive(false);
            }
        }

        // ---------- 横向滚动块 ----------
        if (hScrollHandleRt != null)
        {
            float maxX = GetScrollMaxX();
            if (maxX > 0.5f && viewW > 1f && codeContentWidth > 1f)
            {
                if (!hScrollHandleRt.gameObject.activeSelf)
                    hScrollHandleRt.gameObject.SetActive(true);
                float trackW = hScrollTrackRt != null ? hScrollTrackRt.rect.width : 0f;
                if (trackW <= 1f)
                    trackW = Mathf.Max(40f, viewW); // 同上兜底
                float ratio = Mathf.Clamp01(viewW / Mathf.Max(1f, codeContentWidth));
                float handleW = Mathf.Clamp(trackW * ratio, 40f, trackW);
                hScrollHandleRt.sizeDelta = new Vector2(handleW, hScrollHandleRt.sizeDelta.y);
                float sx = codeRt != null ? Mathf.Clamp(-codeRt.anchoredPosition.x, 0f, maxX) : 0f;
                float t = Mathf.Clamp01(sx / maxX);
                hScrollHandleRt.anchoredPosition = new Vector2(t * Mathf.Max(0f, trackW - handleW), 0f);
            }
            else if (hScrollHandleRt.gameObject.activeSelf)
            {
                hScrollHandleRt.gameObject.SetActive(false);
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
        {
            // 统一闸门：打字期间禁止「向前大跳」。
            // 块顶 / 行号任何一处口径出错（活动块判错、块高估算、光标钳到 0）都会算出一行靠前的 y，
            // 表现就是「在下面输着字，视图突然飞回文档前面」。前面各处的跟随保护都基于「谁算错了」的假设，
            // 总有漏掉的路径；这里直接在写入点兜底：打字帧内一次性往回拽超过半屏的写入一律拒绝并留日志。
            // 明确的上移按键（↑ / PageUp / Home）视为用户本意，照旧放行。
            float current = codeRt.anchoredPosition.y;
            if (typingFrameStamp >= Time.frameCount - 2 && scrollY < current - 0.5f)
            {
                float limit = Mathf.Max(GetLineHeight() * 3f, codeViewportRt != null ? codeViewportRt.rect.height * 0.5f : 200f);
                bool askUp = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.Home);
                if (scrollY < current - limit && !askUp)
                {
                    if (scrollJumpBlockStamp != Time.frameCount)
                    {
                        scrollJumpBlockStamp = Time.frameCount;
                        Debug.LogWarning("[CodeEditor][滚动] 打字期间拦截一次向前大跳：当前=" + current.ToString("0.#") +
                                         " → 目标=" + scrollY.ToString("0.#") + "，已保持原位");
                    }
                    scrollY = current;
                }
            }
            codeRt.anchoredPosition = new Vector2(codeRt.anchoredPosition.x, scrollY);
        }
        if (lineRt != null)
            lineRt.anchoredPosition = new Vector2(lineRt.anchoredPosition.x, scrollY);
    }

    /// <summary>仅同步内容高度：codeContentHeight = max(视口高, 各分块行数累计×行高+16)，并同步 codeRt / lineRt 高度。</summary>
    private void SyncContentHeight()
    {
        if (inputField == null || codeRt == null || lineRt == null || codeViewportRt == null)
            return;
        float lineHeight = GetLineHeight();
        float viewH = codeViewportRt.rect.height;
        // 与 LayoutSegments 严格同口径：内部块 = 实际渲染行数（含段尾空行）× 行高 + 上下内边距 16，
        // 末块取 max(实测高度, 同式硬算)。口径必须完全一致，否则滚动范围与块实际摆放高度不符，
        // 滚到底会露白或滚不到最后几行。
        float sumH = 0f;
        int totalLinesAll = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment s = segments[i];
            string t = s.field != null ? s.field.text : (s.text ?? string.Empty);
            int countLines = CountLines(t);
            int rawLines = Mathf.Max(1, Mathf.Max(countLines, MeasureRenderedLines(s.highlightText, t)));
            int n = s.lineCount > 0 ? s.lineCount : countLines;
            totalLinesAll += n;
            float hardHeight = rawLines * lineHeight + 16f;
            if (segments.Count > 1)
                sumH += Mathf.Max(MeasureSegmentHeight(s, n, lineHeight, out _), hardHeight);
            else
                sumH += Mathf.Max(MeasureSegmentHeight(s, n, lineHeight, out _), n * lineHeight);
        }
        float contentH = Mathf.Max(viewH, Mathf.Max(sumH, totalLinesAll * lineHeight) + 16f + lineHeight);
        codeContentHeight = contentH;
        codeRt.sizeDelta = new Vector2(codeRt.sizeDelta.x, contentH);
        lineRt.sizeDelta = new Vector2(lineRt.sizeDelta.x, contentH);
    }

    /// <summary>内容高度随代码行数动态更新，并将当前滚动偏移重新钳制到有效范围（含横向）。</summary>
    private void SyncContentHeightAndScroll()
    {
        SyncContentHeight();
        SyncContentWidth();
        if (codeRt == null || codeViewportRt == null)
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
        // 指针落在底部错误区：滚轮改翻报错文字（报错多到一屏放不下时），不再翻代码
        if (HandleErrorWheelScroll(wheel))
            return;
        if (HandleReWheelScroll(wheel)) // 指针在紫色 RE 窗口上：滚轮只翻运行时报错，不带动代码
            return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(codeViewportRt, Input.mousePosition, null))
            return;
        SyncContentHeight(); // 滚动前强制同步内容高度，避免 contentH 未更新导致滚动范围恒为 0（内容翻不下去）
        float lineHeight = GetLineHeight();
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
        InputField activeInput = ActiveField;
        if (codeRt == null || codeViewportRt == null || activeInput == null || !IsInputFocused())
            return;
        string text = activeInput.text;
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
        InputField activeInput = ActiveField;
        if (activeInput == null)
            return;
        pos = Mathf.Max(0, Mathf.Min(pos, activeInput.text.Length));
        activeInput.caretPosition = pos;
        activeInput.selectionAnchorPosition = pos;
        activeInput.selectionFocusPosition = pos;
    }

    /// <summary>
    /// Ctrl+滚轮总入口：先看指针压在哪个区，只缩放那一个区的字号。
    /// 判定顺序 = 叠放顺序：紫窗（浮在最上层）→ 红窗 → 代码区；
    /// 三个区都没踩中就不缩放，避免「指针停在空白处滚一下，字号莫名全变」。
    /// </summary>
    private void HandlePointerWheelZoom()
    {
        if (Mathf.Abs(Input.mouseScrollDelta.y) < 0.01f)
            return;
        if (HandleReFontZoom())     // 紫色运行时报错窗
            return;
        if (HandleErrorFontZoom())  // 红色编译报错窗
            return;
        if (IsPointerOverCodeZone())
            HandleFontZoom();       // 代码阅读区
    }

    /// <summary>指针是否落在代码阅读区（文字视口或右侧竖向滚动条）。</summary>
    private bool IsPointerOverCodeZone()
    {
        if (codeViewportRt == null || !codeViewportRt.gameObject.activeInHierarchy)
            return false;
        if (RectTransformUtility.RectangleContainsScreenPoint(codeViewportRt, Input.mousePosition, null))
            return true;
        return vScrollTrackRt != null && vScrollTrackRt.gameObject.activeInHierarchy &&
               RectTransformUtility.RectangleContainsScreenPoint(vScrollTrackRt, Input.mousePosition, null);
    }

    /// <summary>
    /// Ctrl+滚轮：紫窗（RE）字号缩放，指针压在紫窗内（文字区 / 顶部紫条 / 右侧滚动条）才生效。
    /// 算法与代码区、红区一致：以「鼠标所指的那一行」为锚点，缩放前后该行仍贴在同一屏幕位置。
    /// </summary>
    private bool HandleReFontZoom()
    {
        if (reZoneCollapsed || reZoneRt == null || reMaskRt == null || reListText == null)
            return false;
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f)
            return false;
        bool overZone = reZoneRt.gameObject.activeInHierarchy &&
                        RectTransformUtility.RectangleContainsScreenPoint(reZoneRt, Input.mousePosition, null);
        bool overBar = reScrollTrackRt != null && reScrollTrackRt.gameObject.activeInHierarchy &&
                       RectTransformUtility.RectangleContainsScreenPoint(reScrollTrackRt, Input.mousePosition, null);
        if (!overZone && !overBar)
            return false;

        int newSize = Mathf.Clamp(reFontSize + (wheel > 0f ? 2 : -2), ReFontSizeMin, ReFontSizeMax);
        if (newSize == reFontSize)
            return true;

        // 缩放前：记录滚动偏移与旧行高，并把鼠标屏幕点换算到内容层局部坐标（取行号）
        float scrollY0 = reScrollY;
        float lineHeight0 = Mathf.Max(12f, reListText.fontSize * 1.45f);
        Vector2 localPt;
        bool hasPointer = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            reListText.rectTransform, Input.mousePosition, null, out localPt);
        float rowF = hasPointer ? (-localPt.y) / lineHeight0 : 0f;
        if (rowF < 0f) rowF = 0f;   // 指针落在内容上方留白 / 换算失败：按第一行锚定

        reFontSize = newSize;
        string keep = reListText.text;
        // 紫窗高度自适应字号：这里重排一次布局，窗口会按新字号重算高度
        RefreshReLayout(keep);

        // 缩放后：按新行高把锚定行放回同一屏幕位置，并夹在合法滚动范围内
        float lineHeight1 = Mathf.Max(12f, reListText.fontSize * 1.45f);
        float viewH = ReViewportHeight();
        float maxScroll = Mathf.Max(0f, reContentHeight - viewH);
        reScrollY = Mathf.Clamp(rowF * lineHeight1 - (rowF * lineHeight0 - scrollY0), 0f, maxScroll);
        ApplyReScroll();
        SaveFontSettings();
        return true;
    }

    /// <summary>
    /// 指针是否压在某条分隔条上：以「粗提示条的实际屏幕矩形」为基准，向上放宽 6px、向下放宽 padDown（盖住细条）。
    /// 不看透明命中区，因为命中区尺寸/锚点是为拖拽手感调的，未必贴住可见条。
    /// </summary>
    private bool IsPointerOverSplitBand(RectTransform gripRt, float padDown)
    {
        if (gripRt == null || !gripRt.gameObject.activeInHierarchy)
            return false;
        gripRt.GetWorldCorners(splitCorners); // 0:左下 1:左上 2:右上 3:右下
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(null, splitCorners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(null, splitCorners[2]);
        Vector2 m = Input.mousePosition;
        return m.x >= bl.x - 2f && m.x <= tr.x + 2f &&
               m.y >= bl.y - padDown && m.y <= tr.y + 6f;
    }

    /// <summary>
    /// 红色 / 紫色分隔条的悬停高亮：鼠标压上去时整条提亮（向白色靠、alpha 拉满），移开平滑恢复原色。
    /// 只改 Image.color，不动 sizeDelta / 布局，因此不会干扰拖拽、折叠与合并判定。
    /// </summary>
    private void UpdateSplitHoverHighlights()
    {
        float step = Time.unscaledDeltaTime * 12f;
        // 红线的命中范围同样以「上方归属区」为基准：归属区 6px + 线本体 4px + 呼吸位 6px 都算在红线里
        bool overError = IsPointerOverSplitBand(errorSplitTopPadRt, ErrorSplitGripHeight + ErrorSplitBarHeight + 6f);
        // 紫线的命中范围以「上方归属区」为基准：归属区 6px + 线本体 4px + 呼吸位 6px 都算在线里
        bool overRe = IsPointerOverSplitBand(reSplitTopPadRt, ReSplitLineHeight + ReSplitBreath + 6f);
        errorSplitHoverT = Mathf.MoveTowards(errorSplitHoverT, overError ? 1f : 0f, step);
        reSplitHoverT = Mathf.MoveTowards(reSplitHoverT, overRe ? 1f : 0f, step);

        if (errorSplitBarImg != null)
            errorSplitBarImg.color = Color.Lerp(UITheme.WithAlpha(errorColor, 0.95f),
                UITheme.WithAlpha(Color.Lerp(errorColor, Color.white, 0.55f), 1f), errorSplitHoverT);
        if (errorSplitGripImg != null)
            errorSplitGripImg.color = Color.Lerp(UITheme.WithAlpha(errorColor, 0.85f),
                UITheme.WithAlpha(Color.Lerp(errorColor, Color.white, 0.45f), 1f), errorSplitHoverT);
        // 红线上方那 6px 归属区跟着整条一起亮：只提透明度，颜色仍是淡红，不会变成实线
        if (errorSplitTopPadImg != null)
            errorSplitTopPadImg.color = Color.Lerp(UITheme.WithAlpha(errorColor, ErrorSplitTopPadAlpha),
                UITheme.WithAlpha(Color.Lerp(errorColor, Color.white, 0.45f), ErrorSplitTopPadHoverAlpha), errorSplitHoverT);
        if (reSplitBarImg != null)
            reSplitBarImg.color = Color.Lerp(UITheme.WithAlpha(UITheme.Purple, 0.95f),
                UITheme.WithAlpha(Color.Lerp(UITheme.Purple, Color.white, 0.55f), 1f), reSplitHoverT);
        if (reSplitGripImg != null)
            reSplitGripImg.color = Color.Lerp(UITheme.WithAlpha(UITheme.Purple, 0.85f),
                UITheme.WithAlpha(Color.Lerp(UITheme.Purple, Color.white, 0.45f), 1f), reSplitHoverT);
        // 线上方那 6px 归属区跟着整条一起亮：只有透明度的提亮，颜色仍是淡紫，不会变成实线
        if (reSplitTopPadImg != null)
            reSplitTopPadImg.color = Color.Lerp(UITheme.WithAlpha(UITheme.Purple, ReSplitTopPadAlpha),
                UITheme.WithAlpha(Color.Lerp(UITheme.Purple, Color.white, 0.45f), ReSplitTopPadHoverAlpha), reSplitHoverT);
    }

    /// <summary>
    /// Ctrl+滚轮：代码字号缩放（放大 / 缩小），鼠标需在代码视口内。
    /// 缩放以「鼠标所指的那一行」为锚点：该行在缩放前后保持贴在同一屏幕位置，
    /// 因此连续放大 / 缩小都不会丢失当前阅读位置。
    /// 仅当新字号下内容高度不足以填满视口（上方会出现空洞）时，滚动值被钳到 0 → 自然顶格显示。
    /// </summary>
    private void HandleFontZoom()
    {
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f || codeViewportRt == null || codeRt == null || ActiveField == null)
            return;
        if (!IsPointerOverCodeZone())
            return;
        int newSize = Mathf.Clamp(codeFontSize + (wheel > 0f ? 2 : -2), CodeFontSizeMin, CodeFontSizeMax);
        if (newSize == codeFontSize)
            return;

        // 缩放前：记录当前滚动偏移与旧行高，并把鼠标屏幕点换算到内容层局部坐标
        // （codeRt pivot=(0,1)、锚点左上，局部 y 向下为负，与滚动偏移无关，故换算结果天然是「行」位置）。
        float scrollY0 = codeRt.anchoredPosition.y;
        float lineHeight0 = GetLineHeight();

        Vector2 localPt;
        bool hasPointer = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            codeRt, Input.mousePosition, null, out localPt);

        // 行顶 y = -8 - 行号 × 行高（与 UpdateScrollToCaret / 高亮层排版口径一致），rowF 为含小数的行位置。
        float rowF = hasPointer ? (-localPt.y - 8f) / lineHeight0 : 0f;
        if (rowF < 0f)
            rowF = 0f;                      // 鼠标落在内容上方留白 / 坐标换算失败：按第一行锚定
        int row = Mathf.FloorToInt(rowF);
        float frac = rowF - row;            // 0~1：鼠标落在该行内的纵向比例

        codeFontSize = newSize;
        SaveFontSettings();           // 字号存进存档：重启游戏 / 重开编辑器后照旧
        ApplyFontSizeToAllSegments(); // 所有分块的输入层 / 高亮层 / 行号层统一字号
        RefreshCodeView(); // 重算高亮 / 行号 / 内容高度与滚动范围（末尾的光标跟随会改写滚动偏移，下面统一覆盖）

        // 锚定鼠标行：行高由 lineHeight0 变为 lineHeight1 后，第 row 行行顶下移 row×Δ，
        // 行内的 frac 部分再下移 frac×Δ；滚动偏移同量增加即可让该行仍贴在原屏幕位置。
        float lineHeight1 = GetLineHeight();
        float scrollY = scrollY0 + (row + frac) * (lineHeight1 - lineHeight0);
        scrollY = Mathf.Clamp(scrollY, 0f, GetScrollMaxY()); // maxY=0（内容不足视口）时自动顶格，不留上方空洞
        ApplyScrollY(scrollY);

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
    /// <param name="allowPullBack">
    /// 是否允许「行顶高于视口顶 → 把内容向下回拉」。打字 / 输入触发的刷新必须传 false：
    /// 插入字符后一旦块内行号被算小（块重排、活动块判错都会导致），vTop 就恒大于 0，
    /// 每按一个键都回拉一次，表现为「在代码后面插入字符后视图跳回文档前面」。
    /// 只有方向键上移 / Home / PgUp 这类真实光标跳动才允许回拉。
    /// </param>
    /// <param name="forcedSegIndex">强制使用的块下标（打字帧由 OnSegmentValueChanged 直接给出，-1 = 按活动输入框反查）。</param>
    // 取光标所在行的真实排版度量：rowOffset = 该行行顶相对文本层顶部的下移量（首行 0，向下为正），
    // rowHeight = 该行实际行高。数据源是 InputField 自己渲染用的 TextGenerator，与屏幕上看到的分行
    // 完全同源；取不到时返回 false，由调用方退回「行号 × 估算行高」。
    private bool TryGetCaretRowMetrics(InputField field, int caretPos, out float rowOffset, out float rowHeight)
    {
        rowOffset = 0f;
        rowHeight = 0f;
        if (field == null)
            return false;
        Text tc = field.textComponent;
        if (tc == null)
            return false;
        TextGenerator gen = tc.cachedTextGenerator;
        if (gen == null || gen.lineCount <= 0)
            return false;
        int line = 0;
        for (int i = 0; i < gen.lineCount; i++)
        {
            if (gen.lines[i].startCharIdx <= caretPos)
                line = i;
            else
                break;
        }
        UILineInfo info = gen.lines[line];
        rowHeight = info.height > 0.5f ? info.height : GetLineHeight();
        // UILineInfo.topY 是「相对首行顶、向下为负」的偏移，旧写法 `topY > 0f` 对这个约定永远不成立，
        // 事实上一直在走 line * rowHeight 兜底。这里改成按符号取绝对值，两种约定都算对。
        rowOffset = Mathf.Abs(info.topY);
        return true;
    }

    private void UpdateScrollToCaret(bool allowPullBack = true, int forcedSegIndex = -1)
    {
        InputField activeInput = ActiveField;
        if (activeInput == null || codeRt == null || codeViewportRt == null)
            return;
        // 鼠标点击 / 拖选定光标期间不跟随滚动：光标本来就在指针下方，跟随只会让内容乱跳
        if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0) || Time.unscaledTime < scrollFollowSuppressUntil)
            return;
        // 整个代码区都没有焦点时绝不跟随：此刻 ActiveField 会退化到 activeSegIndex（甚至是块 0）的输入框，
        // 用它的块顶 + 陈旧块内光标算出来的行 y 一定偏前，「在下面输入后视图飞到上面」就是这条路径。
        if (!IsInputFocused())
            return;
        string text = activeInput.text;
        int pos = Mathf.Clamp(activeInput.caretPosition, 0, text.Length);
        // 块来源单一化：打字帧直接用调用方给的块（就是刚被编辑的那块），其余情况才按活动输入框反查。
        // 不再依赖 isFocused / EventSystem 的选中状态——活动块被判成 0 号块时，块顶按 0 算，
        // 行 y 就退化成「文档开头那一行」，vTop 恒 > 0，于是逐键把视图拉回文档前面。
        int segIdx = forcedSegIndex;
        // 强制块必须与当前活动输入框是同一块，否则以实际输入框为准：
        // 用别的块的块顶换算行 y，同样会把视图甩到文档前面。
        if (segIdx >= 0 && segIdx < segments.Count && segments[segIdx].field != activeInput)
            segIdx = -1;
        if (segIdx < 0 || segIdx >= segments.Count)
            segIdx = IndexOfField(activeInput);
        if (segIdx < 0 || segIdx >= segments.Count)
            return; // 找不到所属块宁可不动：按错误块顶换算只会把内容顶飞
        int localLine = 0;
        for (int i = 0; i < pos; i++)
        {
            if (text[i] == '\n')
                localLine++;
        }
        // 行 y 必须按「本块真实块顶 + 块内行号」换算：块高是按真实排版高度累加的，
        // 拿全局行号 × 行高换算在分块后会累积误差，跳行 / 点击时把内容顶飞一截。
        float lineHeight = GetLineHeight();
        // 行偏移只用「块内行号 × 行高」这个绝对可靠的量：
        // 行高现在取的是 Text 的 preferredHeight ÷ 行数，与块高（rawLines × 行高 + 16）完全同源，
        // 不再有「估算行高 vs 真实行高」的线性误差。
        // 曾经优先用 cachedTextGenerator 的行信息，但它是「上一次 Canvas 重建」的快照：
        // 打字后文本已变、生成器还没重建，startCharIdx 与新 caretPos 错位，算出的行会随机跳几十行
        // —— 这正是「在 404 行输入，视角有概率跳到 450 多行」的来源。
        float rowOffset = localLine * lineHeight;
        float rowHeight = lineHeight;
        float viewH = codeViewportRt.rect.height;
        float blockTop = segments[segIdx].topY;
        float rowTop = -(blockTop + 8f + rowOffset);
        float rowBottom = rowTop - rowHeight;
        float scrollY = codeRt.anchoredPosition.y;
        float scrollYBeforeFollow = scrollY;   // 大跳保护基准：本帧跟随前的真实滚动位置
        float vTop = scrollY + rowTop;
        float vBottom = scrollY + rowBottom;
        if (vBottom < -viewH - 1f)
            scrollY = -viewH - rowBottom;                 // 行底低于视口底：向下跟随到行底对齐
        else if (allowPullBack && vBottom > -1f)
            scrollY = -rowTop;                            // 整行已完全跑到视口上方才回拉：只在行体彻底不可见时动，避免贴着视口顶时逐键抖动
        scrollY = Mathf.Clamp(scrollY, 0f, GetScrollMaxY());
        // 大跳保护（最后一道闸门）：同一次跟随里把视图往前拽超过「3 行或半屏」时，只有明确的上移按键
        // （↑ / PageUp / Home）才算用户本意；否则就是块顶 / 行号算错了位置（典型是活动块被判成前面某块，
        // 用它的块顶加块内光标算出一行靠前的 y）。此时宁可不跟随，也不能把正在编辑的视图甩回文档前部。
        float hugeJump = Mathf.Max(lineHeight * 3f, viewH * 0.5f);
        // 明确的行导航键视为用户本意，两个方向都放行
        bool navKey = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                      Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.PageDown) ||
                      Input.GetKey(KeyCode.Home) || Input.GetKey(KeyCode.End);
        float jump = Mathf.Abs(scrollY - scrollYBeforeFollow);
        bool typingFrame = typingFrameStamp >= Time.frameCount - 2;
        // 打字帧任何方向的大跳一律拦（光标就在刚敲的那一行，视图绝不该一次挪过 3 行以上）；
        // 非打字帧仍只拦「向前拽」，保留跳行 / 跳转的正常滚动。
        if (jump > hugeJump && !navKey && (typingFrame || scrollY < scrollYBeforeFollow))
        {
            if (scrollJumpBlockStamp != Time.frameCount)
            {
                scrollJumpBlockStamp = Time.frameCount;
                Debug.LogWarning("[CodeEditor][滚动] 拦截一次大跳 块" + segIdx + " 块内行=" + localLine +
                                 " 光标=" + pos + "/" + text.Length + " 块顶=" + blockTop.ToString("0.0") +
                                 " 行偏移=" + rowOffset.ToString("0.0") + " 行高=" + rowHeight.ToString("0.0") +
                                 " 滚动 " + scrollYBeforeFollow.ToString("0.0") + "→" + scrollY.ToString("0.0") +
                                 " 视口高=" + viewH.ToString("0.0") + " 打字帧=" + typingFrame);
            }
            return;
        }
        if (scrollY < scrollYBeforeFollow - hugeJump && !navKey)
            return;
        ApplyScrollY(scrollY);
    }

    /// <summary>记录一次文本变化前的快照（onValueChanged 触发；SetTextWithoutNotify 不触发，需手动同步）。</summary>
    private void RecordSnapshotForUndo()
    {
        if (inputField == null)
            return;
        string t = GetFullCode();   // 全文（多分块时自动拼接）
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
        InputField target = ActiveField;
        if (target == null)
            return;
        if (target.isFocused)
            target.DeactivateInputField(); // 失焦屏蔽原生 Ctrl+Z 撤销
        string current = GetFullCode();
        string prev = undoList[undoList.Count - 1];
        undoList.RemoveAt(undoList.Count - 1);
        redoList.Add(current);
        lastCommittedText = prev;
        // 撤销是「整篇文本换掉」：SetFullCode 重写全部块并把 caret 放到末尾，但输入框此刻还没重新聚焦
        // （caretPosition 会被内部重置），若照常按 caret 跟随，视图就会跳回文档前面。这里冻结跟随：
        // 视图保持撤销前的滚动位置，同时设一个短冻结窗口挡住下一帧 Update 里的光标跟随。
        suppressScrollFollowOnce = true;
        scrollFollowSuppressUntil = Time.unscaledTime + 0.35f;
        SetFullCode(prev, prev.Length); // 全文写回（内部按需重新分段并定位光标到末尾）
        target = ActiveField;
        if (target != null)
        {
            target.ActivateInputField();
            target.Select();
        }
        suppressScrollFollowOnce = true;
        RefreshCodeView();
        // 与当前 caret 对齐基准值：否则下一帧 Update 会把「撤销后 caret 落定」当成光标移动再跟随一次，
        // 而那一瞬输入框可能还没真正拿到焦点（caret 读到 0），视图就跳回文档前面。
        InputField settled = ActiveField;
        lastCaretPos = GlobalCaretPosition();
        lastCaretPosInSeg = settled != null ? Mathf.Clamp(settled.caretPosition, 0, settled.text.Length) : -1;
        lastCaretSegIndex = settled != null ? IndexOfField(settled) : -1;
        SetStatus(Localization.Pick("已撤销", "Undone"), false);
    }

    private void RedoSnapshot()
    {
        if (redoList.Count == 0)
        {
            SetStatus(Localization.Pick("没有可重做的操作", "Nothing to redo"), false);
            return;
        }
        InputField target = ActiveField;
        if (target == null)
            return;
        if (target.isFocused)
            target.DeactivateInputField();
        string current = GetFullCode();
        string next = redoList[redoList.Count - 1];
        redoList.RemoveAt(redoList.Count - 1);
        undoList.Add(current);
        lastCommittedText = next;
        suppressScrollFollowOnce = true;                        // 与撤销同理：重做也冻结跟随，避免视图跳回文档前面
        scrollFollowSuppressUntil = Time.unscaledTime + 0.35f;
        SetFullCode(next, next.Length);
        target = ActiveField;
        if (target != null)
        {
            target.ActivateInputField();
            target.Select();
        }
        suppressScrollFollowOnce = true;
        RefreshCodeView();
        InputField settled = ActiveField;
        lastCaretPos = GlobalCaretPosition();
        lastCaretPosInSeg = settled != null ? Mathf.Clamp(settled.caretPosition, 0, settled.text.Length) : -1;
        lastCaretSegIndex = settled != null ? IndexOfField(settled) : -1;
        SetStatus(Localization.Pick("已重做", "Redone"), false);
    }

    /// <summary>
    /// 刷新行号列：按分块各配一组行号文本，显示全局行号（前序块行数 + 块内行号），
    /// 使行号层顶点数随分块下降，同时行号与代码文本逐行对齐。
    /// </summary>
    private void RefreshLineNumbers(string code)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            if (seg.lineNumberText == null)
                continue;
            int count = seg.lineCount > 0 ? seg.lineCount : CountLines(seg.text ?? string.Empty);
            StringBuilder sb = new StringBuilder(count * 5);
            for (int k = 1; k <= count; k++)
            {
                sb.Append(seg.startLine + k);
                if (k < count)
                    sb.Append('\n');
            }
            seg.lineNumberText.text = sb.ToString();
            // 行号节点的高度 / 位置统一在 LayoutSegments 里按块计算（多块时逐块摆放，单块时保持原有拉伸）
        }
    }

    /// <summary>状态栏「行:列」按全局口径显示：前序块总行数 + 当前块内行列。</summary>
    private void RefreshCursorStatus()
    {
        InputField activeInput = ActiveField;
        if (statusLineText == null)
            return;
        if (activeInput == null)
            return;
        string text = activeInput.text;
        int pos = Mathf.Clamp(activeInput.caretPosition, 0, text.Length);
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
        int segIdx = IndexOfField(activeInput);
        if (segIdx > 0 && segIdx < segments.Count)
            line += segments[segIdx].startLine; // 全局行号
        // 语言 / 行列随界面语言切换：英文界面下不能残留中文「语言 / 行 / 列」
        statusLineText.text = Localization.Pick(
            "语言 C#    行:" + line + "  列:" + col + "    ",
            "Lang C#    Ln:" + line + "  Col:" + col + "    ")
            + BuildSegmentStatusTag() + lastStatus;
        statusLineText.color = statusColor;
        statusLineText.font = UIFontHelper.LoadForText(statusLineText.text, statusLineText.fontSize);
    }

    /// <summary>
    /// 状态栏的分段摘要（多块时才有内容，如「段 1-169 | 170-396 |」）：
    /// 用来一眼核对段边界与斜纹带（禁写区）到底落在第几行 —— 斜纹带异常时不用再猜。
    /// </summary>
    private string BuildSegmentStatusTag()
    {
        if (segments.Count <= 1)
            return string.Empty;
        StringBuilder sb = new StringBuilder(48);
        sb.Append(Localization.Pick("段", "Seg"));
        for (int i = 0; i < segments.Count; i++)
        {
            CodeSegment seg = segments[i];
            int lines = seg.lineCount > 0 ? seg.lineCount : CountLines(seg.text ?? string.Empty);
            int first = seg.startLine + 1;
            int last = seg.startLine + lines;
            if (i > 0)
                sb.Append('|');
            sb.Append(' ').Append(first).Append('-').Append(last);
        }
        sb.Append("  ");
        return sb.ToString();
    }

    private void SetStatus(string message, bool isError)
    {
        lastStatus = message;
        statusColor = isError ? errorColor : UITheme.TextMuted;
        if (IsOpen)
            RefreshCursorStatus();
    }

    // ==================== 临时诊断区（定位 CS1026，纯只读、无任何副作用，定位完成后可整段删除） ====================
    // 说明：仅在编译失败分支被调用，不参与任何既有流程；F9 导出同样独立。
    private static readonly int[] DiagFullWidthCodepoints = new int[]
    {
        0xFF08, // （
        0xFF09, // ）
        0xFF0C, // ，
        0xFF1B, // ；
        0xFF1A, // ：
        0x3000, // 全角空格
        0x3001, // 、
        0xFF01, // ！
        0xFF1F, // ？
        0xFF1D, // ＝
    };

    /// <summary>临时诊断：对一条编译错误输出定位信息（长度 / 分段 / 出错行原文 / 列窗逐字符码位 / 全角符号扫描）。</summary>
    private void LogCompileErrorDiagnostics(string userCode, CompilerError e, int userLine)
    {
        StringBuilder sb = new StringBuilder(3072);
        try
        {
            // 换行统一为 \n，避免 \r 干扰列偏移与逐字符打印
            string norm = userCode == null ? string.Empty : userCode.Replace("\r\n", "\n").Replace('\r', '\n');
            int crDiff = (userCode != null ? userCode.Length : 0) - norm.Length;

            sb.Append("[诊断] ===== 编译错误定位开始 =====\n");
            sb.Append("[诊断] 全文长度 userCode.Length=").Append(userCode != null ? userCode.Length : 0)
              .Append(" | 归一化长度=").Append(norm.Length)
              .Append(" | 全文行数=").Append(CountLines(norm))
              .Append(" | 换行差异字符数=").Append(crDiff)
              .Append(" | segments.Count=").Append(segments != null ? segments.Count : -1)
              .Append('\n');

            sb.Append("[诊断] 各段长度=");
            if (segments != null && segments.Count > 0)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    CodeSegment seg = segments[i];
                    sb.Append('[').Append(i).Append("]text=").Append(seg != null && seg.text != null ? seg.text.Length : -1);
                    int fieldLen = (seg != null && seg.field != null && seg.field.text != null) ? seg.field.text.Length : -1;
                    sb.Append("(field=").Append(fieldLen).Append(')');
                }
            }
            else
            {
                sb.Append("无分段（单块模式）");
            }
            sb.Append('\n');

            sb.Append("[诊断] 错误：模板行=").Append(e != null ? e.Line : -1)
              .Append(" | 用户行=").Append(userLine)
              .Append(" | 列=").Append(e != null ? e.Column : -1)
              .Append(" | 编号=").Append(e != null ? e.ErrorNumber : "?")
              .Append(" | 文本=").Append(e != null ? e.ErrorText : "?")
              .Append('\n');

            string[] lines = norm.Split('\n');
            if (userLine >= 1 && userLine <= lines.Length)
            {
                string lineText = lines[userLine - 1];
                sb.Append("[诊断] 出错行长度=").Append(lineText.Length)
                  .Append(" | 可用用户行数=").Append(lines.Length)
                  .Append('\n');
                sb.Append("[诊断] 出错行原文=").Append(ClipMiddle(lineText, 120)).Append('\n');

                int col = e != null ? e.Column : 1;
                int caret = Mathf.Clamp(col - 1, 0, lineText.Length);
                int from = Mathf.Max(0, caret - 40);
                int to = Mathf.Min(lineText.Length, caret + 40);
                sb.Append("[诊断] 列窗范围 [").Append(from).Append(',').Append(to).Append(") 文本=");
                sb.Append(lineText.Substring(from, Mathf.Max(0, to - from)));
                sb.Append('\n');
                sb.Append("[诊断] 列窗逐字符=");
                for (int i = from; i < to; i++)
                {
                    char c = lineText[i];
                    sb.Append(i).Append(':');
                    if (c == '\t') sb.Append("\\t");
                    else if (c < ' ') sb.Append("<控制符0x").Append(((int)c).ToString("X2")).Append('>');
                    else sb.Append(c);
                    sb.Append("(U+").Append(((int)c).ToString("X4")).Append(')');
                    if (i != to - 1) sb.Append(" | ");
                }
                sb.Append('\n');
                if (caret < lineText.Length)
                {
                    char cc = lineText[caret];
                    sb.Append("[诊断] 报错列(第 ").Append(col).Append(" 列)字符=")
                      .Append(cc < ' ' ? "控制符" : cc.ToString())
                      .Append("(U+").Append(((int)cc).ToString("X4")).Append(')')
                      .Append('\n');
                }
                else
                {
                    sb.Append("[诊断] 报错列(第 ").Append(col).Append(" 列)已超出该行长度 ").Append(lineText.Length).Append("（指向行尾之后）\n");
                }
            }
            else
            {
                sb.Append("[诊断] 用户行 ").Append(userLine).Append(" 越界（可用行数=").Append(lines.Length).Append("），跳过行窗口\n");
            }

            sb.Append("[诊断] 全角符号扫描=").Append(DescribeFullWidthScan(norm)).Append('\n');
            sb.Append("[诊断] ===== 编译错误定位结束 =====");
        }
        catch (System.Exception ex)
        {
            sb.Append("[诊断] 诊断自身异常：").Append(ex.Message);
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>临时诊断：扫描全文全角标点与全角区段字符，统计出现次数与首个位置。</summary>
    private static string DescribeFullWidthScan(string code)
    {
        if (string.IsNullOrEmpty(code))
            return "文本为空";

        StringBuilder sb = new StringBuilder(512);
        bool anyHit = false;
        for (int k = 0; k < DiagFullWidthCodepoints.Length; k++)
        {
            char target = (char)DiagFullWidthCodepoints[k];
            int count = 0;
            int first = -1;
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] != target) continue;
                if (first < 0) first = i;
                count++;
            }
            if (count <= 0) continue;
            // 同一码位去重输出
            if (sb.Length > 0) sb.Append("；");
            sb.Append(target).Append("(U+").Append(((int)target).ToString("X4")).Append(")×").Append(count)
              .Append(" 首现索引=").Append(first)
              .Append("(第 ").Append(IndexToLine(code, first)).Append(" 行)");
            anyHit = true;
        }

        // 全角区段聚合（兜底：未被上面清单列举的全角字符也要暴露出来）
        int ffCount = 0; int ffFirst = -1;
        int cjkCount = 0; int cjkFirst = -1;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c >= '\uFF00' && c <= '\uFF5E')
            {
                if (ffFirst < 0) ffFirst = i;
                ffCount++;
            }
            else if (c >= '\u3000' && c <= '\u303F')
            {
                if (cjkFirst < 0) cjkFirst = i;
                cjkCount++;
            }
        }
        if (sb.Length > 0) sb.Append(" || ");
        sb.Append("全角区段U+FF00-FF5E 共").Append(ffCount).Append("个")
          .Append(ffFirst >= 0 ? " 首现索引=" + ffFirst + "(第 " + IndexToLine(code, ffFirst) + " 行)" : string.Empty)
          .Append("；CJK标点U+3000-303F 共").Append(cjkCount).Append("个")
          .Append(cjkFirst >= 0 ? " 首现索引=" + cjkFirst + "(第 " + IndexToLine(code, cjkFirst) + " 行)" : string.Empty);
        if (!anyHit) sb.Insert(0, "清单项无命中 | ");
        return sb.ToString();
    }

    /// <summary>临时诊断：索引 → 1 基行号。</summary>
    private static int IndexToLine(string code, int index)
    {
        if (string.IsNullOrEmpty(code) || index <= 0) return 1;
        int line = 1;
        int max = Mathf.Min(index, code.Length);
        for (int i = 0; i < max; i++)
        {
            if (code[i] == '\n') line++;
        }
        return line;
    }

    /// <summary>临时诊断：过长文本保留首尾各 keep 个字符，中间折叠。</summary>
    private static string ClipMiddle(string s, int keep)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= keep * 2 + 6) return s;
        return s.Substring(0, keep) + " …<中间省略 " + (s.Length - keep * 2) + " 字符>… " + s.Substring(s.Length - keep);
    }

    /// <summary>旧版桌面直出（已被「导出 (F9)」目录选择面板取代，仅留作应急调用，勿接按钮）。</summary>
    private void DumpCodeToDesktop()
    {
        try
        {
            string code = GetFullCode();
            string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop))
            {
                string profile = System.Environment.GetEnvironmentVariable("USERPROFILE");
                desktop = System.IO.Path.Combine(profile == null ? string.Empty : profile, "Desktop");
            }
            string path = System.IO.Path.Combine(desktop, "code_dump.cs");
            System.IO.File.WriteAllText(path, code, new System.Text.UTF8Encoding(true));
            Debug.Log("[CodeEditor][诊断] 已导出全文 → " + System.IO.Path.GetFullPath(path)
                + " | 字符数=" + code.Length + " | 行数=" + CountLines(code));
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[CodeEditor][诊断] 导出全文失败：" + ex.Message);
        }
    }
    // ==================== 临时诊断区结束 ====================

    // ==================== 打开已保存的代码（顶栏「载入 (F8)」按钮） ====================

    /// <summary>列出可能存有「已保存代码」的候选路径（按实际存在的文件优先，最后按写入时间取最新）。</summary>
    private static string[] GetSavedCodeCandidates()
    {
        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop))
        {
            string profile = System.Environment.GetEnvironmentVariable("USERPROFILE");
            desktop = System.IO.Path.Combine(profile == null ? string.Empty : profile, "Desktop");
        }
        string persistent = Application.persistentDataPath;
        return new string[]
        {
            System.IO.Path.Combine(desktop, "code_dump.cs"),
            System.IO.Path.Combine(desktop, "Code.cs"),
            System.IO.Path.Combine(persistent, "saved_code.cs"),
            System.IO.Path.Combine(persistent, "code_dump.cs"),
            System.IO.Path.Combine(persistent, "ABOVE_THE_VOID", "saved_code.cs"),
        };
    }

    /// <summary>在候选路径中挑出最后写入时间最新的那个已存在文件；都不存在时返回 null。</summary>
    private string FindLatestSavedCode()
    {
        string best = null;
        System.DateTime bestTime = System.DateTime.MinValue;
        string[] candidates = GetSavedCodeCandidates();
        for (int i = 0; i < candidates.Length; i++)
        {
            string p = candidates[i];
            try
            {
                if (string.IsNullOrEmpty(p) || !System.IO.File.Exists(p))
                    continue;
                System.DateTime t = System.IO.File.GetLastWriteTimeUtc(p);
                if (best == null || t > bestTime)
                {
                    best = p;
                    bestTime = t;
                }
            }
            catch { }
        }
        return best;
    }

    /// <summary>
    /// 顶栏「载入 (F8)」按钮：弹出自绘的文件选择面板（类似系统的打开对话框），
    /// 由用户自己在磁盘上逐级浏览目录、指定要载入的代码文件，不再做「自动找候选 + 二次确认」。
    /// </summary>
    private void OnOpenSavedCodeClicked()
    {
        OpenCodeFilePicker();
    }

    // ==================== 代码文件选择面板（自绘目录浏览器） ====================

    // ---- 标题栏（语言切换后需要重排：语言在主页切换时编辑器早就 BuildUI 完了，文案与按钮宽度都不会自己变） ----
    private Text titleBarTitleText;                          // 标题「炮台代码编辑器 / Tower Code Editor」
    private Button titleBtnStop;                             // 停止 (F7)
    private Button titleBtnCompile;                          // 编译运行 (F6)
    private Button titleBtnExport;                           // 导出 (F9)
    private Button titleBtnOpen;                             // 打开代码文件 (F8)
    private Button titleBtnErrors;                           // 报错界面 (F10)：展开 / 收起底部报错区（与 Open Code File 并列）
    private const float TitleBarRightPadding = 14f;          // 最右边 ✕ 距窗口右缘
    private const float TitleBarCloseWidth = 56f;            // ✕ 按钮固定宽
    private const float TitleBarGap = 12f;                   // 按钮之间的横向间隔
    private const float TitleBarLabelPadding = 30f;          // 按钮文字左右各留 15px

    /// <summary>文件面板的两种用途：载入（浏览磁盘选文件）/ 保存（导出到指定目录 + 文件名）。</summary>
    private enum FilePickerMode { Open, Save }

    private GameObject filePickerRoot;                       // 全屏遮罩 + 面板
    private Text filePickerTitleText;                        // 标题（随模式切换文案）
    private Text filePickerPathText;                         // 当前目录路径
    private Text filePickerPageText;                         // 分页信息
    private RectTransform filePickerListRt;                  // 行容器
    private RectTransform filePickerSaveRowRt;               // 保存模式：文件名输入 + 保存按钮行
    private InputField filePickerNameField;                  // 保存模式：文件名输入框
    private Button filePickerSaveBtn;                        // 保存按钮（等待覆盖确认时改文案）
    private Text filePickerSaveBtnText;
    private readonly List<Button> filePickerRowBtns = new List<Button>();
    private FilePickerMode filePickerMode = FilePickerMode.Open;
    private string filePickerDir = string.Empty;             // 空串 = 「此电脑」盘符列表视图
    private int filePickerPage;
    private string pendingOverwritePath;                     // 保存时待二次确认的已存在路径
    private string lastCodeFilePath;                         // 最近一次载入 / 保存的文件（作保存默认名）

    private const int FilePickerRowsPerPage = 12;
    private const float FilePickerRowStride = 30f;
    private const string LastDirPrefKey = "atv.codeEditor.lastDir";
    private static readonly string[] FilePickerExts = { ".cs", ".txt", ".csharp" };

    /// <summary>文件选择面板是否处于打开状态（供 Update / Esc 路由判断优先级）。</summary>
    public bool IsCodeFilePickerOpen
    {
        get { return filePickerRoot != null && filePickerRoot.activeSelf; }
    }

    /// <summary>打开「载入」面板（F8 / 顶栏按钮）：首次=此电脑盘符列表，之后停在上次访问的目录。</summary>
    public void OpenCodeFilePicker()
    {
        ShowCodeFilePicker(FilePickerMode.Open);
    }

    /// <summary>打开「保存」面板（F9 / 顶栏按钮）：把编辑器当前代码导出到指定目录 + 指定文件名。</summary>
    public void OpenCodeSavePicker()
    {
        ShowCodeFilePicker(FilePickerMode.Save);
    }

    /// <summary>按模式准备并显示面板（两种模式共用同一套节点，只是切换标题 / 保存行 / 分页行的显隐）。</summary>
    private void ShowCodeFilePicker(FilePickerMode mode)
    {
        if (filePickerRoot == null)
            BuildCodeFilePicker();
        if (filePickerRoot == null)
            return;

        filePickerMode = mode;
        pendingOverwritePath = null;

        // 目录：优先上次访问过的目录（跨会话用 PlayerPrefs 记住）；没有记录时进「此电脑」盘符列表
        string remembered = PlayerPrefs.GetString(LastDirPrefKey, string.Empty);
        filePickerDir = (!string.IsNullOrEmpty(remembered) && System.IO.Directory.Exists(remembered))
            ? remembered
            : string.Empty;

        if (filePickerTitleText != null)
        {
            filePickerTitleText.text = mode == FilePickerMode.Open
                ? Localization.Pick("打开代码文件（.cs / .txt）", "Open code file (.cs / .txt)")
                : Localization.Pick("保存代码到指定文件夹", "Save code to a folder");
            filePickerTitleText.font = UIFontHelper.LoadForText(filePickerTitleText.text, filePickerTitleText.fontSize);
        }
        if (filePickerSaveRowRt != null)
            filePickerSaveRowRt.gameObject.SetActive(mode == FilePickerMode.Save);
        if (filePickerPageText != null)
            filePickerPageText.gameObject.SetActive(mode == FilePickerMode.Open);
        if (filePickerNameField != null)
            filePickerNameField.text = DefaultSaveFileName();
        if (filePickerSaveBtnText != null)
            SetButtonLabel(filePickerSaveBtn, Localization.Pick("保存", "Save"));

        filePickerPage = 0;
        filePickerRoot.SetActive(true);
        filePickerRoot.transform.SetAsLastSibling();
        RefreshCodeFilePicker();

        SetStatus(mode == FilePickerMode.Open
            ? Localization.Pick("载入：点目录进入，点文件即载入，Esc 取消",
                                "Open: click a folder to enter, click a file to load, Esc to cancel")
            : Localization.Pick("导出：选好目录并输入文件名后按「保存」，Esc 取消",
                                "Export: choose a folder, type a file name, then press Save (Esc to cancel)"), false);
    }

    /// <summary>保存模式的默认文件名：优先复用上次打开 / 保存过的文件名。</summary>
    private string DefaultSaveFileName()
    {
        if (!string.IsNullOrEmpty(lastCodeFilePath))
        {
            try
            {
                string n = System.IO.Path.GetFileName(lastCodeFilePath);
                if (!string.IsNullOrEmpty(n))
                    return n;
            }
            catch { }
        }
        return "code_dump.cs";
    }

    /// <summary>关闭文件选择面板（只隐藏，不销毁；下次打开复用同一套节点）。</summary>
    public void CloseCodeFilePicker()
    {
        if (filePickerRoot != null)
            filePickerRoot.SetActive(false);
    }

    /// <summary>构建文件选择面板：遮罩 + 居中面板 + 标题栏 + 路径栏 + 13 行列表 + 翻页栏。</summary>
    private void BuildCodeFilePicker()
    {
        if (editorRoot == null)
            return;

        RectTransform rootRt = CreateRect("CodeFilePicker", editorRoot.transform,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 4000f), new Vector2(0.5f, 0.5f));
        filePickerRoot = rootRt.gameObject;

        Image mask = filePickerRoot.AddComponent<Image>();
        mask.color = UITheme.WithAlpha(UITheme.Void, 0.78f);
        mask.raycastTarget = true; // 吃掉点击，避免穿透到下面的编辑器

        RectTransform panelRt = CreateRect("Panel", filePickerRoot.transform,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 566f), new Vector2(0.5f, 0.5f));
        Image panelBg = panelRt.gameObject.AddComponent<Image>();
        panelBg.color = UITheme.Panel;

        Text title = CreateText("Title", panelRt,
            Localization.Pick("打开代码文件（.cs / .txt）", "Open code file (.cs / .txt)"),
            22, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        filePickerTitleText = title;
        SetAnchored(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -54f), new Vector2(-20f, -10f));

        // 路径栏
        RectTransform pathBox = CreateRect("PathBox", panelRt, new Vector2(0.5f, 1f),
            new Vector2(0f, -58f), new Vector2(744f, 28f), new Vector2(0.5f, 0.5f));
        Image pathBg = pathBox.gameObject.AddComponent<Image>();
        pathBg.color = UITheme.PanelSunk;
        pathBox.anchoredPosition = new Vector2(0f, -72f);

        filePickerPathText = CreateText("Path", pathBox, string.Empty, 15, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        filePickerPathText.horizontalOverflow = HorizontalWrapMode.Overflow;
        filePickerPathText.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchored(filePickerPathText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(12f, 0f), new Vector2(-12f, 0f));

        // 行容器
        filePickerListRt = CreateRect("List", panelRt, new Vector2(0.5f, 1f),
            new Vector2(0f, -92f), new Vector2(744f, FilePickerRowsPerPage * FilePickerRowStride),
            new Vector2(0.5f, 1f));
        Image listBg = filePickerListRt.gameObject.AddComponent<Image>();
        listBg.color = UITheme.WithAlpha(UITheme.PanelDeep, 0.85f);

        // 保存模式专用行：文件名输入 + 保存按钮（打开模式 / 分页信息行互斥显示在同一带）
        filePickerSaveRowRt = CreateRect("SaveRow", panelRt, new Vector2(0.5f, 0f),
            new Vector2(0f, 77f), new Vector2(744f, 38f), new Vector2(0.5f, 0.5f));

        Text nameLabel = CreateText("NameLabel", filePickerSaveRowRt,
            Localization.Pick("文件名", "File name"), 16, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        SetAnchored(nameLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(2f, 0f), new Vector2(96f, 0f));

        filePickerNameField = CreatePickerNameField(filePickerSaveRowRt);

        filePickerSaveBtn = CreatePickerButton("BtnPickerSave", filePickerSaveRowRt,
            Localization.Pick("保存", "Save"), SaveCodeFromPicker,
            new Vector2(128f, 38f), new Vector2(-0f, 0f), new Vector2(1f, 0.5f),
            UITheme.WithAlpha(UITheme.Accent, 0.26f));
        RectTransform saveBtnRt = filePickerSaveBtn.GetComponent<RectTransform>();
        saveBtnRt.anchorMin = new Vector2(1f, 0f);
        saveBtnRt.anchorMax = new Vector2(1f, 1f);
        saveBtnRt.pivot = new Vector2(1f, 0.5f);
        saveBtnRt.offsetMin = new Vector2(-128f, 0f);
        saveBtnRt.offsetMax = new Vector2(0f, 0f);
        filePickerSaveBtnText = filePickerSaveBtn.GetComponentInChildren<Text>();

        // 底部按钮栏（anchor 贴面板底边，pos.y 为距底距离）
        CreatePickerButton("BtnPickerUp", panelRt, Localization.Pick("↑ 上级", "↑ Up"),
            GoUpCodeFilePicker, new Vector2(120f, 34f), new Vector2(-320f, 24f), new Vector2(0.5f, 0f),
            UITheme.WithAlpha(UITheme.PanelRaised, 1f));
        CreatePickerButton("BtnPickerDesktop", panelRt, Localization.Pick("桌面", "Desktop"),
            GoDesktopCodeFilePicker, new Vector2(96f, 34f), new Vector2(-206f, 24f), new Vector2(0.5f, 0f),
            UITheme.WithAlpha(UITheme.PanelRaised, 1f));
        CreatePickerButton("BtnPickerPrev", panelRt, Localization.Pick("上一页", "Prev"),
            delegate { TurnCodeFilePickerPage(-1); }, new Vector2(96f, 34f), new Vector2(-4f, 24f), new Vector2(0.5f, 0f),
            UITheme.WithAlpha(UITheme.PanelRaised, 1f));
        CreatePickerButton("BtnPickerNext", panelRt, Localization.Pick("下一页", "Next"),
            delegate { TurnCodeFilePickerPage(1); }, new Vector2(96f, 34f), new Vector2(98f, 24f), new Vector2(0.5f, 0f),
            UITheme.WithAlpha(UITheme.PanelRaised, 1f));
        CreatePickerButton("BtnPickerCancel", panelRt, Localization.Pick("取消", "Cancel"),
            CloseCodeFilePicker, new Vector2(112f, 34f), new Vector2(320f, 24f), new Vector2(0.5f, 0f),
            UITheme.WithAlpha(UITheme.AlertDim, 1f));

        filePickerPageText = CreateText("PageInfo", panelRt, string.Empty, 15, UITheme.TextMuted, TextAnchor.MiddleCenter);
        SetAnchored(filePickerPageText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(150f, 60f), new Vector2(-150f, 90f));

        filePickerRoot.SetActive(false);
        filePickerSaveRowRt.gameObject.SetActive(false);
    }

    /// <summary>按 filePickerDir 重新扫描目录并重建当前页的行按钮。</summary>
    private void RefreshCodeFilePicker()
    {
        if (filePickerListRt == null)
            return;

        for (int i = 0; i < filePickerRowBtns.Count; i++)
        {
            if (filePickerRowBtns[i] != null)
                Destroy(filePickerRowBtns[i].gameObject);
        }
        filePickerRowBtns.Clear();

        List<string> dirs = new List<string>();
        List<string> files = new List<string>();

        if (string.IsNullOrEmpty(filePickerDir))
        {
            // 「此电脑」视图：列可用磁盘根目录
            try
            {
                System.IO.DriveInfo[] drives = System.IO.DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        if (drives[i].IsReady)
                            dirs.Add(drives[i].RootDirectory.FullName);
                    }
                    catch { }
                }
                // 盘符按字母序（C: / D: / E: ...），保证每次打开顺序稳定
                dirs.Sort(System.StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }
        else
        {
            try
            {
                string[] sub = System.IO.Directory.GetDirectories(filePickerDir);
                System.Array.Sort(sub, System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sub.Length; i++)
                    dirs.Add(sub[i]);
            }
            catch { }
            try
            {
                string[] fs = System.IO.Directory.GetFiles(filePickerDir);
                System.Array.Sort(fs, System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fs.Length; i++)
                {
                    string ext = System.IO.Path.GetExtension(fs[i]);
                    for (int k = 0; k < FilePickerExts.Length; k++)
                    {
                        if (string.Equals(ext, FilePickerExts[k], System.StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add(fs[i]);
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        int total = dirs.Count + files.Count;
        int pageCount = Mathf.Max(1, (total + FilePickerRowsPerPage - 1) / FilePickerRowsPerPage);
        if (filePickerPage < 0)
            filePickerPage = 0;
        if (filePickerPage > pageCount - 1)
            filePickerPage = pageCount - 1;

        if (filePickerPathText != null)
        {
            filePickerPathText.text = string.IsNullOrEmpty(filePickerDir)
                ? Localization.Pick("此电脑（选择磁盘）", "This PC (choose a drive)")
                : filePickerDir;
            filePickerPathText.font = UIFontHelper.LoadForText(filePickerPathText.text, filePickerPathText.fontSize);
        }
        if (filePickerPageText != null)
        {
            filePickerPageText.text = Localization.Pick("第 ", "Page ") + (filePickerPage + 1) + " / " + pageCount
                                    + Localization.Pick(" 页 · 共 " + total + " 项", " · " + total + " items");
            filePickerPageText.font = UIFontHelper.LoadForText(filePickerPageText.text, filePickerPageText.fontSize);
        }

        int start = filePickerPage * FilePickerRowsPerPage;
        int rowIndex = 0;
        for (int i = start; i < total && rowIndex < FilePickerRowsPerPage; i++, rowIndex++)
        {
            if (i < dirs.Count)
            {
                string d = dirs[i];
                string dirTag = Localization.Pick("[目录] ", "[DIR] ");
                string shortName = System.IO.Path.GetFileName(d.TrimEnd('\\', '/'));
                // 盘符根（C:\）的 GetFileName 为空，直接显示盘符全路径
                string label = string.IsNullOrEmpty(shortName) ? dirTag + d : dirTag + shortName + "\\";
                Button row = CreatePickerRow(filePickerRowBtns.Count, label,
                    UITheme.WithAlpha(UITheme.PanelRaised, 0.95f), rowIndex, delegate { EnterCodeFilePickerDir(d); });
                if (row != null)
                    filePickerRowBtns.Add(row);
            }
            else
            {
                string f = files[i - dirs.Count];
                long bytes = 0L;
                try { bytes = new System.IO.FileInfo(f).Length; } catch { }
                string label = System.IO.Path.GetFileName(f) + "    " + Mathf.Max(1, (int)(bytes / 1024L)) + " KB";
                Button row = CreatePickerRow(filePickerRowBtns.Count, label,
                    UITheme.WithAlpha(UITheme.PanelSunk, 0.95f), rowIndex, delegate { OnPickerFileRowClicked(f); });
                if (row != null)
                    filePickerRowBtns.Add(row);
            }
        }

        if (total == 0)
        {
            Text empty = CreateText("Empty", filePickerListRt,
                Localization.Pick("（此目录下没有 .cs / .txt 文件）", "(no .cs / .txt files here)"),
                13, UITheme.TextMuted, TextAnchor.MiddleCenter);
            SetAnchored(empty.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            filePickerRowBtns.Add(null);
        }
    }

    /// <summary>在列表容器里创建一行可点击条目（左对齐文本）。</summary>
    private Button CreatePickerRow(int index, string label, Color bg, int rowIndex, System.Action onClick)
    {
        if (filePickerListRt == null)
            return null;

        GameObject go = new GameObject("Row" + index, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(filePickerListRt, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        float top = rowIndex * FilePickerRowStride;
        rt.offsetMin = new Vector2(2f, -(top + FilePickerRowStride - 2f));
        rt.offsetMax = new Vector2(-2f, -top);

        Image img = go.GetComponent<Image>();
        img.color = bg;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(delegate { onClick(); });

        Text txt = CreateText("Text", rt, label, 15, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchored(txt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(12f, 0f), new Vector2(-12f, 0f));
        return btn;
    }

    private void SetAnchored(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        if (rt == null)
            return;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    /// <summary>面板按钮：复用 CreateButton，但把文字放大到 16 号（系统对话框那种字号观感）。</summary>
    private Button CreatePickerButton(string name, Transform parent, string label, System.Action onClick,
        Vector2 size, Vector2 pos, Vector2 anchor, Color bg)
    {
        Button b = CreateButton(name, parent, label, onClick, size, pos, anchor, bg);
        SetButtonLabel(b, label);
        return b;
    }

    /// <summary>改写按钮文字并重设字号（中英文都走 UIFontHelper，避免缺字形）。</summary>
    private static void SetButtonLabel(Button btn, string label)
    {
        if (btn == null)
            return;
        Text t = btn.GetComponentInChildren<Text>();
        if (t == null)
            return;
        t.text = label;
        t.fontSize = 16;
        t.font = UIFontHelper.LoadForText(label, 16);
    }

    /// <summary>保存模式的文件名输入框（单行、限长 120、拒绝富文本）。</summary>
    private InputField CreatePickerNameField(Transform parent)
    {
        GameObject go = new GameObject("NameField", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(96f, 3f);
        rt.offsetMax = new Vector2(-136f, -3f);

        Image bg = go.GetComponent<Image>();
        bg.color = UITheme.PanelSunk;

        InputField field = go.GetComponent<InputField>();
        field.targetGraphic = bg;

        Text txt = CreateText("Text", rt, "code_dump.cs", 15, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        txt.supportRichText = false;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchored(txt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(10f, 0f), new Vector2(-10f, 0f));

        field.textComponent = txt;
        field.text = "code_dump.cs";
        field.lineType = InputField.LineType.SingleLine;
        field.characterLimit = 120;
        field.caretColor = UITheme.Accent;
        field.selectionColor = UITheme.WithAlpha(UITheme.Accent, 0.35f);
        return field;
    }

    /// <summary>记住本次访问的目录：下次打开面板直接停在这里（跨会话由 PlayerPrefs 保存）。</summary>
    private static void RememberPickerDir(string dir)
    {
        if (string.IsNullOrEmpty(dir))
            return;
        PlayerPrefs.SetString(LastDirPrefKey, dir);
        PlayerPrefs.Save();
    }

    /// <summary>进入某个子目录（页面回到第一页，并记住该目录）。</summary>
    private void EnterCodeFilePickerDir(string dir)
    {
        filePickerDir = dir;
        filePickerPage = 0;
        pendingOverwritePath = null;
        if (filePickerSaveBtnText != null)
            SetButtonLabel(filePickerSaveBtn, Localization.Pick("保存", "Save"));
        RememberPickerDir(filePickerDir);
        RefreshCodeFilePicker();
        SetStatus(dir, false);
    }

    /// <summary>回到上一级；已到盘符根则进入「此电脑」视图。</summary>
    private void GoUpCodeFilePicker()
    {
        if (string.IsNullOrEmpty(filePickerDir))
        {
            CloseCodeFilePicker();
            return;
        }
        string parent = null;
        try
        {
            string trimmed = filePickerDir.TrimEnd('\\', '/');
            parent = System.IO.Path.GetDirectoryName(trimmed);
        }
        catch { }
        filePickerDir = string.IsNullOrEmpty(parent) ? string.Empty : parent;
        filePickerPage = 0;
        pendingOverwritePath = null;
        if (filePickerSaveBtnText != null)
            SetButtonLabel(filePickerSaveBtn, Localization.Pick("保存", "Save"));
        RememberPickerDir(filePickerDir);
        RefreshCodeFilePicker();
    }

    /// <summary>跳到桌面目录（常用的存放位置）。</summary>
    private void GoDesktopCodeFilePicker()
    {
        string desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desk) || !System.IO.Directory.Exists(desk))
        {
            SetStatus(Localization.Pick("找不到桌面目录", "Desktop folder not found"), true);
            return;
        }
        filePickerDir = desk;
        filePickerPage = 0;
        pendingOverwritePath = null;
        if (filePickerSaveBtnText != null)
            SetButtonLabel(filePickerSaveBtn, Localization.Pick("保存", "Save"));
        RememberPickerDir(filePickerDir);
        RefreshCodeFilePicker();
    }

    private void TurnCodeFilePickerPage(int delta)
    {
        filePickerPage += delta;
        RefreshCodeFilePicker();
    }

    /// <summary>点文件行：载入模式下直接载入；保存模式下只把文件名填进输入框（作为覆盖目标）。</summary>
    private void OnPickerFileRowClicked(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (filePickerMode == FilePickerMode.Save)
        {
            string name = System.IO.Path.GetFileName(path);
            if (filePickerNameField != null)
                filePickerNameField.text = name;
            pendingOverwritePath = null;
            SetButtonLabel(filePickerSaveBtn, Localization.Pick("保存", "Save"));
            SetStatus(Localization.Pick("已填入文件名：", "File name set: ") + name, false);
            return;
        }

        LoadCodeFileFromPicker(path);
    }

    /// <summary>选中的文件载入编辑器；载入前的全文进 undo 栈，可 Ctrl+Z 回退。</summary>
    private void LoadCodeFileFromPicker(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            long bytes = new System.IO.FileInfo(path).Length;
            if (bytes > 4L * 1024L * 1024L)
            {
                SetStatus(Localization.Pick("文件过大（>4MB），已拒绝载入：", "File too large (>4MB), refused: ")
                          + System.IO.Path.GetFileName(path), true);
                return;
            }
            string code = System.IO.File.ReadAllText(path);
            string prev = GetFullCode();
            CloseCodeFilePicker();
            SetFullCode(code, 0);
            // 载入新文件后视图回到文件第一行：不清零会停在上一份文件停留的位置，
            // 而且上一帧的光标基准与新文件的 0 光标的差值会被误判成一次「光标移动」，把视图再拽下去一截。
            ApplyScrollY(0f);
            UpdateScrollBars();
            lastCaretPos = GlobalCaretPosition();
            lastCaretPosInSeg = 0;
            lastCaretSegIndex = 0;
            scrollFollowSuppressUntil = Time.unscaledTime + 0.3f;
            if (!string.IsNullOrEmpty(prev) && prev != code)
            {
                undoList.Add(prev);
                redoList.Clear();
            }
            lastCodeFilePath = path;
            try { RememberPickerDir(System.IO.Path.GetDirectoryName(path)); } catch { }
            SetStatus(Localization.Pick("已打开 ", "Opened ") + System.IO.Path.GetFileName(path)
                      + "（" + code.Length + Localization.Pick(" 字符 / ", " chars / ") + CountLines(code)
                      + Localization.Pick(" 行，Ctrl+Z 可撤销载入）", " lines, Ctrl+Z to undo)"), false);
            Debug.Log("[CodeEditor] 已载入代码文件 → " + System.IO.Path.GetFullPath(path) + " | 字符数=" + code.Length);
        }
        catch (System.Exception ex)
        {
            SetStatus(Localization.Pick("打开失败：", "Open failed: ") + ex.Message, true);
            Debug.LogError("[CodeEditor] 载入代码文件失败：" + path + " → " + ex.Message);
        }
    }

    /// <summary>保存模式：把当前代码写到 filePickerDir + 输入的文件名；已存在则先要求确认覆盖。</summary>
    private void SaveCodeFromPicker()
    {
        string name = filePickerNameField != null ? (filePickerNameField.text ?? string.Empty) : string.Empty;
        name = name.Trim();
        if (name.Length == 0)
        {
            SetStatus(Localization.Pick("请先输入文件名", "Please enter a file name"), true);
            return;
        }
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
        {
            if (name.IndexOf(invalid[i]) >= 0)
            {
                SetStatus(Localization.Pick("文件名含非法字符：", "Invalid character in file name: ") + invalid[i], true);
                return;
            }
        }
        if (name == "." || name == "..")
        {
            SetStatus(Localization.Pick("文件名无效", "Invalid file name"), true);
            return;
        }
        if (System.IO.Path.GetExtension(name).Length == 0)
            name += ".cs";

        if (string.IsNullOrEmpty(filePickerDir) || !System.IO.Directory.Exists(filePickerDir))
        {
            SetStatus(Localization.Pick("请先进入一个具体文件夹（盘符列表不能直接保存）",
                                        "Enter a folder first (cannot save into the drive list)"), true);
            return;
        }

        string full;
        try
        {
            full = System.IO.Path.Combine(filePickerDir, name);
        }
        catch (System.Exception ex)
        {
            SetStatus(Localization.Pick("路径拼接失败：", "Path error: ") + ex.Message, true);
            return;
        }

        if (System.IO.File.Exists(full) && string.Compare(pendingOverwritePath, full, true) != 0)
        {
            pendingOverwritePath = full;
            SetButtonLabel(filePickerSaveBtn, Localization.Pick("确认覆盖?", "Overwrite?"));
            SetStatus(Localization.Pick("该文件已存在：再点一次「确认覆盖?」写入 ", "File exists: press Overwrite? again to write ") + full, true);
            return;
        }

        try
        {
            string code = GetFullCode();
            System.IO.File.WriteAllText(full, code, new System.Text.UTF8Encoding(true));
            lastCodeFilePath = full;
            pendingOverwritePath = null;
            RememberPickerDir(filePickerDir);
            CloseCodeFilePicker();
            SetStatus(Localization.Pick("已保存到 ", "Saved to ") + full
                      + "（" + code.Length + Localization.Pick(" 字符 / ", " chars / ") + CountLines(code)
                      + Localization.Pick(" 行）", " lines)"), false);
            Debug.Log("[CodeEditor] 代码已导出 → " + full + " | 字符数=" + code.Length);
        }
        catch (System.Exception ex)
        {
            SetStatus(Localization.Pick("保存失败：", "Save failed: ") + ex.Message, true);
            Debug.LogError("[CodeEditor] 导出代码失败：" + full + " → " + ex.Message);
        }
    }

    /// <summary>
    /// 按当前语言重排标题栏：4 个功能按钮 + 标题。
    ///   - 文案中英各一套（中文模式必须显示中文，而不是构建时那份英文）；
    ///   - 每个按钮宽度按文案实际量出来的 preferredWidth + 左右各 15px，中英文切换后不会出现文字挤出 / 空荡；
    ///   - 从右往左依次排布，按钮之间固定 12px 间隔，标题按剩余宽度收右边距。
    /// </summary>
    private void RefreshTitleBarChrome()
    {
        if (titleBarTitleText == null)
            return;

        bool zh = Localization.IsChinese;

        titleBarTitleText.text = zh ? "炮台代码编辑器" : "Tower Code Editor";
        titleBarTitleText.font = UIFontHelper.LoadForText(titleBarTitleText.text, titleBarTitleText.fontSize);

        ApplyChromeButton(titleBtnStop, zh ? "停止 (F7)" : "Stop (F7)",
            zh ? "Tips：按 F7 或点此按钮结束当前运行的用户代码，炮台回到待命状态。"
               : "Tips: press F7 (or this button) to stop the running user code and park the tower.");
        ApplyChromeButton(titleBtnCompile, zh ? "编译运行 (F6)" : "Compile & Run (F6)",
            zh ? "Tips：按 F6 或点此按钮编译当前代码并立即运行；编译失败会在下方逐条列出错误行。"
               : "Tips: press F6 (or this button) to compile and run the current code; any error is listed with its line number below.");
        ApplyChromeButton(titleBtnExport, zh ? "导出 (F9)" : "Export (F9)",
            zh ? "Tips：按 F9 或点此按钮打开导出面板，先选目录再填文件名，即可把当前代码写入磁盘（UTF-8 with BOM）。"
               : "Tips: press F9 (or this button) to open the export panel - pick a folder, type a file name, and the current code is written to disk (UTF-8 with BOM).");
        ApplyChromeButton(titleBtnOpen, zh ? "打开代码文件 (F8)" : "Open Code File (F8)",
            zh ? "Tips：按 F8 或点此按钮打开文件面板，可逐级浏览磁盘目录，选中 .cs 或 .txt 文件即载入编辑器；载入前的内容仍留在撤销栈里（Ctrl+Z 可回退）。"
               : "Tips: press F8 (or this button) to open the file panel and browse your disk; picking a .cs or .txt file loads it into the editor, while the previous content stays in the undo stack (Ctrl+Z).");
        // 「报错界面」按钮：与 Open Code File 并列，负责展开 / 收起底部报错区（打开编辑器时默认收起）
        ApplyChromeButton(titleBtnErrors, zh ? "报错界面 (F10)" : "Error Panel (F10)",
            zh ? "Tips：按 F10 或点此按钮展开 / 收起底部报错界面；打开编辑器时默认不显示报错，编译失败会自动弹出。紫色 RE 窗口按 F11 单独开关。"
               : "Tips: press F10 (or this button) to expand/collapse the error panel; it stays hidden when the editor opens and pops up automatically on compile errors. The purple RE window toggles with F11.");
        // 报错区当前是否可展开：收起态时按钮文案前加「▸」，展开态加「▾」，一眼看出下一次点击是开还是关
        if (titleBtnErrors != null)
        {
            Text errLabel = titleBtnErrors.GetComponentInChildren<Text>();
            if (errLabel != null)
            {
                string baseLabel = errLabel.text.Replace("▸ ", string.Empty).Replace("▾ ", string.Empty);
                errLabel.text = (errorZoneCollapsed ? "▸ " : "▾ ") + baseLabel;
                errLabel.font = UIFontHelper.LoadForText(errLabel.text, errLabel.fontSize);
            }
        }

        // 从右往左：✕ 按钮中点固定在 -TitleBarRightPadding，其后 5 个按钮按各自文案宽度 + 12px 间隔依次右对齐
        float cursor = -(TitleBarRightPadding + TitleBarCloseWidth * 0.5f) - TitleBarGap;
        cursor = LayoutChromeButton(titleBtnStop, cursor);
        cursor = LayoutChromeButton(titleBtnCompile, cursor);
        cursor = LayoutChromeButton(titleBtnExport, cursor);
        cursor = LayoutChromeButton(titleBtnOpen, cursor);
        cursor = LayoutChromeButton(titleBtnErrors, cursor);

        // 标题只允许占到最左按钮左侧 20px，避免长文案顶到按钮
        RectTransform titleRt = titleBarTitleText.rectTransform;
        Vector2 offsetMax = titleRt.offsetMax;
        offsetMax.x = cursor - 20f;
        titleRt.offsetMax = offsetMax;
    }

    /// <summary>写按钮文案（含字体 / 不换行）并同步悬停提示，宽度在 LayoutChromeButton 里算。</summary>
    private static void ApplyChromeButton(Button btn, string label, string tip)
    {
        if (btn == null)
            return;
        Text text = btn.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = label;
            // 文案全部单行显示：宽度由下面按实际字宽给足，不允许自动换行 / 截断
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.font = UIFontHelper.LoadForText(label, text.fontSize);
        }
        HoverTip hover = btn.GetComponent<HoverTip>();
        if (hover != null)
            hover.tip = tip;
    }

    /// <summary>按文案实际宽度设置按钮尺寸，并把它的右边缘贴在 rightEdge 上；返回下一个按钮可用的右边缘。</summary>
    private static float LayoutChromeButton(Button btn, float rightEdge)
    {
        if (btn == null)
            return rightEdge;
        RectTransform rt = btn.GetComponent<RectTransform>();
        Text text = btn.GetComponentInChildren<Text>();
        float labelWidth = 0f;
        if (text != null)
        {
            labelWidth = text.preferredWidth;
            if (labelWidth <= 0f)
                labelWidth = text.text != null ? text.text.Length * text.fontSize * 0.62f : 0f;
        }
        float width = Mathf.Ceil(Mathf.Max(72f, labelWidth + TitleBarLabelPadding));
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        rt.anchoredPosition = new Vector2(rightEdge - width * 0.5f, rt.anchoredPosition.y);
        return rightEdge - width - TitleBarGap;
    }

    /// <summary>给按钮挂一个悬停提示：鼠标进入时把提示写到底部状态栏，离开时清空。</summary>
    private void AttachHoverTip(GameObject target, string tip)
    {
        if (target == null || string.IsNullOrEmpty(tip))
            return;
        HoverTip existing = target.GetComponent<HoverTip>();
        if (existing == null)
            existing = target.AddComponent<HoverTip>();
        existing.owner = this;
        existing.tip = tip;
    }

    /// <summary>按钮悬停提示组件（只改状态栏文本，不创建任何新 UI 节点）。</summary>
    private sealed class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public CodeEditorController owner;
        public string tip;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner != null)
                owner.SetStatus(tip, false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner != null)
                owner.SetStatus(string.Empty, false);
        }
    }

    private void AppendError(string text)
    {
        if (errorListText == null)
            return;
        errorListText.text = text;
        RefreshErrorLayout(text);
        if (!string.IsNullOrEmpty(text))
        {
            // 多行报错在状态栏报个数：玩家一眼知道下面还有几条（配合错误区滚轮）
            int count = CountLines(text);
            string label = Localization.Pick("编译失败 / 运行错误", "Compile / runtime error");
            if (count > 1)
                label += Localization.Pick(" · " + count + " 条（滚轮可翻）", " · " + count + " issues (wheel to scroll)");
            SetStatus(label, true);
        }
    }

    /// <summary>错误区可用高度上限：给代码区至少留 240px，同时不超过整个编辑器高度的 45%。</summary>
    private float GetErrorZoneMaxHeight()
    {
        float rootH = editorRootRt != null ? editorRootRt.rect.height : 1080f;
        if (rootH <= 1f || float.IsNaN(rootH))
            rootH = 1080f;
        float rootCap = rootH - 240f;              // 代码区最少 240px
        float screenCap = rootH * 0.45f;
        return Mathf.Max(ErrorZoneMinHeight, Mathf.Min(rootCap, screenCap));
    }

    /// <summary>按字号与可用宽度实测错误文字高度（不依赖等一帧布局，TextGenerator 直接算）。</summary>
    private float MeasureErrorHeight(string text, int size, float width)
    {
        if (string.IsNullOrEmpty(text) || errorListText == null)
            return 0f;
        TextGenerationSettings settings = errorListText.GetGenerationSettings(new Vector2(Mathf.Max(1f, width), 0f));
        settings.font = UIFontHelper.LoadForText(text, size);
        settings.fontSize = size;
        settings.fontStyle = FontStyle.Normal;
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.richText = false;
        settings.scaleFactor = 1f;
        TextGenerator gen = new TextGenerator();
        float h = gen.GetPreferredHeight(text, settings);
        if (float.IsNaN(h) || float.IsInfinity(h) || h < 0f)
            h = 0f;
        return h;
    }

    /// <summary>
    /// 按报错文案重排错误区：先试最大字号，放不下就让错误区长高（上限见 GetErrorZoneMaxHeight），
    /// 长到上限仍放不下才缩字号（15 → 11），最后仍超出时交给滚轮 —— 任何情况下内容都留在屏幕内。
    /// </summary>
    private void RefreshErrorLayout(string text)
    {
        if (errorListText == null)
            return;

        errorScrollY = 0f; // 新报错一律从第一条看起

        // 没有任何报错文字（编译成功 / 刚打开编辑器）：错误区不占画面，只留底部状态栏，
        // 屏幕上就是纯代码编辑状态；红条 / 粗提示条 / 报错视口一并收起。
        if (string.IsNullOrEmpty(text))
        {
            errorListText.text = string.Empty;
            errorListText.fontSize = Mathf.Clamp(errorFontSize, ErrorFontMin, ErrorFontZoomMax);
            errorListText.font = UIFontHelper.LoadForText(string.Empty, errorListText.fontSize);
            errorContentHeight = 0f;
            errorListText.rectTransform.sizeDelta = new Vector2(0f, 0f);
            errorZoneCollapsed = true;
            ApplyErrorZoneHeight(30f); // 状态栏高度
            return;
        }

        // 可视宽度：错误区宽度 - 左右各 12px 内边距；尚未完成首次布局时按编辑器宽度兜底
        float viewW = errorMaskRt != null ? errorMaskRt.rect.width : 0f;
        if (viewW <= 1f)
            viewW = Mathf.Max(320f, (editorRootRt != null ? editorRootRt.rect.width : 1920f) - 24f);

        float maxZone = GetErrorZoneMaxHeight();
        float chrome = 36f + ErrorTopInset;   // 视口上下内缩：底部 36（状态栏 30 + 6）+ 顶部 ErrorTopInset（红色提示条 + 分界条 + 6px 呼吸位）
        // 玩家拖过红条时，以手动设定的高度为排版上限；没拖过（或被压到底收起）就按最大可用高度排
        float fitCap = (errorZoneManualHeight > 0f && !errorZoneCollapsed) ? Mathf.Min(errorZoneManualHeight, maxZone) : maxZone;
        int maxSize = errorFontScaled
            ? Mathf.Clamp(errorFontSize, ErrorFontMin, ErrorFontZoomMax)
            : Mathf.Clamp(errorFontSize, ErrorFontMin, ErrorFontMax);
        float contentH = MeasureErrorHeight(text, maxSize, viewW);
        int size = maxSize;
        // 手动缩放过的字号直接尊重玩家设定，不再自动缩回去（否则刚放大就被缩回原样，滚轮等于白转）；
        // 放不下的部分交给「错误区长高 + 滚轮翻页」消化。
        if (!errorFontScaled && contentH + chrome > fitCap)
        {
            for (size = maxSize - 1; size >= ErrorFontMin; size--)
            {
                contentH = MeasureErrorHeight(text, size, viewW);
                if (contentH + chrome <= fitCap || size == ErrorFontMin)
                    break;
            }
        }

        errorListText.fontSize = size;
        errorListText.font = UIFontHelper.LoadForText(text, size);
        errorContentHeight = contentH;
        errorListText.rectTransform.sizeDelta = new Vector2(0f, contentH);

        float desired;
        if (errorZoneCollapsed)
        {
            // 收起态（红条与蓝条已合并消失）来了新报错：自动弹回来，别让玩家以为没报错
            errorZoneCollapsed = false;
            errorZoneManualHeight = 0f;
            desired = Mathf.Clamp(contentH + chrome, ErrorZoneMinHeight, maxZone);
        }
        else if (errorZoneManualHeight > 0f)
            desired = Mathf.Clamp(errorZoneManualHeight, ErrorZoneMinHeight, maxZone);   // 拖过红条：尊重玩家设定的高度
        else
            desired = Mathf.Clamp(contentH + chrome, ErrorZoneMinHeight, maxZone);        // 没拖过：跟着内容自适应
        ApplyErrorZoneHeight(desired);
    }

    /// <summary>
    /// 应用错误区高度：代码 / 行号视口与横向滚动条一律跟着错误区上缘走，中间不留缝。
    /// 高度允许一路压到 0 —— 压到 SeamMergeThreshold 以下时红色分界条与错误视口一起收掉，
    /// 视觉上只剩底部那条横滚动条（两条合并成一条）；把横条往上拖又能把错误区整块拉回来。
    /// </summary>
    private void ApplyErrorZoneHeight(float height)
    {
        height = Mathf.Clamp(height, 0f, GetErrorZoneMaxHeight());
        // 两条已经重合，不再各占一道，只留横条；errorZoneCollapsed 为「收起报错界面」态：
        // 打开编辑器 / 按 F10 收起时高度只留状态栏（30px），红条与粗提示条同样不显示，只有代码区 + 状态栏。
        bool merged = height < SeamMergeThreshold || errorZoneCollapsed;
        if (errorZoneRt != null)
            errorZoneRt.sizeDelta = new Vector2(0f, height);

        float reH = reZoneRt != null ? Mathf.Max(0f, reZoneRt.sizeDelta.y) : 0f;
        if (reZoneRt != null)
            reZoneRt.anchoredPosition = new Vector2(0f, height); // 紫窗始终叠在红窗上方
        float top = height + reH + 10f;   // 横向滚动条占 160→170 那 10px 的旧位置，这里同样留 10px
        if (lineViewportRt != null)
            lineViewportRt.offsetMin = new Vector2(0f, top);
        if (codeViewportRt != null)
            codeViewportRt.offsetMin = new Vector2(70f, top);
        if (vScrollTrackRt != null)
            vScrollTrackRt.offsetMin = new Vector2(-(rightReservedWidth + 16f), top); // 左边界随右侧预留宽度一起左移，保持 10px 宽
        if (hScrollTrackRt != null)
        {
            hScrollTrackRt.offsetMin = new Vector2(70f, height + reH);
            hScrollTrackRt.offsetMax = new Vector2(-(rightReservedWidth + 16f), top);
        }

        // 合并态：红条（视觉细条 + 透明命中区）/ 上方粗提示条 / 错误文字视口 / 错误竖滚动条全部收掉，屏幕上只剩底部那条横滚动条
        SetActiveIfChanged(errorSplitTopPadRt, !merged);
        SetActiveIfChanged(errorSplitBarRt, !merged);
        SetActiveIfChanged(errorSplitGripRt, !merged);
        SetActiveIfChanged(errorSplitHitRt, !merged);
        SetActiveIfChanged(errorMaskRt, !merged);
        SetActiveIfChanged(errorScrollTrackRt, !merged);

        ApplyErrorScroll();

        // 代码区可视高度变了：把已有滚动偏移夹回新范围，避免视口变高后露出底部空白（或变矮后滚过头）
        if (codeRt != null)
        {
            float maxY = GetScrollMaxY();
            float y = codeRt.anchoredPosition.y;
            if (y > maxY || y < 0f)
                codeRt.anchoredPosition = new Vector2(codeRt.anchoredPosition.x, Mathf.Clamp(y, 0f, maxY));
        }

        UpdateScrollBars();
    }

    /// <summary>切换显隐，但不产生多余的 SetActive 调用（避免每帧布局刷新时的无谓开销 / 重排）。</summary>
    private static void SetActiveIfChanged(RectTransform rt, bool active)
    {
        if (rt == null)
            return;
        if (rt.gameObject.activeSelf != active)
            rt.gameObject.SetActive(active);
    }

    /// <summary>收起报错界面：错误区只留状态栏高度（红条 / 粗提示条 / 报错文字 / 滚动条全部隐藏），代码区独占画面。</summary>
    private void CollapseErrorZone()
    {
        errorZoneCollapsed = true;
        errorZoneManualHeight = 0f;
        errorScrollY = 0f;
        // 这里绝不能走 RefreshErrorLayout：它见到 errorZoneCollapsed 会自动弹回（那是「来了新报错」的语义）。
        // 收起态只是把高度压到状态栏那一格，错误文本原样留在内存里，等 F10 或下次编译报错再显示。
        ApplyErrorZoneHeight(30f);  // 状态栏自身高度：收起后状态栏仍可见，代码区一直用到蓝条
    }

    /// <summary>展开报错界面：按内容排版一次错误区（没有报错文本就显示一句占位说明），并把滚动复位到顶部。</summary>
    private void ExpandErrorPanel()
    {
        errorZoneCollapsed = false;
        errorScrollY = 0f;
        string text = errorListText != null ? errorListText.text : string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            // 展开时手上没有任何报错：给一句占位提示，免得弹出一块纯空白让人以为界面坏了（下次编译结果会直接覆盖）
            text = Localization.Pick("暂无报错。编译失败时会在这里逐条列出错误行（Ctrl+滚轮可缩放字号）。",
                                     "No errors yet. Compile failures are listed here line by line (Ctrl+wheel to zoom).");
        }
        RefreshErrorLayout(text);
        ApplyErrorZoneHeight(errorZoneHeightCurrent());
    }

    // ================= 运行时错误（RE）紫色窗口：与红色 CE 窗口同构、互不覆盖 =================

    /// <summary>紫色窗口可用高度上限：代码区至少留 320px，同时不超过编辑器高度的 34%。</summary>
    private float GetReZoneMaxHeight()
    {
        float rootH = editorRootRt != null ? editorRootRt.rect.height : 1080f;
        if (rootH <= 1f || float.IsNaN(rootH)) rootH = 1080f;
        float rootCap = rootH - 320f;
        float ratioCap = rootH * 0.50f; // 上限放到半屏：此前 34% 常常一拖到顶就没反应，看着像「拖不动」
        return Mathf.Max(ReZoneMinHeight, Mathf.Min(rootCap, ratioCap));
    }

    /// <summary>按字号与可用宽度实测 RE 文字高度（TextGenerator 直接算，不等布局帧）。</summary>
    private float MeasureReHeight(string text, int size, float width)
    {
        if (string.IsNullOrEmpty(text) || reListText == null) return 0f;
        TextGenerationSettings settings = reListText.GetGenerationSettings(new Vector2(Mathf.Max(1f, width), 0f));
        settings.font = UIFontHelper.LoadForText(text, size);
        settings.fontSize = size;
        settings.fontStyle = FontStyle.Normal;
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.richText = false;
        settings.scaleFactor = 1f;
        TextGenerator gen = new TextGenerator();
        float h = gen.GetPreferredHeight(text, settings);
        if (float.IsNaN(h) || float.IsInfinity(h) || h < 0f) h = 0f;
        return h;
    }

    /// <summary>追加一条运行时错误（RE）：写进紫色窗口并自动展开，红色窗口只留编译错误（CE）。</summary>
    public void AppendRuntimeError(string message)
    {
        if (reListText == null) return;
        string line = Localization.Pick("RE：", "RE: ") + (message ?? string.Empty);
        string text = string.IsNullOrEmpty(reListText.text) ? line : reListText.text + "\n" + line;
        RefreshReLayout(text);
        SetStatus(Localization.Pick("运行时错误（RE）已弹出紫色窗口", "Runtime error (RE) shown in purple panel"), false);
        statusColor = UITheme.Purple; // RE 的状态文字也用紫色：红色只留给编译错误（CE）
        if (IsOpen) RefreshCursorStatus();
        RefreshTitleBarChrome();
    }

    /// <summary>清空紫色窗口（重新编译 / 关闭编辑器时调用）。</summary>
    public void ClearRuntimeErrorPanel()
    {
        if (reListText == null) return;
        reListText.text = string.Empty;
        reContentHeight = 0f;
        reScrollY = 0f;
        RefreshReLayout(string.Empty);
    }

    /// <summary>紫色窗口此刻是否可见（有内容且没被收起）。F10 判断用它决定先关紫窗还是去动红窗。</summary>
    public bool IsReZoneVisible()
    {
        return !reZoneCollapsed && reZoneRt != null && reZoneRt.sizeDelta.y > 1f;
    }

    /// <summary>关闭紫色 RE 窗口：只收高度、文字留在内存里，随时按 F11 再打开（不丢这次 RE）。</summary>
    public void CloseReZonePanel()
    {
        if (reZoneRt == null) return;
        reZoneCollapsed = true; // 关掉只是隐藏，再打开按字号重新自适应高度
        ApplyReZoneHeight(0f);
        UpdateReScrollBar();
        RefreshTitleBarChrome();
    }

    /// <summary>打开紫色 RE 窗口：固定尺寸；当前没有 RE 内容时只落一条状态栏提示，不弹空窗。</summary>
    public void OpenReZonePanel()
    {
        if (reZoneRt == null) return;
        if (reListText == null || string.IsNullOrEmpty(reListText.text))
        {
            statusColor = UITheme.Purple;
            if (IsOpen) RefreshCursorStatus();
            return;
        }
        reZoneCollapsed = false;
        ApplyReZoneHeight(GetReZoneAutoHeight());
        UpdateReScrollBar();
        RefreshTitleBarChrome();
    }

    /// <summary>F11：紫色 RE 窗口开 / 关（与 F10 的红色报错框互不干扰）。</summary>
    public void ToggleReZonePanel()
    {
        if (IsReZoneVisible()) CloseReZonePanel();
        else OpenReZonePanel();
    }

    /// <summary>紫窗自适应高度 = 内容实测高度 + 上下留白，夹在 [ReZoneMinHeight, 上限定值] 之间。</summary>
    private float GetReZoneAutoHeight()
    {
        float desired = reContentHeight + ReZoneChromeHeight;
        return Mathf.Clamp(desired, ReZoneMinHeight, GetReZoneMaxHeight());
    }

    /// <summary>按文案重排紫色窗口：窗口高度随字号自适应，内容多了交给滚轮翻页。</summary>
    private void RefreshReLayout(string text)
    {
        if (reListText == null) return;
        reScrollY = 0f;
        if (string.IsNullOrEmpty(text))
        {
            reListText.text = string.Empty;
            reListText.fontSize = reFontSize;
            reListText.font = UIFontHelper.LoadForText(string.Empty, reFontSize);
            reContentHeight = 0f;
            reListText.rectTransform.sizeDelta = new Vector2(0f, 0f);
            reZoneCollapsed = true;
            ApplyReZoneHeight(0f);
            return;
        }
        reListText.text = text;
        reListText.fontSize = reFontSize;
        reListText.font = UIFontHelper.LoadForText(text, reFontSize);
        float width = reMaskRt != null ? reMaskRt.rect.width : 0f;
        if (width <= 1f)
        {
            float rootW = editorRootRt != null ? editorRootRt.rect.width : 1600f;
            width = Mathf.Max(240f, rootW - 48f);
        }
        reContentHeight = MeasureReHeight(text, reFontSize, width);
        reListText.rectTransform.sizeDelta = new Vector2(0f, reContentHeight);
        // 自适应尺寸：窗口高度跟着「内容实测高度 + 上下留白」走，字号变大窗就变高、变小窗就变矮。
        float desired = GetReZoneAutoHeight();
        reZoneCollapsed = false;
        ApplyReZoneHeight(desired);
        UpdateReScrollBar();
    }

    /// <summary>应用紫色窗口高度（0 = 整块收起），并让代码区顶部与横滚动条一起让开这块高度。</summary>
    private void ApplyReZoneHeight(float height)
    {
        if (reZoneRt == null) return;
        height = Mathf.Clamp(height, 0f, GetReZoneMaxHeight());
        bool merged = height < 1f || reZoneCollapsed;
        reZoneRt.sizeDelta = new Vector2(0f, merged ? 0f : height);
        float ceH = errorZoneRt != null ? errorZoneRt.sizeDelta.y : 30f;
        reZoneRt.anchoredPosition = new Vector2(0f, ceH);
        SetActiveIfChanged(reSplitTopPadRt, !merged);
        SetActiveIfChanged(reSplitBarRt, !merged);
        SetActiveIfChanged(reSplitGripRt, !merged);
        SetActiveIfChanged(reSplitHitRt, !merged);
        SetActiveIfChanged(reMaskRt, !merged);
        SetActiveIfChanged(reScrollTrackRt, !merged);
        if (!merged) ApplyReScroll();
        ApplyErrorZoneHeight(ceH); // 代码区 / 横滚动条按「红 + 紫」两层总高重新让位
        UpdateScrollBars();
    }

    /// <summary>同步 RE 文字滚动偏移。</summary>
    private void ApplyReScroll()
    {
        if (reListText == null || reMaskRt == null) return;
        float viewH = ReViewportHeight();
        float maxScroll = Mathf.Max(0f, reContentHeight - viewH);
        // 内容没超出视口时不显示紫色滚动条，避免右侧常驻一条空轨道
        SetActiveIfChanged(reScrollHandleRt, maxScroll > 0.5f);
        if (maxScroll <= 0.5f) return;
        reScrollY = Mathf.Clamp(reScrollY, 0f, maxScroll);
        reListText.rectTransform.anchoredPosition = new Vector2(0f, reScrollY);
        UpdateReScrollBar();
    }

    private float ReViewportHeight()
    {
        float viewH = reMaskRt != null ? reMaskRt.rect.height : 0f;
        if (viewH <= 1f || float.IsNaN(viewH))
            viewH = Mathf.Max(24f, (reZoneRt != null ? reZoneRt.sizeDelta.y : 0f) - ReZoneChromeHeight);
        return viewH;
    }

    /// <summary>更新紫色窗口竖滚动条滑块长度与位置。</summary>
    private void UpdateReScrollBar()
    {
        if (reScrollHandleRt == null) return;
        float viewH = ReViewportHeight();
        float trackH = reScrollTrackRt != null ? reScrollTrackRt.rect.height : 0f;
        if (trackH <= 1f || float.IsNaN(trackH)) trackH = Mathf.Max(24f, viewH);
        float ratio = reContentHeight > 1f ? viewH / reContentHeight : 1f;
        float handleH = Mathf.Clamp(trackH * Mathf.Clamp01(ratio), 24f, trackH);
        float maxScroll = Mathf.Max(0f, reContentHeight - viewH);
        float t = maxScroll > 0.5f ? Mathf.Clamp01(reScrollY / maxScroll) : 0f;
        reScrollHandleRt.sizeDelta = new Vector2(reScrollHandleRt.sizeDelta.x, handleH);
        reScrollHandleRt.anchoredPosition = new Vector2(0f, -t * Mathf.Max(0f, trackH - handleH));
    }

    private void SetReScrollFromBar(float t)
    {
        float viewH = ReViewportHeight();
        float maxScroll = Mathf.Max(0f, reContentHeight - viewH);
        reScrollY = Mathf.Clamp01(t) * maxScroll;
        ApplyReScroll();
    }

    private bool IsPointerOverReZone()
    {
        return reMaskRt != null && reMaskRt.gameObject.activeInHierarchy &&
               RectTransformUtility.RectangleContainsScreenPoint(reMaskRt, Input.mousePosition, null);
    }

    /// <summary>滚轮翻 RE 窗口：指针在紫色文字区或它的滚动条上时接管滚轮。</summary>
    private bool HandleReWheelScroll(float wheel)
    {
        if (reZoneCollapsed || reMaskRt == null || reListText == null || Mathf.Abs(wheel) < 0.01f) return false;
        bool overText = IsPointerOverReZone();
        bool overBar = reScrollTrackRt != null && reScrollTrackRt.gameObject.activeInHierarchy &&
                       RectTransformUtility.RectangleContainsScreenPoint(reScrollTrackRt, Input.mousePosition, null);
        if (!overText && !overBar) return false;
        float viewH = ReViewportHeight();
        if (reContentHeight <= viewH + 0.5f) return true; // 放得下也吃掉滚轮，避免把代码区一起滚走
        float lineH = Mathf.Max(12f, reListText.fontSize * 1.45f);
        reScrollY -= wheel * lineH * 2f;
        ApplyReScroll();
        return true;
    }

    /// <summary>F10 / 标题栏 Error Panel 按钮：展开时收起，收起时展开。</summary>
    private void ToggleErrorPanel()
    {
        if (!IsOpen)
            return;
        if (errorZoneCollapsed)
            ExpandErrorPanel();
        else
            CollapseErrorZone();
        SetStatus(Localization.Pick(
            errorZoneCollapsed ? "报错界面已收起（按 F10 展开）" : "报错界面已展开（按 F10 收起）",
            errorZoneCollapsed ? "Error panel collapsed (F10 to expand)" : "Error panel expanded (F10 to collapse)"), false);
        RefreshTitleBarChrome(); // 同步按钮前导箭头（▸ 收起 / ▾ 展开）与宽度
    }

    /// <summary>
    /// 红条的自管拖动：不依赖 EventSystem 的 IBeginDrag。
    /// EventSystem 要等指针移动超过 pixelDragThreshold 才发 BeginDrag，而 OnBeginDrag 里拿到的
    /// position 是「越过阈值那一帧」的位置，不是按下点；红条命中带又只有十几像素，一划就出界，
    /// 于是表现为「按住了、也拖了，但有时候整条纹丝不动」。这里改成按下那一刻就用屏幕坐标判定并记起点，
    /// 拖动全程用当前指针算增量，指针移出红条范围也不中断，与系统滚动条的手感一致。
    /// </summary>
    private void TickErrorSplitInputDrag()
    {
        if (!IsOpen)
        {
            errorSplitInputDrag = false;
            return;
        }

        if (Input.GetMouseButtonDown(0) && CanStartErrorSplitInputDrag())
        {
            errorSplitInputDrag = true;
            errorSplitInputDragTime = Time.unscaledTime;
            errorSplitStartMouseY = Input.mousePosition.y;
            errorSplitStartHeight = errorZoneHeightCurrent();
            if (errorZoneManualHeight <= 0f)
                errorZoneManualHeight = errorSplitStartHeight;
        }

        if (!errorSplitInputDrag)
            return;

        // 按住超过 10 秒就当作抬起事件丢了，强制松手，免得松掉鼠标后红条还黏在指针上
        if (Time.unscaledTime - errorSplitInputDragTime > 10f)
        {
            errorSplitInputDrag = false;
            return;
        }

        if (Input.GetMouseButton(0))
            DragErrorSplitCore(Input.mousePosition.y);
        else
            errorSplitInputDrag = false;
    }

    /// <summary>按下点是否落在红条命中带内（含上下各 ErrorSplitInputSlack 的容差）。</summary>
    private bool CanStartErrorSplitInputDrag()
    {
        if (errorSplitHitRt == null || !errorSplitHitRt.gameObject.activeInHierarchy)
            return false; // 红条被压在底部收起时不接管指针，让蓝条自己去拉

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(errorSplitHitRt, Input.mousePosition, null, out local))
            return false;

        Rect r = errorSplitHitRt.rect;
        return local.x >= r.xMin && local.x <= r.xMax
            && local.y >= r.yMin - ErrorSplitInputSlack && local.y <= r.yMax + ErrorSplitInputSlack;
    }

    /// <summary>按下红色分隔条：记下起点（屏幕像素）与当时的错误区高度。</summary>
    private void BeginErrorSplitDrag(PointerEventData ev)
    {
        // 自管拖动已经在按下那一刻记好起点了：这里再写一次会把「按下点到越过拖动阈值」的那段位移吞掉。
        if (errorSplitInputDrag)
            return;
        errorSplitStartMouseY = ev.position.y;
        errorSplitStartHeight = errorZoneHeightCurrent();
        if (errorZoneManualHeight <= 0f)
            errorZoneManualHeight = errorSplitStartHeight;
    }

    /// <summary>
    /// 拖动中：往上拖错误区变高（代码区变矮），往下拖反之；下限放到 0 —— 一路压到底时
    /// 红条会与底部横滚动条重合、收掉，只剩一条；蓝条同样可作为把手把错误区重新拉出来。
    /// </summary>
    private void DragErrorSplit(PointerEventData ev)
    {
        DragErrorSplitCore(ev.position.y);
    }

    /// <summary>红条拖动的唯一核心：无论事件来自 EventSystem 还是自管输入，都按「当前指针 Y - 按下时指针 Y」算增量。</summary>
    private void DragErrorSplitCore(float pointerScreenY)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
        float dy = (pointerScreenY - errorSplitStartMouseY) / scale;
        float target = Mathf.Clamp(errorSplitStartHeight + dy, 0f, GetErrorZoneMaxHeight());
        errorZoneManualHeight = target;              // 记住手动设定值，后续报错自适应不再覆盖它
        // 压到合并阈值以下就算「收起」：红条与蓝条已经并成一条，再来报错时才自动弹回，避免卡在半收不放的怪状态
        errorZoneCollapsed = target < SeamMergeThreshold;
        ApplyErrorZoneHeight(target);
        UpdateErrorScrollBar();
    }

    /// <summary>同步错误文字滚动偏移（仅当内容高于可视区时有值，范围 [0, 内容高 - 可视高]）。</summary>
    private void ApplyErrorScroll()
    {
        if (errorListText == null || errorMaskRt == null)
            return;
        float viewH = ErrorViewportHeight();
        float maxScroll = Mathf.Max(0f, errorContentHeight - viewH);
        errorScrollY = Mathf.Clamp(errorScrollY, 0f, maxScroll);
        errorListText.rectTransform.anchoredPosition = new Vector2(0f, errorScrollY);
        UpdateErrorScrollBar();
    }

    /// <summary>错误视口可视高度：优先取实际 rect，首帧布局未完成时按错误区高度减去上下内缩（36 / 6）兜底。</summary>
    private float ErrorViewportHeight()
    {
        float h = errorMaskRt != null ? errorMaskRt.rect.height : 0f;
        if (h <= 1f || float.IsNaN(h))
            h = Mathf.Max(24f, errorZoneHeightCurrent() - (36f + ErrorTopInset));
        return h;
    }

    /// <summary>
    /// 刷新错误区红色滚动块：平时整条不可见（错误区右侧不出现任何红条），
    /// 只有报错内容真的超出可视高度时才冒出一小截滑块；长度按「可视高 : 内容高」换算，位置按滚动进度换算。
    /// </summary>
    private void UpdateErrorScrollBar()
    {
        if (errorScrollHandleRt == null)
            return;

        float viewH = ErrorViewportHeight();
        float maxScroll = Mathf.Max(0f, errorContentHeight - viewH);
        if (maxScroll <= 0.5f)
        {
            // 放得下：不显示任何滚动块
            if (errorScrollHandleRt.gameObject.activeSelf)
                errorScrollHandleRt.gameObject.SetActive(false);
            return;
        }

        if (!errorScrollHandleRt.gameObject.activeSelf)
            errorScrollHandleRt.gameObject.SetActive(true);

        float trackH = errorScrollTrackRt != null ? errorScrollTrackRt.rect.height : 0f;
        if (trackH <= 1f)
            trackH = Mathf.Max(24f, viewH); // 首帧布局未完成时按可视高兜底
        float ratio = errorContentHeight > 1f ? viewH / errorContentHeight : 1f;
        float handleH = Mathf.Clamp(trackH * Mathf.Clamp01(ratio), 24f, trackH);
        errorScrollHandleRt.sizeDelta = new Vector2(errorScrollHandleRt.sizeDelta.x, handleH);
        float t = Mathf.Clamp01(errorScrollY / Mathf.Max(0.001f, maxScroll));
        errorScrollHandleRt.anchoredPosition = new Vector2(0f, -t * Mathf.Max(0f, trackH - handleH));
    }

    /// <summary>错误区红色滑块拖动 / 轨道点击回调：t 为归一化 0~1 进度（0 = 第一条，1 = 最后一条）。</summary>
    private void SetErrorScrollFromBar(float t)
    {
        if (errorListText == null || errorMaskRt == null)
            return;
        float maxScroll = Mathf.Max(0f, errorContentHeight - ErrorViewportHeight());
        errorScrollY = Mathf.Clamp01(t) * maxScroll;
        ApplyErrorScroll();
    }

    /// <summary>滚轮翻错误区：指针落在错误区且内容超出可视区时才生效，返回 true 表示这次滚轮已被吃掉。</summary>
    private bool HandleErrorWheelScroll(float wheel)
    {
        if (errorMaskRt == null || errorListText == null || Mathf.Abs(wheel) < 0.01f)
            return false;
        if (errorContentHeight <= ErrorViewportHeight() + 0.5f)
            return false;
        // 指针落在错误文字区或其右侧红色滚动条上都算：滚轮与拖条两条入口行为一致
        bool overText = RectTransformUtility.RectangleContainsScreenPoint(errorMaskRt, Input.mousePosition, null);
        bool overBar = errorScrollTrackRt != null && errorScrollTrackRt.gameObject.activeSelf &&
                       RectTransformUtility.RectangleContainsScreenPoint(errorScrollTrackRt, Input.mousePosition, null);
        if (!overText && !overBar)
            return false;
        float lineH = Mathf.Max(12f, errorListText.fontSize * 1.45f);
        errorScrollY -= wheel * lineH * 2f;
        ApplyErrorScroll();
        return true;
    }

    /// <summary>
    /// Ctrl+滚轮：报错文字字号缩放（放大 / 缩小），鼠标需落在报错区（文字视口或右侧红滚动条）内。
    /// 缩放算法与代码区 HandleFontZoom 完全一致：以「鼠标所指的那一行」为锚点，该行缩放前后贴在同一屏幕位置。
    /// 手动缩放过字号后（errorFontScaled=true）RefreshErrorLayout 不再自动缩字号，放不下的部分交给长高 + 滚轮翻页。
    /// </summary>
    private bool HandleErrorFontZoom()
    {
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f || errorMaskRt == null || errorListText == null)
            return false;
        bool overText = errorMaskRt.gameObject.activeSelf &&
                        RectTransformUtility.RectangleContainsScreenPoint(errorMaskRt, Input.mousePosition, null);
        bool overBar = errorScrollTrackRt != null && errorScrollTrackRt.gameObject.activeSelf &&
                       RectTransformUtility.RectangleContainsScreenPoint(errorScrollTrackRt, Input.mousePosition, null);
        if (!overText && !overBar)
            return false;
        int newSize = Mathf.Clamp(errorFontSize + (wheel > 0f ? 2 : -2), ErrorFontMin, ErrorFontZoomMax);
        if (newSize == errorFontSize)
            return true;

        // 缩放前：记录当前滚动偏移与旧行高，并把鼠标屏幕点换算到内容层局部坐标
        // （errorListText pivot=(0.5,1)、锚点左上，局部 y 向下为负；内容层已带滚动偏移，换算结果天然是「行」位置）。
        float scrollY0 = errorScrollY;
        float lineHeight0 = Mathf.Max(12f, errorListText.fontSize * 1.45f);
        Vector2 localPt;
        bool hasPointer = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            errorListText.rectTransform, Input.mousePosition, null, out localPt);
        float rowF = hasPointer ? (-localPt.y) / lineHeight0 : 0f;
        if (rowF < 0f)
            rowF = 0f;                      // 鼠标落在内容上方留白 / 坐标换算失败：按第一行锚定
        int row = Mathf.FloorToInt(rowF);
        float frac = rowF - row;            // 0~1：鼠标落在该行内的纵向比例

        errorFontSize = newSize;
        errorFontScaled = true;             // 之后 RefreshErrorLayout 尊重该字号，不再自动缩回
        SaveFontSettings();                 // 报错字号同样存档：重启后按玩家调好的大小显示
        RefreshErrorLayout(errorListText.text); // 重算行高 / 内容高度 / 错误区高度（内部会把滚动清零，下面统一覆盖）

        // 锚定鼠标行：行高由 lineHeight0 变为 lineHeight1 后，第 row 行行顶下移 row×Δ，
        // 行内的 frac 部分再下移 frac×Δ；滚动偏移同量增加即可让该行仍贴在原屏幕位置。
        float lineHeight1 = Mathf.Max(12f, errorListText.fontSize * 1.45f);
        float scrollY = scrollY0 + (row + frac) * (lineHeight1 - lineHeight0);
        scrollY = Mathf.Clamp(scrollY, 0f, Mathf.Max(0f, errorContentHeight - ErrorViewportHeight()));
        errorScrollY = scrollY;
        ApplyErrorScroll();

        SetStatus(Localization.Pick("报错字号已调整：" + errorFontSize, "Error font size: " + errorFontSize), false);
        return true;
    }

    private bool IsInputFocused()
    {
        for (int i = 0; i < segments.Count; i++)
        {
            InputField f = segments[i].field;
            if (f != null && f.isFocused)
                return true;
        }
        if (inputField == null)
            return false;
        if (inputField.isFocused)
            return true; // InputField 自身聚焦状态优先（EventSystem 选中项可能滞后 / 不同步）
        EventSystem es = EventSystem.current;
        if (es == null)
            return false;
        GameObject selected = es.currentSelectedGameObject;
        if (selected == null)
            return false;
        if (selected == inputField.gameObject)
            return true;
        for (int i = 0; i < segments.Count; i++)
        {
            InputField f = segments[i].field;
            if (f != null && selected == f.gameObject)
                return true;
        }
        return false;
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
        titleBarBg.color = UITheme.PanelRaised; // #2D2D30

        Text title = CreateText("Title", titleBarRt, Localization.Pick("炮台代码编辑器", "Tower Code Editor"), titleFontSize, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        titleBarTitleText = title;
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 0f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(20f, 0f);
        titleRt.offsetMax = new Vector2(-670f, 0f);

        // 右侧按钮：✕ / 停止 (F7) / 编译运行 (F6) / 导出 (F9) / 载入 (F8)（从右往左）
        CreateButton("BtnClose", titleBarRt, "✕", Close, new Vector2(56f, 34f), new Vector2(-14f, 0f), new Vector2(1f, 0.5f), UITheme.PanelSunk);
        Button btnStop = CreateButton("BtnStop", titleBarRt, Localization.Pick("停止 (F7)", "Stop (F7)"), StopRunner, new Vector2(116f, 34f), new Vector2(-112f, 0f), new Vector2(1f, 0.5f), UITheme.AlertDim);
        titleBtnStop = btnStop;
        AttachHoverTip(btnStop != null ? btnStop.gameObject : null,
            Localization.Pick("Tips：按 F7 或点此按钮结束当前运行的用户代码，炮台回到待命状态。", "Tips: press F7 (or this button) to stop the running user code and park the tower."));

        Button btnCompile = CreateButton("BtnCompile", titleBarRt, Localization.Pick("编译运行 (F6)", "Compile & Run (F6)"), CompileAndRun, new Vector2(156f, 34f), new Vector2(-260f, 0f), new Vector2(1f, 0.5f), UITheme.WithAlpha(UITheme.Accent, 0.22f));
        titleBtnCompile = btnCompile;
        AttachHoverTip(btnCompile != null ? btnCompile.gameObject : null,
            Localization.Pick("Tips：按 F6 或点此按钮编译当前代码并立即运行；编译失败会在下方逐条列出错误行。", "Tips: press F6 (or this button) to compile and run the current code; any error is listed with its line number below."));

        // 「导出 (F9)」：弹出文件夹选择面板，由用户指定保存目录 + 文件名后写盘（UTF-8 with BOM）。
        Button btnExport = CreateButton("BtnExport", titleBarRt,
            Localization.Pick("导出 (F9)", "Export (F9)"),
            OpenCodeSavePicker,
            new Vector2(132f, 34f), new Vector2(-416f, 0f), new Vector2(1f, 0.5f),
            UITheme.WithAlpha(UITheme.Accent, 0.14f));
        titleBtnExport = btnExport;
        AttachHoverTip(btnExport != null ? btnExport.gameObject : null,
            Localization.Pick("Tips：按 F9 或点此按钮打开导出面板，先选目录再填文件名，即可把当前代码写入磁盘（UTF-8 with BOM）。", "Tips: press F9 (or this button) to open the export panel - pick a folder, type a file name, and the current code is written to disk (UTF-8 with BOM)."));

        // 「打开代码文件 (F8)」：弹出目录浏览面板，由用户自己选磁盘上任意代码文件载入。
        // 悬停时在底部状态栏给出提示；点文件即载入，载入前内容进 undo 栈（Ctrl+Z 可撤销）。
        Button btnOpenSaved = CreateButton("BtnOpenSaved", titleBarRt,
            Localization.Pick("打开代码文件 (F8)", "Open Code File (F8)"),
            OnOpenSavedCodeClicked,
            new Vector2(156f, 34f), new Vector2(-572f, 0f), new Vector2(1f, 0.5f),
            UITheme.WithAlpha(UITheme.TextMuted, 0.30f));
        titleBtnOpen = btnOpenSaved;
        AttachHoverTip(btnOpenSaved != null ? btnOpenSaved.gameObject : null,
            Localization.Pick("Tips：按 F8 或点此按钮打开文件面板，可逐级浏览磁盘目录，选中 .cs 或 .txt 文件即载入编辑器；载入前的内容仍留在撤销栈里（Ctrl+Z 可回退）。", "Tips: press F8 (or this button) to open the file panel and browse your disk; picking a .cs or .txt file loads it into the editor, while the previous content stays in the undo stack (Ctrl+Z)."));

        // 「报错界面 (F10)」：与 Open Code File 并列（同在标题栏右侧一排），展开 / 收起底部报错区。
        // 打开编辑器时默认收起，只显示代码编辑状态；编译失败会自动弹出，也可以用这个按钮随时调回来回看。
        Button btnErrors = CreateButton("BtnErrors", titleBarRt,
            Localization.Pick("报错界面 (F10)", "Error Panel (F10)"),
            ToggleErrorPanel,
            new Vector2(148f, 34f), new Vector2(-728f, 0f), new Vector2(1f, 0.5f),
            UITheme.WithAlpha(UITheme.TextMuted, 0.24f));
        titleBtnErrors = btnErrors;
        AttachHoverTip(btnErrors != null ? btnErrors.gameObject : null,
            Localization.Pick("Tips：按 F10 或点此按钮展开 / 收起底部报错界面；打开编辑器时默认不显示报错，编译失败会自动弹出。紫色 RE 窗口按 F11 单独开关。", "Tips: press F10 (or this button) to expand/collapse the error panel; it stays hidden when the editor opens and pops up automatically on compile errors. The purple RE window toggles with F11."));

        // 构建完立刻按当前语言排一次：文案、字体、按钮宽度与间隔都由它统一算
        RefreshTitleBarChrome();

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
        lineNumberText.font = UIFontHelper.LoadMono();   // 与代码区同字体，行号行高才能与代码逐行对齐
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
        codeViewportBg.color = UITheme.CodeBg;
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
        codeBg.color = UITheme.CodeBg; // 点击事件落在此 Image（InputField 挂在 CodeArea 上）

        // 底层 InputField（用户在此输入 / 编辑；文字透明，原生光标与选区可见）
        InputField input = codeGo.AddComponent<InputField>();
        input.targetGraphic = codeBg;
        Text inputText = CreateText("InputText", codeRt, "", codeFontSize, UITheme.WithAlpha(UITheme.TextPrimary, 0f), TextAnchor.UpperLeft);
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
        input.caretColor = UITheme.TextPrimary; // 原生光标为可见白色
        input.caretBlinkRate = caretBlinkRate; // 恢复默认约 0.85f 闪烁
        input.caretWidth = 1;
        // 选中高亮：半透明淡蓝（alpha 仅 0.26），叠加在深色底板上不遮字、不发白
        input.selectionColor = UITheme.WithAlpha(UITheme.CodeSelection, 0.26f);
        input.onValueChanged.AddListener(s => OnSegmentValueChanged(0));
        inputField = input;
        this.inputText = inputText;

        // 上层只读高亮 Text（同 RectTransform 完全重叠，显示高亮代码）
        highlightText = CreateText("HighlightText", codeRt, "", codeFontSize, UITheme.TextPrimary, TextAnchor.UpperLeft);
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
        vTrackImg.color = UITheme.WithAlpha(UITheme.CodeBg, 0f); // 透明轨道：仅保留可点击跳转的命中区域
        vTrackImg.raycastTarget = true;
        vScrollTrackRt.gameObject.AddComponent<ScrollBarTrackJump>().Setup(vScrollTrackRt, true, t => SetScrollFromBar(0, t));

        // 右侧竖滑块：青蓝色（与底部横滑块同色，UITheme.CodeScrollbar）、宽 10（与轨道同宽，比原来 6 更粗更好抓）
        vScrollHandleRt = CreateRect("VScrollHandle", vScrollTrackRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 60f), new Vector2(0.5f, 1f));
        vScrollHandleRt.anchorMin = new Vector2(0.5f, 1f);
        vScrollHandleRt.anchorMax = new Vector2(0.5f, 1f);
        vScrollHandleRt.pivot = new Vector2(0.5f, 1f);
        vScrollHandleRt.anchoredPosition = Vector2.zero;
        vScrollHandleRt.sizeDelta = new Vector2(10f, 60f);
        Image vHandleImg = vScrollHandleRt.gameObject.AddComponent<Image>();
        vHandleImg.color = UITheme.CodeScrollbar;   // 与底部横滑块同一个颜色（青蓝 #57D2EF / 50%）：横竖两根拉动条视觉统一
        vScrollHandleRt.gameObject.AddComponent<ScrollBarHandle>().Setup(vScrollTrackRt, true, t => SetScrollFromBar(0, t));
        // 滑块自建即隐藏：内容超出视口时由 UpdateScrollBars 唤起
        vScrollHandleRt.gameObject.SetActive(false);

        // 横条：贴底部错误区上缘（高 10，左 70 对齐代码视口左缘、右 16 留出竖条空间），轨道底色同样透明
        hScrollTrackRt = CreateRect("HScrollTrack", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(1834f, 10f), new Vector2(0f, 1f));
        hScrollTrackRt.anchorMin = Vector2.zero;
        hScrollTrackRt.anchorMax = new Vector2(1f, 0f);
        hScrollTrackRt.pivot = new Vector2(0f, 1f);
        hScrollTrackRt.offsetMin = new Vector2(70f, 160f);
        hScrollTrackRt.offsetMax = new Vector2(-16f, 170f);
        Image hTrackImg = hScrollTrackRt.gameObject.AddComponent<Image>();
        hTrackImg.color = UITheme.WithAlpha(UITheme.CodeBg, 0f); // 透明轨道：仅保留可点击跳转的命中区域
        hTrackImg.raycastTarget = true;
        hScrollTrackRt.gameObject.AddComponent<ScrollBarTrackJump>().Setup(hScrollTrackRt, false, t => SetScrollFromBar(1, t));
        // 蓝条只做横向滚动：不再兼任「上下拖拽拉出错误区」的把手，否则按住蓝条上下拖会把底部红框一起拽出来

        hScrollHandleRt = CreateRect("HScrollHandle", hScrollTrackRt, new Vector2(0f, 1f), Vector2.zero, new Vector2(120f, 10f), new Vector2(0f, 1f));
        hScrollHandleRt.anchorMin = new Vector2(0f, 1f);
        hScrollHandleRt.anchorMax = new Vector2(0f, 1f);
        hScrollHandleRt.pivot = new Vector2(0f, 1f);
        hScrollHandleRt.anchoredPosition = Vector2.zero;
        hScrollHandleRt.sizeDelta = new Vector2(120f, 10f);
        Image hHandleImg = hScrollHandleRt.gameObject.AddComponent<Image>();
        hHandleImg.color = UITheme.CodeScrollbar;
        hScrollHandleRt.gameObject.AddComponent<ScrollBarHandle>().Setup(hScrollTrackRt, false, t => SetScrollFromBar(1, t));
        hScrollHandleRt.gameObject.SetActive(false); // 横向滑块自建即隐藏：内容超宽时由 UpdateScrollBars 唤起

        // ---- 底部错误 / 状态区（默认高 160px，报错多时自动长高，最高不超过编辑器可用高度的 45%） ----
        RectTransform bottomRt = CreateRect("ErrorZone", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, ErrorZoneMinHeight), new Vector2(0.5f, 0f));
        bottomRt.anchorMin = Vector2.zero;
        bottomRt.anchorMax = new Vector2(1f, 0f);
        bottomRt.pivot = new Vector2(0.5f, 0f);
        bottomRt.anchoredPosition = Vector2.zero;
        bottomRt.sizeDelta = new Vector2(0f, ErrorZoneMinHeight);
        Image bottomBg = bottomRt.gameObject.AddComponent<Image>();
        bottomBg.color = UITheme.WithAlpha(consoleBgFullColor, 0f); // 根层不画颜色，只留指针拦截；真正的底板交给下面那层
        consoleBg = bottomBg;
        errorZoneRt = bottomRt;

        // 底板单独一层并让出顶部 6px：那条缝已归红线管辖，露出代码区底色后淡红才看得出是「红条上身」
        RectTransform errorBgRt = CreateRect("ErrorZoneBg", bottomRt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        errorBgRt.anchorMin = Vector2.zero;
        errorBgRt.anchorMax = Vector2.one;
        errorBgRt.offsetMin = Vector2.zero;
        errorBgRt.offsetMax = new Vector2(0f, -ErrorSplitTopGap);
        Image errorBg = errorBgRt.gameObject.AddComponent<Image>();
        errorBg.color = consoleBgFullColor;
        errorBg.raycastTarget = false;
        consoleBg = errorBg; // 后续动态着色（分栏 / 整块）统一作用在真正的底板上

        // 错误文字可视区：RectMask2D 裁剪。文字内容按实测高度向下生长，超出部分靠滚轮翻页查看，
        // 绝不使用 verticalOverflow = Overflow —— 那样多行报错会直接溢出面板、掉到屏幕下沿之外，玩家根本看不到。
        RectTransform errMaskRt = CreateRect("ErrorViewport", bottomRt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        errMaskRt.anchorMin = Vector2.zero;
        errMaskRt.anchorMax = Vector2.one;
        errMaskRt.offsetMin = new Vector2(12f, 36f);    // 底部让开状态栏（30）与 6px 间隙
        errMaskRt.offsetMax = new Vector2(-16f, -ErrorTopInset);  // 顶部让开粗提示条 + 细分界条 + 6px 呼吸位；右侧让开滚动块
        errMaskRt.gameObject.AddComponent<RectMask2D>();
        errorMaskRt = errMaskRt;

        int errorStartFont = Mathf.Clamp(errorFontSize, ErrorFontMin, ErrorFontMax); // 沿用 Inspector 里的上报字号（夹在 11~15）
        errorListText = CreateText("ErrorList", errMaskRt, "", errorStartFont, errorColor, TextAnchor.UpperLeft);
        RectTransform errRt = errorListText.rectTransform;
        errRt.anchorMin = new Vector2(0f, 1f);
        errRt.anchorMax = new Vector2(1f, 1f);
        errRt.pivot = new Vector2(0.5f, 1f);
        errRt.anchoredPosition = Vector2.zero;
        errRt.sizeDelta = new Vector2(0f, 0f);      // 高度由 RefreshErrorLayout 按实测文字高度写入
        errorListText.supportRichText = false;
        errorListText.horizontalOverflow = HorizontalWrapMode.Wrap;
        errorListText.verticalOverflow = VerticalWrapMode.Overflow;

        // ---- 错误区（红）竖向滚动块：平时完全不可见（右侧不出现任何红条），
        //      只有报错真的超出可视高度时才冒出一小截红色滑块；轨道留作透明命中区，点一下可跳转 ----
        errorScrollTrackRt = CreateRect("ErrorScrollTrack", bottomRt, new Vector2(1f, 0f), Vector2.zero, new Vector2(6f, 0f), new Vector2(1f, 0.5f));
        errorScrollTrackRt.anchorMin = new Vector2(1f, 0f);
        errorScrollTrackRt.anchorMax = new Vector2(1f, 1f);
        errorScrollTrackRt.pivot = new Vector2(1f, 0.5f);
        errorScrollTrackRt.offsetMin = new Vector2(-18f, 36f);
        errorScrollTrackRt.offsetMax = new Vector2(-12f, -ErrorTopInset);
        Image errTrackImg = errorScrollTrackRt.gameObject.AddComponent<Image>();
        errTrackImg.color = UITheme.WithAlpha(errorColor, 0f); // 透明：不画轨道，只当点击跳转的命中区
        errTrackImg.raycastTarget = true;
        errorScrollTrackRt.gameObject.AddComponent<ScrollBarTrackJump>().Setup(errorScrollTrackRt, true, t => SetErrorScrollFromBar(t));

        errorScrollHandleRt = CreateRect("ErrorScrollHandle", errorScrollTrackRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(6f, 40f), new Vector2(0.5f, 1f));
        errorScrollHandleRt.anchorMin = new Vector2(0.5f, 1f);
        errorScrollHandleRt.anchorMax = new Vector2(0.5f, 1f);
        errorScrollHandleRt.pivot = new Vector2(0.5f, 1f);
        errorScrollHandleRt.anchoredPosition = Vector2.zero;
        errorScrollHandleRt.sizeDelta = new Vector2(6f, 40f);
        Image errHandleImg = errorScrollHandleRt.gameObject.AddComponent<Image>();
        errHandleImg.color = UITheme.WithAlpha(errorColor, 0.6f); // 红色滚动块，可上下拖动
        errHandleImg.raycastTarget = true;
        errorScrollHandleRt.gameObject.AddComponent<ScrollBarHandle>().Setup(errorScrollTrackRt, true, t => SetErrorScrollFromBar(t));
        errorScrollHandleRt.gameObject.SetActive(false); // 报错放得下时完全不出现，溢出才显示

        // ---- 错误区上缘红色分隔条：和教程卡片那条可拖蓝条同样的玩法 —— 按住它上下拖，
        //      「错误区（红）/ 代码区」的高度比例实时互换；拖动结果会被记住，之后不再被内容自适应冲掉 ----
        // 红线上方的 6px 归属区：与上方蓝色横滚动条之间的空白，现在算作红线的一部分，
        // 只承担「鼠标压上去整条高亮」与「拉高命中判定」这两件事，颜色保持一层很淡的红，不当实线用。
        errorSplitTopPadRt = CreateRect("ErrorSplitTopPad", bottomRt, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, ErrorSplitTopGap), new Vector2(0.5f, 1f));
        errorSplitTopPadRt.anchorMin = new Vector2(0f, 1f);
        errorSplitTopPadRt.anchorMax = new Vector2(1f, 1f);
        errorSplitTopPadRt.pivot = new Vector2(0.5f, 1f);
        errorSplitTopPadRt.anchoredPosition = Vector2.zero;
        errorSplitTopPadRt.sizeDelta = new Vector2(0f, ErrorSplitTopGap);
        errorSplitTopPadImg = errorSplitTopPadRt.gameObject.AddComponent<Image>();
        errorSplitTopPadImg.color = UITheme.WithAlpha(errorColor, ErrorSplitTopPadAlpha);
        errorSplitTopPadImg.raycastTarget = false; // 不抢指针：上方那条蓝色横滚动条要照常可拖，悬停判定走屏幕坐标

        errorSplitBarRt = CreateRect("ErrorSplitBar", bottomRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, ErrorSplitBarHeight), new Vector2(0.5f, 1f));
        errorSplitBarRt.anchorMin = new Vector2(0f, 1f);
        errorSplitBarRt.anchorMax = new Vector2(1f, 1f);
        errorSplitBarRt.pivot = new Vector2(0.5f, 1f);
        errorSplitBarRt.anchoredPosition = new Vector2(0f, -(ErrorSplitTopGap + ErrorSplitGripHeight)); // 让位给上方粗提示条：细条紧贴粗条下缘
        errorSplitBarRt.sizeDelta = new Vector2(0f, ErrorSplitBarHeight);
        errorSplitBarImg = errorSplitBarRt.gameObject.AddComponent<Image>();
        errorSplitBarImg.color = UITheme.WithAlpha(errorColor, 0.95f); // 鲜红旗标：一眼看出这里能上下拖
        errorSplitBarImg.raycastTarget = false; // 3px 细条只负责「显示」，点击/拖动全部交给下面那层更高的透明命中区

        // 红色细条「正上方」的提示条：厚度收到 6px（比底部横向蓝条的 10px 更细），下缘贴住 3px 细条（向下对齐），
        // 整条都能按住上下拖，交互与蓝条完全相同 —— 细条继续画分界，这条负责看得见、抓得住。
        errorSplitGripRt = CreateRect("ErrorSplitGrip", bottomRt, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, ErrorSplitGripHeight), new Vector2(0.5f, 1f));
        errorSplitGripRt.anchorMin = new Vector2(0f, 1f);
        errorSplitGripRt.anchorMax = new Vector2(1f, 1f);
        errorSplitGripRt.pivot = new Vector2(0.5f, 1f);
        errorSplitGripRt.anchoredPosition = new Vector2(0f, -ErrorSplitTopGap); // 顶格：粗提示条上缘贴住错误区顶边，细条被压在它下面
        errorSplitGripRt.sizeDelta = new Vector2(0f, ErrorSplitGripHeight);
        errorSplitGripImg = errorSplitGripRt.gameObject.AddComponent<Image>();
        errorSplitGripImg.color = UITheme.WithAlpha(errorColor, 0.85f); // 与细条同色系，但整条更粗、更像把手
        errorSplitGripImg.raycastTarget = true;
        errorSplitGripRt.gameObject.AddComponent<ErrorSplitBarDrag>().Setup(this);

        // 命中区比红条更高（9px），细条看着细、但依旧好按住 —— 细归细，手感不能丢
        errorSplitHitRt = CreateRect("ErrorSplitHit", bottomRt, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, ErrorSplitHitHeight), new Vector2(0.5f, 1f));
        errorSplitHitRt.anchorMin = new Vector2(0f, 1f);
        errorSplitHitRt.anchorMax = new Vector2(1f, 1f);
        errorSplitHitRt.pivot = new Vector2(0.5f, 1f);
        // 命中带从「红条上沿再往上 4px」一直铺到红条下方 —— 按住红条本体、或按到它上下边缘几像素都不会落空。
        errorSplitHitRt.anchoredPosition = new Vector2(0f, -(ErrorSplitTopGap - ErrorSplitInputSlack));
        errorSplitHitRt.sizeDelta = new Vector2(0f, ErrorSplitHitHeight);
        errorSplitHitRt.SetAsLastSibling(); // 压在最上层：红条、提示条、归属区都不许抢指针
        Image errSplitHitImg = errorSplitHitRt.gameObject.AddComponent<Image>();
        errSplitHitImg.color = UITheme.WithAlpha(errorColor, 0f);
        errSplitHitImg.raycastTarget = true;
        errorSplitHitRt.gameObject.AddComponent<ErrorSplitBarDrag>().Setup(this);

        // ---- 紫色运行时错误（RE）窗口：叠在红色编译错误窗口之上，结构与红色窗口同构 ----
        reZoneRt = CreateRect("RuntimeErrorZone", editorRoot.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 0f), new Vector2(0.5f, 0f));
        reZoneRt.anchorMin = new Vector2(0f, 0f);
        reZoneRt.anchorMax = new Vector2(1f, 0f);
        reZoneRt.pivot = new Vector2(0.5f, 0f);
        reZoneRt.anchoredPosition = Vector2.zero;
        reZoneRt.sizeDelta = new Vector2(0f, 0f);
        reZoneRt.gameObject.AddComponent<RectMask2D>();
        // 背景不再铺满整块 rect：顶部 ReSplitTopGap(6px) 是给上方蓝色横滚动条让出的缝隙，
        // 这段以前也被紫色背景染上，看起来就是「紫线上方还有一截紫色残留」。背景从紫线下沿开始画，顶边全透明。
        RectTransform reBgRt = CreateRect("RuntimeErrorBg", reZoneRt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        reBgRt.anchorMin = Vector2.zero;
        reBgRt.anchorMax = Vector2.one;
        reBgRt.offsetMin = Vector2.zero;
        reBgRt.offsetMax = new Vector2(0f, -(ReSplitTopGap + ReSplitLineHeight));
        Image reBg = reBgRt.gameObject.AddComponent<Image>();
        reBg.color = Color.Lerp(UITheme.CodeBg, UITheme.Purple, 0.16f);
        reBg.raycastTarget = true;
        reBgRt.SetAsFirstSibling(); // 压在文字与紫色滚动条之下

        reMaskRt = CreateRect("RuntimeErrorViewport", reZoneRt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        reMaskRt.anchorMin = Vector2.zero;
        reMaskRt.anchorMax = Vector2.one;
        reMaskRt.offsetMin = new Vector2(12f, ReBottomInset);                               // 底部留白 = 呼吸位 6 + 半根紫线 2，与线下方 6px 视觉居中
        reMaskRt.offsetMax = new Vector2(-16f, -ReTopInset); // 顶部让开 6px 缝 + 紫线 4px + 6px 呼吸位
        reMaskRt.gameObject.AddComponent<RectMask2D>();

        reListText = CreateText("RuntimeErrorList", reMaskRt, string.Empty, reFontSize, UITheme.Purple, TextAnchor.UpperLeft);
        RectTransform reTextRt = reListText.rectTransform;
        reTextRt.anchorMin = new Vector2(0f, 1f);
        reTextRt.anchorMax = new Vector2(1f, 1f);
        reTextRt.pivot = new Vector2(0.5f, 1f);
        reTextRt.anchoredPosition = Vector2.zero;
        reTextRt.sizeDelta = new Vector2(0f, 0f);
        reListText.supportRichText = false;
        reListText.horizontalOverflow = HorizontalWrapMode.Wrap;
        reListText.verticalOverflow = VerticalWrapMode.Overflow;

        reScrollTrackRt = CreateRect("RuntimeErrorScrollTrack", reZoneRt, new Vector2(1f, 0f), Vector2.zero, new Vector2(6f, 0f), new Vector2(1f, 0.5f));
        reScrollTrackRt.anchorMin = new Vector2(1f, 0f);
        reScrollTrackRt.anchorMax = new Vector2(1f, 1f);
        reScrollTrackRt.pivot = new Vector2(1f, 0.5f);
        reScrollTrackRt.offsetMin = new Vector2(-18f, ReBottomInset); // 滚动条下端与文字视口同步，吃同一套上下留白
        reScrollTrackRt.offsetMax = new Vector2(-12f, -ReTopInset);
        Image reTrackImg = reScrollTrackRt.gameObject.AddComponent<Image>();
        reTrackImg.color = UITheme.WithAlpha(UITheme.Purple, 0f);
        reTrackImg.raycastTarget = true;
        ScrollBarTrackJump reTrackJump = reScrollTrackRt.gameObject.AddComponent<ScrollBarTrackJump>();
        reTrackJump.Setup(reScrollTrackRt, true, SetReScrollFromBar);

        reScrollHandleRt = CreateRect("RuntimeErrorScrollHandle", reScrollTrackRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(6f, 40f), new Vector2(0.5f, 1f));
        reScrollHandleRt.anchorMin = new Vector2(0.5f, 1f);
        reScrollHandleRt.anchorMax = new Vector2(0.5f, 1f);
        reScrollHandleRt.pivot = new Vector2(0.5f, 1f);
        reScrollHandleRt.anchoredPosition = Vector2.zero;
        reScrollHandleRt.sizeDelta = new Vector2(6f, 40f);
        Image reHandleImg = reScrollHandleRt.gameObject.AddComponent<Image>();
        reHandleImg.color = UITheme.WithAlpha(UITheme.Purple, 0.6f);
        reHandleImg.raycastTarget = true;
        ScrollBarHandle reHandleDrag = reScrollHandleRt.gameObject.AddComponent<ScrollBarHandle>();
        reHandleDrag.Setup(reScrollTrackRt, true, SetReScrollFromBar);

        // 紫线上方的 6px 归属区：与上方蓝色横滚动条之间的空白，现在算作紫线的一部分，
        // 只承担「鼠标压上去整条高亮」与「拉高判定」这两件事，颜色保持一层很淡的紫，不当实线用。
        reSplitTopPadRt = CreateRect("RuntimeErrorSplitTopPad", reZoneRt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0.5f, 1f));
        reSplitTopPadRt.anchorMin = new Vector2(0f, 1f);
        reSplitTopPadRt.anchorMax = new Vector2(1f, 1f);
        reSplitTopPadRt.pivot = new Vector2(0.5f, 1f);
        reSplitTopPadRt.anchoredPosition = Vector2.zero;
        reSplitTopPadRt.sizeDelta = new Vector2(0f, ReSplitTopGap);
        reSplitTopPadImg = reSplitTopPadRt.gameObject.AddComponent<Image>();
        reSplitTopPadImg.color = UITheme.WithAlpha(UITheme.Purple, ReSplitTopPadAlpha);
        reSplitTopPadImg.raycastTarget = true; // 归属区自己接指针：悬停高亮与拉高判定都落在这块上
        ReSplitClickClose rePadClick = reSplitTopPadRt.gameObject.AddComponent<ReSplitClickClose>();
        rePadClick.Setup(this); // 点一下紫线（含这 6px 归属区）= 按一次 F10，紫窗直接收起

        // 紫色分隔条：细条（视觉 2px）+ 提示条（2px，整条可拖）+ 透明命中区（12px，好按住）
        reSplitBarRt = CreateRect("RuntimeErrorSplitBar", reZoneRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, ReSplitBarHeight), new Vector2(0.5f, 1f));
        reSplitBarRt.anchorMin = new Vector2(0f, 1f);
        reSplitBarRt.anchorMax = new Vector2(1f, 1f);
        reSplitBarRt.pivot = new Vector2(0.5f, 1f);
        reSplitBarRt.anchoredPosition = new Vector2(0f, -(ReSplitTopGap + ReSplitGripHeight));
        reSplitBarRt.sizeDelta = new Vector2(0f, ReSplitBarHeight);
        reSplitBarImg = reSplitBarRt.gameObject.AddComponent<Image>();
        reSplitBarImg.color = UITheme.WithAlpha(UITheme.Purple, 0.95f);
        reSplitBarImg.raycastTarget = true; // 紫条本体可点：点一下收起紫窗（等同按一次 F10）
        ReSplitClickClose reBarClick = reSplitBarRt.gameObject.AddComponent<ReSplitClickClose>();
        reBarClick.Setup(this);

        reSplitGripRt = CreateRect("RuntimeErrorSplitGrip", reZoneRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, ReSplitGripHeight), new Vector2(0.5f, 1f));
        reSplitGripRt.anchorMin = new Vector2(0f, 1f);
        reSplitGripRt.anchorMax = new Vector2(1f, 1f);
        reSplitGripRt.pivot = new Vector2(0.5f, 1f);
        reSplitGripRt.anchoredPosition = new Vector2(0f, -ReSplitTopGap);
        reSplitGripRt.sizeDelta = new Vector2(0f, ReSplitGripHeight);
        reSplitGripImg = reSplitGripRt.gameObject.AddComponent<Image>();
        reSplitGripImg.color = UITheme.WithAlpha(UITheme.Purple, 0.85f);
        reSplitGripImg.raycastTarget = true; // 紫条本体可点：点一下收起紫窗（等同按一次 F10）
        ReSplitClickClose reGripClick = reSplitGripRt.gameObject.AddComponent<ReSplitClickClose>();
        reGripClick.Setup(this);

        reSplitHitRt = CreateRect("RuntimeErrorSplitHit", reZoneRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, ReSplitHitHeight), new Vector2(0.5f, 1f));
        // 关键修正：命中区必须贴住窗口顶边。此前锚点误设为 (0,0.5) + pivot 0.5，
        // 实际落点跑到了紫窗垂直中线附近，视觉上的紫条按下去常常没反应（表现为「有概率拖不动」）。
        reSplitHitRt.anchorMin = new Vector2(0f, 1f);
        reSplitHitRt.anchorMax = new Vector2(1f, 1f);
        reSplitHitRt.pivot = new Vector2(0.5f, 1f);
        reSplitHitRt.anchoredPosition = new Vector2(0f, -ReSplitTopGap); // 顶边下移 6px，与上方蓝条留缝，谁也不抢谁的指针
        reSplitHitRt.sizeDelta = new Vector2(0f, ReSplitHitHeight);
        Image reHitImg = reSplitHitRt.gameObject.AddComponent<Image>();
        reHitImg.color = UITheme.WithAlpha(UITheme.Purple, 0f);
        reHitImg.raycastTarget = false; // 命中区一并停用：紫条不可拖，指针直接穿给下方内容

        SetActiveIfChanged(reSplitTopPadRt, false);
        SetActiveIfChanged(reSplitBarRt, false);
        SetActiveIfChanged(reSplitGripRt, false);
        SetActiveIfChanged(reSplitHitRt, false);
        SetActiveIfChanged(reMaskRt, false);
        SetActiveIfChanged(reScrollTrackRt, false);

        RectTransform statusRt = CreateRect("StatusBar", bottomRt.transform, new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 30f), new Vector2(0.5f, 0f));
        statusRt.anchorMin = new Vector2(0f, 0f);
        statusRt.anchorMax = new Vector2(1f, 0f);
        statusRt.pivot = new Vector2(0.5f, 0f);
        statusRt.anchoredPosition = Vector2.zero;
        statusRt.sizeDelta = new Vector2(0f, 30f);
        Image statusBg = statusRt.gameObject.AddComponent<Image>();
        statusBg.color = UITheme.CodeStatusBg;

        statusLineText = CreateText("StatusText", statusRt, "", statusFontSize, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform statusTextRt = statusLineText.rectTransform;
        statusTextRt.anchorMin = new Vector2(0f, 0f);
        statusTextRt.anchorMax = new Vector2(1f, 1f);
        statusTextRt.offsetMin = new Vector2(16f, 0f);
        statusTextRt.offsetMax = new Vector2(-16f, 0f);
        statusLineText.supportRichText = false;

        // ---- Level 3 进场提示：时间已冻结，按 Enter 输入代码（默认隐藏；挂 Canvas 根，不随编辑器开关） ----
        RectTransform promptRt = CreateRect("EnterPrompt", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 96f), new Vector2(0.5f, 0.5f));
        Image promptBg = promptRt.gameObject.AddComponent<Image>();
        promptBg.color = UITheme.ScrimSoft;
        Text promptText = CreateText("PromptText", promptRt,
            Localization.Pick("时间已冻结 —— Enter / Alt+O 编写代码", "Time frozen — Press Enter or Alt+O to write code"),
            26, UITheme.TextPrimary, TextAnchor.MiddleCenter);
        Stretch(promptText.rectTransform);
        enterPromptRt = promptRt;
        enterPromptRt.gameObject.SetActive(false);

        // ---- 顶部区域层级置顶：标题栏（含标题文字与 ✕ / 停止 / 编译运行 按钮）置于兄弟节点最后，
        //      渲染顺序压在行号列 / 代码视口 / 滚动条 / 底部错误区之上，避免顶部被下层元素覆盖 ----
        if (titleBarRt != null)
            titleBarRt.SetAsLastSibling();

        // 把已建好的单块 UI 登记为 segments[0]（阈值以下始终只有这一块，行为与原版一致）
        InitSegments();

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

        Text txt = CreateText("Text", rt, label, 15, UITheme.TextPrimary, TextAnchor.MiddleCenter);
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
        private bool dragging;
        private float startT;          // 按下瞬间滑块代表的滚动进度
        private float usable = 1f;     // 滑块中心可移动距离（轨道长 - 滑块长）
        private Vector2 startPointer;  // 按下瞬间的屏幕坐标

        public void Setup(RectTransform track, bool isVertical, System.Action<float> callback)
        {
            trackRt = track;
            vertical = isVertical;
            onSet = callback;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            dragging = false;
            if (trackRt == null)
                return;
            RectTransform self = transform as RectTransform;
            if (self == null)
                return;
            float trackSize = vertical ? trackRt.rect.height : trackRt.rect.width;
            float handleSize = vertical ? self.rect.height : self.rect.width;
            usable = Mathf.Max(1f, trackSize - handleSize);
            // 用滑块当下的 anchoredPosition 反算进度：竖向 t=0 在轨道最上，t=1 在最下
            startT = vertical
                ? Mathf.Clamp01(-self.anchoredPosition.y / usable)
                : Mathf.Clamp01(self.anchoredPosition.x / usable);
            startPointer = e.position;
            dragging = true;
        }

        /// <summary>
        /// 按「相对按下点的位移」改进度，而不是把滑块中心对齐到指针：
        /// 抓在滑块哪个位置就一直在那个位置，滑块不会每次都被拽到指针正中。
        /// </summary>
        public void OnDrag(PointerEventData e)
        {
            if (!dragging || onSet == null || trackRt == null)
                return;
            Vector2 lpNow, lpStart;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, e.position, null, out lpNow) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, startPointer, null, out lpStart))
                return;
            float delta = vertical ? (lpNow.y - lpStart.y) : (lpNow.x - lpStart.x);
            // 竖向：往上拖（delta>0）进度变小；横向：往右拖进度变大。
            float t = vertical ? (startT - delta / usable) : (startT + delta / usable);
            onSet(Mathf.Clamp01(t));
        }
    }

    /// <summary>滚动条轨道点击跳转：点击轨道任意位置即把滑块中心移到该处（拖动滑块时事件冒泡到轨道，位置与当前滑块一致，无跳变）。</summary>
    private sealed class ScrollBarTrackJump : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private RectTransform trackRt;
        private bool vertical;
        private System.Action<float> onSet;
        private bool pressed;
        private Vector2 downPointer;

        public void Setup(RectTransform track, bool isVertical, System.Action<float> callback)
        {
            trackRt = track;
            vertical = isVertical;
            onSet = callback;
        }

        /// <summary>按下只记坐标：抬手时才判断「是点击还是拖动」，让轨道同时能被用作分界拖拽不误跳。</summary>
        public void OnPointerDown(PointerEventData e)
        {
            pressed = true;
            downPointer = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!pressed)
                return;
            pressed = false;
            if ((e.position - downPointer).sqrMagnitude > 36f) // 移动超过 6px 视为拖动，不跳转
                return;
            // 按在滑块上（指针拖拽目标是滑块本身）不算点轨道：滑块只按位移走，中心绝不跳到指针处
            if (e.pointerDrag != null && e.pointerDrag != gameObject)
                return;
            Vector2 lp;
            if (trackRt == null || onSet == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, e.position, null, out lp))
                return;
            float trackSize = vertical ? trackRt.rect.height : trackRt.rect.width;
            float v = vertical ? lp.y : lp.x;
            // 竖轨道 pivot.y=0.5：局部 y 向上为正，点上方应是「内容往回翻到顶部」= 进度 0，所以要取反（0.5 - v/h）。
            // 横轨道 pivot.x=0：局部 x 向右为正，点右边 = 进度 1，方向本就是对的。
            float t = vertical ? (0.5f - v / Mathf.Max(1f, trackSize)) : (v / Mathf.Max(1f, trackSize));
            onSet(Mathf.Clamp01(t));
        }
    }

    /// <summary>错误区上缘红色分隔条的拖动手柄：按住上下拖，实时改变「错误区（红）/ 代码区」的高度比例。
    /// 紫色 RE 窗口已改为高度自适应字号，不再挂本组件。</summary>
    private sealed class ErrorSplitBarDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private CodeEditorController host;

        public void Setup(CodeEditorController owner) { host = owner; }

        public void OnBeginDrag(PointerEventData e) { if (host == null) return; host.BeginErrorSplitDrag(e); }
        public void OnDrag(PointerEventData e) { if (host == null) return; host.DragErrorSplit(e); }
    }

    /// <summary>紫线点一下收起：在紫色分隔条（6px 归属区 / 2px 细条 / 2px 提示条）上点一下 = 按一次 F10。
    /// 紫窗可见时只收紫窗，红窗的高度与屏幕位置一律不动，与 F10 的分支口径完全一致。</summary>
    private sealed class ReSplitClickClose : MonoBehaviour, IPointerClickHandler
    {
        private CodeEditorController host;

        public void Setup(CodeEditorController owner) { host = owner; }

        public void OnPointerClick(PointerEventData e)
        {
            if (host == null) return;
            if (e.button != PointerEventData.InputButton.Left) return; // 只认左键，右键留给后续菜单
            if (host.IsReZoneVisible()) host.CloseReZonePanel();
            else host.ToggleReZonePanel();
        }
    }

}


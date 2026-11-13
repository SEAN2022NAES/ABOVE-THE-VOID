using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Level 1 - Part 2「编程序章」：夹在「全程用手点方块」与「C# 语法课」之间的过渡讲解关。
///   · 面向完全不懂代码的玩家：先讲「为什么不要再用手一个一个点」，再讲「把规则交给电脑」，
///     最后看一眼真实代码长什么样、接下来两门课怎么学；
///   · 多页阅读（4 页），底部 ‹ 上一页 / 下一页 › 可来回翻，最后一页「开始语法课」直接进 Level 1 - Part 3；
///   · 键盘 ← → 也能翻页；「跳过序章 (S)」按钮 / S 键直接进语法课；
///   · 读字期间释放鼠标并冻结视角（鼠标移动不会转动背后场景的朝向），ESC 暂停仍然可用；
///   · 本关不打开代码编辑器、不出方块，纯粹读字。
/// 章节跳转：/level 1 2 n 可直接翻到第 n 页（见 LevelDirector）。
/// </summary>
public class CodeIntroTutorialController : MonoBehaviour
{
    /// <summary>讲解页数（供 /level 1 2 n 的章节校验与页码显示）。</summary>
    public const int PageCount = 4;

    public static CodeIntroTutorialController Instance { get; private set; }

    /// <summary>讲解关是否正在显示。</summary>
    public bool Active { get; private set; }

    /// <summary>是否正在显示讲解页（供 CodeEditorController 拦截 Enter / Ctrl+O，避免读字时误弹编辑器）。</summary>
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

    // 进入 Level 1 - Part 3 的黑幕过渡时长：与 Level 1 - Part 1 → Part 2（TutorialController）保持一致
    private const float FadeOutSeconds = 0.35f;
    private const float FadeInSeconds = 0.5f;
    private Button prevBtn;
    private Button nextBtn;
    private Text prevLabel;
    private Text nextLabel;
    private Text skipLabel;
    private PlayerCameraController playerController; // 读字期间接管视角：释放鼠标、禁止鼠标转朝向
    private const KeyCode SkipKey = KeyCode.S;       // 跳过序章的快捷键（与按钮同效）

    // ---- 正文字号自适应（修复换字体后的文字溢出）----
    // 正文框是「固定高度 + verticalOverflow = Overflow」，字号原先写死 26。
    // 字体从 msyh / Consolas 换成 Noto Sans CJK SC / JetBrains Mono 后：
    //   · 同一字号的字形宽度增约 9%（CJK 全角宽度不变，但西文 / 数字变宽，混排断行位置随之变化）；
    //   · 行高从雅黑的 1.32em 涨到 Noto 的 1.45em（+9.6%，1.35f 行距下 26 号每行从 46.4px 变 50.8px）。
    // 两者叠加使 4 页正文里有 3 页超出正文框（第 2/3 页 +222px、第 4 页 +171px），文字直接压到页脚提示、
    // 按钮上并越过面板下边界。这里改为按正文框真实尺寸挑字号：在 [BodyFontMin, BodyFontMax] 内取
    // 「4 页中文 + 4 页英文都放得下」的最大字号，翻页时字号统一、不跳变；不改文案、不改流程。
    private const int BodyFontMax = 26;
    private const int BodyFontMin = 17;
    private const float BodyFitPadding = 6f; // 留余量：避免字形取整 / 行高取整后正好压线
    private int bodyFontSize = BodyFontMax;

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
            if (!PageFits(BodiesZh[i], fontSize, limit) || !PageFits(BodiesEn[i], fontSize, limit))
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

    private static readonly string[] TitlesZh =
    {
        "序章 1/4 · 刚才那几关，你一直在用手点",
        "序章 2/4 · 接下来，让电脑替你去点",
        "序章 3/4 · 这段规则，写成代码就是这个样子",
        "序章 4/4 · 接下来两步，你就能自己写出它"
    };

    private static readonly string[] TitlesEn =
    {
        "Prologue 1/4 - So far you did everything by hand",
        "Prologue 2/4 - Let the computer do the clicking",
        "Prologue 3/4 - The same rule, written as code",
        "Prologue 4/4 - Two steps and you can write it yourself"
    };

    // 正文统一控制在此行数内（约 14 行），保证一屏放得下、不需要缩到看不清
    private static readonly string[] BodiesZh =
    {
        "回忆一下刚才那一关：\n"
        + "方块从天上掉下来，你把准星对准它，按下鼠标左键，打掉一个。\n\n"
        + "可你也看到了 —— 方块越多、来得越快，你就越忙不过来。\n"
        + "手速是有上限的：一秒钟冒出几十个方块时，没有人能一个一个点掉。\n\n"
        + "这不是你手慢，是所有「手动操作」的极限。\n"
        + "所以在真正的关卡里，我们不再用手一个一个点 —— 这件事交给电脑自动去做。",

        "所以接下来要做的事只有一件：自动化。\n"
        + "把「怎么打」写成一段规则，交给电脑反复执行，而不是一直用鼠标一个一个点。\n\n"
        + "这段规则就叫「程序」，写规则的过程就叫「写代码」。\n"
        + "写代码不是背咒语，它只是把你的想法按电脑认得的格式摆出来：\n\n"
        + "    第一步：每一帧先看看场上都有哪些方块\n"
        + "    第二步：从中挑出你先要打的那一个（比如最靠近你的）\n"
        + "    第三步：朝它开一炮\n"
        + "    第四步：下一帧，回到第一步\n\n"
        + "你只写一次，电脑每帧执行一次 —— 一秒钟几十次，它也不会累。",

        "（下面这几行，你现在不必看懂每个符号，只看个大概样子）\n\n"
        + "    var blocks = tower.GetAllObjects();\n"
        + "    for (int i = 0; i < blocks.Count; i++)\n"
        + "    {\n"
        + "        var b = blocks[i];\n"
        + "        if (b.Position.z < 0f) { tower.FireAtPlane(0f, 1f, 0f); break; }\n"
        + "    }\n\n"
        + "一行一行读，它就是你刚才「用手做的那件事」：\n"
        + "    GetAllObjects()      把场上所有方块列成一张清单\n"
        + "    for (...; i++)       挨个检查清单里的每一个\n"
        + "    if (b.Position.z<0)  判断它是不是已经冲到你面前（z 是远近坐标）\n"
        + "    FireAtPlane(...)     就是你刚才按下的那一下左键\n"
        + "    break                打完一个先收手，下一帧再看",

        "第一步（马上开始）：C# 基础语法课，18 个小任务。\n"
        + "    只教「砌墙的砖」：变量、运算、if、循环、数组、List、方法、字符串。\n"
        + "    每个任务都有示例代码，照着抄、照着改，再点【编译运行 (F6)】验证。\n"
        + "    任务卡右上角有 ‹ 细节 ›：看不懂就翻到第 2 页，那里写着第几步按哪里。\n\n"
        + "第二步：炮台 API 课。\n"
        + "    教你把炮台会做的事（列出方块、看血量、开炮）接进自己的代码里。\n\n"
        + "两门课做完，上面那段程序就是你亲手写的：\n"
        + "手不用动，炮台自己打。"
    };

    private static readonly string[] BodiesEn =
    {
        "Think back to the last level:\n"
        + "blocks fell from the sky, you aimed at one and pressed the left mouse button.\n\n"
        + "You also saw the problem - the faster they come, the less you can keep up.\n"
        + "There is a human limit: nobody can click dozens of blocks per second.\n\n"
        + "That is not slow hands, that is the limit of doing everything manually.\n"
        + "So real levels stop clicking one by one and let the computer do it automatically.",

        "So the next step has exactly one name: automation.\n"
        + "Write HOW to shoot down as a rule and let the computer repeat it, instead of clicking every block by hand.\n\n"
        + "That rule is a program; writing it is coding.\n"
        + "It is not magic spelling, it is your idea in a shape a computer understands:\n\n"
        + "    step 1: every frame, look at which blocks exist\n"
        + "    step 2: pick the one you want to hit first (say the nearest)\n"
        + "    step 3: fire one shot at it\n"
        + "    step 4: next frame, back to step 1\n\n"
        + "You write it once, the computer runs it every frame - dozens of times a second, never tired.",

        "(do not worry about every symbol below, just look at the shape)\n\n"
        + "    var blocks = tower.GetAllObjects();\n"
        + "    for (int i = 0; i < blocks.Count; i++)\n"
        + "    {\n"
        + "        var b = blocks[i];\n"
        + "        if (b.Position.z < 0f) { tower.FireAtPlane(0f, 1f, 0f); break; }\n"
        + "    }\n\n"
        + "Read it line by line - it is exactly what your hand did:\n"
        + "    GetAllObjects()      list every block on the field\n"
        + "    for (...; i++)       walk through that list one by one\n"
        + "    if (b.Position.z<0)  is it already in front of you (z is distance)\n"
        + "    FireAtPlane(...)     the left click you used to press\n"
        + "    break                stop after one shot, look again next frame",

        "Step one (starts right now): the C# syntax course, 18 small tasks.\n"
        + "    Only the bricks: variables, operators, if, loops, arrays, List, methods, strings.\n"
        + "    Every task has sample code: copy it, change it, press [Compile & Run (F6)].\n"
        + "    The card has a detail page: press the arrows at the top right for click-by-click steps.\n\n"
        + "Step two: the tower API course.\n"
        + "    Connect what the tower can do (list blocks, read hp, fire) to your own code.\n\n"
        + "When both are done, the program above is yours: hands off, the tower shoots by itself."
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

        // 键盘也能来回翻页（← 上一页 / → 下一页），不用鼠标点
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
            PrevPage();
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown)
                 || Input.GetKeyDown(KeyCode.Space))
            NextPage();
        else if (Input.GetKeyDown(SkipKey))
            SkipPrologue(); // S：直接跳过序章进语法课（与「跳过序章」按钮同效）
    }

    // ================== 对外接口 ==================

    /// <summary>进入讲解关并从第 startIndex 页（0 基）开始；重复进入即重置阅读进度。</summary>
    public static void Begin(int startIndex)
    {
        CodeIntroTutorialController inst = EnsureInstance();
        if (inst == null)
            return;

        inst.BuildUI();
        inst.Active = true;
        inst.transitioning = false;
        inst.CurrentPage = Mathf.Clamp(startIndex, 0, PageCount - 1);
        if (inst.root != null && !inst.root.activeSelf)
            inst.root.SetActive(true);
        inst.Refresh();
        inst.EnterReadingMode(); // 读字期间释放鼠标，且鼠标不再转动背后场景的朝向
        Debug.Log("[CodeIntroTutorial] 编程序章开始：第 " + (inst.CurrentPage + 1) + "/" + PageCount + " 页。");
    }

    /// <summary>结束讲解（离开本关 / 跳去语法课时调用，可重复调用）。</summary>
    public static void End()
    {
        if (Instance == null)
            return;
        Instance.Active = false;
        Instance.ExitReadingMode(); // 交还鼠标、解除视角封锁（跳关 / 回主页的调用方随后还会再锁一次鼠标）
        if (Instance.root != null && Instance.root.activeSelf)
            Instance.root.SetActive(false);
    }

    private static CodeIntroTutorialController EnsureInstance()
    {
        if (Instance != null)
            return Instance;
        GameObject go = new GameObject("CodeIntroTutorial");
        return go.AddComponent<CodeIntroTutorialController>();
    }

    // ================== 翻页 ==================

    private void PrevPage()
    {
        if (!Active || transitioning || CurrentPage <= 0)
            return;
        CurrentPage--;
        Refresh();
    }

    private void NextPage()
    {
        if (!Active || transitioning)
            return;
        if (CurrentPage >= PageCount - 1)
        {
            GoGrammarCourse(); // 已经是最后一页：再按一次就进语法课
            return;
        }
        CurrentPage++;
        Refresh();
    }

    /// <summary>跳过序章（S 键或「跳过序章」按钮）：不看讲解、直接进 Level 1 - Part 3 语法课。</summary>
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
    /// 进入 Level 1 - Part 3 语法课。过渡方式与 Level 1 - Part 1 → Part 2 完全一致：
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
            LevelDirector.JumpTo(1, 3, 1); // Level 1 - Part 3 语法课，从第 1 个任务开始
        });
    }

    // ================== 文案刷新 ==================

    private void Refresh()
    {
        bool zh = Localization.IsChinese;
        int page = Mathf.Clamp(CurrentPage, 0, PageCount - 1);

        SetLabel(pageTagText, (zh ? "序章 " : "Prologue ") + (page + 1) + "/" + PageCount);
        SetLabel(titleText, zh ? TitlesZh[page] : TitlesEn[page]);
        if (bodyText != null)
            bodyText.fontSize = bodyFontSize; // 先定字号，SetLabel 才会按该字号加载对应字形
        SetLabel(bodyText, zh ? BodiesZh[page] : BodiesEn[page]);
        SetLabel(footerText, zh
            ? "键盘 ← → 也能翻页　·　按 S 跳过序章　·　读完最后一页即可进入语法课"
            : "Keyboard arrows also page through · press S to skip the prologue · finish the last page to start the syntax course");

        SetLabel(prevLabel, zh ? "‹ 上一页" : "‹ Back");
        // 最后一页的「下一页」变成入口按钮，明确告诉玩家下一步去哪
        SetLabel(nextLabel, page >= PageCount - 1
            ? (zh ? "开始语法课 ›" : "Start the course ›")
            : (zh ? "下一页 ›" : "Next ›"));
        SetLabel(skipLabel, zh ? "跳过序章 (S)" : "Skip prologue (S)");

        if (prevBtn != null)
            prevBtn.interactable = page > 0;
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
        scrim.raycastTarget = true; // 吞掉点击，避免误触背后的界面

        // 阅读面板（高度由 820 → 900：换字体后正文行高 +9.6%，正文区从 540 撑到 620，
        // 配合正文自适应字号可让 4 页都完整放得下，同时仍在 1080 参考高度内留出上下各 90 的边距）
        RectTransform panelRt = CreateRect("IntroPanel", canvasGo.transform, new Vector2(0.5f, 0.5f),
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
        pageTagText = CreateText("PageTag", panelRt, "", 22, UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform tagRt = pageTagText.rectTransform;
        tagRt.anchorMin = new Vector2(0f, 1f);
        tagRt.anchorMax = new Vector2(1f, 1f);
        tagRt.pivot = new Vector2(0.5f, 1f);
        tagRt.anchoredPosition = new Vector2(0f, -36f);
        tagRt.offsetMin = new Vector2(56f, tagRt.offsetMin.y);
        tagRt.offsetMax = new Vector2(-56f, tagRt.offsetMax.y);
        tagRt.sizeDelta = new Vector2(-112f, 30f);

        // 页标题
        titleText = CreateText("Title", panelRt, "", 38, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        RectTransform titleRt = titleText.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -74f);
        titleRt.sizeDelta = new Vector2(-112f, 56f);

        // 正文
        bodyText = CreateText("Body", panelRt, "", 26, UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform bodyRt = bodyText.rectTransform;
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
        footerText = CreateText("Footer", panelRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform footerRt = footerText.rectTransform;
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0f, 0f);
        footerRt.anchoredPosition = new Vector2(56f, 88f);   // 按钮组顶边约 71，这里摆在 88 上方，两者不重叠
        footerRt.sizeDelta = new Vector2(-300f, 28f);

        // 右下按钮组：‹ 上一页 / 下一页 › / 跳过序章
        // 三个按钮的间距必须一致：之前是各自写死 x 坐标（-330 / -120 / 150），
        // 结果 上一页↔下一页只隔 15px、下一页↔跳过序章只隔 5px，三个挤成一坨，右边却空出一大截，观感很不协调。
        // 现在统一按 NavGap 等距排布，并让整组在面板内水平居中。
        const float NavGap = 28f;
        const float PrevW = 180f;
        const float NextW = 210f;
        const float SkipW = 320f;
        float groupW = PrevW + NextW + SkipW + NavGap * 2f;
        float cursor = -groupW * 0.5f;                       // 整组左边界（相对面板中心）

        prevBtn = CreateButton(panelRt, "PrevBtn", "‹ 上一页",
            new Vector2(0.5f, 0f), new Vector2(cursor + PrevW * 0.5f, 40f), new Vector2(PrevW, 62f));
        prevBtn.onClick.AddListener(PrevPage);
        prevLabel = prevBtn.GetComponentInChildren<Text>();
        cursor += PrevW + NavGap;

        nextBtn = CreateButton(panelRt, "NextBtn", "下一页 ›",
            new Vector2(0.5f, 0f), new Vector2(cursor + NextW * 0.5f, 40f), new Vector2(NextW, 62f));
        nextBtn.onClick.AddListener(NextPage);
        nextLabel = nextBtn.GetComponentInChildren<Text>();
        cursor += NextW + NavGap;

        // 横宽按中英文两套文案的最长者给足：「Skip prologue (S)」比「跳过序章 (S)」宽得多，
        // 原来的窄按钮会让英文顶出右边界，这里直接加宽到 320；
        // 同时给标签开自动缩字兜底（Wrap + Best Fit），再长的文案也只缩字、不越界。
        Button skipBtn = CreateButton(panelRt, "SkipBtn", "跳过序章 (S)",
            new Vector2(0.5f, 0f), new Vector2(cursor + SkipW * 0.5f, 40f), new Vector2(SkipW, 62f));
        skipBtn.onClick.AddListener(GoGrammarCourse);
        skipLabel = skipBtn.GetComponentInChildren<Text>();
        if (skipLabel != null)
        {
            // Unity 的 Best Fit 需要横向换行模式才会真正生效：文字先尝试换行，仍放不下再按 min→max 缩字号
            skipLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            skipLabel.verticalOverflow = VerticalWrapMode.Truncate;
            skipLabel.resizeTextForBestFit = true;
            skipLabel.resizeTextMinSize = 12;
            skipLabel.resizeTextMaxSize = 22;
        }

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

        Text t = CreateText("Label", rt, label, 22, UITheme.TextPrimary, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform);
        return btn;
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

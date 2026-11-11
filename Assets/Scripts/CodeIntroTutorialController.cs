using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Level 1 - Part 2「编程序章」：夹在「全程用手点方块」与「C# 语法课」之间的过渡讲解关。
///   · 面向完全不懂代码的玩家：先讲「为什么不要再用手一个一个点」，再讲「把规则交给电脑」，
///     最后看一眼真实代码长什么样、接下来两门课怎么学；
///   · 多页阅读（4 页），底部 ‹ 上一页 / 下一页 › 可来回翻，最后一页「开始语法课」直接进 Level 1 - Part 3；
///   · 键盘 ← → 也能翻页；「跳过序章」按钮直接进语法课；
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

        // 键盘也能来回翻页（← 上一页 / → 下一页），不用鼠标点
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
            PrevPage();
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown)
                 || Input.GetKeyDown(KeyCode.Space))
            NextPage();
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
        Debug.Log("[CodeIntroTutorial] 编程序章开始：第 " + (inst.CurrentPage + 1) + "/" + PageCount + " 页。");
    }

    /// <summary>结束讲解（离开本关 / 跳去语法课时调用，可重复调用）。</summary>
    public static void End()
    {
        if (Instance == null)
            return;
        Instance.Active = false;
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
        SetLabel(bodyText, zh ? BodiesZh[page] : BodiesEn[page]);
        SetLabel(footerText, zh
            ? "键盘 ← → 也能翻页　·　读完最后一页即可进入语法课"
            : "Keyboard arrows also page through · finish the last page to start the syntax course");

        SetLabel(prevLabel, zh ? "‹ 上一页" : "‹ Back");
        // 最后一页的「下一页」变成入口按钮，明确告诉玩家下一步去哪
        SetLabel(nextLabel, page >= PageCount - 1
            ? (zh ? "开始语法课 ›" : "Start the course ›")
            : (zh ? "下一页 ›" : "Next ›"));
        SetLabel(skipLabel, zh ? "跳过序章" : "Skip prologue");

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

        // 阅读面板
        RectTransform panelRt = CreateRect("IntroPanel", canvasGo.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1680f, 820f), new Vector2(0.5f, 0.5f));
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

        // 操作提示（键盘 ← → 也能翻页）：放在右下按钮组【上方】一行，避免与按钮横向重叠压住文字
        footerText = CreateText("Footer", panelRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform footerRt = footerText.rectTransform;
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0f, 0f);
        footerRt.anchoredPosition = new Vector2(56f, 88f);   // 按钮组顶边约 71，这里摆在 88 上方，两者不重叠
        footerRt.sizeDelta = new Vector2(-300f, 28f);

        // 右下按钮组：‹ 上一页 / 下一页 › / 跳过序章
        prevBtn = CreateButton(panelRt, "PrevBtn", "‹ 上一页",
            new Vector2(0.5f, 0f), new Vector2(-330f, 40f), new Vector2(180f, 62f));
        prevBtn.onClick.AddListener(PrevPage);
        prevLabel = prevBtn.GetComponentInChildren<Text>();

        nextBtn = CreateButton(panelRt, "NextBtn", "下一页 ›",
            new Vector2(0.5f, 0f), new Vector2(-120f, 40f), new Vector2(210f, 62f));
        nextBtn.onClick.AddListener(NextPage);
        nextLabel = nextBtn.GetComponentInChildren<Text>();

        Button skipBtn = CreateButton(panelRt, "SkipBtn", "跳过序章",
            new Vector2(0.5f, 0f), new Vector2(120f, 40f), new Vector2(170f, 62f));
        skipBtn.onClick.AddListener(GoGrammarCourse);
        skipLabel = skipBtn.GetComponentInChildren<Text>();

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

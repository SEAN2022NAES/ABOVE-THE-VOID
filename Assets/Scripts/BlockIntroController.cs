using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Level 2 - Part 1「方块图鉴」：在学习炮台 API 之前，先把「你到底在打什么」讲清楚。
///   · 左侧是一个真的在慢慢旋转的立方体（独立相机 + RenderTexture 渲染到面板上），六个面分别记着
///     行进面 / 终点 / 起始点 / 编号 / 名称 / 数据类型，转到哪个面，右侧就解释那个面；
///   · 右侧同时给出该页正文（双语），底部 ‹ 上一页 / 下一页 › 翻页，最后一页的下一页直接进
///     Level 2 - Part 2 炮台 API 课；
///   · 本关不打开代码编辑器、不出方块，纯粹看图 + 读字；
///   · 页码访问记录写盘（ABOVETHEVOID.Done.BlockIntro），与语法课 / API 课的任务完成记录同一套机制。
/// 章节跳转：/level 2 1 n 可直接翻到第 n 页（见 LevelDirector）。
/// </summary>
public class BlockIntroController : MonoBehaviour
{
    /// <summary>图鉴页数（供 /level 2 1 n 的章节校验与页码显示）。</summary>
    public const int PageCount = 5;

    public static BlockIntroController Instance { get; private set; }

    /// <summary>图鉴关是否正在显示。</summary>
    public bool Active { get; private set; }

    /// <summary>是否正在显示图鉴（供其它控制器拦截 Enter / Ctrl+O，避免看字时误弹编辑器）。</summary>
    public static bool Reading
    {
        get { return Instance != null && Instance.Active; }
    }

    /// <summary>当前页（0 基）。</summary>
    public int CurrentPage { get; private set; }

    private const float RefWidth = 1920f;
    private const float RefHeight = 1080f;
    private const float PanelWidth = 1680f;
    private const float PanelHeight = 940f;   // 1080 参考高度下上下各留 70，正文区多让出两行空间
    private const float ViewSize = 600f;      // 左侧 3D 视图边长

    // ---- 右侧文字栏：区域常量 + 字号自适应（文案不动，字号跟着每页内容走，任何语言都不压到图例 / 页脚）----
    private const float ColumnLeftInset = 676f;   // 文字栏距面板左边 = 56 + 视图 600 + 20 间距
    private const float ColumnRightInset = 52f;
    private const float ColumnBottomInset = 150f; // 文字栏底部让位给按钮组 / 页脚
    private const float ColumnTopInset = 132f;    // 文字栏顶部让位给页码 / 标题
    private const float BodyTopInset = 156f;      // 正文顶部让位给「正对你的面 / 值 / 说明」
    private const float BodyBottomInset = 16f;
    private const float FaceDescHeight = 74f;
    private const int BodyFontMax = 22;
    private const int BodyFontMin = 15;
    private const int DescFontMax = 20;
    private const int DescFontMin = 15;

    private static float BodyBoxWidth { get { return PanelWidth - ColumnLeftInset - ColumnRightInset; } }
    private static float BodyBoxHeight { get { return PanelHeight - ColumnTopInset - ColumnBottomInset - BodyTopInset - BodyBottomInset; } }
    private const float FaceOffset = 0.5f;    // 立方体半边长（单位立方体）
    private const int TextureSize = 768;      // RenderTexture 边长

    private const float FadeOutSeconds = 0.35f;
    private const float FadeInSeconds = 0.5f;
    private const KeyCode SkipKey = KeyCode.S;   // 跳过整段图鉴（与「跳过图鉴 (S)」按钮同一条链路）

    // ================== 面板 UI ==================
    private GameObject root;
    private GameObject panelRoot;
    private Text pageTagText;
    private Text titleText;
    private Text bodyText;
    private int lastBodyFontSize = -1;   // 上一次自适应得到的正文字号（仅用于日志，页面切换时打印一次）
    private Text faceNameText;
    private Text faceValueText;
    private Text faceDescText;
    private Text footerText;
    private RawImage viewImage;
    private Button prevBtn;
    private Button nextBtn;
    private Text prevLabel;
    private Text nextLabel;
    private Text skipLabel;
    private readonly Image[] legendBgs = new Image[6];
    private readonly Text[] legendTexts = new Text[6];
    private PlayerCameraController playerController; // 看页期间接管视角：释放鼠标、禁止鼠标转朝向

    // ================== 3D 视图 ==================
    private GameObject viewRoot;      // 独立 3D 舞台（放在远处，主相机看不到）
    private GameObject cubeRoot;      // 会旋转的立方体
    private Camera viewCamera;
    private RenderTexture viewTexture;
    private readonly TextMesh[] faceLabels = new TextMesh[6];      // 每个面只有一张朝外的文字（内壁不再放镜像字）
    private readonly Transform[] faceTransforms = new Transform[6];
    private int frontFace = -1;       // 正对镜头的面（-1 = 还没算过）

    private bool transitioning;
    private bool[] pageDone;

    // ================== 六个面的定义 ==================

    private sealed class FaceDef
    {
        public Vector3 normal;
        public string nameZh;
        public string nameEn;
        public string value;
        public string descZh;
        public string descEn;
        public Color faceColor;
        public Color textColor;
    }

    private static readonly Color DarkOnFace = new Color(0.05f, 0.08f, 0.11f, 1f);

    private static FaceDef[] BuildFaces()
    {
        FaceDef[] list = new FaceDef[6];

        // 行进面：+Z（白色，永远朝飞行方向）
        list[0] = new FaceDef
        {
            normal = Vector3.forward,
            nameZh = "行进面",
            nameEn = "Facing",
            value = "WHITE",
            descZh = "白色那一面，永远朝着飞行方向。相机从这一侧看过去时，方块中心会露出标记点是它。",
            descEn = "The white face always points where the block flies. Only this face is white on real blocks.",
            faceColor = new Color(0.93f, 0.94f, 0.95f, 1f),
            textColor = DarkOnFace
        };

        // 终点：-Z
        list[1] = new FaceDef
        {
            normal = Vector3.back,
            nameZh = "终点",
            nameEn = "Goal",
            value = "z = 0",
            descZh = "飞到 0 平面（基地）就算到终点：红块扣生命扣分，蓝块静默消失。判定看坐标，不看碰撞体。",
            descEn = "Reaching the zero plane ends the run: red blocks cost lives and score, blue blocks vanish silently.",
            faceColor = UITheme.Purple,
            textColor = DarkOnFace
        };

        // 起始点：+Y
        list[2] = new FaceDef
        {
            normal = Vector3.up,
            nameZh = "起始点",
            nameEn = "Spawn",
            value = "(0, 8, -2)",
            descZh = "出生坐标：在 X / Y / Z 里挑一条轴，取一个远处的点生成，之后笔直朝原点飞。",
            descEn = "Spawn coordinate: one axis is picked at random, a far point on that axis is used, then it flies straight at the origin.",
            faceColor = UITheme.Accent,
            textColor = DarkOnFace
        };

        // 编号：-Y
        list[3] = new FaceDef
        {
            normal = Vector3.down,
            nameZh = "编号",
            nameEn = "Index",
            value = "#0  int",
            descZh = "本局第几个出生（int，从 0 开始）。清单里一变，编号就跟着变，适合用来锁定同一个方块。",
            descEn = "Spawn order in this run (int, from 0). Stable enough to remember one specific block.",
            faceColor = new Color(0.85f, 0.65f, 0.24f, 1f),
            textColor = DarkOnFace
        };

        // 名称：+X
        list[4] = new FaceDef
        {
            normal = Vector3.right,
            nameZh = "名称",
            nameEn = "Name",
            value = "ATTACK",
            descZh = "ATTACK 红块（会扣分，血 1~6）/ BOMB 蓝块（血固定 1，击毁不计分）。string，只能比较文字。",
            descEn = "ATTACK red block (costs score, 1-6 hp) / BOMB blue block (1 hp, no score). A string, compare text only.",
            faceColor = UITheme.Alert,
            textColor = DarkOnFace
        };

        // 数据类型：-X
        list[5] = new FaceDef
        {
            normal = Vector3.left,
            nameZh = "数据类型",
            nameEn = "Types",
            value = "int float Vector3",
            descZh = "Name 是 string、HP / Index 是 int、Volume 是 float、Position / Velocity / Size 是 Vector3。",
            descEn = "Name is string, HP / Index are int, Volume is float, Position / Velocity / Size are Vector3.",
            faceColor = UITheme.Value,
            textColor = DarkOnFace
        };

        return list;
    }

    private static readonly FaceDef[] Faces = BuildFaces();

    // ================== 文案 ==================

    private static readonly string[] TitlesZh =
    {
        "方块图鉴 1/5 · 你一直在打的这个方块，到底是什么",
        "方块图鉴 2/5 · 六个面：把它翻过来看",
        "方块图鉴 3/5 · 起始点：它是从哪儿来的",
        "方块图鉴 4/5 · 终点：它要去哪儿，到了会怎样",
        "方块图鉴 5/5 · 名称与数据类型：为什么有的字段是 int，有的是 Vector3"
    };

    private static readonly string[] TitlesEn =
    {
        "Block guide 1/5 - What exactly are you shooting at",
        "Block guide 2/5 - Six faces, seen from every side",
        "Block guide 3/5 - Spawn: where it comes from",
        "Block guide 4/5 - Goal: where it goes and what happens",
        "Block guide 5/5 - Names and types: why int, float or Vector3"
    };

    private static readonly string[] BodiesZh =
    {
        "先把它看清楚：屏幕上那个「方块」不是贴图，也不是特效。\n"
        + "它 = 一个可见模型 + 一份数据。模型只负责让你看见，真正被瞄准、被打掉的是数据。\n\n"
        + "代码里能读到的字段就这几项：\n"
        + "    Name / Type   名字（ATTACK、BOMB）\n"
        + "    Index         编号（本局第几个出生）\n"
        + "    Position      现在的位置（Vector3）\n"
        + "    Velocity      速度方向（Vector3）\n"
        + "    HP            血量（int）      Volume 体积（float）\n\n"
        + "接下来四页，一边慢慢转这个立方体，一边把六个面记的东西讲清楚。",

        "这个立方体是一张图鉴卡：六个面各记一项 —— 行进面、终点、起始点、编号、名称、数据类型。\n"
        + "它一直慢慢转，正对镜头的那个面会被高亮，右边跟着换解释。\n\n"
        + "六面里唯一不能乱的是白色那一面：它永远朝飞行方向，叫行进面。\n"
        + "真实方块里只有行进面是白的，其余五面按名字上色（红块 / 蓝块）。\n\n"
        + "为什么要认面？因为 API 给你的 Position / Velocity 是世界坐标，\n"
        + "而你在屏幕上看到的是方块转到你面前的样子 —— 两边对得上，写代码才不会打错方向。",

        "每个方块出生时只有一个起点，写在生成器里：\n"
        + "    在 X / Y / Z 三条轴里挑一条 → 在该轴上取一个远处的坐标 → 出生。\n"
        + "    之后速度只朝原点 (0, 0, 0)，所以它要么沿 X、要么沿 Y、要么沿 Z 笔直飞过来。\n\n"
        + "代码里读到的起始点：\n"
        + "    Position   出生那一帧的坐标，例如 (0, 8.0, -2.0)\n"
        + "    Velocity   每秒位移，例如 (0, 0, 6.5)\n\n"
        + "为什么关心起始点？自动炮台必须趁它还在远处就算提前量；等飞到面前才开火，激光来不及。",

        "终点只有一个：0 平面 —— 你脚下基地所在的那个平面。\n"
        + "    ATTACK 红块摸到它 → 扣生命、扣分\n"
        + "    BOMB 蓝块摸到它   → 静默消失，不扣分（但它会挡在你和红块之间）\n\n"
        + "判定看的是坐标，不是碰撞体：\n"
        + "    if (b.Position.z < 0f)   // 已经越过基地平面\n"
        + "（沿 X 轴来的看 x，沿 Y 轴来的看 y，三轴同理）\n\n"
        + "所以 FireAtPlane(A, B, C) 的三个参数，就是「在这个平面上开炮」；\n"
        + "提前量算的就是「方块到达这个平面时，炮口该指向哪」。",

        "名称当前只有两种：\n"
        + "    ATTACK  红块，会扣分，血 1~6\n"
        + "    BOMB    蓝块，血固定 1，击毁不计分\n\n"
        + "数据类型决定你能拿它做什么：\n"
        + "    int       HP / Index → 只能比大小、加减\n"
        + "    float     Volume / 秒数 → 可以带小数\n"
        + "    Vector3   Position / Velocity / Size → x / y / z 三个 float\n"
        + "    string    Name / Type → 只能比较文字，不能算数\n\n"
        + "常见错：把 Position 当成一个数来比较（它有三个分量）；把 string 写成 == 0。\n"
        + "按右下「下一页」进入炮台 API 课，把这些字段真正用起来。"
    };

    private static readonly string[] BodiesEn =
    {
        "Look closely: the block on screen is not a texture and not a particle effect.\n"
        + "It is a visible mesh plus a data record. The mesh only makes it visible; the data is what gets targeted and destroyed.\n\n"
        + "This is all the data code can read:\n"
        + "    Name / Type   ATTACK, BOMB\n"
        + "    Index         spawn order in this run\n"
        + "    Position      current position (Vector3)\n"
        + "    Velocity      direction and speed (Vector3)\n"
        + "    HP            hit points (int)      Volume (float)\n\n"
        + "In the next four pages the cube keeps turning and each face explains one part of that data.",

        "This cube is a reference card: six faces, each remembering one thing - facing, goal, spawn, index, name, data types.\n"
        + "It keeps turning slowly; whichever face points at the camera is highlighted and explained on the right.\n\n"
        + "Only one rule is fixed: the white face always points where the block flies.\n"
        + "On real blocks only that face is white; the other five take the block colour (red / blue).\n\n"
        + "Why care about faces? Position and Velocity are world space coordinates,\n"
        + "while what you see is the cube turned toward you - matching both keeps your aiming code correct.",

        "Every block is born at one point, decided by the spawner:\n"
        + "    pick one axis among X / Y / Z -> take a far coordinate on it -> spawn.\n"
        + "    Velocity then points at the origin (0, 0, 0), so it flies straight along X, Y or Z.\n\n"
        + "What code reads:\n"
        + "    Position   coordinate at the spawn frame, e.g. (0, 8.0, -2.0)\n"
        + "    Velocity   units per second, e.g. (0, 0, 6.5)\n\n"
        + "Why it matters: an automatic turret must lead the target while it is still far away. Firing only when it is close is too late.",

        "There is exactly one goal: the zero plane - the plane of the base under your feet.\n"
        + "    ATTACK block reaching it -> lose a life, lose score\n"
        + "    BOMB block reaching it   -> disappears silently, no penalty (but it blocks your ray to red blocks)\n\n"
        + "The check uses coordinates, not colliders:\n"
        + "    if (b.Position.z < 0f)   // already past the base plane\n"
        + "(blocks coming along X check x, along Y check y - same idea on every axis)\n\n"
        + "So FireAtPlane(A, B, C) means \"fire at this plane\",\n"
        + "and leading a target is just predicting where the turret should aim when the block arrives there.",

        "There are two names for now:\n"
        + "    ATTACK  red block, costs score, hp 1-6\n"
        + "    BOMB    blue block, hp fixed at 1, destroying it gives no score\n\n"
        + "The data type decides what you can do with it:\n"
        + "    int       HP / Index -> compare, add, subtract\n"
        + "    float     Volume / seconds -> decimals allowed\n"
        + "    Vector3   Position / Velocity / Size -> three floats x / y / z\n"
        + "    string    Name / Type -> compare text only, no maths\n\n"
        + "Common mistakes: treating Position as a single number (it has three parts); comparing a string with 0.\n"
        + "Press Next to enter the tower API course and actually use these fields."
    };

    // ================== 生命周期 ==================

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        ReleaseViewport();
    }

    private void Update()
    {
        if (!Active)
            return;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
        {
            PrevPage();
            return;
        }
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown)
            || Input.GetKeyDown(KeyCode.Space))
        {
            NextPage();
            return;
        }

        // S 键：跳过整段图鉴，直接进入 Level 2 - Part 2 炮台 API 课
        if (Input.GetKeyDown(SkipKey))
        {
            SkipAll();
            return;
        }

        SpinCube();
        UpdateFrontFace();
    }

    // ================== 对外接口 ==================

    /// <summary>进入图鉴关并从第 startIndex 页（0 基）开始。</summary>
    public static void Begin(int startIndex)
    {
        BlockIntroController inst = EnsureInstance();
        if (inst == null)
            return;

        inst.BuildUI();
        inst.BuildViewportIfNeeded();
        inst.Active = true;
        inst.transitioning = false;
        inst.CurrentPage = Mathf.Clamp(startIndex, 0, PageCount - 1);
        inst.pageDone = GameProgress.LoadTaskDone(GameProgress.TaskDoneKeyBlockIntro, PageCount);
        inst.frontFace = -1;

        if (inst.root != null && !inst.root.activeSelf)
            inst.root.SetActive(true);
        if (inst.viewRoot != null && !inst.viewRoot.activeSelf)
            inst.viewRoot.SetActive(true);

        inst.Refresh();
        inst.EnterReadingMode(); // 看图鉴期间释放鼠标，鼠标移动不再转动背后场景的朝向
        Debug.Log("[BlockIntro] 方块图鉴开始：第 " + (inst.CurrentPage + 1) + "/" + PageCount + " 页。");
    }

    /// <summary>结束图鉴（离开本关 / 跳去 API 课时调用，可重复调用）。</summary>
    public static void End()
    {
        if (Instance == null)
            return;
        Instance.Active = false;
        Instance.ExitReadingMode(); // 交还鼠标、解除视角封锁
        if (Instance.root != null && Instance.root.activeSelf)
            Instance.root.SetActive(false);
        if (Instance.viewRoot != null && Instance.viewRoot.activeSelf)
            Instance.viewRoot.SetActive(false);
    }

    private static BlockIntroController EnsureInstance()
    {
        if (Instance != null)
            return Instance;
        GameObject go = new GameObject("BlockIntro");
        return go.AddComponent<BlockIntroController>();
    }

    // ================== 阅读态：释放鼠标 + 冻结视角 ==================

    /// <summary>
    /// 进入阅读态：放出鼠标（可以点翻页 / 跳过按钮），并让 PlayerCameraController 进入「纯阅读」封锁，
    /// 鼠标移动不再转动背后场景的朝向（ESC 暂停仍然可用）。
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

    /// <summary>退出阅读态：解除视角封锁、把鼠标重新锁回（随后跳关 / 回主页的调用方会再锁一次，幂等）。</summary>
    private void ExitReadingMode()
    {
        if (playerController == null)
            playerController = FindObjectOfType<PlayerCameraController>();
        if (playerController != null)
            playerController.SetViewBlocked(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = true;
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
            GoApiCourse();
            return;
        }
        CurrentPage++;
        Refresh();
    }

    /// <summary>
    /// 跳过整段图鉴（S 键 / 跳过按钮）：把 5 页全部记成已读并同步存档，随后走与末页相同的黑幕过渡进入 API 课。
    /// </summary>
    private void SkipAll()
    {
        if (!Active || transitioning)
            return;

        if (pageDone != null)
        {
            bool changed = false;
            for (int i = 0; i < pageDone.Length; i++)
            {
                if (!pageDone[i])
                {
                    pageDone[i] = true;
                    changed = true;
                }
            }
            if (changed)
                GameProgress.SaveTaskDone(GameProgress.TaskDoneKeyBlockIntro, pageDone);
        }

        Debug.Log("[BlockIntro] 跳过图鉴 → 进入 Level 2 - Part 2 炮台 API 课。");
        GoApiCourse();
    }

    /// <summary>
    /// 进入 Level 2 - Part 2 炮台 API 课：黑幕淡出 → 全黑帧执行 LevelDirector.JumpTo（内部会 End() 清场）→ 淡入，
    /// 与序章 → 语法课的过渡保持一致。过渡期间忽略键盘与按钮，防止重复触发。
    /// </summary>
    private void GoApiCourse()
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
            LevelDirector.JumpTo(2, 2, 1); // Level 2 - Part 2 炮台 API 课，从第 1 个任务开始
        });
    }

    // ================== 文案刷新 ==================

    private void Refresh()
    {
        bool zh = Localization.IsChinese;
        int page = Mathf.Clamp(CurrentPage, 0, PageCount - 1);

        if (pageDone != null && page < pageDone.Length && !pageDone[page])
        {
            pageDone[page] = true;
            GameProgress.SaveTaskDone(GameProgress.TaskDoneKeyBlockIntro, pageDone);
        }

        SetLabel(pageTagText, (zh ? "图鉴 " : "Guide ") + (page + 1) + "/" + PageCount);
        SetLabel(titleText, zh ? TitlesZh[page] : TitlesEn[page]);

        // 正文：先按正文框实测挑字号，再写文本 / 载字体，保证每页都完整落在框内
        string bodyContent = zh ? BodiesZh[page] : BodiesEn[page];
        int bodySize = FitFontSize(bodyText, bodyContent, BodyFontMax, BodyFontMin, BodyBoxWidth, BodyBoxHeight);
        if (bodyText != null)
            bodyText.fontSize = bodySize;
        SetLabel(bodyText, bodyContent);

        if (bodySize != lastBodyFontSize)
        {
            lastBodyFontSize = bodySize;
            Debug.Log("[BlockIntro] 第 " + (page + 1) + "/" + PageCount + " 页正文自适应字号："
                + bodySize + "（正文框 " + (int)BodyBoxWidth + "×" + (int)BodyBoxHeight + "）");
        }

        SetLabel(footerText, zh
            ? "键盘 ← → 翻页　·　按 S 跳过图鉴　·　读完最后一页即可进入炮台 API 课"
            : "Arrows page through · press S to skip the guide · finish the last page to start the API course");

        SetLabel(prevLabel, zh ? "‹ 上一页" : "‹ Back");
        SetLabel(nextLabel, page >= PageCount - 1
            ? (zh ? "开始 API 课 ›" : "Start the API course ›")
            : (zh ? "下一页 ›" : "Next ›"));
        SetLabel(skipLabel, zh ? "跳过图鉴 (S)" : "Skip guide (S)");

        if (prevBtn != null)
            prevBtn.interactable = page > 0;
        if (nextBtn != null)
            nextBtn.interactable = true;

        // 六个面的标签与右侧图例（按当前语言重写）
        for (int i = 0; i < Faces.Length; i++)
        {
            FaceDef f = Faces[i];
            string faceName = zh ? f.nameZh : f.nameEn;

            string faceLabelText = faceName + "\n" + f.value;
            SetFaceLabelText(faceLabels[i], faceLabelText);

            if (legendTexts[i] != null)
                SetLabel(legendTexts[i], faceName + "　" + f.value);
        }

        frontFace = -1;      // 强制下一帧重算高亮
        UpdateFrontFace();
    }

    /// <summary>每秒刷新一次「哪个面正对镜头」：换面时更新高亮与右侧该面的解释。</summary>
    private void UpdateFrontFace()
    {
        if (viewCamera == null || cubeRoot == null)
            return;

        Vector3 camPos = viewCamera.transform.position;
        int best = -1;
        float bestDot = -2f;
        for (int i = 0; i < Faces.Length; i++)
        {
            Transform ft = faceTransforms[i];
            if (ft == null)
                continue;
            Vector3 n = ft.rotation * Vector3.forward;   // 面的外法线
            float dot = Vector3.Dot(n, (camPos - ft.position).normalized);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        if (best < 0 || best == frontFace)
            return;
        frontFace = best;

        // 字必须贴在方块「外面」：TextMesh 用的是字体材质（GUI/Text Shader，ZTest Always + Cull Off），
        // 它不受深度遮挡、双面都画——六个面的字全开着时，背面 / 侧面的字会穿过前面的色块，
        // 看起来就像写在方块里面。这里只保留正对镜头的那一张，其余全部关掉，任何时刻只有一面有字。
        for (int i = 0; i < faceLabels.Length; i++)
        {
            if (faceLabels[i] == null)
                continue;
            Renderer labelRenderer = faceLabels[i].GetComponent<Renderer>();
            if (labelRenderer != null)
                labelRenderer.enabled = i == frontFace;
        }

        bool zh = Localization.IsChinese;
        FaceDef f = Faces[frontFace];
        SetLabel(faceNameText, (zh ? "正对你的面：" : "Facing you: ") + (zh ? f.nameZh : f.nameEn));
        SetLabel(faceValueText, f.value);

        // 面的说明长短不一（英文普遍更长），同样按框实测挑字号，避免压到下方正文
        string desc = zh ? f.descZh : f.descEn;
        int descSize = FitFontSize(faceDescText, desc, DescFontMax, DescFontMin, BodyBoxWidth, FaceDescHeight);
        if (faceDescText != null)
            faceDescText.fontSize = descSize;
        SetLabel(faceDescText, desc);

        for (int i = 0; i < legendBgs.Length; i++)
        {
            if (legendBgs[i] == null)
                continue;
            bool on = i == frontFace;
            legendBgs[i].color = on ? UITheme.WithAlpha(UITheme.Accent, 0.26f) : UITheme.WithAlpha(UITheme.PanelSunk, 0.55f);
            if (legendTexts[i] != null)
                legendTexts[i].color = on ? UITheme.TextPrimary : UITheme.TextMuted;
        }
    }

    private void SpinCube()
    {
        if (cubeRoot == null)
            return;
        float t = Time.unscaledTime;
        // 一边自转一边上下轻微点头，保证六个面都能轮到正对镜头
        cubeRoot.transform.localRotation = Quaternion.Euler(
            18f * Mathf.Sin(t * 0.45f) + 10f,
            Mathf.Repeat(t * 24f, 360f),
            0f);
    }

    // ================== 3D 视图（独立相机 + RenderTexture） ==================

    private void BuildViewportIfNeeded()
    {
        if (viewRoot != null)
            return;

        // 舞台放在很远的地方：主相机（远裁剪 1000）看不到它，只有本关自己的相机渲染它
        viewRoot = new GameObject("BlockIntroViewport");
        viewRoot.transform.position = new Vector3(0f, -8000f, 0f);

        cubeRoot = new GameObject("Cube");
        cubeRoot.transform.SetParent(viewRoot.transform, false);

        for (int i = 0; i < Faces.Length; i++)
            BuildFace(i);

        GameObject camGo = new GameObject("BlockIntroCamera");
        camGo.transform.SetParent(viewRoot.transform, false);
        camGo.transform.localPosition = new Vector3(-1.18f, 1.02f, -2.72f);
        camGo.transform.LookAt(viewRoot.transform.position);

        viewCamera = camGo.AddComponent<Camera>();
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = new Color(0.035f, 0.055f, 0.075f, 1f);
        viewCamera.fieldOfView = 30f;
        viewCamera.nearClipPlane = 0.05f;
        viewCamera.farClipPlane = 12f;
        viewCamera.allowHDR = false;
        viewCamera.allowMSAA = false;

        viewTexture = new RenderTexture(TextureSize, TextureSize, 24);
        viewTexture.name = "BlockIntroRT";
        viewTexture.antiAliasing = 2;
        viewTexture.Create();
        viewCamera.targetTexture = viewTexture;

        if (viewImage != null)
            viewImage.texture = viewTexture;

        viewRoot.SetActive(Active);
    }

    private void BuildFace(int index)
    {
        FaceDef f = Faces[index];

        // 面的挂点：+Z 朝外（外法线方向），后面所有子物体都按这个局部坐标系摆放
        GameObject faceNode = new GameObject("Face" + index + "_" + f.nameEn);
        faceNode.transform.SetParent(cubeRoot.transform, false);
        faceNode.transform.localPosition = f.normal * FaceOffset;
        Vector3 up = Mathf.Abs(Vector3.Dot(f.normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
        faceNode.transform.localRotation = Quaternion.LookRotation(f.normal, up);

        // 一张 Quad 只有单面可见，而 Unity 内置 Quad 的正面朝哪一侧属于引擎实现细节（不同版本 / 不同
        // 内置资源可能不一致）。这里直接正反各铺一张同色 Quad：无论正面朝 +Z 还是 -Z，绕 Y 转 180°
        // 的另一张都会把另一侧补上，因此从立方体外任意方向看过去都一定是实体色块，不会出现整面透明。
        CreateQuad(faceNode.transform, "Color" + index + "A", 0f, f.faceColor);
        CreateQuad(faceNode.transform, "Color" + index + "B", 180f, f.faceColor);

        // 面上文字：只放一张，朝向 = 面挂点的 +Z（即外法线方向），贴在色块外侧一点，
        // 从立方体外看是正读的；内壁不再放镜像字，避免转过来时看到双重 / 背面反字。
        faceLabels[index] = CreateFaceLabel(faceNode.transform, index, f);

        faceTransforms[index] = faceNode.transform;
    }

    /// <summary>在面挂点下铺一张单位 Quad（去掉碰撞体、关阴影），yRotation 控制正面朝向。</summary>
    private void CreateQuad(Transform parent, string name, float yRotation, Color color)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
        quad.transform.localScale = Vector3.one;

        Collider col = quad.GetComponent<Collider>();
        if (col != null)
            Destroy(col); // 图鉴关没有碰撞需求，销毁碰撞体避免被炮台 / 射线误判

        MeshRenderer mr = quad.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Shader sh = Shader.Find("Unlit/Color");
            if (sh == null)
                sh = Shader.Find("Standard");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");
            Material mat = new Material(sh);
            mat.color = color;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }

    /// <summary>
    /// 面上的文字标签（面名 + 取值）：唯一一张，可读面朝立方体外侧。
    /// 注意 Unity 的 TextMesh 与内置 Quad 一样，正面朝自身局部 -Z（不是 +Z）；面挂点的 +Z 是外法线，
    /// 所以标签必须绕 Y 转 180° 才能把可读面翻到外面，否则从方块外看到的是背面反字 —— 看起来就像写在方块里面。
    /// </summary>
    private TextMesh CreateFaceLabel(Transform parent, int index, FaceDef f)
    {
        GameObject labelGo = new GameObject("FaceLabel" + index);
        labelGo.transform.SetParent(parent, false);
        labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);     // TextMesh 正面 = 局部 -Z，转 180° 后朝外
        labelGo.transform.localPosition = new Vector3(0f, 0f, 0.02f);          // 贴在色块外侧，避免与面片 Z 冲突

        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.text = f.nameZh + "\n" + f.value;
        tm.font = UIFontHelper.LoadForText(tm.text, 56);
        tm.fontSize = 56;
        tm.characterSize = 0.042f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = f.textColor;

        MeshRenderer labelMr = labelGo.GetComponent<MeshRenderer>();
        if (labelMr != null && tm.font != null)
            labelMr.sharedMaterial = tm.font.material;

        FitFaceLabel(tm);
        return tm;
    }

    /// <summary>
    /// 按实测文字包围盒把字号缩放到「不超出面宽」：中英文 / 长短不同的面名（如「数据类型 int float Vector3」）
    /// 都能贴合到同一块面上，不会有大有小或者顶出方块边缘。
    /// </summary>
    private static void FitFaceLabel(TextMesh tm)
    {
        if (tm == null)
            return;
        tm.characterSize = 0.05f; // 基准字号，再按实测包围盒等比缩放
        Renderer r = tm.GetComponent<Renderer>();
        if (r == null)
            return;
        Vector3 size = r.bounds.size; // 父级只有旋转、没有缩放，世界尺寸即局部尺寸
        if (size.x <= 0.0001f || size.y <= 0.0001f)
            return;
        float k = Mathf.Min(0.80f / size.x, 0.60f / size.y); // 面宽 1 单位，留 10% 边距
        tm.characterSize = Mathf.Clamp(0.05f * k, 0.004f, 0.2f);
    }

    /// <summary>改写面上文字（换语言 / 换页时同步正反两面），并按新文本重新配字体图集。</summary>
    private static void SetFaceLabelText(TextMesh tm, string text)
    {
        if (tm == null)
            return;
        tm.text = text;
        tm.font = UIFontHelper.LoadForText(text, 56);
        MeshRenderer mr = tm.GetComponent<MeshRenderer>();
        if (mr != null && tm.font != null)
            mr.sharedMaterial = tm.font.material;
        FitFaceLabel(tm);
    }

    private void ReleaseViewport()
    {
        if (viewCamera != null)
            viewCamera.targetTexture = null;
        if (viewTexture != null)
        {
            if (Application.isPlaying)
                viewTexture.Release();
            Destroy(viewTexture);
            viewTexture = null;
        }
        if (viewRoot != null)
        {
            Destroy(viewRoot);
            viewRoot = null;
        }
    }

    // ================== UI 构建 ==================

    private void BuildUI()
    {
        if (root != null)
            return;

        GameObject canvasGo = new GameObject("BlockIntroCanvas", typeof(RectTransform));
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

        // 全屏遮罩：把 3D 场景压暗，正文与立方体都更好看
        RectTransform scrimRt = CreateRect("Scrim", canvasGo.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(RefWidth + 400f, RefHeight + 400f), new Vector2(0.5f, 0.5f));
        Image scrim = scrimRt.gameObject.AddComponent<Image>();
        scrim.sprite = WhiteSprite();
        scrim.color = UITheme.WithAlpha(UITheme.Void, 0.68f);
        scrim.raycastTarget = true;

        // 主面板
        RectTransform panelRt = CreateRect("GuidePanel", canvasGo.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PanelWidth, PanelHeight), new Vector2(0.5f, 0.5f));
        panelRoot = panelRt.gameObject;
        Image panelImg = panelRoot.AddComponent<Image>();
        panelImg.sprite = WhiteSprite();
        panelImg.color = UITheme.WithAlpha(UITheme.PanelDeep, 0.97f);
        panelImg.raycastTarget = true;

        // 顶部强调线
        RectTransform lineRt = CreateRect("TopLine", panelRt, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(PanelWidth, 3f), new Vector2(0.5f, 1f));
        Image lineImg = lineRt.gameObject.AddComponent<Image>();
        lineImg.sprite = WhiteSprite();
        lineImg.color = UITheme.WithAlpha(UITheme.Accent, 0.55f);
        lineImg.raycastTarget = false;

        // 页码
        pageTagText = CreateText("PageTag", panelRt, "", 22, UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform tagRt = pageTagText.rectTransform;
        tagRt.anchorMin = new Vector2(0f, 1f);
        tagRt.anchorMax = new Vector2(1f, 1f);
        tagRt.pivot = new Vector2(0.5f, 1f);
        tagRt.offsetMin = new Vector2(56f, 0f);
        tagRt.offsetMax = new Vector2(-56f, 0f);
        tagRt.anchoredPosition = new Vector2(0f, -32f);
        tagRt.sizeDelta = new Vector2(-112f, 30f);

        // 页标题
        titleText = CreateText("Title", panelRt, "", 34, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        RectTransform titleRt = titleText.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -66f);
        titleRt.sizeDelta = new Vector2(-112f, 50f);

        // 左：3D 视图（RawImage 展示 RenderTexture）
        RectTransform viewRt = CreateRect("Viewport", panelRt, new Vector2(0f, 1f),
            new Vector2(56f, -132f), new Vector2(ViewSize, ViewSize), new Vector2(0f, 1f));
        Image viewBg = viewRt.gameObject.AddComponent<Image>();
        viewBg.sprite = WhiteSprite();
        viewBg.color = UITheme.WithAlpha(UITheme.PanelSunk, 0.9f);
        viewBg.raycastTarget = false;

        RectTransform rawRt = CreateRect("ViewRaw", viewRt, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(ViewSize - 8f, ViewSize - 8f), new Vector2(0.5f, 0.5f));
        viewImage = rawRt.gameObject.AddComponent<RawImage>();
        viewImage.raycastTarget = false;
        viewImage.color = Color.white;
        if (viewTexture != null)
            viewImage.texture = viewTexture;

        // 视图下方的说明（自动旋转提示）
        Text viewHint = CreateText("ViewHint", panelRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform hintRt = viewHint.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 1f);
        hintRt.anchorMax = new Vector2(0f, 1f);
        hintRt.pivot = new Vector2(0f, 1f);
        hintRt.anchoredPosition = new Vector2(56f, -736f);
        hintRt.sizeDelta = new Vector2(ViewSize, 26f);
        viewHint.text = Localization.IsChinese
            ? "自动旋转 · 正对镜头的面会被高亮"
            : "Auto-rotating · the face aiming at the camera is highlighted";
        viewHint.font = UIFontHelper.LoadForText(viewHint.text, 18);

        // 右：文字栏
        RectTransform colRt = CreateRect("Column", panelRt, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        colRt.anchorMin = new Vector2(0f, 0f);
        colRt.anchorMax = new Vector2(1f, 1f);
        colRt.offsetMin = new Vector2(ColumnLeftInset, ColumnBottomInset);
        colRt.offsetMax = new Vector2(-ColumnRightInset, -ColumnTopInset);

        faceNameText = StackText(colRt, "FaceName", 26, UITheme.Accent, 0f, 38f);
        faceNameText.fontStyle = FontStyle.Bold;
        faceValueText = StackText(colRt, "FaceValue", 22, UITheme.Value, 42f, 28f);
        faceDescText = StackText(colRt, "FaceDesc", DescFontMax, UITheme.TextSub, 74f, FaceDescHeight);

        // 正文：从「面的说明」下方一直排到文字栏底部（图例已移到左侧，底部不再和正文抢空间）
        bodyText = CreateText("Body", colRt, "", BodyFontMax, UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform bodyRt = bodyText.rectTransform;
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.offsetMin = new Vector2(0f, BodyBottomInset);
        bodyRt.offsetMax = new Vector2(0f, -BodyTopInset);
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        bodyText.lineSpacing = 1.28f;

        // 图例：2 列 × 3 行，挂在左侧 3D 视图下方（原先塞在右侧正文底部，长页正文会压到图例上）
        Text legendTitle = CreateText("LegendTitle", panelRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform legendTitleRt = legendTitle.rectTransform;
        legendTitleRt.anchorMin = new Vector2(0f, 1f);
        legendTitleRt.anchorMax = new Vector2(0f, 1f);
        legendTitleRt.pivot = new Vector2(0f, 1f);
        legendTitleRt.anchoredPosition = new Vector2(56f, -770f);
        legendTitleRt.sizeDelta = new Vector2(ViewSize, 22f);
        legendTitle.text = Localization.IsChinese ? "六个面（转一圈就认全了）" : "Six faces (one turn and you know them all)";
        legendTitle.font = UIFontHelper.LoadForText(legendTitle.text, 18);

        float colW = (ViewSize - 12f) * 0.5f;
        for (int i = 0; i < 6; i++)
        {
            int col = i % 2;
            int row = i / 2;
            RectTransform rowRt = CreateRect("Legend" + i, panelRt, new Vector2(0f, 1f),
                new Vector2(56f + col * (colW + 12f), -(800f + row * 26f)),
                new Vector2(colW, 24f), new Vector2(0f, 1f));
            Image bg = rowRt.gameObject.AddComponent<Image>();
            bg.sprite = WhiteSprite();
            bg.color = UITheme.WithAlpha(UITheme.PanelSunk, 0.55f);
            bg.raycastTarget = false;
            legendBgs[i] = bg;

            Text t = CreateText("Label", rowRt, "", 16, UITheme.TextMuted, TextAnchor.MiddleLeft);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(10f, 0f);
            t.rectTransform.offsetMax = new Vector2(-8f, 0f);
            legendTexts[i] = t;
        }

        // 右下按钮组
        prevBtn = CreateButton(panelRt, "PrevBtn", "‹ 上一页",
            new Vector2(0.5f, 0f), new Vector2(-330f, 40f), new Vector2(180f, 62f));
        prevBtn.onClick.AddListener(PrevPage);
        prevLabel = prevBtn.GetComponentInChildren<Text>();

        nextBtn = CreateButton(panelRt, "NextBtn", "下一页 ›",
            new Vector2(0.5f, 0f), new Vector2(-120f, 40f), new Vector2(210f, 62f));
        nextBtn.onClick.AddListener(NextPage);
        nextLabel = nextBtn.GetComponentInChildren<Text>();

        Button skipBtn = CreateButton(panelRt, "SkipBtn", "跳过图鉴 (S)",
            new Vector2(0.5f, 0f), new Vector2(120f, 40f), new Vector2(200f, 62f));
        skipBtn.onClick.AddListener(SkipAll);
        skipLabel = skipBtn.GetComponentInChildren<Text>();

        // 操作提示：放右下角，避开底部中间按钮组
        footerText = CreateText("Footer", panelRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleRight);
        RectTransform footerRt = footerText.rectTransform;
        footerRt.anchorMin = new Vector2(1f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(1f, 0f);
        footerRt.anchoredPosition = new Vector2(-56f, 92f);
        footerRt.sizeDelta = new Vector2(720f, 26f);

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

    /// <summary>在容器顶部按 y（距顶像素）与高度纵向排列一行文本。</summary>
    private static Text StackText(Transform parent, string name, int size, Color color, float y, float height)
    {
        return StackText(parent, name, size, color, y, height, TextAnchor.MiddleLeft);
    }

    private static Text StackText(Transform parent, string name, int size, Color color, float y, float height, TextAnchor anchor)
    {
        Text t = CreateText(name, parent, "", size, color, anchor);
        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(0f, height);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.lineSpacing = 1.2f;
        return t;
    }

    /// <summary>按框实测挑字号：从 max 逐级降到 min，返回第一个能让内容完整落在框内的字号。</summary>
    private static int FitFontSize(Text t, string content, int max, int min, float boxWidth, float boxHeight)
    {
        if (t == null || string.IsNullOrEmpty(content))
            return max;

        for (int size = max; size > min; size--)
        {
            if (MeasureTextHeight(t, content, size, boxWidth) <= boxHeight)
                return size;
        }
        return min;
    }

    /// <summary>用 Text 自己的 TextGenerator 量一段文字在给定宽度、字号下的排版高度（与渲染同一套设置）。</summary>
    private static float MeasureTextHeight(Text t, string content, int size, float width)
    {
        TextGenerationSettings settings = t.GetGenerationSettings(new Vector2(Mathf.Max(32f, width), 0f));
        settings.fontSize = size;
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.generateOutOfBounds = true;

        TextGenerator gen = t.cachedTextGeneratorForLayout;
        gen.Populate(content, settings);
        return gen.GetPreferredHeight(content, settings) / t.pixelsPerUnit;
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

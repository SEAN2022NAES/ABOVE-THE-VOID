using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 主页（主菜单）：
///   - 展示 ABOVE THE VOID 主题（标题 / 副标题 / 三坐标轴色彩点缀）；
///   - 项目目的介绍：培养学生的编程兴趣与数学思维（扩写段落）；
///   - 作者署名 SEAN MA，并在署名下面挂一条可点击的 GitHub 引导（Application.OpenURL 打开
///     https://github.com/SEAN2022NAES，中英双语，样式与署名行一致）；
///   - 关卡入口：关卡 0（Part 1 手动体验关 / Part 2 自由玩耍关）/ 关卡 1（教学线：Part 1 编程序章、
///     Part 2 编辑器入门、Part 3 语法课）/ 关卡 2（Part 1 方块图鉴、Part 2 API 课）/ 关卡 3（完整玩法）；
///     首次进入只有关卡 0 可玩，逐级解锁。
///   - 左下角常驻版本号（读 GameVersion.Display，全工程唯一来源）。
/// 进入关卡 0 后：隐藏主页、解除视角输入占用、启动教学并允许鼠标左键手动开火；
/// 关卡 1 / 关卡 2 统一走 LevelDirector 路由（与 /level 指令同一条路径）；
/// 关卡 3 保留主页侧的会话复位与统一开场入口（LevelDirector.BeginFullLevelOpening）。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("配色")]
    [Tooltip("主页背景色（半透明，露出后方坐标系作为主题背景）")]
    public Color bgColor = UITheme.PanelDeep;
    [Tooltip("强调色（标题 / 副标题 / 关卡按钮）")]
    public Color accentColor = UITheme.Accent;

    [Header("字号")]
    public int titleFontSize = 100;
    public int subtitleFontSize = 36;
    public int bodyFontSize = 28;
    public int authorFontSize = 32;
    public int levelTitleFontSize = 26;
    public int buttonFontSize = 30;

    private GameObject root;
    private Text titleText;
    private Text subtitleText;
    private Text bodyText;
    private Text authorText;

    /// <summary>作者 GitHub 引导行（可点击，样式与署名行一致，中英双语）。</summary>
    private Text githubText;

    /// <summary>作者 GitHub 引导行的容器：把 Octocat mark 图标与文案水平排布在同一行（图标在左、垂直居中）。</summary>
    private RectTransform githubRow;

    /// <summary>作者 GitHub 引导行的官方 Octocat mark 图标（与文案同一点击行为：点图标即打开作者主页）。</summary>
    private Image githubIcon;

    /// <summary>左下角常驻版本号（内容取自 GameVersion.Display，全工程唯一来源）。</summary>
    private Text versionText;

    /// <summary>作者署名（正文用）与作者 GitHub 主页地址（点击引导用）。</summary>
    public const string AuthorName = "SEAN MA";
    public const string AuthorGitHubUrl = "https://github.com/SEAN2022NAES";
    public const string AuthorGitHubHandle = "github.com/SEAN2022NAES";

    /// <summary>作者 GitHub 引导行图标边长（px；参考分辨率 1920x1080 下与 20 px 文案的行高匹配）。</summary>
    private const float GitHubIconSize = 24f;

    /// <summary>图标与文案之间的水平间距（px，随 CanvasScaler 等比缩放）。</summary>
    private const float GitHubIconGap = 10f;

    /// <summary>官方 Octocat mark 的 PNG 资源路径（对应 Assets/Resources/Sprites/github-mark.png）。</summary>
    private const string GitHubMarkResourcePath = "Sprites/github-mark";

    /// <summary>官方 mark 贴图缓存（主页可能被反复重建，避免重复 Sprite.Create）。</summary>
    private static Sprite githubMarkSprite;

    private Text levelTitleText;

    private Text level0Label;
    private Text level1Label;
    private Text level2Label;
    private Text level3Label;
    // 逐级解锁：关卡按钮 / 底图 / 文本引用（下标 = 关卡序号 - LevelProgress.MinLevel，MinLevel = 0 即关卡 0），
    // 用于解锁后即时刷新灰显与锁定状态
    private readonly Button[] levelButtons = new Button[4];
    private readonly Image[] levelButtonImages = new Image[4];
    private readonly Text[] levelButtonLabels = new Text[4];
    private Text langZhLabel;
    private Text langEnLabel;
    private FlyingBlockSpawner spawner;
    private PlayerCameraController playerController;
    private float pulseTime;

    /// <summary>指令框引用（LateUpdate 判断「指令框打开时不要清 EventSystem 选中项」，惰性获取）。</summary>
    private CommandConsoleController commandConsole;

    /// <summary>
    /// 主页是否正在显示（Home 面板可见）。
    /// EscapeRouter 用它判断「主页状态按 ESC 直接忽略（无反应）」，以及指令框收起后是否回到主页。
    /// </summary>
    public bool IsMenuVisible => root != null && root.activeSelf;

    private void Start()
    {
        // 主页接管：暂停生成器（不刷新任何长方体），阻塞视角输入，解锁鼠标以点击按钮
        spawner = FlyingBlockSpawner.Instance;
        if (spawner != null)
            spawner.SuspendSpawning();

        playerController = FindObjectOfType<PlayerCameraController>();
        if (playerController != null)
            playerController.SetExternalBlock(true);

        SetCursorLocked(false);
        BuildUI();

        // 主页表现：不显示 Score 分数 HUD（含增量提示 / 负分红字）、不显示 print 荧幕、
        // 不生成蓝块，并隐藏三维坐标轴（否则透过主页半透明遮罩仍能看到蓝色 X 轴）
        LevelPresentation.ApplyMainMenu();

        Localization.OnLanguageChanged += RefreshLanguageTexts; // 主页语言切换即时刷新
    }

    private void OnDestroy()
    {
        Localization.OnLanguageChanged -= RefreshLanguageTexts;
    }

    private void LateUpdate()
    {
        // 标题轻微呼吸脉动（真实时间，不受时间冻结影响）
        pulseTime += Time.unscaledDeltaTime;
        if (titleText != null)
        {
            float a = 0.85f + 0.15f * Mathf.Sin(pulseTime * 2.2f);
            Color c = titleText.color;
            c.a = a;
            titleText.color = c;
        }

        // 主页屏蔽 Enter：清掉 EventSystem 的选中项，避免「点击过按钮后 Enter 被当成 Submit 二次触发主页按钮」
        // （主页不提供键盘导航，选中项本身无展示作用；Enter 在主页应与 ESC 一样完全无反应）
        //
        // 关键修正：主页可见时 ` 键打开的指令框里的 InputField 需要长期保持 EventSystem 焦点。
        // uGUI 的 InputField 只有作为 currentSelectedGameObject 时才会收到键盘事件（EventSystem 每帧派发
        // UpdateSelectedEvent）；若这里无条件每帧 SetSelectedGameObject(null)，输入框会在每帧末被取消选中，
        // 表现为「打开指令框后敲字没反应、回车也提交不了」。因此仅在「主页可见 且 指令框未打开」时才清空选中项。
        if (!IsMenuVisible)
            return; // 非主页（关卡内 / 代码编辑器 / 教学）一律不干预选中项

        if (commandConsole == null)
            commandConsole = FindObjectOfType<CommandConsoleController>();
        if (commandConsole != null && commandConsole.IsOpen)
            return; // 指令框打开：保留输入框焦点，保证能打字与回车提交

        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null)
            es.SetSelectedGameObject(null);
    }

    /// <summary>点击关卡入口：隐藏主页并启动对应关卡（关卡1引导教学、关卡2炮台API教学、关卡3完整玩法）。</summary>
    public void StartLevel(int levelIndex)
    {
        // 逐级解锁：未解锁关卡不可进入（按钮本身已灰显 + 锁定，此处再兜底拦截一次）
        if (!LevelProgress.IsLevelUnlocked(levelIndex))
        {
            Debug.Log("[MainMenu] 关卡 " + levelIndex + " 尚未解锁（当前最高可玩：关卡 " +
                      LevelProgress.HighestUnlockedLevel + "）。");
            return;
        }

        // 离开主页：恢复三维坐标轴显示（主页期间被 ApplyMainMenu 隐藏）
        LevelPresentation.RestoreAxes();

        // 断点续玩：主页点击某关时，若存在「属于该关」的合法存档，就从记录的 part / section 续玩，而不是从该关开头重来。
        //   · 只有带章节拆分的分区（Level 1 - Part 1 编程序章 / Part 2 编辑器入门 / Part 3 语法课 / Level 2 - Part 1 图鉴关 / Part 2 API 课）才需要按章节定位，
        //     这里统一交给 LevelDirector.JumpTo 路由，与 /level 走完全同一条路径，避免两套逻辑不一致；
        //   · 优先级：/level 跳关成功后已用跳关参数覆盖存档，所以这里读到的必然是「最后一次到达的位置」，
        //     存档不会把玩家拽回跳关之前的旧位置；
        //   · 脏存档（越界 / 不存在的章节 / 非法分区）→ GameProgress.TryGetResume 返回 false，
        //     直接落到下面的原有流程，从该关开头进入，绝不会因为坏存档进不去关；
        //   · 启动后仍停在主页：本方法只在玩家主动点击关卡按钮时调用，不做任何自动跳关。
        if (GameProgress.TryGetResume(levelIndex, out int resumePart, out int resumeSection) &&
            GameProgress.GetSectionCount(levelIndex, resumePart) > 0)
        {
            string resumeMessage = LevelDirector.JumpTo(levelIndex, resumePart, resumeSection);
            Debug.Log("[MainMenu] 断点续玩：Level " + levelIndex + " - Part " + resumePart +
                      " 第 " + resumeSection + " 章 → " + resumeMessage);
            return;
        }

        // 关卡 1（Part 1 讲解关起）/ 关卡 2 - 1 方块图鉴（图鉴读完自动进 Part 2 API 课）：
        // 统一走 LevelDirector 路由，保证表现开关 / 生成控制 / 教学进度与指令框、/level 完全一致
        if (levelIndex == 1 || levelIndex == 2)
        {
            string msg = LevelDirector.JumpTo(levelIndex, 1);
            Debug.Log("[MainMenu] 进入关卡" + levelIndex + "：" + msg);
            return;
        }

        if (levelIndex != 0 && levelIndex != 3)
            return;

        // Level 0 - Part 2 自由玩耍关：玩家上次停在自由玩耍关（体验关已走完、自动顺接或 /level 0 2 进过）时，
        // 主页点「关卡 0」直接回到自由玩耍关，而不是每次都强迫重听 Part 1 教学。
        // 统一交给 LevelDirector.JumpTo 路由（清场 / 表现开关 / 远古手动手感 / 立即出块都在里面），
        // 与 /level 0 2 走同一条路径，避免两套逻辑不一致；无此处存档（首次进入）则落到下面的 Part 1 教学流程。
        if (levelIndex == 0 &&
            GameProgress.TryGetResume(levelIndex, out int level0Part, out int level0Section) &&
            level0Part == 2)
        {
            string freePlayMessage = LevelDirector.JumpTo(levelIndex, level0Part, level0Section);
            Debug.Log("[MainMenu] 断点续玩：Level 0 - Part 2 自由玩耍关 → " + freePlayMessage);
            return;
        }

        // 断点续玩：主页直接进入的两个分区（Level 0 - Part 1 手动体验关、Level 3 - Part 1 完整玩法）没有章节拆分，
        // 位置固定从该分区开头开始；这里把「关卡 + 分区」写入存档（section = 0），
        // 既作为续玩点，也作为 /lock 清档范围与 Level 3 最高分结算所需的「当前关卡」会话标识。
        GameProgress.SaveResume(levelIndex, 1, 0);

        if (root != null)
            root.SetActive(false);

        // 从主页进入关卡：先把可能残留的教学关组件（讲解关 / 图鉴关 / C# 语法课）全部收掉，
        // 否则上一次没走完的面板会盖在实时画面上
        CodeIntroTutorialController.End();
        BlockIntroController.End();
        CodeSyntaxTutorialController.End();

        // 代码编辑器复位：主页直接进入关卡（Level 1 - Part 1 / Level 3 - Part 1）不走 LevelDirector.JumpTo，
        // 必须在这里同样收尾，否则上一关编译成功的 runner 会继续每帧驱动炮台（代码跨关仍然生效）。
        // 位置在各教学 Begin/Tutorial 启动之前，保证教学能自己重新设置编辑器内容。
        CodeEditorController menuEditor = FindObjectOfType<CodeEditorController>();
        if (menuEditor != null)
            menuEditor.ResetForLevelChange();

        if (playerController != null)
            playerController.SetExternalBlock(false); // 解除输入占用

        SetCursorLocked(true);

        // 鼠标左键手动激光：只有 Level 0（主页按钮进入的是 Part 1 手动体验关）允许，
        // Level 3 等其余关卡一律禁用（改由代码编辑器 / 炮台自动开火接管）。
        // 仅影响「左键 → FireLaser」手动链路，不影响 tower.Fire 等自动化调用。
        CommandConsoleController.SetManualLaserAllowed(levelIndex, 1);

        // 计分：进入关卡即重开一局分数（击毁红块加分 / 红块到基地扣分 / 蓝块可击毁但不计分）
        // 漏块扣分开关：只有 Level 0（Part 1 教学 / Part 2 自由玩耍）关闭（红方块撞原点不扣分），其它关卡恢复扣分。
        // 必须在 ResetAll() 之前下发，且必须显式写 true，避免从关卡 0 返回主页后再进关卡 3 仍带着「不扣分」的残留状态。
        ScoreManager.SetLeakPenaltyEnabled(levelIndex != 0);
        ScoreManager.ResetAll();

        // 生命值：进入关卡即重开一局生命（默认 5 点）
        LivesManager.ResetAll();

        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (levelIndex == 0)
        {
            // Level 0 - Part 1 手动体验关（原「引导教学关」）：由教学控制器接管，逐步引导鼠标左键手动开火打方块；
            // 表现 = 显示 Score HUD 与生命条（life）、不显示 print 荧幕、**禁用蓝色 BOMB 块**（只出红块，
            // 蓝块是唯一掉 life 的来源，禁用后新手误点也不会 game over），红块撞原点不扣分（漏块扣分关掉）
            if (tutorial != null)
            {
                tutorial.StartTutorial(); // 内部会下发 ApplyLevel0（HUD + life + 禁用蓝块）与手动激光开关
            }
            else
            {
                LevelPresentation.ApplyLevel0();  // 无教学组件时同样保证手动体验关表现一致
                ScoreManager.SetLeakPenaltyEnabled(false); // 红块撞原点不扣分
                if (spawner != null)
                    spawner.BeginSpawning();      // 开始出块（保留进游戏前5秒缓冲）
            }
            Debug.Log("[MainMenu] 进入关卡0：主页隐藏，手动体验开始。");
        }
        else
        {
            // Level 3 完整玩法：跳过教学，隐藏教学面板并恢复 E 键，重开会话（全部重新计时）后正常生成方块
            if (tutorial != null)
                tutorial.ExitToMenu();
            LevelPresentation.ApplyLevel3(); // 恢复 Score / print 荧幕显示与蓝色 BOMB 块生成
            if (spawner != null)
                spawner.ResetSession();    // 重开会话：清零消灭计数与方块编号
            // 主页进入完整玩法关同样走统一开场入口：先 Cancel 旧门控再进入「等待玩家启动」状态
            // （期间不出块、不显示炮台），只有玩家代码调用 tower.GameStart() 才开始出块；
            // 与控制台 /level 3 1 跳转路径保持一致，避免两条入口行为不一致导致「一进场就出块 / 提前开始」
            LevelDirector.BeginFullLevelOpening();
            TimeHintController timeHint = FindObjectOfType<TimeHintController>();
            if (timeHint != null)
            {
                timeHint.ResetTimer();     // 顶部时间提示重新从 00:00 开始
                timeHint.SetVisible(true); // 确保顶部计时显示（教学退出时可能已隐藏）
            }
            Debug.Log("[MainMenu] 进入关卡3：主页隐藏，完整玩法开始。");
        }
    }

    /// <summary>隐藏主页并解除输入占用（供关卡跳转 / 开发者指令调用；已在游戏内时无操作）。</summary>
    public void HideMenu()
    {
        if (root == null || !root.activeSelf)
            return;
        root.SetActive(false);
        if (playerController != null)
            playerController.SetExternalBlock(false);
        SetCursorLocked(true);
        LevelPresentation.RestoreAxes(); // 离开主页：恢复三维坐标轴
        Debug.Log("[MainMenu] 主页已隐藏（开发者指令触发）。");
    }

    /// <summary>从游戏内返回主页：终止教学、清场暂停生成、解除暂停并回原点、主页接管输入。</summary>
    public void ShowMenu()
    {
        if (root == null)
            return;

        // 关键：先收掉可能仍在显示的教学 / 图鉴面板。这些面板的 Canvas sortingOrder（210）高于主页（100），
        // 不收起会把主页整个盖住 —— 表现为「关卡里按 ESC → 点 Main Menu，却回不到主页（画面还是图鉴）」。
        CodeIntroTutorialController.End();
        BlockIntroController.End();

        // 终止教学（隐藏面板 + 清场）
        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (tutorial != null)
            tutorial.ExitToMenu();

        // 终止代码编辑器内的 C# 语法教学（隐藏任务卡与提示条；下次进入 Level 1 - Part 3 时进度重置）
        CodeSyntaxTutorialController.End();

        // 回到主页：不在任何关卡内，手动激光（鼠标左键）一律禁用
        CommandConsoleController.SetManualLaserAllowed(0, 0);

        // 回到主页：清掉「只能手动游玩（Level 0 - Part 1）」标记，
        // 否则主页停留期间代码编辑器仍被锁在禁编辑状态，「继续游戏 / 其它入口」将无法打开编辑器
        LevelDirector.ClearManualPlayOnly();

        // 回到主页：终止仍在进行的开场停顿（否则停顿到期会把生成器重新打开，主页上又开始出块）
        LevelOpeningGate.Cancel();

        // 主页不刷方块：暂停生成并清空场上；同时把「远古纯手动手感」（Level 0 - Part 2 自由玩耍关）参数还原，
        // 否则从自由玩耍关返回主页后，再进 Level 3 / 关卡 0 会带着 1.2 秒出块间隔与 40 块上限的旧手感。
        // （/level 指令路径由 LevelDirector.JumpTo 还原，这里补上「主页按钮」这条路径。）
        if (spawner != null)
        {
            spawner.SuspendSpawning();
            spawner.ClearAllBlocks();
            spawner.SetLegacyManualProfile(false);
        }

        // 解除暂停 / 冻结并回原点；随后主页接管输入（禁止游戏按键与暂停）
        if (playerController != null)
        {
            playerController.ResetToLevelStart();
            playerController.SetExternalBlock(true);
        }

        SetCursorLocked(false);
        root.SetActive(true);

        // 主页表现：隐藏 Score 分数 HUD（含增量 / 负分红字）、print 荧幕、蓝块与三维坐标轴
        LevelPresentation.ApplyMainMenu();

        // 一局结束结算：从游戏内返回主页时，若本局处于 Level 3，把本局分数提交为该关最高分
        //（只在分数更高时写入并 Save()，其它关卡 / 主页不产生任何写入）
        GameProgress.CommitCurrentLevelHighScore(ScoreManager.Instance != null ? ScoreManager.Instance.Score : 0);

        // 回到主页 = 不在任何关卡内：清掉会话位置（只清内存，不动存档），避免重复结算；
        // 续玩断点本身保留不动，下次点击该关按钮时仍从记录的位置续玩。
        GameProgress.ClearSessionPosition();

        // 逐级解锁：回到主页时按最新进度刷新关卡按钮（刚通关的关卡解锁后立刻可见可点；最高分同步刷新）
        RefreshLevelLocks();

        Debug.Log("[MainMenu] 返回主页。");
    }

    // ---------------- UI 构建 ----------------

    private void BuildUI()
    {
        EnsureEventSystem();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // 置顶于 HUD 之上

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        // 背景：全屏半透明遮罩（露出坐标系作为主题背景）
        RectTransform bg = CreateRect("BG", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f), new Vector2(0.5f, 0.5f));
        Stretch(bg);
        Image bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = bgColor;

        root = bg.gameObject;

        // ---- 标题 ----
        titleText = CreateText("Title", root.transform, "ABOVE THE VOID", titleFontSize, UITheme.TextPrimary, TextAnchor.MiddleCenter, true);
        titleText.fontStyle = FontStyle.Bold; // v0.1.1：标题加粗微调
        SetRect(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(1600f, 120f));
        AddOutline(titleText.gameObject, UITheme.WithAlpha(UITheme.Void, 0.9f), new Vector2(4f, -4f));

        // ---- 副标题（主题展示）----
        subtitleText = CreateText("Subtitle", root.transform, "", subtitleFontSize, accentColor, TextAnchor.MiddleCenter, false);
        SetRect(subtitleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(1700f, 60f));

        // ---- 三坐标轴色彩点缀：X 蓝 / Y 红 / Z 绿 ----
        CreateAxisBadge("AxisX", new Vector2(0f, 168f), -150f, "X", UITheme.AxisX);
        CreateAxisBadge("AxisY", new Vector2(0f, 168f), 0f, "Y", UITheme.AxisY);
        CreateAxisBadge("AxisZ", new Vector2(0f, 168f), 150f, "Z", UITheme.AxisZ);

        // ---- 分隔线 ----
        RectTransform divider = CreateRect("Divider", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(900f, 2f), new Vector2(0.5f, 0.5f));
        Image dividerImg = divider.gameObject.AddComponent<Image>();
        dividerImg.color = UITheme.Hairline;

        // ---- 项目目的（扩写：编程兴趣 + 数学思维；语言切换后刷新）----
        bodyText = CreateText("Goal", root.transform, "", bodyFontSize, UITheme.TextBody, TextAnchor.MiddleCenter, false);
        SetRect(bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1550f, 360f));
        bodyText.lineSpacing = 1.25f;

        // ---- 作者 ----
        authorText = CreateText("Author", root.transform, "", authorFontSize, UITheme.Value, TextAnchor.MiddleCenter, false);
        SetRect(authorText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -235f), new Vector2(1200f, 48f));

        // ---- 作者 GitHub 引导（点击用 Application.OpenURL 打开作者 GitHub 主页）----
        // 官方 Octocat mark 图标 + 文案放在同一个水平排布容器里（图标在文案左侧、垂直居中）：
        // 图标尺寸固定、文案长度随语言切换变化，交给 HorizontalLayoutGroup 自动排版，
        // 因此中英文切换时图标始终紧跟文案左侧、不会跑位；窗口缩放走 CanvasScaler 等比缩放，间距同步缩放。
        // CreateText 默认 raycastTarget = false，可点击行必须在 MakeClickableLink 里单独打开射线；
        // 图标同样单独打开射线并挂同一点击回调（点图标 = 点文案，都打开作者 GitHub 主页）。
        githubRow = CreateRect("AuthorGitHubRow", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -266f), new Vector2(1200f, 28f), new Vector2(0.5f, 0.5f));
        HorizontalLayoutGroup rowLayout = githubRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleCenter; // 整行居中：文案变长变短，图标与文案作为整体保持居中
        rowLayout.spacing = GitHubIconGap;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;  // 不拉伸子项，图标保持正方形比例
        rowLayout.childForceExpandHeight = false;

        githubIcon = CreateGitHubMarkIcon(githubRow, GitHubIconSize);

        githubText = CreateText("AuthorGitHub", githubRow, "", 20, UITheme.TextSub, TextAnchor.MiddleCenter, false);
        MakeClickableLink(githubText, OpenAuthorGitHub);
        MakeClickableIcon(githubIcon, OpenAuthorGitHub); // 点图标 = 点文案：同一回调打开作者 GitHub 主页

        // ---- 左下角常驻版本号（唯一来源：GameVersion.Display）----
        versionText = CreateText("Version", root.transform, "", 18, UITheme.TextMuted, TextAnchor.MiddleLeft, true);
        // 锚点挂在屏幕左下角（屏幕空间固定，不随主内容纵向排布移动）：左 220 px、底 26 px
        SetRect(versionText.rectTransform, Vector2.zero, new Vector2(220f, 26f), new Vector2(400f, 26f));

        // ---- 语言选择：中文 / English ----
        CreateLanguageSelector();

        // ---- 关卡区 ----
        levelTitleText = CreateText("LevelTitle", root.transform, "", levelTitleFontSize, UITheme.TextSub, TextAnchor.MiddleCenter, false);
        SetRect(levelTitleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -304f), new Vector2(1200f, 44f));

        // 关卡 0 手动体验关 / 关卡 1 教学线 / 关卡 2 图鉴与 API / 关卡 3 完整玩法（四个按钮等距排布）
        CreateLevelButton("Level0", new Vector2(-390f, -382f), 0, out level0Label);
        CreateLevelButton("Level1", new Vector2(-130f, -382f), 1, out level1Label);
        CreateLevelButton("Level2", new Vector2(130f, -382f), 2, out level2Label);
        CreateLevelButton("Level3", new Vector2(390f, -382f), 3, out level3Label);

        // ---- 第三方字体署名（SIL OFL 1.1 要求署名 + 随包附带许可全文）----
        // 两款字体均以 SIL Open Font License 1.1 授权，许可全文见 Assets/Resources/Fonts/OFL.txt。
        Text fontCredits = CreateText("FontCredits", root.transform,
            "Fonts: Noto Sans CJK SC / JetBrains Mono — SIL OFL 1.1",
            18, UITheme.TextMuted, TextAnchor.MiddleCenter, false);
        SetRect(fontCredits.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -472f), new Vector2(1400f, 26f));

        // 初始填充当前语言文案
        RefreshLanguageTexts();
    }

    /// <summary>创建三坐标轴彩色徽标（X/Y/Z 三色点缀）。</summary>
    private void CreateAxisBadge(string name, Vector2 center, float offsetX, string label, Color color)
    {
        Text t = CreateText(name, root.transform, label, 44, color, TextAnchor.MiddleCenter, true);
        SetRect(t.rectTransform, new Vector2(0.5f, 0.5f), center + new Vector2(offsetX, 0f), new Vector2(80f, 70f));
        AddOutline(t.gameObject, UITheme.WithAlpha(UITheme.Void, 0.9f), new Vector2(2f, -2f));
    }

    /// <summary>
    /// 创建关卡按钮（按 LevelProgress 判断是否已解锁：未解锁自动灰显 + 不可点击，并在文本末尾标注「锁定」）。
    /// 按钮 / 底图 / 文本的引用按下标（关卡序号 - LevelProgress.MinLevel）保存，供解锁后 RefreshLevelLocks 即时刷新。
    /// </summary>
    private void CreateLevelButton(string name, Vector2 pos, int levelIndex, out Text labelText)
    {
        bool unlocked = LevelProgress.IsLevelUnlocked(levelIndex);

        RectTransform rt = CreateRect(name, root.transform, new Vector2(0.5f, 0.5f), pos, new Vector2(240f, 92f), new Vector2(0.5f, 0.5f));
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = unlocked
            ? UITheme.WithAlpha(UITheme.Accent, 0.18f)
            : UITheme.PanelSunk;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = unlocked;
        if (unlocked)
        {
            int captured = levelIndex;
            btn.onClick.AddListener(() => StartLevel(captured));
        }

        labelText = CreateText("Label", rt, "", buttonFontSize, unlocked ? UITheme.TextPrimary : UITheme.TextDim, TextAnchor.MiddleCenter, false);
        Stretch(labelText.rectTransform);
        labelText.lineSpacing = 0.9f;

        int index = levelIndex - LevelProgress.MinLevel;
        if (index >= 0 && index < levelButtons.Length)
        {
            levelButtons[index] = btn;
            levelButtonImages[index] = img;
            levelButtonLabels[index] = labelText;
        }
    }

    /// <summary>
    /// 按当前解锁进度刷新关卡按钮：已解锁 → 青蓝底 + 白字 + 可点击；
    /// 未解锁 → 灰暗底 + 灰字 + 「锁定 / LOCKED」标注 + 点击无效。
    /// 通关解锁、/unlock 指令、返回主页、语言切换时都会调用（幂等）。
    /// </summary>
    public void RefreshLevelLocks()
    {
        for (int i = 0; i < levelButtons.Length; i++)
        {
            int levelIndex = i + LevelProgress.MinLevel;
            bool unlocked = LevelProgress.IsLevelUnlocked(levelIndex);

            Button btn = levelButtons[i];
            if (btn != null)
            {
                btn.interactable = unlocked;
                btn.onClick.RemoveAllListeners();
                if (unlocked)
                {
                    int captured = levelIndex;
                    btn.onClick.AddListener(() => StartLevel(captured));
                }
            }

            Image img = levelButtonImages[i];
            if (img != null)
            {
                img.color = unlocked
                    ? UITheme.WithAlpha(UITheme.Accent, 0.18f)
                    : UITheme.PanelSunk;
            }

            Text label = levelButtonLabels[i];
            if (label != null)
            {
                // Level 3 有最高分记录时按钮文案多一行（关卡 3 / 完整玩法 / 最高分 xxx），
                // 三行放不进 240x92 的按钮，故仅该按钮把字号收一档，其余按钮保持 buttonFontSize 不变。
                bool showBest = (levelIndex == 3 && unlocked && GameProgress.HasHighScore(levelIndex));
                string text = LevelButtonText(levelIndex);
                if (!unlocked)
                    text += Localization.IsChinese ? "\n锁定" : "\nLOCKED";
                label.fontSize = showBest ? Mathf.Max(16, buttonFontSize - 8) : buttonFontSize;
                label.text = text;
                label.color = unlocked ? UITheme.TextPrimary : UITheme.TextDim;
                label.font = UIFontHelper.LoadForText(label.text, label.fontSize);
            }
        }
    }

    /// <summary>关卡按钮基础文案（不含锁定标注），随语言切换。</summary>
    private static string LevelButtonText(int levelIndex)
    {
        bool zh = Localization.IsChinese;
        switch (levelIndex)
        {
            case 0:
                // 副标题只体现「手动」这一玩法属性：不再出现 Free / 自由玩字样（避免与「自由玩耍关」混淆）。
                return zh ? "关卡 0\n手动体验" : "Level 0\nHands-on";
            case 1:
                return zh ? "关卡 1\n教学" : "Level 1\nLessons";
            case 2:
                return zh ? "关卡 2\n方块 · API" : "Level 2\nBlocks · API";
            case 3:
                {
                    string level3Text = zh ? "关卡 3\n完整玩法" : "Level 3\nFull Game";
                    // 最高分：仅当该关已有记录时才追加第三行（中文「最高分 12345」/ 英文「BEST 12345」），
                    // 无记录（HasHighScore = false）时不显示，保持与加持久化之前完全一致的两行文案。
                    if (GameProgress.HasHighScore(3))
                        level3Text += zh
                            ? "\n最高分 " + GameProgress.GetHighScore(3)
                            : "\nBEST " + GameProgress.GetHighScore(3);
                    return level3Text;
                }
            default:
                return zh ? "关卡 " + levelIndex : "Level " + levelIndex;
        }
    }

    /// <summary>创建语言选择按钮（中文 / English）：点击切换全局语言，并高亮当前语言。</summary>
    private void CreateLanguageSelector()
    {
        langZhLabel = CreateLangButton("LangZh", new Vector2(660f, 480f), "中文", true);
        langEnLabel = CreateLangButton("LangEn", new Vector2(830f, 480f), "English", false);
        ApplyLanguage(); // 初始高亮当前语言
    }

    private Text CreateLangButton(string name, Vector2 pos, string label, bool isChinese)
    {
        RectTransform rt = CreateRect(name, root.transform, new Vector2(0.5f, 0.5f), pos, new Vector2(150f, 48f), new Vector2(0.5f, 0.5f));
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = UITheme.PanelSunk;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => Localization.SetLanguage(isChinese ? GameLanguage.Chinese : GameLanguage.English));

        Text t = CreateText("Label", rt, label, 24, UITheme.TextPrimary, TextAnchor.MiddleCenter, false);
        Stretch(t.rectTransform);
        return t;
    }

    /// <summary>按当前语言刷新主页全部文案（副标题 / 项目目的 / 作者 / 关卡 / 底部说明）。</summary>
    private void RefreshLanguageTexts()
    {
        bool zh = Localization.IsChinese;

        if (subtitleText != null)
            subtitleText.text = zh
                ? "三维直角坐标系冒险"
                : "A 3D Cartesian Coordinate Adventure";

        if (bodyText != null)
        {
            bodyText.text = zh
                ? "项 目 目 的\n\n《ABOVE THE VOID》致力于以游戏化的方式，激发学生的编程兴趣与数学思维。\n玩家在三维直角坐标系中自由飞行，亲手感受空间坐标、向量方向与几何变换：\n每一次躲避、瞄准与射击，都是对数学直觉的淬炼；每一次观察规律、调整策略，\n都在悄然建立逻辑推理与抽象思维的能力。\n让数学不再是纸上的符号，而是触手可及的世界——\n在探索与创造中，点燃对科学与技术的热爱。"
                : "P R O J E C T   G O A L\n\nABOVE THE VOID aims to spark students' interest in programming and mathematical thinking through gameplay.\nFly freely inside a 3D Cartesian coordinate system, and feel coordinates, vectors and geometry firsthand:\nevery dodge, aim and shot sharpens your mathematical intuition; every pattern you notice and every strategy\nyou adjust quietly builds logical reasoning and abstract thinking.\nMake math no longer symbols on paper, but a world within reach —\nignite your passion for science and technology through exploration and creation.";

            // v0.1.1：中文文案较长 → 缩小字号、增大行距，缓解拥挤
            bodyText.fontSize = zh ? 22 : bodyFontSize;
            bodyText.lineSpacing = zh ? 1.55f : 1.25f;
        }

        if (authorText != null)
            authorText.text = zh
                ? "作者  ·  " + AuthorName
                : "Author  ·  " + AuthorName;

        // 作者 GitHub 引导（中英双语；整行可点击，点击即打开作者 GitHub 主页）
        if (githubText != null)
            githubText.text = zh
                ? "GitHub  ·  " + AuthorGitHubHandle + "  （点击打开）"
                : "GitHub  ·  " + AuthorGitHubHandle + "  (click to open)";

        // 左下角常驻版本号：内容只取 GameVersion.Display，不在这里另写版本字符串
        if (versionText != null)
            versionText.text = zh
                ? "版本  " + GameVersion.Display
                : "Version  " + GameVersion.Display;

        if (levelTitleText != null)
            levelTitleText.text = zh
                ? "选择关卡"
                : "S E L E C T   L E V E L";

        // 关卡按钮文案 + 解锁/锁定状态一起刷新（未解锁附「锁定 / LOCKED」标注）
        RefreshLevelLocks();

        // v0.1.1：语言切换刷新文案时同步刷新字体（中文 → Noto Sans CJK SC，英文/数字 → JetBrains Mono）
        RefreshTextFonts();

        // GitHub 引导行：文案长度与字体都随语言变化，立刻按新宽度重排一次，
        // 图标始终紧贴文案左侧、整行保持居中（不等下一帧重排，避免切换瞬间图标跑位）
        if (githubRow != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(githubRow);

        ApplyLanguage();
    }

    /// <summary>v0.1.1：按当前文本内容刷新全部动态文本字体。</summary>
    private void RefreshTextFonts()
    {
        if (titleText != null)
            titleText.font = UIFontHelper.LoadForText(titleText.text, titleText.fontSize); // v0.1.1：标题刷新字体（英文 → JetBrains Mono）
        if (subtitleText != null)
            subtitleText.font = UIFontHelper.LoadForText(subtitleText.text, subtitleText.fontSize);
        if (bodyText != null)
            bodyText.font = UIFontHelper.LoadForText(bodyText.text, bodyText.fontSize);
        if (authorText != null)
            authorText.font = UIFontHelper.LoadForText(authorText.text, authorText.fontSize);
        if (githubText != null)
            githubText.font = UIFontHelper.LoadForText(githubText.text, githubText.fontSize);
        if (versionText != null)
            versionText.font = UIFontHelper.LoadForText(versionText.text, versionText.fontSize);
        if (levelTitleText != null)
            levelTitleText.font = UIFontHelper.LoadForText(levelTitleText.text, levelTitleText.fontSize);
        if (level0Label != null)
            level0Label.font = UIFontHelper.LoadForText(level0Label.text, level0Label.fontSize);
        if (level1Label != null)
            level1Label.font = UIFontHelper.LoadForText(level1Label.text, level1Label.fontSize);
        if (level2Label != null)
            level2Label.font = UIFontHelper.LoadForText(level2Label.text, level2Label.fontSize);
        if (level3Label != null)
            level3Label.font = UIFontHelper.LoadForText(level3Label.text, level3Label.fontSize);
    }

    /// <summary>高亮当前语言按钮（选中项用强调色，未选中灰白）。</summary>
    private void ApplyLanguage()
    {
        bool zh = Localization.IsChinese;
        if (langZhLabel != null)
            langZhLabel.color = zh ? accentColor : UITheme.TextMuted;
        if (langEnLabel != null)
            langEnLabel.color = !zh ? accentColor : UITheme.TextMuted;
    }

    // ---------------- 基础 UI 辅助 ----------------

    /// <summary>
    /// 把一行文本做成可点击链接：左键点击触发 onClick（用于作者 GitHub 引导），悬停时文字由
    /// 常规色提亮到强调色，移出还原 —— 样式沿用现有作者页文本，只是多一层交互反馈。
    /// CreateText 里统一把 raycastTarget 关掉（避免挡住按钮），这里的可点击行必须单独打开射线。
    /// </summary>
    private void MakeClickableLink(Text target, UnityEngine.Events.UnityAction onClick)
    {
        if (target == null)
            return;

        target.raycastTarget = true;

        Color normal = target.color;
        EventTrigger trigger = target.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        click.callback.AddListener(_ => onClick());
        trigger.triggers.Add(click);

        EventTrigger.Entry hoverIn = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        hoverIn.callback.AddListener(_ => target.color = accentColor);
        trigger.triggers.Add(hoverIn);

        EventTrigger.Entry hoverOut = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        hoverOut.callback.AddListener(_ => target.color = normal);
        trigger.triggers.Add(hoverOut);
    }

    /// <summary>
    /// 把图标做成可点击：点击行为与 GitHub 文案完全一致（打开作者主页），悬停时同样提亮到强调色。
    /// Image 与 Text 都要单独打开 raycastTarget 才有射线（主页背景不拦鼠标）。
    /// </summary>
    private void MakeClickableIcon(Image target, UnityEngine.Events.UnityAction onClick)
    {
        if (target == null)
            return;

        target.raycastTarget = true;

        Color normal = target.color;
        EventTrigger trigger = target.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        click.callback.AddListener(_ => onClick());
        trigger.triggers.Add(click);

        EventTrigger.Entry hoverIn = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        hoverIn.callback.AddListener(_ => target.color = accentColor);
        trigger.triggers.Add(hoverIn);

        EventTrigger.Entry hoverOut = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        hoverOut.callback.AddListener(_ => target.color = normal);
        trigger.triggers.Add(hoverOut);
    }

    /// <summary>
    /// 创建作者 GitHub 引导行左侧的官方 Octocat mark 图标。
    /// 尺寸由 LayoutElement 固定为边长 size（不随文案长度变化），由 HorizontalLayoutGroup 与文案垂直居中对齐；
    /// 资源优先取 Resources 里的官方 PNG（白色 glyph + 透明底，适配深色背景），取不到则退化为代码绘制的简易 mark。
    /// </summary>
    private Image CreateGitHubMarkIcon(Transform parent, float size)
    {
        GameObject go = new GameObject("AuthorGitHubIcon", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);

        Image img = go.AddComponent<Image>();
        img.raycastTarget = true;    // 图标与文案一样可点击（MakeClickableIcon 里再挂事件）
        img.preserveAspect = true;   // 保持 mark 原始比例，避免被拉伸变形
        img.color = UITheme.TextSub; // 与文案同色：资源本身是白色 glyph，染成同色文字色即可；悬停时提亮为强调色

        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minWidth = size;
        layout.minHeight = size;
        layout.preferredWidth = size;
        layout.preferredHeight = size;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        Sprite mark = LoadGitHubMarkSprite();
        img.sprite = mark != null ? mark : BuildFallbackGitHubMark();
        return img;
    }

    /// <summary>
    /// 加载官方 mark：先按 Sprite 资源取，其次把贴图手工包成 Sprite（应对贴图被导入成 Default 类型的情况）；
    /// 两者都失败返回 null，由调用方退化为代码绘制的简易 mark。
    /// </summary>
    private Sprite LoadGitHubMarkSprite()
    {
        if (githubMarkSprite != null)
            return githubMarkSprite;

        Sprite loaded = Resources.Load<Sprite>(GitHubMarkResourcePath);
        if (loaded != null)
        {
            githubMarkSprite = loaded;
            return loaded;
        }

        Texture2D tex = Resources.Load<Texture2D>(GitHubMarkResourcePath);
        if (tex != null)
        {
            try
            {
                githubMarkSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                return githubMarkSprite;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MainMenu] 官方 GitHub mark 贴图无法转成 Sprite，改为代码绘制：" + e.Message);
            }
        }

        Debug.LogWarning("[MainMenu] 未找到官方 GitHub mark（Resources/" + GitHubMarkResourcePath + "），改为代码绘制简易 mark。");
        return null;
    }

    /// <summary>
    /// 兜底 mark：官方 PNG 不可用时，用代码把简易 Octocat 头像（圆头 + 两只耳朵 + 两个透明眼睛）
    /// 画进一张 64x64 贴图，白色 glyph + 透明底，颜色与官方版本保持一致（同样吃 Image.color 染色）。
    /// </summary>
    private Sprite BuildFallbackGitHubMark()
    {
        const int size = 64; // 64 px 足够 24 px 显示尺寸，放大也不糊
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "GitHubMarkFallback";
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        Color clear = new Color(1f, 1f, 1f, 0f);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / size;
                float py = (y + 0.5f) / size;

                // 简易 Octocat mark = 上方圆头（dome）+ 左侧、右侧两条向下垂的「手臂」，中下方留空与官方 mark 一致
                bool dome = NormalizedDistance(px, py, 0.5f, 0.615f) <= 0.315f;
                bool armLeft = CapsuleDistance(px, py, 0.243f, 0.455f, 0.243f, 0.145f) <= 0.085f;
                bool armRight = CapsuleDistance(px, py, 0.757f, 0.455f, 0.757f, 0.145f) <= 0.085f;

                if (dome || armLeft || armRight)
                    pixels[y * size + x] = Color.white;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>归一化坐标下两点距离（兜底 mark 画圆用）。</summary>
    private static float NormalizedDistance(float x, float y, float centerX, float centerY)
    {
        float dx = x - centerX;
        float dy = y - centerY;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>归一化坐标下点到线段的距离（兜底 mark 画「手臂」这种胶囊形状用）。</summary>
    private static float CapsuleDistance(float x, float y, float ax, float ay, float bx, float by)
    {
        float abx = bx - ax;
        float aby = by - ay;
        float apx = x - ax;
        float apy = y - ay;
        float lengthSq = abx * abx + aby * aby;
        float t = lengthSq > 0f ? (apx * abx + apy * aby) / lengthSq : 0f;
        t = Mathf.Clamp01(t);
        float dx = apx - abx * t;
        float dy = apy - aby * t;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>打开作者 GitHub 主页（主页作者页 GitHub 引导行的点击回调）。</summary>
    private void OpenAuthorGitHub()
    {
        Application.OpenURL(AuthorGitHubUrl);
        Debug.Log("[MainMenu] 打开作者 GitHub：" + AuthorGitHubUrl);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
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

    private Text CreateText(string name, Transform parent, string content, int fontSizeValue, Color color, TextAnchor anchor, bool mono)
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
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void AddOutline(GameObject go, Color color, Vector2 distance)
    {
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
// touch refresh 2026-11-03

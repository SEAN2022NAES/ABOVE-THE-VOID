using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 主页（主菜单）：
///   - 展示 ABOVE THE VOID 主题（标题 / 副标题 / 三坐标轴色彩点缀）；
///   - 项目目的介绍：培养学生的编程兴趣与数学思维（扩写段落）；
///   - 作者署名 SEAN2022NAES；
///   - 关卡入口：关卡1 可点击进入游戏，关卡2 / 关卡3 占位（敬请期待）。
/// 进入关卡1后：隐藏主页、解除视角输入占用、通知生成器开始出块（保留进游戏前5秒缓冲）。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("配色")]
    [Tooltip("主页背景色（半透明，露出后方坐标系作为主题背景）")]
    public Color bgColor = new Color(0.04f, 0.05f, 0.09f, 0.88f);
    [Tooltip("强调色（标题 / 副标题 / 关卡按钮）")]
    public Color accentColor = new Color(0.30f, 1f, 1f, 1f);

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
    private Text levelTitleText;

    private Text level1Label;
    private Text level2Label;
    private Text level3Label;
    // 逐级解锁：关卡按钮 / 底图 / 文本引用（下标 = 关卡序号 - 1），用于解锁后即时刷新灰显与锁定状态
    private readonly Button[] levelButtons = new Button[3];
    private readonly Image[] levelButtonImages = new Image[3];
    private readonly Text[] levelButtonLabels = new Text[3];
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
        //   · 只有带章节拆分的分区（Level 1 - Part 2 语法课 / Level 2 - Part 1 API 课）才需要按章节定位，
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

        // 关卡 2 - 1 炮台 API 教学：统一走 LevelDirector 路由，保证表现开关 / 生成控制 / 教学启动与指令框一致
        if (levelIndex == 2)
        {
            string msg = LevelDirector.JumpTo(2, 1);
            Debug.Log("[MainMenu] 进入关卡2：" + msg);
            return;
        }

        if (levelIndex != 1 && levelIndex != 3)
            return;

        // 断点续玩：主页直接进入的两个分区（Level 1 - Part 1 引导教学、Level 3 - Part 1 完整玩法）没有章节拆分，
        // 位置固定从该分区开头开始；这里把「关卡 + 分区」写入存档（section = 0），
        // 既作为续玩点，也作为 /lock 清档范围与 Level 3 最高分结算所需的「当前关卡」会话标识。
        GameProgress.SaveResume(levelIndex, 1, 0);

        if (root != null)
            root.SetActive(false);

        // 从主页进入关卡：先把可能残留的「代码编辑器 C# 语法教学」重置（Part 2 进度清空、任务卡隐藏）
        CodeSyntaxTutorialController.End();

        if (playerController != null)
            playerController.SetExternalBlock(false); // 解除输入占用

        SetCursorLocked(true);

        // 鼠标左键手动激光：只有 Level 1 - Part 1 允许（主页关卡 1 即 Level 1 - Part 1 引导教学关），
        // Level 3 等其余关卡一律禁用（改由代码编辑器 / 炮台自动开火接管）。
        // 仅影响「左键 → FireLaser」手动链路，不影响 tower.FireAtPlane 等自动化调用。
        CommandConsoleController.SetManualLaserAllowed(levelIndex, 1);

        // 计分：进入关卡即重开一局分数（击毁红块加分 / 红块到基地扣分 / 蓝块可击毁但不计分）
        ScoreManager.ResetAll();

        // 生命值：进入关卡即重开一局生命（默认 5 点）
        LivesManager.ResetAll();

        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (levelIndex == 1)
        {
            // Level 1 由教学控制器接管：先不生成方块，逐步引导操作（无教学组件时回退旧逻辑）
            if (tutorial != null)
            {
                tutorial.StartTutorial(); // 内部会关闭 Score / print 荧幕与蓝块生成
            }
            else
            {
                LevelPresentation.ApplyLevel1(); // 无教学组件时同样保证教学关表现一致
                if (spawner != null)
                    spawner.BeginSpawning(); // 开始出块（保留进游戏前5秒缓冲）
            }
            Debug.Log("[MainMenu] 进入关卡1：主页隐藏，游戏开始。");
        }
        else
        {
            // Level 3 完整玩法：跳过教学，隐藏教学面板并恢复 E 键，重开会话（全部重新计时）后正常生成方块
            if (tutorial != null)
                tutorial.ExitToMenu();
            LevelPresentation.ApplyLevel3(); // 恢复 Score / print 荧幕显示与蓝色 BOMB 块生成
            if (spawner != null)
                spawner.ResetSession();    // 重开会话：清零消灭计数与方块编号
            // 主页进入完整玩法关同样走统一开场停顿：先 Cancel 旧计时再重新停顿 5 秒（期间不出块、不显示炮台），
            // 与控制台 /level 3 1 跳转路径保持一致，避免两条入口行为不一致导致「一进场就出块」
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

        // 终止教学（隐藏面板 + 清场）
        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (tutorial != null)
            tutorial.ExitToMenu();

        // 终止代码编辑器内的 C# 语法教学（隐藏任务卡与提示条；下次进入 Part 2 时进度重置）
        CodeSyntaxTutorialController.End();

        // 回到主页：不在任何关卡内，手动激光（鼠标左键）一律禁用
        CommandConsoleController.SetManualLaserAllowed(0, 0);

        // 回到主页：终止仍在进行的开场停顿（否则停顿到期会把生成器重新打开，主页上又开始出块）
        LevelOpeningGate.Cancel();

        // 主页不刷方块：暂停生成并清空场上
        if (spawner != null)
        {
            spawner.SuspendSpawning();
            spawner.ClearAllBlocks();
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
        titleText = CreateText("Title", root.transform, "ABOVE THE VOID", titleFontSize, Color.white, TextAnchor.MiddleCenter, true);
        titleText.fontStyle = FontStyle.Bold; // v0.1.1：标题加粗微调
        SetRect(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(1600f, 120f));
        AddOutline(titleText.gameObject, new Color(0f, 0f, 0f, 0.9f), new Vector2(4f, -4f));

        // ---- 副标题（主题展示）----
        subtitleText = CreateText("Subtitle", root.transform, "", subtitleFontSize, accentColor, TextAnchor.MiddleCenter, false);
        SetRect(subtitleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(1700f, 60f));

        // ---- 三坐标轴色彩点缀：X 蓝 / Y 红 / Z 绿 ----
        CreateAxisBadge("AxisX", new Vector2(0f, 168f), -150f, "X", new Color(0.30f, 0.55f, 1f));
        CreateAxisBadge("AxisY", new Vector2(0f, 168f), 0f, "Y", new Color(1f, 0.35f, 0.30f));
        CreateAxisBadge("AxisZ", new Vector2(0f, 168f), 150f, "Z", new Color(0.30f, 1f, 0.40f));

        // ---- 分隔线 ----
        RectTransform divider = CreateRect("Divider", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(900f, 2f), new Vector2(0.5f, 0.5f));
        Image dividerImg = divider.gameObject.AddComponent<Image>();
        dividerImg.color = new Color(1f, 1f, 1f, 0.35f);

        // ---- 项目目的（扩写：编程兴趣 + 数学思维；语言切换后刷新）----
        bodyText = CreateText("Goal", root.transform, "", bodyFontSize, new Color(0.92f, 0.94f, 0.98f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1550f, 360f));
        bodyText.lineSpacing = 1.25f;

        // ---- 作者 ----
        authorText = CreateText("Author", root.transform, "", authorFontSize, new Color(1f, 0.85f, 0.35f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(authorText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -235f), new Vector2(1200f, 48f));

        // ---- 语言选择：中文 / English ----
        CreateLanguageSelector();

        // ---- 关卡区 ----
        levelTitleText = CreateText("LevelTitle", root.transform, "", levelTitleFontSize, new Color(0.75f, 0.8f, 0.9f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(levelTitleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -304f), new Vector2(1200f, 44f));

        CreateLevelButton("Level1", new Vector2(-280f, -382f), 1, out level1Label);
        CreateLevelButton("Level2", new Vector2(0f, -382f), 2, out level2Label);
        CreateLevelButton("Level3", new Vector2(280f, -382f), 3, out level3Label);

        // 初始填充当前语言文案
        RefreshLanguageTexts();
    }

    /// <summary>创建三坐标轴彩色徽标（X/Y/Z 三色点缀）。</summary>
    private void CreateAxisBadge(string name, Vector2 center, float offsetX, string label, Color color)
    {
        Text t = CreateText(name, root.transform, label, 44, color, TextAnchor.MiddleCenter, true);
        SetRect(t.rectTransform, new Vector2(0.5f, 0.5f), center + new Vector2(offsetX, 0f), new Vector2(80f, 70f));
        AddOutline(t.gameObject, new Color(0f, 0f, 0f, 0.9f), new Vector2(2f, -2f));
    }

    /// <summary>
    /// 创建关卡按钮（按 LevelProgress 判断是否已解锁：未解锁自动灰显 + 不可点击，并在文本末尾标注「锁定」）。
    /// 按钮 / 底图 / 文本的引用按下标（关卡序号 - 1）保存，供解锁后 RefreshLevelLocks 即时刷新。
    /// </summary>
    private void CreateLevelButton(string name, Vector2 pos, int levelIndex, out Text labelText)
    {
        bool unlocked = LevelProgress.IsLevelUnlocked(levelIndex);

        RectTransform rt = CreateRect(name, root.transform, new Vector2(0.5f, 0.5f), pos, new Vector2(240f, 92f), new Vector2(0.5f, 0.5f));
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = unlocked
            ? new Color(0.20f, 0.62f, 0.72f, 0.55f)
            : new Color(0.16f, 0.18f, 0.24f, 0.60f);

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = unlocked;
        if (unlocked)
        {
            int captured = levelIndex;
            btn.onClick.AddListener(() => StartLevel(captured));
        }

        labelText = CreateText("Label", rt, "", buttonFontSize, unlocked ? Color.white : new Color(0.55f, 0.58f, 0.65f, 1f), TextAnchor.MiddleCenter, false);
        Stretch(labelText.rectTransform);
        labelText.lineSpacing = 0.9f;

        int index = levelIndex - 1;
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
            int levelIndex = i + 1;
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
                    ? new Color(0.20f, 0.62f, 0.72f, 0.55f)
                    : new Color(0.16f, 0.18f, 0.24f, 0.60f);
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
                label.color = unlocked ? Color.white : new Color(0.55f, 0.58f, 0.65f, 1f);
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
            case 1:
                return zh ? "关卡 1" : "Level 1";
            case 2:
                return zh ? "关卡 2\nAPI 教学" : "Level 2\nAPI Tutorial";
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
        img.color = new Color(0.16f, 0.22f, 0.30f, 0.60f);

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => Localization.SetLanguage(isChinese ? GameLanguage.Chinese : GameLanguage.English));

        Text t = CreateText("Label", rt, label, 24, new Color(0.85f, 0.92f, 1f, 1f), TextAnchor.MiddleCenter, false);
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
                ? "作者  ·  SEAN2022NAES"
                : "Author  ·  SEAN2022NAES";

        if (levelTitleText != null)
            levelTitleText.text = zh
                ? "选择关卡"
                : "S E L E C T   L E V E L";

        // 关卡按钮文案 + 解锁/锁定状态一起刷新（未解锁附「锁定 / LOCKED」标注）
        RefreshLevelLocks();

        // v0.1.1：语言切换刷新文案时同步刷新字体（中文 → 微软雅黑，英文/数字 → Consolas）
        RefreshTextFonts();

        ApplyLanguage();
    }

    /// <summary>v0.1.1：按当前文本内容刷新全部动态文本字体。</summary>
    private void RefreshTextFonts()
    {
        if (titleText != null)
            titleText.font = UIFontHelper.LoadForText(titleText.text, titleText.fontSize); // v0.1.1：标题刷新字体（英文 → Consolas）
        if (subtitleText != null)
            subtitleText.font = UIFontHelper.LoadForText(subtitleText.text, subtitleText.fontSize);
        if (bodyText != null)
            bodyText.font = UIFontHelper.LoadForText(bodyText.text, bodyText.fontSize);
        if (authorText != null)
            authorText.font = UIFontHelper.LoadForText(authorText.text, authorText.fontSize);
        if (levelTitleText != null)
            levelTitleText.font = UIFontHelper.LoadForText(levelTitleText.text, levelTitleText.fontSize);
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
            langZhLabel.color = zh ? accentColor : new Color(0.75f, 0.8f, 0.85f, 1f);
        if (langEnLabel != null)
            langEnLabel.color = !zh ? accentColor : new Color(0.75f, 0.8f, 0.85f, 1f);
    }

    // ---------------- 基础 UI 辅助 ----------------

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
        text.font = UIFontHelper.LoadForText(content, fontSizeValue); // v0.1.1：按内容自动选字体（中文微软雅黑 / 英文 Consolas）
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

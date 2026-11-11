using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关卡 1 - 第 1 部分 引导教学（Tutorial）：
///   - 进入关卡先不生成方块（由本控制器接管生成时机）；
///   - 逐步引导：按 Q 自由视角 → WASD / Space / C 移动 → 左键发射激光；
///   - 解释方块碰到坐标平面（两根坐标轴夹起来的面：X=0 / Y=0 / Z=0）即消失；
///   - 用激光消灭 5 个方块后，预告「自动化游玩」（只引导体验，不展示代码）。
/// 纯代码构建顶部提示面板；文案随 Localization 中 / 英切换。
/// </summary>
public class TutorialController : MonoBehaviour
{
    public static TutorialController Instance { get; private set; }

    /// <summary>Level 1 - Part 1 教学是否已启动（启动期间禁用 E 键时间冻结）。</summary>
    public bool Started { get; private set; }

    [Header("UI")]
    public int titleFontSize = 32;
    public int bodyFontSize = 30; // 指导框字号放大，远距离也能看清
    public Color panelColor = UITheme.WithAlpha(UITheme.Void, 0f); // v0.2.1：完全透明背景（无底色、不遮挡 3D 场景）
    public Color titleColor = UITheme.Accent;
    public Color bodyColor = UITheme.TextPrimary;

    [Header("教学参数")]
    [Tooltip("判定学会移动：相机离开原点的距离阈值")]
    public float moveAwayDistance = 0.6f;
    [Tooltip("移动教学：每个按键需长按的秒数")]
    public float keyHoldSeconds = 0.5f;
    [Tooltip("需要消灭的方块数量")]
    public int targetKills = 5;
    [Tooltip("欢迎页停留秒数（真实时间）")]
    public float welcomeHoldSeconds = 2.8f;
    [Tooltip("引导教学完成后，自动顺接进入 Level 1 - Part 2 前在结语页停留的秒数（真实时间，可调）")]
    public float completionHoldSeconds = 2.2f;
    [Tooltip("进入 Level 1 - Part 2 前的淡出到全黑时长（秒，真实时间，可调；0 = 无淡出）")]
    public float transitionFadeOutSeconds = 0.35f;
    [Tooltip("进入 Level 1 - Part 2 后的淡入时长（秒，真实时间，可调；0 = 无淡入）")]
    public float transitionFadeInSeconds = 0.5f;

    private enum Step
    {
        Welcome,        // 欢迎 / 总览
        PressQ,         // 按 Q 进入自由视角
        Move,           // WASD / Space / C 移动
        FireLaser,      // 左键发射激光
        PlaneKill,      // 解释坐标平面消失 + 消灭 5 个方块
        CSharpIntro     // 引出 C# 自动化激光
    }

    private Step currentStep;
    private bool active;
    private float welcomeTimer;
    private bool spawnStarted;
    // 结语页 → 自动顺接 Level 1 - Part 2 的过渡计时与一次性触发标记（防重复触发）
    private float nextLessonTimer;
    private bool nextLessonTriggered;

    // 移动教学：六键逐个长按充能
    private static readonly KeyCode[] MoveKeyOrder = new KeyCode[]
    {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Space, KeyCode.C
    };
    private readonly float[] keyCharge = new float[6];
    private readonly bool[] keyDone = new bool[6];
    private Image[] keyFill;
    private Image[] keyBlue;
    private Text[] keyLabel;
    private Image[] keyBg;
    private RectTransform moveKeyRowRt;

    private GameObject panelRoot;
    private Text stepText;
    private Text bodyText;

    private FlyingBlockSpawner spawner;
    private PlayerCameraController player;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        spawner = FlyingBlockSpawner.Instance;
        player = FindObjectOfType<PlayerCameraController>();

        BuildUI();
        panelRoot.SetActive(false);

        Localization.OnLanguageChanged += RefreshText;
        CommandConsoleController.OnLaserFired += HandleLaserFired;
    }

    private void OnDestroy()
    {
        Localization.OnLanguageChanged -= RefreshText;
        CommandConsoleController.OnLaserFired -= HandleLaserFired;
        if (Instance == this)
            Instance = null;
    }

    /// <summary>进入 Level 1 - Part 1 教学：清场、暂停生成、重置玩家，从欢迎页开始引导。可重复调用以重学。</summary>
    public void StartTutorial()
    {
        MainMenuController menu = FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.HideMenu();

        if (player != null)
            player.ResetToLevelStart();

        if (spawner != null)
        {
            spawner.ClearAllBlocks();
            spawner.SuspendSpawning(); // 教学前期先不生成方块
            spawner.ResetSession(); // 重置本局会话：消灭计数清零、方块编号从 1 开始
        }

        TimeHintController timeHint = FindObjectOfType<TimeHintController>();
        if (timeHint != null)
            timeHint.SetVisible(false);

        // Level 1 教学关不显示炮台实体，仅保留原点炮台功能（Level 3 由 LevelDirector 重新显示）
        CodeTower tower = FindObjectOfType<CodeTower>();
        if (tower != null)
            tower.SetVisualVisible(false);

        // Level 1 教学关表现：不显示 Score 分数 UI、不显示 print 输出面板、不生成蓝色 BOMB 块（只出红块）
        LevelPresentation.ApplyLevel1();

        // 手动激光（鼠标左键）只在 Level 1 - Part 1 存在：进入本关教学即启用（其他关卡由 LevelDirector 关闭）
        CommandConsoleController.SetManualLaserAllowed(1, 1);

        spawnStarted = false;
        nextLessonTimer = 0f;
        nextLessonTriggered = false; // 重学本关时重置顺接标记，保证下一次仍能自动进入 Part 2
        active = true;
        Started = true; // 教学进行中：PlayerCameraController 据此禁用 E 键时间冻结
        panelRoot.SetActive(true);
        SetStep(Step.Welcome);
        Debug.Log("[Tutorial] Level 1 - Part 1 引导教学开始（先不生成方块，等待玩家完成操作引导）。");
    }

    private void Update()
    {
        if (!active)
            return;

        switch (currentStep)
        {
            case Step.Welcome:
                welcomeTimer += Time.unscaledDeltaTime;
                if (welcomeTimer >= welcomeHoldSeconds)
                    Advance();
                break;
            case Step.PressQ:
                if (player != null && player.Mode == PlayerCameraController.GameMode.Spirit)
                    Advance();
                break;
            case Step.Move:
                UpdateMoveKeyCharge();
                if (AllMoveKeysDone())
                    Advance();
                break;
            case Step.PlaneKill:
                RefreshPlaneKillProgress();
                if (spawner != null && spawner.PlayerKills >= targetKills)
                    Advance();
                break;
            case Step.CSharpIntro:
                // 结语页（5/5 完成提示）先停留 completionHoldSeconds，再用黑幕淡出 → 跳转 → 淡入，丝滑进入 Level 1 - Part 2
                if (nextLessonTriggered)
                    break;
                nextLessonTimer += Time.unscaledDeltaTime;
                if (nextLessonTimer >= completionHoldSeconds)
                {
                    nextLessonTriggered = true; // 一次性触发：之后即使再次进入本分支也不会重复跳转
                    GoToNextLesson();
                }
                break;
        }
    }

    private void Advance()
    {
        switch (currentStep)
        {
            case Step.Welcome:
                SetStep(Step.PressQ);
                break;
            case Step.PressQ:
                SetStep(Step.Move);
                break;
            case Step.Move:
                SetStep(Step.FireLaser);
                break;
            case Step.FireLaser:
                SetStep(Step.PlaneKill);
                BeginSpawningForTutorial(); // 开始生成方块：观察平面消失 + 练习消灭
                break;
            case Step.PlaneKill:
                SetStep(Step.CSharpIntro);
                break;
            case Step.CSharpIntro:
                // 已经是最后一页：保持 active，交由 Update 中的过渡计时在停留结束后自动顺接进入 Level 1 - Part 2
                break;
        }
    }

    /// <summary>
    /// 引导教学完成后自动顺接进入 Level 1 - Part 2（C# 基础语法教学）。
    /// 流程：先在结语页停留（5/5 完成提示）→ 黑幕淡出到全黑 → 在全黑帧执行 LevelDirector.JumpTo(1, 2)
    /// （完成清场、表现切换、语法教学从头开始）→ 黑幕淡入，避免关卡切换出现生硬瞬切。
    /// 跳转前先收尾本关教学状态（面板隐藏、Started=false 解除「教学进行中禁止打开代码编辑器」互斥）。
    /// </summary>
    private void GoToNextLesson()
    {
        // 先停掉本关引导教学的逐帧判定，避免过渡期间继续刷新进度 / 再次走到本方法
        active = false;
        Started = false;

        // 淡出 → 全黑瞬间跳转 → 淡入；三个时长均由 inspector 参数控制
        ScreenFade.PlayTransition(transitionFadeOutSeconds, transitionFadeInSeconds, JumpToNextLesson);
    }

    /// <summary>黑幕全黑的瞬间执行关卡切换：进入 Level 1 - Part 2。</summary>
    private void JumpToNextLesson()
    {
        string result = LevelDirector.JumpTo(1, 2);
        if (string.IsNullOrEmpty(result) || result.Contains("尚未开放"))
            Debug.LogWarning("[Tutorial] 自动顺接 Level 1 - Part 2 失败：" + result);
        else
            Debug.Log("[Tutorial] 引导教学完成，已自动顺接进入 Level 1 - Part 2（" + result + "）");
    }

    // ---------------- 移动教学：六键长按充能 ----------------

    private void ResetMoveKeys()
    {
        for (int i = 0; i < MoveKeyOrder.Length; i++)
        {
            keyCharge[i] = 0f;
            keyDone[i] = false;
        }
        RefreshKeyChargeUI();
    }

    private void UpdateMoveKeyCharge()
    {
        for (int i = 0; i < MoveKeyOrder.Length; i++)
        {
            if (keyDone[i])
                continue;

            if (Input.GetKey(MoveKeyOrder[i]))
            {
                keyCharge[i] += Time.unscaledDeltaTime;
                if (keyCharge[i] >= keyHoldSeconds)
                {
                    keyCharge[i] = keyHoldSeconds;
                    keyDone[i] = true;
                }
            }
            else
            {
                // 松开即重新充能：必须持续长按够 1 秒
                keyCharge[i] = 0f;
            }
        }
        RefreshKeyChargeUI();
    }

    private bool AllMoveKeysDone()
    {
        for (int i = 0; i < MoveKeyOrder.Length; i++)
            if (!keyDone[i])
                return false;
        return true;
    }

    private string KeyLabel(int index)
    {
        return MoveKeyOrder[index] == KeyCode.Space ? "Space" : MoveKeyOrder[index].ToString();
    }

    private void RefreshKeyChargeUI()
    {
        if (keyFill == null)
            return;

        Color chargeColor = UITheme.Value;   // 充能中：绿色从左往右填充
        Color doneColor = UITheme.ValueBright;         // 完成：亮绿

        for (int i = 0; i < MoveKeyOrder.Length; i++)
        {
            float t = Mathf.Clamp01(keyCharge[i] / Mathf.Max(0.0001f, keyHoldSeconds));
            keyFill[i].fillAmount = t;

            // 蓝色底层每帧自愈：强制满条蓝色并重建网格，确保 Blue 层必然可见
            if (keyBlue != null && keyBlue[i] != null)
            {
                keyBlue[i].color = UITheme.WithAlpha(UITheme.Accent, 0.90f);
                keyBlue[i].SetVerticesDirty();
            }
            if (keyDone[i])
            {
                keyFill[i].color = doneColor;
                keyBg[i].color = UITheme.ValueDim; // v0.1.1：完成态键格亮绿低透明底（更亮）
                Outline outline = keyBg[i].GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = UITheme.WithAlpha(UITheme.ValueBright, 0.55f); // v0.1.1：完成态描边转亮绿
                keyLabel[i].text = KeyLabel(i) + " [OK]";
                keyLabel[i].color = doneColor;
            }
            else
            {
                keyFill[i].color = chargeColor;
                keyBg[i].color = UITheme.WithAlpha(UITheme.Accent, 0.06f);
                Outline outline = keyBg[i].GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = UITheme.Hairline;
                keyLabel[i].text = KeyLabel(i);
                keyLabel[i].color = UITheme.TextSub;
            }
        }
    }

    private void BeginSpawningForTutorial()
    {
        if (spawner == null || spawnStarted)
            return;
        spawnStarted = true;
        spawner.BeginSpawning(0.6f); // 教学：约 0.6 秒后开始出块（缩短开局缓冲）
    }

    private void HandleLaserFired()
    {
        if (active && currentStep == Step.FireLaser)
            Advance();
    }

    /// <summary>返回主页：终止教学、隐藏面板、清场并暂停生成（可再次 StartTutorial 重新教学）。</summary>
    public void ExitToMenu()
    {
        active = false;
        Started = false; // 教学结束：恢复 E 键时间冻结
        spawnStarted = false;
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (spawner != null)
        {
            spawner.SuspendSpawning();
            spawner.ClearAllBlocks();
            spawner.ResetPlayerKills();
        }
        Debug.Log("[Tutorial] 教学已退出（返回主页）。");
    }

    // ---------------- 文本 / 刷新 ----------------

    private void SetStep(Step step)
    {
        currentStep = step;
        RefreshText();
        if (step == Step.Welcome)
            welcomeTimer = 0f;
        // 进入结语页：重置自动顺接计时（此时才允许开始计算停留时间）
        if (step == Step.CSharpIntro)
        {
            nextLessonTimer = 0f;
            nextLessonTriggered = false;
        }

        // 充能行仅在 Move 步骤可见
        if (moveKeyRowRt != null)
        {
            bool showKeys = (step == Step.Move);
            moveKeyRowRt.gameObject.SetActive(showKeys);
            if (showKeys)
                ResetMoveKeys();
        }
    }

    private void RefreshText()
    {
        if (panelRoot == null || !panelRoot.activeSelf)
            return;

        bool zh = Localization.IsChinese;
        // 结语页（打满目标数量后）标题直接显示「5/5 完成」，让玩家明确知道本部分已达成
        if (currentStep == Step.CSharpIntro)
        {
            stepText.text = zh
                ? "5/5 完成 · 引导教学（关卡 1 · 第 1 部分）"
                : "5/5 Complete · Tutorial (Level 1 · Part 1)";
        }
        else
        {
            stepText.text = zh ? "引导教学（关卡 1 · 第 1 部分）" : "Tutorial (Level 1 · Part 1)";
        }

        switch (currentStep)
        {
            case Step.Welcome:
                bodyText.text = zh
                    ? "欢迎来到 ABOVE THE VOID 教学关！\n\n本关带你认识自由视角、移动、激光射击，\n以及「方块碰到坐标平面就会消失」的机制。"
                    : "Welcome to the ABOVE THE VOID tutorial!\n\nHere you will learn free view, movement, laser firing,\nand the rule: blocks vanish when they touch a coordinate plane.";
                break;
            case Step.PressQ:
                bodyText.text = zh
                    ? "按 Q 键进入自由视角（灵魂出窍）\n\n离开原点后，你就能自由观察整个三维直角坐标系了。\n（再按 Q 返回原点）"
                    : "Press Q to enter free view (Spirit Mode)\n\nOnce you leave the origin, you can freely observe the 3D Cartesian coordinate space.\n(Press Q again to return to the origin.)";
                break;
            case Step.Move:
                bodyText.text = zh
                    ? "自由移动需要长按每个键 1 秒充能：\n\nW / S —— 前后横移（沿 X 轴）\nA / D —— 左右横移（沿 Y 轴）\nSpace / C —— 上升 / 下降（沿 Z 轴）\n\n把下方 6 个充能条全部充满，即可继续！"
                    : "Hold each movement key for 1 second:\n\nW / S — forward / backward (X axis)\nA / D — left / right (Y axis)\nSpace / C — up / down (Z axis)\n\nFill all 6 charge bars below to continue!";
                break;
            case Step.FireLaser:
                bodyText.text = zh
                    ? "鼠标左键发射激光！\n\n每命中一次吃掉方块 1 层厚度；层数 = 它自身最大 HP（1~6 层）。\n累计吃满自身 HP 层，方块即消灭。\n\n屏幕底部「HP: 剩余/最大」实时显示（6/6 → 5/6 → … → 1/6）。\n\n先空射一枪感受一下（暂未生成方块）。"
                    : "Left-click to fire your laser!\n\nEach hit eats 1 layer; layers = that block's own max HP (1~6).\nA block dies once its own HP is fully eaten.\n\nBottom HUD shows remaining HP (6/6 -> 5/6 -> ... -> 1/6).\n\nFire once to feel it (no blocks yet, dry fire is fine).";
                break;
            case Step.PlaneKill:
                RefreshPlaneKillProgress();
                break;
            case Step.CSharpIntro:
                bodyText.text = zh
                    ? "5/5 完成！你已经消灭了 " + targetKills + " 个方块。\n\n接下来，我们将走入「自动化游玩」——\n让程序自己控制激光去打方块！\n\n即将进入下一课：C# 基础语法\n（画面将淡出后自动切换，代码编辑器内教学无需任何输入…）"
                    : "5/5 complete! You destroyed " + targetKills + " blocks.\n\nNext, we move into automated play —\nletting the program control the laser by itself!\n\nEntering the next lesson: C# basics\n(screen fades out and switches automatically; no input needed in the editor tutorial…)";
                break;
        }

        // v0.1.1：语言切换刷新文案时同步刷新字体（中文 → Noto Sans CJK SC，英文/数字 → JetBrains Mono）
        if (stepText != null)
            stepText.font = UIFontHelper.LoadForText(stepText.text, stepText.fontSize);
        if (bodyText != null)
            bodyText.font = UIFontHelper.LoadForText(bodyText.text, bodyText.fontSize);

        // v0.2.0：按正文实际高度自适应面板高度（避免长文挤压 / 短文留白）
        ApplyPanelHeight();
    }

    /// <summary>v0.2.0：按正文期望高度动态调整面板高度（锚点顶部固定，向下生长）。</summary>
    private void ApplyPanelHeight()
    {
        if (panelRoot == null || bodyText == null)
            return;

        RectTransform panelRt = panelRoot.GetComponent<RectTransform>();
        if (panelRt == null)
            return;

        // 正文在当前宽度（面板宽 1200 - 左右边距 80）下换行后的期望高度
        float bodyH = bodyText.preferredHeight;
        if (bodyH <= 1f)
            bodyH = 120f; // 兜底：空文本时按最小正文高度处理

        // 底部预留区：Move 步骤显示六键充能行约 150，其余步骤仅留少量边距约 48
        float bottomReserve = (currentStep == Step.Move) ? 150f : 48f;

        // 面板高度 = 标题区 + 正文期望高度 + 底部预留区 + 上下边距
        const float titleZone = 84f;    // 标题(56) + 顶部间距 + 标题区高度
        const float topMargin = 24f;    // 标题区到正文起点
        const float bottomMargin = 44f; // 面板底部留白
        float target = titleZone + topMargin + bodyH + bottomReserve + bottomMargin;
        target = Mathf.Clamp(target, 380f, 660f); // 字号放大后允许面板更高，避免正文被裁

        panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, target);

        // 正文区底部预留随步骤调整（Move 给充能行留 128，其余收窄为 40，正文区随面板自然伸缩）
        RectTransform bodyRt = bodyText.rectTransform;
        bodyRt.offsetMin = new Vector2(bodyRt.offsetMin.x, bottomReserve);
    }

    private void RefreshPlaneKillProgress()
    {
        if (!active || currentStep != Step.PlaneKill)
            return;
        int kills = spawner != null ? spawner.PlayerKills : 0;
        bool zh = Localization.IsChinese;
        bodyText.text = zh
            ? "注意观察！\n\n每个长方体都沿着坐标轴飞向原点，\n当它的表面碰到任意坐标平面（由两根坐标轴夹起来的面：X=0 / Y=0 / Z=0）时，它就会消失。\n\n现在用激光消灭 " + targetKills + " 个方块（每命中 1 次吃掉 1 层，层数 = 该方块最大 HP）：当前 " + kills + "/" + targetKills
            : "Watch closely!\n\nEach block flies along an axis toward the origin. When its surface touches\nany coordinate plane (the plane spanned by two axes: X=0 / Y=0 / Z=0), it vanishes.\n\nNow destroy " + targetKills + " blocks with your laser (1 hit eats 1 layer; layers = that block's max HP): " + kills + "/" + targetKills;
    }

    // ---------------- UI 构建 ----------------

    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // 高于 HUD、低于主页(100)

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform panelRt = CreateRect("TutorialPanel", canvas.transform, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1400f, 420f), new Vector2(0.5f, 1f));
        panelRoot = panelRt.gameObject;
        Image panelImg = panelRoot.AddComponent<Image>();
        panelImg.color = panelColor; // v0.2.1：alpha=0 完全透明，仅作文本容器，不遮挡 3D 场景
        panelImg.raycastTarget = false; // v0.2.1：透明容器不拦截射线

        stepText = CreateText("Step", panelRt, "", titleFontSize, titleColor, TextAnchor.MiddleLeft);
        stepText.fontStyle = FontStyle.Bold; // v0.1.1：标题加粗
        RectTransform stepRt = stepText.rectTransform;
        stepRt.anchorMin = new Vector2(0f, 1f);
        stepRt.anchorMax = new Vector2(1f, 1f);
        stepRt.pivot = new Vector2(0.5f, 1f);
        stepRt.anchoredPosition = new Vector2(0f, -20f);
        stepRt.sizeDelta = new Vector2(-70f, 56f);

        bodyText = CreateText("Body", panelRt, "", bodyFontSize, bodyColor, TextAnchor.MiddleCenter);
        RectTransform bodyRt = bodyText.rectTransform;
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(48f, 150f); // 底部留出六键充能行空间
        bodyRt.offsetMax = new Vector2(-48f, -66f);
        bodyText.lineSpacing = 1.4f; // v0.2.1：增大行距，增加呼吸感
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;

        BuildKeyChargeRow();
    }

    private void BuildKeyChargeRow()
    {
        RectTransform rowRt = CreateRect("MoveKeyRow", panelRoot.transform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1440f, 72f), new Vector2(0.5f, 0f));
        moveKeyRowRt = rowRt;

        keyFill = new Image[MoveKeyOrder.Length];
        keyBlue = new Image[MoveKeyOrder.Length];
        keyLabel = new Text[MoveKeyOrder.Length];
        keyBg = new Image[MoveKeyOrder.Length];

        float slotW = 210f;
        float gap = 20f;
        float totalW = slotW * MoveKeyOrder.Length + gap * (MoveKeyOrder.Length - 1);
        float startX = -totalW * 0.5f + slotW * 0.5f;

        for (int i = 0; i < MoveKeyOrder.Length; i++)
        {
            float x = startX + i * (slotW + gap);

            // 键格背景
            RectTransform bgRt = CreateRect("KeySlot_" + i, rowRt, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(slotW, 66f), new Vector2(0.5f, 0.5f));
            Image bg = bgRt.gameObject.AddComponent<Image>();
            bg.sprite = WhiteSprite();                      // 显式 sprite：无 sprite 的 Image 渲染行为不可靠
            bg.type = Image.Type.Simple;
            bg.color = UITheme.WithAlpha(UITheme.Accent, 0.06f);
            keyBg[i] = bg;

            // v0.1.1：键格细描边（未完成 = 半透明白，完成 = 亮绿，由 RefreshKeyChargeUI 更新）
            Outline bgOutline = bgRt.gameObject.AddComponent<Outline>();
            bgOutline.effectColor = UITheme.Hairline;
            bgOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // 充能条底层：蓝色满条（Simple 满矩形，无 sprite 依赖也必然渲染；固定尺寸，非 stretch）
            RectTransform blueRt = CreateRect("Blue_" + i, bgRt, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(200f, 30f), new Vector2(0.5f, 0f));
            Image blue = blueRt.gameObject.AddComponent<Image>();
            blue.sprite = WhiteSprite();
            blue.type = Image.Type.Simple;                   // 满条蓝底无需 Filled，Simple 是最可靠的渲染路径
            blue.color = UITheme.WithAlpha(UITheme.Accent, 0.90f); // 蓝色
            blue.raycastTarget = false;
            keyBlue[i] = blue;

            // 充能条上层：绿色从左往右覆盖蓝色，充满后整条变绿
            RectTransform fillRt = CreateRect("Fill_" + i, bgRt, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(200f, 30f), new Vector2(0.5f, 0f));
            Image fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = WhiteSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            fill.color = UITheme.Value; // 绿色覆盖层
            fill.raycastTarget = false;
            keyFill[i] = fill;

            // 键名
            Text label = CreateText("Key_" + i, bgRt, KeyLabel(i), 24, UITheme.TextSub, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(0f, 30f);
            labelRt.offsetMax = new Vector2(0f, 0f);
            keyLabel[i] = label;
        }

        for (int i = 0; i < MoveKeyOrder.Length; i++)
        {
            Debug.Log("[Tutorial] KeyChargeV3 slot " + i
                + " blue rect=" + keyBlue[i].rectTransform.rect + " color=" + keyBlue[i].color
                + " | fill rect=" + keyFill[i].rectTransform.rect + " color=" + keyFill[i].color
                + " fillAmount=" + keyFill[i].fillAmount);
        }

        // 默认隐藏，进入 Move 步骤时再显示
        rowRt.gameObject.SetActive(false);
        Debug.Log("[Tutorial] KeyChargeV3: 6 slots built (blue base + green fill). keyHoldSeconds=" + keyHoldSeconds);
    }

    // 生成 1x1 白色 Sprite，作为无图集 UI 的可靠渲染源（无 sprite 的 Image 渲染不可靠）
    private static Sprite WhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        sp.hideFlags = HideFlags.HideAndDontSave;
        return sp;
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
        text.font = UIFontHelper.LoadForText(content, fontSizeValue); // v0.1.1：按内容自动选字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }
}

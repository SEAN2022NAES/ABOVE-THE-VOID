using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 生命值系统（Lives）：
///   - 初始生命 5 点（maxLives，可在 Inspector 调整）；
///   - 击毁一块蓝色 BOMB 方块扣 1 点（livesLostPerBombKill，可调；设为 0 表示击毁蓝块不掉血）；
///     漏掉红色 ATTACK 块仍按原规则只扣分（ScoreManager.NotifyAttackLeaked），不扣生命；
///   - HUD 位于右上角 SCORE 得分下方（同侧、同风格：白字 + 描边 + 生命方块 + 减血提示）；
///   - 生命归 0 立即判负：暂停时间、清场并停止生成、弹出 GAME OVER 面板，可直接重新开始或返回主菜单；
///   - 教学关（Level 1 / Level 2）由 LevelPresentation 关闭本系统（不显示 HUD、不掉血），
///     不影响原有的「击毁蓝块零分零罚」规则；
///   - 与现有暂停 / 结算机制兼容：判负时强制退出暂停与代码编辑器，避免状态残留。
/// </summary>
public class LivesManager : MonoBehaviour
{
    /// <summary>全局单例（不存在时为 null，所有静态入口均做空判）。</summary>
    public static LivesManager Instance { get; private set; }

    [Header("生命值参数")]
    [Tooltip("每局初始生命值（默认 5）")]
    public int maxLives = 5;

    [Tooltip("每击毁一块蓝色 BOMB 方块扣除的生命值（默认 1；设为 0 表示击毁蓝块不掉血）")]
    public int livesLostPerBombKill = 1;

    [Tooltip("生命值归 0 时是否立即判负（关闭后只显示生命值，不进入 Game Over）")]
    public bool gameOverEnabled = true;

    [Header("HUD 样式")]
    [Tooltip("剩余生命的方块颜色")]
    public Color lifeColor = new Color(1f, 0.30f, 0.34f, 1f);

    [Tooltip("已失去生命的方块颜色（灰暗）")]
    public Color lifeEmptyColor = new Color(0.28f, 0.30f, 0.36f, 0.50f);

    [Tooltip("生命值 ≤1（濒死）时文本颜色")]
    public Color lifeDangerColor = new Color(1f, 0.32f, 0.32f, 1f);

    /// <summary>当前剩余生命值。</summary>
    public int CurrentLives { get; private set; }

    /// <summary>是否已判负（Game Over 面板显示中）。</summary>
    public bool IsGameOver { get; private set; }

    // 静态请求：组件尚未创建 / 被禁用时也能安全调用
    private static bool enabledRequested = true;

    private bool livesEnabled = true;

    // ---------------- HUD 引用 ----------------
    private GameObject hudRoot;
    private Text livesText;
    private Text deltaText;
    private readonly List<Image> lifeMarks = new List<Image>();

    // ---------------- Game Over 面板引用 ----------------
    private GameObject gameOverRoot;
    private Text gameOverTitle;
    private Text gameOverSubtitle;
    private Text restartLabel;
    private Text homeLabel;
    private Text gameOverHint;

    private float lastDeltaUnscaledTime = -100f;

    // ================= 生命周期 =================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CurrentLives = Mathf.Max(1, maxLives);

        BuildHud();
        BuildGameOverUI();

        livesEnabled = enabledRequested;
        ApplyHudVisible(livesEnabled);
        RefreshHud();
        RefreshLocalizedTexts();

        Localization.OnLanguageChanged += RefreshLocalizedTexts;

        Debug.Log("[LivesManager] 生命值系统就绪：初始 " + CurrentLives + " 点，击毁蓝块扣 " +
                  Mathf.Max(0, livesLostPerBombKill) + " 点。");
    }

    private void OnDestroy()
    {
        Localization.OnLanguageChanged -= RefreshLocalizedTexts;
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        // 减血提示 1.2 秒后自动清除（真实时间，判负时 Time.timeScale = 0 也能正常计时）
        if (deltaText != null && deltaText.text.Length > 0 &&
            Time.unscaledTime - lastDeltaUnscaledTime > 1.2f)
        {
            deltaText.text = string.Empty;
        }

        if (!IsGameOver)
            return;

        // Game Over 面板：R 直接重开本关，ESC 返回主菜单（鼠标点击按钮同样有效）。
        // ESC 的判定统一在 EscapeRouter（优先级：编辑器 &gt; 指令框 &gt; Game Over），本组件不再自行读取 ESC。
        if (Input.GetKeyDown(KeyCode.R))
            RestartLevel();
    }

    // ================= 对外静态接口 =================

    /// <summary>重开一局生命值（进入 / 跳转任意关卡时调用，含清掉 Game Over 面板）。</summary>
    public static void ResetAll()
    {
        if (Instance != null)
            Instance.ResetLives();
    }

    /// <summary>
    /// 启用 / 关闭生命值系统：关闭时 HUD 隐藏且不再掉血（教学关使用）。
    /// 由 LevelPresentation 在各关卡进出时统一调用，幂等。
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        enabledRequested = enabled;
        if (Instance != null)
            Instance.ApplyEnabled(enabled);
    }

    /// <summary>
    /// 方块被击毁的上报入口（由 FlyingBlock 在击毁时调用，与 ScoreManager 同一条链路上报）。
    /// 只有蓝色 BOMB 块会扣生命值；红色 ATTACK 块击毁不加分也不扣血。
    /// </summary>
    public static void NotifyBlockDestroyed(FlyingBlock.BlockType type)
    {
        if (Instance == null)
            return;
        if (type != FlyingBlock.BlockType.Bomb)
            return;
        Instance.ApplyBombKillDamage();
    }

    // ================= 内部实现 =================

    private void ApplyEnabled(bool enabled)
    {
        livesEnabled = enabled;
        ApplyHudVisible(enabled);
        if (!enabled && IsGameOver)
            HideGameOverPanel();
    }

    private void ResetLives()
    {
        IsGameOver = false;
        CurrentLives = Mathf.Max(1, maxLives);
        lastDeltaUnscaledTime = -100f;
        if (deltaText != null)
            deltaText.text = string.Empty;
        HideGameOverPanel();
        RefreshHud();
        Debug.Log("[LivesManager] 生命值已重置为 " + CurrentLives + " 点。");
    }

    private void ApplyBombKillDamage()
    {
        if (!livesEnabled || IsGameOver)
            return;

        int loss = Mathf.Max(0, livesLostPerBombKill);
        if (loss <= 0)
            return;

        CurrentLives = Mathf.Max(0, CurrentLives - loss);
        lastDeltaUnscaledTime = Time.unscaledTime;
        RefreshHud();

        if (deltaText != null)
        {
            deltaText.text = "-" + loss;
            deltaText.color = lifeDangerColor;
        }

        Debug.Log("[LivesManager] 击毁蓝色 BOMB 块：生命值 -" + loss + "，剩余 " + CurrentLives + " 点。");

        if (CurrentLives <= 0 && gameOverEnabled)
            TriggerGameOver();
    }

    /// <summary>生命归 0：暂停时间、清场停生成、关闭其它模态，并弹出 GAME OVER 面板。</summary>
    private void TriggerGameOver()
    {
        if (IsGameOver)
            return;

        IsGameOver = true;

        // 本局结束结算：判负即一局终了，若当前处于 Level 3，把本局分数提交为该关最高分
        //（只在更高时写入并 Save()；后面不会清零分数，重置发生在 RestartLevel / ReturnToMenu）
        GameProgress.CommitCurrentLevelHighScore(ScoreManager.Instance != null ? ScoreManager.Instance.Score : 0);

        // 先关闭暂停面板 / 代码编辑器等模态（它们退出时会自行恢复 timeScale），避免与判负界面叠加
        PlayerCameraController player = FindObjectOfType<PlayerCameraController>();
        if (player != null)
        {
            player.ResumeExternal();
            player.SetExternalBlock(true);
        }
        CodeEditorController.ForceCloseIfOpen();

        // 最后冻结游戏时间：方块 / 炮台 / 计时全部停止（面板按钮走 unscaled 输入，不受影响）
        Time.timeScale = 0f;

        // 清场并停止生成：判负后场上不再出现新方块
        LevelOpeningGate.Cancel();
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null)
            spawner = FindObjectOfType<FlyingBlockSpawner>();
        if (spawner != null)
        {
            spawner.SuspendSpawning();
            spawner.ClearAllBlocks();
        }

        // 判负界面需要鼠标点击按钮
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RefreshLocalizedTexts();
        if (gameOverRoot != null)
            gameOverRoot.SetActive(true);

        Debug.Log("[LivesManager] 生命值归 0，本局判负（GAME OVER）。");
    }

    /// <summary>重新开始本关（Game Over 面板按钮 / R 键）。</summary>
    public void RestartLevel()
    {
        HideGameOverPanel();
        IsGameOver = false;
        Time.timeScale = 1f;

        PlayerCameraController player = FindObjectOfType<PlayerCameraController>();
        if (player != null)
        {
            player.ResetToLevelStart();   // 视角 / 姿态回到关卡起点，解除时间冻结与输入占用
            player.SetExternalBlock(false);
        }

        // 优先复用主页的关卡入口：清场、重开计分与生命值、重开开场停顿、锁鼠标，行为与正常进入关卡完全一致
        MainMenuController menu = FindObjectOfType<MainMenuController>();
        if (menu != null && LevelProgress.IsLevelUnlocked(3))
        {
            menu.StartLevel(3);
            return;
        }

        // 兜底（例如通过 /level 指令未解锁进入 Level 3 后判负）：直接跳转 Level 3
        LevelDirector.JumpTo(3, 1);
    }

    /// <summary>返回主菜单（Game Over 面板按钮 / ESC 键）。</summary>
    public void ReturnToMenu()
    {
        HideGameOverPanel();
        IsGameOver = false;
        Time.timeScale = 1f;
        ResetLives();

        MainMenuController menu = FindObjectOfType<MainMenuController>();
        if (menu != null)
        {
            menu.ShowMenu();
        }
        else
        {
            Time.timeScale = 1f;
            Debug.LogWarning("[LivesManager] 未找到主菜单，无法返回主页。");
        }
    }

    private void HideGameOverPanel()
    {
        if (gameOverRoot != null)
            gameOverRoot.SetActive(false);
    }

    // ================= HUD =================

    private void ApplyHudVisible(bool visible)
    {
        if (hudRoot != null)
            hudRoot.SetActive(visible);
    }

    private void RefreshHud()
    {
        if (livesText != null)
        {
            livesText.text = "LIFE " + CurrentLives + "/" + Mathf.Max(1, maxLives);
            livesText.color = CurrentLives <= 1 ? lifeDangerColor : Color.white;
        }

        for (int i = 0; i < lifeMarks.Count; i++)
        {
            if (lifeMarks[i] == null)
                continue;
            lifeMarks[i].color = i < CurrentLives ? lifeColor : lifeEmptyColor;
        }
    }

    /// <summary>
    /// 构建生命值 HUD：右上角、SCORE 得分下方（与得分同侧同风格），
    /// 结构 = 「LIFE 5/5」大号白字 + 一排生命方块 + 减血提示（-1）。
    /// </summary>
    private void BuildHud()
    {
        GameObject canvasGo = new GameObject("LivesCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // 与 Score HUD 同层（两者位置不重叠）

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UIFitScreen>();

        // HUD 根：右上角，锚点 (1,1)，位于 SCORE（顶部 -24，高 56）与其增量提示（-76~-116）下方
        RectTransform root = CreateRect("LivesRoot", canvasGo.transform, new Vector2(1f, 1f),
                                       new Vector2(-24f, -124f), new Vector2(420f, 124f));
        hudRoot = root.gameObject;

        livesText = CreateText("LivesText", root, "LIFE 5/5", 40, Color.white, TextAnchor.UpperRight, true);
        livesText.fontStyle = FontStyle.Bold;
        SetRect(livesText.rectTransform, new Vector2(1f, 1f), Vector2.zero, new Vector2(420f, 56f));
        AddOutline(livesText.gameObject, new Color(0f, 0f, 0f, 0.85f), new Vector2(2f, -2f));

        // 生命方块行：右对齐，从左到右依次点亮（1 个方块 = 1 点生命）
        int count = Mathf.Max(1, maxLives);
        float markSize = 22f;
        float markGap = 6f;
        RectTransform marksRow = CreateRect("LivesMarks", root, new Vector2(1f, 1f),
                                           new Vector2(0f, -56f), new Vector2(420f, markSize));
        lifeMarks.Clear();
        for (int i = 0; i < count; i++)
        {
            float x = -(count - 1 - i) * (markSize + markGap);
            RectTransform markRt = CreateRect("LifeMark" + i, marksRow, new Vector2(1f, 1f),
                                              new Vector2(x, 0f), new Vector2(markSize, markSize));
            Image img = markRt.gameObject.AddComponent<Image>();
            img.color = lifeColor;
            img.raycastTarget = false;
            lifeMarks.Add(img);
        }

        // 减血提示：与 SCORE 的增量提示同风格（-1 红字，1.2 秒后消失）
        deltaText = CreateText("LivesDelta", root, string.Empty, 28, lifeDangerColor, TextAnchor.UpperRight, true);
        SetRect(deltaText.rectTransform, new Vector2(1f, 1f), new Vector2(0f, -82f), new Vector2(420f, 34f));
        AddOutline(deltaText.gameObject, new Color(0f, 0f, 0f, 0.85f), new Vector2(2f, -2f));
    }

    // ================= Game Over 面板 =================

    private void BuildGameOverUI()
    {
        EnsureEventSystem();

        GameObject canvasGo = new GameObject("LivesGameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 240; // 高于 HUD / 主轴 / 指令框，判负时置顶

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UIFitScreen>();

        // 全屏遮罩（拦截点击，判负时不再误触游戏画面）
        RectTransform dim = CreateRect("GameOverDim", canvasGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                                       new Vector2(1920f, 1080f));
        Stretch(dim);
        Image dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.color = new Color(0.02f, 0.02f, 0.04f, 0.86f);
        dimImg.raycastTarget = true;

        gameOverRoot = dim.gameObject;

        // 面板
        RectTransform panel = CreateRect("GameOverPanel", dim, new Vector2(0.5f, 0.5f), Vector2.zero,
                                         new Vector2(900f, 440f));
        Image panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0.06f, 0.07f, 0.11f, 0.97f);

        gameOverTitle = CreateText("GameOverTitle", panel, "GAME OVER", 84, new Color(1f, 0.28f, 0.32f, 1f),
                                   TextAnchor.MiddleCenter, true);
        gameOverTitle.fontStyle = FontStyle.Bold;
        SetRect(gameOverTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(860f, 110f));
        AddOutline(gameOverTitle.gameObject, new Color(0f, 0f, 0f, 0.9f), new Vector2(3f, -3f));

        gameOverSubtitle = CreateText("GameOverSubtitle", panel, string.Empty, 30,
                                      new Color(0.95f, 0.95f, 1f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(gameOverSubtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 36f), new Vector2(860f, 120f));
        gameOverSubtitle.lineSpacing = 1.15f;

        restartLabel = CreateButton("RestartButton", panel, new Vector2(-170f, -110f), new Vector2(300f, 72f),
                                    new Color(0.20f, 0.62f, 0.72f, 0.95f));
        if (restartLabel != null)
        {
            Button btn = restartLabel.transform.parent.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(RestartLevel);
        }

        homeLabel = CreateButton("HomeButton", panel, new Vector2(170f, -110f), new Vector2(300f, 72f),
                                 new Color(0.30f, 0.34f, 0.44f, 0.95f));
        if (homeLabel != null)
        {
            Button btn = homeLabel.transform.parent.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(ReturnToMenu);
        }

        gameOverHint = CreateText("GameOverHint", panel, string.Empty, 22,
                                  new Color(0.70f, 0.75f, 0.85f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(gameOverHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(860f, 36f));

        gameOverRoot.SetActive(false);
    }

    /// <summary>按当前语言刷新 Game Over 面板与 HUD 文案（语言切换后同步）。</summary>
    private void RefreshLocalizedTexts()
    {
        int score = ScoreManager.Instance != null ? ScoreManager.Instance.Score : 0;

        if (gameOverSubtitle != null)
        {
            string livesLabel = Localization.Pick("生命值已耗尽", "Out of lives");

            // 底部保留分数结算，与右上角 SCORE 数值一致
            gameOverSubtitle.text = livesLabel + "\n" +
                                    Localization.Pick("本局得分 ", "SCORE ") + score;
        }

        if (restartLabel != null)
            restartLabel.text = Localization.Pick("重新开始", "RESTART");

        if (homeLabel != null)
            homeLabel.text = Localization.Pick("返回主菜单", "MAIN MENU");

        if (gameOverHint != null)
            gameOverHint.text = Localization.Pick("按 R 重新开始 · 按 ESC 返回主菜单",
                                                  "Press R to restart · Press ESC for main menu");

        if (gameOverTitle != null)
            gameOverTitle.font = UIFontHelper.LoadForText(gameOverTitle.text, gameOverTitle.fontSize);
        if (gameOverSubtitle != null)
            gameOverSubtitle.font = UIFontHelper.LoadForText(gameOverSubtitle.text, gameOverSubtitle.fontSize);
        if (restartLabel != null)
            restartLabel.font = UIFontHelper.LoadForText(restartLabel.text, restartLabel.fontSize);
        if (homeLabel != null)
            homeLabel.font = UIFontHelper.LoadForText(homeLabel.text, homeLabel.fontSize);
        if (gameOverHint != null)
            gameOverHint.font = UIFontHelper.LoadForText(gameOverHint.text, gameOverHint.fontSize);
    }

    // ================= UI 辅助 =================

    private static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private static Text CreateText(string name, Transform parent, string content, int fontSizeValue, Color color,
                                   TextAnchor anchor, bool mono)
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
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void AddOutline(GameObject go, Color color, Vector2 distance)
    {
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
    }

    /// <summary>创建一个居中按钮，返回其文本组件（用于后续语言刷新）。</summary>
    private static Text CreateButton(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        RectTransform rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), pos, size);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        Text label = CreateText("Label", rt, string.Empty, 30, Color.white, TextAnchor.MiddleCenter, false);
        Stretch(label.rectTransform);
        return label;
    }
}

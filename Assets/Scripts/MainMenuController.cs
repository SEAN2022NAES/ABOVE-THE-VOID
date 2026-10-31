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
    private Text hintText;
    private Text level1Label;
    private Text level2Label;
    private Text level3Label;
    private Text langZhLabel;
    private Text langEnLabel;
    private FlyingBlockSpawner spawner;
    private PlayerCameraController playerController;
    private float pulseTime;

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
    }

    /// <summary>点击关卡入口：隐藏主页并启动对应关卡（当前仅关卡1可用）。</summary>
    public void StartLevel(int levelIndex)
    {
        if (levelIndex != 1)
            return; // 关卡2 / 3 占位未开放

        if (root != null)
            root.SetActive(false);

        if (playerController != null)
            playerController.SetExternalBlock(false); // 解除输入占用

        SetCursorLocked(true);

        // Level 1 由教学控制器接管：先不生成方块，逐步引导操作（无教学组件时回退旧逻辑）
        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (tutorial != null)
            tutorial.StartTutorial();
        else if (spawner != null)
            spawner.BeginSpawning(); // 开始出块（保留进游戏前5秒缓冲）

        Debug.Log("[MainMenu] 进入关卡1：主页隐藏，游戏开始。");
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
        SetRect(bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1550f, 330f));
        bodyText.lineSpacing = 1.25f;

        // ---- 作者 ----
        authorText = CreateText("Author", root.transform, "", authorFontSize, new Color(1f, 0.85f, 0.35f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(authorText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -196f), new Vector2(1200f, 48f));

        // ---- 语言选择：中文 / English ----
        CreateLanguageSelector();

        // ---- 关卡区 ----
        levelTitleText = CreateText("LevelTitle", root.transform, "", levelTitleFontSize, new Color(0.75f, 0.8f, 0.9f, 1f), TextAnchor.MiddleCenter, false);
        SetRect(levelTitleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -304f), new Vector2(1200f, 44f));

        CreateLevelButton("Level1", new Vector2(-280f, -382f), true, 1, out level1Label);
        CreateLevelButton("Level2", new Vector2(0f, -382f), false, 2, out level2Label);
        CreateLevelButton("Level3", new Vector2(280f, -382f), false, 3, out level3Label);

        // ---- 底部说明 ----
        hintText = CreateText("Hint", root.transform, "", 20, new Color(0.6f, 0.65f, 0.75f, 0.9f), TextAnchor.MiddleCenter, false);
        SetRect(hintText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -480f), new Vector2(1800f, 40f));

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

    /// <summary>创建关卡按钮（interactable=false 时自动灰显；label 文本存引用供语言切换）。</summary>
    private void CreateLevelButton(string name, Vector2 pos, bool interactable, int levelIndex, out Text labelText)
    {
        RectTransform rt = CreateRect(name, root.transform, new Vector2(0.5f, 0.5f), pos, new Vector2(240f, 92f), new Vector2(0.5f, 0.5f));
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = interactable
            ? new Color(0.20f, 0.62f, 0.72f, 0.55f)
            : new Color(0.16f, 0.18f, 0.24f, 0.60f);

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = interactable;
        if (interactable)
        {
            int captured = levelIndex;
            btn.onClick.AddListener(() => StartLevel(captured));
        }

        labelText = CreateText("Label", rt, "", buttonFontSize, interactable ? Color.white : new Color(0.55f, 0.58f, 0.65f, 1f), TextAnchor.MiddleCenter, false);
        Stretch(labelText.rectTransform);
        labelText.lineSpacing = 0.9f;
    }

    /// <summary>创建语言选择按钮（中文 / English）：点击切换全局语言，并高亮当前语言。</summary>
    private void CreateLanguageSelector()
    {
        langZhLabel = CreateLangButton("LangZh", new Vector2(-80f, -252f), "中文", true);
        langEnLabel = CreateLangButton("LangEn", new Vector2(80f, -252f), "English", false);
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
                ? "A 3D Cartesian Coordinate Adventure  ·  三维直角坐标系冒险"
                : "A 3D Cartesian Coordinate Adventure";

        if (bodyText != null)
            bodyText.text = zh
                ? "项 目 目 的   ·   P R O J E C T   G O A L\n\n《ABOVE THE VOID》致力于以游戏化的方式，激发学生的编程兴趣与数学思维。\n玩家在三维直角坐标系中自由飞行，亲手感受空间坐标、向量方向与几何变换：\n每一次躲避、瞄准与射击，都是对数学直觉的淬炼；每一次观察规律、调整策略，\n都在悄然建立逻辑推理与抽象思维的能力。\n让数学不再是纸上的符号，而是触手可及的世界——\n在探索与创造中，点燃对科学与技术的热爱。"
                : "P R O J E C T   G O A L\n\nABOVE THE VOID aims to spark students' interest in programming and mathematical thinking through gameplay.\nFly freely inside a 3D Cartesian coordinate system, and feel coordinates, vectors and geometry firsthand:\nevery dodge, aim and shot sharpens your mathematical intuition; every pattern you notice and every strategy\nyou adjust quietly builds logical reasoning and abstract thinking.\nMake math no longer symbols on paper, but a world within reach —\nignite your passion for science and technology through exploration and creation.";

        if (authorText != null)
            authorText.text = zh
                ? "作者  ·  Author  ·  SEAN2022NAES"
                : "Author  ·  SEAN2022NAES";

        if (levelTitleText != null)
            levelTitleText.text = zh
                ? "选择关卡   S E L E C T   L E V E L"
                : "S E L E C T   L E V E L";

        if (level1Label != null)
            level1Label.text = zh ? "关卡 1\nLevel 1" : "Level 1";
        if (level2Label != null)
            level2Label.text = zh ? "关卡 2\n敬请期待" : "Level 2\nComing Soon";
        if (level3Label != null)
            level3Label.text = zh ? "关卡 3\n敬请期待" : "Level 3\nComing Soon";

        if (hintText != null)
            hintText.text = zh
                ? "进入关卡后：Q 自由视角 · WASD 横移 · Space/C 上下 · 左键激光 · E 冻结 · ESC 暂停"
                : "In level: Q free view · WASD move · Space/C up/down · LMB laser · E freeze time · ESC pause";

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
        if (hintText != null)
            hintText.font = UIFontHelper.LoadForText(hintText.text, hintText.fontSize);
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

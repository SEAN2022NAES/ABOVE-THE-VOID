using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「位于原点」提示条：玩家视角回到原点时，在画面下方给出一条「开始语法课 / Start the course」提示，点一下即可进课。
///
/// 显示规则（都满足才出）：
///   1. 玩家视角严格位于原点（PlayerCameraController.Mode == Normal，灵魂出窍 Q / 暂停看着都不算）；
///   2. 没有时间冻结；
///   3. 不在 Level 1 - Part 1 教学里（那里顶部已有教学提示面板，不再重复弹）；
///   4. 序章面板 / 语法课、API 课面板 / 代码编辑器都没有占屏（它们自己就带着入口，避免叠着两层）。
///
/// 因此按 Q 灵魂出窍（脱离教学、离开原点）时这条提示会自动消失，回到原点后重新出现。
/// </summary>
public class OriginHintController : MonoBehaviour
{
    private const float RefWidth = 1920f;
    private const float RefHeight = 1080f;
    private const float HintY = -330f;          // 相对屏幕中心的下方位置（1080 参考高度下离底约 210）
    private const float HintHeight = 74f;
    private const float HintMinWidth = 380f;
    private const float HintMaxWidth = 900f;
    private const float HintPadding = 120f;     // 文字两侧留白（含两侧箭头/内边距）
    private const float FadeSpeed = 7f;         // 淡入淡出速度（按 unscaled 时间）
    private const float ShowHold = 0.35f;       // 条件持续满足多久才真正显示，避免关面板瞬间闪一下

    private static OriginHintController inst;
    private static Sprite whiteSprite;

    private CanvasGroup group;
    private RectTransform hintRt;
    private Text label;
    private Image bg;

    private PlayerCameraController player;
    private CodeEditorController editor;
    private MainMenuController mainMenu;
    private float holdTimer;
    private bool labelIsChinese;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (inst != null)
            return;
        GameObject go = new GameObject("OriginHint");
        DontDestroyOnLoad(go);
        go.AddComponent<OriginHintController>();
    }

    private void Awake()
    {
        inst = this;
        BuildUI();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        if (inst == this)
            inst = null;
        if (group != null)
            Destroy(group.gameObject);
    }

    private void Update()
    {
        if (group == null)
            return;

        if (player == null)
            player = FindObjectOfType<PlayerCameraController>();
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();

        // 语言在设置里切换后（中/英），提示文案要跟着换，并重新量宽度
        if (labelIsChinese != Localization.IsChinese)
            RefreshLabel();

        bool want = WantShow();
        if (want)
        {
            holdTimer += Time.unscaledDeltaTime;
        }
        else
        {
            holdTimer = 0f;
        }

        bool visible = want && holdTimer >= ShowHold;
        float target = visible ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
        group.blocksRaycasts = group.alpha > 0.5f;
        group.interactable = group.blocksRaycasts;

        if (visible && Input.GetKeyDown(KeyCode.Return))
            EnterCourse();
    }

    /// <summary>是否应该出现「位于原点」提示。</summary>
    private bool WantShow()
    {
        if (player == null)
            return false;

        // 只有视角严格在原点时才给：灵魂出窍（Spirit）、暂停（Paused）、冻结飞行都不算
        if (player.Mode != PlayerCameraController.GameMode.Normal)
            return false;

        if (Time.timeScale <= 0.001f)
            return false;

        // Level 1 - Part 1 教学：顶部已有教学提示面板，这里不再重复弹（按 Q 脱离教学时 Mode 已不是 Normal，自然也收起了）
        if (TutorialController.Instance != null && TutorialController.Instance.Started)
            return false;

        // 序章面板自己带「开始语法课」按钮
        if (CodeIntroTutorialController.Reading)
            return false;

        // 已经在语法课 / API 课里，不需要再提示进课
        if (CodeSyntaxTutorialController.IsActive)
            return false;

        // 代码编辑器打开中
        if (editor != null && editor.IsOpen)
            return false;

        // 主页 / 暂停菜单在显示时不要冒出来（那里有自己的入口链）
        if (mainMenu == null)
            mainMenu = FindObjectOfType<MainMenuController>();
        if (mainMenu != null && mainMenu.IsMenuVisible)
            return false;

        return true;
    }

    private void EnterCourse()
    {
        // 与序章末页「开始语法课」同一条路径：黑幕淡出 → 进 C# 语法课第 1 个任务 → 淡入
        holdTimer = 0f;
        if (ScreenFade.Instance != null)
            ScreenFade.PlayTransition(0.35f, 0.5f, () => LevelDirector.JumpTo(1, 3, 1));
        else
            LevelDirector.JumpTo(1, 3, 1);
    }

    // ==================== UI ====================

    private void BuildUI()
    {
        GameObject canvasGo = new GameObject("OriginHintCanvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 180; // 低于教学 / 序章面板（210），高于常规 HUD
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UIFitScreen>();
        canvasGo.AddComponent<GraphicRaycaster>();

        group = canvasGo.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        hintRt = CreateRect("Hint", canvasGo.transform, new Vector2(0.5f, 0.5f),
            new Vector2(0f, HintY), new Vector2(HintMinWidth, HintHeight), new Vector2(0.5f, 0.5f));

        bg = hintRt.gameObject.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.WithAlpha(UITheme.PanelRaised, 0.94f);

        Button btn = hintRt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
        colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.onClick.AddListener(EnterCourse);

        // 上沿一条强调细线，跟序章面板 / HUD 保持同一套视觉语言
        RectTransform lineRt = CreateRect("TopLine", hintRt, new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 3f), new Vector2(0.5f, 1f));
        lineRt.anchorMin = new Vector2(0f, 1f);
        lineRt.anchorMax = new Vector2(1f, 1f);
        lineRt.offsetMin = new Vector2(0f, -3f);
        lineRt.offsetMax = Vector2.zero;
        Image lineImg = lineRt.gameObject.AddComponent<Image>();
        lineImg.sprite = WhiteSprite();
        lineImg.color = UITheme.WithAlpha(UITheme.Accent, 0.55f);
        lineImg.raycastTarget = false;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.SetParent(hintRt, false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(28f, 0f);
        labelRt.offsetMax = new Vector2(-28f, 0f);

        label = labelGo.AddComponent<Text>();
        label.text = "";
        label.fontSize = 30;
        label.color = UITheme.Accent;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 16;
        label.resizeTextMaxSize = 30;
        label.raycastTarget = false;
        label.font = UIFontHelper.LoadForText("Start the course › 开始语法课 ›", 30);
    }

    private void RefreshLabel()
    {
        if (label == null)
            return;

        string content = Localization.Pick("开始语法课 ›", "Start the course ›");
        labelIsChinese = Localization.IsChinese;
        label.text = content;
        label.font = UIFontHelper.LoadForText(content, 30);
        FitHintWidth(content);
    }

    /// <summary>提示条宽度按文案实测宽度给，中英文都不会把字挤在框外。</summary>
    private void FitHintWidth(string content)
    {
        if (hintRt == null || label == null)
            return;

        float width = HintMinWidth;
        if (!string.IsNullOrEmpty(content))
        {
            TextGenerationSettings settings = label.GetGenerationSettings(new Vector2(HintMaxWidth, HintHeight));
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.resizeTextForBestFit = false;
            settings.fontSize = 30;
            float scale = label.pixelsPerUnit > 0.01f ? label.pixelsPerUnit : 1f;
            float natural = label.cachedTextGeneratorForLayout.GetPreferredWidth(content, settings) / scale;
            width = Mathf.Clamp(natural + HintPadding, HintMinWidth, HintMaxWidth);
        }

        hintRt.sizeDelta = new Vector2(width, HintHeight);
    }

    private void OnEnable()
    {
        RefreshLabel();
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
}

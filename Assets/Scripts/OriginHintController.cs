using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「状态解锁」提示条：按玩家当前所处状态给出一到两条简短提示，纯提示、不可点击。
///
/// 两种状态（可同时成立，命中几条就叠几行）：
///   1. 时间静止（PlayerCameraController.IsTimeFrozen）→ 「时间已静止 · 按下 E 解锁」，按 E 恢复时间流动。
///      这条不分场景：教学关、语法课、代码编辑器打开时同样提示，只有主页菜单与 ESC 暂停面板不叠。
///   2. 视角锁定在原点（Mode == Normal 或 Mode == Frozen）→ 「你所在原点 · 按下 Q 解锁」，按 Q 进入灵魂出窍自由飞行。
///      序章（Level 0 Part 1 手动体验关）按 Q 教过、进入移动步骤之后就开始生效，此后所有关卡全程生效；
///      只有全屏教学关 / 代码编辑器 / 语法课占满屏幕的时段不叠。
/// 时间静止时视角本就停在原点（Mode == Frozen），此刻两条提示同时成立，上下两行一起提示，
/// 不要用互斥的 if/else 只留一条。
///
/// 其它情况一律不显示：主页菜单、暂停、全屏教学关（Level 1 Part 1~3 / Level 2 Part 1~2 / Level 0 Part 1 手动体验关）、
/// 代码编辑器与语法课面板占屏时都不弹，避免叠成两层。
/// 例外：Level 0 Part 1 手动体验关的引导面板只占上半屏、底部留空，与底部提示条不重叠，因此不拦截原点提示。
/// 原本的「开始语法课 / Start the course」跳转入口已移除：它不属于完整关的玩法提示，
/// 语法课入口保留在序章面板与编辑器入门末页。
/// </summary>
public class OriginHintController : MonoBehaviour
{
    [System.Flags]
    private enum HintState
    {
        None = 0,
        Origin = 1,   // 视角锁定在原点：提示按 Q 解锁
        Frozen = 2    // 时间静止：提示按 E 解锁
    }

    private const float HintY = -330f;          // 相对屏幕中心的下方位置（1080 参考高度下离底约 210）
    private const float HintHeight = 74f;
    private const float HintLineGap = 34f;      // 两条提示并成两行时，每多一行增加的高度
    private const float HintMinWidth = 380f;
    private const float HintMaxWidth = 900f;
    private const float FadeSpeed = 7f;         // 淡入淡出速度（按 unscaled 时间）
    private const float ShowHold = 0.35f;       // 条件持续满足多久才真正显示，避免切换瞬间闪一下
    private const int HintFontSize = 30;

    private static OriginHintController inst;

    private CanvasGroup group;
    private RectTransform hintRt;
    private Text label;

    private PlayerCameraController player;
    private CodeEditorController editor;
    private MainMenuController mainMenu;

    private HintState currentState = HintState.None;
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
        ApplyState(HintState.Origin); // 先按原点文案初始化，之后由 Update 按真实状态刷新
        currentState = HintState.None;
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
        if (mainMenu == null)
            mainMenu = FindObjectOfType<MainMenuController>();

        HintState state = ResolveState();
        if (state != currentState)
        {
            currentState = state;
            if (state != HintState.None)
                ApplyState(state);
            holdTimer = 0f;
        }

        // 语言在设置面板里被切换时立即换文案
        if (labelIsChinese != Localization.IsChinese && currentState != HintState.None)
            ApplyState(currentState);

        if (state != HintState.None)
            holdTimer += Time.unscaledDeltaTime;
        else
            holdTimer = 0f;

        bool visible = state != HintState.None && holdTimer >= ShowHold;
        float target = visible ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
        group.blocksRaycasts = false; // 纯提示，不拦鼠标
        group.interactable = false;
    }

    /// <summary>当前该显示哪些提示（可同时命中）；None 表示不收任何提示。</summary>
    private HintState ResolveState()
    {
        if (player == null)
            return HintState.None;

        // 主页菜单与 ESC 暂停面板是模态界面，任何提示都不往上叠
        if (player.Mode == PlayerCameraController.GameMode.Paused)
            return HintState.None;
        if (mainMenu != null && mainMenu.IsMenuVisible)
            return HintState.None;

        HintState state = HintState.None;

        // 时间静止提示：不分场景，教学关、语法课、编辑器打开时同样要给，
        // 玩家在任何地方被冻住都得知道按 E 解冻。
        if (player.IsTimeFrozen)
            state |= HintState.Frozen;

        // 原点提示：序章按 Q 教过、进入移动步骤之后生效，其余关卡全程生效；
        // 只要视角回到原点（时间冻结时停在原点也算）就给提示。
        // 只有全屏教学面板 / 代码编辑器占满屏幕的时段不叠，避免两层提示打架。
        if (IsOriginHintUnlocked() && !IsOverlayTutorialActive())
        {
            if (player.Mode == PlayerCameraController.GameMode.Normal ||
                player.Mode == PlayerCameraController.GameMode.Frozen)
                state |= HintState.Origin;
        }

        return state;
    }

    /// <summary>原点提示的门槛：序章教过按 Q 之后才对原点状态提示，其余关卡全程开放。</summary>
    private static bool IsOriginHintUnlocked()
    {
        TutorialController tutorial = TutorialController.Instance;
        if (tutorial != null && tutorial.Started && !tutorial.TaughtQ)
            return false;
        return true;
    }

    /// <summary>正被全屏教学面板或代码编辑器盖住屏幕：原点提示先让位。</summary>
    private bool IsOverlayTutorialActive()
    {
        // 注意：Level 0 Part 1 手动体验关的引导教学面板只占上半屏、底部留空，
        // 与底部提示条不重叠，所以这里不拦 TutorialController.Started。
        if (CodeIntroTutorialController.Reading)
            return true;
        if (EditorIntroTutorialController.Instance != null && EditorIntroTutorialController.Instance.Active)
            return true;
        if (CodeSyntaxTutorialController.IsActive)
            return true;
        if (editor != null && editor.IsOpen)
            return true;
        return false;
    }

    /// <summary>按状态刷新文案、字体与提示条宽度。</summary>
    private void ApplyState(HintState state)
    {
        if (label == null)
            return;

        bool atOrigin = (state & HintState.Origin) != 0;
        bool frozen = (state & HintState.Frozen) != 0;
        if (!atOrigin && !frozen)
            return;

        string originText = Localization.Pick("你所在原点 · 按下 Q 解锁", "You are at the Origin · press Q to unlock");
        string frozenText = Localization.Pick("时间已静止 · 按下 E 解锁", "Time is frozen · press E to unlock");

        // 两条同时成立时上下两行并列显示：先原点、再时间静止，互不覆盖
        string content;
        if (atOrigin && frozen)
            content = originText + "\n" + frozenText;
        else if (atOrigin)
            content = originText;
        else
            content = frozenText;

        labelIsChinese = Localization.IsChinese;
        label.text = content;
        label.font = UIFontHelper.LoadForText(content, HintFontSize);
        FitHintWidth(content);
    }

    /// <summary>按文字自然宽度缩放宽窄、按行数抬高高度，均夹在上下限之间。</summary>
    private void FitHintWidth(string content)
    {
        if (hintRt == null || label == null)
            return;

        float width = HintMinWidth;
        int lineCount = 1;
        if (!string.IsNullOrEmpty(content))
        {
            string[] lines = content.Split('\n');
            lineCount = Mathf.Max(1, lines.Length);

            float scale = label.pixelsPerUnit > 0.01f ? label.pixelsPerUnit : 1f;
            float widest = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                    continue;
                TextGenerationSettings settings = label.GetGenerationSettings(new Vector2(HintMaxWidth, HintHeight));
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                settings.resizeTextForBestFit = false;
                settings.fontSize = HintFontSize;
                float natural = label.cachedTextGeneratorForLayout.GetPreferredWidth(lines[i], settings) / scale;
                if (natural > widest)
                    widest = natural;
            }

            width = Mathf.Clamp(widest + 72f, HintMinWidth, HintMaxWidth);
        }

        // 两条提示并排成两行时整体加高，避免第二行被 Truncate 截掉
        float height = HintHeight + HintLineGap * (lineCount - 1);
        hintRt.sizeDelta = new Vector2(width, height);
        hintRt.anchoredPosition = new Vector2(0f, HintY);
    }

    // ---------------- UI 构建 ----------------

    private void BuildUI()
    {
        GameObject canvasGo = new GameObject("OriginHintCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<UIFitScreen>();
        canvasGo.AddComponent<GraphicRaycaster>();

        group = canvasGo.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        hintRt = CreateRect("OriginHintBar", canvasGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, HintY), new Vector2(HintMinWidth, HintHeight), new Vector2(0.5f, 0.5f));

        Image bg = hintRt.gameObject.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(0.06f, 0.07f, 0.10f, 0.86f);
        bg.raycastTarget = false;

        // 上沿一条强调细线，跟序章面板 / HUD 保持同一套视觉语言
        RectTransform lineRt = CreateRect("TopLine", hintRt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 3f), new Vector2(0.5f, 1f));
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
        label.fontSize = HintFontSize;
        label.color = UITheme.Accent;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 16;
        label.resizeTextMaxSize = HintFontSize;
        label.raycastTarget = false;
        label.font = UIFontHelper.LoadForText("你所在原点 · 按下 Q 解锁", HintFontSize);
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

    private static Sprite cachedWhiteSprite;

    private static Sprite WhiteSprite()
    {
        if (cachedWhiteSprite != null)
            return cachedWhiteSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        cachedWhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        cachedWhiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return cachedWhiteSprite;
    }
}

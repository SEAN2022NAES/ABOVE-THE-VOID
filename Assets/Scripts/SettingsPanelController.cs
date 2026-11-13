using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 自动构建「暂停」UI（纯代码，无需手工搭建 Canvas）：
///   - 暂停面板：全屏遮罩 + 居中卡片，内含鼠标灵敏度滑块、键位说明、退出暂停与返回主页按钮；
///   - 暂停由 ESC（PlayerCameraController）触发，本组件负责展示面板与按钮事件。
/// 与 PlayerCameraController 通过事件解耦，可独立挂载。
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    /// <summary>灵敏度变化事件（Slider 拖动时触发）。</summary>
    public event System.Action<float> OnSensitivityChanged;

    /// <summary>视场角变化事件（FOV 滑杆拖动时触发，供相机实时应用）。</summary>
    public event System.Action<float> OnFieldOfViewChanged;

    /// <summary>暂停请求事件（退出暂停按钮触发，主控负责切换状态）。</summary>
    public event System.Action OnPauseRequested;

    /// <summary>返回主页请求事件（「返回主页」按钮触发，主控负责统一收尾）。</summary>
    public event System.Action OnReturnHomeRequested;

    [Header("灵敏度范围")]
    public float minSensitivity = 0.5f;
    public float maxSensitivity = 10f;
    public float defaultSensitivity = 1.75f;

    [Header("帧率范围")]
    public float minFps = 30f;
    public float maxFps = 240f;
    public float defaultFps = 60f;

    [Header("视场角范围（度）")]
    public float minFov = 50f;
    public float maxFov = 110f;
    public float defaultFov = 90f;

    [Header("UI 颜色")]
    public Color panelBackgroundColor = UITheme.PanelDeep;
    public Color accentColor = UITheme.Accent;

    // 本地存档键（PlayerPrefs）：每个可调项一个键，改一次即保存，下次启动自动恢复
    private const string FovPrefsKey = "atv.settings.fov";                 // 视场角（度）
    private const string SensitivityPrefsKey = "atv.settings.sensitivity"; // 鼠标灵敏度（倍率）
    private const string FpsPrefsKey = "atv.settings.fps";                 // 帧率上限（Application.targetFrameRate）

    private GameObject pausePanel;
    private Slider sensitivitySlider;
    private Text sensitivityValueText;
    private Slider fpsSlider;
    private Text fpsValueText;
    private Slider fovSlider;
    private Text fovValueText;

    // 需要随语言切换刷新的文案控件（默认语言为英文时整屏英文，不留硬编码中文）
    private Text titleText;
    private Text sensLabelText;
    private Text fpsLabelText;
    private Text fovLabelText;
    private Text helpText;
    private Text resumeButtonText;
    private Text homeButtonText;

    /// <summary>暂停面板当前是否可见。</summary>
    public bool IsPanelVisible => pausePanel != null && pausePanel.activeSelf;

    /// <summary>当前视场角（度），供主控初始化相机时读取。</summary>
    public float FieldOfView => fovSlider != null ? fovSlider.value : defaultFov;

    /// <summary>当前鼠标灵敏度（倍率），供相机初始化时读取存档值（未构建滑块时回落到默认值）。</summary>
    public float Sensitivity => sensitivitySlider != null ? sensitivitySlider.value : defaultSensitivity;

    /// <summary>当前帧率上限（FPS），供主控初始化时读取。</summary>
    public float TargetFrameRate => fpsSlider != null ? fpsSlider.value : defaultFps;

    private void Awake()
    {
        // 读取上次保存的视场角 / 灵敏度 / 帧率（没有存档则用各自默认值；读到脏值一律 Clamp 兜底）
        if (PlayerPrefs.HasKey(FovPrefsKey))
            defaultFov = Mathf.Clamp(PlayerPrefs.GetFloat(FovPrefsKey, defaultFov), minFov, maxFov);

        if (PlayerPrefs.HasKey(SensitivityPrefsKey))
            defaultSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityPrefsKey, defaultSensitivity), minSensitivity, maxSensitivity);

        if (PlayerPrefs.HasKey(FpsPrefsKey))
            defaultFps = Mathf.Clamp(PlayerPrefs.GetFloat(FpsPrefsKey, defaultFps), minFps, maxFps);

        EnsureEventSystem();
        BuildUI();
        HidePanel();
    }

    private void OnDestroy()
    {
        Localization.OnLanguageChanged -= HandleLanguageChanged;
    }

    private void HandleLanguageChanged()
    {
        ApplyLocalizedTexts();
    }

    // ---------------- UI 构建 ----------------

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void BuildUI()
    {
        // Canvas（Screen Space Overlay）
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 暂停界面必须压在教学面板（TutorialController = 60）与任务卡(210) 之上，否则引导关按 ESC 时暂停面板会被教学面板挡住；
        // 仍低于指令框(320) 与淡入淡出(900)。
        canvas.sortingOrder = 260;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        // ---- 暂停面板（全屏遮罩 + 居中卡片） ----
        pausePanel = CreateRect("PausePanel", canvas.transform).gameObject;
        RectTransform overlayRt = pausePanel.GetComponent<RectTransform>();
        Stretch(overlayRt);
        Image overlayImg = pausePanel.AddComponent<Image>();
        overlayImg.color = UITheme.ScrimSoft;

        RectTransform card = CreateRect("Card", pausePanel.transform);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(620f, 660f);
        Image cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = panelBackgroundColor;

        // 标题
        titleText = CreateText("Title", card, Localization.Pick("游戏暂停", "Paused"), 34, UITheme.TextPrimary);
        RectTransform titleRt = titleText.rectTransform;
        titleRt.anchorMin = new Vector2(0.5f, 1f);
        titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -28f);
        titleRt.sizeDelta = new Vector2(560f, 50f);

        // 灵敏度行（标签 + 滑块 + 数值）
        Text sensLabel = CreateText("SensLabel", card, Localization.Pick("鼠标灵敏度", "Mouse sensitivity"), 22, UITheme.TextSub);
        sensLabelText = sensLabel;
        sensLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        sensLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        sensLabel.rectTransform.pivot = new Vector2(0f, 1f);
        sensLabel.rectTransform.anchoredPosition = new Vector2(36f, -110f);
        sensLabel.rectTransform.sizeDelta = new Vector2(160f, 40f);
        sensLabel.alignment = TextAnchor.MiddleLeft;

        sensitivityValueText = CreateText("SensValue", card, Mathf.Clamp(defaultSensitivity, minSensitivity, maxSensitivity).ToString("F1"), 22, accentColor);
        sensitivityValueText.rectTransform.anchorMin = new Vector2(1f, 1f);
        sensitivityValueText.rectTransform.anchorMax = new Vector2(1f, 1f);
        sensitivityValueText.rectTransform.pivot = new Vector2(1f, 1f);
        sensitivityValueText.rectTransform.anchoredPosition = new Vector2(-36f, -110f);
        sensitivityValueText.rectTransform.sizeDelta = new Vector2(60f, 40f);
        sensitivityValueText.alignment = TextAnchor.MiddleRight;

        Slider slider = CreateSlider("SensitivitySlider", card);
        RectTransform sRt = slider.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 1f);
        sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, -162f);
        sRt.sizeDelta = new Vector2(-72f, 24f);
        slider.minValue = minSensitivity;
        slider.maxValue = maxSensitivity;
        slider.SetValueWithoutNotify(Mathf.Clamp(defaultSensitivity, minSensitivity, maxSensitivity)); // 启动即对齐存档值，避免先显默认值再跳变
        slider.onValueChanged.AddListener(OnSliderChanged);
        sensitivitySlider = slider;

        // 帧率行（标签 + 滑块 + 数值）
        Text fpsLabel = CreateText("FpsLabel", card, Localization.Pick("帧率 / FPS", "Frame rate / FPS"), 22, UITheme.TextSub);
        fpsLabelText = fpsLabel;
        fpsLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        fpsLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        fpsLabel.rectTransform.pivot = new Vector2(0f, 1f);
        fpsLabel.rectTransform.anchoredPosition = new Vector2(36f, -210f);
        fpsLabel.rectTransform.sizeDelta = new Vector2(160f, 40f);
        fpsLabel.alignment = TextAnchor.MiddleLeft;

        fpsValueText = CreateText("FpsValue", card, Mathf.Clamp(defaultFps, minFps, maxFps).ToString("F0"), 22, accentColor);
        fpsValueText.rectTransform.anchorMin = new Vector2(1f, 1f);
        fpsValueText.rectTransform.anchorMax = new Vector2(1f, 1f);
        fpsValueText.rectTransform.pivot = new Vector2(1f, 1f);
        fpsValueText.rectTransform.anchoredPosition = new Vector2(-36f, -210f);
        fpsValueText.rectTransform.sizeDelta = new Vector2(60f, 40f);
        fpsValueText.alignment = TextAnchor.MiddleRight;

        fpsSlider = CreateSlider("FpsSlider", card);
        RectTransform fpsRt = fpsSlider.GetComponent<RectTransform>();
        fpsRt.anchorMin = new Vector2(0f, 1f);
        fpsRt.anchorMax = new Vector2(1f, 1f);
        fpsRt.pivot = new Vector2(0.5f, 1f);
        fpsRt.anchoredPosition = new Vector2(0f, -262f);
        fpsRt.sizeDelta = new Vector2(-72f, 24f);
        fpsSlider.minValue = minFps;
        fpsSlider.maxValue = maxFps;
        fpsSlider.SetValueWithoutNotify(Mathf.Clamp(defaultFps, minFps, maxFps)); // 启动即对齐存档值
        fpsSlider.onValueChanged.AddListener(OnFpsSliderChanged);
        ApplyTargetFrameRate(defaultFps); // 初始化即应用存档帧率（拖动滑杆后再按滑杆值调整）

        // 视场角行（标签 + 滑块 + 数值）：实时作用到第一人称相机，并写入 PlayerPrefs 持久化
        Text fovLabel = CreateText("FovLabel", card, Localization.Pick("视场角 / FOV", "Field of view / FOV"), 22, UITheme.TextSub);
        fovLabelText = fovLabel;
        fovLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        fovLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        fovLabel.rectTransform.pivot = new Vector2(0f, 1f);
        fovLabel.rectTransform.anchoredPosition = new Vector2(36f, -310f);
        fovLabel.rectTransform.sizeDelta = new Vector2(320f, 40f);
        fovLabel.alignment = TextAnchor.MiddleLeft;

        fovValueText = CreateText("FovValue", card, defaultFov.ToString("F0") + "°", 22, accentColor);
        fovValueText.rectTransform.anchorMin = new Vector2(1f, 1f);
        fovValueText.rectTransform.anchorMax = new Vector2(1f, 1f);
        fovValueText.rectTransform.pivot = new Vector2(1f, 1f);
        fovValueText.rectTransform.anchoredPosition = new Vector2(-36f, -310f);
        fovValueText.rectTransform.sizeDelta = new Vector2(90f, 40f);
        fovValueText.alignment = TextAnchor.MiddleRight;

        fovSlider = CreateSlider("FovSlider", card);
        RectTransform fovRt = fovSlider.GetComponent<RectTransform>();
        fovRt.anchorMin = new Vector2(0f, 1f);
        fovRt.anchorMax = new Vector2(1f, 1f);
        fovRt.pivot = new Vector2(0.5f, 1f);
        fovRt.anchoredPosition = new Vector2(0f, -362f);
        fovRt.sizeDelta = new Vector2(-72f, 24f);
        fovSlider.minValue = minFov;
        fovSlider.maxValue = maxFov;
        fovSlider.SetValueWithoutNotify(Mathf.Clamp(defaultFov, minFov, maxFov));
        fovSlider.onValueChanged.AddListener(OnFovSliderChanged);

        // 键位说明（随语言切换刷新）
        helpText = CreateText("HelpText", card, GetHelpText(), 18, UITheme.TextMuted);
        helpText.rectTransform.anchorMin = new Vector2(0f, 1f);
        helpText.rectTransform.anchorMax = new Vector2(1f, 1f);
        helpText.rectTransform.pivot = new Vector2(0.5f, 1f);
        helpText.rectTransform.anchoredPosition = new Vector2(0f, -410f);
        helpText.rectTransform.sizeDelta = new Vector2(-72f, 150f);
        helpText.alignment = TextAnchor.UpperLeft;
        helpText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // 底部按钮：退出暂停 / 返回主页
        Button resumeBtn = CreateButton(card, "ResumeButton", Localization.Pick("退出暂停", "Resume"), 22,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-105f, 28f), new Vector2(200f, 54f));
        resumeBtn.onClick.AddListener(() => OnPauseRequested?.Invoke());
        resumeButtonText = resumeBtn.GetComponentInChildren<Text>();

        Button homeBtn = CreateButton(card, "ReturnHomeButton", Localization.Pick("返回主页", "Main menu"), 22,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(105f, 28f), new Vector2(200f, 54f));
        homeBtn.onClick.AddListener(() => OnReturnHomeRequested?.Invoke());
        homeButtonText = homeBtn.GetComponentInChildren<Text>();

        // 语言切换时整屏文案跟随（默认英文即全英文，不留硬编码中文）
        Localization.OnLanguageChanged -= HandleLanguageChanged;
        Localization.OnLanguageChanged += HandleLanguageChanged;
        ApplyLocalizedTexts();
    }

    private static string GetHelpText()
    {
        if (Localization.IsChinese)
        {
            return "移动：W / A / S / D        上升 / 下降：Space / C\n" +
                   "视角旋转：鼠标              Q：坐标轴模式 / F1：自由模式\n" +
                   "再按对应按键返回原点       加速：LeftShift   暂停：ESC / 时间冻结：E";
        }
        return "Move: W / A / S / D        Up / Down: Space / C\n" +
               "Look: mouse                Q: axis mode / F1: free mode\n" +
               "Press again to return      Sprint: LeftShift   Pause: ESC / Freeze time: E";
    }

    /// <summary>按当前语言刷新全部文案与字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）。</summary>
    private void ApplyLocalizedTexts()
    {
        SetLocalized(titleText, Localization.Pick("游戏暂停", "Paused"));
        SetLocalized(sensLabelText, Localization.Pick("鼠标灵敏度", "Mouse sensitivity"));
        SetLocalized(fpsLabelText, Localization.Pick("帧率 / FPS", "Frame rate / FPS"));
        SetLocalized(fovLabelText, Localization.Pick("视场角 / FOV", "Field of view / FOV"));
        SetLocalized(helpText, GetHelpText());
        SetLocalized(resumeButtonText, Localization.Pick("退出暂停", "Resume"));
        SetLocalized(homeButtonText, Localization.Pick("返回主页", "Main menu"));
    }

    private static void SetLocalized(Text target, string content)
    {
        if (target == null)
            return;
        target.text = content;
        target.font = UIFontHelper.LoadForText(content, target.fontSize);
    }

    // ---------------- 对外接口 ----------------

    public void ShowPanel() => SetPanelVisible(true);

    public void HidePanel() => SetPanelVisible(false);

    /// <summary>同步外部灵敏度到 UI（不触发回调，供主控初始化时对齐）。</summary>
    public void SetSensitivityWithoutNotify(float value)
    {
        if (sensitivitySlider == null)
            return;
        sensitivitySlider.SetValueWithoutNotify(Mathf.Clamp(value, minSensitivity, maxSensitivity));
        if (sensitivityValueText != null)
            sensitivityValueText.text = sensitivitySlider.value.ToString("F1");
    }

    private void SetPanelVisible(bool visible)
    {
        if (pausePanel != null)
            pausePanel.SetActive(visible);
    }

    private void OnSliderChanged(float value)
    {
        float clamped = Mathf.Clamp(value, minSensitivity, maxSensitivity);
        if (sensitivityValueText != null)
            sensitivityValueText.text = clamped.ToString("F1");

        // 持久化：写入本地存档，下次启动由 Awake 读回
        PlayerPrefs.SetFloat(SensitivityPrefsKey, clamped);
        PlayerPrefs.Save();

        OnSensitivityChanged?.Invoke(clamped);
    }

    private void OnFpsSliderChanged(float value)
    {
        float clamped = Mathf.Clamp(value, minFps, maxFps);
        if (fpsValueText != null)
            fpsValueText.text = ((int)clamped).ToString();

        // 持久化 + 立即生效
        PlayerPrefs.SetFloat(FpsPrefsKey, clamped);
        PlayerPrefs.Save();
        ApplyTargetFrameRate(clamped);
    }

    /// <summary>把帧率上限应用到运行时（Clamp 后取整，避免存档脏值写入非法帧率）。</summary>
    private void ApplyTargetFrameRate(float fps)
    {
        Application.targetFrameRate = Mathf.RoundToInt(Mathf.Clamp(fps, minFps, maxFps));
    }

    /// <summary>同步外部视场角到 UI（不触发回调）；同时立即应用到相机并保存。</summary>
    public void SetFieldOfViewWithoutNotify(float value)
    {
        if (fovSlider == null)
            return;
        float clamped = Mathf.Clamp(value, minFov, maxFov);
        fovSlider.SetValueWithoutNotify(clamped);
        if (fovValueText != null)
            fovValueText.text = clamped.ToString("F0") + "°";
        PlayerPrefs.SetFloat(FovPrefsKey, clamped);
        PlayerPrefs.Save();
    }

    private void OnFovSliderChanged(float value)
    {
        if (fovValueText != null)
            fovValueText.text = value.ToString("F0") + "°";

        // 持久化：写入本地存档，下次启动由 Awake 读回
        PlayerPrefs.SetFloat(FovPrefsKey, value);
        PlayerPrefs.Save();

        // 实时作用到第一人称相机
        if (OnFieldOfViewChanged != null)
            OnFieldOfViewChanged(value);
    }

    /// <summary>出厂默认值：固定，不随存档变化（/lock 回厂重置使用）。</summary>
    private const float FactoryDefaultFov = 90f;
    private const float FactoryDefaultSensitivity = 1.75f;
    private const float FactoryDefaultFps = 60f;

    /// <summary>删除视场角本地存档（兼容旧入口；设置面板尚未构建时也能调用）。</summary>
    public static void ClearSavedFov()
    {
        PlayerPrefs.DeleteKey(FovPrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>删除全部设置存档（视场角 / 灵敏度 / 帧率），/lock 回厂重置使用；面板未构建时也能调用。</summary>
    public static void ClearSavedSettings()
    {
        PlayerPrefs.DeleteKey(FovPrefsKey);
        PlayerPrefs.DeleteKey(SensitivityPrefsKey);
        PlayerPrefs.DeleteKey(FpsPrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 回厂重置（/lock 指令调用）：删除全部设置存档，把视场角 / 灵敏度 / 帧率恢复为出厂默认，
    /// 并通过事件让相机与帧率立即应用，不用重启也能看到默认效果。
    /// </summary>
    public void ResetToFactoryDefaults()
    {
        ClearSavedSettings();

        defaultFov = Mathf.Clamp(FactoryDefaultFov, minFov, maxFov);
        defaultSensitivity = Mathf.Clamp(FactoryDefaultSensitivity, minSensitivity, maxSensitivity);
        defaultFps = Mathf.Clamp(FactoryDefaultFps, minFps, maxFps);

        if (fovSlider != null)
        {
            fovSlider.SetValueWithoutNotify(defaultFov);
            if (fovValueText != null)
                fovValueText.text = defaultFov.ToString("F0") + "°";
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.SetValueWithoutNotify(defaultSensitivity);
            if (sensitivityValueText != null)
                sensitivityValueText.text = defaultSensitivity.ToString("F1");
        }

        if (fpsSlider != null)
        {
            fpsSlider.SetValueWithoutNotify(defaultFps);
            if (fpsValueText != null)
                fpsValueText.text = ((int)defaultFps).ToString();
        }

        // 立即生效（不写存档，存档已在上面删除）
        if (OnFieldOfViewChanged != null)
            OnFieldOfViewChanged(defaultFov);
        if (OnSensitivityChanged != null)
            OnSensitivityChanged(defaultSensitivity);
        ApplyTargetFrameRate(defaultFps);

        Debug.Log("[Settings] 已恢复出厂设置：视场角 " + defaultFov.ToString("F0")
                  + "°、灵敏度 " + defaultSensitivity.ToString("F1")
                  + "、帧率 " + ((int)defaultFps).ToString());
    }

    // ---------------- UI 构建辅助 ----------------

    private Canvas canvas;

    private RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private Text CreateText(string name, Transform parent, string content, int fontSize, Color color)
    {
        RectTransform rt = CreateRect(name, parent);
        Text t = rt.gameObject.AddComponent<Text>();
        t.text = content;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        t.font = UIFontHelper.LoadForText(content, fontSize); // v0.1.1：按内容自动选字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）
        return t;
    }

    private Slider CreateSlider(string name, Transform parent)
    {
        RectTransform rt = CreateRect(name, parent);
        Slider slider = rt.gameObject.AddComponent<Slider>();

        // 背景
        RectTransform bg = CreateRect("Background", rt);
        Stretch(bg);
        Image bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = UITheme.WithAlpha(UITheme.Accent, 0.16f);

        // 拖动区域 + 手柄
        RectTransform area = CreateRect("Handle Slide Area", rt);
        Stretch(area);

        RectTransform handle = CreateRect("Handle", area);
        handle.anchorMin = new Vector2(0.5f, 0.5f);
        handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(24f, 24f);

        Image handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = accentColor;

        slider.targetGraphic = handleImg;
        slider.handleRect = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private Button CreateButton(Transform parent, string name, string label, int fontSize,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        RectTransform rt = CreateRect(name, parent);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = rt.gameObject.AddComponent<Image>();
        img.color = accentColor;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        // 按钮底色为亮青 Accent，文字必须用深色（OnAccent）才能看清
        Text t = CreateText("Text", rt, label, fontSize, UITheme.OnAccent);
        Stretch(t.rectTransform);
        return btn;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

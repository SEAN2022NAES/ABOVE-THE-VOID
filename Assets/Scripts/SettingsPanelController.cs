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

    [Header("UI 颜色")]
    public Color panelBackgroundColor = new Color(0.06f, 0.06f, 0.10f, 0.85f);
    public Color accentColor = new Color(0.20f, 0.45f, 1.00f);

    private GameObject pausePanel;
    private Slider sensitivitySlider;
    private Text sensitivityValueText;
    private Slider fpsSlider;
    private Text fpsValueText;

    /// <summary>暂停面板当前是否可见。</summary>
    public bool IsPanelVisible => pausePanel != null && pausePanel.activeSelf;

    private void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        HidePanel();
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
        overlayImg.color = new Color(0f, 0f, 0f, 0.55f);

        RectTransform card = CreateRect("Card", pausePanel.transform);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(560f, 560f);
        Image cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = panelBackgroundColor;

        // 标题
        Text title = CreateText("Title", card, "游戏暂停", 34, Color.white);
        title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.anchoredPosition = new Vector2(0f, -28f);
        title.rectTransform.sizeDelta = new Vector2(520f, 50f);

        // 灵敏度行（标签 + 滑块 + 数值）
        Text sensLabel = CreateText("SensLabel", card, "鼠标灵敏度", 22, Color.white);
        sensLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        sensLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        sensLabel.rectTransform.pivot = new Vector2(0f, 1f);
        sensLabel.rectTransform.anchoredPosition = new Vector2(36f, -110f);
        sensLabel.rectTransform.sizeDelta = new Vector2(160f, 40f);
        sensLabel.alignment = TextAnchor.MiddleLeft;

        sensitivityValueText = CreateText("SensValue", card, defaultSensitivity.ToString("F1"), 22, accentColor);
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
        slider.SetValueWithoutNotify(defaultSensitivity);
        slider.onValueChanged.AddListener(OnSliderChanged);
        sensitivitySlider = slider;

        // 帧率行（标签 + 滑块 + 数值）
        Text fpsLabel = CreateText("FpsLabel", card, "帧率 / FPS", 22, Color.white);
        fpsLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        fpsLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        fpsLabel.rectTransform.pivot = new Vector2(0f, 1f);
        fpsLabel.rectTransform.anchoredPosition = new Vector2(36f, -210f);
        fpsLabel.rectTransform.sizeDelta = new Vector2(160f, 40f);
        fpsLabel.alignment = TextAnchor.MiddleLeft;

        fpsValueText = CreateText("FpsValue", card, defaultFps.ToString("F0"), 22, accentColor);
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
        fpsSlider.SetValueWithoutNotify(defaultFps);
        fpsSlider.onValueChanged.AddListener(OnFpsSliderChanged);
        Application.targetFrameRate = (int)defaultFps; // 初始化即应用默认帧率（拖动滑杆后再按滑杆值调整）

        // 键位说明
        Text help = CreateText("HelpText", card,
            "移动：W / A / S / D        上升 / 下降：Space / C\n" +
            "视角旋转：鼠标              Q：坐标轴模式 / F1：自由模式\n" +
            "再按对应按键返回原点       加速：LeftShift   暂停：ESC / 时间冻结：E",
            18, new Color(0.85f, 0.85f, 0.85f, 1f));
        help.rectTransform.anchorMin = new Vector2(0f, 1f);
        help.rectTransform.anchorMax = new Vector2(1f, 1f);
        help.rectTransform.pivot = new Vector2(0.5f, 1f);
        help.rectTransform.anchoredPosition = new Vector2(0f, -310f);
        help.rectTransform.sizeDelta = new Vector2(-72f, 150f);
        help.alignment = TextAnchor.UpperLeft;
        help.horizontalOverflow = HorizontalWrapMode.Wrap;

        // 底部按钮：退出暂停 / 返回主页
        Button resumeBtn = CreateButton(card, "ResumeButton", "退出暂停", 22,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-95f, 28f), new Vector2(180f, 54f));
        resumeBtn.onClick.AddListener(() => OnPauseRequested?.Invoke());

        Button homeBtn = CreateButton(card, "ReturnHomeButton", "返回主页", 22,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(95f, 28f), new Vector2(180f, 54f));
        homeBtn.onClick.AddListener(() => OnReturnHomeRequested?.Invoke());
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
        if (sensitivityValueText != null)
            sensitivityValueText.text = value.ToString("F1");
        OnSensitivityChanged?.Invoke(value);
    }

    private void OnFpsSliderChanged(float value)
    {
        if (fpsValueText != null)
            fpsValueText.text = ((int)value).ToString();
        Application.targetFrameRate = (int)value;
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

        t.font = UIFontHelper.LoadForText(content, fontSize); // v0.1.1：按内容自动选字体（中文微软雅黑 / 英文 Consolas）
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
        bgImg.color = new Color(1f, 1f, 1f, 0.18f);

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

        Text t = CreateText("Text", rt, label, fontSize, Color.white);
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

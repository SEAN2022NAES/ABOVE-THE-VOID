using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F3 开发者模式：
///   - 按 F3 切换显示 / 隐藏；
///   - 左上角信息面板显示玩家世界坐标 x / y / z（单位 = 坐标轴刻度单位长度，
///     直接取 Camera.main.transform.position 的实际分量，F2 精度）；
///   - 显示精确开始时间：本局运行起始时刻（DateTime，精确到毫秒）与
///     运行时长（基于 unscaledDeltaTime 累计，暂停 / timeScale=0 时继续走动）。
/// 纯代码构建 UI，挂到任意物体即可。
/// </summary>
public class DevOverlayController : MonoBehaviour
{
    [Header("按键")]
    [Tooltip("开发者模式切换键")]
    public KeyCode toggleKey = KeyCode.F3;

    [Header("UI")]
    [Tooltip("面板文字大小")]
    public int fontSize = 18;
    [Tooltip("面板文字颜色")]
    public Color textColor = UITheme.TextSub;

    private Canvas canvas;
    private GameObject overlayRoot;
    private Text overlayText;

    private DateTime startTime;   // 精确开始时间（墙钟，毫秒级）
    private float runtimeUnscaled; // 运行时长累计（unscaled 真实时间，暂停 / timeScale=0 时继续走动）

    private float fpsValue = 60f; // 平滑帧率（每 0.5s 采样一次）
    private float fpsAccumulator; // 帧率采样累计耗时（unscaled）
    private int fpsFrameCount;    // 帧率采样累计帧数

    public bool IsVisible => overlayRoot != null && overlayRoot.activeSelf;

    private void Awake()
    {
        startTime = DateTime.Now;
        runtimeUnscaled = 0f;

        BuildUI();
        overlayRoot.SetActive(false); // 默认隐藏，按 F3 打开
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    private void LateUpdate()
    {
        // 运行时长用 unscaledDeltaTime 累计：timeScale=0（暂停）时依旧继续走动
        runtimeUnscaled += Time.unscaledDeltaTime;
        SampleFps(); // 每帧累计采样，0.5s 平滑一次帧率（面板隐藏时也持续采样，避免打开瞬间显示旧值）
        if (overlayRoot != null && overlayRoot.activeSelf)
            UpdateText();
    }

    /// <summary>切换开发者面板显示状态。</summary>
    public void Toggle()
    {
        if (overlayRoot != null)
            overlayRoot.SetActive(!overlayRoot.activeSelf);
    }

    // ---------------- UI 构建 ----------------

    private void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = CreateRect("DevOverlay", canvas.transform, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(520f, 220f), new Vector2(0f, 1f));
        overlayRoot = root.gameObject;

        overlayText = CreateText("DevOverlayText", root, "", fontSize, textColor, TextAnchor.UpperLeft);
        RectTransform txtRt = overlayText.rectTransform;
        txtRt.anchorMin = new Vector2(0f, 1f);
        txtRt.anchorMax = new Vector2(1f, 1f);
        txtRt.pivot = new Vector2(0f, 1f);
        txtRt.anchoredPosition = Vector2.zero;
        txtRt.sizeDelta = new Vector2(0f, 0f);

        overlayText.font = UIFontHelper.LoadMono();
        overlayText.supportRichText = false;
        overlayText.horizontalOverflow = HorizontalWrapMode.Overflow;
        overlayText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private void UpdateText()
    {
        Camera cam = Camera.main;
        Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
        TimeSpan elapsed = TimeSpan.FromSeconds(Mathf.Max(0f, runtimeUnscaled));

        overlayText.text =
            "ABOVE THE VOID - Dev Mode [F3]\n" +
            "Position: x=" + pos.x.ToString("F2") + "  y=" + pos.y.ToString("F2") + "  z=" + pos.z.ToString("F2") + "\n" +
            "Start: " + startTime.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\n" +
            "Runtime: " + FormatElapsed(elapsed) + "\n" +
            "FPS: " + fpsValue.ToString("F0");
    }

    /// <summary>累计帧数与耗时，每 0.5s 更新一次平滑帧率（使用 unscaledDeltaTime，不受 timeScale / 暂停影响）。</summary>
    private void SampleFps()
    {
        fpsAccumulator += Time.unscaledDeltaTime;
        fpsFrameCount++;
        if (fpsAccumulator >= 0.5f)
        {
            fpsValue = fpsFrameCount / fpsAccumulator;
            fpsAccumulator = 0f;
            fpsFrameCount = 0;
        }
    }

    /// <summary>重置运行基准时间（再次进入 Level 3 时调用，Start / Runtime 全部重新计时）。</summary>
    public void ResetRuntime()
    {
        startTime = DateTime.Now;
        runtimeUnscaled = 0f;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.Hours.ToString("00") + ":" +
               elapsed.Minutes.ToString("00") + ":" +
               elapsed.Seconds.ToString("00") + "." +
               elapsed.Milliseconds.ToString("000");
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
        text.font = UIFontHelper.LoadMono();
        text.raycastTarget = false;
        return text;
    }
}

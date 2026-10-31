using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面正上方常驻时间提示：
///   - 顶部居中显示游戏实际运行时间（精确到秒）；
///   - 未满 1 小时显示 mm:ss，满 1 小时后显示 HH:mm:ss（如 01:23:42 = 1小时23分42秒）；
///   - 使用 Consolas 字体（Resources/Fonts/consolas.ttf，系统 Consolas 已复制入项目），
///     缺失时回退 arial / 引擎内置字体；
///   - 基于 Time.time（受 timeScale 影响），暂停（timeScale=0）时计时冻结。
/// 纯代码构建 UI，挂到任意物体即可。
/// </summary>
public class TimeHintController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("时间文字大小")]
    public int fontSize = 40;
    [Tooltip("时间文字颜色")]
    public Color textColor = Color.white;
    [Tooltip("距屏幕上边缘距离（像素）")]
    public float topOffset = 24f;

    private Canvas canvas;
    private Text timeText;
    private float startTime; // 本局开始时的 Time.time，作为运行时长基准

    private void Awake()
    {
        startTime = Time.time;
        BuildUI();
    }

    private void LateUpdate()
    {
        if (timeText != null)
            timeText.text = FormatRuntime(Time.time - startTime);
    }

    /// <summary>将运行秒数格式化为时间提示文本：未满 1 小时显示 mm:ss，满 1 小时后显示 HH:mm:ss。</summary>
    private static string FormatRuntime(float seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
        return t.TotalHours >= 1d
            ? t.ToString(@"hh\:mm\:ss")
            : t.ToString(@"mm\:ss");
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

        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = CreateRect("TimeHintRoot", canvas.transform, new Vector2(0.5f, 1f), new Vector2(0f, -topOffset), new Vector2(640f, 80f), new Vector2(0.5f, 1f));

        timeText = CreateText("TimeHintText", root, "00:00", fontSize, textColor, TextAnchor.MiddleCenter);
        RectTransform txtRt = timeText.rectTransform;
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        timeText.font = UIFontHelper.LoadMono();
        timeText.fontStyle = FontStyle.Bold;
        timeText.supportRichText = false;
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

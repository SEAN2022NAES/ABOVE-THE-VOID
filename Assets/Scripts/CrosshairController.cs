using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minecraft 风格反色十字准心：
///   - 屏幕中心经典十字（上 / 下 / 左 / 右四段）+ 中心点；
///   - 使用反色材质（Blend OneMinusDstColor Zero），准心颜色对背景取反，任意背景下均清晰；
///   - 反色材质缺失时自动回退「白芯 + 黑描边」双层方案，保证可见性。
/// 纯代码构建，挂到任意物体上即可自动创建 Overlay Canvas。
/// </summary>
public class CrosshairController : MonoBehaviour
{
    [Header("准心尺寸（像素）")]
    [Tooltip("十字单段长度（不含中心间隙）")]
    public float segmentLength = 12f;
    [Tooltip("线段粗细")]
    public float segmentThickness = 3f;
    [Tooltip("十字与中心点之间的间隙")]
    public float centerGap = 4f;
    [Tooltip("中心点尺寸")]
    public float centerSize = 5f;

    private Canvas canvas;
    private bool useInvertMaterial;

    private void Awake()
    {
        BuildCanvas();
        BuildCrosshair();
    }

    // ---------------- UI 构建 ----------------

    private void BuildCanvas()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();
    }

    private void BuildCrosshair()
    {
        RectTransform root = CreateRect("CrosshairRoot", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        Material invertMat = CreateInvertMaterial();
        useInvertMaterial = invertMat != null;

        // 上 / 下 / 左 / 右四段
        CreateSegment(root, "Segment_Up",    new Vector2(0f,                centerGap + segmentLength * 0.5f), new Vector2(segmentThickness, segmentLength), invertMat);
        CreateSegment(root, "Segment_Down",  new Vector2(0f,               -(centerGap + segmentLength * 0.5f)), new Vector2(segmentThickness, segmentLength), invertMat);
        CreateSegment(root, "Segment_Left",  new Vector2(-(centerGap + segmentLength * 0.5f), 0f), new Vector2(segmentLength, segmentThickness), invertMat);
        CreateSegment(root, "Segment_Right", new Vector2( centerGap + segmentLength * 0.5f, 0f), new Vector2(segmentLength, segmentThickness), invertMat);

        // 中心点
        CreateSegment(root, "CenterDot", Vector2.zero, new Vector2(centerSize, centerSize), invertMat);
    }

    /// <summary>尝试加载反色材质；找不到时返回 null（走黑白描边回退方案）。</summary>
    private Material CreateInvertMaterial()
    {
        Shader shader = Resources.Load<Shader>("Shaders/InvertCrosshair");
        if (shader == null)
            shader = Shader.Find("Custom/InvertCrosshair");
        if (shader == null)
            return null;
        return new Material(shader);
    }

    private void CreateSegment(RectTransform parent, string name, Vector2 pos, Vector2 size, Material invertMat)
    {
        RectTransform rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), pos, size);
        Image img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;

        if (useInvertMaterial)
        {
            // 反色材质：白色像素输出 = 屏幕背景的反色
            img.material = invertMat;
            img.color = UITheme.TextPrimary;
        }
        else
        {
            // 回退：黑描边（底层稍大）+ 白芯（上层）
            RectTransform borderRt = CreateRect(name + "_Border", parent, new Vector2(0.5f, 0.5f), pos, size + new Vector2(2f, 2f));
            Image borderImg = borderRt.gameObject.AddComponent<Image>();
            borderImg.color = UITheme.WithAlpha(UITheme.Void, 0.9f);
            borderImg.raycastTarget = false;

            img.color = UITheme.TextPrimary;
        }
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }
}

using UnityEngine;

/// <summary>
/// 在原点绘制 3D 直角坐标系可视化：
/// X 轴（红）、Y 轴（绿，深度方向）、Z 轴（蓝，竖直方向），带刻度与箭头标识。
/// 挂到任意空物体上，Awake 时自动创建全部可视化元素，无需手工搭建预制体。
/// </summary>
public class CoordinateAxesDrawer : MonoBehaviour
{
    [Header("轴长与刻度")]
    [Tooltip("坐标范围的正负最大值")]
    public float axisLength = 20f;

    [Tooltip("刻度间距（单位：米/格）")]
    public float tickStep = 1f;

    [Tooltip("刻度短线长度")]
    public float tickSize = 0.08f;

    [Header("线条外观")]
    [Tooltip("主轴宽度")]
    public float axisWidth = 0.06f;

    [Tooltip("刻度线宽度")]
    public float tickWidth = 0.04f;

    [Tooltip("箭头大小")]
    public float arrowSize = 0.4f;

    [Header("颜色（X / Y / Z 默认 红 / 绿 / 蓝）")]
    public Color xColor = Color.blue;
    public Color yColor = Color.red;
    public Color zColor = Color.green;
    public Color tickColor = new Color(1f, 1f, 1f, 0.85f);

    private void Awake()
    {
        DrawAxes();
    }

    private void DrawAxes()
    {
        // 当前视图约定：Z 竖直且居中，Y 为深度方向并投影到左侧，X 投影到右侧。
        Vector3 xTip = new Vector3(axisLength, 0f, 0f);
        Vector3 yTip = new Vector3(0f, 0f, axisLength);
        // Z 轴正方向向上；标签单独做水平镜像以匹配当前图示。
        Vector3 zTip = new Vector3(0f, axisLength, 0f);

        // 正方向使用轴颜色，负方向使用对应的反色。
        DrawLine(Vector3.zero, xTip, xColor, axisWidth, "Axis_X");
        DrawLine(Vector3.zero, yTip, yColor, axisWidth, "Axis_Y");
        DrawLine(Vector3.zero, zTip, zColor, axisWidth, "Axis_Z");
        DrawLine(Vector3.zero, -xTip, InvertColor(xColor), axisWidth, "Axis_Negative_X");
        DrawLine(Vector3.zero, -yTip, InvertColor(yColor), axisWidth, "Axis_Negative_Y");
        DrawLine(Vector3.zero, -zTip, InvertColor(zColor), axisWidth, "Axis_Negative_Z");

        // 正方向箭头
        DrawArrowTip(xTip, Vector3.right, Vector3.up, xColor, "Arrow_X");
        DrawArrowTip(yTip, Vector3.forward, Vector3.up, yColor, "Arrow_Y");
        DrawArrowTip(zTip, Vector3.up, Vector3.right, zColor, "Arrow_Z");

        // 仅 ±1 刻度使用轴颜色，其余刻度统一为白色。
        DrawTicks(Vector3.right, Vector3.up, xColor, "Tick_X");
        DrawTicks(Vector3.forward, Vector3.up, yColor, "Tick_Y");
        DrawTicks(Vector3.up, Vector3.right, zColor, "Tick_Z");

        // 轴名称标签（Billboard 面向玩家，加载外部图片素材，便于替换）
        DrawLabel(xTip, "X", "Label_X", xColor, false, true);
        DrawLabel(yTip + Vector3.up * 0.45f, "Y", "Label_Y", yColor);
        DrawLabel(zTip + Vector3.up * 0.45f, "Z", "Label_Z", zColor, true);
    }

    /// <summary>创建一条线段（LineRenderer）。</summary>
    private LineRenderer DrawLine(Vector3 start, Vector3 end, Color color, float width, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = width;
        lr.endWidth = width;
        lr.numCapVertices = 4;
        lr.startColor = color;
        lr.endColor = color;
        lr.sharedMaterial = CreateColorMaterial(color);
        return lr;
    }

    /// <summary>在轴端绘制三角箭头（两条斜线段）。</summary>
    private void DrawArrowTip(Vector3 tip, Vector3 dir, Vector3 side, Color color, string name)
    {
        Vector3 basePoint = tip - dir * arrowSize;
        Vector3 s1 = side * arrowSize * 0.35f;
        Vector3 s2 = -side * arrowSize * 0.35f;
        DrawLine(tip, basePoint + s1, color, axisWidth, name + "_a");
        DrawLine(tip, basePoint + s2, color, axisWidth, name + "_b");
    }

    /// <summary>沿指定轴绘制正负方向等距刻度短线。</summary>
    private void DrawTicks(Vector3 axisDir, Vector3 perpDir, Color positiveColor, string namePrefix)
    {
        for (float v = -axisLength; v <= axisLength + 0.001f; v += tickStep)
        {
            if (Mathf.Abs(v) < tickStep * 0.1f)
                continue;

            Vector3 center = axisDir * v;
            bool unitTick = Mathf.Abs(Mathf.Abs(v) - tickStep) < 0.001f;
            Color tickLineColor = !unitTick
                ? Color.white
                : v > 0f ? positiveColor : InvertColor(positiveColor);
            DrawLine(center - perpDir * tickSize * 0.5f,
                     center + perpDir * tickSize * 0.5f,
                     tickLineColor,
                     tickWidth,
                     namePrefix + "_" + v.ToString("0.##"));
        }
    }

    /// <summary>
    /// 创建轴末端漂浮提示。使用 TextMesh，避免 SVG 导入设置导致标签不可见。
    /// </summary>
    private void DrawLabel(Vector3 pos, string label, string name, Color axisColor,
        bool horizontalFlip = false, bool bottomAlign = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        TextMesh text = go.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = bottomAlign ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = 60;
        text.characterSize = 0.0875f;
        text.color = axisColor;

        BillboardToCamera billboard = go.AddComponent<BillboardToCamera>();
        billboard.SetConstantScreenSize(10f);
        billboard.SetHorizontalFlip(horizontalFlip);
    }

    private Color InvertColor(Color color)
    {
        return new Color(1f - color.r, 1f - color.g, 1f - color.b, color.a);
    }

    /// <summary>
    /// 创建纯色材质。优先 Sprites/Default（顶点色模式，不设置材质色，
    /// 颜色完全由 LineRenderer 的 startColor/endColor 控制，保证轴为鲜艳的红绿蓝）；
    /// 找不到时降级 Unlit/Color（用材质色兜底）。
    /// </summary>
    private Material CreateColorMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        bool vertexColorSupported = shader != null;
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        if (!vertexColorSupported)
            mat.color = color; // Unlit/Color 无顶点色，用材质色兜底
        return mat;
    }
}

using UnityEngine;

/// <summary>
/// 在原点绘制 3D 直角坐标系可视化：
/// X 轴（蓝，深度方向）、Y 轴（红，右方向）、Z 轴（绿，竖直方向），带刻度与箭头标识。
/// 挂到任意空物体上，Awake 时自动创建全部可视化元素，无需手工搭建预制体。
/// </summary>
public class CoordinateAxesDrawer : MonoBehaviour
{
    [Header("轴长与刻度")]
    [Tooltip("坐标范围的正负最大值")]
    public float axisLength = 10f;

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

    [Header("颜色（X / Y / Z 默认 蓝 / 红 / 绿）")]
    public Color xColor = Color.blue;
    public Color yColor = Color.red;
    public Color zColor = Color.green;
    public Color tickColor = new Color(1f, 1f, 1f, 0.85f);

    [Header("轴名称标签（三轴统一，保证距离 / 字号 / 大小完全一致）")]
    [Tooltip("标签中心到所属轴的垂直距离（世界米）。三个轴共用同一个值，"
             + "方向恒取「垂直于轴且朝上」：一般轴用世界 +Y，竖直轴（世界 +Y）改用世界 +X")]
    public float labelOffset = 0.5f;

    [Tooltip("标签字形尺寸（TextMesh.fontSize，三轴统一）")]
    public int labelFontSize = 60;

    [Tooltip("标签字身缩放（TextMesh.characterSize，三轴统一）")]
    public float labelCharacterSize = 0.0875f;

    [Tooltip("标签恒定屏幕大小参考距离（BillboardToCamera，三轴统一，保证远近观感一致）")]
    public float labelScreenSize = 10f;

    private void Awake()
    {
        // 先清除本物体下旧的可视化元素，再重建：
        // 避免 Play 模式热重载脚本时旧箭头/旧标签残留叠加成"延长线"或"小太阳"。
        ClearExistingDrawings();
        DrawAxes();
    }

    /// <summary>清除本物体下此前程序创建的可视化物体（仅限本脚本命名的前缀，不影响场景其它物体）。</summary>
    private void ClearExistingDrawings()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            string childName = child.name;
            if (childName.StartsWith("Axis_") || childName.StartsWith("Tick_") || childName.StartsWith("Label_"))
                Destroy(child.gameObject);
        }
    }

    private void DrawAxes()
    {
        // 当前视图约定：Z 竖直且居中，X（蓝）为深度方向（世界 Z），Y（红）为右方向（世界 X）。
        Vector3 xTip = new Vector3(0f, 0f, axisLength);   // X 轴（蓝）：画在世界 Z 方向（深度）
        Vector3 yTip = new Vector3(axisLength, 0f, 0f);   // Y 轴（红）：画在世界 X 方向（右）
        // Z 轴正方向向上；标签朝向由 BillboardToCamera 统一保证"正面朝相机可正读"，
        // 这里不再使用水平镜像（镜像会让 Z 显示成反字）。
        Vector3 zTip = new Vector3(0f, axisLength, 0f);   // Z 轴（绿）：竖直向上

        // 正方向使用轴颜色，负方向使用对应的反色。
        DrawLine(Vector3.zero, xTip, xColor, axisWidth, "Axis_X");
        DrawLine(Vector3.zero, yTip, yColor, axisWidth, "Axis_Y");
        DrawLine(Vector3.zero, zTip, zColor, axisWidth, "Axis_Z");
        DrawLine(Vector3.zero, -xTip, InvertColor(xColor), axisWidth, "Axis_Negative_X");
        DrawLine(Vector3.zero, -yTip, InvertColor(yColor), axisWidth, "Axis_Negative_Y");
        DrawLine(Vector3.zero, -zTip, InvertColor(zColor), axisWidth, "Axis_Negative_Z");

        // 仅 ±1 刻度使用轴颜色，其余刻度统一为白色。
        DrawTicks(Vector3.forward, Vector3.up, xColor, "Tick_X");
        DrawTicks(Vector3.right, Vector3.up, yColor, "Tick_Y");
        DrawTicks(Vector3.up, Vector3.right, zColor, "Tick_Z");

        // 轴名称标签（Billboard 面向玩家）。
        // 排布规则（三轴完全一致，避免历史上 X 偏位、Y 压轴身、Z 字号不一致）：
        //   labelPos = 轴正方向端点 + LabelOffsetDir(轴方向) * labelOffset
        //   LabelOffsetDir 恒取「垂直于该轴且朝上」：一般轴用世界 +Y，竖直轴（世界 +Y）用世界 +X；
        //   锚点统一 MiddleCenter（字形中心正好落在计算点上，不随 Billboard 旋转而漂移）。
        DrawLabel(xTip + LabelOffsetDir(xTip) * labelOffset, "X", "Label_X", xColor);
        DrawLabel(yTip + LabelOffsetDir(yTip) * labelOffset, "Y", "Label_Y", yColor);
        DrawLabel(zTip + LabelOffsetDir(zTip) * labelOffset, "Z", "Label_Z", zColor);
    }

    /// <summary>
    /// 计算轴名称标签的偏移方向：垂直于轴且朝向"上方"。
    /// 优先世界 +Y；当轴本身接近竖直（即世界 +Y 轴）时，+Y 与轴平行无法作为偏移，
    /// 改用世界 +X（侧向），同样与轴垂直、且不会压到轴身上。
    /// </summary>
    private static Vector3 LabelOffsetDir(Vector3 axisTip)
    {
        Vector3 axisDir = axisTip.sqrMagnitude > 0.0001f ? axisTip.normalized : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(axisDir, Vector3.up)) > 0.9f)
            return Vector3.right;
        return Vector3.up;
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
    /// 创建轴末端漂浮提示（TextMesh）。三个轴的标签用完全相同的字号 / 字身尺寸 /
    /// 恒定屏幕大小 / 锚点（MiddleCenter），保证视觉上与轴的距离、大小一致。
    /// 锚点取 MiddleCenter 是关键：字形中心恒等于传入的世界坐标，
    /// BillBoard 旋转时标签不会像 LowerCenter 那样绕着点偏移、掉进轴身。
    /// </summary>
    private void DrawLabel(Vector3 pos, string label, string name, Color axisColor)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        TextMesh text = go.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;   // 三轴统一居中：字形中心 = pos
        text.alignment = TextAlignment.Center;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = labelFontSize;           // 三轴统一字号
        text.characterSize = labelCharacterSize; // 三轴统一字身尺寸
        text.color = axisColor;

        BillboardToCamera billboard = go.AddComponent<BillboardToCamera>();
        billboard.SetConstantScreenSize(labelScreenSize); // 三轴统一参考距离 → 屏幕上大小一致
        billboard.SetHorizontalFlip(false);               // 三轴统一不镜像，正面正读
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

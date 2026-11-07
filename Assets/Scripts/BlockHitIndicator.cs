using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 准心指向检测（屏幕中心射线）：
///   - 每帧从相机中心发射射线，RaycastAll 命中多个长方体时只显示最近的一个（透视遮挡原则）；
///   - 命中长方体调用 FlyingBlock.SetHighlight 高亮（高亮时显示中心原点标记）；
///   - 屏幕底部显示编号、英文类型与几何中心坐标（加粗描边，不与目标长方体重叠）；
///   - 相机处于任一活跃长方体内部时，屏幕中上方以英文提示方块参数并告知玩家处在内部；
///   - 未命中时清除高亮并隐藏文本。
/// </summary>
public class BlockHitIndicator : MonoBehaviour
{
    [Header("检测")]
    [Tooltip("射线最大距离")]
    public float rayDistance = 500f;

    [Header("UI")]
    [Tooltip("HUD 字体大小")]
    public int fontSize = 36;
    [Tooltip("文本颜色")]
    public Color textColor = Color.white;

    private Canvas canvas;
    private Text infoText;
    private Text insideText; // 相机处于长方体内部时的英文提示
    private FlyingBlock current;

    // 「选中方块信息」的水平位置控制：默认居中；当代码编辑器教学（左右平分屏）开启时，
    // 由 CodeSyntaxTutorialController 把该行移到左半屏中心，避免与右侧任务卡 / 左侧底部提示条重叠。
    private static BlockHitIndicator instance;
    private static float globalHorizontalOffset;
    private RectTransform infoRt;

    /// <summary>底部「选中方块信息」行的垂直位置（屏幕坐标偏移，负值向下）：上移到提示条之上。</summary>
    private const float InfoBottomY = -376f;

    public static BlockHitIndicator Instance
    {
        get { return instance; }
    }

    /// <summary>全局设置「选中方块信息」水平偏移（0 = 屏幕居中，-480 = 左半屏中心）。</summary>
    public static void SetHorizontalOffsetGlobal(float offsetX)
    {
        globalHorizontalOffset = offsetX;
        if (instance != null)
            instance.ApplyHorizontalOffset();
    }

    private void ApplyHorizontalOffset()
    {
        if (infoRt != null)
            infoRt.anchoredPosition = new Vector2(globalHorizontalOffset, InfoBottomY);
    }

    private void Awake()
    {
        instance = this;
        BuildUI();
        ApplyHorizontalOffset();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        FlyingBlock target = DetectNearestBlock();
        if (target != current)
        {
            if (current != null && !current.Destroyed)
                current.SetHighlight(false);
            current = target;
            if (current != null)
                current.SetHighlight(true);
        }

        if (current != null && !current.Destroyed)
        {
            Vector3 c = current.GeometryCenter;
            // 进度统一显示「剩余 HP / 该方块最大 HP」（递减：N/N → … → 1/N），吃满即死亡不显示 0
            // 蓝色 BOMB 块 maxHits 恒为 1，因此始终显示 1/1（未被击中即已击毁，不会出现中间档）
            infoText.text = "#" + current.BlockId + " " + FlyingBlock.GetTypeText(current.Type) +
                            "   HP: " + current.RemainingHits + "/" + current.maxHits +
                            "   Center: (" + c.x.ToString("F2") + ", " + c.y.ToString("F2") + ", " + c.z.ToString("F2") + ")";
        }
        else
        {
            infoText.text = "";
        }

        // 相机处于长方体内部：英文提示参数并告知玩家位置（自由视角飞行进入时触发）
        FlyingBlock inside = DetectContainingBlock();
        if (inside != null && !inside.Destroyed)
        {
            Vector3 c = inside.GeometryCenter;
            insideText.text = "YOU ARE INSIDE A BLOCK  #" + inside.BlockId + " " + FlyingBlock.GetTypeText(inside.Type) +
                              "  HP: " + inside.RemainingHits + "/" + inside.maxHits +
                              "  Size: " + inside.size.x.ToString("F1") + "x" + inside.size.y.ToString("F1") + "x" + inside.size.z.ToString("F1") +
                              "  Center: (" + c.x.ToString("F2") + ", " + c.y.ToString("F2") + ", " + c.z.ToString("F2") + ")";
        }
        else
        {
            insideText.text = "";
        }
    }

    /// <summary>屏幕中心射线检测：返回最近的未被销毁长方体（无则 null）。</summary>
    private FlyingBlock DetectNearestBlock()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return null;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance);

        FlyingBlock nearest = null;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            FlyingBlock block = hits[i].collider.GetComponentInParent<FlyingBlock>();
            if (block == null || block.Destroyed)
                continue;
            if (hits[i].distance < nearestDist)
            {
                nearestDist = hits[i].distance;
                nearest = block;
            }
        }
        return nearest;
    }

    /// <summary>相机是否处于某个活跃长方体内部：命中返回该长方体（无则 null）。</summary>
    private FlyingBlock DetectContainingBlock()
    {
        Camera cam = Camera.main;
        if (cam == null || FlyingBlockSpawner.Instance == null)
            return null;

        Vector3 p = cam.transform.position;
        foreach (FlyingBlock block in FlyingBlockSpawner.Instance.ActiveBlocks)
        {
            if (block == null || block.Destroyed)
                continue;
            Vector3 half = block.HalfExtents;
            Vector3 c = block.transform.position;
            if (Mathf.Abs(p.x - c.x) <= half.x &&
                Mathf.Abs(p.y - c.y) <= half.y &&
                Mathf.Abs(p.z - c.z) <= half.z)
                return block;
        }
        return null;
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

        GameObject go = new GameObject("BlockTargetInfo", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // 底部「选中方块信息」：上移到常驻提示条（屏幕底部 40~104）上方，两者不再重叠
        rt.anchoredPosition = new Vector2(globalHorizontalOffset, InfoBottomY);
        rt.sizeDelta = new Vector2(900f, 72f);
        infoRt = rt;

        infoText = go.AddComponent<Text>();
        infoText.font = UIFontHelper.LoadMono();
        infoText.fontSize = fontSize;
        infoText.color = textColor;
        infoText.alignment = TextAnchor.MiddleCenter;
        infoText.supportRichText = false;
        infoText.raycastTarget = false;
        // 单行完整显示：禁止换行与垂直截断，避免末尾 Center 的 Z 坐标被吞掉
        infoText.horizontalOverflow = HorizontalWrapMode.Overflow;
        infoText.verticalOverflow = VerticalWrapMode.Overflow;

        // 黑色描边增强对比：深色/浅色背景上均清晰可读（等效反色效果）
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);

        // 相机处于长方体内部时的提示（屏幕中上方，英文警示，亮黄描边）
        GameObject insideGo = new GameObject("InsideBlockInfo", typeof(RectTransform));
        RectTransform irt = insideGo.GetComponent<RectTransform>();
        irt.SetParent(canvas.transform, false);
        irt.anchorMin = new Vector2(0.5f, 0.5f);
        irt.anchorMax = new Vector2(0.5f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = new Vector2(0f, 240f);
        irt.sizeDelta = new Vector2(1900f, 64f);

        insideText = insideGo.AddComponent<Text>();
        insideText.font = UIFontHelper.LoadMono();
        insideText.fontSize = 30;
        insideText.color = new Color(1f, 0.85f, 0.2f);
        insideText.alignment = TextAnchor.MiddleCenter;
        insideText.supportRichText = false;
        insideText.raycastTarget = false;
        // 单行完整显示：禁止换行与垂直截断，避免末尾 Center 的 Z 坐标被吞掉
        insideText.horizontalOverflow = HorizontalWrapMode.Overflow;
        insideText.verticalOverflow = VerticalWrapMode.Overflow;

        Outline insideOutline = insideGo.AddComponent<Outline>();
        insideOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        insideOutline.effectDistance = new Vector2(3f, -3f);
    }
}

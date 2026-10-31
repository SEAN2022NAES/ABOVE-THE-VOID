using System;
using UnityEngine;

/// <summary>
/// 飞行长方体单体（纯代码构建）：
///   - 尺寸：长/宽/高为浮点（生成器保证每边 ≥ minSide、三边总和 ≤ 3），使用标准立方体网格 + 加粗线框棱；
///   - 类型：Attack=红 / Heal=绿 / Bomb=蓝；
///   - 运动：仅沿生成时的单个坐标轴方向朝原点飞行（其余轴坐标不变）；任一 AABB 边缘首次到达任意轴坐标 0 平面时销毁；
///   - 防重叠：移动前通过 FlyingBlockSpawner 检测目标 AABB，若与其它长方体相交则本帧原地等待；
///   - 高亮：准心指向时整体变亮、线框加粗，并在几何中心显示高亮原点标记；
///   - 行进面：长方体使用 6-submesh 单位立方体网格，行进方向对应的面渲染为白色；
///   - 中心标记：从白色面一侧查看时中心点渲染为黑色，其余角度为白色；
///   - 激光预留：外部可通过 ApplyLaserHit / DestroyBlock 接口受控销毁，生命周期不写死。
/// </summary>
public class FlyingBlock : MonoBehaviour
{
    /// <summary>长方体类型（用于颜色与 HUD 英文显示）。</summary>
    public enum BlockType
    {
        Attack, // 攻击型：红
        Heal,   // 恢复型：绿
        Bomb    // 炸弹型：蓝
    }

    [Header("配置")]
    [Tooltip("尺寸（长/宽/高，浮点，每边 ≥ 0.4，三边和 ≤ 3）")]
    public Vector3 size = Vector3.one;
    [Tooltip("飞行速度（单位/秒）")]
    public float speed = 2f;
    [Tooltip("防重叠检测允许的最小间距")]
    public float overlapMargin = 0.05f;

    /// <summary>类型。</summary>
    public BlockType Type { get; private set; }

    /// <summary>几何中心（世界坐标，长方体不旋转时即 transform.position）。</summary>
    public Vector3 GeometryCenter => colliderRef != null ? colliderRef.bounds.center : transform.position;

    /// <summary>半尺寸（AABB 半宽/半高/半深）。</summary>
    public Vector3 HalfExtents => size * 0.5f;

    /// <summary>编号（生成器分配，HUD 显示）。</summary>
    public int BlockId { get; private set; }

    /// <summary>是否已进入销毁流程。</summary>
    public bool Destroyed { get; private set; }

    /// <summary>销毁事件（供生成器注销登记 / 未来激光系统监听）。</summary>
    public event Action<FlyingBlock> OnDestroyed;

    private Material blockMaterial;
    private Material whiteFaceMaterial;     // 行进方向面专用白色材质（恒白，不受高亮/类型色影响）
    private GameObject centerMarkerWhite;   // 中心标记-白球（固定白色材质）
    private GameObject centerMarkerBlack;   // 中心标记-黑球（固定黑色材质，连线穿过白色面时显示）
    private int whiteFaceIndex;             // 行进方向面对应的 submesh 下标（0=+X、1=-X、2=+Y、3=-Y、4=+Z、5=-Z）
    private LineRenderer[] edgeRenderers = new LineRenderer[12];
    private Material edgeMaterial;
    private BoxCollider colliderRef;
    private Vector3 moveDir = Vector3.zero;
    private bool highlighted;

    /// <summary>初始化（由生成器调用）。</summary>
    public void Init(BlockType type, Vector3 blockSize, Vector3 position, float moveSpeed, int blockId, Vector3 dir)
    {
        Type = type;
        BlockId = blockId;
        size = new Vector3(
            Mathf.Max(blockSize.x, 0.4f),
            Mathf.Max(blockSize.y, 0.4f),
            Mathf.Max(blockSize.z, 0.4f)); // 每边保底下限，三边和 ≤ 3 由生成器保证
        speed = moveSpeed;
        transform.position = position;
        // 只沿单个轴方向移动：dir 由生成器按生成轴给出（其余轴为 0）
        moveDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : (Vector3.zero - position).normalized;

        BuildVisual();
        ReapplyColor();
    }

    /// <summary>设置移动方向（生成器如需变化方向/速度时调用，仅单轴分量）。</summary>
    public void SetMoveDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude > 0.0001f)
            moveDir = dir.normalized;
    }

    // ---------------- 对外接口（激光预留） ----------------

    /// <summary>激光命中占位：后续可扩展血量/分层击穿，当前直接受控销毁。</summary>
    public void ApplyLaserHit(float damage = 1f)
    {
        // TODO: 接入激光系统后按 damage 累计血量，穿透到下一层后再销毁。
        DestroyBlock("laser_hit");
    }

    /// <summary>受控销毁：注销登记 → 触发事件 → 销毁物体。生命周期不写死，外部可调用。</summary>
    public void DestroyBlock(string reason = "")
    {
        if (Destroyed)
            return;
        Destroyed = true;

        if (FlyingBlockSpawner.Instance != null)
            FlyingBlockSpawner.Instance.Unregister(this);

        OnDestroyed?.Invoke(this);

        if (!string.IsNullOrEmpty(reason))
            Debug.Log("[FlyingBlock] destroyed, reason=" + reason + " type=" + GetTypeText(Type) +
                      " pos=" + transform.position.ToString("F2"));

        Destroy(gameObject);
    }

    /// <summary>高亮 / 取消高亮（由准心检测指示器调用）。</summary>
    public void SetHighlight(bool on)
    {
        if (highlighted == on || Destroyed)
            return;
        highlighted = on;
        ReapplyColor();
    }

    // ---------------- 每帧逻辑 ----------------

    private void Update()
    {
        if (Destroyed)
            return;

        // 1) 防重叠移动：目标位置若与其它长方体相交则本帧原地等待
        Vector3 nextPos = transform.position + moveDir * speed * Time.deltaTime;
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null || spawner.IsPositionFree(nextPos, HalfExtents, this))
        {
            transform.position = nextPos;
        }

        // 2) 边界销毁：任一 AABB 边缘首次到达任意轴坐标 = 0（0 平面进入长方体）即销毁
        Vector3 center = transform.position;
        Vector3 half = HalfExtents;
        Vector3 min = center - half;
        Vector3 max = center + half;
        if ((min.x <= 0f && max.x >= 0f) ||
            (min.y <= 0f && max.y >= 0f) ||
            (min.z <= 0f && max.z >= 0f))
        {
            DestroyBlock("edge_reached_zero");
        }
    }

    // ---------------- 中心标记视角变色 ----------------

    private void LateUpdate()
    {
        UpdateCenterMarkerColor();
    }

    /// <summary>
    /// 中心标记颜色：仅当中心标记显示（高亮）时执行；
    /// 相机与中心的连线**真正穿过白色行进面**（连线与白面平面的交点落在白色面矩形内部，且相机在白面外侧）时显示黑球，否则显示白球。
    /// 黑/白球各为固定颜色材质，只切换 active，避免运行时改材质颜色。
    /// </summary>
    private void UpdateCenterMarkerColor()
    {
        if (Destroyed || centerMarkerWhite == null || centerMarkerBlack == null)
            return;
        if (!centerMarkerWhite.activeSelf && !centerMarkerBlack.activeSelf)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 center = GeometryCenter;
        Vector3 half = HalfExtents;
        Vector3 toCam = cam.transform.position - center;

        // 白面法线 = 行进方向（单轴），与 ComputeWhiteFaceIndex 用同一套主导轴规则
        float ax = Mathf.Abs(moveDir.x), ay = Mathf.Abs(moveDir.y), az = Mathf.Abs(moveDir.z);
        Vector3 faceNormal;
        if (ax >= ay && ax >= az)
            faceNormal = moveDir.x > 0f ? Vector3.right : Vector3.left;   // 白面在 +X / -X
        else if (ay >= az)
            faceNormal = moveDir.y > 0f ? Vector3.up : Vector3.down;      // 白面在 +Y / -Y
        else
            faceNormal = moveDir.z > 0f ? Vector3.forward : Vector3.back; // 白面在 +Z / -Z

        // 白面平面位置：中心 + 法线 * halfAlongDir（白面到中心的距离 = 沿法线的半尺寸）
        float halfAlongDir = Vector3.Dot(half, faceNormal);
        // 相机在白面法线方向上的投影距离
        float proj = Vector3.Dot(toCam, faceNormal);

        bool rayHitsWhiteFace = false;
        if (proj > halfAlongDir) // 相机位于白面外侧（连线方向指向白面一侧）
        {
            // 连线与白面平面的交点相对中心的偏移（t = halfAlongDir / proj ∈ (0,1)，交点在相机与中心之间）
            Vector3 hit = toCam * (halfAlongDir / proj);
            // 交点必须落在白色面矩形内部：白面垂直于 faceNormal，面内两个方向的半尺寸取其余两个轴
            bool insideFace;
            if (Mathf.Abs(faceNormal.x) > 0.5f)
                insideFace = Mathf.Abs(hit.y) <= half.y && Mathf.Abs(hit.z) <= half.z;
            else if (Mathf.Abs(faceNormal.y) > 0.5f)
                insideFace = Mathf.Abs(hit.x) <= half.x && Mathf.Abs(hit.z) <= half.z;
            else
                insideFace = Mathf.Abs(hit.x) <= half.x && Mathf.Abs(hit.y) <= half.y;
            rayHitsWhiteFace = insideFace;
        }

        centerMarkerBlack.SetActive(rayHitsWhiteFace);
        centerMarkerWhite.SetActive(!rayHitsWhiteFace);
    }

    // ---------------- 视觉构建 ----------------

    private void BuildVisual()
    {
        // 标准立方体 Primitive：保留自带 BoxCollider 供射线检测，但将 MeshFilter.mesh 替换为
        // 自建的 6-submesh 单位立方体网格（6 个面各为一个 submesh，可独立着色）
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "CubeMesh";
        cube.transform.SetParent(transform, false);
        cube.transform.localScale = size;
        colliderRef = cube.GetComponent<BoxCollider>();

        MeshFilter meshFilter = cube.GetComponent<MeshFilter>();
        meshFilter.sharedMesh = BuildUnitCubeMesh();

        MeshRenderer meshRenderer = cube.GetComponent<MeshRenderer>();
        blockMaterial = CreateBlockMaterial();
        // 白色行进面：与 blockMaterial 同 shader、恒为纯白
        whiteFaceMaterial = CreateBlockMaterial();
        whiteFaceMaterial.color = Color.white;
        // 根据行进方向（单轴单位向量）决定白色面对应的 submesh 下标
        whiteFaceIndex = ComputeWhiteFaceIndex(moveDir);
        Material[] faceMaterials = new Material[6];
        for (int i = 0; i < faceMaterials.Length; i++)
            faceMaterials[i] = (i == whiteFaceIndex) ? whiteFaceMaterial : blockMaterial;
        meshRenderer.sharedMaterials = faceMaterials;

        // 边框（重新实现）：12 条棱各自独立一根 LineRenderer（仅两端点、平头 cap），
        // 纯白细实线、端点无圆头凸出、无刺点，贴合 P2 细实线风格；高亮时统一加粗。
        BuildEdges();

        // 中心原点标记：实心球体×2（白/黑各一，固定颜色材质），仅选中（高亮）时显示，直径 0.075（高亮联动放大到 0.125），位置保持在几何中心；
        // 穿透渲染由 Custom/CenterMarker 提供（Queue=Overlay + ZTest Always + ZWrite Off），任意角度可见。
        // 黑球只会在"视点-中心连线真正穿过白色面"时由 UpdateCenterMarkerColor 切换显示。
        centerMarkerWhite = CreateCenterMarker("CenterMarkerWhite", Color.white);
        centerMarkerBlack = CreateCenterMarker("CenterMarkerBlack", Color.black);
        centerMarkerWhite.SetActive(false); // 初始隐藏，仅选中（高亮）时由 ReapplyColor 显示
        centerMarkerBlack.SetActive(false);
    }

    /// <summary>创建中心标记球（固定颜色材质，穿透渲染）。</summary>
    private GameObject CreateCenterMarker(string markerName, Color color)
    {
        GameObject markerGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerGo.name = markerName;
        markerGo.transform.SetParent(transform, false);
        markerGo.transform.localScale = Vector3.one * 0.075f;
        MeshRenderer markerRenderer = markerGo.GetComponent<MeshRenderer>();
        Shader markerShader = Shader.Find("Custom/CenterMarker");
        if (markerShader == null)
            markerShader = Resources.Load<Shader>("Shaders/CenterMarker");
        if (markerShader == null)
            markerShader = Shader.Find("Unlit/Color");
        Material markerMat = new Material(markerShader);
        markerMat.color = color; // 创建时固定颜色，运行期不再修改
        markerRenderer.sharedMaterial = markerMat;
        return markerGo;
    }

    /// <summary>
    /// 构建长方体边框（重写实现）：每条棱独立一根 LineRenderer，仅两个端点，
    /// numCapVertices / numCornerVertices = 0 平头无圆角，端点不凸出、无刺点；
    /// 材质统一共享（Sprites/Default，颜色由顶点色 startColor/endColor 控制）。
    /// </summary>
    private void BuildEdges()
    {
        GameObject edgesRoot = new GameObject("Edges");
        edgesRoot.transform.SetParent(transform, false);

        Shader wireShader = Shader.Find("Sprites/Default");
        if (wireShader == null)
            wireShader = Shader.Find("Unlit/Color");
        edgeMaterial = new Material(wireShader);

        Vector3 h = HalfExtents;
        Vector3[] corners = new Vector3[8]
        {
            new Vector3(-h.x, -h.y, -h.z), new Vector3( h.x, -h.y, -h.z),
            new Vector3( h.x, -h.y,  h.z), new Vector3(-h.x, -h.y,  h.z),
            new Vector3(-h.x,  h.y, -h.z), new Vector3( h.x,  h.y, -h.z),
            new Vector3( h.x,  h.y,  h.z), new Vector3(-h.x,  h.y,  h.z)
        };
        int[,] edges = new int[12, 2]
        {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        };

        for (int i = 0; i < edges.GetLength(0); i++)
        {
            GameObject edgeGo = new GameObject("Edge" + i);
            edgeGo.transform.SetParent(edgesRoot.transform, false);
            LineRenderer lr = edgeGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.loop = false;
            lr.numCapVertices = 0;    // 平头：端点不凸出圆头
            lr.numCornerVertices = 0; // 单线段无拐角，无需圆角
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.sharedMaterial = edgeMaterial;
            lr.startColor = new Color(1f, 1f, 1f, 1f);
            lr.endColor = new Color(1f, 1f, 1f, 1f);
            lr.SetPosition(0, corners[edges[i, 0]]);
            lr.SetPosition(1, corners[edges[i, 1]]);
            edgeRenderers[i] = lr;
        }
    }

    /// <summary>
    /// 构建 6-submesh 单位立方体网格：6 个面各为一个 submesh（每面独立 4 顶点 + 法线 + 2 三角形，顶点坐标 ±0.5）。
    /// submesh 顺序约定：0=+X、1=-X、2=+Y、3=-Y、4=+Z、5=-Z；每面三角形逆时针（从面外看）保证法线朝外。
    /// </summary>
    private Mesh BuildUnitCubeMesh()
    {
        Vector3[][] faceVerts = new Vector3[6][]
        {
            // 0: +X
            new Vector3[] { new Vector3( 0.5f, -0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f) },
            // 1: -X
            new Vector3[] { new Vector3(-0.5f, -0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f) },
            // 2: +Y
            new Vector3[] { new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f) },
            // 3: -Y
            new Vector3[] { new Vector3( 0.5f, -0.5f,  0.5f), new Vector3(-0.5f, -0.5f,  0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f) },
            // 4: +Z
            new Vector3[] { new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f) },
            // 5: -Z
            new Vector3[] { new Vector3( 0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f) }
        };
        Vector3[] faceNormals = new Vector3[6]
        {
            new Vector3( 1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
            new Vector3( 0f, 1f, 0f), new Vector3( 0f,-1f, 0f),
            new Vector3( 0f, 0f, 1f), new Vector3( 0f, 0f,-1f)
        };

        Vector3[] verts = new Vector3[24];
        Vector3[] normals = new Vector3[24];
        Vector2[] uvs = new Vector2[24];
        for (int f = 0; f < 6; f++)
        {
            for (int v = 0; v < 4; v++)
            {
                int idx = f * 4 + v;
                verts[idx] = faceVerts[f][v];
                normals[idx] = faceNormals[f];
                uvs[idx] = new Vector2(v == 1 || v == 2 ? 1f : 0f, v >= 2 ? 1f : 0f);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "UnitCube6Faces";
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.subMeshCount = 6;
        for (int f = 0; f < 6; f++)
        {
            int baseIdx = f * 4;
            int[] tris = new int[6]
            {
                baseIdx + 0, baseIdx + 1, baseIdx + 2,
                baseIdx + 0, baseIdx + 2, baseIdx + 3
            };
            mesh.SetTriangles(tris, f);
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 根据行进方向计算白色面对应的 submesh 下标：取最大分量所在轴，再按符号确定正/负半轴面；
    /// 单轴单位向量时与阈值判断等价，但非严格单轴时更稳健。映射：0=+X、1=-X、2=+Y、3=-Y、4=+Z、5=-Z。
    /// </summary>
    private int ComputeWhiteFaceIndex(Vector3 dir)
    {
        float ax = Mathf.Abs(dir.x);
        float ay = Mathf.Abs(dir.y);
        float az = Mathf.Abs(dir.z);
        if (ax >= ay && ax >= az)
            return dir.x > 0f ? 0 : 1; // +X / -X
        if (ay >= az)
            return dir.y > 0f ? 2 : 3; // +Y / -Y
        return dir.z > 0f ? 4 : 5;     // +Z / -Z
    }

    private Material CreateBlockMaterial()
    {
        // 必须用不透明材质：实心方块 6 个面是独立 submesh，若用半透明（Sprites/Default，
        // 不写深度、Blend 混合），后绘制的面会叠加混合到先绘制的白色行进面上，白面被盖掉。
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");
        return new Material(shader);
    }

    private void ReapplyColor()
    {
        if (blockMaterial == null)
            return;

        Color baseColor = GetTypeColor(Type);
        float edgeWidth = highlighted ? 0.1f : 0.04f; // 普通更细；选中（高亮）回到当前粗细 0.1
        Color edgeColor = new Color(1f, 1f, 1f, 1f); // 纯白实线（P2 风格，不发灰不发亮）

        if (highlighted)
        {
            blockMaterial.color = Color.Lerp(baseColor, Color.white, 0.6f);
        }
        else
        {
            blockMaterial.color = baseColor;
        }

        // 中心原点标记：仅选中（高亮）时显示；普通直径 0.075，高亮联动放大到 0.125。
        // 高亮初始显示白球，随后由 UpdateCenterMarkerColor 每帧按视角在黑/白球间切换；非高亮一律隐藏。
        if (centerMarkerWhite != null)
        {
            bool show = highlighted;
            centerMarkerWhite.SetActive(show);
            centerMarkerBlack.SetActive(false);
            float markerScale = highlighted ? 0.125f : 0.075f;
            centerMarkerWhite.transform.localScale = Vector3.one * markerScale;
            centerMarkerBlack.transform.localScale = Vector3.one * markerScale;
        }

        // 统一刷新 12 条棱：细实线宽度 + 纯白顶点色
        for (int i = 0; i < edgeRenderers.Length; i++)
        {
            if (edgeRenderers[i] == null)
                continue;
            edgeRenderers[i].startWidth = edgeWidth;
            edgeRenderers[i].endWidth = edgeWidth;
            edgeRenderers[i].startColor = edgeColor;
            edgeRenderers[i].endColor = edgeColor;
        }
    }

    /// <summary>类型对应颜色：攻击=红、恢复=绿、炸弹=蓝。</summary>
    public static Color GetTypeColor(BlockType type)
    {
        switch (type)
        {
            case BlockType.Attack: return new Color(1f, 0.25f, 0.25f);
            case BlockType.Heal:   return new Color(0.25f, 1f, 0.35f);
            case BlockType.Bomb:   return new Color(0.25f, 0.45f, 1f);
            default: return Color.white;
        }
    }

    /// <summary>类型英文文本（HUD 显示）。</summary>
    public static string GetTypeText(BlockType type)
    {
        switch (type)
        {
            case BlockType.Attack: return "ATTACK";
            case BlockType.Heal:   return "HEAL";
            case BlockType.Bomb:   return "BOMB";
            default: return "UNKNOWN";
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 飞行长方体单体（纯代码构建）：
///   - 尺寸：长/宽/高为浮点（生成器保证每边 ≥ minSide、三边总和 ≤ 3），使用标准立方体网格 + 加粗线框棱；
///   - 类型：Attack=红 / Bomb=蓝（Heal=绿 已废弃，生成器不再产出，枚举仅为兼容保留）；
///   - 运动：仅沿生成时的单个坐标轴方向朝原点飞行（其余轴坐标不变）；任一 AABB 边缘首次到达任意轴坐标 0 平面时销毁；
///   - 防重叠：移动前通过 FlyingBlockSpawner 检测目标 AABB，若与其它长方体相交则本帧原地等待；
///   - 高亮：准心指向时整体变亮、线框加粗，并在几何中心显示高亮原点标记；
///   - 行进面：长方体使用 6-submesh 单位立方体网格，行进方向对应的面渲染为白色；
///   - 中心标记：从白色面一侧查看时中心点渲染为黑色，其余角度为白色；
///   - 受击阶段（HP）：每个块生成时 HP 随机为 1~7（等概率，恒 ≥ 1）。HP=1 表示首次被命中即碎掉（不产生残骸）；
///     HP=N 表示沿攻击方向分 N 层厚度（每层 = 初始尺寸的 1/N），每次激光命中从入射侧吃掉一层，
///     剩余部分保留为子块继续飞行，累计 N 次被吃光即死亡。受击阶段颜色逐档变暗。
///   - 类型规则：蓝色（Bomb）炸弹块可被激光正常击打并击毁，但击毁既不加分也不扣分（score-neutral）；
///     红色（Attack）方块抵达 0 平面基地时按剩余体积扣分，击毁红色方块按 初始体积 + 初始 HP 加分（详见 ScoreManager）。
///   - 超时清理：方块记录生成时刻，存活超过 expireAfterSeconds（默认 60s）或已抵达/越过 0 平面时静默清除，
///     不计分、不扣分、不触发任何 ScoreManager 逻辑（红块正常抵达基地的漏块罚分不受影响）。
/// </summary>
public class FlyingBlock : MonoBehaviour
{
    /// <summary>长方体类型（用于颜色与 HUD 英文显示）。</summary>
    public enum BlockType
    {
        Attack, // 攻击型：红
        Heal,   // 恢复型：绿（已废弃：生成器不再产出，仅为兼容旧引用 / 序列化值保留）
        Bomb    // 炸弹型：蓝（可被击打并击毁，击毁不加分也不扣分）
    }

    [Header("配置")]
    [Tooltip("尺寸（长/宽/高，浮点，每边 ≥ 0.4，三边和 ≤ 3）")]
    public Vector3 size = Vector3.one;
    [Tooltip("飞行速度（单位/秒）")]
    public float speed = 2f;
    [Tooltip("防重叠检测允许的最小间距")]
    public float overlapMargin = 0.05f;

    [Header("受击阶段 / 血量")]
    [Tooltip("血量（= 受击总层数），生成时由 FlyingBlockSpawner 随机赋值为 1~7（恒 ≥ 1）：1 = 首次被命中即碎掉（无残骸），" +
             "N = 沿攻击方向分 N 层（每层 = 初始尺寸的 1/N），累计 N 次被吃光死亡")]
    public int maxHits = 4;

    [Header("超时清理")]
    [Tooltip("存活时长上限（秒）：生成后超过此时间仍未被消灭则静默清除（不计分、不扣分）。<= 0 表示不启用超时清理")]
    public float expireAfterSeconds = 60f;

    /// <summary>类型。</summary>
    public BlockType Type { get; private set; }

    /// <summary>累计受击次数（形态阶段：0=完整，1~3=被吃掉对应层数，≥maxHits 死亡）。</summary>
    public int HitCount { get; private set; }

    /// <summary>剩余血量（剩余层数）：maxHits - HitCount，供 HUD 显示。血量恒 ≥ 1，被吃光前不会为 0。</summary>
    public int RemainingHits => Mathf.Max(0, maxHits - HitCount);

    /// <summary>当前剩余体积（三边之积）：红色方块抵达基地时按此体积扣分。</summary>
    public float Volume => size.x * size.y * size.z;

    /// <summary>初始体积（生成时三边之积，不随受击削减）：击毁得分按此体积计算。</summary>
    public float InitialVolume => initialSize.x * initialSize.y * initialSize.z;

    /// <summary>几何中心（世界坐标，长方体不旋转时即 transform.position）。</summary>
    public Vector3 GeometryCenter => colliderRef != null ? colliderRef.bounds.center : transform.position;

    /// <summary>半尺寸（AABB 半宽/半高/半深）。</summary>
    public Vector3 HalfExtents => size * 0.5f;

    /// <summary>当前速度向量（单位/秒）：移动方向 × 速度，供 GetAllObjects 暴露。</summary>
    public Vector3 Velocity => moveDir * speed;

    /// <summary>编号（生成器分配，HUD 显示）。</summary>
    public int BlockId { get; private set; }

    /// <summary>生成时刻（Time.time）。受击后产生的剩余子块从自身创建时刻重新计时。</summary>
    public float SpawnTime => spawnTime;

    /// <summary>已存活时长（秒）。</summary>
    public float AliveSeconds => Time.time - spawnTime;

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
    private Vector3 initialSize; // 生成时的初始尺寸，用于固定每层厚度（与受击后的当前尺寸无关）
    private bool highlighted;
    private float spawnTime; // 生成时刻（以 Time.time 记录，用于"存活过久静默清除"判定；暂停时 Time.time 不增长故不会误清）

    /// <summary>初始化（由生成器调用）。hp = 该块血量（受击层数），1~7，由生成器 Random.Range(1, 8) 随机给出。</summary>
    public void Init(BlockType type, Vector3 blockSize, Vector3 position, float moveSpeed, int blockId, Vector3 dir, int hp)
    {
        Type = type;
        BlockId = blockId;
        maxHits = Mathf.Max(1, hp); // 血量 = 受击层数，恒 ≥ 1：HP=1 时第 1 发命中即吃满死亡（不会出现 0 血块）
        spawnTime = Time.time;      // 记录生成时刻，供超时静默清除判定
        size = new Vector3(
            Mathf.Max(blockSize.x, 0.4f),
            Mathf.Max(blockSize.y, 0.4f),
            Mathf.Max(blockSize.z, 0.4f)); // 每边保底下限，三边和 ≤ 3 由生成器保证
        initialSize = size; // 记录初始尺寸，受击每层厚度固定为初始尺寸的 1/maxHits
        speed = moveSpeed;
        transform.position = position;
        // 只沿单个轴方向移动：dir 由生成器按生成轴给出（其余轴为 0）
        moveDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : (Vector3.zero - position).normalized;

        BuildVisual();
        ReapplyColor();
    }

    /// <summary>受击剩余子块专用初始化：尺寸使用切割计算的精确值，不做每边 ≥0.4 的保底；继承源块初始尺寸、血量与受击次数（形态阶段连续）。</summary>
    public void InitRemnant(BlockType type, Vector3 blockSize, Vector3 position, float moveSpeed, int blockId, Vector3 dir, Vector3 originSize, int hitCount, int hp)
    {
        Type = type;
        BlockId = blockId;
        maxHits = Mathf.Max(1, hp); // 继承源块血量（恒 ≥ 1）：子块与源块共用同一份 HP，避免被打过的块血量被重置
        spawnTime = Time.time;      // 子块从自身创建时刻重新计时（超时清理以子块生命周期为准）
        size = blockSize; // 不 clamp：激光切割出的精确剩余尺寸
        initialSize = originSize; // 继承源块初始尺寸：后续受击每层厚度仍按初始尺寸的 1/maxHits 计算
        HitCount = hitCount;
        speed = moveSpeed;
        transform.position = position;
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

    // ---------------- 对外接口（激光） ----------------

    /// <summary>
    /// 激光命中（受击四阶段）：射线沿 laserDir 从 laserOrigin 发射并命中本块时，
    /// 从入射侧沿激光主导轴吃掉一层厚度（每层 = 初始尺寸沿该轴的 1/maxHits，固定不变）。
    /// 剩余部分保留为一个子块继续飞行，形态阶段（HitCount）连续累计；
    /// 累计 maxHits 次受击后整块被吃光，直接死亡销毁。
    /// 返回剩余子块列表（空 = 本块被吃光死亡）；未命中返回 null。
    /// 蓝色炸弹块与红色攻击块走完全相同的切割流程（可被击打、可被击毁），
    /// 区别仅在于击毁蓝块不产生任何分数变化（ScoreManager.ApplyKill 对 Bomb 直接返回）。
    /// </summary>
    public List<FlyingBlock> CutByLaser(Vector3 laserOrigin, Vector3 laserDir, float laserRadius)
    {
        if (Destroyed)
            return null;

        Vector3 rayDir = laserDir.sqrMagnitude > 0.0001f ? laserDir.normalized : Vector3.forward;
        Vector3 half = HalfExtents;
        Vector3 center = transform.position;
        if (!RayIntersectsAabb(laserOrigin, rayDir, center - half, center + half, out float tNear, out float tFar))
            return null;

        // 切割主导轴：激光方向的最大分量轴（与白面主导轴规则一致）
        float ax = Mathf.Abs(rayDir.x), ay = Mathf.Abs(rayDir.y), az = Mathf.Abs(rayDir.z);
        int cutAxis = (ax >= ay && ax >= az) ? 0 : (ay >= az ? 1 : 2);

        // 受击计数：第 maxHits 次受击整块被吃光，直接死亡（maxHits 由生成器给出 1~7，恒 ≥ 1；
        // HP=1 的块第 1 发命中即被吃光，不存在 HP=0 的一击即毁块）
        HitCount++;
        if (HitCount >= maxHits)
        {
            FlyingBlockSpawner killerSpawner = FlyingBlockSpawner.Instance;
            if (killerSpawner != null)
                killerSpawner.RegisterPlayerKill(); // 玩家激光消灭计数（教学任务用）
            // 击毁奖励：按 初始体积 + 初始 HP 加分（分值规则见 ScoreManager.ApplyKill）
            ScoreManager.NotifyBlockDestroyed(Type, InitialVolume, maxHits);
            DestroyBlock("hit_exhausted");
            return new List<FlyingBlock>();
        }

        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null)
            return null;

        // 每层厚度 = 初始尺寸沿攻击轴的 1/maxHits（固定，不受已吃层数影响）；maxHits=0 已在上面死亡返回，此处仅作除零保护
        float layerThickness = Mathf.Abs(initialSize[cutAxis]) / Mathf.Max(1, maxHits);
        float minA = center[cutAxis] - half[cutAxis];
        float maxA = center[cutAxis] + half[cutAxis];

        // 从入射侧吃一层：激光正方向进入 → 入射侧为 minA；负方向进入 → 入射侧为 maxA
        float remainLow, remainHigh;
        if (rayDir[cutAxis] > 0f)
        {
            remainLow = Mathf.Min(minA + layerThickness, maxA); // 吃掉 [minA, minA+layer]
            remainHigh = maxA;
        }
        else
        {
            remainLow = minA;
            remainHigh = Mathf.Max(maxA - layerThickness, minA); // 吃掉 [maxA-layer, maxA]
        }

        const float minRemain = 0.05f; // 剩余厚度下限，低于此认为被吃光
        List<FlyingBlock> remnants = new List<FlyingBlock>();

        if (remainHigh - remainLow > minRemain)
        {
            Vector3 newSize = size;
            Vector3 newCenter = center;
            newSize[cutAxis] = remainHigh - remainLow;
            newCenter[cutAxis] = (remainLow + remainHigh) * 0.5f; // 剩余子块中心 = 剩余区间中点
            FlyingBlock r = spawner.CreateRemnant(this, Type, newSize, newCenter, moveDir, speed, initialSize, HitCount, maxHits);
            if (r != null) remnants.Add(r);
        }

        // 原块已被吃掉一层，销毁（剩余部分以子块形式继续存在）
        DestroyBlock("hit_layer_consumed");

        return remnants;
    }

    /// <summary>射线与轴对齐包围盒求交（slab 法），返回是否命中及进入/离开参数 t。</summary>
    private static bool RayIntersectsAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float tNear, out float tFar)
    {
        tNear = float.NegativeInfinity;
        tFar = float.PositiveInfinity;
        for (int i = 0; i < 3; i++)
        {
            float o = origin[i];
            float d = dir[i];
            if (Mathf.Abs(d) < 1e-6f)
            {
                // 射线与该轴平行：起点必须在包围盒范围内
                if (o < min[i] || o > max[i])
                    return false;
                continue;
            }
            float t1 = (min[i] - o) / d;
            float t2 = (max[i] - o) / d;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tNear) tNear = t1;
            if (t2 < tFar) tFar = t2;
            if (tNear > tFar)
                return false;
        }
        return tFar >= 0f; // 至少命中射线正方向
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

        // 0) 超时静默清除：存活超过 expireAfterSeconds（默认 60s）仍未被消灭 → 直接清除
        //    不计分、不扣分、不触发任何 ScoreManager 逻辑，避免久飞不落的方块长期占场
        if (expireAfterSeconds > 0f && Time.time - spawnTime > expireAfterSeconds)
        {
            DestroyBlock("life_expired");
            return;
        }

        // 1) 防重叠移动：目标位置若与其它长方体相交则本帧原地等待
        Vector3 nextPos = transform.position + moveDir * speed * Time.deltaTime;
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null || spawner.IsPositionFree(nextPos, HalfExtents, this))
        {
            transform.position = nextPos;
        }

        // 2) 边界销毁：任一 AABB 边缘首次到达任意轴坐标 = 0（0 平面进入长方体）即销毁
        //    红色攻击方块：抵达/跨过基地 → 按剩余体积扣分；
        //    蓝色炸弹块：抵达/跨过 0 平面 → 静默清除（不计分、不扣分）
        Vector3 center = transform.position;
        Vector3 half = HalfExtents;
        Vector3 min = center - half;
        Vector3 max = center + half;
        if ((min.x <= 0f && max.x >= 0f) ||
            (min.y <= 0f && max.y >= 0f) ||
            (min.z <= 0f && max.z >= 0f))
        {
            // 红色攻击方块抵达基地：按当前剩余体积扣分（被打小的块扣得少）
            if (Type == BlockType.Attack)
            {
                ScoreManager.NotifyAttackLeaked(Volume);
                DestroyBlock("edge_reached_zero");
            }
            else
            {
                DestroyBlock("zero_plane_cleared");
            }
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
        // 受击阶段形态：每被击中一次整体变暗一档（0 次=原色，1~3 次逐档变暗，第 4 次已死亡）
        float dim = 1f - 0.18f * HitCount;
        baseColor.r *= dim;
        baseColor.g *= dim;
        baseColor.b *= dim;

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

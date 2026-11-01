using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 原点炮台（Code Tower）：
///   - 位置恒定在原点 (0,0,0)（每帧强制归位），为「代码编辑器」的用户代码提供可调用接口；
///   - 视觉：纯代码构建简易炮台——底座圆柱 + 可旋转炮管圆柱（深灰 #3A3A3A），炮口端亮青色；
///   - 激光：从炮口沿 AimDirection 发射（射程 laserMaxLength=60、半径 laserRadius=0.15），复用现有规则——
///     命中 FlyingBlock 调 block.CutByLaser（受击四阶段），并触发 CommandConsoleController.OnLaserFired 事件；
///   - 冷却：cooldownSeconds = 0.25s，用 Time.time 判断，冷却中 FireLaser 返回 false；
///   - 公开 API（签名固定）供运行时编译的用户代码直接调用。
/// </summary>
public class CodeTower : MonoBehaviour
{
    [Header("激光")]
    [Tooltip("激光半径（粗细）")]
    public float laserRadius = 0.15f;
    [Tooltip("激光最大射程（单位）")]
    public float laserMaxLength = 60f;
    [Tooltip("激光视觉显示时长（秒，用不受时间冻结影响的真实时间）")]
    public float laserVisualDuration = 0.9f;
    [Tooltip("激光视觉颜色（亮青色）")]
    public Color laserColor = new Color(0.3f, 1f, 1f, 0.9f);

    [Header("炮台")]
    [Tooltip("开火冷却时间（秒）")]
    public float cooldownSeconds = 0.25f;
    [Tooltip("炮台主体颜色（深灰 #3A3A3A）")]
    public Color towerColor = new Color(0.227f, 0.227f, 0.227f, 1f);
    [Tooltip("炮口高亮颜色（亮青色）")]
    public Color muzzleColor = new Color(0.3f, 1f, 1f, 1f);

    // 视觉引用
    private GameObject baseVisual; // 底座（炮台实体根，SetVisualVisible 控制）
    private Transform barrelRoot;  // 炮管旋转轴（位于底座顶面中心）
    private Transform muzzleTip;   // 炮口端亮青色小球
    private LineRenderer aimLine;  // 瞄准预览线（DrawAimLine 控制）

    // 朝向与冷却
    private Vector3 aimDir = Vector3.forward;
    private float lastFireTime = -1000f; // 初始视为已冷却，可立即发射

    /// <summary>炮台世界位置（恒定原点）。</summary>
    public Vector3 Position => transform.position;

    /// <summary>当前炮口朝向（归一化）。</summary>
    public Vector3 AimDirection => aimDir;

    /// <summary>是否已冷却完毕可发射。</summary>
    public bool CanFire => Time.time - lastFireTime >= cooldownSeconds;

    /// <summary>冷却进度（0=冷却完毕可发射，1=刚发射完）。</summary>
    public float Cooldown01
    {
        get
        {
            float t = Time.time - lastFireTime;
            if (t >= cooldownSeconds)
                return 0f;
            return Mathf.Clamp01(1f - t / Mathf.Max(0.0001f, cooldownSeconds));
        }
    }

    /// <summary>剩余冷却秒数。</summary>
    public float CooldownLeft => Mathf.Max(0f, cooldownSeconds - (Time.time - lastFireTime));

    /// <summary>场上离原点最近的活跃块（无则 null）。</summary>
    public FlyingBlock NearestBlock
    {
        get
        {
            FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
            if (spawner == null || spawner.ActiveBlocks == null || spawner.ActiveBlocks.Count == 0)
                return null;

            Vector3 towerPos = transform.position;
            FlyingBlock best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < spawner.ActiveBlocks.Count; i++)
            {
                FlyingBlock b = spawner.ActiveBlocks[i];
                if (b == null || b.Destroyed)
                    continue;
                float d = (b.GeometryCenter - towerPos).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = b;
                }
            }
            return best;
        }
    }

    /// <summary>尝试获取离原点最近的活跃块。</summary>
    public bool TryGetNearestBlock(out FlyingBlock block)
    {
        block = NearestBlock;
        return block != null;
    }

    /// <summary>场上方块数量。</summary>
    public int ActiveBlockCount
        => FlyingBlockSpawner.Instance != null ? FlyingBlockSpawner.Instance.ActiveBlocks.Count : 0;

    /// <summary>已流逝时间（游戏时间，受 timeScale 影响）。</summary>
    public float Elapsed => Time.time;

    /// <summary>本帧时间增量（受时间冻结影响）。</summary>
    public float DeltaTime => Time.deltaTime;

    /// <summary>随机范围（min ~ max）。</summary>
    public float RandomRange(float min, float max) => Random.Range(min, max);

    /// <summary>打印日志。</summary>
    public void Log(string message) => Debug.Log("[CodeTower] " + message);

    /// <summary>是否显示炮口朝向预览线（从炮口到射程末端）。</summary>
    public void DrawAimLine(bool on)
    {
        if (aimLine != null)
            aimLine.gameObject.SetActive(on);
    }

    /// <summary>
    /// 控制炮台实体视觉显隐（Level 1 教学关不显示炮台模型，仅保留原点炮台功能；
    /// Level 3 完整玩法关显示实体炮台）。隐藏仅关掉底座 / 炮管 / 炮口 / 瞄准线，
    /// Aim / FireLaser / NearestBlock 等 API 与激光发射功能不受影响。
    /// </summary>
    public void SetVisualVisible(bool visible)
    {
        if (baseVisual != null)
            baseVisual.SetActive(visible);
        if (barrelRoot != null)
            barrelRoot.gameObject.SetActive(visible);
        // 瞄准线不随实体显隐：由 DrawAimLine 单独控制（默认关闭）
    }

    private void Awake()
    {
        BuildVisual();
    }

    private void Update()
    {
        transform.position = Vector3.zero; // 位置恒定在原点

        // 炮管朝向与瞄准线持续跟随（防止外部直接改 transform 造成的漂移）
        if (barrelRoot != null)
            barrelRoot.localRotation = Quaternion.FromToRotation(Vector3.up, aimDir);
        if (aimLine != null && aimLine.gameObject.activeSelf)
            UpdateAimLine();
    }

    /// <summary>设置炮口朝向（非零向量归一化，旋转炮管子物体；零向量忽略）。</summary>
    public void Aim(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        aimDir = direction.normalized;
        if (barrelRoot != null)
            barrelRoot.localRotation = Quaternion.FromToRotation(Vector3.up, aimDir);
        if (aimLine != null && aimLine.gameObject.activeSelf)
            UpdateAimLine();
    }

    /// <summary>
    /// 发射激光：冷却中返回 false；成功则从炮口沿 AimDirection 发射
    /// （射程 60、半径 0.15），命中所有 FlyingBlock 调 block.CutByLaser（受击四阶段），
    /// 触发 CommandConsoleController.OnLaserFired，并生成双层激光视觉（命中点带火花）。
    /// </summary>
    public bool FireLaser()
    {
        if (!CanFire)
            return false;
        lastFireTime = Time.time;

        CommandConsoleController.NotifyLaserFired(); // 教学 / 自动化脚本监听

        Vector3 origin = MuzzleWorldPosition();
        Vector3 dir = aimDir;

        // 收集路径上所有命中（含子物体 collider，取所属 FlyingBlock 去重）
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, laserMaxLength);
        HashSet<FlyingBlock> hitBlocks = new HashSet<FlyingBlock>();
        float nearestT = laserMaxLength;
        for (int i = 0; i < hits.Length; i++)
        {
            FlyingBlock block = hits[i].collider != null
                ? hits[i].collider.GetComponentInParent<FlyingBlock>()
                : null;
            if (block != null && !block.Destroyed)
            {
                hitBlocks.Add(block);
                if (hits[i].distance < nearestT)
                    nearestT = hits[i].distance;
            }
        }

        // 逐个命中：每个块从入射侧吃掉一层厚度（受击四阶段），第 4 层吃光即死亡
        int hitCount = 0;
        int deathCount = 0;
        foreach (FlyingBlock block in hitBlocks)
        {
            List<FlyingBlock> remnants = block.CutByLaser(origin, dir, laserRadius);
            if (remnants != null)
            {
                hitCount++;
                if (remnants.Count == 0)
                    deathCount++; // 被吃光死亡（无剩余子块）
            }
        }

        // 激光视觉：从炮口到最近命中点（未命中则打满射程）；命中时在端点额外显示火花特效
        Vector3 endPoint = origin + dir * nearestT;
        SpawnLaserVisual(origin, endPoint, hitBlocks.Count > 0);

        Debug.Log("[CodeTower] 发射激光：hits=" + hitBlocks.Count + " deaths=" + deathCount);
        return true;
    }

    /// <summary>炮口世界位置（炮管末端炮口小球中心）。</summary>
    private Vector3 MuzzleWorldPosition()
    {
        if (muzzleTip != null)
            return muzzleTip.position;
        Vector3 pivot = barrelRoot != null ? barrelRoot.position : transform.position;
        return pivot + aimDir * 0.45f;
    }

    // ---------------- 视觉构建 ----------------

    private void BuildVisual()
    {
        // 底座：矮圆柱，深灰
        GameObject baseGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseGo.name = "TowerBase";
        baseGo.transform.SetParent(transform, false);
        baseGo.transform.localScale = new Vector3(0.9f, 0.09f, 0.9f); // 半径 0.45、高 0.18
        DisableLaserCollider(baseGo);
        baseGo.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial(towerColor);
        baseVisual = baseGo;

        // 炮管旋转轴：位于底座顶面中心（子物体）
        GameObject barrelGo = new GameObject("TowerBarrel");
        barrelGo.transform.SetParent(transform, false);
        barrelGo.transform.localPosition = new Vector3(0f, 0.18f, 0f);
        barrelRoot = barrelGo.transform;

        // 炮管主体：细长圆柱，深灰（长 0.9、半径 0.06，局部 +Y 为长轴）
        GameObject barrelBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrelBody.name = "BarrelBody";
        barrelBody.transform.SetParent(barrelRoot, false);
        barrelBody.transform.localScale = new Vector3(0.12f, 0.45f, 0.12f);
        DisableLaserCollider(barrelBody);
        barrelBody.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial(towerColor);

        // 炮口端：亮青色小球
        GameObject muzzle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        muzzle.name = "MuzzleTip";
        muzzle.transform.SetParent(barrelRoot, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        muzzle.transform.localScale = Vector3.one * 0.14f;
        DisableLaserCollider(muzzle);
        muzzle.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial(muzzleColor);
        muzzleTip = muzzle.transform;

        // 初始朝向：炮管指向 aimDir（默认 +Z）
        barrelRoot.localRotation = Quaternion.FromToRotation(Vector3.up, aimDir);

        // 瞄准预览线（默认隐藏）
        GameObject lineGo = new GameObject("AimLine");
        lineGo.transform.SetParent(transform, false);
        aimLine = lineGo.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.startWidth = 0.03f;
        aimLine.endWidth = 0.03f;
        aimLine.numCapVertices = 2;
        aimLine.numCornerVertices = 0;
        aimLine.sharedMaterial = CreateLaserMaterial(new Color(0.3f, 1f, 1f, 0.35f));
        aimLine.startColor = new Color(1f, 1f, 1f, 1f);
        aimLine.endColor = new Color(1f, 1f, 1f, 1f);
        lineGo.SetActive(false);
    }

    private void UpdateAimLine()
    {
        Vector3 origin = MuzzleWorldPosition();
        aimLine.SetPosition(0, origin);
        aimLine.SetPosition(1, origin + aimDir * laserMaxLength);
    }

    // ---------------- 激光视觉（参考 CommandConsoleController 写法） ----------------

    /// <summary>生成激光视觉（双层圆柱：外层光晕 + 内芯高亮；命中时端点加火花小球），保留后淡出销毁。</summary>
    private void SpawnLaserVisual(Vector3 origin, Vector3 end, bool impact)
    {
        Vector3 delta = end - origin;
        float length = delta.magnitude;
        if (length < 0.01f)
            return;

        Vector3 center = (origin + end) * 0.5f;
        Vector3 dir = delta / length;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        // 外层光晕圆柱：半径 3 倍、低透明度，营造光束辉光
        GameObject haloGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        haloGo.name = "CodeTowerLaser_Halo";
        haloGo.transform.position = center;
        haloGo.transform.rotation = rot;
        haloGo.transform.localScale = new Vector3(laserRadius * 6f, length * 0.5f, laserRadius * 6f);
        DisableLaserCollider(haloGo);
        Material haloMat = CreateLaserMaterial(new Color(laserColor.r, laserColor.g, laserColor.b, 0.22f));
        haloGo.GetComponent<MeshRenderer>().sharedMaterial = haloMat;

        // 内芯圆柱：半径 1 倍、高亮，实际"激光"本体
        GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coreGo.name = "CodeTowerLaser_Core";
        coreGo.transform.position = center;
        coreGo.transform.rotation = rot;
        coreGo.transform.localScale = new Vector3(laserRadius * 2f, length * 0.5f, laserRadius * 2f);
        DisableLaserCollider(coreGo);
        Material coreMat = CreateLaserMaterial(laserColor);
        coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

        // 命中点特效：发光小球（偏暖色火花），随淡出一起缩小消失
        GameObject impactGo = null;
        Material impactMat = null;
        if (impact)
        {
            impactGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactGo.name = "CodeTowerLaser_Impact";
            impactGo.transform.position = end;
            impactGo.transform.localScale = Vector3.one * laserRadius * 3f;
            DisableLaserCollider(impactGo);
            impactMat = CreateLaserMaterial(new Color(1f, 0.85f, 0.35f, 0.95f));
            impactGo.GetComponent<MeshRenderer>().sharedMaterial = impactMat;
        }

        StartCoroutine(DestroyLaserVisual(coreGo, haloGo, impactGo, coreMat, haloMat, impactMat));
    }

    /// <summary>禁用并移除激光视觉物体的 Collider，避免干扰后续射线检测。</summary>
    private void DisableLaserCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null)
        {
            c.enabled = false;
            Destroy(c);
        }
    }

    /// <summary>创建激光特效材质（Sprites/Default 半透明叠加，缺失时退回 Unlit/Color）。</summary>
    private Material CreateLaserMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    /// <summary>创建实心材质（Unlit/Color 深灰 / 亮青，缺失时退回 Sprites/Default / Standard）。</summary>
    private Material CreateSolidMaterial(Color color)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    /// <summary>
    /// 激光视觉存活协程：前 40% 时长保持全亮，后 60% 时长线性淡出（真实时间，不受 timeScale=0 冻结影响），
    /// 命中火花同步缩小；结束后销毁全部视觉物体与材质。
    /// </summary>
    private IEnumerator DestroyLaserVisual(GameObject core, GameObject halo, GameObject impact, Material coreMat, Material haloMat, Material impactMat)
    {
        float t = 0f;
        float dur = Mathf.Max(0.05f, laserVisualDuration);
        Vector3 impactScale = impact != null ? impact.transform.localScale : Vector3.one;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            // 前 40% 全亮，后 60% 从 1 线性降到 0
            float fade = k < 0.4f ? 1f : Mathf.Clamp01(1f - (k - 0.4f) / 0.6f);

            if (coreMat != null) coreMat.color = FadeLaserAlpha(laserColor, fade);
            if (haloMat != null) haloMat.color = FadeLaserAlpha(haloMat.color, 0.22f * fade);
            if (impactMat != null) impactMat.color = FadeLaserAlpha(impactMat.color, fade);
            if (impact != null)
                impact.transform.localScale = impactScale * Mathf.Lerp(1f, 0.15f, k);

            yield return null;
        }

        if (core != null) Destroy(core);
        if (halo != null) Destroy(halo);
        if (impact != null) Destroy(impact);
        if (coreMat != null) Destroy(coreMat);
        if (haloMat != null) Destroy(haloMat);
        if (impactMat != null) Destroy(impactMat);
    }

    /// <summary>按给定 alpha 覆盖颜色透明度（保留 RGB）。</summary>
    private static Color FadeLaserAlpha(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 飞行长方体生成器与全局登记表：
///   - 开局前 startDelay 秒不生成任何长方体（留出缓冲）；
///   - 定时从坐标轴边缘（x/y/z 任一轴坐标 = ±edgeExtent 的面）生成长方体，仅沿该轴朝原点移动；
///   - 生成与运动均不允许重叠：IsPositionFree 对所有活跃长方体做 AABB 相交检测；
///   - 提供 Unregister 供长方体受控销毁时注销登记。
/// </summary>
public class FlyingBlockSpawner : MonoBehaviour
{
    public static FlyingBlockSpawner Instance { get; private set; }

    [Header("生成配置")]
    [Tooltip("坐标轴边缘（长方体在该轴的面坐标 = ±edgeExtent，默认 10 与坐标轴 [-10,10] 一致）")]
    public float edgeExtent = 10f;
    [Tooltip("随机边长下限（每边 ≥ minSide，默认 0.4）")]
    public float minSide = 0.4f;
    [Tooltip("生成间隔（秒）")]
    public float spawnInterval = 1.2f;
    [Tooltip("开局延迟（秒），此期间不生成任何长方体")]
    public float startDelay = 5f;
    [Tooltip("活跃长方体数量上限")]
    public int maxBlocks = 40;
    [Tooltip("飞行速度下限（单位/秒）")]
    public float speedMin = 0.5f;
    [Tooltip("飞行速度上限（单位/秒）")]
    public float speedMax = 1.2f;

    [Header("类型概率（%）")]
    [Tooltip("攻击型（红）刷新概率")]
    public int attackChance = 50;
    [Tooltip("炸弹型（蓝）刷新概率")]
    public int bombChance = 30;
    [Tooltip("恢复型（绿）刷新概率（剩余部分）")]
    public int healChance = 20;

    /// <summary>所有活跃长方体（生成器与检测器共用登记表）。</summary>
    public readonly List<FlyingBlock> ActiveBlocks = new List<FlyingBlock>();

    /// <summary>是否允许生成：主页显示期间为 false，点击关卡后由 BeginSpawning 开启。</summary>
    public bool spawningEnabled = true;

    /// <summary>玩家激光消灭的方块总数（受击吃光死亡计 1；教学 / 统计用）。</summary>
    public int PlayerKills { get; private set; }

    private float timer;
    private int nextBlockId = 1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        timer = -startDelay; // 开局前 startDelay 秒不生成任何长方体
    }

    private void Update()
    {
        if (!spawningEnabled)
            return; // 主页显示期间不生成任何长方体
        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            if (ActiveBlocks.Count < maxBlocks)
                SpawnBlock();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>注销长方体（受控销毁流程调用）。</summary>
    public void Unregister(FlyingBlock block)
    {
        if (block != null)
            ActiveBlocks.Remove(block);
    }

    /// <summary>主页接管：暂停生成（保留当前 timer）。</summary>
    public void SuspendSpawning()
    {
        spawningEnabled = false;
    }

    /// <summary>清空场上所有活跃长方体（关卡跳转 / 重置用）。</summary>
    public void ClearAllBlocks()
    {
        for (int i = ActiveBlocks.Count - 1; i >= 0; i--)
        {
            FlyingBlock block = ActiveBlocks[i];
            if (block != null && block.gameObject != null)
                Destroy(block.gameObject);
        }
        ActiveBlocks.Clear();
    }

    /// <summary>点击关卡后开始生成：重置开局缓冲（进游戏前 startDelay 秒不生成）。</summary>
    public void BeginSpawning()
    {
        BeginSpawning(startDelay);
    }

    /// <summary>开始生成并指定缓冲时长（教学 / 特殊关卡可缩短等待）。</summary>
    public void BeginSpawning(float delay)
    {
        spawningEnabled = true;
        timer = -Mathf.Max(0f, delay);
    }

    /// <summary>玩家激光消灭计数：由 FlyingBlock 受击吃光死亡时调用。</summary>
    public void RegisterPlayerKill()
    {
        PlayerKills++;
    }

    /// <summary>清零消灭计数（教学 / 关卡重开时调用）。</summary>
    public void ResetPlayerKills()
    {
        PlayerKills = 0;
    }

    /// <summary>重置本局会话：清零消灭计数并重置方块编号（再次进入 Level 3 时调用；教学关不调用以免打断教学统计）。</summary>
    public void ResetSession()
    {
        PlayerKills = 0;
        nextBlockId = 1;
    }

    /// <summary>目标 AABB 是否与除 ignore 外的任意活跃长方体重叠（生成与运动防重叠共用）。</summary>
    public bool IsPositionFree(Vector3 center, Vector3 halfExtents, FlyingBlock ignore)
    {
        for (int i = 0; i < ActiveBlocks.Count; i++)
        {
            FlyingBlock other = ActiveBlocks[i];
            if (other == null || other == ignore || other.Destroyed)
                continue;

            Vector3 oCenter = other.transform.position;
            Vector3 oHalf = other.HalfExtents;
            if (AabbOverlap(center, halfExtents, oCenter, oHalf))
                return false;
        }
        return true;
    }

    /// <summary>生成一个长方体（自动选择边缘面 / 尺寸 / 位置，并做防重叠重试）。</summary>
    public FlyingBlock SpawnBlock()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Vector3 size = PickBlockSize();
            Vector3 half = size * 0.5f;

            // 选定单一轴向：生成面与该轴一致，移动方向只有该轴分量（其余轴为 0）
            int axis = Random.Range(0, 3);        // 0=x, 1=y, 2=z
            int sign = Random.Range(0, 2) == 0 ? -1 : 1; // 生成于 -1 / +1 两个面
            Vector3 spawnPos = PickEdgePosition(half, axis, sign);
            if (!IsPositionFree(spawnPos, half, null))
                continue;

            Vector3 dir = Vector3.zero;
            dir[axis] = -sign; // 指向原点：+面朝 -1 方向，-面朝 +1 方向

            GameObject go = new GameObject("FlyingBlock_" + nextBlockId, typeof(FlyingBlock));
            FlyingBlock block = go.GetComponent<FlyingBlock>();
            FlyingBlock.BlockType type = PickBlockType();
            float speed = Random.Range(speedMin, speedMax);
            block.Init(type, size, spawnPos, speed, nextBlockId, dir);
            nextBlockId++;
            ActiveBlocks.Add(block);
            return block;
        }
        return null; // 空间不足时放弃本次生成
    }

    /// <summary>
    /// 受击后创建剩余子块：保留源块类型、移动方向与原编号（击打过后编号不变），
    /// 新尺寸/新中心由切割计算给出，并继承源块初始尺寸与受击次数（形态阶段连续），
    /// 注册进全局登记表（受数量上限约束，超限返回 null）。
    /// </summary>
    public FlyingBlock CreateRemnant(FlyingBlock source, FlyingBlock.BlockType type, Vector3 newSize, Vector3 newCenter, Vector3 dir, float moveSpeed, Vector3 originSize, int hitCount)
    {
        if (ActiveBlocks.Count >= maxBlocks)
            return null;
        int blockId = source != null ? source.BlockId : nextBlockId; // 子块沿用源块编号，HUD 编号不随击打变化
        GameObject go = new GameObject("FlyingBlock_" + blockId, typeof(FlyingBlock));
        FlyingBlock block = go.GetComponent<FlyingBlock>();
        block.InitRemnant(type, newSize, newCenter, moveSpeed, blockId, dir, originSize, hitCount);
        ActiveBlocks.Add(block);
        return block;
    }

    /// <summary>随机浮点边长：每边 ≥ minSide，三边和 ≤ 3（按三边和 [2, 3] 随机分配）。</summary>
    private Vector3 PickBlockSize()
    {
        float target = Random.Range(2f, 3f);         // 三边和约束，始终 ≤ 3
        float x = Random.Range(minSide, target - 2f * minSide); // 保留 y/z 各 ≥ minSide
        float y = Random.Range(minSide, target - x - minSide);  // 保留 z ≥ minSide
        float z = target - x - y;                    // 三边和 = target
        return new Vector3(x, y, z);
    }

    /// <summary>按配置概率选择类型：红(attack) / 蓝(bomb) / 绿(heal)。</summary>
    private FlyingBlock.BlockType PickBlockType()
    {
        int roll = Random.Range(0, 100);
        if (roll < attackChance)
            return FlyingBlock.BlockType.Attack;
        if (roll < attackChance + bombChance)
            return FlyingBlock.BlockType.Bomb;
        return FlyingBlock.BlockType.Heal;
    }

    /// <summary>按指定坐标轴边缘面生成点（外边缘朝内偏移半个尺寸，保证初始完全落在范围内）。</summary>
    private Vector3 PickEdgePosition(Vector3 half, int axis, int sign)
    {
        Vector3 pos = Vector3.zero;
        pos[axis] = sign * (edgeExtent - half[axis]);

        // 另外两个轴在 [-edgeExtent+half, edgeExtent-half] 内随机
        for (int i = 0; i < 3; i++)
        {
            if (i == axis)
                continue;
            float min = -edgeExtent + half[i];
            float max = edgeExtent - half[i];
            pos[i] = Random.Range(min, max);
        }
        return pos;
    }

    /// <summary>AABB 相交检测（带最小间距 margin）。</summary>
    private static bool AabbOverlap(Vector3 aCenter, Vector3 aHalf, Vector3 bCenter, Vector3 bHalf, float margin = 0.05f)
    {
        return Mathf.Abs(aCenter.x - bCenter.x) < (aHalf.x + bHalf.x + margin) &&
               Mathf.Abs(aCenter.y - bCenter.y) < (aHalf.y + bHalf.y + margin) &&
               Mathf.Abs(aCenter.z - bCenter.z) < (aHalf.z + bHalf.z + margin);
    }
}

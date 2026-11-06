using UnityEngine;

/// <summary>
/// 关卡开场停顿（Level 3 完整玩法关专用，修复 Level 1 → Level 3 跳转时炮台立刻出现、方块瞬间生成的 bug）：
///   - 进入关卡后先停顿 OpeningSeconds 秒（真实时间，E 键时间冻结 / 编辑器暂停期间也照常倒计时）；
///   - 停顿期间：隐藏炮台实体、暂停方块生成；
///   - 停顿结束：显示炮台实体，并以 spawnBuffer 秒缓冲开始生成方块。
/// 重复跳转关卡会重置计时；跳转到其他关卡时调用 Cancel() 终止，避免停顿结束误显示炮台或误开启生成。
/// </summary>
public class LevelOpeningGate : MonoBehaviour
{
    /// <summary>进入 Level 3 后的开场停顿秒数（5 秒）。</summary>
    public const float OpeningSeconds = 5f;

    public static LevelOpeningGate Instance { get; private set; }

    private float remain;
    private bool running;
    private CodeTower tower;
    private FlyingBlockSpawner spawner;
    private float spawnBuffer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 启动开场停顿：立即隐藏炮台并暂停生成，停顿结束后显示炮台并开始生成方块。
    /// 注意：调用方需保证传入的 tower / spawner 可为 null（缺失时对应动作自动跳过）。
    /// </summary>
    public static void Play(CodeTower tower, FlyingBlockSpawner spawner, float seconds, float spawnBuffer)
    {
        LevelOpeningGate gate = Instance;
        if (gate == null)
        {
            GameObject go = new GameObject("LevelOpeningGate");
            gate = go.AddComponent<LevelOpeningGate>(); // AddComponent 会立即执行 Awake，Instance 随之就绪
        }

        // 先终止上一次可能仍在进行的计时，再用新的秒数重新开始，避免残留计时把本次停顿缩短或直接跳过
        gate.running = false;
        gate.remain = 0f;

        gate.tower = tower;
        gate.spawner = spawner;
        gate.spawnBuffer = Mathf.Max(0f, spawnBuffer);
        gate.remain = Mathf.Max(0f, seconds);
        gate.running = true;

        // 停顿期间：炮台先不显示，方块先不生成（杜绝"进场瞬间显示炮台 / 瞬间出块"）
        if (gate.tower != null)
            gate.tower.SetVisualVisible(false);
        if (gate.spawner != null)
            gate.spawner.SuspendSpawning();

        // 生成器侧的硬保证：停顿计时未结束时，任何入口的 BeginSpawning 都不会真正开始出块
        if (gate.spawner != null)
            gate.spawner.BeginOpeningHold(gate.remain);
    }

    /// <summary>取消开场停顿（跳转到其他关卡时调用）：停顿不再结束后触发显示 / 生成。</summary>
    public static void Cancel()
    {
        if (Instance == null)
            return;

        Instance.running = false;
        Instance.remain = 0f;
        if (Instance.spawner != null)
            Instance.spawner.EndOpeningHold(); // 同步解除生成器的停顿锁，避免下一关被残留停顿阻塞
    }

    private void Update()
    {
        if (!running)
            return;

        // 停顿期间持续压制生成：即使其他系统在此期间调用 BeginSpawning，也保持不生成
        if (spawner != null)
            spawner.SuspendSpawning();

        remain -= Time.unscaledDeltaTime; // 真实时间：E 键冻结 / 编辑器暂停期间也照常倒计时
        if (remain > 0f)
            return;

        running = false;
        // 停顿结束只开始生成方块：炮台本体永久不现身（不再显示），火力逻辑一直在原点照常工作
        if (spawner != null)
        {
            spawner.EndOpeningHold();          // 解除停顿锁
            spawner.BeginSpawning(spawnBuffer); // 停顿结束才开始生成方块
        }
        Debug.Log("[LevelOpeningGate] 开场停顿结束：开始生成方块（炮台本体保持隐藏）。");
    }
}

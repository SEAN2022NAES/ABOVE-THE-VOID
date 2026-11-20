using UnityEngine;

/// <summary>
/// 关卡开场门控（Level 3 完整玩法关专用）：
///   - 进入关卡后<b>不再自动开场</b>：门控进入「等待玩家启动」状态（waiting），期间隐藏炮台、硬锁方块生成，
///     任何入口调用 BeginSpawning 都不会出块；
///   - 只有玩家代码调用 CodeTower.GameStart() 才真正开始本关（RequestStart），
///     未调用则永远不出方块 —— 「游戏什么时候才开始」由玩家代码决定；
///   - 「开场缓冲」能力保留：Play 的 seconds 参数即启动后的缓冲秒数，传 OpeningSeconds 就恢复原有 5 秒节奏，
///     当前 Level 3 - Part 1 传 0（玩家一调用 GameStart 立即出块，不再有自动的 5 秒等待）；
///   - 重复调用 GameStart() 幂等：只有「仍在等待」的那一次有效，之后的调用一律直接忽略（无任何副作用）；
///   - 跳转到其他关卡 / 回主页调用 Cancel()：重置回等待状态，避免上一关的计时或等待在本关误开生成。
/// </summary>
public class LevelOpeningGate : MonoBehaviour
{
    /// <summary>原「开场停顿 5 秒」秒数，保留为可选能力（Play 的 seconds 传它即恢复启动后的 5 秒缓冲）。</summary>
    public const float OpeningSeconds = 5f;

    /// <summary>等待玩家启动期间给生成器的硬锁秒数（每帧刷新，保证等待期间绝不出块）。</summary>
    private const float HoldWhileWaiting = 3600f;

    public static LevelOpeningGate Instance { get; private set; }

    private float remain;    // 启动后剩余的开场缓冲秒数（0 = 玩家一调用 GameStart 就立即开始出块）
    private bool waiting;    // 正在等待玩家代码调用 GameStart()
    private bool running;    // 已启动，正在按缓冲秒数倒计时
    private bool started;    // 本次关卡是否已被玩家启动（幂等标记，Play / Cancel 时重置）
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

    /// <summary>是否正在等待玩家代码调用 GameStart()（调试 / 教学判断用）。</summary>
    public static bool IsWaitingForGameStart
    {
        get { return Instance != null && Instance.waiting; }
    }

    /// <summary>
    /// 进入完整玩法关时的统一入口：立即隐藏炮台、硬锁方块生成，然后<b>停在等待状态</b>，
    /// 直到玩家代码调用 CodeTower.GameStart()（内部走 RequestStart）才真正开始出块。
    /// 注意：调用方需保证传入的 tower / spawner 可为 null（缺失时对应动作自动跳过）。
    /// </summary>
    /// <param name="seconds">启动后的开场缓冲秒数：0 = 调用 GameStart 即刻出块；传 OpeningSeconds 即原 5 秒节奏。</param>
    /// <param name="spawnBuffer">开始生成时给生成器的首块缓冲秒数。</param>
    public static void Play(CodeTower tower, FlyingBlockSpawner spawner, float seconds, float spawnBuffer)
    {
        LevelOpeningGate gate = Instance;
        if (gate == null)
        {
            GameObject go = new GameObject("LevelOpeningGate");
            gate = go.AddComponent<LevelOpeningGate>(); // AddComponent 会立即执行 Awake，Instance 随之就绪
        }

        // 先终止上一次可能仍在进行的等待 / 计时，再按本次关卡参数重新进入等待状态
        gate.running = false;
        gate.started = false;
        gate.waiting = true;
        gate.remain = Mathf.Max(0f, seconds);

        gate.tower = tower;
        gate.spawner = spawner;
        gate.spawnBuffer = Mathf.Max(0f, spawnBuffer);

        // 等待期间：炮台先不显示，方块先不生成（杜绝"进场瞬间显示炮台 / 瞬间出块"）
        if (gate.tower != null)
            gate.tower.SetVisualVisible(false);
        if (gate.spawner != null)
        {
            gate.spawner.SuspendSpawning();
            gate.spawner.BeginOpeningHold(HoldWhileWaiting); // 生成器侧硬锁：等待期间任何 BeginSpawning 都无效
        }
    }

    /// <summary>
    /// 玩家代码调用 CodeTower.GameStart() 时执行：结束等待、正式开始本关。
    /// 幂等：只有「仍在等待且尚未启动」的那一次返回 true 并产生效果，
    /// 第二次、第三次以及已经开始的关卡再次调用一律返回 false 且无任何副作用。
    /// </summary>
    public static bool RequestStart()
    {
        LevelOpeningGate gate = Instance;
        if (gate == null || !gate.waiting || gate.started)
            return false; // 没有等待中的门控（非完整玩法关）或已经启动过：直接忽略

        gate.started = true;
        gate.waiting = false;

        if (gate.spawner != null)
            gate.spawner.BeginOpeningHold(gate.remain); // 有缓冲秒数时重新按住生成器，等缓冲走完再放开

        if (gate.remain <= 0f)
        {
            gate.FinishOpening(); // 缓冲为 0：立即开始出块
        }
        else
        {
            gate.running = true;
            Debug.Log("[LevelOpeningGate] 玩家代码调用 GameStart()：本关正式开始，" +
                      "开场缓冲 " + gate.remain.ToString("F2") + " 秒后开始出块。");
        }
        return true;
    }

    /// <summary>取消开场门控（跳转到其他关卡 / 回主页时调用）：重置回等待状态，且不再触发显示 / 生成。</summary>
    public static void Cancel()
    {
        if (Instance == null)
            return;

        Instance.running = false;
        Instance.waiting = false;
        Instance.started = false;
        Instance.remain = 0f;
        if (Instance.spawner != null)
            Instance.spawner.EndOpeningHold(); // 同步解除生成器的停顿锁，避免下一关被残留停顿阻塞
    }

    private void Update()
    {
        if (waiting)
        {
            // 等待玩家启动：持续压制生成，并且绝不到期自动开始（未调用 GameStart 就永远不出块）
            if (spawner != null)
            {
                spawner.SuspendSpawning();
                spawner.BeginOpeningHold(HoldWhileWaiting);
            }
            return;
        }

        if (!running)
            return;

        // 缓冲期间持续压制生成：即使其他系统在此期间调用 BeginSpawning，也保持不生成
        if (spawner != null)
            spawner.SuspendSpawning();

        remain -= Time.unscaledDeltaTime; // 真实时间：E 键冻结 / 编辑器暂停期间也照常倒计时
        if (remain > 0f)
            return;

        FinishOpening();
    }

    /// <summary>结束开场门控：放开生成器硬锁并开始出块（炮台本体保持隐藏）。</summary>
    private void FinishOpening()
    {
        running = false;
        // 开场结束只开始生成方块：炮台本体永久不现身（不再显示），火力逻辑一直在原点照常工作
        if (spawner != null)
        {
            spawner.EndOpeningHold();           // 解除停顿锁
            spawner.BeginSpawning(spawnBuffer); // 开场结束才开始生成方块
        }
        Debug.Log("[LevelOpeningGate] 本关已开始：开始生成方块（炮台本体保持隐藏）。");
    }
}

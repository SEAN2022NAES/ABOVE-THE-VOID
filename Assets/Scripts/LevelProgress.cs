using UnityEngine;

/// <summary>
/// 关卡解锁进度（逐级解锁 + PlayerPrefs 持久化）：
///   - 首次进入游戏只有「关卡 0」（手动体验关）可进入，未解锁关卡在主菜单显示为灰显 + 锁定、点击无效；
///   - 通关某一关后自动解锁下一关（Level 0 → 1 → 2 → 3），进度写入 PlayerPrefs，跨重启保留；
///   - 开发者指令 /unlock（unlock / 解锁）可一键解锁全部关卡，同样持久化。
///
/// 通关判定（由代码编辑器教学关 / 手动体验关在「全部任务完成」时上报）：
///   - Level 0 通关 = Level 0 - Part 1（手动体验关，鼠标手动开火打方块）教学流程走完 → 解锁 Level 1；
///   - Level 1 通关 = Level 1 - Part 3（C# 基础语法课，49 项任务）全部完成 → 解锁 Level 2；
///   - Level 2 通关 = Level 2 - Part 2（炮台 API 课，15 项任务）全部完成 → 解锁 Level 3；
///   - Level 3 为最后一关，通关后不再解锁新关卡（仅记录已完成）。
/// </summary>
public static class LevelProgress
{
    /// <summary>最小关卡序号（0 = Level 0 - Part 1 手动体验关，玩家进入游戏最先看到的一关）。</summary>
    public const int MinLevel = 0;

    /// <summary>最大关卡序号（当前共 4 个关卡：0 / 1 / 2 / 3）。</summary>
    public const int MaxLevel = 3;

    /// <summary>PlayerPrefs 键：已解锁到的最高关卡（0 = 只解锁关卡 0）。</summary>
    private const string UnlockedPrefsKey = "ABOVETHEVOID.UnlockedLevel";

    /// <summary>PlayerPrefs 键：已通关的最高关卡（-1 = 尚未通关任何关卡）。</summary>
    private const string CompletedPrefsKey = "ABOVETHEVOID.CompletedLevel";

    /// <summary>「尚未通关任何关卡」的哨兵值（比 MinLevel = 0 更小，保证 IsLevelCompleted(0) 能区分「Level 0 已通关」与「什么都没通关」）。</summary>
    public const int NoCompleted = -1;

    private static bool loaded;
    private static int highestUnlockedLevel = MinLevel;
    private static int highestCompletedLevel = NoCompleted;

    /// <summary>已解锁的最高关卡序号。</summary>
    public static int HighestUnlockedLevel
    {
        get
        {
            EnsureLoaded();
            return highestUnlockedLevel;
        }
    }

    /// <summary>已通关的最高关卡序号（NoCompleted = 尚未通关任何关卡）。</summary>
    public static int HighestCompletedLevel
    {
        get
        {
            EnsureLoaded();
            return highestCompletedLevel;
        }
    }

    /// <summary>指定关卡是否已解锁（关卡序号从 0 开始：0 = 手动体验关）。</summary>
    public static bool IsLevelUnlocked(int level)
    {
        if (level < MinLevel || level > MaxLevel)
            return false;
        return level <= HighestUnlockedLevel;
    }

    /// <summary>指定关卡是否已通关。</summary>
    public static bool IsLevelCompleted(int level)
    {
        if (level < MinLevel || level > MaxLevel)
            return false;
        return level <= HighestCompletedLevel;
    }

    /// <summary>
    /// 标记某关已通关：记录通关进度，并把下一关设为已解锁。
    /// 返回 true 表示本次有进度变化（需要刷新主菜单按钮）。
    /// </summary>
    public static bool MarkLevelCompleted(int level)
    {
        EnsureLoaded();
        if (level < MinLevel || level > MaxLevel)
            return false;

        bool changed = false;

        if (level > highestCompletedLevel)
        {
            highestCompletedLevel = Mathf.Clamp(level, NoCompleted, MaxLevel);
            changed = true;
        }

        int nextLevel = Mathf.Clamp(level + 1, MinLevel, MaxLevel);
        if (level < MaxLevel && nextLevel > highestUnlockedLevel)
        {
            highestUnlockedLevel = nextLevel;
            changed = true;
        }

        if (changed)
            Save();
        return changed;
    }

    /// <summary>
    /// 一键解锁全部关卡（/unlock 指令）。
    /// 返回 true 表示本次确实解锁了新关卡；false 表示此前已全部解锁。
    /// </summary>
    public static bool UnlockAllLevels()
    {
        EnsureLoaded();
        if (highestUnlockedLevel >= MaxLevel)
            return false;

        highestUnlockedLevel = MaxLevel;
        Save();
        return true;
    }

    /// <summary>重置全部解锁 / 通关进度（调试用，回到「只有关卡 0 可玩」的首次进入状态）。</summary>
    public static void ResetProgress()
    {
        loaded = true;
        highestUnlockedLevel = MinLevel;
        highestCompletedLevel = NoCompleted;
        PlayerPrefs.DeleteKey(UnlockedPrefsKey);
        PlayerPrefs.DeleteKey(CompletedPrefsKey);
        PlayerPrefs.Save();
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;

        // 「首次使用」判定：两个存档键都不存在 = 全新机器 / 全新玩家（或刚被 /lock 清档）
        bool hasUnlockedKey = PlayerPrefs.HasKey(UnlockedPrefsKey);
        bool hasCompletedKey = PlayerPrefs.HasKey(CompletedPrefsKey);
        bool firstRun = !hasUnlockedKey && !hasCompletedKey;

        // 默认值即「只有关卡 0 可玩」：UnlockedLevel 缺省 MinLevel=0、CompletedLevel 缺省 NoCompleted。
        // 注意：这里只做「缺省读取」，不会写入 PlayerPrefs，也不会覆盖玩家已有的合法进度
        //（UnlockedLevel=1/2/3 的老存档原样保留，除非玩家自己执行 /lock 回厂重置）。
        // 老存档兼容：旧版 CompletedLevel 语义为「已通关的最高教学关（1 = 语法课线）」，与新版 Level 1 语义一致，
        // 故旧值可原样沿用（旧版不会写入 0，缺省即 NoCompleted），不会读崩也不会误判解锁。
        int unlocked = PlayerPrefs.GetInt(UnlockedPrefsKey, MinLevel);
        int completed = PlayerPrefs.GetInt(CompletedPrefsKey, NoCompleted);
        highestUnlockedLevel = Mathf.Clamp(unlocked, MinLevel, MaxLevel);
        highestCompletedLevel = Mathf.Clamp(completed, NoCompleted, MaxLevel);

        Debug.Log("[LevelProgress] 读取进度：UnlockedLevel=" + highestUnlockedLevel +
                  "，CompletedLevel=" + highestCompletedLevel +
                  (firstRun
                      ? "（首次使用：无存档键 → 仅关卡 0 可玩，关卡 1 / 2 / 3 锁定）"
                      : "（已有存档：原样沿用，不自动重置）"));
    }

    private static void Save()
    {
        PlayerPrefs.SetInt(UnlockedPrefsKey, highestUnlockedLevel);
        PlayerPrefs.SetInt(CompletedPrefsKey, highestCompletedLevel);
        PlayerPrefs.Save();
    }
}

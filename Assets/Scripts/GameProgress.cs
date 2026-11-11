using UnityEngine;

/// <summary>
/// 玩家游玩进度存档（断点续玩 + 各关最高分），全部基于 PlayerPrefs 持久化，跨重启保留。
///
/// 一、断点续玩（Resume）
///   - 记录玩家当前所处位置：关卡号 level + 分区 part + 章节 section（1 起；0 表示该分区无章节拆分）；
///   - 写入时机：LevelDirector.JumpTo 成功应用位置之后、代码编辑器教学关内「任务（章节）推进」时；
///   - 读取时机：主页点击某关按钮时（MainMenuController.StartLevel），存在属于该关的合法存档就从记录的
///     part / section 续玩；启动后仍停在主页，不做任何自动跳关；
///   - 合法性校验：level / part 必须落在已实现的分区集合内，section 必须在该分区章节数范围内，
///     任何脏值（越界 / 不存在 / 类型被手改）一律视为「无存档」，回落到该关开头，绝不因坏存档卡死。
///
/// 二、最高分（HighScore）
///   - 每关一个键：ABOVETHEVOID.HighScore.Level&lt;n&gt;（至少 Level 3 使用，主页按钮展示）；
///   - 提交时机：Level 3 一局结束（判负 GAME OVER / 返回主页 / 用 /level 跳走）时，本局分数高于历史最高分才写入；
///   - 无记录时 GetHighScore 返回 0、HasHighScore 返回 false，主页不显示最高分行。
///
/// 三、任务完成记录（TaskDone）
///   - 每个教学课程一份「哪些章节真的做过」的记录：键 ABOVETHEVOID.Done.&lt;课程名&gt;，内容为等长的 0/1 串；
///   - 用途：任务卡顶部的进度格只标记「玩家实际完成过的任务」，而不是「当前序号之前的一律算做过」，
///     所以用 /level 直接跳到第 10 个任务时，前面 9 个仍然是未完成状态；
///   - 写入时机：每个任务完成时立刻写盘；读取时机：进入课程（Begin）时读回。

/// 四、与 /lock 联动
///   - /lock 回厂重置调用 ClearAll()，一次清空续玩断点、任务完成记录与全部最高分存档。
///
/// 五、兼容性
///   - 未存过档的老玩家：所有读取都先判 HasKey，缺省一律视为「无存档」，行为与加持久化前完全一致
///     （主页进关卡从该关开头开始、主页不显示最高分），读取路径不写任何键。
/// </summary>
public static class GameProgress
{
    /// <summary>PlayerPrefs 键：续玩断点的关卡号（1~3）。</summary>
    public const string ResumeLevelKey = "ABOVETHEVOID.ResumeLevel";

    /// <summary>PlayerPrefs 键：续玩断点的分区号（1~3，仅 Level 1 用到 3）。</summary>
    public const string ResumePartKey = "ABOVETHEVOID.ResumePart";

    /// <summary>PlayerPrefs 键：续玩断点的章节号（1 起；0 = 该分区无章节拆分）。</summary>
    public const string ResumeSectionKey = "ABOVETHEVOID.ResumeSection";

    /// <summary>PlayerPrefs 键前缀：各关最高分，完整键名 = 前缀 + 关卡号（如 ABOVETHEVOID.HighScore.Level3）。</summary>
    public const string HighScoreKeyPrefix = "ABOVETHEVOID.HighScore.Level";

    /// <summary>PlayerPrefs 键前缀：各课程「已完成任务」记录，完整键名 = 前缀 + 课程名（如 ABOVETHEVOID.Done.Api）。</summary>
    public const string TaskDoneKeyPrefix = "ABOVETHEVOID.Done.";

    /// <summary>C# 语法课（Level 1 - Part 3）的任务完成记录键名。</summary>
    public const string TaskDoneKeyGrammar = TaskDoneKeyPrefix + "Grammar";

    /// <summary>炮台 API 课（Level 2 - Part 2）的任务完成记录键名。</summary>
    public const string TaskDoneKeyApi = TaskDoneKeyPrefix + "Api";

    /// <summary>方块图鉴关（Level 2 - Part 1）的页码完成记录键名。</summary>
    public const string TaskDoneKeyBlockIntro = TaskDoneKeyPrefix + "BlockIntro";

    /// <summary>分区号最小值。</summary>
    public const int MinPart = 1;

    /// <summary>分区号最大值（Level 1 有 part 1 / 2 / 3，Level 2 有 part 1 / 2，其余关卡只有 part 1）。</summary>
    public const int MaxPart = 3;

    /// <summary>本次运行的当前位置：关卡号（0 = 不在任何关卡内，例如停在主页）。</summary>
    public static int CurrentLevel { get; private set; }

    /// <summary>本次运行的当前位置：分区号。</summary>
    public static int CurrentPart { get; private set; }

    /// <summary>本次运行的当前位置：章节号（0 = 该分区无章节拆分）。</summary>
    public static int CurrentSection { get; private set; }

    /// <summary>本次运行是否处于某个关卡内（主页为 false）。</summary>
    public static bool InLevel
    {
        get { return CurrentLevel >= LevelProgress.MinLevel; }
    }

    // ==================== 分区 / 章节合法性 ====================

    /// <summary>
    /// 该「关卡 - 分区」是否为已实现的分区（与 LevelDirector.JumpTo 的分区集合保持一致：
    /// level 1 - part 1（引导教学）、level 1 - part 2（编程序章讲解）、level 1 - part 3（C# 语法课）、
    /// level 2 - part 1（方块图鉴）、level 2 - part 2（炮台 API 课）、level 3 - part 1）。
    /// </summary>
    public static bool IsSupportedLocation(int level, int part)
    {
        if (level < LevelProgress.MinLevel || level > LevelProgress.MaxLevel)
            return false;
        if (part < MinPart || part > MaxPart)
            return false;
        if (level == 1)
            return part == 1 || part == 2 || part == 3;
        if (level == 2)
            return part == 1 || part == 2;
        return part == 1;
    }

    /// <summary>该关卡的分区总数（Level 1 为 3、Level 2 为 2，其余为 1）。</summary>
    public static int GetPartCount(int level)
    {
        if (level == 1)
            return 3;
        if (level == 2)
            return 2;
        return 1;
    }

    /// <summary>
    /// 该「关卡 - 分区」的章节（section）总数；返回 0 表示该分区没有章节拆分。
    /// 章节拆分：Level 1 - Part 2 编程序章（页数，可翻页）、Level 1 - Part 3 C# 语法课（18 项任务）、
    /// Level 2 - Part 1 方块图鉴（页数）、Level 2 - Part 2 API 课（13 项任务）。
    /// </summary>
    public static int GetSectionCount(int level, int part)
    {
        if (level == 1 && part == 2)
            return CodeIntroTutorialController.PageCount;
        if (level == 1 && part == 3)
            return CodeSyntaxTutorialController.GetTaskCount(CodeSyntaxTutorialController.Course.Grammar);
        if (level == 2 && part == 1)
            return BlockIntroController.PageCount;
        if (level == 2 && part == 2)
            return CodeSyntaxTutorialController.GetTaskCount(CodeSyntaxTutorialController.Course.Api);
        return 0;
    }

    /// <summary>教学课程 → 「关卡 - 分区」映射（语法课 = Level 1 - Part 3，API 课 = Level 2 - Part 2）。</summary>
    public static void GetCourseLocation(CodeSyntaxTutorialController.Course course, out int level, out int part)
    {
        if (course == CodeSyntaxTutorialController.Course.Api)
        {
            level = 2;
            part = 2;
        }
        else
        {
            level = 1;
            part = 3;
        }
    }

    // ==================== 任务完成记录 ====================

    /// <summary>
    /// 读取某课程「哪些章节真的做过」的记录，长度 = count；没有记录时返回全 false 的数组（不写任何键）。
    /// 记录的是一份独立的完成表，而不是「当前序号之前的都算做过」——用 /level 直接跳到第 10 个任务时，
    /// 第 1~9 个不会被误标为已完成。
    /// </summary>
    public static bool[] LoadTaskDone(string key, int count)
    {
        bool[] done = new bool[Mathf.Max(0, count)];
        if (string.IsNullOrEmpty(key) || count <= 0)
            return done;
        if (!PlayerPrefs.HasKey(key))
            return done;

        string raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(raw))
            return done;

        int len = Mathf.Min(raw.Length, done.Length);
        for (int i = 0; i < len; i++)
            done[i] = raw[i] == '1';
        return done;
    }

    /// <summary>写入某课程的任务完成记录（0/1 串）并 PlayerPrefs.Save()；空数组视为清除该键。</summary>
    public static void SaveTaskDone(string key, bool[] done)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (done == null || done.Length == 0)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return;
        }

        char[] chars = new char[done.Length];
        for (int i = 0; i < done.Length; i++)
            chars[i] = done[i] ? '1' : '0';
        PlayerPrefs.SetString(key, new string(chars));
        PlayerPrefs.Save();
    }

    /// <summary>清除全部课程的任务完成记录（/lock 回厂重置）。</summary>
    public static void ClearAllTaskDone()
    {
        PlayerPrefs.DeleteKey(TaskDoneKeyGrammar);
        PlayerPrefs.DeleteKey(TaskDoneKeyApi);
        PlayerPrefs.DeleteKey(TaskDoneKeyBlockIntro);
        PlayerPrefs.Save();
    }

    // ==================== 断点续玩：写 ====================

    /// <summary>只更新本次运行的当前位置（不写盘），供跳转 / 进关时同步内存状态。</summary>
    public static void SetSessionPosition(int level, int part, int section)
    {
        CurrentLevel = Mathf.Clamp(level, 0, LevelProgress.MaxLevel);
        CurrentPart = Mathf.Clamp(part, 0, MaxPart);
        CurrentSection = Mathf.Max(0, section);
    }

    /// <summary>清空本次运行的当前位置（回到主页 / 回厂重置）。</summary>
    public static void ClearSessionPosition()
    {
        CurrentLevel = 0;
        CurrentPart = 0;
        CurrentSection = 0;
    }

    /// <summary>
    /// 写入续玩断点（关卡 + 分区 + 章节）并 PlayerPrefs.Save()。
    /// 写入前做 Clamp 合法性校验：非法分区直接拒绝写入（返回 false），章节越界自动收敛到 [1, 章节总数]。
    /// /level 跳关优先级最高：JumpTo 成功后这里会用跳关参数覆盖旧存档，跳关后的位置即新的续玩点。
    /// </summary>
    public static bool SaveResume(int level, int part, int section)
    {
        SetSessionPosition(level, part, section);

        if (!IsSupportedLocation(level, part))
        {
            Debug.LogWarning("[GameProgress] 拒绝写入续玩存档：Level " + level + " - Part " + part + " 不是已实现的分区。");
            return false;
        }

        int maxSection = GetSectionCount(level, part);
        int safeSection = maxSection > 0 ? Mathf.Clamp(section, 1, maxSection) : 0;

        PlayerPrefs.SetInt(ResumeLevelKey, level);
        PlayerPrefs.SetInt(ResumePartKey, part);
        PlayerPrefs.SetInt(ResumeSectionKey, safeSection);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>
    /// 该分区全部章节已完成（整关通关）时清掉属于它的续玩断点：下次进入从该关开头开始，
    /// 避免「已完成最后一章」的存档把玩家直接丢进已通关的末章。
    /// </summary>
    public static void ClearResumeIfMatches(int level, int part)
    {
        if (!PlayerPrefs.HasKey(ResumeLevelKey))
            return;
        if (PlayerPrefs.GetInt(ResumeLevelKey, 0) != level)
            return;
        if (PlayerPrefs.GetInt(ResumePartKey, 0) != part)
            return;
        ClearResume();
    }

    /// <summary>清除续玩断点（/lock 回厂重置、整关通关后调用）。</summary>
    public static void ClearResume()
    {
        PlayerPrefs.DeleteKey(ResumeLevelKey);
        PlayerPrefs.DeleteKey(ResumePartKey);
        PlayerPrefs.DeleteKey(ResumeSectionKey);
        PlayerPrefs.Save();
    }

    // ==================== 断点续玩：读 ====================

    /// <summary>
    /// 读取指定关卡的续玩断点（只读：只 GetInt，不写任何键）。
    /// 返回 false 表示没有可用存档，调用方应从该关开头进入，包含这些情况：
    ///   · 从未存过档（老玩家）或刚被 /lock 清档；
    ///   · 存档属于别的关卡（与本次点击的关卡不匹配）；
    ///   · 脏值：level / part 非法、section 越界或指向不存在的章节（一律回落到该关开头）。
    /// </summary>
    public static bool TryGetResume(int level, out int part, out int section)
    {
        part = MinPart;
        section = 0;

        if (level < LevelProgress.MinLevel || level > LevelProgress.MaxLevel)
            return false;

        // 老玩家 / 清档后：完全没有存档键 → 视为无存档（不写任何键，行为与改动前一致）
        if (!PlayerPrefs.HasKey(ResumeLevelKey))
            return false;

        int savedLevel = PlayerPrefs.GetInt(ResumeLevelKey, 0);
        int savedPart = PlayerPrefs.GetInt(ResumePartKey, 0);
        int savedSection = PlayerPrefs.GetInt(ResumeSectionKey, 0);

        if (savedLevel != level)
            return false; // 存档属于别的关卡：与本关无关，从本关开头进入

        // 存档迁移（教学关重排）：旧版本里「Level 1 - Part 2」是 C# 语法课（18 项任务），
        // 现在语法课整体挪到 Level 1 - Part 3，Part 2 改为「编程序章」讲解关（4 页）。
        // 旧存档的 part=2 一定指旧语法课，因此原样迁移到 part=3（章节号保留，越界则回落到第 1 章），
        // 迁移后立即写回盘，保证玩家进度不因这次重排而回退，也不会被反复迁移。
        if (savedLevel == 1 && savedPart == 2)
        {
            int grammarCount = CodeSyntaxTutorialController.GetTaskCount(CodeSyntaxTutorialController.Course.Grammar);
            int migrateSection = (savedSection >= 1 && savedSection <= grammarCount) ? savedSection : 1;
            Debug.Log("[GameProgress] 旧存档迁移：Level 1 - Part 2（旧语法课）第 " + savedSection +
                      " 章 → Level 1 - Part 3 第 " + migrateSection + " 章。");
            savedPart = 3;
            savedSection = migrateSection;
            SaveResume(savedLevel, savedPart, savedSection);
        }

        if (!IsSupportedLocation(savedLevel, savedPart))
        {
            Debug.LogWarning("[GameProgress] 续玩存档脏值（Level " + savedLevel + " - Part " + savedPart +
                             " 不是已实现的分区），回落到该关开头。");
            return false;
        }

        int maxSection = GetSectionCount(savedLevel, savedPart);
        if (maxSection <= 0)
        {
            part = savedPart; // 该分区无章节拆分：只要分区合法就能续玩
            return true;
        }

        if (savedSection < 1 || savedSection > maxSection)
        {
            Debug.LogWarning("[GameProgress] 续玩存档章节越界（Level " + savedLevel + " - Part " + savedPart +
                             " 章节 " + savedSection + "，合法范围 1~" + maxSection + "），回落到该关开头。");
            return false;
        }

        part = savedPart;
        section = savedSection;
        return true;
    }

    // ==================== 最高分 ====================

    /// <summary>最高分存档键名（ABOVETHEVOID.HighScore.Level&lt;关卡号&gt;）。</summary>
    public static string GetHighScoreKey(int level)
    {
        return HighScoreKeyPrefix + level;
    }

    /// <summary>该关是否已有最高分记录（无记录 = 从未在该关拿到正分，主页不显示最高分行）。</summary>
    public static bool HasHighScore(int level)
    {
        if (level < LevelProgress.MinLevel || level > LevelProgress.MaxLevel)
            return false;
        return PlayerPrefs.HasKey(GetHighScoreKey(level));
    }

    /// <summary>该关历史最高分；无记录返回 0。</summary>
    public static int GetHighScore(int level)
    {
        if (level < LevelProgress.MinLevel || level > LevelProgress.MaxLevel)
            return 0;
        return Mathf.Max(0, PlayerPrefs.GetInt(GetHighScoreKey(level), 0));
    }

    /// <summary>
    /// 提交本局分数为该关最高分：仅当本局得分 &gt; 0 且高于历史最高分时写入并 PlayerPrefs.Save()。
    /// 返回 true 表示刷新了纪录（主页需要重绘按钮）。
    /// </summary>
    public static bool SaveHighScore(int level, int score)
    {
        if (level < LevelProgress.MinLevel || level > LevelProgress.MaxLevel)
            return false;
        if (score <= 0)
            return false; // 0 / 负分不产生记录，避免出现负的最高分

        if (HasHighScore(level) && score <= GetHighScore(level))
            return false;

        PlayerPrefs.SetInt(GetHighScoreKey(level), score);
        PlayerPrefs.Save();
        Debug.Log("[GameProgress] Level " + level + " 最高分刷新为 " + score + "。");
        return true;
    }

    /// <summary>
    /// 一局结束时结算当前关卡的最高分（GAME OVER / 返回主页 / 用 /level 跳走时调用）：
    /// 只有本次运行确实处于 Level 3 时才写档，其它关卡 / 主页不产生任何写入。
    /// </summary>
    public static bool CommitCurrentLevelHighScore(int score)
    {
        if (!InLevel)
            return false;
        if (CurrentLevel != 3)
            return false;
        return SaveHighScore(3, score);
    }

    /// <summary>清空全部关卡的最高分存档（/lock 回厂重置）。</summary>
    public static void ClearAllHighScores()
    {
        for (int level = LevelProgress.MinLevel; level <= LevelProgress.MaxLevel; level++)
            PlayerPrefs.DeleteKey(GetHighScoreKey(level));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// /lock 回厂重置的统一清理入口：续玩断点（Level / Part / Section 三个键）+ 全部关卡最高分 + 各课程任务完成记录
    /// 一次清空，并把本次运行的当前位置重置为「不在任何关卡内」。
    /// </summary>
    public static void ClearAll()
    {
        PlayerPrefs.DeleteKey(ResumeLevelKey);
        PlayerPrefs.DeleteKey(ResumePartKey);
        PlayerPrefs.DeleteKey(ResumeSectionKey);
        PlayerPrefs.DeleteKey(TaskDoneKeyGrammar);
        PlayerPrefs.DeleteKey(TaskDoneKeyApi);
        PlayerPrefs.DeleteKey(TaskDoneKeyBlockIntro);
        for (int level = LevelProgress.MinLevel; level <= LevelProgress.MaxLevel; level++)
            PlayerPrefs.DeleteKey(GetHighScoreKey(level));
        PlayerPrefs.Save();
        ClearSessionPosition();
        Debug.Log("[GameProgress] 回厂重置：续玩断点、任务完成记录与全部最高分存档已清空。");
    }
}

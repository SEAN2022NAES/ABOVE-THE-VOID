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
    /// <summary>PlayerPrefs 键：续玩断点的关卡号（0~3；0 = Level 0 手动体验关）。</summary>
    public const string ResumeLevelKey = "ABOVETHEVOID.ResumeLevel";

    /// <summary>PlayerPrefs 键：续玩断点的分区号（1 起；Level 1 用到 3、Level 2 用到 2，Level 0 / 3 只有 1）。</summary>
    public const string ResumePartKey = "ABOVETHEVOID.ResumePart";

    /// <summary>PlayerPrefs 键：续玩断点的章节号（1 起；0 = 该分区无章节拆分）。</summary>
    public const string ResumeSectionKey = "ABOVETHEVOID.ResumeSection";

    /// <summary>PlayerPrefs 键：教学分区重排（语法课 Part 3 → Part 4，后续又由 Level0Shift 迁回 Part 3）的一次性存档迁移标记。</summary>
    private const string MigrateEditorIntroKey = "GameProgress.Migrate.EditorIntro.v1";

    /// <summary>
    /// PlayerPrefs 键：Level 0 插入后「原 Level 1 整体后移」的一次性存档迁移标记
    /// （旧 Level 1 - Part 1/2/3/4 → 新 Level 0 - Part 1 / Level 1 - Part 1/2/3）。
    /// </summary>
    private const string MigrateLevel0ShiftKey = "GameProgress.Migrate.Level0Shift.v1";

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

    /// <summary>分区号最大值（Level 1 有 part 1 / 2 / 3，Level 2 有 part 1 / 2，Level 0 / Level 3 只有 part 1）。</summary>
    public const int MaxPart = 3;

    /// <summary>「不在任何关卡内」的哨兵关卡号（主页 / 回厂重置后使用）。</summary>
    public const int NoLevel = -1;

    /// <summary>本次运行的当前位置：关卡号（NoLevel = 不在任何关卡内，例如停在主页）。</summary>
    public static int CurrentLevel { get; private set; } = NoLevel;

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
    /// level 0 - part 1（手动体验关，原「引导教学关」）、level 0 - part 2（自由玩耍关）、
    /// level 1 - part 1（编程序章讲解）、level 1 - part 2（代码编辑器界面入门）、level 1 - part 3（C# 语法课）、
    /// level 2 - part 1（方块图鉴）、level 2 - part 2（炮台 API 课）、level 3 - part 1）。
    /// </summary>
    public static bool IsSupportedLocation(int level, int part)
    {
        if (level < LevelProgress.MinLevel || level > LevelProgress.MaxLevel)
            return false;
        if (part < MinPart || part > MaxPart)
            return false;
        if (level == 0)
            return part == 1 || part == 2;
        if (level == 1)
            return part == 1 || part == 2 || part == 3;
        if (level == 2)
            return part == 1 || part == 2;
        return part == 1;
    }

    /// <summary>该关卡的分区总数（Level 0 为 2、Level 1 为 3、Level 2 为 2，其余为 1）。</summary>
    public static int GetPartCount(int level)
    {
        if (level == 0)
            return 2;
        if (level == 1)
            return 3;
        if (level == 2)
            return 2;
        return 1;
    }

    /// <summary>
    /// 该「关卡 - 分区」的章节（section）总数；返回 0 表示该分区没有章节拆分。
    /// 章节拆分：Level 1 - Part 1 编程序章（页数，可翻页）、Level 1 - Part 2 代码编辑器界面入门（8 页）、
    /// Level 1 - Part 3 C# 语法课（49 项任务）、
    /// Level 2 - Part 1 方块图鉴（页数）、Level 2 - Part 2 API 课（15 项任务）。
    /// </summary>
    public static int GetSectionCount(int level, int part)
    {
        if (level == 1 && part == 1)
            return CodeIntroTutorialController.PageCount;
        if (level == 1 && part == 2)
            return EditorIntroTutorialController.PageCount;
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
        CurrentLevel = Mathf.Clamp(level, NoLevel, LevelProgress.MaxLevel);
        CurrentPart = Mathf.Clamp(part, 0, MaxPart);
        CurrentSection = Mathf.Max(0, section);
    }

    /// <summary>清空本次运行的当前位置（回到主页 / 回厂重置）。</summary>
    public static void ClearSessionPosition()
    {
        CurrentLevel = NoLevel;
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
        // 只要本版本写过一次存档，就说明这份存档已经是新编号体系，
        // 顺手把「旧存档迁移」标记置位，避免升级后用 /level 跳过教学再回主页时把新存档误迁。
        PlayerPrefs.SetInt(MigrateLevel0ShiftKey, 1);
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

        // 注意：迁移必须在「存档是否属于本关」的比对之前执行 ——
        // 旧 Level 1 - Part 1 存档迁移后会变成 Level 0 - Part 1，若先比对关卡号就会永远走不到迁移分支。

        // 存档迁移（教学关二次重排）：上一版的「Level 1 - Part 3」是 C# 语法课（18 项任务），
        // 这一版把语法课整体挪到 Level 1 - Part 4，Part 3 让位给新的「代码编辑器界面入门」（8 页）。
        // 旧存档 part=3 一定指旧语法课，因此原样迁移到 part=4（章节号保留，越界则回落到第 1 章）。
        // 迁移只做一次：用 PlayerPrefs 一次性标记，否则新版本里玩家在 Part 3 的正常进度
        //（编辑器入门第 n 页，n ≤ 8，与语法的 1~18 页范围重叠）会在每次启动时被反复拽到 Part 4。
        if (PlayerPrefs.GetInt(MigrateEditorIntroKey, 0) == 0)
        {
            PlayerPrefs.SetInt(MigrateEditorIntroKey, 1);

            if (savedLevel == 1 && savedPart == 3)
            {
                int grammarCount = CodeSyntaxTutorialController.GetTaskCount(CodeSyntaxTutorialController.Course.Grammar);
                int migrateSection = (savedSection >= 1 && savedSection <= grammarCount) ? savedSection : 1;
                Debug.Log("[GameProgress] 旧存档迁移：Level 1 - Part 3（旧语法课）第 " + savedSection +
                          " 章 → Level 1 - Part 4 第 " + migrateSection + " 章。");
                savedPart = 4;
                savedSection = migrateSection;
                SaveResume(savedLevel, savedPart, savedSection);
            }

            PlayerPrefs.Save();
        }

        // 存档迁移（插入 Level 0 - Part 1 手动体验关，原 Level 1 整体后移一位）：
        //   旧 Level 1 - Part 1（引导教学）→ 新 Level 0 - Part 1（手动体验关，无章节）
        //   旧 Level 1 - Part 2（编程序章）→ 新 Level 1 - Part 1（同一控制器，章节号直接沿用）
        //   旧 Level 1 - Part 3（编辑器入门）→ 新 Level 1 - Part 2（同一控制器，章节号直接沿用）
        //   旧 Level 1 - Part 4（语法课）→ 新 Level 1 - Part 3（章节号沿用，越界由 SaveResume 收敛）
        // Level 2 / Level 3 编号不变，不做迁移。
        // 迁移只做一次（一次性标记）：否则新版本里玩家在 Level 1 - Part 1/2/3 的正常进度会被反复往回拽。
        if (PlayerPrefs.GetInt(MigrateLevel0ShiftKey, 0) == 0)
        {
            PlayerPrefs.SetInt(MigrateLevel0ShiftKey, 1);

            // 判别旧 1-1：旧版 Part 1（引导教学）没有章节拆分，section 恒为 0；
            // 新版 Level 1 - Part 1（编程序章）一定有章节号（SaveResume 会收敛到 1..PageCount）。
            // 因此只有 section == 0 才是旧 1-1 —— 否则升级后若先用 /level 1 1 写下新存档，
            // 首次点关卡时会被误判成旧存档并拽回 Level 0。
            if (savedLevel == 1 && (savedPart >= 2 || savedSection == 0))
            {
                int newLevel = savedPart == 1 ? 0 : 1;
                int newPart = savedPart == 1 ? 1 : savedPart - 1;
                int newSection = newLevel == 0 ? 0 : savedSection;
                Debug.Log("[GameProgress] 旧存档迁移（Level 0 插入）：Level 1 - Part " + savedPart +
                          " 第 " + savedSection + " 章 → Level " + newLevel + " - Part " + newPart +
                          " 第 " + newSection + " 章。");
                savedLevel = newLevel;
                savedPart = newPart;
                savedSection = newSection;
                SaveResume(savedLevel, savedPart, savedSection);
            }

            PlayerPrefs.Save();
        }

        // 迁移完成后才比对关卡号：存档属于别的关卡时与本关无关，从本关开头进入
        if (savedLevel != level)
            return false;

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

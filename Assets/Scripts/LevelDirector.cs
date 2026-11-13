using UnityEngine;

/// <summary>
/// 关卡跳转导演（开发者指令 /level 专用）：
///   - 负责从任意状态跳转到指定「关卡 - 分区」（已实现 level 1 - part 1 引导教学 /
///     level 1 - part 2 编程序章 / level 1 - part 3 C# 语法课 / level 2 - part 1 方块图鉴 /
///     level 2 - part 2 炮台 API 课 / level 3 - part 1）；
///   - 跳转动作：隐藏主页（若显示）、重置玩家到开局、清空场上长方体并重新开始生成、
///     按分区配置顶部时间提示显隐；
///   - JumpTo 返回面向指令框的结果文本。
/// </summary>
public static class LevelDirector
{
    /// <summary>跳转到指定关卡分区（从该分区第 1 章开始）。</summary>
    public static string JumpTo(int level, int part)
    {
        return JumpTo(level, part, 0);
    }

    /// <summary>
    /// 跳转到指定关卡分区，并可直接定位到该分区第 section 个 part 章节（1 起；section &lt;= 0 表示从第 1 章开始）。
    /// 章节拆分仅存在于教学关（Level 1 - Part 2 讲解页 / Level 1 - Part 3 语法课 / Level 2 - Part 1 方块图鉴 / Level 2 - Part 2 API 课），
    /// 其他关卡传入 section 时按「无章节拆分、从开头进入」处理并提示。
    /// </summary>
    public static string JumpTo(int level, int part, int section)
    {
        bool level1 = (level == 1 && part == 1);        // 引导教学关（全程手动点方块）
        bool levelIntro = (level == 1 && part == 2);    // 编程序章讲解关（多页阅读，最后一页直接进语法课）
        bool levelGrammar = (level == 1 && part == 3);  // C# 基础语法教学关（代码编辑器内教学，18 项任务）
        bool levelBlockIntro = (level == 2 && part == 1); // 方块图鉴关（旋转立方体 + 多页阅读，最后一页直接进 API 课）
        bool levelApi = (level == 2 && part == 2);      // 炮台 API 调用教学关（代码编辑器内教学，13 项任务）
        bool level3 = (level == 3 && part == 1);        // 完整玩法关（跳过教学）
        if (!level1 && !levelIntro && !levelGrammar && !levelBlockIntro && !levelApi && !level3)
            return "[level] 该关卡部分尚未开放（当前已实现：level 1 - part 1、level 1 - part 2、level 1 - part 3、" +
                   "level 2 - part 1、level 2 - part 2、level 3 - part 1）。";

        // 章节参数校验：必须在改动任何状态之前完成，越界只提示、不跳转、不崩
        int chapterCount = 0;
        if (levelIntro)
            chapterCount = CodeIntroTutorialController.PageCount; // 讲解关的「章节」= 讲解页
        else if (levelGrammar)
            chapterCount = CodeSyntaxTutorialController.GetTaskCount(CodeSyntaxTutorialController.Course.Grammar);
        else if (levelBlockIntro)
            chapterCount = BlockIntroController.PageCount;       // 图鉴关的「章节」= 图鉴页
        else if (levelApi)
            chapterCount = CodeSyntaxTutorialController.GetTaskCount(CodeSyntaxTutorialController.Course.Api);

        bool useSection = (section > 0);
        if (useSection)
        {
            if (chapterCount <= 0)
                useSection = false; // 该关卡没有章节拆分：忽略章节参数
            else if (section > chapterCount)
                return "[level] Level " + level + " - Part " + part + " 共 " + chapterCount +
                       " 个 part 章节，章节参数 n 取值范围 1~" + chapterCount + "。";
        }

        // 一局结束结算：若当前正处在 Level 3，先用本局分数结算最高分再跳走
        //   （下面会 ScoreManager.ResetAll() 把分数清零，结算必须在清零之前；其它关卡 / 主页不产生写入）
        GameProgress.CommitCurrentLevelHighScore(ScoreManager.Instance != null ? ScoreManager.Instance.Score : 0);

        // 先终止上一次可能仍在进行的开场停顿，避免它在本关结束后误显示炮台 / 误开启生成
        LevelOpeningGate.Cancel();

        // 离开主页进入关卡：恢复三维坐标轴显示（主页期间被 LevelPresentation.ApplyMainMenu 隐藏）
        LevelPresentation.RestoreAxes();

        // 先结束可能仍在进行的教学序列（编程序章 / 方块图鉴 / 语法课），跳进对应分区时会在下方重新开始并重置进度
        CodeIntroTutorialController.End();
        BlockIntroController.End();
        CodeSyntaxTutorialController.End();

        // 代码编辑器复位（必须在下面各教学 Begin() 之前）：离开上一关时把代码文本、编译结果（runner）、
        // 撤销栈、defaultCodeFilled 全部清回本关初始状态。
        // 之前没有任何收尾，导致 Level 3 - Part 1 编译运行后跳进 Level 1 - Part 1，上一关的 Run() 仍在每帧驱动炮台。
        CodeEditorController levelEditor = Object.FindObjectOfType<CodeEditorController>();
        if (levelEditor != null)
            levelEditor.ResetForLevelChange();

        // 隐藏主页（若主页仍显示，如开发者指令在主页面板输入时）
        MainMenuController menu = Object.FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.HideMenu();

        // 重置玩家到关卡开局：恢复视角原点朝向、解除全部模态 / 冻结 / 暂停
        PlayerCameraController player = Object.FindObjectOfType<PlayerCameraController>();
        if (player != null)
            player.ResetToLevelStart();

        TutorialController tutorial = Object.FindObjectOfType<TutorialController>();

        // Level 3 完整玩法 / Level 1 Part 2 讲解关 / Level 2 Part 1 图鉴关 / Level 1 Part 3 语法教学 / Level 2 Part 2 API 教学：
        // 若正处 Part 1 引导教学，先退出教学，确保教学面板隐藏、Started=false（E 键时间冻结恢复可用；编辑器教学关还需编辑器可打开）
        if ((level3 || levelIntro || levelBlockIntro || levelGrammar || levelApi) && tutorial != null)
            tutorial.ExitToMenu();

        // 清空场上所有长方体；教学关由 Tutorial / 语法教学接管生成时机（先不生成），Level 3 先进入开场停顿
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner != null)
        {
            spawner.ClearAllBlocks();
            if (level1)
            {
                if (tutorial != null)
                    spawner.SuspendSpawning();
                else
                    spawner.BeginSpawning();
            }
            else
            {
                spawner.ResetSession();     // Level 3 / 讲解关 / 语法课 / API 关：重开会话（清零计数与编号）
                spawner.SuspendSpawning();  // 先暂停生成：Level 3 等 5 秒开场停顿，编辑器教学关等全部任务完成
            }
        }

        // Level 1 Part 1 为引导教学关：启动教学流程（重复跳转即重学）
        if (level1 && tutorial != null)
            tutorial.StartTutorial();

        // 章节起始序号（0 基）：/level 1 3 3 与 /level 2 2 3 表示从该分区第 3 个章节开始
        int startIndex = useSection ? (section - 1) : 0;

        // Level 1 Part 2 为「编程序章」讲解关：进入即显示讲解卡片（重复进入即回到第一页 = 重置阅读进度）
        if (levelIntro)
            CodeIntroTutorialController.Begin(startIndex);

        // Level 2 Part 1 为「方块图鉴」关：进入即显示旋转立方体 + 图鉴页
        if (levelBlockIntro)
            BlockIntroController.Begin(startIndex);

        // Level 1 Part 3 为 C# 基础语法教学关：进入即启用编辑器内教学序列（重复进入即重置进度）
        if (levelGrammar)
            CodeSyntaxTutorialController.Begin(CodeSyntaxTutorialController.Course.Grammar, startIndex);

        // Level 2 Part 2 为炮台 API 调用教学关：同样进入即启用编辑器内教学序列（重复进入即重置进度）
        if (levelApi)
            CodeSyntaxTutorialController.Begin(CodeSyntaxTutorialController.Course.Api, startIndex);

        // Level 1（Part 1 / 2 / 3）与 Level 2（Part 1 / 2）不显示顶部时间提示（Level 3 显示，保留实现）；Level 3 重新开始计时
        TimeHintController timeHint = Object.FindObjectOfType<TimeHintController>();
        if (timeHint != null)
        {
            timeHint.SetVisible(level >= 3);
            if (level3)
                timeHint.ResetTimer(); // Level 3：顶部时间提示重新从 00:00 开始
        }

        // 炮台实体显隐：Level 1 教学关不显示实体炮台（原点保留炮台功能）；
        // Level 3 完整玩法关同样先隐藏，等 5 秒开场停顿结束后由 LevelOpeningGate 显示（避免跳转瞬间炮台就出现）
        CodeTower tower = Object.FindObjectOfType<CodeTower>();
        if (tower != null)
            tower.SetVisualVisible(false);

        // 关卡表现开关：Level 1 Part 1 / Part 2（讲解关）不显示 Score / print 输出面板、只出红块；
        // Level 2 Part 1 方块图鉴关同样按最精简表现（纯阅读，不出方块）；
        // Part 3 语法教学关不显示 Score 但显示 print 荧幕（观察 PrintLine 输出）、只出红块；
        // Level 2 Part 2 API 教学关不显示 Score、显示 print 荧幕，并允许蓝块（教学 Type 取值）；
        // Level 3 全部恢复
        if (level1 || levelIntro || levelBlockIntro)
            LevelPresentation.ApplyLevel1();
        else if (levelGrammar)
            LevelPresentation.ApplyLevel2();
        else if (levelApi)
            LevelPresentation.ApplyLevel2Api();
        else
            LevelPresentation.ApplyLevel3();

        // 计分：每次跳转关卡都重开一局分数（击毁红块加分 / 红块到基地扣分 / 蓝块可击毁但不计分）
        ScoreManager.ResetAll();

        // 生命值：每次跳转关卡都重开一局生命（默认 5 点），并清掉上一局可能残留的 Game Over 面板
        LivesManager.ResetAll();

        // 鼠标左键手动激光：只有 Level 1 - Part 1（引导教学关，教学 Step.FireLaser 需要左键开火）允许，
        // 其余所有关卡（Part 2 讲解关 / Part 3 语法课 / API 关 / Level 3 完整玩法关）一律禁用。
        // 仅影响「左键 → FireLaser」手动链路，不影响 tower.FireAtPlane 等自动化调用。
        CommandConsoleController.SetManualLaserAllowed(level, part);

        // Level 3 完整玩法关：进场必须处于「正常时间流逝 + 无代码编辑器 + 无进场代码提示」状态。
        // 旧实现会在这里主动冻结时间并弹「按 Enter 输入代码」提示，导致从其它关卡切进来时
        // 像是直接进入了代码模式、关掉代码后仍停在暂停；现改为强制收尾：
        //   1) 无条件强制关闭代码编辑器（含 Part 2 / API 教学留下的编辑器），并抑制紧随其后的同一次 Enter 再次打开；
        //   2) 清掉可能残留的进场代码提示；
        //   3) 解除残留的 E 键时间冻结并确保 timeScale = 1（正常计时、方块计时器正常走）。
        CodeEditorController editor = Object.FindObjectOfType<CodeEditorController>();
        if (editor != null)
        {
            editor.HideEnterPrompt();
            if (level3)
                editor.ForceCloseSelf(); // 静态入口 ForceCloseIfOpen() 会另找实例，这里直接对已取到的实例收尾
        }
        if (level3)
        {
            if (player != null)
                player.SetTimeFrozen(false);
            Time.timeScale = 1f;
        }

        // Level 3 完整玩法关：进入后先停顿 5 秒（期间不显示炮台、不生成方块），
        // 停顿结束后再开始生成方块（修复从 Level 1 跳转时炮台立刻出现、方块瞬间生成的问题）
        if (level3)
            BeginFullLevelOpening();

        // 断点续玩：位置成功应用后写入存档（关卡 + 分区 + 章节），并 PlayerPrefs.Save()
        //   · /level 跳关优先级最高：这里用「本次跳关参数」无条件覆盖旧存档，跳关后的位置就是新的续玩点；
        //   · 主页点击关卡进入时读的就是这份刚被覆盖的存档，因此绝不可能出现「跳关后又被旧存档拽回旧位置」；
        //   · 无章节拆分的分区（Level 1 - Part 1、Level 3 - Part 1）section 存 0，表示从该分区开头进入；
        //     有章节拆分的分区（Part 2 讲解页 / Part 3 语法课 / API 课）section 为 1 起的页码或任务号。
        GameProgress.SaveResume(level, part, useSection ? section : 0);

        Debug.Log("[LevelDirector] 已跳转 Level " + level + " - Part " + part +
                  (useSection ? "，第 " + section + " 章" : string.Empty));
        string result = "[level] 已跳转到 Level " + level + " - Part " + part + "。";
        if (useSection)
            result = "[level] 已跳转到 Level " + level + " - Part " + part + " 第 " + section +
                     " 个 part 章节（共 " + chapterCount + " 章）。";
        else if (section > 0)
            result += "（该关卡无 part 章节拆分，已从开头进入）";
        return result;
    }

    /// <summary>
    /// 进入完整玩法关（Level 3 等非教学关）的统一开场停顿入口：
    /// 主页按钮进入、控制台 /level 3 1 跳转、自动化测试 StartLevel(3) 等所有路径都必须调用本方法，
    /// 保证「先取消上一次可能仍在进行的计时 → 再重新开始 OpeningSeconds 秒停顿」，
    /// 停顿期间不显示炮台、不生成方块（生成器侧另有 BeginOpeningHold 硬锁，任何提前的 BeginSpawning 都会被推迟）。
    /// </summary>
    public static void BeginFullLevelOpening()
    {
        // 跳转/进场入口先 Cancel 旧 gate 计时，避免残留计时让本次停顿被缩短或直接跳过
        LevelOpeningGate.Cancel();
        LevelOpeningGate.Play(Object.FindObjectOfType<CodeTower>(), FlyingBlockSpawner.Instance,
                              LevelOpeningGate.OpeningSeconds, 0f);
    }
}

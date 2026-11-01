using UnityEngine;

/// <summary>
/// 关卡跳转导演（开发者指令 /level 专用）：
///   - 负责从任意状态跳转到指定「关卡 - 分区」（当前已实现 level 1 - part 1）；
///   - 跳转动作：隐藏主页（若显示）、重置玩家到开局、清空场上长方体并重新开始生成、
///     按分区配置顶部时间提示显隐；
///   - JumpTo 返回面向指令框的结果文本。
/// </summary>
public static class LevelDirector
{
    /// <summary>跳转到指定关卡分区。已实现分区返回成功消息，未开放分区返回提示。</summary>
    public static string JumpTo(int level, int part)
    {
        bool level1 = (level == 1 && part == 1); // 引导教学关
        bool level3 = (level == 3 && part == 1); // 完整玩法关（跳过教学）
        if (!level1 && !level3)
            return "[level] 该关卡部分尚未开放（当前已实现：level 1 - part 1、level 3 - part 1）。";

        // 隐藏主页（若主页仍显示，如开发者指令在主页面板输入时）
        MainMenuController menu = Object.FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.HideMenu();

        // 重置玩家到关卡开局：恢复视角原点朝向、解除全部模态 / 冻结 / 暂停
        PlayerCameraController player = Object.FindObjectOfType<PlayerCameraController>();
        if (player != null)
            player.ResetToLevelStart();

        TutorialController tutorial = Object.FindObjectOfType<TutorialController>();

        // Level 3 完整玩法：若正处教学（如从 Level 1 直接 /level 3 1 跳转），先退出教学，
        // 确保教学面板隐藏、Started=false（E 键时间冻结恢复可用）
        if (level3 && tutorial != null)
            tutorial.ExitToMenu();

        // 清空场上所有长方体；教学关由 Tutorial 接管生成时机（先不生成），Level 3 直接正常生成
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
                spawner.ResetSession();   // Level 3：重开会话（清零计数与编号）
                spawner.BeginSpawning(0f); // Level 3：无开局缓冲，直接开始（进场即 E 冻结写代码，解除冻结后立即出块）
            }
        }

        // Level 1 Part 1 为引导教学关：启动教学流程（重复跳转即重学）
        if (level1 && tutorial != null)
            tutorial.StartTutorial();

        // Level 1 Part 1 不显示顶部时间提示（Level 3 显示，保留实现）；Level 3 重新开始计时
        TimeHintController timeHint = Object.FindObjectOfType<TimeHintController>();
        if (timeHint != null)
        {
            timeHint.SetVisible(level != 1 || part != 1);
            if (level3)
                timeHint.ResetTimer(); // Level 3：顶部时间提示重新从 00:00 开始
        }

        // 炮台实体显隐：Level 1 教学关不显示实体炮台（原点保留炮台功能）；Level 3 完整玩法关显示
        CodeTower tower = Object.FindObjectOfType<CodeTower>();
        if (tower != null)
            tower.SetVisualVisible(level3);

        // Level 3 完整玩法关：进入即自动进入 E 键时间冻结状态，并提示按下 Enter 输入代码
        if (level3)
        {
            if (player != null)
                player.SetTimeFrozen(true);
            CodeEditorController editor = Object.FindObjectOfType<CodeEditorController>();
            if (editor != null)
                editor.ShowEnterPrompt();
        }

        Debug.Log("[LevelDirector] 已跳转 Level " + level + " - Part " + part);
        return "[level] 已跳转到 Level " + level + " - Part " + part + "。";
    }
}

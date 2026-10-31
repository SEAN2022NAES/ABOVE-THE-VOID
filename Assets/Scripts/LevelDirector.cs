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
        if (level != 1 || part != 1)
            return "[level] 该关卡部分尚未开放（当前已实现：level 1 - part 1）。";

        // 隐藏主页（若主页仍显示，如开发者指令在主页面板输入时）
        MainMenuController menu = Object.FindObjectOfType<MainMenuController>();
        if (menu != null)
            menu.HideMenu();

        // 重置玩家到关卡开局：恢复视角原点朝向、解除全部模态 / 冻结 / 暂停
        PlayerCameraController player = Object.FindObjectOfType<PlayerCameraController>();
        if (player != null)
            player.ResetToLevelStart();

        // 清空场上所有长方体；教学关由 Tutorial 接管生成时机（先不生成），非教学则恢复开局缓冲
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        TutorialController tutorial = Object.FindObjectOfType<TutorialController>();
        if (spawner != null)
        {
            spawner.ClearAllBlocks();
            if (tutorial != null)
                spawner.SuspendSpawning();
            else
                spawner.BeginSpawning();
        }

        // Level 1 Part 1 为引导教学关：启动教学流程（重复跳转即重学）
        if (tutorial != null)
            tutorial.StartTutorial();

        // Level 1 Part 1 不显示顶部时间提示（保留实现，后续关卡 / 分区启用）
        TimeHintController timeHint = Object.FindObjectOfType<TimeHintController>();
        if (timeHint != null)
            timeHint.SetVisible(level != 1 || part != 1);

        Debug.Log("[LevelDirector] 已跳转 Level " + level + " - Part " + part);
        return "[level] 已跳转到 Level " + level + " - Part " + part + "。";
    }
}

using UnityEngine;

/// <summary>
/// 关卡表现统一开关（分数 HUD / print 输出荧幕 / 蓝色 BOMB 块生成）：
///   - Level 1 - Part 1 教学关：不显示 Score 分数 UI、不显示炮台 print 输出荧幕、
///     不生成蓝色 BOMB 块（该关只出红色 ATTACK 块）；
///   - Level 1 - Part 2 语法教学关：不显示 Score 分数 UI，但**显示** print 输出荧幕
///     （玩家要能看见 tower.PrintLine 的输出），同样只出红色 ATTACK 块；
///   - Level 2 - Part 1 炮台 API 教学关：不显示 Score 分数 UI，显示 print 输出荧幕，
///     并允许生成蓝色 BOMB 块（教学 TowerObjectInfo.Type 的两种取值）；
///   - 其他关卡（Level 3 完整玩法）：恢复上述全部显示与蓝块生成。
/// 由 TutorialController / CodeSyntaxTutorialController / LevelDirector / MainMenuController
/// 在进出关卡时调用，幂等、可重复调用。
/// </summary>
public static class LevelPresentation
{
    /// <summary>Level 1 - Part 1 教学关表现：隐藏分数与 print 荧幕，只生成红色块。</summary>
    public static void ApplyLevel1()
    {
        Apply(false, false);
    }

    /// <summary>Level 1 - Part 2 语法教学关表现：隐藏分数、显示 print 荧幕（观察 PrintLine 输出），只生成红色块。</summary>
    public static void ApplyLevel2()
    {
        Apply(false, true);
    }

    /// <summary>
    /// Level 2 - Part 1 炮台 API 教学关表现：隐藏分数、显示 print 荧幕（观察 API 输出），
    /// 并**允许蓝色 BOMB 块生成** —— 教学 TowerObjectInfo.Type 的 "ATTACK" / "BOMB" 两种取值需要两种块都出现。
    /// </summary>
    public static void ApplyLevel2Api()
    {
        Apply(false, true, true);
    }

    /// <summary>其他关卡（Level 3 完整玩法）表现：恢复分数、print 荧幕与蓝块生成。</summary>
    public static void ApplyLevel3()
    {
        Apply(true, true);
    }

    /// <summary>
    /// 主页（主菜单）表现：不显示 Score 分数 HUD（含增量提示与负分红字）、不显示 print 输出荧幕、
    /// 不生成蓝色 BOMB 块，并隐藏整套三维坐标轴（半透明黑色遮罩后不再看到蓝色 X 轴）。
    /// 进入关卡时由各自的 Apply 方法恢复。
    /// </summary>
    public static void ApplyMainMenu()
    {
        // 分数 HUD：主页不显示（计分逻辑照常，重新显示时数值正确）
        ScoreManager.SetHudVisible(false);

        // 生命值 HUD：主页不显示，且主页不属于任何关卡 → 生命系统关闭（不掉血、不判负）
        LivesManager.SetEnabled(false);

        // print 输出荧幕：主页不显示
        CodeTower tower = UnityEngine.Object.FindObjectOfType<CodeTower>();
        if (tower != null)
            tower.SetPrintScreenVisible(false);

        // 蓝色 BOMB 块：主页不出块
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null)
            spawner = UnityEngine.Object.FindObjectOfType<FlyingBlockSpawner>();
        if (spawner != null)
            spawner.bombEnabled = false;

        // 三维坐标轴：主页隐藏（否则透过主页半透明遮罩仍能看到蓝色 X 轴），进入关卡时恢复
        CoordinateAxesDrawer.SetAxesVisibleGlobal(false);

        // 顶部倒计时 / 时间提示：主页一律隐藏（否则主页底部仍能看到 00:00 计时），Level 3 进入时再显式打开
        TimeHintController.SetVisibleGlobal(false);
    }

    /// <summary>恢复三维坐标轴显示（进入任意关卡时调用，与 ApplyMainMenu 配对的显式恢复）。</summary>
    public static void RestoreAxes()
    {
        CoordinateAxesDrawer.SetAxesVisibleGlobal(true);
    }

    private static void Apply(bool hudVisible, bool printScreenVisible)
    {
        Apply(hudVisible, printScreenVisible, hudVisible);
    }

    private static void Apply(bool hudVisible, bool printScreenVisible, bool bombEnabled)
    {
        // 进入任意关卡：先恢复三维坐标轴（与主页隐藏配对，保证从主页进入关卡后轴一定可见）
        RestoreAxes();

        // 分数 HUD：教学关隐藏（计分逻辑照常，重新显示时数值正确）
        ScoreManager.SetHudVisible(hudVisible);

        // 生命值：与分数 HUD 同开同关 —— 只有显示分数 HUD 的关卡（Level 3 完整玩法）才启用；
        // Level 1 / Level 2 教学关不掉血、不判负，保持原有「击毁蓝块零分零罚」的精简规则
        LivesManager.SetEnabled(hudVisible);

        // print 输出荧幕：Part 1 不显示；Part 2 / 完整玩法显示
        CodeTower tower = UnityEngine.Object.FindObjectOfType<CodeTower>();
        if (tower != null)
            tower.SetPrintScreenVisible(printScreenVisible);

        // 蓝色 BOMB 块：教学关只出红色 ATTACK 块（仅完整玩法 / API 教学关允许蓝块）
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null)
            spawner = UnityEngine.Object.FindObjectOfType<FlyingBlockSpawner>();
        if (spawner != null)
            spawner.bombEnabled = bombEnabled;
    }
}

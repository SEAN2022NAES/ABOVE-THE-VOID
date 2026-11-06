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

    private static void Apply(bool hudVisible, bool printScreenVisible)
    {
        Apply(hudVisible, printScreenVisible, hudVisible);
    }

    private static void Apply(bool hudVisible, bool printScreenVisible, bool bombEnabled)
    {
        // 分数 HUD：教学关隐藏（计分逻辑照常，重新显示时数值正确）
        ScoreManager.SetHudVisible(hudVisible);

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

/// <summary>游戏语言（中 / 英）。</summary>
public enum GameLanguage
{
    Chinese,
    English
}

/// <summary>
/// 全局语言设置：
///   - 主页「选择语言」切换中文 / English；
///   - 切换后触发 OnLanguageChanged，各 UI 订阅并即时刷新文案；
///   - Pick(zh, en) 按当前语言取中文 / 英文文案。
/// </summary>
public static class Localization
{
    public static GameLanguage Current { get; private set; } = GameLanguage.English; // v0.1.1：英文作为首选项

    /// <summary>语言切换事件（主页 / 教学等 UI 订阅后刷新文本）。</summary>
    public static event System.Action OnLanguageChanged;

    public static bool IsChinese => Current == GameLanguage.Chinese;

    public static void SetLanguage(GameLanguage lang)
    {
        if (Current == lang)
            return;
        Current = lang;
        OnLanguageChanged?.Invoke();
    }

    /// <summary>按当前语言取文案：中文取 zh，英文取 en。</summary>
    public static string Pick(string zh, string en)
    {
        return IsChinese ? zh : en;
    }
}

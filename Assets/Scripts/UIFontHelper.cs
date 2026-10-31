using UnityEngine;

/// <summary>
/// UI 字体工具（静态类）：
///   - LoadMono：优先加载项目内 Consolas（Resources/Fonts/consolas.ttf），
///     缺失时回退 Resources/Fonts/arial.ttf，再回退引擎内置字体；
///   - LoadDefault：arial 优先，再回退内置字体。
/// 供准心无关的时间提示 / 开发者面板 / 指令框等 UI 统一使用。
/// </summary>
public static class UIFontHelper
{
    /// <summary>加载等宽风格字体（Consolas 优先）。</summary>
    public static Font LoadMono()
    {
        Font font = Resources.Load<Font>("Fonts/consolas");
        if (font != null)
            return font;

        font = Resources.Load<Font>("Fonts/arial");
        if (font != null)
            return font;

        return LoadBuiltinFallback();
    }

    /// <summary>加载默认 UI 字体（arial 优先）。</summary>
    public static Font LoadDefault()
    {
        Font font = Resources.Load<Font>("Fonts/arial");
        if (font != null)
            return font;

        return LoadBuiltinFallback();
    }

    private static Font LoadBuiltinFallback()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { }

        try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        catch { }

        return null;
    }
}

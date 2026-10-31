using UnityEngine;

/// <summary>
/// UI 字体工具（静态类）：
///   - LoadForText：按文本内容自动选字体——含中文字符 → 微软雅黑（Microsoft YaHei），否则 → Consolas；
///     所有 UI 文本创建与语言切换刷新时统一走该方法，确保中 / 英文各自使用合适字体。
///   - LoadChinese：优先加载项目内微软雅黑（Resources/Fonts/msyh.ttc），
///     加载失败时用 Font.CreateDynamicFontFromOSFont("Microsoft YaHei", size) 兜底，再回退内置字体；
///   - LoadConsolas / LoadMono：优先加载项目内 Consolas（Resources/Fonts/consolas.ttf），
///     加载失败时用 Font.CreateDynamicFontFromOSFont("Consolas", size) 兜底，再回退 arial / 内置；
///   - LoadDefault：arial 优先，再回退内置字体（兼容旧调用，新代码请使用 LoadForText）。
/// 供准心无关的时间提示 / 开发者面板 / 指令框等 UI 统一使用。
/// </summary>
public static class UIFontHelper
{
    /// <summary>
    /// 按文本内容自动选择字体：文本含中文字符 → 微软雅黑；否则 → Consolas。
    /// 空文本按英文（Consolas）处理。
    /// </summary>
    public static Font LoadForText(string text, int fontSize = 32)
    {
        return ContainsChinese(text) ? LoadChinese(fontSize) : LoadConsolas(fontSize);
    }

    /// <summary>判断文本是否包含中文字符（基本区 \u4E00-\u9FFF）。</summary>
    public static bool ContainsChinese(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
        }
        return false;
    }

    /// <summary>加载中文字体（微软雅黑）：优先项目内 msyh.ttc，失败用 OS 动态字体 Microsoft YaHei，再回退内置字体。</summary>
    public static Font LoadChinese(int fontSize = 32)
    {
        Font font = Resources.Load<Font>("Fonts/msyh");
        if (font != null)
            return font;

        try { return Font.CreateDynamicFontFromOSFont("Microsoft YaHei", fontSize); }
        catch { }

        return LoadBuiltinFallback();
    }

    /// <summary>加载 Consolas（等宽字体）：优先项目内 consolas.ttf，失败用 OS 动态字体 Consolas，再回退 arial / 内置。</summary>
    public static Font LoadConsolas(int fontSize = 32)
    {
        Font font = Resources.Load<Font>("Fonts/consolas");
        if (font != null)
            return font;

        try { return Font.CreateDynamicFontFromOSFont("Consolas", fontSize); }
        catch { }

        return LoadBuiltinFallback();
    }

    /// <summary>加载等宽风格字体（Consolas 优先，等价于 LoadConsolas，保留旧语义）。</summary>
    public static Font LoadMono()
    {
        return LoadConsolas();
    }

    /// <summary>加载默认 UI 字体（arial 优先，兼容旧调用）。</summary>
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

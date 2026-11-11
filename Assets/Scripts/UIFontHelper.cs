using UnityEngine;

/// <summary>
/// UI 字体工具（静态类）：
///   - LoadForText：按文本内容自动选字体——含中文字符 → 思源黑体 / Noto Sans CJK SC（SIL OFL 1.1），否则 → JetBrains Mono（SIL OFL 1.1）；
///     所有 UI 文本创建与语言切换刷新时统一走该方法，确保中 / 英文各自使用合适字体。
///   - 两款字体均为开源可再分发字体，随包分发合规，许可全文见 Resources/Fonts/OFL.txt。
///   - LoadChinese：优先加载项目内 Noto Sans CJK SC（Resources/Fonts/NotoSansCJKsc-Regular.otf），
///     加载失败时用 Font.CreateDynamicFontFromOSFont 依次尝试常见中文字体兜底，再回退内置字体；
///   - LoadConsolas / LoadMono：优先加载项目内 JetBrains Mono（Resources/Fonts/JetBrainsMono-Regular.ttf），
///     加载失败时用 Font.CreateDynamicFontFromOSFont 依次尝试常见等宽字体兜底，再回退内置；
///   - LoadDefault：等宽字体优先（兼容旧调用，新代码请使用 LoadForText）。
/// 供准心无关的时间提示 / 开发者面板 / 指令框等 UI 统一使用。
/// </summary>
public static class UIFontHelper
{
    /// <summary>
    /// 按文本内容自动选择字体：文本含中文字符 → Noto Sans CJK SC；否则 → JetBrains Mono。
    /// 空文本按英文（JetBrains Mono）处理。
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

    /// <summary>加载中文字体（Noto Sans CJK SC）：优先项目内 NotoSansCJKsc-Regular.otf，失败用 OS 动态字体兜底，再回退内置字体。</summary>
    public static Font LoadChinese(int fontSize = 32)
    {
        Font font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
        if (font != null)
            return font;

        try
        {
            return Font.CreateDynamicFontFromOSFont(
                new[] { "Noto Sans CJK SC", "Source Han Sans SC", "Microsoft YaHei", "PingFang SC", "WenQuanYi Micro Hei" },
                fontSize);
        }
        catch { }

        return LoadBuiltinFallback();
    }

    /// <summary>加载等宽字体（JetBrains Mono）：优先项目内 JetBrainsMono-Regular.ttf，失败用 OS 动态字体兜底，再回退内置。</summary>
    public static Font LoadConsolas(int fontSize = 32)
    {
        Font font = Resources.Load<Font>("Fonts/JetBrainsMono-Regular");
        if (font != null)
            return font;

        try
        {
            return Font.CreateDynamicFontFromOSFont(
                new[] { "JetBrains Mono", "Consolas", "Menlo", "DejaVu Sans Mono" },
                fontSize);
        }
        catch { }

        return LoadBuiltinFallback();
    }

    /// <summary>加载等宽风格字体（沿用旧方法名，等价于 LoadConsolas）。</summary>
    public static Font LoadMono()
    {
        return LoadConsolas();
    }

    /// <summary>加载默认 UI 字体（沿用旧方法名；旧项目内 arial 已移除，现回落到等宽字体再回退内置）。</summary>
    public static Font LoadDefault()
    {
        return LoadConsolas();
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

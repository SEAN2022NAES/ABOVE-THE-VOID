using UnityEngine;

/// <summary>
/// UI 字体工具（静态类）：
///   - LoadForText：按文本内容自动选字体——
///       ① 不含中文 → JetBrains Mono（等宽，SIL OFL 1.1）；
///       ② 含中文且夹着英文字母（代码 / 变量名 / 快捷键 / 英文缩写）→ 等宽版思源黑体 Noto Sans Mono CJK SC：
///          中文字形与思源黑体完全一致，而英文、数字、符号是等宽拉丁，代码和变量名因此在中文句子里呈现为等宽字体；
///       ③ 纯中文（无英文字母）→ 比例版思源黑体 Noto Sans CJK SC，排版与原来完全一致。
///     所有 UI 文本创建与语言切换刷新时统一走该方法，确保中文 / 英文各自使用合适字体。
///   - 三款字体均为开源可再分发字体（SIL OFL 1.1），随包分发合规，许可全文见 Resources/Fonts/OFL.txt。
///   - LoadChinese / LoadChineseMono：优先加载项目内 NotoSansCJKsc-Regular.otf / NotoSansMonoCJKsc-Regular.otf，
///     加载失败时用 Font.CreateDynamicFontFromOSFont 依次尝试常见中文字体兜底，再回退内置字体；
///   - LoadConsolas / LoadMono：优先加载项目内 JetBrains Mono（Resources/Fonts/JetBrainsMono-Regular.ttf），
///     加载失败时用 Font.CreateDynamicFontFromOSFont 依次尝试常见等宽字体兜底，再回退内置；
///   - LoadDefault：等宽字体优先（兼容旧调用，新代码请使用 LoadForText）。
/// 供准心无关的时间提示 / 开发者面板 / 指令框等 UI 统一使用。
/// </summary>
public static class UIFontHelper
{
    /// <summary>
    /// 按文本内容自动选择字体：
    ///   不含中文 → JetBrains Mono；含中文且夹英文字母或数字 → 等宽版 CJK；纯中文 → 比例版 CJK。
    /// 空文本按英文（JetBrains Mono）处理。
    /// </summary>
    public static Font LoadForText(string text, int fontSize = 32)
    {
        if (!ContainsChinese(text))
            return LoadConsolas(fontSize);

        // 中文里混排了英文代码 / 变量名 / 数字 / 快捷键：改用等宽版 CJK，让英文与数字以等宽字体现形。
        return ContainsLatinOrDigit(text) ? LoadChineseMono(fontSize) : LoadChinese(fontSize);
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

    /// <summary>判断文本是否包含英文字母（A-Z / a-z）。</summary>
    public static bool ContainsLatinLetter(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                return true;
        }
        return false;
    }

    /// <summary>判断文本是否包含英文字母或阿拉伯数字（0-9）——英文与数字统一按等宽拉丁字形处理。</summary>
    public static bool ContainsLatinOrDigit(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                return true;
        }
        return false;
    }

    /// <summary>加载比例版中文字体（Noto Sans CJK SC）：纯中文文案使用，字形与排版保持原样。</summary>
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

    /// <summary>
    /// 加载等宽版中文字体（Noto Sans Mono CJK SC）：中文夹代码 / 英文变量时使用，
    /// 中文字形与思源黑体一致，英文数字为等宽拉丁。
    /// </summary>
    public static Font LoadChineseMono(int fontSize = 32)
    {
        Font font = Resources.Load<Font>("Fonts/NotoSansMonoCJKsc-Regular");
        if (font != null)
            return font;

        try
        {
            return Font.CreateDynamicFontFromOSFont(
                new[] { "Noto Sans Mono CJK SC", "Sarasa Mono SC", "Source Han Mono SC", "Noto Sans CJK SC", "Microsoft YaHei" },
                fontSize);
        }
        catch { }

        return LoadChinese(fontSize);
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

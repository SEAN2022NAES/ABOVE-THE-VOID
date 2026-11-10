using UnityEngine;

/// <summary>
/// 全局 UI 视觉主题（纯配色层，不改任何布局与渲染逻辑）。
///
/// 取色依据：参考图采样结果 —— 底色近黑深蓝 #04101E，冷青发丝线，#04101E→#0B2233
/// 之间用三层明度区分层次，正文冷灰蓝，强调色为青(#5CD8F2) / 绿(#48DFA0) / 品红(#F05C86)。
/// 全部直角平面填充，无圆角、无阴影、无渐变。
///
/// 使用约定：
///   - 只替换颜色值，禁止在此引入任何 RectTransform / 布局 / 渲染流程改动；
///   - 同一语义的元素必须共用同一 token，避免再次散落硬编码色值；
///   - 需要透明度时用 Hex(rgb, alpha) 或带 alpha 的语义 token，不要手写 new Color(...)。
/// </summary>
public static class UITheme
{
    // ── 基底（由深到浅，层次靠明度而非投影） ─────────────────────────
    /// <summary>场景 / 最深底色 #04101E（参考图主背景）</summary>
    public static readonly Color Void = Hex(0x04101E, 1f);
    /// <summary>一级面板底 #061421</summary>
    public static readonly Color PanelDeep = Hex(0x061421, 0.97f);
    /// <summary>二级面板底 #081A28</summary>
    public static readonly Color Panel = Hex(0x081A28, 0.93f);
    /// <summary>抬升层（标题栏 / 悬浮块）#0B2233</summary>
    public static readonly Color PanelRaised = Hex(0x0B2233, 0.95f);
    /// <summary>下沉块（未解锁按钮等）#0A1622</summary>
    public static readonly Color PanelSunk = Hex(0x0A1622, 0.66f);
    /// <summary>模态遮罩（重）#01080F</summary>
    public static readonly Color Scrim = Hex(0x01080F, 0.76f);
    /// <summary>模态遮罩（轻）#01080F</summary>
    public static readonly Color ScrimSoft = Hex(0x01080F, 0.55f);

    // ── 发丝线（冷青，靠 alpha 控制强弱） ────────────────────────────
    /// <summary>普通分隔线</summary>
    public static readonly Color Hairline = Hex(0x57D2EF, 0.22f);
    /// <summary>强调分隔线 / 描边</summary>
    public static readonly Color HairlineStrong = Hex(0x57D2EF, 0.45f);
    /// <summary>冷灰弱线</summary>
    public static readonly Color HairlineSoft = Hex(0x8FA6B8, 0.16f);
    /// <summary>极弱网格线</summary>
    public static readonly Color HairlineDim = Hex(0x57D2EF, 0.10f);

    // ── 文字三级 ───────────────────────────────────────────────────
    /// <summary>主文字 / 标题 #D3E9F4</summary>
    public static readonly Color TextPrimary = Hex(0xD3E9F4, 1f);
    /// <summary>次级标题 #A8C4D6</summary>
    public static readonly Color TextSub = Hex(0xA8C4D6, 1f);
    /// <summary>正文 #8FA6B8</summary>
    public static readonly Color TextBody = Hex(0x8FA6B8, 1f);
    /// <summary>弱化文字（占位符 / 计数）#5D7488</summary>
    public static readonly Color TextMuted = Hex(0x5D7488, 1f);
    /// <summary>禁用文字 #435667</summary>
    public static readonly Color TextDim = Hex(0x435667, 1f);

    // ── 强调色 ─────────────────────────────────────────────────────
    /// <summary>主强调青 #5CD8F2（标题 / 激活态 / 焦点）</summary>
    public static readonly Color Accent = Hex(0x5CD8F2, 1f);
    /// <summary>青色弱化（描边 / 非激活强调）</summary>
    public static readonly Color AccentSoft = Hex(0x5CD8F2, 0.55f);
    /// <summary>亮底按钮上的深色文字 #04101E（Accent 亮青底专用，保证对比度）</summary>
    public static readonly Color OnAccent = Hex(0x04101E, 1f);
    /// <summary>数值绿 #48DFA0（数值 / 成功 / 已完成）</summary>
    public static readonly Color Value = Hex(0x48DFA0, 1f);
    /// <summary>数值绿高亮（完成态闪烁 / 充能完成）</summary>
    public static readonly Color ValueBright = Hex(0x7BF0BE, 1f);
    /// <summary>绿色弱底（完成态键格底）</summary>
    public static readonly Color ValueDim = Hex(0x48DFA0, 0.30f);
    /// <summary>警示品红 #F05C86（危险 / 扣分 / 失败）</summary>
    public static readonly Color Alert = Hex(0xF05C86, 1f);
    /// <summary>警示品红高亮</summary>
    public static readonly Color AlertBright = Hex(0xFF7A9C, 1f);
    /// <summary>警示弱底（危险按钮底）</summary>
    public static readonly Color AlertDim = Hex(0x3A1A24, 1f);
    /// <summary>次级强调紫 #B46BE6</summary>
    public static readonly Color Purple = Hex(0xB46BE6, 1f);

    // ── 代码编辑器 ─────────────────────────────────────────────────
    /// <summary>编辑器 / 代码区底 #05111C</summary>
    public static readonly Color CodeBg = Hex(0x05111C, 1f);
    /// <summary>行号槽底 #071825</summary>
    public static readonly Color CodeLineBg = Hex(0x071825, 1f);
    /// <summary>行号数字 #4A6678</summary>
    public static readonly Color CodeGutter = Hex(0x4A6678, 1f);
    /// <summary>更深的输入底（状态条 / 错误区）#040D16</summary>
    public static readonly Color CodeInputBg = Hex(0x040D16, 1f);
    /// <summary>底部状态条底 #061421</summary>
    public static readonly Color CodeStatusBg = Hex(0x061421, 1f);
    /// <summary>选中区域（冷青半透明）</summary>
    public static readonly Color CodeSelection = Hex(0x2E7EA0, 0.28f);
    /// <summary>滚动条滑块（冷青细条）</summary>
    public static readonly Color CodeScrollbar = Hex(0x57D2EF, 0.50f);
    /// <summary>语法高亮：关键字</summary>
    public static readonly Color SyntaxKeyword = Hex(0x5CD8F2, 1f);
    /// <summary>语法高亮：字符串</summary>
    public static readonly Color SyntaxString = Hex(0x48DFA0, 1f);
    /// <summary>语法高亮：数字</summary>
    public static readonly Color SyntaxNumber = Hex(0xB46BE6, 1f);
    /// <summary>语法高亮：注释</summary>
    public static readonly Color SyntaxComment = Hex(0x5D7488, 1f);

    // ── 语法高亮 hex 字符串（供 UGUI 富文本 &lt;color=#xxxxxx&gt; 使用） ──────
    /// <summary>关键字 #5CD8F2</summary>
    public const string HexKeyword = "#5CD8F2";
    /// <summary>字符串 #48DFA0</summary>
    public const string HexString = "#48DFA0";
    /// <summary>数字 #B46BE6</summary>
    public const string HexNumber = "#B46BE6";
    /// <summary>注释 #5D7488</summary>
    public const string HexComment = "#5D7488";
    /// <summary>普通代码正文 #8FA6B8</summary>
    public const string HexDefault = "#8FA6B8";

    // ── 语义色（坐标轴 X/Y/Z） ──────────────────────────────────────
    /// <summary>X 轴：青</summary>
    public static readonly Color AxisX = Hex(0x5CD8F2, 1f);
    /// <summary>Y 轴：品红</summary>
    public static readonly Color AxisY = Hex(0xF05C86, 1f);
    /// <summary>Z 轴：绿</summary>
    public static readonly Color AxisZ = Hex(0x48DFA0, 1f);

    /// <summary>0xRRGGBB + alpha → Color</summary>
    public static Color Hex(int rgb, float alpha)
    {
        return new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            alpha);
    }

    /// <summary>按比例提亮（保留原 alpha）：悬停高亮等运行期微调用。</summary>
    public static Color Lighten(Color c, float amount)
    {
        return new Color(
            Mathf.Min(1f, c.r + amount),
            Mathf.Min(1f, c.g + amount),
            Mathf.Min(1f, c.b + amount),
            c.a);
    }

    /// <summary>替换 alpha，保留 RGB。</summary>
    public static Color WithAlpha(Color c, float alpha)
    {
        return new Color(c.r, c.g, c.b, alpha);
    }
}

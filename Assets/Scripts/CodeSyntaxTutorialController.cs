using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 代码编辑器内的顺序式 C# 教学（两门课程，均由 LevelDirector 调用 Begin(course) 进入）：
///   - Course.Grammar（Level 1 - Part 4）：C# 基础语法全套 49 项 —— 注释、变量声明与类型、var 隐式类型推断、const 常量、
///     算术运算符、条件与流程控制（关系 / 逻辑运算符 + if / if-else / switch 合成一章）、
///     for 循环（三要素）、一维数组、List 集合、foreach 遍历、while 循环、do … while 循环、
///     方法定义与参数（局部函数）、方法返回值 return、字符串拼接、string.Format、
///     Vector2 / Vector3 向量（new 构造 + x/y/z 成员访问）、类与对象（new 造对象 + 点号访问成员）、
///     GlobalCode 全局区（本作扩展语法，非 C# 关键字：块内声明被提升为类字段以跨帧存活）、
///     break / continue、三元运算符 ?:、复合赋值与 ++/--、逻辑 && || ! 与短路、字符串常用成员、
///     数组 Length 与嵌套循环、while(true) + break、类型转换（Parse / TryParse / 强转 / ToString 格式符）、
///     try / catch / finally 与 null 判空、Dictionary 字典、enum 枚举、类进阶（构造函数 / 属性与字段 / static）、
///     方法进阶（重载 / 可选参数 / out / ref / 递归）、多维数组（int[,] 与 int[][]）、
///     索引器（this[int] 下标访问）、接口与多态（interface 与实现）、委托 / Action / Func、Lambda（=>）、
///     LINQ 基础（Where / Select / Count / Any / Sum）、泛型与泛型约束（<T> 与 where T : class）、
///     命名空间与全限定名、预处理指令（#region / #if / #endif）、struct 与 class 的区别、
///     元组与解构（(a, b) 与 var (x, y) = ...）、可空类型与 ?. / ??、字符串插值 $"" 与格式符、
///     对象初始化器（new X { ... }）、迭代器 yield return、枚举转换（枚举 ↔ 整数 ↔ 字符串）、
///     扩展方法（this 参数）与等价静态写法；
///   - Course.Api（Level 2 - Part 2）：炮台 API 调用 15 项（同族 API 已合并授课）—— Print、PrintLine、ClearPrint、
///     GetAllObjects、TowerObjectInfo.Index / Type / HP / Position / Velocity / Volume、
///     内置 API 三课（Time.time / Time.deltaTime 限频计时、Mathf.Clamp / Min / Max / Abs 取最近目标与夹取、
///     Vector3.Distance / normalized / magnitude / 向量相减求瞄准方向）、GetFireCooldown、
///     Fire、TowerObjectInfo 剩余字段（MaxHits / Size / InitialVolume / Params）、
///     对象调试输出（o.ToString() / objects.Count / objects[i]）、
///     「遍历 + 条件 + 发射」实战组合，
///     以及收官项 GameStart（本关正式开始：不调用就永远不出方块，重复调用幂等、可放心每帧经过）；
///   - 每一项任务的完成条件都相同：① 编辑器里的代码命中该任务的语法 ② 本次任务期间点过【编译运行 (F6)】
///     并且**编译通过的那份代码本身**满足该语法（防止先写后编译 / 先编译后补写蒙混过关）；
///     任务卡常驻显示「① 语法 / ② 编译」两条勾选状态，未编译时明确提示「点标题栏【编译运行 (F6)】验证」；
///   - 说明分页（专为零基础）：每个任务都有「第 1 页 = 写法 / 示例」「第 2 页 = 手把手细节」「第 3 页 = 代码动画」，
///     任务卡右上角「‹ 细节 1/3 ›」可来回翻；第 2、3 页整段通栏放大指导框（DetailPageRatio），
///     第 2 页内容 = ① 一步一步怎么做（按哪个键、写在哪）② 逐符号读懂它 ③ 三个必踩的坑 + 正确做法；
///     第 3 页内容 = 示例代码每 1.8 秒高亮前进一行 + 左栏用大白话讲「这一刻这行在干什么」，
///     把静态代码演成一段可看的过程（数据从哪来、条件真假、循环跑了几圈、最后打印出什么）；
///   - 判定机制：结构类用正则解析编辑器文本（先剥离注释与字符串字面量，避免注释 / 字符串误判），
///     编译类用 CodeEditorController.OnCompiledSuccessfully 事件，不依赖运行时反射；
///   - 离开 / 重进关卡都会重置进度（Begin 每次从第 1 项开始并重建进度格）。
/// UI 风格与 TutorialController 保持一致（纯代码构建 ScreenSpaceOverlay Canvas + 半透明面板 + Text）。
/// </summary>
public class CodeSyntaxTutorialController : MonoBehaviour
{
    /// <summary>教学课程：C# 基础语法（Level 1-4）/ 炮台 API 调用（Level 2-2）。</summary>
    public enum Course
    {
        Grammar = 0,
        Api = 1
    }

    /// <summary>单任务结构判定：入参为原始代码与「剥离注释 / 字符串后」的代码。</summary>
    private delegate bool TaskMatch(string raw, string code);

    /// <summary>单个教学任务定义（标题 / 正文 / 判定）。</summary>
    private class TaskDef
    {
        public string TitleZh;
        public string TitleEn;
        public string BodyZh;      // 第 1 页：写法 / 示例 + 目标 / 提示 / 判定（原有内容）
        public string BodyEn;
        public string Page2Zh;     // 第 2 页：零基础手把手细节（为 null 表示本任务只有一页）
        public string Page2En;
        public AnimStep[] Anim;    // 第 3 页：代码动画（逐行高亮 + 旁白，为 null 表示本任务没有动画页）
        public MemScene Mem;       // 第 3 页：内存可视化场景（有它就走图形页，优先于 Dia / Anim）
        public DiagramScene Dia;   // 第 3 页：逻辑图（概念型知识点：控制流 / 调用链 / 映射表 / 关系图，优先于 Anim）
        public TaskMatch Match;
    }

    // ================== 第 3 页：内存可视化（manim 风格）的数据结构 ==================

    /// <summary>
    /// 一个变量在内存里占的那一段：栈 / 堆上的连续若干字节。
    /// 类型只是画在标签上的说明，真正决定宽度的是 Bytes 里写了几个字节 ——
    /// char / bool 占 1 格，int / float 占 4 格，数组 / List / string 的变量名本身只占 4 格引用（门牌号），
    /// 它指向的那段数据另用一个变量表示（Type 写 "堆 · int[3]" 之类），这样「引用」和「数据」不会混在一起。
    /// </summary>
    private class MemVar
    {
        public string Name;    // 变量名（画在行首）
        public string Type;    // 类型（int / char / float / bool / string / int[] / List<int> …）
        public string Bytes;   // 每个字节的十六进制值，空格分隔；"??" 表示这一格还没被写
        public string Note;    // 这一行下面的一行小字（讲清这段内存为什么长这样），可为空
        public string Badge;   // 行首小徽章（"锁" / "引用" / "栈帧" / "丢弃"），可为空
        public bool Ghost;     // true = 灰虚线：代码里写了、但内存里根本没有它（注释 / 方法定义）
        // ---- 英文模式专用文案（Localization.IsChinese = false 时优先用这几项，留空则回落到中文） ----
        public string NameEn;  // 变量名的英文写法（如 "hp（参数）" → "hp (param)"）
        public string TypeEn;  // 类型名牌的英文写法（如 "堆 · int[3]" → "heap · int[3]"）
        public string NoteEn;  // 行说明的英文版
        public string BadgeEn; // 行首徽章的英文版（如 "锁" → "CONST"）

        /// <summary>给这段内存补一份英文文案（不传的那项继续用中文，避免漏写就空掉）。</summary>
        internal MemVar En(string note, string name = null, string type = null, string badge = null)
        {
            NoteEn = note;
            if (!string.IsNullOrEmpty(name))
                NameEn = name;
            if (!string.IsNullOrEmpty(type))
                TypeEn = type;
            if (!string.IsNullOrEmpty(badge))
                BadgeEn = badge;
            return this;
        }
    }

    /// <summary>内存可视化的一步：内存里已经有前 Reveal 个变量、正在写第 Write 个，配一句旁白。</summary>
    private class MemStep
    {
        public string Expr;    // 这一步在执行的代码（画在舞台下方的执行台上）
        public string ExprEn;  // 同一行代码的英文版（英文模式用；代码里的注释跟着语言走，留空则回落到 Expr）
        public string Zh;      // 中文旁白
        public string En;      // 英文旁白
        public int Reveal;     // 到这一步为止，内存里有几个变量（1-based 个数，0 = 什么都没分配）
        public int Write;      // 这一步正在往里写字节的变量下标（-1 = 只在读 / 只作判断）
        public int[] PatchVar;      // 这一步要改写哪几段内存的值（演示「同一格的值被改了」，如 for 里的 i）
        public string[] PatchBytes; // 改写后的字节串，与 PatchVar 一一对应

        /// <summary>补一份英文代码行（只换注释里的语言，代码本身不动）。链式写法：MS(...).Code("// remaining hp")</summary>
        internal MemStep Code(string exprEn)
        {
            ExprEn = exprEn;
            return this;
        }
    }

    /// <summary>
    /// 舞台上的一行 = 一个变量（或一段数据）：行首徽章 + 类型名牌 + 变量名 + 若干字节格 + 行下一行小字。
    /// 所有子件在 BuildMemStage 里一次建好并塞进池子，播放时只改颜色 / 文字 / 位置，绝不动态建 UI。
    /// </summary>
    private class MemRowUI
    {
        public RectTransform Root;                 // 整块（行内容 + 下方小字）
        public Image FrameBg;                      // 整块底色（栈帧 / 幽灵行用）
        public Text Badge;                         // 行首徽章（锁 / 引用 / 栈帧）
        public Image TypeBg;                       // 类型名牌底色
        public Text TypeText;                      // 类型名牌文字（int / char …）
        public Text NameText;                      // 变量名
        public Text NoteText;                      // 行下小字
        public RectTransform[] CellRt;             // 本行字节格
        public Image[] CellBg;
        public Text[] CellAddr;
        public Text[] CellVal;
        public Vector2[] CellPos;                  // 格子目标位置（补间用）
    }

    /// <summary>一段内存戏：变量表 + 分步脚本。</summary>
    private class MemScene
    {
        public MemVar[] Vars;
        public MemStep[] Steps;
    }

    /// <summary>
    /// 代码动画的一个步骤：把示例代码里的某一行高亮，并配一句「这一行在干什么」。
    /// 行不用写死下标，只给该行里的一段特征文字（Key），由 FindAnimLine 去示例代码里定位 ——
    /// 以后改示例代码不会因为行号错位而高亮到别的行。
    /// </summary>
    private class AnimStep
    {
        public string Key;   // 该步骤要演示的代码行特征子串
        public string KeyEn; // 英文示例代码里的特征子串（Key 本身是中文注释时用，如 "// 计算剩余血量" → "// remaining hp"）
        public string Zh;    // 中文旁白
        public string En;    // 英文旁白

        /// <summary>补一份英文定位子串：Key 里带中文注释时，英文模式要在英文示例代码里找得到这一行。</summary>
        internal AnimStep KeyIn(string keyEn)
        {
            KeyEn = keyEn;
            return this;
        }
    }

    // ================== 第 3 页：逻辑图（概念型知识点）的数据结构 ==================
    // 内存页讲的是「一格一字节」，可控制流、调用栈、映射表、继承关系这些东西根本没有字节可数 ——
    // 所以另起一套「方框 + 箭头」的画法：方框讲概念，箭头讲数据或控制的流向，每一步点亮其中几个。
    // 它与内存页共用同一套第 3 页入口（PageCountOf / 翻页 / ↓ 步进），玩家看到的仍是「Detail 3」。

    /// <summary>方框的语义色调：同一个颜色在整门课里始终表示同一类东西，玩家看第二张图就不用再解释。</summary>
    private enum DiaTone
    {
        Panel = 0,    // 普通概念（数据 / 变量 / 步骤）
        Accent = 1,   // 正在发生的事（当前执行的那一步）
        Value = 2,    // 结果 / 取到的值（绿色）
        Alert = 3,    // 出错 / 危险 / 会被跳过的分支（红色）
        Purple = 4,   // 类型 / 分类 / 成员（紫色）
    }

    /// <summary>逻辑图里的一个方框：一句话概念 + 归一化位置（0~1，左上为原点，窄卡自动按比例缩）。</summary>
    private class DiaNode
    {
        public string Text;      // 方框正文（中文）
        public string TextEn;    // 方框正文（英文，留空回落中文）
        public float X;          // 方框中心横向位置 0~1
        public float Y;          // 方框中心纵向位置 0~1（0 = 顶部）
        public float W;          // 方框宽度 0~1（0 = 按内容给默认宽度）
        public float H;          // 方框高度 0~1（0 = 按有没有附注自动给）
        public DiaTone Tone;     // 语义色调
        public string Desc;      // 方框下方的一行小注（讲「它为什么在这里」），可为空
        public string DescEn;

        /// <summary>补英文正文。链式：DN("if (hp &gt; 0)", ...).En("if (hp &gt; 0)")</summary>
        internal DiaNode En(string text)
        {
            TextEn = text;
            return this;
        }

        /// <summary>补英文小注。链式：.Note("条件成立才进", "enters only if true")</summary>
        internal DiaNode Note(string zh, string en)
        {
            Desc = zh;
            DescEn = en;
            return this;
        }

        /// <summary>改色调。链式：.Tint(DiaTone.Value)</summary>
        internal DiaNode Tint(DiaTone tone)
        {
            Tone = tone;
            return this;
        }

        /// <summary>指定宽高（归一化，0~1）。链式：.Size(0.26f)</summary>
        internal DiaNode Size(float w, float h = 0f)
        {
            W = w;
            H = h;
            return this;
        }
    }

    /// <summary>两个方框之间的一条连线：谁指向谁 + 线上写什么（比如 "true" / "0x2A" / "继承"）。</summary>
    private class DiaEdge
    {
        public int From;         // 起点方框下标
        public int To;           // 终点方框下标
        public string Label;     // 线中间的标签（中文），可为空
        public string LabelEn;
        public bool Dashed;      // true = 虚线：不走的路径 / 弱关系
        public bool Arrow = true;// false = 纯连线（双向对照、分组框之间的关系）
        public DiaTone Tone;

        internal DiaEdge Lb(string zh, string en)
        {
            Label = zh;
            LabelEn = en;
            return this;
        }

        /// <summary>改成虚线（不发生的路径）。链式：.Dash()</summary>
        internal DiaEdge Dash()
        {
            Dashed = true;
            return this;
        }

        /// <summary>去掉箭头（只表示「两者对照」，没有先后）。链式：.NoArrow()</summary>
        internal DiaEdge NoArrow()
        {
            Arrow = false;
            return this;
        }

        internal DiaEdge Tint(DiaTone tone)
        {
            Tone = tone;
            return this;
        }
    }

    /// <summary>逻辑图的一步：点亮哪几个方框、哪几条连线，配一句「为什么会这样」的旁白。</summary>
    private class DiaStep
    {
        public int[] Hot;        // 这一步点亮的方框下标
        public int[] HotEdge;    // 这一步点亮的连线下标（可为 null = 不特别强调某条线）
        public string Zh;        // 中文旁白：讲清「为什么」
        public string En;
    }

    /// <summary>一张逻辑图：方框表 + 连线表 + 分步脚本 + 顶部「怎么读这张图」。</summary>
    private class DiagramScene
    {
        public DiaNode[] Nodes;
        public DiaEdge[] Edges;
        public DiaStep[] Steps;
        public string Hint;      // 顶部说明（中文）
        public string HintEn;
    }

    /// <summary>舞台上的一个方框：底 + 正文 + 小注。全部一次建好，播放时只改颜色 / 文字 / 位置。</summary>
    private class DiaNodeUI
    {
        public RectTransform Root;
        public Image Bg;
        public Text Text;
        public Text Desc;
        public Vector2 Home;     // 目标位置（补间起点用）
        public Vector2 Size;
    }

    /// <summary>舞台上的一条连线：线身 + 箭头 + 线上的标签。</summary>
    private class DiaEdgeUI
    {
        public RectTransform Line;
        public Image LineBg;
        public RectTransform Head;
        public Image HeadBg;
        public Text Label;
        public RectTransform LabelRt;
        public Vector2 Home;     // 线身中点
        public Vector2 Size;     // (长度, 粗细)
        public Vector2 HeadSize;
        public float Angle;      // 线身旋转角
    }

    // ---- 逻辑图的配置小工具：让 15 个任务的图能写成一行一行的声明，而不是一堆 new ----

    private static DiaNode DN(string text, float x, float y, DiaTone tone = DiaTone.Panel)
    {
        return new DiaNode { Text = text, X = x, Y = y, Tone = tone };
    }

    private static DiaEdge DE(int from, int to, string label = null)
    {
        return new DiaEdge { From = from, To = to, Label = label };
    }

    private static int[] H(params int[] items)
    {
        return items;
    }

    private static DiaStep DS(int[] hot, int[] hotEdge, string zh, string en)
    {
        return new DiaStep { Hot = hot, HotEdge = hotEdge, Zh = zh, En = en };
    }

    private static DiaStep DS(int hot, string zh, string en)
    {
        return new DiaStep { Hot = new[] { hot }, HotEdge = null, Zh = zh, En = en };
    }

    private static DiagramScene DSC(DiaNode[] nodes, DiaEdge[] edges, DiaStep[] steps,
        string hint = null, string hintEn = null)
    {
        return new DiagramScene { Nodes = nodes, Edges = edges, Steps = steps, Hint = hint, HintEn = hintEn };
    }

    public static CodeSyntaxTutorialController Instance { get; private set; }

    /// <summary>教学序列是否处于激活状态（进入对应关卡为 true，离开关卡后为 false）。</summary>
    public bool Active { get; private set; }

    /// <summary>当前任务序号（0-based）；等于任务总数表示全部完成。</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>当前课程。</summary>
    public Course CurrentCourse { get; private set; }

    /// <summary>当前课程的任务总数。</summary>
    public int TaskCount
    {
        get { return tasks != null ? tasks.Length : 0; }
    }

    /// <summary>外部只读查询：教学是否在进行（供 CodeEditorController / 其他系统判断）。</summary>
    public static bool IsActive
    {
        get { return Instance != null && Instance.Active; }
    }

    // ---------------- UI 引用 ----------------
    private const int MaxChips = 56;   // 进度格最多预建数量（两门课程任务数均 ≤ 56）
    // 上下分栏布局：代码编辑器占上方整块，指导框（任务卡）横跨屏幕底部整条，
    // 其高度即编辑器底部预留高度（CodeEditorController.SetBottomReservedHeight），两者上下一一对齐、互不重叠。
    private const float ReferenceWidth = 1920f;          // CanvasScaler 参考分辨率宽（面板铺满宽度回退值）
    private const float ReferenceHeight = 1080f;         // Canvas 参考分辨率高（取不到父级高度时的回退值）
    private const float SplitRatio = 0.5f;               // 上下对半平分：上方代码区 50%、下方指导框 50%，固定不随窗口 / 文案变化
    private const float DetailPageRatio = 0.66f;
    private const float AnimStepInterval = 1.8f;      // 第 3 页代码动画：旧版自动推进的节拍（现已改手动，仅保留作补间基准）
    private const float MemStepInterval = 4.2f;       // 第 3 页内存可视化：一步的补间时长上限（不再自动跳步，靠 ↓ 手动推进）
    private const float MemFadeTime = 0.45f;          // 新变量「滑进来」的时长（这一步前 0.45 秒内做淡入 + 位移）
    private const float MemCellGapX = 8f;             // 相邻字节格的水平间距
    private const float MemRowGapY = 14f;             // 相邻变量块之间的垂直间距
    private const int MemRowPool = 4;                 // 预建的行数（一个场景最多同时摊开 4 段内存）
    private const int MemCellPerRow = 12;             // 每行最多几格（数组 int[3] = 12 字节，是最长的一段）
    private const float MemStageTopInset = 108f;      // 舞台顶边内缩：让开任务名那一行
    private const float MemStageBottomInset = 26f;    // 舞台底边内缩：一条呼吸位
    private const float MemStageSideInset = 26f;      // 舞台左右内缩
    private const float MemHeaderOnceH = 64f;         // 「怎么读这张图」只在一个任务里展示：高度给足两行，行与行不再压叠
    private const int MemHeaderTaskIndex = 1;         // 说明条挂在哪个任务（0 基）：1 = Task 2「变量与类型」——第一次真正出现 int/char/bool/float 的地方
    private const float MemFootH = 34f;               // 说明条收起后，右上角步数行保留的高度（行区不会顶到它）
    private const float MemExprH = 34f;               // 执行台（当前代码行）高度
    private const float MemNoteH = 56f;               // 底部旁白高度
    private const float MemNoteGapY = 6f;             // 旁白与执行台之间的间距
    // ---- 第 3 页：逻辑图（概念型知识点的框图）----
    private const int DiaNodePool = 14;               // 预建的方框数（一张逻辑图最多 14 个概念）
    private const int DiaEdgePool = 20;               // 预建的连线数（最多 20 条箭头）
    private const float DiaHeaderH = 46f;             // 顶部「怎么读这张图」一行的高度
    private const float DiaNoteH = 108f;              // 底部步骤旁白高度（字号已放大，留四到五行余量）
    private const float DiaEdgeThick = 2.6f;          // 连线粗细
    private const float DiaNodeMinW = 96f;            // 方框最窄宽度（窄卡也不会缩成一条）
    private const float MinHintHeight = 200f;         // 提示区最小高度（拖到底也不会消失）
    private const float MinCodeHeight = 180f;         // 代码区最小高度（拖动分割线时的下限保护）
    private const float DragHandleHeight = 12f;       // 蓝色分割线的拖动热区高度（跨在线上，好抓）         // 第 2 页（手把手细节）指导框占比：文字量大，临时把指导框放大到 68%，翻回第 1 页自动复原
    private const float GuideBodyTopInset = 132f;        // 正文上边距（让开强调线 / 标题 / 进度格 / 任务名）
    private const float GuideBodyBottomInset = 46f;      // 正文下边距（让开「任务完成 ✔」提示）
    private const int GuideBodyFontSize = 24;            // 左栏（目标 / 提示 / 判定）固定字号：不再自适应，靠拖动蓝色长条看全
    private const int SampleFontSize = 20;               // 右栏（写法 / 示例代码）固定字号：同上，恒定不随高度变化
    private const float GuideBodyLineSpacing = 1.05f;    // 正文行距
    private const float ColumnSplitRatio = 0.5f;         // 左（引导）/ 右（示例代码）分栏界线位置（0.5 = 对半）
    private const float ColumnGap = 28f;                 // 分栏内边距：文字与竖分隔线之间的间距
    private const float DividerWidth = 2f;               // 分栏竖分隔线宽度
    private const float HintBarMaxWidth = 1400f;         // 提示条最大宽度
    private const float HintBarMinWidth = 420f;          // 提示条最小宽度
    private const float HintBarMinHeight = 64f;          // 提示条最小高度
    private const float EntryPromptY = 150f;             // 进入提示在实机页面上的纵向位置（相对屏幕中心向上偏移）
    private const float PageNavWidth = 240f;             // 任务卡右上角翻页条宽度（‹ 页码 ›）
    private GameObject panelRoot;      // 编辑器打开时显示的任务卡（底部指导框）
    private float appliedSplitHeight = -1f; // 最近一次应用的对半分栏高度（用于窗口尺寸变化时重算）
    private RectTransform hintRect;    // 提示条矩形（按文案自适应宽高）
    private Text headlineText;         // 「关卡 1 · 第 4 部分 · C# 基础语法教学」
    private Text progressText;         // 「任务 2/16」
    private Text taskNameText;         // 当前任务名
    private Text bodyText;             // 左栏：目标 / 提示 / 判定 + 完成条件状态
    private Text sampleText;           // 右栏：写法 / 示例代码（无写法段时隐藏，左栏自动通栏）

    // ---- 第 3 页：内存可视化舞台（整块正文区改成图形演示，manim 风格） ----
    private RectTransform memRoot;      // 舞台根节点（覆盖整个正文区）
    private Text memHeaderText;         // 顶部说明：一格 = 1 字节 / 右高左低（小端），只在 Task 2 出现
    private Text memExprText;           // 执行台：当前这一行代码
    private Text memNoteText;           // 旁白：这一步在干什么
    private Text memFootText;           // 顶部右侧：第几步 / 循环提示
    private RectTransform memExprRt;    // 执行台那条深色带（底部整块要跟着内容走，得能改它的位置）
    private Text memKeyHintText;        // 执行台右侧：按下 ↓ 观看下一个（代码区握焦点时改成 ↓ 代码模式）
    private string memKeyHintCache = "";// 键位提示文案缓存，避免逐帧改文字
    private MemRowUI[] memRows;         // 行池（每个变量/每段数据一块）
    private int memBuiltStep = -1;      // 上一次重建舞台时的步骤号（变了才重排文字）
    private int memBuiltIndex = -1;     // 上一次重建舞台时的任务号
    private float memBuiltH = -1f;      // 上一次重建舞台时的可用高度（拖动分割线后要重排）

    // ---- 第 3 页：逻辑图舞台（概念型知识点：方框 + 箭头，与内存页同一套入口） ----
    private RectTransform diaRoot;      // 舞台根节点（覆盖整个正文区）
    private Text diaHeaderText;         // 顶部说明：这张图怎么读
    private Text diaExprText;           // 底条左侧：第几步 / 本步在讲什么
    private Text diaNoteText;           // 底部旁白：这一步为什么会这样
    private RectTransform diaNoteRt;    // 旁白块（跟着内容高度走）
    private RectTransform diaBarRt;     // 底条（步数 + 键位提示）
    private Text diaKeyHintText;        // 底条右侧：↑↓ 看步骤 / ↓ 代码模式
    private string diaKeyHintCache = ""; // 键位提示文案缓存，避免逐帧改文字
    private DiaNodeUI[] diaNodes;       // 方框池
    private DiaEdgeUI[] diaEdges;       // 连线池
    private int diaBuiltStep = -1;      // 上一次重建舞台时的步骤号
    private int diaBuiltIndex = -1;     // 上一次重建舞台时的任务号
    private float diaBuiltH = -1f;      // 上一次重建舞台时的可用高度
    private Image columnDivider;       // 左 / 右分栏之间的竖分隔线
    private bool columnHasSample;      // 当前任务是否有右侧示例代码（决定是否分栏）
    private Text flashText;            // 「任务完成 ✔」短暂提示
    private GameObject pageNavRoot;    // 任务卡内翻页条（‹ 说明 1/2 ›），只在该任务有多页时显示
    private Button prevPageBtn;        // 上一页（翻回去）
    private Button nextPageBtn;        // 下一页（翻回来 / 翻到第 2 页）
    private Text pageNoText;           // 「说明 1/2」页码
    private Text[] chipTexts;          // 进度格文字（索引 = 任务序号）
    private Image[] chipBgs;           // 进度格底色
    private Color[] chipBaseColors;    // 进度格基准色（悬停高亮后据此还原）
    private GameObject hintRoot;       // 编辑器关闭时实机页面正中的进入提示（提示「按下 Enter 打开代码编辑界面」）
    private Text hintText;

    // ---------------- 运行时状态 ----------------
    private TaskDef[] tasks;           // 当前课程任务表
    private bool[] done;               // 各任务完成标记
    private CodeEditorController editor;
    private FlyingBlockSpawner spawner;
    private CodeTower tower;
    private bool compiledThisTask;     // 本次任务期间是否发生过「编译运行成功」
    private string compiledRaw;        // 最近一次编译成功的用户代码原文
    private string compiledSnapshot;   // 最近一次编译成功的代码（已剥离注释 / 字符串）
    private bool finished;
    private float homeReturnTimer;     // 「全部任务真的做完」后自动回主页的倒计时（0 = 不计时）
    private const float HomeReturnDelay = 3.5f; // 完成提示停留多久再回主页（真实秒）
    private float checkTimer;          // 结构判定节流（真实时间）
    private float flashTimer;          // 「任务完成」提示倒计时（真实时间）
    private string hintCache = "";     // 提示条文案缓存，避免逐帧改文字
    private int statusCache = -1;      // 任务卡状态缓存（1=语法命中 2=编译通过），避免逐帧重建正文
    private int guidePage;             // 当前任务正在看的说明页（0 = 第 1 页 写法/示例，1 = 第 2 页 手把手细节，2 = 第 3 页 代码动画）
    private float animTimer;           // 代码动画计时（真实时间，编辑器打开时 timeScale = 0）
    private int animStep;              // 代码动画当前步骤（0-based，超过步数自动回 0 循环）
    private float manualSplitOffset;  // 手动拖动蓝色分割线的偏移（UI 像素，>0 = 提示区更高 / 代码区更矮）
    private Image accentLine;         // 面板顶部那条蓝色分割线（拖动把手悬停时提亮）
    private SplitDragHandle splitDragHandle;

    // ---------------- 结构判定正则（语法课） ----------------
    // 变量声明与初始化：类型 + 变量名 = 值;
    private static readonly Regex ReVariable = new Regex(
        @"\b(int|float|double|string|bool|char|long)\s+[A-Za-z_]\w*\s*=\s*[^;=][^;]*;",
        RegexOptions.Compiled);
    // var 隐式类型声明：var 名字 = 值;（ReVariable 也认 var，这一条专门判「真的用了 var」）
    private static readonly Regex ReImplicitVar = new Regex(
        @"\bvar\s+[A-Za-z_]\w*\s*=\s*[^;=][^;]*;",
        RegexOptions.Compiled);
    // const 常量：const 类型 名字 = 值;
    private static readonly Regex ReConst = new Regex(
        @"\bconst\s+(int|float|double|string|bool|char|long)\s+[A-Za-z_]\w*\s*=\s*[^;]+;",
        RegexOptions.Compiled);
    // 算术表达式：操作数 运算符 操作数（+ - * / %）
    private static readonly Regex ReArithmetic = new Regex(
        @"([A-Za-z_]\w*|\d+(?:\.\d+)?)\s*[+\-*/%]\s*([A-Za-z_]\w*|\d+(?:\.\d+)?)",
        RegexOptions.Compiled);
    // 关系运算：操作数 比较运算符 操作数（== != >= <= > <）
    private static readonly Regex ReRelational = new Regex(
        @"([A-Za-z_]\w*|\d+(?:\.\d+)?)\s*(==|!=|>=|<=|>|<)\s*([A-Za-z_]\w*|\d+(?:\.\d+)?)",
        RegexOptions.Compiled);
    // 逻辑运算：&& / || / 单目 !
    private static readonly Regex ReLogical = new Regex(
        @"&&|\|\||(?<![=!<>])!(?!=)",
        RegexOptions.Compiled);
    // if 条件判断：if ( … 条件 … )
    private static readonly Regex ReIf = new Regex(
        @"\bif\s*\([^;{}]*(==|!=|>=|<=|>|<|&&|\|\||!)",
        RegexOptions.Compiled);
    // if … else …
    private static readonly Regex ReIfElse = new Regex(
        @"\bif\s*\([^;{}]*\)[\s\S]*?\belse\b",
        RegexOptions.Compiled);
    // switch：switch ( … ) { case … }
    private static readonly Regex ReSwitch = new Regex(
        @"\bswitch\s*\([^;{}]*\)\s*\{[\s\S]*\bcase\b",
        RegexOptions.Compiled);
    // for 循环：for ( 初始化; 条件; 迭代 )
    private static readonly Regex ReFor = new Regex(
        @"\bfor\s*\([^;{}]*;[^;{}]*;",
        RegexOptions.Compiled);
    // while 循环：while ( … ) {（do-while 的 while 后不带 { ，故不会误判）
    private static readonly Regex ReWhile = new Regex(
        @"\bwhile\s*\([^;{}]*\)\s*\{",
        RegexOptions.Compiled);
    // do { … } while ( … );
    private static readonly Regex ReDoWhile = new Regex(
        @"\bdo\s*\{[\s\S]*?\}\s*while\s*\(",
        RegexOptions.Compiled);
    // 一维数组：new 类型[ … ] 或 数组名[下标]
    private static readonly Regex ReArray = new Regex(
        @"(\bnew\s+(int|float|double|string|bool|char)\s*\[)|([A-Za-z_]\w*\s*\[\s*\d+\s*\])",
        RegexOptions.Compiled);
    // List 集合：new List< … >
    private static readonly Regex ReListNew = new Regex(
        @"\bnew\s+List\s*<",
        RegexOptions.Compiled);
    // List 添加元素：xxx.Add(
    private static readonly Regex ReListAdd = new Regex(
        @"\.\s*Add\s*\(",
        RegexOptions.Compiled);
    // 方法（局部函数）定义：返回类型 名字(参数) {
    private static readonly Regex ReMethodDef = new Regex(
        @"\b(void|int|float|double|bool|string|long|char)\s+[A-Za-z_]\w*\s*\([^)]*\)\s*\{",
        RegexOptions.Compiled);
    // return 语句
    private static readonly Regex ReReturn = new Regex(@"\breturn\b", RegexOptions.Compiled);
    // 方法调用并接收返回值：变量 = 方法名(
    private static readonly Regex ReMethodCallAssign = new Regex(
        @"[A-Za-z_]\w*\s*=\s*[A-Za-z_]\w*\s*\(",
        RegexOptions.Compiled);
    // 字符串拼接：字符串字面量（剥离后变成 ""）+ 变量，或变量 + 字符串字面量
    private static readonly Regex ReConcat = new Regex(
        @"""\s*\+|\+\s*""",
        RegexOptions.Compiled);
    // string.Format(
    private static readonly Regex ReStringFormat = new Regex(
        @"\bstring\s*\.\s*Format\s*\(",
        RegexOptions.Compiled);
    // 注释（作用于原始代码）：// 或 /* */
    private static readonly Regex ReComment = new Regex(
        @"//|/\*",
        RegexOptions.Compiled);

    // ---------------- 结构判定正则（API 课） ----------------
    // 方法接收者（不再写死 tower.）：
    //   · 任意合法变量名（含 var 别名 var t = tower;、自己起的名字 myTower）
    //   · 也允许 a.b 这样的成员链
    //   · 也允许 GetComponent<CodeTower>() 直接取到炮塔后调用
    // 之所以放宽：tower 只是 Run(CodeTower tower) 的形参名，玩家完全可以让别的变量指向同一台炮塔，
    // 那些写法在 C# 里合法、在游戏里等价，判定不该因此判不过。
    private const string ApiRecv =
        @"(?:[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*|GetComponent\s*<\s*CodeTower\s*>\s*\(\s*\))";

    // foreach 遍历
    private static readonly Regex ReForeach = new Regex(
        @"\bforeach\s*\([^;{}]*\bin\b",
        RegexOptions.Compiled);
    // GetAllObjects()
    private static readonly Regex ReGetAllObjects = new Regex(
        ApiRecv + @"\s*\.\s*GetAllObjects\s*\(",
        RegexOptions.Compiled);
    // TowerObjectInfo 各字段读取
    private static readonly Regex ReFieldIndex = new Regex(@"\.\s*Index\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldType = new Regex(@"\.\s*Type\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldHp = new Regex(@"\.\s*HP\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldPosition = new Regex(@"\.\s*Position\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldVelocity = new Regex(@"\.\s*Velocity\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldVolume = new Regex(@"\.\s*Volume\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldInitialVolume = new Regex(@"\.\s*InitialVolume\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldMaxHits = new Regex(@"\.\s*MaxHits\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldSize = new Regex(@"\.\s*Size\b", RegexOptions.Compiled);
    private static readonly Regex ReFieldParams = new Regex(@"\.\s*Params\b", RegexOptions.Compiled);
    // TowerObjectInfo 整体调试输出：ToString() / 列表 Count / 下标取单个 objects[i]
    private static readonly Regex ReObjToString = new Regex(
        @"\.\s*ToString\s*\(\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex ReListCount = new Regex(
        @"\.\s*Count\b",
        RegexOptions.Compiled);
    // 下标取单个对象：要求 objects[i] 后面紧跟 .成员（如 objects[0].ToString()），
    // 这样普通的数组下标（arr[0] 单独出现）不会被误判成「按对象下标取值」。
    private static readonly Regex ReIndexAccess = new Regex(
        @"[A-Za-z_]\w*\s*\[\s*[^\[\]]+\s*\]\s*\.\s*[A-Za-z_]",
        RegexOptions.Compiled);
    // GetFireCooldown() / Fire( / PrintLine( / Print( / ClearPrint(（接收者见 ApiRecv，不写死 tower）
    private static readonly Regex ReGetFireCooldown = new Regex(
        ApiRecv + @"\s*\.\s*GetFireCooldown\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ReFire = new Regex(
        ApiRecv + @"\s*\.\s*Fire\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex RePrintLine = new Regex(
        ApiRecv + @"\s*\.\s*PrintLine\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex RePrintOnly = new Regex(
        ApiRecv + @"\s*\.\s*Print\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ReClearPrint = new Regex(
        ApiRecv + @"\s*\.\s*ClearPrint\s*\(",
        RegexOptions.Compiled);
    // GameStart()（本关正式开始；接收者同样不写死 tower）
    private static readonly Regex ReGameStart = new Regex(
        ApiRecv + @"\s*\.\s*GameStart\s*\(\s*\)",
        RegexOptions.Compiled);

    // ---------------- 结构判定正则（API 课：内置 API 三课） ----------------
    // Time.time / Time.deltaTime / Time.unscaledDeltaTime
    private static readonly Regex ReTimeApi = new Regex(
        @"\bTime\s*\.\s*(?:time|deltaTime|unscaledDeltaTime)\b",
        RegexOptions.Compiled);
    // Mathf.Clamp / Clamp01 / Min / Max / Abs
    private static readonly Regex ReMathfApi = new Regex(
        @"\bMathf\s*\.\s*(?:Clamp|Clamp01|Min|Max|Abs)\b",
        RegexOptions.Compiled);
    // Vector3.Distance( / a.Position - b.Position 这种向量相减
    private static readonly Regex ReVectorDistance = new Regex(
        @"\bVector3\s*\.\s*Distance\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ReVectorSubtract = new Regex(
        @"\.\s*Position\s*-\s*[A-Za-z_(]",
        RegexOptions.Compiled);
    // .normalized / .magnitude
    private static readonly Regex ReVectorNormalized = new Regex(
        @"\.\s*normalized\b",
        RegexOptions.Compiled);
    private static readonly Regex ReVectorMagnitude = new Regex(
        @"\.\s*magnitude\b",
        RegexOptions.Compiled);

    // ---------------- 结构判定正则（语法课：Vector 与对象） ----------------
    // new Vector2(...) / new Vector3(...)
    private static readonly Regex ReVectorNew = new Regex(
        @"\bnew\s+Vector[23]\s*\(",
        RegexOptions.Compiled);
    // new 任意类型（类 / 结构体）
    private static readonly Regex ReNewObj = new Regex(
        @"\bnew\s+[A-Za-z_]\w*\s*[\(\[]",
        RegexOptions.Compiled);
    // 点号成员访问（.x / .Position / .Fire 之类）
    private static readonly Regex ReMemberAccess = new Regex(
        @"\.\s*[A-Za-z_]\w*",
        RegexOptions.Compiled);
    // GlobalCode 全局区开头（本作扩展语法，不是 C# 关键字：块内声明会被提升到类体级别成为字段）
    private static readonly Regex ReGlobalBlock = new Regex(
        @"\bGlobalCode\s*\{",
        RegexOptions.Compiled);
    // GlobalCode 整块（取 { } 之间的声明文本）
    private static readonly Regex ReGlobalBlockBody = new Regex(
        @"\bGlobalCode\s*\{([\s\S]*?)\}",
        RegexOptions.Compiled);
    // 块内字段声明「类型 名字」（含数组）
    private static readonly Regex ReGlobalFieldName = new Regex(
        @"\b(?:int|float|double|bool|string|long|char|byte|short|decimal)\s*(?:\[\s*\])?\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled);

    // ---------------- 结构判定正则（语法课：流程控制 / 表达式 / 字符串 / 类型转换 / 容器与自定义类型） ----------------
    // break; / continue;
    private static readonly Regex ReBreak = new Regex(
        @"\bbreak\s*;",
        RegexOptions.Compiled);
    private static readonly Regex ReContinue = new Regex(
        @"\bcontinue\s*;",
        RegexOptions.Compiled);
    // 三元运算符：条件 ? 成立值 : 不成立值
    private static readonly Regex ReTernary = new Regex(
        @"\?[^?:;{}\n]+:",
        RegexOptions.Compiled);
    // 复合赋值 += -= *= /= %=
    private static readonly Regex ReCompoundAssign = new Regex(
        @"(?:\+=|-=|\*=|/=|%=)",
        RegexOptions.Compiled);
    // 自增自减
    private static readonly Regex ReIncrement = new Regex(
        @"(?:\+\+|--)",
        RegexOptions.Compiled);
    // 逻辑与 / 逻辑或
    private static readonly Regex ReLogicAnd = new Regex(
        @"&&",
        RegexOptions.Compiled);
    private static readonly Regex ReLogicOr = new Regex(
        @"\|\|",
        RegexOptions.Compiled);
    // 逻辑非（排除 != 与 == 里的 !）
    private static readonly Regex ReLogicNot = new Regex(
        @"(?<![=!<>])!(?!=)",
        RegexOptions.Compiled);
    // 字符串成员：.Length / .Contains( / .Split( / .Substring( / .Trim( / .ToUpper( / .Replace( / .IndexOf( 等
    private static readonly Regex ReStrMember = new Regex(
        @"\.\s*(?:Length|Contains|Split|Substring|Trim|ToUpper|ToLower|Replace|IndexOf|StartsWith|EndsWith)\b",
        RegexOptions.Compiled);
    // 下标访问 s[i]（变量当下标）
    private static readonly Regex ReStrIndex = new Regex(
        @"[A-Za-z_)\]]\s*\[\s*[A-Za-z_]\w*\s*\]",
        RegexOptions.Compiled);
    // 数组长度 .Length
    private static readonly Regex ReArrayLength = new Regex(
        @"\.\s*Length\b",
        RegexOptions.Compiled);
    // 嵌套 for：for (...) { for (
    private static readonly Regex ReNestedFor = new Regex(
        @"\bfor\s*\([^;{}]*;[^;{}]*;[^;{}]*\)\s*\{\s*for\s*\(",
        RegexOptions.Compiled);
    // while (true)
    private static readonly Regex ReWhileTrue = new Regex(
        @"\bwhile\s*\(\s*true\s*\)",
        RegexOptions.Compiled);
    // int.Parse / int.TryParse / float.Parse
    private static readonly Regex ReParseCall = new Regex(
        @"\b(?:int|float|double|long|bool)\s*\.\s*(?:Parse|TryParse)\s*\(",
        RegexOptions.Compiled);
    // (int) / (float) 这类强制转换
    private static readonly Regex ReCastExpr = new Regex(
        @"\(\s*(?:int|float|double|long|char|bool)\s*\)\s*[A-Za-z_(0-9]",
        RegexOptions.Compiled);
    // try { ... } catch
    private static readonly Regex ReTryCatch = new Regex(
        @"\btry\s*\{[\s\S]*?\bcatch\b",
        RegexOptions.Compiled);
    // 与 null 的比较（== null / != null / null ==）
    private static readonly Regex ReNullCompare = new Regex(
        @"(?:==|!=)\s*null\b|\bnull\s*(?:==|!=)",
        RegexOptions.Compiled);
    // new Dictionary<
    private static readonly Regex ReDictNew = new Regex(
        @"\bnew\s+Dictionary\s*<",
        RegexOptions.Compiled);
    // dict[键] = 值
    private static readonly Regex ReDictSet = new Regex(
        @"[A-Za-z_]\w*\s*\[[^\]]+\]\s*=(?!=)",
        RegexOptions.Compiled);
    // enum 名字 {
    private static readonly Regex ReEnumDef = new Regex(
        @"\benum\s+[A-Za-z_]\w*\s*\{",
        RegexOptions.Compiled);
    // 自己写的 class
    private static readonly Regex ReLocalClass = new Regex(
        @"\bclass\s+[A-Za-z_]\w*",
        RegexOptions.Compiled);
    // 属性的 get / set
    private static readonly Regex RePropGet = new Regex(
        @"\bget\s*[\{;=]",
        RegexOptions.Compiled);
    private static readonly Regex RePropSet = new Regex(
        @"\bset\s*[\{;=]",
        RegexOptions.Compiled);
    // out / ref 参数
    private static readonly Regex ReOutRefParam = new Regex(
        @"\b(?:out|ref)\s+(?:[A-Za-z_][\w<>\[\]]*\s+)?[A-Za-z_]\w*\s*(?=[,)=])",
        RegexOptions.Compiled);
    // 方法自己调用自己（递归）
    private static readonly Regex ReSelfRecursion = new Regex(
        @"\b(?:void|int|float|double|bool|string|long|char)\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*\{[\s\S]{0,600}?\b\1\s*\(",
        RegexOptions.Compiled);
    // 二维数组创建：new int[a,b] / new int[a][]
    private static readonly Regex ReMultiArray = new Regex(
        @"\bnew\s+(?:int|float|double|string|bool|char)\s*\[[^\]]*,\s*[^\]]*\]|\bnew\s+(?:int|float|double|string|bool|char)\s*\[[^\]]*\]\s*\[",
        RegexOptions.Compiled);
    // 双下标访问：grid[r, c] 或 jag[r][c]
    private static readonly Regex ReMultiIndex = new Regex(
        @"[A-Za-z_]\w*\s*\[\s*[^\]]+,\s*[^\]]+\]|[A-Za-z_]\w*\s*\[[^\]]+\]\s*\[",
        RegexOptions.Compiled);
    // 索引器声明：public int this[int i] { ... }
    private static readonly Regex ReIndexerDecl = new Regex(
        @"\bthis\s*\[\s*[^\]]+\s*\]\s*\{",
        RegexOptions.Compiled);
    // 接口声明：interface IHittable
    private static readonly Regex ReInterfaceDecl = new Regex(
        @"\binterface\s+[A-Za-z_]\w*",
        RegexOptions.Compiled);
    // 类继承 / 实现：class Dummy : IHittable
    private static readonly Regex ReBaseList = new Regex(
        @"\bclass\s+[A-Za-z_]\w*\s*:\s*[A-Za-z_]\w*",
        RegexOptions.Compiled);
    // 委托类型变量的赋值：Func<int,int> sq = ... / Action<string> log = ...
    private static readonly Regex ReDelegateAssign = new Regex(
        @"\b(?:Action|Func)\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s+[A-Za-z_]\w*\s*=",
        RegexOptions.Compiled);
    // Lambda：x => ... 或 (a, b) => ...
    private static readonly Regex ReLambda = new Regex(
        @"=\s*(?:\(\s*[^()]*\)|[A-Za-z_]\w*)\s*=>",
        RegexOptions.Compiled);
    // 自定义 delegate 类型声明：delegate int Op(int a, int b);
    private static readonly Regex ReDelegateDecl = new Regex(
        @"\bdelegate\s+[A-Za-z_][\w<>\[\]]*\s+[A-Za-z_]\w*\s*\(",
        RegexOptions.Compiled);
    // LINQ 调用里带 Lambda：.Where(v => ...) / .Select(v => ...) / .OrderBy(v => ...)
    private static readonly Regex ReLinqCall = new Regex(
        @"\.\s*(?:Where|Select|OrderBy|OrderByDescending|First|FirstOrDefault|Any|All|Sum|Count|Max|Min|ToList|ToArray)\s*\(\s*(?:\([^()]*\)|[A-Za-z_]\w*)\s*=>",
        RegexOptions.Compiled);
    // 泛型类 / 泛型方法：class Box<T> / T Pick<T>(...)
    private static readonly Regex ReGenericClassDecl = new Regex(
        @"\b(?:class|struct|interface)\s+[A-Za-z_]\w*\s*<\s*T\s*>",
        RegexOptions.Compiled);
    private static readonly Regex ReGenericMethodDecl = new Regex(
        @"\b[A-Za-z_][\w\[\]]*\s+[A-Za-z_]\w*\s*<\s*T\s*>\s*\(",
        RegexOptions.Compiled);
    // 泛型约束：where T : class / where T : struct / where T : new()
    private static readonly Regex ReWhereConstraint = new Regex(
        @"\bwhere\s+T\s*:",
        RegexOptions.Compiled);
    // 全限定名：System.Math.Max(...) / System.DateTime.Now / System.Collections.Generic.List<int>
    private static readonly Regex ReQualifiedName = new Regex(
        @"\bSystem\s*\.\s*[A-Za-z_]\w*\s*(?:\.\s*[A-Za-z_]\w*\s*)*[\.<]",
        RegexOptions.Compiled);
    // 预处理指令：#region / #if / #endif（必须逐行开头，所以开 Multiline）
    private static readonly Regex RePreprocRegion = new Regex(
        @"^[ \t]*#region\b",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex RePreprocIf = new Regex(
        @"^[ \t]*#if\b",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex RePreprocEndif = new Regex(
        @"^[ \t]*#endif\b",
        RegexOptions.Compiled | RegexOptions.Multiline);
    // 结构体声明：struct Pt
    private static readonly Regex ReStructDecl = new Regex(
        @"\bstruct\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled);
    // 元组类型 / 元组字面量 / 解构：(int x, int y) t = ... / (3, 4) / var (a, b) = ...
    private static readonly Regex ReTupleType = new Regex(
        @"\(\s*[A-Za-z_][\w<>\[\]\.]*\s+[A-Za-z_]\w*\s*,\s*[A-Za-z_][\w<>\[\]\.]*\s+[A-Za-z_]\w*\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex ReTupleLiteral = new Regex(
        @"=\s*\(\s*[^(),;]+\s*,\s*[^(),;]+\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex ReDeconstruct = new Regex(
        @"(?:var\s*\(|\(\s*[A-Za-z_][\w<>\[\]\.]*\s+)[A-Za-z_]\w*\s*,\s*[A-Za-z_]\w*\s*\)\s*=",
        RegexOptions.Compiled);
    // 可空类型 / null 条件访问 / null 合并：int? hp = null; go?.transform; hp ?? 0
    private static readonly Regex ReNullableType = new Regex(
        @"\?\s+[A-Za-z_]\w*\s*=",
        RegexOptions.Compiled);
    private static readonly Regex ReNullConditional = new Regex(
        @"[A-Za-z_\]\)]\s*\?\.",
        RegexOptions.Compiled);
    private static readonly Regex ReNullCoalesce = new Regex(
        @"\?\?",
        RegexOptions.Compiled);
    // 字符串插值 $"" 与格式符 {x:F2}
    private static readonly Regex ReInterpolated = new Regex(
        "\\$\"",
        RegexOptions.Compiled);
    private static readonly Regex ReFormatSpec = new Regex(
        @"\{[^{}]*:\s*[FfNnPpXxEeCcDd0#]",
        RegexOptions.Compiled);
    // 对象初始化器：new Bullet { dmg = 5 } / new Bullet() { dmg = 5 }
    private static readonly Regex ReObjectInitializer = new Regex(
        @"\bnew\s+([A-Za-z_][\w\.]*)\s*(?:\([^()]*\))?\s*\{",
        RegexOptions.Compiled);
    // 静态类声明：static class IntExt
    private static readonly Regex ReStaticClassDecl = new Regex(
        @"\bstatic\s+class\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled);
    // 迭代器方法：方法体里有 yield return
    private static readonly Regex ReYieldReturn = new Regex(
        @"\byield\s+return\b",
        RegexOptions.Compiled);
    // 枚举转换：(int)kind / (Kind)2 / Enum.Parse
    private static readonly Regex ReEnumCast = new Regex(
        @"\(\s*[A-Za-z_]\w*\s*\)\s*[A-Za-z_]\w*\s*[;\)]",
        RegexOptions.Compiled);


    /// <summary>判定：写了 GlobalCode 块，且块内声明的变量在块外被真正用到（只摆着不用的块不算完成）。</summary>
    private static bool JudgeGlobalBlock(string raw, string code)
    {
        if (string.IsNullOrEmpty(code) || !ReGlobalBlock.IsMatch(code))
            return false;
        Match body = ReGlobalBlockBody.Match(code);
        string inner = body.Success ? body.Groups[1].Value : string.Empty;
        string rest = body.Success ? code.Remove(body.Index, body.Length) : code;
        Match name = ReGlobalFieldName.Match(inner);
        if (!name.Success)
            return false;
        string varName = name.Groups[1].Value;
        return Regex.IsMatch(rest, @"\b" + Regex.Escape(varName) + @"\b");
    }

    // ================== 静态入口（供 LevelDirector / MainMenuController 调用） ==================

    /// <summary>进入教学关：启动（或重开）指定课程的顺序式教学，进度从任务 1 重新开始。</summary>
    public static void Begin(Course course)
    {
        Begin(course, 0);
    }

    /// <summary>
    /// 进入教学关并从第 startIndex 个任务（0 基）开始（供 /level 1 3 n、/level 2 2 n 章节跳转使用）；
    /// 越界值自动收敛到合法范围；跳过的任务保持「未开始」，是否已完成只以玩家真实完成记录为准。
    /// </summary>
    public static void Begin(Course course, int startIndex)
    {
        CodeSyntaxTutorialController t = Instance;
        if (t == null)
        {
            GameObject go = new GameObject("CodeSyntaxTutorial");
            t = go.AddComponent<CodeSyntaxTutorialController>(); // AddComponent 会立即执行 Awake，Instance 随之就绪
        }
        t.BeginInternal(course, startIndex);
    }

    /// <summary>指定课程的任务（part 章节）总数，供指令框与关卡导演校验章节参数。</summary>
    public static int GetTaskCount(Course course)
    {
        TaskDef[] list = (course == Course.Api) ? ApiTasks : GrammarTasks;
        return list != null ? list.Length : 0;
    }

    /// <summary>课程 → 该课「已完成任务」记录的 PlayerPrefs 键名（语法课 / API 课各一张完成表）。</summary>
    public static string DoneKeyOf(Course course)
    {
        return course == Course.Api ? GameProgress.TaskDoneKeyApi : GameProgress.TaskDoneKeyGrammar;
    }

    /// <summary>离开教学关（回主页 / 跳其他关卡）：结束教学，隐藏任务卡与提示条并清空激活状态。</summary>
    public static void End()
    {
        if (Instance != null)
            Instance.EndInternal();
    }

    // ================== 生命周期 ==================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
        SetAllVisible(false);
    }

    private void OnDestroy()
    {
        CodeEditorController.OnCompiledSuccessfully -= HandleCompiledSuccessfully;
        Localization.OnLanguageChanged -= RefreshText;
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!Active)
            return;

        EnsureRefs();

        // 窗口尺寸变化时工作区总高改变 → 重新按「一半」重算指导框高度并同步给编辑器（比例恒定，绝对高度跟着走）
        if (Mathf.Abs(appliedSplitHeight - SplitHalfHeight()) > 0.5f)
            ApplySplitLayout();

        // 「任务完成」提示倒计时（真实时间：编辑器打开时 timeScale = 0）
        if (flashTimer > 0f)
        {
            flashTimer -= Time.unscaledDeltaTime;
            if (flashTimer <= 0f && flashText != null)
                flashText.text = "";
        }

        // 全部任务真的做完 → 完成提示停留 HomeReturnDelay 秒后自动回主页（走完最后一章不算）
        if (homeReturnTimer > 0f)
        {
            homeReturnTimer -= Time.unscaledDeltaTime;
            if (homeReturnTimer <= 0f)
            {
                homeReturnTimer = 0f;
                MainMenuController menu = Object.FindObjectOfType<MainMenuController>();
                if (menu != null)
                {
                    Debug.Log("[CodeSyntaxTutorial] 全部任务完成，返回主页面。");
                    menu.ShowMenu();
                }
            }
        }

        // ← / → 翻页、↑ / ↓ 走步骤：键盘就能把整段教学看完，不用去点小三角
        HandlePageNavKey();
        AdvanceAnim();

        SyncVisibility();

        // 编辑器未打开时不做结构判定（玩家在编辑代码时才推进）
        if (finished || CurrentIndex >= TaskCount)
            return;
        if (editor == null || !editor.IsOpen)
            return;

        checkTimer += Time.unscaledDeltaTime;
        if (checkTimer < 0.15f) // 每 0.15 秒判定一次，避免逐帧跑正则
            return;
        checkTimer = 0f;

        EvaluateCurrentTask();
    }

    // ================== 启动 / 结束 ==================

    private void BeginInternal(Course course, int startIndex)
    {
        EnsureRefs();

        Active = true;
        CurrentCourse = course;
        tasks = (course == Course.Api) ? ApiTasks : GrammarTasks;
        done = new bool[tasks.Length];
        finished = false;
        homeReturnTimer = 0f;   // 新一次教学从「不打算回主页」开始

        // 章节定位：startIndex 为 0 基任务序号，越界自动收敛。
        //   · 「哪些章节做过了」只认玩家真实完成记录（PlayerPrefs 里一张独立的完成表），
        //     不再按「序号在 startIndex 之前的都算做过」伪造进度：直接 /level 1 3 10 或 /level 2 2 10
        //     跳到第 10 个任务时，第 1~9 个任务保持「未开始」的灰色，不会显示成已完成；
        //   · 没有记录（第一次进入 / 清档后 / 老版本存档）时是一张全 false 的表，等价于「什么都没做过」。
        int start = Mathf.Clamp(startIndex, 0, Mathf.Max(0, tasks.Length - 1));
        done = GameProgress.LoadTaskDone(DoneKeyOf(course), tasks.Length);

        // 「从关卡起点进入」（startIndex ≤ 0：选关卡片、主页继续按到该关开头、直接进某个 Part 都是这种）
        // 时不再从第 1 个任务重新讲起，而是直接停在最近一个没做完的任务上：
        //   · 完成表里第一个 false 就是玩家接下来该做的那一项（中间被跳过的空洞也顺手补上，不会越做越散）；
        //   · 玩家 / 开发者显式指定章节（/level 2 2 7 的第三个参数 → startIndex > 0）时一律不动，
        //     保留精确跳转能力，方便回看早先的任务；
        //   · 整门课全部做完（找不到 false）时仍从第 1 个任务进，方便整关从头重玩。
        if (start <= 0)
        {
            int firstUndone = -1;
            for (int i = 0; i < done.Length; i++)
            {
                if (!done[i])
                {
                    firstUndone = i;
                    break;
                }
            }
            if (firstUndone > 0)
                start = firstUndone;
        }

        CurrentIndex = start;
        guidePage = 0; // 进入教学总是从每个任务的第 1 页（写法 / 示例）开始看

        compiledThisTask = false;
        compiledRaw = "";
        compiledSnapshot = "";
        checkTimer = 0f;
        flashTimer = 0f;
        hintCache = "";
        statusCache = -1;

        // 若之前是 Level 1 - Part 1 的引导教学，先退出它：编辑器在 TutorialController.Started 期间禁止打开
        TutorialController tutorial = FindObjectOfType<TutorialController>();
        if (tutorial != null && tutorial.Started)
            tutorial.ExitToMenu();

        // 关卡表现：两门课都隐藏分数 HUD、显示 print 输出荧幕（要看到 PrintLine 结果）；
        // API 课额外允许蓝色 BOMB 块（教学 o.Type 判断需要两种类型）
        if (course == Course.Api)
            LevelPresentation.ApplyLevel2Api();
        else
            LevelPresentation.ApplyLevel2();

        // 教学期间先不出块：全部任务完成后开始出块，让玩家写的程序立刻「有用武之地」；
        // API 课额外预生成少量静止方块，保证 GetAllObjects / 字段读取任务一上手就有真实数据。
        if (spawner != null)
        {
            spawner.ClearAllBlocks();
            spawner.ResetSession();
            spawner.SuspendSpawning();
            if (course == Course.Api)
                EnsureDemoBlocks();
        }

        // 订阅编译成功事件与语言切换事件（先退订再订阅，防止重复订阅）
        CodeEditorController.OnCompiledSuccessfully -= HandleCompiledSuccessfully;
        CodeEditorController.OnCompiledSuccessfully += HandleCompiledSuccessfully;
        Localization.OnLanguageChanged -= RefreshText;
        Localization.OnLanguageChanged += RefreshText;

        // 清空编辑器内容：避免默认示例代码一进来就满足全部结构判定（注释任务也需玩家自己写）
        if (editor != null)
            editor.SetCodeForTutorial(string.Empty);

        // 布局：上下对半平分 —— 上方代码编辑器占工作区上半（50%，代码过长时用编辑器内部竖向滚动条），
        // 下方指导框横跨工作区下半（50%）、上边缘与代码编辑器下边缘严丝合缝、下边缘贴屏幕底部。
        // 比例固定，不随窗口尺寸变化、也不随任务文案长短变化（ApplySplitLayout）。
        // 只在本教学（Level 1-Part 3 / Level 2-Part 2）生效，EndInternal 恢复默认全屏布局。
        if (editor != null)
            editor.SetRightReservedWidth(0f); // 上下分栏不再左右平分，右侧不再预留
        // 「选中方块信息」恢复屏幕水平居中（下方指导框已横向占满，不再需要左右错位避让）
        BlockHitIndicator.SetHorizontalOffsetGlobal(0f);

        LayoutChips();
        RefreshText();
        ApplySplitLayout(); // 指导框固定占工作区下半 50%，并把同一高度作为编辑器底部预留

        // 进入教学后先停在实机页面（不自动弹出编辑器）：玩家看到的是真实关卡画面，
        // 页面正中的提示告知「按下 Enter 打开代码编辑界面」，按 Enter 之后才进入编辑器、任务卡随之出现。
        if (editor != null && editor.IsOpen)
            editor.ForceCloseSelf(); // 从 API 课等别的教学切进来时，先收掉上一关残留的编辑器

        SyncVisibility();
        Debug.Log("[CodeSyntaxTutorial] 教学开始（" + course + "，共 " + TaskCount + " 项任务）。");
    }

    private void EndInternal()
    {
        EnsureRefs();

        // 离开教学（回主页 / 跳其它关卡）先无条件收尾布局与编辑器：恢复编辑器全屏、强制关闭界面并收起 Enter 提示，
        // 避免「从 Part 2 / API 教学跳到 Level 3」时还带着打开的全屏代码编辑器（本方法可能在教学已结束后被再次调用）。
        if (editor != null)
        {
            editor.SetBottomReservedHeight(0f);
            editor.SetRightReservedWidth(0f);
            editor.ForceCloseSelf();
        }
        BlockHitIndicator.SetHorizontalOffsetGlobal(0f);

        if (!Active)
            return;

        Active = false;
        finished = false;
        homeReturnTimer = 0f;
        CurrentIndex = 0;
        compiledThisTask = false;
        compiledRaw = "";
        compiledSnapshot = "";
        flashTimer = 0f;
        hintCache = "";
        statusCache = -1;
        if (done != null)
        {
            for (int i = 0; i < done.Length; i++)
                done[i] = false;
        }

        CodeEditorController.OnCompiledSuccessfully -= HandleCompiledSuccessfully;
        Localization.OnLanguageChanged -= RefreshText;

        // 布局收尾（恢复编辑器全屏 / 模块居中）已在方法开头无条件完成，这里只复位教学状态
        SetAllVisible(false);
        Debug.Log("[CodeSyntaxTutorial] 教学结束，进度已重置。");
    }

    private void SetAllVisible(bool visible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(visible);
        if (hintRoot != null)
            hintRoot.SetActive(false); // 提示条由 SyncVisibility 按编辑器开关单独控制
    }

    private void EnsureRefs()
    {
        if (editor == null)
            editor = FindObjectOfType<CodeEditorController>();
        if (spawner == null)
            spawner = FlyingBlockSpawner.Instance != null ? FlyingBlockSpawner.Instance : FindObjectOfType<FlyingBlockSpawner>();
        if (tower == null)
            tower = FindObjectOfType<CodeTower>();
    }

    /// <summary>API 课：补齐至少 4 个场上物体，让 GetAllObjects / 字段读取任务有真实数据可读。</summary>
    private void EnsureDemoBlocks()
    {
        if (spawner == null)
            return;

        int alive = 0;
        for (int i = 0; i < spawner.ActiveBlocks.Count; i++)
        {
            FlyingBlock b = spawner.ActiveBlocks[i];
            if (b != null && !b.Destroyed)
                alive++;
        }
        for (int k = alive; k < 4; k++)
        {
            if (spawner.SpawnBlock() == null)
                break;
        }
    }

    // ================== 任务判定 ==================

    /// <summary>当前代码是否命中当前任务的语法（入参剥离注释 / 字符串后再判定）。</summary>
    private bool StructureHit(string raw, bool useCompiledSnapshot)
    {
        if (tasks == null || CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return false;
        TaskMatch m = tasks[CurrentIndex].Match;
        if (m == null)
            return false;
        if (useCompiledSnapshot)
            return m(compiledRaw ?? string.Empty, compiledSnapshot ?? string.Empty);
        if (string.IsNullOrEmpty(raw))
            return false;
        return m(raw, StripCommentsAndStrings(raw));
    }

    private void EvaluateCurrentTask()
    {
        if (editor == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= TaskCount)
            return;

        string raw = editor.CurrentCode;
        if (string.IsNullOrEmpty(raw))
            return;

        bool hit = StructureHit(raw, false);
        bool compiledHit = compiledThisTask && StructureHit(string.Empty, true);

        int key = (hit ? 1 : 0) | (compiledHit ? 2 : 0);
        if (key != statusCache)
        {
            statusCache = key;
            RefreshText(); // 语法 / 编译状态变化时刷新任务卡上的两条勾选
        }

        if (!hit || !compiledHit)
            return;

        CompleteCurrentTask();
    }

    private void HandleCompiledSuccessfully(string compiledCode)
    {
        if (!Active)
            return;

        compiledThisTask = true;
        compiledRaw = compiledCode ?? string.Empty;
        compiledSnapshot = StripCommentsAndStrings(compiledRaw);

        // API 课：编译后若场上没块了，补几个，保证紧随其后的运行期输出不空
        if (CurrentCourse == Course.Api)
            EnsureDemoBlocks();

        Debug.Log("[CodeSyntaxTutorial] 收到编译成功事件（任务 " + (CurrentIndex + 1) + "/" + TaskCount + "）。");

        // 立即用编译快照判定：编译通过的这版代码本身要满足当前任务的语法
        if (finished || CurrentIndex >= TaskCount)
        {
            RefreshText();
            return;
        }
        if (StructureHit(string.Empty, true))
            CompleteCurrentTask();
        else
            RefreshText();
    }

    /// <summary>
    /// 点击进度格跳转：在同一关（同一门教学课）内切换到任意任务。
    /// 只移动「当前任务」指针，已完成标记与关卡进度保持不变，也不会跳到别的关卡。
    /// </summary>
    private void JumpToTask(int index)
    {
        if (!Active)
            return;
        if (editor == null || !editor.IsOpen)
            return;
        if (tasks == null || index < 0 || index >= TaskCount || index == CurrentIndex)
            return;

        // 走完最后一章、但还有没做完的 Task：允许点进度格回去补做，
        // 同时解除「已走完」状态并取消自动回主页倒计时（否则会被强制送回主页）。
        if (finished)
        {
            finished = false;
            homeReturnTimer = 0f;
            Debug.Log("[CodeSyntaxTutorial] 点进度格回到任务补做，取消结业状态。");
        }

        CurrentIndex = index;
        guidePage = 0; // 换任务回到第 1 页
        ResetAnim();   // 动画也从头开演

        // 编译快照属于上一个任务，跳转后必须清零：否则新任务会被旧代码误判成「已编译通过」
        compiledThisTask = false;
        compiledRaw = string.Empty;
        compiledSnapshot = string.Empty;
        statusCache = -1;
        hintCache = string.Empty;
        flashTimer = 0f;
        if (flashText != null)
            flashText.text = string.Empty;

        // 断点续玩同步：手动跳转也算章节位置变化
        GameProgress.GetCourseLocation(CurrentCourse, out int jumpLevel, out int jumpPart);
        GameProgress.SaveResume(jumpLevel, jumpPart, index + 1);

        Debug.Log("[CodeSyntaxTutorial] 点击进度格跳转到任务 " + (index + 1) + "/" + TaskCount + "。");

        // 新任务的语法 / 编译两条勾选都要重新判定，先按当前编辑器内容刷新一次
        RefreshText();
        EvaluateCurrentTask();
    }

    // ================== 说明分页（第 2 页：零基础手把手细节） ==================

    /// <summary>某个任务有几页说明：有第 3 页动画即 3 页，否则有第 2 页文案即 2 页，都没有则 1 页。</summary>
    private int PageCountOf(int index)
    {
        if (tasks == null || index < 0 || index >= tasks.Length)
            return 1;
        TaskDef t = tasks[index];
        bool hasThird = (t.Anim != null && t.Anim.Length > 0)
            || (t.Mem != null && t.Mem.Vars != null && t.Mem.Vars.Length > 0
                && t.Mem.Steps != null && t.Mem.Steps.Length > 0)
            || (t.Dia != null && t.Dia.Nodes != null && t.Dia.Nodes.Length > 0
                && t.Dia.Steps != null && t.Dia.Steps.Length > 0);
        if (hasThird)
            return 3;
        bool hasSecond = !string.IsNullOrEmpty(t.Page2Zh) || !string.IsNullOrEmpty(t.Page2En);
        return hasSecond ? 2 : 1;
    }

    /// <summary>翻回上一页（第 1 页时按钮不可点）。</summary>
    private void PrevPage()
    {
        if (!Active || finished || tasks == null)
            return;
        if (guidePage <= 0 || CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;
        guidePage--;
        ResetAnim();
        RefreshText();
    }

    /// <summary>翻到下一页（手把手细节页 / 代码动画页）。</summary>
    private void NextPage()
    {
        if (!Active || finished || tasks == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;
        if (guidePage >= PageCountOf(CurrentIndex) - 1)
            return;
        guidePage++;
        ResetAnim();
        RefreshText();
    }

    /// <summary>代码动画从头开始（换任务 / 换页时调用，避免停在上一个任务的中途）。</summary>
    private void ResetAnim()
    {
        animTimer = 0f;
        animStep = 0;
        memBuiltIndex = -1;    // 内存舞台也从头演：强制下一帧重排
        memBuiltStep = -1;
        diaBuiltIndex = -1;    // 逻辑图舞台同理
        diaBuiltStep = -1;
    }

    // ================== 第 3 页：代码动画（逐行高亮 + 旁白） ==================

    /// <summary>本任务是否有第 3 页代码动画。</summary>
    private bool HasAnimPage(int index)
    {
        return tasks != null && index >= 0 && index < tasks.Length
            && tasks[index].Anim != null && tasks[index].Anim.Length > 0;
    }

    /// <summary>
    /// 从任务正文里抽出「示例代码行」：第 1 页写法段的代码统一缩进 4 个空格，
    /// 目标 / 提示 / 判定那几段顶格写 —— 所以「缩进 ≥ 4 空格」就是代码行，不用另存一份代码。
    /// 固定读中文正文（两套语言的示例代码完全一致），避免切语言后动画行对不上。
    /// </summary>
    private static string[] ExampleCodeLines(TaskDef task, bool zh)
    {
        if (task == null)
            return new string[0];
        // 代码里的注释也要跟着语言走：英文模式读 BodyEn 的缩进行（两套示例代码行序、缩进完全一致），
        // 万一某个任务没写英文正文，再回落到中文正文，绝不出现空白页。
        if (!zh && !string.IsNullOrEmpty(task.BodyEn))
        {
            string[] en = IndentedCodeLines(task.BodyEn);
            if (en.Length > 0)
                return en;
        }
        if (string.IsNullOrEmpty(task.BodyZh))
            return new string[0];
        return IndentedCodeLines(task.BodyZh);
    }

    /// <summary>抽出「缩进 ≥ 4 空格」的行 —— 任务正文里代码行的统一约定。</summary>
    private static string[] IndentedCodeLines(string body)
    {
        string[] raw = body.Replace("\r\n", "\n").Split('\n');
        List<string> code = new List<string>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i];
            if (line.StartsWith("    ", System.StringComparison.Ordinal))
                code.Add(line.Substring(4).TrimEnd());
        }
        return code.ToArray();
    }

    /// <summary>按特征子串在示例代码里找行；找不到就退回第 1 行（宁可高亮错行也不整页空掉）。</summary>
    private static int FindAnimLine(string[] lines, string key, string keyEn = null)
    {
        if (lines == null || lines.Length == 0)
            return 0;
        int hit = FindLineByKey(lines, key);
        if (hit >= 0)
            return hit;
        hit = FindLineByKey(lines, keyEn);   // 中文 Key 在英文示例代码里找不到时换英文子串
        if (hit >= 0)
            return hit;
        return 0;
    }

    private static int FindLineByKey(string[] lines, string key)
    {
        if (lines == null || lines.Length == 0 || string.IsNullOrEmpty(key))
            return -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].IndexOf(key, System.StringComparison.Ordinal) >= 0)
                return i;
        }
        return -1;
    }

    /// <summary>右栏：示例代码全文，当前执行的那一行用 ► 加高亮色标出来（每步重新生成一次即「动画」）。</summary>
    private string BuildAnimCode(int index, bool zh)
    {
        string[] lines = ExampleCodeLines(tasks[index], zh);
        if (lines.Length == 0)
            return string.Empty;

        AnimStep[] anim = tasks[index].Anim;
        int step = Mathf.Clamp(animStep, 0, anim.Length - 1);
        int hl = FindAnimLine(lines, anim[step].Key, anim[step].KeyEn);

        StringBuilder sb = new StringBuilder(lines.Length * 48 + 96);
        sb.Append(zh ? "【示例代码 · ► 就是现在执行到的那一行】\n\n" : "[Sample code - ► marks the line running now]\n\n");
        for (int i = 0; i < lines.Length; i++)
        {
            string no = (i + 1).ToString("00") + "  ";
            if (i == hl)
            {
                sb.Append("<color=#5FD7FF><b>► ").Append(no).Append(lines[i]).Append("</b></color>\n");
            }
            else
            {
                sb.Append("  ").Append(no).Append(lines[i]).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>左栏：动画旁白 —— 进度点 + 这一步在干什么 + 循环播放说明。</summary>
    private string BuildAnimGuide(int index, bool zh)
    {
        AnimStep[] anim = tasks[index].Anim;
        int step = Mathf.Clamp(animStep, 0, anim.Length - 1);
        AnimStep cur = anim[step];

        StringBuilder sb = new StringBuilder(420);
        sb.Append(zh
            ? "【这一页：代码动画 —— 让这段代码自己跑一遍】\n本页不用你敲代码，眼睛跟着右栏的 ► 走就行。"
            : "[THIS PAGE: CODE ANIMATION - watch the code run itself]\nNothing to type here: just follow ► on the right.");

        sb.Append("\n\n").Append(zh ? "进度 " : "Step ")
          .Append(step + 1).Append('/').Append(anim.Length).Append("   ");
        for (int i = 0; i < anim.Length; i++)
            sb.Append(i <= step ? "●" : "○");

        sb.Append("\n\n").Append(zh ? "▶ 这一步在做什么：\n" : "▶ What happens here:\n");
        sb.Append(zh ? cur.Zh : cur.En);

        sb.Append(zh
            ? "\n\n（不会自动播放：按 ↓ 一步一步看，走到底再按一次回到第一步。想重看：翻回第 1 页再翻回本页即可。）"
            : "\n(No auto-play: press ↓ to step through; after the last step it wraps to the first. To replay, flip back to page 1 and return here.)");
        return sb.ToString();
    }

    // ================== 第 3 页：内存可视化舞台（manim 风格） ==================

    /// <summary>本任务是否有内存可视化页（有它就用图形舞台，优先于逻辑图 / 旧的逐行高亮）。</summary>
    private bool HasMemPage(int index)
    {
        return tasks != null && index >= 0 && index < tasks.Length
            && tasks[index].Mem != null
            && tasks[index].Mem.Vars != null && tasks[index].Mem.Vars.Length > 0
            && tasks[index].Mem.Steps != null && tasks[index].Mem.Steps.Length > 0;
    }

    /// <summary>
    /// 本任务是否有逻辑图页（概念型知识点：控制流 / 调用链 / 映射表 / 关系图）。
    /// 优先级排在内存页之后、代码动画之前 —— 一个任务只可能用其中一种画法，不会两张图抢同一页。
    /// </summary>
    private bool HasDiagramPage(int index)
    {
        if (HasMemPage(index))
            return false;
        return tasks != null && index >= 0 && index < tasks.Length
            && tasks[index].Dia != null
            && tasks[index].Dia.Nodes != null && tasks[index].Dia.Nodes.Length > 0
            && tasks[index].Dia.Steps != null && tasks[index].Dia.Steps.Length > 0;
    }

    /// <summary>一段内存占几个字节（= 这一行要画几个格子）。</summary>
    private static int CountBytes(MemVar v)
    {
        if (v == null || string.IsNullOrEmpty(v.Bytes))
            return 1;
        string[] parts = v.Bytes.Split(' ');
        int n = 0;
        for (int i = 0; i < parts.Length; i++)
            if (!string.IsNullOrEmpty(parts[i]))
                n++;
        return Mathf.Max(1, n);
    }

    /// <summary>
    /// 建内存舞台：顶部一行说明 + 中间一行行变量（每行 = 徽章 / 类型名牌 / 变量名 / 字节格 / 行下小字）
    /// + 底部「当前执行的那行代码」与「这一拍在干什么」。
    /// 整块正文区都归舞台，所有子件一次建好塞进池子，播放时只改颜色 / 文字 / 位置。
    /// </summary>
    private void BuildMemStage(RectTransform panelRt)
    {
        RectTransform root = CreateRect("MemStage", panelRt, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        // 内存页整块都是图，不需要给「写法 / 示例代码」留位，所以上下留白比文字栏窄一点：
        // 上边到任务名那一行下面（-108，任务名占到 -124），下边只留一条呼吸位。
        // 多出来的这几十像素正好让 4 行变量也放得下「行下小字」。
        root.offsetMin = new Vector2(MemStageSideInset, MemStageBottomInset);
        root.offsetMax = new Vector2(-MemStageSideInset, -MemStageTopInset);
        memRoot = root;

        // ---- 顶部：左边一句「怎么读这张图」，右边步数 ----
        memHeaderText = CreateText("MemHeader", root, "", 15, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform hdr = memHeaderText.rectTransform;
        hdr.anchorMin = new Vector2(0f, 1f);
        hdr.anchorMax = new Vector2(1f, 1f);
        hdr.pivot = new Vector2(0f, 1f);
        hdr.anchoredPosition = Vector2.zero;
        hdr.sizeDelta = new Vector2(-190f, MemHeaderOnceH);
        memHeaderText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memHeaderText.verticalOverflow = VerticalWrapMode.Truncate;
        // 不再开 bestFit：原来 40px 里硬塞两行会被自动缩到 12 号、行与行压在一起（看着就是「重合」）。
        // 现在只在首次出现时展开到 MemHeaderOnceH，用固定的 15 号两行稳稳坐下。
        memHeaderText.resizeTextForBestFit = false;
        memHeaderText.lineSpacing = 1.12f;

        memFootText = CreateText("MemFoot", root, "", 16, UITheme.TextDim, TextAnchor.MiddleRight);
        RectTransform ftr = memFootText.rectTransform;
        ftr.anchorMin = new Vector2(1f, 1f);
        ftr.anchorMax = new Vector2(1f, 1f);
        ftr.pivot = new Vector2(1f, 1f);
        ftr.anchoredPosition = Vector2.zero;
        ftr.sizeDelta = new Vector2(180f, MemFootH);
        memFootText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // ---- 中间：行池 ----
        memRows = new MemRowUI[MemRowPool];
        for (int i = 0; i < MemRowPool; i++)
            memRows[i] = BuildMemRow(root, i);

        // ---- 底部：旁白（最底） ----
        memNoteText = CreateText("MemNote", root, "", 19, UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform nr = memNoteText.rectTransform;
        nr.anchorMin = new Vector2(0f, 1f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(0f, 1f);
        nr.anchoredPosition = new Vector2(0f, 0f);   // 真实位置由 PlaceMemBottom 按内容高度摆
        nr.sizeDelta = new Vector2(0f, MemNoteH);
        memNoteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memNoteText.verticalOverflow = VerticalWrapMode.Truncate;
        memNoteText.lineSpacing = 1.08f;
        memNoteText.resizeTextForBestFit = true;
        memNoteText.resizeTextMinSize = 12;
        memNoteText.resizeTextMaxSize = 19;

        // ---- 底部：执行台（旁白上方一条深色带，写「正在跑的那行代码」） ----
        RectTransform exprRt = CreateRect("MemExprBg", root, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, MemExprH), new Vector2(0.5f, 1f));
        exprRt.anchorMin = new Vector2(0f, 1f);
        exprRt.anchorMax = new Vector2(1f, 1f);
        exprRt.pivot = new Vector2(0.5f, 1f);
        exprRt.anchoredPosition = new Vector2(0f, 0f);   // 真实位置由 PlaceMemBottom 按内容高度摆
        exprRt.sizeDelta = new Vector2(0f, MemExprH);
        memExprRt = exprRt;
        Image exprBg = exprRt.gameObject.AddComponent<Image>();
        exprBg.sprite = WhiteSprite();
        exprBg.color = UITheme.WithAlpha(UITheme.CodeBg, 0.96f);
        exprBg.raycastTarget = false;

        memExprText = CreateText("MemExpr", exprRt, "", 19, UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform er = memExprText.rectTransform;
        er.anchorMin = Vector2.zero;
        er.anchorMax = Vector2.one;
        er.offsetMin = new Vector2(14f, 0f);
        er.offsetMax = new Vector2(-340f, 0f);   // 右侧让出键位提示的位置，代码行不会压到提示上
        memExprText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memExprText.verticalOverflow = VerticalWrapMode.Truncate;
        memExprText.resizeTextForBestFit = true;
        memExprText.resizeTextMinSize = 12;
        memExprText.resizeTextMaxSize = 19;

        // 执行台右侧的键位提示（多步任务才出现）：告诉玩家「按下 ↓ 观看下一个」；
        // 玩家点回代码区、输入框握了焦点之后，↓ 归代码用，提示随之改成「↓ 代码模式」。
        memKeyHintText = CreateText("MemKeyHint", exprRt, "", 17, UITheme.TextMuted, TextAnchor.MiddleRight);
        RectTransform kh = memKeyHintText.rectTransform;
        kh.anchorMin = new Vector2(1f, 0f);
        kh.anchorMax = new Vector2(1f, 1f);
        kh.pivot = new Vector2(1f, 0.5f);
        kh.anchoredPosition = new Vector2(-14f, 0f);
        kh.sizeDelta = new Vector2(320f, 0f);
        memKeyHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        memKeyHintText.verticalOverflow = VerticalWrapMode.Truncate;
        memKeyHintText.resizeTextForBestFit = true;
        memKeyHintText.resizeTextMinSize = 12;
        memKeyHintText.resizeTextMaxSize = 17;

        memRoot.gameObject.SetActive(false);
    }

    /// <summary>建一行：整块底 + 徽章 + 类型名牌 + 变量名 + 12 个字节格 + 行下小字。全部一次建好，之后只改内容。</summary>
    private MemRowUI BuildMemRow(RectTransform parent, int idx)
    {
        MemRowUI row = new MemRowUI();

        RectTransform root = CreateRect("MemRow" + idx, parent, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(0f, 88f), new Vector2(0f, 1f));
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(0f, 1f);
        row.Root = root;

        RectTransform frame = CreateRect("Frame", root, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(0f, 88f), new Vector2(0f, 1f));
        frame.anchorMin = new Vector2(0f, 0f);
        frame.anchorMax = new Vector2(1f, 1f);
        frame.pivot = new Vector2(0f, 1f);
        frame.offsetMin = new Vector2(-8f, -2f);
        frame.offsetMax = new Vector2(8f, 2f);
        Image fbg = frame.gameObject.AddComponent<Image>();
        fbg.sprite = WhiteSprite();
        fbg.color = UITheme.WithAlpha(UITheme.PanelDeep, 0f);
        fbg.raycastTarget = false;
        row.FrameBg = fbg;

        row.Badge = CreateText("Badge", root, "", 13, UITheme.PanelDeep, TextAnchor.MiddleCenter);
        row.Badge.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.Badge.resizeTextForBestFit = true;
        row.Badge.resizeTextMinSize = 10;
        row.Badge.resizeTextMaxSize = 13;

        RectTransform typeBgRt = CreateRect("TypeBg", root, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(90f, 26f), new Vector2(0f, 1f));
        Image typeBg = typeBgRt.gameObject.AddComponent<Image>();
        typeBg.sprite = WhiteSprite();
        typeBg.color = UITheme.WithAlpha(UITheme.Accent, 0.16f);
        typeBg.raycastTarget = false;
        row.TypeBg = typeBg;

        row.TypeText = CreateText("TypeText", typeBgRt, "", 14, UITheme.Accent, TextAnchor.MiddleCenter);
        row.TypeText.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.TypeText.verticalOverflow = VerticalWrapMode.Truncate;
        row.TypeText.resizeTextForBestFit = true;
        row.TypeText.resizeTextMinSize = 10;
        row.TypeText.resizeTextMaxSize = 14;

        row.NameText = CreateText("Name", root, "", 18, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        row.NameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.NameText.verticalOverflow = VerticalWrapMode.Truncate;
        row.NameText.resizeTextForBestFit = true;
        row.NameText.resizeTextMinSize = 11;
        row.NameText.resizeTextMaxSize = 18;

        row.CellRt = new RectTransform[MemCellPerRow];
        row.CellBg = new Image[MemCellPerRow];
        row.CellAddr = new Text[MemCellPerRow];
        row.CellVal = new Text[MemCellPerRow];
        row.CellPos = new Vector2[MemCellPerRow];
        for (int i = 0; i < MemCellPerRow; i++)
        {
            RectTransform cr = CreateRect("Cell" + i, root, new Vector2(0f, 1f), Vector2.zero,
                new Vector2(64f, 64f), new Vector2(0f, 1f));
            Image cbg = cr.gameObject.AddComponent<Image>();
            cbg.sprite = WhiteSprite();
            cbg.color = UITheme.WithAlpha(UITheme.PanelRaised, 0.92f);
            cbg.raycastTarget = false;

            Text addr = CreateText("Addr", cr, "", 10, UITheme.TextDim, TextAnchor.MiddleCenter);
            RectTransform ar = addr.rectTransform;
            ar.anchorMin = new Vector2(0f, 1f);
            ar.anchorMax = new Vector2(1f, 1f);
            ar.pivot = new Vector2(0.5f, 1f);
            ar.anchoredPosition = new Vector2(0f, -1f);
            ar.sizeDelta = new Vector2(-2f, 13f);
            addr.horizontalOverflow = HorizontalWrapMode.Wrap;
            addr.verticalOverflow = VerticalWrapMode.Truncate;
            addr.resizeTextForBestFit = true;
            addr.resizeTextMinSize = 8;
            addr.resizeTextMaxSize = 10;

            Text val = CreateText("Val", cr, "", 22, UITheme.TextPrimary, TextAnchor.MiddleCenter);
            RectTransform vr = val.rectTransform;
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.one;
            vr.pivot = new Vector2(0.5f, 0.5f);
            vr.offsetMin = new Vector2(1f, 2f);
            vr.offsetMax = new Vector2(-1f, -14f);
            val.horizontalOverflow = HorizontalWrapMode.Wrap;
            val.verticalOverflow = VerticalWrapMode.Truncate;
            val.resizeTextForBestFit = true;
            val.resizeTextMinSize = 9;
            val.resizeTextMaxSize = 22;

            row.CellRt[i] = cr;
            row.CellBg[i] = cbg;
            row.CellAddr[i] = addr;
            row.CellVal[i] = val;
            row.CellPos[i] = Vector2.zero;
            cr.gameObject.SetActive(false);
        }

        row.NoteText = CreateText("Note", root, "", 20, UITheme.TextBody, TextAnchor.MiddleLeft);
        row.NoteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        row.NoteText.verticalOverflow = VerticalWrapMode.Truncate;
        row.NoteText.resizeTextForBestFit = true;
        row.NoteText.resizeTextMinSize = 12;
        row.NoteText.resizeTextMaxSize = 20;

        root.gameObject.SetActive(false);
        return row;
    }

    /// <summary>
    /// 按当前步骤重排舞台：决定这一拍内存里有几段、每段占几格、每格写什么值。
    /// 只在步骤变化（或面板高度变了）时调用，平时只做补间，避免每帧重设文字。
    /// 每一步都可以用 Patch 改写某段内存的字节 —— 所以「for 里 i 从 0 变到 1 变到 2」是真的同一格在变，不是新开一段。
    /// </summary>
    private void RebuildMemStage(int index, int step)
    {
        if (!HasMemPage(index) || memRoot == null || memRows == null)
            return;

        MemScene scene = tasks[index].Mem;
        int n = Mathf.Clamp(step, 0, scene.Steps.Length - 1);
        MemStep cur = scene.Steps[n];
        bool zh = Localization.IsChinese;

        // 刚 SetActive 的那一帧，自身 rect 有时还没算出宽高：退而用宿主面板的实测尺寸，
        // 实在拿不到才用半屏常量。宁可先按小的排，也不能按大的排 —— 排大了行会压到底部说明上。
        RectTransform memHost = memRoot.parent as RectTransform;
        float availW = memRoot.rect.width;
        float availH = memRoot.rect.height;
        if (availW < 120f)
            availW = (memHost != null && memHost.rect.width >= 120f ? memHost.rect.width : ReferenceWidth) - 52f;
        if (availH < 120f)
            availH = (memHost != null && memHost.rect.height >= 120f
                ? memHost.rect.height
                : SplitHalfHeight()) - GuideBodyTopInset - GuideBodyBottomInset;

        int reveal = Mathf.Clamp(cur.Reveal, 0, Mathf.Min(scene.Vars.Length, MemRowPool));
        int rows = Mathf.Max(1, reveal);

        // 排版用的高度：面板被蓝色长条拖矮时，内容不往上吸，仍按「基准高度」这块地排，
        // 于是底部的执行台 / 旁白顺着往下走出画面，而不是钉在原地看着字被越挤越窄。
        float flowH = Mathf.Max(availH, MemStageBaselineHeight());
        PlaceMemBottom(flowH);

        // 顶部说明条只挂在 MemHeaderTaskIndex 那一个任务（Task 2「变量与类型」）上：
        // 那是第一次真正出现 int / char / bool / float 的地方，讲「一格 = 1 字节、小端怎么读」正对得上；
        // 别的任务（含 Task 1 注释、Task 4 算术）一律不出现，只留右上角步数那一行的高度，
        // 空出来的几十像素全还给字节行。
        bool showMemHeader = index == MemHeaderTaskIndex;
        float headerH = showMemHeader ? MemHeaderOnceH : MemFootH;

        // 底部固定占位：旁白 + 执行台；其余高度留给行区
        float bottomH = MemNoteH + MemNoteGapY + MemExprH + 12f;
        float rowsArea = Mathf.Max(90f, flowH - headerH - 10f - bottomH);
        float gap = MemRowGapY;
        // 均分 + 先收行间距、再收行高：任何情况下 totalH 都不会超过 rowsArea，
        // 于是行永远不会压到底部的执行台 / 旁白上（旧写法把 blockH 抬到 56 又保底 gap，反而整体溢出）。
        float blockH = Mathf.Min((rowsArea - gap * (rows - 1)) / rows, 122f);
        if (blockH < 56f)
        {
            gap = rows > 1 ? 6f : 0f;
            blockH = Mathf.Max(34f, (rowsArea - gap * (rows - 1)) / rows);
        }
        float totalH = rows * blockH + gap * (rows - 1);
        float firstTop = -headerH - 10f - Mathf.Max(0f, (rowsArea - totalH) * 0.5f);

        // 左侧标签列：徽章 / 类型名牌 / 变量名，窗口窄时按比例收
        float leftW = Mathf.Min(330f, Mathf.Max(210f, availW * 0.44f));
        float badgeW = leftW >= 300f ? 54f : 42f;
        float typeW = Mathf.Clamp(leftW * 0.30f, 62f, 98f);
        float nameW = Mathf.Max(60f, leftW - badgeW - typeW - 6f);
        float cellX = badgeW + typeW + nameW + 8f;

        int maxBytes = 1;
        for (int i = 0; i < reveal; i++)
            if (scene.Vars[i] != null)
                maxBytes = Mathf.Max(maxBytes, Mathf.Clamp(CountBytes(scene.Vars[i]), 1, MemCellPerRow));
        float span = Mathf.Max(100f, availW - cellX - 4f);
        float cellW = Mathf.Clamp((span - MemCellGapX * (maxBytes - 1)) / maxBytes, 16f, 88f);
        // 格数多到摆不下时，先让变量名列让出宽度（名字可以截短，格子不能被挤出面板）
        float needW = cellX + cellW * maxBytes + MemCellGapX * (maxBytes - 1);
        if (needW > availW - 4f && nameW > 90f)
        {
            nameW -= Mathf.Min(nameW - 90f, needW - (availW - 4f));
            cellX = badgeW + typeW + nameW + 8f;
            span = Mathf.Max(100f, availW - cellX - 4f);
            cellW = Mathf.Clamp((span - MemCellGapX * (maxBytes - 1)) / maxBytes, 16f, 88f);
        }

        // 行说明搬到了字节格右侧：先给它留出 300 的宽度，格子太宽时先收格子（格子最小 30 仍放得下 4 位十六进制）
        float noteReserve = zh ? 300f : 400f;   // 英文行说明比中文长，右侧多留点宽度
        bool anyNote = false;
        for (int i = 0; i < reveal; i++)
            if (scene.Vars[i] != null && !string.IsNullOrEmpty(mNoteText(scene.Vars[i], zh)))
                anyNote = true;
        if (anyNote)
        {
            float cellByNote = (availW - cellX - 12f - noteReserve - MemCellGapX * (maxBytes - 1)) / maxBytes;
            if (cellByNote >= 30f)
                cellW = Mathf.Min(cellW, cellByNote);
        }
        // 行说明统一从「最宽那一行的格子右边缘」开始，几行说明才会左对齐成一条竖线，不会参差不齐
        float noteX = cellX + maxBytes * (cellW + MemCellGapX) + 12f;

        // 先算好每一步实际要显示的字节（本步 Patch 会覆盖原始值）
        string[][] bytes = new string[reveal][];
        for (int i = 0; i < reveal; i++)
        {
            MemVar mv = scene.Vars[i];
            int cnt = mv == null ? 1 : Mathf.Clamp(CountBytes(mv), 1, MemCellPerRow);
            bytes[i] = new string[cnt];
            string[] parts = mv == null || string.IsNullOrEmpty(mv.Bytes)
                ? new string[0] : mv.Bytes.ToUpperInvariant().Split(' ');
            for (int b = 0; b < cnt; b++)
                bytes[i][b] = b < parts.Length && !string.IsNullOrEmpty(parts[b]) ? parts[b] : "??";
        }
        if (cur.PatchVar != null && cur.PatchBytes != null)
        {
            int pk = Mathf.Min(cur.PatchVar.Length, cur.PatchBytes.Length);
            for (int k = 0; k < pk; k++)
            {
                int vi = cur.PatchVar[k];
                if (vi < 0 || vi >= reveal)
                    continue;
                string[] parts = string.IsNullOrEmpty(cur.PatchBytes[k])
                    ? new string[0] : cur.PatchBytes[k].ToUpperInvariant().Split(' ');
                for (int b = 0; b < bytes[vi].Length; b++)
                    bytes[vi][b] = b < parts.Length && !string.IsNullOrEmpty(parts[b]) ? parts[b] : "??";
            }
        }

        // 行池显隐
        for (int i = 0; i < MemRowPool; i++)
        {
            if (memRows[i] != null && memRows[i].Root != null && memRows[i].Root.gameObject.activeSelf != (i < reveal))
                memRows[i].Root.gameObject.SetActive(i < reveal);
        }

        int addr = 0x100;   // 地址只是示意：真实地址由系统决定，这里从 0x100 起按字节往上数
        for (int vi = 0; vi < reveal; vi++)
        {
            MemRowUI row = memRows[vi];
            MemVar mv = scene.Vars[vi] ?? new MemVar();
            if (row == null || row.Root == null)
                continue;

            int cnt = bytes[vi].Length;
            // 行说明从「格子下方的一行小字」改成「格子右侧的一段大字」：行高不再分给小字，
            // 右侧原本空着的区域正好用来放大字号（14 → 20），再也不会看不清。
            float cellH = Mathf.Max(28f, blockH - 4f);
            float noteLeft = noteX;
            // 英文模式优先用英文文案，没写就回落到中文，绝不出现空白
            string badgeText = mBadgeText(mv, zh);
            string typeText = mTypeText(mv, zh);
            string nameText = mNameText(mv, zh);
            string noteText = mNoteText(mv, zh);
            bool hasNote = !string.IsNullOrEmpty(noteText) && availW - noteLeft - 6f >= 120f;

            row.Root.anchoredPosition = new Vector2(0f, firstTop - vi * (blockH + gap));
            row.Root.sizeDelta = new Vector2(0f, blockH);
            row.Root.localScale = Vector3.one;

            // 整块底：幽灵行（代码里有、内存里没有）给一点深色，让它看起来「虚」
            if (row.FrameBg != null)
                row.FrameBg.color = mv.Ghost
                    ? UITheme.WithAlpha(UITheme.PanelDeep, 0.28f)   // 压淡一点就够：「虚」交给虚线格表达，不牺牲字的可读性
                    : UITheme.WithAlpha(UITheme.PanelDeep, 0f);

            // 徽章
            if (row.Badge != null)
            {
                bool hasBadge = !string.IsNullOrEmpty(badgeText);
                if (row.Badge.gameObject.activeSelf != hasBadge)
                    row.Badge.gameObject.SetActive(hasBadge);
                if (hasBadge)
                {
                    RectTransform br = row.Badge.rectTransform;
                    br.anchorMin = new Vector2(0f, 0f);
                    br.anchorMax = new Vector2(0f, 0f);
                    br.pivot = new Vector2(0f, 0f);
                    br.anchoredPosition = new Vector2(0f, cellH * 0.5f - 10f);
                    br.sizeDelta = new Vector2(badgeW - 8f, 20f);
                    SetLabel(row.Badge, badgeText);
                    row.Badge.color = UITheme.Purple;   // 幽灵行（注释 / 方法定义）文字同样正常亮度，只是格子画成虚线
                }
            }

            // 类型名牌
            if (row.TypeBg != null)
            {
                RectTransform tr = row.TypeBg.rectTransform;
                tr.anchorMin = new Vector2(0f, 0f);
                tr.anchorMax = new Vector2(0f, 0f);
                tr.pivot = new Vector2(0f, 0f);
                tr.anchoredPosition = new Vector2(badgeW, cellH * 0.5f - 13f);
                tr.sizeDelta = new Vector2(typeW - 8f, 26f);
                row.TypeBg.color = UITheme.WithAlpha(UITheme.Accent, mv.Ghost ? 0.10f : 0.16f);
            }
            if (row.TypeText != null)
            {
                SetLabel(row.TypeText, typeText ?? "");
                row.TypeText.color = UITheme.Accent;
            }

            // 变量名
            if (row.NameText != null)
            {
                RectTransform nr2 = row.NameText.rectTransform;
                nr2.anchorMin = new Vector2(0f, 0f);
                nr2.anchorMax = new Vector2(0f, 0f);
                nr2.pivot = new Vector2(0f, 0f);
                nr2.anchoredPosition = new Vector2(badgeW + typeW, cellH * 0.5f - 12f);
                nr2.sizeDelta = new Vector2(nameW, 24f);
                SetLabel(row.NameText, nameText ?? "");
                row.NameText.color = UITheme.TextPrimary;
            }

            // 字节格
            for (int b = 0; b < MemCellPerRow; b++)
            {
                bool show = b < cnt;
                if (row.CellRt[b] == null)
                    continue;
                if (row.CellRt[b].gameObject.activeSelf != show)
                    row.CellRt[b].gameObject.SetActive(show);
                if (!show)
                    continue;

                Vector2 pos = new Vector2(cellX + b * (cellW + MemCellGapX), 0f);
                row.CellPos[b] = pos;
                row.CellRt[b].anchoredPosition = pos;
                row.CellRt[b].sizeDelta = new Vector2(cellW, cellH);
                row.CellRt[b].localScale = Vector3.one;

                string hex = bytes[vi][b];
                bool unknown = hex == "??";
                if (row.CellAddr[b] != null)
                    row.CellAddr[b].text = mv.Ghost ? "—" : "0x" + (addr + b).ToString("X3");
                if (row.CellVal[b] != null)
                {
                    SetLabel(row.CellVal[b], unknown ? (mv.Ghost ? "--" : "??") : hex);
                    row.CellVal[b].color = mv.Ghost ? UITheme.TextBody
                        : (unknown ? UITheme.WithAlpha(UITheme.TextDim, 0.6f) : UITheme.TextPrimary);
                }
                if (row.CellBg[b] != null)
                    row.CellBg[b].color = mv.Ghost
                        ? UITheme.WithAlpha(UITheme.TextDim, 0.10f)
                        : UITheme.WithAlpha(UITheme.PanelRaised, 0.92f);
            }
            if (!mv.Ghost)
                addr += cnt;

            // 行说明：贴在字节格右侧、垂直居中，字号 20（自适应最多 20 / 最少 13）
            if (row.NoteText != null)
            {
                if (row.NoteText.gameObject.activeSelf != hasNote)
                    row.NoteText.gameObject.SetActive(hasNote);
                if (hasNote)
                {
                    RectTransform nrt = row.NoteText.rectTransform;
                    nrt.anchorMin = new Vector2(0f, 0f);
                    nrt.anchorMax = new Vector2(1f, 0f);
                    nrt.pivot = new Vector2(0f, 0f);
                    nrt.anchoredPosition = new Vector2(noteLeft, 2f);
                    nrt.sizeDelta = new Vector2(-(noteLeft + 6f), Mathf.Max(24f, cellH - 4f));
                    SetLabel(row.NoteText, noteText);
                    row.NoteText.color = UITheme.TextSub;
                }
            }
        }

        // 顶部说明与步数
        // 「一格 = 1 字节 / 小端」这张「怎么读这张图」的说明只挂在 Task 2（变量与类型）：
        // 那一步第一次把 int / char / bool / float 摆上台，说明条正好对上眼前的格子；
        // 其余任务收起，读法已经记住了，再挂一遍只是白占一行高度、把字节行挤窄。
        if (memHeaderText != null)
        {
            if (memHeaderText.gameObject.activeSelf != showMemHeader)
                memHeaderText.gameObject.SetActive(showMemHeader);
            if (showMemHeader)
            {
                SetLabel(memHeaderText, zh
                    ? "一格 = 1 字节；int 占 4 格、char 占 1 格，地址从 0x100 起递增。\n3 写成 0x00000003，小端存放 = 低字节在低地址，所以从 0x100 往高读是 03 00 00 00"
                    : "1 box = 1 byte. int = 4 boxes, char = 1, addresses grow from 0x100.\n3 is 0x00000003; little-endian keeps the low byte at the low address, so reading up from 0x100 gives 03 00 00 00.");
            }
        }
        if (memFootText != null)
            SetLabel(memFootText, (zh ? "第 " : "Step ") + (n + 1) + "/" + scene.Steps.Length + (zh ? " 步" : ""));
        if (memExprText != null)
        {
            // 代码行里的注释同样跟语言走：英文模式优先用 ExprEn，没写才回落到中文原行
            string expr = (!zh && !string.IsNullOrEmpty(cur.ExprEn)) ? cur.ExprEn : cur.Expr;
            SetLabel(memExprText, string.IsNullOrEmpty(expr) ? "" : "▶  " + expr);
        }
        if (memNoteText != null)
            SetLabel(memNoteText, zh ? cur.Zh : cur.En);
        RefreshMemKeyHint(zh, scene.Steps.Length);
    }

    /// <summary>
    /// 执行台右侧的键位提示：多步任务才提示「按下 ↓ 观看下一个」；
    /// 玩家点回代码区（输入框握住焦点）之后，↓ 归代码用，提示改成「↓ 代码模式」，绝不抢键。
    /// </summary>
    private void RefreshMemKeyHint(bool zh, int stepCount)
    {
        if (memKeyHintText == null)
            return;

        bool codeMode = editor != null && editor.IsOpen && editor.IsCodeInputFocused;
        string s;
        if (stepCount <= 1)
            s = string.Empty;
        else if (codeMode)
            s = zh ? "↓ 代码模式（移动光标）" : "↓ caret mode";
        else
            s = zh ? "↑↓ 看步骤   ←→ 翻页" : "↑↓ steps   ←→ pages";

        bool show = !string.IsNullOrEmpty(s);
        if (s == memKeyHintCache && memKeyHintText.gameObject.activeSelf == show)
            return;
        memKeyHintCache = s;
        if (memKeyHintText.gameObject.activeSelf != show)
            memKeyHintText.gameObject.SetActive(show);
        if (show)
        {
            SetLabel(memKeyHintText, s);
            memKeyHintText.color = codeMode ? UITheme.TextMuted : UITheme.Accent;
        }
    }

    /// <summary>
    /// 内存页的手动步进：↓ 下一步、↑ 上一步（到底 / 到顶循环）。
    /// 只在「代码输入框没有焦点」时归教学卡用；玩家一旦点回代码区，本方法直接返回 ——
    /// 上下键完全交回代码编辑器移动光标，不会被拿去看动画。
    /// </summary>
    private void HandleMemStepKey()
    {
        if (!Active || finished || tasks == null)
            return;
        if (guidePage < 2)
            return;
        int index = CurrentIndex;
        if (!HasMemPage(index))
            return;
        if (editor == null || !editor.IsOpen)
            return;
        if (editor.IsCodeInputFocused)
            return;                      // 代码区握着焦点：上下键是光标，不是翻页

        bool next = Input.GetKeyDown(KeyCode.DownArrow);
        bool prev = Input.GetKeyDown(KeyCode.UpArrow);
        if (!next && !prev)
            return;

        int count = tasks[index].Mem.Steps.Length;
        if (count <= 1)
            return;

        // ↓ 往后、↑ 往前，都在首尾循环，不用记「到底了没」
        animStep = next ? (animStep + 1) % count : (animStep - 1 + count) % count;
        animTimer = 0f;                  // 手动翻过之后重新计时，别刚翻完又被自动推进
        RebuildMemStage(index, animStep);
        AnimateMemStage(1f, index, animStep);
    }

    /// <summary>
    /// 补间：新出现的一段从右侧滑进来，正在被写入的那几个字节脉动发光 ——
    /// 让「内存里多出了一段」「这一格的值被改写了」这两件事都是看得见的。
    /// 全程只改颜色 / 位置 / 缩放，不碰文字，开销极小。
    /// </summary>
    private void AnimateMemStage(float t, int index, int step)
    {
        if (!HasMemPage(index) || memRoot == null || memRows == null || tasks[index].Mem == null)
            return;

        MemScene scene = tasks[index].Mem;
        int n = Mathf.Clamp(step, 0, scene.Steps.Length - 1);
        MemStep cur = scene.Steps[n];
        int reveal = Mathf.Clamp(cur.Reveal, 0, Mathf.Min(scene.Vars.Length, MemRowPool));
        int prevReveal = n > 0 ? Mathf.Clamp(scene.Steps[n - 1].Reveal, 0, Mathf.Min(scene.Vars.Length, MemRowPool)) : 0;

        float fade = Mathf.Clamp01(t * MemStepInterval / Mathf.Max(0.05f, MemFadeTime));
        fade = fade * fade * (3f - 2f * fade);                       // smoothstep：起步柔和、落位干脆
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.2f);

        for (int vi = 0; vi < reveal; vi++)
        {
            MemRowUI row = memRows[vi];
            if (row == null || row.Root == null || !row.Root.gameObject.activeSelf)
                continue;

            bool isNew = vi >= prevReveal;
            if (row.Root != null)
                row.Root.anchoredPosition = new Vector2(isNew ? (1f - fade) * 34f : 0f, row.Root.anchoredPosition.y);

            bool writing = vi == cur.Write;
            int cnt = row.CellRt == null ? 0 : row.CellRt.Length;
            for (int b = 0; b < cnt; b++)
            {
                if (row.CellRt[b] == null || !row.CellRt[b].gameObject.activeSelf)
                    continue;
                if (row.CellBg[b] == null)
                    continue;
                if (writing)
                {
                    float glow = 0.16f + 0.30f * pulse;
                    row.CellBg[b].color = UITheme.WithAlpha(UITheme.Accent, glow);
                    if (row.CellVal[b] != null)
                        row.CellVal[b].color = UITheme.AccentSoft;
                }
                else
                {
                    MemVar mv = scene.Vars[vi];
                    bool ghost = mv != null && mv.Ghost;
                    row.CellBg[b].color = ghost
                        ? UITheme.WithAlpha(UITheme.TextDim, 0.10f)
                        : UITheme.WithAlpha(UITheme.PanelRaised, 0.92f);
                    if (row.CellVal[b] != null)
                        row.CellVal[b].color = ghost ? UITheme.TextBody : UITheme.TextPrimary;
                }
            }
        }
    }

    /// <summary>第 3 页（内存页）每帧驱动：按真实时间推进步骤，并做补间；编辑器打开时 timeScale=0，所以用 unscaledDeltaTime。</summary>
    private void AnimMemFrame()
    {
        int index = CurrentIndex;
        if (!HasMemPage(index) || memRoot == null)
            return;

        MemScene scene = tasks[index].Mem;
        int count = scene.Steps.Length;
        if (count <= 0)
            return;

        // 不自动播放：步骤只由玩家按 ↓ 手动推进（HandleMemStepKey）。
        // 这里只让计时器跑到满格后停住，好让「新行滑入 / 写入字节脉动」这一段补间自己播完一遍。
        animTimer = Mathf.Min(animTimer + Time.unscaledDeltaTime, MemStepInterval);

        int step = Mathf.Clamp(animStep, 0, count - 1);
        float h = memRoot.rect.height;
        if (memBuiltIndex != index || memBuiltStep != step || Mathf.Abs(memBuiltH - h) > 1f)
            RebuildMemStage(index, step);

        AnimateMemStage(Mathf.Clamp01(animTimer / MemStepInterval), index, step);

        // 焦点会在「代码区 ↔ 教学卡」之间随时切换，键位提示跟着变：
        // 在代码区就写「↓ 代码模式」，在教学卡上才写「按下 ↓ 观看下一个」。
        RefreshMemKeyHint(Localization.IsChinese, count);
    }

    // ================== 第 3 页：逻辑图舞台（方框 + 箭头） ==================
    // 与内存页同一套套路：所有方框 / 连线一次建好塞进池子，播放时只改颜色 / 文字 / 角度，
    // 绝不每帧新建 UI。窄卡自适应靠的是「位置与尺寸全部按当前可用宽高归一化再夹住」。

    private static Sprite arrowSprite;   // 箭头三角（朝右的小贴图，用时按连线方向旋转）
    private static Sprite dashSprite;    // 虚线贴片（Tiled 平铺出来就是一段一段的）

    /// <summary>箭头：16×16 的小三角贴图，用时按连线方向旋转。</summary>
    private static Sprite ArrowSprite()
    {
        if (arrowSprite == null)
        {
            const int n = 16;
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float half = (n - 1) * 0.5f;
                    float ty = Mathf.Abs(y - half) / half;     // 0 = 中线，1 = 上下边缘
                    float tx = x / (float)(n - 1);             // 0 = 尾，1 = 尖
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, tx >= ty ? 1f : 0f));
                }
            }
            tex.Apply();
            arrowSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f));
        }
        return arrowSprite;
    }

    /// <summary>虚线贴片：24×6，6 像素一段明暗交替；Image 用 Tiled 平铺即得虚线。</summary>
    private static Sprite DashSprite()
    {
        if (dashSprite == null)
        {
            const int w = 24;
            const int h = 6;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, (x / 6) % 2 == 0 ? 1f : 0f));
            }
            tex.Apply();
            dashSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
        }
        return dashSprite;
    }

    /// <summary>方框底色：同一种色调在全课里含义一致，玩家第二次看图不用再解释。</summary>
    private static Color DiaToneFill(DiaTone tone, bool hot)
    {
        switch (tone)
        {
            case DiaTone.Alert: return UITheme.WithAlpha(UITheme.Alert, hot ? 0.34f : 0.15f);
            case DiaTone.Value: return UITheme.WithAlpha(UITheme.Value, hot ? 0.34f : 0.13f);
            case DiaTone.Purple: return UITheme.WithAlpha(UITheme.Purple, hot ? 0.36f : 0.15f);
            case DiaTone.Accent: return UITheme.WithAlpha(UITheme.Accent, hot ? 0.42f : 0.18f);
            default: return hot ? UITheme.WithAlpha(UITheme.Accent, 0.20f) : UITheme.WithAlpha(UITheme.PanelRaised, 0.86f);
        }
    }

    /// <summary>连线颜色。</summary>
    private static Color DiaToneLine(DiaTone tone, bool hot)
    {
        switch (tone)
        {
            case DiaTone.Alert: return hot ? UITheme.AlertBright : UITheme.Alert;
            case DiaTone.Value: return hot ? UITheme.ValueBright : UITheme.Value;
            case DiaTone.Purple: return hot ? UITheme.Purple : UITheme.Purple;
            default: return hot ? UITheme.Accent : UITheme.HairlineStrong;
        }
    }

    /// <summary>方框正文颜色。</summary>
    private static Color DiaToneText(DiaTone tone, bool hot)
    {
        switch (tone)
        {
            case DiaTone.Alert: return hot ? UITheme.AlertBright : UITheme.TextBody;
            case DiaTone.Value: return hot ? UITheme.ValueBright : UITheme.TextBody;
            case DiaTone.Purple: return hot ? UITheme.Purple : UITheme.TextBody;
            default: return hot ? UITheme.TextPrimary : UITheme.TextSub;
        }
    }

    /// <summary>
    /// 建逻辑图舞台：顶部一行「怎么读这张图」+ 中间整块画布（箭头在底层、方框在上层）
    /// + 底部「第几步」与旁白。整块正文区都归舞台，子件一次建好，播放时只改颜色 / 文字 / 角度。
    /// </summary>
    private void BuildDiagramStage(RectTransform panelRt)
    {
        RectTransform root = CreateRect("DiagramStage", panelRt, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.offsetMin = new Vector2(MemStageSideInset, MemStageBottomInset);
        root.offsetMax = new Vector2(-MemStageSideInset, -MemStageTopInset);
        diaRoot = root;

        // 连线层先建：同级里「后建的画在上面」，于是箭头永远压在方框底下，不会盖住字
        RectTransform edgeLayer = CreateRect("DiaEdges", root, Vector2.zero, Vector2.zero,
            new Vector2(0f, 0f), new Vector2(0f, 0f));
        edgeLayer.anchorMin = Vector2.zero;
        edgeLayer.anchorMax = Vector2.one;
        edgeLayer.pivot = new Vector2(0.5f, 0.5f);
        edgeLayer.offsetMin = Vector2.zero;
        edgeLayer.offsetMax = Vector2.zero;
        diaEdges = new DiaEdgeUI[DiaEdgePool];
        for (int i = 0; i < DiaEdgePool; i++)
            diaEdges[i] = BuildDiaEdge(edgeLayer, i);

        RectTransform nodeLayer = CreateRect("DiaNodes", root, Vector2.zero, Vector2.zero,
            new Vector2(0f, 0f), new Vector2(0f, 0f));
        nodeLayer.anchorMin = Vector2.zero;
        nodeLayer.anchorMax = Vector2.one;
        nodeLayer.pivot = new Vector2(0.5f, 0.5f);
        nodeLayer.offsetMin = Vector2.zero;
        nodeLayer.offsetMax = Vector2.zero;
        diaNodes = new DiaNodeUI[DiaNodePool];
        for (int i = 0; i < DiaNodePool; i++)
            diaNodes[i] = BuildDiaNode(nodeLayer, i);

        // ---- 顶部：一句「这张图怎么读」 ----
        diaHeaderText = CreateText("DiaHeader", root, "", 15, UITheme.TextMuted, TextAnchor.MiddleLeft);
        RectTransform hdr = diaHeaderText.rectTransform;
        hdr.anchorMin = new Vector2(0f, 1f);
        hdr.anchorMax = new Vector2(1f, 1f);
        hdr.pivot = new Vector2(0f, 1f);
        hdr.anchoredPosition = Vector2.zero;
        hdr.sizeDelta = new Vector2(-16f, DiaHeaderH);
        diaHeaderText.horizontalOverflow = HorizontalWrapMode.Wrap;
        diaHeaderText.verticalOverflow = VerticalWrapMode.Truncate;
        diaHeaderText.resizeTextForBestFit = false;
        diaHeaderText.lineSpacing = 1.12f;

        // ---- 底部：旁白（最底） ----
        RectTransform noteRt = CreateRect("DiaNoteRt", root, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, DiaNoteH), new Vector2(0.5f, 1f));
        noteRt.anchorMin = new Vector2(0f, 1f);
        noteRt.anchorMax = new Vector2(1f, 1f);
        noteRt.pivot = new Vector2(0.5f, 1f);
        noteRt.anchoredPosition = new Vector2(0f, 0f);   // 真实位置由 PlaceDiaBottom 按内容高度摆
        noteRt.sizeDelta = new Vector2(0f, DiaNoteH);
        diaNoteRt = noteRt;

        diaNoteText = CreateText("DiaNote", noteRt, "", 20, UITheme.TextBody, TextAnchor.UpperLeft);
        diaNoteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        diaNoteText.verticalOverflow = VerticalWrapMode.Truncate;
        diaNoteText.lineSpacing = 1.08f;
        diaNoteText.resizeTextForBestFit = true;
        diaNoteText.resizeTextMinSize = 15;
        diaNoteText.resizeTextMaxSize = 20;

        // ---- 底部：底条（左边「第几步」、右边键位提示），与内存页的执行台同高同位 ----
        RectTransform barRt = CreateRect("DiaBarBg", root, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, MemExprH), new Vector2(0.5f, 1f));
        barRt.anchorMin = new Vector2(0f, 1f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(0.5f, 1f);
        barRt.anchoredPosition = new Vector2(0f, 0f);
        barRt.sizeDelta = new Vector2(0f, MemExprH);
        diaBarRt = barRt;
        Image barBg = barRt.gameObject.AddComponent<Image>();
        barBg.sprite = WhiteSprite();
        barBg.color = UITheme.WithAlpha(UITheme.CodeBg, 0.96f);
        barBg.raycastTarget = false;

        diaExprText = CreateText("DiaBar", barRt, "", 18, UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform er = diaExprText.rectTransform;
        er.anchorMin = Vector2.zero;
        er.anchorMax = Vector2.one;
        er.offsetMin = new Vector2(14f, 0f);
        er.offsetMax = new Vector2(-300f, 0f);
        diaExprText.horizontalOverflow = HorizontalWrapMode.Wrap;
        diaExprText.verticalOverflow = VerticalWrapMode.Truncate;
        diaExprText.resizeTextForBestFit = true;
        diaExprText.resizeTextMinSize = 11;
        diaExprText.resizeTextMaxSize = 18;

        diaKeyHintText = CreateText("DiaKeyHint", barRt, "", 17, UITheme.TextMuted, TextAnchor.MiddleRight);
        RectTransform kh = diaKeyHintText.rectTransform;
        kh.anchorMin = new Vector2(1f, 0f);
        kh.anchorMax = new Vector2(1f, 1f);
        kh.pivot = new Vector2(1f, 0.5f);
        kh.anchoredPosition = new Vector2(-14f, 0f);
        kh.sizeDelta = new Vector2(290f, 0f);
        diaKeyHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        diaKeyHintText.verticalOverflow = VerticalWrapMode.Truncate;
        diaKeyHintText.resizeTextForBestFit = true;
        diaKeyHintText.resizeTextMinSize = 11;
        diaKeyHintText.resizeTextMaxSize = 17;

        diaRoot.gameObject.SetActive(false);
    }

    /// <summary>建一个方框：底 + 正文 + 可选小注。之后只改内容 / 颜色 / 尺寸。</summary>
    private DiaNodeUI BuildDiaNode(RectTransform parent, int idx)
    {
        DiaNodeUI ui = new DiaNodeUI();

        RectTransform root = CreateRect("DiaNode" + idx, parent, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(180f, 54f), new Vector2(0.5f, 0.5f));
        ui.Root = root;

        Image bg = root.gameObject.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.WithAlpha(UITheme.PanelRaised, 0.86f);
        bg.raycastTarget = false;
        ui.Bg = bg;

        Text text = CreateText("Text", root, "", 15, UITheme.TextSub, TextAnchor.MiddleCenter);
        RectTransform tr = text.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(8f, 6f);
        tr.offsetMax = new Vector2(-8f, -6f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.lineSpacing = 1.02f;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 11;
        text.resizeTextMaxSize = 17;
        ui.Text = text;

        Text desc = CreateText("Desc", root, "", 12, UITheme.TextDim, TextAnchor.MiddleCenter);
        RectTransform dr = desc.rectTransform;
        dr.anchorMin = new Vector2(0f, 0f);
        dr.anchorMax = new Vector2(1f, 0f);
        dr.pivot = new Vector2(0.5f, 0f);
        dr.anchoredPosition = new Vector2(0f, 4f);
        dr.sizeDelta = new Vector2(-10f, 18f);
        desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        desc.verticalOverflow = VerticalWrapMode.Truncate;
        desc.resizeTextForBestFit = true;
        desc.resizeTextMinSize = 10;
        desc.resizeTextMaxSize = 12;
        desc.gameObject.SetActive(false);
        ui.Desc = desc;

        root.gameObject.SetActive(false);
        return ui;
    }

    /// <summary>建一条连线：线身 + 箭头 + 线上的标签。</summary>
    private DiaEdgeUI BuildDiaEdge(RectTransform parent, int idx)
    {
        DiaEdgeUI ui = new DiaEdgeUI();
        ui.HeadSize = new Vector2(13f, 13f);

        RectTransform line = CreateRect("DiaEdge" + idx, parent, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(120f, DiaEdgeThick), new Vector2(0.5f, 0.5f));
        Image lb = line.gameObject.AddComponent<Image>();
        lb.sprite = WhiteSprite();
        lb.color = UITheme.HairlineStrong;
        lb.raycastTarget = false;
        ui.Line = line;
        ui.LineBg = lb;

        RectTransform head = CreateRect("Head" + idx, parent, new Vector2(0f, 1f), Vector2.zero,
            ui.HeadSize, new Vector2(0.5f, 0.5f));
        Image hb = head.gameObject.AddComponent<Image>();
        hb.sprite = ArrowSprite();
        hb.color = UITheme.HairlineStrong;
        hb.raycastTarget = false;
        ui.Head = head;
        ui.HeadBg = hb;

        Text label = CreateText("Label" + idx, parent, "", 12, UITheme.TextDim, TextAnchor.MiddleCenter);
        RectTransform lr = label.rectTransform;
        lr.anchorMin = new Vector2(0f, 1f);
        lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0.5f, 0.5f);
        lr.anchoredPosition = Vector2.zero;
        lr.sizeDelta = new Vector2(180f, 18f);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.gameObject.SetActive(false);
        ui.Label = label;
        ui.LabelRt = lr;

        line.gameObject.SetActive(false);
        head.gameObject.SetActive(false);
        return ui;
    }

    /// <summary>把「底条 + 旁白」摆到内容的自然底部（与内存页同一套算法，拖矮面板时它们顺出去）。</summary>
    private void PlaceDiaBottom(float layoutH)
    {
        float noteTop = layoutH - DiaNoteH;
        if (diaNoteRt != null)
            diaNoteRt.anchoredPosition = new Vector2(0f, -noteTop);
        if (diaBarRt != null)
            diaBarRt.anchoredPosition = new Vector2(0f, -(noteTop - MemNoteGapY - MemExprH));
    }

    /// <summary>
    /// 按当前步骤重排逻辑图：方框摆位（归一化坐标 → 像素，窄卡自动收紧）、连线算角度与长度、
    /// 顶部说明与底部旁白换字。只在步骤变化 / 面板尺寸变化时调用，平时只做颜色补间。
    /// </summary>
    private void RebuildDiagramStage(int index, int step)
    {
        if (!HasDiagramPage(index) || diaRoot == null || diaNodes == null || diaEdges == null)
            return;

        DiagramScene scene = tasks[index].Dia;
        int n = Mathf.Clamp(step, 0, scene.Steps.Length - 1);
        DiaStep cur = scene.Steps[n];
        bool zh = Localization.IsChinese;

        // 刚 SetActive 的那一帧自身 rect 还没算出来：退而用宿主面板尺寸，宁可先按小的排
        RectTransform host = diaRoot.parent as RectTransform;
        float availW = diaRoot.rect.width;
        float availH = diaRoot.rect.height;
        if (availW < 120f)
            availW = (host != null && host.rect.width >= 120f ? host.rect.width : ReferenceWidth) - 52f;
        if (availH < 120f)
            availH = (host != null && host.rect.height >= 120f
                ? host.rect.height
                : SplitHalfHeight()) - GuideBodyTopInset - GuideBodyBottomInset;

        float flowH = Mathf.Max(availH, MemStageBaselineHeight());
        PlaceDiaBottom(flowH);

        float top = DiaHeaderH + 2f;
        float bottomLimit = flowH - (DiaNoteH + MemNoteGapY + MemExprH + 12f);
        float areaH = Mathf.Max(80f, bottomLimit - top);

        int nodeCount = Mathf.Min(scene.Nodes.Length, DiaNodePool);
        Vector2[] centers = new Vector2[nodeCount];
        Vector2[] halves = new Vector2[nodeCount];

        for (int i = 0; i < DiaNodePool; i++)
        {
            DiaNodeUI ui = diaNodes[i];
            if (ui == null || ui.Root == null)
                continue;
            if (i >= nodeCount || scene.Nodes[i] == null)
            {
                if (ui.Root.gameObject.activeSelf)
                    ui.Root.gameObject.SetActive(false);
                continue;
            }

            DiaNode node = scene.Nodes[i];
            string body = !zh && !string.IsNullOrEmpty(node.TextEn) ? node.TextEn : (node.Text ?? string.Empty);
            string desc = !zh && !string.IsNullOrEmpty(node.DescEn) ? node.DescEn : node.Desc;
            bool hasDesc = !string.IsNullOrEmpty(desc);

            float w = node.W > 0f ? node.W * availW : Mathf.Max(DiaNodeMinW, availW * 0.24f);
            w = Mathf.Clamp(w, Mathf.Min(DiaNodeMinW, Mathf.Max(40f, availW * 0.5f)), Mathf.Max(40f, availW - 8f));
            float h = node.H > 0f ? node.H * areaH : (hasDesc ? 76f : 54f);
            h = Mathf.Clamp(h, 40f, Mathf.Max(44f, areaH * 0.36f));

            float cx = Mathf.Clamp(node.X, 0f, 1f) * availW;
            float cy = top + Mathf.Clamp(node.Y, 0f, 1f) * areaH;
            // 贴边不留半个框：把中心夹在自己的半径之内
            float minX = w * 0.5f + 2f;
            float maxX = Mathf.Max(minX, availW - w * 0.5f - 2f);
            float minY = top + h * 0.5f;
            float maxY = Mathf.Max(minY, top + areaH - h * 0.5f);
            cx = Mathf.Clamp(cx, minX, maxX);
            cy = Mathf.Clamp(cy, minY, maxY);

            centers[i] = new Vector2(cx, cy);
            halves[i] = new Vector2(w * 0.5f, h * 0.5f);

            RectTransform rt = ui.Root;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(cx, -cy);
            ui.Home = rt.anchoredPosition;
            ui.Size = rt.sizeDelta;
            if (!rt.gameObject.activeSelf)
                rt.gameObject.SetActive(true);

            SetLabel(ui.Text, body);
            RectTransform tr = ui.Text.rectTransform;
            tr.offsetMin = new Vector2(8f, hasDesc ? 22f : 6f);
            tr.offsetMax = new Vector2(-8f, -6f);

            if (ui.Desc != null)
            {
                if (ui.Desc.gameObject.activeSelf != hasDesc)
                    ui.Desc.gameObject.SetActive(hasDesc);
                if (hasDesc)
                    SetLabel(ui.Desc, desc);
            }
        }

        int edgeCount = scene.Edges == null ? 0 : Mathf.Min(scene.Edges.Length, DiaEdgePool);
        for (int i = 0; i < DiaEdgePool; i++)
        {
            DiaEdgeUI ui = diaEdges[i];
            if (ui == null || ui.Line == null)
                continue;

            bool show = false;
            if (i < edgeCount && scene.Edges[i] != null)
            {
                DiaEdge e = scene.Edges[i];
                if (e.From >= 0 && e.From < nodeCount && e.To >= 0 && e.To < nodeCount && e.From != e.To
                    && scene.Nodes[e.From] != null && scene.Nodes[e.To] != null)
                {
                    show = LayoutDiaEdge(ui, e, centers[e.From], halves[e.From], centers[e.To], halves[e.To]);
                    if (show)
                    {
                        string lb = !zh && !string.IsNullOrEmpty(e.LabelEn) ? e.LabelEn : e.Label;
                        bool hasLabel = !string.IsNullOrEmpty(lb);
                        if (ui.Label.gameObject.activeSelf != hasLabel)
                            ui.Label.gameObject.SetActive(hasLabel);
                        if (hasLabel)
                            SetLabel(ui.Label, lb);
                    }
                }
            }

            if (ui.Line.gameObject.activeSelf != show)
                ui.Line.gameObject.SetActive(show);
            bool showHead = show && scene.Edges[i] != null && scene.Edges[i].Arrow;
            if (ui.Head.gameObject.activeSelf != showHead)
                ui.Head.gameObject.SetActive(showHead);
            bool showLabel = show && ui.Label != null && ui.Label.gameObject.activeSelf;
            if (!show && ui.Label != null && ui.Label.gameObject.activeSelf)
                ui.Label.gameObject.SetActive(false);
            if (showLabel)
                ui.Label.gameObject.SetActive(true);
        }

        if (diaHeaderText != null)
        {
            string hint = !zh && !string.IsNullOrEmpty(scene.HintEn) ? scene.HintEn : scene.Hint;
            if (string.IsNullOrEmpty(hint))
                hint = zh ? "方框 = 概念，箭头 = 数据或控制的流向；按 ↓ 一步步点亮"
                          : "boxes = concepts, arrows = flow of data or control; press ↓ to step";
            SetLabel(diaHeaderText, hint);
        }
        if (diaExprText != null)
            SetLabel(diaExprText, zh
                ? "第 " + (n + 1) + " / " + scene.Steps.Length + " 步"
                : "Step " + (n + 1) + " / " + scene.Steps.Length);
        if (diaNoteText != null)
            SetLabel(diaNoteText, zh ? cur.Zh : cur.En);

        RefreshDiaKeyHint(zh, scene.Steps.Length);

        diaBuiltIndex = index;      // 记下这次排的是哪个任务的第几步、按多高排的：
        diaBuiltStep = n;           // 三者任一变化才重排，其余帧只跑颜色补间
        diaBuiltH = diaRoot.rect.height;
    }

    /// <summary>
    /// 算一条连线的几何：从起点框中心朝终点走，撞到框边才起笔，落到终点框边前收笔，
    /// 于是箭头永远贴在两框之间，不会钻进框里压住文字。坐标用「从左上角往下量」，与方框摆位同一套。
    /// </summary>
    private bool LayoutDiaEdge(DiaEdgeUI ui, DiaEdge edge, Vector2 a, Vector2 ha, Vector2 b, Vector2 hb)
    {
        Vector2 d = b - a;
        float dist = d.magnitude;
        if (dist < 8f)
            return false;

        Vector2 dir = d / dist;
        float ax = Mathf.Max(0.001f, Mathf.Abs(dir.x));
        float ay = Mathf.Max(0.001f, Mathf.Abs(dir.y));
        float ta = Mathf.Min(ha.x / ax, ha.y / ay);    // 从 a 中心到 a 框边的距离
        float tb = Mathf.Min(hb.x / ax, hb.y / ay);    // 从 b 中心到 b 框边的距离

        Vector2 start = a + dir * (ta + 3f);
        Vector2 end = b - dir * (tb + 3f);
        Vector2 seg = end - start;
        float segLen = seg.magnitude;
        if (segLen < 6f)
            return false;

        // 屏幕坐标（y 向下）→ 本地坐标（y 向上）：只翻 y
        Vector2 localStart = new Vector2(start.x, -start.y);
        Vector2 localEnd = new Vector2(end.x, -end.y);
        Vector2 delta = localEnd - localStart;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Vector2 mid = (localStart + localEnd) * 0.5f;
        ui.Angle = angle;
        ui.Home = mid;
        ui.Size = new Vector2(segLen, edge.Dashed ? DiaEdgeThick * 0.8f : DiaEdgeThick);
        ui.Line.sizeDelta = ui.Size;
        ui.Line.anchoredPosition = mid;
        ui.Line.localEulerAngles = new Vector3(0f, 0f, angle);

        if (ui.Head != null)
        {
            float half = ui.HeadSize.x * 0.5f;
            Vector2 headPos = localEnd - (delta / Mathf.Max(0.001f, segLen)) * half;
            ui.Head.sizeDelta = ui.HeadSize;
            ui.Head.anchoredPosition = headPos;
            ui.Head.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        if (ui.LabelRt != null)
        {
            // 标签挂在线上侧，避免压在箭头上；竖直的线也能自动挪到左右
            Vector2 perp = new Vector2(-dir.y, dir.x);
            if (perp.y > 0f)
                perp = -perp;
            Vector2 lb = mid + new Vector2(perp.x * 14f, -perp.y * 14f);
            ui.LabelRt.anchoredPosition = lb;
        }
        return true;
    }

    /// <summary>
    /// 补间：本步点亮的方框亮起来并轻微呼吸，其余压暗；点亮的连线变亮，未强调的退成暗色。
    /// 全程只改颜色 / 缩放，不碰文字，开销极小。
    /// </summary>
    private void AnimateDiagramStage(float t, int index, int step)
    {
        if (!HasDiagramPage(index) || diaRoot == null || diaNodes == null || diaEdges == null)
            return;

        DiagramScene scene = tasks[index].Dia;
        int n = Mathf.Clamp(step, 0, scene.Steps.Length - 1);
        DiaStep cur = scene.Steps[n];

        float fade = Mathf.Clamp01(t * MemStepInterval / Mathf.Max(0.05f, MemFadeTime));
        fade = fade * fade * (3f - 2f * fade);                       // smoothstep：起步柔和、落位干脆
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.2f);

        int nodeCount = Mathf.Min(scene.Nodes.Length, DiaNodePool);
        for (int i = 0; i < nodeCount; i++)
        {
            DiaNodeUI ui = diaNodes[i];
            if (ui == null || ui.Root == null || !ui.Root.gameObject.activeSelf)
                continue;
            DiaNode node = scene.Nodes[i];
            if (node == null)
                continue;

            bool hot = cur.Hot != null && System.Array.IndexOf(cur.Hot, i) >= 0;
            if (ui.Bg != null)
            {
                Color fill = DiaToneFill(node.Tone, hot);
                if (hot && node.Tone == DiaTone.Panel)
                    fill = UITheme.WithAlpha(UITheme.Accent, 0.16f + 0.12f * pulse);
                ui.Bg.color = fill;
            }
            if (ui.Text != null)
                ui.Text.color = DiaToneText(node.Tone, hot);
            if (ui.Desc != null && ui.Desc.gameObject.activeSelf)
                ui.Desc.color = hot ? UITheme.TextSub : UITheme.TextDim;

            // 点亮的一瞬间轻轻放大，让「轮到它了」这件事看得见；落位后回到原尺寸，字不会糊
            float scale = hot ? 1f + 0.02f * (1f - fade) * pulse * 2f : 1f;
            ui.Root.localScale = new Vector3(scale, scale, 1f);
        }

        int edgeCount = scene.Edges == null ? 0 : Mathf.Min(scene.Edges.Length, DiaEdgePool);
        for (int i = 0; i < edgeCount; i++)
        {
            DiaEdgeUI ui = diaEdges[i];
            if (ui == null || ui.Line == null || !ui.Line.gameObject.activeSelf)
                continue;
            DiaEdge e = scene.Edges[i];
            if (e == null)
                continue;

            bool hot = cur.HotEdge != null && System.Array.IndexOf(cur.HotEdge, i) >= 0;
            Color c = DiaToneLine(e.Tone, hot);
            Sprite want = e.Dashed ? DashSprite() : WhiteSprite();
            if (ui.LineBg != null)
            {
                ui.LineBg.color = hot ? c : UITheme.WithAlpha(c, 0.5f);
                if (ui.LineBg.sprite != want)
                {
                    ui.LineBg.sprite = want;
                    ui.LineBg.type = e.Dashed ? Image.Type.Tiled : Image.Type.Simple;
                }
            }
            if (ui.HeadBg != null)
                ui.HeadBg.color = hot ? c : UITheme.WithAlpha(c, 0.6f);
            if (ui.Label != null && ui.Label.gameObject.activeSelf)
                ui.Label.color = hot ? UITheme.TextPrimary : UITheme.TextDim;
        }
    }

    /// <summary>逻辑图页的键位提示：与内存页同一套文案与规则（代码区握焦点时让开 ↓）。</summary>
    private void RefreshDiaKeyHint(bool zh, int stepCount)
    {
        if (diaKeyHintText == null)
            return;

        bool codeMode = editor != null && editor.IsOpen && editor.IsCodeInputFocused;
        string s;
        if (stepCount <= 1)
            s = string.Empty;
        else if (codeMode)
            s = zh ? "↓ 代码模式（移动光标）" : "↓ caret mode";
        else
            s = zh ? "↑↓ 看步骤   ←→ 翻页" : "↑↓ steps   ←→ pages";

        bool show = !string.IsNullOrEmpty(s);
        if (s == diaKeyHintCache && diaKeyHintText.gameObject.activeSelf == show)
            return;
        diaKeyHintCache = s;
        if (diaKeyHintText.gameObject.activeSelf != show)
            diaKeyHintText.gameObject.SetActive(show);
        if (show)
        {
            SetLabel(diaKeyHintText, s);
            diaKeyHintText.color = codeMode ? UITheme.TextMuted : UITheme.Accent;
        }
    }

    /// <summary>
    /// 逻辑图页的手动步进：↓ 下一步、↑ 上一步（到底 / 到顶循环）。
    /// 代码输入框握焦点时直接返回，上下键交回代码编辑器移动光标 —— 与内存页完全一致。
    /// </summary>
    private void HandleDiaStepKey()
    {
        if (!Active || finished || tasks == null)
            return;
        if (guidePage < 2)
            return;
        int index = CurrentIndex;
        if (!HasDiagramPage(index))
            return;
        if (editor == null || !editor.IsOpen)
            return;
        if (editor.IsCodeInputFocused)
            return;

        bool next = Input.GetKeyDown(KeyCode.DownArrow);
        bool prev = Input.GetKeyDown(KeyCode.UpArrow);
        if (!next && !prev)
            return;

        int count = tasks[index].Dia.Steps.Length;
        if (count <= 1)
            return;

        animStep = next ? (animStep + 1) % count : (animStep - 1 + count) % count;
        animTimer = 0f;
        RebuildDiagramStage(index, animStep);
        AnimateDiagramStage(1f, index, animStep);
    }

    /// <summary>逻辑图页每帧驱动：步骤手动推进，这里只负责尺寸变化时重排 + 补间。</summary>
    private void AnimDiaFrame()
    {
        int index = CurrentIndex;
        if (!HasDiagramPage(index) || diaRoot == null)
            return;

        int count = tasks[index].Dia.Steps.Length;
        if (count <= 0)
            return;

        animTimer = Mathf.Min(animTimer + Time.unscaledDeltaTime, MemStepInterval);

        int step = Mathf.Clamp(animStep, 0, count - 1);
        float h = diaRoot.rect.height;
        if (diaBuiltIndex != index || diaBuiltStep != step || Mathf.Abs(diaBuiltH - h) > 1f)
            RebuildDiagramStage(index, step);

        AnimateDiagramStage(Mathf.Clamp01(animTimer / MemStepInterval), index, step);
        RefreshDiaKeyHint(Localization.IsChinese, count);
    }

    /// <summary>
    /// 第 3 页的时间驱动入口。三种第 3 页都从这里走：
    /// 有内存可视化场景（Mem）→ 走图形舞台 AnimMemFrame（它自己管步骤与补间）；
    /// 否则回落到旧的「逐行高亮 + 旁白」。
    /// </summary>
    private void AdvanceAnim()
    {
        if (!Active || finished || guidePage < 2 || tasks == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;

        if (HasMemPage(CurrentIndex))
        {
            HandleMemStepKey();   // 先吃 ↓ 手动步进（玩家在代码区时它自己会让开，把 ↓ 交回代码编辑器）
            AnimMemFrame();
            return;
        }

        if (HasDiagramPage(CurrentIndex))
        {
            HandleDiaStepKey();   // 逻辑图页同样手动步进，↑↓ 的归属规则与内存页一致
            AnimDiaFrame();
            return;
        }

        // 旧的「逐行高亮」页同样改成手动：↓ 一步一步看，绝不偷偷自动播
        HandleAnimStepKey();
    }

    /// <summary>
    /// 旧「逐行高亮」页的手动步进：↓ 下一步、↑ 上一步（首尾循环）。
    /// 只在代码区没握焦点时归教学卡用；玩家点回代码区之后本方法直接返回，上下键完全交回代码编辑器移动光标。
    /// </summary>
    private void HandleAnimStepKey()
    {
        if (!Active || finished || tasks == null || guidePage < 2)
            return;
        int index = CurrentIndex;
        if (index < 0 || index >= tasks.Length)
            return;
        AnimStep[] anim = tasks[index].Anim;
        if (anim == null || anim.Length < 2)
            return;
        if (editor == null || !editor.IsOpen || editor.IsCodeInputFocused)
            return;

        bool next = Input.GetKeyDown(KeyCode.DownArrow);
        bool prev = Input.GetKeyDown(KeyCode.UpArrow);
        if (!next && !prev)
            return;

        animStep = next ? (animStep + 1) % anim.Length : (animStep - 1 + anim.Length) % anim.Length;
        animTimer = 0f;
        RefreshAnimFrame();
    }

    /// <summary>
    /// ← / → 翻页：左退回上一页、右进到下一页（第 3 页的细节页也算一页）。
    /// 代码区握着焦点时不抢键 —— 那时光标要在代码里左右移动。
    /// </summary>
    private void HandlePageNavKey()
    {
        if (!Active || finished || tasks == null)
            return;
        if (CurrentIndex < 0 || CurrentIndex >= tasks.Length)
            return;
        if (editor != null && editor.IsOpen && editor.IsCodeInputFocused)
            return;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            PrevPage();
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            NextPage();
    }

    /// <summary>只重画两栏文案（不动布局 / 字号），够动画用且开销小。</summary>
    private void RefreshAnimFrame()
    {
        if (tasks == null)
            return;
        int index = CurrentIndex;
        if (index < 0 || index >= tasks.Length || !HasAnimPage(index))
            return;

        bool zh = Localization.IsChinese;
        if (bodyText != null)
            bodyText.text = BuildGuideBody(index, zh, BuildAnimGuide(index, zh));
        if (sampleText != null)
            sampleText.text = BuildAnimCode(index, zh);
    }

    private static string mNoteText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.NoteEn)) ? v.NoteEn : v.Note;
    }

    private static string mNameText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.NameEn)) ? v.NameEn : v.Name;
    }

    private static string mTypeText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.TypeEn)) ? v.TypeEn : v.Type;
    }

    private static string mBadgeText(MemVar v, bool zh)
    {
        if (v == null)
            return null;
        return (!zh && !string.IsNullOrEmpty(v.BadgeEn)) ? v.BadgeEn : v.Badge;
    }

    /// <summary>刷新翻页条：该任务有多页时才显示，并按当前页码更新文字与按钮可点状态（index = -1 表示隐藏）。</summary>
    private void RefreshPageNav(int index)
    {
        if (pageNavRoot == null)
            return;

        bool show = !finished && tasks != null && index >= 0 && index < tasks.Length && PageCountOf(index) > 1;
        if (pageNavRoot.activeSelf != show)
            pageNavRoot.SetActive(show);
        if (!show)
            return;

        bool zh = Localization.IsChinese;
        int pageCount = PageCountOf(index);
        SetLabel(pageNoText, zh
            ? "细节 " + (guidePage + 1) + "/" + pageCount
            : "Detail " + (guidePage + 1) + "/" + pageCount);

        if (prevPageBtn != null)
            prevPageBtn.interactable = guidePage > 0;
        if (nextPageBtn != null)
            nextPageBtn.interactable = guidePage < pageCount - 1;
    }

    /// <summary>构建翻页条：‹ 细节 1/2 ›，横向贴在任务名右侧同一行。</summary>
    private void BuildPageNav(Transform panelRt)
    {
        RectTransform navRt = CreateRect("PageNav", panelRt, new Vector2(1f, 1f), Vector2.zero,
            new Vector2(PageNavWidth, 32f), new Vector2(1f, 1f));
        navRt.anchoredPosition = new Vector2(-24f, -90f);
        pageNavRoot = navRt.gameObject;

        // ‹ 上一页：贴翻页条左端
        prevPageBtn = CreateNavButton(navRt, "PrevPage", "‹", TextAnchor.MiddleCenter,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(32f, 30f));
        prevPageBtn.onClick.AddListener(PrevPage);

        // › 下一页：贴翻页条右端
        nextPageBtn = CreateNavButton(navRt, "NextPage", "›", TextAnchor.MiddleCenter,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(32f, 30f));
        nextPageBtn.onClick.AddListener(NextPage);

        // 中间页码：「细节 1/2」
        pageNoText = CreateText("PageNo", navRt, "", 18, UITheme.TextMuted, TextAnchor.MiddleCenter);
        RectTransform pnRt = pageNoText.rectTransform;
        pnRt.anchorMin = new Vector2(0f, 0f);
        pnRt.anchorMax = new Vector2(1f, 1f);
        pnRt.offsetMin = new Vector2(38f, 0f);
        pnRt.offsetMax = new Vector2(-38f, 0f);

        pageNavRoot.SetActive(false);
    }

    /// <summary>
    /// 构建「蓝色分割线」的拖动热区：一条横跨面板、骑在顶部强调线上的透明横带（高 DragHandleHeight），
    /// 上下拖动即调整提示区高度（向上拖 = 提示区变高，向下拖 = 提示区变矮、代码区变高）。
    /// 拖动只改高度、不重算字号：字号保持玩家看到的大小，放不下的部分直接截断。
    /// </summary>
    private void BuildSplitDragHandle(RectTransform panelRt)
    {
        RectTransform handleRt = CreateRect("SplitDragHandle", panelRt, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(0f, DragHandleHeight), new Vector2(0.5f, 0.5f));
        handleRt.anchorMin = new Vector2(0f, 1f);
        handleRt.anchorMax = new Vector2(1f, 1f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.anchoredPosition = new Vector2(0f, 0f); // 跨在强调线上：上方外沿 DragHandleHeight/2 也能抓
        handleRt.sizeDelta = new Vector2(0f, DragHandleHeight);

        Image hit = handleRt.gameObject.AddComponent<Image>();
        hit.sprite = WhiteSprite();
        hit.color = new Color(1f, 1f, 1f, 0f); // 完全透明：视觉上仍是那条 3px 蓝线，但整条都能拖
        hit.raycastTarget = true;

        splitDragHandle = handleRt.gameObject.AddComponent<SplitDragHandle>();
        splitDragHandle.Owner = this;
        splitDragHandle.Hit = hit;
    }

    /// <summary>分割线被拖动：把屏幕像素增量换成 UI 像素，叠加到 manualSplitOffset 后重排（不重算字号）。</summary>
    internal void DragSplitLineBy(float screenDeltaY)
    {
        if (screenDeltaY == 0f)
            return;
        float scale = 1f;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.scaleFactor > 0.0001f)
            scale = canvas.scaleFactor;
        manualSplitOffset += screenDeltaY / scale;
        ApplySplitLayout(); // 拖蓝色长条：只改上下高度，字号恒定（不重排、不缩放）
    }

    /// <summary>鼠标悬停在分割线上时给一点视觉反馈（半透明蓝带），让玩家知道这条线能拖。</summary>
    internal void SetSplitHandleHover(bool on)
    {
        if (splitDragHandle == null || splitDragHandle.Hit == null)
            return;
        splitDragHandle.Hit.color = on
            ? UITheme.WithAlpha(UITheme.Accent, 0.22f)
            : new Color(1f, 1f, 1f, 0f);
        if (accentLine != null)
            accentLine.color = UITheme.WithAlpha(UITheme.Accent, on ? 1f : 0.95f);
    }

    /// <summary>蓝色分割线的拖动把手：只处理「按下拖动 → 回调控制器」，其余交给控制器。</summary>
    private sealed class SplitDragHandle : MonoBehaviour,
        UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler,
        UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public CodeSyntaxTutorialController Owner;
        public Image Hit;

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.SetSplitHandleHover(true);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.DragSplitLineBy(e.delta.y);
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.SetSplitHandleHover(true);
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Owner != null)
                Owner.SetSplitHandleHover(false);
        }
    }

    /// <summary>创建一个翻页按钮（深色底 + 箭头文字，点击回调由调用方挂接）。</summary>
    private Button CreateNavButton(Transform parent, string name, string label, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        RectTransform rt = CreateRect(name, parent, anchor, pos, size, pivot);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.PanelSunk;
        bg.raycastTarget = true;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint; // 可点 / 不可点由 interactable 的着色体现

        Text t = CreateText("Label", rt, label, 20, UITheme.TextPrimary, align);
        Stretch(t.rectTransform);
        return btn;
    }

    /// <summary>进度格悬停高亮：在基准色上提亮，提示这一格可以点击。</summary>
    private void SetChipHover(int index, bool on)
    {
        if (chipBgs == null || chipBaseColors == null)
            return;
        if (index < 0 || index >= chipBgs.Length || chipBgs[index] == null)
            return;

        Color baseColor = chipBaseColors[index];
        chipBgs[index].color = on
            ? new Color(Mathf.Min(1f, baseColor.r * 1.5f + 0.10f),
                        Mathf.Min(1f, baseColor.g * 1.5f + 0.10f),
                        Mathf.Min(1f, baseColor.b * 1.5f + 0.10f),
                        baseColor.a)
            : baseColor;
    }

    private void CompleteCurrentTask()
    {
        if (CurrentIndex < 0 || CurrentIndex >= TaskCount)
            return;

        int completedIndex = CurrentIndex;
        done[completedIndex] = true;
        // 真做过的章节写盘（跨重启 / 跨跳关保留）：进度格显示的是这张表，而不是「序号之前的都算做过」
        GameProgress.SaveTaskDone(DoneKeyOf(CurrentCourse), done);

        // 通关条件 = 每一个 Task 都做过。「走到最后一章」不算（玩家可能点进度格 / /level 跳着做）
        int doneNow = CountDoneTasks();
        int leftNow = Mathf.Max(0, TaskCount - doneNow);
        bool allDone = leftNow <= 0;

        int completed = completedIndex + 1;
        guidePage = 0; // 新任务从第 1 页开始
        ResetAnim();   // 动画也从头开演

        // 编译标记清零 —— 每个任务都必须重新点一次【编译运行 (F6)】
        compiledThisTask = false;
        compiledRaw = "";
        compiledSnapshot = "";
        statusCache = -1;

        flashTimer = allDone ? 3.5f : 2.6f;
        SetLabel(flashText, allDone
            ? Localization.Pick("全部任务完成！", "All tasks done!")
            : Localization.Pick("任务完成 ✔ 还有 " + leftNow + " 个 Task 没做完 —— 点进度格回去补做",
                                "Task done ✔ · " + leftNow + " task(s) left — click a chip to catch up"));

        Debug.Log("[CodeSyntaxTutorial] 任务 " + completed + "/" + TaskCount + " 完成（真做过 " + doneNow + "/" + TaskCount + "）。");

        if (allDone)
        {
            // 全部做完：进入结业状态（FinishAll 里标记通关、显示完成、几秒后回主页面），
            // 并清掉属于本分区的断点，下次从该关开头进入。
            CurrentIndex = TaskCount;
            GameProgress.GetCourseLocation(CurrentCourse, out int clearLevel, out int clearPart);
            GameProgress.ClearResumeIfMatches(clearLevel, clearPart);
            FinishAll();
            return;
        }

        // 还没做满：先往下走一个；若已经走到最后一章但前面有漏做的，退回第一个没做过的任务 ——
        // 让玩家和玩家看到的提示，始终对着「还没做完的那个 Task」，而不是误以为整课已完成。
        CurrentIndex = completedIndex + 1;
        if (CurrentIndex >= TaskCount)
            CurrentIndex = FirstUnfinishedIndex();
        if (CurrentIndex >= TaskCount)
            CurrentIndex = completedIndex; // 兜底：理论上走不到

        // 断点续玩：章节（任务）推进即把「关卡 + 分区 + 章节」写入存档并 Save()
        //   · 语法课（Course.Grammar）= Level 1 - Part 4，API 课（Course.Api）= Level 2 - Part 2；
        //   · CurrentIndex 是 0 基任务序号，存档中的 section 是 1 起章节号，故 +1；
        //   · 整关通关的情况已在上面清掉断点，这里只处理「还有没做完的」。
        GameProgress.GetCourseLocation(CurrentCourse, out int progressLevel, out int progressPart);
        GameProgress.SaveResume(progressLevel, progressPart, CurrentIndex + 1);

        RefreshText();
    }

    /// <summary>真实已完成任务数（看完成表，不看「走到第几个」）。</summary>
    private int CountDoneTasks()
    {
        int n = 0;
        if (done != null)
            for (int i = 0; i < done.Length && i < TaskCount; i++)
                if (done[i])
                    n++;
        return n;
    }

    /// <summary>第一个没做过的任务序号（0 基）；全部做完时返回 TaskCount。</summary>
    private int FirstUnfinishedIndex()
    {
        for (int i = 0; i < TaskCount; i++)
            if (done == null || i >= done.Length || !done[i])
                return i;
        return TaskCount;
    }

    private void FinishAll()
    {
        // 走完最后一章 ≠ 一门课真的做完：玩家可能用进度格 / /level 跳着做，
        // 前面还有没做的 Task 时不能报「完成」、也不能算通关解锁下一关。
        int doneCountNow = CountDoneTasks();
        int leftNow = Mathf.Max(0, TaskCount - doneCountNow);
        bool reallyAll = leftNow == 0;

        finished = true;
        flashTimer = 3.5f;

        if (reallyAll)
        {
            // 逐级解锁：本次课程全部完成即视为整关通关（PlayerPrefs 持久化，跨重启保留）
            //   语法课（Level 1 - Part 4）→ 关卡 1 通关，解锁关卡 2；
            //   API 课（Level 2 - Part 2）→ 关卡 2 通关，解锁关卡 3。
            LevelProgress.MarkLevelCompleted(CurrentCourse == Course.Api ? 2 : 1);

            SetLabel(flashText, Localization.Pick(
                "全部任务完成！正在返回主页面…",
                "All tasks done! Returning to the main menu…"));

            // 完成提示停留几秒再自动回主页（Update 里计时，Time.unscaledDeltaTime 不受暂停影响）
            homeReturnTimer = HomeReturnDelay;
        }
        else
        {
            SetLabel(flashText, Localization.Pick(
                "最后一章完成 · 还有 " + leftNow + " 个 Task 没做完 —— 点进度格回去补做",
                "Last chapter done · " + leftNow + " task(s) still unfinished — click a progress chip to catch up"));
        }

        // 教学收尾：开始生成方块 —— 玩家写的程序开始自动驱动炮台（炮台本体永久不现身，只在原点工作）
        if (tower != null)
            tower.SetVisualVisible(false);
        if (spawner != null)
            spawner.BeginSpawning(0.6f);

        RefreshText();
        SyncVisibility();
        Debug.Log("[CodeSyntaxTutorial] 教学全部完成：开始生成方块，炮台进入自动运行。");
    }

    /// <summary>剥离注释与字符串 / 字符字面量，避免注释或字符串内容造成结构误判。</summary>
    private static string StripCommentsAndStrings(string code)
    {
        if (string.IsNullOrEmpty(code))
            return string.Empty;

        StringBuilder sb = new StringBuilder(code.Length);
        int i = 0;
        int n = code.Length;
        while (i < n)
        {
            char c = code[i];

            // 行注释 // ...
            if (c == '/' && i + 1 < n && code[i + 1] == '/')
            {
                while (i < n && code[i] != '\n')
                    i++;
                continue;
            }

            // 块注释 /* ... */
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(code[i] == '*' && code[i + 1] == '/'))
                    i++;
                i = Mathf.Min(n, i + 2);
                continue;
            }

            // 字符串字面量 "..."
            if (c == '"')
            {
                i++;
                while (i < n && code[i] != '"')
                {
                    if (code[i] == '\\')
                        i++;
                    i++;
                }
                i++;
                sb.Append("\"\"");
                continue;
            }

            // 字符字面量 '...'
            if (c == '\'')
            {
                i++;
                while (i < n && code[i] != '\'')
                {
                    if (code[i] == '\\')
                        i++;
                    i++;
                }
                i++;
                sb.Append("''");
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    // ================== 文案刷新 ==================

    private void RefreshText()
    {
        RefreshTextCore();
        RefreshFonts();
        // 切语言后内存页必须重排：行说明 / 类型名牌都是按语言取的，不重置就会停在旧语言上
        memBuiltIndex = -1;
        memBuiltStep = -1;
        memKeyHintCache = null;
        // 文案变化后重新排版：面板高度仍是固定半屏（不随文案变），只按新文案重算正文自适应字号
        ApplySplitLayout();
    }

    /// <summary>文本创建时是空串，字体按英文（JetBrains Mono）选的；写入中文后必须重选字体，否则中文缺字形。</summary>
    private void RefreshFonts()
    {
        ReloadFont(headlineText);
        ReloadFont(progressText);
        ReloadFont(taskNameText);
        ReloadFont(bodyText);
        ReloadFont(sampleText);
        ReloadFont(flashText);
        ReloadFont(hintText);
        ReloadFont(pageNoText);
        if (chipTexts != null)
        {
            for (int i = 0; i < chipTexts.Length; i++)
                ReloadFont(chipTexts[i]);
        }
    }

    private static void ReloadFont(Text t)
    {
        if (t == null || string.IsNullOrEmpty(t.text))
            return;
        t.font = UIFontHelper.LoadForText(t.text, t.fontSize);
    }

    /// <summary>赋值并同步字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）。</summary>
    private static void SetLabel(Text t, string content)
    {
        if (t == null)
            return;
        t.text = content;
        t.font = UIFontHelper.LoadForText(content, t.fontSize);
    }

    private void RefreshTextCore()
    {
        if (taskNameText == null)
            return;

        bool zh = Localization.IsChinese;

        if (headlineText != null)
        {
            if (CurrentCourse == Course.Api)
            {
                headlineText.text = zh
                    ? "关卡 2 · 第 2 部分 · 炮台 API 调用教学"
                    : "Level 2 · Part 2 · Tower API Tutorial";
            }
            else
            {
                headlineText.text = zh
                    ? "关卡 1 · 第 4 部分 · C# 基础语法教学"
                    : "Level 1 · Part 4 · C# Syntax Tutorial";
            }
        }

        // 任务进度格
        for (int i = 0; i < MaxChips && chipTexts != null; i++)
        {
            if (chipTexts[i] == null)
                continue;
            if (i >= TaskCount)
            {
                if (chipBgs[i] != null && chipBgs[i].gameObject.activeSelf)
                    chipBgs[i].gameObject.SetActive(false);
                continue;
            }
            bool isDone = done != null && i < done.Length && done[i];
            bool isCurrent = !finished && i == CurrentIndex;
            chipTexts[i].text = (i + 1).ToString();
            chipTexts[i].color = isDone
                ? UITheme.ValueBright
                : (isCurrent ? UITheme.TextPrimary : UITheme.TextMuted);
            if (chipBgs[i] != null)
            {
                Color chipColor = isDone
                    ? UITheme.WithAlpha(UITheme.Value, 0.22f)   // 已完成：绿
                    : (isCurrent
                        ? UITheme.WithAlpha(UITheme.Accent, 0.20f) // 当前：蓝
                        : UITheme.PanelSunk); // 未开始：灰
                chipBgs[i].color = chipColor;
                if (chipBaseColors != null)
                    chipBaseColors[i] = chipColor; // 供悬停高亮还原
            }
        }

        // 「当前在第几个任务」与「实际做完几个任务」是两回事：用 /level 直接跳到第 10 个任务时，
        // 前面 9 个并没有做过，因此进度文案一律以真实完成数为准，避免看起来像「前面全做完了」。
        int doneCount = 0;
        if (done != null)
            for (int i = 0; i < done.Length && i < TaskCount; i++)
                if (done[i])
                    doneCount++;

        int leftCount = Mathf.Max(0, TaskCount - doneCount);

        if (progressText != null)
        {
            int shown = Mathf.Min(CurrentIndex + 1, TaskCount);
            if (finished)
            {
                // 走完最后一章 ≠ 每一章都做过：没做满就报「还有几个没做完」，绝不写「全部完成」
                if (leftCount <= 0)
                    progressText.text = zh ? "全部完成" : "All done";
                else if (zh)
                    progressText.text = "最后一章已走完 · 还有 " + leftCount + " 个没做（已完成 " + doneCount + "/" + TaskCount + "）";
                else
                    progressText.text = "Last chapter done · " + leftCount + " left (done " + doneCount + "/" + TaskCount + ")";
            }
            else if (zh)
                progressText.text = "任务 " + shown + "/" + TaskCount + " · 已完成 " + doneCount + "/" + TaskCount;
            else
                progressText.text = "Task " + shown + "/" + TaskCount + " · done " + doneCount + "/" + TaskCount;
        }

        if (finished)
        {
            // 走完最后一章 ≠ 每章都做过（可能是点进度格 / /level 跳过来的），标题也按真实完成数给
            taskNameText.text = leftCount <= 0
                ? (zh ? "✔ 教学完成" : "✔ Tutorial complete")
                : (zh ? "✔ 最后一章已走完 · 还有 " + leftCount + " 个 Task 没做完"
                      : "✔ Last chapter done · " + leftCount + " task(s) still unfinished");
            if (bodyText != null)
            {
                if (!bodyText.gameObject.activeSelf)
                    bodyText.gameObject.SetActive(true);   // 内存页隐藏过，结业文案要能看见
                if (leftCount > 0)
                {
                    // 没做满：不吹「你已经学完全部」，只告诉他怎么把剩下的 Task 做完
                    bodyText.text = zh
                        ? "最后一章做完了，但前面还有 " + leftCount + " 个 Task 没做完（已完成 " + doneCount + "/" + TaskCount + "）。\n"
                          + "这一关的通关条件是「每一个 Task 都做完」，所以现在还不算完成、也不会解锁下一关。\n\n"
                          + "怎么补：按 Enter 打开编辑器 → 点本卡上方的进度格（灰色 = 还没做过、绿色 = 已完成）跳回去补做，\n"
                          + "每做完一个任务，格子会变绿并自动跳到下一个；全部变绿后本课才会显示完成并回到主页面。"
                        : "The last chapter is done, but " + leftCount + " task(s) are still unfinished (done "
                          + doneCount + "/" + TaskCount + ").\n"
                          + "This level counts as cleared only when every task is done, so nothing is unlocked yet.\n\n"
                          + "How to catch up: press Enter to open the editor, then click a grey chip above (green = done)\n"
                          + "to jump back to that task. Once every chip is green the course completes and returns to the main menu.";
                }
                else
                {
                    bodyText.text = CurrentCourse == Course.Api
                    ? (zh
                        ? "你已经用到了全部炮台 API：GetAllObjects、TowerObjectInfo 的 Index / Type / HP / Position /\n"
                          + "Velocity / Volume 字段、GetFireCooldown、Fire、PrintLine / Print / ClearPrint，\n"
                          + "以及内置 API 三件套：Time.time / Time.deltaTime 限频计时、Mathf.Clamp / Min / Max / Abs、\n"
                          + "Vector3.Distance / normalized / magnitude 与向量相减求瞄准方向。\n\n"
                          + "关卡已开始刷新方块：按 Esc 关闭编辑器，你写的程序就会每帧自动运行、驱动炮台。\n"
                          + "想继续改进代码：按 Enter 重新打开编辑器修改，按 F6 重新编译运行。\n"
                          + "想从头再学一遍：在开发者指令框输入 /level 2 2，进度会自动重置。"
                        : "You have used the whole tower API: GetAllObjects, TowerObjectInfo.Index / Type / HP /\n"
                          + "Position / Velocity / Volume, GetFireCooldown, Fire, PrintLine / Print / ClearPrint,\n"
                          + "plus the built-in trio: Time.time / Time.deltaTime throttling, Mathf.Clamp / Min / Max / Abs,\n"
                          + "and Vector3.Distance / normalized / magnitude with subtraction for the aim direction.\n\n"
                          + "Blocks are now spawning: press Esc to close the editor and your program runs every frame.\n"
                          + "Press Enter to reopen the editor and F6 to recompile.\n"
                          + "Type /level 2 2 in the console to restart this tutorial.")
                    : (zh
                        ? "你已经用到了变量、const、运算符、关系 / 逻辑运算、if / if-else / switch、for、\n"
                          + "数组、List、foreach、while / do-while、方法定义与返回值、字符串拼接与 string.Format、注释。\n\n"
                          + "关卡已开始刷新方块：按 Esc 关闭编辑器，你写的程序就会每帧自动运行、驱动炮台。\n"
                          + "想继续改进代码：按 Enter 重新打开编辑器修改，按 F6 重新编译运行。\n"
                          + "想学炮台 API：在开发者指令框输入 /level 2 2。想重学本关：输入 /level 1 3。"
                        : "You have used variables, const, operators, comparisons, logic, if / if-else / switch,\n"
                          + "for, arrays, List, foreach, while / do-while, methods & return values, string concat & Format, comments.\n\n"
                          + "Blocks are now spawning: press Esc to close the editor and your program runs every frame.\n"
                          + "Press Enter to reopen the editor and F6 to recompile.\n"
                          + "For the tower API tutorial type /level 2 2; to restart this one type /level 1 3.");
                }
            }
            if (sampleText != null)
                sampleText.text = string.Empty;
            if (memRoot != null && memRoot.gameObject.activeSelf)
                memRoot.gameObject.SetActive(false);   // 全部完成：内存舞台也让位给结业文案
            RefreshPageNav(-1); // 全部完成：没有分页可翻
            LayoutGuideColumns();
            return;
        }

        int index = Mathf.Clamp(CurrentIndex, 0, Mathf.Max(0, TaskCount - 1));
        if (tasks != null && index < tasks.Length)
        {
            int pageCount = PageCountOf(index);
            guidePage = Mathf.Clamp(guidePage, 0, Mathf.Max(0, pageCount - 1));

            taskNameText.text = zh ? tasks[index].TitleZh : tasks[index].TitleEn;
            if (bodyText != null)
            {
                string guidePart;
                string samplePart = string.Empty;
                bool memPage = guidePage >= 2 && HasMemPage(index);
                bool diaPage = guidePage >= 2 && HasDiagramPage(index);
                if (memPage)
                {
                    // 第 3 页 = 内存可视化：整块正文区交给图形舞台（行 / 字节格 / 地址 / 执行台 / 旁白），
                    // 文字栏一律清空，翻到本页时动画从头开演。
                    guidePart = string.Empty;
                    samplePart = string.Empty;
                    animStep = 0;
                    animTimer = 0f;
                    memBuiltIndex = -1;
                    memBuiltStep = -1;
                    memBuiltH = -1f;
                }
                else if (diaPage)
                {
                    // 第 3 页 = 逻辑图：同样是整块正文区交给图形舞台（方框 / 箭头 / 底条 / 旁白），
                    // 文字栏清空，翻到本页从第一步开始
                    guidePart = string.Empty;
                    samplePart = string.Empty;
                    animStep = 0;
                    animTimer = 0f;
                    diaBuiltIndex = -1;
                    diaBuiltStep = -1;
                    diaBuiltH = -1f;
                }
                else if (guidePage >= 2 && HasAnimPage(index))
                {
                    // 第 3 页 = 代码动画：右栏放示例代码（当前执行行高亮），左栏放旁白 + 进度点
                    guidePart = BuildAnimGuide(index, zh);
                    samplePart = BuildAnimCode(index, zh);
                }
                else if (guidePage > 0)
                {
                    // 第 2 页 = 零基础手把手细节（怎么一步步做出来 / 常见写错的样子）：
                    // 整段放左栏通栏显示，不配示例代码（示例已经在第 1 页右侧）
                    guidePart = (zh ? tasks[index].Page2Zh : tasks[index].Page2En) ?? string.Empty;
                }
                else
                {
                    SplitTaskBody(zh ? tasks[index].BodyZh : tasks[index].BodyEn, out guidePart, out samplePart);
                }
                // 第 3 页（内存页）整块交给图形舞台：连 BuildGuideBody 那条尾巴（【蓝色长条】拖动提示 /
                // ① 语法 / ② 编译 状态）一并不要 —— 它们是给文字页看的，留在图上就是压字。
                // 文字对象直接隐藏，不只是清空文本：避免任何残留排版参与遮挡。
                bool stagePage = memPage || diaPage;   // 第 3 页 = 图形舞台（内存页或逻辑图页）
                bodyText.text = stagePage ? string.Empty : BuildGuideBody(index, zh, guidePart);
                if (bodyText.gameObject.activeSelf == stagePage)
                    bodyText.gameObject.SetActive(!stagePage);
                if (sampleText != null)
                    sampleText.text = samplePart;

                // 舞台随页面显隐：只有「第 3 页 + 本任务有内存场景」时才露面
                if (memRoot != null)
                {
                    if (memRoot.gameObject.activeSelf != memPage)
                        memRoot.gameObject.SetActive(memPage);
                    if (memPage)
                    {
                        int ms = Mathf.Clamp(animStep, 0, tasks[index].Mem.Steps.Length - 1);
                        RebuildMemStage(index, ms);
                        AnimateMemStage(1f, index, ms);   // 首帧直接落位，不要从全透明开始
                    }
                }

                // 逻辑图舞台同理：两块舞台互斥，永远只有一块在场
                if (diaRoot != null)
                {
                    if (diaRoot.gameObject.activeSelf != diaPage)
                        diaRoot.gameObject.SetActive(diaPage);
                    if (diaPage)
                    {
                        int ds = Mathf.Clamp(animStep, 0, tasks[index].Dia.Steps.Length - 1);
                        RebuildDiagramStage(index, ds);
                        AnimateDiagramStage(1f, index, ds);   // 首帧直接落位
                    }
                }

                LayoutGuideColumns();
            }
            RefreshPageNav(index);
        }
    }

    /// <summary>
    /// 把任务正文按第一个空行拆成两段：首段 = 写法 / 签名 / 示例代码（放右栏），其余 = 目标 / 提示 / 判定（放左栏）。
    /// 首段本身就是引导（「目标：」/「Goal:」开头）或全文没有空行时，整段留左栏通栏显示。
    /// </summary>
    private static void SplitTaskBody(string main, out string guide, out string sample)
    {
        guide = main ?? string.Empty;
        sample = string.Empty;
        if (string.IsNullOrEmpty(main))
            return;

        int cut = main.IndexOf("\n\n", System.StringComparison.Ordinal);
        if (cut <= 0)
            return;

        string head = main.Substring(0, cut);
        bool headIsGuide = head.StartsWith("目标", System.StringComparison.Ordinal)
            || head.StartsWith("Goal", System.StringComparison.Ordinal);
        if (headIsGuide)
            return;

        sample = head;
        guide = main.Substring(cut + 2).TrimStart('\n');
    }

    /// <summary>左栏正文 = 目标 / 提示 / 判定 + 本任务完成条件（语法 & 编译两条状态）。</summary>
    private string BuildGuideBody(int index, bool zh, string guide)
    {
        string main = guide ?? string.Empty;

        bool hit = false;
        bool compiledHit = false;
        if (editor != null && tasks != null && index == CurrentIndex)
        {
            string raw = editor.CurrentCode;
            if (!string.IsNullOrEmpty(raw))
                hit = StructureHit(raw, false);
            if (compiledThisTask)
                compiledHit = StructureHit(string.Empty, true);
        }

        string compileState;
        if (compiledHit)
        {
            compileState = zh
                ? "✔ 已通过编译验证（编译成功的代码中包含本任务语法）"
                : "✔ Verified by compile (compiled code contains this syntax)";
        }
        else if (compiledThisTask)
        {
            compileState = zh
                ? "✘ 上次编译的代码还不含本任务语法 —— 补全后重新点【编译运行 (F6)】"
                : "✘ Last compiled code lacks this syntax — fix it and click [Compile & Run (F6)] again";
        }
        else
        {
            compileState = zh
                ? "⏳ 等待编译 —— 写出右侧的语法后，点标题栏【编译运行 (F6)】验证"
                : "⏳ Not compiled yet — write the syntax on the right, then click [Compile & Run (F6)]";
        }

        string syntaxState = hit
            ? (zh ? "✔ 已命中" : "✔ Found")
            : (guidePage > 0
                ? (zh ? "✘ 未命中 —— 照本页①的步骤写出来，再点【编译运行 (F6)】" : "✘ Missing — follow the step-by-step list on this page, then click [Compile & Run (F6)]")
                : (zh ? "✘ 未命中 —— 请按右侧的写法补全" : "✘ Missing — add it as shown on the right"));

        StringBuilder sb = new StringBuilder(main.Length + 320);
        sb.Append(main);

        // 蓝色长条提示：只在 Level 1 - Part 4（语法课）的每一章出现 —— 新手不知道卡顶那条蓝线能拖，
        // 字号固定后文案过长会被截断，必须先告诉他可以把提示区拉高；API 课及之后不再重复提示。
        if (CurrentCourse == Course.Grammar)
        {
            sb.Append(zh
                ? "\n\n【蓝色长条】按住本卡顶部的蓝色长条上下拖动，可随时调整「提示区 / 代码区」的高度，字号不变。"
                : "\n\n[Blue bar] Drag the blue bar at the top of this card up or down to resize the hint area and the code area. The font size stays the same.");
        }

        // 第 1 页底部提示「还有一页手把手细节」：完全不懂代码的人通常不会主动去点右上角的翻页箭头，
        // 所以在第 1 页明确告诉他下一步该翻页（第 2 页是逐步操作 + 常见写错的样子，第 3 页是代码动画）。
        if (guidePage == 0 && PageCountOf(index) > 1)
        {
            int pageCount = PageCountOf(index);
            // 第 3 页到底长什么样，按本任务实际配了哪种画法说：内存格 / 逻辑图 / 代码动画，别描述错
            string thirdZh;
            string thirdEn;
            if (HasMemPage(index))
            {
                thirdZh = "\n再按一次 → 还有第 3 页【内存可视化】：把 int / float / bool / char 在内存里怎么摆、每格到底是什么字节，一格一格画给你看。";
                thirdEn = "\nPress → once more for the memory view: it draws how ints, floats, bools and chars are actually laid out in memory, byte by byte.";
            }
            else if (HasDiagramPage(index))
            {
                thirdZh = "\n再按一次 → 还有第 3 页【逻辑图】：把这一步到底怎么走、谁调用谁、数据从哪流到哪，用方框和箭头一步步点亮给你看。";
                thirdEn = "\nPress → once more for the diagram view: it lights up the boxes and arrows step by step, so you can see how control and data actually flow.";
            }
            else
            {
                thirdZh = "\n再按一次 → 还有第 3 页【代码演算】：一行一行走一遍示例代码，每行配一句旁白说明它在干什么。";
                thirdEn = "\nPress → once more for the code walkthrough: it steps through the sample line by line, with a note on what each line does.";
            }
            sb.Append(zh
                ? "\n\n【不会写？】点本卡右上角「‹ 细节 1/" + pageCount + " ›」，或直接按键盘 ← / → 前后翻页：\n"
                  + "第 2 页写着一共几步、每一步敲什么、写在哪一行，以及三个最常见的写错方式。"
                  + (pageCount >= 3 ? thirdZh : "")
                : "\n\n[Stuck?] Click \"‹ Detail 1/" + pageCount + " ›\" at the top right of this card, or just press ← / → on the keyboard to flip pages:\n"
                  + "Page 2 lists every step, what to type, where to type it, and the three most common mistakes."
                  + (pageCount >= 3 ? thirdEn : ""));
        }

        sb.Append(zh ? "\n\n—— 本任务完成条件（两项都满足才算完成）——\n" : "\n\n—— Complete when BOTH are satisfied ——\n");
        sb.Append(zh ? "① 语法：" : "① Syntax: ").Append(syntaxState).Append('\n');
        sb.Append(zh ? "② 编译：" : "② Compile: ").Append(compileState);
        return sb.ToString();
    }

    private void SyncVisibility()
    {
        if (panelRoot == null)
            return;

        // 任务卡只在编辑器打开时显示；编辑器关闭时玩家看到的是实机页面，
        // 由页面正中的进入提示（按下 Enter 打开代码编辑界面）引导，不遮挡任何玩法元素。
        bool editorOpen = editor != null && editor.IsOpen;

        if (panelRoot.activeSelf != editorOpen)
            panelRoot.SetActive(editorOpen);

        if (hintRoot != null)
        {
            bool showHint = !editorOpen;
            if (hintRoot.activeSelf != showHint)
                hintRoot.SetActive(showHint);
        }

        if (!editorOpen)
            RefreshHint();
    }

    private void RefreshHint()
    {
        if (hintText == null)
            return;

        bool zh = Localization.IsChinese;

        // 主行：实机页面上最醒目的一句 —— 按下 Enter 打开代码编辑界面
        string main = zh ? "按下 Enter 打开代码编辑界面" : "Press Enter to open the code editor";

        // 副行：当前任务进度 / 完成后可随时回来改代码
        string sub;
        if (finished)
        {
            // 走完最后一章 ≠ 每一章都做过（可能是点进度格 / /level 跳过来的）：
            // 没做满就明确报「还有几个 Task 没做完」，只有真做满了才说完成、才回主页。
            int allDoneCount = CountDoneTasks();
            int leftCount = Mathf.Max(0, TaskCount - allDoneCount);
            if (leftCount > 0)
                sub = zh
                    ? "还有 " + leftCount + " 个 Task 没做完（已完成 " + allDoneCount + "/" + TaskCount
                      + "）—— 按 Enter 打开编辑器，再点上方进度格回到没做完的任务"
                    : leftCount + " task(s) still unfinished (done " + allDoneCount + "/" + TaskCount
                      + ") — press Enter, then click an unfinished chip to catch up";
            else
                sub = CurrentCourse == Course.Api
                    ? (zh ? "炮台 API 教学全部完成 —— 正在返回主页面" : "Tower API course complete — returning to the main menu")
                    : (zh ? "C# 语法教学全部完成 —— 正在返回主页面" : "C# syntax course complete — returning to the main menu");
        }
        else
        {
            int shown = Mathf.Clamp(CurrentIndex + 1, 1, Mathf.Max(1, TaskCount));
            string title = "";
            if (tasks != null && CurrentIndex >= 0 && CurrentIndex < tasks.Length)
                title = zh ? tasks[CurrentIndex].TitleZh : tasks[CurrentIndex].TitleEn;
            sub = zh
                ? "当前任务 " + shown + "/" + TaskCount + "：" + title + "（进入编辑器后写代码，写完点【编译运行 (F6)】）"
                : "Task " + shown + "/" + TaskCount + ": " + title + " (write the code in the editor, then click [Compile & Run (F6)])";
        }

        string text = "<color=#5CD8F2>" + main + "</color>\n<size=22><color=#A8C4D6>" + sub + "</color></size>";

        // 语法课额外补一行前提说明：正式关卡是「玩家代码调用 GameStart() 才开场」，
        // 不写那一行就永远不出方块；本教学关自动开场，避免玩家把「不动」误认为卡死。
        if (!finished && CurrentCourse == Course.Grammar)
        {
            string premise = zh
                ? "前提：正式关卡要你写 tower.GameStart() 才会开始（不写就一直不出方块）；本教学关自动开场。"
                : "Note: a real level only starts when your code calls tower.GameStart() (without it, no blocks ever spawn); this tutorial level starts by itself.";
            text += "\n<size=20><color=#7FA6BC>" + premise + "</color></size>";
        }

        if (text == hintCache)
            return;
        hintCache = text;
        SetLabel(hintText, text);
        RefitHintBar(); // 文案长度变化时按实测宽高重排，长文案自动换行加高，不被截断
    }

    // ================== 任务表（C# 基础语法 49 项） ==================

    private static TaskDef[] grammarTasks;

    private static TaskDef[] GrammarTasks
    {
        get
        {
            if (grammarTasks == null)
                grammarTasks = BuildGrammarTasks();
            return grammarTasks;
        }
    }

    private static TaskDef Def(string titleZh, string titleEn, string bodyZh, string bodyEn, TaskMatch match)
    {
        TaskDef t = new TaskDef();
        t.TitleZh = titleZh;
        t.TitleEn = titleEn;
        t.BodyZh = bodyZh;
        t.BodyEn = bodyEn;
        t.Match = match;
        return t;
    }

    private static TaskDef[] BuildGrammarTasks()
    {
        TaskDef[] list = new TaskDef[49];

        list[0] = Def(
            "任务 1：注释",
            "Task 1: Comments",
            "写法：// 单行注释      /* 多行注释 */\n"
            + "示例代码：\n"
            + "    // 计算剩余血量\n"
            + "    int remainHp = 3;\n"
            + "    /* 下面这行打印到荧幕\n"
            + "       多行注释可以换行 */\n"
            + "    tower.PrintLine(\"hp={0}\", remainHp);\n\n"
            + "目标：给代码加至少 1 条注释。\n"
            + "提示：注释不参与运行；养成在关键逻辑上方写一行说明的习惯。\n"
            + "先说一句前提：正式关卡（Level 3 - Part 1）里，代码不写 tower.GameStart() 游戏就不会开始 —— 方块一个都不出，等你按下开关；本教学关会自动开场，这里专心学语法即可，GameStart 到炮台 API 课再专门讲。\n"
            + "判定：代码里出现 // 或 /*（写在字符串里的不算）。",
            "Syntax: // single line      /* multi line */\n"
            + "Example Code:\n"
            + "    // remaining hp\n"
            + "    int remainHp = 3;\n"
            + "    /* this prints to the screen\n"
            + "       multi-line comments can wrap */\n"
            + "    tower.PrintLine(\"hp={0}\", remainHp);\n\n"
            + "Goal: add at least one comment.\n"
            + "Hint: comments never execute; document key logic above it.\n"
            + "One prerequisite first: in a real level (Level 3 - Part 1) the game never starts until your code calls tower.GameStart() - not a single block spawns while it waits for you to press the switch. This tutorial level starts by itself, so just focus on syntax here; GameStart is taught properly in the Tower API course.\n"
            + "Check: // or /* appears in the code (not inside a string).",
            (raw, code) => ReComment.IsMatch(raw ?? string.Empty));

        list[1] = Def(
            "任务 2：变量声明与类型",
            "Task 2: Variables & Types",
            "写法：类型 变量名 = 初始值;\n"
            + "示例代码：\n"
            + "    int hp = 100;\n"
            + "    float speed = 1.5f;\n"
            + "    bool ready = true;\n"
            + "    string tag = \"tower\";\n\n"
            + "目标：声明 int / float / bool / string 四种变量。\n"
            + "提示：float 字面量要带 f。每个任务的示例都能直接粘贴进编辑器（变量名互不重复），粘完点编译即可。\n"
            + "判定：出现「类型 名字 = 值;」。",
            "Syntax: type name = value;\n"
            + "Example Code:\n"
            + "    int hp = 100;\n"
            + "    float speed = 1.5f;\n"
            + "    bool ready = true;\n"
            + "    string tag = \"tower\";\n\n"
            + "Goal: declare int / float / bool / string variables.\n"
            + "Hint: float literals need the f suffix. Every example can be pasted as-is (names never collide) — then compile.\n"
            + "Check: \"type name = value;\" appears.",
            (raw, code) => ReVariable.IsMatch(code));

        list[2] = Def(
            "任务 3：var 隐式类型推断",
            "Task 3: var Type Inference",
            "写法：var 变量名 = 初始值;（类型由右边推断，必须当场赋值）\n"
            + "示例代码：\n"
            + "    var hp = 100;        // 推断成 int\n"
            + "    var speed = 1.5f;    // 推断成 float（f 不能省）\n"
            + "    var tag = \"tower\";  // 推断成 string\n\n"
            + "目标：用 var 声明整数、小数、文字各 1 个。\n"
            + "提示：var 不是「没有类型」，是「让编译器看右边替你写上类型」；定下来之后 hp 还是 int，装不进小数。\n"
            + "判定：出现以 var 开头的声明并当场赋值。",
            "Syntax: var name = value;  (the type comes from the right side and it must be initialised)\n"
            + "Example Code:\n"
            + "    var hp = 100;        // inferred as int\n"
            + "    var speed = 1.5f;    // inferred as float (the f is required)\n"
            + "    var tag = \"tower\";  // inferred as string\n\n"
            + "Goal: declare one whole number, one decimal and one text with var.\n"
            + "Hint: var does not mean \"no type\" - it means \"let the compiler write the type\". Once inferred, hp is int and will not hold a decimal.\n"
            + "Check: a declaration starts with var and assigns a value right away.",
            (raw, code) => ReImplicitVar.IsMatch(code));

        list[3] = Def(
            "任务 4：常量 const",
            "Task 4: const Constants",
            "写法：const 类型 常量名 = 值;\n"
            + "示例代码：\n"
            + "    const int MaxBlocks = 400;\n"
            + "    const float Pi = 3.14f;\n\n"
            + "目标：声明至少 1 个 const 常量。\n"
            + "提示：const 必须在声明时就赋值，之后不能再改。\n"
            + "判定：出现 const 声明。",
            "Syntax: const type NAME = value;\n"
            + "Example Code:\n"
            + "    const int MaxBlocks = 400;\n"
            + "    const float Pi = 3.14f;\n\n"
            + "Goal: declare at least one const.\n"
            + "Hint: a const must be initialised at declaration and can never change.\n"
            + "Check: a const declaration appears.",
            (raw, code) => ReConst.IsMatch(code));

        list[4] = Def(
            "任务 5：算术运算符与表达式",
            "Task 5: Arithmetic Operators",
            "写法：操作数 运算符 操作数，运算符可用 + - * / %\n"
            + "示例代码：\n"
            + "    int total = 2 + 3 * 4;\n"
            + "    float halfTotal = total / 2f;\n"
            + "    int rest = total % 5;\n\n"
            + "目标：写至少 1 个含 + - * / % 的计算式。\n"
            + "提示：* / % 优先级高于 + -，可用括号改变顺序；整数相除会丢掉小数。\n"
            + "判定：出现算术运算表达式。",
            "Syntax: operand op operand, ops: + - * / %\n"
            + "Example Code:\n"
            + "    int total = 2 + 3 * 4;\n"
            + "    float halfTotal = total / 2f;\n"
            + "    int rest = total % 5;\n\n"
            + "Goal: write an expression using + - * / or %.\n"
            + "Hint: * / % bind tighter than + -; int division drops the fraction.\n"
            + "Check: an arithmetic expression appears.",
            (raw, code) => ReArithmetic.IsMatch(code));

        list[5] = Def(
            "任务 6：条件与流程控制",
            "Task 6: Conditions & Control Flow",
            "这一项把关系运算符、逻辑运算符、if / else / else if 与 switch 合成一段：所有判断都是「先算出一个 bool，再按 bool 走不同的路」。\n"
            + "写法：\n"
            + "· 关系：a > b、a == b、a != b → 得到 true / false\n"
            + "· 逻辑：&&（都真）、||（一真即真）、!（取反）\n"
            + "· if / else：条件成立走上面，不成立走 else\n"
            + "· else if：多个条件从上往下判，谁先成立走谁\n"
            + "· switch：按值挑一支，每个 case 结尾要 break\n"
            + "示例代码：\n"
            + "    int hpMax = 3;\n"
            + "    int hp = 2;\n"
            + "    if (hp <= 0)\n"
            + "        tower.PrintLine(\"down\");\n"
            + "    else if (hp < hpMax && hpMax > 0)\n"
            + "        tower.PrintLine(\"damaged {0}\", hp);\n"
            + "    else\n"
            + "        tower.PrintLine(\"full hp\");\n"
            + "    switch (hpMax)\n"
            + "    {\n"
            + "        case 3: tower.PrintLine(\"cap is 3\"); break;\n"
            + "        default: tower.PrintLine(\"other cap\"); break;\n"
            + "    }\n\n"
            + "目标：一段代码里同时写出 if 与 switch。\n"
            + "提示：= 是赋值，== 才是判断相等；case 结尾漏了 break 会一路往下串。\n"
            + "判定：同时出现 if、switch（并至少有一次比较或逻辑运算）。",
            "This task folds comparison, logic, if / else / else if and switch into one snippet: every branch starts with a bool, then the bool decides the path.\n"
            + "Syntax:\n"
            + "- Comparison: a > b, a == b, a != b  -> true / false\n"
            + "- Logic: && (both true), || (either true), ! (invert)\n"
            + "- if / else: run the block above when the condition holds, else the else block\n"
            + "- else if: test further conditions top to bottom; the first match wins\n"
            + "- switch: pick one branch by value; each case ends with break\n"
            + "Example Code:\n"
            + "    int hpMax = 3;\n"
            + "    int hp = 2;\n"
            + "    if (hp <= 0)\n"
            + "        tower.PrintLine(\"down\");\n"
            + "    else if (hp < hpMax && hpMax > 0)\n"
            + "        tower.PrintLine(\"damaged {0}\", hp);\n"
            + "    else\n"
            + "        tower.PrintLine(\"full hp\");\n"
            + "    switch (hpMax)\n"
            + "    {\n"
            + "        case 3: tower.PrintLine(\"cap is 3\"); break;\n"
            + "        default: tower.PrintLine(\"other cap\"); break;\n"
            + "    }\n\n"
            + "Goal: write if and switch in the same code.\n"
            + "Hint: = assigns, == compares; a case without break falls through.\n"
            + "Check: if and switch both appear (plus at least one comparison or logical operator).",
            (raw, code) => ReIf.IsMatch(code) && ReSwitch.IsMatch(code)
                && (ReRelational.IsMatch(code) || ReLogical.IsMatch(code)));

        list[6] = Def(
            "任务 7：for 循环（三要素）",
            "Task 7: for Loops",
            "写法：for (初始化; 条件; 每轮收尾) { 循环体 }\n"
            + "示例代码：\n"
            + "    for (int i = 0; i < 3; i++)\n"
            + "    {\n"
            + "        tower.PrintLine(\"shot {0}\", i);\n"
            + "    }\n\n"
            + "目标：写一个 for 循环，让循环体正好执行 3 次。\n"
            + "提示：三段之间必须是英文分号，一段都不能省；漏掉 i++ 条件永远成立，就是死循环。\n"
            + "判定：出现 for ( 初始化 ; 条件 ; 收尾 ) 结构。",
            "Syntax: for (init; condition; step) { body }\n"
            + "Example Code:\n"
            + "    for (int i = 0; i < 3; i++)\n"
            + "    {\n"
            + "        tower.PrintLine(\"shot {0}\", i);\n"
            + "    }\n\n"
            + "Goal: write one for loop whose body runs exactly three times.\n"
            + "Hint: the three parts are separated by English semicolons and none can be dropped; without i++ the condition never fails and you get an infinite loop.\n"
            + "Check: a for ( init ; condition ; step ) structure appears.",
            (raw, code) => ReFor.IsMatch(code));

        list[7] = Def(
            "任务 8：一维数组",
            "Task 8: Arrays",
            "写法：类型[] 名字 = new 类型[长度];\n"
            + "示例代码：\n"
            + "    int[] hits = new int[3];\n"
            + "    hits[0] = 2;\n"
            + "    tower.PrintLine(\"hits[0]={0}\", hits[0]);\n\n"
            + "目标：声明一维数组并读写元素。\n"
            + "提示：下标从 0 开始，长度 3 的合法下标是 0/1/2，越界会报错。\n"
            + "判定：出现 new 类型[长度] 或 名字[下标]。",
            "Syntax: type[] name = new type[length];\n"
            + "Example Code:\n"
            + "    int[] hits = new int[3];\n"
            + "    hits[0] = 2;\n"
            + "    tower.PrintLine(\"hits[0]={0}\", hits[0]);\n\n"
            + "Goal: declare an array and read/write an element.\n"
            + "Hint: indexes start at 0; a length of 3 allows 0/1/2 only.\n"
            + "Check: new type[length] or name[index] appears.",
            (raw, code) => ReArray.IsMatch(code));

        list[8] = Def(
            "任务 9：List 集合",
            "Task 9: List<T>",
            "写法：List<类型> 名字 = new List<类型>();\n"
            + "示例代码：\n"
            + "    List<int> hpList = new List<int>();\n"
            + "    hpList.Add(3);\n"
            + "    tower.PrintLine(\"count={0}\", hpList.Count);\n\n"
            + "目标：声明 List 并添加至少 1 个元素。\n"
            + "提示：编辑器模板已带 using System.Collections.Generic;；用 Add 追加、Count 取数量。\n"
            + "判定：出现 new List<…> 且出现 .Add(。",
            "Syntax: List<T> name = new List<T>();\n"
            + "Example Code:\n"
            + "    List<int> hpList = new List<int>();\n"
            + "    hpList.Add(3);\n"
            + "    tower.PrintLine(\"count={0}\", hpList.Count);\n\n"
            + "Goal: declare a List and add at least one element.\n"
            + "Hint: the editor template already includes using System.Collections.Generic; use Add to append and Count to read the size.\n"
            + "Check: new List<...> and .Add( both appear.",
            (raw, code) => ReListNew.IsMatch(code) && ReListAdd.IsMatch(code));

        list[9] = Def(
            "任务 10：foreach 遍历",
            "Task 10: foreach Loops",
            "写法：foreach (元素类型 变量名 in 集合) { 循环体 }\n"
            + "示例代码：\n"
            + "    int[] hits = new int[3];\n"
            + "    hits[0] = 2;\n"
            + "    hits[1] = 4;\n"
            + "    foreach (int h in hits)\n"
            + "    {\n"
            + "        tower.PrintLine(\"h={0}\", h);\n"
            + "    }\n\n"
            + "目标：用 foreach 把数组里的每个元素挨个读一遍。\n"
            + "提示：foreach 自己从第一个走到最后一个，你不写下标也不会越界；拿到的 h 是副本，改它不会改到数组。\n"
            + "判定：出现 foreach ( … in … ) 结构。",
            "Syntax: foreach (elementType name in collection) { body }\n"
            + "Example Code:\n"
            + "    int[] hits = new int[3];\n"
            + "    hits[0] = 2;\n"
            + "    hits[1] = 4;\n"
            + "    foreach (int h in hits)\n"
            + "    {\n"
            + "        tower.PrintLine(\"h={0}\", h);\n"
            + "    }\n\n"
            + "Goal: use foreach to read every element of an array.\n"
            + "Hint: foreach walks from the first element to the last on its own, so no index means no out-of-range; the h you get is a copy, so changing it never changes the array.\n"
            + "Check: a foreach ( ... in ... ) structure appears.",
            (raw, code) => ReForeach.IsMatch(code));

        list[10] = Def(
            "任务 11：while 循环",
            "Task 11: while Loops",
            "写法：while (条件) { 循环体 }\n"
            + "示例代码：\n"
            + "    int wi = 0;\n"
            + "    while (wi < 3)\n"
            + "    {\n"
            + "        wi++;\n"
            + "    }\n\n"
            + "目标：写一个 while 循环。\n"
            + "提示：循环体里要改变条件变量，否则会死循环。\n"
            + "判定：出现 while ( … ) { 结构。",
            "Syntax: while (condition) { body }\n"
            + "Example Code:\n"
            + "    int wi = 0;\n"
            + "    while (wi < 3)\n"
            + "    {\n"
            + "        wi++;\n"
            + "    }\n\n"
            + "Goal: write one while loop.\n"
            + "Hint: change the condition variable inside the body, or it never ends.\n"
            + "Check: a while ( ... ) { structure appears.",
            (raw, code) => ReWhile.IsMatch(code));

        list[11] = Def(
            "任务 12：do … while 循环",
            "Task 12: do / while Loops",
            "写法：do { 循环体 } while (条件);\n"
            + "示例代码：\n"
            + "    int di = 0;\n"
            + "    do\n"
            + "    {\n"
            + "        di++;\n"
            + "    } while (di < 3);\n\n"
            + "目标：写一个 do … while 循环。\n"
            + "提示：do 的循环体至少执行 1 次，条件写在最后且末尾要加分号。\n"
            + "判定：出现 do { … } while ( … ); 结构。",
            "Syntax: do { body } while (condition);\n"
            + "Example Code:\n"
            + "    int di = 0;\n"
            + "    do\n"
            + "    {\n"
            + "        di++;\n"
            + "    } while (di < 3);\n\n"
            + "Goal: write one do ... while loop.\n"
            + "Hint: the body runs at least once; the condition comes last and needs a semicolon.\n"
            + "Check: a do { ... } while ( ... ); structure appears.",
            (raw, code) => ReDoWhile.IsMatch(code));

        list[12] = Def(
            "任务 13：方法定义与参数",
            "Task 13: Methods & Parameters",
            "写法：返回类型 方法名(参数类型 参数名, …) { 方法体 }\n"
            + "示例代码：\n"
            + "    void ShowHp(int hp)\n"
            + "    {\n"
            + "        tower.PrintLine(\"hp={0}\", hp);\n"
            + "    }\n\n"
            + "    ShowHp(3);\n\n"
            + "目标：定义 1 个带参数的方法。\n"
            + "提示：方法写在 Run 里面（局部函数），先定义后调用。\n"
            + "判定：出现「类型 名字(参数…) {」。",
            "Syntax: returnType name(type arg, ...) { body }\n"
            + "Example Code:\n"
            + "    void ShowHp(int hp)\n"
            + "    {\n"
            + "        tower.PrintLine(\"hp={0}\", hp);\n"
            + "    }\n\n"
            + "    ShowHp(3);\n\n"
            + "Goal: define one method that takes parameters.\n"
            + "Hint: declare it inside Run (a local function) before calling it.\n"
            + "Check: \"type name(args...) {\" appears.",
            (raw, code) => ReMethodDef.IsMatch(code));

        list[13] = Def(
            "任务 14：方法返回值 return",
            "Task 14: Return Values",
            "写法：return 表达式; 并用变量接收调用结果\n"
            + "示例代码：\n"
            + "    int Twice(int v)\n"
            + "    {\n"
            + "        return v * 2;\n"
            + "    }\n\n"
            + "    int sum = Twice(2);\n"
            + "    tower.PrintLine(\"sum={0}\", sum);\n\n"
            + "目标：用变量接收方法返回值。\n"
            + "提示：return 后面跟表达式；返回值类型要与方法声明一致。\n"
            + "判定：出现 return …; 且出现「变量 = 方法调用(」。",
            "Syntax: return expression; and receive it into a variable\n"
            + "Example Code:\n"
            + "    int Twice(int v)\n"
            + "    {\n"
            + "        return v * 2;\n"
            + "    }\n\n"
            + "    int sum = Twice(2);\n"
            + "    tower.PrintLine(\"sum={0}\", sum);\n\n"
            + "Goal: store a method's return value in a variable.\n"
            + "Hint: return takes an expression whose type matches the declaration.\n"
            + "Check: return ...; and \"var = method(\" both appear.",
            (raw, code) => ReReturn.IsMatch(code) && ReMethodCallAssign.IsMatch(code));

        list[14] = Def(
            "任务 15：字符串拼接",
            "Task 15: String Concatenation",
            "写法：用 + 拼接字符串与其他值\n"
            + "示例代码：\n"
            + "    int curHp = 3;\n"
            + "    string msg = \"hp=\" + curHp;\n"
            + "    tower.PrintLine(msg + \" / done\");\n\n"
            + "目标：用 + 拼接字符串与其他值。\n"
            + "提示：数字会自动转成字符串，多个值用多个 + 连起来。\n"
            + "判定：出现「字符串 + 值」。",
            "Syntax: join strings and values with +\n"
            + "Example Code:\n"
            + "    int curHp = 3;\n"
            + "    string msg = \"hp=\" + curHp;\n"
            + "    tower.PrintLine(msg + \" / done\");\n\n"
            + "Goal: concatenate a string with other values using +.\n"
            + "Hint: numbers are converted to text automatically; chain more + for more values.\n"
            + "Check: \"string + value\" appears.",
            (raw, code) => ReConcat.IsMatch(code));

        list[15] = Def(
            "任务 16：string.Format 格式化",
            "Task 16: string.Format",
            "写法：string.Format(\"模板 {0} {1}\", 值1, 值2)\n"
            + "示例代码：\n"
            + "    int baseHp = 3;\n"
            + "    string msgFmt = string.Format(\"hp={0}, half={1:F1}\", baseHp, baseHp / 2f);\n"
            + "    tower.PrintLine(msgFmt);\n\n"
            + "目标：用 string.Format 生成格式化字符串。\n"
            + "提示：{0} {1} 是占位符，按顺序取后面的参数；{1:F1} 表示保留 1 位小数。\n"
            + "判定：出现 string.Format(。",
            "Syntax: string.Format(\"template {0} {1}\", v1, v2)\n"
            + "Example Code:\n"
            + "    int baseHp = 3;\n"
            + "    string msgFmt = string.Format(\"hp={0}, half={1:F1}\", baseHp, baseHp / 2f);\n"
            + "    tower.PrintLine(msgFmt);\n\n"
            + "Goal: build a formatted string with string.Format.\n"
            + "Hint: {0} {1} are placeholders filled by the following arguments; {1:F1} keeps one decimal.\n"
            + "Check: string.Format( appears.",
            (raw, code) => ReStringFormat.IsMatch(code));

        list[16] = Def(
            "任务 17：Vector2 / Vector3 向量",
            "Task 17: Vector2 / Vector3",
            "向量就是「几个 float 打包成一个值」：Vector3 有 x / y / z，Vector2 只有 x / y。\n"
            + "写法：new Vector3(x, y, z)；取成员用点号：v.x。\n"
            + "示例代码：\n"
            + "    Vector3 up = new Vector3(0f, 1f, 0f);\n"
            + "    Vector3 side = new Vector3(1f, 0f, 0f);\n"
            + "    tower.PrintLine(\"up.x={0}\", up.x);\n"
            + "    tower.Fire(up);\n"
            + "    Vector2 flat = new Vector2(1f, 0f);\n"
            + "    tower.PrintLine(\"flat.x={0}\", flat.x);\n\n"
            + "目标：用 new Vector3(...) 造一个向量，并点号访问它的成员。\n"
            + "提示：向量能整个当参数传（tower.Fire(up)），不用再拆成三个 float。\n"
            + "判定：出现 new Vector3( 或 new Vector2(。",
            "A vector is a few floats packed into one value: Vector3 has x / y / z, Vector2 only x / y.\n"
            + "Syntax: new Vector3(x, y, z); read members with a dot: v.x.\n"
            + "Example Code:\n"
            + "    Vector3 up = new Vector3(0f, 1f, 0f);\n"
            + "    Vector3 side = new Vector3(1f, 0f, 0f);\n"
            + "    tower.PrintLine(\"up.x={0}\", up.x);\n"
            + "    tower.Fire(up);\n"
            + "    Vector2 flat = new Vector2(1f, 0f);\n"
            + "    tower.PrintLine(\"flat.x={0}\", flat.x);\n\n"
            + "Goal: build a vector with new Vector3(...) and read its members with a dot.\n"
            + "Hint: a whole vector can be passed as one argument (tower.Fire(up)) instead of three floats.\n"
            + "Check: new Vector3( or new Vector2( appears.",
            (raw, code) => ReVectorNew.IsMatch(code));

        list[17] = Def(
            "任务 18：类与对象（new 与成员）",
            "Task 18: Classes & Objects",
            "类 = 一份数据 + 一组方法打包在一起。你不用自己写类，先学会用现成的：\n"
            + "· tower 是 CodeTower 类的对象，tower.Fire(...) 是调它的方法\n"
            + "· Vector3 是结构体（当类理解即可），new 出一个值，点号取成员\n"
            + "· tower.GetAllObjects() 返回的对象都有 .Position / .HP 这样的成员\n"
            + "写法：new 类型(参数) 造对象；对象.成员 取值或调方法。\n"
            + "示例代码：\n"
            + "    Vector3 dir = new Vector3(0f, 0f, 1f);   // new：造一个对象\n"
            + "    tower.Fire(dir);                          // 对象整个当参数交给方法\n"
            + "    tower.PrintLine(\"dir.z={0}\", dir.z);     // 点号取成员\n\n"
            + "目标：用 new 造一个对象，并用点号访问它的成员。\n"
            + "提示：new 出来的东西可以存进变量、当参数传、拆开取成员；一个变量名只装一个对象。\n"
            + "判定：代码里出现 new 且出现「对象.成员」的点号访问。",
            "A class packs data plus methods together. You do not write your own class yet - learn to use the existing ones:\n"
            + "- tower is an object of class CodeTower; tower.Fire(...) calls its method\n"
            + "- Vector3 is a struct (treat it like a class): new a value, read members with a dot\n"
            + "- objects from tower.GetAllObjects() expose members such as .Position / .HP\n"
            + "Syntax: new Type(args) creates an object; object.member reads a value or calls a method.\n"
            + "Example Code:\n"
            + "    Vector3 dir = new Vector3(0f, 0f, 1f);   // new: create an object\n"
            + "    tower.Fire(dir);                          // pass the whole object to a method\n"
            + "    tower.PrintLine(\"dir.z={0}\", dir.z);     // read a member with a dot\n\n"
            + "Goal: create an object with new and read one of its members with a dot.\n"
            + "Hint: a created object can be stored, passed as an argument or take its members apart; one variable name holds one object.\n"
            + "Check: new appears together with a dotted member access.",
            (raw, code) => ReNewObj.IsMatch(code) && ReMemberAccess.IsMatch(code));

        list[18] = Def(
            "任务 19：自己造一个全局区（GlobalCode）",
            "Task 19: Declare your own globals (GlobalCode)",
            "先说清楚：GlobalCode 不是 C# 的功能，是作者刻意加的扩展语法（真正的 C# 不允许在方法里声明字段）。\n"
            + "写法：在代码最前面写 GlobalCode { 一堆字段声明 }，块里的东西会被搬到整个类的最外层，所以它能跨帧记住值。\n"
            + "· 块内：只能写「类型 名字 = 值;」这种字段声明（int / float / bool / string / 数组 / List 都行）\n"
            + "· 块外（每帧语句）：直接像普通变量一样读写块里声明的名字\n"
            + "· 块内不能写 tower.xxx —— tower 只在 Run(tower) 里存在\n"
            + "· 写错（后面漏 {、大括号不成对、写了两个块）会是致命错误，不会当成普通代码放过\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        int shots = 0;              // 跨帧计数器：下一次 Run 接着上次的值\n"
            + "    }\n"
            + "    shots = shots + 1;\n"
            + "    tower.PrintLine(\"shots={0}\", shots);\n\n"
            + "目标：写一个 GlobalCode 块，声明至少一个变量，并在块外的每帧语句里用到它。\n"
            + "提示：普通变量每帧都是新的（下次进 Run 又从初始值开始）；全局区里的变量一直活到重新编译为止，刷屏、换关、存值都靠它。\n"
            + "判定：出现 GlobalCode 块，且块内声明的变量名在块外被用到。",
            "First, to be clear: GlobalCode is NOT a C# feature - it is an extension the author added on purpose (real C# does not let you declare fields inside a method).\n"
            + "Syntax: put GlobalCode { field declarations } at the very top of your code; whatever is inside is hoisted to class level, so it remembers values across frames.\n"
            + "- Inside: only field declarations like 'type name = value;' (int / float / bool / string / arrays / List)\n"
            + "- Outside (per-frame code): read and write those names like ordinary variables\n"
            + "- No tower.xxx inside the block: tower only exists in Run(tower)\n"
            + "- Misuse (missing {, unbalanced braces, two blocks) is a hard error, never silently ignored\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        int shots = 0;              // a counter that survives frames\n"
            + "    }\n"
            + "    shots = shots + 1;\n"
            + "    tower.PrintLine(\"shots={0}\", shots);\n\n"
            + "Goal: write a GlobalCode block with at least one variable and use it in the per-frame code below.\n"
            + "Hint: an ordinary variable is brand new every frame; a global lives until you recompile - use it for state you must remember.\n"
            + "Check: a GlobalCode block appears and a variable declared inside it is used outside.",
            (raw, code) => JudgeGlobalBlock(raw, code));

        list[19] = Def(
            "任务 20：break 与 continue",
            "Task 20: break & continue",
            "写法：循环体里 break; 立刻跳出整个循环；continue; 放弃这一轮、直接进下一轮。\n"
            + "示例代码：\n"
            + "    for (int i = 1; i <= 5; i++) {\n"
            + "        if (i == 2) continue;      // i 等于 2 时跳过这一轮\n"
            + "        if (i == 4) break;         // i 等于 4 时整个循环结束\n"
            + "        tower.PrintLine(\"i={0}\", i);\n"
            + "    }\n"
            + "    tower.PrintLine(\"loop over\");\n\n"
            + "目标：写一个循环，里面同时出现 continue; 和 break;。\n"
            + "提示：break 跳的是「整个循环」，continue 跳的是「这一轮」；两个都要放进 if 里才有条件，否则一进循环就断了。\n"
            + "判定：代码里同时出现 break; 与 continue;。",
            "Syntax: inside a loop, break; leaves the whole loop at once; continue; drops the rest of this round and goes to the next.\n"
            + "Example Code:\n"
            + "    for (int i = 1; i <= 5; i++) {\n"
            + "        if (i == 2) continue;      // skip this round when i is 2\n"
            + "        if (i == 4) break;         // end the whole loop when i is 4\n"
            + "        tower.PrintLine(\"i={0}\", i);\n"
            + "    }\n"
            + "    tower.PrintLine(\"loop over\");\n\n"
            + "Goal: write one loop that contains both continue; and break;.\n"
            + "Hint: break leaves the whole loop, continue leaves only this round; wrap both in an if or they fire on the first round.\n"
            + "Check: break; and continue; both appear in the code.",
            (raw, code) => ReBreak.IsMatch(code) && ReContinue.IsMatch(code));

        list[20] = Def(
            "任务 21：三元运算符 ?:",
            "Task 21: Ternary Operator ?:",
            "写法：条件 ? 成立时的值 : 不成立时的值    —— 一行就是一个完整表达式，能直接赋给变量。\n"
            + "示例代码：\n"
            + "    int hpLeft = 3;\n"
            + "    string state = hpLeft > 0 ? \"alive\" : \"dead\";\n"
            + "    int bonus = hpLeft > 5 ? 10 : 0;\n"
            + "    tower.PrintLine(\"state={0} bonus={1}\", state, bonus);\n\n"
            + "目标：用一次 ?: 造出一个值并赋给变量。\n"
            + "提示：if-else 适合「做一堆事」，?: 适合「选一个值」；两个分支的类型要能对上（都是 string 或都是 int）。\n"
            + "判定：代码里出现「条件 ? 值 : 值」的形式。",
            "Syntax: condition ? valueWhenTrue : valueWhenFalse    - one expression that can be assigned directly.\n"
            + "Example Code:\n"
            + "    int hpLeft = 3;\n"
            + "    string state = hpLeft > 0 ? \"alive\" : \"dead\";\n"
            + "    int bonus = hpLeft > 5 ? 10 : 0;\n"
            + "    tower.PrintLine(\"state={0} bonus={1}\", state, bonus);\n\n"
            + "Goal: use one ?: to produce a value and assign it.\n"
            + "Hint: if-else is for doing many things; ?: is for picking one value. Both branches must share a type (both string or both int).\n"
            + "Check: a 'condition ? value : value' form appears.",
            (raw, code) => ReTernary.IsMatch(code));

        list[21] = Def(
            "任务 22：复合赋值与 ++ / --",
            "Task 22: Compound Assignment & ++ / --",
            "写法：a += b; 等价 a = a + b;（还有 -= *= /= %=）；a++; 等价 a = a + 1;，a--; 就是减 1。\n"
            + "示例代码：\n"
            + "    int shotCount = 0;\n"
            + "    shotCount += 3;        // 0 + 3 = 3\n"
            + "    shotCount *= 2;        // 3 * 2 = 6\n"
            + "    shotCount -= 1;        // 6 - 1 = 5\n"
            + "    shotCount++;           // 5 + 1 = 6\n"
            + "    shotCount--;           // 6 - 1 = 5\n"
            + "    tower.PrintLine(\"shotCount={0}\", shotCount);\n\n"
            + "目标：至少用一次复合赋值（+= -= *= /=），并且出现一次 ++ 或 --。\n"
            + "提示：复合赋值先算右边再写回左边，比写两遍名字更短也更不容易打错；++ 只能用在变量上，不能用在数字上。\n"
            + "判定：代码里出现 += -= *= /= 之一，并且出现 ++ 或 --。",
            "Syntax: a += b; means a = a + b; (also -= *= /= %=); a++; means a = a + 1; and a--; subtracts one.\n"
            + "Example Code:\n"
            + "    int shotCount = 0;\n"
            + "    shotCount += 3;        // 0 + 3 = 3\n"
            + "    shotCount *= 2;        // 3 * 2 = 6\n"
            + "    shotCount -= 1;        // 6 - 1 = 5\n"
            + "    shotCount++;           // 5 + 1 = 6\n"
            + "    shotCount--;           // 6 - 1 = 5\n"
            + "    tower.PrintLine(\"shotCount={0}\", shotCount);\n\n"
            + "Goal: use at least one compound assignment (+= -= *= /=) and one ++ or --.\n"
            + "Hint: a compound assignment computes the right side first and writes it back; ++ only works on a variable, never on a literal.\n"
            + "Check: one of += -= *= /= appears together with ++ or --.",
            (raw, code) => ReCompoundAssign.IsMatch(code) && ReIncrement.IsMatch(code));

        list[22] = Def(
            "任务 23：逻辑运算 && || ! 与短路",
            "Task 23: Logic && || ! and Short-Circuit",
            "写法：&& 并且（两个都真才真）、|| 或者（一个真就真）、! 取反。\n"
            + "示例代码：\n"
            + "    int hpCur = 5;\n"
            + "    bool aliveFlag = hpCur > 0 && hpCur <= 10;   // 活着且在 10 以内\n"
            + "    bool dangerFlag = hpCur <= 0 || hpCur >= 8;  // 死了或快到点\n"
            + "    bool deadFlag = !aliveFlag;                  // 取反\n"
            + "    string nameNull = null;\n"
            + "    bool hasName = nameNull != null && nameNull.Length > 0;  // 先判空，右边才不会炸\n"
            + "    tower.PrintLine(\"alive={0} danger={1} dead={2} hasName={3}\", aliveFlag, dangerFlag, deadFlag, hasName);\n\n"
            + "目标：代码里同时用到 &&、||、! 各一次。\n"
            + "提示：&& 和 || 会短路：&& 左边是 false 就根本不看右边，所以 nameNull != null && nameNull.Length > 0 这个顺序不能颠倒。\n"
            + "判定：代码里同时出现 &&、|| 和（作为取反用的）!。",
            "Syntax: && and, || or, ! not.\n"
            + "Example Code:\n"
            + "    int hpCur = 5;\n"
            + "    bool aliveFlag = hpCur > 0 && hpCur <= 10;   // alive and within 10\n"
            + "    bool dangerFlag = hpCur <= 0 || hpCur >= 8;  // dead or nearly out\n"
            + "    bool deadFlag = !aliveFlag;                  // negate\n"
            + "    string nameNull = null;\n"
            + "    bool hasName = nameNull != null && nameNull.Length > 0;  // null first, or the right side explodes\n"
            + "    tower.PrintLine(\"alive={0} danger={1} dead={2} hasName={3}\", aliveFlag, dangerFlag, deadFlag, hasName);\n\n"
            + "Goal: use &&, || and ! at least once each.\n"
            + "Hint: && and || short-circuit: a false left side means the right side never runs, so keep the null check on the left.\n"
            + "Check: && and || and a negating ! all appear.",
            (raw, code) => ReLogicAnd.IsMatch(code) && ReLogicOr.IsMatch(code) && ReLogicNot.IsMatch(code));

        list[23] = Def(
            "任务 24：字符串的常用成员",
            "Task 24: String Members",
            "写法：字符串是对象，用点号取成员：s.Length（长度，属性不带括号）、s.Contains / s.Split / s.Substring / s.Trim /\n"
            + "s.ToUpper / s.Replace / s.IndexOf，以及 s[0] 取单个字符。静态的 string.Join 把数组拼回一句话。\n"
            + "示例代码：\n"
            + "    string rawLine = \"  fire,speed,ice  \";\n"
            + "    string trimmed = rawLine.Trim();                 // 去掉两端空格\n"
            + "    string[] parts = trimmed.Split(',');             // 按逗号切成数组\n"
            + "    tower.PrintLine(\"len={0}\", trimmed.Length);      // 属性：不带 ()\n"
            + "    tower.PrintLine(\"up={0}\", trimmed.ToUpper());\n"
            + "    tower.PrintLine(\"has={0}\", trimmed.Contains(\"speed\"));\n"
            + "    tower.PrintLine(\"sub={0}\", trimmed.Substring(0, 4));\n"
            + "    tower.PrintLine(\"at={0}\", trimmed.IndexOf(\"speed\"));\n"
            + "    tower.PrintLine(\"rep={0}\", trimmed.Replace(\"ice\", \"water\"));\n"
            + "    tower.PrintLine(\"first={0}\", parts[0]);\n"
            + "    tower.PrintLine(\"join={0}\", string.Join(\"|\", parts));\n\n"
            + "目标：至少调用 2 个字符串成员，并且用 s[i] 下标取一次字符或元素。\n"
            + "提示：字符串一旦造出来就改不了，Trim() / Replace() 都是「返回一个新字符串」，不接住等于白做。\n"
            + "判定：代码里出现字符串成员（Length / Contains / Split / Substring / Trim / ToUpper / Replace / IndexOf / Join）并且出现 s[i] 下标。",
            "Syntax: a string is an object, read members with a dot: s.Length (a property, no parentheses), s.Contains / s.Split /\n"
            + "s.Substring / s.Trim / s.ToUpper / s.Replace / s.IndexOf, and s[0] for one character. Static string.Join glues an array back.\n"
            + "Example Code:\n"
            + "    string rawLine = \"  fire,speed,ice  \";\n"
            + "    string trimmed = rawLine.Trim();                 // strip spaces on both ends\n"
            + "    string[] parts = trimmed.Split(',');             // split into an array\n"
            + "    tower.PrintLine(\"len={0}\", trimmed.Length);      // property: no ()\n"
            + "    tower.PrintLine(\"up={0}\", trimmed.ToUpper());\n"
            + "    tower.PrintLine(\"has={0}\", trimmed.Contains(\"speed\"));\n"
            + "    tower.PrintLine(\"sub={0}\", trimmed.Substring(0, 4));\n"
            + "    tower.PrintLine(\"at={0}\", trimmed.IndexOf(\"speed\"));\n"
            + "    tower.PrintLine(\"rep={0}\", trimmed.Replace(\"ice\", \"water\"));\n"
            + "    tower.PrintLine(\"first={0}\", parts[0]);\n"
            + "    tower.PrintLine(\"join={0}\", string.Join(\"|\", parts));\n\n"
            + "Goal: call at least two string members and use one s[i] indexer.\n"
            + "Hint: strings never change in place - Trim() / Replace() return a new string, so keep the result.\n"
            + "Check: a string member (Length / Contains / Split / Substring / Trim / ToUpper / Replace / IndexOf / Join) and an s[i] index both appear.",
            (raw, code) => ReStrMember.IsMatch(code) && ReStrIndex.IsMatch(code));

        list[24] = Def(
            "任务 25：数组 Length 与嵌套循环",
            "Task 25: Array Length & Nested Loops",
            "写法：数组名.Length 是元素个数（属性，不带括号）；下标从 0 开始，最后一个下标是 Length - 1。\n"
            + "示例代码：\n"
            + "    int[] hitsArr = new int[3];\n"
            + "    for (int i = 0; i < hitsArr.Length; i++) {\n"
            + "        hitsArr[i] = i + 1;                     // 1 2 3\n"
            + "    }\n"
            + "    for (int r = 0; r < 2; r++) {               // 外层：跑 2 轮\n"
            + "        for (int c = 0; c < hitsArr.Length; c++) {  // 内层：把数组走一遍\n"
            + "            tower.PrintLine(\"r{0}c{1}={2}\", r, c, hitsArr[c]);\n"
            + "        }\n"
            + "    }\n\n"
            + "目标：代码里出现数组的 .Length，并且出现 for 里面套 for。\n"
            + "提示：外层变量管「第几遍」，内层变量管「数组第几个」，两个名字不能一样（都用 i 会报同名冲突）。\n"
            + "判定：代码里出现 .Length 且出现一个 for 循环体里还有 for。",
            "Syntax: arrayName.Length is the element count (a property, no parentheses); indexes start at 0, so the last is Length - 1.\n"
            + "Example Code:\n"
            + "    int[] hitsArr = new int[3];\n"
            + "    for (int i = 0; i < hitsArr.Length; i++) {\n"
            + "        hitsArr[i] = i + 1;                     // 1 2 3\n"
            + "    }\n"
            + "    for (int r = 0; r < 2; r++) {               // outer: two passes\n"
            + "        for (int c = 0; c < hitsArr.Length; c++) {  // inner: walk the array\n"
            + "            tower.PrintLine(\"r{0}c{1}={2}\", r, c, hitsArr[c]);\n"
            + "        }\n"
            + "    }\n\n"
            + "Goal: use an array's .Length and put one for loop inside another.\n"
            + "Hint: the outer variable counts passes, the inner one counts elements; never reuse the same name for both.\n"
            + "Check: .Length appears and a for loop contains another for loop.",
            (raw, code) => ReArrayLength.IsMatch(code) && ReNestedFor.IsMatch(code));

        list[25] = Def(
            "任务 26：while (true) 与 break 退出",
            "Task 26: while (true) with break",
            "写法：while (true) { ... } 是「一直循环」，唯一的出口是循环体里的 break;，通常配一个 if 条件。\n"
            + "示例代码：\n"
            + "    int tick = 0;\n"
            + "    while (true) {\n"
            + "        tick++;\n"
            + "        if (tick >= 3) break;          // 出口：没有这句就是死循环\n"
            + "        tower.PrintLine(\"tick={0}\", tick);\n"
            + "    }\n"
            + "    tower.PrintLine(\"done tick={0}\", tick);\n\n"
            + "目标：写一个 while (true) 循环，并用 break 在里面退出。\n"
            + "提示：while (true) 的条件永远是 bool 真值，所以循环本身不会停；break 必须真的会被执行到，否则程序卡在这里出不来。\n"
            + "判定：代码里出现 while (true) 并且出现 break;。",
            "Syntax: while (true) { ... } loops forever; the only way out is a break; inside, normally guarded by an if.\n"
            + "Example Code:\n"
            + "    int tick = 0;\n"
            + "    while (true) {\n"
            + "        tick++;\n"
            + "        if (tick >= 3) break;          // the exit: drop this and it never stops\n"
            + "        tower.PrintLine(\"tick={0}\", tick);\n"
            + "    }\n"
            + "    tower.PrintLine(\"done tick={0}\", tick);\n\n"
            + "Goal: write a while (true) loop and leave it with break.\n"
            + "Hint: while (true) can never become false by itself, so break must actually be reached; otherwise the program is stuck here.\n"
            + "Check: while (true) and break; both appear.",
            (raw, code) => ReWhileTrue.IsMatch(code) && ReBreak.IsMatch(code));

        list[26] = Def(
            "任务 27：类型转换（Parse / TryParse / 强转 / ToString）",
            "Task 27: Conversions (Parse / TryParse / Cast / ToString)",
            "写法：文字转数字用 int.Parse / float.Parse；怕出错用 int.TryParse(文字, out 变量) 返回 true / false；\n"
            + "小数削成整数用 (int) 强转（直接截断，不四舍五入）；数字转文字用 ToString(\"0.00\") 这种格式串。\n"
            + "示例代码：\n"
            + "    string numText = \"42\";\n"
            + "    int numA = int.Parse(numText);          // 文字 -> int\n"
            + "    float numF = float.Parse(\"1.5\");        // 文字 -> float\n"
            + "    int parsedVal;\n"
            + "    bool parsedOk = int.TryParse(\"abc\", out parsedVal);   // 失败返回 false，不抛异常\n"
            + "    int cutInt = (int)3.9f;                 // 强转：3.9 变成 3\n"
            + "    string numTxt = numF.ToString(\"0.00\");   // 1.5 -> \"1.50\"\n"
            + "    tower.PrintLine(\"a={0} ok={1} cut={2} txt={3}\", numA, parsedOk, cutInt, numTxt);\n\n"
            + "目标：代码里出现 Parse / TryParse 调用，并且出现 (int) 这类强制转换。\n"
            + "提示：Parse 遇到不合法的文字会当场抛异常并把这一帧打断；TryParse 只回一个 true / false，玩家输入一律用它。\n"
            + "判定：代码里出现 int.Parse / int.TryParse / float.Parse，并且出现 (int) 或 (float) 强转。",
            "Syntax: text to number with int.Parse / float.Parse; the safe way is int.TryParse(text, out var) returning true / false;\n"
            + "shave a decimal with an (int) cast (truncates, does not round); number to text with ToString(\"0.00\").\n"
            + "Example Code:\n"
            + "    string numText = \"42\";\n"
            + "    int numA = int.Parse(numText);          // text -> int\n"
            + "    float numF = float.Parse(\"1.5\");        // text -> float\n"
            + "    int parsedVal;\n"
            + "    bool parsedOk = int.TryParse(\"abc\", out parsedVal);   // false instead of an exception\n"
            + "    int cutInt = (int)3.9f;                 // cast: 3.9 becomes 3\n"
            + "    string numTxt = numF.ToString(\"0.00\");   // 1.5 -> \"1.50\"\n"
            + "    tower.PrintLine(\"a={0} ok={1} cut={2} txt={3}\", numA, parsedOk, cutInt, numTxt);\n\n"
            + "Goal: call Parse / TryParse and use an (int) style cast.\n"
            + "Hint: Parse throws and aborts the frame on bad text; TryParse only answers true / false - always use it for player input.\n"
            + "Check: int.Parse / int.TryParse / float.Parse and an (int) or (float) cast appear.",
            (raw, code) => ReParseCall.IsMatch(code) && ReCastExpr.IsMatch(code));

        list[27] = Def(
            "任务 28：try / catch / finally 与 null 判空",
            "Task 28: try / catch / finally & null Checks",
            "写法：try { 可能出事的代码 } catch (Exception e) { 出事时走这里 } finally { 不管出不出事都走这里 }；\n"
            + "判断「这个变量有没有指向对象」永远用 x == null / x != null，判过再用它的成员。\n"
            + "示例代码：\n"
            + "    string rawNum = \"abc\";\n"
            + "    int parsedVal2 = 0;\n"
            + "    try {\n"
            + "        parsedVal2 = int.Parse(rawNum);\n"
            + "        tower.PrintLine(\"ok {0}\", parsedVal2);\n"
            + "    } catch (Exception e) {\n"
            + "        tower.PrintLine(\"failed: {0}\", e.Message);\n"
            + "        parsedVal2 = -1;\n"
            + "    } finally {\n"
            + "        tower.PrintLine(\"parsedVal2={0}\", parsedVal2);\n"
            + "    }\n"
            + "    string tagNull = null;\n"
            + "    if (tagNull == null) tower.PrintLine(\"tag is null\");\n"
            + "    tower.PrintLine(\"len={0}\", tagNull == null ? 0 : tagNull.Length);\n\n"
            + "目标：代码里同时出现 try、catch，并且对某个变量做了 == null / != null 判断。\n"
            + "提示：catch 里至少要打印或兜一个默认值，空着 catch 等于把问题藏起来；finally 不管前面怎么跳都会执行。\n"
            + "判定：代码里出现 try { ... } catch，并且出现与 null 的比较。",
            "Syntax: try { risky code } catch (Exception e) { handle it } finally { always runs };\n"
            + "test an object with x == null / x != null before touching its members.\n"
            + "Example Code:\n"
            + "    string rawNum = \"abc\";\n"
            + "    int parsedVal2 = 0;\n"
            + "    try {\n"
            + "        parsedVal2 = int.Parse(rawNum);\n"
            + "        tower.PrintLine(\"ok {0}\", parsedVal2);\n"
            + "    } catch (Exception e) {\n"
            + "        tower.PrintLine(\"failed: {0}\", e.Message);\n"
            + "        parsedVal2 = -1;\n"
            + "    } finally {\n"
            + "        tower.PrintLine(\"parsedVal2={0}\", parsedVal2);\n"
            + "    }\n"
            + "    string tagNull = null;\n"
            + "    if (tagNull == null) tower.PrintLine(\"tag is null\");\n"
            + "    tower.PrintLine(\"len={0}\", tagNull == null ? 0 : tagNull.Length);\n\n"
            + "Goal: have try, catch and a == null / != null test in the same code.\n"
            + "Hint: a catch that does nothing simply hides the bug; finally runs no matter how you leave the try.\n"
            + "Check: try { ... } catch and a comparison against null both appear.",
            (raw, code) => ReTryCatch.IsMatch(code) && ReNullCompare.IsMatch(code));

        list[28] = Def(
            "任务 29：Dictionary 字典",
            "Task 29: Dictionary",
            "写法：Dictionary<键类型, 值类型> 名字 = new Dictionary<键类型, 值类型>();\n"
            + "· 写 / 改：dict[键] = 值;（键不存在就是新增）· 判定存在：dict.ContainsKey(键)\n"
            + "· 安全取值：dict.TryGetValue(键, out 变量) 返回 true / false · 数量：dict.Count · 遍历：KeyValuePair 的 .Key / .Value\n"
            + "示例代码：\n"
            + "    Dictionary<string, int> hpMap = new Dictionary<string, int>();\n"
            + "    hpMap[\"fire\"] = 3;\n"
            + "    hpMap[\"ice\"] = 1;\n"
            + "    if (!hpMap.ContainsKey(\"wind\")) hpMap[\"wind\"] = 0;\n"
            + "    int fireHp;\n"
            + "    bool hasFire = hpMap.TryGetValue(\"fire\", out fireHp);\n"
            + "    tower.PrintLine(\"count={0} fire={1} has={2}\", hpMap.Count, fireHp, hasFire);\n"
            + "    foreach (KeyValuePair<string, int> kv in hpMap) {\n"
            + "        tower.PrintLine(\"{0}={1}\", kv.Key, kv.Value);\n"
            + "    }\n\n"
            + "目标：造一个 Dictionary，用下标写至少一对键值，并读出其中一个。\n"
            + "提示：字典是「按名字查值」的抽屉柜，顺序不重要；List 是按位置找，字典是按键找，别混用。\n"
            + "判定：代码里出现 new Dictionary<...> 并且出现 dict[键] = 值 这种下标赋值。",
            "Syntax: Dictionary<KeyType, ValueType> map = new Dictionary<KeyType, ValueType>();\n"
            + "- write / update: map[key] = value; (adds when the key is new) - exists: map.ContainsKey(key)\n"
            + "- safe read: map.TryGetValue(key, out value) returning true / false - size: map.Count - loop with KeyValuePair .Key / .Value\n"
            + "Example Code:\n"
            + "    Dictionary<string, int> hpMap = new Dictionary<string, int>();\n"
            + "    hpMap[\"fire\"] = 3;\n"
            + "    hpMap[\"ice\"] = 1;\n"
            + "    if (!hpMap.ContainsKey(\"wind\")) hpMap[\"wind\"] = 0;\n"
            + "    int fireHp;\n"
            + "    bool hasFire = hpMap.TryGetValue(\"fire\", out fireHp);\n"
            + "    tower.PrintLine(\"count={0} fire={1} has={2}\", hpMap.Count, fireHp, hasFire);\n"
            + "    foreach (KeyValuePair<string, int> kv in hpMap) {\n"
            + "        tower.PrintLine(\"{0}={1}\", kv.Key, kv.Value);\n"
            + "    }\n\n"
            + "Goal: build a Dictionary, write at least one key with the indexer and read one value back.\n"
            + "Hint: a dictionary looks values up by name; a List looks them up by position - do not mix the two.\n"
            + "Check: new Dictionary<...> and a map[key] = value assignment both appear.",
            (raw, code) => ReDictNew.IsMatch(code) && ReDictSet.IsMatch(code));

        list[29] = Def(
            "任务 30：enum 枚举",
            "Task 30: enum",
            "写法：enum 名字 { 成员 = 值, 成员 = 值 } 定义一组有名字的常数；使用时写 类型.成员。\n"
            + "注意：方法体里不允许声明类型，所以本任务的 enum 要写进 GlobalCode 块（它会被搬到类里），粘贴前先清空编辑器。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        enum TowerState { Idle = 0, Scan = 1, Fire = 2 }\n"
            + "    }\n"
            + "    TowerState st = TowerState.Scan;\n"
            + "    switch (st) {\n"
            + "        case TowerState.Idle: tower.PrintLine(\"idle\"); break;\n"
            + "        case TowerState.Scan: tower.PrintLine(\"scan\"); break;\n"
            + "        default: tower.PrintLine(\"other\"); break;\n"
            + "    }\n\n"
            + "目标：自己定义一个 enum，并用 switch 按它的成员分支。\n"
            + "提示：enum 把「0 / 1 / 2」这种魔法数字换成看得懂的名字，代码里就不会再出现猜数字含义的情况。\n"
            + "判定：代码里出现 enum 定义，并且出现 switch 使用它。",
            "Syntax: enum Name { Member = value, Member = value } declares named constants; use them as Type.Member.\n"
            + "Note: a method body cannot contain a type declaration, so this enum goes inside the GlobalCode block (hoisted into the class) - clear the editor before pasting.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        enum TowerState { Idle = 0, Scan = 1, Fire = 2 }\n"
            + "    }\n"
            + "    TowerState st = TowerState.Scan;\n"
            + "    switch (st) {\n"
            + "        case TowerState.Idle: tower.PrintLine(\"idle\"); break;\n"
            + "        case TowerState.Scan: tower.PrintLine(\"scan\"); break;\n"
            + "        default: tower.PrintLine(\"other\"); break;\n"
            + "    }\n\n"
            + "Goal: define an enum and branch on it with switch.\n"
            + "Hint: an enum replaces magic numbers with readable names, so nobody has to guess what 2 means.\n"
            + "Check: an enum definition and a switch that uses it both appear.",
            (raw, code) => ReEnumDef.IsMatch(code) && ReSwitch.IsMatch(code));

        list[30] = Def(
            "任务 31：类进阶（构造函数 / 属性 / static）",
            "Task 31: Classes (constructor / property / static)",
            "写法：类 = 字段（存数据）+ 属性（带 get / set 门卫的字段）+ 构造函数（与类同名、new 时自动跑、无返回类型）+ static（属于类、全体共用一份）。\n"
            + "注意：类声明不能写在方法体里，要写进 GlobalCode 块；粘贴前先清空编辑器（全局区只允许一个）。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        class Dart {\n"
            + "            public float Speed;                  // 字段：直接能读能写\n"
            + "            private int hits = 0;                // 私有字段：只有类内部能用\n"
            + "            public int Hits {                    // 属性：外面只能读\n"
            + "                get { return hits; }\n"
            + "                private set { hits = value; }\n"
            + "            }\n"
            + "            public static int Total = 0;         // 静态：所有 Dart 共用一份\n"
            + "            public Dart(float speed) {           // 构造函数：名字 = 类名，没有返回类型\n"
            + "                Speed = speed;\n"
            + "                Total = Total + 1;\n"
            + "            }\n"
            + "            public void Hit() { hits = hits + 1; }\n"
            + "        }\n"
            + "    }\n"
            + "    Dart a = new Dart(2.5f);\n"
            + "    Dart b = new Dart(1f);\n"
            + "    a.Hit();\n"
            + "    tower.PrintLine(\"speed={0} hits={1} total={2}\", a.Speed, a.Hits, Dart.Total);\n\n"
            + "目标：自己写一个 class，里面有构造函数、带 get / set 的属性，并且用到 static 成员。\n"
            + "提示：字段是「敞开的口袋」，属性是「带门卫的口袋」：想在校验 / 只读 / 统计访问时用属性，纯装数据用字段。\n"
            + "判定：代码里出现自己写的 class，并且类里有带 get / set 的属性。",
            "Syntax: a class = fields (data) + properties (fields with a get / set gate) + constructors (same name as the class, no return type, runs on new) + static members (owned by the class, shared by all).\n"
            + "Note: type declarations cannot live in a method body, so put the class inside GlobalCode; clear the editor first (only one block allowed).\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        class Dart {\n"
            + "            public float Speed;                  // field: readable and writable\n"
            + "            private int hits = 0;                // private field: only this class\n"
            + "            public int Hits {                    // property: read-only outside\n"
            + "                get { return hits; }\n"
            + "                private set { hits = value; }\n"
            + "            }\n"
            + "            public static int Total = 0;         // static: one copy for every Dart\n"
            + "            public Dart(float speed) {           // constructor: name = class name, no return type\n"
            + "                Speed = speed;\n"
            + "                Total = Total + 1;\n"
            + "            }\n"
            + "            public void Hit() { hits = hits + 1; }\n"
            + "        }\n"
            + "    }\n"
            + "    Dart a = new Dart(2.5f);\n"
            + "    Dart b = new Dart(1f);\n"
            + "    a.Hit();\n"
            + "    tower.PrintLine(\"speed={0} hits={1} total={2}\", a.Speed, a.Hits, Dart.Total);\n\n"
            + "Goal: write your own class with a constructor, a get / set property and one static member.\n"
            + "Hint: a field is an open pocket, a property is a guarded one - use a property when you need validation, read-only access or counting.\n"
            + "Check: your own class appears and it contains a property with get / set.",
            (raw, code) => ReLocalClass.IsMatch(code) && RePropGet.IsMatch(code) && RePropSet.IsMatch(code));

        list[31] = Def(
            "任务 32：方法进阶（重载 / 可选参数 / out / ref / 递归）",
            "Task 32: Methods (overload / optional / out / ref / recursion)",
            "写法：同名不同参数 = 重载；参数给默认值 = 可选参数（调用时可省）；out 由方法负责赋值、ref 由调用方先赋值；\n"
            + "方法里调用自己 = 递归，但必须有终止条件。\n"
            + "注意：重载必须写在类里（方法体内不能写同名不同参的两个方法），所以整块声明放进 GlobalCode 块。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        int Pow2(int n) { return n * n; }                     // 重载 1：int 版\n"
            + "        float Pow2(float n) { return n * n; }                 // 重载 2：float 版\n"
            + "        int Add(int a, int b = 10) { return a + b; }          // 可选参数：不传 b 就用 10\n"
            + "        void SplitHalf(int total, out int half, ref int rest) {  // out 必赋值，ref 进来先有值\n"
            + "            half = total / 2;\n"
            + "            rest = rest + total % 2;\n"
            + "        }\n"
            + "        int Fact(int n) { return n <= 1 ? 1 : n * Fact(n - 1); }  // 递归：自己调自己\n"
            + "    }\n"
            + "    int powInt = Pow2(3);\n"
            + "    float powFloat = Pow2(2.5f);\n"
            + "    int optSum = Add(5);\n"
            + "    int half = 0;\n"
            + "    int rest = 0;\n"
            + "    SplitHalf(9, out half, ref rest);\n"
            + "    tower.PrintLine(\"{0} {1} {2} {3} {4} {5}\", powInt, powFloat, optSum, half, rest, Fact(4));\n\n"
            + "目标：定义带 out 或 ref 参数的方法，并写一个方法自己调用自己（递归）。\n"
            + "提示：重载靠「参数列表不同」区分，只改返回类型不行；递归一定要先写终止条件，否则栈会被压爆。\n"
            + "判定：代码里有方法定义且用了 out / ref 参数，并且出现方法内调用自己。",
            "Syntax: same name with different parameters = overload; a default value = optional parameter (can be omitted); out is assigned by the method, ref must be set by the caller; a method calling itself = recursion, which needs a stopping condition.\n"
            + "Note: overloads must live in a class (a method body cannot hold two same-named methods), so the declarations go inside GlobalCode.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        int Pow2(int n) { return n * n; }                     // overload 1: int\n"
            + "        float Pow2(float n) { return n * n; }                 // overload 2: float\n"
            + "        int Add(int a, int b = 10) { return a + b; }          // optional: b defaults to 10\n"
            + "        void SplitHalf(int total, out int half, ref int rest) {  // out is assigned, ref arrives filled\n"
            + "            half = total / 2;\n"
            + "            rest = rest + total % 2;\n"
            + "        }\n"
            + "        int Fact(int n) { return n <= 1 ? 1 : n * Fact(n - 1); }  // recursion: calls itself\n"
            + "    }\n"
            + "    int powInt = Pow2(3);\n"
            + "    float powFloat = Pow2(2.5f);\n"
            + "    int optSum = Add(5);\n"
            + "    int half = 0;\n"
            + "    int rest = 0;\n"
            + "    SplitHalf(9, out half, ref rest);\n"
            + "    tower.PrintLine(\"{0} {1} {2} {3} {4} {5}\", powInt, powFloat, optSum, half, rest, Fact(4));\n\n"
            + "Goal: define a method with an out or ref parameter and write one recursive method.\n"
            + "Hint: overloads differ by parameter list, never by return type alone; a recursion without a base case blows the stack.\n"
            + "Check: a method definition with out / ref and a method that calls itself both appear.",
            (raw, code) => ReMethodDef.IsMatch(code) && ReOutRefParam.IsMatch(code) && ReSelfRecursion.IsMatch(code));

        list[32] = Def(
            "任务 33：多维数组（int[,] 与 int[][]）",
            "Task 33: Multi-dimensional Arrays",
            "写法：矩形二维数组 int[,] 一个方括号里写两个下标（grid[行, 列]）；交错数组 int[][] 每行长度可以不同，要逐行 new。\n"
            + "示例代码：\n"
            + "    int[,] grid2d = new int[2, 3];        // 2 行 3 列，一共 6 个格子\n"
            + "    grid2d[0, 0] = 1;\n"
            + "    grid2d[1, 2] = 5;\n"
            + "    tower.PrintLine(\"rows={0} cols={1} all={2}\", grid2d.GetLength(0), grid2d.GetLength(1), grid2d.Length);\n"
            + "    int[][] jagArr = new int[2][];        // 先开 2 行「壳子」\n"
            + "    jagArr[0] = new int[3];\n"
            + "    jagArr[1] = new int[2];\n"
            + "    for (int r2 = 0; r2 < jagArr.Length; r2++) {\n"
            + "        for (int c2 = 0; c2 < jagArr[r2].Length; c2++) {\n"
            + "            jagArr[r2][c2] = r2 + c2;\n"
            + "            tower.PrintLine(\"j{0}{1}={2}\", r2, c2, jagArr[r2][c2]);\n"
            + "        }\n"
            + "    }\n\n"
            + "目标：代码里出现二维数组的创建（new int[2,3] 或 new int[2][]），并按下标访问它。\n"
            + "提示：矩形数组的行列数用 GetLength(0) / GetLength(1)；Length 在二维里是总格子数，不是行数。\n"
            + "判定：代码里出现 new int[a,b] 或 new int[a][] 这类二维创建，并出现双下标访问。",
            "Syntax: a rectangular array int[,] takes two indexes in one bracket (grid[row, col]); a jagged array int[][] lets each row differ, so every row is new'd separately.\n"
            + "Example Code:\n"
            + "    int[,] grid2d = new int[2, 3];        // 2 rows x 3 cols = 6 cells\n"
            + "    grid2d[0, 0] = 1;\n"
            + "    grid2d[1, 2] = 5;\n"
            + "    tower.PrintLine(\"rows={0} cols={1} all={2}\", grid2d.GetLength(0), grid2d.GetLength(1), grid2d.Length);\n"
            + "    int[][] jagArr = new int[2][];        // two empty row slots\n"
            + "    jagArr[0] = new int[3];\n"
            + "    jagArr[1] = new int[2];\n"
            + "    for (int r2 = 0; r2 < jagArr.Length; r2++) {\n"
            + "        for (int c2 = 0; c2 < jagArr[r2].Length; c2++) {\n"
            + "            jagArr[r2][c2] = r2 + c2;\n"
            + "            tower.PrintLine(\"j{0}{1}={2}\", r2, c2, jagArr[r2][c2]);\n"
            + "        }\n"
            + "    }\n\n"
            + "Goal: create a 2D array (new int[2,3] or new int[2][]) and read or write it with indexes.\n"
            + "Hint: rows and cols of a rectangular array come from GetLength(0) / GetLength(1); Length is the total cell count, not the row count.\n"
            + "Check: a 2D creation (new int[a,b] or new int[a][]) and a double-index access both appear.",
            (raw, code) => ReMultiArray.IsMatch(code) && ReMultiIndex.IsMatch(code));

        list[33] = Def(
            "任务 34：索引器（让对象像数组一样用）",
            "Task 34: Indexers (use an object like an array)",
            "写法：在类里写 类型 this[下标] { get { ... } set { ... } }，这个 this[...] 就叫索引器，外面就能用 bag[0] 取值、赋值。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        class Bag {\n"
            + "            private int[] items = new int[3];\n"
            + "            public int this[int i] {\n"
            + "                get { return items[i]; }\n"
            + "                set { items[i] = value; }\n"
            + "            }\n"
            + "        }\n"
            + "    }\n"
            + "    Bag bag = new Bag();\n"
            + "    bag[0] = 7;\n"
            + "    bag[1] = 9;\n"
            + "    tower.PrintLine(\"bag[0]={0} bag[1]={1}\", bag[0], bag[1]);\n\n"
            + "目标：自己写一个带 this[...] 的索引器，get 与 set 都要写，然后在下面用 bag[0] 取值或赋值。\n"
            + "提示：索引器没有名字，它的名字就是 this；set 里的 value 是自动出现的关键字，等于右边赋进来的值。\n"
            + "判定：代码里出现 this[...] { ... } 形式的索引器，并且 get / set 都在。",
            "Syntax: inside a class write  type this[index] { get { ... } set { ... } } - that this[...] member is an indexer, so from outside you can read and write obj[0].\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        class Bag {\n"
            + "            private int[] items = new int[3];\n"
            + "            public int this[int i] {\n"
            + "                get { return items[i]; }\n"
            + "                set { items[i] = value; }\n"
            + "            }\n"
            + "        }\n"
            + "    }\n"
            + "    Bag bag = new Bag();\n"
            + "    bag[0] = 7;\n"
            + "    bag[1] = 9;\n"
            + "    tower.PrintLine(\"bag[0]={0} bag[1]={1}\", bag[0], bag[1]);\n\n"
            + "Goal: declare your own this[...] indexer with both get and set, then read or write it from outside with bag[0].\n"
            + "Hint: an indexer has no name - its name is this; value inside set is the implicit keyword holding the assigned value.\n"
            + "Check: a this[...] { ... } indexer with get and set both present appears.",
            (raw, code) => ReIndexerDecl.IsMatch(code) && RePropGet.IsMatch(code) && RePropSet.IsMatch(code));

        list[34] = Def(
            "任务 35：接口与多态（interface / 实现 / 父类型装子类）",
            "Task 35: Interfaces and Polymorphism",
            "写法：interface 只写「要有什么」，不写怎么做；class 名 : 接口名 表示这个类实现了它，必须把接口里的成员全部写出来；\n"
            + "用接口类型来装实现类的对象，调用时实际跑的是那个类的版本，这就是多态。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        interface IHittable {\n"
            + "            int Hp { get; }\n"
            + "            void Hit(int dmg);\n"
            + "        }\n"
            + "        class Dummy : IHittable {\n"
            + "            public int Hp { get; private set; }\n"
            + "            public Dummy(int hp) { Hp = hp; }\n"
            + "            public void Hit(int dmg) { Hp -= dmg; }\n"
            + "        }\n"
            + "    }\n"
            + "    IHittable target = new Dummy(10);\n"
            + "    target.Hit(3);\n"
            + "    tower.PrintLine(\"hp={0}\", target.Hp);\n\n"
            + "目标：写一个 interface，再写一个用 : 实现它的 class，并且用接口类型去调用成员。\n"
            + "提示：实现接口的成员必须是 public；接口里不能有字段、不能有实现体（C# 8 之后有默认实现，这个项目不用）。\n"
            + "判定：代码里既有 interface 声明，也有 class X : Y 这样的实现写法。",
            "Syntax: an interface only says what must exist, never how; class Name : IName means the class implements it and must define every member.\n"
            + "Hold the object in a variable of the interface type and the call still runs the class version - that is polymorphism.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        interface IHittable {\n"
            + "            int Hp { get; }\n"
            + "            void Hit(int dmg);\n"
            + "        }\n"
            + "        class Dummy : IHittable {\n"
            + "            public int Hp { get; private set; }\n"
            + "            public Dummy(int hp) { Hp = hp; }\n"
            + "            public void Hit(int dmg) { Hp -= dmg; }\n"
            + "        }\n"
            + "    }\n"
            + "    IHittable target = new Dummy(10);\n"
            + "    target.Hit(3);\n"
            + "    tower.PrintLine(\"hp={0}\", target.Hp);\n\n"
            + "Goal: declare an interface, implement it with class X : Y, and call the members through the interface type.\n"
            + "Hint: members that implement an interface must be public; an interface holds no fields and no bodies.\n"
            + "Check: an interface declaration and a class X : Y implementation both appear.",
            (raw, code) => ReInterfaceDecl.IsMatch(code) && ReBaseList.IsMatch(code));

        list[35] = Def(
            "任务 36：委托 / Action / Func（把方法当参数传）",
            "Task 36: Delegates, Action and Func",
            "写法：委托就是「装方法的变量」。Action<T> 装没有返回值的方法，Func<T, TResult> 装最后一个类型是返回值的方法。\n"
            + "赋值时可以填方法名，也可以填自定义 delegate 类型的实例；调用委托就等于调用它装的那个方法。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        class Calc {\n"
            + "            public int Add(int a, int b) { return a + b; }\n"
            + "        }\n"
            + "    }\n"
            + "    Calc calc = new Calc();\n"
            + "    Func<int, int, int> add = calc.Add;\n"
            + "    Action<string> show = msg => tower.PrintLine(msg);\n"
            + "    show(\"delegate ready\");\n"
            + "    tower.PrintLine(\"2+3={0}\", add(2, 3));\n\n"
            + "目标：声明一个 Action 或 Func 变量并赋值，然后调用它。\n"
            + "提示：Func<int,int,int> 的最后一个 int 是返回值类型，前面的是参数类型；Action 没有返回值所以只有参数类型。\n"
            + "判定：代码里出现 Action<...> 或 Func<...> 变量并给它赋值。",
            "Syntax: a delegate is a variable that holds a method. Action<T> holds a void method; Func<T, TResult> holds a method whose last type argument is the return type.\n"
            + "You can assign a method name (or a lambda), and calling the delegate calls the method inside it.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        class Calc {\n"
            + "            public int Add(int a, int b) { return a + b; }\n"
            + "        }\n"
            + "    }\n"
            + "    Calc calc = new Calc();\n"
            + "    Func<int, int, int> add = calc.Add;\n"
            + "    Action<string> show = msg => tower.PrintLine(msg);\n"
            + "    show(\"delegate ready\");\n"
            + "    tower.PrintLine(\"2+3={0}\", add(2, 3));\n\n"
            + "Goal: declare an Action or Func variable, assign it, then call it.\n"
            + "Hint: in Func<int,int,int> the last int is the return type and the others are parameters; Action has parameters only, no return type.\n"
            + "Check: an Action<...> or Func<...> variable with an assignment appears.",
            (raw, code) => ReDelegateAssign.IsMatch(code));

        list[36] = Def(
            "任务 37：Lambda 表达式（x => 的结果）",
            "Task 37: Lambda Expressions",
            "写法：参数 => 表达式 就是 Lambda，它是一段「还没起名字的方法」，最常直接塞进 Action / Func 里用。\n"
            + "无参数写 () => ...；两个及以上参数要加括号 (a, b) => ...；需要多行就用 { } 包起来，并写 return。\n"
            + "示例代码：\n"
            + "    Func<int, int> sq = x => x * x;\n"
            + "    Action<string> log = m => tower.PrintLine(m);\n"
            + "    log(\"lambda ready\");\n"
            + "    int[] nums = new int[] { 1, 2, 3 };\n"
            + "    for (int i = 0; i < nums.Length; i++) {\n"
            + "        tower.PrintLine(\"{0}^2={1}\", nums[i], sq(nums[i]));\n"
            + "    }\n\n"
            + "目标：写一个 Lambda 赋给 Action 或 Func，并真的调用它。\n"
            + "提示：=> 左边是参数、右边是返回值；单表达式不用写 return，写成 { } 就必须写 return。\n"
            + "判定：代码里出现 = x => ... 或 = (a, b) => ... 这样的 Lambda 赋值。",
            "Syntax:  parameter => expression  is a lambda - an unnamed method, most often handed straight to an Action or Func.\n"
            + "No parameters: () => ...; two or more: (a, b) => ...; several statements: wrap in { } and write return.\n"
            + "Example Code:\n"
            + "    Func<int, int> sq = x => x * x;\n"
            + "    Action<string> log = m => tower.PrintLine(m);\n"
            + "    log(\"lambda ready\");\n"
            + "    int[] nums = new int[] { 1, 2, 3 };\n"
            + "    for (int i = 0; i < nums.Length; i++) {\n"
            + "        tower.PrintLine(\"{0}^2={1}\", nums[i], sq(nums[i]));\n"
            + "    }\n\n"
            + "Goal: write a lambda, assign it to an Action or Func, and actually call it.\n"
            + "Hint: left of => are the parameters, right is the result; a single expression needs no return, a { } body must return.\n"
            + "Check: a lambda assignment such as = x => ... or = (a, b) => ... appears.",
            (raw, code) => ReLambda.IsMatch(code));

        list[37] = Def(
            "任务 38：LINQ 基础（Where / Select / Count / Any / Sum）",
            "Task 38: LINQ Basics (Where / Select / Count / Any / Sum)",
            "写法：对 List 点一个 . 后面接 Where / Select / Count / Any / Sum，就能一行完成「筛选 / 变形 / 计数 / 判断有没有 / 求和」。\n"
            + "Where 留下来的是满足条件的元素，通常再 .ToList() 收成一个新 List；Any 有就返回 true，Sum 直接给出总和。\n"
            + "示例代码：\n"
            + "    List<int> hps = new List<int>();\n"
            + "    hps.Add(3);\n"
            + "    hps.Add(7);\n"
            + "    hps.Add(0);\n"
            + "    hps.Add(12);\n"
            + "    List<int> alive = hps.Where(v => v > 0).ToList();\n"
            + "    int total = alive.Sum();\n"
            + "    int best = hps.Max();\n"
            + "    tower.PrintLine(\"alive={0} total={1} best={2}\", alive.Count, total, best);\n"
            + "    if (hps.Any(v => v >= 10)) tower.PrintLine(\"has big one\");\n\n"
            + "目标：用 Where / Select / Count / Any / Sum 里至少两个，并且真的把结果打印出来。\n"
            + "提示：LINQ 用的是 Lambda（v => 条件）；Where 返回的不是 List 而是「可继续串的序列」，要存成 List 记得 .ToList()。\n"
            + "判定：代码里出现 .Where(...) / .Select(...) / .Any(...) / .OrderBy(...) 这类带 Lambda 的 LINQ 调用。",
            "Syntax: put a . after a List and call Where / Select / Count / Any / Sum - filter, transform, count, test and add up in a single line.\n"
            + "Where keeps the elements that pass, usually followed by .ToList(); Any answers true or false; Sum returns the total.\n"
            + "Example Code:\n"
            + "    List<int> hps = new List<int>();\n"
            + "    hps.Add(3);\n"
            + "    hps.Add(7);\n"
            + "    hps.Add(0);\n"
            + "    hps.Add(12);\n"
            + "    List<int> alive = hps.Where(v => v > 0).ToList();\n"
            + "    int total = alive.Sum();\n"
            + "    int best = hps.Max();\n"
            + "    tower.PrintLine(\"alive={0} total={1} best={2}\", alive.Count, total, best);\n"
            + "    if (hps.Any(v => v >= 10)) tower.PrintLine(\"has big one\");\n\n"
            + "Goal: use at least two of Where / Select / Count / Any / Sum and actually print the result.\n"
            + "Hint: LINQ takes lambdas (v => condition); Where returns a chainable sequence, so call .ToList() when you want a real List.\n"
            + "Check: a LINQ call with a lambda such as .Where(...) / .Select(...) / .Any(...) / .OrderBy(...) appears.",
            (raw, code) => ReLinqCall.IsMatch(code) && Regex.IsMatch(code, @"\bnew\s+List\s*<"));

        list[38] = Def(
            "任务 39：泛型与泛型约束（<T> 与 where）",
            "Task 39: Generics and Constraints",
            "写法：在类名或方法名后面加 <T>，T 就是「待定的类型」，用的时候才决定（Box<string>、Box<int>）。\n"
            + "where T : class 限定 T 只能是类（引用类型），where T : struct 限定只能是值类型，where T : new() 要求能 new 出来。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        class Box<T> where T : class {\n"
            + "            private T value;\n"
            + "            public void Put(T v) { value = v; }\n"
            + "            public T Get() { return value; }\n"
            + "        }\n"
            + "    }\n"
            + "    Box<string> box = new Box<string>();\n"
            + "    box.Put(\"hello\");\n"
            + "    tower.PrintLine(\"box={0}\", box.Get());\n\n"
            + "目标：自己写一个带 <T> 的类或方法，并且写上 where 约束，然后用具体类型 new 出来用。\n"
            + "提示：T 只是一个占位名字，可以叫 TKey / TValue；同一个泛型类可以分别装 string 和 int，各用各的类型。\n"
            + "判定：代码里出现 <T> 的泛型声明，并且出现 where T : 约束。",
            "Syntax: append <T> to a class or method name; T is a type placeholder decided at use time (Box<string>, Box<int>).\n"
            + "where T : class limits T to reference types, where T : struct to value types, where T : new() requires a parameterless constructor.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        class Box<T> where T : class {\n"
            + "            private T value;\n"
            + "            public void Put(T v) { value = v; }\n"
            + "            public T Get() { return value; }\n"
            + "        }\n"
            + "    }\n"
            + "    Box<string> box = new Box<string>();\n"
            + "    box.Put(\"hello\");\n"
            + "    tower.PrintLine(\"box={0}\", box.Get());\n\n"
            + "Goal: write your own class or method with <T> plus a where constraint, then use it with a concrete type.\n"
            + "Hint: T is only a placeholder name (TKey / TValue are fine); the same generic class can hold string and int separately.\n"
            + "Check: a <T> generic declaration and a where T : constraint both appear.",
            (raw, code) => (ReGenericClassDecl.IsMatch(code) || ReGenericMethodDecl.IsMatch(code))
                && ReWhereConstraint.IsMatch(code));

        list[39] = Def(
            "任务 40：命名空间与全限定名（System. 开头）",
            "Task 40: Namespaces and Fully Qualified Names",
            "写法：C# 把类型按「命名空间」分房间，System 是最常用的那个，System.Collections.Generic 里放 List / Dictionary。\n"
            + "顶部写 using System; 之后可以直接写 Math / DateTime；不写 using 时，就要写全名 System.Math / System.DateTime，这个全名就叫全限定名。\n"
            + "示例代码（编辑器模板已经替你写好了 using UnityEngine / System / System.Collections.Generic，所以这两行都能编译）：\n"
            + "    System.Collections.Generic.List<int> hps = new System.Collections.Generic.List<int>();\n"
            + "    hps.Add(3);\n"
            + "    tower.PrintLine(\"max={0} count={1}\", System.Math.Max(2, 9), hps.Count);\n"
            + "    System.DateTime now = System.DateTime.Now;\n"
            + "    tower.PrintLine(\"year={0} minute={1}\", now.Year, now.Minute);\n\n"
            + "目标：至少两次用 System. 开头的全限定名来访问类型或静态成员。\n"
            + "提示：命名空间用点分层级，越具体越靠右；同名类型冲突时，全限定名就是唯一不会搞错的那种写法。\n"
            + "判定：代码里出现两处以上 System.xxx 形式的全限定名。",
            "Syntax: C# groups types into namespaces; System is the everyday one and System.Collections.Generic holds List and Dictionary.\n"
            + "With using System; at the top you may write Math / DateTime directly; without it you need the full name System.Math / System.DateTime - that is the fully qualified name.\n"
            + "Example Code (the editor template already emits using UnityEngine / System / System.Collections.Generic, so both lines compile):\n"
            + "    System.Collections.Generic.List<int> hps = new System.Collections.Generic.List<int>();\n"
            + "    hps.Add(3);\n"
            + "    tower.PrintLine(\"max={0} count={1}\", System.Math.Max(2, 9), hps.Count);\n"
            + "    System.DateTime now = System.DateTime.Now;\n"
            + "    tower.PrintLine(\"year={0} minute={1}\", now.Year, now.Minute);\n\n"
            + "Goal: touch a type or static member twice or more through a fully qualified System. name.\n"
            + "Hint: namespaces are dot-separated paths, the more specific part on the right; when two types share a name the full name is the only unambiguous spelling.\n"
            + "Check: two or more fully qualified System.xxx names appear.",
            (raw, code) => Regex.Matches(code, @"\bSystem\s*\.").Count >= 2 && ReQualifiedName.IsMatch(code));

        list[40] = Def(
            "任务 41：预处理指令（#region / #if / #endif）",
            "Task 41: Preprocessor Directives (#region / #if / #endif)",
            "写法：# 开头的这些行在「编译之前」就被处理掉了，不是普通语句，所以行首写、不加分号。\n"
            + "#region 名字 … #endregion 只是把代码折起来方便看；#if 条件 … #else … #endif 决定哪几行真的参与编译。\n"
            + "示例代码：\n"
            + "    #region 每帧逻辑\n"
            + "    int frameCount = 0;\n"
            + "    frameCount = frameCount + 1;\n"
            + "    tower.PrintLine(\"frame={0}\", frameCount);\n"
            + "    #endregion\n"
            + "    #if true\n"
            + "    tower.PrintLine(\"if branch\");\n"
            + "    #else\n"
            + "    tower.PrintLine(\"else branch\");\n"
            + "    #endif\n\n"
            + "目标：写一个 #region / #endregion 折叠块，再写一个 #if / #endif 分支。\n"
            + "提示：#if 后面接的是「编译符号」，不是变量；Unity 常用符号有 UNITY_EDITOR，没定义的符号那一支会被整段丢弃。\n"
            + "判定：代码里同时出现行首的 #region、#if 和 #endif。",
            "Syntax: lines starting with # are handled before compilation, so they sit at the start of a line and take no semicolon.\n"
            + "#region name ... #endregion only folds code; #if condition ... #else ... #endif decides which lines are really compiled.\n"
            + "Example Code:\n"
            + "    #region per-frame logic\n"
            + "    int frameCount = 0;\n"
            + "    frameCount = frameCount + 1;\n"
            + "    tower.PrintLine(\"frame={0}\", frameCount);\n"
            + "    #endregion\n"
            + "    #if true\n"
            + "    tower.PrintLine(\"if branch\");\n"
            + "    #else\n"
            + "    tower.PrintLine(\"else branch\");\n"
            + "    #endif\n\n"
            + "Goal: write a #region / #endregion fold block and a #if / #endif branch.\n"
            + "Hint: #if takes a compile symbol, not a variable; UNITY_EDITOR is the common Unity one, and a branch with an undefined symbol is dropped entirely.\n"
            + "Check: #region, #if and #endif all appear at the start of a line.",
            (raw, code) => RePreprocRegion.IsMatch(code) && RePreprocIf.IsMatch(code) && RePreprocEndif.IsMatch(code));

        list[41] = Def(
            "任务 42：struct 与 class 的区别（复制值 vs 复制引用）",
            "Task 42: struct vs class (copying a value vs copying a reference)",
            "写法：struct 是值类型，赋值时把整份数据复制一份，之后两个变量各改各的；class 是引用类型，赋值只是多了一个指向同一个对象的名字。\n"
            + "struct 适合装几个小数据（坐标、颜色），class 适合装会变的对象（炮台、敌人）。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        struct Pt { public int X; }\n"
            + "        class Node { public int X; }\n"
            + "    }\n"
            + "    Pt p1 = new Pt();\n"
            + "    p1.X = 1;\n"
            + "    Pt p2 = p1;\n"
            + "    p2.X = 5;\n"
            + "    Node n1 = new Node();\n"
            + "    n1.X = 1;\n"
            + "    Node n2 = n1;\n"
            + "    n2.X = 5;\n"
            + "    tower.PrintLine(\"p1={0} p2={1} n1={2} n2={3}\", p1.X, p2.X, n1.X, n2.X);\n\n"
            + "目标：自己写一个 struct 和一个 class，各 new 一次、各赋值复制一次，把四个值打印出来对比。\n"
            + "提示：运行后会看到 p1=1 p2=5（各存一份），而 n1=5 n2=5（两个名字指同一个对象）。\n"
            + "判定：代码里出现 struct 声明，并且用 new 创建了这个结构体。",
            "Syntax: struct is a value type - assignment copies the whole payload, so the two variables then change independently; class is a reference type - assignment only adds another name for the same object.\n"
            + "struct fits a few small numbers (a point, a color); class fits objects that live and change (towers, enemies).\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        struct Pt { public int X; }\n"
            + "        class Node { public int X; }\n"
            + "    }\n"
            + "    Pt p1 = new Pt();\n"
            + "    p1.X = 1;\n"
            + "    Pt p2 = p1;\n"
            + "    p2.X = 5;\n"
            + "    Node n1 = new Node();\n"
            + "    n1.X = 1;\n"
            + "    Node n2 = n1;\n"
            + "    n2.X = 5;\n"
            + "    tower.PrintLine(\"p1={0} p2={1} n1={2} n2={3}\", p1.X, p2.X, n1.X, n2.X);\n\n"
            + "Goal: write one struct and one class, new each, copy each, and print all four values to compare.\n"
            + "Hint: you should see p1=1 p2=5 (two copies) while n1=5 n2=5 (one shared object).\n"
            + "Check: a struct declaration appears and that struct is created with new.",
            (raw, code) =>
            {
                Match ms = ReStructDecl.Match(code);
                return ms.Success && Regex.IsMatch(code, @"\bnew\s+" + Regex.Escape(ms.Groups[1].Value) + @"\b");
            });

        list[42] = Def(
            "任务 43：元组与解构（(int, int) 与 var (a, b)）",
            "Task 43: Tuples and Deconstruction",
            "写法：元组就是「把几个值打包在一起」，类型写 (int x, int y)，值写 (3, 4)，用 .x / .y 取出来。\n"
            + "解构就是「把包拆开」：var (a, b) = (5, 6); 一行拿到两个变量，不用再写 t.Item1 / t.Item2。\n"
            + "示例代码：\n"
            + "    (int x, int y) size = (3, 4);\n"
            + "    tower.PrintLine(\"x={0} y={1} sum={2}\", size.x, size.y, size.x + size.y);\n"
            + "    var pair = (hp: 10, name: \"A\");\n"
            + "    tower.PrintLine(\"hp={0} name={1}\", pair.hp, pair.name);\n"
            + "    var (a, b) = (5, 6);\n"
            + "    tower.PrintLine(\"a={0} b={1} a+b={2}\", a, b, a + b);\n\n"
            + "目标：写一个带名字的元组并访问它的成员，再用 var (x, y) = ... 解构一次。\n"
            + "提示：元组是值类型，赋值时会整份复制；它适合方法一次返回两个值，不适合长期当数据结构用。\n"
            + "判定：代码里出现元组类型或元组字面量，并且出现 var (a, b) = 形式的解构。",
            "Syntax: a tuple packs several values together - type (int x, int y), value (3, 4), members read with .x / .y.\n"
            + "Deconstruction unpacks it: var (a, b) = (5, 6); gives two variables instead of t.Item1 / t.Item2.\n"
            + "Example Code:\n"
            + "    (int x, int y) size = (3, 4);\n"
            + "    tower.PrintLine(\"x={0} y={1} sum={2}\", size.x, size.y, size.x + size.y);\n"
            + "    var pair = (hp: 10, name: \"A\");\n"
            + "    tower.PrintLine(\"hp={0} name={1}\", pair.hp, pair.name);\n"
            + "    var (a, b) = (5, 6);\n"
            + "    tower.PrintLine(\"a={0} b={1} a+b={2}\", a, b, a + b);\n\n"
            + "Goal: build a named tuple and read its members, then deconstruct once with var (x, y) = ... .\n"
            + "Hint: a tuple is a value type and copies on assignment; it shines when a method must return two values, not as a long-lived data structure.\n"
            + "Check: a tuple type or tuple literal appears together with a var (a, b) = deconstruction.",
            (raw, code) => (ReTupleType.IsMatch(code) || ReTupleLiteral.IsMatch(code)) && ReDeconstruct.IsMatch(code));

        list[43] = Def(
            "任务 44：可空类型与 ?. / ??（null 安全访问）",
            "Task 44: Nullable Types and ?. / ??",
            "写法：int? 表示「这个 int 可能是空的」，可以写 int? hp = null;，用 hp.HasValue / hp.Value 判断和取值。\n"
            + "?. 是「如果左边是 null 就整段返回 null，不要崩」；?? 是「左边是 null 就用右边这个兜底值」。\n"
            + "示例代码：\n"
            + "    int? hp = null;\n"
            + "    int real = hp ?? 0;\n"
            + "    tower.PrintLine(\"real={0} hasValue={1}\", real, hp.HasValue);\n"
            + "    GameObject go = null;\n"
            + "    int childCount = go?.transform.childCount ?? -1;\n"
            + "    tower.PrintLine(\"childCount={0}\", childCount);\n"
            + "    if (hp.HasValue) tower.PrintLine(\"hp={0}\", hp.Value);\n\n"
            + "目标：声明一个可空类型并用 ?? 兜底，再用 ?. 安全地访问一个可能为 null 的引用。\n"
            + "提示：int 不能直接 = null，必须写成 int?；?. 之后得到的还是可空值，通常紧跟一个 ?? 才有意义。\n"
            + "判定：代码里出现 xxx? 变量 = 声明，并且出现 ?. 或 ?? 之一。",
            "Syntax: int? means the int may be absent - int? hp = null; is legal, and hp.HasValue / hp.Value inspect it.\n"
            + "?. short-circuits to null when the left side is null instead of crashing; ?? supplies a fallback value when the left side is null.\n"
            + "Example Code:\n"
            + "    int? hp = null;\n"
            + "    int real = hp ?? 0;\n"
            + "    tower.PrintLine(\"real={0} hasValue={1}\", real, hp.HasValue);\n"
            + "    GameObject go = null;\n"
            + "    int childCount = go?.transform.childCount ?? -1;\n"
            + "    tower.PrintLine(\"childCount={0}\", childCount);\n"
            + "    if (hp.HasValue) tower.PrintLine(\"hp={0}\", hp.Value);\n\n"
            + "Goal: declare a nullable type with a ?? fallback, then use ?. to touch a reference that may be null.\n"
            + "Hint: an int cannot be set to null, only int?; a ?. chain yields a nullable value, so a ?? usually follows.\n"
            + "Check: a xxx? declaration appears together with ?. or ??.",
            (raw, code) => ReNullableType.IsMatch(code) && (ReNullConditional.IsMatch(code) || ReNullCoalesce.IsMatch(code)));

        list[44] = Def(
            "任务 45：字符串插值 $\"...\" 与格式符",
            "Task 45: String Interpolation $\"...\" and Format Specifiers",
            "写法：字符串前面加 $，里面就能直接写 {变量}，比 string.Format 少一截、也不会把参数顺序写错。\n"
            + "大括号里还能加格式符：{ratio:F2} 保留两位小数，{hp:00} 补零，{ratio:P0} 百分比，{n:X} 十六进制。\n"
            + "示例代码：\n"
            + "    int hp = 7;\n"
            + "    float ratio = 0.853f;\n"
            + "    tower.PrintLine($\"hp={hp} ratio={ratio:F2}\");\n"
            + "    tower.PrintLine($\"pad={hp:00} pct={ratio:P0} hex={hp:X}\");\n"
            + "    string msg = $\"double={hp * 2}\";\n"
            + "    tower.PrintLine(msg);\n\n"
            + "目标：用 $\"...\" 写至少一条插值字符串，并且里面出现一个带格式符的 {}。\n"
            + "提示：插值字符串里的 { } 用来放表达式，想输出真正的大括号要写 {{ 和 }}。\n"
            + "别被 $ 唬住：它是 C# 的插值前缀符号，不是运算符、不是变量名、更不是乱码，$ 和紧跟的\\\" 是同一个记号，必须连着写（$\\\"…\\\"）。漏掉 $ 就退化成普通字符串，{hp} 会原样打印；写错成 @\\\"…\\\" 也一样不替换。\n"
            + "判定：代码里出现 $\" 开头，并且出现 {x:F2} 这类带冒号格式符的占位。",
            "Syntax: prefix the string with $ and write {variable} directly inside - shorter than string.Format and immune to argument-order mistakes.\n"
            + "A format specifier may follow a colon: {ratio:F2} two decimals, {hp:00} zero padded, {ratio:P0} percent, {n:X} hexadecimal.\n"
            + "Example Code:\n"
            + "    int hp = 7;\n"
            + "    float ratio = 0.853f;\n"
            + "    tower.PrintLine($\"hp={hp} ratio={ratio:F2}\");\n"
            + "    tower.PrintLine($\"pad={hp:00} pct={ratio:P0} hex={hp:X}\");\n"
            + "    string msg = $\"double={hp * 2}\";\n"
            + "    tower.PrintLine(msg);\n\n"
            + "Goal: write at least one $\"...\" interpolated string containing a formatted {} placeholder.\n"
            + "Hint: braces inside an interpolated string hold expressions; to print literal braces write {{ and }}.\n"
            + "Do not be rattled by the $: it is C#'s interpolation prefix, not an operator, not a variable and certainly not garbled text - the $ and the quote right after it are one token and must be written together ($\\\"...\\\"). Drop the $ and the string degrades to a plain one, so {hp} prints literally; using @\\\"...\\\" instead has the same effect.\n"
            + "Check: a $\" literal appears plus a {x:F2}-style placeholder with a colon.",
            (raw, code) => ReInterpolated.IsMatch(code) && ReFormatSpec.IsMatch(code));

        list[45] = Def(
            "任务 46：对象初始化器（new X { ... }）",
            "Task 46: Object Initializers (new X { ... })",
            "写法：new 出对象后，紧接着用 { 字段 = 值, 字段 = 值 } 一次把成员全部设好，不用连着写好几行 xxx.dmg = 5;。\n"
            + "它和「先 new 再逐个赋值」完全等价，只是更短、更清楚；构造函数的括号可写可不写。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        class Bullet { public int dmg; public float speed; public string owner; }\n"
            + "    }\n"
            + "    Bullet b = new Bullet { dmg = 5, speed = 12.0f, owner = \"player\" };\n"
            + "    tower.PrintLine(\"dmg={0} speed={1} owner={2}\", b.dmg, b.speed, b.owner);\n\n"
            + "目标：自己写一个带公开字段的类，用 new X { ... } 一次初始化至少两个成员。\n"
            + "提示：能进初始化器的只有公开的字段或可写属性；只读成员只能在构造函数里赋值。List 也能用 { 1, 2, 3 } 集合初始化器。\n"
            + "判定：代码里出现 new 类名 { 前面没有 new 的数组写法不算 } 这种对象初始化器。",
            "Syntax: right after new, a { member = value, member = value } block initialises everything at once, replacing several assignment lines.\n"
            + "It is exactly equivalent to new-then-assign, just shorter; the constructor parentheses are optional.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        class Bullet { public int dmg; public float speed; public string owner; }\n"
            + "    }\n"
            + "    Bullet b = new Bullet { dmg = 5, speed = 12.0f, owner = \"player\" };\n"
            + "    tower.PrintLine(\"dmg={0} speed={1} owner={2}\", b.dmg, b.speed, b.owner);\n\n"
            + "Goal: declare a class with public fields and initialise at least two members with new X { ... }.\n"
            + "Hint: only public fields or writable properties qualify; read-only members need the constructor. Lists accept a { 1, 2, 3 } collection initializer too.\n"
            + "Check: a new ClassName { ... } object initializer appears.",
            (raw, code) =>
            {
                Match mo = ReObjectInitializer.Match(code);
                return mo.Success && !mo.Groups[1].Value.StartsWith("int") && !mo.Groups[1].Value.Contains("[");
            });
        list[42].Page2Zh = "【这一页：先打包两个值，再把包拆开】\n"
            + "① 先写 (int x, int y) size = (3, 4); 打印 size.x + size.y，确认拿到 7。\n"
            + "② 把名字换成含义更清楚的：var box = (w: 3, h: 4); 打印 box.w * box.h。\n"
            + "③ 解构：var (a, b) = (5, 6); 打印 a、b，确认不用写 Item1 / Item2 也能取。\n"
            + "④ 故意只解构一个：var (only) = (5, 6); 编译报错 —— 解构的变量个数必须和元素个数一致。\n"
            + "【逐符号读懂它】\n"
            + "· (int x, int y) ：元组类型，x / y 是元素名（编译器记在元数据里，运行时其实还是 Item1 / Item2）。\n"
            + "· (3, 4) ：元组字面量，圆括号加逗号，不需要写 new。\n"
            + "· var (a, b) = ：解构赋值，左边只写变量名，类型由右边推出来。\n"
            + "· .x / .y ：按元素名访问；写 .Item1 / .Item2 也能编译，但可读性差。\n"
            + "【三个必踩的坑】\n"
            + "✘ 元素名写重复：(int x, int x) → CS8126，元组元素名不能重复。\n"
            + "✘ 把元组当引用去共享：元组是 struct，赋值时整份复制，改一个不影响另一个。\n"
            + "✘ 以为元组能塞无限多个值：ValueTuple 到 7 个元素就够用，更多元素应改用类。\n"
            + "✔ 最常见用法：方法一次返回两个结果 —— return (min, max); 调用处 var (lo, hi) = Range();";
        list[42].Page2En = "THIS PAGE: PACK TWO VALUES, THEN UNPACK THEM\n"
            + "1) Write (int x, int y) size = (3, 4); print size.x + size.y and confirm 7.\n"
            + "2) Give the elements meaning: var box = (w: 3, h: 4); print box.w * box.h.\n"
            + "3) Deconstruct: var (a, b) = (5, 6); print a and b - no Item1 / Item2 needed.\n"
            + "4) Deconstruct only one: var (only) = (5, 6); fails - the number of names must match the number of elements.\n"
            + "READ EVERY SYMBOL\n"
            + "- (int x, int y) : a tuple type; x and y are element names (kept in metadata, Item1 / Item2 at runtime).\n"
            + "- (3, 4) : a tuple literal - parentheses plus commas, no new required.\n"
            + "- var (a, b) = : deconstruction assignment; types are inferred from the right side.\n"
            + "- .x / .y : access by element name; .Item1 / .Item2 compiles but reads worse.\n"
            + "THREE TRAPS\n"
            + "X Duplicate element names (int x, int x) - CS8126.\n"
            + "X Treating a tuple as shared state - it is a struct, assignment copies it wholesale.\n"
            + "X Assuming unlimited elements - ValueTuple tops out around 7; beyond that use a class.\n"
            + "V The classic use: return two results at once - return (min, max); and var (lo, hi) = Range(); at the call site.";
        list[43].Page2Zh = "【这一页：先让 null 崩一次，再用 ?. / ?? 修好】\n"
            + "① 先写 GameObject go = null; 然后直接 go.transform.childCount，运行立刻 NullReferenceException（画面卡住）。\n"
            + "② 改成 go?.transform.childCount，编译不报错，但结果是「可能为空的 int」，不能直接赋给 int。\n"
            + "③ 补上兜底：int n = go?.transform.childCount ?? -1; 运行输出 -1，不崩。\n"
            + "④ 再试值类型：int? hp = null; 打印 hp.HasValue（False）、hp ?? 0（0）；给 hp 赋 5 后再打印变成 True / 5。\n"
            + "【逐符号读懂它】\n"
            + "· int? ：Nullable<int> 的简写，值类型也能表示「没有值」。\n"
            + "· .HasValue / .Value ：先问有没有值，再取；没值就取 Value 会抛 InvalidOperationException。\n"
            + "· ?. ：左边是 null 就整条链短路成 null，后面的 .transform.childCount 一律不执行。\n"
            + "· ?? ：空值合并，左边 null 就用右边；可以连着写 a ?? b ?? 0。\n"
            + "【三个必踩的坑】\n"
            + "✘ int hp = null; → CS0037，null 不能赋给非可空的值类型。\n"
            + "✘ go?.transform.childCount 直接赋给 int → CS0266，左边是 int?，要先 ?? 兜底或取 .Value。\n"
            + "✘ 只写 ?. 就以为万事大吉 → 它只管引用链，数组越界之类的运行时错误照旧会崩。\n"
            + "✔ 口诀：引用可能为空用 ?.，值可能为空用 int?，两者都靠 ?? 给兜底值。";
        list[43].Page2En = "THIS PAGE: CRASH ON NULL FIRST, THEN FIX IT WITH ?. AND ??\n"
            + "1) Write GameObject go = null; then go.transform.childCount - NullReferenceException at once.\n"
            + "2) Change it to go?.transform.childCount: it compiles, but the result is a maybe-null int and cannot feed an int directly.\n"
            + "3) Add the fallback: int n = go?.transform.childCount ?? -1; prints -1, no crash.\n"
            + "4) Value types next: int? hp = null; print hp.HasValue (False) and hp ?? 0 (0); assign hp = 5 and both change.\n"
            + "READ EVERY SYMBOL\n"
            + "- int? : shorthand for Nullable<int>, letting a value type represent no value.\n"
            + "- .HasValue / .Value : ask whether a value exists, then read it; .Value on null throws InvalidOperationException.\n"
            + "- ?. : a null left side short-circuits the whole chain to null and .transform.childCount never runs.\n"
            + "- ?? : null coalescing - the right side wins when the left is null; chainable as a ?? b ?? 0.\n"
            + "THREE TRAPS\n"
            + "X int hp = null; - CS0037, null cannot be assigned to a non-nullable value type.\n"
            + "X Assigning go?.transform.childCount straight to int - CS0266; the left side is int?, so add ?? or .Value.\n"
            + "X Believing ?. fixes everything - it guards the reference chain only; bad indexes still throw.\n"
            + "V Rule: references may be null -> ?. ; values may be null -> int? ; both are tamed by ?? with a fallback.";
        list[44].Page2Zh = "【这一页：把 {0} 全部换成 {} 试试】\n"
            + "① 先用熟悉的老写法：tower.PrintLine(\"hp={0} ratio={1}\", hp, ratio); 记住参数顺序。\n"
            + "② 换成插值：tower.PrintLine($\"hp={hp} ratio={ratio}\"); 少一个参数列表，输出一样。\n"
            + "③ 加格式符：{ratio:F2} 两位小数、{hp:00} 补零、{hp:X} 十六进制，逐个试一遍。\n"
            + "④ 故意写 tower.PrintLine($\"{{hp}}\"); 输出是字面的 {hp} —— 双大括号是转义。\n"
            + "【逐符号读懂它】\n"
            + "· $ ：告诉编译器「这个字符串里有表达式要算」，没有它就只是普通字符串。\n"
            + "· {expr} ：里面可以放变量、算式、方法调用，甚至三元表达式。\n"
            + "· :F2 / :P0 / :X ：格式符，F 定点小数、P 百分比、X 十六进制、N 千分位、0 补零占位。\n"
            + "· {{ }} ：转义成真正的大括号，不会被当成占位符。\n"
            + "【三个必踩的坑】\n"
            + "✘ 插值字符串里大括号没配对 → CS8086 / CS1733。\n"
            + "✘ 把 $ 写成 @ → @\"...\" 是原样字符串，{hp} 不会被替换，输出死板的 {hp}。\n"
            + "✘ 格式符写错（{hp:Q}）→ 运行时抛 FormatException，比编译错误更难查。\n"
            + "✔ 老代码里的 string.Format 都能换成 $\"...\"，参数一多就该换，顺序再也错不了。";
        list[44].Page2En = "THIS PAGE: TURN EVERY {0} INTO {}\n"
            + "1) Start with the old form: tower.PrintLine(\"hp={0} ratio={1}\", hp, ratio); note the argument order.\n"
            + "2) Switch to interpolation: tower.PrintLine($\"hp={hp} ratio={ratio}\"); no argument list, same output.\n"
            + "3) Add specifiers: {ratio:F2}, {hp:00}, {hp:X} - try each once.\n"
            + "4) Write tower.PrintLine($\"{{hp}}\") on purpose: the output is the literal {hp} - double braces escape.\n"
            + "READ EVERY SYMBOL\n"
            + "- $ : tells the compiler the string contains expressions; without it you have a plain string.\n"
            + "- {expr} : may hold a variable, an expression, a method call or even a conditional.\n"
            + "- :F2 / :P0 / :X : format specifiers - F fixed decimals, P percent, X hex, N thousands, 0 zero padding.\n"
            + "- {{ }} : escaped literal braces that are not treated as placeholders.\n"
            + "THREE TRAPS\n"
            + "X Unbalanced braces inside an interpolated string - CS8086 / CS1733.\n"
            + "X Writing @\"...\" instead of $\"...\" - a verbatim string leaves {hp} untouched.\n"
            + "X A bogus specifier ({hp:Q}) - FormatException at runtime, harder to spot than a compile error.\n"
            + "V Every string.Format can become $\"...\", and with many arguments it should - the order can no longer drift.";
        list[45].Page2Zh = "【这一页：三步都做完，再试试嵌套初始化】\n"
            + "① 先在 GlobalCode 里写 class Bullet，包含 public int dmg; public float speed; 两个公开字段。\n"
            + "② 老写法：Bullet b = new Bullet(); b.dmg = 5; b.speed = 12f; 打印确认能用。\n"
            + "③ 新写法：Bullet b2 = new Bullet { dmg = 5, speed = 12f }; 两行并一行，结果完全一样。\n"
            + "④ 试集合初始化器：List<int> nums = new List<int> { 1, 2, 3 }; 打印 nums.Count 得到 3。\n"
            + "【逐符号读懂它】\n"
            + "· new Bullet { } ：对象初始化器，编译后等价于「先 new 再逐个赋值」。\n"
            + "· dmg = 5 ：左边的 dmg 是成员名，不是变量；右边的 5 可以是任意表达式。\n"
            + "· 逗号分隔 ：成员之间用逗号，最后一项后面写不写逗号都合法。\n"
            + "· new List<int> { 1, 2, 3 } ：同一个语法位置，对集合来说就是「逐个 Add」。\n"
            + "【三个必踩的坑】\n"
            + "✘ 初始化私有字段 → CS0117，初始化器只能碰能访问到的成员。\n"
            + "✘ 对只读属性在 { } 里赋值 → CS0200，只读属性只能构造函数里设。\n"
            + "✘ 以为 { } 会调用特殊构造 → 它只是语法糖，构造函数照旧执行一次。\n"
            + "✔ 初始化器还能嵌套：new A { sub = new B { x = 1 } }，一层层建对象很清爽。";
        list[45].Page2En = "THIS PAGE: FINISH ALL THREE STEPS, THEN TRY NESTING\n"
            + "1) Declare class Bullet in GlobalCode with public int dmg; public float speed;.\n"
            + "2) Old way: Bullet b = new Bullet(); b.dmg = 5; b.speed = 12f; print to confirm it works.\n"
            + "3) New way: Bullet b2 = new Bullet { dmg = 5, speed = 12f }; two lines collapse into one, same result.\n"
            + "4) Collection initializer: List<int> nums = new List<int> { 1, 2, 3 }; nums.Count is 3.\n"
            + "READ EVERY SYMBOL\n"
            + "- new Bullet { } : the object initializer; it compiles to new-then-assign.\n"
            + "- dmg = 5 : the left side is a member name, not a variable; the right side may be any expression.\n"
            + "- commas : separate the members; a trailing comma is legal but unnecessary.\n"
            + "- new List<int> { 1, 2, 3 } : the same syntax slot means repeated Add on a collection.\n"
            + "THREE TRAPS\n"
            + "X Initialising a private field - CS0117; only reachable members are allowed.\n"
            + "X Assigning a read-only property inside { } - CS0200; set it in the constructor instead.\n"
            + "X Expecting a special constructor - it is pure sugar; the constructor still runs once.\n"
            + "V Initializers nest: new A { sub = new B { x = 1 } } builds a small object graph cleanly.";

        list[46] = Def(
            "任务 47：迭代器（yield return 与 foreach）",
            "Task 47: Iterators (yield return with foreach)",
            "写法：想让一个方法「一次吐一个值」，就在方法体里写 yield return，返回值类型写成 IEnumerable<int>，调用方用 foreach 一个个拿。\n"
            + "它的好处是不用先造一个数组或 List：要几个就算几个，用到第几个才执行到第几个。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        class Frames {\n"
            + "            public IEnumerable<int> CountTo(int n) {\n"
            + "                for (int i = 1; i <= n; i++) {\n"
            + "                    yield return i * 10;\n"
            + "                }\n"
            + "            }\n"
            + "        }\n"
            + "    }\n"
            + "    Frames f = new Frames();\n"
            + "    foreach (int v in f.CountTo(3)) {\n"
            + "        tower.PrintLine(\"v={0}\", v);\n"
            + "    }\n\n"
            + "目标：自己写一个带 yield return 的方法，并用 foreach 把它跑完。\n"
            + "提示：带 yield 的方法返回值不能是 int / void，必须是 IEnumerable<T> 这一类；yield break 可以提前收尾。\n"
            + "判定：代码里出现 yield return。",
            "Syntax: to have a method hand back one value at a time, write yield return inside it, declare the return type as IEnumerable<int>, and let foreach pull them.\n"
            + "The payoff: no array or List has to be built first - values are produced lazily as the loop asks for them.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        class Frames {\n"
            + "            public IEnumerable<int> CountTo(int n) {\n"
            + "                for (int i = 1; i <= n; i++) {\n"
            + "                    yield return i * 10;\n"
            + "                }\n"
            + "            }\n"
            + "        }\n"
            + "    }\n"
            + "    Frames f = new Frames();\n"
            + "    foreach (int v in f.CountTo(3)) {\n"
            + "        tower.PrintLine(\"v={0}\", v);\n"
            + "    }\n\n"
            + "Goal: write your own method containing yield return and drain it with foreach.\n"
            + "Hint: an iterator cannot return int or void - it must return IEnumerable<T>; yield break ends it early.\n"
            + "Check: yield return appears.",
            (raw, code) => ReYieldReturn.IsMatch(code));

        list[47] = Def(
            "任务 48：枚举转换（枚举 ↔ 整数 ↔ 字符串）",
            "Task 48: Converting Enums (enum <-> int <-> string)",
            "写法：枚举底层就是整数，所以 (int)Kind.Big 能拿到它的编号，(Kind)2 能反查回来，k.ToString() 得到名字。\n"
            + "字符串转枚举要写 (Kind)Enum.Parse(typeof(Kind), \"Boss\")；不确定字符串合法时用 Enum.TryParse 更安全。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        enum Kind { Small, Big, Boss }\n"
            + "    }\n"
            + "    Kind k = Kind.Big;\n"
            + "    int n = (int)k;\n"
            + "    string s = k.ToString();\n"
            + "    Kind back = (Kind)2;\n"
            + "    tower.PrintLine(\"n={0} s={1} back={2} isBoss={3}\", n, s, back, back == Kind.Boss);\n\n"
            + "目标：把一个枚举值转成整数，再用整数转回枚举，并至少打印一次字符串形式。\n"
            + "提示：枚举编号从 0 开始按声明顺序递增；也可以显式写 enum Kind { Small = 1, Big = 5 }。\n"
            + "判定：代码里出现 (类型)变量 形式的强制转换，并且出现 ToString() 或 Enum.Parse。",
            "Syntax: an enum is an integer underneath, so (int)Kind.Big yields its number, (Kind)2 maps back, and k.ToString() gives the name.\n"
            + "String to enum needs (Kind)Enum.Parse(typeof(Kind), \"Boss\"); prefer Enum.TryParse when the text may be invalid.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        enum Kind { Small, Big, Boss }\n"
            + "    }\n"
            + "    Kind k = Kind.Big;\n"
            + "    int n = (int)k;\n"
            + "    string s = k.ToString();\n"
            + "    Kind back = (Kind)2;\n"
            + "    tower.PrintLine(\"n={0} s={1} back={2} isBoss={3}\", n, s, back, back == Kind.Boss);\n\n"
            + "Goal: turn an enum into an int, turn an int back into the enum, and print the string form at least once.\n"
            + "Hint: numbering starts at 0 in declaration order; explicit values work too: enum Kind { Small = 1, Big = 5 }.\n"
            + "Check: a (Type)value cast appears together with ToString() or Enum.Parse.",
            (raw, code) => ReEnumCast.IsMatch(code)
                && Regex.IsMatch(code, @"Enum\s*\.\s*Parse|\.\s*ToString\s*\(\s*\)"));

        list[48] = Def(
            "任务 49：扩展方法（this 参数）与等价静态写法",
            "Task 49: Extension Methods (the this parameter) and the Static Equivalent",
            "概念：扩展方法就是「给别人的类型加一个方法」，写在静态类里的静态方法，第一个参数前面加 this，调用时就能写成 hp.Twice()。\n"
            + "关键限制：扩展方法必须写在「顶层的静态类」里。本教程模板会把你写的类嵌到 UserTowerCode 内部，所以在 GlobalCode 里写 this 参数会报 CS1109。\n"
            + "所以这里练等价写法：静态工具类 + 静态方法，调用时写 IntExt.Twice(hp)，效果和 hp.Twice() 一模一样。\n"
            + "示例代码：\n"
            + "    GlobalCode {\n"
            + "        static class IntExt {\n"
            + "            public static int Twice(int v) { return v * 2; }\n"
            + "            public static int AddTo(int v, int add) { return v + add; }\n"
            + "        }\n"
            + "    }\n"
            + "    int hp = 6;\n"
            + "    int twice = IntExt.Twice(hp);\n"
            + "    tower.PrintLine(\"twice={0} plus={1}\", twice, IntExt.AddTo(hp, 4));\n\n"
            + "目标：写一个 static class 和它的静态方法，并至少调用两次（其中一个带两个参数）。\n"
            + "提示：真正写扩展方法时（顶层静态类中），方法写 public static int Twice(this int v)，第一个参数就是「被扩展的那个对象」。\n"
            + "判定：代码里出现 static class 声明，并且调用了这个类的静态方法。",
            "Concept: an extension method adds a method to someone else's type - a static method in a static class whose first parameter carries the this modifier, so it reads as hp.Twice().\n"
            + "Hard limit: extension methods must live in a top-level static class. This tutorial template nests your classes inside UserTowerCode, so a this parameter inside GlobalCode reports CS1109.\n"
            + "So practise the equivalent form here: a static utility class plus static methods, called as IntExt.Twice(hp) - exactly what hp.Twice() would do.\n"
            + "Example Code:\n"
            + "    GlobalCode {\n"
            + "        static class IntExt {\n"
            + "            public static int Twice(int v) { return v * 2; }\n"
            + "            public static int AddTo(int v, int add) { return v + add; }\n"
            + "        }\n"
            + "    }\n"
            + "    int hp = 6;\n"
            + "    int twice = IntExt.Twice(hp);\n"
            + "    tower.PrintLine(\"twice={0} plus={1}\", twice, IntExt.AddTo(hp, 4));\n\n"
            + "Goal: declare a static class with static methods and call it at least twice, one call taking two arguments.\n"
            + "Hint: a real extension method (in a top-level static class) is written public static int Twice(this int v) - the first parameter is the extended object.\n"
            + "Check: a static class declaration appears and that class's static method is called.",
            (raw, code) =>
            {
                Match me = ReStaticClassDecl.Match(code);
                return me.Success && Regex.IsMatch(code, @"\b" + Regex.Escape(me.Groups[1].Value) + @"\s*\.\s*[A-Za-z_]\w*\s*\(");
            });
        list[46].Page2Zh = "【这一页：先普通方法，再换 yield，看出差别】\n"
            + "① 先写老办法：int[] CountTo(int n) 里 new 一个数组、填好、return，调用处 foreach 遍历。\n"
            + "② 换成 yield：返回类型改成 IEnumerable<int>，方法体里去掉 new，循环里写 yield return i * 10;\n"
            + "③ 在 yield return 后面加一行 tower.PrintLine(\"算到 {0}\", i); 观察它是「边拿边算」，不是一次性算完。\n"
            + "④ 在循环中途加 if (i == 2) yield break; 看结果只剩两个值。\n"
            + "【逐符号读懂它】\n"
            + "· IEnumerable<int> ：可以被 foreach 遍历的序列接口，yield 方法的法定返回类型之一。\n"
            + "· yield return ：吐一个值后暂停，等下一次 MoveNext 再继续往下走。\n"
            + "· yield break ：提前结束，后面的代码不再执行，相当于序列到此为止。\n"
            + "· 惰性执行 ：不 foreach 就一次都不跑；foreach 停在中途，剩余代码也不会执行。\n"
            + "【三个必踩的坑】\n"
            + "✘ 返回类型写成 int 或 void → CS1624，带 yield 的方法必须返回 IEnumerable / IEnumerator 系列。\n"
            + "✘ 在 try-catch 的 catch 块里写 yield return → 编译器报错，catch 里不允许 yield。\n"
            + "✘ 以为每次 foreach 都能接着上次继续 → 每次 foreach 都从头开始，序列会重新生成。\n"
            + "✔ 典型用途：分批刷怪、逐帧生成、无限序列（while (true) yield return ...），只为用到的那几个付出代价。";
        list[46].Page2En = "THIS PAGE: START WITH A NORMAL METHOD, THEN SWITCH TO yield\n"
            + "1) Old way: int[] CountTo(int n) allocates an array, fills it, returns it, and foreach drains it.\n"
            + "2) Switch to yield: return IEnumerable<int>, drop the allocation, and write yield return i * 10; inside the loop.\n"
            + "3) Add tower.PrintLine(\"step {0}\", i); after the yield return and watch it compute on demand rather than all at once.\n"
            + "4) Insert if (i == 2) yield break; mid-loop and see only two values arrive.\n"
            + "READ EVERY SYMBOL\n"
            + "- IEnumerable<int> : the sequence interface foreach can walk - one of the legal iterator return types.\n"
            + "- yield return : hands out one value, pauses, and resumes on the next MoveNext.\n"
            + "- yield break : ends the sequence early; the rest of the method never runs.\n"
            + "- Lazy execution : without foreach nothing runs at all, and stopping the loop stops the work.\n"
            + "THREE TRAPS\n"
            + "X Returning int or void - CS1624; an iterator must return IEnumerable / IEnumerator.\n"
            + "X yield return inside a catch block - the compiler rejects it.\n"
            + "X Expecting a foreach to resume - every foreach restarts the sequence from the beginning.\n"
            + "V Typical uses: staged spawns, per-frame generation, infinite sequences (while (true) yield return ...) - you pay only for what you consume.";
        list[47].Page2Zh = "【这一页：把编号、名字、字符串三条路都走一遍】\n"
            + "① 声明 enum Kind { Small, Big, Boss }，打印 (int)Kind.Small / .Big / .Boss，看到 0 / 1 / 2。\n"
            + "② 反查：(Kind)1 打印出来是 Big，说明枚举就是个整数标签。\n"
            + "③ 名字：Kind.Big.ToString() 得到 \"Big\"，适合存进配置或显示到 UI。\n"
            + "④ 字符串转枚举：(Kind)Enum.Parse(typeof(Kind), \"Boss\")；再试 Enum.TryParse(\"Bogus\", out Kind r)，返回 false 且不抛异常。\n"
            + "【逐符号读懂它】\n"
            + "· (int)k ：拆箱式强转，拿到枚举的底层编号。\n"
            + "· (Kind)2 ：反向强转，编号 2 对应 Boss；越界编号不会报错，只是得到一个非法枚举值。\n"
            + "· k.ToString() ：返回枚举成员名，未定义编号会返回数字字符串。\n"
            + "· Enum.Parse / Enum.TryParse ：字符串转枚举，后者用 out 返回布尔结果，配 if 用最稳。\n"
            + "【三个必踩的坑】\n"
            + "✘ Enum.Parse 碰到拼错的名字 → 抛 ArgumentException，玩家输入必须用 TryParse。\n"
            + "✘ 直接用 (Kind)99 → 编译通过、运行也不抛，但后续 switch 会走到 default，属于隐形 bug。\n"
            + "✘ 拿枚举当 int 参与算术 → 必须先 (int) 转换，C# 不会隐式把枚举变整数。\n"
            + "✔ 记住：枚举 = 有名字的整数常量，转 int 用强转，转 string 用 ToString，转回来用 Parse / TryParse。";
        list[47].Page2En = "THIS PAGE: WALK ALL THREE PATHS - NUMBER, NAME, TEXT\n"
            + "1) Declare enum Kind { Small, Big, Boss } and print (int)Kind.Small / .Big / .Boss: 0 / 1 / 2.\n"
            + "2) Reverse it: (Kind)1 prints Big, proving an enum is just a labelled integer.\n"
            + "3) Names: Kind.Big.ToString() gives \"Big\" - good for config files or UI text.\n"
            + "4) Text to enum: (Kind)Enum.Parse(typeof(Kind), \"Boss\"); then try Enum.TryParse(\"Bogus\", out Kind r), which returns false without throwing.\n"
            + "READ EVERY SYMBOL\n"
            + "- (int)k : a cast that reads the underlying number.\n"
            + "- (Kind)2 : the reverse cast; out-of-range numbers do not throw, they just create an invalid enum value.\n"
            + "- k.ToString() : returns the member name; an undefined number stringifies as its digits.\n"
            + "- Enum.Parse / Enum.TryParse : text to enum; the Try form reports success through out plus a bool - safest with if.\n"
            + "THREE TRAPS\n"
            + "X Enum.Parse on a misspelled name throws ArgumentException - always use TryParse for player input.\n"
            + "X Casting (Kind)99 compiles and runs, but later switches fall through to default - a silent bug.\n"
            + "X Doing arithmetic on an enum - C# does not implicitly convert it to int; cast first.\n"
            + "V Remember: an enum is a named integer constant - cast to int, ToString to name, Parse / TryParse back.";
        list[48].Page2Zh = "【这一页：先学会「写工具方法」，再理解 this 参数】\n"
            + "① 写 static class IntExt，里面放 public static int Twice(int v)，调用 IntExt.Twice(6) 得到 12。\n"
            + "② 加一个两参数版本 AddTo(int v, int add)，体会「第一个参数 = 操作对象，后面 = 额外参数」。\n"
            + "③ 想改成扩展方法，就把签名写成 public static int Twice(this int v)：第一个参数前加 this，调用变成 6.Twice()。\n"
            + "④ 在本模板里这么写会报 CS1109 —— 因为模板把你的类嵌进了 UserTowerCode，扩展方法必须在顶层静态类里。\n"
            + "【逐符号读懂它】\n"
            + "· static class ：只能有静态成员、不能 new，天生就是「工具箱」。\n"
            + "· static 方法 ：不依赖对象状态，直接用 类名.方法() 调。\n"
            + "· this 参数 ：扩展方法的标记，告诉编译器「这个方法可以挂到第一个参数的类型上」。\n"
            + "· 等价关系 ：x.Twice() 编译后就是 IntExt.Twice(x)，只是写法更顺手、能接在链式调用后面。\n"
            + "【三个必踩的坑】\n"
            + "✘ 扩展方法写在非静态类里 → CS1106，所在类必须是 static。\n"
            + "✘ 扩展方法写在嵌套类里 → CS1109（本模板的实际情况），必须放在命名空间级别的顶层静态类。\n"
            + "✘ 以为扩展方法能改原类型 → 它根本进不了那个类，私有成员一样碰不到，只是语法糖。\n"
            + "✔ 记住两条：静态类放工具方法（IntExt.Twice），顶层静态类才能写 this 参数（6.Twice()）。";
        list[48].Page2En = "THIS PAGE: LEARN THE UTILITY METHOD FIRST, THEN THE this PARAMETER\n"
            + "1) Write static class IntExt with public static int Twice(int v); IntExt.Twice(6) returns 12.\n"
            + "2) Add a two-parameter AddTo(int v, int add) - first parameter is the subject, the rest are extras.\n"
            + "3) To make it an extension method, change the signature to public static int Twice(this int v): the this goes on the first parameter and the call becomes 6.Twice().\n"
            + "4) Inside this template that reports CS1109 - your class is nested in UserTowerCode, and extension methods must sit in a top-level static class.\n"
            + "READ EVERY SYMBOL\n"
            + "- static class : static members only, cannot be instantiated - a toolbox by design.\n"
            + "- static method : holds no object state and is called as Class.Method().\n"
            + "- the this parameter : marks an extension method so the compiler can bolt it onto the first parameter's type.\n"
            + "- equivalence : x.Twice() compiles to IntExt.Twice(x) - nicer syntax, chainable.\n"
            + "THREE TRAPS\n"
            + "X An extension method in a non-static class - CS1106; the containing class must be static.\n"
            + "X An extension method in a nested class - CS1109, exactly this template's case; it must be top-level.\n"
            + "X Believing it can modify the original type - it never enters that class and cannot touch private members; it is sugar only.\n"
            + "V Two rules: static classes hold utilities (IntExt.Twice); only top-level static classes can carry a this parameter (6.Twice()).";


        AppendGrammarDetailPages(list); // 每项任务补「第 2 页」手把手细节（任务卡右上角翻页查看）
        AppendGrammarAnimPages(list);   // 每项任务再补「第 3 页」代码动画（高亮逐行走，演代码在干什么）
        AppendGrammarMemPages(list);    // 语法课的第 3 页升级为「内存可视化」：画出 char / int 在内存里到底怎么摆
        AppendGrammarDiagramPages(list); // 第 20~34 项的第 3 页：控制流 / 数据结构这类没有字节可看的知识点改用逻辑图（方框 + 箭头 + 分步点亮）
        return list;
    }

    // ================== 第 2 页：零基础手把手细节（每任务一页，任务卡右上角 ‹ › 翻页） ==================

    /// <summary>
    /// 给语法课 49 项任务补上「第 2 页」：完全不懂代码的人照着做就能完成的那一页。
    /// 第 1 页讲写法与示例，第 2 页讲「这一步到底在做什么 / 手把手第几步按哪里 / 逐行拆解 / 新手最容易踩的坑」，
    /// 并参照 runoob C# 教程补充了类型转换、运算符优先级、真值表、数组与 List 差异、局部函数等概念。
    /// </summary>
    private static void AppendGrammarDetailPages(TaskDef[] list)
    {
        list[0].Page2Zh = "【这一页：亲手敲一遍，再故意写错一次】\n"
            + "① 点上方代码区空白处，让光标在那里闪，就可以开始敲。\n"
            + "② 敲两个英文斜杠 //，后面随便写一句话，回车。\n"
            + "③ 需要写多行说明就用 /* 开头、*/ 结尾，中间可以换行。\n"
            + "④ 点【编译运行 (F6)】：注释不参与运行，能编译就是对的。\n"
            + "【逐符号读懂它】\n"
            + "· // ：从这里到本行末尾，全部给人看，程序看不见。\n"
            + "· /* */ ：成对出现，中间的换行也算注释，不能只写一半。\n"
            + "【三个必踩的坑】\n"
            + "✘ 中文输入法敲出「／／」→ 全角斜杠报 CS1056 意外字符，切英文再敲。\n"
            + "✘ 只写 /* 不写 */ → 报 CS1035 注释未结束，后面代码全被吃掉。\n"
            + "✔ 想临时屏蔽一行代码：在行首加 //，比删掉更安全。";
        list[0].Page2En = "THIS PAGE: TYPE IT YOURSELF, THEN BREAK IT ON PURPOSE\n"
            + "1) Click a blank spot in the code area and let the caret blink.\n"
            + "2) Type two forward slashes // and any sentence after them, then Enter.\n"
            + "3) For several lines use /* to open and */ to close; newlines are allowed.\n"
            + "4) Press [Compile & Run (F6)]. A comment never runs; compiling clean means it is valid.\n"
            + "READ EVERY SYMBOL\n"
            + "- // : from here to the end of the line is for humans only.\n"
            + "- /* */ : always a pair; half a pair swallows the rest of the file.\n"
            + "THREE TRAPS\n"
            + "- Full-width slashes from a Chinese IME give CS1056; switch to English input.\n"
            + "- Missing */ gives CS1035 and eats every line below it.\n"
            + "- To mute one line fast, put // in front of it instead of deleting.";
        list[1].Page2Zh = "【这一页：亲手敲出四个不同类型的变量】\n"
            + "① 光标进代码区，先敲类型：int / float / bool / string。\n"
            + "② 空一格写名字：只能用字母、数字、下划线，不能用数字开头。\n"
            + "③ 写 = 和初始值，行末必须敲英文分号 ;。\n"
            + "④ 四行敲完，点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· int hp = 100; int 只装整数，= 把右边放进左边，; 表示一句结束。\n"
            + "· float sp = 1.5f; 尾巴那个 f 必须写，否则 1.5 被当成 double 而报错。\n"
            + "· bool ok = true; 只装 true / false，不加引号。\n"
            + "· string tag = \"塔\"; 文字要用英文双引号，中文引号不认。\n"
            + "【三个必踩的坑】\n"
            + "✘ int hp = 1.5; → 报 CS0266 无法把 double 隐式转成 int，小数要用 float。\n"
            + "✘ 行末漏分号 → 报 CS1002 应输入 ;。\n"
            + "✔ 类型转换：int a = 3; float b = a; 小盒子倒大盒子自动转；(int)2.8f 得 2，要强制。";
        list[1].Page2En = "THIS PAGE: TYPE FOUR VARIABLES OF DIFFERENT TYPES\n"
            + "1) In the code area start with a type: int / float / bool / string.\n"
            + "2) Space once, then the name: letters, digits, underscore, never starting with a digit.\n"
            + "3) Type = and the starting value, then an English semicolon ;.\n"
            + "4) After four lines press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- int hp = 100;  int holds whole numbers only; = copies right into left; ; ends the statement.\n"
            + "- float sp = 1.5f;  the trailing f is required, else 1.5 is a double and will not fit.\n"
            + "- bool ok = true;  only true / false, no quotes.\n"
            + "- string tag = \"tower\";  text needs double quotes; curly quotes are rejected.\n"
            + "THREE TRAPS\n"
            + "- int hp = 1.5; gives CS0266, use float for decimals.\n"
            + "- A missing semicolon gives CS1002.\n"
            + "- Conversion: int a = 3; float b = a; widens free; (int)2.8f narrows to 2 by hand.";
        list[2].Page2Zh = "【这一页：让编译器替你写类型】\n"
            + "① 敲 var hp = 100; 前面不要写 int。\n"
            + "② 再敲 var speed = 1.5f; 小数尾巴那个 f 必须留着。\n"
            + "③ 最后敲 var tag = \"塔\"; 文字照旧用英文双引号。\n"
            + "④ 点【编译运行 (F6)】：能编译就说明三个类型都推断对了。\n"
            + "【逐符号读懂它】\n"
            + "· var ：不是一种类型，是「请看右边，替我填上类型」。\n"
            + "· var hp = 100; 等价于 int hp = 100;，之后 hp 就是 int，装不进 1.5。\n"
            + "· var 必须当场赋值：var x; 编译器没东西可看，直接报错。\n"
            + "【三个必踩的坑】\n"
            + "✘ var x; → 报 CS0818 隐式类型的变量必须已初始化。\n"
            + "✘ var hp = 100; 之后写 hp = 1.5f; → 推断成 int 再塞小数，报 CS0266。\n"
            + "✔ 右边一眼能看出类型时才用 var（new List<int>() 这种长类型最省事）；纯数字的场合写明类型更好读。";
        list[2].Page2En = "THIS PAGE: LET THE COMPILER WRITE THE TYPE\n"
            + "1) Type var hp = 100; with no int in front.\n"
            + "2) Then var speed = 1.5f; the trailing f must stay.\n"
            + "3) Finally var tag = \"tower\"; text still needs double quotes.\n"
            + "4) Press [Compile & Run (F6)]: a clean compile means all three types were inferred right.\n"
            + "READ EVERY SYMBOL\n"
            + "- var : not a type at all, but \"look right and fill the type in for me\".\n"
            + "- var hp = 100; equals int hp = 100;; afterwards hp is int and will not take 1.5.\n"
            + "- var must be initialised on the spot: var x; gives the compiler nothing to look at.\n"
            + "THREE TRAPS\n"
            + "- var x; gives CS0818, an implicitly typed variable must be initialised.\n"
            + "- Writing hp = 1.5f; after var hp = 100; gives CS0266.\n"
            + "- Use var only when the right side is obvious; for plain numbers, name the type for readability.";
        list[3].Page2Zh = "【这一页：把「不该变的数」交给 const 保管】\n"
            + "① 先敲 const，紧跟类型 int / string，再空一格写名字。\n"
            + "② 写 = 和值，行末分号；命名习惯全大写，如 MAX_HP。\n"
            + "③ 再写一行 int x = MAX_HP; x = 6; 观察常量本身不许改。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· const int MAX_HP = 100; const 表示这个盒子封死后不许再改。\n"
            + "· 必须在声明同一行就给值，不能先 const int a; 之后再赋值。\n"
            + "· 想改常量会报 CS0131，赋值号左边必须是可赋值变量。\n"
            + "· 常量天生适合放「魔法数字」：冷却、血量上限、计分倍数。\n"
            + "【三个必踩的坑】\n"
            + "✘ 用 const 装会变的数（比如当前血量）→ 一改就编译不过。\n"
            + "✘ const 后面忘了写类型 → const 不是类型，语法错误。\n"
            + "✔ 同一个数字在多处用到，就定义成常量，改一处全局生效。";
        list[3].Page2En = "THIS PAGE: PUT NUMBERS THAT MUST NOT CHANGE INTO const\n"
            + "1) Type const, then the type int / string, then a space and the name.\n"
            + "2) Type = and the value, then a semicolon; name it in caps like MAX_HP.\n"
            + "3) Add int x = MAX_HP; x = 6; and see that the constant itself cannot change.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- const int MAX_HP = 100;  const seals the box after the first value.\n"
            + "- The value must be given on the same line; const int a; then a = 5; is illegal.\n"
            + "- Assigning to a constant raises CS0131.\n"
            + "- Constants are for magic numbers: cooldowns, HP caps, score multipliers.\n"
            + "THREE TRAPS\n"
            + "- A const holding a value that must change will fail to compile later.\n"
            + "- Forgetting the type after const is a syntax error; const is not a type.\n"
            + "- One number used in many places should be a constant so one edit fixes all.";
        list[4].Page2Zh = "【这一页：用 C# 当计算器，算四道题】\n"
            + "① 敲 int total = 2 + 3 * 4; 先猜结果再看下一段。\n"
            + "② 敲 int t2 = 7 / 2; 再敲 float half = 7 / 2f; 对比两者的差别。\n"
            + "③ 敲 int rest = 7 % 3; % 是取余数，7 除 3 余 1。\n"
            + "④ 点【编译运行 (F6)】，用 PrintLine 把四个值打到荧幕上。\n"
            + "【逐符号读懂它】\n"
            + "· + - * / ：加减乘除；乘是星号 *，写成 x 或 × 都不认。\n"
            + "· % ：取余数，判断奇偶就用 n % 2 == 0。\n"
            + "· 优先级：先 * / %，再 + -，有括号先算括号，和小学数学一样。\n"
            + "· total 是 14 不是 20，因为 3 * 4 先算。\n"
            + "· 7 / 2 是 3，整数除整数小数被直接丢掉。\n"
            + "【三个必踩的坑】\n"
            + "✘ 想要小数却写 7 / 2 → 得 3；要 3.5 请写 7 / 2f 或 7f / 2。\n"
            + "✘ 除以 0 → 运行时报 DivideByZeroException，程序直接中断。\n"
            + "✔ 拿不准就加括号：(2 + 3) * 4 = 20，读起来也清楚。";
        list[4].Page2En = "THIS PAGE: USE C# AS A CALCULATOR, FOUR EXERCISES\n"
            + "1) Type int total = 2 + 3 * 4; guess the result before reading on.\n"
            + "2) Type int t2 = 7 / 2; then float half = 7 / 2f; and compare both.\n"
            + "3) Type int rest = 7 % 3; the % sign returns the remainder, here 1.\n"
            + "4) Press [Compile & Run (F6)] and PrintLine all four values.\n"
            + "READ EVERY SYMBOL\n"
            + "- + - * / : add, subtract, multiply, divide. Multiplication is *, not x.\n"
            + "- % : remainder, handy for odd/even with n % 2 == 0.\n"
            + "- Order: * / % first, then + -, brackets win, exactly like school maths.\n"
            + "- total is 14, not 20, because 3 * 4 runs first.\n"
            + "- 7 / 2 is 3: integer division throws the fraction away.\n"
            + "THREE TRAPS\n"
            + "- 7 / 2 gives 3; write 7 / 2f or 7f / 2 for 3.5.\n"
            + "- Dividing by zero throws DivideByZeroException at runtime.\n"
            + "- When in doubt add brackets: (2 + 3) * 4 = 20.";
        list[5].Page2Zh = "【这一页：让代码自己决定走哪条路】\n"
            + "① 先写 if (hp <= 0) { ... }：括号里必须是一个能算出 true / false 的问题。\n"
            + "② 补上 else if (hp < hpMax && hpMax > 0) { ... }：前面不成立时才轮到它。\n"
            + "③ 最后写 else { ... } 兜底，再另起 switch (hpMax) { case 3: ... break; default: ... break; }。\n"
            + "④ 点【编译运行 (F6)】：荧幕上应该出现 damaged 2 与 cap is 3 两行。\n"
            + "【逐符号读懂它】\n"
            + "· <=、<、== 这些比较运算当场算出 true / false，if 只看这个结果。\n"
            + "· && 两边都真才真，|| 一边真就真，! 取反。\n"
            + "· else if 可以串很多层，从上往下判，谁先成立走谁，后面的不再看。\n"
            + "· switch 的 case 用冒号结尾，结尾写 break 才跳出整个 switch。\n"
            + "【三个必踩的坑】\n"
            + "✘ if (hp = 0) → 少一个等号，赋值不能当条件，报 CS0029。\n"
            + "✘ case 里漏 break; → 报 CS0163，前一支会一路串下来。\n"
            + "✔ switch 每个 case 的值必须唯一且是常量，重复了报 CS0152。";
        list[5].Page2En = "THIS PAGE: LET THE CODE DECIDE WHICH WAY TO GO\n"
            + "1) Start with if (hp <= 0) { ... }: the brackets must hold something that evaluates to true / false.\n"
            + "2) Add else if (hp < hpMax && hpMax > 0) { ... } - it is only tested when the first check fails.\n"
            + "3) Close with else { ... }, then start switch (hpMax) { case 3: ... break; default: ... break; }.\n"
            + "4) Press [Compile & Run (F6)]: damaged 2 and cap is 3 should both appear.\n"
            + "READ THE SYMBOLS\n"
            + "- <=, < and == produce true / false on the spot; if only looks at that result.\n"
            + "- && needs both true, || needs one, ! inverts.\n"
            + "- else if can chain many times: tested top to bottom, first match wins, the rest are skipped.\n"
            + "- switch cases end with a colon, and break leaves the whole switch.\n"
            + "THREE CLASSIC MISTAKES\n"
            + "X if (hp = 0) - one = too few; an assignment is not a condition (CS0029).\n"
            + "X a case without break; - CS0163: control cannot fall through.\n"
            + "V every case value must be a unique constant; a duplicate gives CS0152.";
        list[6].Page2Zh = "【这一页：亲手数三次】\n"
            + "① 敲 for (int i = 0; i < 3; i++)：三段之间是英文分号，不是逗号。\n"
            + "② 下一行敲 {，回车写 tower.PrintLine(\"shot {0}\", i);，再敲 }。\n"
            + "③ 想数到 5 就只改中间那个 3，前后两段不用动。\n"
            + "④ 点【编译运行 (F6)】：荧幕上应该正好三行 shot 0 / 1 / 2。\n"
            + "【逐符号读懂它】\n"
            + "· int i = 0 ：只在进门时做一次，造出计数器。\n"
            + "· i < 3 ：每轮进门先问一次，答 false 就结束循环。\n"
            + "· i++ ：每轮出门做一次，等于 i = i + 1。\n"
            + "【三个必踩的坑】\n"
            + "✘ for (int i = 0, i < 3, i++) → 报 CS1003 语法错误，三段之间必须是分号。\n"
            + "✘ 漏掉 i++ → 条件永远成立，程序卡死在循环里（死循环）。\n"
            + "✔ i <= 3 会跑 4 次（0/1/2/3）；要正好 3 次就写 i < 3。";
        list[6].Page2En = "THIS PAGE: COUNT TO THREE BY HAND\n"
            + "1) Type for (int i = 0; i < 3; i++): the three parts need English semicolons, not commas.\n"
            + "2) Next line type {, then tower.PrintLine(\"shot {0}\", i);, then }.\n"
            + "3) To count to five change only the 3 in the middle.\n"
            + "4) Press [Compile & Run (F6)]: exactly three lines shot 0 / 1 / 2.\n"
            + "READ EVERY SYMBOL\n"
            + "- int i = 0 runs once on entry and creates the counter.\n"
            + "- i < 3 is asked at the top of every round; false ends the loop.\n"
            + "- i++ runs at the end of every round and means i = i + 1.\n"
            + "THREE TRAPS\n"
            + "- for (int i = 0, i < 3, i++) gives CS1003: those separators must be semicolons.\n"
            + "- Dropping i++ keeps the condition true forever: an infinite loop.\n"
            + "- i <= 3 runs four times (0/1/2/3); use i < 3 for exactly three.";
        list[7].Page2Zh = "【这一页：一个名字，装一排数】\n"
            + "① 敲 int[] hp = new int[3]; 声明能装 3 个整数的数组。\n"
            + "② 下标从 0 开始：hp[0] = 10; hp[1] = 20; hp[2] = 30;\n"
            + "③ 也可以一次写全：int[] a = new int[] { 1, 2, 3 };\n"
            + "④ 用 a.Length 取长度，点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· int[] ：方括号表示这是数组，不是单个 int。\n"
            + "· new int[3] 分配 3 个坑位，每个默认值都是 0。\n"
            + "· hp[0] 读第一个，最大下标是 Length - 1，写 hp[3] 就越界。\n"
            + "· foreach (int v in a) 能逐个取出值，不用自己管下标。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写 hp[3] → 报 IndexOutOfRangeException 越界，运行中断。\n"
            + "✘ 声明写 int hp[] → C# 要求方括号跟类型：int[] hp。\n"
            + "✔ 数组长度定死不能改，数量会变就改用 List。";
        list[7].Page2En = "THIS PAGE: ONE NAME HOLDING A WHOLE ROW OF VALUES\n"
            + "1) Type int[] hp = new int[3]; to declare an array of three integers.\n"
            + "2) Indexing starts at 0: hp[0] = 10; hp[1] = 20; hp[2] = 30;\n"
            + "3) Or fill it at once: int[] a = new int[] { 1, 2, 3 };\n"
            + "4) Read the size with a.Length and press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- int[] : the brackets make it an array instead of a single int.\n"
            + "- new int[3] reserves three slots, each defaulting to 0.\n"
            + "- hp[0] is the first slot; the highest index is Length - 1, hp[3] overflows.\n"
            + "- foreach (int v in a) walks the values without touching indexes.\n"
            + "THREE TRAPS\n"
            + "- hp[3] throws IndexOutOfRangeException and kills the run.\n"
            + "- int hp[] is not C#; brackets follow the type: int[] hp.\n"
            + "- An array size is fixed. When the count varies, use a List.";
        list[8].Page2Zh = "【这一页：会自己长个儿的「数组」】\n"
            + "① 文件第一行必须有 using System.Collections.Generic; 否则 List 不认。\n"
            + "② 敲 List<int> hp = new List<int>(); 尖括号里是元素类型。\n"
            + "③ 加元素 hp.Add(10); 取元素 hp[0]，数量看 hp.Count。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· List<int> ：尖括号里的 int 表示这个列表只装整数。\n"
            + "· new List<int>() 后面那对圆括号不能少，那是构造调用。\n"
            + "· Count 是属性不是方法：写 hp.Count，不要写 hp.Count()。\n"
            + "· 删除用 hp.Remove(10) 或 hp.RemoveAt(0)，数组做不到这些。\n"
            + "【三个必踩的坑】\n"
            + "✘ 只写 using System.Collections; → 缺 Generic，报 CS0246 找不到 List。\n"
            + "✘ 用 hp.Length → List 没有 Length，只有 Count。\n"
            + "✔ 数量事先不知道用 List；长度固定又要快就用数组。";
        list[8].Page2En = "THIS PAGE: AN ARRAY THAT GROWS BY ITSELF\n"
            + "1) The first line needs using System.Collections.Generic; or List is unknown.\n"
            + "2) Type List<int> hp = new List<int>(); the type goes inside the angle brackets.\n"
            + "3) Add with hp.Add(10); read with hp[0]; count with hp.Count.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- List<int> : the angle brackets say this list holds integers only.\n"
            + "- The empty () after new List<int> is required, it is the constructor call.\n"
            + "- Count is a property, so write hp.Count, never hp.Count().\n"
            + "- Remove with hp.Remove(10) or hp.RemoveAt(0); arrays cannot do this.\n"
            + "THREE TRAPS\n"
            + "- using System.Collections; alone gives CS0246; Generic is required.\n"
            + "- hp.Length does not exist on a List, only Count.\n"
            + "- Unknown count: use List. Fixed length and speed: use an array.";
        list[9].Page2Zh = "【这一页：把一整排数挨个读出来】\n"
            + "① 先备好集合：int[] hits = new int[3]; 再给 hits[0]、hits[1] 赋值。\n"
            + "② 敲 foreach (int h in hits)，括号里是「元素类型 变量名 in 集合」。\n"
            + "③ 循环体里直接用 h 打印，不用写任何下标。\n"
            + "④ 点【编译运行 (F6)】：荧幕上按顺序出现每个元素。\n"
            + "【逐符号读懂它】\n"
            + "· foreach ：从头到尾自动走一遍，走几步由集合长度决定，数错了也不会越界。\n"
            + "· int h ：每轮拿到的是一份副本（值类型是复制），所以改 h 不会改到 hits 里。\n"
            + "· in hits ：被遍历的集合，数组和 List 都能这么写。\n"
            + "【三个必踩的坑】\n"
            + "✘ foreach (h in hits) → 报 CS1525 语法错误，元素类型要写出来（或写 var h）。\n"
            + "✘ 在循环里给 h 赋值（h = 5;）→ 报 CS1656，foreach 的循环变量是只读的。\n"
            + "✔ 需要下标、或者要边遍历边改数组，就换 for，别硬用 foreach。";
        list[9].Page2En = "THIS PAGE: READ A WHOLE ROW OF VALUES\n"
            + "1) Prepare the collection: int[] hits = new int[3]; then set hits[0] and hits[1].\n"
            + "2) Type foreach (int h in hits): element type, name, then in, then the collection.\n"
            + "3) Print h inside the body - no index anywhere.\n"
            + "4) Press [Compile & Run (F6)]: every element shows up in order.\n"
            + "READ EVERY SYMBOL\n"
            + "- foreach walks from the first element to the last; the count comes from the collection, so no out-of-range.\n"
            + "- int h receives a copy each round (value types are copied), so changing h never changes hits.\n"
            + "- in hits is the collection being walked; arrays and List both work.\n"
            + "THREE TRAPS\n"
            + "- foreach (h in hits) gives CS1525; name the element type (or write var h).\n"
            + "- Assigning to h inside the body (h = 5;) gives CS1656: the loop variable is read-only.\n"
            + "- If you need the index, or must edit the array while walking, use for instead.";
        list[10].Page2Zh = "【这一页：条件成立就一直转下去】\n"
            + "① 先在循环外面备好变量：int n = 3;\n"
            + "② 敲 while (n > 0) {，花括号里写要重复的事。\n"
            + "③ 关键：花括号里必须让 n 变小（n--;），否则永不退出。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· while (条件)：每圈开始前检查一次，真就再转，假就退出。\n"
            + "· 条件一开始就是假 → 花括号一次都不进，这是它和 do-while 的区别。\n"
            + "· n-- 等于 n = n - 1，也可以写 n -= 1。\n"
            + "· 不知道自己会转多少圈时，用 while 比 for 更自然。\n"
            + "【三个必踩的坑】\n"
            + "✘ 花括号里忘了改条件变量 → 死循环，按【停止 (F7)】退出。\n"
            + "✘ while (n > 0); 多写一个分号 → 变成空循环，永远卡在这行。\n"
            + "✔ 只有循环体里一定会变的东西，才适合当 while 的条件。";
        list[10].Page2En = "THIS PAGE: KEEP SPINNING WHILE THE CONDITION HOLDS\n"
            + "1) Prepare the variable outside the loop: int n = 3;\n"
            + "2) Type while (n > 0) { and put the repeated work inside the braces.\n"
            + "3) Critical: something inside must shrink n (n--;), or the loop never ends.\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- while (condition) checks before every round: true spins again, false exits.\n"
            + "- A condition that starts false means the body never runs, unlike do-while.\n"
            + "- n-- equals n = n - 1, also written n -= 1.\n"
            + "- When the number of rounds is unknown, while reads better than for.\n"
            + "THREE TRAPS\n"
            + "- Forgetting to change the condition variable loops forever; press [Stop (F7)].\n"
            + "- while (n > 0); with a stray semicolon becomes an empty endless loop.\n"
            + "- Only something that really changes inside the body belongs in the condition.";
        list[11].Page2Zh = "【这一页：先干一次，再问要不要继续】\n"
            + "① 先敲 do {，里面写要做的事，别忘了改条件变量。\n"
            + "② 写完 } 紧跟 while (条件); 这里的分号必须有。\n"
            + "③ 条件为真就回去再执行一轮 do 里的内容。\n"
            + "④ 点【编译运行 (F6)】，把初值改成不满足条件，看它是否仍跑一次。\n"
            + "【逐符号读懂它】\n"
            + "· do { } while (条件); 先执行一次再判断，所以至少执行一次。\n"
            + "· while 后面那个分号不能漏，漏了报 CS1002 / CS1525。\n"
            + "· 初值 n = 0 且条件 n > 0：while 一次不跑，do-while 会跑一次。\n"
            + "· 适合「菜单至少显示一次」「先出手再判断」这类场景。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写成 do (条件) { } → do 后面直接跟花括号，不带条件。\n"
            + "✘ 漏掉 while 后面的分号 → 编译直接报错。\n"
            + "✔ 只要逻辑是「无论如何先做一次」，就用 do-while。";
        list[11].Page2En = "THIS PAGE: ACT FIRST, THEN ASK WHETHER TO REPEAT\n"
            + "1) Type do { and write the work inside, remembering to change the condition variable.\n"
            + "2) After the closing brace follow with while (condition); the semicolon is required.\n"
            + "3) A true condition sends you back for another round.\n"
            + "4) Press [Compile & Run (F6)], then start with a false value and see it still run once.\n"
            + "READ EVERY SYMBOL\n"
            + "- do { } while (condition);  runs once, then decides, so the body runs at least once.\n"
            + "- The semicolon after while may not be omitted; missing it gives CS1002 / CS1525.\n"
            + "- Starting at n = 0 with n > 0: a while loop skips, a do-while still runs once.\n"
            + "- Perfect for show the menu at least once or fire first, judge later.\n"
            + "THREE TRAPS\n"
            + "- do (condition) { } is wrong; do goes straight to the brace.\n"
            + "- Dropping the semicolon after while fails to compile.\n"
            + "- If the logic is always do it once, choose do-while.";
        list[12].Page2Zh = "【这一页：把一段代码打包成一个按钮】\n"
            + "① 先敲返回类型 void，空格，方法名（首字母大写，如 FireOnce）。\n"
            + "② 紧跟一对圆括号，括号里写参数：类型 名字，多个用逗号隔开。\n"
            + "③ 下一行花括号里放这段代码要做的事。\n"
            + "④ 写完必须调用一次：FireOnce(); 定义不等于执行。\n"
            + "【逐符号读懂它】\n"
            + "· void 表示不交回答案，只做事。\n"
            + "· (int x, float y) 是参数表，调用时按顺序把值交进去。\n"
            + "· 本课程的方法写在当前代码里，调用它的语句要放在它下面或同一段内。\n"
            + "· 名字后面那对圆括号才是调用，只写名字不会执行。\n"
            + "【三个必踩的坑】\n"
            + "✘ 方法名中间带空格（fire once）→ 语法错误，名字是一个整体。\n"
            + "✘ 定义了却从不调用 → 编译能过，但荧幕毫无反应，判定也不认。\n"
            + "✔ 参数个数与类型必须和定义完全一致，多一个少一个报 CS1501。";
        list[12].Page2En = "THIS PAGE: PACK A BLOCK OF CODE INTO ONE BUTTON\n"
            + "1) Type the return type void, a space, then the name in PascalCase such as FireOnce.\n"
            + "2) Add round brackets holding the parameters: type then name, comma separated.\n"
            + "3) On the next line put the work inside braces.\n"
            + "4) Then call it once: FireOnce(); a definition alone does nothing.\n"
            + "READ EVERY SYMBOL\n"
            + "- void means nothing is returned, it only performs an action.\n"
            + "- (int x, float y) is the parameter list; arguments arrive in order.\n"
            + "- In this course the method lives in your own code; the call goes below it.\n"
            + "- The brackets after the name are the call. The bare name never executes.\n"
            + "THREE TRAPS\n"
            + "- A space inside the name (fire once) is a syntax error.\n"
            + "- Defining without calling compiles but prints nothing, so the checker refuses it.\n"
            + "- Argument count and types must match exactly, otherwise CS1501.";
        list[13].Page2Zh = "【这一页：让方法把答案交回来】\n"
            + "① 把返回类型从 void 改成 int（要交回什么类型就写什么）。\n"
            + "② 方法体最后写 return 结果; 交回答案。\n"
            + "③ 调用处接住它：int n = AddHp(1, 2);\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· int AddHp(int a, int b) ：开头的 int 是交回的类型，不再是 void。\n"
            + "· return a + b; 交回 a + b 的值，同时立刻结束这个方法。\n"
            + "· return 之后的语句永远不会执行，别在那之后写代码。\n"
            + "· 非 void 方法必须保证每条路径都有 return，否则报 CS0161。\n"
            + "【三个必踩的坑】\n"
            + "✘ 声明成 int 却只写 return; → return 后面必须跟一个值。\n"
            + "✘ 忘写 return → 报 CS0161 并非所有代码路径都返回值。\n"
            + "✔ 只要方法要「算出个数、算出伤害」，就该让它带返回值。";
        list[13].Page2En = "THIS PAGE: MAKE THE METHOD HAND AN ANSWER BACK\n"
            + "1) Change the return type from void to int, or whatever type you return.\n"
            + "2) End the body with return value; to hand the answer back.\n"
            + "3) Catch it where you call: int n = AddHp(1, 2);\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- int AddHp(int a, int b) : the leading int is what comes back, no longer void.\n"
            + "- return a + b; hands back the value and ends the method immediately.\n"
            + "- Anything after return never runs, so do not write code there.\n"
            + "- A non-void method must return on every path, or you get CS0161.\n"
            + "THREE TRAPS\n"
            + "- Declaring int but writing return; fails, a value is required.\n"
            + "- Forgetting return gives CS0161, not all code paths return a value.\n"
            + "- Any method that computes a count or damage should return a value.";
        list[14].Page2Zh = "【这一页：把文字和数字接成一句话】\n"
            + "① 先备变量：int n = 3; string tag = \"塔\";\n"
            + "② 用 + 接起来：string s = tag + \" 有 \" + n + \" 个\";\n"
            + "③ 想换行就在字符串里写 \\n，要输出就 tower.PrintLine(s);\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· + 两边只要有一边是 string，另一边就会自动变成文字。\n"
            + "· 引号里的空格也算字符，\" 有 \" 的前后空格是故意留的。\n"
            + "· 3 + \" 个\" 得 \"3 个\"，但 3 + 4 得 7，看的是运算顺序与类型。\n"
            + "· tag + n 与 n + tag 结果一样，但 n + 3 会先做加法再拼字符串。\n"
            + "【三个必踩的坑】\n"
            + "✘ 用中文引号「塔」或“塔” → 报 CS1010 / CS1012，必须英文双引号。\n"
            + "✘ 想装文字却用单引号 '塔' → 单引号只装一个字符，多字报错。\n"
            + "✔ 想先算数再拼：写成 \"共 \" + (n + 1) + \" 个\"，把算式用括号包起来。";
        list[14].Page2En = "THIS PAGE: GLUE TEXT AND NUMBERS INTO ONE SENTENCE\n"
            + "1) Prepare values: int n = 3; string tag = \"tower\";\n"
            + "2) Glue with +: string s = tag + \" has \" + n + \" blocks\";\n"
            + "3) Put \\n inside the string to break a line; print with tower.PrintLine(s);\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- When either side of + is a string, the other side turns into text.\n"
            + "- Spaces inside the quotes count, so \" has \" really has them.\n"
            + "- 3 + \" blocks\" gives \"3 blocks\" while 3 + 4 gives 7: order and types matter.\n"
            + "- n + 3 adds numbers first, then the sum is glued to a string.\n"
            + "THREE TRAPS\n"
            + "- Curly quotes like \"tower\" give CS1010 / CS1012; use straight double quotes.\n"
            + "- Single quotes are for one char only; 'tower' is invalid.\n"
            + "- To add before gluing write \"total \" + (n + 1) + \" blocks\".";
        list[15].Page2Zh = "【这一页：把变量塞进一句话的占位符】\n"
            + "① 一句话里先写占位符 {0} {1}，编号从 0 开始。\n"
            + "② 逗号后面按顺序把值填进去：string.Format(\"塔 {0} 有 {1} 个\", 1, 3);\n"
            + "③ 也可以直接 tower.PrintLine(\"块数={0}\", n); 省掉拼接。\n"
            + "④ 点【编译运行 (F6)】。\n"
            + "【逐符号读懂它】\n"
            + "· {0} 是第 0 个值的坑位，{1} 是第 1 个，可以重复用也可以乱序用。\n"
            + "· 占位符数量比给的参数多 → 运行时抛 FormatException。\n"
            + "· {0:F2} 保留两位小数，{0,5} 占 5 个字符宽度并靠右对齐。\n"
            + "· string.Format 返回一个新字符串，不会改动原来的变量。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写了 {1} 却只给一个参数 → 运行时报 FormatException 中断。\n"
            + "✘ 占位符用中文花括号｛0｝→ 编辑器不认，必须英文 { }。\n"
            + "✔ 句子长、变量多时用 {0} 代替拼接，少写很多加号和引号。";
        list[15].Page2En = "THIS PAGE: DROP VARIABLES INTO PLACEHOLDERS\n"
            + "1) Put placeholders {0} {1} inside the sentence, numbered from 0.\n"
            + "2) After the comma pass the values in order: string.Format(\"tower {0} has {1}\", 1, 3);\n"
            + "3) Or print directly: tower.PrintLine(\"blocks={0}\", n);\n"
            + "4) Press [Compile & Run (F6)].\n"
            + "READ EVERY SYMBOL\n"
            + "- {0} is the slot for the first value, {1} the second; they may repeat or reorder.\n"
            + "- More placeholders than arguments throws FormatException at runtime.\n"
            + "- {0:F2} keeps two decimals; {0,5} pads to five characters, right aligned.\n"
            + "- string.Format returns a new string and never changes the original variable.\n"
            + "THREE TRAPS\n"
            + "- Writing {1} with only one argument throws FormatException.\n"
            + "- Full-width braces from a Chinese IME are rejected; use { }.\n"
            + "- Long sentences with many values are cleaner with {0} than with +.";
        list[16].Page2Zh = "【这一页：把三个数打包成一个向量】\n"
            + "① 敲 Vector3 up = new Vector3(0f, 1f, 0f); —— new 出来的就是一个值。\n"
            + "② 用 up.x / up.y / up.z 单独取分量。\n"
            + "③ 整个 up 直接当参数：tower.Fire(up); 朝 +Y 打一发。\n"
            + "④ Vector2 只有 x / y，适合平面上的方向。\n"
            + "【为什么要打包】\n"
            + "· 方向、位置、速度本质上都是三个数，打包后一个变量就能传。\n"
            + "· tower.Fire(up) 比 tower.Fire(0f, 1f, 0f) 更好读，也不会把分量顺序记错。\n"
            + "【三个必踩的坑】\n"
            + "✘ new Vector3(0, 1, 0) → 整数会隐式转 float，能过；但 1f / 2 这种会被整除，一律写 f。\n"
            + "✘ new Vector2(1f) → Vector2 至少要两个参数，报 CS7036。\n"
            + "✔ 零向量 new Vector3(0f, 0f, 0f) 传给 tower.Fire 只会返回 false，别拿它试炮。";
        list[16].Page2En = "THIS PAGE: PACK THREE NUMBERS INTO ONE VECTOR\n"
            + "1) Type Vector3 up = new Vector3(0f, 1f, 0f); - new makes one value.\n"
            + "2) Read single parts with up.x / up.y / up.z.\n"
            + "3) Pass the whole thing: tower.Fire(up); fires along +Y.\n"
            + "4) Vector2 has only x / y - good for flat directions.\n"
            + "WHY PACK THEM\n"
            + "- Direction, position and velocity are all three numbers; packing lets one variable carry them.\n"
            + "- tower.Fire(up) reads better than tower.Fire(0f, 1f, 0f) and cannot mix up the order.\n"
            + "THREE CLASSIC MISTAKES\n"
            + "X new Vector3(0, 1, 0) - ints convert implicitly, but 1f / 2 drops the fraction; always write f.\n"
            + "X new Vector2(1f) - Vector2 needs two arguments (CS7036).\n"
            + "V tower.Fire(new Vector3(0f, 0f, 0f)) only returns false - do not test the gun with a zero vector.";
        list[17].Page2Zh = "【这一页：new 一个对象，再用点号使唤它】\n"
            + "① Vector3 dir = new Vector3(0f, 0f, 1f); —— new 负责造，= 负责存。\n"
            + "② tower.Fire(dir); —— 整个对象当参数传进去。\n"
            + "③ dir.z 取成员；tower.PrintLine(\"dir.z={0}\", dir.z); 打印出来。\n"
            + "④ 想换方向就再 new 一个赋给同一个变量，旧对象自然被替换。\n"
            + "【类、对象、成员】\n"
            + "· 类：图纸（Vector3、CodeTower、TowerObjectInfo）。\n"
            + "· 对象：按图纸造出来的实物（dir、tower）。\n"
            + "· 成员：对象身上的数据（.x）和方法（.Fire）。\n"
            + "【三个必踩的坑】\n"
            + "✘ Vector3 dir; 然后直接 dir.x = 1f; → 结构体没赋值就用，报 CS0165。\n"
            + "✘ tower.Fire(); → 少了参数，报 CS7036。\n"
            + "✔ 类型名区分大小写：vector3 不存在，永远写 Vector3。";
        list[17].Page2En = "THIS PAGE: NEW AN OBJECT, THEN USE IT WITH A DOT\n"
            + "1) Vector3 dir = new Vector3(0f, 0f, 1f); - new builds it, = stores it.\n"
            + "2) tower.Fire(dir); - pass the whole object as one argument.\n"
            + "3) dir.z reads a member; print it with tower.PrintLine(\"dir.z={0}\", dir.z);\n"
            + "4) To change direction, new another one into the same variable; the old object is simply replaced.\n"
            + "CLASS, OBJECT, MEMBER\n"
            + "- Class: the blueprint (Vector3, CodeTower, TowerObjectInfo).\n"
            + "- Object: something built from it (dir, tower).\n"
            + "- Member: data (.x) or methods (.Fire) on that object.\n"
            + "THREE CLASSIC MISTAKES\n"
            + "X Vector3 dir; then dir.x = 1f; - used before assignment (CS0165).\n"
            + "X tower.Fire(); - missing argument (CS7036).\n"
            + "V Type names are case sensitive: vector3 does not exist, always Vector3.";
        list[18].Page2Zh = "【这一页：GlobalCode 到底被搬到了哪里】\n"
            + "先把真相说清楚：你写的代码会被原样塞进 public void Run(CodeTower tower) { ... } 里面，而真实的 C# 不允许在方法体里声明字段。\n"
            + "GlobalCode 是作者刻意加的扩展语法：编译前编辑器把块里的行整段「剪」到类的最外层当字段，剩下的语句才留在 Run 里。\n"
            + "所以块里写 int shots = 0;，等于给你这个类加了一个字段 shots —— 每帧进来都是同一份，值能跨帧活着。\n"
            + "【能写 / 不能写】\n"
            + "✔ int / float / bool / string / char，数组 int[] hits = new int[3];，泛型 List<int> queue = new List<int>();\n"
            + "✘ tower.SetVisualVisible(true); —— 块里没有 tower，写了直接致命错误。\n"
            + "✘ shots = shots + 1; —— 块里只能声明，干活要写到下面的每帧语句里。\n"
            + "✘ 两个 GlobalCode 块 —— 只允许一个，多个请合并成一个。\n"
            + "【四个必踩的坑】\n"
            + "✘ my_global { ... } / code_global { ... } —— 早期草稿名，本作关键字已定名 GlobalCode，写旧名字会直接致命报错。\n"
            + "✘ GlobalCode = 0; / GlobalCode(); → 它不是变量也不是方法，会报致命错误提醒你补 {。\n"
            + "✘ GlobalCode { int shots = 0; 少了大括号 → 致命错误，块尾必须补 }。\n"
            + "✔ 点编译 = 类重新生成，全局区跟着归零；它记的是这次运行，不是永久存档。";
        list[18].Page2En = "THIS PAGE: WHERE GlobalCode ACTUALLY GOES\n"
            + "The truth first: your code is pasted verbatim inside public void Run(CodeTower tower) { ... }, and real C# does not allow field declarations in a method body.\n"
            + "GlobalCode is an extension the author added on purpose: before compiling, the editor cuts those lines out of the block and pastes them at class level as fields; only the rest stays in Run.\n"
            + "So int shots = 0; inside the block adds a field shots to your class - the same one every frame, so the value survives across frames.\n"
            + "ALLOWED / NOT ALLOWED\n"
            + "V int / float / bool / string / char, arrays like int[] hits = new int[3];, generics like List<int> queue = new List<int>();\n"
            + "X tower.SetVisualVisible(true); - there is no tower inside the block; that is a hard error.\n"
            + "X shots = shots + 1; - the block only declares; do the work in the per-frame code below.\n"
            + "X Two GlobalCode blocks - only one is allowed; merge them.\n"
            + "FOUR CLASSIC MISTAKES\n"
            + "X my_global { ... } / code_global { ... } - old draft names; the keyword here is GlobalCode, and an old name is a hard error.\n"
            + "X GlobalCode = 0; / GlobalCode(); - it is neither a variable nor a method; you get a fatal error asking for {.\n"
            + "X GlobalCode { int shots = 0; without the closing brace - fatal error; add } at the end.\n"
            + "V Compile = the class is rebuilt, so globals reset; they last for this run, not as a save file.";

        list[19].Page2Zh = "【这一页：亲手把循环切断两次】\n"
            + "① 先敲 for (int i = 1; i <= 5; i++) { tower.PrintLine(\"i={0}\", i); }，跑一次，1 到 5 全都打出来。\n"
            + "② 在循环体最前面加一行 if (i == 2) continue; 再跑：2 消失了，1、3、4、5 还在。\n"
            + "③ 把 continue 改成 break 再跑：只打出 1，循环整个结束。\n"
            + "④ 两句都留着，循环体最后再加一句 tower.PrintLine(\"end\"); 你会看到 1 → 3 →（i=4 时 break）→ 循环后面的语句。\n"
            + "【逐符号读懂它】\n"
            + "· break; 只跳出「最近的一层」循环，嵌套循环里它不管外层。\n"
            + "· continue; 不是退出循环，而是放弃本轮剩下的语句，回到 for 的第三段 i++ 再判条件。\n"
            + "· 两个都必须写在 if 里才有条件，直接写在循环体第一行等于无条件断掉。\n"
            + "· while / do-while / foreach 里是同一套写法，语义完全一样。\n"
            + "【三个必踩的坑】\n"
            + "✘ break; 或 continue; 写在循环外面 → 报 CS0139「没有可跳出或继续的外层循环」。\n"
            + "✘ break; 之后再写语句 → 报 CS0162「检测到无法访问的代码」，那句永远不会执行。\n"
            + "✘ while 里把 continue 放在 i++ 之前 → 编译能过，但 i 永远不变，运行死循环，按【停止 (F7)】退出。";
        list[19].Page2En = "THIS PAGE: CUT A LOOP TWICE WITH YOUR OWN HANDS\n"
            + "1) First type for (int i = 1; i <= 5; i++) { tower.PrintLine(\"i={0}\", i); } and run it: 1 to 5 all print.\n"
            + "2) Add if (i == 2) continue; at the top of the body and run again: 2 vanishes, 1 3 4 5 remain.\n"
            + "3) Change continue to break and run: only 1 prints and the loop is over.\n"
            + "4) Keep both and add tower.PrintLine(\"end\"); at the bottom: you get 1, then 3, then break at i=4, then the code after the loop.\n"
            + "READ EVERY SYMBOL\n"
            + "- break; leaves only the innermost loop; in nested loops it does not touch the outer one.\n"
            + "- continue; does not end the loop: it drops the rest of this round and goes back to i++ and the condition.\n"
            + "- Both only make sense inside an if; putting them on the first line kills the loop unconditionally.\n"
            + "- The same two keywords work identically in while / do-while / foreach.\n"
            + "THREE TRAPS\n"
            + "X break; or continue; outside a loop - CS0139: no enclosing loop to break or continue.\n"
            + "X Any statement after break; - CS0162: unreachable code detected; that line never runs.\n"
            + "X In a while loop, continue placed before i++ compiles fine but loops forever; press Stop (F7).";

        list[20].Page2Zh = "【这一页：把 if-else 压成一行】\n"
            + "① 先写传统的 int hpLeft = 3; string state; if (hpLeft > 0) { state = \"alive\"; } else { state = \"dead\"; }\n"
            + "② 再把它整段换成 string state = hpLeft > 0 ? \"alive\" : \"dead\";，两次输出必须一模一样。\n"
            + "③ 换一个数字（-1）再跑，看分支真的翻过去。\n"
            + "④ 再写一个数值版：int bonus = hpLeft > 5 ? 10 : 0; 体会「选一个值」和「做一堆事」的区别。\n"
            + "【逐符号读懂它】\n"
            + "· ? 前面的整段是条件，结果必须是 bool（不能写 hpLeft 本身）。\n"
            + "· ? 与 : 之间是条件成立时的值，: 后面是不成立时的值 —— 两个各只有一个表达式。\n"
            + "· 两个分支的类型要能统一：都是 string、都是 int，不能一边 string 一边 int。\n"
            + "· ?: 是表达式能嵌进别处：可以塞进 PrintLine 的参数、可以套第二层 ?:（但超过两层就该换 if）。\n"
            + "【三个必踩的坑】\n"
            + "✘ 忘掉冒号写成 hpLeft > 0 ? \"alive\" → 报 CS1003 语法错误，应输入 \":\"。\n"
            + "✘ 两个分支类型不一样（? 1 : \"none\"）→ 报 CS0173 无法确定条件表达式的类型。\n"
            + "✘ 单独写 hpLeft > 0 ? 1 : 2; 当语句 → 报 CS0201 只有赋值、调用、自增等才能作为语句。\n"
            + "✔ 还有一个隐蔽坑：中文全角问号 ？/ 冒号 ：不能当运算符，报 CS1056 意外的字符。";
        list[20].Page2En = "THIS PAGE: SQUEEZE if-else INTO ONE LINE\n"
            + "1) Write the classic int hpLeft = 3; string state; if (hpLeft > 0) { state = \"alive\"; } else { state = \"dead\"; }\n"
            + "2) Replace the whole block with string state = hpLeft > 0 ? \"alive\" : \"dead\"; - the output must be identical.\n"
            + "3) Try -1 so you see the branch really flip.\n"
            + "4) Then a numeric one: int bonus = hpLeft > 5 ? 10 : 0; - feel the difference between picking a value and doing work.\n"
            + "READ EVERY SYMBOL\n"
            + "- Everything before ? is the condition and must be a bool (hpLeft alone is not).\n"
            + "- Between ? and : is the true value; after : is the false value - exactly one expression each.\n"
            + "- Both branches must share a type: both string or both int, never mixed.\n"
            + "- ?: is an expression, so it nests: inside a PrintLine argument, or as a second layer (beyond two layers use if).\n"
            + "THREE TRAPS\n"
            + "X Forgetting the colon (hpLeft > 0 ? \"alive\") - CS1003 syntax error, ':' expected.\n"
            + "X Mixed branch types (? 1 : \"none\") - CS0173: no implicit conversion between the two types.\n"
            + "X Using it as a statement: hpLeft > 0 ? 1 : 2; - CS0201: only assignment, call, increment can be a statement.\n"
            + "V A sneaky one: a full-width ? or : is not an operator - CS1056 unexpected character.";

        list[21].Page2Zh = "【这一页：把 += 和 ++ 亲手算一遍】\n"
            + "① 敲 int shotCount = 0; 然后只写 shotCount += 3; 用 PrintLine 打出结果，确认是 3。\n"
            + "② 把 += 换成 = shotCount + 3 的老写法，结果一样，但左边名字写了两遍，容易打错。\n"
            + "③ 依次写 *= 2、-= 1、++、--，每步都打一次，把 0 → 3 → 6 → 5 → 6 → 5 走一遍。\n"
            + "④ 最后试 int before = shotCount++; 与 int after = ++shotCount;，看 before 和 after 差 1。\n"
            + "【逐符号读懂它】\n"
            + "· a += b 的意思是 a = a + b，读的时候先看等号右边那份「旧的 a」。\n"
            + "· -= *= /= %= 同理，除以 0 会抛 DivideByZeroException（/= 0）。\n"
            + "· a++ 是后置：先交出去旧值，再加 1；++a 是前置：先加 1，再交出去。单独一行的 a++; 两者没区别。\n"
            + "· ++ 不是「加一的快捷键」，它是「自增运算符」，操作数必须是一个能写回去的变量。\n"
            + "【三个必踩的坑】\n"
            + "✘ 对数字自增：1++ 或 5-- → 报 CS1059 操作数必须是变量、属性或索引器。\n"
            + "✘ 对 const 自增：const int MAX_HP = 10; MAX_HP++; → 报 CS0131 赋值号左边必须是变量、属性或索引器。\n"
            + "✘ 把 =+ 当成 +=：a =+ 3 编译能过，但它是 a = +3，会把 a 直接覆盖成 3，逻辑全错且编译器不吭声。";
        list[21].Page2En = "THIS PAGE: RUN += AND ++ THROUGH YOUR FINGERS\n"
            + "1) Type int shotCount = 0; then only shotCount += 3; and print it - confirm 3.\n"
            + "2) Replace += with the old shotCount = shotCount + 3; same result, but the name is written twice and easy to mistype.\n"
            + "3) Then *= 2, -= 1, ++, -- one at a time, printing each step: 0 -> 3 -> 6 -> 5 -> 6 -> 5.\n"
            + "4) Finally try int before = shotCount++; and int after = ++shotCount; and see the one-step difference.\n"
            + "READ EVERY SYMBOL\n"
            + "- a += b means a = a + b: read the right side with the old a.\n"
            + "- -= *= /= %= work the same; /= 0 throws DivideByZeroException.\n"
            + "- a++ is postfix: hand out the old value, then add one; ++a is prefix: add first, then hand out. On its own line they are identical.\n"
            + "- ++ is an operator, not a shortcut: its operand must be a variable you can write back to.\n"
            + "THREE TRAPS\n"
            + "X Incrementing a literal: 1++ or 5-- - CS1059: the operand of an increment must be a variable, property or indexer.\n"
            + "X Incrementing a const: MAX_HP++ - CS0131: the left-hand side of an assignment must be a variable, property or indexer.\n"
            + "X Typing =+ instead of +=: a =+ 3 compiles quietly but means a = +3, overwriting a with 3 - logic ruined, no error.";

        list[22].Page2Zh = "【这一页：亲手验证「短路」到底短在哪里】\n"
            + "① 先写 bool aliveFlag = hpCur > 0 && hpCur <= 10; 用两个不同的 hpCur 值各跑一次，看 true / false。\n"
            + "② 再写 string nameNull = null; 然后 bool hasName = nameNull != null && nameNull.Length > 0; —— 能安全跑完。\n"
            + "③ 把左右两边对调成 nameNull.Length > 0 && nameNull != null; 再跑：直接 NullReferenceException。这就是短路的价值。\n"
            + "④ 最后加一行 bool deadFlag = !aliveFlag; 把取反也用上。\n"
            + "【逐符号读懂它】\n"
            + "· && 只要左边是 false，右边整段不执行，结果直接 false。\n"
            + "· || 只要左边是 true，右边整段不执行，结果直接 true。\n"
            + "· ! 只作用于紧跟着的那一个 bool 表达式，!a && b 等于 (!a) && b。\n"
            + "· 三者都只吃 bool：C# 不像 C 那样把 0 / 1 当真假。\n"
            + "【三个必踩的坑】\n"
            + "✘ 用单个 & / | 代替 && / ||：编译能过，但没有短路，两边都会算，判空保护直接失效。\n"
            + "✘ 写成 !hpCur > 0 → 报 CS0023 运算符 \"!\" 无法应用于 int 类型的操作数。\n"
            + "✘ 判空顺序反过来（先 .Length 后判 null）→ 编译没意见，运行时 NullReferenceException。\n"
            + "✔ 全角感叹号 ！不是取反运算符，报 CS1056 意外的字符。";
        list[22].Page2En = "THIS PAGE: PROVE WHERE THE SHORT-CIRCUIT ACTUALLY SHORTS\n"
            + "1) Write bool aliveFlag = hpCur > 0 && hpCur <= 10; and run it with two different hpCur values.\n"
            + "2) Then string nameNull = null; and bool hasName = nameNull != null && nameNull.Length > 0; - it runs safely.\n"
            + "3) Swap the order to nameNull.Length > 0 && nameNull != null; and run: NullReferenceException. That is what short-circuiting buys you.\n"
            + "4) Finally add bool deadFlag = !aliveFlag; to use negation too.\n"
            + "READ EVERY SYMBOL\n"
            + "- With &&, a false left side skips the whole right side and answers false.\n"
            + "- With ||, a true left side skips the right side and answers true.\n"
            + "- ! binds to the single bool expression right after it: !a && b means (!a) && b.\n"
            + "- All three take bool only; C# never treats 0 / 1 as true and false.\n"
            + "THREE TRAPS\n"
            + "X Single & / | instead of && / ||: compiles, but no short-circuit, both sides run and the null guard dies.\n"
            + "X Writing !hpCur > 0 - CS0023: operator '!' cannot be applied to an int operand.\n"
            + "X Reversed guard order (.Length before the null test) - compiles, then NullReferenceException at runtime.\n"
            + "V A full-width ! is not the negation operator: CS1056 unexpected character.";

        list[23].Page2Zh = "【这一页：把一句话拆成四段再看回去】\n"
            + "① 从最脏的数据开始：string rawLine = \"  fire,speed,ice  \"; 先直接 PrintLine(rawLine) 看两端空格。\n"
            + "② 加 trimmed = rawLine.Trim(); 再打一次，空格没了，长度也从 18 变成 15。\n"
            + "③ 用 Split(',') 切成数组，先打 parts.Length 看是 3，再打 parts[0]。\n"
            + "④ 逐个补上 ToUpper / Contains / Substring(0,4) / IndexOf / Replace，最后 string.Join(\"|\", parts) 拼回去。\n"
            + "【逐符号读懂它】\n"
            + "· Length 是属性不带括号；Contains / Substring / IndexOf 是方法，必须带括号和参数。\n"
            + "· Substring(start, length) 取的是「从 start 起共 length 个字符」，不是「start 到 end」。\n"
            + "· IndexOf 找不到时返回 -1，不是抛异常，所以要先判 -1 再拿它当下标。\n"
            + "· s[0] 得到的是 char 不是 string，直接和 \"f\" 比会失败，要和 'f' 比。\n"
            + "· string.Join(分隔符, 数组) 是静态方法，前缀是 string. 不是变量名。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写 trimmed.Length() → 报 CS1955 不可调用的成员 \"Length\"，因为它不是方法。\n"
            + "✘ 忘了接住：只写 rawLine.Trim(); 不赋值 → 编译不报错，但空格一直都在（字符串不可变）。\n"
            + "✘ Substring 越界（trimmed.Substring(0, 100)）→ 运行时 ArgumentOutOfRangeException。\n"
            + "✔ parts[0] 之前不判 Split 结果长度 → 空字符串 split 后仍越界 IndexOutOfRangeException。";
        list[23].Page2En = "THIS PAGE: TAKE ONE SENTENCE APART AND PUT IT BACK\n"
            + "1) Start from dirty data: string rawLine = \"  fire,speed,ice  \"; print it raw and look at the spaces.\n"
            + "2) Add trimmed = rawLine.Trim(); print again - spaces gone and the length drops from 18 to 15.\n"
            + "3) Split(',') into an array; print parts.Length (3) and then parts[0].\n"
            + "4) Add ToUpper / Contains / Substring(0,4) / IndexOf / Replace one by one, then glue back with string.Join(\"|\", parts).\n"
            + "READ EVERY SYMBOL\n"
            + "- Length is a property with no parentheses; Contains / Substring / IndexOf are methods and need them.\n"
            + "- Substring(start, length) takes length characters from start, not from start to end.\n"
            + "- IndexOf returns -1 when not found instead of throwing, so check for -1 before using it as an index.\n"
            + "- s[0] is a char, not a string: compare it with 'f', never with \"f\".\n"
            + "- string.Join(separator, array) is static: the prefix is string. not a variable.\n"
            + "THREE TRAPS\n"
            + "X Writing trimmed.Length() - CS1955: Length cannot be called, it is not a method.\n"
            + "X Forgetting to keep the result: a bare rawLine.Trim(); compiles, but strings never change in place.\n"
            + "X Out-of-range Substring (trimmed.Substring(0, 100)) - ArgumentOutOfRangeException at runtime.\n"
            + "V Using parts[0] without checking the split count - IndexOutOfRangeException.";

        list[24].Page2Zh = "【这一页：用两层循环把一块内存走两遍】\n"
            + "① 敲 int[] hitsArr = new int[3]; 先直接打 hitsArr.Length，看是 3。\n"
            + "② 用 for 把 1 2 3 填进去，边填边打，确认下标 0/1/2 都对得上。\n"
            + "③ 加外层 for (int r = 0; r < 2; r++)，内层还是走数组，跑起来看打印顺序：先 r0 的三个，再 r1 的三个。\n"
            + "④ 把内层条件故意写成 c <= hitsArr.Length 跑一次，亲手撞一次 IndexOutOfRangeException 再改回来。\n"
            + "【逐符号读懂它】\n"
            + "· .Length 是数组的属性，返回 int，直接用于循环条件最稳，改数组长度不用改循环。\n"
            + "· 下标合法范围是 0 ~ Length-1，所以条件永远写 < Length，不写 <=。\n"
            + "· 外层循环的 r 管「第几遍」，内层的 c 管「第几个」，两个变量名必须不同。\n"
            + "· 内层每跑完一轮，外层才 +1；所以总打印次数 = 外层次数 × 内层次数。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写 hitsArr.Length() → 报 CS1061，数组没有 Length() 这个方法（它是属性）。\n"
            + "✘ 条件写 c <= hitsArr.Length → 编译通过，运行时越界 IndexOutOfRangeException。\n"
            + "✘ 内层也用 i（for (int i…){ for (int i…) }） → 报 CS0136 无法在此作用域中声明名为 i 的局部变量。\n"
            + "✔ new int[3] 的合法下标是 0、1、2 —— 别以为「3 个元素就能用到 3」。";
        list[24].Page2En = "THIS PAGE: WALK ONE ARRAY WITH TWO LOOPS\n"
            + "1) Type int[] hitsArr = new int[3]; print hitsArr.Length: 3.\n"
            + "2) Fill it with 1 2 3 using a for loop and print as you go - indexes 0/1/2 line up.\n"
            + "3) Add an outer for (int r = 0; r < 2; r++) with the same inner walk: first all of r0, then all of r1.\n"
            + "4) Deliberately write c <= hitsArr.Length, run it, crash on IndexOutOfRangeException, then fix it back.\n"
            + "READ EVERY SYMBOL\n"
            + "- .Length is an int property; using it as the condition means the loop follows the array size automatically.\n"
            + "- Valid indexes are 0 to Length-1, so the condition is always < Length, never <=.\n"
            + "- The outer r counts passes, the inner c counts elements: two different names, always.\n"
            + "- The outer counter advances only after the inner loop finishes the array: total prints = outer x inner.\n"
            + "THREE TRAPS\n"
            + "X hitsArr.Length() - CS1061: an array has no Length() method, only the property.\n"
            + "X Condition c <= hitsArr.Length - compiles, then IndexOutOfRangeException at runtime.\n"
            + "X Reusing i in the inner loop - CS0136: a local named i cannot be declared in this scope.\n"
            + "V new int[3] accepts 0, 1, 2 only: three elements does not mean index 3 works.";

        list[25].Page2Zh = "【这一页：写一个「必须靠 break 才停」的循环】\n"
            + "① 敲 int tick = 0; while (true) { tick++; tower.PrintLine(\"tick={0}\", tick); } 先别运行太久 —— 它真的不会自己停。\n"
            + "② 在里面加 if (tick >= 3) break; 现在它跑三轮就出来了。\n"
            + "③ 把 break 的位置换到 tick++ 之前再跑，观察打出来的数字变化（这是理解顺序的最好方式）。\n"
            + "④ 循环后面加一句 PrintLine(\"done\")，确认 break 是「跳出循环」而不是「结束整个方法」。\n"
            + "【逐符号读懂它】\n"
            + "· while (条件) 只看一个 bool：true 就一直转，false 就结束，条件可以是变量也可以是 true 常量。\n"
            + "· while (true) 是最直白的「无限循环」，写法合法，靠 break 承担出口。\n"
            + "· break; 只跳出最内层循环，后面的语句继续执行（所以要写 done 验证）。\n"
            + "· 这种结构在游戏里很常见：状态机 / 等待某条件成立 / 逐帧推进都靠它。\n"
            + "【三个必踩的坑】\n"
            + "✘ 忘写 break → 编译零错误，运行时循环不停，游戏卡死，只能按【停止 (F7)】。\n"
            + "✘ 写成 while (true); 多打一个分号 → 循环体是空语句，立刻死循环，后面的代码根本到不了。\n"
            + "✘ 把条件写成 while (1) → 报 CS0029 无法将 int 隐式转换为 bool（C# 不接受 1 当真）。\n"
            + "✔ break 的 if 条件必须是「真的会成立」的，永远不成立等于没写出口。";
        list[25].Page2En = "THIS PAGE: A LOOP THAT ONLY STOPS BECAUSE OF break\n"
            + "1) Type int tick = 0; while (true) { tick++; tower.PrintLine(\"tick={0}\", tick); } and do not let it run long - it truly never stops.\n"
            + "2) Add if (tick >= 3) break; and now it ends after three rounds.\n"
            + "3) Move the break above tick++ and run again: watch the printed numbers shift - the best way to learn the order.\n"
            + "4) Add PrintLine(\"done\") after the loop: proof that break leaves the loop, not the whole method.\n"
            + "READ EVERY SYMBOL\n"
            + "- while (condition) reads one bool: true keeps spinning, false ends it; the condition can be a variable or the literal true.\n"
            + "- while (true) is the plainest infinite loop; completely legal, with break as the only exit.\n"
            + "- break; leaves the innermost loop only; the code after it still runs (hence the done line).\n"
            + "- This shape is everywhere in games: state machines, waiting for a condition, frame-by-frame progress.\n"
            + "THREE TRAPS\n"
            + "X No break - zero compile errors, endless runtime loop, frozen game; press Stop (F7).\n"
            + "X An extra semicolon: while (true); makes the body an empty statement - instant infinite loop, later code never runs.\n"
            + "X Writing while (1) - CS0029: cannot implicitly convert int to bool (C# does not treat 1 as true).\n"
            + "V The if that guards break must eventually be true; a condition that never fires is no exit at all.";

        list[26].Page2Zh = "【这一页：让一段文字安全地变成数字】\n"
            + "① 先写 int numA = int.Parse(\"42\"); 打出来确认是 42。\n"
            + "② 把 \"42\" 故意改成 \"abc\" 再跑：亲眼看到 FormatException 打断这一帧，PrintLine 后面的都不执行。\n"
            + "③ 换成 int.TryParse(\"abc\", out parsedVal) 再跑：不崩了，返回 false，parsedVal 是 0。\n"
            + "④ 加 int cutInt = (int)3.9f; 打印看是 3 不是 4；再加 numF.ToString(\"0.00\") 看小数位被补上。\n"
            + "【逐符号读懂它】\n"
            + "· Parse 是「必须成功」的转型：文本不合法就抛异常，适合你自己确定内容没错的地方。\n"
            + "· TryParse 是「问一下」的转型：返回 bool 告诉你成不成，值从 out 参数带出来。\n"
            + "· (int) 是强制转换：只对有数值关系的类型生效，且是截断（3.9 → 3，-3.9 → -3）。\n"
            + "· ToString(\"0.00\") 里的 0.00 是格式串：0 表示「这一位必须有」，四舍五入到两位小数。\n"
            + "· 想四舍五入成整数用 Mathf.RoundToInt，不要指望 (int)。\n"
            + "【三个必踩的坑】\n"
            + "✘ int.Parse(\"abc\") → 运行时 FormatException：「该字符串的格式不正确」。\n"
            + "✘ 用强转把文字变数字：int x = (int)\"3\"; → 报 CS0030 无法将类型 string 转换为 int。\n"
            + "✘ int.TryParse 的 out 变量没先声明 → 报 CS0103 当前上下文中不存在该名称（老语法要求先声明）。\n"
            + "✔ ToString 的格式串写错（如 \"0.0.0\"）→ 运行时 FormatException，格式符不是随便写的。";
        list[26].Page2En = "THIS PAGE: TURN TEXT INTO A NUMBER SAFELY\n"
            + "1) Write int numA = int.Parse(\"42\"); and print it: 42.\n"
            + "2) Change \"42\" to \"abc\" and run: watch FormatException kill the frame - nothing after it runs.\n"
            + "3) Switch to int.TryParse(\"abc\", out parsedVal): no crash, it returns false and parsedVal is 0.\n"
            + "4) Add int cutInt = (int)3.9f; print it - 3, not 4; then numF.ToString(\"0.00\") to pad the decimals.\n"
            + "READ EVERY SYMBOL\n"
            + "- Parse must succeed: bad text throws, so use it only where you control the input.\n"
            + "- TryParse asks instead of demanding: it returns bool and hands the value out through out.\n"
            + "- (int) is a cast: only between numeric types, and it truncates (3.9 -> 3, -3.9 -> -3).\n"
            + "- In ToString(\"0.00\") the 0.00 is a format string: 0 means a required digit, rounded to two decimals.\n"
            + "- To round to an integer use Mathf.RoundToInt; never expect that from a cast.\n"
            + "THREE TRAPS\n"
            + "X int.Parse(\"abc\") - FormatException at runtime: the string was not in a correct format.\n"
            + "X Casting text to a number: int x = (int)\"3\"; - CS0030: cannot convert string to int.\n"
            + "X int.TryParse with an undeclared out variable - CS0103: the name does not exist in this context.\n"
            + "V A bad format string in ToString (like \"0.0.0\") - FormatException at runtime; format specifiers are not free text.";

        list[27].Page2Zh = "【这一页：亲手制造一次异常再把它接住】\n"
            + "① 先不写 try，直接 int parsedVal2 = int.Parse(\"abc\"); 跑一次，看清红色 FormatException 和堆栈。\n"
            + "② 把它整段塞进 try { ... } catch (Exception e) { tower.PrintLine(e.Message); } 再跑：不崩了，错误信息被打出来。\n"
            + "③ 加 finally 打一句「不管怎样都执行」，然后在 try 里故意写 return，确认 finally 照样执行。\n"
            + "④ 再写 string tagNull = null; 先 if (tagNull == null) 判一次，然后演示 tagNull.Length 直接崩（NullReferenceException）。\n"
            + "【逐符号读懂它】\n"
            + "· try 圈住「可能出事」的一段；出事就立刻跳到 catch，try 里剩下的语句不执行。\n"
            + "· catch (Exception e) 里 Exception 是类型、e 是变量名，e.Message / e.GetType().Name 是排查用的关键信息。\n"
            + "· finally 无论正常结束、异常、还是 return 都执行，收尾（关状态、打日志）放这里。\n"
            + "· null 的含义是「这个变量没有指向任何对象」，对 null 取成员就是 NullReferenceException。\n"
            + "【三个必踩的坑】\n"
            + "✘ 只写 try 不写 catch / finally → 报 CS1524 应输入 catch 或 finally。\n"
            + "✘ 先取成员再判空（tagNull.Length 在判空之前）→ 编译通过，运行时 NullReferenceException。\n"
            + "✘ catch (string s) 这种非 Exception 类型 → 报 CS0155 捕获的类型必须派生自 System.Exception。\n"
            + "✔ 空的 catch { } 吞掉一切：不崩，但你也永远不知道错在哪 —— 至少打印 e.Message。";
        list[27].Page2En = "THIS PAGE: MAKE AN EXCEPTION AND CATCH IT YOURSELF\n"
            + "1) Without try, run int parsedVal2 = int.Parse(\"abc\"); and read the red FormatException and its stack.\n"
            + "2) Wrap it in try { ... } catch (Exception e) { tower.PrintLine(e.Message); } and run: no crash, the message prints.\n"
            + "3) Add finally with an 'always runs' line, then put a return inside try and confirm finally still runs.\n"
            + "4) Then string tagNull = null; check if (tagNull == null) once, and watch tagNull.Length throw NullReferenceException.\n"
            + "READ EVERY SYMBOL\n"
            + "- try marks the risky block; on failure control jumps straight to catch and the rest of try is skipped.\n"
            + "- In catch (Exception e), Exception is the type and e the variable; e.Message / e.GetType().Name are your debugging gold.\n"
            + "- finally runs on normal exit, on exception and on return: put cleanup there.\n"
            + "- null means the variable points at no object at all; touching a member of null is NullReferenceException.\n"
            + "THREE TRAPS\n"
            + "X A try with no catch and no finally - CS1524: expected catch or finally.\n"
            + "X Member access before the null test - compiles, then NullReferenceException at runtime.\n"
            + "X Catching a non-Exception type like catch (string s) - CS0155: the type caught or thrown must derive from System.Exception.\n"
            + "V An empty catch { } hides everything: no crash, but you never learn what broke - print e.Message at least.";

        list[28].Page2Zh = "【这一页：做一本会自己长大的「名字 → 数值」账本】\n"
            + "① 敲 Dictionary<string, int> hpMap = new Dictionary<string, int>(); 先打 hpMap.Count，是 0。\n"
            + "② hpMap[\"fire\"] = 3; 再打 Count 变 1，再打 hpMap[\"fire\"] 拿到 3 —— 下标不是数字，是键。\n"
            + "③ 用 ContainsKey 判一下 \"wind\"，不存在才写入 0，体会「先判后写」怎么避免覆盖已有值。\n"
            + "④ 用 TryGetValue 安全取一次，再用 foreach 遍历 KeyValuePair，把每一对的 .Key 和 .Value 打出来。\n"
            + "【逐符号读懂它】\n"
            + "· Dictionary<键类型, 值类型> 两个泛型参数：左边是键的类型，右边是值的类型，键必须唯一。\n"
            + "· dict[键] = 值 是「有则改、无则增」，一个语句覆盖两种用途。\n"
            + "· ContainsKey 只问存在性；真正取值用 dict[键]（不存在会抛）或 TryGetValue（不存在返回 false）。\n"
            + "· 遍历时每项是 KeyValuePair<TKey, TValue>，用 kv.Key / kv.Value 拆开。\n"
            + "【三个必踩的坑】\n"
            + "✘ 直接读不存在的键 hpMap[\"nope\"] → 运行时 KeyNotFoundException，字典不会帮你补默认值。\n"
            + "✘ 用 hpMap.Length 当数量 → 报 CS1061 字典没有 Length，数量是 Count 属性（不带括号）。\n"
            + "✘ 在 foreach 里新增 / 删除元素 → 运行时 InvalidOperationException「集合已修改」。\n"
            + "✔ 键的类型必须与声明一致：声明 Dictionary<string, int> 却用 1 当键，编译就报类型不匹配。";
        list[28].Page2En = "THIS PAGE: BUILD A LEDGER THAT GROWS BY NAME\n"
            + "1) Type Dictionary<string, int> hpMap = new Dictionary<string, int>(); print hpMap.Count - it is 0.\n"
            + "2) hpMap[\"fire\"] = 3; Count becomes 1, and hpMap[\"fire\"] gives 3 back - the index is a key, not a number.\n"
            + "3) Use ContainsKey on \"wind\" and write only when missing: see how check-then-write protects existing values.\n"
            + "4) Read one value with TryGetValue, then walk it with foreach over KeyValuePair printing .Key and .Value.\n"
            + "READ EVERY SYMBOL\n"
            + "- Dictionary<KeyType, ValueType> has two generic parameters; keys are unique.\n"
            + "- dict[key] = value updates when present and adds when absent - one statement, two jobs.\n"
            + "- ContainsKey only answers existence; read with dict[key] (throws) or TryGetValue (returns false).\n"
            + "- Each item in the loop is a KeyValuePair<TKey, TValue>, split with kv.Key / kv.Value.\n"
            + "THREE TRAPS\n"
            + "X Reading a missing key: hpMap[\"nope\"] - KeyNotFoundException; the dictionary will not invent a default.\n"
            + "X Counting with hpMap.Length - CS1061: dictionaries have no Length, the count is the Count property (no parentheses).\n"
            + "X Adding or removing inside foreach - InvalidOperationException: collection was modified.\n"
            + "V The key type must match the declaration; a Dictionary<string, int> with 1 as key is a compile-time type error.";

        list[29].Page2Zh = "【这一页：把 0 / 1 / 2 换成看得懂的名字】\n"
            + "① 先在 GlobalCode 块里声明 enum TowerState { Idle = 0, Scan = 1, Fire = 2 }（清空编辑器原有代码再粘贴）。\n"
            + "② 每帧语句里写 TowerState st = TowerState.Scan; 打 PrintLine(st.ToString()) 看名字，再打 (int)st 看数字 1。\n"
            + "③ 用 switch (st) 分三支，每支打一句，看它进的是哪一支。\n"
            + "④ 把 st 改成 TowerState.Fire 再跑，观察分支跟着换；最后加 default 兜住没列出的情况。\n"
            + "【逐符号读懂它】\n"
            + "· enum 名字 { … } 里的成员是常量：可以只写名字（自动从 0 递增），也可以显式 = 值。\n"
            + "· 使用时永远写 类型.成员（TowerState.Fire），不能只写 Fire。\n"
            + "· switch 的 case 只能跟常量，每个 case 结尾必须 break（或 return），default 兜底。\n"
            + "· 枚举底层就是整数：(int)TowerState.Fire 得到 2，TowerState 变量不能直接和 2 比较，要比就先强转。\n"
            + "· 为什么非用它：写 Fire 比写 2 更难打错，编译器还会帮你检查名字是否存在。\n"
            + "【三个必踩的坑】\n"
            + "✘ 把 enum 写在 GlobalCode 外面（方法体里）→ 报语法错误，方法体里不允许声明类型，必须提升到类里。\n"
            + "✘ 某个 case 忘写 break → 报 CS0163 控件不能从一个 case 标签贯穿到另一个 case 标签。\n"
            + "✘ case 写成变量（case st:） → 报 CS0150 应输入常量值，case 后面只能是常量。\n"
            + "✔ 两个成员给同一个值不会报错，但会让你 switch 出现两个一样的 case，直接编译失败。";
        list[29].Page2En = "THIS PAGE: REPLACE 0 / 1 / 2 WITH NAMES YOU CAN READ\n"
            + "1) Declare enum TowerState { Idle = 0, Scan = 1, Fire = 2 } inside a GlobalCode block (clear the editor before pasting).\n"
            + "2) In the per-frame code write TowerState st = TowerState.Scan; print st.ToString() for the name, then (int)st for 1.\n"
            + "3) Branch with switch (st), one print per case, and see which branch it takes.\n"
            + "4) Change st to TowerState.Fire and rerun; finally add default for anything not listed.\n"
            + "READ EVERY SYMBOL\n"
            + "- Members inside enum Name { … } are constants: name only (auto from 0) or name = value.\n"
            + "- Always write Type.Member (TowerState.Fire), never a bare Fire.\n"
            + "- switch cases must be constants and each case ends with break (or return); default is the safety net.\n"
            + "- Underneath it is just an integer: (int)TowerState.Fire is 2, and an enum variable will not compare to 2 without a cast.\n"
            + "- Why bother: Fire is harder to mistype than 2, and the compiler checks that the name exists.\n"
            + "THREE TRAPS\n"
            + "X Declaring the enum inside the method body - a syntax error: type declarations are not allowed there; hoist it into the class via GlobalCode.\n"
            + "X A case without break - CS0163: control cannot fall through from one case label to another.\n"
            + "X A variable in a case (case st:) - CS0150: a constant value is expected.\n"
            + "V Two members with the same value is legal, but it produces duplicate cases and a compile error in the switch.";

        list[30].Page2Zh = "【这一页：自己造一个「有门卫」的类型】\n"
            + "① 在 GlobalCode 块里写 class Dart { public float Speed; private int hits = 0; }（清空编辑器再粘贴）。\n"
            + "② 每帧写 Dart a = new Dart(); 打 a.Speed，再试 a.hits —— 外面看不见私有字段，编译直接报错。\n"
            + "③ 加构造函数 public Dart(float speed) { Speed = speed; }，然后 new Dart(2.5f)，确认参数真的进了 Speed。\n"
            + "④ 加属性 public int Hits { get { return hits; } private set { hits = value; } } 和 public void Hit()，最后加 public static int Total 并在构造函数里 +1，用 Dart.Total 读。\n"
            + "【逐符号读懂它】\n"
            + "· 构造函数的名字必须与类名完全一致，且不能写返回类型（连 void 都不写）。\n"
            + "· 字段 public float Speed; 是敞开的口袋：外面能读能写，没有任何检查。\n"
            + "· 属性是带 get / set 的字段：get 里写读的时候做什么，set 里写写的时候做什么，用 value 表示外部传进来的值。\n"
            + "· static 成员属于「类」不属于「某个对象」：所有 Dart 共用一份 Total，访问用 Dart.Total 而不是 a.Total。\n"
            + "· new 一个对象 = 分配内存 + 跑构造函数，字段的初始值就是那一刻定下来的。\n"
            + "【三个必踩的坑】\n"
            + "✘ 构造函数写成 void Dart(float s) → 报 CS1520 方法必须有返回类型（它被当成了普通方法）。\n"
            + "✘ 构造函数写成 private → 外面 new 时报告 CS0122 不可访问（因为它具有一定的保护级别）。\n"
            + "✘ 属性只有 get 却去赋值（a.Hits = 3）→ 报 CS0200 无法分配属性，它是只读的。\n"
            + "✔ 在 static 方法里直接用实例字段 hits → 报 CS0120 对象引用对于非静态字段是必需的。";
        list[30].Page2En = "THIS PAGE: BUILD A TYPE WITH A GUARDED POCKET\n"
            + "1) In a GlobalCode block write class Dart { public float Speed; private int hits = 0; } (clear the editor first).\n"
            + "2) In the per-frame code do Dart a = new Dart(); print a.Speed, then try a.hits - private fields are invisible outside, a compile error.\n"
            + "3) Add public Dart(float speed) { Speed = speed; } then new Dart(2.5f) and confirm the argument really lands in Speed.\n"
            + "4) Add public int Hits { get { return hits; } private set { hits = value; } }, public void Hit(), and public static int Total incremented in the constructor, read as Dart.Total.\n"
            + "READ EVERY SYMBOL\n"
            + "- A constructor has exactly the class name and no return type at all (not even void).\n"
            + "- public float Speed; is an open pocket: readable and writable, no checks.\n"
            + "- A property is a field with get / set: get decides how reading works, set how writing works; value is the incoming value.\n"
            + "- static members belong to the class, not to an object: every Dart shares Total, reached as Dart.Total, not a.Total.\n"
            + "- new = allocate + run the constructor; field initial values are fixed at that moment.\n"
            + "THREE TRAPS\n"
            + "X Writing void Dart(float s) - CS1520: method must have a return type (it became an ordinary method).\n"
            + "X Making the constructor private - CS0122: it is inaccessible due to its protection level when you new it outside.\n"
            + "X A get-only property being assigned (a.Hits = 3) - CS0200: the property is read-only.\n"
            + "V Using an instance field inside a static method - CS0120: an object reference is required for the non-static field.";

        list[31].Page2Zh = "【这一页：一个名字，五套武器】\n"
            + "① 在 GlobalCode 块里先写 int Pow2(int n) { return n * n; }，每帧 powInt = Pow2(3) 打出 9。\n"
            + "② 再加 float Pow2(float n) { return n * n; }，同一个名字传 2.5f，编译器自动挑 float 版。\n"
            + "③ 加 int Add(int a, int b = 10)，分别用 Add(5) 和 Add(5, 2) 调用，看 b 是 10 还是 2。\n"
            + "④ 加带 out / ref 的 SplitHalf，注意 out 的 half 由方法赋值、ref 的 rest 进来前必须先有值；最后写递归 Fact(4) 打出 24。\n"
            + "【逐符号读懂它】\n"
            + "· 重载的判定标准是「参数列表不同」（个数 / 类型 / 顺序），与返回类型无关。\n"
            + "· 可选参数在声明处写 = 默认值，调用时省略即可；默认值必须是编译期常量。\n"
            + "· out 表示「这个参数是方法的输出」，方法里必须给它赋值；ref 表示「传进来再改回去」，调用前变量必须有值。\n"
            + "· 递归 = 自己调自己 + 一个能结束的分支，Fact 里 n <= 1 就是那个分支，靠它一层层退回来。\n"
            + "· 递归每层都占一个栈帧，层数太深会 StackOverflowException，循环能写就别用递归。\n"
            + "【三个必踩的坑】\n"
            + "✘ 只改返回类型做重载（int Foo() 和 float Foo()）→ 报 CS0111 类型已定义了一个具有相同参数类型的成员。\n"
            + "✘ out 参数在方法里忘了赋值 → 报 CS0177 在控制离开当前方法之前，必须对 out 参数赋值。\n"
            + "✘ ref 传的变量事先没初始化 → 报 CS0165 使用了未赋值的局部变量。\n"
            + "✔ 可选参数后面再跟必选参数（int F(int a = 1, int b)）→ 报 CS1737 可选参数必须出现在所有必需参数之后。";
        list[31].Page2En = "THIS PAGE: ONE NAME, FIVE WEAPONS\n"
            + "1) In a GlobalCode block start with int Pow2(int n) { return n * n; } and print Pow2(3) = 9.\n"
            + "2) Add float Pow2(float n) { return n * n; } - same name, pass 2.5f and the compiler picks the float version.\n"
            + "3) Add int Add(int a, int b = 10) and call Add(5) and Add(5, 2): watch b be 10 or 2.\n"
            + "4) Add SplitHalf with out / ref (out half is assigned inside, ref rest must be set before the call), then the recursive Fact(4) = 24.\n"
            + "READ EVERY SYMBOL\n"
            + "- Overloads are decided by a different parameter list (count / types / order), never by return type.\n"
            + "- An optional parameter carries = default in its declaration and may be omitted at the call; the default must be a compile-time constant.\n"
            + "- out means the method writes it; ref means it goes in and comes back changed, so it must be set before the call.\n"
            + "- Recursion = calling yourself plus a branch that ends: n <= 1 in Fact, which unwinds the stack.\n"
            + "- Every recursive call costs a stack frame; too deep is StackOverflowException - prefer a loop when you can.\n"
            + "THREE TRAPS\n"
            + "X Overloading by return type alone (int Foo() and float Foo()) - CS0111: the type already defines a member with the same parameter types.\n"
            + "X Forgetting to assign an out parameter - CS0177: the out parameter must be assigned before control leaves the method.\n"
            + "X Passing an uninitialized variable as ref - CS0165: use of an unassigned local variable.\n"
            + "V An optional parameter followed by a required one (int F(int a = 1, int b)) - CS1737: optional parameters must appear after all required parameters.";

        list[32].Page2Zh = "【这一页：亲手摸两种「二维」的区别】\n"
            + "① 敲 int[,] grid2d = new int[2, 3]; 打 grid2d.Length 得到 6（总格子数），再打 GetLength(0)=2、GetLength(1)=3。\n"
            + "② 用 grid2d[0, 0] = 1; 赋值，注意两个下标在同一个方括号里、用逗号隔开。\n"
            + "③ 敲 int[][] jagArr = new int[2][]; 只打 jagArr[0] 看是不是 null —— 壳子开了但每行还没开。\n"
            + "④ 逐行 new int[3] / new int[2]，再用两层 for 填值，内层条件用 jagArr[r2].Length（每行长度不同）。\n"
            + "【逐符号读懂它】\n"
            + "· int[,] 是矩形数组：所有行等长，一个方括号写两个下标 grid[row, col]。\n"
            + "· int[][] 是数组的数组：外层装的是「每行」，所以 jag[row][col] 是两个方括号。\n"
            + "· new int[2, 3] 一次开满 6 个格子；new int[2][] 只开两行空壳，每行要单独 new。\n"
            + "· 行数用 GetLength(0)，列数用 GetLength(1)；一维数组才用 Length 表示个数。\n"
            + "· 交错数组的好处是每行长度能不一样（做不规则的关卡地图特别合适）。\n"
            + "【三个必踩的坑】\n"
            + "✘ 对矩形数组用 jag 写法：grid2d[0][1] → 报 CS0022 方括号中的索引数目不对，应输入 2。\n"
            + "✘ 对交错数组用逗号写法：jagArr[0, 0] → 报 CS0022 方括号中的索引数目不对，应输入 1。\n"
            + "✘ 只 new int[2][] 却不给每行 new → 编译通过，运行时 jagArr[0][0] 抛 NullReferenceException。\n"
            + "✔ 以为 grid2d.Length 是行数（2）→ 其实是总格子数 6，行数得用 GetLength(0)。";
        list[32].Page2En = "THIS PAGE: FEEL THE DIFFERENCE BETWEEN THE TWO KINDS OF 2D\n"
            + "1) Type int[,] grid2d = new int[2, 3]; print grid2d.Length = 6 (total cells), then GetLength(0)=2 and GetLength(1)=3.\n"
            + "2) Assign with grid2d[0, 0] = 1; - two indexes in one bracket, comma separated.\n"
            + "3) Type int[][] jagArr = new int[2][]; print jagArr[0] and see null: the row slots exist, the rows do not.\n"
            + "4) new int[3] and new int[2] per row, fill with two loops, inner condition jagArr[r2].Length (rows differ).\n"
            + "READ EVERY SYMBOL\n"
            + "- int[,] is a rectangle: every row the same length, two indexes in one bracket grid[row, col].\n"
            + "- int[][] is an array of arrays: the outer holds rows, so jag[row][col] uses two brackets.\n"
            + "- new int[2, 3] allocates all 6 cells at once; new int[2][] only allocates two empty slots, each row needs its own new.\n"
            + "- Rows come from GetLength(0), columns from GetLength(1); only a 1D array uses Length for its count.\n"
            + "- The jagged form lets rows differ in length - perfect for irregular level maps.\n"
            + "THREE TRAPS\n"
            + "X Jagged syntax on a rectangle: grid2d[0][1] - CS0022: wrong number of indexes inside []; expected 2.\n"
            + "X Comma syntax on a jagged array: jagArr[0, 0] - CS0022: wrong number of indexes inside []; expected 1.\n"
            + "X new int[2][] without new'ing the rows - compiles, then jagArr[0][0] throws NullReferenceException.\n"
            + "V Expecting grid2d.Length to be the row count (2) - it is the total cell count 6; rows are GetLength(0).";
        list[33].Page2Zh = "【这一页：先让类能下标取值，再故意漏掉 set】\n"
            + "① 类必须写在 GlobalCode { } 块里 —— 方法体里不允许声明 class。\n"
            + "② 先在类里只写 get { return items[i]; }，下面用 tower.PrintLine(\"{0}\", bag[0]) 读，编译运行。\n"
            + "③ 再补 set { items[i] = value; }，写 bag[0] = 7; 赋一次值，再读出来看是否变成 7。\n"
            + "④ 故意把 set 整段删掉再编译：bag[0] = 7 会报 CS0200（属性或索引器不能赋值，它是只读的）。\n"
            + "【逐符号读懂它】\n"
            + "· this[...] ：索引器，名字固定是 this，方括号里写参数（下标类型可以不是 int，也可以是 string）。\n"
            + "· get { } ：读的时候跑这一段，return 出去的值就是 bag[i] 的值。\n"
            + "· set { } ：写的时候跑这一段，右边的值被放进关键字 value。\n"
            + "· private int[] items ：真正存数据的是这个数组，索引器只是它的「门口」。\n"
            + "【三个必踩的坑】\n"
            + "✘ 索引器带名字写成 public int Get(int i) → 那不是索引器，是普通方法，bag[0] 报 CS0021。\n"
            + "✘ 忘了写下标越界检查 → bag[5] 直接抛 IndexOutOfRangeException，游戏画面卡住不动。\n"
            + "✘ 把类写在 GlobalCode 外面（直接写在 Run 里）→ 报 CS1519 / CS1022，方法体里不能放类型声明。\n"
            + "✔ 索引器可以是只读的（只写 get），也可以只写 set；要读写就两个都写。";
        list[33].Page2En = "THIS PAGE: GIVE A CLASS AN INDEX, THEN BREAK IT\n"
            + "1) The class must sit inside GlobalCode { } - a method body may not declare a class.\n"
            + "2) Write only get { return items[i]; } first and read it with tower.PrintLine(\"{0}\", bag[0]); compile and run.\n"
            + "3) Add set { items[i] = value; }, assign bag[0] = 7; then read it back and see 7.\n"
            + "4) Delete the set block on purpose: bag[0] = 7 now fails with CS0200 (property or indexer cannot be assigned - it is read-only).\n"
            + "READ EVERY SYMBOL\n"
            + "- this[...] : the indexer; its name is always this and the bracket takes parameters (the key need not be int, string works too).\n"
            + "- get { } : runs when you read; what it returns is the value of bag[i].\n"
            + "- set { } : runs when you write; the right-hand value lands in the keyword value.\n"
            + "- private int[] items : the real storage; the indexer is only the doorway to it.\n"
            + "THREE TRAPS\n"
            + "X Naming it public int Get(int i) - that is a normal method, so bag[0] reports CS0021.\n"
            + "X No bounds check - bag[5] throws IndexOutOfRangeException and the tower freezes.\n"
            + "X Declaring the class outside GlobalCode (directly in Run) - CS1519 / CS1022, no type declarations in a method body.\n"
            + "V An indexer may be get-only or set-only; write both when you need read and write.";
        list[34].Page2Zh = "【这一页：先看「接口只是合同」，再故意漏实现一个成员】\n"
            + "① 在 GlobalCode 里先只写 interface IHittable { int Hp { get; } void Hit(int dmg); }，此时还没有任何类。\n"
            + "② 写 class Dummy : IHittable，只实现 Hp，先不写 Hit，编译 —— 报 CS0535（没有实现接口成员）。\n"
            + "③ 补上 public void Hit(int dmg) { Hp -= dmg; }，编译通过。\n"
            + "④ 用 IHittable target = new Dummy(10); 声明，再 target.Hit(3); 打印 target.Hp，看到 7。\n"
            + "【逐符号读懂它】\n"
            + "· interface ：只列成员签名，一条实现都不写，所以它不能被 new。\n"
            + "· class Dummy : IHittable ：冒号后面是「要实现 / 继承的名单」，多个用逗号隔开，类必须是第一个。\n"
            + "· IHittable target = new Dummy(10) ：左边是接口类型、右边是实现类，这就是「父类型装子类」。\n"
            + "· { get; private set; } ：自动属性，编译器替你生成存值用的隐藏字段。\n"
            + "【三个必踩的坑】\n"
            + "✘ 实现成员忘了写 public → CS0535，接口成员必须 public，不能是 private。\n"
            + "✘ 在接口里写字段 int hp; → CS0525，接口只能有方法 / 属性 / 事件 / 索引器的签名。\n"
            + "✘ 用接口类型去访问类里独有的成员 → CS1061，接口看得见的只有它自己列出来的东西。\n"
            + "✔ 接口变量里装的其实是 Dummy 对象，所以 target.Hit 跑的是 Dummy 的版本。";
        list[34].Page2En = "THIS PAGE: AN INTERFACE IS ONLY A CONTRACT - THEN BREAK IT\n"
            + "1) Declare only interface IHittable { int Hp { get; } void Hit(int dmg); } inside GlobalCode; no class yet.\n"
            + "2) Write class Dummy : IHittable implementing only Hp; compile - CS0535 (does not implement interface member).\n"
            + "3) Add public void Hit(int dmg) { Hp -= dmg; } and it compiles.\n"
            + "4) Declare IHittable target = new Dummy(10); call target.Hit(3); print target.Hp and see 7.\n"
            + "READ EVERY SYMBOL\n"
            + "- interface : member signatures only, no bodies, so it can never be new'd.\n"
            + "- class Dummy : IHittable : after the colon comes the list of things implemented or inherited, comma separated; the class goes first.\n"
            + "- IHittable target = new Dummy(10) : interface type on the left, implementing class on the right.\n"
            + "- { get; private set; } : an auto property; the compiler generates the hidden backing field.\n"
            + "THREE TRAPS\n"
            + "X Forgetting public on the implementing member - CS0535; interface members must be public.\n"
            + "X Declaring a field int hp; inside the interface - CS0525; only method, property, event and indexer signatures are allowed.\n"
            + "X Reaching a class-only member through the interface variable - CS1061; the interface only exposes what it lists.\n"
            + "V The interface variable really holds a Dummy object, so target.Hit runs Dummy's version.";
        list[35].Page2Zh = "【这一页：把方法装进变量，再换一个方法试试】\n"
            + "① 先写一个普通方法（写在 GlobalCode 的类里最稳），确认它能单独调用。\n"
            + "② 声明 Func<int, int, int> add = calc.Add; 注意最后那个 int 才是返回值类型。\n"
            + "③ 用 tower.PrintLine(\"{0}\", add(2, 3)) 调用，输出 5；再改成 add = (a, b) => a * b; 输出 6。\n"
            + "④ Action<string> show = msg => tower.PrintLine(msg); 写 show(\"ok\")，验证没返回值也能调用。\n"
            + "【逐符号读懂它】\n"
            + "· Action<T> ：装「无返回值、一个 T 参数」的方法；Action 无参数，Action<T1,T2> 两个参数。\n"
            + "· Func<T, TResult> ：最后一个类型永远是返回值，前面的是参数。\n"
            + "· = calc.Add ：方法名后面不要加括号，加括号就变成「先调用再把结果赋值」。\n"
            + "· delegate 关键字 ：自己定义委托类型时才用（delegate int Op(int a, int b);），平时直接用 Action / Func 就够。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写成 Func<int, int, int> add = calc.Add(); → CS0029，赋进去的是 int 结果不是方法。\n"
            + "✘ 以为 Func<int,int> 是两个参数 → 它只有 1 个参数、返回 int，两个参数要写 Func<int,int,int>。\n"
            + "✘ 委托变量还没赋值就调用 → NullReferenceException（委托是引用类型，默认是 null）。\n"
            + "✔ 委托的值可以是 null，调用前写 if (add != null) add(2, 3); 或 add?.Invoke(2, 3)。";
        list[35].Page2En = "THIS PAGE: PUT A METHOD IN A VARIABLE, THEN SWAP IT\n"
            + "1) Write an ordinary method first (inside a class in GlobalCode is safest) and call it directly.\n"
            + "2) Declare Func<int, int, int> add = calc.Add; - the last int is the return type.\n"
            + "3) Call tower.PrintLine(\"{0}\", add(2, 3)) -> 5; then reassign add = (a, b) => a * b; -> 6.\n"
            + "4) Action<string> show = msg => tower.PrintLine(msg); call show(\"ok\") to prove void delegates work.\n"
            + "READ EVERY SYMBOL\n"
            + "- Action<T> : holds a void method taking one T; Action takes none, Action<T1,T2> takes two.\n"
            + "- Func<T, TResult> : the last type argument is always the return type, the rest are parameters.\n"
            + "- = calc.Add : no parentheses after the method name; with parentheses you call it and assign the result.\n"
            + "- the delegate keyword : only for your own delegate types (delegate int Op(int a, int b);); Action and Func cover most needs.\n"
            + "THREE TRAPS\n"
            + "X Func<int, int, int> add = calc.Add(); - CS0029, you assigned an int result, not a method.\n"
            + "X Thinking Func<int,int> takes two parameters - it takes one and returns int.\n"
            + "X Calling a delegate variable that was never assigned - NullReferenceException.\n"
            + "V A delegate can be null; guard with if (add != null) add(2, 3); or add?.Invoke(2, 3).";
        list[36].Page2Zh = "【这一页：把一段算式写成箭头，再看它被谁调用】\n"
            + "① 先写 Func<int, int> sq = x => x * x; 然后用 tower.PrintLine(\"{0}\", sq(4)) 确认输出 16。\n"
            + "② 换成多行写法：sq = v => { int r = v * v; return r; }; 注意必须写 return。\n"
            + "③ 写 Action<string> log = m => tower.PrintLine(m); 调 log(\"hi\")，验证无返回值。\n"
            + "④ 无参数时写 () => tower.PrintLine(\"tick\")，注意空括号不能省。\n"
            + "【逐符号读懂它】\n"
            + "· => ：读作「goes to」，左边参数、右边结果，整体是一段没有名字的方法。\n"
            + "· x => x * x ：单表达式形态，等价于 int M(int x) { return x * x; }。\n"
            + "· { } 形态 ：里面当普通方法体写，最后必须有 return（除非返回 void）。\n"
            + "· 闭包 ：Lambda 里可以直接用外面的局部变量（比如 tower），这就是它能直接打印的原因。\n"
            + "【三个必踩的坑】\n"
            + "✘ 多行形态漏写 return → CS0161，并非所有代码路径都返回值。\n"
            + "✘ 三个以上参数漏括号：a, b, c => → CS1525，两个及以上参数必须写 ( )。\n"
            + "✘ 把 Lambda 当成「立即执行」→ 它只是被存起来，不调用就不跑。\n"
            + "✔ Lambda 就是一个匿名方法，能赋给 Action / Func，也能当参数传给别的方法。";
        list[36].Page2En = "THIS PAGE: TURN AN EXPRESSION INTO AN ARROW, THEN CALL IT\n"
            + "1) Start with Func<int, int> sq = x => x * x; and confirm tower.PrintLine(\"{0}\", sq(4)) prints 16.\n"
            + "2) Rewrite it multi-line: sq = v => { int r = v * v; return r; }; the return is mandatory.\n"
            + "3) Action<string> log = m => tower.PrintLine(m); call log(\"hi\") to prove the void form.\n"
            + "4) With no parameters write () => tower.PrintLine(\"tick\"); the empty parentheses are required.\n"
            + "READ EVERY SYMBOL\n"
            + "- => : read as goes to; parameters on the left, result on the right - the whole thing is an unnamed method.\n"
            + "- x => x * x : expression form, equal to int M(int x) { return x * x; }.\n"
            + "- the { } form : an ordinary method body, so it must end with return unless it returns void.\n"
            + "- closure : a lambda may use outer locals such as tower, which is why it can print directly.\n"
            + "THREE TRAPS\n"
            + "X Missing return in the { } form - CS0161, not all code paths return a value.\n"
            + "X Dropping the parentheses for three parameters: a, b, c => - CS1525; two or more need ( ).\n"
            + "X Assuming the lambda runs immediately - it is only stored; nothing happens until you call it.\n"
            + "V A lambda is just an anonymous method: assignable to Action or Func, and passable as an argument.";
        list[37].Page2Zh = "【这一页：把 for 循环换成一行 LINQ】\n"
            + "① 先用最笨的写法：List<int> hps = new List<int>(); 逐个 Add，再用 for 循环挑出大于 0 的数打印，确认数据对不对。\n"
            + "② 换成一行：List<int> alive = hps.Where(v => v > 0).ToList(); 再打印 alive.Count，两次结果应该一样。\n"
            + "③ 试 Sum / Max / Count() / Any：hps.Sum() 求和、hps.Max() 最大值、hps.Count() 个数、hps.Any(v => v >= 10) 有没有大的。\n"
            + "④ 试 Select 变形：hps.Select(v => v * 2).ToList()，每个元素乘 2 得到新 List，原 List 不变。\n"
            + "【逐符号读懂它】\n"
            + "· .Where(v => 条件) ：只留条件为 true 的元素，v 是每个元素的临时名字。\n"
            + "· .ToList() ：把筛选结果收成真正的 List，可以再用 Count / Add；不写它就只能继续往后串。\n"
            + "· .Any(条件) ：只要有一个满足就 true，比 Count() > 0 更快。\n"
            + "· .Sum() / .Max() ：直接算总和 / 最大值，元素为空时 Max 会抛 InvalidOperationException。\n"
            + "【三个必踩的坑】\n"
            + "✘ 忘了模板的 using System.Linq 就自己写 using → GlobalCode 里写 using 会报 CS1529，using 只能写在文件最上面（模板已内置）。\n"
            + "✘ 对 Where 的结果直接用 [0] → 它是序列不是数组，要 .ToList() 或 .First() / .FirstOrDefault()。\n"
            + "✘ 以为 LINQ 会改原 List → 它返回新序列，原 List 一个元素都不会变。\n"
            + "✔ 空集合调用 Max / First 会抛异常，先 Any() 判断，或用 FirstOrDefault / DefaultIfEmpty。";
        list[37].Page2En = "THIS PAGE: REPLACE A FOR LOOP WITH ONE LINQ LINE\n"
            + "1) Do it the dumb way first: List<int> hps = new List<int>(); Add each value, then a for loop printing those above 0.\n"
            + "2) Replace with one line: List<int> alive = hps.Where(v => v > 0).ToList(); print alive.Count - same numbers.\n"
            + "3) Try Sum / Max / Count() / Any: hps.Sum(), hps.Max(), hps.Count(), hps.Any(v => v >= 10).\n"
            + "4) Try Select: hps.Select(v => v * 2).ToList() gives a new List of doubled values; the original stays untouched.\n"
            + "READ EVERY SYMBOL\n"
            + "- .Where(v => condition) : keeps elements whose condition is true; v is a temporary name for each element.\n"
            + "- .ToList() : materialises the result into a real List so Count / Add work; without it you can only keep chaining.\n"
            + "- .Any(condition) : true when at least one element passes - faster than Count() > 0.\n"
            + "- .Sum() / .Max() : add up / pick the largest; Max on an empty sequence throws InvalidOperationException.\n"
            + "THREE TRAPS\n"
            + "X Writing your own using System.Linq inside GlobalCode - CS1529; usings belong at the top of the file (the template already emits it).\n"
            + "X Indexing a Where result with [0] - it is a sequence, not an array; use .ToList() or .First() / .FirstOrDefault().\n"
            + "X Expecting LINQ to modify the original List - it returns a new sequence and leaves the source alone.\n"
            + "V Max / First on an empty collection throws; guard with Any(), or use FirstOrDefault / DefaultIfEmpty.";
        list[38].Page2Zh = "【这一页：先写一个只会装 string 的盒子，再把它变成什么都能装】\n"
            + "① 先在 GlobalCode 里写 class BoxString { private string value; ... }，Put / Get 都写死 string，跑通。\n"
            + "② 把类改成 class Box<T>，把两处 string 全换成 T，下面 new Box<string>() 使用 —— 行为完全一样。\n"
            + "③ 再试 new Box<int>()：同一个类，这次装 int，不需要再写第二个类。\n"
            + "④ 加约束 where T : class 后再写 new Box<int>()，编译报 CS0452（类型必须是引用类型才能用作参数 T）。\n"
            + "【逐符号读懂它】\n"
            + "· <T> ：类型参数，T 只是习惯叫法，TKey / TValue / TItem 都行。\n"
            + "· Box<string> ：使用时把 T 换成 string，这叫「构造泛型类型」。\n"
            + "· where T : class ：约束写在类名后、大括号前；class 指引用类型，struct 指值类型，new() 指必须有公开无参构造。\n"
            + "· T value ：字段类型可以是 T，方法参数、返回值也可以是 T。\n"
            + "【三个必踩的坑】\n"
            + "✘ 在静态字段上用 T → CS0695 之类的错误，静态成员不能依赖实例类型参数（本项目避开静态泛型成员）。\n"
            + "✘ 约束写 where T : class 后又去 new T() → CS0304，class 约束不保证有无参构造，要 new() 才行。\n"
            + "✘ 把 where 写在方法体里或大括号后 → CS1031，约束必须在类型名与方法体之间。\n"
            + "✔ List<T> / Dictionary<K,V> 本身就是泛型类，你写的 Box<T> 和它们是一个套路。";
        list[38].Page2En = "THIS PAGE: WRITE A STRING-ONLY BOX, THEN MAKE IT GENERIC\n"
            + "1) In GlobalCode write class BoxString { private string value; ... } with Put / Get hard-coded to string; run it.\n"
            + "2) Change it to class Box<T>, replace both string occurrences with T, and use new Box<string>() - behaviour is identical.\n"
            + "3) Now try new Box<int>(): same class, holds an int, no second class needed.\n"
            + "4) Add where T : class and compile new Box<int>() again - CS0452 (the type must be a reference type to be used as T).\n"
            + "READ EVERY SYMBOL\n"
            + "- <T> : a type parameter; T is only a convention, TKey / TValue / TItem are fine.\n"
            + "- Box<string> : substituting string for T - a constructed generic type.\n"
            + "- where T : class : the constraint sits after the class name and before the brace; class means reference type, struct value type, new() a public parameterless constructor.\n"
            + "- T value : fields, parameters and return types can all be T.\n"
            + "THREE TRAPS\n"
            + "X Using T in a static field - the static member cannot depend on an instance type parameter; this project simply avoids static generic members.\n"
            + "X where T : class then new T() - CS0304; class does not imply a parameterless constructor, you need new().\n"
            + "X Putting where inside the body or after the brace - CS1031; constraints go between the name and the body.\n"
            + "V List<T> and Dictionary<K,V> are generic classes themselves - your Box<T> follows the very same pattern.";
        list[39].Page2Zh = "【这一页：同一个东西，两种名字都写一遍】\n"
            + "① 先用简写：List<int> hp = new List<int>(); hp.Add(3); 打印 hp.Count。\n"
            + "② 换成全限定名：System.Collections.Generic.List<int> hp2 = new System.Collections.Generic.List<int>(); 效果完全一样。\n"
            + "③ 试静态成员：System.Math.Max(2, 9) 和 Math.Max(2, 9) 结果一样，前者不依赖任何 using。\n"
            + "④ 试 DateTime：System.DateTime.Now.Year 拿到当前年份，再打印 .Minute。\n"
            + "【逐符号读懂它】\n"
            + "· 命名空间 ：类型的「房间号」，用点分层级，越靠右越具体。\n"
            + "· using ：写在文件最上面，把某个房间「敞开门」，之后可以只写短名。\n"
            + "· 全限定名 ：System.Math 这种从根写起的名字，永远不会和别人的同名类型搞混。\n"
            + "· 模板内置 ：编辑器已替你写好 using UnityEngine / System / System.Collections / System.Collections.Generic / System.Linq，所以 List 短名能直接用。\n"
            + "【三个必踩的坑】\n"
            + "✘ 在 GlobalCode 里写 using System.IO; → CS1529，GlobalCode 内容会被提到类体里，类体里不能写 using。\n"
            + "✘ 只写 List 但把 using System.Collections.Generic 删了 → CS0246 找不到类型 List。\n"
            + "✘ 把 System 当成变量名 → CS0119，System 是命名空间不是对象。\n"
            + "✔ 记不住在哪个房间时，写全名最稳：System.Math、System.DateTime、System.Collections.Generic.List。";
        list[39].Page2En = "THIS PAGE: WRITE THE SAME THING TWICE, SHORT NAME AND FULL NAME\n"
            + "1) Short form: List<int> hp = new List<int>(); hp.Add(3); print hp.Count.\n"
            + "2) Fully qualified: System.Collections.Generic.List<int> hp2 = new System.Collections.Generic.List<int>(); identical behaviour.\n"
            + "3) Static members: System.Math.Max(2, 9) and Math.Max(2, 9) agree; the first needs no using at all.\n"
            + "4) DateTime: System.DateTime.Now.Year for the current year, then print .Minute.\n"
            + "READ EVERY SYMBOL\n"
            + "- namespace : the room number of a type, dot-separated, the specific part on the right.\n"
            + "- using : written at the top of the file to open a room so short names work.\n"
            + "- fully qualified name : System.Math written from the root - it can never collide with a same-named type.\n"
            + "- template built-ins : the editor already emits using UnityEngine / System / System.Collections / System.Collections.Generic / System.Linq, so List works short.\n"
            + "THREE TRAPS\n"
            + "X Writing using System.IO; inside GlobalCode - CS1529; the block is hoisted into the class body and usings are not allowed there.\n"
            + "X Using List after deleting using System.Collections.Generic - CS0246, type List not found.\n"
            + "X Treating System as a variable - CS0119, System is a namespace, not an object.\n"
            + "V When unsure of the room, write the full name: System.Math, System.DateTime, System.Collections.Generic.List.";
        list[40].Page2Zh = "【这一页：先折叠，再让一半代码不参与编译】\n"
            + "① 写 #region 试试 / #endregion 把两行包起来，编译运行 —— 结果完全没变，它只影响阅读。\n"
            + "② 写 #if true / #else / #endif，两句 PrintLine 只会执行 true 那一支。\n"
            + "③ 把 true 改成 false 再编译：另一支生效，true 那支被整段丢掉（连语法错误都不会报）。\n"
            + "④ 试 Unity 符号 #if UNITY_EDITOR：编辑器里跑能进这一支，打包后不进。\n"
            + "【逐符号读懂它】\n"
            + "· 行首 # ：预处理行必须顶格或前面只有空格，前面不能有代码。\n"
            + "· #region / #endregion ：成对出现，纯粹为了折叠，不影响编译结果。\n"
            + "· #if 符号 ：判断的是「编译符号」有没有定义，不是变量真假。\n"
            + "· #else / #elif / #endif ：和 if 一样要有收尾，少了 #endif 报 CS1027。\n"
            + "【三个必踩的坑】\n"
            + "✘ #region 后面忘写名字 → 报错要求 #region 后面跟标识符。\n"
            + "✘ 把 #if 写成 #IF → 预处理指令区分大小写，报 CS1024 预处理指令无效。\n"
            + "✘ 在被 #if false 丢掉的代码里写错语法却以为能编译通过 → 是的，它根本不编译，不要靠它「藏错误」。\n"
            + "✔ 想临时排除一整段代码，用 #if false / #endif 比一行行加 // 更快，也更不容易漏。";
        list[40].Page2En = "THIS PAGE: FOLD FIRST, THEN LET HALF THE CODE SKIP COMPILATION\n"
            + "1) Wrap two lines in #region try / #endregion and run - nothing changes; it only affects reading.\n"
            + "2) Write #if true / #else / #endif with two PrintLine calls; only the true branch runs.\n"
            + "3) Flip true to false and recompile: the other branch wins and the true branch is dropped entirely (even syntax errors vanish).\n"
            + "4) Try the Unity symbol #if UNITY_EDITOR: true in the editor, false in a build.\n"
            + "READ EVERY SYMBOL\n"
            + "- leading # : a preprocessor line starts at the line start (spaces allowed), never after code.\n"
            + "- #region / #endregion : a matched pair, purely for folding, no effect on the output.\n"
            + "- #if symbol : tests whether a compile symbol is defined - not whether a variable is true.\n"
            + "- #else / #elif / #endif : still needs closing; a missing #endif reports CS1027.\n"
            + "THREE TRAPS\n"
            + "X #region without a name - the compiler wants an identifier after it.\n"
            + "X Typing #IF - directives are case sensitive; CS1024 invalid preprocessor directive.\n"
            + "X Relying on #if false to hide broken code - it truly is not compiled, so it hides mistakes instead of fixing them.\n"
            + "V To exclude a whole block quickly, #if false / #endif beats commenting every line.";
        list[41].Page2Zh = "【这一页：同一份数据复制两次，看谁被改】\n"
            + "① 先在 GlobalCode 里写 struct Pt { public int X; }，下面 new Pt() 并打印 p1.X，确认能用。\n"
            + "② 写 Pt p2 = p1; 再改 p2.X = 5，打印两个：p1.X 还是 1，p2.X 是 5 —— 值类型各存一份。\n"
            + "③ 换成 class Node，同样写 n2 = n1; n2.X = 5;，打印两个都是 5 —— 引用类型共用一个对象。\n"
            + "④ 再补一个差别：struct 不能继承，也不能写无参构造（C# 10 之前），class 可以。\n"
            + "【逐符号读懂它】\n"
            + "· struct Pt ：值类型，变量本身就装着数据，赋值时整份复制。\n"
            + "· class Node ：引用类型，变量里装的是「地址」，赋值只是复制地址。\n"
            + "· public int X; ：struct 里可以写字段，但习惯上写成属性更安全。\n"
            + "· new Pt() ：即便 struct 也有 new 写法，它只是在当前变量里把字段清零，不产生堆对象。\n"
            + "【三个必踩的坑】\n"
            + "✘ 在 struct 里写无参构造（老语法）→ CS0568，结构体不能包含显式无参构造。\n"
            + "✘ 让 struct 继承别的类型 → CS0527，结构体只能实现接口。\n"
            + "✘ 把大对象塞进 struct → 每次赋值都在复制整份数据，性能反而更差，超过 16 字节就该用 class。\n"
            + "✔ 判断口诀：装一串小数字用 struct（Vector2 就是），装会变的对象用 class。";
        list[41].Page2En = "THIS PAGE: COPY THE SAME DATA TWICE AND SEE WHAT CHANGES\n"
            + "1) In GlobalCode write struct Pt { public int X; }, then new Pt() and print p1.X.\n"
            + "2) Write Pt p2 = p1; set p2.X = 5 and print both: p1.X stays 1, p2.X is 5 - value types keep separate copies.\n"
            + "3) Switch to class Node, do the same n2 = n1; n2.X = 5; and both print 5 - reference types share one object.\n"
            + "4) One more difference: a struct cannot inherit and (before C# 10) cannot declare a parameterless constructor; a class can do both.\n"
            + "READ EVERY SYMBOL\n"
            + "- struct Pt : a value type - the variable holds the data, so assignment copies all of it.\n"
            + "- class Node : a reference type - the variable holds an address, so assignment copies the address.\n"
            + "- public int X; : fields are allowed in a struct, but a property is usually safer.\n"
            + "- new Pt() : even structs accept new; it just zeroes the fields in place, no heap object is created.\n"
            + "THREE TRAPS\n"
            + "X Declaring a parameterless constructor in a struct (older syntax) - CS0568.\n"
            + "X Trying to inherit a base type - CS0527; a struct may only implement interfaces.\n"
            + "X Stuffing a big object into a struct - every assignment copies everything; past ~16 bytes prefer a class.\n"
            + "V Rule of thumb: a few small numbers -> struct (Vector2 is one); living, changing objects -> class.";


    }

    // ================== 第 3 页：语法课代码动画数据 ==================

    /// <summary>建一个动画步骤（Key = 该行特征子串，用于在示例代码里定位要亮起来的行）。</summary>
    private static AnimStep AS(string key, string zh, string en)
    {
        AnimStep s = new AnimStep();
        s.Key = key;
        s.Zh = zh;
        s.En = en;
        return s;
    }

    // ================== 第 3 页（语法课）：内存可视化场景数据 ==================

    /// <summary>
    /// 造一段内存：type = 类型名（画成名牌）、name = 变量名、bytes = 每个字节的十六进制（空格分隔，"??" = 还没写）。
    /// note = 这一行下面的小字（讲清这段内存为什么长这样）；badge = 行首徽章（锁 / 引用 / 栈帧 / 丢弃）；
    /// ghost = true 表示「代码里有、内存里没有」（注释、方法定义），画成灰色虚线块，也不占地址。
    /// </summary>
    private static MemVar MV(string type, string name, string bytes, string note = null,
        string badge = null, bool ghost = false)
    {
        MemVar v = new MemVar();
        v.Type = type;
        v.Name = name;
        v.Bytes = bytes;
        v.Note = note;
        v.Badge = badge;
        v.Ghost = ghost;
        return v;
    }

    /// <summary>造一步：这一拍在跑哪行代码、内存里已有几个变量、正在写第几个（-1 = 只读）、配什么旁白。</summary>
    private static MemStep MS(string expr, int reveal, int write, string zh, string en)
    {
        MemStep s = new MemStep();
        s.Expr = expr;
        s.Reveal = reveal;
        s.Write = write;
        s.Zh = zh;
        s.En = en;
        return s;
    }

    /// <summary>造一步，并顺带改写某几段内存的字节 —— 用来演示「同一格的值被改了」（for 里的 i、Add 之后的 Count）。</summary>
    private static MemStep MS(string expr, int reveal, int write, string zh, string en,
        int[] patchVar, string[] patchBytes)
    {
        MemStep s = MS(expr, reveal, write, zh, en);
        s.PatchVar = patchVar;
        s.PatchBytes = patchBytes;
        return s;
    }

    private static MemScene MSC(MemVar[] vars, MemStep[] steps)
    {
        MemScene s = new MemScene();
        s.Vars = vars;
        s.Steps = steps;
        return s;
    }

    /// <summary>
    /// 给语法课前 19 项任务配「内存可视化」场景（第 20~49 项讲的是流程控制 / 表达式 / 字符串 / 类型转换 / 容器与自定义类型 / 面向对象与语言特性，只配 2 页说明）。每一章按它自己讲的东西来设计，不是套模板：
    /// 注释 → 演示「代码里有、内存里没有」的虚行；变量与类型 → 同一台内存上 int/char/bool/float 的格数差；
    /// const → 只读的那块；四则 / 关系 / 逻辑 → 结果落在哪几格、占几个字节；if / switch → 判断读哪里、哪个分支真的改了内存；
    /// for（先讲计数循环）→ 数组 / List / foreach（引用、下标、逐个读）→ while / do-while（同一块内存被反复改写，Patch）；
    /// 方法 / 返回值 → 栈帧的生死；字符串 → 引用 + 堆里的 UTF-16 字符格。
    /// 字节值都是真值：int 3 = 03 00 00 00（小端），float 1.5f = 00 00 C0 3F，bool true = 01，char 'A' = 41。
    /// </summary>
    private static void AppendGrammarMemPages(TaskDef[] list)
    {
        // 这一课只讲注释本身的功效：代码里有它、内存里没有它。所以故意不放任何变量行，
        // 整页就一排虚线格 —— 玩家一眼就能看出「注释不占内存」这件事。
        list[0].Mem = MSC(
            new[]
            {
                MV("// 注释", "（整行）", "-- -- --",
                    "注释只写给人看：编译时整行被扔掉，内存里连格子都不会划出来。", "丢弃", true).En("Comments are for humans only: the compiler throws the whole line away, so no box is ever allocated for it.", name: "(whole line)", type: "// comment", badge: "DROP")
            },
            new[]
            {
                MS("// 计算剩余血量", 1, -1,
                    "单行注释：// 后面的字编译器一律不看。上面那排虚线格就是它的下场 —— 代码里有，内存里没有。",
                    "A line comment: everything after // is ignored. The dashed row above is where it lives — in the code, never in memory.").Code("// remaining hp"),
                MS("/* 多行注释：说明一整段逻辑 */", 1, -1,
                    "块注释 /* … */ 一样占 0 字节：写一行是 0，写一百行还是 0，内存里始终只有那排虚线。",
                    "A block comment /* … */ is 0 bytes too: one line or a hundred, memory never grows a box.").Code("/* block comment: explain a whole chunk of logic */"),
                MS("/// <summary>给方法写说明</summary>", 1, -1,
                    "文档注释 /// 同样 0 字节，还能被编辑器提示读出来。注释越多，代码越好懂，内存一点没多。",
                    "Doc comments /// cost 0 bytes and still show up in editor tooltips. More comments, no extra memory.").Code("/// <summary>describe the method</summary>")
            });

        list[1].Mem = MSC(
            new[]
            {
                MV("int", "remainHp", "03 00 00 00", "int = 4 字节 = 32 位，装得下约 ±21 亿。").En("int = 4 bytes = 32 bits, holds about ±2.1 billion."),
                MV("char", "grade", "41", "char = 1 字节，存的是编号不存字形：'A' 的编号 65 = 0x41。").En("char = 1 byte: it stores a code point, not a glyph - 'A' is 65 = 0x41."),
                MV("bool", "alive", "01", "bool = 1 字节：true 就是 01，false 是 00。").En("bool = 1 byte: true is 01, false is 00."),
                MV("float", "ratio", "00 00 C0 3F", "float = 4 字节，按 IEEE-754 存：1.5f 的位模式就是 00 00 C0 3F。").En("float = 4 bytes, IEEE-754: 1.5f is exactly 00 00 C0 3F.")
            },
            new[]
            {
                MS("int remainHp = 3;", 1, 0,
                    "int 一律 4 格。数值 3 写成 0x00000003，落进内存是小端：从低地址 0x100 往高读就是 03 00 00 00 —— 低字节最先，不是按人写字的 00 00 00 03 排。",
                    "int is always 4 boxes. The value 3 is 0x00000003 and lands little-endian: reading upward from 0x100 gives 03 00 00 00 — low byte first, not the 00 00 00 03 you would write on paper."),
                MS("char grade = 'A';", 2, 1,
                    "char 只占 1 格，里面不是字母 'A' 这个形状，而是它的编号 65（0x41）—— 字母靠一张表才变回字形。",
                    "char takes 1 box holding the code 65 (0x41), not the letter's shape — a table turns it back into a glyph."),
                MS("bool alive = true;", 3, 2,
                    "bool 也只占 1 格：01 就是 true。C# 里它不能当数字用，省得你把 01 当 1 去算。",
                    "bool also takes 1 box: 01 means true. C# refuses to treat it as a number, so you cannot do maths on it."),
                MS("float ratio = 1.5f;", 4, 3,
                    "float 又回到 4 格，但装法完全不同：符号位 + 指数 + 尾数，1.5f 恰好是 00 00 C0 3F。",
                    "float is back to 4 boxes with a totally different layout — sign, exponent, mantissa — and 1.5f happens to be 00 00 C0 3F.")
            });

        list[2].Mem = MSC(
            new[]
            {
                MV("int", "hp", "64 00 00 00", "var 推断成 int：内存还是标准的 4 格，100 = 0x64，小端排成 64 00 00 00。").En("var resolves to int: still the usual 4 boxes; 100 = 0x64 little-endian is 64 00 00 00."),
                MV("float", "speed", "00 00 C0 3F", "var 推断成 float：4 格，按 IEEE-754 摆成 00 00 C0 3F。").En("var resolves to float: 4 boxes laid out by IEEE-754 as 00 00 C0 3F.")
            },
            new[]
            {
                MS("var hp = 100;", 1, 0,
                    "编译器看右边是 100（int 字面量），于是替你写成 int hp = 100;。内存里就是那 4 格，一个字节都没省。",
                    "100 on the right is an int literal, so the compiler writes int hp = 100; for you. The same 4 boxes: var saves typing, never bytes."),
                MS("var speed = 1.5f;", 2, 1,
                    "右边带 f 就推断成 float：还是 4 格，只是摆法换成 IEEE-754 的 00 00 C0 3F。",
                    "The f makes it float: still 4 boxes, only the layout differs - IEEE-754 00 00 C0 3F."),
                MS("hp = 1.5f;   // 类型已定", 2, -1,
                    "把小数塞进已经推断成 int 的 hp：编译期报 CS0266。var 定下类型之后，和手写类型一样不自由。",
                    "A decimal into hp, already inferred as int, fails at compile time with CS0266. Once var picks a type it is as rigid as a hand-written one.").Code("hp = 1.5f;   // type already fixed")
            });

        list[3].Mem = MSC(
            new[]
            {
                MV("const int", "MAX_HP", "64 00 00 00", "const 照样占 4 格，只是这块被标成只读 —— 改它编译就过不去。", "锁").En("const takes the same 4 boxes; they are just marked read-only - change it and the build fails.", badge: "CONST"),
                MV("int", "hp", "64 00 00 00", "普通变量的 4 格一模一样，区别只在于随时可以改写。").En("An ordinary variable gets the same 4 boxes; the only difference is you can rewrite them any time.")
            },
            new[]
            {
                MS("const int MAX_HP = 100;", 1, 0,
                    "const 的快照和普通 int 一模一样：4 格，100 = 64 00 00 00。它并不「不占内存」。",
                    "A const looks identical to a plain int: 4 boxes, 100 = 64 00 00 00. It is not \"free\"."),
                MS("int hp = 100;", 2, 1,
                    "再开一块同样的 4 格装当前血量。两块值相同，权限不同。",
                    "A second block of 4 boxes holds the current HP. Same value, different permission."),
                MS("MAX_HP = 999;   // 编译错误", 2, -1,
                    "这一行编译直接报错，内存里的 64 一个字节都没动 —— const 是编译期保护，不是运行时检查。",
                    "This line fails to compile and not a byte changes: const is enforced at compile time, not at run time.").Code("MAX_HP = 999;   // compile error")
            });

        list[4].Mem = MSC(
            new[]
            {
                MV("int", "a", "07 00 00 00", "第一个操作数：7。").En("First operand: 7."),
                MV("int", "b", "05 00 00 00", "第二个操作数：5。").En("Second operand: 5."),
                MV("int", "sum", "0C 00 00 00", "结果 12 = 0x0C，高位三格补 0。加出来是几位就写几位，格子数不变。").En("Result 12 = 0x0C, the upper three boxes stay 0. Only as many digits as needed; the box count never changes.")
            },
            new[]
            {
                MS("int a = 7;", 1, 0, "a 占 4 格，值是 7。", "a takes 4 boxes holding 7."),
                MS("int b = 5;", 2, 1, "b 再占 4 格，值是 5。两块内存互不相干。", "b takes another 4 boxes holding 5. The two blocks are independent."),
                MS("int sum = a + b;", 3, 2,
                    "CPU 把 a、b 各 4 格读进寄存器相加，再把 12 写进 sum 那 4 格 —— 原两块一个字节都没变。",
                    "The CPU reads both 4-box blocks into registers, adds, and writes 12 into sum's 4 boxes — the originals are untouched.")
            });

        list[5].Mem = MSC(
            new[]
            {
                MV("int", "hpMax", "03 00 00 00", "血量上限 3，整个流程里都不变。").En("HP cap 3, never changed in the whole flow."),
                MV("int", "hp", "02 00 00 00", "当前血量 2，if 与 else if 都要来读它。").En("Current HP 2; both if and else if read it.")
            },
            new[]
            {
                MS("int hpMax = 3;", 1, 0, "hpMax 占 4 格，值是 3，后面的比较和 switch 都要读它。", "hpMax takes 4 boxes holding 3; the comparisons and the switch read it."),
                MS("int hp = 2;", 2, 1, "hp 也占 4 格，写入 2 → 02 00 00 00，两块内存互不相干。", "hp takes another 4 boxes written with 2 -> 02 00 00 00; the two blocks are independent."),
                MS("if (hp <= 0)", 3, 1, "读 hp 那 4 格和 0 比：2 <= 0 为假，这一支整块跳过，内存一个字节都没动。", "Reads hp's 4 boxes against 0: 2 <= 0 is false, so the branch is skipped and nothing changes."),
                MS("else if (hp < hpMax && hpMax > 0)", 4, 1, "两条比较各算出 true / false，&& 只有两边都真才进：2 < 3 真、3 > 0 真 → 走这一支。", "Two comparisons each yield true / false and && needs both: 2 < 3 is true and 3 > 0 is true, so this branch runs."),
                MS("switch (hpMax)", 5, 0, "按 hpMax 那 4 格的值挑分支：3 命中 case 3，打印 cap is 3，break 跳出整个 switch。", "switch picks by the value in hpMax's 4 boxes: 3 hits case 3, prints cap is 3, then break leaves the switch.")
            });

        list[6].Mem = MSC(
            new[]
            {
                MV("int", "i", "00 00 00 00", "for 的计数器只有这 1 个 4 格槽位：转多少圈，改的都是同一格。").En("The for counter owns a single 4-box slot: however many rounds, the same box is rewritten.")
            },
            new[]
            {
                MS("int i = 0;", 1, 0,
                    "进门之前只做一次：造出计数器 i，写进 4 格，值是 0 → 00 00 00 00。",
                    "Done once before entering: create i and write 0 into its 4 boxes -> 00 00 00 00."),
                MS("i < 3   // 成立", 1, -1,
                    "每轮进门先读这 4 格，和 3 比：现在 0 < 3 成立，可以进循环体。",
                    "Each round reads those 4 boxes and compares with 3: 0 < 3 holds, so the body runs.").Code("i < 3   // holds"),
                MS("i++", 1, 0,
                    "一趟走完，把同一格从 0 改写成 1：01 00 00 00。不是新造变量，是覆盖旧值。",
                    "After the body the same box is overwritten from 0 to 1. No new variable: the old value is replaced.",
                    new[] { 0 }, new[] { "01 00 00 00" }),
                MS("i++", 1, 0,
                    "再一圈，同一格又变成 2：02 00 00 00。",
                    "One more round and the same box becomes 2.",
                    new[] { 0 }, new[] { "02 00 00 00" }),
                MS("i < 3   // 不成立", 1, -1,
                    "第三次进门先比：3 < 3 为假，循环当场结束，i 就停在这格不再变。",
                    "The next check reads 3 < 3, false, so the loop ends and i stays put.").Code("i < 3   // fails")
            });

        list[7].Mem = MSC(
            new[]
            {
                MV("int[]", "hits", "E8 2F 00 00", "变量本身只有 4 格：它存的是门牌号（引用），不是那三个 int。", "引用").En("The variable itself is only 4 boxes: it holds an address (a reference), not the three ints.", badge: "REF"),
                MV("堆 · int[3]", "hits[0..2]", "02 00 00 00 00 00 00 00 00 00 00 00",
                    "12 格 = 3 个 int 连续排：hits[0] 在第 1 格，hits[1] 偏移 +4，hits[2] 偏移 +8。").En("12 boxes = 3 ints back to back: hits[0] in the first box, hits[1] at +4, hits[2] at +8.", type: "heap · int[3]")
            },
            new[]
            {
                MS("int[] hits = new int[3];", 2, 0,
                    "new 先在堆上划出 12 格（3 个 int），再把这 12 格的起始门牌号写进 hits 那 4 格里。",
                    "new reserves 12 boxes on the heap, then stores the starting address in hits' 4 boxes."),
                MS("hits[0] = 2;", 2, 1,
                    "顺着门牌号找到那一段，把第 0 个 int 改成 2（02 00 00 00）。下标就是「第几块 4 格」。",
                    "Follow the address and set element 0 to 2. An index is just \"which 4-box block\".",
                    new[] { 1 }, new[] { "02 00 00 00 00 00 00 00 00 00 00 00" }),
                MS("hits[3]   // 越界", 2, -1,
                    "只有 0/1/2 三块 4 格。写 hits[3] 就跑到这段内存外面去了 —— 运行时会抛越界异常挡住你。",
                    "Only blocks 0/1/2 exist. hits[3] steps outside this run of memory, and the runtime throws to stop you.").Code("hits[3]   // out of range")
            });

        list[8].Mem = MSC(
            new[]
            {
                MV("List<int>", "hpList", "E8 2F 00 00", "List 变量本身还是 4 格引用，指向它内部的数组和账本。", "引用").En("The List variable is still a 4-box reference pointing at its internal array and counters.", badge: "REF"),
                MV("内部 int", "Count", "00 00 00 00", "Count 是另一个 int：Add 一次就从 0 变 1，取数量不用数元素。").En("Count is another int: one Add moves it 0 -> 1, so size costs no scan.", type: "inner int"),
                MV("内部 int[2]", "hpList[0..1]", "00 00 00 00 00 00 00 00",
                    "内部数组一开始容量小（这里 2 个 int = 8 格），装满会自动翻倍换更大的。").En("The internal array starts small (2 ints = 8 boxes) and doubles into a bigger one when full.", type: "inner int[2]")
            },
            new[]
            {
                MS("List<int> hpList = new List<int>();", 3, 0,
                    "new 一个 List：内部数组先划一小段（8 格），Count 那 4 格写 0，hpList 只记门牌号。",
                    "A new List reserves a small internal array (8 boxes), zeroes Count, and hpList keeps only the address."),
                MS("hpList.Add(3);", 3, 2,
                    "Add 干两件事：把 3 写进内部数组第 0 块（03 00 00 00），再把 Count 改成 1。所以亮的是两段内存。",
                    "Add does two things: write 3 into block 0 and set Count to 1 — two blocks light up at once.",
                    new[] { 1, 2 }, new[] { "01 00 00 00", "03 00 00 00 00 00 00 00" }),
                MS("hpList.Count", 3, 1,
                    "取数量读的就是 Count 那块 int（所以这一步亮的是它），O(1)；换成数组得自己维护一个计数变量。",
                    "Reading Count is a single int read (that is why it lights up here), O(1); with a plain array you would maintain a counter yourself.")
            });

        list[9].Mem = MSC(
            new[]
            {
                MV("int[]", "hits", "E8 2F 00 00", "数组变量本身只有 4 格：存的是门牌号（引用），三个 int 在堆上。", "引用").En("The array variable is 4 boxes holding an address; the three ints live on the heap.", badge: "REF"),
                MV("堆 · int[3]", "hits[0..2]", "02 00 00 00 04 00 00 00 00 00 00 00",
                    "12 格 = 3 个 int 连排：hits[0]=2、hits[1]=4、hits[2]=0。").En("12 boxes = 3 ints back to back: hits[0]=2, hits[1]=4, hits[2]=0.", type: "heap · int[3]"),
                MV("int", "h", "02 00 00 00", "foreach 的 h 是独立的 4 格：每轮只是把当前元素复制进来，它不是元素本身。", "副本").En("The foreach h is its own 4 boxes: each round copies the current element in; it is not the element itself.", badge: "COPY")
            },
            new[]
            {
                MS("int[] hits = new int[3];", 2, 0,
                    "先在堆上划 12 格（3 个 int），再把起始门牌号写进 hits 那 4 格。此刻三块都是 0。",
                    "Reserve 12 boxes on the heap, then store the starting address in hits' 4 boxes. All three blocks are 0 right now."),
                MS("hits[0] = 2;  hits[1] = 4;", 3, 1,
                    "顺着门牌号改前两块 4 格：第 0 块变 2，第 1 块变 4。",
                    "Follow the address and rewrite the first two blocks: block 0 becomes 2, block 1 becomes 4.",
                    new[] { 1 }, new[] { "02 00 00 00 04 00 00 00 00 00 00 00" }),
                MS("foreach (int h in hits)", 3, 2,
                    "第一轮：把 hits[0] 的 2 复制进 h 那 4 格。h 是副本，改它不会动数组。",
                    "Round one: copy hits[0]'s 2 into h's 4 boxes. h is a copy, so changing it never touches the array."),
                MS("foreach 第二轮", 3, 2,
                    "第二轮：h 那格被覆盖成 4（hits[1] 的值）。同一格反复用，不新造变量。",
                    "Round two: the same h box is overwritten with 4, the value of hits[1]. One box, reused.",
                    new[] { 2 }, new[] { "04 00 00 00" }).Code("foreach round 2"),
                MS("foreach 第三轮", 3, 2,
                    "第三轮：h 变成 0。三轮走完，hits 那 12 格一个字节都没动过。",
                    "Round three: h becomes 0. After three rounds, none of the 12 boxes in hits has changed.",
                    new[] { 2 }, new[] { "00 00 00 00" }).Code("foreach round 3")
            });

        list[10].Mem = MSC(
            new[]
            {
                MV("int", "n", "03 00 00 00", "while 的条件读的就是这块：只要还 > 0 就继续。").En("The while condition reads exactly this slot: keep looping while it stays > 0.")
            },
            new[]
            {
                MS("int n = 3;", 1, 0, "先给 n 一个值：4 格，03 00 00 00。", "Give n a value first: 4 boxes, 03 00 00 00."),
                MS("while (n > 0)", 1, -1,
                    "每轮开头都重新读这块内存判断：3 > 0 成立，进循环体。判断本身不改它。",
                    "Every round re-reads this block: 3 > 0 holds, so enter the body. The test itself writes nothing."),
                MS("n--;", 1, 0,
                    "循环体把它减 1，同一块 4 格变成 02 —— 这就是让循环最终能停下来的那一句。",
                    "The body decrements it: the same 4 boxes become 02 — this is what eventually stops the loop.",
                    new[] { 0 }, new[] { "02 00 00 00" }),
                MS("while (n > 0)  →  false", 1, 0,
                    "再减到 0，条件不成立，循环结束。忘记改这块内存就是死循环。",
                    "Down to 0, the condition fails and the loop ends. Forgetting to change this block is how infinite loops happen.",
                    new[] { 0 }, new[] { "00 00 00 00" })
            });

        list[11].Mem = MSC(
            new[]
            {
                MV("int", "d", "00 00 00 00", "do 先冲进循环体，所以这块一定会被改成 1，哪怕条件一开始就不成立。").En("A do loop runs its body first, so this slot always becomes 1 even when the condition was false from the start.")
            },
            new[]
            {
                MS("int d = 0;", 1, 0, "计数器先归零：4 格全 00。", "Zero the counter: 4 boxes, all 00."),
                MS("do { d++; }", 1, 0,
                    "do 的特点：不看条件先执行一次。d 那 4 格被无条件改写成 1。",
                    "That is what do does: run once without checking. d's 4 boxes are overwritten with 1 unconditionally.",
                    new[] { 0 }, new[] { "01 00 00 00" }),
                MS("while (d < 3);", 1, -1,
                    "最后才判断 1 < 3 成立，回头再干一次。注意条件后面那个分号，漏了编译器就报错。",
                    "Only then the test 1 < 3, so go again. Do not drop that trailing semicolon or the compiler complains.")
            });

        list[12].Mem = MSC(
            new[]
            {
                MV("void", "ShowHp（定义）", "-- -- -- --", "方法定义不占数据内存：方法体躺在代码区，等被调用才在栈上开格子。", "代码区", true).En("A method definition takes no data memory: the body lives in the code area and only gets stack boxes when called.", name: "ShowHp (definition)", badge: "CODE"),
                MV("int", "hp（参数）", "64 00 00 00", "调用时才给参数开 4 格，把实参 100 复制进去 —— 是复制，不是共享。", "栈帧").En("A parameter's 4 boxes are opened at call time and 100 is copied in - copied, not shared.", name: "hp (param)", badge: "FRAME")
            },
            new[]
            {
                MS("void ShowHp(int hp) { … }", 1, -1,
                    "只是定义一个方法：一行数据内存都不占。上面那排虚线格提醒你 —— 它在代码区，不在数据区。",
                    "Defining a method claims no data memory. The dashed row reminds you it lives in the code region."),
                MS("ShowHp(100);", 2, 1,
                    "调用发生：栈上新开一层栈帧，给 hp 划 4 格，把 100 复制进去。此时方法外的变量一点没动。",
                    "The call opens a new stack frame, reserves 4 boxes for hp and copies 100 in. Outside variables are untouched."),
                MS("方法返回", 1, -1,
                    "方法一结束，这层栈帧整块销毁，hp 的 4 格随之消失 —— 局部变量为什么不能跨方法用，原因就在这。",
                    "When the method ends the frame is destroyed and hp's 4 boxes vanish — that is why locals cannot leak out.").Code("method returns")
            });

        list[13].Mem = MSC(
            new[]
            {
                MV("int", "v（参数）", "02 00 00 00", "调用时新开的参数格：把 2 复制进来。", "栈帧").En("A freshly opened parameter slot: 2 is copied into it.", name: "v (param)", badge: "FRAME"),
                MV("int", "返回值", "04 00 00 00", "return 把结果放进这块，它是方法交给外面的唯一东西。", "栈帧").En("return puts the result in this box; it is the only thing handed back out.", name: "return value", badge: "FRAME"),
                MV("int", "sum", "04 00 00 00", "调用者的变量。它比方法内的格子活得久，因为它在外面那一层。").En("The caller's variable. It outlives the boxes inside the method because it lives one level out.")
            },
            new[]
            {
                MS("int Twice(int v) { … }", 0, -1,
                    "方法还没调用：内存里什么都没有（返回值也在等）。",
                    "Not called yet: nothing is allocated, not even the return slot."),
                MS("Twice(2)", 1, 0,
                    "参数 v 先拿到 4 格，值 2。", "Parameter v gets its 4 boxes holding 2."),
                MS("return v * 2;", 2, 1,
                    "算出 4，放进「返回值」那 4 格。到这里方法内的内存还是活的。",
                    "Compute 4 into the return slot. The method's own memory is still alive here."),
                MS("int sum = Twice(2);", 3, 2,
                    "外面把 4 复制进 sum，随后栈帧销毁：v 和返回值都没了，只剩 sum。",
                    "Outside, 4 is copied into sum and the frame is destroyed — v and the return slot are gone, sum remains.")
            });

        list[14].Mem = MSC(
            new[]
            {
                MV("int", "curHp", "03 00 00 00", "先有数字，才有得拼。").En("First the digits, then there is something to concatenate."),
                MV("string", "msg", "E8 2F 00 00", "msg 只有 4 格：存门牌号。字符串本身在堆上，不在这 4 格里。", "引用").En("msg is only 4 boxes: an address. The characters live on the heap, not here.", badge: "REF"),
                MV("堆 · char[]", "\"hp=3\"", "68 00 70 00 3D 00 33 00",
                    "string 内部是 UTF-16：每个字符 2 字节。h=0068、p=0070、==003D、3=0033，正好 8 格。").En("Inside a string it is UTF-16: 2 bytes per char. h=0068, p=0070, = 003D, 3 = 0033 - 8 boxes.", type: "heap · char[]")
            },
            new[]
            {
                MS("int curHp = 3;", 1, 0, "数字先占 4 格：03 00 00 00。", "The number takes 4 boxes: 03 00 00 00."),
                MS("string msg = \"hp=\" + curHp;", 3, 1,
                    "字符串不可变：+ 会在堆上新造一段字符（h p = 3 各占 2 字节），再把门牌号写进 msg。原来那段没人动。",
                    "Strings are immutable: + builds a brand-new char array in the heap, then stores its address in msg.",
                    new[] { 2 }, new[] { "68 00 70 00 3D 00 33 00" }),
                MS("tower.PrintLine(msg + \" / done\");", 3, -1,
                    "再 + 一次又造一段更长的字符数组，刚才那 8 格立刻变成没人引用的垃圾，等 GC 回收。",
                    "Another + builds an even longer array; the previous 8 boxes become garbage for the GC.")
            });

        list[15].Mem = MSC(
            new[]
            {
                MV("int", "baseHp", "64 00 00 00", "参与格式化的值：100 = 64 00 00 00。").En("The value being formatted: 100 = 64 00 00 00."),
                MV("string", "text", "F0 2F 00 00", "格式化完后 text 只拿到门牌号。", "引用").En("After formatting, text only receives an address.", badge: "REF"),
                MV("堆 · char[]", "\"hp=100\"", "68 00 70 00 3D 00 31 00 30 00 30 00",
                    "UTF-16 每字符 2 字节：h p = 1 0 0 共 6 个字符 = 12 格，正好排满这一行。").En("UTF-16, 2 bytes per char: h p = 1 0 0 is 6 chars = 12 boxes, filling this row exactly.", type: "heap · char[]")
            },
            new[]
            {
                MS("int baseHp = 100;", 1, 0, "先把要填进模板的值准备好：4 格。", "First prepare the value that fills the template: 4 boxes."),
                MS("string.Format(\"hp={0}\", baseHp)", 3, 1,
                    "Format 按模板一趟拼完：把 100 的十进制字符接在 \"hp=\" 后面，堆上新出 12 格；它不会为每个片段各造一段再相加。",
                    "Format assembles the result in one pass: append \"100\" after \"hp=\", allocating 12 fresh char boxes instead of one per fragment."),
                MS("text = 格式化结果", 3, -1,
                    "text 拿到这段新字符的门牌号。模板里的 {0} 意思就是「把第 0 个参数塞到这个位置」。",
                    "text receives the address of that new string. The {0} in the template means \"put argument 0 right here\".").Code("text = formatted result")
            });

        list[16].Mem = MSC(
            new[]
            {
                MV("float", "up.x", "00 00 00 00", "Vector3 就是 3 个 float 挨着排：第一块是 x = 0。").En("A Vector3 is three floats side by side: the first box is x = 0."),
                MV("float", "up.y", "00 00 80 3F", "第二块 y = 1f。float 的 1 不是 01，而是 0x3F800000（小端写成 00 00 80 3F）。").En("The second box is y = 1f. Float 1 is not 01 but 0x3F800000, little-endian 00 00 80 3F."),
                MV("float", "up.z", "00 00 00 00", "第三块 z = 0。三块合起来 12 字节，整包就叫 up。").En("Third box z = 0. Together 12 bytes, and the whole pack is up."),
                MV("Vector2", "flat", "00 00 80 3F 00 00 00 00", "Vector2 只划两块 = 8 字节，x 在前 y 在后，没有 z。").En("Vector2 gets only two boxes = 8 bytes, x then y, no z.")
            },
            new[]
            {
                MS("Vector3 up = new Vector3(0f, 1f, 0f);", 1, 0,
                    "new 一次划出 3 个 float 的格子，先把第一块 x 写成 0。",
                    "new reserves three float boxes at once and writes the first one, x = 0."),
                MS("Vector3 up = new Vector3(0f, 1f, 0f);", 2, 1,
                    "第二块写 y = 1f —— 存的是 0x3F800000 的位模式，不是整数字 1。",
                    "Write y = 1f into the second box - the bit pattern 0x3F800000, not the integer 1."),
                MS("Vector3 up = new Vector3(0f, 1f, 0f);", 3, 2,
                    "第三块 z = 0，三个 float 齐了。变量 up 指的就是这连续 12 字节。",
                    "The third box z = 0 completes it; up names these 12 contiguous bytes."),
                MS("tower.Fire(up);", 4, -1,
                    "传参不是传三个数：把这段 12 字节整块复制给方法。所以方法签名只写一个 Vector3 就够。",
                    "Passing it does not send three numbers: the whole 12-byte block is copied in, which is why one Vector3 parameter is enough."),
                MS("Vector2 flat = new Vector2(1f, 0f);", 5, 3,
                    "Vector2 只划两块：8 字节。少的那块不是 0，是根本没有 z。",
                    "Vector2 reserves two boxes only: 8 bytes. The missing one is not zero, it simply does not exist.")
            });

        list[17].Mem = MSC(
            new[]
            {
                MV("CodeTower", "tower", "A0 1C 00 00", "tower 这个变量只占 4 格：里面是门牌号，真身躺在堆上。", "引用").En("The variable tower is 4 bytes: an address; the real object sits on the heap.", badge: "REF"),
                MV("堆 · 对象", "CodeTower 真身", "-- -- -- --",
                    "类 = 图纸，对象 = 按图纸造出来的实物。new 出来的东西住在这里，不在这 4 格里。").En("Class = blueprint, object = the thing built from it. Whatever new creates lives here, not in those 4 bytes.", name: "the CodeTower object", type: "heap · object"),
                MV("Vector3", "dir", "00 00 00 00 00 00 00 00 00 00 80 3F",
                    "dir 是结构体：12 字节直接摊在变量自己身上，不含门牌号，所以点号取成员不用绕堆。").En("dir is a struct: 12 bytes inline in the variable itself, no address to follow, so the dot needs no heap hop.")
            },
            new[]
            {
                MS("Vector3 dir = new Vector3(0f, 0f, 1f);", 3, 2,
                    "new 造出一个对象，= 把它存进 dir：dir 自己那 12 格里现在就装着这个对象。",
                    "new creates an object and = stores it: dir's own 12 bytes now hold it."),
                MS("tower.Fire(dir);", 3, -1,
                    "点号先顺着 tower 的 4 格门牌号找到堆上的真身，再把 dir 整块复制进 Fire 的参数。",
                    "The dot follows tower's 4-byte address to the real object, then dir is copied whole into Fire's parameter."),
                MS("dir.z", 3, -1,
                    "点号取成员：直接在 dir 那 12 格里跳到对应那块 float 读出来，读的时候内存一个字节都没改。",
                    "Dot member access jumps to that float inside dir's 12 bytes and reads it - not a byte is changed."),
                MS("tower.Fire(dir);", 3, -1,
                    "同一个点号，左边是对象、右边是成员：右边是数据就读，是方法就调。这就是「类把数据和动作打包在一起」。",
                    "Same dot both times: object on the left, member on the right. Data gets read, methods get called - that is a class packing data with behaviour.").Code("tower.Fire(dir);")
            });

        list[18].Mem = MSC(
            new[]
            {
                MV("类 · 字段区", "shots", "00 00 00 00", "GlobalCode 里的声明被搬到类层级：shots 不属于某一帧，而是住在这个类的对象里（堆上）。", "字段").En("A declaration inside GlobalCode is hoisted to class level: shots belongs to the object on the heap, not to a frame.", type: "class · fields", badge: "GLOBAL"),
                MV("每帧 · Run", "同一份 shots", "00 00 00 00", "每次 Run 拿到的都是同一个 shots：上一帧写进去的值，这一帧进来还在（普通局部变量正相反，进方法就重造）。", "同一份").En("Every Run sees the same shots: what the last frame wrote is still there (a local variable is rebuilt on entry).", name: "the same shots", type: "per frame · Run", badge: "SAME"),
                MV("重新编译", "全部归零", "-- -- -- --", "点编译 = 新造一个类、新造一个对象，全局区跟着重新初始化；它记的是这次运行，不是永久存档。", "重置").En("Recompiling builds a new class and a new object, so globals re-initialise; they last for this run, not as a save file.", name: "all back to zero", type: "recompile", badge: "RESET")
            },
            new[]
            {
                MS("GlobalCode {", 3, -1,
                    "编辑器在编译前把块里的行剪走、贴到类的最外层 —— 方法体里不允许的字段声明，在这里变成了合法字段。这也是作者刻意加的语法，不是 C# 本身的功能。",
                    "Before compiling, the editor cuts those lines out and pastes them at class level - what a method body forbids becomes a legal field. This syntax is the author's own extension, not a C# feature."),
                MS("int shots = 0;", 3, -1,
                    "这条声明变成字段 shots：初始值 0 只在对象创建时给一次，之后由每帧代码自己改。",
                    "The declaration becomes the field shots: 0 is assigned once at construction, afterwards the per-frame code drives it."),
                MS("shots = shots + 1;", 3, -1,
                    "每帧读、加一、再写回同一个位置：所以它是「累计」，不是「每帧重置」。",
                    "Each frame reads it, adds one, writes it back to the same slot - so it accumulates instead of resetting."),
                MS("tower.PrintLine(\"shots={0}\", shots);", 3, -1,
                    "打印的就是那份跨帧存活的值：1、2、3…… 一路涨上去。",
                    "What gets printed is that surviving value: 1, 2, 3... climbing.")
            });

    }

    /// <summary>
    /// 给语法课第 20~34 项任务配第 3 页。原则：能用「一格一字节」讲清的仍旧走内存页（三元取舍、复合赋值前后值），
    /// 讲的是控制流 / 调用关系 / 数据结构这类「没有字节可看」的概念，就改用逻辑图（方框 + 箭头 + 分步点亮），
    /// 两种形式共用同一套第 3 页入口、页数计数和 ↑↓ 手动推进。
    /// </summary>
    private static void AppendGrammarDiagramPages(TaskDef[] list)
    {
        // ---- Task 21 三元运算：仍然用内存格，因为要讲的正是「两个分支里只有一个会写进内存」 ----
        list[20].Mem = MSC(
            new[]
            {
                MV("int", "hpLeft", "03 00 00 00", "hpLeft 是 int：4 格，3 写成 03 00 00 00（小端）。").En("hpLeft is an int: 4 boxes, 3 becomes 03 00 00 00 (little-endian)."),
                MV("bool", "alive", "01", "alive 是 bool：只有 1 格，01 就是 true。", badge: "1B").En("alive is a bool: 1 box only, 01 means true.", badge: "1B"),
                MV("int", "bonus", "00 00 00 00", "bonus 是 int：4 格，先全 0，等右边算完再整块盖掉。").En("bonus is an int: 4 boxes, all zero until the right side is computed.")
            },
            new[]
            {
                MS("int hpLeft = 3;", 1, 0,
                    "先把操作数放进格子：hpLeft = 3，4 格写满 03 00 00 00。三元运算要等它算完才轮到结果落地。",
                    "The operand lands first: hpLeft = 3 fills 4 boxes with 03 00 00 00. The ternary waits for it before anything is written."),
                MS("bool alive = hpLeft > 0;", 2, 1,
                    "比较只读不写：3 > 0 成立 → 取 true，落进 bool 那 1 格就是 01。原来的 hpLeft 一格没动。",
                    "A comparison only reads: 3 > 0 is true, so the single bool box becomes 01. hpLeft is untouched."),
                MS("int bonus = hpLeft > 5 ? 10 : 0;", 3, 2,
                    "三元只走一支：条件 hpLeft > 5 是假，于是取冒号后面的 0，冒号前面的 10 根本不参与计算；结果 0 写进 bonus 的 4 格 → 全是 00。",
                    "Only one branch runs: hpLeft > 5 is false, so the 0 after the colon is chosen and the 10 is never even computed. That 0 becomes 00 00 00 00."),
                MS("hpLeft = 8;", 3, 0,
                    "把操作数改大，同一行三元代码的走向就会反过来 —— 这也是为什么它能当「二选一」用。",
                    "Make the operand bigger and the same ternary flips direction - that is what makes it a two-way choice.",
                    new[] { 0 }, new[] { "08 00 00 00" }),
                MS("bonus = hpLeft > 5 ? 10 : 0;", 3, 2,
                    "这次条件为真，取冒号前面的 10 = 0x0A → bonus 变成 0A 00 00 00。一行代码两种结果，但每次只有一支被执行。",
                    "Now the condition holds, so the 10 is chosen: 0x0A lands in bonus. One line, two possible results, one branch per run.",
                    new[] { 2 }, new[] { "0A 00 00 00" })
            });

        // ---- Task 22 复合赋值 / ++ / --：内存格最直观，看同一段 4 格被反复覆盖 ----
        list[21].Mem = MSC(
            new[]
            {
                MV("int", "a", "05 00 00 00", "a 是 int：4 格，起始 5。").En("a is an int: 4 boxes starting at 5."),
                MV("int", "b", "00 00 00 00", "b 是 int：4 格，等 a++ 的旧值。").En("b is an int: 4 boxes waiting for a++ 's old value."),
                MV("int", "c", "00 00 00 00", "c 是 int：4 格，等 ++a 的新值。").En("c is an int: 4 boxes waiting for ++a 's new value.")
            },
            new[]
            {
                MS("int a = 5;", 1, 0,
                    "起点：a 的 4 格写进 5。后面所有操作都是在这同一段格子上反复覆盖，不会另开新格子。",
                    "Starting point: 5 goes into a's 4 boxes. Everything after overwrites this same slot - no new boxes appear."),
                MS("int b = a++;", 2, 1,
                    "a++ 是「先用后加」：先把旧的 5 交给 b，然后 a 自己加 1 变成 6。所以 b = 5、a = 6，两颗格子各拿一个值。",
                    "a++ uses first, increments after: b receives the old 5, then a becomes 6. Two boxes, two different numbers.",
                    new[] { 0 }, new[] { "06 00 00 00" }),
                MS("int c = ++a;", 3, 2,
                    "++a 是「先加后用」：a 先变 7，c 才拿到 7。同样的自增，写在变量前面还是后面，取到的值完全不同。",
                    "++a increments first: a becomes 7, then c copies 7. Same operator, different side of the name, different value.",
                    new[] { 0 }, new[] { "07 00 00 00" }),
                MS("a += 3;", 3, 0,
                    "a += 3 就是 a = a + 3：先读出 7 算成 10，再整块写回同一段 4 格 → 0A 00 00 00。-= *= /= %= 全是这个套路。",
                    "a += 3 means a = a + 3: read 7, compute 10, write it back into the same 4 boxes. -= *= /= %= all work the same way.",
                    new[] { 0 }, new[] { "0A 00 00 00" }),
                MS("a *= 2;", 3, 0,
                    "读 10、乘 2、写回 20 → 14 00 00 00。复合赋值只有一处变量名，少写一次就不会把 a 拼错成别的名字。",
                    "Read 10, double it, write back 20. Compound assignment names the variable once, so you cannot mistype it on the other side.",
                    new[] { 0 }, new[] { "14 00 00 00" })
            });

        // ---- Task 20 break / continue：控制流，逻辑图最清楚 ----
        list[19].Dia = DSC(
            new[]
            {
                DN("for (i = 1; i <= 5)", 0.14f, 0.12f, DiaTone.Accent).En("for (i = 1; i <= 5)").Size(0.26f),
                DN("i == 2 ?", 0.28f, 0.46f, DiaTone.Purple).Size(0.18f),
                DN("continue：跳过本轮", 0.14f, 0.8f, DiaTone.Alert).En("continue: skip this round").Size(0.26f),
                DN("i == 4 ?", 0.58f, 0.46f, DiaTone.Purple).Size(0.18f),
                DN("Debug.Log(i)", 0.58f, 0.12f, DiaTone.Value).Size(0.20f),
                DN("break：整个循环结束", 0.88f, 0.46f, DiaTone.Alert).En("break: end the whole loop").Size(0.26f),
                DN("循环后面的代码", 0.88f, 0.8f, DiaTone.Panel).En("code after the loop").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("进圈", "enter"),
                DE(1, 2).Lb("是", "yes"),
                DE(2, 0).Lb("回到 i++", "back to i++"),
                DE(1, 3).Lb("否", "no"),
                DE(3, 4).Lb("否", "no"),
                DE(3, 5).Lb("是", "yes"),
                DE(4, 0).Lb("回到 i++", "back to i++"),
                DE(5, 6).Lb("跳出", "go on")
            },
            new[]
            {
                DS(H(0), H(0),
                    "for 的头先判 i <= 5，成立才进圈。这一行就是整张图的跑道，两个 if 是跑道上的两个岔口。",
                    "The for header checks i <= 5 and only then enters. That is the track; the two ifs are its forks."),
                DS(H(1, 2), H(1, 2),
                    "i 到 2 时，i == 2 成立 → continue 立刻结束这一轮：下面的 Debug.Log 一个字都不执行，直接回到 i++ 再来。所以 2 永远不会被打印。",
                    "At i == 2 the first check fires: continue ends this round immediately, Debug.Log never runs, control jumps to i++. That is why 2 never prints."),
                DS(H(3, 4), H(3, 5, 6),
                    "i 是 1 或 3 时，两个条件都不成立，才轮到 Debug.Log(i)：打印 1 和 3，然后回到 i++。注意它是「穿过两个岔口」才走到的。",
                    "For i = 1 or 3 neither condition fires, so Debug.Log(i) finally runs: it prints 1 and 3, then loops back through i++. It has to pass both forks to get here."),
                DS(H(5, 6), H(7),
                    "i 到 4 时 i == 4 成立 → break 不是「跳过一轮」，而是「整个 for 立刻结束」：连该跑的 i=5 都不跑了，直接去做循环后面的代码。",
                    "At i == 4 break fires - it does not skip a round, it ends the whole loop: i = 5 never happens and control continues after the for."),
                DS(H(2), H(2),
                    "两类常见误写：break 只跳最近的一层循环，嵌在 switch 里时它跳的是 switch、外面这圈照样转；continue 跳回的是 i++ 那一步，所以 for 里安全 —— 但在 while 里如果把自增写在 continue 后面，那一轮被跳过后 i 就再也不变，直接死循环。",
                    "Two classic slips: break leaves only the innermost loop, so inside a switch it leaves the switch and the outer loop keeps turning; continue jumps to the i++ step, which is why for loops are safe - but in a while loop an increment placed after continue is skipped and the counter never moves again."),
            },
            "continue = 只跳过这一轮（回 i++）；break = 整个循环结束（往下走）。",
            "continue skips this round only (back to i++); break ends the whole loop.");

        // ---- Task 23 逻辑运算与短路：右侧被「省掉」这件事，只有流程图讲得清 ----
        list[22].Dia = DSC(
            new[]
            {
                DN("name != null", 0.24f, 0.14f, DiaTone.Purple).Size(0.22f),
                DN("name.Length > 0", 0.24f, 0.50f, DiaTone.Purple).En("name.Length > 0").Size(0.24f),
                DN("整个 && 为 false", 0.74f, 0.30f, DiaTone.Alert).En("the && is false").Size(0.24f),
                DN("进 if 分支", 0.74f, 0.78f, DiaTone.Value).En("enter the if body").Size(0.22f)
            },
            new[]
            {
                DE(0, 1).Lb("真，才看右边", "true, check right"),
                DE(0, 2).Lb("假，直接定", "false, short-circuit").Dash(),
                DE(1, 2).Lb("假", "false"),
                DE(1, 3).Lb("真", "true")
            },
            new[]
            {
                DS(H(0), null,
                    "&& 从左往右算：先算最左边的 name != null。这一步只做一件事 —— 问 name 是不是空。",
                    "&& evaluates left to right: the leftmost name != null comes first. It answers one question only - is name empty?"),
                DS(H(0, 2), H(1),
                    "name 是 null 时左边为假，&& 已经能定结果了，于是右边的 name.Length 一次都不执行 —— 因为对 null 取 .Length 会抛 NullReferenceException。这叫短路：右侧是被「省掉」的，不是算完再合并。",
                    "If name is null the left side is false and the && is already decided, so name.Length never runs at all - reading .Length on null throws NullReferenceException. That is short-circuiting: the right side is skipped, not computed and merged."),
                DS(H(1), H(0),
                    "name 不为 null 时才轮得到右边。所以判断「空 or 空串」必须把 null 检查写在 && 左边，写反了照样崩。",
                    "Only when name is not null does the right side run. That is why the null check must sit left of the && - swap them and it crashes anyway."),
                DS(H(3), H(2),
                    "两边都真 → 整体 true → 进分支；只要有一边假，整行立刻结束。|| 完全对称：左边真就跳过右边。所以最便宜、最可能先定结论的条件，放最左边。",
                    "Both true means the whole thing is true and the body runs; one false ends the line at once. || is the mirror image: a true left side skips the right. Put the cheapest, most decisive check leftmost."),
                DS(H(0, 1), H(0),
                    "两个坑：写成单个 & 或 | 就没有短路，两边一定都算（对 bool 照样能编过，所以很难发现）；另外别把带副作用的表达式塞进条件里，一旦短路跳过右边，那句 i++ 或那次方法调用就根本没发生。",
                    "Two traps: a single & or | disables short-circuiting and always evaluates both sides (it still compiles on bool, which is why it hides well); and never put side effects inside the condition, since a short circuit means that i++ or method call simply never happens."),
            },
            "&&：左边假 → 右边不执行；||：左边真 → 右边不执行。",
            "&&: left false means right never runs; ||: left true means right never runs.");

        // ---- Task 24 字符串成员：把一条处理流水线拆成几段，看每一段产出什么 ----
        list[23].Dia = DSC(
            new[]
            {
                DN("\"  Rifle,AK  \"", 0.14f, 0.12f, DiaTone.Purple).Size(0.20f),
                DN("Trim()", 0.4f, 0.12f, DiaTone.Accent).Size(0.16f),
                DN("\"Rifle,AK\"", 0.64f, 0.12f, DiaTone.Value).Size(0.20f),
                DN("Split(',')", 0.52f, 0.46f, DiaTone.Accent).Size(0.16f),
                DN("parts[0] = \"Rifle\"", 0.52f, 0.84f, DiaTone.Value).Size(0.22f),
                DN("parts[1] = \"AK\"", 0.2f, 0.84f, DiaTone.Value).Size(0.20f),
                DN("Length = 12", 0.14f, 0.46f, DiaTone.Purple).En("Length = 12").Size(0.16f),
                DN("ToUpper / Replace / Substring …", 0.82f, 0.84f, DiaTone.Panel).En("ToUpper / Replace / Substring ...").Size(0.28f)
            },
            new[]
            {
                DE(0, 1).Lb("砍首尾空格", "trim both ends"),
                DE(1, 2),
                DE(2, 3).Lb("按逗号切", "split on ','"),
                DE(3, 4).Lb("下标 0", "index 0"),
                DE(3, 5).Lb("下标 1", "index 1"),
                DE(0, 6).Lb("属性，不是方法", "a property").Dash().NoArrow(),
                DE(4, 7).Lb("还能继续串", "chain further")
            },
            new[]
            {
                DS(H(0, 6), H(5),
                    "字符串建好就改不了（不可变）。Length 是属性不是方法，直接数出字符个数：「  Rifle,AK  」两边各两个空格，加上 8 个字符共 12 个。",
                    "Once built a string never changes. Length is a property, not a method: two spaces on each side plus 8 characters gives 12."),
                DS(H(1, 2), H(0, 1),
                    "Trim() 砍掉首尾空格（中间的不管），并且返回一个新串「Rifle,AK」—— 原来那个带空格的串一个字都没变，还在内存里。",
                    "Trim() removes the outer spaces (inner ones stay) and returns a brand new string. The original, spaces and all, is untouched in memory."),
                DS(H(3, 4, 5), H(2, 3, 4),
                    "Split(',') 按逗号切开，交回一个 string[]：parts[0] 是「Rifle」，parts[1] 是「AK」。下标从 0 开始，切出几段就有几个元素。",
                    "Split(',') cuts on the comma and hands back a string[]: parts[0] is \"Rifle\", parts[1] is \"AK\". Indexes start at 0, one element per piece."),
                DS(H(4, 7), H(6),
                    "拿到一段之后，ToUpper / Contains / Substring / IndexOf / Replace / Join 全是「再产出一个新串」，谁都不修改原件。所以字符串方法要看返回值 —— 不接返回值等于白算。",
                    "From there ToUpper / Contains / Substring / IndexOf / Replace / Join each produce yet another new string and never edit the source. Always use their return value, or the work is thrown away."),
                DS(H(4, 7), H(6, 7),
                    "实际写起来的两条纪律：循环里别用 + 反复拼串，每拼一次就造一个新串、留一份垃圾，几百次就够卡一下 —— 用 StringBuilder；比内容永远用 == 或 Equals，别拿 ReferenceEquals 当相等，两个内容一样的串通常不是同一块内存。",
                    "Two habits that pay: never build a string with + inside a loop, every pass allocates a new one and leaves garbage - use StringBuilder; and compare contents with == or Equals, never ReferenceEquals, since two identical strings are usually different blocks of memory."),
            },
            "字符串不可变：每个方法都在造新串，原串不动。",
            "Strings are immutable: every method builds a new string and leaves the original alone.");

        // ---- Task 25 数组 Length 与下标范围：越界那一步必须画出来 ----
        list[24].Dia = DSC(
            new[]
            {
                DN("int[] hits = new int[3]", 0.36f, 0.10f, DiaTone.Purple).En("int[] hits = new int[3]").Size(0.32f),
                DN("hits[0]", 0.12f, 0.42f, DiaTone.Value).Size(0.14f),
                DN("hits[1]", 0.36f, 0.42f, DiaTone.Value).Size(0.14f),
                DN("hits[2]", 0.60f, 0.42f, DiaTone.Value).Size(0.14f),
                DN("hits.Length = 3", 0.24f, 0.76f, DiaTone.Accent).En("hits.Length = 3").Size(0.24f),
                DN("hits[3] 越界异常", 0.74f, 0.76f, DiaTone.Alert).En("hits[3] out of range").Size(0.26f)
            },
            new[]
            {
                DE(0, 1), DE(0, 2), DE(0, 3),
                DE(0, 4).Lb("长度记在数组头上", "length lives on the array").Dash(),
                DE(4, 5).Lb("下标 ≥ 3 没有格子", "no slot from 3 up")
            },
            new[]
            {
                DS(H(0), null,
                    "new int[3] 一次在堆上划出 3 个格子，每格 4 字节，一开始全是 0。数组对象头上记着长度 3 —— 这就是 Length。",
                    "new int[3] carves 3 boxes on the heap, 4 bytes each, all zero at first. The array object remembers its length of 3 - that is Length."),
                DS(H(1, 2, 3), H(0, 1, 2),
                    "下标从 0 开始，所以合法范围是 0 ~ Length-1，也就是 0、1、2。嵌套循环里写 r < rows、c < cols，用的正是这条边界。",
                    "Indexes start at 0, so the legal range is 0..Length-1 - here 0, 1, 2. Nested loops use exactly that bound when they write r < rows, c < cols."),
                DS(H(4), H(3),
                    "Length 是属性，读它不花钱；循环里写 i < hits.Length 比写死 3 更安全 —— 以后数组变长，循环自动跟着变。",
                    "Length is a property and free to read; writing i < hits.Length beats hard-coding 3, because the loop follows the array when it grows."),
                DS(H(5), H(4),
                    "写 hits[3] 编译器不拦（长度是运行时才知道的），一跑就抛 IndexOutOfRangeException。所以循环条件用 < 而不是 <=，是这里最常踩的一脚。",
                    "hits[3] compiles fine (length is only known at runtime) and throws IndexOutOfRangeException the moment it runs. Using < instead of <= in loops is the classic way to avoid it."),
                DS(H(4), H(3),
                    "顺手分清两个名字：数组用 Length（属性、只读，想给它赋值直接编译不过），List 用 Count。两者都是 O(1) 读，不会每次都数一遍，所以放在循环条件里放心用。",
                    "Keep two names apart: arrays use Length (a read-only property - assigning to it will not even compile), List uses Count. Both read in O(1) and never recount, so either is safe in a loop condition."),
            },
            "合法下标 0 ~ Length-1；Length 是属性，不是方法。",
            "Valid indexes are 0..Length-1, and Length is a property, not a method.");

        // ---- Task 26 while(true) + break：唯一的出口就是 break，得画成一条回头路 ----
        list[25].Dia = DSC(
            new[]
            {
                DN("while (true)", 0.20f, 0.14f, DiaTone.Accent).Size(0.18f),
                DN("tick++", 0.20f, 0.46f, DiaTone.Panel).Size(0.14f),
                DN("tick >= 3 ?", 0.20f, 0.80f, DiaTone.Purple).Size(0.20f),
                DN("break → 跳出", 0.60f, 0.80f, DiaTone.Alert).En("break -> exit").Size(0.20f),
                DN("再来一圈", 0.58f, 0.46f, DiaTone.Panel).En("another round").Size(0.18f),
                DN("while 之后的代码", 0.86f, 0.80f, DiaTone.Value).En("code after the while").Size(0.24f)
            },
            new[]
            {
                DE(0, 1),
                DE(1, 2),
                DE(2, 3).Lb("是", "yes"),
                DE(2, 4).Lb("否", "no"),
                DE(4, 0).Lb("条件还是真", "still true"),
                DE(3, 5)
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "while (true) 的条件永远成立 —— 这个循环不会自己停下来，只能从里面出去。所以每一圈必须让某个状态往前走（tick++），否则就是死循环。",
                    "while (true) is always true: the loop never stops on its own and can only be left from inside. Each round must move some state forward (tick++), or it spins forever."),
                DS(H(2, 4), H(1, 3, 4),
                    "tick 是 1、2 时 tick >= 3 不成立 → 走「否」这条路，回到条件再来一圈。游戏里「每帧检查一次」就是这个形状。",
                    "While tick is 1 or 2 the check fails, so control takes the 'no' path and comes back for another round. The classic per-frame check has exactly this shape."),
                DS(H(3, 5), H(2, 5),
                    "到第 3 圈 tick == 3 成立 → break 立刻跳出整个 while，去做后面的代码。break 只跳一层；只想结束这一圈、接着下一圈，要用 continue。",
                    "On the third round the check holds and break exits the whole while at once. break leaves one loop only; to end just this round and continue the next, use continue."),
                DS(H(0), H(0),
                    "为什么敢写「永远为真」？因为停下来的条件不在头上，而在体内：只要有一条 break 一定能被走到，这个循环就是有限的。判断标准很实在 —— 每圈有没有让某个变量单调推进，那个变量有没有机会越过阈值。两条缺一条，while(true) 就是定时炸弹。",
                    "Why dare write a condition that is always true? Because the stopping rule lives inside, not on the header: the loop is finite only if some break is guaranteed to be reached. The test is concrete - does each round move a variable monotonically, and can that variable pass the threshold? Miss either and while(true) is a time bomb."),
                DS(H(3, 5), H(3, 4, 5),
                    "break 之后 tick 停在触发那一圈的值（这里是 3）：既不会回到 0，也不会再加一。循环变量就是普通变量，循环结束不等于变量复位 —— 想拿它做后续判断，break 之后直接读就行；反过来，忘了复位就会带着上一轮的残留进下一个循环。",
                    "After break, tick keeps the value from the round that fired (3 here): it does not reset to 0 and does not advance again. Loop variables are ordinary variables, so ending the loop resets nothing. Read tick right after break if you need it, and remember a later loop inherits that leftover."),
            },
            "while(true) 靠内部 break 出口；忘了 break 就是死循环。",
            "while(true) leaves only through break inside; forget it and you spin forever.");

        // ---- Task 27 类型转换：三条路各自的结果与失败形态 ----
        list[26].Dia = DSC(
            new[]
            {
                DN("\"42\" 是文本不是数", 0.2f, 0.12f, DiaTone.Purple).En("\"42\" is text, not a number").Size(0.26f),
                DN("int.Parse", 0.1f, 0.46f, DiaTone.Accent).Size(0.16f),
                DN("= 42", 0.1f, 0.8f, DiaTone.Value).En("-> 42").Size(0.14f),
                DN("int.TryParse", 0.44f, 0.46f, DiaTone.Accent).Size(0.18f),
                DN("true + out 42", 0.66f, 0.8f, DiaTone.Value).En("true + out 42").Size(0.20f),
                DN("false + out 0（不抛）", 0.38f, 0.8f, DiaTone.Alert).En("false + out 0, no throw").Size(0.24f),
                DN("(int)2.9f → 2", 0.86f, 0.46f, DiaTone.Panel).Size(0.18f)
            },
            new[]
            {
                DE(0, 1),
                DE(1, 2),
                DE(0, 3),
                DE(3, 4).Lb("成功", "ok"),
                DE(3, 5).Lb("失败也不崩", "no crash"),
                DE(1, 5).Lb("非数字 → 抛异常", "throws").Dash(),
                DE(0, 6).Lb("(int) 不行", "(int) won't").Dash()
            },
            new[]
            {
                DS(H(0), null,
                    "「42」在内存里是两个字符 '4'、'2'，不是数值 42。想拿它做算术，必须先转成 int —— 这就是 Parse 存在的原因。",
                    "In memory \"42\" is two characters, '4' and '2', not the number 42. To do arithmetic you must convert it first - that is why Parse exists."),
                DS(H(1, 2), H(0, 1),
                    "int.Parse 只有两种结局：成功给值，失败抛 FormatException（给「4x2」或空串就抛），程序当场中断。所以玩家输入不要直接 Parse。",
                    "int.Parse has exactly two outcomes: a value, or a FormatException on \"4x2\" or an empty string - and the game stops right there. Never Parse raw player input."),
                DS(H(3, 4, 5), H(2, 3, 4),
                    "TryParse 用返回值告诉你成不成，成功能才把值写进 out 参数；失败返回 false、out 给 0，绝不抛异常。玩家输入走这条最稳。",
                    "TryParse reports success through its return value and only writes the out parameter on success; on failure it returns false, leaves 0 and never throws. The safe path for player input."),
                DS(H(6), H(6),
                    "(int) 是另一件事：它只在数值之间硬砍小数，2.9f 变 2（不四舍五入）；对字符串用 (int) 连编译都过不去。反过来数值转文本用 ToString(\"F1\") 这种格式符。",
                    "(int) is different: it chops decimals off a number, 2.9f becomes 2 without rounding; on a string it does not even compile. Going the other way needs ToString(\"F1\") style format specifiers."),
                DS(H(6), H(6),
                    "两个易错点：(int) 与 Convert.ToInt32 在小数上给出不同答案 —— (int)2.9f 直接截成 2，Convert 会四舍五入到 3；另外宽转窄（把 long 塞进 int）会静默截掉高位、不报错，想让它抛异常得用 checked 包起来。",
                    "Two traps: (int) and Convert.ToInt32 disagree on fractions - (int)2.9f truncates to 2 while Convert rounds to 3; and narrowing (a long into an int) silently drops the high bits without complaint, so wrap it in checked when you want it to throw."),
            },
            "文本 ↔ 数值走 Parse / TryParse / ToString；(int) 只在数值之间砍小数。",
            "Text <-> number needs Parse / TryParse / ToString; (int) only chops decimals off a number.");

        // ---- Task 28 try/catch/finally：执行顺序 + 没人接会怎样 ----
        list[27].Dia = DSC(
            new[]
            {
                DN("try { 可能出错的行 }", 0.14f, 0.12f, DiaTone.Accent).En("try { risky lines }").Size(0.28f),
                DN("抛异常：对 null 取成员", 0.5f, 0.46f, DiaTone.Alert).En("throws: member on null").Size(0.28f),
                DN("catch 按类型接住", 0.84f, 0.8f, DiaTone.Purple).En("catch by type").Size(0.24f),
                DN("finally 一定执行", 0.46f, 0.8f, DiaTone.Value).En("finally always runs").Size(0.22f),
                DN("继续往下走", 0.14f, 0.8f, DiaTone.Panel).En("carry on").Size(0.18f),
                DN("没人接 → 崩在栈顶", 0.84f, 0.46f, DiaTone.Alert).En("uncaught -> crash").Size(0.22f)
            },
            new[]
            {
                DE(0, 1).Lb("某行出错", "a line throws"),
                DE(1, 2).Lb("类型匹配就接住", "type matches"),
                DE(2, 3).Lb("无论成败都走", "always"),
                DE(3, 4),
                DE(1, 5).Lb("没有匹配的 catch", "no match").Dash(),
                DE(0, 3).Lb("没出错也走", "even with no error").Dash()
            },
            new[]
            {
                DS(H(0), null,
                    "try 里正常一行行往下跑；一旦某行抛异常，try 里剩下的行全部不执行，控制权立刻跳出去找 catch。",
                    "Inside try the lines run in order; the moment one throws, every remaining line in try is skipped and control jumps out looking for a catch."),
                DS(H(1, 2), H(0, 1),
                    "最常见的异常是空引用：对 null 取 .Length 或调方法就抛 NullReferenceException。catch 按异常类型接，接住之后程序不崩，继续往下。",
                    "The commonest is the null reference: reading .Length or calling a method on null throws NullReferenceException. A matching catch keeps the program alive."),
                DS(H(3), H(2, 5),
                    "finally 无论有没有出错、有没有被接住，都一定执行 —— 关文件、清计数、还原状态放这儿最可靠；try 里 return 了也照样先走 finally。",
                    "finally runs no matter what - with or without an error, caught or not. Put file closing, counters and state restore here; even a return inside try runs finally first."),
                DS(H(4, 5), H(3, 4),
                    "没有 catch 能匹配，异常就沿调用栈一路往外冒，最外层也没人接 → 程序崩。所以 catch 不是保险箱：类型写错了照样崩。",
                    "With no matching catch the exception bubbles up the call stack, and if nobody at all catches it the game crashes. catch is not a safety box: a wrong type catches nothing."),
                DS(H(3), H(2, 3),
                    "几条经验：catch 里不要空着，至少 Debug.Log 把堆栈留下来，否则 bug 从此消失；尽量只 catch 你确实能处理的具体类型，catch (Exception) 一把抓会把真正的 bug 一起吞掉；还有 finally 里写 return 会丢掉正在往外传的异常。",
                    "A few rules: never leave a catch body empty - log the stack or the bug vanishes; catch the specific type you can actually handle, because catch (Exception) swallows real bugs too; and a return inside finally discards the exception currently propagating."),
            },
            "顺序固定：try →（出错才）catch → finally 必走。",
            "The order is fixed: try, then catch only on error, then finally always.");

        // ---- Task 29 Dictionary：算哈希直接跳桶，这是它比 List 快的全部原因 ----
        list[28].Dia = DSC(
            new[]
            {
                DN("key \"rifle\"", 0.12f, 0.12f, DiaTone.Purple).Size(0.18f),
                DN("桶 3", 0.48f, 0.12f, DiaTone.Purple).En("bucket 3").Size(0.16f),
                DN("value 25", 0.82f, 0.12f, DiaTone.Value).Size(0.16f),
                DN("key \"sniper\"", 0.12f, 0.52f, DiaTone.Purple).Size(0.18f),
                DN("桶 7", 0.48f, 0.52f, DiaTone.Purple).En("bucket 7").Size(0.16f),
                DN("value 60", 0.82f, 0.52f, DiaTone.Value).Size(0.16f),
                DN("TryGetValue / ContainsKey", 0.40f, 0.86f, DiaTone.Panel).En("TryGetValue / ContainsKey").Size(0.30f)
            },
            new[]
            {
                DE(0, 1).Lb("算哈希 → 直接跳桶", "hash -> jump to bucket"),
                DE(1, 2).Lb("取出 value", "read the value"),
                DE(3, 4).Lb("算哈希 → 直接跳桶", "hash -> jump to bucket"),
                DE(4, 5).Lb("取出 value", "read the value"),
                DE(4, 6).Lb("查不到 → false / 抛异常", "missing -> false or throw").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "字典不是从头比字符串：先把 key 算成哈希值，直接跳到对应的桶。所以里面存 10 条还是 10 万条，找一次都只要一两步。",
                    "A dictionary does not scan keys: it hashes the key and jumps straight to a bucket. Ten entries or a hundred thousand, a lookup still takes a step or two."),
                DS(H(1, 2), H(1),
                    "桶里存的是一对 (key, value)。取的时候还要拿原 key 再比一次 —— 防的是哈希碰撞：两个不同的 key 算出同一个桶，光靠哈希分不开。",
                    "A bucket holds a (key, value) pair, and the real key is compared once more on read - that guards against collisions, where two keys hash to the same bucket."),
                DS(H(3, 4, 5), H(2, 3),
                    "换个 key「sniper」落到另一个桶，还是「算哈希 → 跳过去」。这就是 Dictionary 比 List 逐个查找快的全部秘密，也是它用键取值、不靠位置的道理。",
                    "A different key lands in a different bucket by the same hash-and-jump. That is the whole reason a Dictionary beats scanning a List - it addresses by key, not by position."),
                DS(H(6), H(4),
                    "取不存在的 key：hits[\"xx\"] 会抛 KeyNotFoundException；TryGetValue 返回 false，ContainsKey 先问一句。所以「可能有也可能没有」时用后者。",
                    "On a missing key hits[\"xx\"] throws KeyNotFoundException, while TryGetValue returns false and ContainsKey asks first. Use those when the key may or may not exist."),
                DS(H(0, 1), H(0, 2),
                    "使用上的三条约束：key 不能是 null（直接抛 ArgumentNullException）；key 放进字典后就别再改它参与哈希的字段，否则下次按原 key 找不到；遍历时不能增删（抛 InvalidOperationException），要边遍历边删就先 ToList 复制一份 key。",
                    "Three constraints in practice: a null key throws ArgumentNullException; never mutate the parts of a key that feed its hash after inserting, or the entry becomes unreachable; and no add or remove while iterating (InvalidOperationException) - copy the keys with ToList first."),
            },
            "Dictionary = 算哈希直接定位桶，桶里成对存 key 与 value。",
            "A Dictionary hashes the key to jump to a bucket holding a key/value pair.");

        // ---- Task 30 enum：名字只活在代码里，内存里始终是整数 ----
        list[29].Dia = DSC(
            new[]
            {
                DN("enum TowerState", 0.20f, 0.10f, DiaTone.Purple).En("enum TowerState").Size(0.26f),
                DN("Idle = 0", 0.12f, 0.40f, DiaTone.Value).Size(0.16f),
                DN("Scan = 1", 0.40f, 0.40f, DiaTone.Value).Size(0.16f),
                DN("Fire = 2", 0.68f, 0.40f, DiaTone.Value).Size(0.16f),
                DN("state = Fire", 0.16f, 0.72f, DiaTone.Accent).En("state = Fire").Size(0.22f),
                DN("里面存的是 2", 0.46f, 0.72f, DiaTone.Purple).En("stores 2").Size(0.20f),
                DN("switch 比整数", 0.76f, 0.72f, DiaTone.Accent).En("switch compares ints").Size(0.22f)
            },
            new[]
            {
                DE(0, 1), DE(0, 2), DE(0, 3),
                DE(3, 4).Lb("赋值", "assign"),
                DE(4, 5),
                DE(5, 6)
            },
            new[]
            {
                DS(H(0, 1, 2, 3), H(0, 1, 2),
                    "enum 就是给整数起名字：不写 = 数字就从 0 开始依次编号。也可以显式写 0 / 1 / 2，因为存档里存的就是这些数字 —— 顺序一改，旧档就串味。",
                    "An enum just names integers: without an = the numbering starts at 0. Writing 0 / 1 / 2 explicitly matters because a save file stores those numbers - reorder them and old saves mean something else."),
                DS(H(4, 5), H(3, 4),
                    "变量 state 只占 4 字节，里面是 2，不是字符串「Fire」。名字只活在代码里，编译完就没了，运行时没有任何开销。",
                    "The variable takes 4 bytes and holds 2, not the text \"Fire\". Names live in the source only and vanish at compile time, so there is no runtime cost."),
                DS(H(6), H(5),
                    "所以 switch 比的是整数，快；而且编译器能查错：写成 TowerState.Fir 直接编译不过。这就是用 enum 代替魔法数字 0 / 1 / 2 的全部理由。",
                    "That is why switch compares integers and stays fast - and why a typo like TowerState.Fir fails to compile. This is the whole case for enums over magic numbers."),
                DS(H(0, 4), H(0, 4),
                    "把 2 直接写在代码里的代价是慢慢显形的：日志里冒出一个 2 无从解释；想加一个 Paused 状态，得先决定它占几，散落各处的 0/1/2 全靠人肉同步；值一调整，旧存档和历史日志全部错位。换成 enum，名字和数字绑在一起，IDE 能补全，改值只改一处。",
                    "Hard-coding a 2 costs you slowly: a 2 in a log explains nothing; adding Paused means inventing its number while every scattered 0/1/2 is kept in sync by hand; shift a value and old saves and logs silently mean something else. An enum binds name to number, offers completion, and changes in one place."),
                DS(H(1, 2, 3), H(1, 2),
                    "还有一处容易忽略：决定身份的是值，不是名字。给两个成员写同一个值（Idle = 0、Sleep = 0），比较时完全等价；switch 里出现两个这样的 case 会直接报「常量值重复」；ToString() 也只会打印其中一个名字，另一个被盖住。显式编号时，唯一性得自己保证。",
                    "One thing easily missed: values decide identity, not names. Give two members the same value (Idle = 0, Sleep = 0) and they compare as equal; a switch with both cases fails to build as a duplicate constant; and ToString() prints only one of the names. With explicit numbering, uniqueness is yours to keep."),
            },
            "enum = 有名字的整数；变量里存的是数字，不是名字。",
            "An enum is a named integer: the variable stores the number, never the name.");

        // ---- Task 31 类进阶：new 的三步走，加上 static 与属性的位置 ----
        list[30].Dia = DSC(
            new[]
            {
                DN("new Dart(2.5f)", 0.15f, 0.12f, DiaTone.Accent).Size(0.20f),
                DN("堆上造一个对象", 0.44f, 0.12f, DiaTone.Purple).En("allocate on the heap").Size(0.22f),
                DN("构造函数跑一遍", 0.44f, 0.46f, DiaTone.Accent).En("constructor runs").Size(0.22f),
                DN("speed = 2.5", 0.15f, 0.80f, DiaTone.Value).Size(0.16f),
                DN("hits = 0", 0.44f, 0.80f, DiaTone.Value).Size(0.14f),
                DN("static Total += 1", 0.74f, 0.80f, DiaTone.Value).Size(0.22f),
                DN("引用交给 dart", 0.76f, 0.12f, DiaTone.Purple).En("reference handed back").Size(0.22f),
                DN("属性 Speed 包着字段", 0.76f, 0.46f, DiaTone.Purple).En("property wraps the field").Size(0.24f)
            },
            new[]
            {
                DE(0, 1),
                DE(1, 2),
                DE(2, 3),
                DE(2, 4),
                DE(2, 5),
                DE(1, 6),
                DE(2, 7)
            },
            new[]
            {
                DS(H(0, 1, 6), H(0, 1, 5),
                    "new 干三件事：在堆上要一块地造对象、跑构造函数、把地址交回给变量。所以 dart 里存的不是数据本身，是一个引用（地址）。",
                    "new does three things: allocate the object on the heap, run its constructor, hand the address back. So dart holds a reference, not the data itself."),
                DS(H(2, 3, 4), H(1, 2, 3),
                    "构造函数把每个新对象的字段初始化：speed 用参数 2.5，hits 从 0 起。没写构造函数，编译器会送你一个空的 —— 字段就是默认值。",
                    "The constructor initialises the fields of this new object: speed from the argument, hits from 0. With no constructor the compiler supplies an empty one and fields keep their defaults."),
                DS(H(5), H(4),
                    "static 字段不属于某一个对象，所有 Dart 共用一份 —— 每 new 一次 Total 加 1，正好用来数「一共造了几个」。它不能读某个对象的字段。",
                    "A static field belongs to no single object: all Darts share one copy, so each new bumps Total by 1 and it counts how many exist. It cannot read any object's fields."),
                DS(H(7), H(6),
                    "属性 Speed 不是多出来的存储：它只是包着 speed 字段的 get / set 大门，可以在里面加校验。写 dart.Speed 读的仍是这个对象的那块内存。",
                    "The property Speed stores nothing extra: it is a get/set gate over the speed field where you can add checks. Reading dart.Speed still touches this object's own memory."),
                DS(H(0, 1, 6), H(0, 5, 6),
                    "还有两点：没 new 过的引用变量里是 null，访问它任何成员都抛 NullReferenceException，这是 C# 最常见的一类崩溃；反过来，对象没人引用了也不会立刻消失，等 GC 来收 —— 所以别在每帧里 new 对象，那是在给 GC 造垃圾。",
                    "Two more things: a reference that was never new'd holds null, and touching any member throws NullReferenceException - the commonest crash in C#. Conversely an unreferenced object does not vanish at once; the GC reclaims it later, so do not new per frame and feed the GC."),
            },
            "new = 堆上造对象 + 跑构造函数 + 交出引用；static 全类共用一份。",
            "new = allocate, construct, return a reference; static is shared by all instances.");

        // ---- Task 32 方法进阶：重载怎么挑、可选参数在哪填、out 与 ref 的区别 ----
        list[31].Dia = DSC(
            new[]
            {
                DN("Pow2(3)", 0.18f, 0.10f, DiaTone.Accent).Size(0.16f),
                DN("Pow2(int)", 0.10f, 0.44f, DiaTone.Purple).Size(0.16f),
                DN("Pow2(float)", 0.38f, 0.44f, DiaTone.Purple).Size(0.18f),
                DN("实际走 Pow2(int)", 0.18f, 0.78f, DiaTone.Value).En("Pow2(int) wins").Size(0.22f),
                DN("Add(1)", 0.64f, 0.10f, DiaTone.Accent).Size(0.14f),
                DN("Add(a, b = 10)", 0.64f, 0.44f, DiaTone.Purple).Size(0.20f),
                DN("等价 Add(1, 10)", 0.64f, 0.78f, DiaTone.Value).En("same as Add(1, 10)").Size(0.20f),
                DN("out 带出值 / ref 改原变量", 0.90f, 0.44f, DiaTone.Panel).En("out returns, ref edits").Size(0.22f)
            },
            new[]
            {
                DE(0, 1).Lb("int 精确匹配", "exact int"),
                DE(0, 2).Lb("要转 float，次选", "needs a cast").Dash(),
                DE(1, 3),
                DE(4, 5),
                DE(5, 6).Lb("默认值编译期就填好", "default filled at compile time"),
                DE(5, 7)
            },
            new[]
            {
                DS(H(0, 1, 2), H(0, 1),
                    "重载只差参数表：个数、类型、顺序。编译器按「最不需要隐式转换」的那个挑，所以 int 实参命中 Pow2(int)，而不是先转成 float 再进。",
                    "Overloads differ only in their parameter list: count, types, order. The compiler picks the one needing the fewest conversions, so an int argument hits Pow2(int) rather than being widened to float."),
                DS(H(3), H(2),
                    "挑中之后就在编译期定死在那个方法上，跟运行时无关。想走 float 版必须自己写 3f —— 这也是重载不会带来运行开销的原因。",
                    "Once chosen, the call is bound to that method at compile time, not at runtime. Writing 3f is the only way to reach the float version - which is why overloads cost nothing at runtime."),
                DS(H(4, 5, 6), H(3, 4),
                    "可选参数在编译期就把默认值填进去：Add(1) 实际就是 Add(1, 10)。所以默认值只能从参数表最右边开始省，跳过中间那个是不行的。",
                    "Optional parameters get their defaults filled in at compile time: Add(1) really is Add(1, 10). That is why you can only omit from the right end - skipping a middle one is not allowed."),
                DS(H(7), H(5),
                    "out 是「方法带出来的第二个返回值」：进去前不必有值，但方法必须给它赋值；ref 是「把你那个变量本身交进去」，方法改的就是你手里那一个。",
                    "out is a second return value: nothing needs to be there on the way in, but the method must assign it. ref hands over your actual variable, so the method edits the one you hold."),
                DS(H(5, 6), H(5),
                    "一个真实隐患：默认值是在调用方编译时就写死的。库里的 b = 10 后来改成 20，只重编库、不重编调用方，调用方那行 Add(1) 仍然传 10 —— 这种 bug 极难查。所以默认值适合短期便利，长期对外的方法改用重载或显式传值更稳。",
                    "A real hazard: the default is baked into the caller at compile time. Change b = 10 to 20 in a library, rebuild only the library, and Add(1) in the caller still passes 10. That bug is brutal to find, so defaults suit convenience while long-lived APIs should use overloads or explicit arguments."),
            },
            "重载按参数表挑最匹配的那个；可选参数填默认；out 带出值，ref 改原变量。",
            "Overloads resolve to the best match; optional args fill defaults; out returns extra, ref edits yours.");

        // ---- Task 33 多维数组：矩形网格 vs 交错数组，形状差别一眼可见 ----
        list[32].Dia = DSC(
            new[]
            {
                DN("int[,] map = new int[2,3]", 0.36f, 0.10f, DiaTone.Purple).En("int[,] map = new int[2,3]").Size(0.32f),
                DN("map[0,0]", 0.12f, 0.42f, DiaTone.Value).Size(0.14f),
                DN("map[0,1]", 0.36f, 0.42f, DiaTone.Value).Size(0.14f),
                DN("map[0,2]", 0.60f, 0.42f, DiaTone.Value).Size(0.14f),
                DN("map[1,0]", 0.12f, 0.68f, DiaTone.Value).Size(0.14f),
                DN("map[1,1]", 0.36f, 0.68f, DiaTone.Value).Size(0.14f),
                DN("map[1,2]", 0.60f, 0.68f, DiaTone.Value).Size(0.14f),
                DN("int[][] jag", 0.87f, 0.42f, DiaTone.Purple).En("int[][] jag").Size(0.18f),
                DN("jag[0] 两个元素", 0.87f, 0.68f, DiaTone.Value).En("jag[0] holds 2").Size(0.20f),
                DN("jag[1] 四个元素", 0.87f, 0.90f, DiaTone.Value).En("jag[1] holds 4").Size(0.20f)
            },
            new[]
            {
                DE(0, 1), DE(0, 2), DE(0, 3), DE(0, 4), DE(0, 5), DE(0, 6),
                DE(7, 8), DE(7, 9),
                DE(0, 7).Lb("对照：一个是矩形", "rectangle vs jagged").Dash().NoArrow()
            },
            new[]
            {
                DS(H(0), null,
                    "int[,] 是一整块连续内存，行数 × 列数在 new 的那一刻就定死：这里 2 行 3 列 = 6 格。Length 是总格数 6；行列数要用 GetLength(0) = 2、GetLength(1) = 3。",
                    "int[,] is one contiguous block whose rows and columns are fixed at new: 2 by 3 here means 6 boxes. Length is the total 6; rows and columns come from GetLength(0) and GetLength(1)."),
                DS(H(1, 2, 3, 4, 5, 6), H(0, 1, 2, 3, 4, 5),
                    "访问必须给两个下标：map[行, 列]，都从 0 开始。嵌套循环的 r、c 正好对上这两层 —— r 走到 GetLength(0)，c 走到 GetLength(1)。",
                    "Access takes two indexes - map[row, column], both from 0. The nested loops' r and c line up with those two levels: r against GetLength(0), c against GetLength(1)."),
                DS(H(7, 8, 9), H(6, 7, 8),
                    "交错数组 int[][] 是「数组的数组」：外层只有几个槽，每个槽各自指一个长度可以不同的内层数组 —— 所以能画三角形，而 int[,] 永远是个矩形。",
                    "int[][] is an array of arrays: the outer one holds a few slots, each pointing at an inner array whose length can differ - so you can build a triangle, while int[,] is always a rectangle."),
                DS(H(1, 2, 3, 4, 5, 6), H(0, 1, 2, 3, 4, 5),
                    "这 6 格在内存里是连成一排的，按「第 0 行的三格，然后第 1 行的三格」摆放，也就是行优先。所以外层走行、内层走列刚好顺着内存扫过去，缓存友好；把行列循环写反，每次访问都要来回跳，同样的循环体能慢好几倍。",
                    "Those 6 slots sit in one continuous run ordered row by row: row 0's three, then row 1's three - row-major. Looping rows outside and columns inside therefore walks memory in order and stays cache friendly; swapping the loops makes every access jump, easily several times slower for identical work."),
                DS(H(7, 8, 9), H(6, 7, 8),
                    "两者不能互换：int[][] 和 int[,] 是不同的类型，赋值、传参都不通用。几个数字也要分清 —— int[,] 的 Length 是总格数 6，int[][] 的 Length 只是外层行数 2；而且 new int[2][] 建好时每个 jag[i] 还是 null，必须再 new 一次内层，否则 jag[0][0] 直接空引用。",
                    "The two are not interchangeable: int[][] and int[,] are different types, so neither assignment nor parameter passing crosses over. Mind the counts too - Length on int[,] is the total 6, while Length on int[][] is just 2 rows; and new int[2][] leaves every jag[i] null, so each inner array needs its own new or jag[0][0] throws a null reference."),
            },
            "int[,] 是矩形、连续、尺寸固定；int[][] 的每个子数组长度自由。",
            "int[,] is a fixed contiguous rectangle; int[][] lets every inner array differ in length.");

        // ---- Task 34 索引器：box[1] 其实是一次带参数的属性调用 ----
        list[33].Dia = DSC(
            new[]
            {
                DN("Box box = new Box(3)", 0.18f, 0.12f, DiaTone.Accent).En("Box box = new Box(3)").Size(0.26f),
                DN("对象里藏着 int[] data", 0.18f, 0.48f, DiaTone.Purple).En("the object holds int[] data").Size(0.26f),
                DN("box[1]", 0.18f, 0.84f, DiaTone.Accent).Size(0.14f),
                DN("this[int i] 索引器", 0.56f, 0.48f, DiaTone.Purple).En("this[int i] indexer").Size(0.22f),
                DN("get → data[1] 得 7", 0.56f, 0.84f, DiaTone.Value).En("get -> data[1] = 7").Size(0.22f),
                DN("set → data[1] = 9", 0.88f, 0.84f, DiaTone.Value).En("set -> data[1] = 9").Size(0.20f)
            },
            new[]
            {
                DE(0, 1),
                DE(2, 3).Lb("编译器翻成 this[1]", "compiled to this[1]"),
                DE(3, 4).Lb("等号右边走 get", "get on the right"),
                DE(3, 5).Lb("等号左边走 set", "set on the left"),
                DE(1, 3).Lb("背后就是它", "backed by it").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "索引器没有魔法：类里照样先放一个普通数组 data，对象持有它。所谓「像数组一样用」，只是给这个数组套了个壳。",
                    "There is no magic: the class still holds an ordinary array, data. Using an object like an array is just a shell around it."),
                DS(H(2, 3), H(1),
                    "你写 box[1]，编译器把它翻成调用 box.this[1] —— 也就是那个 this[int i] 成员。它和普通属性一样，只是名字换成了方括号加参数。",
                    "When you write box[1] the compiler calls box.this[1], the this[int i] member. It behaves like a normal property whose name is brackets plus a parameter."),
                DS(H(3, 4), H(2),
                    "放在等号右边就走 get：读 data[1]，于是 box[1] 得到 7。取值这一段和读普通属性没有任何区别。",
                    "On the right of an = it goes through get: data[1] is read and box[1] yields 7. That read is indistinguishable from reading a plain property."),
                DS(H(5), H(3),
                    "放在等号左边走 set：data[1] 被改成 9。外部看到的是「这个对象能当下标用」，内部怎么存、范围怎么查，完全由你决定。",
                    "On the left of an = it goes through set: data[1] becomes 9. Callers see an object you can index; how it stores and range-checks is entirely up to you."),
                DS(H(3), H(3),
                    "几个延伸：索引器的参数不限于 int，字典风格 this[string key] 很常见，也可以带多个参数（this[int r, int c]）；它不能是 static，因为必须有「哪个对象」；内部一定要自己查范围，否则越界异常会从 data[i] 里冒出来，报错位置离调用处很远。",
                    "Worth extending: the parameter need not be an int - this[string key] is common - and it can take several (this[int r, int c]); it cannot be static because there must be an instance; and do your own bounds check, or the range error surfaces from data[i], far from the call site."),
            },
            "索引器 = 带参数的属性，box[i] 就是编译成 this[i]。",
            "An indexer is a property with parameters: box[i] compiles to this[i].");

        // ================= 回头重做：任务 1~19 里第三页不合格的几项 =================
        // 判据：与新增任务（20~49）形式雷同、或整页只是静态文字堆叠、看完看不到「过程」的，改画逻辑图。
        // 重做清单：任务 1（注释）、任务 6（if / else if / switch）、任务 7（for）、任务 12（do-while）、
        //          任务 13（方法定义与调用）、任务 19（GlobalCode 全局区）。
        // 其余各内存页保留：它们讲的正是「几个字节、怎么排」，换成方框反而把信息丢了。

        // ---- 重做 Task 1 注释：原来三行虚线讲的是同一句「0 字节」，改成编译流程才看得见它在哪一步没的 ----
        list[0].Mem = null;
        list[0].Dia = DSC(
            new[]
            {
                DN("// 计算剩余血量", 0.24f, 0.12f, DiaTone.Accent).En("// remaining hp").Size(0.34f),
                DN("① 词法分析认出这是注释", 0.24f, 0.46f, DiaTone.Purple).En("1. the lexer marks it a comment").Size(0.34f),
                DN("② 整段丢掉，只剩一个换行", 0.24f, 0.84f, DiaTone.Alert).En("2. dropped, only the newline stays").Size(0.34f),
                DN("③ 编译产物里没有它", 0.62f, 0.84f, DiaTone.Value).En("3. absent from the compiled output").Size(0.26f),
                DN("运行时内存里也没有格子", 0.62f, 0.46f, DiaTone.Value).En("no box in memory at run time").Size(0.30f),
                DN("注释越多，机器开销仍是 0", 0.90f, 0.12f, DiaTone.Panel).En("more comments, still zero cost").Size(0.26f)
            },
            new[]
            {
                DE(0, 1).Lb("编译第一步", "first pass"),
                DE(1, 2).Lb("0 字节", "0 bytes"),
                DE(2, 3),
                DE(1, 4).Lb("所以内存里从不出现", "never reaches memory"),
                DE(0, 5).Lb("只有人和 /// 在读", "only humans and /// read it").Dash().NoArrow()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "编译器第一步是词法分析：把源码切成一个个记号。// 一出现，从这里到行尾整段被标成注释，后面不再当代码看。",
                    "The first pass is the lexer, cutting source into tokens. Once // appears, everything to the end of the line is marked as a comment and never read as code."),
                DS(H(2), H(1),
                    "注释在编译期就被删掉，只留一个换行给下一行定位。写一行和写一百行，编出来的程序一样大 —— 这件事编译期就定了，不是运行时省下来的。",
                    "Comments are removed at compile time, leaving only a newline for line mapping. One line or a hundred, the program comes out the same size: decided while compiling, not saved at run time."),
                DS(H(3, 4), H(2, 3),
                    "产物里没有它，运行时内存自然也不会给它划格子 —— 这就是「注释 0 字节」的真正原因。",
                    "Not in the output, so run-time memory never allocates a box for it: that is the real reason a comment costs 0 bytes."),
                DS(H(5), H(0),
                    "那为什么还要写？因为读代码的是人。/// 文档注释会被编进元数据，供编辑器悬停提示用 —— 它占的是元数据，不是运行时那几块内存。",
                    "So why write them? Because humans read code. /// doc comments go into metadata for editor tooltips: metadata, not run-time memory."),
                DS(H(0), H(0),
                    "顺手记住两个常见坑：// 只吃到行尾，想注释一整段得逐行加；/* */ 不能嵌套，写两层时内层的 */ 会把整段提前结束，后面那些行反而变成了代码。",
                    "Two traps worth remembering: // stops at the end of the line, so a whole block needs a // per line; /* */ does not nest, and the inner */ closes the block early, turning the rest back into code."),
            },
            "注释在编译第一步就被丢掉，所以 0 字节；/// 唯一例外是进元数据供悬停提示。",
            "A comment is dropped in the very first pass, so it costs 0 bytes; /// only lands in metadata for tooltips.");

        // ---- 重做 Task 6 if / else if / switch：原内存页 5 步里有 4 步都不改字节，
        //      该讲的是「从上往下问、命中一个就停」，这个只有流程图说得清 ----
        list[5].Mem = null;
        list[5].Dia = DSC(
            new[]
            {
                DN("hp = 2，hpMax = 3", 0.18f, 0.12f, DiaTone.Purple).En("hp = 2, hpMax = 3").Size(0.28f),
                DN("if (hp <= 0)", 0.18f, 0.46f, DiaTone.Accent).En("if (hp <= 0)").Size(0.22f),
                DN("else if (hp < hpMax && hpMax > 0)", 0.18f, 0.84f, DiaTone.Accent).En("else if (hp < hpMax && hpMax > 0)").Size(0.38f),
                DN("else 兜底", 0.56f, 0.46f, DiaTone.Panel).En("else fallback").Size(0.18f),
                DN("switch (hpMax) → case 3", 0.58f, 0.84f, DiaTone.Value).En("switch (hpMax) -> case 3").Size(0.30f),
                DN("default：一个 case 都不中才走", 0.86f, 0.46f, DiaTone.Alert).En("default: nothing matched").Size(0.26f),
                DN("自上而下，命中一个就不再问", 0.88f, 0.12f, DiaTone.Alert).En("top-down: one hit and it stops").Size(0.24f)
            },
            new[]
            {
                DE(0, 1),
                DE(1, 2).Lb("2 <= 0 假，接着问", "2 <= 0 is false, ask again"),
                DE(2, 4).Lb("真，进这一支", "true, take it"),
                DE(2, 3).Lb("也不中就落到 else", "neither -> else"),
                DE(4, 5),
                DE(1, 6).Lb("顺序就是优先级", "order is priority").Dash().NoArrow()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "先算 hp <= 0：2 <= 0 是假，这一支整块跳过。跳过的代价是零 —— 内存里一个字节都没动。",
                    "First hp <= 0: 2 <= 0 is false, so the whole branch is skipped. Skipping costs nothing - not a byte changes."),
                DS(H(2), H(1),
                    "接着问 else if：先算 hp < hpMax（2 < 3 真），再算 hpMax > 0（3 > 0 真），&& 两边都真才算真，所以今天走这一支。",
                    "Then the else if: hp < hpMax (2 < 3, true) and hpMax > 0 (3 > 0, true); && needs both, so this branch runs today."),
                DS(H(3), H(3),
                    "一旦有一支命中，后面的 else if / else 全都不再问。这就是为什么顺序有讲究：越窄、越特殊的情况越该写在前头。",
                    "Once one branch hits, the later else if / else chains are never asked. That is why order matters: narrow, special cases belong first."),
                DS(H(4, 5), H(4),
                    "switch 是另一套：它不判真假，而是拿 hpMax 的值去对 case 后面的常量，对上哪个就跳哪儿；一个都不对，才走 default。",
                    "switch works differently: it does not test truth, it matches hpMax against the constants after each case and jumps there; only when nothing matches does default run."),
                DS(H(4), H(2),
                    "两种写法都只走一条路。分支不是并行 —— 想让两个 case 干同一件事，得在 case 里 fall through，而不是指望它们同时执行。",
                    "Both forms take exactly one path. Branches are not parallel - to make two cases do the same thing you fall through in the case, you cannot run them at once.")
            },
            "if 链从上往下问、命中一个就停；switch 拿值去对常量。两种都只走一条路。",
            "An if chain asks top-down and stops at the first hit; switch matches a value against constants. Either way one path runs.");

        // ---- 重做 Task 7 for：原内存页只是「i 从 0 变到 2」，看不出四段的执行顺序与次数 ----
        list[6].Mem = null;
        list[6].Dia = DSC(
            new[]
            {
                DN("for (int i = 0; i < 3; i++)", 0.48f, 0.12f, DiaTone.Purple).En("for (int i = 0; i < 3; i++)").Size(0.40f),
                DN("① 初始化 i = 0（整场只一次）", 0.16f, 0.46f, DiaTone.Panel).En("1. init i = 0 (only once)").Size(0.32f),
                DN("② 判断 i < 3", 0.48f, 0.46f, DiaTone.Accent).En("2. test i < 3").Size(0.20f),
                DN("③ 循环体", 0.48f, 0.84f, DiaTone.Value).En("3. body").Size(0.16f),
                DN("④ i++ 后回到②", 0.16f, 0.84f, DiaTone.Accent).En("4. i++ then back to 2").Size(0.26f),
                DN("判断为假 → 出循环", 0.84f, 0.46f, DiaTone.Alert).En("false -> leave the loop").Size(0.26f)
            },
            new[]
            {
                DE(0, 1).Lb("先做一次", "done once"),
                DE(1, 2).Lb("顺序往下", "in order"),
                DE(2, 3).Lb("真", "true"),
                DE(3, 4),
                DE(4, 2).Lb("再判一次", "test again"),
                DE(2, 5).Lb("假", "false")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "for 的三段被拆成四件事。① 初始化只在进场时做一次：造出 i 并写 0，之后再也不初始化。",
                    "A for loop splits into four steps. Step 1, the initialiser, runs once on entry: create i and write 0; never again."),
                DS(H(2), H(1, 2),
                    "② 判断在每一轮开头都做一次。三段循环体会让这里执行 4 次判断（0、1、2 各进一次，最后 3 < 3 为假退出）—— 判断比循环体多一次，这是最容易数错的地方。",
                    "Step 2 tests at the top of every round. Three iterations mean four tests (0, 1, 2 enter, then 3 < 3 fails) - one more test than body runs, which is where people miscount."),
                DS(H(3, 4), H(3, 4),
                    "③ 进循环体干活，④ 走末尾的 i++，然后无条件回到②。③④ 是一对：每进一次体，就跟着一次 ++。",
                    "Step 3 runs the body, step 4 runs the trailing i++. They pair up: every body run is followed by exactly one ++."),
                DS(H(5), H(5),
                    "② 为假就掉出去，循环结束，i 停在不满足条件的那个值（这里是 3）。所以循环结束后 i 不等于 2 —— 它是 3。",
                    "When step 2 is false, control drops out and i holds the value that failed (3 here). After the loop i is 3, not 2."),
                DS(H(3, 4), H(3, 4),
                    "两个常见坑：循环体里别再手写 i++ —— 末尾那次还会再加一遍，等于一步跨两个元素，中间的被跳过；另外 i 是在 for 头里声明的，出了循环就不存在（编译直接报找不到），想把计数留到循环之后用，得把 i 声明在循环外面。",
                    "Two common slips: never hand-write i++ inside the body - the trailing one adds again, skipping every other element; and an i declared in the header disappears after the loop (it will not compile outside), so declare the counter outside when you need it afterwards."),
            },
            "初始化只一次；判断比循环体多一次；循环结束后 i 是失败的那个值。",
            "The initialiser runs once; there is one more test than body runs; afterwards i holds the value that failed.");

        // ---- 重做 Task 12 do-while：原来只有「d 被改成 1」三步，看不出它和 while 的根本差别 ----
        list[11].Mem = null;
        list[11].Dia = DSC(
            new[]
            {
                DN("do { d++; } while (d < 3);", 0.46f, 0.12f, DiaTone.Purple).En("do { d++; } while (d < 3);").Size(0.40f),
                DN("先无条件跑一次循环体", 0.18f, 0.46f, DiaTone.Accent).En("run the body first, no test").Size(0.30f),
                DN("再判断 d < 3", 0.18f, 0.84f, DiaTone.Accent).En("then test d < 3").Size(0.22f),
                DN("真 → 回头再跑", 0.54f, 0.84f, DiaTone.Value).En("true -> run again").Size(0.22f),
                DN("假 → 结束，但已经跑过至少一次", 0.54f, 0.46f, DiaTone.Value).En("false -> done, but it ran at least once").Size(0.32f),
                DN("对照 while：条件假就一次都不跑", 0.87f, 0.46f, DiaTone.Alert).En("while: a false test means zero runs").Size(0.24f),
                DN("条件后漏分号 → 编译不过", 0.87f, 0.84f, DiaTone.Alert).En("drop the semicolon and it will not build").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("先做再说", "act first"),
                DE(1, 2),
                DE(2, 3).Lb("真", "true"),
                DE(3, 1).Lb("再跑一遍", "run it again"),
                DE(2, 4).Lb("假", "false"),
                DE(4, 5).Lb("差别就在这", "the whole difference").Dash().NoArrow(),
                DE(2, 6).Lb("语法陷阱", "syntax trap").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "do-while 把顺序颠倒过来：进门先执行循环体，一次都不少。这就是它和 while 唯一但致命的区别。",
                    "do-while flips the order: it runs the body on entry, at least once. That is the only, and decisive, difference from while."),
                DS(H(2, 4), H(1, 2),
                    "执行完才判断 d < 3。为假也照样结束了，可循环体已经实打实跑过一次 —— 适合「至少要做一次」的场景，比如先取一次输入再问要不要继续。",
                    "Only after running does it test d < 3. A false result still ends the loop, but the body really ran once - which suits do-it-at-least-once work such as reading input before asking to continue."),
                DS(H(3), H(3),
                    "为真就跳回循环体开头接着跑，不再回到条件处判断 —— 因为判断本来就在末尾。",
                    "If true it jumps back to the top of the body, not to the test, because the test already lives at the end."),
                DS(H(5), H(5),
                    "换成 while (d < 3) 写法，条件假的时候连第一次都不会执行 —— 同一份逻辑，两种循环给出的次数可以差一次。",
                    "Written as while (d < 3), a false test means not even the first pass - the same logic can differ by one iteration between the two forms."),
                DS(H(6), H(6),
                    "还有个纯语法坑：while (条件) 后面必须跟分号，因为这条语句到此结束。漏了的话下一行会被当成循环体，报错位置离真正的问题很远。",
                    "There is a pure syntax trap too: the while (condition) must end with a semicolon because the statement ends there. Miss it and the next line becomes the body, with the error far from the real mistake.")
            },
            "do-while 先跑后判，至少执行一次；while 先判后跑，可能一次都不执行。",
            "do-while runs then tests, so at least once; while tests then runs, so possibly never.");

        // ---- 重做 Task 13 方法定义与调用：原来「定义不占内存」全靠一排虚线，太抽象了，
        //      改成栈帧进出示意图，能看见「调用才开帧、返回就销毁」 ----
        list[12].Mem = null;
        list[12].Dia = DSC(
            new[]
            {
                DN("void ShowHp(int hp) { … }", 0.20f, 0.12f, DiaTone.Purple).En("void ShowHp(int hp) { … }").Size(0.34f),
                DN("只是定义：一行数据内存都不占", 0.20f, 0.46f, DiaTone.Panel).En("just a definition: no data memory").Size(0.32f),
                DN("ShowHp(100) 被调用", 0.20f, 0.84f, DiaTone.Accent).En("ShowHp(100) is called").Size(0.26f),
                DN("栈上新开一层帧：hp = 100", 0.60f, 0.84f, DiaTone.Value).En("a new frame on the stack: hp = 100").Size(0.30f),
                DN("方法返回 → 整帧销毁", 0.60f, 0.46f, DiaTone.Alert).En("return -> the frame is destroyed").Size(0.30f),
                DN("调用者的变量还在（在外层）", 0.88f, 0.12f, DiaTone.Value).En("the caller's own variables survive").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("编译期只登记", "registered at compile time"),
                DE(1, 2).Lb("调用才发生", "only a call changes that"),
                DE(2, 3),
                DE(3, 4).Lb("返回即回收", "freed on return"),
                DE(4, 5).Lb("外层的栈不动", "the outer stack is untouched")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "写下一个方法只是「登记一份做法」，方法体不执行，参数 hp 也还不存在 —— 所以定义本身不占任何数据内存。",
                    "Writing a method only registers a recipe. The body does not run and the parameter hp does not exist yet - a definition costs no data memory."),
                DS(H(2, 3), H(1, 2),
                    "ShowHp(100) 一调用，栈上新压一层帧，帧里才有 hp 这一个 4 格的坑，值 100 被复制进来。参数是方法的本地变量，只在这个帧里活着。",
                    "The moment ShowHp(100) is called, a frame is pushed and only then does hp get its 4 bytes, with 100 copied in. Parameters are locals that live only inside that frame."),
                DS(H(4), H(3),
                    "方法一返回，整层帧连根拿走 —— hp 那 4 格跟着消失。所以出了方法再想读 hp 是拿不到的：它不是被清零，是根本不存在了。",
                    "On return the whole frame goes away, hp's 4 bytes with it. Reading hp afterwards is not possible: it is not zeroed, it simply no longer exists."),
                DS(H(5), H(4),
                    "调用者自己的变量在更外层的帧里，方法进出都不会碰它们。想让方法改到外面的值，才需要 ref / out 或返回值。",
                    "The caller's variables sit in an outer frame and survive the call either way. That is exactly why ref / out or a return value are needed to change them."),
                DS(H(3), H(0, 1),
                    "再往前一步：方法里再调方法，就在上面又压一层帧，一层层垒上去；垒得太深（比如忘了递归出口）会抛 StackOverflowException —— 它不是普通异常，catch 接不住，进程直接结束。",
                    "One step further: a call inside a call pushes another frame on top, and they stack up. Pile them too deep (a recursion without a base case) and you get StackOverflowException, which no catch can take - the process just dies."),
            },
            "定义不占数据内存；调用才压栈帧、参数才有格子；返回即销毁。",
            "A definition costs no data memory; a call pushes a frame where parameters get bytes; return destroys it.");

        // ---- 重做 Task 19 GlobalCode：原来三步全是「不改字节」的说明，看不到「编译前被搬到哪里」 ----
        list[18].Mem = null;
        list[18].Dia = DSC(
            new[]
            {
                DN("GlobalCode { int shots = 0; }", 0.28f, 0.12f, DiaTone.Purple).En("GlobalCode { int shots = 0; }").Size(0.36f),
                DN("编译前：整块被搬到类层级", 0.28f, 0.46f, DiaTone.Accent).En("before compiling, the block is pasted at class level").Size(0.34f),
                DN("变成字段 shots（对象里只有一份）", 0.28f, 0.84f, DiaTone.Value).En("it becomes the field shots (one copy per object)").Size(0.34f),
                DN("每帧 Run() 读的是同一份", 0.66f, 0.84f, DiaTone.Accent).En("every frame's Run() reads that same one").Size(0.30f),
                DN("第 1 帧写 1，第 2 帧读到 1 再写 2", 0.66f, 0.46f, DiaTone.Value).En("frame 1 writes 1, frame 2 reads 1 then writes 2").Size(0.34f),
                DN("点编译 = 重新初始化，全部归零", 0.90f, 0.12f, DiaTone.Alert).En("recompiling re-initialises it all to 0").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("你写的是一块区域", "you wrote a region"),
                DE(1, 2).Lb("生成的还是字段", "it really is a field"),
                DE(2, 3),
                DE(3, 4).Lb("上一帧的值留下来了", "last frame's value carries over"),
                DE(2, 5).Lb("只在本次运行内有效", "valid only for this run").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "GlobalCode 不是魔法：编译前，编辑器把它里面的代码原样搬到类层级，和你在类里手写的字段是同一个待遇。",
                    "GlobalCode is no magic: before compiling, the editor pastes its content at class level, exactly like a field you would type there."),
                DS(H(2), H(1, 2),
                    "所以 int shots = 0; 真的成了一个字段 —— 对象活着它就活着，不是每次 Run 都新建一个。这也是它能在帧与帧之间记住东西的原因。",
                    "So int shots = 0; really becomes a field: it lives as long as the object, not remade on every Run. That is why it can remember across frames."),
                DS(H(3, 4), H(3, 4),
                    "第 1 帧把它写成 1，第 2 帧进来读到的就是 1，再加 1 变 2。同一个对象、同一格内存，这就是「跨帧保存」的全部秘密。",
                    "Frame 1 writes 1, frame 2 reads 1 and bumps it to 2. Same object, same bytes - that is the whole secret of surviving a frame."),
                DS(H(5), H(5),
                    "代价是它只活在这次运行里：点一下编译，脚本重新加载，字段全部回到初始值 0。想跨运行保存，要用存档 / PlayerPrefs 之类，而不是靠字段。",
                    "The price is that it lives only for this run: hit compile, scripts reload, and every field is back to 0. Persisting across runs needs saves or PlayerPrefs, not a field."),
                DS(H(2), H(1, 2),
                    "一个常被误会的点：GlobalCode 是「每个对象各一份」，不是全场共享。想让所有炮台共用一份计数，得把字段写成 static；static 也是每次运行从初始值开始，只是它在所有实例之间是同一份。",
                    "A common misreading: GlobalCode gives one copy per object, not one per game. Sharing a counter across all towers needs static, which still starts from its initial value every run but is a single copy across instances."),
            },
            "GlobalCode 被搬成类字段，因此跨帧保留、只活在本次运行内，点编译就归零。",
            "GlobalCode is pasted in as class fields, so it survives frames, lives only for this run, and resets on compile.");

        // ================= 第二批：任务 35~49 的第三页 =================
        // 形式按知识点挑：关系 / 控制流 / 时序 / 映射用逻辑图；只有「值就摆在几格里」的（元组）继续用内存页。

        // ---- Task 35 接口与多态：接口只写「要有什么」，实现类各写各的，调用处只看接口 ----
        list[34].Dia = DSC(
            new[]
            {
                DN("interface IWeapon", 0.5f, 0.12f, DiaTone.Purple).En("interface IWeapon").Size(0.28f),
                DN("void Fire();  只有签名", 0.14f, 0.46f, DiaTone.Purple).En("void Fire(); signature only").Size(0.24f),
                DN("class Rifle : IWeapon", 0.46f, 0.46f, DiaTone.Accent).En("class Rifle : IWeapon").Size(0.24f),
                DN("class Sniper : IWeapon", 0.78f, 0.46f, DiaTone.Accent).En("class Sniper : IWeapon").Size(0.24f),
                DN("Rifle 必须把 Fire() 写全", 0.14f, 0.8f, DiaTone.Value).En("Rifle must write Fire() out").Size(0.26f),
                DN("Sniper 也必须写全", 0.86f, 0.8f, DiaTone.Value).En("so must Sniper").Size(0.24f),
                DN("IWeapon w = new Rifle(); w.Fire() 跑真身那份", 0.46f, 0.8f, DiaTone.Alert).En("IWeapon w = new Rifle(); w.Fire() runs the real one").Size(0.32f)
            },
            new[]
            {
                DE(0, 1).Lb("只声明，不实现", "declares, never implements"),
                DE(0, 2).Lb("实现", "implements"),
                DE(0, 3).Lb("实现", "implements"),
                DE(2, 4).Lb("漏一个就编译不过", "skip one and it will not build"),
                DE(3, 5).Lb("漏一个就编译不过", "same here"),
                DE(6, 2).Lb("声明类型写接口", "typed as the interface").Dash().NoArrow(),
                DE(6, 5).Lb("运行时看真身", "runs the real object")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "接口只规定「有什么」：IWeapon 里就一行 void Fire();，没有方法体。它是一份合同，自己一行代码都不执行，所以也不能 new。",
                    "An interface only states what exists: IWeapon holds just void Fire(); with no body. It is a contract that executes nothing, so you cannot new one."),
                DS(H(2, 3), H(1, 2),
                    "class Rifle : IWeapon 就是签这份合同。签了就必须把 Fire() 写出来 —— 少写一个，编译器当场报 CS0535，不会拖到运行时才炸。",
                    "class Rifle : IWeapon signs that contract. Signing means Fire() must exist: skip it and the compiler reports CS0535 at once, never at run time."),
                DS(H(4, 5), H(3, 4),
                    "两个类的 Fire() 各写各的：步枪打一发、狙击也许带穿透。接口不管你写成什么样，只管你有没有写。",
                    "Each class writes its own Fire(): a rifle fires one shot, a sniper might pierce. The interface does not care how, only whether."),
                DS(H(6), H(5, 6),
                    "多态就在这一句里：变量类型写接口，装进去的却是具体类。w.Fire() 跑谁的代码，看的是 new 出来的真身，不是左边那三个字母 —— 所以以后加一个 Tower 类，这段调用一行都不用改。",
                    "Polymorphism lives in that one line: the variable is typed as the interface but holds a concrete class. Which Fire() runs is decided by the object you new'd, not by the declared type - so adding a Tower class later changes none of this code."),
                DS(H(6), H(5, 6),
                    "多态也有代价：变量写成 IWeapon 之后，就只能点出接口里声明过的东西 —— Rifle 自己多出来的 Reload() 看不见，想用得先 is / as 转回去。所以接口该按「调用方真正需要的动作」来切：切太窄到处要强转，切太宽又等于没抽象。",
                    "Polymorphism costs something: typed as IWeapon, the variable exposes only what the interface declares - Rifle's own Reload() is invisible until you cast back with is or as. Size the interface by the actions callers actually need: too narrow and you cast everywhere, too wide and the abstraction means nothing."),
            },
            "接口 = 合同（只有签名）；实现类必须写全；调用处看接口，实际跑的是真身。",
            "An interface is a contract with signatures only; implementers must fill them in; callers see the interface, the real object runs.");

        // ---- Task 36 委托与 Action / Func：变量里装的不是数据，是「方法」 ----
        list[35].Dia = DSC(
            new[]
            {
                DN("delegate void HitHandler(int dmg)", 0.16f, 0.12f, DiaTone.Purple).En("delegate void HitHandler(int dmg)").Size(0.40f),
                DN("HitHandler onHit = TakeDamage;", 0.22f, 0.46f, DiaTone.Accent).En("HitHandler onHit = TakeDamage;").Size(0.30f),
                DN("TakeDamage(int) 的方法体", 0.16f, 0.84f, DiaTone.Value).En("the body of TakeDamage(int)").Size(0.28f),
                DN("onHit += AddScore;", 0.56f, 0.46f, DiaTone.Accent).En("onHit += AddScore;").Size(0.24f),
                DN("AddScore(int) 的方法体", 0.56f, 0.84f, DiaTone.Value).En("the body of AddScore(int)").Size(0.24f),
                DN("onHit(10) 一调用，挂上的全跑", 0.62f, 0.12f, DiaTone.Alert).En("onHit(10) runs everything attached").Size(0.24f),
                DN("Action / Func<int,int> 是现成模板", 0.88f, 0.46f, DiaTone.Panel).En("Action / Func<int,int> are ready-made").Size(0.26f)
            },
            new[]
            {
                DE(0, 1).Lb("先声明「长这样的方法」", "declare the shape first"),
                DE(1, 2).Lb("变量里存的是方法", "a variable holding a method"),
                DE(1, 3).Lb("+= 再挂一个", "+= attaches another"),
                DE(3, 4),
                DE(5, 1).Lb("调用一次", "one call"),
                DE(5, 6).Lb("懒得自己声明就用它", "use these instead of declaring").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "delegate 声明的是「一种方法的形状」：有 1 个 int 参数、返回 void。之后凡是长这样的方法，都能装进这种变量 —— 委托变量里存的不是数字，是方法的地址。",
                    "A delegate declares the shape of a method: one int parameter, void return. Any method of that shape fits such a variable - it stores an address, not a number."),
                DS(H(2), H(2),
                    "TakeDamage 本来就是个普通方法。赋值时不会调用它，只是把它的地址抄进 onHit。所以这时候不会有任何伤害结算。",
                    "TakeDamage is just an ordinary method. Assigning it does not call it, only copies its address into onHit, so nothing is resolved yet."),
                DS(H(3, 4), H(3),
                    "+= 不是加法，是往调用列表后面再接一个方法。于是 onHit 里现在挂着两个方法 —— 这就是「多播委托」，一份伤害能通知铺两个系统。",
                    "+= is not addition, it appends another method to the invocation list. onHit now holds two methods: a multicast delegate, one hit notifying two systems."),
                DS(H(5), H(4, 5),
                    "onHit(10) 只写一句，两个方法按挂上去的顺序依次跑，参数 10 都收到。这正是事件、按钮回调、伤害广播的底层机制。",
                    "onHit(10) is one line, yet both methods run in the order they were attached, each receiving 10. This is the machinery behind events, button callbacks and damage broadcasts."),
                DS(H(6), H(5),
                    "Action 是无返回值的现成委托、Func<int,int> 是有返回值的现成委托。99% 的场合不必自己写 delegate，直接用它们省一行声明。",
                    "Action is the ready-made no-return delegate, Func<int,int> the one with a return. Most of the time you skip declaring your own delegate and use these.")
            },
            "委托变量里存的是方法地址；+= 追加到调用列表；一次调用按顺序全跑。",
            "A delegate variable holds a method address; += appends to the invocation list; one call runs them all in order.");

        // ---- Task 37 Lambda：本质是「编译器帮我造一个匿名方法再包成委托」 ----
        list[36].Dia = DSC(
            new[]
            {
                DN("onHit += (dmg) => hp -= dmg;", 0.46f, 0.12f, DiaTone.Accent).En("onHit += (dmg) => hp -= dmg;").Size(0.42f),
                DN("编译器造一个隐藏方法", 0.22f, 0.46f, DiaTone.Purple).En("the compiler creates a hidden method").Size(0.28f),
                DN("再 new 一个委托指向它", 0.22f, 0.84f, DiaTone.Value).En("then a delegate pointing at it").Size(0.28f),
                DN("和手写一个具名方法等价", 0.62f, 0.46f, DiaTone.Panel).En("equivalent to writing a named method").Size(0.30f),
                DN("用到外部变量 hp → 变成闭包字段", 0.62f, 0.84f, DiaTone.Alert).En("using outer hp makes it a closure field").Size(0.34f),
                DN("参数类型由委托推出来，不必再写", 0.88f, 0.12f, DiaTone.Value).En("parameter types come from the delegate").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("语法糖展开", "desugared"),
                DE(1, 2).Lb("包成委托", "wrapped into a delegate"),
                DE(0, 3).Lb("跑起来一模一样", "runs identically").Dash().NoArrow(),
                DE(2, 4).Lb("捕获的变量跟着走", "captured variables travel with it")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "=> 不是「函数指针」，也不是新语法魔法：编译器在类里悄悄生成一个隐藏方法，方法体就是你箭头右边那段。",
                    "=> is neither a function pointer nor magic: the compiler quietly generates a hidden method whose body is everything right of the arrow."),
                DS(H(2), H(1),
                    "然后用这个隐藏方法 new 出一个委托，接进 onHit。所以 lambda 和「先写个具名方法再赋值」编译出来是同一件事，只是省了起名字。",
                    "It then news a delegate over that hidden method and attaches it to onHit. A lambda and a named method plus assignment compile to the same thing, minus the name."),
                DS(H(3), H(2),
                    "参数类型 (int dmg) 是从 HitHandler 的签名推出来的，所以 (dmg) => … 不写类型也对。写全了也一样能过，编译器只是替你省字。",
                    "The parameter type is inferred from HitHandler's signature, which is why (dmg) => … compiles without it. Writing it out also works; the compiler is only saving keystrokes."),
                DS(H(4), H(3),
                    "一旦箭头里用到外部的 hp，这个隐藏方法就不能只靠参数活了：编译器把它升级成一个对象，把 hp 搬成这个对象的字段 —— 这就是闭包，也是「lambda 会延长外部变量寿命」的原因。",
                    "Once the arrow body touches an outer hp, the hidden method cannot live on parameters alone: the compiler promotes it to an object with hp as a field. That is a closure, and why a lambda can keep an outer variable alive."),
                DS(H(5), H(4),
                    "同一套东西在 LINQ 里天天用：hits.Where(h => h > 4) 里的 h => h > 4 就是个 lambda，只不过它被塞给了 Func<int,bool>，而不是你自己声明的委托类型。",
                    "The same thing shows up in LINQ daily: in hits.Where(h => h > 4) the h => h > 4 is a lambda handed to a Func<int,bool> instead of your own delegate type.")
            },
            "lambda = 编译器造的隐藏方法 + 包成委托；用到外部变量就成了闭包。",
            "A lambda is a hidden method wrapped in a delegate; touch an outer variable and it becomes a closure.");

        // ---- Task 38 LINQ：一条数据流水线，每站只做一件事 ----
        list[37].Dia = DSC(
            new[]
            {
                DN("int[] hits = { 5, 12, 3, 8 }", 0.15f, 0.12f, DiaTone.Purple).En("int[] hits = { 5, 12, 3, 8 }").Size(0.26f),
                DN("Where(h => h > 4) → 5, 12, 8", 0.15f, 0.46f, DiaTone.Accent).En("Where(h => h > 4) -> 5, 12, 8").Size(0.30f),
                DN("Select(h => h * 2) → 10, 24, 16", 0.15f, 0.84f, DiaTone.Accent).En("Select(h => h * 2) -> 10, 24, 16").Size(0.32f),
                DN("Sum() → 50，Count() → 3", 0.50f, 0.46f, DiaTone.Value).En("Sum() -> 50, Count() -> 3").Size(0.28f),
                DN("First() / Any() / Max() / Min()", 0.50f, 0.84f, DiaTone.Value).En("First() / Any() / Max() / Min()").Size(0.28f),
                DN("写成一条链：hits.Where(...).Select(...).Sum()", 0.84f, 0.12f, DiaTone.Panel).En("as one chain: hits.Where(...).Select(...).Sum()").Size(0.30f),
                DN("返回 IEnumerable：没遍历就还没算", 0.84f, 0.46f, DiaTone.Alert).En("returns IEnumerable: nothing runs until you iterate").Size(0.28f),
                DN("ToList() / ToArray() 才真跑并留下结果", 0.84f, 0.84f, DiaTone.Value).En("ToList() / ToArray() actually run and keep it").Size(0.30f)
            },
            new[]
            {
                DE(0, 1).Lb("逐个问条件", "ask each one"),
                DE(1, 2).Lb("只把通过的送下去", "pass only survivors on"),
                DE(2, 3).Lb("把结果加起来", "add the results up"),
                DE(2, 4),
                DE(5, 0).Lb("链式写法读起来更顺", "reads better as a chain").Dash().NoArrow(),
                DE(3, 6).Lb("延迟执行", "deferred"),
                DE(6, 7)
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "LINQ 不改变原数组：Where 只是拿着条件逐个问「你过不过」，5 过、12 过、3 不过、8 过。原数组里 4 个元素一个都没动。",
                    "LINQ does not touch the array: Where just asks each element pass or fail - 5 yes, 12 yes, 3 no, 8 yes. All four originals stay as they were."),
                DS(H(2), H(1),
                    "Select 是「变形」不是「筛选」：它把每个通过的元素换成另一个值，10、24、16。个数不变，只是内容变了 —— 这是 Where 和 Select 最容易被搞混的地方。",
                    "Select transforms rather than filters: each survivor is replaced by another value, 10, 24, 16. Same count, different contents - the classic Where / Select mix-up."),
                DS(H(3, 4), H(2, 3),
                    "最后一步是「收口」：Sum 累加成一个数，Count 数个数，First 拿第一个，Any 只回答有没有。前面再长，到这一步才真正得到一个值。",
                    "The last stop collapses the pipeline: Sum adds to one number, Count counts, First takes the first, Any only answers yes or no. However long the chain, a value appears only here."),
                DS(H(5), H(4),
                    "这些方法可以一路点下去，因为它们返回的还是可继续查询的东西。写成一条链比中间开一堆变量更清楚，也不会多生成临时数组。",
                    "You can keep dotting because each returns something still queryable. One chain reads better than a pile of temporary variables and allocates no intermediate arrays."),
                DS(H(6, 7), H(5, 6),
                    "注意延迟执行：Where/Select 那一刻什么都没算，只是把「怎么算」记下来。foreach 或 ToList 开始遍历时才真的跑一遍 —— 所以同一段查询遍历两次，循环体就会执行两次。",
                    "Mind deferred execution: Where/Select compute nothing yet, they record how to compute. The work happens when foreach or ToList iterates - iterate twice and the pipeline runs twice.")
            },
            "Where 筛、Select 变形、末尾 Aggregation 收成一个值；不遍历就不算，遍历两次就跑两次。",
            "Where filters, Select transforms, a final aggregation collapses it; nothing runs until iterated, and iterating twice runs it twice.");

        // ---- Task 39 泛型与 where：T 是占位符，编译时被具体类型替换 ----
        list[38].Dia = DSC(
            new[]
            {
                DN("void Fill<T>(T[] arr, T value)", 0.28f, 0.12f, DiaTone.Accent).En("void Fill<T>(T[] arr, T value)").Size(0.36f),
                DN("T 只是个占位符", 0.14f, 0.46f, DiaTone.Purple).En("T is only a placeholder").Size(0.20f),
                DN("T = int → int[] / int", 0.14f, 0.84f, DiaTone.Value).En("T = int -> int[] / int").Size(0.26f),
                DN("T = string → string[] / string", 0.46f, 0.84f, DiaTone.Value).En("T = string -> string[] / string").Size(0.28f),
                DN("where T : class 限定能填什么", 0.60f, 0.46f, DiaTone.Alert).En("where T : class limits the choices").Size(0.32f),
                DN("where T : new() → 才能 new T()", 0.86f, 0.84f, DiaTone.Panel).En("where T : new() allows new T()").Size(0.30f),
                DN("一份代码，按 T 各生成对应版本", 0.88f, 0.12f, DiaTone.Value).En("one source, a version per T").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("参数类型先留空", "type left blank"),
                DE(1, 2).Lb("填 int", "fill in int"),
                DE(1, 3).Lb("填 string", "fill in string"),
                DE(0, 4).Lb("where 加限制", "where adds limits"),
                DE(4, 5).Lb("常用组合", "a common pair"),
                DE(0, 6).Lb("不用装箱、不用强转", "no boxing, no casts").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "方法签名里的 T 不是某个类型，而是「待填的空」。所以一份 Fill 既能填 int 数组也能填 string 数组，不必写两遍几乎相同的代码。",
                    "The T in the signature is a blank to fill, not a type. One Fill serves int arrays and string arrays alike, so you never write near-duplicate copies."),
                DS(H(2, 3), H(1, 2),
                    "调用 Fill(hits, 0) 时编译器把 T 认成 int，于是 arr 必须是 int[]、value 必须是 int，写错类型当场报错 —— 这就是泛型比 object 强的地方：不用装箱、不用强转。",
                    "At Fill(hits, 0) the compiler binds T to int, so arr must be int[] and value an int; a mismatch fails on the spot. That beats object: no boxing, no casts."),
                DS(H(4), H(3, 4),
                    "where 子句是这个空格的「填写须知」：T : class 只允许引用类型，T : struct 只允许值类型。写上了，方法体里就敢用引用类型才有的用法。",
                    "A where clause is the filling instruction for that blank: T : class admits reference types only, T : struct value types only. With it, the body may rely on those abilities."),
                DS(H(5), H(5),
                    "T : new() 才是能写 new T() 的前提 —— 否则编译器不知道这个类型有没有无参构造函数。约束不是装饰，它直接决定方法体里哪些代码合法。",
                    "T : new() is the precondition for writing new T(); without it the compiler cannot know a parameterless constructor exists. Constraints are not decoration, they decide which body code is legal."),
                DS(H(2, 3, 6), H(2, 3, 6),
                    "顺便说清一个常见误解：「按 T 各生成一份」是值类型的情形。T 是引用类型时所有实例共享同一份代码（反正都是地址），所以 List<string> 和 List<object> 不会各编一份；代价是 T 为值类型时不能用 == 比较，得用 EqualityComparer<T>.Default，或者加上 where T : IEquatable<T>。",
                    "Clearing up a common myth: a version per T applies to value types. When T is a reference type one body serves all instantiations (they are all addresses), so List<string> and List<object> are not compiled twice. The cost is that == is unavailable for a value-type T; use EqualityComparer<T>.Default or add where T : IEquatable<T>."),
            },
            "T 是待填空；调用时被具体类型替换；where 决定这个空能填什么、方法体能用什么。",
            "T is a blank; a call fills it with a real type; where decides what may fill it and what the body may use.");

        // ---- Task 40 命名空间与全限定名：名字的唯一身份证 ----
        list[39].Dia = DSC(
            new[]
            {
                DN("namespace MyGame.Towers", 0.16f, 0.12f, DiaTone.Purple).En("namespace MyGame.Towers").Size(0.32f),
                DN("class Tower", 0.16f, 0.46f, DiaTone.Accent).En("class Tower").Size(0.18f),
                DN("全名 MyGame.Towers.Tower", 0.16f, 0.8f, DiaTone.Value).En("full name MyGame.Towers.Tower").Size(0.30f),
                DN("using MyGame.Towers;", 0.6f, 0.12f, DiaTone.Panel).En("using MyGame.Towers;").Size(0.28f),
                DN("之后写 Tower 就够了", 0.6f, 0.46f, DiaTone.Value).En("after that Tower is enough").Size(0.26f),
                DN("两个 Tower 撞名 → 必须写全名", 0.84f, 0.8f, DiaTone.Alert).En("two Towers clash -> spell it out").Size(0.32f),
                DN("内存里没有命名空间这东西", 0.53f, 0.8f, DiaTone.Panel).En("namespaces take no memory").Size(0.22f)
            },
            new[]
            {
                DE(0, 1).Lb("里面装着", "contains"),
                DE(1, 2).Lb("编译时补全", "completed when compiling"),
                DE(3, 4).Lb("打开这个命名空间", "opens the namespace"),
                DE(4, 5).Lb("重名时 using 就不够了", "a clash defeats using"),
                DE(2, 6).Lb("名字只给人和编译器看", "names are for humans and the compiler")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "命名空间不是文件夹、也不是对象，它只是给名字加前缀的一层「归属」。Tower 写在里面，真名就是 MyGame.Towers.Tower。",
                    "A namespace is neither a folder nor an object, just a prefix marking ownership. Written inside it, Tower's real name is MyGame.Towers.Tower."),
                DS(H(2), H(1, 2),
                    "编译器一直用全名认人。你在文件里写 Tower，它替你补成 MyGame.Towers.Tower —— 所以两个不同命名空间里同名的 Tower 不会被搞混。",
                    "The compiler always uses the full name. When you type Tower it expands it to MyGame.Towers.Tower, which is why two same-named Towers never collide."),
                DS(H(3, 4), H(3),
                    "using 只是替你省字：它说「下面那些短名字，先在 MyGame.Towers 里找」。using 不加载、不执行、不占内存，删掉它只会让编译报找不到类型。",
                    "using only saves typing: it says look up short names in MyGame.Towers first. It loads nothing, runs nothing and costs no memory; remove it and the compiler simply cannot resolve the name."),
                DS(H(5), H(4),
                    "当两个命名空间里都有 Tower 时，两条 using 同时写就会报「不明确引用」，这时候只能写全名 —— 全限定名永远是对的，它不依赖任何 using。",
                    "When both namespaces define Tower, having both usings makes it ambiguous, and only the full name resolves it. The fully qualified name is always correct because it relies on no using at all."),
                DS(H(6), H(5),
                    "顺带一提：命名空间在运行时不占字节。对象在内存里只有字段和类型指针，没有一个格子叫「MyGame.Towers」。",
                    "Worth noting: namespaces cost no bytes at run time. An object holds fields and a type pointer; there is no box labelled MyGame.Towers.")
            },
            "命名空间是名字前缀；using 只省字；撞名时只能用全限定名；运行时不占内存。",
            "A namespace is a name prefix; using only saves typing; a clash needs the full name; nothing is stored at run time.");

        // ---- Task 41 预处理指令：#region 是折叠，#if 是真的决定编不编 ----
        list[40].Dia = DSC(
            new[]
            {
                DN("源码里的 #region / #if", 0.28f, 0.12f, DiaTone.Purple).En("#region / #if in the source").Size(0.30f),
                DN("#region 只折叠，仍参与编译", 0.14f, 0.46f, DiaTone.Panel).En("#region only folds, still compiled").Size(0.28f),
                DN("#if UNITY_EDITOR 为真", 0.50f, 0.46f, DiaTone.Accent).En("#if UNITY_EDITOR is true").Size(0.28f),
                DN("#if 为假的那一段", 0.82f, 0.46f, DiaTone.Alert).En("the block whose #if is false").Size(0.24f),
                DN("这段进编译，能调编辑器 API", 0.50f, 0.84f, DiaTone.Value).En("this part compiles, may call editor APIs").Size(0.30f),
                DN("整段删除，连语法都不检查", 0.82f, 0.84f, DiaTone.Alert).En("deleted whole, not even parsed").Size(0.28f),
                DN("所以打包后这段代码不存在", 0.14f, 0.84f, DiaTone.Value).En("so it does not exist in a build").Size(0.28f)
            },
            new[]
            {
                DE(0, 1).Lb("两种指令别混", "two different tools"),
                DE(0, 2).Lb("按编译符号判", "judged by compile symbols"),
                DE(0, 3),
                DE(2, 4).Lb("为真 → 保留", "true -> kept"),
                DE(3, 5).Lb("为假 → 剔除", "false -> removed"),
                DE(4, 6).Lb("平台不同，符号不同", "different platforms, different symbols")
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "#region 和 #if 长得像，作用完全不同：#region 只是编辑器里的折叠条，里面的代码一视同仁照常编译；#if 则是在编译前做取舍。",
                    "#region and #if look similar but differ completely: #region is only a fold in the editor and its code compiles as usual, while #if decides inclusion before compiling."),
                DS(H(2, 4), H(2),
                    "#if 靠编译符号判断：在编辑器里 UNITY_EDITOR 有定义，这一段被保留，于是你能在里面调 UnityEditor 的 API。打包出去时这个符号没了，整段消失。",
                    "#if tests compile symbols: inside the editor UNITY_EDITOR is defined, the block is kept, and you may call UnityEditor APIs. In a build the symbol is gone and the block vanishes."),
                DS(H(3, 5), H(3),
                    "为假的那一段不是「运行时跳过」，而是编译前就被删掉 —— 连语法都不检查。所以里面的代码写错了、引用了只存在于编辑器的类型，打包也不会报错。",
                    "A false block is not skipped at run time, it is deleted before compiling, not even parsed. Code inside may be broken or reference editor-only types and a build will not complain."),
                DS(H(6), H(4),
                    "这也是为什么 #if UNITY_EDITOR 里可以放心 using UnityEditor：这句 using 一起被删掉了，打包后根本不存在，不会出现「找不到程序集」的错误。",
                    "That is also why using UnityEditor inside #if UNITY_EDITOR is safe: the using is removed with the block, so a build never fails with a missing assembly."),
                DS(H(2, 3), H(2, 3),
                    "实用提醒：#if 的判断依据是编译符号，除了引擎自带的 UNITY_EDITOR / UNITY_ANDROID，还能在 Player Settings 的 Scripting Define Symbols 里自己加（比如 DEMO_BUILD）；但别拿 #if 当业务开关，一套代码分叉成两套会越来越难维护，运行时的平台差异用 Application.platform 判断更清楚。",
                    "In practice: #if tests compile symbols, and beyond the engine's UNITY_EDITOR / UNITY_ANDROID you can define your own in Player Settings (DEMO_BUILD, say). Do not use #if as a business switch though - one body forking into two rots fast; handle run-time platform differences with Application.platform instead."),
            },
            "#region 只折叠；#if 由编译符号决定这段是否存在，为假就连语法都不看。",
            "#region only folds; #if decides whether the block exists at all, and a false block is not even parsed.");

        // ---- Task 42 struct 与 class：值拷贝 vs 引用共享 ----
        list[41].Dia = DSC(
            new[]
            {
                DN("struct SPoint { int x; int y; }", 0.20f, 0.12f, DiaTone.Purple).En("struct SPoint { int x; int y; }").Size(0.34f),
                DN("SPoint a：8 字节就在变量里", 0.20f, 0.46f, DiaTone.Accent).En("SPoint a: 8 bytes inside the variable").Size(0.32f),
                DN("SPoint b = a：整块复制", 0.20f, 0.84f, DiaTone.Value).En("SPoint b = a: copied whole").Size(0.28f),
                DN("class CPoint { int x; int y; }", 0.72f, 0.12f, DiaTone.Purple).En("class CPoint { int x; int y; }").Size(0.34f),
                DN("CPoint a：变量只存一个地址", 0.72f, 0.46f, DiaTone.Accent).En("CPoint a: the variable holds only a reference").Size(0.32f),
                DN("CPoint b = a：两个变量指同一个对象", 0.72f, 0.84f, DiaTone.Alert).En("CPoint b = a: both point at one object").Size(0.34f)
            },
            new[]
            {
                DE(0, 1).Lb("值就装在变量里", "the value lives in the variable"),
                DE(1, 2).Lb("改 b 不影响 a", "changing b leaves a alone"),
                DE(3, 4).Lb("变量装的是地址", "the variable holds an address"),
                DE(4, 5).Lb("改 b 会改到 a", "changing b changes a"),
                DE(1, 4).Lb("一个值、一个引用", "one value, one reference").Dash().NoArrow()
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "struct 是值类型：a 这个变量里直接装着 8 个字节（x 4 格 + y 4 格）。没有 new 出来的堆对象，也就没有额外的地址要去追。",
                    "A struct is a value type: the variable a holds those 8 bytes directly, 4 for x and 4 for y. No heap object is created, so no address has to be followed."),
                DS(H(2), H(2),
                    "SPoint b = a; 会把 8 字节整块抄一份到 b。之后改 b.x，a.x 一点不受影响 —— 两个独立的 8 字节。这就是「复制」。",
                    "SPoint b = a; copies all 8 bytes into b. Changing b.x afterwards leaves a.x untouched: two independent 8-byte blocks. That is a copy."),
                DS(H(3, 4), H(3),
                    "class 是引用类型：new CPoint() 在堆上开一个对象，而变量 a 里只存 4 或 8 字节的地址。对象本身不在变量里，变量只是门牌号。",
                    "A class is a reference type: new CPoint() allocates an object on the heap, and a holds only a reference. The object is not in the variable; the variable is just the address."),
                DS(H(5), H(4, 5),
                    "CPoint b = a; 复制的是门牌号，于是 a 和 b 指向同一个对象。改 b.x 就是改 a.x —— 这不是 bug，是引用类型的定义。",
                    "CPoint b = a; copies the address, so a and b point at one object. Changing b.x changes a.x: not a bug, just what a reference type means."),
                DS(H(1, 4), H(1, 4),
                    "所以选型很简单：小、短命、想按值传（坐标、颜色、伤害数字）用 struct；有身份、要共享、可能为 null、体积大（敌人、武器实例）用 class。",
                    "So the choice is simple: small, short-lived, passed by value (positions, colours, damage numbers) suit a struct; things with identity, shared state, nullability or size (enemies, weapon instances) suit a class."),
                DS(H(2), H(1, 2),
                    "几个 struct 特有的坑：它不能自己定义无参构造函数，new SPoint() 得到的是全零；但一旦写了带参构造函数，就必须把所有字段都赋上，少一个都编译不过；而且每次赋值、传参都在整块复制，字段一多、调用一密，复制成本会盖过省下来的堆分配。",
                    "Struct-specific traps: no parameterless constructor of your own, and new SPoint() gives all zeros; but once you write a constructor every field must be assigned or it will not build; and each assignment or parameter copy duplicates the whole block, so with many fields or hot calls the copying outweighs the heap saving."),
            },
            "struct 拷贝整块值、改副本不影响原件；class 拷贝地址、两个变量共享同一对象。",
            "A struct copies the whole value so the original is safe; a class copies the address so both share one object.");

        // ---- Task 43 元组与解构：这一项讲的就是「值摆在哪儿」，用内存格最直接 ----
        list[42].Mem = MSC(
            new[]
            {
                MV("(int,int)", "size", "03 00 00 00 05 00 00 00",
                        "元组是值类型：8 字节直接躺在变量里，Item1 = 3、Item2 = 5，中间没有门牌号。", "值类型")
                    .En("A tuple is a value type: 8 bytes sit inside the variable, Item1 = 3 and Item2 = 5, with no address in between.", badge: "VALUE"),
                MV("int", "w", "00 00 00 00", "解构出来的 w 是全新的 4 格，值从 Item1 复制过来。", "副本")
                    .En("The destructured w is a brand-new 4 bytes, copied out of Item1.", badge: "COPY"),
                MV("int", "h", "00 00 00 00", "h 也一样：新的 4 格，不与元组共用。", "副本")
                    .En("Same for h: fresh 4 bytes, not shared with the tuple.", badge: "COPY")
            },
            new[]
            {
                MS("(int, int) size = (3, 5);", 1, 0,
                    "元组字面量 (3, 5) 直接摊开成 8 字节写进 size：前 4 格是 Item1 = 3，后 4 格是 Item2 = 5。没有堆、没有 new、没有额外对象。",
                    "(3, 5) is laid out as 8 bytes inside size: the first 4 hold Item1 = 3, the next 4 hold Item2 = 5. No heap, no new, no extra object."),
                MS("var (w, h) = size;", 3, 1,
                    "解构就是按位置拆开再各复制一次：Item1 的 3 抄进 w，Item2 的 5 抄进 h。从此这三块内存互不相干。",
                    "Destructuring splits by position and copies: Item1's 3 into w, Item2's 5 into h. From then on the three blocks are independent.",
                    new[] { 1, 2 }, new[] { "03 00 00 00", "05 00 00 00" }),
                MS("size.Item1", 3, -1,
                    "回头读 size.Item1 还是 3 —— 解构没把元组拆掉，它只是照值抄了一份出来。",
                    "Reading size.Item1 still gives 3: destructuring did not dismantle the tuple, it copied the values out."),
                MS("(width: 3, height: 5).width", 3, -1,
                    "命名元组只是给 Item1 / Item2 起了个编译期的别名：内存里还是那 8 字节、还是按位置排，名字完全不占空间、也不改变布局。所以为了可读性给元组起名字是免费的；但也别指望它顶替一个小类 —— 它没有方法、不能继承。",
                    "Named tuples are compile-time aliases for Item1 / Item2: memory still holds those 8 bytes in order, and the names cost nothing and change no layout. Naming a tuple for readability is free, but it cannot replace a small class - no methods, no inheritance."),
                MS("w = 9;", 3, 1,
                    "把 w 改成 9：动的只有 w 那 4 格，size 里 03 还在原位。值类型「复制一份」说的就是这个。",
                    "Change w to 9 and only w's 4 bytes move; 03 inside size stays put. That is what copying a value type means.",
                    new[] { 1 }, new[] { "09 00 00 00" })
            });

        // ---- Task 44 可空类型与 ?. / ??：一条「有没有值」的短路路径 ----
        list[43].Dia = DSC(
            new[]
            {
                DN("int? bonus = null", 0.14f, 0.12f, DiaTone.Purple).En("int? bonus = null").Size(0.26f),
                DN("HasValue = false", 0.14f, 0.44f, DiaTone.Accent).En("HasValue = false").Size(0.24f),
                DN("bonus ?? 0 → 用右边的 0", 0.14f, 0.8f, DiaTone.Value).En("bonus ?? 0 -> takes the 0").Size(0.28f),
                DN("boss?.Hp → 整句给 null，不崩", 0.8f, 0.12f, DiaTone.Value).En("boss?.Hp -> whole expression null, no crash").Size(0.32f),
                DN("bonus.Value → 直接抛异常", 0.48f, 0.44f, DiaTone.Alert).En("bonus.Value -> throws").Size(0.28f),
                DN("HasValue = true 时两个都能取", 0.84f, 0.8f, DiaTone.Value).En("with HasValue = true both work").Size(0.30f),
                DN("?? 可以串联：a ?? b ?? 0", 0.48f, 0.8f, DiaTone.Panel).En("?? chains: a ?? b ?? 0").Size(0.26f)
            },
            new[]
            {
                DE(0, 1).Lb("先看这一格", "check this byte first"),
                DE(1, 2).Lb("false → 走 ??", "false -> take the fallback"),
                DE(1, 4).Lb("false 还硬取 .Value", "false but you force .Value").Dash(),
                DE(5, 4).Lb("true 才不会抛", "true never throws"),
                DE(6, 2).Lb("一路兜到底", "one fallback after another")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "int? 不是 int 加个问号那么随意，它背后是 Nullable<int>：一个 bool HasValue 加一个 int 值。赋 null 时就是把 HasValue 置成 false。",
                    "int? is not int with a question mark: it is Nullable<int>, a bool HasValue plus an int value. Assigning null simply sets HasValue to false."),
                DS(H(5, 4), H(2, 3),
                    "HasValue 为 false 时读 .Value 会抛 InvalidOperationException —— 这是空引用之外最常见的坑，因为代码看起来完全正常。",
                    "With HasValue false, reading .Value throws InvalidOperationException - one of the most common traps after null references, because the code looks perfectly normal."),
                DS(H(2, 6), H(5),
                    "?? 是「没有就换一个」：左边有值用左边，没值用右边。它不抛异常，所以是处理可空值最安全的方式，还能 a ?? b ?? 0 一路兜底。",
                    "?? means take the left if it has a value, otherwise the right. It never throws, which makes it the safe way to handle nullables, and it chains as a ?? b ?? 0."),
                DS(H(3), H(3),
                    "?. 管的是引用类型：boss?.Hp 先看 boss 是不是 null，是就整句直接给 null、后面不执行；不是才去读 Hp。用它可以少写一层 if。",
                    "?. handles references: boss?.Hp checks boss for null first and, if so, yields null without evaluating further; only otherwise is Hp read. It saves a level of if."),
                DS(H(6), H(6),
                    "注意 ?. 的返回值可能变成可空：boss?.Hp 的类型是 int? 而不是 int，所以要接回 int 还得再 ?? 一个默认值 —— 两个符号经常连着用。",
                    "Note that ?. can make a result nullable: boss?.Hp has type int?, not int, so getting back to int needs a ?? default - the two symbols usually appear together.")
            },
            "可空值 = HasValue + 值；.Value 硬取会抛，?? 安全兜底，?. 遇 null 整句给 null。",
            "A nullable is HasValue plus a value; .Value throws, ?? falls back safely, ?. yields null for the whole expression.");

        // ---- Task 45 字符串插值与格式符：模板拼接，格式符决定怎么写字 ----
        list[44].Dia = DSC(
            new[]
            {
                // 代码行在窄框里会被 BestFit 压缩换行，把 $ 和它的双引号拆到两行会误导成两个东西；
                // 这里显式断成两行（断在逗号后），保证 $" 永远连在一起、且用等宽字体原样显示。
                DN("$\"hp={hp}/{max},\nrate={r:F2}\"", 0.36f, 0.06f, DiaTone.Accent)
                    .En("$\"hp={hp}/{max},\nrate={r:F2}\"").Note("插值字符串：$ 前缀 + 双引号", "interpolated string: $ prefix + quotes").Size(0.52f),
                DN("等于 string.Format(\"hp={0}/{1}, rate={2:F2}\", …)", 0.32f, 0.36f, DiaTone.Purple).En("same as string.Format(\"hp={0}/{1}, rate={2:F2}\", …)").Size(0.42f),
                DN("hp → \"3\"", 0.1f, 0.66f, DiaTone.Value).En("hp -> \"3\"").Size(0.16f),
                DN("max → \"10\"", 0.34f, 0.66f, DiaTone.Value).En("max -> \"10\"").Size(0.16f),
                DN("{r:F2} → \"0.67\"，F2 = 两位小数", 0.62f, 0.66f, DiaTone.Value).En("{r:F2} -> \"0.67\", F2 = two decimals").Size(0.28f),
                DN("拼成一个新串（字符串不可变）", 0.4f, 0.9f, DiaTone.Value).En("joined into a new string (strings are immutable)").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("编译器改写", "rewritten by the compiler"),
                DE(1, 2).Lb("第 0 个参数", "argument 0"),
                DE(1, 3).Lb("第 1 个参数", "argument 1"),
                DE(1, 4).Lb("格式符决定怎么写", "the format spec decides how"),
                DE(2, 5), DE(3, 5), DE(4, 5)
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "$ 只是让编译器改写：花括号里的表达式被求值，按位置变成 {0}、{1}、{2}，最终调 string.Format。它不创建任何特殊类型。",
                    "The $ only makes the compiler rewrite: expressions inside braces are evaluated and become {0}, {1}, {2}, ending in a string.Format call. No special type is created."),
                DS(H(2, 3), H(2, 3),
                    "数字要变成文字才能拼进去，这一步是用当前区域设置转的，所以同一个 3 在某些区域可能写成别的样子。要固定格式就得加格式符。",
                    "Numbers have to become text before joining, and that conversion follows the current culture, so the same 3 may print differently in some locales. Fix the format when that matters."),
                DS(H(4), H(4),
                    "{r:F2} 里的冒号后面是格式符：F 表示定点小数、2 表示两位，于是 0.6666… 变成 \"0.67\"（四舍五入）。常用的还有 D3 补零、P0 百分比、N0 千分位。",
                    "After the colon comes a format spec: F means fixed point and 2 means two digits, so 0.6666… becomes \"0.67\" rounded. Others worth knowing: D3 zero-pads, P0 percent, N0 thousands."),
                DS(H(5), H(5),
                    "整句做完是新建一个字符串：原来那些数字没变，旧字符串也没变 —— 字符串不可变。所以循环里疯狂拼字符串会不停产生垃圾，大量拼接要用 StringBuilder。",
                    "The line builds a new string: the numbers are unchanged and so are the old strings - strings are immutable. Heavy concatenation in a loop therefore creates garbage; use StringBuilder for that."),
                DS(H(0, 1), H(1),
                    "两点实用提醒：插值字符串本质上是 string.Format，值类型参数会装箱 —— 每帧拼日志时这份分配不小，热路径里要缓存或用 StringBuilder；另外花括号里冒号有特殊含义（它分隔格式符），所以在插值里写三元 a ? b : c 必须加括号，否则编译器把 : 当成格式符。",
                    "Two practical notes: an interpolated string is string.Format, and value-type arguments get boxed - per-frame log building allocates real garbage, so cache it or use StringBuilder on hot paths. Also a colon inside the braces means a format spec, so a ternary a ? b : c must be parenthesised or the : is read as one."),
            },
            "$ 是 string.Format 的语法糖；{表达式:格式符} 里冒号后面决定怎么写；结果永远是新字符串。",
            "$ is sugar for string.Format; after the colon in {expr:spec} comes the format; the result is always a new string.");

        // ---- Task 46 对象初始化器：分配 → 构造 → 逐字段赋值 → 交出地址 ----
        list[45].Dia = DSC(
            new[]
            {
                DN("new Dart { Speed = 2.5f, Hits = 3 }", 0.46f, 0.12f, DiaTone.Accent).En("new Dart { Speed = 2.5f, Hits = 3 }").Size(0.42f),
                DN("① 堆上分配对象", 0.14f, 0.46f, DiaTone.Purple).En("1. allocate on the heap").Size(0.22f),
                DN("② 跑无参构造函数", 0.14f, 0.84f, DiaTone.Purple).En("2. run the parameterless constructor").Size(0.28f),
                DN("③ 逐个赋 { } 里的字段", 0.50f, 0.84f, DiaTone.Accent).En("3. assign each field in the braces").Size(0.28f),
                DN("④ 地址交给变量", 0.84f, 0.84f, DiaTone.Value).En("4. hand the address to the variable").Size(0.22f),
                DN("顺序要紧：构造函数写的值会被 { } 覆盖", 0.84f, 0.46f, DiaTone.Alert).En("order matters: the braces overwrite the constructor").Size(0.28f)
            },
            new[]
            {
                DE(0, 1).Lb("第一步", "step one"),
                DE(1, 2).Lb("第二步", "step two"),
                DE(2, 3).Lb("第三步", "step three"),
                DE(3, 4).Lb("第四步", "step four"),
                DE(2, 5).Lb("后赋值赢", "last write wins")
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "new Dart { … } 里没有括号，编译器就当作调用无参构造函数 —— 所以这个类必须有一个能不带参数调用的构造函数，否则编译不过。",
                    "new Dart { … } with no parentheses means the parameterless constructor, so the class must have one that can be called empty, or it will not build."),
                DS(H(2), H(2),
                    "执行顺序是固定的：先分配对象、跑完构造函数，再依次执行花括号里的赋值。所以构造函数里写的初始值一定先落地。",
                    "The order is fixed: allocate, finish the constructor, then run the brace assignments in turn. Whatever the constructor set lands first."),
                DS(H(3), H(3),
                    "花括号里的每一次赋值都在构造函数之后执行，因此会把它刚写下的值覆盖掉。想在初始化器里改的字段，就别指望构造函数的默认值还留着。",
                    "Each brace assignment runs after the constructor and therefore overwrites what it just wrote. Fields you set in the initialiser will not keep the constructor's default."),
                DS(H(4), H(4),
                    "四步做完，一个填好字段的对象就绪，地址交给左边那个变量。整段写法的价值是把「建对象」和「设初值」压成一行，且字段名看得见，参数顺序不再重要。",
                    "After four steps the object is ready with its address handed to the variable. The win is that creation and initialisation become one readable line where names matter, not parameter order."),
                DS(H(5), H(5),
                    "这也是为什么初始化器特别适合配置类：只写想改的字段，其余保持默认；但字段一多、逻辑一复杂，就该走带参构造或工厂方法，别把规则埋进初始化器。",
                    "That makes it a great fit for config-like types: set only what changes and leave the rest. Once there is real logic, prefer a constructor or factory instead of hiding rules in an initialiser.")
            },
            "先分配、再构造、再逐字段赋值、最后交出地址；花括号里的赋值总在构造函数之后。",
            "Allocate, construct, assign each field, then hand out the address; brace assignments always come after the constructor.");

        // ---- Task 47 yield return：调用方与状态机来回切换 ----
        list[46].Dia = DSC(
            new[]
            {
                DN("foreach (int v in Each())", 0.22f, 0.12f, DiaTone.Accent).En("foreach (int v in Each())").Size(0.32f),
                DN("Each() 变成一个状态机", 0.68f, 0.12f, DiaTone.Purple).En("Each() becomes a state machine").Size(0.30f),
                DN("跑到 yield return 1 → 交出值并暂停", 0.68f, 0.46f, DiaTone.Value).En("hits yield return 1 -> hand out and pause").Size(0.32f),
                DN("循环体拿到 1 用掉", 0.22f, 0.46f, DiaTone.Accent).En("the body consumes 1").Size(0.22f),
                DN("再进来接着跑，局部变量不丢", 0.68f, 0.84f, DiaTone.Value).En("re-entering resumes, locals intact").Size(0.32f),
                DN("方法跑完 → foreach 结束", 0.22f, 0.84f, DiaTone.Alert).En("method ends -> foreach ends").Size(0.26f)
            },
            new[]
            {
                DE(0, 1).Lb("调用只是开始", "calling only starts it"),
                DE(2, 3).Lb("yield 交出一个值", "yield hands one value out"),
                DE(3, 4).Lb("MoveNext 再进来", "MoveNext re-enters"),
                DE(4, 2).Lb("下一次 yield", "the next yield"),
                DE(4, 5).Lb("走完就结束", "running out ends it")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "带 yield 的方法被调用时，方法体一行都不执行 —— 编译器把它变成了一个状态机对象，只把「准备好」的东西交出去。真正的执行要等 foreach 来要。",
                    "Calling a method with yield runs none of its body: the compiler turned it into a state machine object and just hands that back. Execution waits for foreach to ask."),
                DS(H(2, 3), H(1, 2),
                    "foreach 第一次 MoveNext 时才开始跑，一直跑到 yield return 1：把 1 交出去，然后就地暂停 —— 不是从头再来，是冻在当前这一行。",
                    "The first MoveNext starts the body, which runs until yield return 1: 1 is handed out and execution freezes right there - not restarting, suspending."),
                DS(H(4), H(3),
                    "下一次 MoveNext 从冻结的那一行后面继续，局部变量、循环计数全都还在状态机里。这就是「不用自己写集合就能逐个产出」的原理。",
                    "The next MoveNext resumes after the frozen line with locals and counters still inside the state machine. That is how you produce items one by one without building a collection."),
                DS(H(5), H(4, 5),
                    "方法跑到结尾就再也产不出值，MoveNext 返回 false，foreach 结束。注意：中间 break 退出时，迭代器会被丢弃，finally 里的清理仍会执行。",
                    "When the body ends it can produce nothing more, MoveNext returns false and the loop stops. Note that breaking early discards the iterator, yet finally blocks still run."),
                DS(H(0), H(0),
                    "所以 yield 的两个好处：一是逐个产出、不必先建一个大列表（内存友好），二是可以写「无限序列」，反正没人要就不往下跑。",
                    "That gives yield two wins: items stream out without building a big list first, and infinite sequences become possible since nothing advances unless asked.")
            },
            "调用不执行；每次 MoveNext 跑到下一个 yield 就暂停；局部变量活在状态机里，方法跑完循环结束。",
            "A call does not execute; each MoveNext runs to the next yield and pauses; locals live in the state machine; the loop ends when the body does.");

        // ---- Task 48 枚举转换：名字 / 整数 / 文本三向映射 ----
        list[47].Dia = DSC(
            new[]
            {
                DN("enum TowerState { Idle = 0, Scan = 1, Fire = 2 }", 0.48f, 0.12f, DiaTone.Purple).En("enum TowerState { Idle = 0, Scan = 1, Fire = 2 }").Size(0.44f),
                DN("\"Fire\" 写在代码里", 0.14f, 0.46f, DiaTone.Panel).En("\"Fire\" as written in code").Size(0.24f),
                DN("整数 2（内存里就是这个）", 0.50f, 0.46f, DiaTone.Value).En("the int 2, what memory holds").Size(0.26f),
                DN("打印出来是 \"Fire\"", 0.84f, 0.46f, DiaTone.Panel).En("printing gives \"Fire\"").Size(0.24f),
                DN("(TowerState)2 → Fire", 0.14f, 0.84f, DiaTone.Accent).En("(TowerState)2 -> Fire").Size(0.24f),
                DN("(int)state → 2", 0.48f, 0.84f, DiaTone.Accent).En("(int)state -> 2").Size(0.20f),
                DN("Enum.Parse<TowerState>(\"Fire\")", 0.84f, 0.84f, DiaTone.Accent).En("Enum.Parse<TowerState>(\"Fire\")").Size(0.30f)
            },
            new[]
            {
                DE(0, 1).Lb("写的时候用名字", "you write the name"),
                DE(0, 2).Lb("存的时候是数字", "it stores as a number"),
                DE(0, 3).Lb("显示时转回名字", "display converts back"),
                DE(4, 1).Lb("数字 → 名字", "number -> name").Dash(),
                DE(5, 2).Lb("名字 → 数字", "name -> number").Dash(),
                DE(6, 3).Lb("文本 → 枚举", "text -> enum").Dash()
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "枚举在源码里给你名字，但落到内存里存的就是整数：Fire 就是 2。所以它和 int 一样便宜，一个枚举变量占 4 字节，不是字符串。",
                    "An enum gives you names in source but stores an integer: Fire is 2. That makes it as cheap as an int - one enum variable is 4 bytes, not a string."),
                DS(H(5, 2), H(1, 2),
                    "枚举转整数是隐式安全的：直接 (int)state 就得 2。反过来 (TowerState)2 也能转，而且编译器不拦你 —— (TowerState)99 语法完全合法，只是没有对应名字。",
                    "Enum to int is safe and implicit: (int)state gives 2. Backwards also compiles and the compiler does not stop you - (TowerState)99 is legal, it simply has no name."),
                DS(H(3, 6), H(3, 6),
                    "要文本就得显式转换：state.ToString() 得到 \"Fire\"，Enum.Parse<TowerState>(\"Fire\") 得到 Fire。前者总能成功，后者遇到没见过的字符串会抛异常，必须 try 或先 TryParse。",
                    "Text needs an explicit call: state.ToString() yields \"Fire\", Enum.Parse<TowerState>(\"Fire\") yields Fire. The first always works; the second throws on unknown text, so wrap it or use TryParse."),
                DS(H(4), H(4, 5),
                    "实用结论：存档 / 网络传输存整数最省，但要防越界；界面显示、日志、配置表用名字最可读。两件事别混着来 —— 存档里塞 \"Fire\" 再 Parse，改个枚举名旧档就废了。",
                    "Practical rule: saves and network traffic store ints (cheapest, but guard the range), UI, logs and configs use names (readable). Do not mix them: a save holding \"Fire\" breaks the day you rename the member."),
                DS(H(4, 5), H(4, 5),
                    "一个必须防的坑：强转不查范围，(TowerState)99 编译、运行都合法，只是没有名字 —— ToString() 会打印 \"99\"，switch 只能落到 default，存进存档就是一串谁都读不懂的数。所以从外部（存档、网络、配置）读进来的枚举值，先过一遍 Enum.IsDefined 或白名单。",
                    "A trap you must guard: casts do not range-check, so (TowerState)99 is legal and simply nameless - ToString() prints \"99\", a switch falls to default, and a save file ends up holding a number nobody can read. Any enum arriving from outside (saves, network, configs) deserves an Enum.IsDefined check or a whitelist."),
            },
            "枚举存的是整数；名字 ↔ 数字可强转（不查范围）；文本要 ToString / Parse，Parse 会抛异常。",
            "An enum stores an int; name and number cast freely without range checks; text needs ToString / Parse, and Parse throws.");

        // ---- Task 49 扩展方法：看着像成员，编译后是静态调用 ----
        list[48].Dia = DSC(
            new[]
            {
                DN("static class TowerExt", 0.24f, 0.12f, DiaTone.Purple).En("static class TowerExt").Size(0.26f),
                DN("static float Dps(this Tower t)", 0.16f, 0.46f, DiaTone.Accent).En("static float Dps(this Tower t)").Size(0.32f),
                DN("this 决定它挂在谁身上", 0.16f, 0.84f, DiaTone.Value).En("this decides which type it hangs on").Size(0.28f),
                DN("调用处写成 tower.Dps()", 0.55f, 0.46f, DiaTone.Panel).En("you write tower.Dps()").Size(0.24f),
                DN("编译成 TowerExt.Dps(tower)", 0.55f, 0.84f, DiaTone.Value).En("compiles to TowerExt.Dps(tower)").Size(0.30f),
                DN("没有新权限：私有成员照样进不去", 0.87f, 0.46f, DiaTone.Alert).En("no new access: privates stay private").Size(0.24f),
                DN("using 到它的命名空间才看得见", 0.87f, 0.12f, DiaTone.Panel).En("visible only where its namespace is used").Size(0.26f)
            },
            new[]
            {
                DE(0, 1).Lb("必须放在静态类里", "must live in a static class"),
                DE(1, 2).Lb("this 标出扩展的是谁", "this marks the extended type"),
                DE(3, 4).Lb("语法糖展开", "desugared"),
                DE(3, 5).Lb("看着像成员", "looks like a member").Dash().NoArrow(),
                DE(0, 6).Lb("命名空间决定可见", "the namespace gates visibility")
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "扩展方法有三个硬性条件：类必须是 static、方法必须是 static、第一个参数前面必须写 this。少一条，它就是个普通静态方法，点不出来。",
                    "Extension methods need three things: a static class, a static method, and this before the first parameter. Miss one and it is just an ordinary static method you cannot dot into."),
                DS(H(2), H(2),
                    "这个 this Tower t 就是「挂在谁身上」的声明：有了它，Tower 类型的变量后面就能点出 Dps()。它不是给类型加成员，只是让编译器允许这种写法。",
                    "That this Tower t is the claim to hang on: with it, a Tower variable can be dotted with Dps(). It adds no member to the type, it only makes the syntax legal."),
                DS(H(3, 4), H(3),
                    "tower.Dps() 编译后就是 TowerExt.Dps(tower) —— 参数换个位置而已，运行时没有任何额外开销，也不会被装箱。",
                    "tower.Dps() compiles to TowerExt.Dps(tower): the parameter just moves position. No run-time overhead, no boxing."),
                DS(H(5), H(5),
                    "最容易误解的地方：扩展方法拿不到私有字段。它站在类外面，只能用它本来就能访问的东西 —— 所以它适合写「基于公开成员的派生计算」，不适合改写类的内部逻辑。",
                    "The usual misunderstanding: an extension method cannot touch private fields. It stands outside the class and sees only what any caller sees, so it suits derived convenience, not internal logic."),
                DS(H(6), H(6),
                    "最后注意可见性：扩展方法跟着它所在的命名空间走，没 using 就根本找不到，编辑器也不会提示。团队里常常专门开一个 .Extensions 命名空间放它们。",
                    "Finally, visibility follows its namespace: without a using it is invisible and the IDE shows no hint at all. Teams usually keep an .Extensions namespace for exactly this.")
            },
            "扩展方法 = 静态类里的静态方法 + 第一个参数加 this；调用是语法糖，权限不变，可见性跟着命名空间。",
            "An extension method is a static method in a static class with this on its first parameter; the call is sugar, access is unchanged, visibility follows the namespace.");
    }

    /// <summary>
    /// 给语法课前 19 项任务各配一段「代码动画」（第 20~49 项暂不配动画页，靠第 2 页的「亲手敲一遍」代替）：每一步 = 高亮示例代码的某一行 + 一句人话解释它做什么。
    /// 行用特征子串定位（见 FindAnimLine），所以以后改示例代码只要还留着这段特征文字就不会错位。
    /// </summary>
    private static void AppendGrammarAnimPages(TaskDef[] list)
    {
        list[0].Anim = new[]
        {
            AS("// 计算剩余血量", "先在代码上贴一张便条：// 后面这一行是写给人看的，电脑根本不执行。", "A sticky note for humans: everything after // is ignored by the computer.").KeyIn("// remaining hp"),
            AS("int remainHp", "真正干活的是这行：造一个整数变量 remainHp，里面装 3。", "The real work: create an int variable remainHp holding 3."),
            AS("/* 下面这行", "多行注释用 /* 开头，可以一直写到 */，中间跨几行都行。", "A block comment starts with /* and runs until */, over as many lines as you like.").KeyIn("/* this prints to the screen"),
            AS("tower.PrintLine", "最后调用炮台的 PrintLine，把变量里的值打印到荧幕上。", "Finally PrintLine prints the variable's value to the screen.")
        };

        list[1].Anim = new[]
        {
            AS("int hp", "int 只能装整数：hp 里放进 100。", "int holds whole numbers only: hp gets 100."),
            AS("float speed", "float 装小数，字面量后面必须带 f，写成 1.5 会报错。", "float holds decimals and needs the f suffix: 1.5 alone is an error."),
            AS("bool ready", "bool 只有两个值 true / false，像一盏只能开或关的灯。", "bool has only true / false, like a switch that is either on or off."),
            AS("string tag", "string 装文字，文字必须用双引号包起来。", "string holds text, and text must be wrapped in double quotes.")
        };

        list[2].Anim = new[]
        {
            AS("var hp", "右边 100 是 int 字面量，编译器就把 hp 定成 int —— var 只是省了你打 int 这两个字。", "100 is an int literal, so hp becomes int; var only saves typing int."),
            AS("var speed", "右边带 f 就是 float。同一个 var，右边一变，推断结果就跟着变。", "With the f suffix the right side is float: same var, different inference."),
            AS("hp = 1.5f", "类型定下之后不能反悔：往 int 的 hp 里塞小数，编译期就报 CS0266。", "The type is fixed afterwards: a decimal into an int hp is CS0266 at compile time.")
        };

        list[3].Anim = new[]
        {
            AS("const int MaxBlocks", "const 表示这个数定死了：MaxBlocks = 400，之后谁想改就会编译报错。", "const freezes the value: change MaxBlocks later and the compiler refuses."),
            AS("const float Pi", "同样是 const，只是类型换成 float，所以 3.14f 后面要带 f。", "Same const idea with the float type, hence the f on 3.14f.")
        };

        list[4].Anim = new[]
        {
            AS("int total", "* 优先级高于 +：先算 3 * 4 = 12，再加 2，total 得到 14。", "* binds tighter than +: 3 * 4 = 12 first, then + 2, so total is 14."),
            AS("float halfTotal", "用 2f（浮点数）去除，小数不会丢；写成 2 就是整数除法，小数被砍掉。", "Dividing by 2f keeps the fraction; dividing by 2 would drop it."),
            AS("int rest", "% 是取余数：14 % 5 余 4，rest 得到 4。", "% is the remainder: 14 % 5 leaves 4, so rest is 4.")
        };

        list[5].Anim = new[]
        {
            AS("if (hp <= 0)", "先算括号里的 hp <= 0：hp 是 2，2 <= 0 为假，这一支整块跳过。", "Evaluate hp <= 0 first: hp is 2, 2 <= 0 is false, so this whole branch is skipped."),
            AS("else if (hp < hpMax", "再看第二问：2 < 3 为真、3 > 0 也为真，&& 两边都真才进这一支。", "Next question: 2 < 3 is true and 3 > 0 is true, and && needs both to pass."),
            AS("else", "两条都不成立才会落到 else；这次用不上，但它负责兜底。", "else only runs when both tests fail; unused this time, but it is the fallback."),
            AS("switch (hpMax)", "if 链走完，接着按 hpMax 的值挑一支进去。", "After the if chain, switch picks a branch by the value of hpMax."),
            AS("case 3", "hpMax 是 3，命中 case 3：打印 cap is 3，然后 break 跳出整个 switch。", "hpMax is 3, so case 3 matches: it prints cap is 3, then break leaves the switch.")
        };

        list[6].Anim = new[]
        {
            AS("int i = 0", "进门只做一次：造出计数器 i = 0。", "Once on entry: create the counter i = 0."),
            AS("i < 3", "每轮进门先问：i 还小于 3 吗？真就进循环体，假就整个 for 结束。", "Each round asks first: is i still below 3? True runs the body, false ends the loop."),
            AS("tower.PrintLine", "循环体里用当前的 i 打印，所以三行分别是 shot 0 / 1 / 2。", "The body prints the current i, so the three lines are shot 0 / 1 / 2."),
            AS("i++", "每轮出门 i 加 1。这句一漏，i 永远是 0，条件永远成立 —— 死循环。", "After each round i goes up by one. Drop this and i stays 0 forever: an infinite loop.")
        };

        list[7].Anim = new[]
        {
            AS("int[] hits", "造一个能装 3 个整数的格子柜，名字叫 hits。", "Build a cabinet that holds 3 ints, named hits."),
            AS("hits[0] = 2", "往第 0 号格子放 2。下标从 0 开始，长度 3 的合法下标只有 0/1/2。", "Put 2 into slot 0. Indexes start at 0, so a length of 3 allows 0/1/2 only."),
            AS("hits[0]={0}", "读第 0 号格子里的值打印出来 → 荧幕上出现 2。", "Read slot 0 and print it - 2 shows up on screen.")
        };

        list[8].Anim = new[]
        {
            AS("List<int> hpList", "List 是会自动变长的柜子，先造一个空的。", "List is a cabinet that grows by itself: start with an empty one."),
            AS("hpList.Add(3)", "往里塞一个 3，柜子自己长高，不用你操心长度。", "Add a 3 and the list grows; you never manage its length."),
            AS("hpList.Count", "Count 是现在装了几个 → 打印 1。", "Count is how many items are inside - it prints 1.")
        };

        list[9].Anim = new[]
        {
            AS("int[] hits", "先把集合准备好：foreach 需要一个能从头数到尾的东西。", "Prepare the collection first: foreach needs something it can walk end to end."),
            AS("foreach (int h in hits)", "foreach 自己管下标：第一轮取 hits[0]，第二轮取 hits[1]，一直到最后一块。", "foreach manages the index: hits[0], then hits[1], up to the last block."),
            AS("h", "h 是当前元素的副本：打印它没问题，但给 h 赋值不会写回数组。", "h is a copy of the current element: printing is fine, assigning never writes back."),
            AS("}", "走完最后一个元素，循环自然结束 —— 不用你写 i++，也不用自己判断越界。", "After the last element the loop just ends: no i++ and no bounds check from you.")
        };

        list[10].Anim = new[]
        {
            AS("int wi = 0", "先造循环变量 wi = 0。", "Create the loop variable wi = 0."),
            AS("while (wi < 3)", "每轮开头先问一次：wi 还小于 3 吗？", "Each round starts by asking: is wi still below 3?"),
            AS("wi++", "循环体里把 wi 加 1 —— 这句千万别漏，否则 wi 永远是 0，死循环。", "Increment wi inside the body - skip this and wi stays 0 forever: an infinite loop.")
        };

        list[11].Anim = new[]
        {
            AS("int di = 0", "di 从 0 开始。", "di starts at 0."),
            AS("do", "do 的意思是先干一次再谈条件：循环体至少执行一遍。", "do means act first, ask later: the body always runs at least once."),
            AS("} while (di < 3);", "最后才判断 di < 3；注意结尾这个分号，漏了直接报错。", "Only at the end is di < 3 checked - and that trailing semicolon is required.")
        };

        list[12].Anim = new[]
        {
            AS("void ShowHp", "定义一台小机器：名字叫 ShowHp，进货口收一个整数 hp。定义的时候什么都不执行。", "Define a little machine named ShowHp that takes one int. Defining runs nothing."),
            AS("tower.PrintLine", "机器内部的动作：把收到的 hp 打印出来。", "Inside the machine: print the hp it received."),
            AS("ShowHp(3);", "真正调用时才跑起来：3 从进货口进去，荧幕打印 hp=3。", "Only the call runs it: 3 goes in, hp=3 is printed.")
        };

        list[13].Anim = new[]
        {
            AS("int Twice", "定义一台机器：收一个 int，吐回一个 int。", "Define a machine that takes an int and hands one back."),
            AS("return v * 2", "return 就是「交货」：把 v * 2 算好递回去。", "return is the hand-over: compute v * 2 and give it back."),
            AS("int sum = Twice(2)", "调用 Twice(2) → 拿到 4，装进变量 sum。", "Call Twice(2) - it returns 4, stored in sum."),
            AS("sum={0}", "最后把 sum 打印出来 → 荧幕上出现 sum=4。", "Print sum, and the screen shows sum=4.")
        };

        list[14].Anim = new[]
        {
            AS("int curHp", "先有一个数 curHp = 3。", "Start with a number: curHp = 3."),
            AS("string msg =", "+ 把文字和数字粘在一起，数字会自动变成文字 → \"hp=3\"。", "+ glues text and numbers; the number turns into text, giving \"hp=3\"."),
            AS("tower.PrintLine(msg", "再在后面拼一段 \" / done\" → \"hp=3 / done\"，然后打印。", "Append \" / done\", giving \"hp=3 / done\", then print it.")
        };

        list[15].Anim = new[]
        {
            AS("int baseHp", "先备好要填进句子的数：baseHp = 3。", "Prepare the value that will go into the sentence: baseHp = 3."),
            AS("string.Format(", "模板里的 {0} {1} 是空位，按顺序取后面传进来的参数填进去。", "The {0} {1} slots are filled in order by the arguments after the comma."),
            AS("{1:F1}", ":F1 会作用在这个空位上吗？会 —— {1:F1} 表示取第 2 个参数并保留 1 位小数 → 1.5。", ":F1 formats that slot: {1:F1} takes the second argument with one decimal, giving 1.5."),
                    };

        list[16].Anim = new[]
        {
            AS("Vector3 up =", "new Vector3(0f, 1f, 0f) 造出一个值：三个 float 打包，up 这个名字一次带走。", "new Vector3(0f, 1f, 0f) builds one value: three floats packed, carried by the single name up."),
            AS("up.x", "点号取分量：up.x 就是那份打包里的第一块，值是 0。", "The dot reads one part: up.x is the first float inside the pack, here 0."),
            AS("tower.Fire(up)", "整个 up 当参数交出去 —— 不用写 (0f, 1f, 0f) 三个数，也不可能把顺序记错。", "Pass the whole up - no three floats to type, no order to forget."),
            AS("Vector2 flat", "Vector2 只有 x / y 两块：适合平面上的方向，占的格子也只有 Vector3 的三分之二。", "Vector2 has only x / y: good for flat directions and two thirds the boxes of Vector3.")
        };

        list[17].Anim = new[]
        {
            AS("Vector3 dir = new Vector3", "new 负责造：按 Vector3 这张图纸做出一个对象；= 负责把它存进 dir。", "new builds an object from the Vector3 blueprint; = stores it in dir."),
            AS("tower.Fire(dir)", "对象可以整个当参数传：把 dir 交给 tower 的 Fire 方法。", "An object can be passed whole: hand dir to tower's Fire method."),
            AS("dir.z", "点号取成员：dir.z 读这个对象身上的 z。同理 tower.Fire 也是点号，只不过取的是方法。", "The dot reads a member: dir.z. tower.Fire is the same dot, only it reaches a method.")
        };

        list[18].Anim = new[]
        {
            AS("GlobalCode {", "GlobalCode 不是 C# 的东西：编辑器在你没看见的时候，把这几行从方法体里剪走、贴到类的最外层。", "GlobalCode is not C#: behind the scenes the editor cuts these lines out of the method body and pastes them at class level."),
            AS("int shots = 0;", "块里这条声明被搬成字段 shots：它不再属于某一帧，而是属于整个类，只在重新编译时重造一次。", "This declaration becomes the field shots: it belongs to the class, not to a frame, and is rebuilt only on recompile."),
            AS("shots = shots + 1;", "每帧进来读的都是同一份 shots：上一帧加过的值还在，所以数字会一直涨，而不是每帧回到 1。", "Every frame reads the same shots: last frame's value is still there, so the number keeps climbing instead of resetting to 1."),
            AS("tower.PrintLine", "把这份跨帧存活的值打印出来：第一帧 1、第二帧 2…… 这就是全局变量能记住东西的意义。", "Print that surviving value: 1 on the first frame, 2 on the next... that is what a global is for.")
        };
    }

    // ================== 任务表（炮台 API 调用 15 项） ==================

    private static TaskDef[] apiTasks;

    private static TaskDef[] ApiTasks
    {
        get
        {
            if (apiTasks == null)
                apiTasks = BuildApiTasks();
            return apiTasks;
        }
    }

    private static TaskDef[] BuildApiTasks()
    {
        TaskDef[] list = new TaskDef[15];

        list[0] = Def(
            "任务 1：Print 打印不换行",
            "Task 1: Print",
            "签名：void Print(string format, params object[] args);\n"
            + "说明：输出到荧幕但不换行，适合在同一行拼多段内容。\n"
            + "示例代码：\n"
            + "    tower.Print(\"hp={0} \", 7);\n"
            + "    tower.Print(\"count={0}\", 42);\n\n"
            + "目标：用 Print 输出一段不换行的文字（两句会拼在同一行）。\n"
            + "提示：Print 与 PrintLine 的区别只有是否换行，下一项就学 PrintLine。\n"
            + "判定：出现「tower.Print(」（PrintLine 不算）。",
            "Signature: void Print(string format, params object[] args);\n"
            + "Note: prints without a trailing newline - handy for building one line piecewise.\n"
            + "Example Code:\n"
            + "    tower.Print(\"hp={0} \", 7);\n"
            + "    tower.Print(\"count={0}\", 42);\n\n"
            + "Goal: use Print for a non-newline output (both calls land on the same line).\n"
            + "Hint: Print vs PrintLine differs only in the trailing newline; PrintLine comes next.\n"
            + "Check: \"tower.Print(\" appears (PrintLine does not count).",
            (raw, code) => RePrintOnly.IsMatch(code));

        list[1] = Def(
            "任务 2：PrintLine 打印换行",
            "Task 2: PrintLine",
            "签名：void PrintLine(string format, params object[] args);\n"
            + "说明：输出到 +X 前方荧幕并换行，占位符同 string.Format。\n"
            + "示例代码：\n"
            + "    tower.PrintLine(\"hp={0}\", 7);\n\n"
            + "目标：用 PrintLine 输出一行文字（每次调用自动换行）。\n"
            + "提示：荧幕最多保留 24 行，超出会自动丢弃最旧的；调试循环里别每帧狂打印。\n"
            + "判定：出现「tower.PrintLine(」。",
            "Signature: void PrintLine(string format, params object[] args);\n"
            + "Note: prints one line to the +X screen; same placeholders as string.Format.\n"
            + "Example Code:\n"
            + "    tower.PrintLine(\"hp={0}\", 7);\n\n"
            + "Goal: print one line with PrintLine (each call ends the line).\n"
            + "Hint: the screen keeps 24 lines; avoid printing every frame in a loop.\n"
            + "Check: \"tower.PrintLine(\" appears.",
            (raw, code) => RePrintLine.IsMatch(code));

        list[2] = Def(
            "任务 3：ClearPrint 清空荧幕",
            "Task 3: ClearPrint",
            "签名：void ClearPrint();   // 清空荧幕全部内容\n"
            + "示例代码：\n"
            + "    tower.ClearPrint();\n"
            + "    tower.PrintLine(\"screen cleared\");\n\n"
            + "目标：调用 ClearPrint 清空一次荧幕。\n"
            + "提示：Run 每帧都会执行，无条件 ClearPrint 会导致荧幕始终只有一行；配合计数条件使用。\n"
            + "判定：出现「tower.ClearPrint(」。",
            "Signature: void ClearPrint();   // clears the whole screen\n"
            + "Example Code:\n"
            + "    tower.ClearPrint();\n"
            + "    tower.PrintLine(\"screen cleared\");\n\n"
            + "Goal: call ClearPrint once.\n"
            + "Hint: Run executes every frame, so an unconditional ClearPrint keeps the screen at one line.\n"
            + "Check: \"tower.ClearPrint(\" appears.",
            (raw, code) => ReClearPrint.IsMatch(code));

        list[3] = Def(
            "任务 4：遍历 + 读身份（GetAllObjects / Index / Type）",
            "Task 4: Iterate + read identity (GetAllObjects / Index / Type)",
            "签名：List<TowerObjectInfo> GetAllObjects();\n"
            + "字段：int Index;      // 方块生成编号（全局递增，不是数组下标）\n"
            + "      string Type;   // \"ATTACK\" 红块 / \"BOMB\" 蓝块\n"
            + "示例代码：\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0} {1}\", o.Index, o.Type);\n"
            + "    }\n\n"
            + "目标：用 foreach 遍历 tower.GetAllObjects() 的返回值，并在循环体里同时用上 o.Index 与 o.Type。\n"
            + "提示：列表为空时循环体一次都不会执行，属正常现象；Type 是字符串，比较要写 o.Type == \"ATTACK\"（两个等号 + 英文引号）。\n"
            + "判定：foreach、tower.GetAllObjects(、.Index、.Type 四样同时出现。",
            "Signature: List<TowerObjectInfo> GetAllObjects();\n"
            + "Fields: int Index;   // spawn serial number (global counter, not an array index)\n"
            + "string Type;   // \"ATTACK\" red / \"BOMB\" blue\n"
            + "Example Code:\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0} {1}\", o.Index, o.Type);\n"
            + "    }\n\n"
            + "Goal: iterate tower.GetAllObjects() with foreach and use both o.Index and o.Type in the body.\n"
            + "Hint: an empty list means the body never runs; Type is a string, so compare with o.Type == \"ATTACK\".\n"
            + "Check: foreach, tower.GetAllObjects(, .Index and .Type all appear.",
            (raw, code) => ReForeach.IsMatch(code) && ReGetAllObjects.IsMatch(code)
                && ReFieldIndex.IsMatch(code) && ReFieldType.IsMatch(code));

        list[4] = Def(
            "任务 5：读状态（HP / Position）",
            "Task 5: Read state (HP / Position)",
            "字段：int HP;              // 剩余受击层数（各块不同，最多 6 层；吃光即被击毁）\n"
            + "      Vector3 Position;   // 几何中心的世界坐标\n"
            + "示例代码：\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.HP <= 1)\n"
            + "            tower.PrintLine(\"#{0} hp={1} at ({2:F1},{3:F1},{4:F1})\",\n"
            + "                o.Index, o.HP, o.Position.x, o.Position.y, o.Position.z);\n"
            + "    }\n\n"
            + "目标：读取 HP 与 Position，把血量和世界坐标一起打印出来。\n"
            + "提示：Position 是 Vector3，用 .x .y .z 取分量；炮台在原点朝 +X 发射，所以 Position.x 越小越靠近炮台。\n"
            + "判定：.HP 与 .Position 同时出现。",
            "Fields: int HP;   // remaining hit layers (varies per block, up to 6; destroyed when gone)\n"
            + "Vector3 Position;   // world-space geometric center\n"
            + "Example Code:\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.HP <= 1)\n"
            + "            tower.PrintLine(\"#{0} hp={1} at ({2:F1},{3:F1},{4:F1})\",\n"
            + "                o.Index, o.HP, o.Position.x, o.Position.y, o.Position.z);\n"
            + "    }\n\n"
            + "Goal: read HP and Position and print both.\n"
            + "Hint: Position is a Vector3 - take .x .y .z; the tower sits at the origin firing toward +X, so a smaller Position.x is closer.\n"
            + "Check: both .HP and .Position appear.",
            (raw, code) => ReFieldHp.IsMatch(code) && ReFieldPosition.IsMatch(code));

        list[5] = Def(
            "任务 6：读运动与体量（Velocity / Volume）",
            "Task 6: Read motion and size (Velocity / Volume)",
            "字段：Vector3 Velocity;   // 速度向量（方向 × 速度，单位/秒）\n"
            + "      float Volume;       // 当前剩余体积（越打越小）\n"
            + "      float InitialVolume;// 初始体积（切割后不变），可作分母算剩余比例\n"
            + "      int MaxHits;        // 初始受击层数；Vector3 Size;   // 当前三边尺寸\n"
            + "示例代码：\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0} speed={1:F2} vol={2:F2}/{3:F2}\",\n"
            + "            o.Index, o.Velocity.magnitude, o.Volume, o.InitialVolume);\n"
            + "    }\n\n"
            + "目标：读取 Velocity 求速度大小，并读取 Volume / InitialVolume 估算收益。\n"
            + "提示：v.magnitude 是标量速度，v.normalized 是飞行方向；Volume / InitialVolume 越大越值得打。\n"
            + "判定：.Velocity 与 .Volume 同时出现。",
            "Fields: Vector3 Velocity;   // velocity vector (direction x speed, units/sec)\n"
            + "float Volume;   // current remaining volume (shrinks when hit)\n"
            + "float InitialVolume;   // initial volume (constant) - use as the denominator for a ratio\n"
            + "int MaxHits;   // initial hit layers; Vector3 Size;   // current edge sizes\n"
            + "Example Code:\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0} speed={1:F2} vol={2:F2}/{3:F2}\",\n"
            + "            o.Index, o.Velocity.magnitude, o.Volume, o.InitialVolume);\n"
            + "    }\n\n"
            + "Goal: get the scalar speed from Velocity and read Volume / InitialVolume to estimate the payoff.\n"
            + "Hint: v.magnitude is the scalar speed, v.normalized the flight direction; a bigger Volume / InitialVolume is worth more.\n"
            + "Check: both .Velocity and .Volume appear.",
            (raw, code) => ReFieldVelocity.IsMatch(code) && ReFieldVolume.IsMatch(code));

        list[6] = Def(
            "任务 7：内置 API · Time.time / Time.deltaTime 限频计时",
            "Task 7: Built-in API - Time.time / Time.deltaTime throttling",
            "签名：static float Time.time         // 游戏启动到现在的秒数（受 timeScale 影响），每帧都在涨\n"
            + "      static float Time.deltaTime    // 上一帧花了多少秒，60FPS 时约 0.0167\n"
            + "说明：Run 每帧都会被执行，想让「打印 / 开火」变成每 0.5 秒一次，就得用时间当闸门。\n"
            + "示例代码：\n"
            + "    GlobalCode { float lastTick = 0f; }   // 跨帧记住上次打印的时刻\n"
            + "    float now = Time.time;\n"
            + "    if (now - lastTick >= 0.5f)\n"
            + "    {\n"
            + "        lastTick = now;\n"
            + "        tower.PrintLine(\"tick {0:F2}s dt={1:F4}\", now, Time.deltaTime);\n"
            + "    }\n\n"
            + "目标：用 Time.time 读当前时刻、用 Time.deltaTime 读帧间隔，写出「每 0.5 秒才打一行」的限频。\n"
            + "提示：lastTick 必须是跨帧存活的变量（放 GlobalCode 块里）；写在 Run 里面的 float 每帧都会重新归零，限频会失效。\n"
            + "判定：代码里出现 Time.time / Time.deltaTime（含 unscaledDeltaTime）。",
            "Signature: static float Time.time        // seconds since launch (scaled by timeScale); grows every frame\n"
            + "static float Time.deltaTime   // seconds the last frame took, about 0.0167 at 60FPS\n"
            + "Note: Run executes every frame; to print or fire once every 0.5s you need time as a gate.\n"
            + "Example Code:\n"
            + "    GlobalCode { float lastTick = 0f; }   // remembers the last print across frames\n"
            + "    float now = Time.time;\n"
            + "    if (now - lastTick >= 0.5f)\n"
            + "    {\n"
            + "        lastTick = now;\n"
            + "        tower.PrintLine(\"tick {0:F2}s dt={1:F4}\", now, Time.deltaTime);\n"
            + "    }\n\n"
            + "Goal: read the current moment with Time.time and the frame gap with Time.deltaTime to throttle to one line every 0.5s.\n"
            + "Hint: lastTick must live across frames (put it in a GlobalCode block); a float declared inside Run resets every frame and the throttle breaks.\n"
            + "Check: Time.time / Time.deltaTime appears (unscaledDeltaTime also counts).",
            (raw, code) => ReTimeApi.IsMatch(code));

        list[7] = Def(
            "任务 8：内置 API · Mathf.Clamp / Min / Max / Abs 取最近目标并夹取",
            "Task 8: Built-in API - Mathf.Clamp / Min / Max / Abs",
            "签名：static float Mathf.Clamp(float v, float min, float max);   // 夹进区间\n"
            + "      static float Mathf.Min(float a, float b);   // 取较小值（Mathf.Max 取较大值）\n"
            + "      static float Mathf.Abs(float v);            // 绝对值\n"
            + "说明：找「最靠前的方块」要一边遍历一边压小候选值，这正是 Mathf.Min；参数越界就用 Clamp 兜住。\n"
            + "示例代码：\n"
            + "    float nearest = float.MaxValue;\n"
            + "    int   nearestId = -1;\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        float gap = Mathf.Abs(o.Position.x);   // 到原点炮台的轴向距离\n"
            + "        if (gap < nearest)\n"
            + "        {\n"
            + "            nearest = gap;\n"
            + "            nearestId = o.Index;\n"
            + "        }\n"
            + "        nearest = Mathf.Min(nearest, gap);\n"
            + "    }\n"
            + "    tower.PrintLine(\"nearest #{0} gap={1:F2}\", nearestId, Mathf.Clamp(nearest, 0f, 100f));\n\n"
            + "目标：用 Mathf.Abs 求轴向距离、用 Mathf.Min 迭代压小最小值、用 Mathf.Clamp 把结果夹进合理区间。\n"
            + "提示：float 初值用 float.MaxValue，才能保证第一个方块就比它小；用 0f 当初值则永远找不到最近的那个。\n"
            + "判定：代码里出现 Mathf.Clamp / Clamp01 / Min / Max / Abs 之一。",
            "Signature: static float Mathf.Clamp(float v, float min, float max);   // clamp into a range\n"
            + "static float Mathf.Min(float a, float b);   // smaller value (Mathf.Max for larger)\n"
            + "static float Mathf.Abs(float v);            // absolute value\n"
            + "Note: picking the front-most block means shrinking a candidate while looping - exactly Mathf.Min; Clamp guards the range.\n"
            + "Example Code:\n"
            + "    float nearest = float.MaxValue;\n"
            + "    int   nearestId = -1;\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        float gap = Mathf.Abs(o.Position.x);   // axial distance from the tower at the origin\n"
            + "        if (gap < nearest)\n"
            + "        {\n"
            + "            nearest = gap;\n"
            + "            nearestId = o.Index;\n"
            + "        }\n"
            + "        nearest = Mathf.Min(nearest, gap);\n"
            + "    }\n"
            + "    tower.PrintLine(\"nearest #{0} gap={1:F2}\", nearestId, Mathf.Clamp(nearest, 0f, 100f));\n\n"
            + "Goal: use Mathf.Abs for the distance, Mathf.Min to keep shrinking the minimum, and Mathf.Clamp to keep it in range.\n"
            + "Hint: start with float.MaxValue so the first block already beats it; starting at 0f means nothing is ever nearer.\n"
            + "Check: one of Mathf.Clamp / Clamp01 / Min / Max / Abs appears.",
            (raw, code) => ReMathfApi.IsMatch(code));

        list[8] = Def(
            "任务 9：内置 API · Vector3 运算（Distance / normalized / magnitude / 向量相减）",
            "Task 9: Built-in API - Vector3 math (Distance / normalized / magnitude / subtraction)",
            "签名：static float Vector3.Distance(Vector3 a, Vector3 b);   // 两点间距离\n"
            + "      Vector3.normalized   // 同方向的单位向量（长度 1）\n"
            + "      Vector3.magnitude    // 向量长度（标量）\n"
            + "      a - b                // 向量相减 = 「从 b 指向 a」的方向向量\n"
            + "说明：炮台在原点，所以 o.Position - 原点 就是瞄准方向；normalized 之后才适合当 Fire 的法向量。\n"
            + "示例代码：\n"
            + "    Vector3 dir = o.Position - new Vector3(0f, 0f, 0f);   // 原点 → 方块的方向向量\n"
            + "    Vector3 aim = dir.normalized;                          // 单位化，长度变 1 方向不变\n"
            + "    float dist  = Vector3.Distance(o.Position, new Vector3(0f, 0f, 0f));\n"
            + "    tower.PrintLine(\"dist={0:F2} len={1:F2} aim=({2:F2},{3:F2},{4:F2})\",\n"
            + "        dist, dir.magnitude, aim.x, aim.y, aim.z);\n\n"
            + "目标：用向量相减求瞄准方向、用 normalized 单位化、用 Distance / magnitude 求距离。\n"
            + "提示：Distance(a, b) 等于 (a - b).magnitude，两者选一个就行；炮台在原点时它也就是 o.Position.magnitude。\n"
            + "判定：出现（Vector3.Distance( 或 .Position - 相减）且（.normalized 或 Vector3.Distance( 或 .magnitude）。",
            "Signature: static float Vector3.Distance(Vector3 a, Vector3 b);   // distance between two points\n"
            + "Vector3.normalized   // unit vector, same direction, length 1\n"
            + "Vector3.magnitude    // vector length as a scalar\n"
            + "a - b                // subtraction gives the direction vector pointing from b to a\n"
            + "Note: the tower sits at the origin, so o.Position minus the origin is the aiming direction; normalize it before using it as Fire's normal.\n"
            + "Example Code:\n"
            + "    Vector3 dir = o.Position - new Vector3(0f, 0f, 0f);   // direction from the origin to the block\n"
            + "    Vector3 aim = dir.normalized;                          // unit length, direction unchanged\n"
            + "    float dist  = Vector3.Distance(o.Position, new Vector3(0f, 0f, 0f));\n"
            + "    tower.PrintLine(\"dist={0:F2} len={1:F2} aim=({2:F2},{3:F2},{4:F2})\",\n"
            + "        dist, dir.magnitude, aim.x, aim.y, aim.z);\n\n"
            + "Goal: subtract to get the aim direction, normalize it, and measure distance with Distance / magnitude.\n"
            + "Hint: Distance(a, b) equals (a - b).magnitude - use either; with the tower at the origin it is also o.Position.magnitude.\n"
            + "Check: (Vector3.Distance( or a .Position - subtraction) AND (.normalized or Vector3.Distance( or .magnitude).",
            (raw, code) => (ReVectorDistance.IsMatch(code) || ReVectorSubtract.IsMatch(code))
                && (ReVectorNormalized.IsMatch(code) || ReVectorDistance.IsMatch(code) || ReVectorMagnitude.IsMatch(code)));

        list[9] = Def(
            "任务 10：GetFireCooldown 冷却查询",
            "Task 10: GetFireCooldown",
            "签名：float GetFireCooldown();   // 距下一发可发射的剩余秒数，0 = 已就绪\n"
            + "示例代码：\n"
            + "    float cd = tower.GetFireCooldown();\n"
            + "    if (cd <= 0f)\n"
            + "        tower.PrintLine(\"ready to fire\");\n\n"
            + "目标：调用 GetFireCooldown 并根据冷却决定是否开火。\n"
            + "提示：冷却未好时 Fire 会返回 false，先判断冷却能省掉无效调用。\n"
            + "判定：出现「tower.GetFireCooldown(」。",
            "Signature: float GetFireCooldown();   // seconds until next shot; 0 = ready\n"
            + "Example Code:\n"
            + "    float cd = tower.GetFireCooldown();\n"
            + "    if (cd <= 0f)\n"
            + "        tower.PrintLine(\"ready to fire\");\n\n"
            + "Goal: call GetFireCooldown and gate firing on it.\n"
            + "Hint: Fire returns false while cooling down.\n"
            + "Check: \"tower.GetFireCooldown(\" appears.",
            (raw, code) => ReGetFireCooldown.IsMatch(code));

        list[10] = Def(
            "任务 11：Fire 发射",
            "Task 11: Fire",
            "签名：bool Fire(Vector3 normal);\n"
            + "说明：参数是一个 Vector3 向量，就是发射方向（同时也是过原点平面的法线）；冷却中返回 false。\n"
            + "约束：平面必过原点（炮台就在原点），所以只传一个方向向量，不用管平面方程里的 D。\n"
            + "示例代码：\n"
            + "    bool fired = tower.Fire(new Vector3(1f, 0f, 0f));   // 法线 (1,0,0)，沿 +X 发射\n"
            + "    tower.PrintLine(\"fired={0}\", fired);\n\n"
            + "目标：调用一次 Fire 发射。\n"
            + "提示：传进去的向量就是方向，不能是零向量 new Vector3(0f, 0f, 0f)；要打某个目标，直接传它的坐标。\n"
            + "判定：出现「tower.Fire(」。",
            "Signature: bool Fire(Vector3 normal);\n"
            + "Note: the single argument is a Vector3 that is the firing direction (also the normal of the origin-passing plane); false while cooling down.\n"
            + "Constraint: the plane always passes through the origin (the tower is at the origin), so you pass one direction vector and never a plane D.\n"
            + "Example Code:\n"
            + "    bool fired = tower.Fire(new Vector3(1f, 0f, 0f));   // normal (1,0,0): fires along +X\n"
            + "    tower.PrintLine(\"fired={0}\", fired);\n\n"
            + "Goal: call Fire once.\n"
            + "Hint: the vector you pass is the direction and must not be zero (new Vector3(0f, 0f, 0f)); to aim at a target just pass its position.\n"
            + "Check: \"tower.Fire(\" appears.",
            (raw, code) => ReFire.IsMatch(code));

        list[11] = Def(
            "任务 12：实战组合（遍历 + 判断 + 发射）",
            "Task 12: Combine (loop + if + fire)",
            "把三个 API 串起来：遍历对象 → 判断类型 → 打最靠前的一个\n"
            + "示例代码：\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\" && o.Position.x > 0f)\n"
            + "        {\n"
            + "            // 目标坐标即法向量\n"
            + "            tower.Fire(o.Position);\n"
            + "            break;\n"
            + "        }\n"
            + "    }\n\n"
            + "目标：同一段代码里同时出现 foreach、if 判断与 Fire 发射。\n"
            + "提示：命中一个就 break，避免一帧内重复调用浪费冷却；把目标的坐标当法向量传给 Fire 即可瞄准它。\n"
            + "判定：同时出现 foreach、if 与 tower.Fire(。",
            "Chain three APIs: iterate → filter by type → shoot one target\n"
            + "Example Code:\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        if (o.Type == \"ATTACK\" && o.Position.x > 0f)\n"
            + "        {\n"
            + "            // target position is the normal\n"
            + "            tower.Fire(o.Position);\n"
            + "            break;\n"
            + "        }\n"
            + "    }\n\n"
            + "Goal: have foreach, an if test and Fire in the same code.\n"
            + "Hint: break after the first hit to avoid wasting cooldown; pass the target position as the normal to aim at it.\n"
            + "Check: foreach, if and tower.Fire( all appear.",
            (raw, code) => ReForeach.IsMatch(code) && ReIf.IsMatch(code) && ReFire.IsMatch(code));

        list[12] = Def(
            "任务 13：剩余字段（MaxHits / Size / InitialVolume / Params）",
            "Task 13: Remaining fields (MaxHits / Size / InitialVolume / Params)",
            "字段：int MaxHits;           // 初始受击层数（出场时是多少层，计分用，切割后不变）\n"
            + "      Vector3 Size;         // 当前三边尺寸（越挨打越小；激光路径求交就靠它）\n"
            + "      float InitialVolume;  // 初始体积（计分用，切割后不变），和 Volume 配合算剩余比例\n"
            + "      string Params;        // 参数摘要字符串：尺寸 / 速度 / 受击进度，已拼好，直接打印\n"
            + "示例代码：\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0} hits={1}/{2} vol={3:F2}/{4:F2} size={5}\",\n"
            + "            o.Index, o.HP, o.MaxHits, o.Volume, o.InitialVolume, o.Size);\n"
            + "        tower.PrintLine(\"  {0}\", o.Params);\n"
            + "    }\n\n"
            + "目标：同一段代码里用上 MaxHits、Size、InitialVolume、Params 四个成员。\n"
            + "提示：MaxHits 是出场层数、HP 是剩余层数，MaxHits - HP 才是已经挨了几次；Size 是 Vector3，直接打印得到 (x, y, z)；Params 已经是拼好的字符串，套 {0:F1} 数值格式会报 FormatException。\n"
            + "判定：.MaxHits、.Size、.InitialVolume、.Params 四样同时出现。",
            "Fields: int MaxHits;   // initial hit layers (as spawned; scoring only, constant after cutting)\n"
            + "Vector3 Size;   // current edge sizes (shrinks when hit; used for laser path intersection)\n"
            + "float InitialVolume;   // initial volume (scoring only, constant) - pair with Volume for a ratio\n"
            + "string Params;   // ready-made summary string: size / speed / hit progress\n"
            + "Example Code:\n"
            + "    foreach (var o in tower.GetAllObjects())\n"
            + "    {\n"
            + "        tower.PrintLine(\"#{0} hits={1}/{2} vol={3:F2}/{4:F2} size={5}\",\n"
            + "            o.Index, o.HP, o.MaxHits, o.Volume, o.InitialVolume, o.Size);\n"
            + "        tower.PrintLine(\"  {0}\", o.Params);\n"
            + "    }\n\n"
            + "Goal: use all four members MaxHits, Size, InitialVolume and Params in one snippet.\n"
            + "Hint: MaxHits is the spawn layers and HP the remaining ones - MaxHits - HP is how many hits it took; Size is a Vector3 so printing it gives (x, y, z); Params is already a string, so applying {0:F1} to it throws FormatException.\n"
            + "Check: .MaxHits, .Size, .InitialVolume and .Params all appear.",
            (raw, code) => ReFieldMaxHits.IsMatch(code) && ReFieldSize.IsMatch(code)
                && ReFieldInitialVolume.IsMatch(code) && ReFieldParams.IsMatch(code));

        list[13] = Def(
            "任务 14：对象调试输出（ToString / Count / objects[i]）",
            "Task 14: Object debug output (ToString / Count / objects[i])",
            "签名：override string ToString();   // 一行摘要：{#编号 类型, HP=.., Position=.., Velocity=.., [Params]}\n"
            + "      int Count;                     // 列表里的对象个数（List 的属性，不带括号）\n"
            + "      objects[i]                     // 按下标取第 i 个对象（0 起，i 越界会抛异常）\n"
            + "示例代码：\n"
            + "    var objects = tower.GetAllObjects();\n"
            + "    tower.PrintLine(\"count={0}\", objects.Count);\n"
            + "    if (objects.Count > 0)\n"
            + "        tower.PrintLine(objects[0].ToString());   // ToString 可直接当 PrintLine 的实参\n\n"
            + "目标：用 Count 数个数、用 objects[i] 取单个对象、用 ToString() 把它的完整摘要打出来。\n"
            + "提示：ToString() 的返回值本身就是 string，直接当 PrintLine 的实参最省事；Index 是生成编号不是下标，按下标取必须用 objects[i]，且 i 要小于 Count。\n"
            + "判定：同时出现 .ToString()、.Count 与 objects[i].成员 形式的索引取值。",
            "Signature: override string ToString();   // one-line summary: {#index type, HP=.., Position=.., Velocity=.., [Params]}\n"
            + "int Count;   // number of objects in the list (List property, no parentheses)\n"
            + "objects[i]   // the i-th object by index (0-based; out-of-range throws)\n"
            + "Example Code:\n"
            + "    var objects = tower.GetAllObjects();\n"
            + "    tower.PrintLine(\"count={0}\", objects.Count);\n"
            + "    if (objects.Count > 0)\n"
            + "        tower.PrintLine(objects[0].ToString());   // ToString fits straight into PrintLine\n\n"
            + "Goal: count with Count, pick one object with objects[i], and print its full summary via ToString().\n"
            + "Hint: ToString() already returns a string, so pass it straight to PrintLine; Index is a spawn serial, not an index - use objects[i] and keep i below Count.\n"
            + "Check: .ToString(), .Count and an objects[i] style index access all appear.",
            (raw, code) => ReObjToString.IsMatch(code) && ReListCount.IsMatch(code)
                && ReIndexAccess.IsMatch(code));

        list[14] = Def(
            "任务 15：GameStart 让游戏开始（开局开关 · 幂等）",
            "Task 15: GameStart - start the level (idempotent switch)",
            "签名：void GameStart();   // 本关正式开始（开局开关，调一次就够）\n"
            + "为什么游戏不会自己开始：\n"
            + "  正式关卡（Level 3 - Part 1）进关后，引擎把发块器按住，状态是「等待玩家开场」——方块一个都不会生成；\n"
            + "  只有你的代码调用 tower.GameStart() 之后，发块器才被放开，方块开始飞出来。\n"
            + "示例代码：\n"
            + "    GlobalCode { bool gameStarted = false; }   // 全局区：跨帧保留的一次性标记\n"
            + "    if (!gameStarted)\n"
            + "    {\n"
            + "        gameStarted = true;   // 先关门：后面的帧就不会再进\n"
            + "        tower.GameStart();    // 删掉这一行，游戏就不会开始\n"
            + "    }\n\n"
            + "目标：在代码里调用一次 tower.GameStart()（用 GlobalCode 标记或条件保证只调一次）。\n"
            + "提示：Run(tower) 每帧都会执行，所以裸写一行 GameStart() 也能跑（它幂等，重复调用没有副作用）；但正确写法是配 GlobalCode 标记只调一次，读起来意图清楚。本关是教学关、方块在你学完后自动放开，所以这里调用看不到开局效果；正式关卡里它就是唯一的开场开关。\n"
            + "判定：出现「tower.GameStart(」。",
            "Signature: void GameStart();   // starts this level for real (call it once)\n"
            + "Why the game does not start by itself:\n"
            + "  In a real level (Level 3 - Part 1) the engine holds the block spawner on entry - the state is \"waiting for the player to start\", so zero blocks spawn.\n"
            + "  Only after your code calls tower.GameStart() is the spawner released and blocks start flying.\n"
            + "Example Code:\n"
            + "    GlobalCode { bool gameStarted = false; }   // global block: one-shot flag that survives frames\n"
            + "    if (!gameStarted)\n"
            + "    {\n"
            + "        gameStarted = true;   // close the door first, later frames skip\n"
            + "        tower.GameStart();    // delete this line and the game never starts\n"
            + "    }\n\n"
            + "Goal: call tower.GameStart() once (use a GlobalCode flag or a condition so it runs only once).\n"
            + "Hint: Run(tower) fires every frame, so a bare GameStart() line still works (it is idempotent, repeated calls have no side effects); the clean way is a GlobalCode flag so the intent is obvious. This is a tutorial level and its blocks are released when you finish, so the call shows no instant effect here - in a real level it is the only start switch.\n"
            + "Check: \"tower.GameStart(\" appears.",
            (raw, code) => ReGameStart.IsMatch(code));

        AppendApiDetailPages(list);
        AppendApiDiagramPages(list); // 第 13、14 项与收官项 15 的第 3 页：成员全景 / 索引取对象 / 开局流水线这类「没有字节可数」的知识点改用逻辑图

        return list;
    }

    /// <summary>
    /// 炮台 API 课（Level 2 - Part 2，15 项）的第 2 页：不写泛泛概念，只写「这一页亲手敲什么 / 每个符号什么意思 /
    /// 新手一定踩的坑（含真实报错编号）」，与语法课 AppendGrammarDetailPages 的结构保持一致。
    /// 第 1 页仍是签名 + 示例 + 判定条件，第 2 页是照着做就能跑通的操作指引。
    /// </summary>
    private static void AppendApiDetailPages(TaskDef[] list)
    {
        // ---------- 任务 1：Print 不换行 ----------
        list[0].Page2Zh = "【这一页：亲手敲出两段拼在同一行的话】\n"
            + "① 光标点进代码区，敲 tower.Print(\"hp={0} \", 7); 回车。\n"
            + "② 再敲 tower.Print(\"count={0}\", 42); 回车（第一句末尾故意留了个空格，别删）。\n"
            + "③ 点【编译运行 (F6)】：编辑器自动关闭，抬头看 +X 正前方那块荧幕。\n"
            + "④ 荧幕上只有一行「hp=7 count=42」，两句话拼成一行——这就是 Print 的全部意义。\n"
            + "【逐符号读懂它】\n"
            + "· tower：炮台对象，引擎已经作为参数 Run(CodeTower tower) 传进来，直接用，不要自己 new。\n"
            + "· .Print( )：方法调用，括号里叫实参。\n"
            + "· \"hp={0} \"：双引号里是格式串，{0} 是第 0 号占位符，会被第一个实参 7 填进去。\n"
            + "· 7 与 42：实参，按顺序填 {0}、{1}。\n"
            + "· 行尾的空格：Print 不换行，空格是唯一的分隔手段，删了就变成 hp=7count=42。\n"
            + "· ; 分号：一句话结束，漏了报 CS1002。\n"
            + "【三个必踩的坑】\n"
            + "✘ 中文输入法敲出「“hp={0}”」→ 报 CS1010/CS1056，引号必须英文。\n"
            + "✘ 格式串里 {0} {1} 写了但实参给少了 → 运行期抛 FormatException，编辑器当场报错停下。\n"
            + "✔ Run 每帧都执行：无条件 Print 会在同一行越拼越长，实测几秒后整行被挤出屏幕。";
        list[0].Page2En = "THIS PAGE: TYPE TWO PRINTS THAT LAND ON ONE LINE\n"
            + "1) Click into the code area and type tower.Print(\"hp={0} \", 7); then Enter.\n"
            + "2) Type tower.Print(\"count={0}\", 42); then Enter (keep the trailing space in line 1).\n"
            + "3) Press [Compile & Run (F6)]; the editor closes and you look at the +X screen.\n"
            + "4) The screen shows one single line: hp=7 count=42 - that is what Print is for.\n"
            + "READ EVERY SYMBOL\n"
            + "- tower: the tower object, already passed in as Run(CodeTower tower); never new it yourself.\n"
            + "- .Print( ): a method call; whatever is inside the parentheses are arguments.\n"
            + "- \"hp={0} \": a format string; {0} is placeholder zero, filled by the first argument 7.\n"
            + "- 7 and 42: arguments, filled into {0} and {1} in order.\n"
            + "- the trailing space: Print never breaks the line, the space is your only separator.\n"
            + "- ; semicolon: ends the statement; missing it gives CS1002.\n"
            + "THREE TRAPS\n"
            + "- Curly quotes from a Chinese IME give CS1010/CS1056; use straight quotes.\n"
            + "- More placeholders than arguments throws FormatException at run time and stops the editor.\n"
            + "- Run executes every frame, so an unconditional Print keeps growing that one line until it scrolls off.";
        // ---------- 任务 2：PrintLine 换行 ----------
        list[1].Page2Zh = "【这一页：让荧幕出现两行独立的话】\n"
            + "① 敲 tower.PrintLine(\"hp={0}\", 7); 回车。\n"
            + "② 再敲 tower.PrintLine(\"tower ready\"); 回车（这句没有占位符，不用给实参）。\n"
            + "③ F6 运行，荧幕从上到下是两行：hp=7 / tower ready。\n"
            + "④ 对比上一项：同样两句，用 PrintLine 就分了两行。\n"
            + "【逐符号读懂它】\n"
            + "· PrintLine 末尾自带换行，等价于 Print(text) 再补一个换行符。\n"
            + "· 只有一串纯文字时，括号里直接放 \"文字\"，不必写 {0}。\n"
            + "· 荧幕上限 24 行（引擎常量 PrintMaxLines），再多的行会从最旧的开始丢弃。\n"
            + "· 每次 Run 每帧执行一次，所以每帧至少多一行 → 一秒几十行，几秒就刷满 24 行。\n"
            + "【三个必踩的坑】\n"
            + "✘ 想换行却写了 Print → 所有输出挤在同一行，越挤越长。\n"
            + "✘ 在 Run 里无条件 PrintLine → 屏幕疯狂滚动，看不到自己关心的那行；要用 if 条件限频。\n"
            + "✔ 想在循环里只打一次，先把结果存变量，循环外再 PrintLine。";
        list[1].Page2En = "THIS PAGE: MAKE THE SCREEN SHOW TWO SEPARATE LINES\n"
            + "1) Type tower.PrintLine(\"hp={0}\", 7); then Enter.\n"
            + "2) Type tower.PrintLine(\"tower ready\"); then Enter (no placeholder, no arguments).\n"
            + "3) Press F6: the screen reads hp=7 on one line and tower ready on the next.\n"
            + "4) Compare with the previous task: same text, but PrintLine splits the lines.\n"
            + "READ EVERY SYMBOL\n"
            + "- PrintLine appends a newline; it equals Print(text) plus a line break.\n"
            + "- For plain text just put \"text\" in the parentheses; {0} is only needed with arguments.\n"
            + "- The screen keeps 24 lines (PrintMaxLines); older lines are dropped first.\n"
            + "- Run fires every frame, so you add at least one line per frame - the 24 lines fill in seconds.\n"
            + "THREE TRAPS\n"
            + "- Using Print when you wanted a newline piles everything onto one ever-growing line.\n"
            + "- An unconditional PrintLine inside Run floods the screen; gate it with an if.\n"
            + "- To print once from inside a loop, collect the value first and print after the loop.";
        // ---------- 任务 3：ClearPrint ----------
        list[2].Page2Zh = "【这一页：先写满，再一次清空】\n"
            + "① 先敲两行 tower.PrintLine(\"line A\"); 和 tower.PrintLine(\"line B\");。\n"
            + "② 在这两行下面敲 tower.ClearPrint();。\n"
            + "③ F6 运行：荧幕空白一片——因为同一次执行里，打印完立刻被清掉了。\n"
            + "④ 把 ClearPrint 挪到最上面（第一句），再运行：荧幕只剩 line A / line B。\n"
            + "【逐符号读懂它】\n"
            + "· 顺序即结果：代码从上往下一句句执行，谁在后面谁生效。\n"
            + "· ClearPrint() 括号里没有参数，连空括号也要写。\n"
            + "· 常见正确用法：每帧先 ClearPrint() 再打印本帧状态 → 荧幕永远只显示「当前这一帧」。\n"
            + "【三个必踩的坑】\n"
            + "✘ ClearPrint 放在 PrintLine 之后 → 刚打的东西立刻被抹掉，看起来像「打印失败」。\n"
            + "✘ 写成 Clear / ClearScreen / clearPrint → 报 CS1061 找不到成员（C# 区分大小写）。\n"
            + "✔ 只想清一次：用 if 判断条件（例如计数到某个值）再调，别每帧无条件清。";
        list[2].Page2En = "THIS PAGE: FILL THE SCREEN, THEN WIPE IT ONCE\n"
            + "1) Type tower.PrintLine(\"line A\"); and tower.PrintLine(\"line B\");.\n"
            + "2) Below them type tower.ClearPrint();.\n"
            + "3) Press F6: the screen stays blank, because the prints were wiped in the same pass.\n"
            + "4) Move ClearPrint to the very top and run again: now line A / line B survive.\n"
            + "READ EVERY SYMBOL\n"
            + "- Order is the result: statements run top to bottom, the last one wins.\n"
            + "- ClearPrint() takes no arguments but still needs the empty parentheses.\n"
            + "- The usual correct pattern: ClearPrint() first, then print this frame's state.\n"
            + "THREE TRAPS\n"
            + "- ClearPrint after your prints erases everything, which looks like printing is broken.\n"
            + "- Clear / ClearScreen / clearPrint give CS1061 (C# is case sensitive).\n"
            + "- To clear only once, guard it with an if instead of calling it every frame.";
        // ---------- 任务 4：遍历 + 读身份（GetAllObjects / Index / Type） ----------
        list[3].Page2Zh = "【这一页：一段循环同时拿到列表、编号和颜色】\n"
            + "① 先敲骨架：foreach (var o in tower.GetAllObjects()) { }，回车后把光标放回花括号中间。\n"
            + "② 括号里敲 tower.PrintLine(\"#{0} {1}\", o.Index, o.Type); 回车。\n"
            + "③ 点【编译运行 (F6)】：场上有几个方块，荧幕就有几行「#3 ATTACK」这样的字。\n"
            + "④ 想只看红块，就在 PrintLine 前面加 if (o.Type == \"ATTACK\")，蓝块自然被过滤掉。\n"
            + "【逐符号读懂它】\n"
            + "· tower.GetAllObjects( )：向炮台要一份当前飞行方块的列表，返回 List<TowerObjectInfo>，括号里不传参数。\n"
            + "· foreach (var o in 列表)：把列表里的元素一个个取出来交给 o，var 让编译器自己推断类型 TowerObjectInfo。\n"
            + "· o.Index：方块生成时领到的编号，全局递增，被击毁的编号不回收，所以荧幕上可能跳号。\n"
            + "· o.Type：字符串，只有 \"ATTACK\"（红）和 \"BOMB\"（蓝）两种取值。\n"
            + "· \"#{0} {1}\"：两个占位符按顺序吃后面的 o.Index 与 o.Type，中间那个空格是自己敲的分隔符。\n"
            + "【三个必踩的坑】\n"
            + "✘ 想按下标取：写 o[0] → TowerObjectInfo 不是集合，报 CS0021；写 GetAllObjects()[0] → 列表为空时直接抛异常。\n"
            + "✘ 写 o.Type == ATTACK（漏引号）→ 编译器把 ATTACK 当变量名，报 CS0103 当前上下文中不存在该名称。\n"
            + "✔ 一个循环里要区分两种颜色，用 if / else if 链；两个并列 if 里都 PrintLine 会把同一块打两遍。";
        list[3].Page2En = "THIS PAGE: ONE LOOP THAT GETS THE LIST, THE ID AND THE COLOR\n"
            + "1) Type the skeleton: foreach (var o in tower.GetAllObjects()) { } and put the caret inside the braces.\n"
            + "2) Type tower.PrintLine(\"#{0} {1}\", o.Index, o.Type); and press Enter.\n"
            + "3) Press [Compile & Run (F6)]: as many lines as blocks appear, each like #3 ATTACK.\n"
            + "4) To keep only red blocks, put if (o.Type == \"ATTACK\") in front of the PrintLine.\n"
            + "READ EVERY SYMBOL\n"
            + "- tower.GetAllObjects( ): asks the tower for the list of flying blocks; takes no arguments, returns List<TowerObjectInfo>.\n"
            + "- foreach (var o in list): takes the elements one by one into o; var lets the compiler infer TowerObjectInfo.\n"
            + "- o.Index: the serial number handed out at spawn; numbers of destroyed blocks are never reused, so it may skip.\n"
            + "- o.Type: a string with exactly two values, \"ATTACK\" (red) and \"BOMB\" (blue).\n"
            + "- \"#{0} {1}\": two placeholders fed in order by o.Index and o.Type; the space between them is yours.\n"
            + "THREE TRAPS\n"
            + "- Indexing: o[0] gives CS0021 (not a collection); GetAllObjects()[0] throws when the list is empty.\n"
            + "- o.Type == ATTACK without quotes: the compiler reads ATTACK as a name and reports CS0103.\n"
            + "- Use an if / else if chain to split colors; two parallel ifs print the same block twice.";
        // ---------- 任务 5：读状态（HP / Position） ----------
        list[4].Page2Zh = "【这一页：把「还剩几层」和「人在哪」一起打出来】\n"
            + "① 照上一项敲好 foreach 骨架，循环体先留空。\n"
            + "② 循环体里敲 if (o.HP <= 1)，回车，再在它的大括号里敲打印语句。\n"
            + "③ 敲 tower.PrintLine(\"#{0} hp={1} at ({2:F1},{3:F1},{4:F1})\", o.Index, o.HP, o.Position.x, o.Position.y, o.Position.z);\n"
            + "④ F6 运行：荧幕只出现残血方块那几行，血量和三个坐标一次读全。\n"
            + "【逐符号读懂它】\n"
            + "· o.HP：int，剩余受击层数，各块不同、最多 6 层；层数吃光即被击毁。\n"
            + "· <= 1：判据写小于等于时，「只剩最后一层」的方块才算快死了；写 < 1 永远等不到（0 层已经消失）。\n"
            + "· o.Position：Vector3，方块几何中心的世界坐标——它是一个值，不是三个数。\n"
            + "· .x .y .z：点号从 Vector3 里取出一块 float；炮台在原点朝 +X 打，Position.x 越小越靠近炮台。\n"
            + "· {2:F1}：第 2 号占位符按 1 位小数格式化，坐标的浮点尾巴被切掉才不会挤爆一行。\n"
            + "【三个必踩的坑】\n"
            + "✘ 直接把 o.Position 塞进 {2} → 输出的是 (1.0, 0.0, 2.0) 这种整串文字，:F1 完全失效。\n"
            + "✘ 用 o.Position == new Vector3(1f, 0f, 0f) 判断位置 → 浮点相等几乎永不成立，要改成区间判断。\n"
            + "✔ 先按远近筛一眼：只在 if (o.Position.x < 5f) 里打印，远处的直接不刷屏。";
        list[4].Page2En = "THIS PAGE: PRINT HOW MANY LAYERS LEFT AND WHERE IT IS\n"
            + "1) Reuse the foreach skeleton from the previous task and leave the body empty.\n"
            + "2) Type if (o.HP <= 1), press Enter, then put the print inside its braces.\n"
            + "3) Type tower.PrintLine(\"#{0} hp={1} at ({2:F1},{3:F1},{4:F1})\", o.Index, o.HP, o.Position.x, o.Position.y, o.Position.z);\n"
            + "4) Press F6: only the nearly dead blocks show up, with HP and three coordinates at once.\n"
            + "READ EVERY SYMBOL\n"
            + "- o.HP: an int, the remaining hit layers; it varies per block (up to 6) and the block dies when they run out.\n"
            + "- <= 1: the last remaining layer counts as almost dead; with < 1 you would never see anything (0 layers already died).\n"
            + "- o.Position: a Vector3, the world-space center - one value, not three numbers.\n"
            + "- .x .y .z: the dot pulls one float out of the Vector3; the tower fires toward +X, so a smaller Position.x is closer.\n"
            + "- {2:F1}: formats placeholder two with one decimal, so the float tail cannot flood the line.\n"
            + "THREE TRAPS\n"
            + "- Passing o.Position into {2} prints \"(1.0, 0.0, 2.0)\" and :F1 does nothing at all.\n"
            + "- Comparing o.Position == new Vector3(...) for equality almost never holds with floats; test with ranges.\n"
            + "- Filter first: print only when if (o.Position.x < 5f), or distant blocks flood the screen.";
        // ---------- 任务 6：读运动与体量（Velocity / Volume） ----------
        list[5].Page2Zh = "【这一页：一眼看出「飞多快」和「值不值得打」】\n"
            + "① 敲好 foreach 骨架，循环体里敲 tower.PrintLine(\"#{0} speed={1:F2} vol={2:F2}/{3:F2}\", o.Index, o.Velocity.magnitude, o.Volume, o.InitialVolume);\n"
            + "② F6 运行：每行末尾是 vol=12.00/12.00 这种「当前体积 / 初始体积」。\n"
            + "③ 多打几发再看：被切过的方块分子变小、分母不动，两者之比就是剩余比例。\n"
            + "④ 想直接看比例，先在循环里敲 float ratio = o.Volume / o.InitialVolume; 再把 ratio 打进格式串。\n"
            + "【逐符号读懂它】\n"
            + "· o.Velocity：Vector3，速度向量 = 方向 × 速度，单位是「单位/秒」，它是向量不是标量。\n"
            + "· .magnitude：把向量折成一个数（勾股定理），得到标量速度；.normalized 则是同方向的单位向量（长度 1）。\n"
            + "· o.Volume：float，当前剩余体积，挨打就变小。\n"
            + "· o.InitialVolume：float，初始体积，被切割后依旧不变，所以适合当分母。\n"
            + "· o.MaxHits / o.Size：初始受击层数 / 当前三边尺寸，需要时同样用点号取。\n"
            + "【三个必踩的坑】\n"
            + "✘ 直接把 o.Velocity 当速度打印 → 得到一串向量文字；要速度大小就写 .magnitude。\n"
            + "✘ 为了「看着整齐」先强转 int 再相除 → 两个小数被截成整数，比值直接变 0。\n"
            + "✔ 算收益用 ratio 乘个常数就够，别把表达式塞进格式串里，{0} 只认值不认算式。";
        list[5].Page2En = "THIS PAGE: SEE HOW FAST IT FLIES AND WHETHER IT IS WORTH SHOOTING\n"
            + "1) With the foreach skeleton in place, type tower.PrintLine(\"#{0} speed={1:F2} vol={2:F2}/{3:F2}\", o.Index, o.Velocity.magnitude, o.Volume, o.InitialVolume);\n"
            + "2) Press F6: each line ends with something like vol=12.00/12.00 (current / initial).\n"
            + "3) Fire a few shots and look again: the numerator shrinks, the denominator stays, and the ratio is what is left.\n"
            + "4) For the ratio itself, type float ratio = o.Volume / o.InitialVolume; inside the loop and print ratio.\n"
            + "READ EVERY SYMBOL\n"
            + "- o.Velocity: a Vector3, velocity = direction x speed in units/sec - a vector, not a scalar.\n"
            + "- .magnitude: folds the vector into one number (Pythagoras) giving scalar speed; .normalized gives a length-1 vector in the same direction.\n"
            + "- o.Volume: a float, the current remaining volume that shrinks on every hit.\n"
            + "- o.InitialVolume: a float, constant after cutting, which makes it a safe denominator.\n"
            + "- o.MaxHits / o.Size: initial hit layers / current edge sizes; same dot access when needed.\n"
            + "THREE TRAPS\n"
            + "- Printing o.Velocity directly gives vector text; for speed use .magnitude.\n"
            + "- Casting to int before dividing truncates two decimals and the ratio collapses to 0.\n"
            + "- Keep expressions out of the format string: {0} takes a value, never an expression.";
        // ---------- 任务 7：Time.time / Time.deltaTime 限频计时 ----------
        list[6].Page2Zh = "【这一页：把「每帧都打印」改成「每 0.5 秒打印一次」】\n"
            + "① 在代码最前面敲 GlobalCode { float lastTick = 0f; }——这是跨帧记住「上次打印时刻」的变量。\n"
            + "② Run 里第一句敲 float now = Time.time;\n"
            + "③ 敲 if (now - lastTick >= 0.5f) 回车，花括号里先写 lastTick = now;\n"
            + "④ 下一行敲 tower.PrintLine(\"tick {0:F2}s dt={1:F4}\", now, Time.deltaTime); 再 F6：荧幕每隔半秒才多出一行。\n"
            + "【逐符号读懂它】\n"
            + "· Time.time：游戏启动到现在的秒数（float），每帧都在涨，是「绝对时刻」，适合当时间戳。\n"
            + "· Time.deltaTime：上一帧花了多少秒，60FPS 约 0.0167；把它乘上速度就是这一帧移动的距离。\n"
            + "· now - lastTick：距离上次执行过去了多久，>= 0.5f 就等于「到点了」。\n"
            + "· lastTick = now：把闸门推到现在，否则条件一直成立，每帧都会打印一行。\n"
            + "· GlobalCode { }：本作扩展语法，块里声明会被搬到类体变成字段，所以 lastTick 能跨帧存活。\n"
            + "【三个必踩的坑】\n"
            + "✘ 把 float lastTick = 0f; 写在 Run 里面 → 每帧重新归零，条件永远成立，限频等于没做。\n"
            + "✘ 写成 if (Time.time >= 0.5f) → 0.5 秒之后每帧都进分支，荧幕照样被刷满。\n"
            + "✔ 想改成每 N 秒一次，只要把 0.5f 换成 N 即可，公式固定为 if (Time.time - lastTick >= N) { lastTick = Time.time; ... }。";
        list[6].Page2En = "THIS PAGE: TURN PRINT-EVERY-FRAME INTO PRINT-EVERY-HALF-SECOND\n"
            + "1) At the very top type GlobalCode { float lastTick = 0f; } - the cross-frame memory of the last print.\n"
            + "2) First line of Run: float now = Time.time;\n"
            + "3) Type if (now - lastTick >= 0.5f) and start the braces with lastTick = now;\n"
            + "4) Next line: tower.PrintLine(\"tick {0:F2}s dt={1:F4}\", now, Time.deltaTime); Press F6: one new line every half second.\n"
            + "READ EVERY SYMBOL\n"
            + "- Time.time: seconds since launch (float), always growing - an absolute timestamp.\n"
            + "- Time.deltaTime: seconds the last frame took, about 0.0167 at 60FPS; times a speed it gives this frame's travel.\n"
            + "- now - lastTick: how long since the last run; >= 0.5f means it is time.\n"
            + "- lastTick = now: pushes the gate forward, otherwise the test stays true and prints every frame.\n"
            + "- GlobalCode { }: this project's extension - declarations are hoisted into class fields, so lastTick survives frames.\n"
            + "THREE TRAPS\n"
            + "- Declaring float lastTick = 0f; inside Run resets it every frame, so the throttle does nothing.\n"
            + "- Writing if (Time.time >= 0.5f) enters the branch every frame after 0.5s and floods the screen.\n"
            + "- For every N seconds just swap 0.5f for N: if (Time.time - lastTick >= N) { lastTick = Time.time; ... }.";
        // ---------- 任务 8：Mathf.Clamp / Min / Max / Abs ----------
        list[7].Page2Zh = "【这一页：一边遍历一边挑出最靠前的那个方块】\n"
            + "① 循环外敲 float nearest = float.MaxValue; 和 int nearestId = -1;——一个记距离、一个记编号。\n"
            + "② 循环里敲 float gap = Mathf.Abs(o.Position.x); 把「轴向距离」取成非负数。\n"
            + "③ 敲 if (gap < nearest) { nearest = gap; nearestId = o.Index; }——更近就换人。\n"
            + "④ 循环结束后敲 tower.PrintLine(\"nearest #{0} gap={1:F2}\", nearestId, Mathf.Clamp(nearest, 0f, 100f)); F6 看结果。\n"
            + "【逐符号读懂它】\n"
            + "· Mathf.Abs(v)：去符号；方块飞到炮台后方（x 为负）时，距离仍然是正数。\n"
            + "· Mathf.Min(a, b) / Mathf.Max(a, b)：返回较小 / 较大的那个，等价于 a < b ? a : b；写 nearest = Mathf.Min(nearest, gap); 和 ③ 的赋值效果一致。\n"
            + "· Mathf.Clamp(v, min, max)：小于 min 取 min、大于 max 取 max，防止把 3.4E+38 这种初值直接打进荧幕。\n"
            + "· float.MaxValue：float 能表示的最大值（约 3.4e38），拿它当「无穷远」的起点最省事。\n"
            + "· 一遍遍历只能记一个最优解：距离和编号必须同时更新，否则会打出别人的编号。\n"
            + "【三个必踩的坑】\n"
            + "✘ nearest 初值写 0f → 任何 gap 都不比它小，永远挑不到目标。\n"
            + "✘ 在循环里 PrintLine 每个方块一行 → 荧幕被刷空；把打印挪到循环结束之后。\n"
            + "✔ 想同时挑「血最少」的，在同一个循环里再维护一个 int minHp = 999; 即可，一次遍历拿两个条件。";
        list[7].Page2En = "THIS PAGE: PICK THE FRONT-MOST BLOCK WHILE LOOPING\n"
            + "1) Before the loop: float nearest = float.MaxValue; and int nearestId = -1; - one tracks distance, one the id.\n"
            + "2) Inside the loop: float gap = Mathf.Abs(o.Position.x); makes the axial distance non-negative.\n"
            + "3) if (gap < nearest) { nearest = gap; nearestId = o.Index; } - nearer block wins.\n"
            + "4) After the loop: tower.PrintLine(\"nearest #{0} gap={1:F2}\", nearestId, Mathf.Clamp(nearest, 0f, 100f)); then F6.\n"
            + "READ EVERY SYMBOL\n"
            + "- Mathf.Abs(v): drops the sign, so a block behind the tower (negative x) still reports a positive distance.\n"
            + "- Mathf.Min(a, b) / Mathf.Max(a, b): return the smaller / larger value, the same as a < b ? a : b; nearest = Mathf.Min(nearest, gap); equals step 3's assignment.\n"
            + "- Mathf.Clamp(v, min, max): floors at min and caps at max, so a sentinel like 3.4E+38 never reaches the screen.\n"
            + "- float.MaxValue: the largest float (about 3.4e38) - a convenient \"infinitely far\" start.\n"
            + "- One pass keeps one best answer: update distance and id together or you print another block's id.\n"
            + "THREE TRAPS\n"
            + "- Starting nearest at 0f means no gap is smaller and nothing is ever picked.\n"
            + "- Printing inside the loop writes one line per block and floods the screen; print after the loop.\n"
            + "- Want the weakest block too? Keep int minHp = 999; in the same pass - two conditions, one traversal.";
        // ---------- 任务 9：Vector3 运算（Distance / normalized / magnitude / 相减求瞄准方向） ----------
        list[8].Page2Zh = "【这一页：用向量相减算瞄准方向，用 Distance 算距离】\n"
            + "① 敲 Vector3 dir = o.Position - new Vector3(0f, 0f, 0f);——炮台就在原点，相减即「原点到方块」。\n"
            + "② 敲 Vector3 aim = dir.normalized; 把它压成长度 1 的单位向量。\n"
            + "③ 敲 float dist = Vector3.Distance(o.Position, new Vector3(0f, 0f, 0f)); 求两点距离（也可写 dir.magnitude）。\n"
            + "④ 敲 tower.PrintLine(\"dist={0:F2} aim=({1:F2},{2:F2},{3:F2})\", dist, aim.x, aim.y, aim.z); F6 对照一下数值。\n"
            + "【逐符号读懂它】\n"
            + "· 向量相减 a - b：三个分量各自相减，结果是「从 b 指向 a」的方向向量，它的长度就是两点距离。\n"
            + "· .magnitude：把 x / y / z 按勾股定理 √(x²+y²+z²) 折成一个长度标量。\n"
            + "· .normalized：各分量除以长度，方向不变、长度变 1；零向量归一化仍然是零向量。\n"
            + "· Vector3.Distance(a, b)：等价于 (a - b).magnitude，一步求距离；炮台在原点时它也就是 o.Position.magnitude。\n"
            + "· 为什么 Fire 要单位向量：平面法线只看方向不看长度，但单位化后参数直观，也不会出现超大数值。\n"
            + "【三个必踩的坑】\n"
            + "✘ 直接打印未归一化的 dir 当「方向」→ 数值看着像坐标，因为长度就是距离；方向要写 .normalized。\n"
            + "✘ 写成 o.Position - o.Position → 零向量，tower.Fire 会直接返回 false。\n"
            + "✔ 真要开火时一句就够：tower.Fire(dir.normalized); 方向和距离各归各位。";
        list[8].Page2En = "THIS PAGE: SUBTRACT FOR THE AIM DIRECTION AND USE DISTANCE FOR RANGE\n"
            + "1) Vector3 dir = o.Position - new Vector3(0f, 0f, 0f); - the tower sits at the origin, so this points origin to block.\n"
            + "2) Vector3 aim = dir.normalized; squeezes it to length 1.\n"
            + "3) float dist = Vector3.Distance(o.Position, new Vector3(0f, 0f, 0f)); gives the distance (dir.magnitude works too).\n"
            + "4) tower.PrintLine(\"dist={0:F2} aim=({1:F2},{2:F2},{3:F2})\", dist, aim.x, aim.y, aim.z); then F6 and compare the numbers.\n"
            + "READ EVERY SYMBOL\n"
            + "- Subtraction a - b: components subtract one by one and the result points from b to a; its length is the distance.\n"
            + "- .magnitude: folds x / y / z into one scalar with sqrt(x*x + y*y + z*z).\n"
            + "- .normalized: divides each component by the length - same direction, length 1; normalizing a zero vector still gives zero.\n"
            + "- Vector3.Distance(a, b): equals (a - b).magnitude, one call for range; with the tower at the origin it is also o.Position.magnitude.\n"
            + "- Why Fire wants a unit vector: a plane normal only cares about direction, but unit length is readable and avoids huge numbers.\n"
            + "THREE TRAPS\n"
            + "- Printing un-normalized dir as \"the direction\" looks like coordinates because its length is the distance; use .normalized.\n"
            + "- Writing o.Position - o.Position gives a zero vector and tower.Fire returns false.\n"
            + "- To actually shoot, one line: tower.Fire(dir.normalized); direction and range each in their own place.";
        // ---------- 任务 10：GetFireCooldown ----------
        list[9].Page2Zh = "【这一页：先问冷却，再决定开不开火】\n"
            + "① 敲 float cd = tower.GetFireCooldown(); 把剩余冷却秒数存下来。\n"
            + "② 敲 if (cd <= 0f) 回车，缩进写 tower.PrintLine(\"ready\");。\n"
            + "③ 在 else 分支里写 tower.PrintLine(\"cooling {0:F3}s\", cd);。\n"
            + "④ F6 运行：发射瞬间荧幕跳出 cooling 0.028s，冷却清零后又变回 ready。\n"
            + "【逐符号读懂它】\n"
            + "· 返回值是「距离下一发还能打，还差几秒」，0 表示现在就能打。\n"
            + "· 默认冷却 cooldownSeconds = 0.0285s，约每秒 35 发，比一帧（1/60≈0.0167s）长，所以每帧最多成功一发。\n"
            + "· 0f 里的 f 表示这是 float 字面量；和 double 比较会报 CS0019 类型不匹配。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写成 if (cd > 0f) 当就绪判断 → 正好反了，冷却中才 > 0。\n"
            + "✘ 在 Run 里无条件 PrintLine 冷却值 → 每帧一行，荧幕 24 行几秒被刷空。\n"
            + "✔ 冷却没到就别调 Fire：虽然它只会返回 false，但白白浪费一次判断。";
        list[9].Page2En = "THIS PAGE: ASK THE COOLDOWN BEFORE FIRING\n"
            + "1) Type float cd = tower.GetFireCooldown(); to store the seconds left.\n"
            + "2) Type if (cd <= 0f) then tower.PrintLine(\"ready\");.\n"
            + "3) In the else branch print tower.PrintLine(\"cooling {0:F3}s\", cd);.\n"
            + "4) Press F6: right after a shot the screen shows cooling 0.028s, then flips back to ready.\n"
            + "READ EVERY SYMBOL\n"
            + "- The return value is how many seconds until the next shot; 0 means ready now.\n"
            + "- Default cooldownSeconds = 0.0285s, about 35 shots per second - longer than a frame (1/60 = 0.0167s), so one shot per frame at most.\n"
            + "- The f in 0f marks a float literal; comparing with a double gives CS0019.\n"
            + "THREE TRAPS\n"
            + "- Testing if (cd > 0f) for readiness is inverted; cooling leaves it above 0.\n"
            + "- Printing the cooldown unconditionally inside Run writes one line per frame and flushes the 24 lines fast.\n"
            + "- Skip Fire while cooling: it only returns false and wastes a check.";
        // ---------- 任务 11：Fire ----------
        list[10].Page2Zh = "【这一页：真的把一发打出去】\n"
            + "① 敲 bool fired = tower.Fire(new Vector3(1f, 0f, 0f));——法向量 (1,0,0)，沿 +X 打一发。\n"
            + "② 敲 tower.PrintLine(\"fired={0}\", fired); 看这次有没有打出去。\n"
            + "③ F6 运行：看到激光从原点射出，荧幕显示 fired=True。\n"
            + "④ 改成 tower.Fire(new Vector3(0f, 1f, 0f)); 再运行：激光改成沿 +Y 向上打，证明传进去的向量就是方向。\n"
            + "【逐符号读懂它】\n"
            + "· 数学含义：沿平面 A*x+B*y+C*z=0 的法线方向 (A,B,C) 发射；平面必过原点（炮台在原点），所以只有三个参数、没有 D。\n"
            + "· 想打某个方块：直接把它的 Position 三分量传进去，tower.Fire(o.Position); 因为从原点指向目标的向量本身就是一个合法法向量。\n"
            + "· 返回 false 的两种情况：(A,B,C) 近似零向量，或者还在冷却中。\n"
            + "· 1f / 0f 的 f 不能省，否则按 double 传入，重载匹配不上报错。\n"
            + "【三个必踩的坑】\n"
            + "✘ 传 (0f, 0f, 0f) → 零向量直接返回 false，什么都不会发生。\n"
            + "✘ 一帧里连调三次想打三发 → 冷却 0.0285s 卡住，后两次全返回 false。\n"
            + "✔ 想打得更准：先按 Type 筛红块、按 Position.x 挑最近的一个，再传它的坐标。";
        list[10].Page2En = "THIS PAGE: ACTUALLY FIRE A SHOT\n"
            + "1) Type bool fired = tower.Fire(new Vector3(1f, 0f, 0f)); - normal (1,0,0), one shot along +X.\n"
            + "2) Type tower.PrintLine(\"fired={0}\", fired); to see whether it went out.\n"
            + "3) Press F6: the laser leaves the origin and the screen prints fired=True.\n"
            + "4) Change it to tower.Fire(new Vector3(0f, 1f, 0f)); and run again: the laser goes straight up, so the vector you pass is the direction.\n"
            + "READ EVERY SYMBOL\n"
            + "- Math: fire along the normal (A,B,C) of the plane A*x+B*y+C*z=0; the plane always passes the origin, so there is no D.\n"
            + "- To aim at a block pass its Position: tower.Fire(o.Position); the vector from the origin to the target is already a valid normal.\n"
            + "- false comes back when (A,B,C) is near zero or the tower is still cooling down.\n"
            + "- The f in 1f / 0f is required, otherwise the arguments are doubles and no overload matches.\n"
            + "THREE TRAPS\n"
            + "- Passing (0f, 0f, 0f) returns false immediately and nothing happens.\n"
            + "- Three calls in one frame cannot fire three shots; the 0.0285s cooldown rejects the last two.\n"
            + "- For accuracy, filter red blocks by Type, pick the nearest by Position.x, then pass its position.";
        // ---------- 任务 12：实战组合 ----------
        list[11].Page2Zh = "【这一页：把三件事串成一段能打的代码】\n"
            + "① 第一句写 tower.ClearPrint();——每帧先清屏，荧幕才不会越堆越乱。\n"
            + "② 写 foreach (var o in tower.GetAllObjects())，花括号里开始筛选。\n"
            + "③ 写 if (o.Type == \"ATTACK\" && o.Position.x > 0f)：只挑红块、且还在炮台前方（没飞过去）的。\n"
            + "④ 里面写 tower.Fire(o.Position); 再 break;，打一发就跳出循环。\n"
            + "【逐符号读懂它】\n"
            + "· && 把两个条件串起来：两个都成立才进 if。\n"
            + "· break 立刻结束整个 foreach，防止一帧内对多个目标浪费冷却。\n"
            + "· o.Position 的三分量直接当法向量传，等于「朝这个方块打」。\n"
            + "· Run 每帧都会跑一遍这段代码，于是炮台就变成了每帧自动找目标开火的自动炮台。\n"
            + "【三个必踩的坑】\n"
            + "✘ 忘了 break → 循环里对每个方块都调一次发射，冷却期内的调用全返回 false，白算。\n"
            + "✘ 用 o.Position.x > 0f 之外还想打远处的 → 条件写反（< 0）会去打已经飞过去、正在远离的方块。\n"
            + "✔ 进阶：想打最靠前的，先用 float best = float.MaxValue; 遍历比较 Position.x 取最小，循环结束后只发一次。";
        list[11].Page2En = "THIS PAGE: CHAIN THE THREE PIECES INTO WORKING CODE\n"
            + "1) First line: tower.ClearPrint(); so the screen never piles up across frames.\n"
            + "2) Write foreach (var o in tower.GetAllObjects()) and start filtering in the braces.\n"
            + "3) Write if (o.Type == \"ATTACK\" && o.Position.x > 0f): red blocks that are still in front of the tower.\n"
            + "4) Inside, call tower.Fire(o.Position); then break; - one shot, leave the loop.\n"
            + "READ EVERY SYMBOL\n"
            + "- && chains two tests: both must hold to enter the if.\n"
            + "- break ends the whole foreach at once, so one frame never burns cooldown on many targets.\n"
            + "- Passing the three components of o.Position as the normal means \"shoot at this block\".\n"
            + "- Run executes this block every frame, which turns the tower into an auto-turret.\n"
            + "THREE TRAPS\n"
            + "- Forgetting break calls Fire on every block; the calls inside the cooldown just return false.\n"
            + "- Flipping the test to < 0 shoots blocks that already flew past and are leaving.\n"
            + "- Next step: keep float best = float.MaxValue;, track the smallest Position.x, and fire once after the loop.";
        // ---------- 任务 13：剩余字段 MaxHits / Size / InitialVolume / Params ----------
        list[12].Page2Zh = "【这一页：把四个没见过的字段一次全打印出来】\n"
            + "① 先搭壳：写 foreach (var o in tower.GetAllObjects()) 和一对大括号。\n"
            + "② 括号里敲 tower.PrintLine(\"#{0} hits={1}/{2} vol={3:F2}/{4:F2}\", o.Index, o.HP, o.MaxHits, o.Volume, o.InitialVolume);。\n"
            + "③ 再补一行 tower.PrintLine(\"  size={0}\", o.Size); 和 tower.PrintLine(\"  {0}\", o.Params);。\n"
            + "④ 点【编译运行 (F6)】，对着荧幕数：hits=3/6 表示出场 6 层、还剩 3 层，size=(1.0, 1.0, 1.0) 就是三边尺寸。\n"
            + "【逐符号读懂它】\n"
            + "· o.MaxHits：出场时的受击层数，激光切一刀它不会变，所以拿它当分母算「还剩几成」。\n"
            + "· o.HP：剩余层数，会变小；MaxHits - HP 就是已经挨了几次。\n"
            + "· o.Size：Vector3，当前三边尺寸，方块被打小后它跟着变小。\n"
            + "· o.InitialVolume：初始体积，恒定；o.Volume / o.InitialVolume 就是剩余体积比例，漏块罚分按它算。\n"
            + "· o.Params：string，内部已经把尺寸 / 速度 / 受击进度拼成一句话，打印时占位符只能写 {0}。\n"
            + "【三个必踩的坑】\n"
            + "✘ 把 Params 当数字套格式 → tower.PrintLine(\"{0:F2}\", o.Params) 运行期抛 FormatException。\n"
            + "✘ 以为 Size 是 float 拿去做除法 → size / 2 报 CS0019，Vector3 要么取 .x 要么用 size.magnitude。\n"
            + "✔ 想判断「挨过打没有」别比较 Volume 和 0：用 o.HP < o.MaxHits 或 o.Volume < o.InitialVolume 更稳。";
        list[12].Page2En = "THIS PAGE: PRINT ALL FOUR UNSEEN FIELDS AT ONCE\n"
            + "1) Start the shell: foreach (var o in tower.GetAllObjects()) plus braces.\n"
            + "2) Inside, type tower.PrintLine(\"#{0} hits={1}/{2} vol={3:F2}/{4:F2}\", o.Index, o.HP, o.MaxHits, o.Volume, o.InitialVolume);.\n"
            + "3) Add tower.PrintLine(\"  size={0}\", o.Size); and tower.PrintLine(\"  {0}\", o.Params);.\n"
            + "4) Press F6 and read the screen: hits=3/6 means 6 layers at spawn with 3 left; size=(1.0, 1.0, 1.0) are the current edges.\n"
            + "READ EVERY SYMBOL\n"
            + "- o.MaxHits: hit layers at spawn; a laser cut never changes it, so use it as the denominator for \"how much is left\".\n"
            + "- o.HP: remaining layers, it shrinks; MaxHits - HP is the number of hits taken.\n"
            + "- o.Size: a Vector3 of the current edges; it shrinks as the block is cut down.\n"
            + "- o.InitialVolume: constant initial volume; o.Volume / o.InitialVolume is the remaining ratio used for leak penalties.\n"
            + "- o.Params: a string already built from size / speed / hit progress - only {0} may be used in the format.\n"
            + "THREE TRAPS\n"
            + "- Treating Params as a number: tower.PrintLine(\"{0:F2}\", o.Params) throws FormatException at runtime.\n"
            + "- Assuming Size is a float: size / 2 is CS0019; use size.x or size.magnitude instead.\n"
            + "- To test \"has it been hit\", prefer o.HP < o.MaxHits or o.Volume < o.InitialVolume over comparing Volume with 0.";
        // ---------- 任务 14：对象调试输出 ToString / Count / objects[i] ----------
        list[13].Page2Zh = "【这一页：三行代码把整张对象表看个明白】\n"
            + "① 敲 var objects = tower.GetAllObjects(); 先把列表存进一个变量（后面要用两次，别每次都去取）。\n"
            + "② 敲 tower.PrintLine(\"count={0}\", objects.Count); 数一数现在场上有几个对象。\n"
            + "③ 敲 if (objects.Count > 0) 换行缩进，写 tower.PrintLine(objects[0].ToString());。\n"
            + "④ 点【编译运行 (F6)】：荧幕先出一行 count=3，再出一行形如 {#12 ATTACK, HP=6, Position=(3.00,0.50,0.00), Velocity=(-1.00,0.00,0.00), [size=1.0 ...]}。\n"
            + "【逐符号读懂它】\n"
            + "· objects.Count：List 的属性，不带括号；写 objects.Count() 报 CS1955（不可调用的成员）。\n"
            + "· objects[0]：下标从 0 开始，取第一个；objects[objects.Count] 一定越界，最大只能到 Count - 1。\n"
            + "· o.Index 与 objects[i] 的区别：Index 是方块出生编号（可能早就涨到 200 多），下标是它此刻在列表里的位置（0、1、2…）。\n"
            + "· ToString()：TowerObjectInfo 重写过，返回的就是那一整行摘要；PrintLine 也认它（实参是 object 时自动转字符串）。\n"
            + "· 先 if 再取下标：列表为空时 objects[0] 抛 ArgumentOutOfRangeException，编辑器会当场报错停下。\n"
            + "【三个必踩的坑】\n"
            + "✘ 写 objects.Count() → CS1955，Count 是属性不是方法。\n"
            + "✘ 用 o.Index 当下标：objects[o.Index] 报 ArgumentOutOfRangeException，编号不是位置。\n"
            + "✔ 想一次看全：for (int i = 0; i < objects.Count; i++) tower.PrintLine(objects[i].ToString()); —— 数量与下标配合才不会越界。";
        list[13].Page2En = "THIS PAGE: THREE LINES THAT SHOW THE WHOLE OBJECT TABLE\n"
            + "1) var objects = tower.GetAllObjects(); store the list once (you need it twice; do not fetch it again).\n"
            + "2) tower.PrintLine(\"count={0}\", objects.Count); to count what is on screen.\n"
            + "3) if (objects.Count > 0) on its own line, then tower.PrintLine(objects[0].ToString()); indented.\n"
            + "4) Press F6: one line count=3, then a line like {#12 ATTACK, HP=6, Position=(3.00,0.50,0.00), Velocity=(-1.00,0.00,0.00), [size=1.0 ...]}.\n"
            + "READ EVERY SYMBOL\n"
            + "- objects.Count: a List property with no parentheses; objects.Count() is CS1955 (member cannot be called).\n"
            + "- objects[0]: indices start at 0; objects[objects.Count] is always out of range, the maximum is Count - 1.\n"
            + "- o.Index vs objects[i]: Index is the spawn serial (may already be 200+), the index is its current slot in the list (0, 1, 2...).\n"
            + "- ToString(): TowerObjectInfo overrides it and returns that whole summary line; PrintLine accepts it directly (object args are converted).\n"
            + "- Check before indexing: on an empty list objects[0] throws ArgumentOutOfRangeException and the editor stops with an error.\n"
            + "THREE TRAPS\n"
            + "- Writing objects.Count() is CS1955 - Count is a property, not a method.\n"
            + "- Using o.Index as an index: objects[o.Index] throws ArgumentOutOfRangeException; a serial is not a position.\n"
            + "- To see everything at once: for (int i = 0; i < objects.Count; i++) tower.PrintLine(objects[i].ToString()); - count and index together never overflow.";
        // ---------- 任务 15：GameStart 开局开关（幂等） ----------
        list[14].Page2Zh = "【这一页：亲手把「游戏开始」交到自己手上】\n"
            + "① 看编辑器最上面两行，那就是开场段（默认模板已经写好）：GlobalCode { bool gameStarted = false; } 和 if (!gameStarted) { gameStarted = true; tower.GameStart(); }。\n"
            + "② 只删掉 tower.GameStart(); 这一行（或整段 if 删掉），点【编译运行 (F6)】：没有方块出来，关卡就那么静止着——开局开关没人按下。\n"
            + "③ 把这一行加回来，再 F6：正式关卡（Level 3 - Part 1）里方块立刻开始飞出来。\n"
            + "④ 验证幂等：在 if 段下面再补一行 tower.GameStart();，再 F6 —— 结果和只写一次完全一样，不会重新计时、不会多出一倍方块。\n"
            + "【逐符号读懂它】\n"
            + "· GameStart()：括号里没有参数，但空括号必须写；写成 GameStart（漏括号）或漏掉 ; 报 CS1002 / CS1955。\n"
            + "· GlobalCode { … }：本作扩展语法、不是 C# 关键字 —— 块内声明被提升成类字段，跨帧一直活着；普通函数体里的局部变量每帧都会重建。\n"
            + "· bool gameStarted：自己起的标记名（bool 只有 true / false），只用来记「这次开场我调过了」。\n"
            + "· !gameStarted：! 取反，标记还是 false 时才进来一次。\n"
            + "· gameStarted = true;：进门的瞬间先关门，从下一帧起条件不再成立。\n"
            + "· tower.GameStart() 放在 if 里面：每帧都会经过这一行，但真正执行只有第一次 —— 这就是「一次性调用」的标准写法。\n"
            + "【为什么重复调用也安全（幂等）】\n"
            + "· 引擎那边有一道开场门控：只在「正在等待 + 还没启动过」时真正开场；已经启动过就直接忽略这次调用。\n"
            + "· 所以第二、第三次调用既不会重新倒计时，也不会再放一批方块，游戏状态完全不变 —— 就算每帧都调也搞不乱关卡。\n"
            + "· 教学关 / 主页里没有等待中的门控，调用同样直接忽略，不会报错。\n"
            + "【三个必踩的坑】\n"
            + "✘ 以为「不写也没关系」→ 正式关卡一直不出方块，看着像卡死，其实是在等你调用 GameStart()。\n"
            + "✘ 把 GameStart() 写进 GlobalCode 块里 → 编译报错：全局区里不能出现 tower（它只在 Run(CodeTower tower) 的参数里存在）。\n"
            + "✔ 想「重开一局」不要靠再调一次 GameStart()：重开由重试 / 重进关卡负责，开场门控会自己复位。";
        list[14].Page2En = "THIS PAGE: TAKE THE START OF THE GAME INTO YOUR OWN HANDS\n"
            + "1) Look at the top two lines - that is the start block (already in the default template): GlobalCode { bool gameStarted = false; } and if (!gameStarted) { gameStarted = true; tower.GameStart(); }.\n"
            + "2) Delete only the tower.GameStart(); line (or the whole if block), then press [Compile & Run (F6)]: no blocks appear, the level just sits there - nobody pressed the start switch.\n"
            + "3) Put the line back and press F6 again: in a real level (Level 3 - Part 1) blocks start flying immediately.\n"
            + "4) Prove idempotence: add a second tower.GameStart(); under the if block and press F6 - the result equals a single call, no timer restart and no doubled spawn.\n"
            + "READ EVERY SYMBOL\n"
            + "- GameStart(): no parameters, but the empty parentheses are required; GameStart without them or a missing ; gives CS1002 / CS1955.\n"
            + "- GlobalCode { ... }: a game-specific extension, not a C# keyword - declarations inside are lifted to class fields and survive frames; locals in a normal body are rebuilt every frame.\n"
            + "- bool gameStarted: your own flag name (bool is only true / false) recording \"I already started this level\".\n"
            + "- !gameStarted: ! negates, so the body runs once while the flag is still false.\n"
            + "- gameStarted = true;: close the door on the way in - from the next frame the condition is false.\n"
            + "- tower.GameStart() inside the if: every frame reaches the line, only the first executes it - the standard one-shot pattern.\n"
            + "WHY A REPEATED CALL IS SAFE (IDEMPOTENT)\n"
            + "- The engine has an opening gate: it really starts only while it is waiting and not started yet; once started it ignores the call.\n"
            + "- So the second and third calls neither restart a timer nor spawn a second wave; the state does not change - calling it every frame cannot break the level.\n"
            + "- Tutorial levels / the main menu have no waiting gate, so the call is ignored there too and never errors.\n"
            + "THREE TRAPS\n"
            + "- Assuming you can skip it: a real level then never spawns a block and looks frozen - it is waiting for GameStart().\n"
            + "- Putting GameStart() inside the GlobalCode block: compile error - tower does not exist there (it only arrives as Run(CodeTower tower)).\n"
            + "- Using GameStart() as a \"restart\": restarts are handled by retry / re-entering the level, the gate resets itself.";
    }

    /// <summary>
    /// 炮台 API 课（15 项）的第 3 页：任务 13、14 讲的是「一个结构体有哪些成员、列表怎么按下标取对象」，
    /// 既没有进程内存可逐格看，也不是一段能逐行高亮的代码 —— 所以复用语法课的逻辑图渲染器（方框 + 箭头 + 分步点亮），
    /// 与内存页共用同一个 Detail 3 入口（PageCountOf / 翻页 / ↓ 步进）。
    /// 收官项 15（GameStart）同样用逻辑图表达「等待 → 调用 GameStart → 幂等判定 → 开始出怪」这条流水线。
    /// </summary>
    private static void AppendApiDiagramPages(TaskDef[] list)
    {
        // ---------- 任务 13：TowerObjectInfo 的 10 个成员全景 —— 先分组，再逐个看「谁在什么时候被改写」 ----------
        list[12].Dia = DSC(
            new[]
            {
                DN("o：TowerObjectInfo", 0.5f, 0.08f, DiaTone.Purple)
                    .En("o: TowerObjectInfo").Size(0.36f),
                DN("玩法直接用", 0.22f, 0.26f, DiaTone.Accent)
                    .En("used directly").Note("瞄准 · 判断 · 计数", "aim / filter / count").Size(0.28f),
                DN("查询用", 0.78f, 0.26f, DiaTone.Value)
                    .En("for queries").Note("计分 · 展示 · 调试", "score / display / debug").Size(0.28f),
                DN("Position · Velocity\nVector3，各 12 字节", 0.22f, 0.48f)
                    .En("Position / Velocity\nVector3, 12 bytes each").Size(0.30f),
                DN("HP · Index\nint，各 4 字节", 0.22f, 0.70f)
                    .En("HP / Index\nint, 4 bytes each").Size(0.30f),
                DN("Type\nstring，8 字节引用", 0.22f, 0.92f)
                    .En("Type\nstring, 8-byte reference").Size(0.30f),
                DN("MaxHits · Size\nint 4B / Vector3 12B", 0.78f, 0.48f)
                    .En("MaxHits / Size\nint 4B / Vector3 12B").Size(0.30f),
                DN("Volume · InitialVolume\nfloat，各 4 字节", 0.78f, 0.70f)
                    .En("Volume / InitialVolume\nfloat, 4 bytes each").Size(0.30f),
                DN("Params\nstring，已拼好", 0.78f, 0.92f)
                    .En("Params\nstring, already built").Size(0.30f),
                DN("MaxHits - HP\n= 已挨几下", 0.5f, 0.70f, DiaTone.Alert)
                    .En("MaxHits - HP\n= hits taken").Size(0.24f)
            },
            new[]
            {
                DE(0, 1).Lb("能拿去打", "usable in play"),
                DE(0, 2).Lb("只拿来读", "read only"),
                DE(1, 3).Lb("喂给 Fire", "pass to Fire"),
                DE(1, 4).Lb("过滤 / 编号", "filter / id"),
                DE(1, 5).Lb("判类型", "test type"),
                DE(2, 6).Lb("上限 / 求交", "limit / intersect"),
                DE(2, 7).Lb("算比例", "ratio"),
                DE(2, 8).Lb("直接打印", "print as-is"),
                DE(4, 9).Lb("HP 是剩余", "HP = left"),
                DE(6, 9).Lb("MaxHits 是初始", "MaxHits = start").Tint(DiaTone.Alert)
            },
            new[]
            {
                DS(H(0), null,
                    "o 不是场上那个方块本身，而是 GetAllObjects() 现拷出来的一张快照：10 个 public 字段全在里面，写 o.字段名 就取到。先分两拨看 —— 哪些每帧都要用，哪些只在报数时读一次。",
                    "o is not the flying block itself but a snapshot copied by GetAllObjects(): all 10 public fields live in it and o.field reads one. Split them in two groups first - what you need every frame, and what you only read when reporting."),
                DS(H(1, 2), H(0, 1),
                    "左边这拨是玩法直接用的：Position / Velocity 拿去瞄准，HP 判断还剩几层，Type 过滤 ATTACK / BOMB / HEAL，Index 当日志编号。右边这拨是查询用的：计分、展示、调试各取所需。分错组最典型的事故是拿 MaxHits 去做瞄准 —— 它是计分字段，不是当前状态。",
                    "The left group is used in play: Position / Velocity for aiming, HP for how many layers remain, Type to filter ATTACK / BOMB / HEAL, Index as a log id. The right group is for queries: scoring, display, debugging. Mis-grouping shows up as aiming with MaxHits - that is a scoring field, not live state."),
                DS(H(3, 4, 5), H(2, 3, 4),
                    "看字节和改写者：Position、Velocity 是 Vector3 = 3 个 float 共 12 字节，每个物理帧被方块自己改写，所以读到的是「这一刻」的位置；HP、Index 各 4 字节，HP 每被切一刀 -1，Index 出场就定下、永不改；Type 是 string，字段本身只占 8 字节引用，文字存在堆上，切不改它。",
                    "Bytes and writers: Position and Velocity are Vector3, three floats = 12 bytes, rewritten by the block every physics frame, so you read the position of this instant; HP and Index are 4 bytes each, HP drops by 1 per cut while Index is fixed at spawn; Type is a string - the field holds only an 8-byte reference, the text lives on the heap and cutting never touches it."),
                DS(H(6, 7, 8), H(5, 6, 7),
                    "剩下四个是查询用的：MaxHits 是出场层数（int 4B），切割后不变；Size 是当前三边尺寸（Vector3 12B），越挨打越小 —— 激光沿路径求交就是拿它算的；Volume / InitialVolume 各 4 字节 float，后者是开局体积、永不改；Params 是已拼好的字符串（size=.. speed=.. hits=剩余/初始），直接打印就行。",
                    "The last four are for queries: MaxHits is the spawn layer count (int 4B) and never changes; Size is the current edge lengths (Vector3 12B) that shrink as it takes hits - laser path intersection uses exactly this; Volume / InitialVolume are 4-byte floats, the latter being the spawn volume and constant; Params is a ready-made string (size=.. speed=.. hits=left/initial) you can print as-is."),
                DS(H(9), H(8, 9),
                    "想报「已经挨了几下」不能直接打印 HP：HP 是还剩几层。MaxHits - HP 才是次数 —— 5 层出场、剩 2 层就是挨了 3 下。Params 里那句 hits=2/5 正是这个意思，前面是 HP、后面是 MaxHits，所以自己拼行时照这个顺序写就行。",
                    "Printing HP does not tell you how many hits it took - HP is what is left. MaxHits - HP is the count: spawned with 5, 2 left means 3 hits. That is exactly what hits=2/5 in Params means - HP first, MaxHits second - so build your own line in that order."),
                DS(H(7, 8), H(4, 6),
                    "最后记一条改写边界：会变的是 HP、Volume、Size（越打越小）和 Position、Velocity（每帧飞）；不变的是 MaxHits、InitialVolume、Index、Type、Params。所以判断和计分尽量读不变的那几个，才不会因为「打着打着数字变了」算出怪结果。另外 Params 已经是 string，套 {0:F1} 会抛 FormatException。",
                    "Rewriting boundary: HP, Volume and Size shrink, Position and Velocity fly every frame; MaxHits, InitialVolume, Index, Type and Params stay put. Base filtering and scoring on the constant ones so the numbers cannot drift mid-fight. And Params is already a string - applying {0:F1} to it throws FormatException.")
            },
            "左边 = 每帧都要用的玩法字段，右边 = 报数时才读的查询字段；会变的是 HP/Volume/Size/Position/Velocity。",
            "Left = gameplay fields used every frame, right = query fields read when reporting; HP/Volume/Size/Position/Velocity are the ones that change.");

        // ---------- 任务 14：count → 边界 → 下标 → 打印，把「取一个对象」拆成四道关 ----------
        list[13].Dia = DSC(
            new[]
            {
                DN("objects = GetAllObjects()", 0.5f, 0.08f, DiaTone.Purple)
                    .En("objects = GetAllObjects()").Size(0.42f),
                DN("objects.Count\n属性，不带括号", 0.22f, 0.28f, DiaTone.Accent)
                    .En("objects.Count\nproperty, no parens").Size(0.30f),
                DN("objects.Count()\nCS1955：不是方法", 0.22f, 0.52f, DiaTone.Alert)
                    .En("objects.Count()\nCS1955: not a method").Size(0.30f),
                DN("if (i < objects.Count)\n先验边界", 0.5f, 0.28f, DiaTone.Accent)
                    .En("if (i < objects.Count)\ncheck first").Size(0.32f),
                DN("objects[i]\n下标从 0 起", 0.5f, 0.52f, DiaTone.Value)
                    .En("objects[i]\n0-based index").Size(0.28f),
                DN("o.ToString()\n一行拼好全部字段", 0.5f, 0.74f, DiaTone.Accent)
                    .En("o.ToString()\none-line summary").Size(0.30f),
                DN("PrintLine(...)\n调试输出", 0.5f, 0.94f, DiaTone.Value)
                    .En("PrintLine(...)\ndebug output").Size(0.28f),
                DN("objects[Count]\n越界抛异常", 0.8f, 0.52f, DiaTone.Alert)
                    .En("objects[Count]\nthrows, out of range").Size(0.28f),
                DN("o.Index\n编号，不是下标", 0.8f, 0.28f, DiaTone.Purple)
                    .En("o.Index\na serial, not a slot").Size(0.28f)
            },
            new[]
            {
                DE(0, 1).Lb("数个数", "count them"),
                DE(1, 2).Lb("加括号就错", "parens break it").Dash().Tint(DiaTone.Alert),
                DE(1, 3).Lb("上界", "upper bound"),
                DE(3, 4).Lb("通过才取", "only if valid"),
                DE(4, 7).Lb("i 超了", "i too big").Tint(DiaTone.Alert),
                DE(0, 8).Lb("容易搞混", "easy mix-up").Dash().NoArrow(),
                DE(4, 5).Lb("拿到对象", "got the object"),
                DE(5, 6).Lb("直接当实参", "pass it in")
            },
            new[]
            {
                DS(H(0), null,
                    "GetAllObjects() 每次都现场走一遍场上所有方块，拼一个新的 List 交回来，里面每个元素都是 TowerObjectInfo 快照。所以先存进 objects 再反复用 —— 别在 for 条件里一遍遍调它。",
                    "Every GetAllObjects() call walks all blocks and builds a fresh List whose elements are TowerObjectInfo snapshots. Store it in objects and reuse it - never call it again inside a loop condition."),
                DS(H(1, 2), H(0, 1, 2),
                    "想知道有几个：objects.Count 直接给个数。Count 是 List 的属性，不是方法 —— 加括号写成 objects.Count() 会报 CS1955（成员不能用括号调用），没引 LINQ 时它也不是那个扩展方法。",
                    "To know how many: objects.Count gives the number. Count is a List property, not a method - adding parentheses gives CS1955 (member cannot be called like a method), and without LINQ it is not that extension method either."),
                DS(H(3), H(2),
                    "下标从 0 开始：Count = 3 说明合法下标只有 0、1、2。所以取之前先拿 Count 当上界拦一道 if (i < objects.Count)，条件不成立就整段不进 —— 这一句能挡掉绝大多数越界崩溃。",
                    "Indices start at 0: Count = 3 means only 0, 1 and 2 are legal. So use Count as the bound first with if (i < objects.Count) and skip the body when it fails - that one line prevents most out-of-range crashes."),
                DS(H(4, 7), H(3, 4),
                    "objects[i] 是按下标直接跳到第 i 个元素，O(1) 不遍历。i 一旦等于或超过 Count 就是 objects[Count] 这种写法，当场抛 ArgumentOutOfRangeException（编辑器里整段代码停下），不会给你 null 让你继续。",
                    "objects[i] jumps straight to the i-th element in O(1), no scanning. If i reaches Count you are writing objects[Count], which throws ArgumentOutOfRangeException right there and stops the whole run - it does not hand you a null to keep going."),
                DS(H(8), H(5),
                    "别把 Index 当下标：Index 是方块出厂时给的全局编号（可能是 3、7、12 这样跳号），不是它在列表里排第几。要按编号挑，得自己遍历比对 o.Index == 7，不能写 objects[o.Index]。",
                    "Do not treat Index as an index: it is a global spawn serial (could jump like 3, 7, 12), not the slot in the list. To pick by serial, loop and compare o.Index == 7 - objects[o.Index] is wrong."),
                DS(H(5, 6), H(6, 7),
                    "取到对象之后，ToString() 一步把编号 / 类型 / HP / 位置 / 速度 / Params 拼成一行字符串，直接塞进 PrintLine 就是最省事的调试输出 —— 不用一个一个 {0} 去接，也不怕漏字段。",
                    "Once you hold the object, ToString() packs index / type / HP / position / velocity / Params into one string that goes straight into PrintLine - no {0} slots to fill and no field forgotten.")
            },
            "Count 是属性（不加括号）→ 先拿它挡边界 → objects[i] 从 0 起 → ToString() 一行打全部。",
            "Count is a property (no parens) -> use it as the bound -> objects[i] is 0-based -> ToString() prints everything in one line.");

        // ---------- 任务 15：等待 → 调用 GameStart → 幂等判定 → 开始出怪，把「开局开关」画成一条流水线 ----------
        list[14].Dia = DSC(
            new[]
            {
                DN("进关：门控 Play(...)", 0.5f, 0.06f, DiaTone.Purple)
                    .En("level entry: gate Play(...)").Size(0.34f),
                DN("waiting = true\n发块器被按住：0 块", 0.5f, 0.26f, DiaTone.Accent)
                    .En("waiting = true\nspawner held: 0 blocks").Size(0.34f),
                DN("删掉这行\n永远停在等待", 0.20f, 0.26f, DiaTone.Alert)
                    .En("delete the line\nwaits forever").Size(0.28f),
                DN("tower.GameStart()\n每帧都会经过", 0.5f, 0.48f, DiaTone.Purple)
                    .En("tower.GameStart()\nreached every frame").Size(0.32f),
                DN("GlobalCode 标记\nif (!gameStarted)\n只进门一次", 0.81f, 0.48f, DiaTone.Value)
                    .En("GlobalCode flag\nif (!gameStarted)\nenter once only").Size(0.30f),
                DN("门控幂等判定\nstarted == false ?", 0.5f, 0.68f, DiaTone.Accent)
                    .En("gate idempotence check\nstarted == false ?").Size(0.32f),
                DN("已启动 → 忽略\n不重置、不重出怪", 0.81f, 0.68f, DiaTone.Panel)
                    .En("already started -> ignored\nno reset, no extra wave").Size(0.30f),
                DN("开始出怪\n方块飞出来", 0.5f, 0.90f, DiaTone.Value)
                    .En("spawning begins\nblocks fly out").Size(0.28f)
            },
            new[]
            {
                DE(0, 1).Lb("进关即按住", "held on entry"),
                DE(1, 2).Lb("不写这行", "skip the call").Dash().Tint(DiaTone.Alert),
                DE(1, 3).Lb("只有你的代码能放开", "only your code releases").Tint(DiaTone.Purple),
                DE(4, 3).Lb("包住它", "wraps the call"),
                DE(3, 5).Lb("交给门控", "hand to the gate"),
                DE(5, 6).Lb("第二次起", "2nd call on").Dash().Tint(DiaTone.Alert),
                DE(5, 7).Lb("第一次", "first call")
            },
            new[]
            {
                DS(H(0), null,
                    "进关第一步：门控被打开（Play），它把状态置成「等待玩家开场」waiting = true，同时把发块器的生成硬按住 —— 这一步之后场上真的一个方块都不会出现，不是加载慢，是在等你。",
                    "First step on entry: the gate is opened (Play), which sets waiting = true and hard-holds the spawner - after that not a single block appears. It is not slow loading, it is waiting for you."),
                DS(H(1, 2), H(0, 1, 2),
                    "所以「游戏为什么不会自己开始」：没有任何代码替玩家按下开关，门控就一直停在等待。如果你是手滑删掉了默认模板里的 tower.GameStart();（或者删了整段 if），表现就是关卡永远静止 —— 补回那一行再点 F6 就动了。",
                    "So why the game never starts by itself: nothing presses the switch for the player, and the gate stays in waiting forever. If you deleted tower.GameStart(); (or the whole if block) by accident, the level simply stays still - put the line back and press F6."),
                DS(H(3, 4), H(3, 4),
                    "你的代码里那一行 tower.GameStart() 只是把请求转给门控。因为 Run(tower) 每帧都会执行，这行每帧都会跑到 —— 于是用 GlobalCode 里的 bool gameStarted 当门闩：第一次进来先置 true 再调用，后面的帧条件不成立就跳过。这就是「一次性调用」的写法。",
                    "The tower.GameStart() line only forwards a request to the gate. Run(tower) fires every frame, so the line is reached every frame - hence the bool gameStarted in GlobalCode acts as a latch: the first time it is set to true and the call is made, later frames skip because the condition fails. That is the one-shot pattern."),
                DS(H(5, 6), H(5, 6),
                    "门控收到请求先做幂等判定：只有在「waiting 且没启动过」时才真正放行；已经 started 的直接忽略这次调用 —— 所以第二、第三次调用既不会重新倒计时，也不会再放一批方块。就算你把 GameStart() 裸写在函数体里每帧调，关卡也不会被搞乱。",
                    "On request the gate first checks idempotence: it only passes while waiting and not started yet; once started the call is ignored - so the 2nd and 3rd calls neither restart a timer nor spawn a second wave. Even a bare GameStart() in the body, called every frame, cannot break the level."),
                DS(H(7), H(7),
                    "第一次通过后门控把发块器放开，方块开始飞出来 —— 这就是「游戏开始了」。往后想重开一局靠重试 / 重进关卡（旧门控被 Cancel 复位），不是靠再调一次 GameStart()。",
                    "On the first pass the gate releases the spawner and blocks start flying - that is the game starting. Restarting later is done by retry / re-entering the level (the old gate is reset by Cancel), never by calling GameStart() again.")
            },
            "进关被按住 → 你的 GameStart() 放行 → 门控查 started 做幂等 → 开始出怪；不写这行就一直等。",
            "Held on entry -> your GameStart() releases -> the gate checks started for idempotence -> blocks spawn; skip the call and it waits forever.");

        // ---------- 任务 1：Print 是往一条文本流末尾追加，永远不会自己换行 ----------
        list[0].Dia = DSC(
            new[]
            {
                DN("Print(\"A\")", 0.5f, 0.06f, DiaTone.Purple)
                    .En("Print(\"A\")").Size(0.30f),
                DN("缓冲 = \"A\"", 0.24f, 0.28f, DiaTone.Accent)
                    .En("buffer = \"A\"").Size(0.26f),
                DN("Print(\"B\")", 0.76f, 0.28f, DiaTone.Purple)
                    .En("Print(\"B\")").Size(0.28f),
                DN("缓冲 = \"AB\"", 0.24f, 0.50f, DiaTone.Accent)
                    .En("buffer = \"AB\"").Size(0.28f),
                DN("Print(\"C\")", 0.76f, 0.50f, DiaTone.Purple)
                    .En("Print(\"C\")").Size(0.28f),
                DN("缓冲 = \"ABC\"", 0.24f, 0.72f, DiaTone.Accent)
                    .En("buffer = \"ABC\"").Size(0.30f),
                DN("编辑器面板：一行 ABC", 0.74f, 0.72f, DiaTone.Value)
                    .En("panel: one line ABC").Size(0.34f),
                DN("换行只发生在 PrintLine / ClearPrint", 0.5f, 0.94f, DiaTone.Alert)
                    .En("only PrintLine / ClearPrint deal with line breaks").Size(0.52f)
            },
            new[]
            {
                DE(0, 1).Lb("追加进去", "appended"),
                DE(1, 2).Lb("下一句又来", "next call"),
                DE(2, 3).Lb("贴在末尾", "pasted on"),
                DE(3, 4).Lb("再来一句", "one more"),
                DE(4, 5).Lb("还是贴末尾", "still appended"),
                DE(5, 6).Lb("整条一起画", "drawn as-is"),
                DE(6, 7).Lb("没有换行符", "no newline").Dash().Tint(DiaTone.Alert)
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "Print(\"A\") 不是「打印一行」，而是把 \"A\" 追加到这条输出缓冲的末尾。第一次调用时缓冲本来是空的，所以里面就只有 A。",
                    "Print(\"A\") does not print a line; it appends \"A\" to the end of the output buffer. The buffer started empty, so it now holds just A."),
                DS(H(2, 3), H(1, 2),
                    "再来 Print(\"B\")：照样只往末尾贴，缓冲变成 \"AB\"。关键在于「接着贴」—— 不清空、也不替你先换行。",
                    "Then Print(\"B\") pastes onto the end again and the buffer becomes \"AB\". The point is appending: nothing is cleared and no newline is added for you."),
                DS(H(4, 5), H(3, 4),
                    "第三次 Print(\"C\")：变成 \"ABC\"。所以连续三次 Print 的结果是一行 ABC，而不是三行。",
                    "A third call Print(\"C\") makes it \"ABC\". Three Print calls give one line ABC, never three lines."),
                DS(H(6), H(5, 6),
                    "面板刷新时把整条缓冲一次画出来：一行 ABC。你想要的「每条一行」，靠 Print 是做不到的。",
                    "The panel redraws the whole buffer at once: a single line ABC. \"One item per line\" is simply not something Print can do."),
                DS(H(7), H(6, 7),
                    "要让后面的内容另起一行，得调 PrintLine（自动补换行）或先 ClearPrint（整段清空）。Print 只管追加，换行是别人的事。",
                    "To start a new line, call PrintLine (which adds the newline) or ClearPrint (which empties everything). Print only appends - line breaks are someone else's job.")
            },
            "Print 只做一件事：把字追加到缓冲末尾。缓冲里没有换行符，面板就永远显示一行。",
            "Print does one thing: append to the buffer. With no newline in the buffer the panel always shows a single line.");

        // ---------- 任务 2：PrintLine = Print + 一个换行符，差别只在缓冲的尾部 ----------
        list[1].Dia = DSC(
            new[]
            {
                DN("Print(\"A\")", 0.24f, 0.06f, DiaTone.Purple)
                    .En("Print(\"A\")").Size(0.28f),
                DN("PrintLine(\"A\")", 0.76f, 0.06f, DiaTone.Purple)
                    .En("PrintLine(\"A\")").Size(0.30f),
                DN("缓冲：A", 0.24f, 0.28f, DiaTone.Accent)
                    .En("buffer: A").Size(0.24f),
                DN("缓冲：A\\n", 0.76f, 0.28f, DiaTone.Accent)
                    .En("buffer: A\\n").Size(0.26f),
                DN("Print(\"B\")", 0.24f, 0.50f, DiaTone.Purple)
                    .En("Print(\"B\")").Size(0.28f),
                DN("PrintLine(\"B\")", 0.76f, 0.50f, DiaTone.Purple)
                    .En("PrintLine(\"B\")").Size(0.30f),
                DN("缓冲：AB", 0.24f, 0.72f, DiaTone.Value)
                    .En("buffer: AB").Size(0.24f),
                DN("缓冲：A\\nB\\n", 0.76f, 0.72f, DiaTone.Value)
                    .En("buffer: A\\nB\\n").Size(0.28f),
                DN("面板：一行 AB   vs   两行 A / B", 0.5f, 0.94f, DiaTone.Alert)
                    .En("panel: one line AB  vs  two lines A / B").Size(0.52f)
            },
            new[]
            {
                DE(0, 2).Lb("只放字", "text only"),
                DE(1, 3).Lb("字 + 换行", "text + newline"),
                DE(2, 4).Lb("贴上去", "glued on"),
                DE(3, 5).Lb("另起一行", "starts a new line"),
                DE(4, 6).Lb("合成 AB", "becomes AB"),
                DE(5, 7).Lb("分成两行", "two lines"),
                DE(6, 8).Lb("挤在一起", "jammed together").Dash().Tint(DiaTone.Alert),
                DE(7, 8).Lb("各占一行", "one per line").Dash().Tint(DiaTone.Value)
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "两个函数只差一个字符：Print(\"A\") 把 A 放进缓冲；PrintLine(\"A\") 放的是 A 再加一个换行符。功能上 PrintLine 就是 Print(内容 + \"\\n\")。",
                    "The two differ by exactly one character of output: Print(\"A\") puts A into the buffer; PrintLine(\"A\") puts A plus a newline. In effect PrintLine is Print(text + \"\\n\")."),
                DS(H(2, 3), H(2, 3),
                    "缓冲此刻的差别肉眼看不到：左边是 A，右边是 A 后面跟着一个看不见的换行符（\\n）。面板这时还一样，都只显示一个 A。",
                    "The buffers already differ, though you cannot see it: the left holds A, the right holds A followed by an invisible newline (\\n). The panel still looks the same - one A on both sides."),
                DS(H(4, 5, 6), H(4),
                    "再打一句 B。左边缓冲原本没有换行，于是 B 紧贴 A 变成 AB —— 这就是新手最常见的「日志糊成一团」。",
                    "Then print B. The left buffer had no newline, so B sticks to A and gives AB - the classic jammed-together log."),
                DS(H(7), H(5, 7),
                    "右边的缓冲本来就以换行结尾，B 自然从头开始：A、换行、B、换行。面板按缓冲画出来就是两行。",
                    "The right buffer already ended in a newline, so B starts fresh: A, newline, B, newline. The panel draws that as two lines."),
                DS(H(8), H(6, 8),
                    "选哪个只看你想不想换行：一行里塞几段用 Print，一条记录一行用 PrintLine。混着用又不清楚缓冲里有没有换行，就会得到 AB 这种粘在一起的结果。",
                    "Choose by whether you want a break: several pieces on one line -> Print; one record per line -> PrintLine. Mixing them without tracking the newline is how you end up with AB glued together.")
            },
            "PrintLine = Print + \"\\n\"。差别全在缓冲尾部那一个看不见的换行符上。",
            "PrintLine is Print plus \"\\n\". The whole difference is that one invisible newline at the tail of the buffer.");

        // ---------- 任务 3：ClearPrint 清的是输出缓冲，一个变量都碰不到 ----------
        list[2].Dia = DSC(
            new[]
            {
                DN("缓冲里已有：A\\nB\\n", 0.5f, 0.06f, DiaTone.Accent)
                    .En("buffer holds: A\\nB\\n").Size(0.38f),
                DN("ClearPrint()", 0.5f, 0.28f, DiaTone.Purple)
                    .En("ClearPrint()").Size(0.28f),
                DN("缓冲 = 空字符串", 0.26f, 0.50f, DiaTone.Value)
                    .En("buffer = empty").Size(0.30f),
                DN("面板上的旧行消失", 0.74f, 0.50f, DiaTone.Value)
                    .En("old lines disappear").Size(0.30f),
                DN("int count = 3;，栈上那 4 格没动", 0.26f, 0.74f, DiaTone.Alert)
                    .En("int count = 3;, its 4 bytes are untouched").Size(0.34f),
                DN("count 还是 3", 0.26f, 0.94f, DiaTone.Value)
                    .En("count is still 3").Size(0.24f),
                DN("再 Print(\"C\") → 面板只剩 C", 0.74f, 0.74f, DiaTone.Accent)
                    .En("then Print(\"C\") -> only C").Size(0.36f)
            },
            new[]
            {
                DE(0, 1).Lb("调用", "call it"),
                DE(1, 2).Lb("清空缓冲区", "empties the buffer"),
                DE(2, 3).Lb("下一帧重画", "redrawn next frame"),
                DE(1, 4).Lb("碰不到任何变量", "never touches variables").Tint(DiaTone.Alert),
                DE(4, 5).Lb("值原样留着", "value stays"),
                DE(2, 6).Lb("从头再来", "start over")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "调用前缓冲里攒着 A 和 B，面板正在显示这两行。ClearPrint 是显示层函数：它只认得这条输出缓冲。",
                    "Before the call the buffer holds A and B and the panel is showing those two lines. ClearPrint belongs to the display layer: it only knows the output buffer."),
                DS(H(2, 3), H(1, 2, 3),
                    "它把缓冲置成空字符串，面板下一次刷新就无内容可画，于是旧行全部消失。屏幕上什么都没了，但那只是「没画」，不是「没了」。",
                    "It sets the buffer to an empty string, so the next refresh has nothing to draw and the old lines vanish. The screen is empty - but only because nothing is drawn."),
                DS(H(4, 5), H(4, 5),
                    "真正的数据一点没动：count 还是栈上那 4 个字节，值还是 03 00 00 00。ClearPrint 拿不到、也不想拿你的变量。",
                    "Your real data is untouched: count still occupies its four bytes on the stack with value 03 00 00 00. ClearPrint cannot reach your variables and does not try."),
                DS(H(5), H(5),
                    "所以清屏之后数字还是对不上，问题一定在你的计算里，绝不在 ClearPrint 上 —— 它没有能力改任何变量。",
                    "So if the numbers are still wrong after clearing, the bug is in your calculation, never in ClearPrint - it has no way to change a variable."),
                DS(H(6), H(6),
                    "清完再 Print(\"C\")，缓冲里只有 C，面板就只剩一行 C。这正是调试时「先清再打」的用法：只留这一帧想看的东西。",
                    "Print(\"C\") afterwards leaves only C in the buffer, so the panel shows a single line. That is the usual debugging habit: clear first, then print just what you want this frame.")
            },
            "ClearPrint 只清输出缓冲（显示层）；变量在内存里的字节一个都没变。",
            "ClearPrint empties the output buffer only; not a single byte of your variables changes.");

        // ---------- 任务 4：foreach 逐个走一遍，读的是身份字段 Index / Type ----------
        list[3].Dia = DSC(
            new[]
            {
                DN("objects = GetAllObjects()", 0.5f, 0.06f, DiaTone.Purple)
                    .En("objects = GetAllObjects()").Size(0.42f),
                DN("foreach (var o in objects)", 0.5f, 0.28f, DiaTone.Accent)
                    .En("foreach (var o in objects)").Size(0.38f),
                DN("第 1 圈：o.Index = 3, Type = \"ATTACK\"", 0.22f, 0.50f)
                    .En("turn 1: o.Index = 3, Type = \"ATTACK\"").Size(0.34f),
                DN("第 2 圈：o.Index = 7, Type = \"BOMB\"", 0.22f, 0.74f)
                    .En("turn 2: o.Index = 7, Type = \"BOMB\"").Size(0.34f),
                DN("o.Index 是出厂编号，不是列表下标", 0.78f, 0.50f, DiaTone.Alert)
                    .En("o.Index is a spawn serial, not a list slot").Size(0.34f),
                DN("o.Type 是字符串，比较用 == \"ATTACK\"", 0.78f, 0.74f, DiaTone.Value)
                    .En("o.Type is a string, compare with == \"ATTACK\"").Size(0.34f),
                DN("这一圈结束 → 自动取下一个", 0.5f, 0.94f, DiaTone.Accent)
                    .En("turn over -> next element").Size(0.40f)
            },
            new[]
            {
                DE(0, 1).Lb("现取一份快照", "fresh snapshot"),
                DE(1, 2).Lb("第 1 个元素", "first element"),
                DE(2, 3).Lb("接着往下", "move on"),
                DE(3, 6).Lb("走完就结束", "ends when done"),
                DE(2, 4).Lb("容易搞混", "easy mix-up").Tint(DiaTone.Alert),
                DE(2, 5).Lb("判类型", "test the type")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "GetAllObjects() 交回一个 List，里面每个元素都是一份快照。foreach 负责从第一个走到最后一个，一圈一个，你不用自己管下标。",
                    "GetAllObjects() hands back a List whose elements are snapshots. foreach walks it from first to last, one per turn - you never track the index yourself."),
                DS(H(2), H(1, 2),
                    "第一圈把第一个元素绑到 o 上：编号 3、类型 \"ATTACK\"。读身份就是读这两个字段 —— Index 认「是哪一个」，Type 认「是什么」。",
                    "The first turn binds the first element to o: serial 3, type \"ATTACK\". Identity means these two fields - Index says which one, Type says what kind."),
                DS(H(3, 6), H(2, 3),
                    "第二圈换成编号 7、类型 \"BOMB\"。o 只是「当前这一圈的对象」，下一圈就被换掉了；循环跑完（没有元素了）自动结束，不需要你手动停。",
                    "The second turn binds serial 7, type \"BOMB\". o is only the object of the current turn and gets replaced next turn; the loop ends by itself when the list runs out."),
                DS(H(4), H(4),
                    "最常见的误解是把 Index 当下标：它是方块出场时给的全局编号，可能是 3、7、12 这种跳号。objects[o.Index] 一定错，要按编号挑就在循环里比 o.Index == 7。",
                    "The usual mistake is treating Index as an index: it is a global serial handed out at spawn and can jump like 3, 7, 12. objects[o.Index] is always wrong - to pick by serial, compare o.Index == 7 inside a loop."),
                DS(H(5), H(5),
                    "Type 是字符串，所以只能用 == / != 比，而且大小写、空格都得一模一样：写成 \"attack\" 或 \"ATTACK \" 会永远不匹配，且不报错，最难查。",
                    "Type is a string, so compare with == / != and match case and spacing exactly: \"attack\" or \"ATTACK \" never matches and throws nothing - the hardest kind of bug."),
                DS(H(6), H(6),
                    "记住 foreach 的三个默认动作：从前往后、一圈一个、不回头。要倒数或者跳着取，就得换成 for 自己管下标。",
                    "Remember foreach's three defaults: front to back, one per turn, never backwards. To go in reverse or skip elements, use a for loop with your own index.")
            },
            "foreach = 逐个走；Index 是编号不是下标；Type 是字符串，要比就一字不差。",
            "foreach walks one by one; Index is a serial not a slot; Type is a string, so compare it exactly.");

        // ---------- 任务 5：读状态 HP / Position —— 这两个字段每帧都被方块自己改写 ----------
        list[4].Mem = MSC(
            new[]
            {
                MV("int", "o.HP", "02 00 00 00", "4 字节。02 = 还剩 2 层受击，不是「挨了 2 下」。").En("4 bytes. 02 means 2 layers left, not 2 hits taken."),
                MV("Vector3", "o.Position", "00 00 80 3F 00 00 00 40 00 00 40 40", "12 字节 = 3 个 float 拼一起，顺序固定 X / Y / Z：这里是 1、2、3。").En("12 bytes = three floats laid out as X / Y / Z: here 1, 2, 3."),
                MV("int", "o.Index", "03 00 00 00", "对照用：出厂编号，切多少刀都不变。", "对照").En("For contrast: the spawn serial, unchanged no matter how many cuts.", badge: "FIXED")
            },
            new[]
            {
                MS("foreach (var o in tower.GetAllObjects())", 1, -1,
                    "先取快照：GetAllObjects() 把场上每个方块此刻的字段拷一份交给 o。之后读到的都是「取的那一瞬间」的值，不是活对象。",
                    "Take a snapshot first: GetAllObjects() copies each block's fields at this instant into o, so everything you read is the value of that instant, not a live object."),
                MS("int hp = o.HP;", 1, -1,
                    "读 HP 就是读那 4 个字节：02 表示还剩 2 层。要报「已经挨了几下」不能直接打印 HP，得算 MaxHits - HP。",
                    "Reading HP is reading those 4 bytes: 02 means 2 layers left. To report how many hits it took, do not print HP - compute MaxHits - HP."),
                MS("Vector3 p = o.Position;", 2, -1,
                    "读 Position 一次拿走 12 个字节：三个 float，顺序固定 X、Y、Z，编辑器里看到的是 (1.00, 2.00, 3.00)。它不是「一个数字」，是三个拼在一起。",
                    "Reading Position takes 12 bytes at once: three floats in the fixed order X, Y, Z - the editor shows (1.00, 2.00, 3.00). It is not one number but three packed together."),
                MS("// 下一物理帧：方块又飞了一段", 2, -1,
                    "注意这 12 个字节变了：方块每个物理帧自己把 Position 改写成新位置。你手里的 p 是旧拷贝，不会跟着动 —— 想要最新坐标得再调一次 GetAllObjects()。",
                    "Those 12 bytes just changed: the block rewrites its own Position every physics frame. Your p is an old copy and does not follow - call GetAllObjects() again for the latest coordinates.",
                    new[] { 1 }, new[] { "00 00 00 40 00 00 40 40 00 00 80 40" }).Code("// next physics frame: the block moved on again"),
                MS("// 刚被激光切了一刀", 2, -1,
                    "HP 那 4 字节从 02 变成 01：命中一次减 1。它同样是方块改的，你只读不能改 —— 想让方块掉血只能靠 Fire 打中它。",
                    "The HP field went from 02 to 01: one hit, minus 1. It is also written by the block; you can only read it - the way to reduce HP is to actually hit it with Fire.",
                    new[] { 0 }, new[] { "01 00 00 00" }).Code("// just sliced by the laser"),
                MS("PrintLine(\"hp=\" + hp);", 2, -1,
                    "所以调试时最常见的困惑就在这里：上面两处改写你都没参与，但你手里的 hp 还是 2、p 还是老位置。快照 = 那一刻的照片，不会自己更新。",
                    "This is the classic debugging confusion: you did not cause either change, yet your hp is still 2 and p is the old position. A snapshot is a photo of that instant and never updates itself.")
            });

        // ---------- 任务 6：Velocity 是每秒位移，Volume 是「看起来多大」，两者不是一回事 ----------
        list[5].Mem = MSC(
            new[]
            {
                MV("Vector3", "o.Velocity", "00 00 00 00 00 00 00 00 00 00 80 3F", "12 字节三个 float，单位是「每秒多少米」：(0,0,1) = 这一秒沿 Z 走 1 米。").En("Three floats in metres per second: (0,0,1) means 1 metre along Z in one second."),
                MV("float", "o.Volume", "00 00 80 3F", "4 字节，1.0 = 当前体量。它跟飞多快毫无关系，只决定「看起来多大」。").En("4 bytes, 1.0 = current volume. Nothing to do with speed - it only decides how big it looks."),
                MV("Vector3", "o.Position", "00 00 00 00 00 00 00 00 00 00 00 00", "12 字节，原点起步。每帧被方块按「速度 × 帧时长」改写。").En("12 bytes, starting at the origin; rewritten every frame by speed x frame time.")
            },
            new[]
            {
                MS("Vector3 v = o.Velocity;", 1, -1,
                    "速度是「每秒」的量：读到 (0,0,1) 表示按现在这个速度，一整秒后会在 Z 方向前进 1 米。它没说「每帧 1 米」—— 这点最容易搞错。",
                    "Velocity is a per-second quantity: (0,0,1) means at this speed the block advances 1 metre along Z in a whole second. It does not mean 1 metre per frame - that is the usual mix-up."),
                MS("float vol = o.Volume;", 2, -1,
                    "Volume 是另一个维度的信息：4 字节 float，1.0 是初始大小，越挨打越小。速度决定「怎么飞」，体积决定「看着多大」，两者互不影响。",
                    "Volume is a different dimension: a 4-byte float starting at 1.0 and shrinking as it takes hits. Speed decides how it flies, volume decides how big it looks - independent of each other."),
                MS("o.Position += o.Velocity * Time.deltaTime;", 3, -1,
                    "方块每物理帧跑的就是这一行：位移 = 每秒速度 × 这一帧的秒数（约 0.0167）。所以速度乘上帧时长，才换算成「这一帧走多远」。",
                    "This is the line the block runs every physics frame: displacement = speed per second x seconds of this frame (about 0.0167). Speed times frame time is what turns per-second into per-frame."),
                MS("// 1 秒之后", 3, -1,
                    "跑满一秒后 Position 的 Z 正好变成 1.0 —— 这就是「每秒 1 米」的字面意思。位置是结果，速度是原因。",
                    "After a full second Position's Z is exactly 1.0 - that is literally \"1 metre per second\". Position is the result; velocity is the cause.",
                    new[] { 2 }, new[] { "00 00 00 00 00 00 00 00 00 00 80 3F" }).Code("// one second later"),
                MS("// 被激光打中之后", 3, -1,
                    "挨打改的是 Volume（1.0 → 0.75），Position 不受影响：方块变小了，但飞行速度和轨迹一点没变。",
                    "Getting hit changes Volume (1.0 -> 0.75) and leaves Position alone: the block looks smaller while its speed and path stay the same.",
                    new[] { 1 }, new[] { "00 00 40 3F" }).Code("// after being hit by the laser"),
                MS("PrintLine(v.magnitude);", 3, -1,
                    "想算「快不快」用 v.magnitude（= 每秒走几米），想算「到哪了」用 Position，想算「多大」用 Volume。三者别混：拿 Volume 当速度去乘 dt，位置就永远不动。",
                    "For \"how fast\" use v.magnitude (metres per second), for \"where\" use Position, for \"how big\" use Volume. Never mix them: treating Volume as speed makes Position never move.")
            });

        // ---------- 任务 7：Time.time 是墙上的钟，Time.deltaTime 是每帧的刻度 ----------
        list[6].Mem = MSC(
            new[]
            {
                MV("float", "Time.time", "00 00 48 41", "4 字节，游戏开始到现在的秒数（12.5 秒）。只增不减，帧率高低都照走。").En("4 bytes: seconds since the game started (12.5 s). It only grows and ignores frame rate."),
                MV("float", "Time.deltaTime", "89 88 88 3C", "4 字节，上一帧花了多少秒：60fps 约 0.0167，卡一下可能 0.05。每帧都不一样。").En("4 bytes: seconds the last frame took - about 0.0167 at 60 fps, maybe 0.05 after a hitch. Different every frame."),
                MV("float", "elapsed", "00 00 00 00", "自己开的 4 字节累加器，专门把每帧的 deltaTime 加进去。").En("Your own 4-byte accumulator that adds up each frame's deltaTime.")
            },
            new[]
            {
                MS("float t = Time.time;", 1, -1,
                    "Time.time 是「墙上的钟」：游戏开跑到现在一共过了多少秒，只往前走。停帧、卡顿它照样记账，所以适合做「几秒后刷新」这类判断。",
                    "Time.time is the wall clock: seconds since the game started, always moving forward. It keeps counting through hitches, which makes it right for \"refresh every N seconds\"."),
                MS("float dt = Time.deltaTime;", 2, -1,
                    "Time.deltaTime 是「尺子的刻度」：上一帧用了多少秒。60fps 约 0.0167，30fps 约 0.033，卡一下可能 0.05 —— 每帧都在变。",
                    "Time.deltaTime is the ruler's tick: how long the last frame took. About 0.0167 at 60 fps, 0.033 at 30 fps, 0.05 after a hitch - it changes every frame."),
                MS("elapsed += Time.deltaTime;", 3, -1,
                    "把每帧的 deltaTime 一片片加进 elapsed：加一千次，得到的就是这一千帧加起来经过的秒数 —— 所以 deltaTime 累加等于经过时间。",
                    "Add each frame's deltaTime into elapsed: after a thousand frames the sum is the real seconds that passed - that is why summing deltaTime equals elapsed time."),
                MS("// 跑了约 60 帧之后", 3, -1,
                    "60 帧过后 elapsed 变成 1.0 秒左右：这期间 Time.time 大概也从 12.5 涨到 13.5。两把尺子量的是同一件事，只是一个给总数、一个给增量。",
                    "After about 60 frames elapsed reaches 1.0 s while Time.time has gone from 12.5 to roughly 13.5. Both rulers measure the same thing - one gives the total, the other the step.",
                    new[] { 2 }, new[] { "00 00 80 3F" }).Code("// after about 60 frames"),
                MS("pos += v;        // 没乘 dt", 3, -1,
                    "不乘 deltaTime 的后果：速度变成「每帧多少米」。60fps 一秒飞 60 米，144fps 一秒飞 144 米 —— 换个显示器，游戏就变难了。",
                    "Dropping deltaTime turns speed into metres per frame: 60 m/s at 60 fps but 144 m/s at 144 fps. Change the monitor and the game gets harder.").Code("pos += v;        // dt not multiplied in"),
                MS("pos += v * dt;   // 正确写法", 3, -1,
                    "乘上 dt 之后，一秒的总位移固定等于 v：帧多每帧走少一点，帧少每帧走多一点，玩家感受到的速度就一致了。",
                    "With dt multiplied in, the displacement per second always equals v: many frames move a little each, few frames move more each, and the felt speed stays the same.").Code("pos += v * dt;   // the correct way")
            });

        // ---------- 任务 8：Clamp / Min / Max / Abs 都是「算出新值交回来」，不改实参 ----------
        list[7].Mem = MSC(
            new[]
            {
                MV("float", "ratio", "00 00 C0 3F", "4 字节，1.5f 是你自己算出来的原始比例，可能超出 0~1。").En("4 bytes: 1.5f is your raw ratio and may sit outside 0~1."),
                MV("float", "clamped", "00 00 80 3F", "4 字节，接住 Clamp 的返回值：超过上界就是上界，这里是 1.0f。").En("4 bytes holding Clamp's return value: past the upper bound it is the bound, here 1.0f."),
                MV("float", "d", "00 00 20 C0", "4 字节，-2.5f。Abs 之后 d 自己一个字节都不变。").En("4 bytes, -2.5f. After Abs, d itself loses not a single byte."),
                MV("float", "a", "00 00 20 40", "4 字节，Abs 的结果 2.5f：把符号位清掉，数值取正。").En("4 bytes holding Abs' result 2.5f: the sign bit cleared, the magnitude kept.")
            },
            new[]
            {
                MS("float ratio = 1.5f;", 1, -1,
                    "ratio 是算出来的原始值：4 个字节 00 00 C0 3F，值 1.5f。算血量条、进度条时很常见 —— 血量算下来超过 100% 就可能大于 1。",
                    "ratio is your raw result: 4 bytes 00 00 C0 3F, the value 1.5f. Common for bars - a computed health percentage can easily exceed 1."),
                MS("float c = Mathf.Clamp(ratio, 0f, 1f);", 2, -1,
                    "Clamp 把值夹进 0~1：1.5 交回 1.0，-0.3 交回 0，区间内原样返回。它不报错、不抛异常，只是悄悄截断 —— 所以「数字对不上」时最容易漏掉这一步。",
                    "Clamp squeezes the value into 0~1: 1.5 comes back as 1.0, -0.3 as 0, and anything inside is returned as-is. It never throws, it just silently truncates - the usual reason a number \"does not add up\"."),
                MS("Mathf.Clamp(ratio, 0f, 1f);   // 没接住", 2, -1,
                    "返回值必须接住：直接调用而不赋值，ratio 那 4 格还是 00 00 C0 3F。Clamp 不改实参，它只算出一个新值交回来。",
                    "You must capture the return value: calling it without assigning leaves ratio at 00 00 C0 3F. Clamp never changes the argument - it computes a new value and hands it back.").Code("Mathf.Clamp(ratio, 0f, 1f);   // return value ignored"),
                MS("float d = -2.5f;", 3, -1,
                    "换成绝对值场景：算「偏差多少」时经常拿到负数，这时要的是长度，不是方向。",
                    "For the absolute-value case: deviations often come out negative, and what you want there is magnitude, not direction."),
                MS("float a = Mathf.Abs(d);", 4, -1,
                    "Abs(-2.5f) 交回 2.5f：底层就是把符号位那个 bit 清掉。d 依然是 -2.5（00 00 20 C0），a 才是 2.5（00 00 20 40）—— 两块独立的内存。",
                    "Abs(-2.5f) returns 2.5f by clearing the sign bit. d is still -2.5 (00 00 20 C0) while a is 2.5 (00 00 20 40) - two separate blocks of memory."),
                MS("float bar = Mathf.Max(hp, 0) / (float)maxHp;", 4, -1,
                    "Min / Max 就是取两数中较小 / 较大的那个：Mathf.Max(hp, 0) 保证血量不会把血条拉成负的。它们和 Clamp 一样，都是纯计算，不动任何变量。",
                    "Min / Max simply picks the smaller / larger of two numbers: Mathf.Max(hp, 0) keeps health from pulling the bar negative. Like Clamp they are pure maths and touch no variable.")
            });

        // ---------- 任务 9：向量相减出方向、magnitude 出距离、normalized 出单位向量 ----------
        list[8].Dia = DSC(
            new[]
            {
                DN("原点 O (0,0,0)\n炮台就驻在这里", 0.24f, 0.06f, DiaTone.Panel)
                    .En("origin O (0,0,0)\nthe tower sits here").Size(0.32f),
                DN("o.Position = (3,0,4)", 0.76f, 0.06f, DiaTone.Value)
                    .En("o.Position = (3,0,4)").Size(0.30f),
                DN("dir = P - O\n三个分量各减各的", 0.24f, 0.28f, DiaTone.Purple)
                    .En("dir = P - O\nsubtract component by component").Size(0.32f),
                DN("dir = (3,0,4)\n从 O 指向方块", 0.76f, 0.28f, DiaTone.Accent)
                    .En("dir = (3,0,4)\npoints from O to the block").Size(0.32f),
                DN("dir.magnitude\n√(9+0+16) ≈ 5.83", 0.24f, 0.50f, DiaTone.Value)
                    .En("dir.magnitude\nsqrt(9+0+16) ~ 5.83").Size(0.32f),
                DN("dir.normalized\n(0.51,0,0.69)\n方向不变、长度变 1", 0.76f, 0.50f, DiaTone.Accent)
                    .En("dir.normalized\n(0.51,0,0.69)\nsame direction, length 1").Size(0.34f),
                DN("Vector3.Distance(P, O)\n一步等于 magnitude", 0.24f, 0.72f, DiaTone.Value)
                    .En("Vector3.Distance(P, O)\none call equals magnitude").Size(0.34f),
                DN("O - O = 零向量\nFire 直接返回 false", 0.76f, 0.72f, DiaTone.Alert)
                    .En("O - O = zero vector\nFire returns false").Size(0.32f),
                DN("距离交给 magnitude／Distance，方向交给 normalized", 0.5f, 0.94f, DiaTone.Alert)
                    .En("range goes to magnitude / Distance, direction goes to normalized").Size(0.62f)
            },
            new[]
            {
                DE(0, 2).Lb("取两个点相减", "take two points"),
                DE(1, 2).Lb("同一个减法", "same subtraction"),
                DE(2, 3).Lb("分量各减各的", "per component"),
                DE(3, 4).Lb("勾股折成标量", "Pythagoras to a scalar"),
                DE(3, 5).Lb("除以自身长度", "divide by its length"),
                DE(4, 6).Lb("同一个数", "the same number"),
                DE(4, 8).Lb("这才是距离", "this is the range"),
                DE(5, 8).Lb("这才是方向", "this is the direction"),
                DE(7, 8).Lb("必须避开", "avoid this").Dash().Tint(DiaTone.Alert)
            },
            new[]
            {
                DS(H(0, 1, 2, 3), H(0, 1, 2),
                    "向量减法不是「两个点比大小」，而是三个分量各减各的：P - O = (3-0, 0-0, 4-0) = (3,0,4)。结果的真正含义是「从 O 指向 P 的那支箭头」—— 它同时背着方向和长度两种信息，所以不能直接当「方向」用。",
                    "Vector subtraction is not comparing two points: each component is subtracted on its own, P - O = (3-0, 0-0, 4-0) = (3,0,4). The result means the arrow from O to P, and it carries both a direction and a length, which is why it cannot be used as the direction as-is."),
                DS(H(4), H(3),
                    "长度按勾股定理折成一个标量：√(3²+0²+4²) = 5。它只回答「多远」，不回答「朝哪」。同一支 dir 能取出这两个答案，区别全在你调哪个成员。",
                    "Length folds into one scalar by Pythagoras: sqrt(3*3 + 0*0 + 4*4) = 5. It answers how far, never which way. The same dir yields both answers - the difference is which member you call."),
                DS(H(5), H(4),
                    "normalized 把三个分量各自除以长度：(3,0,4) / 5.83 = (0.51,0,0.69)，方向一点没动、长度变成 1。Fire 只吃方向，长度对它没意义，所以传单位向量最省心也最不容易算出超大数值。",
                    "normalized divides each component by the length: (3,0,4) / 5.83 = (0.51,0,0.69) - direction unchanged, length becomes 1. Fire only consumes a direction, so a unit vector is both cleaner and safe from huge numbers."),
                DS(H(6), H(5, 6),
                    "Vector3.Distance(P, O) 与 (P - O).magnitude 是同一个数，只是一步算完；炮台固定在原点时它就等于 P.magnitude。距离用来判断「够不够近、值不值得打」，绝不能拿它当方向传给 Fire。",
                    "Vector3.Distance(P, O) equals (P - O).magnitude, just in one call; with the tower pinned at the origin it is simply P.magnitude. Range tells you whether a target is worth shooting - never pass it to Fire as a direction."),
                DS(H(7), H(8),
                    "唯一会让 Fire 立刻返回 false 的向量就是自己减自己：零向量没有方向，也算不出单位向量，引擎只能放弃这一发，而且不报任何错。写 o.Position - o.Position 或漏掉一个分量都会这样静默失败。",
                    "The one vector that makes Fire return false right away is a point minus itself: a zero vector has no direction and no unit form, so the engine quietly drops the shot without any error. o.Position - o.Position, or a missing component, fails exactly this way."),
                DS(H(8), H(6, 7),
                    "把三种用法记牢就不会混：要方向用 .normalized，要距离用 .magnitude 或 Vector3.Distance，要「朝某个方块打」就把它的 Position 直接传进 Fire —— 从原点指向目标的向量本身就是合法法向量。",
                    "Keep the three uses apart: .normalized for a direction, .magnitude or Vector3.Distance for range, and to shoot at a block pass its Position straight into Fire - the origin-to-target vector already is a valid normal.")
            },
            "相减出向量，magnitude 给长度，normalized 给方向；零向量会被 Fire 拒绝。",
            "Subtraction gives the vector, magnitude gives the range, normalized gives the direction; Fire refuses the zero vector.");

        // ---------- 任务 10：GetFireCooldown 读的是「还差几秒」，那 4 字节每帧由炮台自己减 ----------
        list[9].Mem = MSC(
            new[]
            {
                MV("float", "cd", "D5 78 E9 3C", "4 字节 = 0.0285f：距离下一发还差几秒，0 就是随时能打。").En("4 bytes = 0.0285f: seconds left before the next shot; 0 means ready."),
                MV("float", "dt", "70 CE 88 3C", "4 字节 = 0.0167f：60fps 一帧的秒数，炮台每帧从 cd 里减掉这么多。").En("4 bytes = 0.0167f, one frame at 60 fps, subtracted from cd every frame."),
                MV("bool", "ok", "00", "1 字节：cd <= 0f 算出来的结果，01 表示现在就能打。").En("1 byte: the result of cd <= 0f; 01 means you can fire now.")
            },
            new[]
            {
                MS("float cd = tower.GetFireCooldown();", 1, -1,
                    "它交回的是「距离下一发还差几秒」：0.0285 就是还得等 28.5 毫秒。这个数字是一次拷贝，抓的是调用那一瞬间的剩余量，不是活的引用。",
                    "It hands back the seconds until the next shot: 0.0285 means another 28.5 ms. The number is a copy taken at the instant of the call, not a live reference."),
                MS("// 下一帧：引擎内部 cd -= deltaTime", 2, -1,
                    "这一帧引擎自己把那 4 个字节减掉了 0.0167，变成 0.0118，而你的 cd 还停在 0.0285 —— 冷却由炮台每帧维护，你只能读，改不了。",
                    "This frame the engine subtracts 0.0167 from those bytes, leaving 0.0118, while your cd still says 0.0285 - the cooldown is maintained by the tower every frame and you can only read it.",
                    new[] { 0 }, new[] { "CA 54 41 3C" }).Code("// next frame: the engine runs cd -= deltaTime"),
                MS("bool ok = cd <= 0f;", 3, 2,
                    "判据必须写 <= 0：冷却中是正数、冷却结束才等于 0。写成 > 0 就正好反了 —— 这是这条 API 最容易写反的一行，而且不报错，只是永远不开火。",
                    "The test must be <= 0: while cooling it is positive and only reaches 0 when ready. Writing > 0 inverts everything - the easiest line to get backwards here, and it never errors, it just never fires."),
                MS("// 再减两帧：冷却见底", 3, -1,
                    "减到 0 以下引擎会夹回 0，不会给你 -0.02 这种负数，所以「就绪」的判据只有 0 一个值，不用考虑负号。",
                    "Past zero the engine clamps it back to 0 instead of going negative, so readiness is exactly 0 and you never have to reason about signs.",
                    new[] { 0 }, new[] { "00 00 00 00" }).Code("// two frames later: cooldown hits zero"),
                MS("if (cd <= 0f) tower.Fire(dir);", 3, -1,
                    "冷却是限流闸门：0.0285 秒比一帧 0.0167 秒长，所以哪怕代码每帧都调 Fire，也只能隔一帧多打出一次，其余全返回 false。先问冷却再开火，就省掉这些白跑。",
                    "The cooldown is a throttle: 0.0285 s is longer than one frame at 0.0167 s, so even a Fire call every frame succeeds only once per frame or two and the rest return false. Asking first saves those wasted calls."),
                MS("// 于是：一帧最多一发", 3, -1,
                    "把冷却理解成「上一发还没装填完」就不会写错：倒计时没走完，扳机扣下去也只是空响，不会有激光。",
                    "Read the cooldown as the previous shot still being loaded: while the countdown runs, pulling the trigger only clicks - no laser comes out.").Code("// so: at most one shot per frame")
            });

        // ---------- 任务 11：Fire 一次调用内部的固定顺序：先校验、再上锁、后结算 ----------
        list[10].Dia = DSC(
            new[]
            {
                DN("tower.Fire(normal)", 0.5f, 0.06f, DiaTone.Purple)
                    .En("tower.Fire(normal)").Size(0.30f),
                DN("① 法向量是零向量吗？\n是 → 返回 false", 0.24f, 0.28f, DiaTone.Alert)
                    .En("1) is the normal a zero vector?\nyes -> return false").Size(0.36f),
                DN("② 冷却还没走完吗？\ncd > 0 → 返回 false", 0.76f, 0.28f, DiaTone.Alert)
                    .En("2) still cooling down?\ncd > 0 -> return false").Size(0.36f),
                DN("③ 沿法线生成激光\n短暂显形", 0.76f, 0.50f, DiaTone.Accent)
                    .En("3) spawn the laser\nalong the normal, shown briefly").Size(0.36f),
                DN("④ 冷却重置为\ncooldownSeconds", 0.24f, 0.50f, DiaTone.Value)
                    .En("4) reset cd to\ncooldownSeconds").Size(0.32f),
                DN("⑤ 命中判定\n按 Volume 与位置算伤害", 0.76f, 0.72f, DiaTone.Value)
                    .En("5) resolve the hit\nvolume and position decide damage").Size(0.36f),
                DN("⑥ 返回 true", 0.24f, 0.72f, DiaTone.Purple)
                    .En("6) return true").Size(0.26f),
                DN("六步全在这一帧里跑完 → 每帧最多一发", 0.5f, 0.94f, DiaTone.Alert)
                    .En("all six run inside this one frame -> one shot per frame at most").Size(0.62f)
            },
            new[]
            {
                DE(0, 1).Lb("先校验", "validate first"),
                DE(1, 2).Lb("不是零向量", "not a zero vector"),
                DE(2, 3).Lb("冷却已好", "cooldown ready"),
                DE(3, 4).Lb("发完立刻上锁", "lock right after"),
                DE(2, 7).Lb("任一步失败 → 没有激光", "any failure -> no laser").Dash().Tint(DiaTone.Alert),
                DE(4, 5).Lb("同一帧算命中", "hit in the same frame"),
                DE(5, 6).Lb("成功", "success"),
                DE(6, 7).Lb("一次调用一件事", "one call, one job")
            },
            new[]
            {
                DS(H(0, 1), H(0),
                    "Fire 的第一件事不是发射，而是校验：法向量 (A,B,C) 近似为零就直接返回 false。因为零向量没有方向，后面的激光连朝向都算不出来 —— 这一步是在拦住算不出来的情况。",
                    "The first thing Fire does is not fire but validate: a normal (A,B,C) that is practically zero returns false immediately. A zero vector has no direction, so the laser could not even be oriented - this step blocks the impossible case."),
                DS(H(2), H(1),
                    "第二步查冷却：cd > 0 说明上一发还没装填完，同样返回 false。所以一次 Fire 失败基本只有这两种原因，碰到没反应先分清属于哪种，别瞎改方向。",
                    "Step two checks the cooldown: cd > 0 means the last shot is still loading, so it returns false as well. A failed Fire is almost always one of these two, so identify which before touching anything else."),
                DS(H(3), H(2),
                    "校验都过了才真正动手：沿 (A,B,C) 方向生成一条激光并短暂显形。注意顺序 —— 激光是「已经发生」的可视化结果，先射击才有画面，不是为了判定而先画一条。",
                    "Only after both checks does it act: a laser is spawned along (A,B,C) and shown briefly. Note the order - the beam visualises a shot that already happened; it is not drawn first in order to test something."),
                DS(H(4), H(3),
                    "紧接着把冷却重置成 cooldownSeconds，这一步在发射成功的瞬间完成。正因为锁在发完当时才扣下去，下一帧再调必然还在冷却里，限流是自然结果而不是额外判断。",
                    "Right after, the cooldown is reset to cooldownSeconds at the instant of success. Because the lock closes as soon as the shot leaves, the very next frame is still cooling - the throttle falls out naturally instead of needing extra checks."),
                DS(H(5), H(4),
                    "命中判定也在同一次调用里做完：激光扫到的方块按 Volume（体积越大扣得越多）和它当前的位置结算伤害。这属于引擎内部行为，你只需要知道结果在这一帧就已经定了。",
                    "Hit resolution also happens inside the same call: whatever the beam sweeps is damaged according to Volume (bigger means a harder hit) and its current position. That is engine internals; what matters is the outcome is fixed in this frame."),
                DS(H(6, 7), H(5, 6),
                    "全部走完才返回 true。理解这一点就明白「一帧连调三次打三发」为什么不可能：第一次成功后就扣了冷却，后两次都在第二步被拦下。",
                    "True comes back only after everything runs. That explains why three calls in one frame can never mean three shots: the first succeeds and locks the cooldown, the next two are stopped at step two.")
            },
            "先校验向量与冷却，射出的同一帧里扣冷却、算命中；失败只有两种原因。",
            "Validate the vector and the cooldown, then lock and resolve in the same frame; failure has only two causes.");

        // ---------- 任务 12：实战组合 —— 清屏 → 遍历 → 两道筛选 → 开火 → 立刻 break ----------
        list[11].Dia = DSC(
            new[]
            {
                DN("tower.ClearPrint()", 0.5f, 0.06f, DiaTone.Purple)
                    .En("tower.ClearPrint()").Size(0.30f),
                DN("foreach (o in tower.GetAllObjects())", 0.5f, 0.24f, DiaTone.Accent)
                    .En("foreach (o in tower.GetAllObjects())").Size(0.44f),
                DN("o.Type 是不是 ATTACK ？", 0.5f, 0.42f, DiaTone.Alert)
                    .En("is o.Type ATTACK ?").Size(0.34f),
                DN("不是红块\n→ 看下一个", 0.80f, 0.42f, DiaTone.Panel)
                    .En("not a red block\n-> next element").Size(0.30f),
                DN("o.Position.x > 0 ？", 0.5f, 0.60f, DiaTone.Alert)
                    .En("o.Position.x > 0 ?").Size(0.34f),
                DN("已经飞过去\n→ 也跳过", 0.80f, 0.60f, DiaTone.Panel)
                    .En("already passed\n-> skip it too").Size(0.30f),
                DN("tower.Fire(o.Position)", 0.28f, 0.80f, DiaTone.Value)
                    .En("tower.Fire(o.Position)").Size(0.34f),
                DN("break;\n一帧只打一发", 0.72f, 0.80f, DiaTone.Alert)
                    .En("break;\none shot per frame").Size(0.30f)
            },
            new[]
            {
                DE(0, 1).Lb("每帧先清屏", "clear first"),
                DE(1, 2).Lb("逐个拿一个", "one at a time"),
                DE(2, 3).Lb("类型不符", "type mismatch"),
                DE(2, 4).Lb("是红块", "it is red"),
                DE(4, 5).Lb("坐标已越界", "coordinate passed"),
                DE(4, 6).Lb("还在前方", "still in front"),
                DE(6, 7).Lb("打完立刻停", "stop right after")
            },
            new[]
            {
                DS(H(0, 1), H(0, 1),
                    "第一句先清屏：这段代码每帧都会重跑，不清的话上一帧的日志全堆着，二十四行的面板很快刷满，你根本看不出这一帧发生了什么。",
                    "Start by clearing: this code reruns every frame, so without a clear the previous logs pile up, the twenty-four line panel fills instantly and you cannot see what this frame did."),
                DS(H(2), H(1),
                    "接着逐个遍历：foreach 把 GETALL 此刻返回的方块快照一个个交给你。遍历期间才出场的方块不算进来，所以别指望这个循环能处理新来的目标。",
                    "Then walk the list: foreach hands you the snapshot returned by GetAllObjects at this instant. Blocks appearing while you loop are not included, so do not expect this loop to catch newcomers."),
                DS(H(3), H(2),
                    "第一道筛按 Type：不是 ATTACK 的（比如 BOMB）直接看下一个。先筛类型再做别的判断，能少算很多无用功，也避免把冷却浪费在不该打的目标上。",
                    "The first filter is Type: anything that is not ATTACK (a BOMB, say) moves on immediately. Filtering by type first saves work and keeps the cooldown for targets that deserve it."),
                DS(H(4), H(3),
                    "第二道筛按位置：Position.x > 0 说明它还在炮台前方；一旦变成负数就说明已经飞过去、正在离开，这时候打它没有意义。",
                    "The second filter is position: Position.x > 0 means the block is still in front of the tower; once it goes negative the block has passed and is leaving, so shooting it is pointless."),
                DS(H(5), H(4),
                    "两道都过就把它的 Position 直接当法向量传进 Fire：从原点指向目标的向量本身就是合法方向，不需要再手动归一化。注意这里传的是位置，不是 Distance 算出来的距离。",
                    "Both pass and its Position goes straight into Fire as the normal: the origin-to-target vector already is a valid direction, no manual normalisation needed. Note you pass a position, not the Distance value."),
                DS(H(6, 7), H(5, 6),
                    "最后 break 一条都不能少：一帧只打一发，打完立刻跳出。少了这句，循环会对每个红块都调一次 Fire，只有第一发真的出去，其余全卡在冷却上白跑。",
                    "The trailing break is essential: one shot per frame, so leave the loop right after firing. Without it every red block triggers a Fire, only the first really shoots and the rest burn through the cooldown for nothing.")
            },
            "清屏、遍历、先筛类型再筛位置、打进 Fire、立刻 break —— 这五件事合起来才是完整的每帧开火逻辑。",
            "Clear, iterate, filter by type then position, fire, break - those five together are the whole per-frame shooting logic.");
    }

    // ================== UI 构建 ==================

    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 叠在代码编辑器（sortingOrder = 200）之上，保证任务卡常驻可见
        canvas.sortingOrder = 210;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        // 点击进度格跳转任务需要射线检测器：本 Canvas 独立于代码编辑器 Canvas，必须自带一个
        gameObject.AddComponent<GraphicRaycaster>();

        // 保险：没有 EventSystem 时补一个（正常情况下代码编辑器已创建）
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        chipTexts = new Text[MaxChips];
        chipBgs = new Image[MaxChips];
        chipBaseColors = new Color[MaxChips];

        BuildTaskPanel(canvas.transform);
        BuildHintBar(canvas.transform);
    }

    private void BuildTaskPanel(Transform parent)
    {
        // 任务卡（指导框）：屏幕底部横向整条 —— 上下分栏布局。
        //   · 上边缘与代码编辑器（TowerCodeEditor）下边缘严丝合缝：面板高度会同步给
        //     CodeEditorController.SetBottomReservedHeight，编辑器整体上移收窄到上半区，两者不重叠；
        //   · 下边缘贴屏幕底部（anchoredPosition = 0），左右铺满整个可用宽度（anchorMin.x = 0 / anchorMax.x = 1）；
        //   · 高度固定为工作区高度的一半（ApplySplitLayout），与上方代码区严格对半平分，不随窗口 / 文案变化；
        //     字号固定不变，玩家想看全文就拖顶部蓝色长条把提示区拉高（不再自动缩字号）。
        RectTransform panelRt = CreateRect("SyntaxTaskPanel", parent, new Vector2(0f, 0f), Vector2.zero,
            new Vector2(0f, ReferenceHeight * SplitRatio), new Vector2(0.5f, 0f));
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(1f, 0f);
        panelRt.pivot = new Vector2(0.5f, 0f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(0f, ReferenceHeight * SplitRatio);
        panelRoot = panelRt.gameObject;

        Image bg = panelRoot.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.Panel;
        bg.raycastTarget = false; // 不挡编辑器输入：点击穿透到代码区

        // 顶部强调线（横向铺满）
        RectTransform accentRt = CreateRect("Accent", panelRt, new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 3f), new Vector2(0.5f, 1f));
        accentRt.anchorMin = new Vector2(0f, 1f);
        accentRt.anchorMax = new Vector2(1f, 1f);
        accentRt.pivot = new Vector2(0.5f, 1f);
        accentRt.anchoredPosition = Vector2.zero;
        accentRt.sizeDelta = new Vector2(0f, 3f);
        Image accent = accentRt.gameObject.AddComponent<Image>();
        accent.sprite = WhiteSprite();
        accent.color = UITheme.WithAlpha(UITheme.Accent, 0.95f);
        accent.raycastTarget = false;
        accentLine = accent;

        // 蓝色分割线可拖动：把热区做成一条透明横带，骑在强调线上，上下拖动即可调整提示区高度
        BuildSplitDragHandle(panelRt);

        // 标题
        headlineText = CreateText("Headline", panelRt, "", 24,
            UITheme.Accent, TextAnchor.MiddleLeft);
        RectTransform hlRt = headlineText.rectTransform;
        hlRt.anchorMin = new Vector2(0f, 1f);
        hlRt.anchorMax = new Vector2(0f, 1f);
        hlRt.pivot = new Vector2(0f, 1f);
        hlRt.anchoredPosition = new Vector2(24f, -12f);
        hlRt.sizeDelta = new Vector2(700f, 30f);

        // 进度「任务 k/15」
        progressText = CreateText("Progress", panelRt, "", 21,
            UITheme.TextBody, TextAnchor.MiddleRight);
        RectTransform pbRt = progressText.rectTransform;
        pbRt.anchorMin = new Vector2(1f, 1f);
        pbRt.anchorMax = new Vector2(1f, 1f);
        pbRt.pivot = new Vector2(1f, 1f);
        pbRt.anchoredPosition = new Vector2(-24f, -12f);
        pbRt.sizeDelta = new Vector2(220f, 30f);

        // 进度小格（预建 MaxChips 个，按当前课程任务数动态排布 / 显隐；宽度在 LayoutChips 里按面板实际宽度计算）
        for (int i = 0; i < MaxChips; i++)
        {
            RectTransform chipRt = CreateRect("Chip" + (i + 1), panelRt, new Vector2(0f, 1f), Vector2.zero,
                new Vector2(110f, 26f), new Vector2(0f, 1f));
            chipRt.anchoredPosition = new Vector2(24f + i * 114f, -50f);

            Image chipBg = chipRt.gameObject.AddComponent<Image>();
            chipBg.sprite = WhiteSprite();
            chipBg.color = UITheme.PanelSunk;
            chipBg.raycastTarget = true; // 进度格可点击（点数字直接跳到该任务）
            chipBgs[i] = chipBg;
            chipBaseColors[i] = chipBg.color;

            Text chipText = CreateText("ChipText" + (i + 1), chipRt, (i + 1).ToString(), 17,
                UITheme.TextMuted, TextAnchor.MiddleCenter);
            Stretch(chipText.rectTransform);
            chipText.raycastTarget = false;
            chipTexts[i] = chipText;

            // 点击跳转：在本关（同一门教学课）内任意任务之间互跳；不改已完成标记，也不跨关卡
            int chipIndex = i;
            Button chipBtn = chipRt.gameObject.AddComponent<Button>();
            chipBtn.targetGraphic = chipBg;
            chipBtn.transition = Selectable.Transition.None; // 颜色由代码统一控制，避免 ColorTint 覆盖
            chipBtn.onClick.AddListener(delegate { JumpToTask(chipIndex); });

            // 悬停变亮：给出「这个数字能点」的视觉反馈
            UnityEngine.EventSystems.EventTrigger hover = chipRt.gameObject
                .AddComponent<UnityEngine.EventSystems.EventTrigger>();
            UnityEngine.EventSystems.EventTrigger.Entry enter = new UnityEngine.EventSystems.EventTrigger.Entry();
            enter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enter.callback.AddListener(delegate { SetChipHover(chipIndex, true); });
            hover.triggers.Add(enter);
            UnityEngine.EventSystems.EventTrigger.Entry exit = new UnityEngine.EventSystems.EventTrigger.Entry();
            exit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exit.callback.AddListener(delegate { SetChipHover(chipIndex, false); });
            hover.triggers.Add(exit);

            chipRt.gameObject.SetActive(false);
        }

        // 任务名（横向铺满，右侧留 24 内边距）
        taskNameText = CreateText("TaskName", panelRt, "", 26, UITheme.TextPrimary, TextAnchor.MiddleLeft);
        RectTransform tnRt = taskNameText.rectTransform;
        tnRt.anchorMin = new Vector2(0f, 1f);
        tnRt.anchorMax = new Vector2(1f, 1f);
        tnRt.pivot = new Vector2(0f, 1f);
        tnRt.anchoredPosition = new Vector2(24f, -88f);
        // 右侧留出翻页条（‹ 说明 1/2 ›）的宽度，任务名不会与之重叠
        tnRt.sizeDelta = new Vector2(-48f - PageNavWidth - 12f, 36f);

        // 翻页条：与任务名同一行、贴右边界；只有该任务有第 2 页时才显示（见 RefreshPageNav）
        BuildPageNav(panelRt);

        // 正文分左右两栏：左 = 目标 / 提示 / 判定 + 完成条件，右 = 写法 / 示例代码，中间一条竖分隔线；
        // 这样左右两半都用起来，字号可以放大而不溢出（左右共用同一段正文区高度）。
        // 上下边距固定，正文区高度 = 半屏高度 - 上下留白；面板高度不因文案变化（ApplySplitLayout），
        // 字号恒定（PinBodyFontSize），不做任何自适应缩放。
        bodyText = CreateText("Body", panelRt, "", GuideBodyFontSize,
            UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform bodyRt = bodyText.rectTransform;
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(ColumnSplitRatio, 1f);
        bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.offsetMin = new Vector2(24f, GuideBodyBottomInset);
        bodyRt.offsetMax = new Vector2(-ColumnGap * 0.5f, -GuideBodyTopInset);
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        // 手动把分割线往下拖时字号保持不变，放不下的文字直接截断（不外溢到代码区）
        bodyText.verticalOverflow = VerticalWrapMode.Truncate;
        bodyText.lineSpacing = GuideBodyLineSpacing;
        bodyText.raycastTarget = false;

        // 右栏：写法 / 示例代码（无写法段时隐藏，左栏自动变成通栏）
        sampleText = CreateText("Sample", panelRt, "", SampleFontSize,
            UITheme.TextBody, TextAnchor.UpperLeft);
        RectTransform sampleRt = sampleText.rectTransform;
        sampleRt.anchorMin = new Vector2(ColumnSplitRatio, 0f);
        sampleRt.anchorMax = new Vector2(1f, 1f);
        sampleRt.pivot = new Vector2(0.5f, 0.5f);
        sampleRt.offsetMin = new Vector2(ColumnGap * 0.5f, GuideBodyBottomInset);
        sampleRt.offsetMax = new Vector2(-24f, -GuideBodyTopInset);
        sampleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        sampleText.verticalOverflow = VerticalWrapMode.Truncate;
        sampleText.lineSpacing = GuideBodyLineSpacing;
        sampleText.raycastTarget = false;

        // 第 3 页的内存可视化舞台：与两栏文字同层，覆盖整块正文区，平时隐藏、只在内存页露面。
        // 必须建在 bodyText / sampleText 之后才会盖在文字上面（同级 UICanvas 里后面的更靠前）。
        BuildMemStage(panelRt);

        // 第 3 页的逻辑图舞台：与内存舞台同层同位、互斥显隐（概念型知识点用它 —— 内存格表达不了的东西）
        BuildDiagramStage(panelRt);

        // 左 / 右分栏竖分隔线（贯穿正文区，用暗色细线，不抢文字）
        GameObject dividerGo = new GameObject("ColumnDivider", typeof(RectTransform));
        RectTransform dividerRt = dividerGo.GetComponent<RectTransform>();
        dividerRt.SetParent(panelRt, false);
        dividerRt.anchorMin = new Vector2(ColumnSplitRatio, 0f);
        dividerRt.anchorMax = new Vector2(ColumnSplitRatio, 1f);
        dividerRt.pivot = new Vector2(0.5f, 0.5f);
        dividerRt.offsetMin = new Vector2(-DividerWidth * 0.5f, GuideBodyBottomInset);
        dividerRt.offsetMax = new Vector2(DividerWidth * 0.5f, -GuideBodyTopInset);
        columnDivider = dividerGo.AddComponent<Image>();
        columnDivider.sprite = WhiteSprite();
        columnDivider.color = UITheme.HairlineDim;
        columnDivider.raycastTarget = false;
        dividerGo.SetActive(false);

        // 「任务完成 ✔」提示
        flashText = CreateText("Flash", panelRt, "", 20,
            UITheme.Value, TextAnchor.MiddleLeft);
        RectTransform flRt = flashText.rectTransform;
        flRt.anchorMin = new Vector2(0f, 0f);
        flRt.anchorMax = new Vector2(1f, 0f);
        flRt.pivot = new Vector2(0f, 0f);
        flRt.anchoredPosition = new Vector2(24f, 12f);
        flRt.sizeDelta = new Vector2(-48f, 28f);
        flashText.raycastTarget = false;

        panelRt.gameObject.SetActive(false);

        LayoutChips();
        ApplySplitLayout();
    }

    /// <summary>按当前课程任务数重新排布进度格（按当前课程任务数动态计算宽度），并隐藏多余格子。</summary>
    private void LayoutChips()
    {
        int n = TaskCount;
        if (n <= 0 || chipBgs == null)
            return;

        // 面板横向铺满全屏，格子宽度按面板实际宽度计算（屏宽变化时也能排满不断行）
        float area = Mathf.Max(240f, PanelWidth() - 48f); // 面板内可用宽度（左右各 24 内边距）
        float gap = n > 12 ? 3f : 6f;
        float w = Mathf.Clamp(Mathf.Floor((area - gap * (n - 1)) / n), 18f, 110f);
        for (int i = 0; i < MaxChips; i++)
        {
            if (chipBgs[i] == null)
                continue;
            bool show = i < n;
            if (chipBgs[i].gameObject.activeSelf != show)
                chipBgs[i].gameObject.SetActive(show);
            if (!show)
                continue;
            RectTransform rt = chipBgs[i].rectTransform;
            rt.sizeDelta = new Vector2(w, 26f);
            rt.anchoredPosition = new Vector2(24f + i * (w + gap), -50f);
            if (chipTexts[i] != null)
                chipTexts[i].fontSize = w >= 34f ? 17 : (w >= 24f ? 15 : 13);
        }
    }

    // ================== 指导框高度自适应（避免文案溢出黑框） ==================

    /// <summary>指导框横向铺满：返回当前面板实际宽度（首帧布局未完成时退回参考分辨率宽度）。</summary>
    private float PanelWidth()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        if (panelRt != null && panelRt.rect.width > 1f)
            return panelRt.rect.width;
        return ReferenceWidth;
    }

    /// <summary>工作区（面板父节点）的高度，拿不到时退回参考分辨率高度。</summary>
    private float WorkAreaHeight()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        RectTransform parentRt = panelRt != null ? panelRt.parent as RectTransform : null;
        return (parentRt != null && parentRt.rect.height > 1f) ? parentRt.rect.height : ReferenceHeight;
    }

    /// <summary>当前是否停在第 2 页（手把手细节）：只有这类任务才把提示区临时放大。</summary>
    private bool IsOnDetailPage()
    {
        return guidePage > 0 && !finished && tasks != null
            && CurrentIndex >= 0 && CurrentIndex < tasks.Length && PageCountOf(CurrentIndex) > 1;
    }

    /// <summary>
    /// 舞台的「基准高度」＝提示区不拖动时的高度减去上下内缩。
    /// 拖矮面板时内容仍按这个高度排，底部那块才会顺着往下走、移出视野。
    /// </summary>
    private float MemStageBaselineHeight()
    {
        return Mathf.Max(120f, SplitBaseHeight() - (MemStageTopInset + MemStageBottomInset));
    }

    /// <summary>
    /// 把「执行台 + 旁白」这一整块摆到内容的自然底部（layoutH 从舞台顶边往下量）。
    /// 面板被拖矮 → layoutH 不变，这一块就落到面板下沿之外（走出去）；
    /// 面板被拖高 → layoutH 跟着变大，这一块仍贴着真正的底边，不会悬在半空。
    /// </summary>
    private void PlaceMemBottom(float layoutH)
    {
        float noteTop = layoutH - MemNoteH;
        if (memNoteText != null)
            memNoteText.rectTransform.anchoredPosition = new Vector2(0f, -noteTop);
        if (memExprRt != null)
            memExprRt.anchoredPosition = new Vector2(0f, -(noteTop - MemNoteGapY - MemExprH));
    }

    /// <summary>提示区基准高度（不含手动拖动偏移）：第 1 页 = 工作区一半，第 2 页 = DetailPageRatio。</summary>
    private float SplitBaseHeight()
    {
        return Mathf.Max(120f, WorkAreaHeight() * (IsOnDetailPage() ? DetailPageRatio : SplitRatio));
    }

    /// <summary>
    /// 提示区实际高度 = 基准高度 + 玩家手动拖动蓝色分割线产生的偏移（manualSplitOffset），
    /// 并夹在 [MinHintHeight, 工作区高 - MinCodeHeight] 之间，保证代码区与提示区都还剩一截可用空间。
    /// </summary>
    private float SplitHalfHeight()
    {
        float total = WorkAreaHeight();
        float baseHeight = SplitBaseHeight();
        float minHeight = Mathf.Min(MinHintHeight, total * 0.55f);
        float maxHeight = Mathf.Max(minHeight + 40f, total - MinCodeHeight);
        return Mathf.Clamp(baseHeight + manualSplitOffset, minHeight, maxHeight);
    }

    /// <summary>
    /// 应用「上下各占一半」的固定分栏：指导框高度 = 工作区高度 × 50%，代码编辑器的底部预留高度同步为该值，
    /// 于是上方代码区与下方指引区永远对半平分、严丝合缝，不随窗口尺寸 / 文案长度漂移；
    /// 字号恒为固定值，不再随高度 / 栏宽自适应 —— 玩家想多看内容就拖蓝色长条把提示区拉高。
    /// </summary>
    private void ApplySplitLayout()
    {
        RectTransform panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        if (panelRt == null)
            return;

        float height = SplitHalfHeight();
        if (Mathf.Abs(panelRt.sizeDelta.y - height) > 0.5f)
            panelRt.sizeDelta = new Vector2(0f, height); // 横向靠 anchorMin.x=0 / anchorMax.x=1 铺满，只改高度

        appliedSplitHeight = height;
        // 高度被上下限夹住时把偏移量回写，避免继续往同一方向拖时偏移越积越大（回拖时手感发飘）
        manualSplitOffset = height - SplitBaseHeight();

        // 手动拖动分割线只改高度、不重排字号：字号恒定，放不下的部分直接截断（拖高即可看全）
        PinBodyFontSize();
        LayoutChips();

        // 关键：指导框高度即编辑器的「底部预留高度」→ 上方代码区与下方指导框各占一半，两者不重叠、中间不留缝
        if (editor != null && Active)
            editor.SetBottomReservedHeight(height);
    }

    /// <summary>
    /// 分栏布局：有示例代码 → 左 = 引导、右 = 代码、中间竖分隔线；没有示例代码 → 左栏通栏，右栏与分隔线隐藏。
    /// 分栏位置只由 LayoutGuideColumns 决定，与字号无关（字号固定，不再依赖栏宽自适应）。
    /// </summary>
    private void LayoutGuideColumns()
    {
        bool hasSample = sampleText != null && !string.IsNullOrEmpty(sampleText.text);
        columnHasSample = hasSample;

        if (columnDivider != null && columnDivider.gameObject.activeSelf != hasSample)
            columnDivider.gameObject.SetActive(hasSample);

        if (sampleText != null && sampleText.gameObject.activeSelf != hasSample)
            sampleText.gameObject.SetActive(hasSample);

        RectTransform bodyRt = bodyText != null ? bodyText.rectTransform : null;
        if (bodyRt == null)
            return;

        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(hasSample ? ColumnSplitRatio : 1f, 1f);
        bodyRt.offsetMin = new Vector2(24f, GuideBodyBottomInset);
        bodyRt.offsetMax = new Vector2(hasSample ? -ColumnGap * 0.5f : -24f, -GuideBodyTopInset);
    }

    /// <summary>
    /// 字号锁定：蓝色长条可拖动后不再做任何自适应缩放 —— 玩家想看全文就直接把提示区拉高，
    /// 因此这里只把左右两栏恢复到构建时的固定字号（切换任务 / 翻页后也不会被旧字号残留影响）。
    /// </summary>
    private void PinBodyFontSize()
    {
        PinOneFont(bodyText, GuideBodyFontSize);
        PinOneFont(sampleText, SampleFontSize);
    }

    /// <summary>把单个文本块的字号钉回固定值；字号确实变化过才重选字体（中文Noto Sans CJK SC / 英文 JetBrains Mono）。</summary>
    private static void PinOneFont(Text t, int size)
    {
        if (t == null || t.fontSize == size)
            return;

        t.fontSize = size;
        t.lineSpacing = GuideBodyLineSpacing;
        ReloadFont(t);
    }

    private void BuildHintBar(Transform parent)
    {
        // 编辑器关闭时（= 玩家眼前的实机页面：方块、炮台、print 荧幕）在页面正中挂一条进入提示，
        // 明确告知「按下 Enter 打开代码编辑界面」；按 Enter 后才弹出编辑器、任务卡随之出现。
        // 宽高在 RefreshHint / RefitHintBar 中按实测宽高自适应，长文案自动换行加高，不会被截断。
        RectTransform hintRt = CreateRect("SyntaxEntryPrompt", parent, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(HintBarMaxWidth, HintBarMinHeight), new Vector2(0.5f, 0.5f));
        hintRt.anchoredPosition = new Vector2(0f, EntryPromptY);
        hintRoot = hintRt.gameObject;
        hintRect = hintRt;

        Image bg = hintRoot.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = UITheme.ScrimSoft;
        bg.raycastTarget = false;

        hintText = CreateText("HintText", hintRt, "", 34, UITheme.TextPrimary, TextAnchor.MiddleCenter);
        Stretch(hintText.rectTransform);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hintText.verticalOverflow = VerticalWrapMode.Overflow;
        hintText.supportRichText = true; // 主行（按下 Enter）大号高亮、副行（当前任务）小号弱化
        hintText.raycastTarget = false;

        hintRoot.SetActive(false);
    }

    /// <summary>提示条高度 / 宽度自适应：按当前文案实测宽高，超出可用宽度时自动换行加高。</summary>
    private void RefitHintBar()
    {
        if (hintRect == null || hintText == null)
            return;

        string content = hintText.text == null ? "" : hintText.text;
        float maxWidth = Mathf.Min(HintBarMaxWidth, ReferenceWidth - 48f);

        TextGenerationSettings settings = hintText.GetGenerationSettings(new Vector2(maxWidth, 0f));
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        TextGenerator generator = hintText.cachedTextGeneratorForLayout;
        float scale = hintText.pixelsPerUnit > 0.01f ? hintText.pixelsPerUnit : 1f;

        float preferredWidth = generator.GetPreferredWidth(content, settings) / scale;
        float preferredHeight = generator.GetPreferredHeight(content, settings) / scale;

        float width = Mathf.Clamp(preferredWidth + 96f, HintBarMinWidth, maxWidth);
        float height = Mathf.Max(HintBarMinHeight, preferredHeight + 36f);
        hintRect.sizeDelta = new Vector2(width, height);
        hintRect.anchoredPosition = new Vector2(0f, EntryPromptY); // 始终居中偏上，不随文字长短漂移
    }

    // ================== UI 小工具（与 TutorialController 同风格） ==================

    private static Sprite whiteSprite;

    private static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }
        return whiteSprite;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private static Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = UIFontHelper.LoadForText(content, size);
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

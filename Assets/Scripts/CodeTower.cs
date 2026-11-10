using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 原点炮台（Code Tower）：
///   - 位置恒定在原点 (0,0,0)（每帧强制归位），为「代码编辑器」的用户代码提供可调用接口；
///   - 视觉：炮台本体永久隐藏（任何关卡 / 任何阶段都不现身，见 SetVisualVisible），
///     火力逻辑、代码编辑器、发射接口完全照常工作；
///   - 激光：从原点沿炮口当前朝向发射（射程 laserMaxLength=60、作用/视觉半径 laserRadius=0.02 超级小，
///     与鼠标左键触发的玩家激光等效的动画：双层圆柱 + 命中火花，淡出销毁），
///     每个命中 FlyingBlock 只扣 1 层 HP（CutByLaser 吃一层厚度，累计吃满该块 HP 即死亡；红色块 HP 随机 1~6，蓝色 BOMB 块 HP 恒为 1 故一击即毁）；
///     蓝色炸弹块（Bomb）可被激光正常击打并击毁，但击毁既不加分也不扣分（与红块同等切割流程）；
///     并触发 CommandConsoleController.OnLaserFired 事件；
///   - 冷却：cooldownSeconds = 0.0285s（约 35 发/秒，为 0.03s 节奏留余量；即原 0.019s 的 1.5 倍），
///     用 Time.time 判断，冷却中发射返回 false；
///   - 公开 API（签名固定，编辑器用户代码可直接调用）：
///     GetAllObjects() / FireAtPlane(A,B,C) / GetFireCooldown() /
///     Print(format, args...) / PrintLine(format, args...) / ClearPrint()。
///     FireAtPlane 的平面恒过原点，方程为 Ax+By+Cz=0，只需给出法向量 (A,B,C)。
///   - print 荧幕：立在蓝色 X 轴正方向（世界 +Z）上方、面朝原点的世界空间显示面板，
///     用户代码调用 Print / PrintLine 即把文本输出到荧幕上（不写 Unity Debug 日志）。
/// </summary>
public class CodeTower : MonoBehaviour
{
    /// <summary>
    /// 炮台本体是否永久隐藏（当前设计恒为 true：任何关卡、任何阶段都不显示炮台本体）。
    /// 置 false 仅用于调试（会恢复 SetVisualVisible(true) 的显示能力）。
    /// </summary>
    public const bool AlwaysHideVisual = true;

    [Header("激光")]
    [Tooltip("激光作用/视觉半径（粗细，超级小；仅影响视觉粗细，命中判定为射线与 AABB 求交，不依赖此值）")]
    public float laserRadius = 0.02f;
    [Tooltip("激光最大射程（单位）")]
    public float laserMaxLength = 60f;
    [Tooltip("激光视觉显示时长（秒，用不受时间冻结影响的真实时间）。" +
             "高频连射（0.02s）下需保持短促，否则同时叠加数十组激光视觉")]
    public float laserVisualDuration = 0.15f;
    [Tooltip("激光视觉颜色（亮青色）")]
    public Color laserColor = new Color(0.3f, 1f, 1f, 0.9f);

    [Header("炮台")]
    [Tooltip("开火冷却时间（秒，射击间隔下限）。0.0285s（约 35 发/秒，为原 0.019s 的 1.5 倍），"
             + "给用户代码 0.03s 节奏留出余量，避免内部冷却把间隔拦回更长")]
    public float cooldownSeconds = 0.0285f;
    [Tooltip("炮台主体颜色（深灰 #3A3A3A）")]
    public Color towerColor = new Color(0.227f, 0.227f, 0.227f, 1f);
    [Tooltip("炮口高亮颜色（亮青色）")]
    public Color muzzleColor = new Color(0.3f, 1f, 1f, 1f);

    // 视觉引用
    private GameObject baseVisual; // 底座（炮台实体根，SetVisualVisible 控制）
    private Transform barrelRoot;  // 炮管旋转轴（位于底座顶面中心）
    private Transform muzzleTip;   // 炮口端亮青色小球
    private LineRenderer aimLine;  // 瞄准预览线（内部 UpdateAimLine 控制）

    // 朝向与冷却
    private Vector3 aimDir = Vector3.forward;
    private float lastFireTime = -1000f; // 初始视为已冷却，可立即发射

    // print 荧幕（世界空间，立在蓝色 X 轴正方向即世界 +Z 上方、面朝原点，由 BuildPrintScreen 构建）
    private const float PrintScreenDistance = 12f;  // 距原点距离（沿世界 +Z，即蓝色 X 轴方向）
    private const float PrintScreenWidthMin = 5.2f; // 荧幕最小宽度（世界米，比原 3.4 加宽）
    private const float PrintScreenWidthMax = 9.5f; // 荧幕最大宽度（世界米，防止过宽遮挡场景）
    private const float PrintScreenHeight = 1.9f;   // 荧幕世界高度（米，初始值；高度自适应后动态调整）
    private const float PrintScreenHeightMin = 1.2f; // 荧幕最小高度（世界米，约 3 行）
    private const float PrintScreenHeightMax = 5.5f; // 荧幕最大高度（世界米，防止过高遮挡场景）
    private const float PrintScreenBaseY = 2.8f;    // 荧幕底部离地高度（米）
    private const int PrintMaxLines = 24;           // 荧幕最多保留行数（超出滚动丢弃最旧）
    private const int PrintFontSize = 26;           // 荧幕字号（Canvas 局部像素单位）
    private Text printText;                          // 荧幕文本（null 表示尚未构建）
    private RectTransform printCanvasRt;             // 荧幕 Canvas 的 RectTransform（自适应宽度用）
    private readonly List<string> printLines = new List<string>(); // 荧幕当前行

    /// <summary>剩余冷却秒数（0 表示已就绪）。</summary>
    private float CooldownLeft => Mathf.Max(0f, cooldownSeconds - (Time.time - lastFireTime));

    // ---------------- 公开 API（炮台代码编辑器用户代码可直接调用） ----------------

    /// <summary>场上单个 object 的信息：编号 / 类型 / HP / 位置 / 速度 / 参数。</summary>
    public struct TowerObjectInfo
    {
        public int Index;        // 编号（FlyingBlock 生成编号，由生成器全局递增分配，非数组下标）
        public string Type;      // 类型文本：ATTACK（红，击毁加分）/ BOMB（蓝，可击毁但不加分不扣分）/ HEAL（已废弃，不再生成）
        public int HP;           // 剩余血量（剩余受击层数）
        public Vector3 Position; // 几何中心（世界坐标）
        public Vector3 Velocity; // 速度向量（单位/秒，飞行方向 × 速度）
        public string Params;    // 参数摘要：尺寸 / 速度 / 受击进度等

        // ---- 计分 / 几何扩展字段（供用户代码做收益最大化决策；追加字段不影响任何现有按名访问） ----
        public float Volume;        // 当前剩余体积（随受击切割变小；用于估算漏块罚分）
        public float InitialVolume; // 初始体积（计分用，切割后不变）
        public int MaxHits;         // 初始受击层数（= FlyingBlock.maxHits；计分用）
        public Vector3 Size;        // 当前尺寸（三边长，用于激光路径 AABB 求交）

        public override string ToString()
        {
            return "{#" + Index + " " + Type + ", HP=" + HP + ", Position=" + Position.ToString("F2") +
                   ", Velocity=" + Velocity.ToString("F2") + ", [" + Params + "]}";
        }
    }

    /// <summary>返回场上所有活跃 object 的列表（含编号、类型、HP、位置、速度、参数）。</summary>
    public List<TowerObjectInfo> GetAllObjects()
    {
        List<TowerObjectInfo> result = new List<TowerObjectInfo>();
        FlyingBlockSpawner spawner = FlyingBlockSpawner.Instance;
        if (spawner == null || spawner.ActiveBlocks == null)
            return result;
        for (int i = 0; i < spawner.ActiveBlocks.Count; i++)
        {
            FlyingBlock b = spawner.ActiveBlocks[i];
            if (b == null || b.Destroyed)
                continue;
            result.Add(new TowerObjectInfo
            {
                Index = b.BlockId, // 编号 = 生成器分配的全局递增编号（非数组下标）
                Type = FlyingBlock.GetTypeText(b.Type),
                HP = b.RemainingHits,
                Position = b.GeometryCenter,
                Velocity = b.Velocity,
                Params = "size=" + b.size.ToString("F2") +
                         " speed=" + b.speed.ToString("F2") +
                         " hits=" + b.HitCount + "/" + b.maxHits,
                Volume = b.Volume,
                InitialVolume = b.InitialVolume,
                MaxHits = b.maxHits,
                Size = b.size
            });
        }
        return result;
    }

    /// <summary>
    /// 按过原点平面 Ax+By+Cz=0 发射：炮台从原点沿该平面法线方向 (A,B,C) 开火。
    /// 平面恒过原点（炮台就在原点），因此只需给出法向量，无需第四个参数 D。
    /// 冷却中返回 false；法向量 (A,B,C) 为零向量时返回 false。
    /// </summary>
    public bool FireAtPlane(float A, float B, float C)
    {
        Vector3 normal = new Vector3(A, B, C);
        if (normal.sqrMagnitude <= 0.0001f)
            return false;
        Aim(normal);   // 平面必过原点 → 从原点沿法线方向发射
        return FireLaser();
    }

    /// <summary>返回距下一发可发射的剩余冷却时间（秒，0 表示已就绪）。</summary>
    public float GetFireCooldown() => CooldownLeft;

    // ---------------- print 荧幕 API（用户代码调用后输出到 +X 前方荧幕，不写 Debug 日志） ----------------

    /// <summary>
    /// 向 +X 前方荧幕打印文本（不换行）。
    /// format 使用 C# 原生 string.Format 占位符（{0} {1} ...），等价于 C++ printf 的格式化风格。
    /// 示例：tower.Print("HP={0}", 100);
    /// </summary>
    public void Print(string format, params object[] args)
    {
        string text = (args != null && args.Length > 0) ? string.Format(format, args) : format;
        AppendToScreen(text, false);
    }

    /// <summary>
    /// 向 +X 前方荧幕打印文本并换行。
    /// format 使用 C# 原生 string.Format 占位符（{0} {1} ...），等价于 C++ printf 的格式化风格。
    /// 示例：tower.PrintLine("HP={0} pos={1}", hp, pos);
    /// </summary>
    public void PrintLine(string format, params object[] args)
    {
        string text = (args != null && args.Length > 0) ? string.Format(format, args) : format;
        AppendToScreen(text, true);
    }

    /// <summary>清空荧幕全部内容。</summary>
    public void ClearPrint()
    {
        printLines.Clear();
        RefreshPrintScreen();
    }

    /// <summary>
    /// 控制炮台实体视觉显隐（仅供关卡脚本 GameBootstrap / LevelDirector / TutorialController 调用，
    /// 不面向编辑器用户代码）。隐藏仅关掉底座 / 炮管 / 炮口 / 瞄准线，
    /// 激光发射等接口功能不受影响。
    /// <para>当前设计：炮台本体永久不现身（AlwaysHideVisual = true），
    /// 因此传入 true 也会被强制忽略、始终按隐藏处理，避免任何关卡 / 阶段意外露出炮台。</para>
    /// </summary>
    public void SetVisualVisible(bool visible)
    {
        bool finalVisible = visible && !AlwaysHideVisual;
        if (baseVisual != null)
            baseVisual.SetActive(finalVisible);
        if (barrelRoot != null)
            barrelRoot.gameObject.SetActive(finalVisible);
        // 瞄准线不随实体显隐：保持自身当前显隐状态（默认关闭）
    }

    /// <summary>强制隐藏炮台本体（Awake 构建视觉后调用一次，保证运行时任何阶段都不会露出炮台）。</summary>
    private void HideVisualNow()
    {
        if (baseVisual != null)
            baseVisual.SetActive(false);
        if (barrelRoot != null)
            barrelRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// 控制 print 输出荧幕（世界空间面板）显隐，仅供关卡脚本调用（Level 1 教学关不显示输出面板）。
    /// 隐藏期间 Print / PrintLine 仍照常写入内容，重新显示后内容立即正确。
    /// </summary>
    public void SetPrintScreenVisible(bool visible)
    {
        if (printCanvasRt != null)
            printCanvasRt.gameObject.SetActive(visible);
    }

    private void Awake()
    {
        BuildVisual();
        HideVisualNow();   // 炮台本体永久不现身：构建完视觉立刻隐藏，避免首帧闪一下
        BuildPrintScreen();
    }

    private void Update()
    {
        transform.position = Vector3.zero; // 位置恒定在原点

        // 炮管朝向与瞄准线持续跟随（防止外部直接改 transform 造成的漂移）
        if (barrelRoot != null)
            barrelRoot.localRotation = Quaternion.FromToRotation(Vector3.up, aimDir);
        if (aimLine != null && aimLine.gameObject.activeSelf)
            UpdateAimLine();
    }

    /// <summary>设置炮口朝向（非零向量归一化，旋转炮管子物体；零向量忽略）。仅供内部 / FireAtPlane 使用。</summary>
    private void Aim(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        aimDir = direction.normalized;
        if (barrelRoot != null)
            barrelRoot.localRotation = Quaternion.FromToRotation(Vector3.up, aimDir);
        if (aimLine != null && aimLine.gameObject.activeSelf)
            UpdateAimLine();
    }

    /// <summary>
    /// 发射激光：冷却中返回 false；成功则从炮口沿当前朝向发射
    /// （射程 60、半径 0.05；半径只影响视觉粗细，命中判定用射线 + AABB 求交），
    /// 触发 CommandConsoleController.OnLaserFired，并生成双层激光视觉（命中点带火花）。
    /// </summary>
    private bool FireLaser()
    {
        if (Time.time - lastFireTime < cooldownSeconds)
            return false;
        lastFireTime = Time.time;

        CommandConsoleController.NotifyLaserFired(); // 教学 / 自动化脚本监听

        Vector3 origin = MuzzleWorldPosition();
        Vector3 dir = aimDir;

        // 收集路径上所有命中（含子物体 collider，取所属 FlyingBlock 去重）
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, laserMaxLength);
        HashSet<FlyingBlock> hitBlocks = new HashSet<FlyingBlock>();
        float nearestT = laserMaxLength;
        for (int i = 0; i < hits.Length; i++)
        {
            FlyingBlock block = hits[i].collider != null
                ? hits[i].collider.GetComponentInParent<FlyingBlock>()
                : null;
            if (block == null || block.Destroyed)
                continue;

            // 蓝色炸弹块与红色攻击块同等对待：正常加入切割列表、正常可被击毁
            // （击毁蓝块不加分也不扣分，该规则集中由 ScoreManager.ApplyKill 处理）
            hitBlocks.Add(block);
            if (hits[i].distance < nearestT)
                nearestT = hits[i].distance;
        }

        // 逐个命中：每个块从入射侧吃掉一层厚度（红块血量随机 1~6，蓝块血量恒 1；吃满即死亡）
        foreach (FlyingBlock block in hitBlocks)
        {
            block.CutByLaser(origin, dir, laserRadius);
        }

        // 激光视觉：从炮口到最近命中点（未命中则打满射程）；命中任意目标时在端点额外显示火花特效
        Vector3 endPoint = origin + dir * nearestT;
        SpawnLaserVisual(origin, endPoint, hitBlocks.Count > 0);

        return true;
    }

    /// <summary>炮口世界位置（炮管末端炮口小球中心）。</summary>
    private Vector3 MuzzleWorldPosition()
    {
        if (muzzleTip != null)
            return muzzleTip.position;
        Vector3 pivot = barrelRoot != null ? barrelRoot.position : transform.position;
        return pivot + aimDir * 0.45f;
    }

    // ---------------- 视觉构建 ----------------

    private void BuildVisual()
    {
        // 底座：矮圆柱，深灰
        GameObject baseGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseGo.name = "TowerBase";
        baseGo.transform.SetParent(transform, false);
        baseGo.transform.localScale = new Vector3(0.9f, 0.09f, 0.9f); // 半径 0.45、高 0.18
        DisableLaserCollider(baseGo);
        baseGo.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial(towerColor);
        baseVisual = baseGo;

        // 炮管旋转轴：位于底座顶面中心（子物体）
        GameObject barrelGo = new GameObject("TowerBarrel");
        barrelGo.transform.SetParent(transform, false);
        barrelGo.transform.localPosition = new Vector3(0f, 0.18f, 0f);
        barrelRoot = barrelGo.transform;

        // 炮管主体：细长圆柱，深灰（长 0.9、半径 0.06，局部 +Y 为长轴）
        GameObject barrelBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrelBody.name = "BarrelBody";
        barrelBody.transform.SetParent(barrelRoot, false);
        barrelBody.transform.localScale = new Vector3(0.12f, 0.45f, 0.12f);
        DisableLaserCollider(barrelBody);
        barrelBody.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial(towerColor);

        // 炮口端：亮青色小球
        GameObject muzzle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        muzzle.name = "MuzzleTip";
        muzzle.transform.SetParent(barrelRoot, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        muzzle.transform.localScale = Vector3.one * 0.14f;
        DisableLaserCollider(muzzle);
        muzzle.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial(muzzleColor);
        muzzleTip = muzzle.transform;

        // 初始朝向：炮管指向 aimDir（默认 +Z）
        barrelRoot.localRotation = Quaternion.FromToRotation(Vector3.up, aimDir);

        // 瞄准预览线（默认隐藏）
        GameObject lineGo = new GameObject("AimLine");
        lineGo.transform.SetParent(transform, false);
        aimLine = lineGo.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.startWidth = 0.03f;
        aimLine.endWidth = 0.03f;
        aimLine.numCapVertices = 2;
        aimLine.numCornerVertices = 0;
        aimLine.sharedMaterial = CreateLaserMaterial(new Color(0.3f, 1f, 1f, 0.35f));
        aimLine.startColor = new Color(1f, 1f, 1f, 1f);
        aimLine.endColor = new Color(1f, 1f, 1f, 1f);
        lineGo.SetActive(false);
    }

    private void UpdateAimLine()
    {
        Vector3 origin = MuzzleWorldPosition();
        aimLine.SetPosition(0, origin);
        aimLine.SetPosition(1, origin + aimDir * laserMaxLength);
    }

    // ---------------- print 荧幕（世界空间显示面板，立在蓝色 X 轴正方向即世界 +Z 上方、面朝原点） ----------------

    /// <summary>
    /// 构建 print 荧幕：世界空间 Canvas，位于蓝色 X 轴（世界 +Z）正方向上方（面朝原点），
    /// 黑底半透明背景 + 亮青文字。此面板完全独立于 Unity Debug 日志，
    /// Print / PrintLine 的输出只会显示在荧幕上。
    /// </summary>
    private void BuildPrintScreen()
    {
        // 根物体：世界空间 Canvas（局部坐标固定 → 父级原点炮台归位不影响）
        GameObject canvasGo = new GameObject("PrintScreen");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = new Vector3(
            0f,
            PrintScreenBaseY + PrintScreenHeight * 0.5f,
            PrintScreenDistance);
        // 不旋转：Canvas 正面（局部 -Z）保持指向世界 -Z，即从世界 +Z 侧回望原点
        canvasGo.transform.localRotation = Quaternion.identity;
        canvasGo.transform.localScale = Vector3.one * 0.01f; // 1:100，sizeDelta 像素 → 世界米

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // 注意：WorldSpace Canvas 不挂 CanvasScaler，避免其重写 localScale 导致面板尺寸异常
        printCanvasRt = canvasGo.GetComponent<RectTransform>();
        printCanvasRt.sizeDelta = new Vector2(PrintScreenWidthMin / 0.01f, PrintScreenHeight / 0.01f);

        // 背景：极淡黑色衬底，保证文字可读但不形成大块黑色遮挡
        GameObject bgGo = new GameObject("PrintScreen_Bg");
        bgGo.transform.SetParent(canvasGo.transform, false);
        Image bg = bgGo.AddComponent<Image>();
        bg.color = UITheme.WithAlpha(UITheme.Void, 0.22f);
        RectTransform bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        // 文字：左上对齐、自动换行、超行滚动（RefreshPrintScreen 按内容自动选字体）
        GameObject textGo = new GameObject("PrintScreen_Text");
        textGo.transform.SetParent(canvasGo.transform, false);
        printText = textGo.AddComponent<Text>();
        printText.font = UIFontHelper.LoadForText("print screen", PrintFontSize);
        printText.fontSize = PrintFontSize;
        printText.color = UITheme.Accent;
        printText.alignment = TextAnchor.UpperLeft;
        printText.horizontalOverflow = HorizontalWrapMode.Wrap;
        printText.verticalOverflow = VerticalWrapMode.Overflow;
        printText.supportRichText = false;
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16f, 12f);
        textRt.offsetMax = new Vector2(-16f, -12f);

        printLines.Clear();
        printLines.Add("== print screen ==");
        RefreshPrintScreen();
    }

    /// <summary>向荧幕追加文本：newLine=false 时接在最后一行尾部，true 时新起一行；超出行数丢弃最旧。</summary>
    private void AppendToScreen(string text, bool newLine)
    {
        if (printText == null)
            return;
        if (!newLine && printLines.Count > 0)
            printLines[printLines.Count - 1] += text;
        else
            printLines.Add(text);
        if (printLines.Count > PrintMaxLines)
            printLines.RemoveRange(0, printLines.Count - PrintMaxLines);
        RefreshPrintScreen();
    }

    /// <summary>用当前行列表刷新荧幕文本；按内容自动切换中 / 英字体（中→微软雅黑，英→Consolas）。</summary>
    private void RefreshPrintScreen()
    {
        if (printText == null)
            return;
        string joined = string.Join("\n", printLines);
        Font want = UIFontHelper.LoadForText(joined, PrintFontSize);
        if (want != null && printText.font != want)
            printText.font = want;
        printText.text = joined;
        UpdatePrintScreenWidth();
        UpdatePrintScreenHeight();
    }

    /// <summary>
    /// 按最长文本行自适应荧幕宽度：临时切 Overflow 取 preferredWidth（整段不换行所需宽度），
    /// clamp 到 [PrintScreenWidthMin, PrintScreenWidthMax]；超出上限时保持 Wrap 在面板内折行，
    /// 避免面板过宽遮挡场景。
    /// </summary>
    private void UpdatePrintScreenWidth()
    {
        if (printText == null || printCanvasRt == null)
            return;
        HorizontalWrapMode prev = printText.horizontalOverflow;
        printText.horizontalOverflow = HorizontalWrapMode.Overflow; // 临时不换行，测真实宽度
        Canvas.ForceUpdateCanvases();
        float prefPx = printText.preferredWidth;
        printText.horizontalOverflow = prev;

        float maxPx = PrintScreenWidthMax / 0.01f;
        float minPx = PrintScreenWidthMin / 0.01f;
        float wPx = Mathf.Clamp(prefPx, minPx, maxPx);
        printCanvasRt.sizeDelta = new Vector2(wPx, printCanvasRt.sizeDelta.y); // y 由高度自适应 UpdatePrintScreenHeight 控制

        // 超过上限：保持自动换行，超长行在面板内折行显示
        printText.horizontalOverflow = prefPx > maxPx ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
    }

    /// <summary>
    /// 按文本实际行数 × 行高 + 上下边距自适应荧幕高度：宽度确定后读取 preferredHeight
    /// （此时已按最终宽度折行），clamp 到 [PrintScreenHeightMin, PrintScreenHeightMax]，
    /// 并同步中心 Y（底部基线 PrintScreenBaseY 固定，高度增加时整体上移）。
    /// </summary>
    private void UpdatePrintScreenHeight()
    {
        if (printText == null || printCanvasRt == null)
            return;
        Canvas.ForceUpdateCanvases(); // 确保 preferredHeight 按最新宽度 / 换行结果计算
        float contentPx = printText.preferredHeight;
        const float vMarginPx = 24f; // 上下各 12px（与文字 RectTransform offsetMin/offsetMax 一致）
        float minPx = PrintScreenHeightMin / 0.01f;
        float maxPx = PrintScreenHeightMax / 0.01f;
        float hPx = Mathf.Clamp(contentPx + vMarginPx, minPx, maxPx);
        printCanvasRt.sizeDelta = new Vector2(printCanvasRt.sizeDelta.x, hPx);

        // 底部基线固定：中心 Y = 底部离地高度 + 高度一半（localPosition 单位为世界米）
        Vector3 pos = printCanvasRt.localPosition;
        pos.y = PrintScreenBaseY + hPx * 0.01f * 0.5f;
        printCanvasRt.localPosition = pos;
    }

    // ---------------- 激光视觉（参考 CommandConsoleController 写法） ----------------

    /// <summary>生成激光视觉（双层圆柱：外层光晕 + 内芯高亮；命中时端点加火花小球），保留后淡出销毁。</summary>
    private void SpawnLaserVisual(Vector3 origin, Vector3 end, bool impact)
    {
        Vector3 delta = end - origin;
        float length = delta.magnitude;
        if (length < 0.01f)
            return;

        Vector3 center = (origin + end) * 0.5f;
        Vector3 dir = delta / length;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        // 外层光晕圆柱：半径 3 倍、低透明度，营造光束辉光
        GameObject haloGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        haloGo.name = "CodeTowerLaser_Halo";
        haloGo.transform.position = center;
        haloGo.transform.rotation = rot;
        haloGo.transform.localScale = new Vector3(laserRadius * 6f, length * 0.5f, laserRadius * 6f);
        DisableLaserCollider(haloGo);
        Material haloMat = CreateLaserMaterial(new Color(laserColor.r, laserColor.g, laserColor.b, 0.22f));
        haloGo.GetComponent<MeshRenderer>().sharedMaterial = haloMat;

        // 内芯圆柱：半径 1 倍、高亮，实际"激光"本体
        GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coreGo.name = "CodeTowerLaser_Core";
        coreGo.transform.position = center;
        coreGo.transform.rotation = rot;
        coreGo.transform.localScale = new Vector3(laserRadius * 2f, length * 0.5f, laserRadius * 2f);
        DisableLaserCollider(coreGo);
        Material coreMat = CreateLaserMaterial(laserColor);
        coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

        // 命中点特效：发光小球（偏暖色火花），随淡出一起缩小消失
        GameObject impactGo = null;
        Material impactMat = null;
        if (impact)
        {
            impactGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactGo.name = "CodeTowerLaser_Impact";
            impactGo.transform.position = end;
            impactGo.transform.localScale = Vector3.one * laserRadius * 3f;
            DisableLaserCollider(impactGo);
            impactMat = CreateLaserMaterial(new Color(1f, 0.85f, 0.35f, 0.95f));
            impactGo.GetComponent<MeshRenderer>().sharedMaterial = impactMat;
        }

        StartCoroutine(DestroyLaserVisual(coreGo, haloGo, impactGo, coreMat, haloMat, impactMat));
    }

    /// <summary>禁用并移除激光视觉物体的 Collider，避免干扰后续射线检测。</summary>
    private void DisableLaserCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null)
        {
            c.enabled = false;
            Destroy(c);
        }
    }

    /// <summary>创建激光特效材质（Sprites/Default 半透明叠加，缺失时退回 Unlit/Color）。</summary>
    private Material CreateLaserMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    /// <summary>创建实心材质（Unlit/Color 深灰 / 亮青，缺失时退回 Sprites/Default / Standard）。</summary>
    private Material CreateSolidMaterial(Color color)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    /// <summary>
    /// 激光视觉存活协程：前 40% 时长保持全亮，后 60% 时长线性淡出（真实时间，不受 timeScale=0 冻结影响），
    /// 命中火花同步缩小；结束后销毁全部视觉物体与材质。
    /// </summary>
    private IEnumerator DestroyLaserVisual(GameObject core, GameObject halo, GameObject impact, Material coreMat, Material haloMat, Material impactMat)
    {
        float t = 0f;
        float dur = Mathf.Max(0.05f, laserVisualDuration);
        Vector3 impactScale = impact != null ? impact.transform.localScale : Vector3.one;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            // 前 40% 全亮，后 60% 从 1 线性降到 0
            float fade = k < 0.4f ? 1f : Mathf.Clamp01(1f - (k - 0.4f) / 0.6f);

            if (coreMat != null) coreMat.color = FadeLaserAlpha(laserColor, fade);
            if (haloMat != null) haloMat.color = FadeLaserAlpha(haloMat.color, 0.22f * fade);
            if (impactMat != null) impactMat.color = FadeLaserAlpha(impactMat.color, fade);
            if (impact != null)
                impact.transform.localScale = impactScale * Mathf.Lerp(1f, 0.15f, k);

            yield return null;
        }

        if (core != null) Destroy(core);
        if (halo != null) Destroy(halo);
        if (impact != null) Destroy(impact);
        if (coreMat != null) Destroy(coreMat);
        if (haloMat != null) Destroy(haloMat);
        if (impactMat != null) Destroy(impactMat);
    }

    /// <summary>按给定 alpha 覆盖颜色透明度（保留 RGB）。</summary>
    private static Color FadeLaserAlpha(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }
}

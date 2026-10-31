using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 开发者指令框（Dev Console）：
///   - 按 `（BackQuote）键打开 / 再按 ` 关闭，ESC 关闭（输入框聚焦时 Enter 用于提交）；
///   - 识别「开发者指令」：/level &lt;关卡&gt; &lt;部分&gt; 跳转关卡分区（当前已实现 level 1 - part 1），
///     help 查看可用指令；原 laser / tp / timescale 等玩家指令已全部移除；
///   - 打开时：Time.timeScale = 0、释放鼠标，并通过
///     PlayerCameraController.SetExternalBlock(true) 占用视角输入；
///     关闭时恢复 timeScale = 1、锁定鼠标并解除占用。
/// 纯代码构建 UI，挂到任意物体即可。
/// </summary>
public class CommandConsoleController : MonoBehaviour
{
    /// <summary>激光发射事件（教学 / 自动化脚本监听用，鼠标左键与代码开火都会触发）。</summary>
    public static event System.Action OnLaserFired;

    [Header("按键")]
    [Tooltip("打开 / 切换指令框（` 键）")]
    public KeyCode openKey = KeyCode.BackQuote;
    [Tooltip("关闭指令框")]
    public KeyCode closeKey = KeyCode.Escape;

    [Header("UI")]
    [Tooltip("标题字号")]
    public int titleFontSize = 22;
    [Tooltip("输入框字号")]
    public int inputFontSize = 20;
    [Tooltip("输出区字号")]
    public int outputFontSize = 16;
    [Tooltip("面板底色")]
    public Color panelColor = new Color(0.05f, 0.06f, 0.10f, 0.92f);
    [Tooltip("输入框底色")]
    public Color inputBgColor = new Color(0f, 0f, 0f, 0.5f);
    [Tooltip("输出文字颜色")]
    public Color outputColor = new Color(0.85f, 0.95f, 1f, 1f);

    [Header("激光")]
    [Tooltip("激光半径（粗细），代码/指令参数可控制")]
    public float laserRadius = 0.15f;
    [Tooltip("激光最大射程（单位）")]
    public float laserMaxLength = 60f;
    [Tooltip("激光视觉显示时长（秒，用不受时间冻结影响的真实时间）")]
    public float laserVisualDuration = 0.9f;
    [Tooltip("激光视觉颜色")]
    public Color laserColor = new Color(0.3f, 1f, 1f, 0.9f);

    private Canvas canvas;
    private GameObject consoleRoot;
    private InputField inputField;
    private Text outputText;
    private PlayerCameraController playerController;
    // 打开指令框前游戏是否处于暂停（关闭后需还原暂停状态，不让游戏继续）
    private bool wasPausedBeforeOpen;
    // 打开指令框前是否处于 E 键时间冻结（timeScale=0，关闭后需保持冻结，不让游戏继续）
    private bool wasFrozenBeforeOpen;

    /// <summary>指令框当前是否打开。</summary>
    public bool IsOpen => consoleRoot != null && consoleRoot.activeSelf;

    /// <summary>玩家视角相机（自动化脚本取射线起点用）。</summary>
    public Camera PlayerViewCamera
        => playerController != null ? playerController.PlayerViewCamera : Camera.main;

    private void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        consoleRoot.SetActive(false);
    }

    private void Start()
    {
        playerController = FindObjectOfType<PlayerCameraController>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(openKey))
        {
            if (IsOpen)
            {
                // 打开状态下按 ` 键直接关闭；若 InputField 已捕获到反引号字符则先剔除
                if (IsInputFocused() && !string.IsNullOrEmpty(inputField.text) &&
                    inputField.text.EndsWith("`", System.StringComparison.Ordinal))
                    inputField.text = inputField.text.Substring(0, inputField.text.Length - 1);
                Close();
            }
            else
            {
                Open();
            }
            return;
        }

        if (IsOpen && Input.GetKeyDown(closeKey))
            Close();

        // 鼠标左键发射激光：指令框关闭、未被外部模态占用（如主页）、且非暂停状态时有效
        if (!IsOpen && Input.GetMouseButtonDown(0))
        {
            bool blocked = playerController != null && playerController.ExternalBlock;
            bool paused = playerController != null &&
                playerController.Mode == PlayerCameraController.GameMode.Paused;
            if (!blocked && !paused)
                FireLaser(laserRadius, false);
        }
    }

    // ---------------- 开关逻辑（与 PlayerCameraController 联动） ----------------

    /// <summary>打开指令框：暂停时间、释放鼠标、占用视角输入。</summary>
    public void Open()
    {
        if (playerController != null)
        {
            // 若暂停菜单正打开，先恢复，避免两个模态 UI 叠加；记录状态供关闭时还原
            wasPausedBeforeOpen = (playerController.Mode == PlayerCameraController.GameMode.Paused);
            // 记录打开前是否处于 E 键时间冻结（关闭后需保持冻结，不解除）
            wasFrozenBeforeOpen = playerController.IsTimeFrozen;
            if (wasPausedBeforeOpen)
                playerController.ResumeExternal();
            playerController.SetExternalBlock(true);
        }

        consoleRoot.SetActive(true);
        inputField.text = string.Empty;
        inputField.ActivateInputField();
        inputField.Select();

        Time.timeScale = 0f;
        SetCursorLocked(false);

        AppendOutput("开发者指令框已打开。输入 help 查看可用指令，Enter 提交，ESC 或 ` 关闭。");
        Debug.Log("[CommandConsole] 指令框打开");
    }

    /// <summary>关闭指令框：恢复时间流速、锁定鼠标、解除视角输入占用；若打开前处于暂停则还原暂停。</summary>
    public void Close()
    {
        consoleRoot.SetActive(false);
        if (playerController != null)
            playerController.SetExternalBlock(false);

        if (wasPausedBeforeOpen && playerController != null)
        {
            // 打开指令框前游戏处于暂停：关闭后恢复暂停状态，不让游戏继续
            playerController.PauseNow();
            wasPausedBeforeOpen = false;
            wasFrozenBeforeOpen = false;
        }
        else if (wasFrozenBeforeOpen)
        {
            // 打开指令框前处于 E 键时间冻结：保持 timeScale=0，不解除冻结
            SetCursorLocked(true);
            Time.timeScale = 0f;
            wasFrozenBeforeOpen = false;
        }
        else
        {
            Time.timeScale = 1f;
            SetCursorLocked(true);
        }
        Debug.Log("[CommandConsole] 指令框关闭");
    }

    private bool IsInputFocused()
    {
        EventSystem es = EventSystem.current;
        return es != null && es.currentSelectedGameObject == inputField.gameObject;
    }

    // ---------------- 指令执行 ----------------

    private void OnCommandSubmitted(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            inputField.ActivateInputField();
            inputField.Select();
            return;
        }

        string trimmed = text.Trim();
        AppendOutput("> " + trimmed);
        CommandData cmd = CommandParser.Parse(trimmed);
        Execute(cmd);

        // 提交后保持打开，方便连续输入；按 ESC 关闭
        inputField.text = string.Empty;
        inputField.ActivateInputField();
        inputField.Select();
    }

    /// <summary>按 CommandType 分支分发到具体执行函数（新增指令在此补充分支）。</summary>
    private void Execute(CommandData cmd)
    {
        switch (cmd.Type)
        {
            case CommandType.Help:
                ExecuteHelp();
                break;
            case CommandType.Level:
                ExecuteLevel(cmd);
                break;
            default:
                AppendOutput("未知指令：" + cmd.Raw + "（输入 help 查看可用指令）");
                break;
        }
    }

    private void ExecuteHelp()
    {
        AppendOutput(
            "开发者指令：\n" +
            "  /level <关卡> <部分>  - 跳转到指定关卡分区（如 /level 1 1）\n" +
            "  help                - 显示帮助");
    }

    /// <summary>
    /// 开发者指令 /level &lt;关卡&gt; &lt;部分&gt;：跳转到指定关卡的分区。
    /// 当前已实现 level 1 - part 1（回到本部分开头）；其余分区提示未开放。
    /// </summary>
    private void ExecuteLevel(CommandData cmd)
    {
        if (cmd.Args.Length < 2 ||
            !int.TryParse(cmd.Args[0], out int level) ||
            !int.TryParse(cmd.Args[1], out int part))
        {
            AppendOutput("[level] 用法：/level <关卡> <部分>，如 /level 1 1");
            return;
        }

        string message = LevelDirector.JumpTo(level, part);
        AppendOutput(message);
        if (message.Contains("已跳转"))
            CloseAndForceResume(); // 跳转成功后关闭指令框，回到正常游戏状态
    }

    /// <summary>强制关闭指令框并恢复正常游戏状态（关卡跳转后调用，忽略打开前的暂停 / 冻结状态）。</summary>
    public void CloseAndForceResume()
    {
        wasPausedBeforeOpen = false;
        wasFrozenBeforeOpen = false;
        Close();
    }

    /// <summary>
    /// 从玩家视角相机位置沿准心方向发射激光（鼠标左键与 laser 指令共用）。
    /// 命中路径上的所有长方体：每个被击中的块从入射侧沿激光主导轴吃掉一层厚度
    /// （每层 = 初始尺寸的 1/4），剩余部分保留为子块继续飞行；累计 4 次被吃光即死亡。
    /// 激光本体为受控的圆柱视觉，短暂显示后消失。
    /// </summary>
    public void FireLaser(float radius, bool reportToConsole)
    {
        OnLaserFired?.Invoke(); // 教学引导：记录玩家发射激光

        Camera cam = playerController != null ? playerController.PlayerViewCamera : Camera.main;
        if (cam == null)
        {
            if (reportToConsole)
                AppendOutput("[laser] 未找到玩家视角相机，无法发射。");
            return;
        }

        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;

        // 收集路径上所有命中（含子物体 collider，取所属 FlyingBlock 去重）
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, laserMaxLength);
        HashSet<FlyingBlock> hitBlocks = new HashSet<FlyingBlock>();
        float nearestT = laserMaxLength;
        for (int i = 0; i < hits.Length; i++)
        {
            FlyingBlock block = hits[i].collider != null
                ? hits[i].collider.GetComponentInParent<FlyingBlock>()
                : null;
            if (block != null && !block.Destroyed)
            {
                hitBlocks.Add(block);
                if (hits[i].distance < nearestT)
                    nearestT = hits[i].distance;
            }
        }

        // 逐个命中：每个块从入射侧吃掉一层厚度（受击四阶段），第 4 层吃光即死亡
        int hitCount = 0;
        int deathCount = 0;
        foreach (FlyingBlock block in hitBlocks)
        {
            List<FlyingBlock> remnants = block.CutByLaser(origin, dir, radius);
            if (remnants != null)
            {
                hitCount++;
                if (remnants.Count == 0)
                    deathCount++; // 被吃光死亡（无剩余子块）
            }
        }

        // 激光视觉：从发射点到最近命中点（未命中则打满射程）；命中时在端点额外显示火花特效
        Vector3 endPoint = origin + dir * nearestT;
        SpawnLaserVisual(origin, endPoint, radius, hitBlocks.Count > 0);

        if (reportToConsole)
            AppendOutput("[laser] 半径=" + radius.ToString("F2") + "，命中 " + hitCount + " 个长方体，吃掉 " + hitCount + " 层厚度，其中 " + deathCount + " 个被打满4层死亡。");
        Debug.Log("[CommandConsole] 发射激光：radius=" + radius + " hits=" + hitBlocks.Count + " deaths=" + deathCount);
    }

    /// <summary>
    /// 生成激光视觉（特效版）：双层圆柱（外层低透明度光晕 + 内芯高亮光束），
    /// 命中时在端点额外生成发光火花小球；整体保留 laserVisualDuration 秒后淡出并销毁
    /// （用不受时间冻结影响的真实时间）。
    /// </summary>
    private void SpawnLaserVisual(Vector3 origin, Vector3 end, float radius, bool impact)
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
        haloGo.name = "LaserVisual_Halo";
        haloGo.transform.position = center;
        haloGo.transform.rotation = rot;
        haloGo.transform.localScale = new Vector3(radius * 6f, length * 0.5f, radius * 6f);
        DisableLaserCollider(haloGo);
        Material haloMat = CreateLaserMaterial(new Color(laserColor.r, laserColor.g, laserColor.b, 0.22f));
        haloGo.GetComponent<MeshRenderer>().sharedMaterial = haloMat;

        // 内芯圆柱：半径 1 倍、高亮，实际"激光"本体
        GameObject coreGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coreGo.name = "LaserVisual_Core";
        coreGo.transform.position = center;
        coreGo.transform.rotation = rot;
        coreGo.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        DisableLaserCollider(coreGo);
        Material coreMat = CreateLaserMaterial(laserColor);
        coreGo.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

        // 命中点特效：发光小球（偏暖色火花），随淡出一起缩小消失
        GameObject impactGo = null;
        Material impactMat = null;
        if (impact)
        {
            impactGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactGo.name = "LaserImpact";
            impactGo.transform.position = end;
            impactGo.transform.localScale = Vector3.one * radius * 3f;
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

    /// <summary>
    /// 激光视觉存活协程：前 40% 时长保持全亮，后 60% 时长线性淡出（真实时间，不受 timeScale=0 冻结影响），
    /// 命中火花同步缩小；结束后销毁全部视觉物体。
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
    }

    /// <summary>按给定 alpha 覆盖颜色透明度（保留 RGB）。</summary>
    private static Color FadeLaserAlpha(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }


    private void AppendOutput(string line)
    {
        if (outputText == null)
            return;
        if (outputText.text.Length > 0)
            outputText.text += "\n" + line;
        else
            outputText.text = line;

        // 简单长度保护：过长时丢弃最早的一半
        if (outputText.text.Length > 2000)
            outputText.text = "[日志已截断]\n" + outputText.text.Substring(outputText.text.Length / 2);

        // v0.1.1：输出内容中英混合，按内容同步刷新字体（中文 → 微软雅黑，英文/数字 → Consolas）
        outputText.font = UIFontHelper.LoadForText(outputText.text, outputText.fontSize);
    }

    // ---------------- UI 构建 ----------------

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // ---- 面板 ----
        RectTransform panelRt = CreateRect("CommandConsole", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 480f), new Vector2(0.5f, 0.5f));
        consoleRoot = panelRt.gameObject;
        Image panelImg = consoleRoot.AddComponent<Image>();
        panelImg.color = panelColor;

        // ---- 标题 ----
        Text title = CreateText("Title", panelRt, "开发者指令框 (Dev Console)  -  ` 开关 / Enter 提交 / ESC 关闭", titleFontSize, Color.white, TextAnchor.MiddleLeft);
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -20f);
        titleRt.sizeDelta = new Vector2(-40f, 44f);

        // ---- 输出区 ----
        outputText = CreateText("Output", panelRt, "", outputFontSize, outputColor, TextAnchor.UpperLeft);
        RectTransform outRt = outputText.rectTransform;
        outRt.anchorMin = new Vector2(0f, 1f);
        outRt.anchorMax = new Vector2(1f, 1f);
        outRt.pivot = new Vector2(0.5f, 1f);
        outRt.anchoredPosition = new Vector2(0f, -78f);
        outRt.sizeDelta = new Vector2(-40f, -160f);
        outputText.supportRichText = false;
        outputText.horizontalOverflow = HorizontalWrapMode.Wrap;
        outputText.verticalOverflow = VerticalWrapMode.Overflow;

        // ---- 输入框 ----
        GameObject inputGo = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(panelRt, false);
        RectTransform inputRt = inputGo.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0f, 0f);
        inputRt.anchorMax = new Vector2(1f, 0f);
        inputRt.pivot = new Vector2(0.5f, 0f);
        inputRt.anchoredPosition = new Vector2(0f, 20f);
        inputRt.sizeDelta = new Vector2(-40f, 44f);

        Image inputBg = inputGo.GetComponent<Image>();
        inputBg.color = inputBgColor;

        InputField input = inputGo.GetComponent<InputField>();
        input.targetGraphic = inputBg;

        Text inputText = CreateText("Text", inputRt, "", inputFontSize, Color.white, TextAnchor.MiddleLeft);
        Stretch(inputText.rectTransform);
        inputText.supportRichText = false;
        inputText.font = UIFontHelper.LoadForText(inputText.text, inputText.fontSize); // v0.1.1：输入英文指令 → Consolas
        input.textComponent = inputText;

        Text placeholder = CreateText("Placeholder", inputRt, "输入开发者指令，如 /level 1 1 / help", inputFontSize, new Color(0.6f, 0.6f, 0.6f, 1f), TextAnchor.MiddleLeft);
        Stretch(placeholder.rectTransform);
        placeholder.supportRichText = false;
        placeholder.font = UIFontHelper.LoadForText(placeholder.text, placeholder.fontSize); // v0.1.1：占位含中文 → 微软雅黑
        input.placeholder = placeholder;

        input.lineType = InputField.LineType.SingleLine;
        input.onSubmit.AddListener(OnCommandSubmitted);
        inputField = input;
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
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

    private Text CreateText(string name, Transform parent, string content, int fontSizeValue, Color color, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSizeValue;
        text.color = color;
        text.alignment = anchor;
        text.font = UIFontHelper.LoadForText(content, fontSizeValue); // v0.1.1：按内容自动选字体（中文微软雅黑 / 英文 Consolas）
        text.raycastTarget = false;
        return text;
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

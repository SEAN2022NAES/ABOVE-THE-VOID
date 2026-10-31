using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 指令框（Command Console）：
///   - 按 Enter 打开 / 再按 Enter 关闭（输入框聚焦时 Enter 用于提交），ESC 关闭；
///   - 可识别「代码式指令」：基于 CommandType 枚举 + CommandData 结构体 +
///     CommandParser 解析器的分支结构，已预留 Laser（发射激光）、Teleport、
///     TimeScale 的解析分支与占位执行函数；
///   - 打开时：Time.timeScale = 0、释放鼠标，并通过
///     PlayerCameraController.SetExternalBlock(true) 占用视角输入；
///     关闭时恢复 timeScale = 1、锁定鼠标并解除占用。
/// 纯代码构建 UI，挂到任意物体即可。
/// </summary>
public class CommandConsoleController : MonoBehaviour
{
    [Header("按键")]
    [Tooltip("打开 / 切换指令框")]
    public KeyCode openKey = KeyCode.Return;
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
                // 输入框聚焦时 Enter 用于提交指令，不关闭；未聚焦时再按 Enter 关闭
                if (!IsInputFocused())
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

        AppendOutput("指令框已打开。输入 help 查看可用指令，Enter 提交，ESC 关闭。");
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
            case CommandType.Laser:
                ExecuteLaser(cmd);
                break;
            case CommandType.Teleport:
                ExecuteTeleport(cmd);
                break;
            case CommandType.TimeScale:
                ExecuteTimeScale(cmd);
                break;
            default:
                AppendOutput("未知指令：" + cmd.Raw + "（输入 help 查看可用指令）");
                break;
        }
    }

    private void ExecuteHelp()
    {
        AppendOutput(
            "可用指令：\n" +
            "  help            - 显示帮助\n" +
            "  laser [参数]    - 发射激光（占位预留）\n" +
            "  tp <x> <y> <z>  - 传送（占位预留）\n" +
            "  timescale <值>  - 时间流速（占位预留）");
    }

    /// <summary>
    /// 发射激光（占位预留）：当前仅打印日志 / 反馈，接口已固定，后续在此填充
    /// 激光射线生成、伤害判定、特效等真实逻辑。
    /// </summary>
    private void ExecuteLaser(CommandData cmd)
    {
        string argDesc = cmd.Args.Length > 0 ? string.Join(" ", cmd.Args) : "无";
        AppendOutput("[占位] laser 指令已识别：发射激光（尚未实现，预留扩展点）。参数：" + argDesc);
        Debug.Log("[CommandConsole] 发射激光（占位）被调用，参数：" + argDesc);
    }

    /// <summary>传送（占位预留）。</summary>
    private void ExecuteTeleport(CommandData cmd)
    {
        AppendOutput("[占位] tp 指令已识别：传送（尚未实现，预留扩展点）。参数：" +
                     (cmd.Args.Length > 0 ? string.Join(" ", cmd.Args) : "无"));
        Debug.Log("[CommandConsole] tp（占位）被调用，参数：" + string.Join(" ", cmd.Args));
    }

    /// <summary>时间流速（占位预留）。</summary>
    private void ExecuteTimeScale(CommandData cmd)
    {
        AppendOutput("[占位] timescale 指令已识别：调整时间流速（尚未实现，预留扩展点）。参数：" +
                     (cmd.Args.Length > 0 ? string.Join(" ", cmd.Args) : "无"));
        Debug.Log("[CommandConsole] timescale（占位）被调用，参数：" + string.Join(" ", cmd.Args));
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
        Text title = CreateText("Title", panelRt, "指令框 (Command Console)  -  Enter 提交 / ESC 关闭", titleFontSize, Color.white, TextAnchor.MiddleLeft);
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
        inputText.font = UIFontHelper.LoadMono();
        input.textComponent = inputText;

        Text placeholder = CreateText("Placeholder", inputRt, "输入指令，如 laser / tp 0 0 0 / help", inputFontSize, new Color(0.6f, 0.6f, 0.6f, 1f), TextAnchor.MiddleLeft);
        Stretch(placeholder.rectTransform);
        placeholder.supportRichText = false;
        placeholder.font = UIFontHelper.LoadMono();
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
        text.font = UIFontHelper.LoadDefault();
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

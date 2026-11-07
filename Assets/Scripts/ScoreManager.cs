using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Score system (per-run score + top-left HUD), built purely by code.
///   - Kill reward: base + volume bonus + difficulty bonus (see ApplyKill);
///   - Red (Attack) block reaching the zero plane (base) => penalty proportional to its remaining volume;
///   - Blue (Bomb) blocks are destroyable like red ones, but destroying them awards nothing and costs nothing
///     (no penalty entry point exists any more, so blue blocks are score-neutral).
/// Score may go negative (leak penalties always apply). HUD shows current score + last delta (fades after 1.2s).
/// All public entry points are static and null-safe.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Kill reward")]
    [Tooltip("Base score for destroying one block")]
    public int killBaseScore = 10;
    [Tooltip("Volume factor: volume bonus = round(volume * factor); volume = initial size x*y*z (about 0.064~1.0)")]
    public float killVolumeFactor = 20f;
    [Tooltip("Difficulty factor: difficulty bonus = factor * initial HP (HP 1~6)")]
    public int killBonusPerHp = 3;

    [Header("Base damage penalty (red block reached zero plane)")]
    [Tooltip("Base penalty")]
    public int leakBasePenalty = 5;
    [Tooltip("Volume factor: penalty = round(remaining volume * factor)")]
    public float leakVolumeFactor = 15f;

    /// <summary>Current score of this run (may be negative).</summary>
    public int Score { get; private set; }

    /// <summary>Last score change (positive = gain, negative = penalty, 0 = none).</summary>
    public int LastDelta { get; private set; }

    private Text scoreText;
    private Text deltaText;
    private float lastDeltaTime = -100f;
    private int shownDelta;
    private GameObject hudRoot;                       // 分数 HUD 根物体（SetHudVisible 控制显隐）
    private static bool hudVisibleRequested = true;    // HUD 显隐期望值（关卡可能在 Awake 前下发状态）

    // ---------------- static entry points (null-safe) ----------------

    /// <summary>Block destroyed: score by type / initial volume / initial HP (blue bomb is destroyable but awards nothing).</summary>
    public static void NotifyBlockDestroyed(FlyingBlock.BlockType type, float initialVolume, int maxHp)
    {
        if (Instance == null)
            return;
        Instance.ApplyKill(type, initialVolume, maxHp);
    }

    /// <summary>Red block reached the zero plane: penalty by remaining volume.</summary>
    public static void NotifyAttackLeaked(float remainVolume)
    {
        if (Instance == null)
            return;
        Instance.ApplyLeak(remainVolume);
    }

    /// <summary>Reset score of this run (called when a level starts / restarts).</summary>
    public static void ResetAll()
    {
        if (Instance != null)
            Instance.ResetScore();
    }

    /// <summary>
    /// 控制分数 HUD 显隐：Level 1 教学关隐藏（不显示 Score），其他关卡显示。
    /// 仅影响显示，计分逻辑照常运行（重新显示时数值立即正确）。
    /// </summary>
    public static void SetHudVisible(bool visible)
    {
        hudVisibleRequested = visible;
        if (Instance != null)
            Instance.ApplyHudVisible(visible);
    }

    private void ApplyHudVisible(bool visible)
    {
        if (hudRoot != null)
            hudRoot.SetActive(visible);
    }

    // ---------------- scoring ----------------

    private void ApplyKill(FlyingBlock.BlockType type, float initialVolume, int maxHp)
    {
        // 蓝色炸弹块可被激光击毁，但击毁既不加分也不扣分（直接返回，不产生任何分数变化）
        if (type == FlyingBlock.BlockType.Bomb)
            return;

        if (initialVolume < 0f)
            initialVolume = 0f;
        if (maxHp < 0)
            maxHp = 0;

        int gain = killBaseScore + Mathf.RoundToInt(killVolumeFactor * initialVolume) + killBonusPerHp * maxHp;
        AddScore(gain);
    }

    private void ApplyLeak(float remainVolume)
    {
        if (remainVolume < 0f)
            remainVolume = 0f;
        int penalty = leakBasePenalty + Mathf.RoundToInt(leakVolumeFactor * remainVolume);
        AddScore(-penalty);
    }

    private void AddScore(int delta)
    {
        if (delta == 0)
            return;
        Score += delta;
        LastDelta = delta;
        lastDeltaTime = Time.unscaledTime;
        shownDelta = delta;
        RefreshHud();
    }

    /// <summary>Reset score and HUD.</summary>
    public void ResetScore()
    {
        Score = 0;
        LastDelta = 0;
        shownDelta = 0;
        lastDeltaTime = -100f;
        RefreshHud();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
        ApplyHudVisible(hudVisibleRequested); // 关卡已下发隐藏状态时（Awake 前调用）直接生效
        RefreshHud();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (deltaText == null)
            return;
        if (deltaText.text.Length > 0 && Time.unscaledTime - lastDeltaTime > 1.2f)
            deltaText.text = "";
    }

    /// <summary>Refresh HUD text (white when >= 0, red when negative).</summary>
    private void RefreshHud()
    {
        if (scoreText == null)
            return;
        scoreText.text = "SCORE " + Score.ToString();
        scoreText.color = Score < 0 ? new Color(1f, 0.35f, 0.35f, 1f) : Color.white;

        if (deltaText != null)
        {
            if (shownDelta > 0)
            {
                deltaText.text = "+" + shownDelta;
                deltaText.color = new Color(0.35f, 1f, 0.6f, 1f);
            }
            else if (shownDelta < 0)
            {
                deltaText.text = shownDelta.ToString();
                deltaText.color = new Color(1f, 0.35f, 0.35f, 1f);
            }
            else
            {
                deltaText.text = "";
            }
        }
    }

    // ---------------- HUD (top-right) ----------------

    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 分数 HUD 需要始终压在主菜单 / 教程 / 代码编辑器等界面之上（右上角小块区域，不遮挡交互）
        canvas.sortingOrder = 100;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<UIFitScreen>();

        gameObject.AddComponent<GraphicRaycaster>();

        // 右上角：左上角已被 F3 调试覆盖层占用，分数 HUD 让位到右上角避免内容重合
        RectTransform root = CreateRect("ScoreRoot", canvas.transform, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(420f, 100f), new Vector2(1f, 1f));
        hudRoot = root.gameObject; // HUD 显隐总开关（Level 1 教学关隐藏）

        scoreText = CreateText("ScoreText", root, "SCORE 0", 40, Color.white, TextAnchor.UpperRight);
        RectTransform scoreRt = scoreText.rectTransform;
        scoreRt.anchorMin = new Vector2(1f, 1f);
        scoreRt.anchorMax = new Vector2(1f, 1f);
        scoreRt.pivot = new Vector2(1f, 1f);
        scoreRt.anchoredPosition = Vector2.zero;
        scoreRt.sizeDelta = new Vector2(420f, 56f);
        scoreText.fontStyle = FontStyle.Bold;
        scoreText.supportRichText = false;

        deltaText = CreateText("ScoreDeltaText", root, "", 28, Color.white, TextAnchor.UpperRight);
        RectTransform deltaRt = deltaText.rectTransform;
        deltaRt.anchorMin = new Vector2(1f, 1f);
        deltaRt.anchorMax = new Vector2(1f, 1f);
        deltaRt.pivot = new Vector2(1f, 1f);
        deltaRt.anchoredPosition = new Vector2(0f, -52f);
        deltaRt.sizeDelta = new Vector2(420f, 40f);
        deltaText.supportRichText = false;
    }

    private RectTransform CreateRect(string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        return CreateRect(name, transform, anchor, pos, size, pivot);
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
        text.font = UIFontHelper.LoadMono();
        text.raycastTarget = false;
        return text;
    }
}

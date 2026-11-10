using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全屏黑幕过渡（淡出 → 全黑瞬间回调 → 淡入）：
///   - 纯代码自举单例（不挂场景、不改 GameBootstrap），Canvas sortingOrder = 900，
///     压在所有界面之上（代码编辑器 200 / 主页 100 / 计分 HUD 100 / 引导教学 60）；
///   - PlayTransition(fadeOut, fadeIn, atBlack)：先淡到全黑，在全黑那一帧执行 atBlack（关卡跳转），
///     再淡回透明，避免关卡切换出现生硬瞬切；
///   - 计时用 Time.unscaledDeltaTime，E 键时间冻结 / 暂停状态下依然生效；
///   - 全屏遮罩 raycastTarget = false、不挂 GraphicRaycaster，过渡期间不拦截任何点击与输入。
/// </summary>
public class ScreenFade : MonoBehaviour
{
    public static ScreenFade Instance { get; private set; }

    /// <summary>黑幕排序值：高于代码编辑器（200）与主页（100），保证过渡时全屏可见。</summary>
    public int sortingOrder = 900;

    private Image overlay;
    private System.Action atBlack;
    private float fadeOutSeconds;
    private float fadeInSeconds;
    private float timer;
    private int phase; // 0 = 空闲；1 = 淡出到全黑；2 = 淡入到透明

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 播放一次「淡出到黑 → 全黑回调 → 淡入」过渡。
    /// 参数单位为秒（真实时间）；回调在画面完全变黑的那一帧只执行一次。
    /// </summary>
    public static void PlayTransition(float fadeOut, float fadeIn, System.Action onBlack)
    {
        ScreenFade fade = Instance;
        if (fade == null)
        {
            GameObject go = new GameObject("ScreenFade");
            fade = go.AddComponent<ScreenFade>(); // AddComponent 立即 Awake：内部完成 Canvas / 遮罩构建
        }
        fade.StartTransition(fadeOut, fadeIn, onBlack);
    }

    private void StartTransition(float fadeOut, float fadeIn, System.Action onBlack)
    {
        if (overlay == null)
            BuildUI();

        fadeOutSeconds = Mathf.Max(0f, fadeOut);
        fadeInSeconds = Mathf.Max(0f, fadeIn);
        atBlack = onBlack;
        timer = 0f;
        phase = 1;
        overlay.gameObject.SetActive(true);
        overlay.color = UITheme.WithAlpha(UITheme.Void, 0f);
    }

    private void Update()
    {
        if (phase == 1)
        {
            timer += Time.unscaledDeltaTime;
            float t = fadeOutSeconds <= 0.0001f ? 1f : Mathf.Clamp01(timer / fadeOutSeconds);
            overlay.color = UITheme.WithAlpha(UITheme.Void, t);
            if (t >= 1f)
            {
                // 全黑瞬间：执行关卡跳转等切换动作，随后开始淡入
                System.Action callback = atBlack;
                atBlack = null;
                if (callback != null)
                    callback();
                phase = 2;
                timer = 0f;
            }
        }
        else if (phase == 2)
        {
            timer += Time.unscaledDeltaTime;
            float t = fadeInSeconds <= 0.0001f ? 1f : Mathf.Clamp01(timer / fadeInSeconds);
            overlay.color = UITheme.WithAlpha(UITheme.Void, 1f - t);
            if (t >= 1f)
            {
                phase = 0;
                overlay.color = UITheme.WithAlpha(UITheme.Void, 0f);
                overlay.gameObject.SetActive(false);
            }
        }
    }

    // ---------------- UI 构建 ----------------

    private void BuildUI()
    {
        if (overlay != null)
            return;

        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        GameObject go = new GameObject("FadeOverlay", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        overlay = go.AddComponent<Image>();
        overlay.color = UITheme.WithAlpha(UITheme.Void, 0f);
        overlay.raycastTarget = false; // 过渡期间不拦截点击 / 输入
        overlay.gameObject.SetActive(false);
    }
}

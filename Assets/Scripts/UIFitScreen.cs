using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕等比适配组件（contain 模式）：
///   以设计基准分辨率（默认 1920x1080）为准，按当前窗口尺寸等比缩放，
///   scale = min(窗口宽/基准宽, 窗口高/基准高)，
///   任何分辨率 / 宽高比下所有 UI 都完整、等比例地放在窗口内部，不被裁切。
///   窗口尺寸变化（拖拽 Game 视图 / 独立窗口 / 全屏切换）时自动重算。
/// 使用方法：挂在每个 ScreenSpaceOverlay Canvas 所在物体上，会自动接管
///   CanvasScaler 的缩放（将其切换为 ConstantPixelSize，由本组件控制 scaleFactor）。
/// </summary>
[RequireComponent(typeof(Canvas))]
public class UIFitScreen : MonoBehaviour
{
    [Tooltip("UI 设计基准分辨率（宽）")]
    public float designWidth = 1920f;
    [Tooltip("UI 设计基准分辨率（高）")]
    public float designHeight = 1080f;

    private Canvas _canvas;
    private CanvasScaler _scaler;
    private int _lastWidth;
    private int _lastHeight;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _scaler = GetComponent<CanvasScaler>();
        if (_scaler != null)
        {
            // 交给本组件接管缩放（ConstantPixelSize 模式下 Canvas.scaleFactor 不会被覆盖）
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _scaler.scaleFactor = 1f;
        }
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
        Apply();
    }

    private void Update()
    {
        if (Screen.width == _lastWidth && Screen.height == _lastHeight) return;
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
        Apply();
    }

    /// <summary>等比例 contain 缩放：完整放入窗口、不裁切（窄屏/宽屏时可能留边）。</summary>
    private void Apply()
    {
        if (_canvas == null) return;
        float sx = Screen.width / Mathf.Max(1f, designWidth);
        float sy = Screen.height / Mathf.Max(1f, designHeight);
        _canvas.scaleFactor = Mathf.Min(sx, sy);
    }
}

using UnityEngine;

/// <summary>
/// 让挂载的物体始终面向玩家相机（Billboard 效果）。
/// 用于坐标轴字母标签，保证 X / Y / Z 无论相机如何旋转都正面朝向玩家。
/// </summary>
public class BillboardToCamera : MonoBehaviour
{
    private Vector3 baseScale;
    private float referenceDistance = 10f;
    private bool keepScreenSize;
    private bool horizontalFlip;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>让标签随相机距离缩放，保持屏幕上的视觉大小基本恒定。</summary>
    public void SetConstantScreenSize(float distance)
    {
        referenceDistance = Mathf.Max(0.01f, distance);
        baseScale = transform.localScale;
        keepScreenSize = true;
    }

    public void SetHorizontalFlip(bool flip)
    {
        horizontalFlip = flip;
        baseScale.x = Mathf.Abs(baseScale.x) * (horizontalFlip ? -1f : 1f);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        // 从标签指向相机的方向；LookRotation 使 +Z 朝向相机，
        // SpriteRenderer 图片的可读面为 +Z 面，正面始终朝向相机
        Vector3 dir = cam.transform.position - transform.position;
        if (dir.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        if (keepScreenSize)
        {
            float distanceScale = dir.magnitude / referenceDistance;
            transform.localScale = baseScale * Mathf.Max(0.01f, distanceScale);
        }
    }
}

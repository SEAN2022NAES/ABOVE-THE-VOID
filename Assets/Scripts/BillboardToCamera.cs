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

        // 从标签指向相机的方向
        Vector3 toCam = cam.transform.position - transform.position;
        if (toCam.sqrMagnitude < 0.0001f)
            return;

        // TextMesh 的字形正面在本地 -Z 侧（从 local -Z 看过去才是正读方向），
        // 而默认字体材质是双面（Cull Off），用 +Z 朝相机时看到的是字形背面 →
        // 文字仍可见但左右镜像，表现为字母 "Z" 反着显示。
        // 因此这里让 local +Z 背向相机：forward = -toCam。
        Vector3 forwardDir = (-toCam).normalized;

        // 文字"上"方向取相机 up 在垂直于视线平面上的投影（保证与屏幕对齐、正立）。
        // 若视线与相机 up 共线（相机正上方/正下方俯仰看标签），该投影退化，
        // 退而用相机 forward 的反向投影兜底；仍退化则用相机 right，
        // 避免 LookRotation 在退化姿态下随机翻滚造成倒字/镜像。
        Vector3 up = cam.transform.up;
        Vector3 upProj = up - forwardDir * Vector3.Dot(up, forwardDir);
        if (upProj.sqrMagnitude < 1e-4f)
        {
            Vector3 alt = -cam.transform.forward;
            upProj = alt - forwardDir * Vector3.Dot(alt, forwardDir);
            if (upProj.sqrMagnitude < 1e-4f)
                upProj = cam.transform.right;
        }

        transform.rotation = Quaternion.LookRotation(forwardDir, upProj.normalized);

        if (keepScreenSize)
        {
            float distanceScale = toCam.magnitude / referenceDistance;
            transform.localScale = baseScale * Mathf.Max(0.01f, distanceScale);
        }
    }
}

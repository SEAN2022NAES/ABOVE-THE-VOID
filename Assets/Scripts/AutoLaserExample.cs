using UnityEngine;

/// <summary>
/// 自动化激光示例（教学引子）：用 C# 让程序自动瞄准并发射激光。
///
/// 教学对应关系（Level 1 - Part 1 最后一步「引出 C# 自动化激光」）：
///   1. 从玩家视角相机发射一条射线（与鼠标左键同一准心方向）；
///   2. 若射线命中 FlyingBlock，则调用 CommandConsoleController.FireLaser(...) 自动开火；
///   3. FireLaser 是公开接口，与鼠标左键共用同一套激光逻辑（每击吃掉 1 层，层数 = 该方块最大 HP，吃满即消灭）。
///
/// 使用方式：把本脚本挂到任意 GameObject 上，勾选 autoFire 即开始自动打方块。
/// 教学阶段请保持 autoFire = false，先手动感受；下一课再逐行实现并扩展
/// （如自动转向最近的方块、按类型优先、多目标轮询、绕障走位等）。
/// </summary>
public class AutoLaserExample : MonoBehaviour
{
    [Header("自动化开关")]
    [Tooltip("是否自动开火（教学建议先保持关闭，先手动感受激光机制）")]
    public bool autoFire = false;
    [Tooltip("扫描射程（与 CommandConsoleController.laserMaxLength 一致即可）")]
    public float scanRange = 60f;
    [Tooltip("自动开火间隔（秒）")]
    public float fireInterval = 0.5f;

    private CommandConsoleController console;
    private float fireTimer;

    private void Start()
    {
        console = FindObjectOfType<CommandConsoleController>();
    }

    private void Update()
    {
        if (!autoFire || console == null)
            return;

        fireTimer += Time.deltaTime;
        if (fireTimer < fireInterval)
            return;
        fireTimer = 0f;

        // 1. 从玩家视角相机发射一条射线（与鼠标左键同一方向）
        Camera cam = console.PlayerViewCamera;
        if (cam == null)
            return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, scanRange))
            return;

        // 2. 确认命中的是飞行长方体（子物体 collider 需向上找到所属块）
        FlyingBlock block = hit.collider != null
            ? hit.collider.GetComponentInParent<FlyingBlock>()
            : null;
        if (block == null || block.Destroyed)
            return;

        // 3. 自动开火：调用公开接口（与鼠标左键同一套激光逻辑）
        console.FireLaser(0.15f, false);
    }
}

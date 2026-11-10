using UnityEngine;

/// <summary>
/// 点击落点守卫的执行顺序锚点。
///
/// 为什么需要这个组件：uGUI 的 InputField 在自己的 LateUpdate 里才做真正的「激活」与光标改写
/// （ActivateInputFieldInternal / 光标钳位 / UpdateLabel）。CodeEditorController 的 LateUpdate 默认
/// executionOrder 同样是 0，与 InputField 的先后次序不确定——只要控制器先跑，它写入的
/// caretPosition / selectionAnchorPosition 就会在同一帧内被 InputField 立刻覆盖，
/// 表现为「守卫代码写了、也执行了，但画面上完全没效果」。
///
/// 把守卫挪到 executionOrder = 20000 的 LateUpdate，保证它一定晚于 InputField（默认 0）执行，
/// 本帧写入的光标就是最终值。每帧只让第一个触发的实例推进一次守卫，避免多分块重复执行。
/// </summary>
[DefaultExecutionOrder(20000)]
public class CodeEditorCaretGuard : MonoBehaviour
{
    public CodeEditorController owner;

    private void LateUpdate()
    {
        if (owner != null)
            owner.LateGuardTick();
    }
}

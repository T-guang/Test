using UnityEngine;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 工作区的 Unity 视图对象生命周期入口。
    /// Editor 非运行态的验证会立即检查层级、Wire 段和绑定数量；此时延迟 Destroy 会留下当前帧对象并产生警告。
    /// Play Mode 和 Player 仍使用 Unity 的延迟 Destroy，避免在运行中的 UI 事件链中立即销毁对象。
    /// </summary>
    internal static class SpiceUnityObjectLifetime
    {
        internal static void Destroy(Object target)
        {
            if (target == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(target);
                return;
            }
#endif

            Object.Destroy(target);
        }
    }
}

namespace ElectricalSim.UI
{
    /// <summary>
    /// 跟踪当前打开的模态弹窗数量，供画布缩放入口判断是否应拒绝滚轮。
    /// 弹窗在 Show/Hide 时通过 NotifyOpened/NotifyClosed 维护计数，
    /// 不使用 FindObjectOfType 或 GameObject.Find，不依赖 GameObject 名称。
    /// </summary>
    public static class ModalInputGate
    {
        private static int openCount;

        /// <summary>当前是否有任意模态弹窗处于打开状态。</summary>
        public static bool IsAnyOpen => openCount > 0;

        /// <summary>弹窗打开时调用，计数 +1。</summary>
        public static void NotifyOpened()
        {
            openCount++;
        }

        /// <summary>弹窗关闭时调用，计数 -1，最小为 0。</summary>
        public static void NotifyClosed()
        {
            openCount = openCount > 0 ? openCount - 1 : 0;
        }

        /// <summary>仅用于测试重置状态，不在生产代码中调用。</summary>
        public static void ResetForTests()
        {
            openCount = 0;
        }
    }
}

using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 为拓扑搜索提供统一的安全预算，防止异常自锁、短接或嵌套回路导致无限遍历。
    /// 这些数值不是课程电路规模上限；达到预算时调用方必须保守降级并报告不支持拓扑。
    /// 调整后必须运行 Topology Safety 测试及真实模板基线。
    /// </summary>
    public static class TopologyTraversalLimits
    {
        public const int MaxTraversalSteps = 4096;
        public const int MaxSearchDepth = 256;
        public const int MaxVisitedNodes = 2048;
        public const int MaxVisitedEdges = 8192;

        public const string ComplexTopologyRuleId = "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY";
        public const string ComplexTopologyTitle = "复杂回路或暂不支持的嵌套结构";
        public const string ComplexTopologyMessage =
            "当前接线存在复杂回路或不受支持的嵌套结构，系统已停止继续追踪该路径，请检查自锁、互锁或短接支路。";

#if UNITY_EDITOR
        public static int CurrentMaxTraversalSteps = MaxTraversalSteps;
        public static int CurrentMaxVisitedNodes = MaxVisitedNodes;
        public static int CurrentMaxVisitedEdges = MaxVisitedEdges;

        public static void SetEditorTestingLimits(int steps, int nodes, int edges)
        {
            CurrentMaxTraversalSteps = steps;
            CurrentMaxVisitedNodes = nodes;
            CurrentMaxVisitedEdges = edges;
        }

        public static void RestoreDefaultLimits()
        {
            CurrentMaxTraversalSteps = MaxTraversalSteps;
            CurrentMaxVisitedNodes = MaxVisitedNodes;
            CurrentMaxVisitedEdges = MaxVisitedEdges;
        }
#endif

        public static bool IsTraversalBudgetExceeded(int steps, int visitedNodes, int visitedEdges)
        {
            // Editor 中允许测试缩小预算以覆盖降级路径，正式运行始终使用固定安全边界。
#if UNITY_EDITOR
            return steps > CurrentMaxTraversalSteps ||
                visitedNodes > CurrentMaxVisitedNodes ||
                visitedEdges > CurrentMaxVisitedEdges;
#else
            return steps > MaxTraversalSteps ||
                visitedNodes > MaxVisitedNodes ||
                visitedEdges > MaxVisitedEdges;
#endif
        }

        public static void LogTraversalBudgetExceeded(string context)
        {
            Debug.LogWarning("[TopologyTraversal] " + ComplexTopologyMessage + " Context: " + context);
        }
    }
}

using System.Collections.Generic;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 集中同元件跳线策略：判定同一元件内部两个端子之间是否允许创建跳线。
    ///
    /// 本策略是 WireManager.CanCreateWire 和 SaveLoadService 导入预检的共同规则来源，
    /// 取代原先散落在两处的本地星三角白名单和字符串特判。
    ///
    /// 判定依据仅使用正式 ComponentKind 和 ComponentDefinition 字段（kind、allowSameComponentJumper、terminals），
    /// 不依赖 displayName、definition.name.Contains、GameObject.name 或 Prefab 名称。
    ///
    /// 规则顺序：
    /// 1. definition 为 null → 拒绝；
    /// 2. 任一 terminalId 为空 → 拒绝；
    /// 3. startTerminalId == endTerminalId → 拒绝（自连，Ordinal 比较）；
    /// 4. definition.terminals 为 null → 确定性拒绝；
    /// 5. 任一端点不存在于 definition.terminals → 拒绝（Ordinal 比较）；
    /// 6. allowSameComponentJumper=false → 拒绝；
    /// 7. ComponentKind.ContactorCoil → 两个不同且真实存在的端点均允许（含 L1/T1、A1/A2、13/14、21/22、33/34），
    ///    连接层不判断危险旁路或短路；
    /// 8. ComponentKind.Motor → 只允许 U1/V1/W1/U2/V2/W2 之间跳线，PE 和其他端点拒绝；
    /// 9. 其他元件 → 保持拒绝。
    ///
    /// 本策略不负责 duplicate wire、画布锁定、不同元件接线或运行时电气导通；
    /// 这些由 WireManager、WorkspaceController 和 SimulationEngine 各自负责。
    /// </summary>
    public static class SameComponentWirePolicy
    {
        // 星三角电机绕组端子白名单（仅用于 ComponentKind.Motor 分支，不依赖 definition.name）。
        // Motor_ThreePhase 等非星三角电机的 allowSameComponentJumper=false，会在规则 5 被拒绝，
        // 不会进入此分支；因此该集合只对 allowSameComponentJumper=true 的 Motor 生效。
        private static readonly HashSet<string> StarDeltaWindingTerminals = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "U1", "V1", "W1", "U2", "V2", "W2"
        };

        /// <summary>
        /// 判定同一元件内部两个端子之间是否允许创建跳线。
        /// </summary>
        /// <param name="definition">元件定义，提供 kind、allowSameComponentJumper 和 terminals。</param>
        /// <param name="startTerminalId">起点端子 id。</param>
        /// <param name="endTerminalId">终点端子 id。</param>
        /// <param name="rejectionReason">拒绝原因，允许时为空字符串。</param>
        /// <returns>true 表示允许创建跳线；false 表示拒绝。</returns>
        public static bool CanConnect(
            ComponentDefinition definition,
            string startTerminalId,
            string endTerminalId,
            out string rejectionReason)
        {
            rejectionReason = string.Empty;

            // 1. definition 为 null
            if (definition == null)
            {
                rejectionReason = "元件定义为空，不允许同一器件内部端子跳线。";
                return false;
            }

            // 2. 任一 terminalId 为空
            if (string.IsNullOrEmpty(startTerminalId) || string.IsNullOrEmpty(endTerminalId))
            {
                rejectionReason = "端子标识为空，不允许同一器件内部端子跳线。";
                return false;
            }

            // 3. 自连（显式 Ordinal 比较）
            if (string.Equals(startTerminalId, endTerminalId, System.StringComparison.Ordinal))
            {
                rejectionReason = "不能将端子连接到自身。";
                return false;
            }

            // 4. definition.terminals 为 null 时确定性拒绝
            if (definition.terminals == null)
            {
                rejectionReason = "元件定义端子列表为空，不允许同一器件内部端子跳线。";
                return false;
            }

            // 5. 任一端点不存在（显式 Ordinal 比较）
            var startTerm = definition.terminals.Find(t => t != null && string.Equals(t.id, startTerminalId, System.StringComparison.Ordinal));
            var endTerm = definition.terminals.Find(t => t != null && string.Equals(t.id, endTerminalId, System.StringComparison.Ordinal));
            if (startTerm == null || endTerm == null)
            {
                rejectionReason = "端点不存在，不允许同一器件内部端子跳线。";
                return false;
            }

            // 6. allowSameComponentJumper=false
            if (!definition.allowSameComponentJumper)
            {
                rejectionReason = "当前元件不允许同一器件内部端子跳线。";
                return false;
            }

            // 7. ComponentKind.ContactorCoil：两个不同且真实存在的端点均允许
            if (definition.kind == ComponentKind.ContactorCoil)
            {
                // 已通过上述检查，允许任意两个不同端子（含 L1/T1、A1/A2、13/14、21/22、33/34）。
                // 连接层不判断危险旁路或短路。
                return true;
            }

            // 8. ComponentKind.Motor：只允许 U1/V1/W1/U2/V2/W2
            if (definition.kind == ComponentKind.Motor)
            {
                if (!IsStarDeltaWindingTerminal(startTerminalId) || !IsStarDeltaWindingTerminal(endTerminalId))
                {
                    rejectionReason = "星三角电机只允许 U1/V1/W1/U2/V2/W2 参与跳线，PE 不参与。";
                    return false;
                }
                return true;
            }

            // 9. 其他元件保持拒绝
            rejectionReason = "当前元件不允许同一器件内部端子跳线。";
            return false;
        }

        private static bool IsStarDeltaWindingTerminal(string terminalId)
        {
            return StarDeltaWindingTerminals.Contains(terminalId);
        }
    }
}

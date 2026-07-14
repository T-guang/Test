using ElectricalSim.Core;

namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 从当前活动 Workspace 建立学生侧网表，读取 Components 中有效元件及 WireManager 的活动导线端点。
    /// 它只采集身份、定义、端子和连接关系，不修改画布，也不得通过 FindObjectsOfType 扫描 Demo 场景历史对象。
    /// 修改活动集合、无效对象过滤或端子取数后，必须回归自由接线、撤销重做、清空画布和模板进入练习。
    /// </summary>
    public static class StudentNetlistBuilder
    {
        /// <summary>
        /// 按当前工作区的活动元件与导线快照建表。导线必须在元件和端子登记之后加入，
        /// 使 PracticeNetlist 能以实例 ID 与端子 ID 建立统一的连通键。
        /// </summary>
        public static PracticeNetlist Build(WorkspaceController workspace)
        {
            var netlist = new PracticeNetlist();
            if (workspace == null)
            {
                return netlist;
            }

            foreach (var component in workspace.Components)
            {
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                netlist.AddComponent(component.InstanceId, component.Definition.name, CleanDisplayName(component.Definition.displayName, component.Definition.name));
                foreach (var terminal in component.Terminals)
                {
                    if (terminal != null)
                    {
                        netlist.AddTerminal(component.InstanceId, terminal.TerminalId);
                    }
                }
            }

            if (workspace.WireManager != null)
            {
                foreach (var wire in workspace.WireManager.Wires)
                {
                    if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                    {
                        continue;
                    }

                    var startOwner = wire.StartTerminal.Owner;
                    var endOwner = wire.EndTerminal.Owner;
                    if (startOwner == null || endOwner == null)
                    {
                        continue;
                    }

                    netlist.AddConnection(startOwner.InstanceId, wire.StartTerminal.TerminalId, endOwner.InstanceId, wire.EndTerminal.TerminalId);
                }
            }

            return netlist;
        }

        private static string CleanDisplayName(string displayName, string fallback)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return fallback;
            }

            return displayName.Replace("\n", "").Replace("\r", "");
        }
    }
}

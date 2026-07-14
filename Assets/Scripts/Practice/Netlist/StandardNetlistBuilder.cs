using ElectricalSim.Templates;

namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 将当前练习模板 DTO 转换为标准答案侧的网表表示，保留模板中的实例身份、元件定义名和导线端点。
    /// 本类不读取学生当前画布、不执行映射或评分，也不提供 UI 反馈；连接规范化与连通分组由 PracticeNetlist 完成。
    /// 修改模板取数范围或端点写入方式后，必须回归多个练习模板的标准网表元件数和连接数。
    /// </summary>
    public static class StandardNetlistBuilder
    {
        // 空模板返回空网表，供上层统一处理；这里不尝试修复缺失的模板字段或补造标准元件。
        public static PracticeNetlist Build(CircuitTemplateDto template)
        {
            var netlist = new PracticeNetlist();
            if (template == null)
            {
                return netlist;
            }

            if (template.components != null)
            {
                foreach (var component in template.components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    netlist.AddComponent(component.instanceId, component.definitionName, component.definitionName);
                }
            }

            if (template.wires != null)
            {
                foreach (var wire in template.wires)
                {
                    if (wire == null)
                    {
                        continue;
                    }

                    netlist.AddConnection(wire.startComponentId, wire.startTerminalId, wire.endComponentId, wire.endTerminalId);
                }
            }

            return netlist;
        }
    }
}

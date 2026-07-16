namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 练习网表中的单个端子节点，由 PracticeNetlist 从模板 DTO 或活动 Workspace 数据创建。
    /// TerminalKey 由 ComponentId 与 TerminalId 组合，供连通分组和直接边关联；DefinitionName/DisplayName 仅携带元件上下文。
    /// 本类型不检查逻辑端子存在性、不执行规则、不参与 JSON 序列化；空输入会按当前 MakeKey 约定转换为空字符串片段。
    /// </summary>
    public sealed class PracticeNetlistTerminal
    {
        public string ComponentId { get; }
        public string DefinitionName { get; }
        public string DisplayName { get; }
        public string TerminalId { get; }
        public string TerminalKey { get; }

        public PracticeNetlistTerminal(string componentId, string definitionName, string displayName, string terminalId)
        {
            ComponentId = componentId;
            DefinitionName = definitionName;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? definitionName : displayName;
            TerminalId = terminalId;
            TerminalKey = MakeKey(componentId, terminalId);
        }

        public static string MakeKey(string componentId, string terminalId)
        {
            // 当前键格式是 "componentId.terminalId"；修改分隔符或空值规范前需同步复核网表、映射与连接比对读取端。
            return (componentId ?? string.Empty) + "." + (terminalId ?? string.Empty);
        }
    }
}

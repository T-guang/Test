namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 练习网表的一条直接端子连接，由 PracticeNetlist.AddConnection 创建并由连接比对器消费。
    /// 两端 ID 与预计算键只描述当前内存网表的关联，不验证端子存在或方向合法性，也不参与 JSON/模板持久化。
    /// 无向比较键由 GetUndirectedKey 按当前实现生成；调整其规范化规则前需复核缺失/多余连接反馈和练习回归。
    /// </summary>
    public sealed class PracticeNetlistConnection
    {
        public string StartComponentId { get; }
        public string StartTerminalId { get; }
        public string EndComponentId { get; }
        public string EndTerminalId { get; }
        public string StartKey { get; }
        public string EndKey { get; }

        public PracticeNetlistConnection(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            StartComponentId = startComponentId;
            StartTerminalId = startTerminalId;
            EndComponentId = endComponentId;
            EndTerminalId = endTerminalId;
            StartKey = PracticeNetlistTerminal.MakeKey(startComponentId, startTerminalId);
            EndKey = PracticeNetlistTerminal.MakeKey(endComponentId, endTerminalId);
        }

        public string GetUndirectedKey()
        {
            return string.CompareOrdinal(StartKey, EndKey) <= 0 ? StartKey + "<->" + EndKey : EndKey + "<->" + StartKey;
        }
    }
}

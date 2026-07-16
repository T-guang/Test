using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 练习网表中的元件节点数据。StandardNetlistBuilder 从模板 DTO 建立标准侧记录，StudentNetlistBuilder 从活动 Workspace 建立学生侧记录。
    /// ComponentId 是当前网表关联键，DefinitionName 用于同类元件分组，DisplayName 仅用于反馈展示；该类型只在内存中存在，不参与 JSON 序列化。
    /// </summary>
    public sealed class PracticeNetlistComponent
    {
        public string ComponentId { get; }
        public string DefinitionName { get; }
        public string DisplayName { get; }

        public PracticeNetlistComponent(string componentId, string definitionName, string displayName)
        {
            ComponentId = componentId;
            DefinitionName = definitionName;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? definitionName : displayName;
        }
    }

    /// <summary>
    /// 练习接线比对使用的内存网表容器，保存元件、端子、直接导线和 UnionFind 连通分组。
    /// Builder 负责填充，网表层 PracticeConnectionChecker 与映射求解器负责消费；本模型不验证 Definition/端子是否真实存在，
    /// 不执行电气规则，也不把测试夹具数据写回标准模板。componentId/terminalId 只在当前模板或活动画布关联范围内有效。
    /// 多条导线命中同一端子时由 nodeUnion 合并为等价组，DirectConnections 仍按每次 AddConnection 记录直接边。
    /// 本类内部的 string Dictionary 和 DefinitionName 分组使用默认字符串比较，当前键与分组比较区分大小写。
    /// </summary>
    public sealed class PracticeNetlist
    {
        private readonly Dictionary<string, PracticeNetlistComponent> components = new Dictionary<string, PracticeNetlistComponent>();
        private readonly Dictionary<string, PracticeNetlistTerminal> terminals = new Dictionary<string, PracticeNetlistTerminal>();
        private readonly List<PracticeNetlistConnection> directConnections = new List<PracticeNetlistConnection>();
        private readonly UnionFind<string> nodeUnion = new UnionFind<string>();

        public IReadOnlyDictionary<string, PracticeNetlistComponent> Components => components;
        public IReadOnlyDictionary<string, PracticeNetlistTerminal> Terminals => terminals;
        public IReadOnlyList<PracticeNetlistConnection> DirectConnections => directConnections;

        public void AddComponent(string componentId, string definitionName, string displayName)
        {
            // 空 componentId 或重复键不会修改集合；首次记录保留输入的 definitionName 与展示文本。
            if (string.IsNullOrWhiteSpace(componentId) || components.ContainsKey(componentId))
            {
                return;
            }

            components[componentId] = new PracticeNetlistComponent(componentId, definitionName, displayName);
        }

        public PracticeNetlistTerminal AddTerminal(string componentId, string terminalId)
        {
            // 端子不要求对应元件已存在；缺少元件时以空 Definition/展示信息创建节点，空 ID 则返回 null。
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(terminalId))
            {
                return null;
            }

            components.TryGetValue(componentId, out var component);
            var definitionName = component != null ? component.DefinitionName : string.Empty;
            var displayName = component != null ? component.DisplayName : definitionName;
            var terminal = new PracticeNetlistTerminal(componentId, definitionName, displayName, terminalId);
            terminals[terminal.TerminalKey] = terminal;
            nodeUnion.Add(terminal.TerminalKey);
            return terminal;
        }

        public void AddConnection(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            // 原地追加直接边并合并两端节点；任一端子无法建立时不写入连接。
            var start = AddTerminal(startComponentId, startTerminalId);
            var end = AddTerminal(endComponentId, endTerminalId);
            if (start == null || end == null)
            {
                return;
            }

            var connection = new PracticeNetlistConnection(startComponentId, startTerminalId, endComponentId, endTerminalId);
            directConnections.Add(connection);
            nodeUnion.Union(connection.StartKey, connection.EndKey);
        }

        public bool AreConnected(string firstTerminalKey, string secondTerminalKey)
        {
            return nodeUnion.AreConnected(firstTerminalKey, secondTerminalKey);
        }

        public IReadOnlyList<IReadOnlyList<string>> GetEquivalentNodeGroups()
        {
            return nodeUnion.GetGroups();
        }

        public string DescribeTerminal(string terminalKey)
        {
            if (terminals.TryGetValue(terminalKey, out var terminal))
            {
                return terminal.DisplayName + " " + terminal.TerminalId;
            }

            return terminalKey;
        }

        public IEnumerable<IGrouping<string, PracticeNetlistComponent>> GroupComponentsByDefinition()
        {
            // 仅按 DefinitionName 分组候选元件，不建立跨模板或跨会话的稳定映射。
            return components.Values.GroupBy(c => c.DefinitionName);
        }
    }
}

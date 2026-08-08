using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Rules
{
    /// <summary>
    /// 检查助手使用的基础教学规则入口。
    /// 读取当前 Workspace 中的活动元件与活动导线，生成 <see cref="CircuitCheckResult"/> 和教学问题代码；
    /// 不替代 <c>CircuitValidationService</c> 产生的生产结构化 Validation Issue，也不负责报告排版或 UI 展示。
    /// 结果会由检查工作流结合运行态分析后再进行面板误报过滤和格式化，因此不能把该过滤理解为删除生产校验问题。
    /// 修改本类后必须回归 Inspector 报告模型测试、18 张真实模板基线，以及家庭和普通工业电路的检查流程。
    /// </summary>
    public sealed class CircuitRuleChecker
    {
        private readonly WorkspaceController workspace;
        private readonly CircuitCheckResult result = new CircuitCheckResult();
        private readonly Dictionary<string, HashSet<string>> structuralGraph = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, HashSet<string>> liveGraph = new Dictionary<string, HashSet<string>>();
        private readonly List<CircuitComponent> powers = new List<CircuitComponent>();
        private readonly List<CircuitComponent> loads = new List<CircuitComponent>();
        private readonly List<CircuitComponent> switches = new List<CircuitComponent>();
        private readonly List<CircuitComponent> breakers = new List<CircuitComponent>();
        private readonly List<CircuitComponent> meters = new List<CircuitComponent>();

        public CircuitRuleChecker(WorkspaceController workspace)
        {
            this.workspace = workspace;
        }

        public CircuitCheckResult Check()
        {
            if (workspace == null)
            {
                AddIssue(CircuitIssueSeverity.Error, "WORKSPACE_MISSING", "未能读取当前画布。", "规则检测器没有拿到工作区对象。", null, null);
                return result;
            }

            // 以下顺序同时建立静态连通证据和当前触点闭合证据；部分后续规则依赖前面完成的分类与图构建，不能为了合并代码随意调换。
            ClassifyComponents();
            BuildGraphs();
            CheckBasicCompleteness();
            CheckLoadConnections();
            CheckPhaseAndNeutralPaths();
            CheckSwitchOnPhaseBranch();
            CheckBreakers();
            CheckMeters();
            CheckIndustrialControlLoops();
            CheckIsolatedComponents();
            CheckOpenDevicesAffectLoads();
            CheckBypassedDevices();
            CheckSingleSwitchTerminals();
            CheckContactorBypass();
            return result;
        }

        private void ClassifyComponents()
        {
            var components = workspace.Components;
            if (components == null)
            {
                return;
            }

            foreach (var component in components)
            {
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                var definition = component.Definition;
                if (definition.kind == ComponentKind.PowerSource || definition.name.Contains("Power"))
                {
                    powers.Add(component);
                }

                if (definition.kind == ComponentKind.Lamp || definition.kind == ComponentKind.Fan || definition.kind == ComponentKind.Motor)
                {
                    loads.Add(component);
                }

                if (definition.kind == ComponentKind.Switch || definition.kind == ComponentKind.TwoWaySwitch || definition.kind == ComponentKind.PushButton)
                {
                    switches.Add(component);
                }

                if (definition.kind == ComponentKind.Breaker && IsProtectionBreaker(component))
                {
                    breakers.Add(component);
                }

                if (definition.kind == ComponentKind.EnergyMeter)
                {
                    meters.Add(component);
                }
            }
        }

        private void BuildGraphs()
        {
            var components = workspace.Components;
            if (components != null)
            {
                foreach (var component in components)
                {
                    if (component == null || component.Terminals == null)
                    {
                        continue;
                    }

                    foreach (var terminal in component.Terminals)
                    {
                        AddNode(structuralGraph, Node(terminal));
                        AddNode(liveGraph, Node(terminal));
                    }

                    // structuralGraph 描述可接线的结构关系，liveGraph 仅保留当前闭合触点形成的可通电关系；二者不能互相替代。
                    AddInternalEdges(component, structuralGraph, true);
                    AddInternalEdges(component, liveGraph, false);
                }
            }

            var wires = workspace.WireManager != null ? workspace.WireManager.Wires : null;
            if (wires == null)
            {
                return;
            }

            foreach (var wire in wires)
            {
                if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                {
                    continue;
                }

                AddEdge(structuralGraph, Node(wire.StartTerminal), Node(wire.EndTerminal));
                AddEdge(liveGraph, Node(wire.StartTerminal), Node(wire.EndTerminal));
            }
        }

        private void AddInternalEdges(CircuitComponent component, Dictionary<string, HashSet<string>> graph, bool structural)
        {
            if (component == null || component.Definition == null)
            {
                return;
            }

            switch (component.Definition.kind)
            {
                case ComponentKind.Switch:
                case ComponentKind.PushButton:
                    if (structural || component.IsClosed)
                    {
                        AddContactEdges(graph, component);
                    }
                    break;
                case ComponentKind.TwoWaySwitch:
                    if (structural)
                    {
                        AddTerminalEdge(graph, component, "L", "L1");
                        AddTerminalEdge(graph, component, "L", "L2");
                    }
                    else
                    {
                        AddTerminalEdge(graph, component, "L", component.IsClosed ? "L1" : "L2");
                    }
                    break;
                case ComponentKind.Fuse:
                case ComponentKind.Breaker:
                    if (structural || component.IsClosed)
                    {
                        AddTerminalEdge(graph, component, "P1_IN", "P1_OUT");
                        AddTerminalEdge(graph, component, "P2_IN", "P2_OUT");
                        AddTerminalEdge(graph, component, "P3_IN", "P3_OUT");
                        AddTerminalEdge(graph, component, "L_IN", "L_OUT");
                        AddTerminalEdge(graph, component, "N_IN", "N_OUT");
                        AddTerminalEdge(graph, component, "IN", "OUT");
                        AddTerminalEdge(graph, component, "L1_IN", "L1_OUT");
                        AddTerminalEdge(graph, component, "L2_IN", "L2_OUT");
                        AddTerminalEdge(graph, component, "L3_IN", "L3_OUT");
                        AddTerminalEdge(graph, component, "L1", "T1");
                        AddTerminalEdge(graph, component, "L2", "T2");
                        AddTerminalEdge(graph, component, "L3", "T3");
                        AddTerminalEdge(graph, component, "95", "96");
                    }
                    break;
                case ComponentKind.EnergyMeter:
                    AddTerminalEdge(graph, component, "L_IN", "L_OUT");
                    AddTerminalEdge(graph, component, "N_IN", "N_OUT");
                    break;
                case ComponentKind.ContactorCoil:
                    if (structural)
                    {
                        AddTerminalEdge(graph, component, "L1", "T1");
                        AddTerminalEdge(graph, component, "L2", "T2");
                        AddTerminalEdge(graph, component, "L3", "T3");
                    }
                    break;
            }
        }

        private void CheckBasicCompleteness()
        {
            if (powers.Count == 0)
            {
                AddIssue(CircuitIssueSeverity.Error, "NO_POWER", "未检测到电源元件。", "当前画布中没有 220V 电源或交流电源。", "请先放置一个电源元件。");
            }

            if (loads.Count == 0)
            {
                AddIssue(CircuitIssueSeverity.Error, "NO_LOAD", "未检测到负载元件。", "当前画布中没有灯泡或电风扇等负载。", "请至少放置一个负载元件。");
            }

            var wires = workspace.WireManager != null ? workspace.WireManager.Wires : null;
            if (wires == null || wires.Count == 0)
            {
                AddIssue(CircuitIssueSeverity.Error, "NO_WIRE", "当前电路没有导线连接。", "元件之间还没有形成任何接线关系。", "请用导线连接电源、开关和负载端子。");
            }
        }

        private void CheckLoadConnections()
        {
            foreach (var load in loads)
            {
                if (IsThreePhaseMotor(load))
                {
                    CheckThreePhaseMotorConnections(load);
                    continue;
                }

                var lTerminal = FindPhaseTerminal(load);
                var nTerminal = FindNeutralTerminal(load);

                if (lTerminal == null || !HasAnyWire(lTerminal))
                {
                    AddIssue(CircuitIssueSeverity.Error, "LOAD_L_MISSING", LoadName(load) + "缺少 L 端连接。", "负载火线端没有连接导线。", "请检查 " + LoadName(load) + " 的 L 端是否接入火线支路。", load);
                }

                if (nTerminal == null || !HasAnyWire(nTerminal))
                {
                    AddIssue(CircuitIssueSeverity.Error, "LOAD_N_MISSING", LoadName(load) + "缺少 N 端连接。", "负载零线端没有连接导线。", "请检查 " + LoadName(load) + " 的 N 端是否接回电源 N。", load);
                }
            }
        }

        private void CheckPhaseAndNeutralPaths()
        {
            foreach (var load in loads)
            {
                if (IsThreePhaseMotor(load))
                {
                    CheckIndustrialMotorMainCircuitSegments(load);
                    CheckThreePhaseMotorPaths(load);
                    continue;
                }

                var loadL = FindPhaseTerminal(load);
                var loadN = FindNeutralTerminal(load);

                if (loadL != null && !CanReachAnyPowerTerminal(loadL, TerminalRole.Phase, structuralGraph))
                {
                    AddIssue(CircuitIssueSeverity.Error, "PHASE_PATH_MISSING", "未检测到从电源 L 到 " + LoadName(load) + " L 端的完整火线路径。", "该负载火线支路没有和电源 L 形成连通。", "请检查电源 L、空开、开关和负载 L 端之间的连接。", load);
                }

                if (loadN != null && !CanReachAnyPowerTerminal(loadN, TerminalRole.Neutral, structuralGraph))
                {
                    AddIssue(CircuitIssueSeverity.Error, "NEUTRAL_PATH_MISSING", "未检测到 " + LoadName(load) + " N 端回到电源 N 的零线回路。", "该负载零线端没有和电源 N 连通。", "请检查负载 N 端是否接回电源 N。", load);
                }
            }
        }

        private void CheckSwitchOnPhaseBranch()
        {
            var switchExternalGraph = BuildExternalSwitchGraph();
            foreach (var load in loads)
            {
                if (IsThreePhaseMotor(load))
                {
                    continue;
                }

                var loadL = FindPhaseTerminal(load);
                if (loadL == null || !CanReachAnyPowerTerminal(loadL, TerminalRole.Phase, structuralGraph))
                {
                    continue;
                }

                if (switches.Count == 0 && breakers.Count == 0)
                {
                    AddIssue(CircuitIssueSeverity.Info, "LoadConnectedDirectlyToPower", 
                        "当前负载可能直接接在电源两端。", 
                        "这样虽然可以形成回路，但缺少空气开关保护或开关控制，不符合常见家庭照明教学接线习惯。", 
                        "建议加入空气开关和单控开关，使电路具备保护和控制功能。", load);
                    continue;
                }
                else if (switches.Count == 0)
                {
                    AddIssue(CircuitIssueSeverity.Warning, "LoadLivePathWithoutSwitch", 
                        LoadName(load) + "的火线路径可能没有经过控制开关。", 
                        "当前负载 L 端可以直接获得火线，但未检测到火线路径中存在单开单控开关。这样负载可能一直通电，无法通过开关控制。", 
                        "请将开关串联到火线路径中，推荐接法为“电源 L → 空开 → 开关 L → 开关 L1 → 负载 L”。", load);
                    continue;
                }

                var switchOnPhasePath = false;
                foreach (var sw in switches)
                {
                    if (IsSwitchOnPhasePath(sw, loadL, switchExternalGraph))
                    {
                        switchOnPhasePath = true;
                        break;
                    }
                }

                if (!switchOnPhasePath)
                {
                    if (loads.Count > 1)
                    {
                        AddIssue(CircuitIssueSeverity.Warning, "ParallelLoadBypassedControl", 
                            "多个负载中，" + LoadName(load) + "可能没有经过控制开关。", 
                            "并联电路中每个负载都需要按设计接入对应控制支路。如果某个负载绕过开关，它可能无法被正确控制。", 
                            "请检查该负载 L 端是否接在对应开关输出端，而不是直接接到电源或空开输出端。", load);
                    }
                    else
                    {
                        AddIssue(CircuitIssueSeverity.Warning, "LoadLivePathWithoutSwitch", 
                            LoadName(load) + "的火线路径可能没有经过控制开关。", 
                            "当前负载 L 端可以直接获得火线，但未检测到火线路径中存在单开单控开关。这样负载可能一直通电，无法通过开关控制。", 
                            "请将开关串联到火线路径中，推荐接法为“电源 L → 空开 → 开关 L → 开关 L1 → 负载 L”。", load);
                    }
                }
            }

            foreach (var sw in switches)
            {
                var l = sw.GetTerminal("L");
                if (l != null && CanReachAnyPowerTerminal(l, TerminalRole.Neutral, structuralGraph))
                {
                    AddIssue(CircuitIssueSeverity.Warning, "SwitchOnNeutralPath", "单开单控开关疑似接在零线支路上。", "开关如果切断的是零线，灯可能会熄灭，但灯具火线端仍可能带电，维护时存在安全隐患。", "请将开关改接到火线路径中，让电源 L 经过开关后再进入灯泡 L 端。", sw);
                }
            }
        }

        private Dictionary<string, HashSet<string>> BuildExternalSwitchGraph()
        {
            var graph = new Dictionary<string, HashSet<string>>();
            var components = workspace.Components;
            if (components != null)
            {
                foreach (var component in components)
                {
                    if (component == null || component.Terminals == null)
                    {
                        continue;
                    }

                    foreach (var terminal in component.Terminals)
                    {
                        AddNode(graph, Node(terminal));
                    }

                    if (component.Definition != null)
                    {
                        if (component.Definition.kind == ComponentKind.Switch || 
                            component.Definition.kind == ComponentKind.TwoWaySwitch || 
                            component.Definition.kind == ComponentKind.PushButton)
                        {
                            // Do not add internal edges for switches
                        }
                        else
                        {
                            AddInternalEdges(component, graph, true);
                        }
                    }
                }
            }

            var wires = workspace.WireManager != null ? workspace.WireManager.Wires : null;
            if (wires != null)
            {
                foreach (var wire in wires)
                {
                    if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                    {
                        continue;
                    }

                    AddEdge(graph, Node(wire.StartTerminal), Node(wire.EndTerminal));
                }
            }

            return graph;
        }

        private void CheckSingleSwitchTerminals()
        {
            var switchExternalGraph = BuildExternalSwitchGraph();

            foreach (var sw in switches)
            {
                if (sw.Definition == null || sw.GetTerminal("L") == null || sw.GetTerminal("L1") == null || sw.GetTerminal("L2") != null)
                {
                    continue;
                }

                var l = sw.GetTerminal("L");
                var l1 = sw.GetTerminal("L1");

                bool hasLWire = HasAnyWire(l);
                bool hasL1Wire = HasAnyWire(l1);

                if (!hasLWire || !hasL1Wire)
                {
                    AddIssue(CircuitIssueSeverity.Warning, "SingleSwitchTerminalIncomplete", 
                        "单开单控开关进线或出线不完整。", 
                        "单控开关需要 L 端进线，L1 端出线。当前 L 或 L1 缺少连接。", 
                        "请检查开关接线，确保有进有出。", sw);
                    continue;
                }

                bool l1ReachesLoadL = false;
                bool lReachesLoadL = false;
                foreach (var load in loads)
                {
                    var loadL = FindPhaseTerminal(load);
                    if (loadL != null)
                    {
                        if (AreConnected(l1, loadL, switchExternalGraph)) l1ReachesLoadL = true;
                        if (AreConnected(l, loadL, switchExternalGraph)) lReachesLoadL = true;
                    }
                }

                if (!l1ReachesLoadL)
                {
                    AddIssue(CircuitIssueSeverity.Warning, "SingleSwitchTerminalMiswired", 
                        "单开单控开关的 L1 输出端未检测到受控负载。", 
                        "单控开关应通过 L 端接入火线，L1 端输出到负载。当前 L1 未连接到任何负载 L 端。", 
                        "请检查接线顺序是否为“电源 L → 空开 → 开关 L → 开关 L1 → 负载 L”。", sw);
                }
                else if (lReachesLoadL)
                {
                    AddIssue(CircuitIssueSeverity.Warning, "SingleSwitchTerminalMiswired", 
                        "负载火线可能接在单控开关的进线侧（L端）。", 
                        "开关未按 L → L1 顺序控制负载。如果负载直接接在开关 L 侧，开关断开时无法切断负载电源。", 
                        "请检查接线顺序是否为“电源 L → 空开 → 开关 L → 开关 L1 → 负载 L”。", sw);
                }
            }
        }

        private void CheckBypassedDevices()
        {
            var switchExternalGraph = BuildExternalSwitchGraph();
            foreach (var load in loads)
            {
                if (IsThreePhaseMotor(load))
                {
                    continue;
                }

                var loadL = FindPhaseTerminal(load);
                var loadN = FindNeutralTerminal(load);
                if (loadL == null || loadN == null)
                {
                    continue;
                }

                var liveOk = CanReachAnyPowerTerminal(loadL, TerminalRole.Phase, liveGraph) &&
                             CanReachAnyPowerTerminal(loadN, TerminalRole.Neutral, liveGraph);

                if (liveOk)
                {
                    foreach (var sw in switches)
                    {
                        if (!sw.IsClosed && IsSwitchOnPhasePath(sw, loadL, switchExternalGraph))
                        {
                            AddIssue(CircuitIssueSeverity.Warning, "SwitchBypassed", 
                                LoadName(load) + "在" + sw.Definition.displayName + " OFF 时仍然处于通电状态。", 
                                "这通常说明负载火线没有真正经过该开关，或者存在绕过开关的旁路。开关虽然画在电路中，但没有实际控制负载。", 
                                "请检查火线是否按照“电源 L → 空开 → 开关 L → 开关 L1 → 负载 L”的顺序连接。", sw);
                        }
                    }

                    foreach (var br in breakers)
                    {
                        if (!br.IsClosed && IsBreakerOnPhasePath(br, loadL))
                        {
                            string suggestion = IsThreePoleBreaker(br) 
                                ? "请检查三相电源是否先进入空气开关，再连接后级电路。"
                                : "请检查电源 L/N 是否先进入空气开关输入端，再由空气开关输出端连接到后级开关和负载。";

                            AddIssue(CircuitIssueSeverity.Error, "BreakerBypassed", 
                                br.Definition.displayName + "处于 OFF，但后级" + LoadName(load) + "仍然通电。", 
                                "这说明负载可能绕过了空气开关，直接从电源或其它路径获得火线，空开没有起到保护和断电作用。", 
                                suggestion, br);
                        }
                    }
                }
            }
        }

        private bool IsSwitchOnPhasePath(
            CircuitComponent sw,
            TerminalView loadL,
            Dictionary<string, HashSet<string>> switchExternalGraph)
        {
            var input = sw.GetTerminal("L");
            var output = sw.GetTerminal("L1") ?? sw.GetTerminal("L2");
            return input != null && output != null &&
                   CanReachAnyPowerTerminal(input, TerminalRole.Phase, switchExternalGraph) &&
                   AreConnected(output, loadL, switchExternalGraph);
        }

        private bool IsBreakerOnPhasePath(CircuitComponent breaker, TerminalView loadL)
        {
            var p1In = breaker.GetTerminal("P1_IN");
            var p2In = breaker.GetTerminal("P2_IN");
            var p3In = breaker.GetTerminal("P3_IN");
            var p1Out = breaker.GetTerminal("P1_OUT");
            var p2Out = breaker.GetTerminal("P2_OUT");
            var p3Out = breaker.GetTerminal("P3_OUT");

            bool p1Path = p1In != null && p1Out != null && 
                          CanReachAnyPowerPhase(p1In, structuralGraph) && 
                          AreConnected(p1Out, loadL, structuralGraph);
            bool p2Path = p2In != null && p2Out != null && 
                          CanReachAnyPowerPhase(p2In, structuralGraph) && 
                          AreConnected(p2Out, loadL, structuralGraph);
            bool p3Path = p3In != null && p3Out != null && 
                          CanReachAnyPowerPhase(p3In, structuralGraph) && 
                          AreConnected(p3Out, loadL, structuralGraph);

            return p1Path || p2Path || p3Path;
        }

        private void CheckBreakers()
        {
            if (breakers.Count == 0)
            {
                AddIssue(CircuitIssueSeverity.Info, "NO_BREAKER", "\u5f53\u524d\u7535\u8def\u672a\u68c0\u6d4b\u5230\u7a7a\u6c14\u5f00\u5173\u3002", "\u57fa\u7840\u6f14\u793a\u7535\u8def\u53ef\u4ee5\u8fd0\u884c\uff0c\u4f46\u5b9e\u9645\u7535\u8def\u901a\u5e38\u9700\u8981\u4fdd\u62a4\u5f00\u5173\u3002", "\u540e\u7eed\u53ef\u52a0\u5165\u7a7a\u6c14\u5f00\u5173\uff0c\u8ba9\u7535\u6e90\u5148\u8fdb\u5165\u4fdd\u62a4\u5f00\u5173\u8f93\u5165\u7aef\u3002");
                return;
            }

            foreach (var breaker in breakers)
            {
                if (IsThreePoleBreaker(breaker))
                {
                    CheckThreePoleBreaker(breaker);
                }
                else
                {
                    CheckSinglePhaseBreaker(breaker);
                }
            }
        }

        private void CheckSinglePhaseBreaker(CircuitComponent breaker)
        {
            var p1In = breaker.GetTerminal("P1_IN");
            var p2In = breaker.GetTerminal("P2_IN");
            var p1Out = breaker.GetTerminal("P1_OUT");
            var p2Out = breaker.GetTerminal("P2_OUT");
            var incomplete = p1In == null || p2In == null || p1Out == null || p2Out == null ||
                !CanReachAnyPowerTerminal(p1In, TerminalRole.Phase, structuralGraph) ||
                !CanReachAnyPowerTerminal(p2In, TerminalRole.Neutral, structuralGraph) ||
                !HasAnyWire(p1Out) ||
                !HasAnyWire(p2Out);

            if (incomplete)
            {
                AddIssue(CircuitIssueSeverity.Warning, "BREAKER_INCOMPLETE", "\u7a7a\u6c14\u5f00\u5173\u8fdb\u7ebf\u6216\u51fa\u7ebf\u4e0d\u5b8c\u6574\u3002", breaker.Definition.displayName + " \u6ca1\u6709\u5f62\u6210\u5b8c\u6574\u7684 L/N \u8f93\u5165\u8f93\u51fa\u7ed3\u6784\u3002", "\u8bf7\u786e\u8ba4\u7535\u6e90 L/N \u5148\u8fdb\u5165\u7a7a\u5f00\u8f93\u5165\u7aef\uff0c\u518d\u7531\u7a7a\u5f00\u8f93\u51fa\u7aef\u8fde\u63a5\u540e\u7eed\u8d1f\u8f7d\u3002", breaker);
            }
        }

        private void CheckThreePoleBreaker(CircuitComponent breaker)
        {
            var ids = new[] { "P1", "P2", "P3" };
            foreach (var id in ids)
            {
                var input = breaker.GetTerminal(id + "_IN");
                var output = breaker.GetTerminal(id + "_OUT");
                var incomplete = input == null || output == null ||
                    !CanReachAnyPowerPhase(input, structuralGraph) ||
                    !HasAnyWire(output);
                if (incomplete)
                {
                    AddIssue(CircuitIssueSeverity.Warning, "BREAKER_3P_INCOMPLETE", "3P\u7a7a\u6c14\u5f00\u5173\u8fdb\u7ebf\u6216\u51fa\u7ebf\u4e0d\u5b8c\u6574\u3002", breaker.Definition.displayName + " \u7684 " + id + " \u76f8\u6ca1\u6709\u5f62\u6210\u5b8c\u6574\u8f93\u5165\u8f93\u51fa\u7ed3\u6784\u3002", "\u8bf7\u786e\u8ba4\u4e09\u76f8\u7535\u6e90 L1/L2/L3 \u5148\u8fdb\u5165\u7a7a\u5f00\u8f93\u5165\u7aef\uff0c\u518d\u7531\u7a7a\u5f00\u8f93\u51fa\u7aef\u8fde\u63a5\u540e\u7ea7\u5143\u4ef6\u3002", breaker);
                }
            }
        }

        private void CheckMeters()
        {
            foreach (var meter in meters)
            {
                var missing = new List<string>();
                if (!HasAnyWire(meter.GetTerminal("L_IN"))) missing.Add("L_IN");
                if (!HasAnyWire(meter.GetTerminal("L_OUT"))) missing.Add("L_OUT");
                if (!HasAnyWire(meter.GetTerminal("N_IN"))) missing.Add("N_IN");
                if (!HasAnyWire(meter.GetTerminal("N_OUT"))) missing.Add("N_OUT");

                if (missing.Count > 0)
                {
                    AddIssue(CircuitIssueSeverity.Warning, "METER_INCOMPLETE", "单相电能表进线或出线不完整。", "缺少连接端子：" + string.Join("、", missing.ToArray()) + "。", "请检查 L_IN、L_OUT、N_IN、N_OUT 是否按进出线关系连接。", meter);
                }
            }
        }

        private void CheckIndustrialControlLoops()
        {
            var components = workspace.Components;
            if (components == null)
            {
                return;
            }

            foreach (var component in components)
            {
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                if (IsIndustrialContactor(component))
                {
                    CheckContactorControlLoop(component);
                }

                if (IsThermalRelay(component))
                {
                    CheckThermalRelayControlLoop(component);
                }
            }
        }

        private void CheckContactorControlLoop(CircuitComponent contactor)
        {
            var coilA1 = contactor.GetTerminal("A1");
            var coilA2 = contactor.GetTerminal("A2");
            if (coilA1 == null || coilA2 == null)
            {
                AddIssue(CircuitIssueSeverity.Warning, "INDUSTRIAL_CONTACTOR_COIL_TERMINAL_MISSING",
                    ComponentName(contactor) + "\u7f3a\u5c11 A1/A2 \u7ebf\u5708\u7aef\u5b50\u3002",
                    "\u5f53\u524d\u63a5\u89e6\u5668\u65e0\u6cd5\u8fdb\u884c\u63a7\u5236\u56de\u8def\u68c0\u67e5\u3002",
                    "\u8bf7\u786e\u8ba4\u63a5\u89e6\u5668\u6a21\u578b\u5305\u542b A1/A2 \u7ebf\u5708\u7aef\u5b50\u3002", contactor);
                return;
            }

            if (!HasAnyWire(coilA1) || !HasAnyWire(coilA2))
            {
                AddIssue(CircuitIssueSeverity.Warning, "INDUSTRIAL_CONTACTOR_COIL_OPEN",
                    "\u63a5\u89e6\u5668\u7ebf\u5708\u672a\u95ed\u5408\u6216\u7ebf\u8def\u4e0d\u5b8c\u6574\u3002",
                    ComponentName(contactor) + " \u7684 A1/A2 \u63a7\u5236\u56de\u8def\u6ca1\u6709\u5b8c\u6574\u63a5\u7ebf\u3002",
                    "\u8bf7\u68c0\u67e5 Start/Stop \u6309\u94ae\u53ca\u63a5\u89e6\u5668 A1/A2 \u63a5\u7ebf\uff1b\u5982\u6709\u70ed\u7ee7\u7535\u5668\uff0c\u8bf7\u786e\u8ba4 95/96 \u4e32\u5165\u63a7\u5236\u56de\u8def\u3002", contactor);
                return;
            }

            if (!IsControlCoilRoutable(coilA1, coilA2, structuralGraph))
            {
                AddIssue(CircuitIssueSeverity.Warning, "INDUSTRIAL_CONTACTOR_COIL_OPEN",
                    "\u63a5\u89e6\u5668\u7ebf\u5708\u672a\u95ed\u5408\u6216\u7ebf\u8def\u4e0d\u5b8c\u6574\u3002",
                    ComponentName(contactor) + " \u7684 A1/A2 \u6ca1\u6709\u5f62\u6210\u5b8c\u6574\u63a7\u5236\u63a5\u7ebf\u8def\u5f84\u3002",
                    "\u8bf7\u68c0\u67e5\u542f\u52a8/\u505c\u6b62\u6309\u94ae\u548c A1/A2 \u662f\u5426\u6309\u6807\u51c6\u56de\u8def\u63a5\u5165\u63a7\u5236\u7535\u6e90\u4e24\u7aef\u3002", contactor);
            }
        }

        private void CheckThermalRelayControlLoop(CircuitComponent relay)
        {
            var ncIn = relay.GetTerminal("95");
            var ncOut = relay.GetTerminal("96");
            if (ncIn == null || ncOut == null)
            {
                return;
            }

            if (!HasAnyWire(ncIn) || !HasAnyWire(ncOut))
            {
                AddIssue(CircuitIssueSeverity.Warning, "INDUSTRIAL_THERMAL_RELAY_CONTROL_OPEN",
                    "\u7ee7\u7535\u5668\u7ebf\u5708\u672a\u95ed\u5408\u6216\u7ebf\u8def\u4e0d\u5b8c\u6574\u3002",
                    ComponentName(relay) + " \u7684 95/96 \u63a7\u5236\u89e6\u70b9\u6ca1\u6709\u5b8c\u6574\u63a5\u5165\u63a7\u5236\u56de\u8def\u3002",
                    "\u8bf7\u68c0\u67e5\u70ed\u7ee7\u7535\u5668 95/96 \u662f\u5426\u4e32\u5165\u505c\u6b62\u6309\u94ae\u4e0e\u63a5\u89e6\u5668\u7ebf\u5708 A1/A2 \u7684\u63a7\u5236\u7ebf\u8def\u3002", relay);
            }
        }

        private bool IsControlCoilRoutable(TerminalView first, TerminalView second, Dictionary<string, HashSet<string>> graph)
        {
            if (first == null || second == null || AreConnected(first, second, graph))
            {
                return false;
            }

            var firstPhases = GetReachablePowerPhaseKeys(first, graph);
            var secondPhases = GetReachablePowerPhaseKeys(second, graph);
            foreach (var firstPhase in firstPhases)
            {
                foreach (var secondPhase in secondPhases)
                {
                    if (firstPhase != secondPhase)
                    {
                        return true;
                    }
                }
            }

            return firstPhases.Count > 0 && CanReachPowerNeutral(second, graph) ||
                secondPhases.Count > 0 && CanReachPowerNeutral(first, graph);
        }

        private HashSet<string> GetReachablePowerPhaseKeys(TerminalView terminal, Dictionary<string, HashSet<string>> graph)
        {
            var phases = new HashSet<string>();
            if (terminal == null || graph == null)
            {
                return phases;
            }

            foreach (var power in powers)
            {
                if (power == null || power.Terminals == null)
                {
                    continue;
                }

                foreach (var powerTerminal in power.Terminals)
                {
                    if (powerTerminal != null && powerTerminal.Role == TerminalRole.Phase && AreConnected(terminal, powerTerminal, graph))
                    {
                        phases.Add(Node(powerTerminal));
                    }
                }
            }

            return phases;
        }

        private bool CanReachPowerNeutral(TerminalView terminal, Dictionary<string, HashSet<string>> graph)
        {
            return CanReachAnyPowerTerminal(terminal, TerminalRole.Neutral, graph);
        }

        private static bool IsIndustrialContactor(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                component.GetTerminal("A1") != null &&
                component.GetTerminal("A2") != null &&
                component.GetTerminal("L1") != null &&
                component.GetTerminal("T1") != null;
        }

        private static bool IsThermalRelay(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                component.GetTerminal("95") != null &&
                component.GetTerminal("96") != null &&
                component.GetTerminal("L1") != null &&
                component.GetTerminal("T1") != null;
        }

        private void CheckIsolatedComponents()
        {
            var components = workspace.Components;
            if (components == null)
            {
                return;
            }

            foreach (var component in components)
            {
                if (component == null || component.Terminals == null)
                {
                    continue;
                }

                var hasWire = false;
                foreach (var terminal in component.Terminals)
                {
                    if (HasAnyWire(terminal))
                    {
                        hasWire = true;
                        break;
                    }
                }

                if (!hasWire)
                {
                    AddIssue(CircuitIssueSeverity.Info, "ISOLATED_COMPONENT", "元件 " + ComponentName(component) + " 当前没有任何导线连接。", "该元件暂时没有参与当前电路。", "如果它是多余元件，可以删除；如果需要参与电路，请接入对应端子。", component);
                }
            }
        }

        private void CheckOpenDevicesAffectLoads()
        {
            foreach (var load in loads)
            {
                if (IsThreePhaseMotor(load))
                {
                    continue;
                }

                var loadL = FindPhaseTerminal(load);
                var loadN = FindNeutralTerminal(load);
                if (loadL == null || loadN == null)
                {
                    continue;
                }

                var structuralOk = CanReachAnyPowerTerminal(loadL, TerminalRole.Phase, structuralGraph) &&
                    CanReachAnyPowerTerminal(loadN, TerminalRole.Neutral, structuralGraph);
                var liveOk = CanReachAnyPowerTerminal(loadL, TerminalRole.Phase, liveGraph) &&
                    CanReachAnyPowerTerminal(loadN, TerminalRole.Neutral, liveGraph);
                if (structuralOk && !liveOk)
                {
                    var offDevices = new System.Collections.Generic.List<string>();
                    foreach (var b in breakers) { if (!b.IsClosed) offDevices.Add(ComponentName(b)); }
                    foreach (var s in switches) { if (!s.IsClosed) offDevices.Add(ComponentName(s)); }

                    if (offDevices.Count == 1)
                    {
                        AddIssue(CircuitIssueSeverity.Info, "OPEN_DEVICE", 
                            LoadName(load) + "当前可能因" + offDevices[0] + "处于 OFF 状态而未通电。", 
                            "物理结构已接线，但实时计算状态下回路断开", 
                            "建议：请先将" + offDevices[0] + "切换为 ON，再运行仿真观察负载状态。", load);
                    }
                    else if (offDevices.Count > 1)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine(LoadName(load) + "当前可能因以下控制元件处于 OFF 状态而未通电：");
                        for (int i = 0; i < offDevices.Count; i++)
                        {
                            sb.AppendLine((i + 1) + ". " + offDevices[i]);
                        }
                        AddIssue(CircuitIssueSeverity.Info, "OPEN_DEVICE", 
                            sb.ToString().TrimEnd(), 
                            "物理结构已接线，但实时计算状态下回路断开", 
                            "建议：请依次确认这些控制元件是否需要切换为 ON。", load);
                    }
                    else
                    {
                        AddIssue(CircuitIssueSeverity.Info, "OPEN_DEVICE", 
                            LoadName(load) + "当前可能因开关或空开断开而未通电。", 
                            "物理结构已接线，但实时计算状态下回路断开", 
                            "请将双控或单控开关切换为 ON/OFF 进行观察负载状态。", load);
                    }
                }
            }
        }



        private static bool IsProtectionBreaker(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var name = component.Definition.name ?? string.Empty;
            return name.Contains("Breaker") || component.GetTerminal("P1_IN") != null || component.GetTerminal("P2_IN") != null || component.GetTerminal("P3_IN") != null;
        }

        private static bool IsThreePoleBreaker(CircuitComponent component)
        {
            return component != null && component.GetTerminal("P1_IN") != null && component.GetTerminal("P2_IN") != null && component.GetTerminal("P3_IN") != null;
        }

        private bool CanReachAnyPowerTerminal(TerminalView from, TerminalRole powerRole, Dictionary<string, HashSet<string>> graph)
        {
            if (from == null)
            {
                return false;
            }

            foreach (var power in powers)
            {
                TerminalView target = powerRole == TerminalRole.Neutral ? FindNeutralTerminal(power) : FindPhaseTerminal(power);
                if (target != null && AreConnected(from, target, graph))
                {
                    return true;
                }
            }

            return false;
        }

        private void CheckIndustrialMotorMainCircuitSegments(CircuitComponent motor)
        {
            var contactor = FindMainContactorForMotor(motor);
            var fuse = FindComponentWithTerminals("L1_OUT", "L2_OUT", "L3_OUT");
            if (contactor == null || fuse == null)
            {
                return;
            }

            CheckMainCircuitSegment(fuse, "L1_OUT", contactor, "L1", "L1");
            CheckMainCircuitSegment(fuse, "L2_OUT", contactor, "L2", "L2");
            CheckMainCircuitSegment(fuse, "L3_OUT", contactor, "L3", "L3");
        }

        private void CheckMainCircuitSegment(CircuitComponent from, string fromTerminalId, CircuitComponent to, string toTerminalId, string phaseLabel)
        {
            var fromTerminal = from != null ? from.GetTerminal(fromTerminalId) : null;
            var toTerminal = to != null ? to.GetTerminal(toTerminalId) : null;
            if (fromTerminal == null || toTerminal == null || AreConnected(fromTerminal, toTerminal, structuralGraph))
            {
                return;
            }

            AddIssue(CircuitIssueSeverity.Error, "INDUSTRIAL_FUSE_CONTACTOR_SEGMENT_MISSING",
                "\u7194\u65ad\u5668\u5230\u4ea4\u6d41\u63a5\u89e6\u5668\u4e4b\u95f4\u7684 " + phaseLabel + " \u76f8\u4e3b\u56de\u8def\u7f3a\u5931\u3002",
                ComponentName(from) + " " + fromTerminalId + " \u4e0e " + ComponentName(to) + " " + toTerminalId + " \u672a\u8fde\u901a\uff0c\u8be5\u76f8\u4e3b\u56de\u8def\u5728\u7194\u65ad\u5668\u548c\u63a5\u89e6\u5668\u4e4b\u95f4\u65ad\u5f00\u3002",
                "\u8bf7\u5148\u8865\u9f50\u7194\u65ad\u5668\u8f93\u51fa\u7aef\u5230\u4ea4\u6d41\u63a5\u89e6\u5668\u8f93\u5165\u7aef\u7684\u5bfc\u7ebf\uff0c\u518d\u68c0\u67e5\u63a5\u89e6\u5668\u5230\u7535\u673a U/V/W \u7aef\u7684\u8fde\u63a5\u3002", to);
        }

        private CircuitComponent FindMainContactorForMotor(CircuitComponent motor)
        {
            var motorU = motor.GetTerminal("U");
            var motorV = motor.GetTerminal("V");
            var motorW = motor.GetTerminal("W");
            CircuitComponent fallback = null;
            foreach (var component in workspace.Components)
            {
                if (!IsIndustrialContactor(component))
                {
                    continue;
                }

                fallback = fallback ?? component;
                if (AreConnected(component.GetTerminal("T1"), motorU, structuralGraph) ||
                    AreConnected(component.GetTerminal("T2"), motorV, structuralGraph) ||
                    AreConnected(component.GetTerminal("T3"), motorW, structuralGraph))
                {
                    return component;
                }
            }

            return fallback;
        }

        private CircuitComponent FindComponentWithTerminals(params string[] terminalIds)
        {
            foreach (var component in workspace.Components)
            {
                if (component == null)
                {
                    continue;
                }

                var hasAllTerminals = true;
                foreach (var terminalId in terminalIds)
                {
                    if (component.GetTerminal(terminalId) == null)
                    {
                        hasAllTerminals = false;
                        break;
                    }
                }

                if (hasAllTerminals)
                {
                    return component;
                }
            }

            return null;
        }

        private void CheckThreePhaseMotorConnections(CircuitComponent motor)
        {
            CheckMotorTerminalWire(motor, "U");
            CheckMotorTerminalWire(motor, "V");
            CheckMotorTerminalWire(motor, "W");
        }

        private void CheckThreePhaseMotorPaths(CircuitComponent motor)
        {
            CheckMotorPhasePath(motor, "U");
            CheckMotorPhasePath(motor, "V");
            CheckMotorPhasePath(motor, "W");
        }

        private void CheckMotorTerminalWire(CircuitComponent motor, string terminalId)
        {
            var terminal = motor.GetTerminal(terminalId);
            if (terminal == null || !HasAnyWire(terminal))
            {
                AddIssue(CircuitIssueSeverity.Error, "MOTOR_PHASE_MISSING",
                    LoadName(motor) + "缺少 " + terminalId + " 相连接。",
                    "三相电机需要 U/V/W 三个相线端子都接入三相主回路。",
                    "请检查电机 " + terminalId + " 端是否已经接到接触器或三相电源输出端。", motor);
            }
        }

        private void CheckMotorPhasePath(CircuitComponent motor, string terminalId)
        {
            var terminal = motor.GetTerminal(terminalId);
            if (terminal != null && !CanReachAnyPowerPhase(terminal, structuralGraph))
            {
                AddIssue(CircuitIssueSeverity.Error, "MOTOR_PHASE_PATH_MISSING",
                    "未检测到从三相电源到 " + LoadName(motor) + " " + terminalId + " 端的主回路路径。",
                    "三相电机的 U/V/W 端需要分别通过空开、熔断器、接触器等主回路元件接到三相电源。",
                    "请沿该相导线检查三相电源、空开、熔断器、接触器和电机端子之间是否连通。", motor);
            }
        }

        private bool CanReachAnyPowerPhase(TerminalView from, Dictionary<string, HashSet<string>> graph)
        {
            if (from == null)
            {
                return false;
            }

            foreach (var power in powers)
            {
                if (power == null || power.Terminals == null)
                {
                    continue;
                }

                foreach (var terminal in power.Terminals)
                {
                    if (terminal != null && terminal.Role == TerminalRole.Phase && AreConnected(from, terminal, graph))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool AreConnected(TerminalView a, TerminalView b, Dictionary<string, HashSet<string>> graph)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return AreConnected(Node(a), Node(b), graph);
        }

        private static bool AreConnected(string start, string end, Dictionary<string, HashSet<string>> graph)
        {
            if (string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end) || graph == null || !graph.ContainsKey(start) || !graph.ContainsKey(end))
            {
                return false;
            }

            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    TopologyTraversalLimits.LogTraversalBudgetExceeded("CircuitRuleChecker.AreConnected");
                    return false;
                }

                var current = queue.Dequeue();
                if (current == end)
                {
                    return true;
                }

                foreach (var next in graph[current])
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        TopologyTraversalLimits.LogTraversalBudgetExceeded("CircuitRuleChecker.AreConnected.edges");
                        return false;
                    }

                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return false;
        }

        private bool HasAnyWire(TerminalView terminal)
        {
            if (terminal == null || workspace.WireManager == null || workspace.WireManager.Wires == null)
            {
                return false;
            }

            foreach (var wire in workspace.WireManager.Wires)
            {
                if (wire != null && (wire.StartTerminal == terminal || wire.EndTerminal == terminal))
                {
                    return true;
                }
            }

            return false;
        }

        private static TerminalView FindPhaseTerminal(CircuitComponent component)
        {
            return FindTerminal(component, "L") ?? FindTerminal(component, "L_IN") ?? FindByRole(component, TerminalRole.Phase) ?? FindByRole(component, TerminalRole.Input);
        }

        private static TerminalView FindNeutralTerminal(CircuitComponent component)
        {
            return FindTerminal(component, "N") ?? FindTerminal(component, "N_IN") ?? FindByRole(component, TerminalRole.Neutral);
        }

        private static bool IsThreePhaseMotor(CircuitComponent component)
        {
            return component != null && component.Definition != null && component.Definition.kind == ComponentKind.Motor &&
                   component.GetTerminal("U") != null && component.GetTerminal("V") != null && component.GetTerminal("W") != null;
        }

        private static TerminalView FindTerminal(CircuitComponent component, string terminalId)
        {
            return component != null ? component.GetTerminal(terminalId) : null;
        }

        private static TerminalView FindByRole(CircuitComponent component, TerminalRole role)
        {
            if (component == null || component.Terminals == null)
            {
                return null;
            }

            foreach (var terminal in component.Terminals)
            {
                if (terminal != null && terminal.Role == role)
                {
                    return terminal;
                }
            }

            return null;
        }

        private static void AddContactEdges(Dictionary<string, HashSet<string>> graph, CircuitComponent component)
        {
            AddTerminalEdge(graph, component, "L", "L1");
            AddTerminalEdge(graph, component, "11", "12");
            AddTerminalEdge(graph, component, "13", "14");
            AddTerminalEdge(graph, component, "21", "22");
            AddTerminalEdge(graph, component, "23", "24");
        }

        private static void AddTerminalEdge(Dictionary<string, HashSet<string>> graph, CircuitComponent component, string a, string b)
        {
            var first = component.GetTerminal(a);
            var second = component.GetTerminal(b);
            if (first != null && second != null)
            {
                AddEdge(graph, Node(first), Node(second));
            }
        }

        private static void AddNode(Dictionary<string, HashSet<string>> graph, string node)
        {
            if (!string.IsNullOrEmpty(node) && !graph.ContainsKey(node))
            {
                graph[node] = new HashSet<string>();
            }
        }

        private static void AddEdge(Dictionary<string, HashSet<string>> graph, string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return;
            }

            AddNode(graph, a);
            AddNode(graph, b);
            graph[a].Add(b);
            graph[b].Add(a);
        }

        private static string Node(TerminalView terminal)
        {
            if (terminal == null || terminal.Owner == null)
            {
                return string.Empty;
            }

            return terminal.Owner.InstanceId + "." + terminal.TerminalId;
        }

        /// <summary>
        /// KM-2B5 Gate A: 接触器旁路安全规则。
        /// 直接遍历 workspace.WireManager.Wires 中的真实外部导线，检测同一接触器
        /// 受控触点对被直接短接的危险接线。不从 structuralGraph 或 liveGraph 推断，
        /// 因为 graph 中含有正常接触器内部触点边。同一根 Wire 只产生一次 issue。
        /// Main 触点短接 → CONTACTOR_MAIN_CONTACT_BYPASS (Error)；
        /// NO/NC 辅助触点短接 → CONTACTOR_AUX_CONTACT_BYPASS (Warning)。
        /// </summary>
        private void CheckContactorBypass()
        {
            var wires = workspace.WireManager != null ? workspace.WireManager.Wires : null;
            if (wires == null)
            {
                return;
            }

            var bypassIssues = DetectContactorBypassIssues(wires);
            for (var i = 0; i < bypassIssues.Count; i++)
            {
                result.Add(bypassIssues[i]);
            }
        }

        /// <summary>
        /// 基于 WireManager 真实外部 Wire 检测接触器受控触点直接旁路。
        /// 遍历每根 Wire，判断两端是否属于同一 ContactorCoil 元件且构成完整受控触点对。
        /// 不散写 "L1"/"T1"/"13"/"14" 等端子 ID，统一通过 ContactorTerminalSchema.TryFindPair 查询。
        /// </summary>
        private static List<CircuitIssue> DetectContactorBypassIssues(IReadOnlyList<WireView> wires)
        {
            var issues = new List<CircuitIssue>();
            if (wires == null)
            {
                return issues;
            }

            for (var i = 0; i < wires.Count; i++)
            {
                var wire = wires[i];
                if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                {
                    continue;
                }

                var start = wire.StartTerminal;
                var end = wire.EndTerminal;
                var owner = start.Owner;

                // 两端必须属于同一元件
                if (owner == null || end.Owner != owner)
                {
                    continue;
                }

                // 元件必须是接触器线圈
                if (owner.Definition == null || owner.Definition.kind != ComponentKind.ContactorCoil)
                {
                    continue;
                }

                // 两端必须构成完整的受控触点对
                if (!ContactorTerminalSchema.TryFindPair(start.TerminalId, end.TerminalId, out var pair))
                {
                    continue;
                }

                var isMain = pair.ContactType == ContactorContactType.Main;
                var code = isMain
                    ? "CONTACTOR_MAIN_CONTACT_BYPASS"
                    : "CONTACTOR_AUX_CONTACT_BYPASS";
                var severity = isMain
                    ? CircuitIssueSeverity.Error
                    : CircuitIssueSeverity.Warning;

                issues.Add(new CircuitIssue
                {
                    code = code,
                    severity = severity,
                    title = ComponentName(owner) + " " + pair.StartTerminalId + "/" + pair.EndTerminalId + " 触点被直接短接",
                    message = "外部导线直接连接了接触器的受控触点端子，可能导致电路失控或保护失效。",
                    suggestion = "请移除该导线或重新设计控制逻辑。",
                    componentId = owner.InstanceId,
                    componentName = ComponentName(owner)
                });
            }

            return issues;
        }

        private void AddIssue(CircuitIssueSeverity severity, string code, string title, string message, string suggestion, CircuitComponent component = null)
        {
            result.Add(new CircuitIssue
            {
                code = code,
                severity = severity,
                title = title,
                message = message,
                suggestion = suggestion,
                componentId = component != null ? component.InstanceId : string.Empty,
                componentName = component != null ? ComponentName(component) : string.Empty
            });
        }

        private static string ComponentName(CircuitComponent component)
        {
            return component != null && component.Definition != null ? component.Definition.displayName : "未知元件";
        }

        private static string LoadName(CircuitComponent component)
        {
            return ComponentName(component);
        }
    }
}





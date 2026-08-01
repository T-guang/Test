using System;
using System.Collections.Generic;
using System.Text;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 根据当前工作区的元件和导线推导可读的电气与拓扑状态。
    /// 不修改场景、不推进仿真时间，也不决定教学报告的呈现；调用方必须只传入活动工作区图，
    /// 不得把 Demo.unity 中的全部对象作为输入。主要调用方为检查、校验和回归基线路径。
    /// </summary>
    public sealed class CircuitStateAnalyzer
    {
        public const string VoltageNone = "None";
        public const string VoltageL = "L";
        public const string VoltageN = "N";
        public const string VoltageL1 = "L1";
        public const string VoltageL2 = "L2";
        public const string VoltageL3 = "L3";
        public const string VoltagePE = "PE";
        public const string VoltageConflict = "Conflict";
        private const int MaxContactorStateIterations = 5;

        private readonly Dictionary<string, TerminalView> terminalsByKey = new Dictionary<string, TerminalView>();
        private readonly Dictionary<string, string> friendlyNamesByInstanceId = new Dictionary<string, string>();
        private readonly HashSet<string> wiredTerminalKeys = new HashSet<string>();
        private readonly Dictionary<string, HashSet<string>> connectionGraph = new Dictionary<string, HashSet<string>>();
        private bool traversalBudgetWarningLogged;

        public CircuitStateResult Analyze(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
        {
            // 动态线圈与时间继电器触点可能相互依赖。必须保持有界稳定循环的确定性；
            // 改动顺序或迭代上限后，必须验证 18 张真实模板基线。
            traversalBudgetWarningLogged = false;
            friendlyNamesByInstanceId.Clear();
            BuildFriendlyNames(components);

            var previousStates = CreateInitialContactorCoilStates(components);
            var previousTimerStates = CreateInitialTimerRelayCoilStates(components);
            var seenStates = new HashSet<string> { DynamicStateSignature(previousStates, previousTimerStates) };
            CircuitStateResult finalResult = null;
            var stabilized = false;
            var hasInterlockConflict = false;
            var conflictContactorIds = new HashSet<string>();
            var observedStarDeltaConflictMotorIds = new HashSet<string>();
            var observedSimultaneousStarDeltaContactors = false;
            CaptureExplicitStarDeltaConflictEvidence(
                components,
                wires,
                observedStarDeltaConflictMotorIds);
            Dictionary<string, bool> lastCurrentStates = null;
            Dictionary<string, bool> lastCurrentTimerStates = null;

            for (var iteration = 0; iteration < MaxContactorStateIterations; iteration++)
            {
                finalResult = AnalyzePass(components, wires, previousStates, previousTimerStates);
                CaptureStarDeltaConflictEvidence(
                    finalResult,
                    observedStarDeltaConflictMotorIds,
                    ref observedSimultaneousStarDeltaContactors);
                var currentStates = CollectContactorCoilStates(finalResult);
                var currentTimerStates = CollectTimerRelayCoilStates(finalResult);
                lastCurrentStates = currentStates;
                lastCurrentTimerStates = currentTimerStates;
                if (AreDynamicStatesEqual(previousStates, currentStates, previousTimerStates, currentTimerStates))
                {
                    stabilized = true;
                    previousStates = currentStates;
                    previousTimerStates = currentTimerStates;
                    break;
                }

                var signature = DynamicStateSignature(currentStates, currentTimerStates);
                if (!seenStates.Add(signature))
                {
                    hasInterlockConflict = true;
                    AddChangedContactorIds(previousStates, currentStates, conflictContactorIds);
                    break;
                }

                previousStates = currentStates;
                previousTimerStates = currentTimerStates;
            }

            if (!stabilized)
            {
                hasInterlockConflict = true;
                AddChangedContactorIds(previousStates, lastCurrentStates, conflictContactorIds);
                if (conflictContactorIds.Count == 0 && lastCurrentStates != null)
                {
                    foreach (var pair in lastCurrentStates)
                    {
                        conflictContactorIds.Add(pair.Key);
                    }
                }
            }

            if (hasInterlockConflict)
            {
                previousStates = CloneContactorStates(lastCurrentStates ?? previousStates);
                foreach (var instanceId in conflictContactorIds)
                {
                    previousStates[instanceId] = false;
                }

                finalResult = AnalyzePass(
                    components,
                    wires,
                    previousStates,
                    lastCurrentTimerStates ?? previousTimerStates);
                CaptureStarDeltaConflictEvidence(
                    finalResult,
                    observedStarDeltaConflictMotorIds,
                    ref observedSimultaneousStarDeltaContactors);
            }

            ApplyContactorAuxiliaryContactStates(components, finalResult, previousStates, conflictContactorIds);
            ApplyPreservedStarDeltaConflictEvidence(
                finalResult,
                observedStarDeltaConflictMotorIds,
                observedSimultaneousStarDeltaContactors);
            return finalResult;
        }

        private static void CaptureExplicitStarDeltaConflictEvidence(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            HashSet<string> conflictMotorIds)
        {
            if (components == null || wires == null || conflictMotorIds == null)
            {
                return;
            }

            var wireTopology = new TerminalUnionFind();
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var component = components[componentIndex];
                if (component == null || component.Terminals == null)
                {
                    continue;
                }

                for (var terminalIndex = 0; terminalIndex < component.Terminals.Count; terminalIndex++)
                {
                    wireTopology.Add(TerminalKey(component.Terminals[terminalIndex]));
                }
            }

            for (var wireIndex = 0; wireIndex < wires.Count; wireIndex++)
            {
                var wire = wires[wireIndex];
                if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                {
                    continue;
                }

                wireTopology.Union(TerminalKey(wire.StartTerminal), TerminalKey(wire.EndTerminal));
            }

            for (var motorIndex = 0; motorIndex < components.Count; motorIndex++)
            {
                var motor = components[motorIndex];
                if (!IsStarDeltaMotorComponent(motor))
                {
                    continue;
                }

                var hasExplicitStarPoint =
                    AreMotorTerminalsConnected(motor, "U2", "V2", wireTopology) &&
                    AreMotorTerminalsConnected(motor, "V2", "W2", wireTopology);
                var hasExplicitDelta =
                    AreMotorTerminalsConnected(motor, "U1", "W2", wireTopology) &&
                    AreMotorTerminalsConnected(motor, "V1", "U2", wireTopology) &&
                    AreMotorTerminalsConnected(motor, "W1", "V2", wireTopology);

                var hasConfiguredStarContactor = false;
                var hasConfiguredDeltaContactor = false;
                for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
                {
                    var contactor = components[componentIndex];
                    if (!IsContactorComponent(contactor))
                    {
                        continue;
                    }

                    if (IsStarContactorInstanceId(SafeInstanceId(contactor)))
                    {
                        hasConfiguredStarContactor |= HasStandardStarContactorWiring(motor, contactor, wireTopology);
                    }

                    if (IsDeltaContactorInstanceId(SafeInstanceId(contactor)))
                    {
                        hasConfiguredDeltaContactor |= HasStandardDeltaContactorWiring(motor, contactor, wireTopology);
                    }
                }

                if ((hasExplicitStarPoint && (hasExplicitDelta || hasConfiguredDeltaContactor)) ||
                    (hasExplicitDelta && hasConfiguredStarContactor))
                {
                    conflictMotorIds.Add(SafeInstanceId(motor));
                }
            }
        }

        private static bool HasStandardStarContactorWiring(
            CircuitComponent motor,
            CircuitComponent contactor,
            TerminalUnionFind wireTopology)
        {
            return AreTerminalsConnected(motor, "U2", contactor, "L1", wireTopology) &&
                AreTerminalsConnected(motor, "V2", contactor, "L2", wireTopology) &&
                AreTerminalsConnected(motor, "W2", contactor, "L3", wireTopology) &&
                AreComponentTerminalsConnected(contactor, "T1", "T2", wireTopology) &&
                AreComponentTerminalsConnected(contactor, "T2", "T3", wireTopology);
        }

        private static bool HasStandardDeltaContactorWiring(
            CircuitComponent motor,
            CircuitComponent contactor,
            TerminalUnionFind wireTopology)
        {
            return AreTerminalsConnected(motor, "U1", contactor, "L1", wireTopology) &&
                AreTerminalsConnected(motor, "W2", contactor, "T1", wireTopology) &&
                AreTerminalsConnected(motor, "V1", contactor, "L2", wireTopology) &&
                AreTerminalsConnected(motor, "U2", contactor, "T2", wireTopology) &&
                AreTerminalsConnected(motor, "W1", contactor, "L3", wireTopology) &&
                AreTerminalsConnected(motor, "V2", contactor, "T3", wireTopology);
        }

        private static bool AreTerminalsConnected(
            CircuitComponent firstComponent,
            string firstTerminalId,
            CircuitComponent secondComponent,
            string secondTerminalId,
            TerminalUnionFind unionFind)
        {
            var first = firstComponent != null ? firstComponent.GetTerminal(firstTerminalId) : null;
            var second = secondComponent != null ? secondComponent.GetTerminal(secondTerminalId) : null;
            return first != null &&
                second != null &&
                unionFind.Find(TerminalKey(first)) == unionFind.Find(TerminalKey(second));
        }

        private static bool AreComponentTerminalsConnected(
            CircuitComponent component,
            string firstTerminalId,
            string secondTerminalId,
            TerminalUnionFind unionFind)
        {
            return AreTerminalsConnected(component, firstTerminalId, component, secondTerminalId, unionFind);
        }

        private static void CaptureStarDeltaConflictEvidence(
            CircuitStateResult result,
            HashSet<string> conflictMotorIds,
            ref bool simultaneousStarDeltaContactors)
        {
            if (result == null || conflictMotorIds == null)
            {
                return;
            }

            var starContactorClosed = false;
            var deltaContactorClosed = false;
            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.IsStarDeltaMotor &&
                    (component.State == "StarDeltaConflict" ||
                     (component.IsStarPointConnected && component.IsDeltaConnectionDetected)))
                {
                    conflictMotorIds.Add(component.InstanceId);
                }

                if (!component.IsContactor || !component.IsContactorMainContactsClosedByAnalyzer)
                {
                    continue;
                }

                starContactorClosed |= IsStarContactorInstanceId(component.InstanceId);
                deltaContactorClosed |= IsDeltaContactorInstanceId(component.InstanceId);
            }

            if (!starContactorClosed || !deltaContactorClosed)
            {
                return;
            }

            simultaneousStarDeltaContactors = true;
            for (var i = 0; i < result.Components.Count; i++)
            {
                if (result.Components[i].IsStarDeltaMotor)
                {
                    conflictMotorIds.Add(result.Components[i].InstanceId);
                }
            }
        }

        private static void ApplyPreservedStarDeltaConflictEvidence(
            CircuitStateResult result,
            HashSet<string> conflictMotorIds,
            bool simultaneousStarDeltaContactors)
        {
            if (result == null || conflictMotorIds == null || conflictMotorIds.Count == 0)
            {
                return;
            }

            const string topologyDanger =
                "危险：星形连接与三角形连接同时存在，疑似星三角短接，可能造成相间短路。";
            const string contactorDanger =
                "危险：星形接触器 KMY 与三角形接触器 KMD 同时闭合，存在星三角短接风险。";

            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (!component.IsStarDeltaMotor || !conflictMotorIds.Contains(component.InstanceId))
                {
                    continue;
                }

                component.State = "StarDeltaConflict";
                component.StarDeltaConnectionMode = "Conflict";
                component.IsStarPointConnected = true;
                component.IsDeltaConnectionDetected = true;
                component.Judgement = simultaneousStarDeltaContactors
                    ? contactorDanger + topologyDanger
                    : topologyDanger;
                AddUnique(component.MotorIssues, component.Judgement);
                AddComponentError(component, result, component.Judgement);
            }

            result.HasPowerConflict = true;
            result.HasShortCircuit = true;
        }

        private static bool IsStarContactorInstanceId(string instanceId)
        {
            return string.Equals(instanceId, "km_star", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(instanceId) &&
                 instanceId.IndexOf("kmy", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsDeltaContactorInstanceId(string instanceId)
        {
            return string.Equals(instanceId, "km_delta", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(instanceId) &&
                 instanceId.IndexOf("kmd", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private CircuitStateResult AnalyzePass(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            IReadOnlyDictionary<string, bool> contactorCoilStates,
            IReadOnlyDictionary<string, bool> timerRelayCoilStates)
        {
            // 单次推导严格按“登记端子 -> 外部导线 -> 内部触点 -> 电源标签 -> 元件状态”执行。
            // 动态线圈状态由 Analyze 的有界稳定循环提供，本方法本身不推进时间也不修改场景。
            terminalsByKey.Clear();
            wiredTerminalKeys.Clear();
            connectionGraph.Clear();

            var result = new CircuitStateResult();
            var unionFind = new TerminalUnionFind();
            RegisterTerminals(components, unionFind, result);
            AddWireConnections(wires, unionFind, result);
            AddInternalConnections(components, unionFind);
            AddTimerRelayDelayedContacts(components, unionFind, timerRelayCoilStates);
            AddDynamicContactorAuxiliaryContacts(components, unionFind, contactorCoilStates);
            AddDynamicContactorMainContacts(components, unionFind, contactorCoilStates, result);

            var rootLabels = new Dictionary<string, HashSet<string>>();
            var rootSources = new Dictionary<string, List<string>>();
            AddPowerLabels(components, unionFind, rootLabels, rootSources, result);
            DetectPowerConflicts(rootLabels, result);
            BuildComponentStates(components, unionFind, rootLabels, rootSources, result);
            ApplyContactorMainContactStates(result, contactorCoilStates);
            FinalizeDeferredMotorStates(result);
            FinalizeLimitSwitchAnalysis(result);
            BuildLoadPathExplanations(components, result);
            FinalizeBreakerControlAnalysis(result);
            return result;
        }

        private static Dictionary<string, bool> CreateInitialContactorCoilStates(
            IReadOnlyList<CircuitComponent> components)
        {
            var states = new Dictionary<string, bool>();
            if (components == null)
            {
                return states;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (IsContactorComponent(component))
                {
                    states[SafeInstanceId(component)] = false;
                }
            }

            return states;
        }

        private static Dictionary<string, bool> CreateInitialTimerRelayCoilStates(
            IReadOnlyList<CircuitComponent> components)
        {
            var states = new Dictionary<string, bool>();
            if (components == null)
            {
                return states;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (IsOnDelayTimerRelay(component))
                {
                    states[SafeInstanceId(component)] = false;
                }
            }

            return states;
        }

        private static bool AreDynamicStatesEqual(
            IReadOnlyDictionary<string, bool> previousContactorStates,
            IReadOnlyDictionary<string, bool> currentContactorStates,
            IReadOnlyDictionary<string, bool> previousTimerStates,
            IReadOnlyDictionary<string, bool> currentTimerStates)
        {
            return AreContactorStatesEqual(previousContactorStates, currentContactorStates) &&
                AreContactorStatesEqual(previousTimerStates, currentTimerStates);
        }

        private static bool AreContactorStatesEqual(
            IReadOnlyDictionary<string, bool> left,
            IReadOnlyDictionary<string, bool> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, bool> CloneContactorStates(IReadOnlyDictionary<string, bool> states)
        {
            var clone = new Dictionary<string, bool>();
            if (states == null)
            {
                return clone;
            }

            foreach (var pair in states)
            {
                clone[pair.Key] = pair.Value;
            }

            return clone;
        }

        private static void AddChangedContactorIds(
            IReadOnlyDictionary<string, bool> previousStates,
            IReadOnlyDictionary<string, bool> currentStates,
            HashSet<string> changedIds)
        {
            if (previousStates == null || currentStates == null || changedIds == null)
            {
                return;
            }

            foreach (var pair in currentStates)
            {
                if (!previousStates.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
                {
                    changedIds.Add(pair.Key);
                }
            }
        }

        private static string ContactorStateSignature(IReadOnlyDictionary<string, bool> states)
        {
            if (states == null || states.Count == 0)
            {
                return string.Empty;
            }

            var keys = new List<string>(states.Keys);
            keys.Sort(StringComparer.Ordinal);
            var builder = new StringBuilder();
            for (var i = 0; i < keys.Count; i++)
            {
                builder.Append(keys[i]).Append('=').Append(states[keys[i]] ? '1' : '0').Append(';');
            }

            return builder.ToString();
        }

        private static string DynamicStateSignature(
            IReadOnlyDictionary<string, bool> contactorStates,
            IReadOnlyDictionary<string, bool> timerStates)
        {
            return "C:" + ContactorStateSignature(contactorStates) +
                "|T:" + ContactorStateSignature(timerStates);
        }

        private static Dictionary<string, bool> CollectContactorCoilStates(CircuitStateResult result)
        {
            var states = new Dictionary<string, bool>();
            if (result == null)
            {
                return states;
            }

            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.IsContactor)
                {
                    states[component.InstanceId] = component.IsContactorCoilEnergizedByAnalyzer;
                }
            }

            return states;
        }

        private static Dictionary<string, bool> CollectTimerRelayCoilStates(CircuitStateResult result)
        {
            var states = new Dictionary<string, bool>();
            if (result == null)
            {
                return states;
            }

            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.IsOnDelayTimerRelay)
                {
                    states[component.InstanceId] = component.IsTimerRelayCoilEnergizedByAnalyzer;
                }
            }

            return states;
        }

        private void RegisterTerminals(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            CircuitStateResult result)
        {
            if (components == null)
            {
                result.Warnings.Add("当前画布元件列表不可用。");
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                if (component.Terminals == null || component.Terminals.Count == 0)
                {
                    result.Warnings.Add(DisplayName(component) + " 没有可分析的端子。");
                    continue;
                }

                for (var terminalIndex = 0; terminalIndex < component.Terminals.Count; terminalIndex++)
                {
                    var terminal = component.Terminals[terminalIndex];
                    if (terminal == null || string.IsNullOrWhiteSpace(terminal.TerminalId))
                    {
                        result.Warnings.Add(DisplayName(component) + " 存在无法识别的端子。");
                        continue;
                    }

                    var key = TerminalKey(terminal);
                    if (terminalsByKey.ContainsKey(key))
                    {
                        result.Warnings.Add("检测到重复端子标识：" + FriendlyTerminalKey(key) + "。");
                        continue;
                    }

                    terminalsByKey.Add(key, terminal);
                    unionFind.Add(key);
                }
            }
        }

        private void AddWireConnections(
            IReadOnlyList<WireView> wires,
            TerminalUnionFind unionFind,
            CircuitStateResult result)
        {
            if (wires == null)
            {
                result.Warnings.Add("当前画布导线列表不可用。");
                return;
            }

            for (var i = 0; i < wires.Count; i++)
            {
                var wire = wires[i];
                if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                {
                    result.Warnings.Add("发现端点不完整的导线，已跳过。");
                    continue;
                }

                var startKey = TerminalKey(wire.StartTerminal);
                var endKey = TerminalKey(wire.EndTerminal);
                if (!terminalsByKey.ContainsKey(startKey) || !terminalsByKey.ContainsKey(endKey))
                {
                    result.Warnings.Add("导线端点无法对应到当前画布端子：" + FriendlyTerminalKey(startKey) + " -> " + FriendlyTerminalKey(endKey) + "。");
                    continue;
                }

                ConnectTerminalKeys(startKey, endKey, unionFind);
                wiredTerminalKeys.Add(startKey);
                wiredTerminalKeys.Add(endKey);
            }
        }

        private void AddInternalConnections(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind)
        {
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                if (component.Definition.kind == ComponentKind.EnergyMeter || IsEnergyMeter(component))
                {
                    ConnectIfExists(component, "L_IN", "L_OUT", unionFind);
                    ConnectIfExists(component, "N_IN", "N_OUT", unionFind);
                    continue;
                }

                if (component.Definition.kind == ComponentKind.TwoWaySwitch || DefinitionContains(component, "TwoWay", "DoubleThrow", "双控"))
                {
                    ConnectIfExists(component, "L", component.IsClosed ? "L1" : "L2", unionFind);
                    ConnectIfExists(component, "COM", component.IsClosed ? "L1" : "L2", unionFind);
                    continue;
                }

                if (IsThermalRelay(component))
                {
                    ConnectIfExists(component, "L1", "T1", unionFind);
                    ConnectIfExists(component, "L2", "T2", unionFind);
                    ConnectIfExists(component, "L3", "T3", unionFind);
                    if (component.IsClosed)
                    {
                        ConnectIfExists(component, "95", "96", unionFind);
                    }
                    else
                    {
                        ConnectIfExists(component, "97", "98", unionFind);
                    }

                    continue;
                }

                if (IsLimitSwitchComponent(component))
                {
                    ConnectIfExists(
                        component,
                        component.IsClosed ? "23" : "11",
                        component.IsClosed ? "24" : "12",
                        unionFind);
                    continue;
                }

                if (IsCompoundPushButton(component))
                {
                    ConnectIfExists(component, component.IsClosed ? "23" : "11", component.IsClosed ? "24" : "12", unionFind);
                    continue;
                }

                if (IsSelfLockingButton(component))
                {
                    ConnectIfExists(component, component.IsClosed ? "23" : "11", component.IsClosed ? "24" : "12", unionFind);
                    continue;
                }

                if (component.Definition.kind == ComponentKind.PushButton)
                {
                    if (component.IsClosed)
                    {
                        ConnectIfExists(component, "23", "24", unionFind);
                        ConnectIfExists(component, "11", "12", unionFind);
                    }

                    continue;
                }

                if (IsKnifeSwitch(component))
                {
                    if (component.IsClosed)
                    {
                        ConnectKnownPairs(component, unionFind, false);
                    }

                    continue;
                }

                if (component.Definition.kind == ComponentKind.Breaker || IsBreaker(component))
                {
                    if (component.IsClosed)
                    {
                        ConnectKnownPairs(component, unionFind, true);
                    }

                    continue;
                }

                if (component.Definition.kind == ComponentKind.Fuse || DefinitionContains(component, "Fuse", "熔断器", "保险"))
                {
                    if (!component.Definition.togglable || component.IsClosed)
                    {
                        ConnectKnownPairs(component, unionFind, true);
                    }

                    continue;
                }

                if (IsHouseholdSwitch(component))
                {
                    if (component.IsClosed)
                    {
                        ConnectKnownPairs(component, unionFind, true);
                    }
                }
            }
        }

        private void AddDynamicContactorAuxiliaryContacts(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            IReadOnlyDictionary<string, bool> contactorCoilStates)
        {
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsContactorComponent(component))
                {
                    continue;
                }

                var isEnergized = contactorCoilStates != null &&
                    contactorCoilStates.TryGetValue(SafeInstanceId(component), out var state) &&
                    state;
                if (isEnergized)
                {
                    ConnectIfExists(component, "13", "14", unionFind);
                }
                else
                {
                    ConnectIfExists(component, "21", "22", unionFind);
                }
            }
        }

        private void AddTimerRelayDelayedContacts(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            IReadOnlyDictionary<string, bool> timerRelayCoilStates)
        {
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsOnDelayTimerRelay(component))
                {
                    continue;
                }

                var coilEnergized = timerRelayCoilStates != null &&
                    timerRelayCoilStates.TryGetValue(SafeInstanceId(component), out var state) &&
                    state;
                if (coilEnergized && component.IsClosed)
                {
                    ConnectIfExists(component, "15", "18", unionFind);
                }
                else
                {
                    ConnectIfExists(component, "15", "16", unionFind);
                }
            }
        }

        private void AddDynamicContactorMainContacts(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            IReadOnlyDictionary<string, bool> contactorCoilStates,
            CircuitStateResult result)
        {
            if (components == null || contactorCoilStates == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsContactorComponent(component) ||
                    !contactorCoilStates.TryGetValue(SafeInstanceId(component), out var isEnergized) ||
                    !isEnergized)
                {
                    continue;
                }

                var connectedL1 = ConnectIfExists(component, "L1", "T1", unionFind);
                var connectedL2 = ConnectIfExists(component, "L2", "T2", unionFind);
                var connectedL3 = ConnectIfExists(component, "L3", "T3", unionFind);
                if (!connectedL1 || !connectedL2 || !connectedL3)
                {
                    AddUnique(result.Warnings, DisplayName(component) + " 缺少完整的 L1/T1、L2/T2、L3/T3 主触点端子，V1.3 无法完整传播三相标签。");
                }
            }
        }

        private static void ApplyContactorMainContactStates(
            CircuitStateResult result,
            IReadOnlyDictionary<string, bool> contactorCoilStates)
        {
            if (result == null)
            {
                return;
            }

            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (!component.IsContactor)
                {
                    continue;
                }

                var isClosed = contactorCoilStates != null &&
                    contactorCoilStates.TryGetValue(component.InstanceId, out var isEnergized) &&
                    isEnergized;
                component.IsContactorMainContactsClosedByAnalyzer = isClosed;
                component.MainContactStatus = isClosed ? "Closed" : "Open";
                component.MainContactDescription = isClosed
                    ? "根据 V1.2 线圈得电结果，V1.3 将该接触器三组主触点作为闭合处理。"
                    : "接触器线圈未得电，因此 V1.3 不闭合该接触器主触点。";
            }
        }

        private void ApplyContactorAuxiliaryContactStates(
            IReadOnlyList<CircuitComponent> components,
            CircuitStateResult result,
            IReadOnlyDictionary<string, bool> contactorCoilStates,
            HashSet<string> conflictContactorIds)
        {
            if (components == null || result == null)
            {
                return;
            }

            result.HasContactorInterlockConflict = conflictContactorIds != null && conflictContactorIds.Count > 0;
            if (result.HasContactorInterlockConflict)
            {
                AddUnique(result.Warnings, "检测到接触器线圈状态在动态互锁迭代中无法稳定，属于方向同时启动或互锁冲突操作；V1.4 已阻止相关主触点作为正常闭合状态传播。");
            }

            var contactorCount = CountContactors(components);
            var energizedContactorCount = CountEnergizedContactors(contactorCoilStates);
            var hasOpenStopControl = HasOpenStopControlButton(components);
            result.HasCompoundButtonInterlockConflict =
                contactorCount > 1 && CountPressedCompoundButtons(components) > 1;
            if (result.HasCompoundButtonInterlockConflict)
            {
                AddUnique(result.Warnings, "检测到两个方向复合按钮同时按下；按钮联锁使两个方向不能作为正常启动状态。");
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsContactorComponent(component))
                {
                    continue;
                }

                var info = result.FindComponent(SafeInstanceId(component));
                if (info == null)
                {
                    continue;
                }

                info.HasSelfHoldStructure = IsTerminalExternallyWired(component, "13") &&
                    IsTerminalExternallyWired(component, "14");
                info.HasInterlockStructure = IsTerminalExternallyWired(component, "21") &&
                    IsTerminalExternallyWired(component, "22");

                var isEnergized = contactorCoilStates != null &&
                    contactorCoilStates.TryGetValue(info.InstanceId, out var state) &&
                    state;
                var isInterlockConflict = conflictContactorIds != null &&
                    conflictContactorIds.Contains(info.InstanceId);
                info.IsSelfHoldCutByStopOrControlOpen = info.HasSelfHoldStructure && hasOpenStopControl;
                info.IsCompoundButtonInterlockConflict = result.HasCompoundButtonInterlockConflict;
                info.IsInactiveDirectionInForwardReversePair =
                    info.HasSelfHoldStructure && contactorCount > 1 && !isEnergized;
                info.HasSelfHoldHistoryAmbiguity =
                    info.HasSelfHoldStructure &&
                    !hasOpenStopControl &&
                    !isEnergized &&
                    !isInterlockConflict &&
                    !result.HasCompoundButtonInterlockConflict &&
                    !info.IsInactiveDirectionInForwardReversePair;
                info.SelfHoldStatus = BuildSelfHoldStatus(
                    info,
                    isEnergized,
                    isInterlockConflict,
                    energizedContactorCount);
                info.InterlockStatus = !info.HasInterlockStructure
                    ? "未检测到 21/22 互锁结构"
                    : isEnergized
                        ? "线圈得电，21/22 常闭辅助触点按断开处理"
                        : "线圈未得电，21/22 常闭辅助触点按闭合处理";

                if (conflictContactorIds == null || !conflictContactorIds.Contains(info.InstanceId))
                {
                    continue;
                }

                info.IsContactorCoilEnergizedByAnalyzer = false;
                info.IsContactorMainContactsClosedByAnalyzer = false;
                info.State = "InterlockConflict";
                info.CoilStatus = "InterlockConflict";
                info.MainContactStatus = "Open";
                info.Judgement = "检测到方向同时启动或动态互锁状态无法稳定，V1.4 不将该接触器作为正常吸合状态处理。";
                info.MainContactDescription = "互锁冲突状态下，V1.4 已阻止主触点作为正常闭合状态传播。";
            }
        }

        private static string BuildSelfHoldStatus(
            ComponentStateInfo info,
            bool isEnergized,
            bool isInterlockConflict,
            int energizedContactorCount)
        {
            if (!info.HasSelfHoldStructure)
            {
                return "未检测到 13/14 自锁结构";
            }

            if (isInterlockConflict || info.IsCompoundButtonInterlockConflict)
            {
                return "方向同时动作触发按钮联锁或互锁冲突；13/14 未作为正常闭合状态处理，不处于自锁保持状态";
            }

            if (info.IsSelfHoldCutByStopOrControlOpen)
            {
                return "检测到 13/14 自锁结构，但停止按钮已断开，控制电源与自锁保持路径当前被切断";
            }

            if (isEnergized)
            {
                return "13/14 已按线圈得电状态闭合，具备自锁保持条件";
            }

            if (info.IsInactiveDirectionInForwardReversePair)
            {
                return energizedContactorCount > 0
                    ? "当前方向未启动或被另一方向互锁切断；线圈未得电，13/14 未闭合，不处于自锁保持状态"
                    : "当前方向未启动或控制路径未闭合；线圈未得电，13/14 未闭合，不处于自锁保持状态";
            }

            return "检测到接触器 13/14 自锁结构。当前检查面板为静态拓扑分析，不读取画布 RUN 历史；若该接触器此前已经吸合，则可能通过自锁支路保持运行。请以冷启动状态或重置运行状态后再判断是否会自行启动";
        }

        private static int CountContactors(IReadOnlyList<CircuitComponent> components)
        {
            var count = 0;
            if (components == null)
            {
                return count;
            }

            for (var i = 0; i < components.Count; i++)
            {
                if (IsContactorComponent(components[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountEnergizedContactors(IReadOnlyDictionary<string, bool> contactorCoilStates)
        {
            var count = 0;
            if (contactorCoilStates == null)
            {
                return count;
            }

            foreach (var pair in contactorCoilStates)
            {
                if (pair.Value)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountPressedCompoundButtons(IReadOnlyList<CircuitComponent> components)
        {
            var count = 0;
            if (components == null)
            {
                return count;
            }

            for (var i = 0; i < components.Count; i++)
            {
                if (IsCompoundPushButton(components[i]) && components[i].IsClosed)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasOpenStopControlButton(IReadOnlyList<CircuitComponent> components)
        {
            if (components == null)
            {
                return false;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null ||
                    component.Definition == null ||
                    component.Definition.kind != ComponentKind.PushButton ||
                    IsCompoundPushButton(component))
                {
                    continue;
                }

                var hasNormallyClosedPair =
                    component.GetTerminal("11") != null && component.GetTerminal("12") != null;
                var isStopButton = hasNormallyClosedPair &&
                    (component.GetTerminal("23") == null ||
                        component.GetTerminal("24") == null ||
                        DefinitionContains(component, "Stop", "停止"));
                if (isStopButton && !component.IsClosed)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsTerminalExternallyWired(CircuitComponent component, string terminalId)
        {
            var terminal = component != null ? component.GetTerminal(terminalId) : null;
            return terminal != null && wiredTerminalKeys.Contains(TerminalKey(terminal));
        }

        private static void FinalizeDeferredMotorStates(CircuitStateResult result)
        {
            if (result == null)
            {
                return;
            }

            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (!component.IsMotorDeferredByContactorOutput)
                {
                    continue;
                }

                component.State = "Stopped";
                component.Judgement =
                    "该电机接在接触器输出侧，但当前未通过 V1.3 已闭合主触点获得完整三相标签，因此当前未得电。";
                component.Infos.Clear();
                component.Infos.Add("这表示当前上级接触器主触点未闭合，不判定为电机接线缺相。");
            }
        }

        private static void FinalizeLimitSwitchAnalysis(CircuitStateResult result)
        {
            if (result == null)
            {
                return;
            }

            var limitSwitchCount = 0;
            var triggeredCount = 0;
            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (!component.IsLimitSwitch)
                {
                    continue;
                }

                limitSwitchCount++;
                if (component.IsLimitSwitchTriggered)
                {
                    triggeredCount++;
                }
            }

            result.HasLimitSwitches = limitSwitchCount > 0;
            result.HasSimultaneouslyTriggeredLimitSwitches = triggeredCount > 1;
            if (result.HasSimultaneouslyTriggeredLimitSwitches)
            {
                AddUnique(result.Warnings, "检测到两个或更多 SQ 行程开关同时触发，机械位置状态不合理或接线/状态设置异常；不应将其解释为正常自动往返状态。");
            }
        }

        private void AddPowerLabels(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            Dictionary<string, HashSet<string>> rootLabels,
            Dictionary<string, List<string>> rootSources,
            CircuitStateResult result)
        {
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null || component.Definition.kind != ComponentKind.PowerSource)
                {
                    continue;
                }

                if (component.GetTerminal("L1") != null || component.Definition.sourcePhaseCount >= 3)
                {
                    result.HasThreePhaseCircuit = true;
                    result.ContainsUnsupportedThreePhaseCircuit = true;
                    var missingPhases = new List<string>();
                    if (component.GetTerminal("L1") == null) missingPhases.Add("L1");
                    if (component.GetTerminal("L2") == null) missingPhases.Add("L2");
                    if (component.GetTerminal("L3") == null) missingPhases.Add("L3");
                    if (missingPhases.Count > 0)
                    {
                        result.Warnings.Add(DisplayName(component) + " 缺少三相核心端子：" + string.Join("、", missingPhases.ToArray()) + "。");
                    }

                    AddPowerLabel(component.GetTerminal("L1"), VoltageL1, unionFind, rootLabels, rootSources);
                    AddPowerLabel(component.GetTerminal("L2"), VoltageL2, unionFind, rootLabels, rootSources);
                    AddPowerLabel(component.GetTerminal("L3"), VoltageL3, unionFind, rootLabels, rootSources);
                    AddPowerLabel(component.GetTerminal("N"), VoltageN, unionFind, rootLabels, rootSources);
                    AddPowerLabel(component.GetTerminal("PE"), VoltagePE, unionFind, rootLabels, rootSources);
                    continue;
                }

                var line = component.GetTerminal("L");
                var neutral = component.GetTerminal("N");
                if (line == null || neutral == null)
                {
                    result.Warnings.Add(DisplayName(component) + " 缺少 L 或 N 端子，无法传播单相电压标签。");
                    continue;
                }

                AddPowerLabel(line, VoltageL, unionFind, rootLabels, rootSources);
                AddPowerLabel(neutral, VoltageN, unionFind, rootLabels, rootSources);
                // E4：单相电源的 L2/N2 端子与 L/N 同相位，标记为相同电压标签，
                // 使分析器能识别经 L2/N2 供电的灯泡并检测 L2-N 短路。
                AddPowerLabel(component.GetTerminal("L2"), VoltageL, unionFind, rootLabels, rootSources);
                AddPowerLabel(component.GetTerminal("N2"), VoltageN, unionFind, rootLabels, rootSources);
                AddPowerLabel(component.GetTerminal("PE"), VoltagePE, unionFind, rootLabels, rootSources);
            }
        }

        private static void AddPowerLabel(
            TerminalView terminal,
            string label,
            TerminalUnionFind unionFind,
            Dictionary<string, HashSet<string>> rootLabels,
            Dictionary<string, List<string>> rootSources)
        {
            if (terminal == null)
            {
                return;
            }

            var terminalKey = TerminalKey(terminal);
            var root = unionFind.Find(terminalKey);
            if (root == null)
            {
                return;
            }

            if (!rootLabels.TryGetValue(root, out var labels))
            {
                labels = new HashSet<string>();
                rootLabels.Add(root, labels);
            }

            labels.Add(label);
            if (!rootSources.TryGetValue(root, out var sources))
            {
                sources = new List<string>();
                rootSources.Add(root, sources);
            }

            if (!sources.Contains(terminalKey))
            {
                sources.Add(terminalKey);
            }
        }

        private static void DetectPowerConflicts(
            Dictionary<string, HashSet<string>> rootLabels,
            CircuitStateResult result)
        {
            foreach (var pair in rootLabels)
            {
                var labels = pair.Value;
                if (labels.Contains(VoltageL) && labels.Contains(VoltageN))
                {
                    result.HasShortCircuit = true;
                    result.HasPowerConflict = true;
                    AddUnique(result.Errors, "检测到 L 与 N 位于同一导通节点，存在短路风险。");
                }

                DetectPhasePairConflict(labels, VoltageL1, VoltageL2, result);
                DetectPhasePairConflict(labels, VoltageL1, VoltageL3, result);
                DetectPhasePairConflict(labels, VoltageL2, VoltageL3, result);

                if ((labels.Contains(VoltageL1) || labels.Contains(VoltageL2) || labels.Contains(VoltageL3)) &&
                    labels.Contains(VoltageN))
                {
                    result.HasShortCircuit = true;
                    result.HasPowerConflict = true;
                    AddUnique(result.Errors, "检测到三相相线与 N 位于同一导通节点，存在短路风险。");
                }

                if ((labels.Contains(VoltageL) || labels.Contains(VoltageL1) || labels.Contains(VoltageL2) ||
                    labels.Contains(VoltageL3)) && labels.Contains(VoltagePE))
                {
                    result.HasShortCircuit = true;
                    result.HasPowerConflict = true;
                    AddUnique(result.Errors, "检测到相线与 PE 位于同一导通节点，存在接地短路风险。");
                }
                else if (labels.Contains(VoltageN) && labels.Contains(VoltagePE))
                {
                    result.HasPowerConflict = true;
                    AddUnique(result.Warnings, "检测到 N 与 PE 相连，请确认是否为规范接地/接零关系，V1.0 暂不做复杂接地系统判断。");
                }
            }
        }

        private static void DetectPhasePairConflict(
            HashSet<string> labels,
            string phaseA,
            string phaseB,
            CircuitStateResult result)
        {
            if (!labels.Contains(phaseA) || !labels.Contains(phaseB))
            {
                return;
            }

            result.HasShortCircuit = true;
            result.HasPowerConflict = true;
            AddUnique(result.Errors, "检测到 " + phaseA + " 与 " + phaseB + " 位于同一导通节点，存在相间短路风险。");
        }

        private void BuildComponentStates(
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            Dictionary<string, HashSet<string>> rootLabels,
            Dictionary<string, List<string>> rootSources,
            CircuitStateResult result)
        {
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                var info = new ComponentStateInfo
                {
                    InstanceId = SafeInstanceId(component),
                    DefinitionName = component.Definition.name,
                    DisplayName = friendlyNamesByInstanceId.TryGetValue(SafeInstanceId(component), out var friendlyName)
                        ? friendlyName
                        : DisplayName(component),
                    State = component.Definition.togglable ? (component.IsClosed ? "On" : "Off") : "Unknown"
                };

                for (var terminalIndex = 0; terminalIndex < component.Terminals.Count; terminalIndex++)
                {
                    var terminal = component.Terminals[terminalIndex];
                    if (terminal == null)
                    {
                        continue;
                    }

                    WriteActualTerminalState(terminal, info, unionFind, rootLabels, rootSources);
                }

                if (IsLamp(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupLoad;
                    AnalyzeSinglePhaseLoad(component, info, result, false);
                }
                else if (IsFan(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupLoad;
                    AnalyzeSinglePhaseLoad(component, info, result, true);
                }
                else if (IsIndicator(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupLoad;
                    AnalyzeIndicatorLoad(component, info, result);
                }
                else if (IsStarDeltaMotorComponent(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupLoad;
                    info.IsThreePhaseMotor = true;
                    info.IsStarDeltaMotor = true;
                    result.HasStarDeltaMotors = true;
                    AnalyzeStarDeltaMotor(component, unionFind, info, result);
                }
                else if (IsThreePhaseMotorComponent(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupLoad;
                    info.IsThreePhaseMotor = true;
                    AnalyzeThreePhaseMotor(component, components, unionFind, info, result);
                }
                else if (IsOnDelayTimerRelay(component) || IsOffDelayTimerRelay(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.IsTimerRelay = true;
                    info.IsOnDelayTimerRelay = IsOnDelayTimerRelay(component);
                    info.IsOffDelayTimerRelay = IsOffDelayTimerRelay(component);
                    result.HasTimerRelays = true;
                    AnalyzeTimerRelay(component, info, result);
                }
                else if (IsContactorComponent(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.IsContactor = true;
                    AnalyzeContactorCoil(component, info, result);
                }
                else if (IsLimitSwitchComponent(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.IsLimitSwitch = true;
                    info.IsLimitSwitchTriggered = component.IsClosed;
                    info.State = component.IsClosed ? "Triggered" : "NotTriggered";
                    info.LimitSwitchContactDescription = component.IsClosed
                        ? "已触发：11/12 常闭触点断开，23/24 常开触点导通"
                        : "未触发：11/12 常闭触点导通，23/24 常开触点断开";
                    info.Judgement = component.IsClosed
                        ? "当前已触发该限位，常闭触点断开，常开触点闭合。"
                        : "当前未到达该限位位置，常闭触点保持导通。";
                }
                else if (component.Definition.kind == ComponentKind.TwoWaySwitch || DefinitionContains(component, "TwoWay", "DoubleThrow", "双控"))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.IsTwoWaySwitch = true;
                    info.State = component.IsClosed ? "TwoWayL1" : "TwoWayL2";
                    info.ConductionExplanation = component.IsClosed ? "当前导通：COM → L1" : "当前导通：COM → L2";
                }
                else if (IsHouseholdSwitch(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.IsHouseholdSwitch = true;
                    info.State = component.IsClosed ? "Closed" : "Open";
                    info.ConductionExplanation = component.IsClosed
                        ? "当前导通：" + FindFirstTerminalPair(component, " → ")
                        : "当前状态：" + FindFirstTerminalPair(component, " 与 ") + " 不导通";
                }
                else if (component.Definition.kind == ComponentKind.PushButton)
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.State = component.IsClosed ? "Closed" : "Open";
                    if (IsEmergencyStop(component))
                    {
                        info.ConductionExplanation = component.IsClosed
                            ? "急停未按下：NC 触点导通，控制回路可通过。"
                            : "急停已按下：NC 触点断开，控制回路被急停切断。";
                        info.Judgement = component.IsClosed
                            ? "急停按钮当前未触发。"
                            : "急停按钮当前触发，后级接触器线圈或控制支路应失电。";
                    }
                    else
                    {
                        info.ConductionExplanation = component.IsClosed
                            ? "当前导通：" + FindFirstTerminalPair(component, " → ")
                            : "当前状态：" + FindFirstTerminalPair(component, " 与 ") + " 不导通";
                    }
                }
                else if (IsThermalRelay(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.State = "Conducting";
                    info.Judgement = component.IsClosed
                        ? "热继正常，95/96 常闭触点导通，97/98 常开触点断开。"
                        : "热继已跳闸，95/96 常闭触点断开，97/98 常开触点闭合；主回路端子仍保持物理通路，电机停止通常由 KM 线圈失电导致。";
                }
                else if (IsKnifeSwitch(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.State = component.IsClosed ? "Closed" : "Open";
                    info.ConductionExplanation = component.IsClosed
                        ? "刀开关 ON：主回路已接通，L1/T1、L2/T2、L3/T3 按当前端子成对导通。"
                        : "刀开关 OFF：主回路被切断，L1/T1、L2/T2、L3/T3 不导通。";
                    info.Judgement = component.IsClosed
                        ? "刀开关当前闭合，只承担手动通断作用，不按接触器线圈逻辑判断。"
                        : "刀开关当前断开，后级主回路无法通过该开关获得电源。";
                }
                else if (component.Definition.kind == ComponentKind.Breaker || IsBreaker(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.IsBreaker = true;
                    info.State = component.IsClosed ? "Closed" : "Open";
                    info.ConductionExplanation = component.IsClosed
                        ? "当前导通：" + FindFirstTerminalPair(component, " → ")
                        : "当前状态：" + FindFirstTerminalPair(component, " 与 ") + " 不导通";
                    AnalyzeBreaker(component, info, result);
                }
                else if (component.Definition.kind == ComponentKind.Fuse || DefinitionContains(component, "Fuse", "熔断器", "保险"))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupControl;
                    info.State = !component.Definition.togglable || component.IsClosed ? "Conducting" : "Open";
                    info.ConductionExplanation = info.State == "Conducting"
                        ? "当前状态：熔断器输入端与输出端导通"
                        : "当前状态：熔断器已断开，输入端与输出端不导通";
                }
                else if (component.Definition.kind == ComponentKind.PowerSource)
                {
                    info.SummaryGroup = ComponentStateInfo.GroupOther;
                    info.State = "Normal";
                }
                else if (component.Definition.kind == ComponentKind.EnergyMeter || IsEnergyMeter(component))
                {
                    info.SummaryGroup = ComponentStateInfo.GroupOther;
                    info.State = AreMeterTerminalsWired(component) ? "Conducting" : "Incomplete";
                    info.Judgement = "V0.1.1 简化认为电能表 L_IN/L_OUT、N_IN/N_OUT 内部导通，不计算电能。";
                }

                result.Components.Add(info);
            }
        }

        private void AnalyzeThreePhaseMotor(
            CircuitComponent motor,
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            ComponentStateInfo info,
            CircuitStateResult result)
        {
            var uId = FindExistingTerminalId(motor, "U", "U1");
            var vId = FindExistingTerminalId(motor, "V", "V1");
            var wId = FindExistingTerminalId(motor, "W", "W1");
            if (uId == null || vId == null || wId == null)
            {
                info.State = "Unknown";
                info.MotorIssues.Add("检测到电机元件，但未找到完整 U/V/W 三相输入端子，V1.1 暂不分析该电机。");
                return;
            }

            var u = VoltageAt(info, uId);
            var v = VoltageAt(info, vId);
            var w = VoltageAt(info, wId);
            if (u == VoltageConflict || v == VoltageConflict || w == VoltageConflict)
            {
                info.State = "Fault";
                info.Judgement = "电机端子存在电压冲突或相间短路风险，电机不能运行。";
                info.MotorIssues.Add(info.Judgement);
                AddComponentError(info, result, info.Judgement);
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            var invalidTerminals = new List<string>();
            AddInvalidMotorPhase(invalidTerminals, uId, u);
            AddInvalidMotorPhase(invalidTerminals, vId, v);
            AddInvalidMotorPhase(invalidTerminals, wId, w);
            if (invalidTerminals.Count > 0)
            {
                if ((u == VoltageNone || v == VoltageNone || w == VoltageNone) &&
                    IsMotorFedThroughContactorOutputs(motor, components, unionFind, out var contactorNames))
                {
                    info.State = "Unknown";
                    info.IsMotorDeferredByContactorOutput = true;
                    info.MotorFeederContactorNames = contactorNames;
                    info.Judgement =
                        "该电机已接在接触器 " + contactorNames +
                        " 的 T1/T2/T3 输出侧。当前 V1.1 只判断已经传播到电机端子的三相标签，" +
                        "V1.2 虽可单独判断接触器线圈状态，但尚未传播主触点动态闭合，因此暂不判断该电机是否缺相或运行。";
                    info.Infos.Add("待 V1.3 完成接触器主触点动态传播后，再判断该电机实际三相状态。");
                    AnalyzeMotorPe(motor, info, result);
                    return;
                }

                info.State = "Stopped";
                info.Judgement = "电机未获得完整有效三相，当前不运行。";
                info.MotorIssues.Add(string.Join("；", invalidTerminals.ToArray()) + "。");
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            var direction = MotorPhaseSequenceEvaluator.Evaluate(
                new[] { u },
                new[] { v },
                new[] { w });
            switch (direction.Direction)
            {
                case MotorDirectionState.Forward:
                    info.State = "Forward";
                    info.Judgement = "电机获得完整三相，当前相序为正向相序，判断为正转。";
                    break;
                case MotorDirectionState.Reverse:
                    info.State = "Reverse";
                    info.Judgement = "电机获得完整三相，但相序与正转相反，判断为反转。";
                    break;
                case MotorDirectionState.Invalid:
                    info.State = "Fault";
                    info.Judgement = direction.Reason;
                    info.MotorIssues.Add(info.Judgement);
                    break;
                default:
                    info.State = "Unknown";
                    info.Judgement = direction.Reason;
                    break;
            }

            AnalyzeMotorPe(motor, info, result);
        }

        private static void AnalyzeStarDeltaMotor(
            CircuitComponent motor,
            TerminalUnionFind unionFind,
            ComponentStateInfo info,
            CircuitStateResult result)
        {
            var requiredTerminals = new[] { "U1", "V1", "W1", "U2", "V2", "W2" };
            for (var i = 0; i < requiredTerminals.Length; i++)
            {
                if (motor.GetTerminal(requiredTerminals[i]) != null)
                {
                    continue;
                }

                info.State = "Unknown";
                info.StarDeltaConnectionMode = "Incomplete";
                info.Judgement = "星三角电机缺少完整的 U1/V1/W1/U2/V2/W2 绕组端子，V1.7 无法判断连接方式。";
                info.MotorIssues.Add(info.Judgement);
                AddComponentWarning(info, result, info.Judgement);
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            info.IsStarPointConnected =
                AreMotorTerminalsConnected(motor, "U2", "V2", unionFind) &&
                AreMotorTerminalsConnected(motor, "V2", "W2", unionFind);
            info.IsDeltaConnectionDetected =
                AreMotorTerminalsConnected(motor, "U1", "W2", unionFind) &&
                AreMotorTerminalsConnected(motor, "V1", "U2", unionFind) &&
                AreMotorTerminalsConnected(motor, "W1", "V2", unionFind);

            if (info.IsStarPointConnected && info.IsDeltaConnectionDetected)
            {
                info.State = "StarDeltaConflict";
                info.StarDeltaConnectionMode = "Conflict";
                info.Judgement = "危险：星形连接与三角形连接同时存在，疑似星三角短接，不能作为正常运行状态。";
                info.MotorIssues.Add(info.Judgement);
                AddComponentError(info, result, info.Judgement);
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            var u1 = VoltageAt(info, "U1");
            var v1 = VoltageAt(info, "V1");
            var w1 = VoltageAt(info, "W1");
            if (u1 == VoltageConflict || v1 == VoltageConflict || w1 == VoltageConflict)
            {
                info.State = "Fault";
                info.StarDeltaConnectionMode = info.IsStarPointConnected ? "Star" :
                    info.IsDeltaConnectionDetected ? "Delta" : "Unknown";
                info.Judgement = "星三角电机输入端存在电压冲突或相间短路风险，不能启动。";
                info.MotorIssues.Add(info.Judgement);
                AddComponentError(info, result, info.Judgement);
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            var invalidTerminals = new List<string>();
            AddInvalidMotorPhase(invalidTerminals, "U1", u1);
            AddInvalidMotorPhase(invalidTerminals, "V1", v1);
            AddInvalidMotorPhase(invalidTerminals, "W1", w1);
            if (invalidTerminals.Count > 0)
            {
                info.State = "Stopped";
                info.StarDeltaConnectionMode = info.IsStarPointConnected ? "Star" :
                    info.IsDeltaConnectionDetected ? "Delta" : "Unknown";
                info.Judgement = "星三角电机未获得完整有效三相，当前不能正常启动。";
                info.MotorIssues.Add(string.Join("；", invalidTerminals.ToArray()) + "。");
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            var phases = new HashSet<string> { u1, v1, w1 };
            if (phases.Count != 3)
            {
                info.State = "Fault";
                info.StarDeltaConnectionMode = info.IsStarPointConnected ? "Star" :
                    info.IsDeltaConnectionDetected ? "Delta" : "Unknown";
                info.Judgement = "星三角电机 U1/V1/W1 存在重复相，三相输入异常，不能正常启动。";
                info.MotorIssues.Add(info.Judgement);
                AddComponentWarning(info, result, info.Judgement);
                AnalyzeMotorPe(motor, info, result);
                return;
            }

            if (info.IsStarPointConnected)
            {
                info.State = "StarConnected";
                info.StarDeltaConnectionMode = "Star";
                info.Judgement = "U2/V2/W2 已短接成星点，U1/V1/W1 获得完整三相，星形启动条件成立。";
            }
            else if (info.IsDeltaConnectionDetected)
            {
                info.State = "DeltaConnected";
                info.StarDeltaConnectionMode = "Delta";
                info.Judgement = "检测到标准 U1-W2、V1-U2、W1-V2 三角连接，三角运行条件成立。";
            }
            else
            {
                info.State = "Stopped";
                info.StarDeltaConnectionMode = "Unknown";
                info.Judgement = "U1/V1/W1 已获得完整三相，但未检测到完整星形或标准三角形绕组连接，当前不判定为可运行。";
                info.MotorIssues.Add(info.Judgement);
                AddComponentWarning(info, result, info.Judgement);
            }

            if (info.State == "StarConnected" || info.State == "DeltaConnected")
            {
                info.Infos.Add(IsForwardSequence(u1, v1, w1)
                    ? "U1/V1/W1 当前为标准正向相序 L1/L2/L3。"
                    : "检测到完整三相输入，但相序与标准正转不同，请结合后续星三角模板确认方向。");
            }

            AnalyzeMotorPe(motor, info, result);
        }

        private static bool AreMotorTerminalsConnected(
            CircuitComponent motor,
            string terminalA,
            string terminalB,
            TerminalUnionFind unionFind)
        {
            var a = motor.GetTerminal(terminalA);
            var b = motor.GetTerminal(terminalB);
            return a != null &&
                b != null &&
                unionFind.Find(TerminalKey(a)) == unionFind.Find(TerminalKey(b));
        }

        private bool IsMotorFedThroughContactorOutputs(
            CircuitComponent motor,
            IReadOnlyList<CircuitComponent> components,
            TerminalUnionFind unionFind,
            out string contactorNames)
        {
            contactorNames = string.Empty;
            if (motor == null || components == null || unionFind == null)
            {
                return false;
            }

            var motorTerminalIds = new[]
            {
                FindExistingTerminalId(motor, "U", "U1"),
                FindExistingTerminalId(motor, "V", "V1"),
                FindExistingTerminalId(motor, "W", "W1")
            };
            var matchedContactors = new HashSet<string>();

            for (var motorIndex = 0; motorIndex < motorTerminalIds.Length; motorIndex++)
            {
                var motorTerminal = motor.GetTerminal(motorTerminalIds[motorIndex]);
                var motorRoot = motorTerminal != null ? unionFind.Find(TerminalKey(motorTerminal)) : null;
                if (motorRoot == null)
                {
                    return false;
                }

                var matchedOutput = false;
                for (var componentIndex = 0; componentIndex < components.Count && !matchedOutput; componentIndex++)
                {
                    var component = components[componentIndex];
                    if (!IsContactorComponent(component))
                    {
                        continue;
                    }

                    var outputIds = new[] { "T1", "T2", "T3" };
                    for (var outputIndex = 0; outputIndex < outputIds.Length; outputIndex++)
                    {
                        var output = component.GetTerminal(outputIds[outputIndex]);
                        if (output == null || unionFind.Find(TerminalKey(output)) != motorRoot)
                        {
                            continue;
                        }

                        matchedOutput = true;
                        matchedContactors.Add(DisplayName(component));
                        break;
                    }
                }

                if (!matchedOutput)
                {
                    return false;
                }
            }

            contactorNames = matchedContactors.Count > 0
                ? string.Join("、", new List<string>(matchedContactors).ToArray())
                : "KM";
            return true;
        }

        private static void AddInvalidMotorPhase(List<string> issues, string terminalId, string voltage)
        {
            if (IsPhaseLabel(voltage))
            {
                return;
            }

            issues.Add(voltage == VoltageNone
                ? terminalId + " 端未获得三相电源标签，存在缺相"
                : terminalId + " 端获得 " + voltage + "，不是有效三相相线");
        }

        private static void AnalyzeMotorPe(
            CircuitComponent motor,
            ComponentStateInfo info,
            CircuitStateResult result)
        {
            var peId = FindExistingTerminalId(motor, "PE");
            if (peId == null)
            {
                info.PeStatus = "未设置 PE 端子";
                return;
            }

            var pe = VoltageAt(info, peId);
            if (pe == VoltagePE)
            {
                info.PeStatus = "PE 保护接地已连接";
            }
            else if (pe == VoltageNone)
            {
                info.PeStatus = "PE 保护接地未连接";
                AddComponentWarning(info, result, "电机 PE 保护接地未连接，存在安全隐患。");
            }
            else
            {
                info.PeStatus = "PE 端接入 " + pe;
                AddComponentError(info, result, "电机 PE 端接入了非 PE 标签 " + pe + "，存在严重安全风险。");
            }
        }

        private static void AnalyzeContactorCoil(
            CircuitComponent contactor,
            ComponentStateInfo info,
            CircuitStateResult result)
        {
            if (contactor.GetTerminal("A1") == null || contactor.GetTerminal("A2") == null)
            {
                info.State = "CoilUnknown";
                info.CoilStatus = "CoilUnknown";
                info.CoilVoltageDescription = "该接触器缺少 A1/A2 线圈端子，无法判断线圈得电。";
                AddComponentWarning(info, result, info.CoilVoltageDescription);
                return;
            }

            var a1 = VoltageAt(info, "A1");
            var a2 = VoltageAt(info, "A2");
            if (a1 == VoltageConflict || a2 == VoltageConflict)
            {
                info.State = "CoilFault";
                info.CoilStatus = "CoilFault";
                info.CoilVoltageDescription = "线圈端子存在电压冲突或短路风险，不能吸合。";
                AddComponentError(info, result, info.CoilVoltageDescription);
                return;
            }

            if (a1 == VoltagePE || a2 == VoltagePE)
            {
                info.State = "CoilFault";
                info.CoilStatus = "CoilFault";
                info.CoilVoltageDescription = "接触器线圈端子接入 PE，存在严重安全风险。";
                AddComponentError(info, result, info.CoilVoltageDescription);
                return;
            }

            if (IsValidCoilVoltage(a1, a2))
            {
                info.State = "CoilEnergized";
                info.CoilStatus = "CoilEnergized";
                info.IsContactorCoilEnergizedByAnalyzer = true;
                info.CoilVoltageDescription = "A1/A2 之间形成 " + a1 + "-" + a2 + " 有效控制电压。";
                info.Judgement = "接触器线圈得电，应吸合。";
                return;
            }

            info.State = "CoilOff";
            info.CoilStatus = "CoilOff";
            if (a1 == VoltageNone || a2 == VoltageNone)
            {
                info.CoilVoltageDescription = "当前启动按钮或控制路径未闭合，A1 或 A2 未获得有效电源标签。";
            }
            else if (a1 == a2)
            {
                info.CoilVoltageDescription = "A1/A2 同为 " + a1 + "，没有形成有效控制电压。";
            }
            else
            {
                info.CoilVoltageDescription = "A1/A2 未形成可识别的有效控制电压。";
            }
        }

        private static void AnalyzeTimerRelay(
            CircuitComponent timerRelay,
            ComponentStateInfo info,
            CircuitStateResult result)
        {
            info.TimerRelayType = info.IsOnDelayTimerRelay ? "OnDelay" : "OffDelay";
            if (info.IsOffDelayTimerRelay)
            {
                info.State = "TimerUnsupported";
                info.TimerDelayStatus = "Unsupported";
                info.TimerContactDescription = "当前 V1.6.1 暂未支持断电延时触点分析。";
                info.Judgement = "本阶段仅支持通电延时时间继电器。";
                AddComponentInfo(info, result, info.TimerContactDescription);
                return;
            }

            if (timerRelay.GetTerminal("A1") == null || timerRelay.GetTerminal("A2") == null)
            {
                info.State = "CoilUnknown";
                info.CoilStatus = "CoilUnknown";
                info.TimerDelayStatus = "Unknown";
                info.TimerContactDescription = "时间继电器缺少 A1/A2 线圈端子，无法判断线圈和延时触点状态。";
                info.IsTimerDelayedNcClosed = true;
                AddComponentWarning(info, result, info.TimerContactDescription);
                return;
            }

            if (timerRelay.GetTerminal("15") == null ||
                timerRelay.GetTerminal("16") == null ||
                timerRelay.GetTerminal("18") == null)
            {
                info.State = "CoilUnknown";
                info.CoilStatus = "CoilUnknown";
                info.TimerDelayStatus = "Unknown";
                info.TimerContactDescription = "通电延时时间继电器缺少 15/16/18 延时触点端子，无法完成触点分析。";
                AddComponentWarning(info, result, info.TimerContactDescription);
                return;
            }

            var a1 = VoltageAt(info, "A1");
            var a2 = VoltageAt(info, "A2");
            if (a1 == VoltageConflict || a2 == VoltageConflict)
            {
                info.State = "CoilFault";
                info.CoilStatus = "CoilFault";
                info.TimerDelayStatus = "Fault";
                info.TimerContactDescription = "KT 线圈端子存在电压冲突或短路风险，延时触点按复位状态处理。";
                info.IsTimerDelayedNcClosed = true;
                AddComponentError(info, result, info.TimerContactDescription);
                return;
            }

            if (a1 == VoltagePE || a2 == VoltagePE)
            {
                info.State = "CoilFault";
                info.CoilStatus = "CoilFault";
                info.TimerDelayStatus = "Fault";
                info.TimerContactDescription = "KT 线圈端子接入 PE，存在严重接线风险，延时触点按复位状态处理。";
                info.IsTimerDelayedNcClosed = true;
                AddComponentError(info, result, info.TimerContactDescription);
                return;
            }

            info.IsTimerRelayCoilEnergizedByAnalyzer = IsValidCoilVoltage(a1, a2);
            info.IsTimerDelayElapsed = info.IsTimerRelayCoilEnergizedByAnalyzer && timerRelay.IsClosed;
            info.IsTimerDelayedNoClosed = info.IsTimerDelayElapsed;
            info.IsTimerDelayedNcClosed = !info.IsTimerDelayedNoClosed;

            if (!info.IsTimerRelayCoilEnergizedByAnalyzer)
            {
                info.State = "CoilOff";
                info.CoilStatus = "CoilOff";
                info.TimerDelayStatus = "Reset";
                info.CoilVoltageDescription = CoilOffDescription(a1, a2);
                info.TimerContactDescription = "KT 线圈未得电，延时触点保持复位状态。";
                return;
            }

            info.State = "CoilEnergized";
            info.CoilStatus = "CoilEnergized";
            info.CoilVoltageDescription = "A1/A2 之间形成 " + a1 + "-" + a2 + " 有效控制电压。";
            if (info.IsTimerDelayElapsed)
            {
                info.TimerDelayStatus = "Elapsed";
                info.TimerContactDescription = "KT 线圈得电且延时到达，15/18 延时常开触点闭合。";
            }
            else
            {
                info.TimerDelayStatus = "Timing";
                info.TimerContactDescription = "KT 线圈已得电并处于计时中，延时触点保持复位状态。";
            }
        }

        private static string CoilOffDescription(string a1, string a2)
        {
            if (a1 == VoltageNone || a2 == VoltageNone)
            {
                return "A1 或 A2 未获得有效电源标签，线圈控制回路未闭合。";
            }

            if (a1 == a2)
            {
                return "A1/A2 同为 " + a1 + "，没有形成有效控制电压。";
            }

            return "A1/A2 未形成可识别的有效控制电压。";
        }

        private static bool IsValidCoilVoltage(string a1, string a2)
        {
            return IsThreePhaseLine(a1) && IsThreePhaseLine(a2) && a1 != a2 ||
                IsLineOrPhase(a1) && a2 == VoltageN ||
                IsLineOrPhase(a2) && a1 == VoltageN;
        }

        private static bool IsLineOrPhase(string voltage)
        {
            return voltage == VoltageL || IsThreePhaseLine(voltage);
        }

        private static bool IsThreePhaseLine(string voltage)
        {
            return voltage == VoltageL1 || voltage == VoltageL2 || voltage == VoltageL3;
        }

        private static bool IsForwardSequence(string u, string v, string w)
        {
            return MotorPhaseSequenceEvaluator.Evaluate(
                new[] { u },
                new[] { v },
                new[] { w }).Direction == MotorDirectionState.Forward;
        }

        private static bool IsReverseSequence(string u, string v, string w)
        {
            return MotorPhaseSequenceEvaluator.Evaluate(
                new[] { u },
                new[] { v },
                new[] { w }).Direction == MotorDirectionState.Reverse;
        }

        private static bool IsPhaseLabel(string label)
        {
            return label == VoltageL1 || label == VoltageL2 || label == VoltageL3;
        }

        private static string FindExistingTerminalId(CircuitComponent component, params string[] candidates)
        {
            if (component == null || candidates == null)
            {
                return null;
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                if (component.GetTerminal(candidates[i]) != null)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private bool AreMeterTerminalsWired(CircuitComponent component)
        {
            var terminalIds = new[] { "L_IN", "L_OUT", "N_IN", "N_OUT" };
            for (var i = 0; i < terminalIds.Length; i++)
            {
                var terminal = component.GetTerminal(terminalIds[i]);
                if (terminal == null || !wiredTerminalKeys.Contains(TerminalKey(terminal)))
                {
                    return false;
                }
            }

            return true;
        }

        private void AnalyzeBreaker(CircuitComponent component, ComponentStateInfo info, CircuitStateResult result)
        {
            var inputIds = new[] { "IN", "L_IN", "P_IN", "P1_IN", "P2_IN", "P3_IN", "P4_IN", "L1_IN", "L2_IN", "L3_IN", "N_IN" };
            var outputIds = new[] { "OUT", "L_OUT", "P_OUT", "P1_OUT", "P2_OUT", "P3_OUT", "P4_OUT", "L1_OUT", "L2_OUT", "L3_OUT", "N_OUT" };
            info.BreakerInputHasSupply = HasAnySupplyVoltage(info, inputIds);
            info.BreakerOutputHasSupply = HasAnySupplyVoltage(info, outputIds);
            info.BreakerAllInputsHaveSupply = AllExistingTerminalsHaveVoltage(component, info, inputIds);
            info.BreakerHasCompleteExternalConnections =
                AllExistingTerminalsAreWired(component, inputIds) &&
                AllExistingTerminalsAreWired(component, outputIds);

            if (!info.BreakerInputHasSupply)
            {
                AddComponentWarning(info, result, "空开输入端未获得电源标签，请检查空开进线。");
            }
            else if (!component.IsClosed)
            {
                AddComponentInfo(info, result, "空气开关当前断开，输入侧电源标签不会传播到输出侧。");
            }
        }

        private static bool HasAnySupplyVoltage(ComponentStateInfo info, string[] terminalIds)
        {
            return HasVoltage(info, terminalIds, VoltageL) ||
                HasVoltage(info, terminalIds, VoltageN) ||
                HasVoltage(info, terminalIds, VoltageL1) ||
                HasVoltage(info, terminalIds, VoltageL2) ||
                HasVoltage(info, terminalIds, VoltageL3) ||
                HasVoltage(info, terminalIds, VoltagePE);
        }

        private bool AllExistingTerminalsAreWired(CircuitComponent component, string[] terminalIds)
        {
            var found = false;
            for (var i = 0; i < terminalIds.Length; i++)
            {
                var terminal = component.GetTerminal(terminalIds[i]);
                if (terminal == null)
                {
                    continue;
                }

                found = true;
                if (!wiredTerminalKeys.Contains(TerminalKey(terminal)))
                {
                    return false;
                }
            }

            return found;
        }

        private static bool AllExistingTerminalsHaveVoltage(
            CircuitComponent component,
            ComponentStateInfo info,
            string[] terminalIds)
        {
            var found = false;
            for (var i = 0; i < terminalIds.Length; i++)
            {
                if (component.GetTerminal(terminalIds[i]) == null)
                {
                    continue;
                }

                found = true;
                if (!info.TerminalVoltages.TryGetValue(terminalIds[i], out var voltage) ||
                    voltage == VoltageNone ||
                    voltage == VoltageConflict)
                {
                    return false;
                }
            }

            return found;
        }

        private static bool HasVoltage(ComponentStateInfo info, string[] terminalIds, string voltage)
        {
            for (var i = 0; i < terminalIds.Length; i++)
            {
                if (info.TerminalVoltages.TryGetValue(terminalIds[i], out var value) && value == voltage)
                {
                    return true;
                }
            }

            return false;
        }

        private static void FinalizeBreakerControlAnalysis(CircuitStateResult result)
        {
            var hasHouseholdSwitch = false;
            var hasValidClosedBreaker = false;
            var hasProperlyConnectedBreaker = false;
            var hasBreakerWithSuppliedInputs = false;
            var hasOpenSuppliedBreaker = false;
            var hasRunningLoad = false;
            var hasStoppedLoadWithoutLine = false;

            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                hasHouseholdSwitch |= component.IsHouseholdSwitch;
                hasValidClosedBreaker |= component.IsBreaker && component.State == "Closed" &&
                    component.BreakerInputHasSupply && component.BreakerOutputHasSupply;
                hasProperlyConnectedBreaker |= component.IsBreaker &&
                    component.BreakerHasCompleteExternalConnections &&
                    component.BreakerInputHasSupply;
                hasBreakerWithSuppliedInputs |= component.IsBreaker &&
                    component.BreakerAllInputsHaveSupply;
                hasOpenSuppliedBreaker |= component.IsBreaker && component.State == "Open" &&
                    component.BreakerInputHasSupply;
                hasRunningLoad |= component.SummaryGroup == ComponentStateInfo.GroupLoad &&
                    (component.State == "On" || component.State == "Running");
                hasStoppedLoadWithoutLine |= component.SummaryGroup == ComponentStateInfo.GroupLoad &&
                    component.State != "On" && component.State != "Running" &&
                    !HasVoltage(component, new[] { "L", "N" }, VoltageL);
            }

            result.HasHouseholdControlSwitch = hasHouseholdSwitch;
            result.HasValidClosedBreakerControl = hasValidClosedBreaker && hasRunningLoad;
            result.HasProperlyConnectedBreaker = hasProperlyConnectedBreaker;
            result.HasBreakerWithSuppliedInputs = hasBreakerWithSuppliedInputs;
            if (result.HasValidClosedBreakerControl && !hasHouseholdSwitch)
            {
                AddUnique(result.Infos, "当前负载由空气开关直接控制，可以实现通断。若本题是空开控制照明电路，该接法是合理的。");
            }
            else if (hasOpenSuppliedBreaker && hasStoppedLoadWithoutLine)
            {
                AddUnique(result.Warnings, "空气开关当前断开，火线 L 无法传播到负载 L 端，因此负载未通电。");
            }
        }

        private void WriteActualTerminalState(
            TerminalView terminal,
            ComponentStateInfo info,
            TerminalUnionFind unionFind,
            Dictionary<string, HashSet<string>> rootLabels,
            Dictionary<string, List<string>> rootSources)
        {
            var terminalKey = TerminalKey(terminal);
            var root = unionFind.Find(terminalKey);
            var sources = ResolveSources(root, rootSources);
            var voltage = ResolveRootVoltage(root, rootLabels);
            info.TerminalVoltages[terminal.TerminalId] = ResolveVoltageFromSources(voltage, sources);
            info.VoltageSources[terminal.TerminalId] = sources;
        }

        private static string ResolveVoltageFromSources(string voltage, string sources)
        {
            if (voltage == VoltageConflict || voltage == VoltageNone || string.IsNullOrWhiteSpace(sources))
            {
                return voltage;
            }

            var sourceLabels = new HashSet<string>();
            var sourceParts = sources.Split(',');
            for (var i = 0; i < sourceParts.Length; i++)
            {
                var source = sourceParts[i].Trim();
                if (source.EndsWith(".L1", StringComparison.OrdinalIgnoreCase))
                {
                    sourceLabels.Add(VoltageL1);
                }
                else if (source.EndsWith(".L2", StringComparison.OrdinalIgnoreCase))
                {
                    sourceLabels.Add(VoltageL2);
                }
                else if (source.EndsWith(".L3", StringComparison.OrdinalIgnoreCase))
                {
                    sourceLabels.Add(VoltageL3);
                }
                else if (source.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
                {
                    sourceLabels.Add(VoltageL);
                }
                else if (source.EndsWith(".N", StringComparison.OrdinalIgnoreCase))
                {
                    sourceLabels.Add(VoltageN);
                }
                else if (source.EndsWith(".PE", StringComparison.OrdinalIgnoreCase))
                {
                    sourceLabels.Add(VoltagePE);
                }
            }

            if (sourceLabels.Count == 1)
            {
                foreach (var sourceLabel in sourceLabels)
                {
                    return sourceLabel;
                }
            }

            return sourceLabels.Count > 1 ? VoltageConflict : voltage;
        }

        private static void AnalyzeSinglePhaseLoad(
            CircuitComponent component,
            ComponentStateInfo info,
            CircuitStateResult result,
            bool isFan)
        {
            var line = VoltageAt(info, "L");
            var neutral = VoltageAt(info, "N");
            var runningState = isFan ? "Running" : "On";
            var stoppedState = isFan ? "Stopped" : "Off";
            var loadName = isFan ? "风扇" : "灯泡";

            if (component.GetTerminal("L") == null || component.GetTerminal("N") == null)
            {
                info.State = stoppedState;
                AddComponentError(info, result, loadName + "缺少 L 或 N 端子，无法判断供电状态。");
                return;
            }

            if (line == VoltageConflict || neutral == VoltageConflict)
            {
                info.State = stoppedState;
                info.Judgement = loadName + "所在回路存在电压冲突或短路风险。";
                AddComponentError(info, result, loadName + "所在回路存在电压冲突或短路风险。");
                return;
            }

            if (IsLineOrPhase(line) && neutral == VoltageN)
            {
                info.State = runningState;
                info.Judgement = loadName + "获得有效 " + line + "-N 单相电压，当前" + (isFan ? "运行" : "亮灯") + "。";
                return;
            }

            if (line == VoltageN && IsLineOrPhase(neutral))
            {
                // E4：普通交流灯泡无极性，L/N 端子互换不影响亮灯。仅对灯泡（!isFan）生效，
                // 风扇仍保留方向检查。不放宽二极管、直流器件或其他有极性器件规则。
                if (!isFan)
                {
                    info.State = runningState;
                    info.Judgement = loadName + "获得有效 L-N 单相电压，当前亮灯。";
                    return;
                }

                info.State = stoppedState;
                info.Judgement = loadName + "火线和零线接反，不符合规范接线要求，因此不作为正常运行处理。";
                AddComponentWarning(info, result, loadName + "火线和零线接反，不符合规范接线要求。");
                return;
            }

            info.State = stoppedState;
            var hasLine = IsLineOrPhase(line) || IsLineOrPhase(neutral);
            var hasNeutral = line == VoltageN || neutral == VoltageN;
            if (!hasLine)
            {
                AddComponentWarning(info, result, loadName + "未获得火线侧电源 L/L1/L2/L3。");
            }

            if (!hasNeutral)
            {
                AddComponentWarning(info, result, loadName + "缺少零线 N 回路。");
            }

            if (line != VoltageNone && line == neutral)
            {
                AddComponentWarning(info, result, loadName + "两端没有形成有效 L-N 电压差。");
            }

            if (!hasLine && !hasNeutral)
            {
                info.Judgement = loadName + "未获得完整 L-N 供电。";
            }
            else if (!hasLine)
            {
                info.Judgement = loadName + "缺少火线侧电源 L/L1/L2/L3。";
            }
            else if (!hasNeutral)
            {
                info.Judgement = loadName + "缺少零线 N 回路。";
            }
            else
            {
                info.Judgement = loadName + "两端没有形成有效 L-N 电压差。";
            }
        }

        private static void AnalyzeIndicatorLoad(
            CircuitComponent component,
            ComponentStateInfo info,
            CircuitStateResult result)
        {
            var firstTerminalId = component.GetTerminal("L") != null ? "L" : "A1";
            var secondTerminalId = component.GetTerminal("N") != null ? "N" : "A2";
            if (component.GetTerminal(firstTerminalId) == null || component.GetTerminal(secondTerminalId) == null)
            {
                info.State = "Off";
                AddComponentWarning(info, result, "指示灯缺少可识别的 L/N 或 A1/A2 端子，无法判断是否得电。");
                return;
            }

            var firstVoltage = VoltageAt(info, firstTerminalId);
            var secondVoltage = VoltageAt(info, secondTerminalId);
            var isHighVoltageIndicator = component.Definition != null && component.Definition.ratedVoltage >= 300f ||
                DefinitionContains(component, "380V", "380");

            if (firstVoltage == VoltageConflict || secondVoltage == VoltageConflict)
            {
                info.State = "Off";
                info.Judgement = "指示灯所在回路存在电压冲突或短路风险，当前不作为正常点亮处理。";
                AddComponentError(info, result, info.Judgement);
                return;
            }

            if (isHighVoltageIndicator)
            {
                if (IsThreePhaseLine(firstVoltage) && IsThreePhaseLine(secondVoltage) && firstVoltage != secondVoltage)
                {
                    info.State = "On";
                    info.Judgement = "380V 指示灯两端接入不同相线，已得电，当前点亮。";
                    return;
                }

                info.State = "Off";
                info.Judgement = "380V 指示灯需要接入两条不同相线；当前未形成有效线电压，指示灯熄灭。";
                AddComponentWarning(info, result, info.Judgement);
                return;
            }

            if (firstVoltage == VoltageL && secondVoltage == VoltageN ||
                firstVoltage == VoltageN && secondVoltage == VoltageL ||
                IsThreePhaseLine(firstVoltage) && secondVoltage == VoltageN ||
                IsThreePhaseLine(secondVoltage) && firstVoltage == VoltageN)
            {
                info.State = "On";
                info.Judgement = "220V 指示灯两端形成相线/零线电压，已得电，当前点亮。";
                return;
            }

            info.State = "Off";
            info.Judgement = "220V 指示灯未形成有效相线/零线电压，当前熄灭。";
            AddComponentWarning(info, result, info.Judgement);
        }

        private static void AddComponentWarning(ComponentStateInfo info, CircuitStateResult result, string message)
        {
            AddUnique(info.Warnings, message);
            AddUnique(result.Warnings, info.DisplayName + "：" + message);
        }

        private static void AddComponentError(ComponentStateInfo info, CircuitStateResult result, string message)
        {
            AddUnique(info.Errors, message);
            AddUnique(result.Errors, info.DisplayName + "：" + message);
        }

        private static void AddComponentInfo(ComponentStateInfo info, CircuitStateResult result, string message)
        {
            AddUnique(info.Infos, message);
            AddUnique(result.Infos, info.DisplayName + "：" + message);
        }

        private static string ResolveRootVoltage(string root, Dictionary<string, HashSet<string>> rootLabels)
        {
            if (root == null || !rootLabels.TryGetValue(root, out var labels) || labels.Count == 0)
            {
                return VoltageNone;
            }

            if (labels.Count > 1)
            {
                return VoltageConflict;
            }

            foreach (var label in labels)
            {
                return label;
            }

            return VoltageNone;
        }

        private string ResolveSources(string root, Dictionary<string, List<string>> rootSources)
        {
            if (root == null || !rootSources.TryGetValue(root, out var sources))
            {
                return string.Empty;
            }

            var readableSources = new List<string>();
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                var separator = source.LastIndexOf('.');
                var instanceId = separator > 0 ? source.Substring(0, separator) : source;
                var terminalId = separator > 0 ? source.Substring(separator + 1) : string.Empty;
                var readableName = friendlyNamesByInstanceId.TryGetValue(instanceId, out var value) ? value : ShortId(instanceId);
                readableSources.Add(string.IsNullOrWhiteSpace(terminalId) ? readableName : readableName + "." + terminalId);
            }

            return string.Join(", ", readableSources.ToArray());
        }

        private string FriendlyTerminalKey(string terminalKey)
        {
            if (string.IsNullOrWhiteSpace(terminalKey))
            {
                return "未知端子";
            }

            var separator = terminalKey.LastIndexOf('.');
            var instanceId = separator > 0 ? terminalKey.Substring(0, separator) : terminalKey;
            var terminalId = separator > 0 ? terminalKey.Substring(separator + 1) : string.Empty;
            var readableName = friendlyNamesByInstanceId.TryGetValue(instanceId, out var value) ? value : ShortId(instanceId);
            return string.IsNullOrWhiteSpace(terminalId) ? readableName : readableName + "." + terminalId;
        }

        private static string VoltageAt(ComponentStateInfo info, string terminalId)
        {
            return info.TerminalVoltages.TryGetValue(terminalId, out var value) ? value : VoltageNone;
        }

        private static bool IsHouseholdSwitch(CircuitComponent component)
        {
            return component.Definition.category == ComponentCategory.Household &&
                component.Definition.kind == ComponentKind.Switch &&
                !DefinitionContains(component, "TwoWay", "DoubleThrow", "双控");
        }

        private static bool IsBreaker(CircuitComponent component)
        {
            return DefinitionContains(component, "Breaker", "AirSwitch", "CircuitBreaker", "空开", "空气开关", "断路器");
        }

        private static bool IsEnergyMeter(CircuitComponent component)
        {
            return DefinitionContains(component, "EnergyMeter", "ElectricMeter", "电能表", "电表", "单相电能表");
        }

        private static bool IsThermalRelay(CircuitComponent component)
        {
            return component != null &&
                component.GetTerminal("95") != null &&
                component.GetTerminal("96") != null &&
                DefinitionContains(component, "ThermalRelay", "Thermal_Relay", "热继");
        }

        private static bool IsOnDelayTimerRelay(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                DefinitionContains(component, "Timer_OnDelay", "OnDelay", "通电延时");
        }

        private static bool IsOffDelayTimerRelay(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                DefinitionContains(component, "Timer_OffDelay", "OffDelay", "断电延时");
        }

        private static bool IsTimerRelayComponent(CircuitComponent component)
        {
            return IsOnDelayTimerRelay(component) || IsOffDelayTimerRelay(component);
        }

        private static bool IsContactorComponent(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                !IsTimerRelayComponent(component) &&
                (component.Definition.kind == ComponentKind.ContactorCoil ||
                    DefinitionContains(component, "Contactor", "KM", "接触器", "交流接触器"));
        }

        private static bool IsLimitSwitchComponent(CircuitComponent component)
        {
            if (component == null ||
                component.Definition == null ||
                component.GetTerminal("11") == null ||
                component.GetTerminal("12") == null ||
                component.GetTerminal("23") == null ||
                component.GetTerminal("24") == null)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return DefinitionContains(
                    component,
                    "LimitSwitch",
                    "TravelSwitch",
                    "PositionSwitch",
                    "Switch_Limit",
                    "行程开关",
                    "限位开关") ||
                definitionName.StartsWith("SQ", StringComparison.OrdinalIgnoreCase) ||
                definitionName.IndexOf("_SQ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCompoundPushButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                !IsLimitSwitchComponent(component) &&
                component.Definition.kind == ComponentKind.PushButton &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null;
        }

        private static bool IsSelfLockingButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null &&
                component.Definition.name.IndexOf("Button_SelfLock", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKnifeSwitch(CircuitComponent component)
        {
            return DefinitionContains(component, "KnifeSwitch", "Knife_Switch", "刀开关", "隔离开关", "QS");
        }

        private static bool IsLamp(CircuitComponent component)
        {
            return component.Definition.kind == ComponentKind.Lamp ||
                DefinitionContains(component, "Lamp", "Bulb", "Light", "灯泡", "照明");
        }

        private static bool IsFan(CircuitComponent component)
        {
            return component.Definition.kind == ComponentKind.Fan ||
                DefinitionContains(component, "Fan", "风扇", "电风扇");
        }

        private static bool IsIndicator(CircuitComponent component)
        {
            return component.Definition.kind == ComponentKind.Indicator ||
                DefinitionContains(component, "Indicator", "PilotLight", "指示灯");
        }

        private static bool IsEmergencyStop(CircuitComponent component)
        {
            return DefinitionContains(component, "EmergencyStop", "EStop", "急停");
        }

        private static bool IsThreePhaseMotorComponent(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var hasAnyThreePhaseTerminal =
                component.GetTerminal("U") != null || component.GetTerminal("U1") != null ||
                component.GetTerminal("V") != null || component.GetTerminal("V1") != null ||
                component.GetTerminal("W") != null || component.GetTerminal("W1") != null;
            return hasAnyThreePhaseTerminal &&
                DefinitionContains(component, "Motor_ThreePhase", "ThreePhaseMotor", "Three_Phase_Motor", "三相异步电动机", "三相电机", "电动机");
        }

        private static bool IsStarDeltaMotorComponent(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.Motor &&
                component.GetTerminal("U1") != null &&
                component.GetTerminal("V1") != null &&
                component.GetTerminal("W1") != null &&
                DefinitionContains(component, "Motor_StarDelta", "StarDelta", "Star_Delta", "星三角");
        }

        private static bool DefinitionContains(CircuitComponent component, params string[] keywords)
        {
            if (component == null || keywords == null)
            {
                return false;
            }

            var definitionName = component.Definition != null ? component.Definition.name : string.Empty;
            var displayName = component.Definition != null ? component.Definition.displayName : string.Empty;
            var combined = definitionName + " " + displayName;

            for (var i = 0; i < keywords.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(keywords[i]) &&
                    combined.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildLoadPathExplanations(
            IReadOnlyList<CircuitComponent> components,
            CircuitStateResult result)
        {
            if (components == null)
            {
                return;
            }

            var lineSources = new List<string>();
            var neutralSources = new List<string>();
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null ||
                    component.Definition.kind != ComponentKind.PowerSource ||
                    component.GetTerminal("L1") != null ||
                    component.Definition.sourcePhaseCount >= 3)
                {
                    continue;
                }

                AddTerminalKey(component.GetTerminal("L"), lineSources);
                AddTerminalKey(component.GetTerminal("N"), neutralSources);
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || (!IsLamp(component) && !IsFan(component)))
                {
                    continue;
                }

                var info = result.FindComponent(SafeInstanceId(component));
                if (info == null)
                {
                    continue;
                }

                var lineTerminal = component.GetTerminal("L");
                var neutralTerminal = component.GetTerminal("N");
                info.LinePathExplanation = BuildPathExplanation(lineSources, TerminalKey(lineTerminal), "火线 L", info.DisplayName + ".L", false);
                info.NeutralPathExplanation = BuildPathExplanation(neutralSources, TerminalKey(neutralTerminal), "零线 N", info.DisplayName + ".N", true);
            }
        }

        private string BuildPathExplanation(
            List<string> sourceKeys,
            string targetKey,
            string label,
            string targetName,
            bool reverseDisplay)
        {
            var path = FindShortestPath(sourceKeys, targetKey);
            if (path == null || path.Count == 0)
            {
                return label + "：未到达 " + targetName;
            }

            if (reverseDisplay)
            {
                path.Reverse();
            }

            return label + "：" + FormatPath(path);
        }

        private List<string> FindShortestPath(List<string> sourceKeys, string targetKey)
        {
            // 报告路径采用有界 BFS。复杂环路时宁可返回未找到并记录一次安全告警，
            // 也不能为生成教学说明无限遍历；预算调整必须运行 Topology Safety 测试。
            if (sourceKeys == null || string.IsNullOrWhiteSpace(targetKey))
            {
                return null;
            }

            var queue = new Queue<string>();
            var previous = new Dictionary<string, string>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            for (var i = 0; i < sourceKeys.Count; i++)
            {
                var source = sourceKeys[i];
                if (string.IsNullOrWhiteSpace(source) || previous.ContainsKey(source))
                {
                    continue;
                }

                previous.Add(source, null);
                queue.Enqueue(source);
            }

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, previous.Count, visitedEdges))
                {
                    LogTraversalBudgetExceeded("CircuitStateAnalyzer.FindShortestPath");
                    return null;
                }

                var current = queue.Dequeue();
                if (current == targetKey)
                {
                    var path = new List<string>();
                    while (current != null)
                    {
                        path.Add(current);
                        current = previous[current];
                    }

                    path.Reverse();
                    return path;
                }

                if (!connectionGraph.TryGetValue(current, out var neighbors))
                {
                    continue;
                }

                foreach (var neighbor in neighbors)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, previous.Count, visitedEdges))
                    {
                        LogTraversalBudgetExceeded("CircuitStateAnalyzer.FindShortestPath.edges");
                        return null;
                    }

                    if (previous.ContainsKey(neighbor))
                    {
                        continue;
                    }

                    previous.Add(neighbor, current);
                    queue.Enqueue(neighbor);
                }
            }

            return null;
        }

        private void LogTraversalBudgetExceeded(string context)
        {
            if (traversalBudgetWarningLogged)
            {
                return;
            }

            traversalBudgetWarningLogged = true;
            TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
        }

        private string FormatPath(List<string> terminalPath)
        {
            var parts = new List<string>();
            for (var i = 0; i < terminalPath.Count; i++)
            {
                var key = terminalPath[i];
                var separator = key.LastIndexOf('.');
                var instanceId = separator > 0 ? key.Substring(0, separator) : key;
                var isEndpoint = i == 0 || i == terminalPath.Count - 1;
                var text = isEndpoint ? FriendlyTerminalKey(key) :
                    friendlyNamesByInstanceId.TryGetValue(instanceId, out var name) ? name : ShortId(instanceId);
                if (parts.Count == 0 || parts[parts.Count - 1] != text)
                {
                    parts.Add(text);
                }
            }

            return string.Join(" → ", parts);
        }

        private static void AddTerminalKey(TerminalView terminal, List<string> keys)
        {
            if (terminal != null)
            {
                keys.Add(TerminalKey(terminal));
            }
        }

        private static string FindFirstTerminalPair(CircuitComponent component, string separator)
        {
            var pairs = new[]
            {
                new[] { "L", "L1" },
                new[] { "11", "12" },
                new[] { "IN", "OUT" },
                new[] { "L_IN", "L_OUT" },
                new[] { "P_IN", "P_OUT" },
                new[] { "P1_IN", "P1_OUT" }
            };

            for (var i = 0; i < pairs.Length; i++)
            {
                if (component.GetTerminal(pairs[i][0]) != null && component.GetTerminal(pairs[i][1]) != null)
                {
                    return pairs[i][0] + separator + pairs[i][1];
                }
            }

            return "输入端" + separator + "输出端";
        }

        private void BuildFriendlyNames(IReadOnlyList<CircuitComponent> components)
        {
            if (components == null)
            {
                return;
            }

            var totals = new Dictionary<string, int>();
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                var name = FriendlyDisplayName(component);
                totals[name] = totals.TryGetValue(name, out var count) ? count + 1 : 1;
            }

            var indexes = new Dictionary<string, int>();
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                var name = FriendlyDisplayName(component);
                indexes[name] = indexes.TryGetValue(name, out var index) ? index + 1 : 1;
                friendlyNamesByInstanceId[SafeInstanceId(component)] = totals[name] > 1 ? name + " #" + indexes[name] : name;
            }
        }

        private static string FriendlyDisplayName(CircuitComponent component)
        {
            var instanceId = SafeInstanceId(component);
            if (instanceId.Equals("km_main", StringComparison.OrdinalIgnoreCase))
            {
                return "主接触器 KM";
            }

            if (instanceId.Equals("km_star", StringComparison.OrdinalIgnoreCase))
            {
                return "星形接触器 KMY";
            }

            if (instanceId.Equals("km_delta", StringComparison.OrdinalIgnoreCase))
            {
                return "三角形接触器 KMD";
            }

            return DisplayName(component);
        }

        private static string ShortId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return "未知元件";
            }

            return instanceId.Length <= 8 ? instanceId : instanceId.Substring(0, 8);
        }

        private bool ConnectKnownPairs(CircuitComponent component, TerminalUnionFind unionFind, bool fallbackToFirstPair)
        {
            var connected = false;
            connected |= ConnectIfExists(component, "L", "L1", unionFind);
            connected |= ConnectIfExists(component, "11", "12", unionFind);
            connected |= ConnectIfExists(component, "IN", "OUT", unionFind);
            connected |= ConnectIfExists(component, "L_IN", "L_OUT", unionFind);
            connected |= ConnectIfExists(component, "N_IN", "N_OUT", unionFind);
            connected |= ConnectIfExists(component, "P_IN", "P_OUT", unionFind);
            connected |= ConnectIfExists(component, "P1_IN", "P1_OUT", unionFind);
            connected |= ConnectIfExists(component, "P2_IN", "P2_OUT", unionFind);
            connected |= ConnectIfExists(component, "P3_IN", "P3_OUT", unionFind);
            connected |= ConnectIfExists(component, "P4_IN", "P4_OUT", unionFind);
            connected |= ConnectIfExists(component, "L1_IN", "L1_OUT", unionFind);
            connected |= ConnectIfExists(component, "L2_IN", "L2_OUT", unionFind);
            connected |= ConnectIfExists(component, "L3_IN", "L3_OUT", unionFind);

            if (!connected && fallbackToFirstPair && component.Terminals != null && component.Terminals.Count == 2)
            {
                ConnectTerminalKeys(TerminalKey(component.Terminals[0]), TerminalKey(component.Terminals[1]), unionFind);
                connected = true;
            }

            return connected;
        }

        private bool ConnectIfExists(
            CircuitComponent component,
            string terminalA,
            string terminalB,
            TerminalUnionFind unionFind)
        {
            var a = component != null ? component.GetTerminal(terminalA) : null;
            var b = component != null ? component.GetTerminal(terminalB) : null;
            if (a == null || b == null)
            {
                return false;
            }

            ConnectTerminalKeys(TerminalKey(a), TerminalKey(b), unionFind);
            return true;
        }

        private void ConnectTerminalKeys(string a, string b, TerminalUnionFind unionFind)
        {
            unionFind.Union(a, b);
            AddGraphEdge(a, b);
        }

        private void AddGraphEdge(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return;
            }

            if (!connectionGraph.TryGetValue(a, out var from))
            {
                from = new HashSet<string>();
                connectionGraph.Add(a, from);
            }

            if (!connectionGraph.TryGetValue(b, out var to))
            {
                to = new HashSet<string>();
                connectionGraph.Add(b, to);
            }

            from.Add(b);
            to.Add(a);
        }

        private static string TerminalKey(TerminalView terminal)
        {
            return terminal == null
                ? string.Empty
                : SafeInstanceId(terminal.Owner) + "." + terminal.TerminalId;
        }

        private static string SafeInstanceId(CircuitComponent component)
        {
            if (component == null)
            {
                return "unknown";
            }

            return string.IsNullOrWhiteSpace(component.InstanceId)
                ? component.GetInstanceID().ToString()
                : component.InstanceId;
        }

        private static string DisplayName(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return "未知元件";
            }

            return string.IsNullOrWhiteSpace(component.Definition.displayName)
                ? component.Definition.name
                : component.Definition.displayName.Replace("\n", " ");
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list != null && !string.IsNullOrWhiteSpace(value) && !list.Contains(value))
            {
                list.Add(value);
            }
        }
    }

    public sealed class CircuitStateResult
    {
        public readonly List<ComponentStateInfo> Components = new List<ComponentStateInfo>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Infos = new List<string>();

        public bool HasShortCircuit;
        public bool HasPowerConflict;
        public bool HasContactorInterlockConflict;
        public bool HasCompoundButtonInterlockConflict;
        public bool HasLimitSwitches;
        public bool HasSimultaneouslyTriggeredLimitSwitches;
        public bool HasTimerRelays;
        public bool HasStarDeltaMotors;
        public bool ContainsUnsupportedThreePhaseCircuit;
        public bool HasThreePhaseCircuit;
        public bool HasHouseholdControlSwitch;
        public bool HasValidClosedBreakerControl;
        public bool HasProperlyConnectedBreaker;
        public bool HasBreakerWithSuppliedInputs;

        public ComponentStateInfo FindComponent(string instanceId)
        {
            return Components.Find(c => c.InstanceId == instanceId);
        }

        public string ToReadableText(bool includeDebug = false)
        {
            var builder = new StringBuilder();
            builder.AppendLine("【通用现象分析 V0.1.1】");

            if (HasThreePhaseCircuit)
            {
                return ToThreePhaseReadableText(includeDebug);
            }

            var loadCount = CountGroup(ComponentStateInfo.GroupLoad);
            var controlCount = CountGroup(ComponentStateInfo.GroupControl);
            var workingLoadCount = CountWorkingLoads();
            builder.AppendLine();
            builder.AppendLine("一、总体结论");
            builder.AppendLine(HasShortCircuit ? "- 检测到 L/N 短路风险。" : "- 未发现 L/N 短路。");
            builder.AppendLine("- 共识别 " + loadCount + " 个负载、" + controlCount + " 个控制/保护元件。");
            builder.AppendLine("- 当前有 " + workingLoadCount + " 个负载正常工作，" + (loadCount - workingLoadCount) + " 个负载未工作。");

            AppendComponentGroup(builder, "二、负载状态", ComponentStateInfo.GroupLoad);
            AppendComponentGroup(builder, "三、控制与保护元件", ComponentStateInfo.GroupControl);
            AppendComponentGroup(builder, "四、电源与其他元件", ComponentStateInfo.GroupOther);

            AppendMessages(builder, "五、错误", Errors);
            AppendMessages(builder, "六、警告", Warnings);
            AppendMessages(builder, "七、教学提示", Infos);
            if (HasLimitSwitches)
            {
                AppendLimitSwitchAnalysis(builder);
            }

            if (HasTimerRelays)
            {
                AppendTimerRelayAnalysis(builder);
            }

            if (includeDebug)
            {
                AppendDebugTerminalLabels(builder);
            }

            return builder.ToString().TrimEnd();
        }

        private string ToThreePhaseReadableText(bool includeDebug)
        {
            var builder = new StringBuilder();
            builder.AppendLine("【通用现象分析 V1.0：工业三相标签传播】");
            builder.AppendLine();
            builder.AppendLine("一、总体结论");
            builder.AppendLine(HasShortCircuit ? "- 检测到三相电源标签冲突或短路风险。" : "- 未发现三相相间短路。");
            builder.AppendLine("- 共分析 " + Components.Count + " 个元件的端子标签。");
            builder.AppendLine("- V1.0 当前仅进行三相电压标签传播，不判断接触器吸合、电机运行、自锁或互锁。");

            builder.AppendLine();
            builder.AppendLine("二、三相电源");
            var sourceCount = 0;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!HasThreePhaseSourceTerminals(component))
                {
                    continue;
                }

                sourceCount++;
                builder.AppendLine(sourceCount + ". " + component.DisplayName);
                AppendTerminal(builder, component, "L1");
                AppendTerminal(builder, component, "L2");
                AppendTerminal(builder, component, "L3");
                AppendTerminal(builder, component, "N");
                AppendTerminal(builder, component, "PE");
            }

            if (sourceCount == 0)
            {
                builder.AppendLine("- 未检测到可识别的三相电源端子。");
            }

            builder.AppendLine();
            builder.AppendLine("三、工业元件端子标签（不含三相电机，电机状态见 V1.1）");
            var itemIndex = 1;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (HasThreePhaseSourceTerminals(component) || component.IsThreePhaseMotor)
                {
                    continue;
                }

                builder.AppendLine(itemIndex + ". " + component.DisplayName);
                foreach (var terminal in component.TerminalVoltages)
                {
                    AppendTerminal(builder, component, terminal.Key);
                }

                itemIndex++;
            }

            if (itemIndex == 1)
            {
                builder.AppendLine("- 无其他工业元件。");
            }

            AppendMessages(builder, "四、错误", Errors);
            AppendMessages(builder, "五、警告", Warnings);
            builder.AppendLine();
            builder.AppendLine("六、阶段说明");
            builder.AppendLine("- 3P 空开和可切换熔断器按当前 ON/OFF 状态传播标签；不可切换熔断器按默认导通处理。");
            builder.AppendLine("- 热继电器传播 L1/T1、L2/T2、L3/T3 主回路标签，并按当前状态传播 95/96 控制触点。");
            builder.AppendLine("- 接触器线圈与主触点由 V1.2/V1.3 分阶段推导，不属于 V1.0 静态传播。");
            AppendContactorCoilAnalysis(builder);
            AppendContactorMainContactAnalysis(builder);
            AppendContactorAuxiliaryContactAnalysis(builder);
            if (HasLimitSwitches)
            {
                AppendLimitSwitchAnalysis(builder);
            }

            if (HasTimerRelays)
            {
                AppendTimerRelayAnalysis(builder);
            }

            if (HasStarDeltaMotors)
            {
                AppendStarDeltaMotorAnalysis(builder);
            }

            AppendThreePhaseMotorAnalysis(builder);

            if (includeDebug)
            {
                AppendDebugTerminalLabels(builder);
            }

            return builder.ToString().TrimEnd();
        }

        private void AppendContactorCoilAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.2：接触器线圈状态】");
            var index = 1;
            var energizedCount = 0;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsContactor)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                AppendTerminal(builder, component, "A1");
                AppendTerminal(builder, component, "A2");
                builder.AppendLine("   - 状态：" + ReadableState(component.CoilStatus));
                if (!string.IsNullOrWhiteSpace(component.CoilVoltageDescription))
                {
                    builder.AppendLine("   - 说明：" + component.CoilVoltageDescription);
                }

                if (!string.IsNullOrWhiteSpace(component.Judgement))
                {
                    builder.AppendLine("   - 判断：" + component.Judgement);
                }

                if (component.HasSelfHoldHistoryAmbiguity)
                {
                    builder.AppendLine("   - 静态分析说明：" + SelfHoldHistoryExplanation());
                }

                if (component.IsContactorCoilEnergizedByAnalyzer)
                {
                    energizedCount++;
                }

                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 未检测到具有 A1/A2 线圈端子的接触器。");
            }

            builder.AppendLine("- V1.2 当前只判断接触器 A1/A2 线圈是否获得有效控制电压。");
            builder.AppendLine("- V1.4 通过有限轮迭代动态处理 13/14 自锁与 21/22 互锁，线圈状态不读取 SimulationEngine 运行结果。");
            if (energizedCount > 1)
            {
                builder.AppendLine("- 提醒：检测到多个接触器线圈同时获得启动路径，请结合 V1.4 互锁状态检查。");
            }
        }

        private void AppendContactorMainContactAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.3：接触器主触点状态】");
            var index = 1;
            var closedCount = 0;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsContactor)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                builder.AppendLine("   - 线圈状态：" + ReadableState(component.CoilStatus));
                builder.AppendLine("   - 主触点状态：" + ReadableState(component.MainContactStatus));
                AppendMainContactPair(builder, component, "L1", "T1");
                AppendMainContactPair(builder, component, "L2", "T2");
                AppendMainContactPair(builder, component, "L3", "T3");
                if (!string.IsNullOrWhiteSpace(component.MainContactDescription))
                {
                    builder.AppendLine("   - 说明：" + component.MainContactDescription);
                }

                if (component.HasSelfHoldHistoryAmbiguity)
                {
                    builder.AppendLine("   - 静态分析说明：" + SelfHoldHistoryExplanation());
                }

                if (component.IsContactorMainContactsClosedByAnalyzer)
                {
                    closedCount++;
                }

                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 未检测到接触器主触点。");
            }

            builder.AppendLine("- V1.3 当前根据 V1.2 推导出的接触器线圈状态，动态传播 L1/T1、L2/T2、L3/T3 主触点。");
            builder.AppendLine("- V1.4 迭代中的主触点仍严格跟随分析器推导出的线圈状态。");
            if (closedCount > 1)
            {
                builder.AppendLine("- 提醒：检测到多个接触器主触点同时闭合，请检查 V1.4 互锁状态。");
            }
        }

        private void AppendContactorAuxiliaryContactAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.4：自锁与互锁状态】");
            var index = 1;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsContactor)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                builder.AppendLine("   - 13/14 自锁触点：" + component.SelfHoldStatus);
                builder.AppendLine("   - 21/22 互锁触点：" + component.InterlockStatus);
                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 未检测到接触器辅助触点。");
            }

            if (HasContactorInterlockConflict)
            {
                builder.AppendLine("- 检测到正反转方向同时启动或互锁状态无法稳定；该状态属于互锁冲突操作，V1.4 已阻止相关主触点作为正常闭合状态传播。");
                builder.AppendLine("- 历史运行说明：" + InterlockHistoryExplanation());
            }

            if (HasCompoundButtonInterlockConflict)
            {
                builder.AppendLine("- 检测到两个方向复合按钮同时按下；按钮联锁使两个接触器线圈均不能作为正常得电状态，13/14 均不闭合。");
                builder.AppendLine("- 历史运行说明：" + InterlockHistoryExplanation());
            }

            builder.AppendLine("- V1.4 从全部接触器未吸合状态开始有限轮迭代，最多 5 轮；不读取接触器或电机 RUN 状态。");
            builder.AppendLine("- 仅当停止回路闭合、没有方向互锁/按钮联锁冲突，且单接触器自锁结构仍存在历史歧义时，才提示静态快照无法确认此前是否进入保持状态。");
        }

        private void AppendLimitSwitchAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.5：SQ 行程开关状态】");
            var index = 1;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsLimitSwitch)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                builder.AppendLine("   - 当前状态：" + (component.IsLimitSwitchTriggered ? "已触发" : "未触发"));
                builder.AppendLine("   - 11/12 常闭触点：" + (component.IsLimitSwitchTriggered ? "断开" : "导通"));
                builder.AppendLine("   - 23/24 常开触点：" + (component.IsLimitSwitchTriggered ? "导通" : "断开"));
                if (!string.IsNullOrWhiteSpace(component.Judgement))
                {
                    builder.AppendLine("   - 说明：" + component.Judgement);
                }

                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 未检测到 SQ 行程开关。");
            }

            if (HasSimultaneouslyTriggeredLimitSwitches)
            {
                builder.AppendLine("- 异常：检测到两个或更多 SQ 同时触发，机械位置状态不合理；当前不能解释为正常自动往返状态。");
            }

            builder.AppendLine("- 自动往返基础分析仅根据 SQ 当前触发状态改变控制回路路径，不模拟机械连续运动，也不会自动改变 SQ 状态。");
        }

        private void AppendTimerRelayAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.6：时间继电器 KT 状态】");
            var index = 1;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsTimerRelay)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                builder.AppendLine("   - 类型：" + (component.IsOnDelayTimerRelay ? "通电延时" : "断电延时"));
                if (component.IsOffDelayTimerRelay)
                {
                    builder.AppendLine("   - 当前支持状态：V1.6.1 暂未支持断电延时触点分析");
                    builder.AppendLine("   - 说明：本阶段仅支持通电延时时间继电器。");
                    index++;
                    continue;
                }

                builder.AppendLine("   - 当前模式：真实通电延时运行态（Reset / Timing / Elapsed）");
                AppendTerminal(builder, component, "A1");
                AppendTerminal(builder, component, "A2");
                builder.AppendLine("   - 线圈状态：" +
                    (component.IsTimerRelayCoilEnergizedByAnalyzer ? "得电" : "未得电"));
                builder.AppendLine("   - 延时状态：" + TimerDelayStatusText(component.TimerDelayStatus));
                builder.AppendLine("   - 15/16 延时常闭触点：" +
                    (component.IsTimerDelayedNcClosed ? "导通" : "断开"));
                builder.AppendLine("   - 15/18 延时常开触点：" +
                    (component.IsTimerDelayedNoClosed ? "导通" : "断开"));
                if (!string.IsNullOrWhiteSpace(component.CoilVoltageDescription))
                {
                    builder.AppendLine("   - 线圈说明：" + component.CoilVoltageDescription);
                }

                if (!string.IsNullOrWhiteSpace(component.TimerContactDescription))
                {
                    builder.AppendLine("   - 说明：" + component.TimerContactDescription);
                }

                if (!component.IsTimerRelayCoilEnergizedByAnalyzer)
                {
                    builder.AppendLine("   - 操作说明：KT 线圈未得电时按 Reset 处理；15/18 不导通，15/16 导通。");
                }
                else if (component.IsTimerDelayElapsed)
                {
                    builder.AppendLine("   - 操作说明：运行态达到 Elapsed 时，15/18 导通，15/16 断开；实时计时请以画布 KT 状态为准。");
                }
                else
                {
                    builder.AppendLine("   - 操作说明：运行态处于 Timing 时，15/16 保持导通，15/18 暂不导通；实时计时请以画布 KT 状态为准。");
                }

                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 未检测到时间继电器 KT。");
            }

            builder.AppendLine("- V2.1.2 起，OnDelay KT 使用真实通电延时运行态：Reset / Timing / Elapsed。");
            builder.AppendLine("- 线圈得电后开始计时；Timing 时 15/16 导通、15/18 断开；Elapsed 时 15/16 断开、15/18 导通；失电后立即 Reset。");
            builder.AppendLine("- 检查面板主要解释当前接线结构；KT 的实时 ElapsedSeconds / DelaySeconds 请以画布 KT 运行态显示为准。");
        }

        private static string TimerDelayStatusText(string status)
        {
            switch (status)
            {
                case "Reset":
                    return "复位";
                case "Timing":
                    return "计时中";
                case "Elapsed":
                    return "延时到达";
                case "Fault":
                    return "故障，按复位状态处理";
                case "Unsupported":
                    return "暂未支持";
                default:
                    return "暂无法判断";
            }
        }

        private static void AppendMainContactPair(
            StringBuilder builder,
            ComponentStateInfo component,
            string inputTerminal,
            string outputTerminal)
        {
            if (!component.TerminalVoltages.ContainsKey(inputTerminal) ||
                !component.TerminalVoltages.ContainsKey(outputTerminal))
            {
                return;
            }

            builder.AppendLine(
                "   - " + inputTerminal + "/" + outputTerminal + "：" +
                (component.IsContactorMainContactsClosedByAnalyzer ? "导通" : "断开"));
        }

        private void AppendThreePhaseMotorAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.1：三相电机状态】");
            var index = 1;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsThreePhaseMotor || component.IsStarDeltaMotor)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                if (component.IsMotorDeferredByContactorOutput)
                {
                    builder.AppendLine("   - U/V/W：已接至接触器 " + component.MotorFeederContactorNames + " 的 T1/T2/T3 输出侧");
                    builder.AppendLine("   - 当前状态：V1.3 未发现已闭合的上级接触器主触点，因此 U/V/W 当前未获得三相标签；这不判定为电机接线缺相。");
                    AppendMotorTerminal(builder, component, "PE");
                }
                else
                {
                    AppendMotorTerminal(builder, component, "U", "U1");
                    AppendMotorTerminal(builder, component, "V", "V1");
                    AppendMotorTerminal(builder, component, "W", "W1");
                    AppendMotorTerminal(builder, component, "PE");
                }

                builder.AppendLine("   - 状态：" + ReadableState(component.State));
                if (!string.IsNullOrWhiteSpace(component.Judgement))
                {
                    builder.AppendLine("   - 说明：" + component.Judgement);
                }

                if (component.State == "Stopped" &&
                    component.IsMotorDeferredByContactorOutput &&
                    HasAnySelfHoldHistoryAmbiguity())
                {
                    builder.AppendLine("   - 静态分析说明：" + SelfHoldHistoryExplanation());
                }
                else if (component.State == "Stopped" &&
                    component.IsMotorDeferredByContactorOutput &&
                    (HasContactorInterlockConflict || HasCompoundButtonInterlockConflict))
                {
                    builder.AppendLine("   - 静态分析说明：" + InterlockHistoryExplanation());
                }

                for (var issueIndex = 0; issueIndex < component.MotorIssues.Count; issueIndex++)
                {
                    builder.AppendLine("   - 问题：" + component.MotorIssues[issueIndex]);
                }

                for (var infoIndex = 0; infoIndex < component.Infos.Count; infoIndex++)
                {
                    builder.AppendLine("   - 阶段说明：" + component.Infos[infoIndex]);
                }

                if (!string.IsNullOrWhiteSpace(component.PeStatus))
                {
                    builder.AppendLine("   - PE：" + component.PeStatus);
                }

                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 未检测到具有 U/V/W 端子的三相电机。");
            }

            builder.AppendLine("- V1.1 当前只判断已经获得三相标签的电机端子。");
            builder.AppendLine("- 电机经接触器供电时，V1.1 使用 V1.3 主触点传播后的最终标签判断方向；上级主触点未闭合时不把 U/V/W 的 None 判定为接线缺相。");
        }

        private bool HasAnySelfHoldHistoryAmbiguity()
        {
            for (var i = 0; i < Components.Count; i++)
            {
                if (Components[i].HasSelfHoldHistoryAmbiguity)
                {
                    return true;
                }
            }

            return false;
        }

        private static string SelfHoldHistoryExplanation()
        {
            return "检测到接触器 13/14 自锁结构。当前检查面板为静态拓扑分析，不读取画布 RUN 历史；若该接触器此前已经吸合，则可能通过自锁支路保持运行。请以冷启动状态或重置运行状态后再判断是否会自行启动。";
        }

        private static string InterlockHistoryExplanation()
        {
            return "检测到正反转控制冲突或互锁状态异常。当前静态分析不读取历史运行保持；若正转或反转接触器此前已经吸合，画布运行态可能继续保持。建议停止按钮 OFF 后复位，再重新测试。";
        }

        private void AppendStarDeltaMotorAnalysis(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【通用现象分析 V1.7：星三角电机状态】");
            var index = 1;
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (!component.IsStarDeltaMotor)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName);
                if (component.State == "StarDeltaConflict" && !string.IsNullOrWhiteSpace(component.Judgement))
                {
                    builder.AppendLine("   - 危险提示：" + component.Judgement);
                }

                AppendMotorTerminal(builder, component, "U1");
                AppendMotorTerminal(builder, component, "V1");
                AppendMotorTerminal(builder, component, "W1");
                AppendMotorTerminal(builder, component, "U2");
                AppendMotorTerminal(builder, component, "V2");
                AppendMotorTerminal(builder, component, "W2");
                AppendMotorTerminal(builder, component, "PE");
                builder.AppendLine("   - 星点 U2/V2/W2：" + (component.IsStarPointConnected ? "已短接" : "未形成完整星点"));
                builder.AppendLine("   - 标准三角连接：" + (component.IsDeltaConnectionDetected ? "已检测到" : "未检测到"));
                builder.AppendLine("   - 当前连接方式：" + StarDeltaConnectionText(component.StarDeltaConnectionMode));
                builder.AppendLine("   - 状态：" + ReadableState(component.State));
                if (component.State != "StarDeltaConflict" && !string.IsNullOrWhiteSpace(component.Judgement))
                {
                    builder.AppendLine("   - 说明：" + component.Judgement);
                }

                for (var issueIndex = 0; issueIndex < component.MotorIssues.Count; issueIndex++)
                {
                    builder.AppendLine("   - 问题：" + component.MotorIssues[issueIndex]);
                }

                for (var infoIndex = 0; infoIndex < component.Infos.Count; infoIndex++)
                {
                    builder.AppendLine("   - 相序说明：" + component.Infos[infoIndex]);
                }

                if (!string.IsNullOrWhiteSpace(component.PeStatus))
                {
                    builder.AppendLine("   - PE：" + component.PeStatus);
                }

                index++;
            }

            builder.AppendLine("- V1.7 当前识别六端子电机的星形、标准三角形及星三角同时存在风险；本阶段不计算启动电流、转速或转矩。");
        }

        private static string StarDeltaConnectionText(string mode)
        {
            switch (mode)
            {
                case "Star":
                    return "星形连接";
                case "Delta":
                    return "三角形连接";
                case "Conflict":
                    return "星三角冲突";
                case "Incomplete":
                    return "端子不完整";
                default:
                    return "未知 / 未形成完整连接";
            }
        }

        private static void AppendMotorTerminal(
            StringBuilder builder,
            ComponentStateInfo component,
            params string[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (!component.TerminalVoltages.ContainsKey(candidates[i]))
                {
                    continue;
                }

                AppendTerminal(builder, component, candidates[i]);
                return;
            }
        }

        private static bool HasThreePhaseSourceTerminals(ComponentStateInfo component)
        {
            return component != null &&
                component.TerminalVoltages.ContainsKey("L1") &&
                component.TerminalVoltages.ContainsKey("L2") &&
                component.TerminalVoltages.ContainsKey("L3") &&
                component.State == "Normal";
        }

        private int CountGroup(string group)
        {
            var count = 0;
            for (var i = 0; i < Components.Count; i++)
            {
                if (Components[i].SummaryGroup == group)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountWorkingLoads()
        {
            var count = 0;
            for (var i = 0; i < Components.Count; i++)
            {
                if (Components[i].SummaryGroup == ComponentStateInfo.GroupLoad &&
                    (Components[i].State == "On" || Components[i].State == "Running"))
                {
                    count++;
                }
            }

            return count;
        }

        private void AppendComponentGroup(StringBuilder builder, string title, string group)
        {
            var index = 1;
            builder.AppendLine();
            builder.AppendLine(title);
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                if (component.SummaryGroup != group)
                {
                    continue;
                }

                builder.AppendLine(index + ". " + component.DisplayName + "：" + ReadableState(component.State));
                if (!string.IsNullOrWhiteSpace(component.ConductionExplanation))
                {
                    builder.AppendLine("   - " + component.ConductionExplanation);
                }

                if (component.IsTwoWaySwitch)
                {
                    AppendTerminal(builder, component, "L");
                    AppendTerminal(builder, component, "L1");
                    AppendTerminal(builder, component, "L2");
                }
                else
                {
                    foreach (var terminal in component.TerminalVoltages)
                    {
                        AppendTerminal(builder, component, terminal.Key);
                    }
                }

                if (!string.IsNullOrWhiteSpace(component.Judgement))
                {
                    builder.AppendLine("   - 判断：" + component.Judgement);
                }

                if (component.SummaryGroup == ComponentStateInfo.GroupLoad)
                {
                    builder.AppendLine("   - 供电路径：");
                    builder.AppendLine("     " + component.LinePathExplanation);
                    builder.AppendLine("     " + component.NeutralPathExplanation);
                }

                index++;
            }

            if (index == 1)
            {
                builder.AppendLine("- 无");
            }
        }

        private static void AppendTerminal(StringBuilder builder, ComponentStateInfo component, string terminalId)
        {
            if (!component.TerminalVoltages.TryGetValue(terminalId, out var voltage))
            {
                return;
            }

            component.VoltageSources.TryGetValue(terminalId, out var source);
            var displayTerminalId = component.IsTwoWaySwitch && terminalId == "L"
                ? "COM"
                : component.IsBreaker
                    ? BreakerTerminalLabel(terminalId)
                    : terminalId;
            builder.Append("   - ").Append(displayTerminalId).Append("端：").Append(voltage);
            if (!string.IsNullOrWhiteSpace(source))
            {
                builder.Append("，来源：").Append(source);
            }

            builder.AppendLine();
        }

        private static string BreakerTerminalLabel(string terminalId)
        {
            if (terminalId == "IN" || terminalId.EndsWith("_IN", StringComparison.Ordinal))
            {
                return terminalId == "IN" ? "输入" : terminalId.Replace("_IN", " 输入");
            }

            if (terminalId == "OUT" || terminalId.EndsWith("_OUT", StringComparison.Ordinal))
            {
                return terminalId == "OUT" ? "输出" : terminalId.Replace("_OUT", " 输出");
            }

            return terminalId;
        }

        private static void AppendMessages(StringBuilder builder, string title, List<string> messages)
        {
            builder.AppendLine();
            builder.AppendLine(title);
            if (messages == null || messages.Count == 0)
            {
                builder.AppendLine("- 无");
                return;
            }

            for (var i = 0; i < messages.Count; i++)
            {
                builder.AppendLine("- " + messages[i]);
            }
        }

        private void AppendDebugTerminalLabels(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("【端子标签调试】");
            for (var i = 0; i < Components.Count; i++)
            {
                var component = Components[i];
                foreach (var terminal in component.TerminalVoltages)
                {
                    builder.AppendLine(component.DisplayName + "." + terminal.Key + " = " + terminal.Value);
                }
            }
        }

        private static string ReadableState(string state)
        {
            switch (state)
            {
                case "On":
                    return "亮";
                case "Off":
                    return "不亮";
                case "Running":
                    return "运行";
                case "Stopped":
                    return "停止";
                case "Closed":
                    return "闭合";
                case "Open":
                    return "断开";
                case "Normal":
                    return "正常";
                case "Conducting":
                    return "导通";
                case "Incomplete":
                    return "接线不完整";
                case "TwoWayL1":
                    return "COM→L1 导通";
                case "TwoWayL2":
                    return "COM→L2 导通";
                case "Forward":
                    return "正转";
                case "Reverse":
                    return "反转";
                case "Fault":
                    return "故障 / 不能运行";
                case "Unknown":
                    return "暂无法判断";
                case "CoilEnergized":
                    return "线圈得电，应吸合";
                case "CoilOff":
                    return "线圈未得电";
                case "CoilFault":
                    return "线圈故障 / 接线风险";
                case "CoilUnknown":
                    return "线圈状态暂无法判断";
                case "InterlockConflict":
                    return "互锁冲突 / 操作冲突";
                case "TimerUnsupported":
                    return "暂未支持断电延时分析";
                case "StarConnected":
                    return "星形启动条件成立";
                case "DeltaConnected":
                    return "三角运行条件成立";
                case "StarDeltaConflict":
                    return "危险：星形与三角形连接同时存在";
                default:
                    return state;
            }
        }
    }

    public sealed class ComponentStateInfo
    {
        public const string GroupLoad = "Load";
        public const string GroupControl = "Control";
        public const string GroupOther = "Other";

        public string InstanceId;
        public string DefinitionName;
        public string DisplayName;
        public string State;
        public string SummaryGroup;
        public string Judgement;
        public string ConductionExplanation;
        public string LinePathExplanation;
        public string NeutralPathExplanation;
        public string PeStatus;
        public bool IsTwoWaySwitch;
        public bool IsHouseholdSwitch;
        public bool IsBreaker;
        public bool IsThreePhaseMotor;
        public bool IsStarDeltaMotor;
        public bool IsStarPointConnected;
        public bool IsDeltaConnectionDetected;
        public string StarDeltaConnectionMode;
        public bool IsContactor;
        public bool IsLimitSwitch;
        public bool IsLimitSwitchTriggered;
        public string LimitSwitchContactDescription;
        public bool IsTimerRelay;
        public bool IsOnDelayTimerRelay;
        public bool IsOffDelayTimerRelay;
        public bool IsTimerRelayCoilEnergizedByAnalyzer;
        public bool IsTimerDelayElapsed;
        public bool IsTimerDelayedNoClosed;
        public bool IsTimerDelayedNcClosed;
        public string TimerRelayType;
        public string TimerDelayStatus;
        public string TimerContactDescription;
        public bool IsContactorCoilEnergizedByAnalyzer;
        public string CoilStatus;
        public string CoilVoltageDescription;
        public bool IsContactorMainContactsClosedByAnalyzer;
        public string MainContactStatus;
        public string MainContactDescription;
        public bool HasSelfHoldStructure;
        public bool HasInterlockStructure;
        public bool IsSelfHoldCutByStopOrControlOpen;
        public bool IsInactiveDirectionInForwardReversePair;
        public bool IsCompoundButtonInterlockConflict;
        public bool HasSelfHoldHistoryAmbiguity;
        public string SelfHoldStatus;
        public string InterlockStatus;
        public bool IsMotorDeferredByContactorOutput;
        public string MotorFeederContactorNames;
        public bool BreakerInputHasSupply;
        public bool BreakerOutputHasSupply;
        public bool BreakerHasCompleteExternalConnections;
        public bool BreakerAllInputsHaveSupply;

        public readonly Dictionary<string, string> TerminalVoltages = new Dictionary<string, string>();
        public readonly Dictionary<string, string> VoltageSources = new Dictionary<string, string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Infos = new List<string>();
        public readonly List<string> MotorIssues = new List<string>();
    }

    internal sealed class TerminalUnionFind
    {
        private readonly Dictionary<string, string> parents = new Dictionary<string, string>();
        private readonly Dictionary<string, int> ranks = new Dictionary<string, int>();

        public void Add(string item)
        {
            if (string.IsNullOrWhiteSpace(item) || parents.ContainsKey(item))
            {
                return;
            }

            parents.Add(item, item);
            ranks.Add(item, 0);
        }

        public string Find(string item)
        {
            if (string.IsNullOrWhiteSpace(item) || !parents.ContainsKey(item))
            {
                return null;
            }

            var parent = parents[item];
            if (parent != item)
            {
                parents[item] = Find(parent);
            }

            return parents[item];
        }

        public void Union(string a, string b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == null || rootB == null || rootA == rootB)
            {
                return;
            }

            var rankA = ranks[rootA];
            var rankB = ranks[rootB];
            if (rankA < rankB)
            {
                parents[rootA] = rootB;
            }
            else if (rankA > rankB)
            {
                parents[rootB] = rootA;
            }
            else
            {
                parents[rootB] = rootA;
                ranks[rootA] = rankA + 1;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Core;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 面向当前工业教学模板范围的专项识别与检查器。
    /// 基于 Workspace 的活动元件、活动导线和端子连线整理工业控制事实，并输出 CircuitAnalysisResult 的电路类型、问题计数和教学提示；
    /// 不等同于通用 <c>CircuitValidationService</c>，也不负责 CircuitStateAnalyzer 的运行态推导、模板加载或检查报告排版。
    /// 仅覆盖当前已支持的三相电机、正反转、热继保护、自锁等教学控制范围，不应推断为可识别任意工业系统。
    /// 修改后必须回归 18 张模板，尤其是正反转、自动往返、两电机顺序启动和星三角相关场景。
    /// </summary>
    public static class IndustrialCircuitRuleAnalyzer
    {
        public static bool TryAnalyze(WorkspaceController workspace, out CircuitAnalysisResult result)
        {
            // 先统一提取事实，再按公共主回路、控制回路和教学提示的既有顺序分析；这些结果会被工作流与运行态报告共同使用。
            var facts = BuildFacts(workspace);
            result = new CircuitAnalysisResult
            {
                IsIndustrial = facts.IsIndustrial,
                CircuitType = facts.CircuitType
            };

            if (!facts.IsIndustrial)
            {
                return false;
            }

            AnalyzeCommonIndustrialRules(facts, result);
            AnalyzeControlRules(facts, result);
            AddTeachingTips(facts, result);
            return true;
        }

        internal static IndustrialCircuitFacts BuildFacts(WorkspaceController workspace)
        {
            var facts = new IndustrialCircuitFacts { Workspace = workspace };
            if (workspace == null)
            {
                facts.CircuitType = "工业控制电路";
                return facts;
            }

            // 只能读取活动工作区集合，不能扫描 Demo.unity 的全部对象，否则历史场景对象会污染工业电路识别结果。
            facts.Components = workspace.Components != null ? workspace.Components.Where(c => c != null && c.Definition != null).ToList() : new List<CircuitComponent>();
            facts.Wires = workspace.WireManager != null && workspace.WireManager.Wires != null ? workspace.WireManager.Wires.Where(w => w != null).ToList() : new List<WireView>();

            facts.PowerSources = facts.Components.Where(IsThreePhasePower).ToList();
            facts.Motors = facts.Components.Where(IsThreePhaseMotor).ToList();
            facts.Contactors = facts.Components.Where(IsContactor).ToList();
            facts.ThermalRelays = facts.Components.Where(IsThermalRelay).ToList();
            facts.Breakers = facts.Components.Where(IsThreePoleBreaker).ToList();
            facts.Fuses = facts.Components.Where(IsThreePoleFuse).ToList();
            facts.StartButtons = facts.Components.Where(IsStartButton).ToList();
            facts.StopButtons = facts.Components.Where(IsStopButton).ToList();
            facts.CompoundButtons = facts.Components.Where(IsCompoundPushButton).ToList();

            // B3: 复用 CircuitStateAnalyzer 的结构事实，不在此层重建第二套端子编号简化识别器。
            // Analyze 只调用一次，后续 HasSelfHold 和 HasMutualInterlock 均复用该结果。
            var stateResult = new CircuitStateAnalyzer().Analyze(facts.Components, facts.Wires);

            facts.HasSelfHold = facts.Contactors.Any(c =>
            {
                var info = stateResult.FindComponent(c.InstanceId);
                return info != null && info.HasSelfHoldStructure;
            });
            facts.HasThermalControlContact = facts.ThermalRelays.Any(r => HasTerminalWire(facts, r, "95") || HasTerminalWire(facts, r, "96"));
            facts.IsForwardReverseControl = HasForwardReverseRoles(facts.Components);
            // B3: 互锁泛化。严格限定双 KM 场景（Count == 2），此时 HasInterlockStructure
            // 中的 "other contactor" 只能是对方 KM，可安全复用 B2 结构事实。
            // 3+ KM 场景因 target identity 不确定而返回 false。
            facts.HasMutualInterlock = facts.IsForwardReverseControl &&
                facts.Contactors.Count == 2 &&
                HasMutualInterlockByStructure(stateResult, facts.Contactors[0], facts.Contactors[1]);
            facts.HasButtonInterlock = facts.CompoundButtons.Count >= 2 && HasCompoundButtonInterlockWiring(facts);
            facts.IsIndustrial = facts.PowerSources.Count > 0 || facts.Motors.Count > 0 || facts.Contactors.Count > 0 || facts.ThermalRelays.Count > 0;
            facts.CircuitType = ResolveCircuitType(facts);
            return facts;
        }

        private static void AnalyzeCommonIndustrialRules(IndustrialCircuitFacts facts, CircuitAnalysisResult result)
        {
            if (facts.PowerSources.Count == 0)
            {
                result.Errors.Add("未检测到三相交流电源，请确认工业主回路是否有 L1/L2/L3 电源。 ");
            }

            if (facts.Motors.Count == 0 && facts.Contactors.Count > 0)
            {
                result.Warnings.Add("当前有工业控制元件，但未检测到三相异步电动机负载。 ");
            }

            if (facts.Motors.Count > 0 && facts.Breakers.Count == 0)
            {
                result.Warnings.Add("主回路未检测到 3P 空气开关。教学接线中建议三相电源先经过 3P 空开再进入后级。 ");
            }

            if (facts.Motors.Count > 0 && facts.Fuses.Count == 0)
            {
                result.Warnings.Add("主回路未检测到 3P 熔断器。教学接线中建议空开后串接熔断器，再进入接触器主触点。 ");
            }

            foreach (var motor in facts.Motors)
            {
                var missing = new List<string>();
                var isStarDeltaMotor = motor.GetTerminal("U1") != null;
                var first = isStarDeltaMotor ? "U1" : "U";
                var second = isStarDeltaMotor ? "V1" : "V";
                var third = isStarDeltaMotor ? "W1" : "W";
                if (!HasTerminalWire(facts, motor, first)) missing.Add(first);
                if (!HasTerminalWire(facts, motor, second)) missing.Add(second);
                if (!HasTerminalWire(facts, motor, third)) missing.Add(third);
                if (missing.Count > 0)
                {
                    result.Errors.Add("三相异步电动机 " + DisplayName(motor) + " 的 " + string.Join("/", missing) + " 端子未接入主回路。 ");
                }

                if (motor.GetTerminal("PE") != null && !HasTerminalWire(facts, motor, "PE"))
                {
                    result.Warnings.Add("三相异步电动机 " + DisplayName(motor) + " 的 PE 保护接地未连接。 ");
                }
            }
        }

        private static void AnalyzeControlRules(IndustrialCircuitFacts facts, CircuitAnalysisResult result)
        {
            foreach (var contactor in facts.Contactors)
            {
                var hasA1 = HasTerminalWire(facts, contactor, "A1");
                var hasA2 = HasTerminalWire(facts, contactor, "A2");
                if (!hasA1 || !hasA2)
                {
                    result.Errors.Add(DisplayName(contactor) + " 的线圈 A1/A2 接线不完整，请检查控制回路电源、按钮和返回相线。 ");
                }
            }

            if (facts.IsForwardReverseControl && facts.Contactors.Count >= 2)
            {
                if (!facts.HasMutualInterlock)
                {
                    result.Warnings.Add("正反转控制回路未检测到完整的电气互锁。两个接触器线圈支路应分别串入对方的辅助常闭触点，防止同时吸合。 ");
                }

                if (facts.StartButtons.Count(b => b.IsClosed) >= 2)
                {
                    result.Warnings.Add("检测到正转和反转启动按钮同时闭合。当前系统按正转优先处理，但实际操作中不应同时按下两个方向按钮，请先停止后再切换方向。 ");
                }
            }

            if (facts.Contactors.Count > 0 && facts.StopButtons.Count == 0)
            {
                result.Warnings.Add("控制回路未检测到停止按钮 NC。工业控制中停止按钮通常应串在控制回路前级。 ");
            }

            if (facts.ThermalRelays.Count > 0 && !facts.HasThermalControlContact)
            {
                result.Warnings.Add("检测到热继电器，但未检测到 95/96 常闭触点接入控制回路，过载保护可能不能释放接触器。 ");
            }
        }

        private static void AddTeachingTips(IndustrialCircuitFacts facts, CircuitAnalysisResult result)
        {
            if (facts.IsForwardReverseControl && facts.Contactors.Count >= 2)
            {
                result.TeachingTips.Add("正反转电路应把主回路和控制回路分开理解：主回路决定电机相序，控制回路决定哪个接触器吸合。 ");
                result.TeachingTips.Add("接触器辅助常闭触点用于电气互锁，一侧接触器吸合后应切断另一侧线圈回路，防止两个方向同时吸合。 ");
            }
            else if (facts.Contactors.Count == 1 && facts.HasSelfHold)
            {
                result.TeachingTips.Add("接触器辅助常开触点通常并联启动按钮形成自锁回路。 ");
            }
            else if (facts.Contactors.Count == 1)
            {
                result.TeachingTips.Add("点动类控制中，启动按钮松开后线圈应失电，接触器释放，电机停止。 ");
            }

            if (facts.ThermalRelays.Count > 0)
            {
                result.TeachingTips.Add("热继电器主触点用于经过电机电流，95/96 常闭触点应串入控制回路用于跳闸停机。 ");
            }
        }

        private static string ResolveCircuitType(IndustrialCircuitFacts facts)
        {
            // 这里依据已收集的元件和接线证据给出当前支持范围内的类型；工作流层对模板名称的显示覆盖不属于本类职责。
            if (facts.IsForwardReverseControl && facts.Contactors.Count >= 2 && facts.Motors.Count > 0)
            {
                return facts.HasMutualInterlock ? "电气互锁正反转控制电路" : "电动机正反转控制电路";
            }

            if (facts.Contactors.Count == 1 && facts.ThermalRelays.Count > 0)
            {
                return "热继电器保护电动机控制电路";
            }

            if (facts.Contactors.Count == 1 && facts.CompoundButtons.Count > 0 && facts.StartButtons.Count > 0 && facts.HasSelfHold)
            {
                return "点动与连续运行混合控制电路";
            }

            if (facts.Contactors.Count == 1 && facts.HasSelfHold)
            {
                return "电动机连续运行控制电路";
            }

            if (facts.Contactors.Count == 1 && facts.StartButtons.Count > 0)
            {
                return "电动机点动控制电路";
            }

            return "工业控制电路";
        }

        internal static bool HasMutualInterlockWiring(IndustrialCircuitFacts facts, CircuitComponent first, CircuitComponent second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return HasNcAuxiliaryInCoilBranch(facts, first, second) && HasNcAuxiliaryInCoilBranch(facts, second, first);
        }

        /// <summary>
        /// B3: 基于 CircuitStateAnalyzer 结构事实判断双 KM 互锁。
        /// 仅在严格双 KM 场景下使用，此时 HasInterlockStructure 的 "other contactor" 唯一确定。
        /// </summary>
        private static bool HasMutualInterlockByStructure(
            CircuitStateResult stateResult,
            CircuitComponent first,
            CircuitComponent second)
        {
            if (stateResult == null || first == null || second == null)
            {
                return false;
            }

            var firstInfo = stateResult.FindComponent(first.InstanceId);
            var secondInfo = stateResult.FindComponent(second.InstanceId);
            return firstInfo != null && firstInfo.HasInterlockStructure &&
                   secondInfo != null && secondInfo.HasInterlockStructure;
        }

        internal static bool HasCompoundButtonInterlockWiring(IndustrialCircuitFacts facts)
        {
            if (facts == null || facts.CompoundButtons == null || facts.CompoundButtons.Count < 2)
            {
                return false;
            }

            return facts.CompoundButtons.Take(2).All(button =>
                HasTerminalWire(facts, button, "11") &&
                HasTerminalWire(facts, button, "12") &&
                HasTerminalWire(facts, button, "23") &&
                HasTerminalWire(facts, button, "24"));
        }

        private static bool HasNcAuxiliaryInCoilBranch(IndustrialCircuitFacts facts, CircuitComponent auxiliaryOwner, CircuitComponent coilOwner)
        {
            return HasDirectTerminalConnection(facts, auxiliaryOwner, "21", coilOwner, "A1") ||
                HasDirectTerminalConnection(facts, auxiliaryOwner, "22", coilOwner, "A1") ||
                HasDirectTerminalConnection(facts, auxiliaryOwner, "21", coilOwner, "A2") ||
                HasDirectTerminalConnection(facts, auxiliaryOwner, "22", coilOwner, "A2") ||
                HasTerminalWire(facts, auxiliaryOwner, "21") && HasTerminalWire(facts, auxiliaryOwner, "22") && HasTerminalWire(facts, coilOwner, "A1");
        }

        private static bool HasDirectTerminalConnection(IndustrialCircuitFacts facts, CircuitComponent first, string firstTerminal, CircuitComponent second, string secondTerminal)
        {
            var a = first != null ? first.GetTerminal(firstTerminal) : null;
            var b = second != null ? second.GetTerminal(secondTerminal) : null;
            if (a == null || b == null)
            {
                return false;
            }

            return facts.Wires.Any(w => w != null &&
                (w.StartTerminal == a && w.EndTerminal == b || w.StartTerminal == b && w.EndTerminal == a));
        }

        internal static bool HasTerminalWire(IndustrialCircuitFacts facts, CircuitComponent component, string terminalId)
        {
            var terminal = component != null ? component.GetTerminal(terminalId) : null;
            return terminal != null && facts.Wires.Any(w => w != null && w.Uses(terminal));
        }

        internal static string DisplayName(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return "未知元件";
            }

            return string.IsNullOrWhiteSpace(component.Definition.displayName) ? component.Definition.name : component.Definition.displayName;
        }

        internal static string MotorStateText(CircuitComponent motor)
        {
            if (motor == null || !motor.IsEnergized)
            {
                return "停止";
            }

            var direction = 0f;
            var parameter = motor.GetParameter("rotationDirection");
            if (parameter != null)
            {
                direction = parameter.value;
            }

            if (direction > 0.5f)
            {
                return "正转";
            }

            if (direction < -0.5f)
            {
                return "反转";
            }

            return "运行";
        }

        internal static bool TryGetParameterValue(CircuitComponent component, string key, out float value)
        {
            value = 0f;
            var parameter = component != null ? component.GetParameter(key) : null;
            if (parameter == null)
            {
                return false;
            }

            value = parameter.value;
            return true;
        }

        private static bool IsThreePhasePower(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                component.Definition.kind == ComponentKind.PowerSource &&
                component.GetTerminal("L1") != null && component.GetTerminal("L2") != null && component.GetTerminal("L3") != null;
        }

        private static bool IsThreePhaseMotor(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                component.Definition.kind == ComponentKind.Motor &&
                (component.GetTerminal("U") != null && component.GetTerminal("V") != null && component.GetTerminal("W") != null ||
                 component.GetTerminal("U1") != null && component.GetTerminal("V1") != null && component.GetTerminal("W1") != null);
        }

        private static bool HasForwardReverseRoles(List<CircuitComponent> components)
        {
            var hasForward = false;
            var hasReverse = false;
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                var identity = component.InstanceId + " " + component.Definition.name + " " + DisplayName(component);
                hasForward |= ContainsRole(identity, "forward", "正转", "km_f");
                hasReverse |= ContainsRole(identity, "reverse", "反转", "km_r");
            }

            return hasForward && hasReverse;
        }

        private static bool ContainsRole(string identity, string english, string chinese, string alias)
        {
            return identity.IndexOf(english, StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.Contains(chinese) ||
                identity.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsContactor(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                (component.Definition.kind == ComponentKind.ContactorCoil ||
                 component.GetTerminal("A1") != null && component.GetTerminal("A2") != null && component.GetTerminal("L1") != null && component.GetTerminal("T1") != null);
        }

        private static bool IsThermalRelay(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                (component.Definition.name.IndexOf("ThermalRelay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 DisplayName(component).Contains("热继") ||
                 component.GetTerminal("95") != null && component.GetTerminal("96") != null);
        }

        private static bool IsThreePoleBreaker(CircuitComponent component)
        {
            return component != null && component.Definition != null && component.Definition.kind == ComponentKind.Breaker &&
                component.GetTerminal("P1_IN") != null && component.GetTerminal("P2_IN") != null && component.GetTerminal("P3_IN") != null;
        }

        private static bool IsThreePoleFuse(CircuitComponent component)
        {
            return component != null && component.Definition != null && component.Definition.kind == ComponentKind.Fuse &&
                component.GetTerminal("L1_IN") != null && component.GetTerminal("L2_IN") != null && component.GetTerminal("L3_IN") != null;
        }

        private static bool IsStartButton(CircuitComponent component)
        {
            if (component == null || component.Definition == null || component.Definition.kind != ComponentKind.PushButton)
            {
                return false;
            }

            if (component.GetTerminal("23") == null || component.GetTerminal("24") == null || IsCompoundPushButton(component))
            {
                return false;
            }

            var name = component.Definition.name + " " + DisplayName(component) + " " + component.InstanceId;
            return name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("启动") || name.Contains("正转") || name.Contains("反转");
        }

        private static bool IsStopButton(CircuitComponent component)
        {
            if (component == null || component.Definition == null || component.GetTerminal("11") == null || component.GetTerminal("12") == null)
            {
                return false;
            }

            var name = component.Definition.name + " " + DisplayName(component) + " " + component.InstanceId;
            return name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Emergency", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("停止") || name.Contains("急停");
        }

        private static bool IsCompoundPushButton(CircuitComponent component)
        {
            return component != null && component.Definition != null &&
                component.GetTerminal("11") != null && component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null && component.GetTerminal("24") != null &&
                component.Definition.name.IndexOf("Button_Compound", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class IndustrialCircuitFacts
    {
        public WorkspaceController Workspace;
        public bool IsIndustrial;
        public string CircuitType = "工业控制电路";
        public List<CircuitComponent> Components = new List<CircuitComponent>();
        public List<WireView> Wires = new List<WireView>();
        public List<CircuitComponent> PowerSources = new List<CircuitComponent>();
        public List<CircuitComponent> Motors = new List<CircuitComponent>();
        public List<CircuitComponent> Contactors = new List<CircuitComponent>();
        public List<CircuitComponent> ThermalRelays = new List<CircuitComponent>();
        public List<CircuitComponent> Breakers = new List<CircuitComponent>();
        public List<CircuitComponent> Fuses = new List<CircuitComponent>();
        public List<CircuitComponent> StartButtons = new List<CircuitComponent>();
        public List<CircuitComponent> StopButtons = new List<CircuitComponent>();
        public List<CircuitComponent> CompoundButtons = new List<CircuitComponent>();
        public bool HasSelfHold;
        public bool HasThermalControlContact;
        public bool HasMutualInterlock;
        public bool HasButtonInterlock;
        public bool IsForwardReverseControl;
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 仅在 Editor 菜单运行的拓扑安全与负向扰动回归夹具。
    /// 该类用内存测试电路覆盖小型自锁/互锁环路、复杂拓扑上限和若干安全规则边界；
    /// 不加载 18 张标准模板、不写入基线或报告，也不属于运行时产品功能。
    ///
    /// 测试用于发现分析或 Validation 的行为漂移，而不是生成新的期望结果。拓扑规模、超时保护、
    /// RuleId 和断言约束均需谨慎变更；失败写入 Console，真实模板覆盖仍由独立基线工具负责。
    /// </summary>
    public static class TopologySafetyTests
    {
        [MenuItem("Tools/Tests/运行拓扑安全与负向扰动测试")]
        public static void RunTests()
        {
            // 用例在内存中构造；不要把这里的负向扰动当作标准模板接线数据。
            UnityEngine.Debug.Log("==== 开始执行拓扑安全测试 ====");
            int passed = 0;
            int total = 9;

            Action<string, Action> runTest = (name, testAction) =>
            {
                try
                {
                    testAction();
                    passed++;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"{name} 失败: {e.Message}");
                }
            };

            // 第一类：简单环路稳定性测试
            runTest("Test_KMSelfLoop_Stable", Test_KMSelfLoop_Stable);
            runTest("Test_KMMutualLock_Stable", Test_KMMutualLock_Stable);
            runTest("Test_SQKMLoop_Stable", Test_SQKMLoop_Stable);
            runTest("Test_NestedLock_Stable", Test_NestedLock_Stable);

            // 第二类：复杂拓扑超限测试
            runTest("Test_ComplexTopologyExceeded", Test_ComplexTopologyExceeded);

            // 第三类：安全规则验证测试
            runTest("Test_StopButtonBypassed", Test_StopButtonBypassed);
            runTest("Test_ThermalRelayBypassed", Test_ThermalRelayBypassed);
            runTest("Test_SelfHoldingIncomplete", Test_SelfHoldingIncomplete);
            runTest("Test_ReversingConflict", Test_ReversingConflict);

            UnityEngine.Debug.Log($"==== 测试完成: {passed}/{total} 成功 ====");
        }

        // ==========================================
        // 第一类：简单环路稳定性测试
        // 目标：验证正常的小规模自锁/互锁环路不会误判为复杂拓扑，能够在限定时间内安全返回。
        // ==========================================

        private static void Test_KMSelfLoop_Stable()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");
            factory.Connect(km, "13", km, "A1");
            factory.Connect(km, "14", km, "A2");
            factory.Connect(power, "L1", km, "13");
            
            var report = factory.Validate();
            AssertDoesNotHaveRuleId(report, "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY");
            UnityEngine.Debug.Log("[PASS] KMSelfLoop_Stable: 简单回路安全返回，无超时");
        }

        private static void Test_KMMutualLock_Stable()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");
            var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");
            
            factory.Connect(km1, "14", km2, "A1");
            factory.Connect(km2, "14", km1, "A1");
            factory.Connect(power, "L1", km1, "14");

            var report = factory.Validate();
            AssertDoesNotHaveRuleId(report, "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY");
            UnityEngine.Debug.Log("[PASS] KMMutualLock_Stable: 互相自锁安全返回，无超时");
        }

        private static void Test_SQKMLoop_Stable()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");
            var sq = factory.CreateComponent("sq1", "TravelSwitch", ComponentKind.Switch, "23", "24");
            
            factory.Connect(sq, "24", km, "A1");
            factory.Connect(km, "14", sq, "23");
            factory.Connect(power, "L1", sq, "24");

            var report = factory.Validate();
            AssertDoesNotHaveRuleId(report, "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY");
            UnityEngine.Debug.Log("[PASS] SQKMLoop_Stable: 行程开关循环安全返回，无超时");
        }

        private static void Test_NestedLock_Stable()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");
            var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");
            var km3 = factory.CreateComponent("km3", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");

            factory.Connect(km1, "14", km2, "13");
            factory.Connect(km2, "14", km3, "A1");
            factory.Connect(km3, "14", km1, "A1");
            factory.Connect(power, "L1", km1, "14");

            var report = factory.Validate();
            AssertDoesNotHaveRuleId(report, "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY");
            UnityEngine.Debug.Log("[PASS] NestedLock_Stable: 多接触器嵌套自锁安全返回，无超时");
        }

        // ==========================================
        // 第二类：复杂拓扑超限测试
        // 目标：在有限资源下遇到长链/庞大网络时，系统能自动触发异常保护切断搜索，防止 Editor/Game 卡死。
        // ==========================================

        private static void Test_ComplexTopologyExceeded()
        {
            var factory = new CircuitTestFactory();
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            
            var lastTerminal = motor.GetTerminal("U");
            for (int i = 0; i < 30; i++)
            {
                var dummy = factory.CreateComponent($"dummy{i}", "TerminalBlock", ComponentKind.TerminalBlock, "1", "2");
                factory.Connect(lastTerminal.Owner, lastTerminal.TerminalId, dummy, "1");
                factory.Connect(dummy, "1", dummy, "2");
                lastTerminal = dummy.GetTerminal("2");
            }

            try
            {
                // Editor-only: 临时调低阈值，使得 30 个节点即可触发超限保护
                TopologyTraversalLimits.SetEditorTestingLimits(20, 20, 40);

                var report = factory.Validate();
                AssertHasRuleId(report, "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY");
                UnityEngine.Debug.Log("[PASS] ComplexTopologyExceeded: 成功触发拓扑超限异常保护");
            }
            finally
            {
                // 确保一定恢复默认配置，不影响正式版
                TopologyTraversalLimits.RestoreDefaultLimits();
            }
        }

        // ==========================================
        // 第三类：安全规则验证测试
        // ==========================================

        private static void Test_StopButtonBypassed()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "Power", ComponentKind.PowerSource, "L1", "N");
            var sb_stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            var km = factory.CreateComponent("km", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2");

            factory.Connect(power, "L1", sb_stop, "11");
            factory.Connect(sb_stop, "12", km, "A1");
            factory.Connect(km, "A2", power, "N");
            factory.Connect(power, "L1", km, "A1");

            var report = factory.Validate();
            AssertHasRuleId(report, "STOP_BUTTON_BYPASSED");
            UnityEngine.Debug.Log("[PASS] STOP_BUTTON_BYPASSED");
        }

        private static void Test_ThermalRelayBypassed()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_ThreePhase_Power", ComponentKind.PowerSource, "L1", "L2", "L3", "N");
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var fr = factory.CreateComponent("fr", "ThermalRelay_FR_380V", ComponentKind.Switch, "95", "96", "97", "98", "L1", "L2", "L3", "T1", "T2", "T3");
            var km = factory.CreateComponent("km", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");

            factory.Connect(power, "L1", km, "L1");
            factory.Connect(power, "L2", km, "L2");
            factory.Connect(power, "L3", km, "L3");
            
            factory.Connect(km, "T1", fr, "L1");
            factory.Connect(km, "T2", fr, "L2");
            factory.Connect(km, "T3", fr, "L3");
            
            factory.Connect(fr, "T1", motor, "U");
            factory.Connect(fr, "T2", motor, "V");
            factory.Connect(fr, "T3", motor, "W");

            factory.Connect(power, "L1", km, "A1");
            factory.Connect(km, "A2", fr, "95");
            factory.Connect(fr, "96", power, "N");
            factory.Connect(km, "A2", power, "N");

            var report = factory.Validate();
            AssertHasRuleId(report, "THERMAL_RELAY_CONTROL_BYPASSED");
            UnityEngine.Debug.Log("[PASS] THERMAL_RELAY_CONTROL_BYPASSED");
        }

        private static void Test_SelfHoldingIncomplete()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            var sb_stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            var sb_start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
            var km = factory.CreateComponent("km", "Contactor_KM_220V", ComponentKind.ContactorCoil, "A1", "A2", "13", "14");

            factory.Connect(power, "L1", sb_stop, "11");
            factory.Connect(sb_stop, "12", sb_start, "23");
            factory.Connect(sb_start, "24", km, "A1");
            factory.Connect(km, "A2", power, "N");
            
            factory.Connect(km, "13", sb_start, "23");

            var report = factory.Validate();
            AssertHasRuleId(report, "SELF_HOLDING_BRANCH_INCOMPLETE");
            UnityEngine.Debug.Log("[PASS] SELF_HOLDING_BRANCH_INCOMPLETE");
        }

        private static void Test_ReversingConflict()
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_ThreePhase_Power", ComponentKind.PowerSource, "L1", "L2", "L3", "N");
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var km_f = factory.CreateComponent("km_forward", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");
            var km_r = factory.CreateComponent("km_reverse", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");
            
            factory.Connect(power, "L1", km_f, "L1");
            factory.Connect(power, "L2", km_f, "L2");
            factory.Connect(power, "L3", km_f, "L3");
            
            factory.Connect(km_f, "T1", motor, "U");
            factory.Connect(km_f, "T2", motor, "V");
            factory.Connect(km_f, "T3", motor, "W");

            factory.Connect(power, "L1", km_r, "L1");
            factory.Connect(power, "L2", km_r, "L2");
            factory.Connect(power, "L3", km_r, "L3");

            factory.Connect(km_r, "T1", motor, "W");
            factory.Connect(km_r, "T2", motor, "V");
            factory.Connect(km_r, "T3", motor, "U");

            factory.Connect(power, "L1", km_f, "A1");
            factory.Connect(power, "N", km_f, "A2");
            
            factory.Connect(power, "L1", km_r, "A1");
            factory.Connect(power, "N", km_r, "A2");

            var report = factory.Validate();
            AssertHasRuleId(report, "REVERSING_CONTACTOR_CONFLICT");
            UnityEngine.Debug.Log("[PASS] REVERSING_CONTACTOR_CONFLICT");
        }

        private static void AssertHasRuleId(CircuitValidationReport report, string ruleId)
        {
            if (report == null) throw new System.Exception("Report is null");
            foreach (var issue in report.Issues)
            {
                if (issue.RuleId == ruleId) return;
            }
            throw new System.Exception($"Assert failed: expected RuleId '{ruleId}' but not found.");
        }

        private static void AssertDoesNotHaveRuleId(CircuitValidationReport report, string ruleId)
        {
            if (report == null) return;
            foreach (var issue in report.Issues)
            {
                if (issue.RuleId == ruleId)
                {
                    throw new System.Exception($"Assert failed: found unexpected RuleId '{ruleId}'.");
                }
            }
        }
    }

    public class CircuitTestFactory
    {
        private List<CircuitComponent> components = new List<CircuitComponent>();
        private List<WireView> wires = new List<WireView>();

        public CircuitComponent CreateComponent(string instanceId, string defName, ComponentKind kind, params string[] terminalIds)
        {
            var go = new GameObject(instanceId);
            var comp = go.AddComponent<CircuitComponent>();
            typeof(CircuitComponent).GetProperty("InstanceId").SetValue(comp, instanceId);
            
            var def = ScriptableObject.CreateInstance<ComponentDefinition>();
            def.name = defName;
            def.kind = kind;
            if (defName != null && defName.IndexOf("220V", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                def.ratedVoltage = 220f;
                def.sourceVoltage = 220f;
            }
            else if (defName != null && defName.IndexOf("380V", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                def.ratedVoltage = 380f;
                def.sourceLineVoltage = 380f;
            }

            typeof(CircuitComponent).GetProperty("Definition").SetValue(comp, def);

            var terms = new List<TerminalView>();
            foreach (var id in terminalIds)
            {
                var tgo = new GameObject(id);
                tgo.transform.SetParent(go.transform);
                var term = tgo.AddComponent<TerminalView>();
                typeof(TerminalView).GetProperty("TerminalId").SetValue(term, id);
                
                var role = TerminalRole.Generic;
                if (id == "L" || id == "L1" || id == "L2" || id == "L3") role = TerminalRole.Phase;
                else if (id == "N") role = TerminalRole.Neutral;
                else if (id == "PE") role = TerminalRole.ProtectiveEarth;
                else if (id == "A1") role = TerminalRole.CoilA1;
                else if (id == "A2") role = TerminalRole.CoilA2;
                else if (id == "U" || id == "V" || id == "W") role = TerminalRole.Input;
                else if (id == "T1" || id == "T2" || id == "T3") role = TerminalRole.Output;
                
                typeof(TerminalView).GetProperty("Role").SetValue(term, role);
                typeof(TerminalView).GetProperty("Owner").SetValue(term, comp);
                
                terms.Add(term);
            }

            var field = typeof(CircuitComponent).GetField("terminals", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) field.SetValue(comp, terms);

            components.Add(comp);
            return comp;
        }

        public void Connect(CircuitComponent c1, string t1, CircuitComponent c2, string t2)
        {
            var term1 = c1.GetTerminal(t1);
            var term2 = c2.GetTerminal(t2);
            if (term1 == null || term2 == null) throw new Exception("找不到端子");

            var go = new GameObject("Wire");
            var wire = go.AddComponent<WireView>();
            typeof(WireView).GetProperty("StartTerminal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wire, term1);
            typeof(WireView).GetProperty("EndTerminal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wire, term2);
            
            wires.Add(wire);
        }

        public void SetClosed(CircuitComponent component, bool closed)
        {
            if (component == null)
            {
                throw new Exception("Component is null.");
            }

            component.SetClosed(closed);
        }

        public void SetTogglable(CircuitComponent component, bool togglable)
        {
            if (component == null || component.Definition == null)
            {
                throw new Exception("Component definition is null.");
            }

            component.Definition.togglable = togglable;
        }

        public CircuitValidationReport Validate()
        {
            var stopwatch = Stopwatch.StartNew();
            
            var analyzer = new CircuitStateAnalyzer();
            var result = analyzer.Analyze(components, wires);

            var service = new CircuitValidationService();
            var report = service.Validate(components, wires, result);
            
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                throw new Exception("验证耗时过长，可能陷入死循环！");
            }
            
            return report;
        }
    }
}

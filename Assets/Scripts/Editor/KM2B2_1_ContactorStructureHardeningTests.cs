using System;
using System.Collections.Generic;
using System.Reflection;
using ElectricalSim.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-2B2.1: StateAnalyzer 按钮角色去名称化与互锁误报收口测试。
    ///
    /// 验证 B2.1 修复：
    /// A. 按钮角色判断不再依赖 "Stop"/"停止" 名称，纯结构判断；
    /// B. 互锁 candidate 检查使用 candidate-excluded 拓扑，防止 dangling NC 误报；
    /// C. 自锁回归保持 B2 全部 15 场景不变。
    ///
    /// 新增测试：
    /// - T16: dangling NC → KM2.A1（无供电上游）→ false
    /// - T17: NC → 灯/孤立节点 + KM2.A1（无有效控制上游）→ false
    /// - T18: 名称无关启动按钮参与自锁识别
    /// - T19: 名称无关停止按钮参与 static NC 拓扑
    /// - T20: 名称欺骗（名含 Stop 但结构为 23/24 NO）仍被识别为启动按钮
    /// </summary>
    public static class KM2B2_1_ContactorStructureHardeningTests
    {
        [MenuItem("Tools/Tests/Run KM2B2.1 Contactor Structure Hardening Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                // === A. 互锁误报收口 ===
                Test16_Dangling_NC_To_KM2_A1(failures);
                Test17_NC_To_Lamp_And_KM2_A1(failures);

                // === B. 按钮角色去名称化 ===
                Test18_NameIndependent_StartButton(failures);
                Test19_NameIndependent_StopButton(failures);
                Test20_NameDeception_StopName_NO_Structure(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM2B2_1_ContactorStructureHardeningTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B2_1_ContactorStructureHardeningTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B2_1_ContactorStructureHardeningTests 通过 (5/5) ===");
        }

        // =========================================================================
        // 反射辅助
        // =========================================================================

        private static string GetSelfHoldContactPair(ComponentStateInfo info)
        {
            if (info == null) return null;
            var field = typeof(ComponentStateInfo).GetField("SelfHoldContactPair");
            return field != null ? (string)field.GetValue(info) : null;
        }

        private static string GetInterlockContactPair(ComponentStateInfo info)
        {
            if (info == null) return null;
            var field = typeof(ComponentStateInfo).GetField("InterlockContactPair");
            return field != null ? (string)field.GetValue(info) : null;
        }

        private static CircuitStateResult Analyze(CircuitTestFactory factory)
        {
            var components = (List<CircuitComponent>)typeof(CircuitTestFactory)
                .GetField("components", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(factory);
            var wires = (List<WireView>)typeof(CircuitTestFactory)
                .GetField("wires", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(factory);
            var analyzer = new CircuitStateAnalyzer();
            return analyzer.Analyze(components, wires);
        }

        // =========================================================================
        // T16: dangling NC → KM2.A1（无供电上游）
        // KM1.21 → 悬空/dangling 支路（仅连到 KM2.A1）
        // KM1.22 → KM2.A1
        // 两端均有外部 Wire，但无有效控制上游
        // 预期：HasInterlockStructure = false
        // =========================================================================
        private static void Test16_Dangling_NC_To_KM2_A1(List<string> failures)
        {
            const string scenario = "T16-Dangling-NC-To-KM2-A1";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                // KM1 正常自锁控制回路
                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km1, "A1");
                factory.Connect(km1, "A2", power, "N");
                factory.Connect(stop, "12", km1, "13");
                factory.Connect(km1, "14", km1, "A1");

                // KM1 NC 21 → dangling（连到一个未接电源的孤立端子 km2.A2）
                // KM1 NC 22 → KM2.A1
                // KM2.A2 不接电源 → 无有效控制上游
                factory.Connect(km1, "21", km2, "A2");
                factory.Connect(km1, "22", km2, "A1");
                // KM2 的 A2 不接到任何电源 → dangling

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] KM1: HasInterlockStructure={info.HasInterlockStructure}, " +
                    $"InterlockContactPair={GetInterlockContactPair(info)}");

                if (info.HasInterlockStructure)
                    failures.Add($"{scenario}: KM1 HasInterlockStructure 应为 false（dangling NC 无有效供电上游）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // T17: NC → 灯/孤立控制节点 + KM2.A1（无有效供电路径）
        // KM1.21 → 灯 L
        // KM1.22 → KM2.A1
        // 灯不构成有效控制上游
        // 预期：false
        // =========================================================================
        private static void Test17_NC_To_Lamp_And_KM2_A1(List<string> failures)
        {
            const string scenario = "T17-NC-To-Lamp-And-KM2-A1";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var lamp = factory.CreateComponent("lamp1", "Lamp_220V", ComponentKind.Lamp, "L", "N");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                // KM1 正常自锁控制回路
                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km1, "A1");
                factory.Connect(km1, "A2", power, "N");
                factory.Connect(stop, "12", km1, "13");
                factory.Connect(km1, "14", km1, "A1");

                // KM1 NC 21 → 灯 L
                // KM1 NC 22 → KM2.A1
                // 灯不接电源（不构成有效控制上游），KM2.A2 也不接电源
                factory.Connect(km1, "21", lamp, "L");
                factory.Connect(km1, "22", km2, "A1");
                // 灯 N 不接 → 灯不构成有效供电节点
                // KM2.A2 不接电源

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] KM1: HasInterlockStructure={info.HasInterlockStructure}, " +
                    $"InterlockContactPair={GetInterlockContactPair(info)}");

                if (info.HasInterlockStructure)
                    failures.Add($"{scenario}: KM1 HasInterlockStructure 应为 false（灯非有效控制上游）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // T18: 名称无关启动按钮参与自锁识别
        // instanceId = "random_button_001"
        // definitionName = "Custom_NO_Switch"（不含 Start/启动）
        // 结构：PushButton + 23/24
        // 预期：自锁识别成功
        // =========================================================================
        private static void Test18_NameIndependent_StartButton(List<string> failures)
        {
            const string scenario = "T18-NameIndependent-StartButton";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                // 名称完全不包含 Start/启动，但结构为 PushButton + 23/24
                var start = factory.CreateComponent("random_button_001", "Custom_NO_Switch",
                    ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");
                factory.Connect(stop, "12", km, "13");
                factory.Connect(km, "14", km, "A1");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}");

                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true（名称无关启动按钮应参与自锁识别）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // T19: 名称无关停止按钮参与 static NC 拓扑
        // instanceId = "custom_nc_btn"
        // definitionName = "Custom_NC_Switch"（不含 Stop/停止）
        // 结构：PushButton + 11/12, startsClosed=true
        // 预期：停止按钮 NC 被正确加入 staticNcTopology，自锁识别正常
        // =========================================================================
        private static void Test19_NameIndependent_StopButton(List<string> failures)
        {
            const string scenario = "T19-NameIndependent-StopButton";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                // 名称完全不包含 Stop/停止，但结构为 PushButton + 11/12
                var stop = factory.CreateComponent("custom_nc_btn", "Custom_NC_Switch",
                    ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");
                factory.Connect(stop, "12", km, "13");
                factory.Connect(km, "14", km, "A1");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}");

                // 如果停止按钮 NC 被正确识别，staticNcTopology 会包含 stop.11↔12 连接，
                // 启动按钮 23/24 能通过 NC 拓扑到达 KM A1/A2，自锁识别应成功
                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true（名称无关停止按钮应参与 static NC 拓扑）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // T20: 名称欺骗——名含 Stop 但结构为 23/24 NO
        // definitionName = "Button_Stop_Fake"（含 Stop），但只有 23/24
        // 预期：仍被识别为启动按钮（结构优先于名称）
        // =========================================================================
        private static void Test20_NameDeception_StopName_NO_Structure(List<string> failures)
        {
            const string scenario = "T20-NameDeception-StopName-NO-Structure";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                // 名含 Stop 但结构只有 23/24（NO-only）
                var fakeStart = factory.CreateComponent("fake_btn", "Button_Stop_Fake",
                    ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(fakeStart, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", fakeStart, "23");
                factory.Connect(fakeStart, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");
                factory.Connect(stop, "12", km, "13");
                factory.Connect(km, "14", km, "A1");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}");

                // 名称含 Stop 但结构为 23/24 NO → 应被识别为启动按钮，自锁应成功
                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true（名含 Stop 但结构为 NO 应被识别为启动按钮）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }
    }
}

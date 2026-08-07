using System;
using System.Collections.Generic;
using System.Reflection;
using ElectricalSim.AI;
using ElectricalSim.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-2B3: IndustrialCircuitRuleAnalyzer 结构语义泛化测试。
    ///
    /// 验证 HasSelfHold 和 HasMutualInterlock 不再通过固定端子编号 13/14、21/22 的
    /// 简化 wire presence 判断，而是复用 CircuitStateAnalyzer 的结构事实
    /// (HasSelfHoldStructure / HasInterlockStructure)。
    ///
    /// 测试场景：
    /// - T01: 13/14 标准自锁 (baseline)
    /// - T02: 33/34 真正自锁 (RED: 旧代码只检查 13/14)
    /// - T03: 33/34 仅联动，不是自锁
    /// - T04: 13/14 自锁 + 33/34 联动
    /// - T05: 33/34 自锁 + 13/14 联动
    /// - T06: 标准双 KM 21/22 互锁
    /// - T07: dangling NC (RED: 旧代码误报)
    /// - T08: 灯/孤立支路
    /// - T09: Start.23 悬空 (RED: 旧代码误报)
    ///
    /// 使用反射访问 internal BuildFacts 和 IndustrialCircuitFacts 字段。
    /// </summary>
    public static class KM2B3_IndustrialRuleStructuralFactsTests
    {
        [MenuItem("Tools/Tests/Run KM2B3 Industrial Rule Structural Facts Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                T01_13_14_StandardSelfHold(failures);
                T02_33_34_RealSelfHold(failures);
                T03_33_34_LinkageOnly(failures);
                T04_13_14_SelfHold_33_34_Linkage(failures);
                T05_33_34_SelfHold_13_14_Linkage(failures);
                T06_StandardDualKMInterlock(failures);
                T07_DanglingNC(failures);
                T08_LampIsolatedBranch(failures);
                T09_DanglingStart23(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM2B3_IndustrialRuleStructuralFactsTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B3_IndustrialRuleStructuralFactsTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B3_IndustrialRuleStructuralFactsTests 通过 (9/9) ===");
        }

        // =========================================================================
        // 反射辅助
        // =========================================================================

        private static object InvokeBuildFacts(WorkspaceController workspace)
        {
            var method = typeof(IndustrialCircuitRuleAnalyzer).GetMethod(
                "BuildFacts", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException("BuildFacts 方法未找到");
            return method.Invoke(null, new object[] { workspace });
        }

        private static bool GetFactsBool(object facts, string fieldName)
        {
            var field = facts.GetType().GetField(fieldName);
            if (field == null)
                throw new InvalidOperationException("字段 " + fieldName + " 未找到");
            return (bool)field.GetValue(facts);
        }

        // =========================================================================
        // 工作区构造辅助
        // =========================================================================

        private static WorkspaceController CreateTestWorkspace(CircuitTestFactory factory)
        {
            var go = new GameObject("B3TestWorkspace");
            var workspace = go.AddComponent<WorkspaceController>();

            // Awake 可能已创建 WireManager；获取或设置
            var wmField = typeof(WorkspaceController).GetField("wireManager",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var wireManager = wmField.GetValue(workspace) as WireManager;
            if (wireManager == null)
            {
                wireManager = go.AddComponent<WireManager>();
                wmField.SetValue(workspace, wireManager);
            }

            // 注入 components
            var componentsList = (List<CircuitComponent>)typeof(WorkspaceController)
                .GetField("components", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(workspace);
            var factoryComponents = (List<CircuitComponent>)typeof(CircuitTestFactory)
                .GetField("components", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(factory);
            componentsList.AddRange(factoryComponents);

            // 注入 wires
            var wiresList = (List<WireView>)typeof(WireManager)
                .GetField("wires", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(wireManager);
            var factoryWires = (List<WireView>)typeof(CircuitTestFactory)
                .GetField("wires", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(factory);
            wiresList.AddRange(factoryWires);

            return workspace;
        }

        private static void CleanupWorkspace(WorkspaceController workspace)
        {
            if (workspace != null && workspace.gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(workspace.gameObject);
            }
        }

        // =========================================================================
        // 电路构造：自锁场景
        // =========================================================================

        /// <summary>
        /// 构造标准自锁控制回路，使用指定的 NO 对作为自锁触点。
        /// power.L1 → stop.11 → stop.12 → start.23 → start.24 → km.A1
        /// km.A2 → power.N
        /// 自锁并联：stop.12 → km.{noStart} → km.{noEnd} → km.A1
        /// </summary>
        private static CircuitTestFactory CreateSelfHoldCircuit(
            string noStart, string noEnd,
            out CircuitComponent stop, out CircuitComponent start, out CircuitComponent km)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
            km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");

            factory.SetClosed(stop, true);
            factory.SetClosed(start, false);

            factory.Connect(power, "L1", stop, "11");
            factory.Connect(stop, "12", start, "23");
            factory.Connect(start, "24", km, "A1");
            factory.Connect(km, "A2", power, "N");

            // 自锁并联
            factory.Connect(stop, "12", km, noStart);
            factory.Connect(km, noEnd, km, "A1");

            return factory;
        }

        // =========================================================================
        // 电路构造：互锁场景
        // =========================================================================

        /// <summary>
        /// 构造标准正反转互锁电路。
        /// km_f 和 km_r 互为 NC 互锁。
        /// instanceId 包含 km_f/km_r 以触发 IsForwardReverseControl。
        /// </summary>
        private static CircuitTestFactory CreateStandardInterlockCircuit(
            out CircuitComponent stop, out CircuitComponent fwd, out CircuitComponent rev,
            out CircuitComponent kmF, out CircuitComponent kmR)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            fwd = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
            rev = factory.CreateComponent("sb3", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
            kmF = factory.CreateComponent("km_f", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");
            kmR = factory.CreateComponent("km_r", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");

            factory.SetClosed(stop, true);
            factory.SetClosed(fwd, false);
            factory.SetClosed(rev, false);

            // 公共停止
            factory.Connect(power, "L1", stop, "11");

            // 正转支路：stop.12 → fwd.23 → fwd.24 → km_r.21(NC) → km_r.22 → km_f.A1
            factory.Connect(stop, "12", fwd, "23");
            factory.Connect(fwd, "24", kmR, "21");
            factory.Connect(kmR, "22", kmF, "A1");
            factory.Connect(kmF, "A2", power, "N");

            // 反转支路：stop.12 → rev.23 → rev.24 → km_f.21(NC) → km_f.22 → km_r.A1
            factory.Connect(stop, "12", rev, "23");
            factory.Connect(rev, "24", kmF, "21");
            factory.Connect(kmF, "22", kmR, "A1");
            factory.Connect(kmR, "A2", power, "N");

            return factory;
        }

        // =========================================================================
        // T01: 13/14 标准自锁 (baseline)
        // =========================================================================
        private static void T01_13_14_StandardSelfHold(List<string> failures)
        {
            const string scenario = "T01-13-14-StandardSelfHold";
            WorkspaceController workspace = null;
            try
            {
                CircuitComponent stop, start, km;
                var factory = CreateSelfHoldCircuit("13", "14", out stop, out start, out km);
                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasSelfHold = GetFactsBool(facts, "HasSelfHold");

                Debug.Log($"[{scenario}] HasSelfHold={hasSelfHold}");

                if (!hasSelfHold)
                    failures.Add($"{scenario}: HasSelfHold 应为 true (13/14 标准自锁)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T02: 33/34 真正自锁 (RED: 旧代码只检查 13/14)
        // =========================================================================
        private static void T02_33_34_RealSelfHold(List<string> failures)
        {
            const string scenario = "T02-33-34-RealSelfHold";
            WorkspaceController workspace = null;
            try
            {
                CircuitComponent stop, start, km;
                var factory = CreateSelfHoldCircuit("33", "34", out stop, out start, out km);
                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasSelfHold = GetFactsBool(facts, "HasSelfHold");

                Debug.Log($"[{scenario}] HasSelfHold={hasSelfHold}");

                if (!hasSelfHold)
                    failures.Add($"{scenario}: HasSelfHold 应为 true (33/34 真正自锁，旧代码只检查 13/14 导致 RED)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T03: 33/34 仅联动，不是自锁
        // =========================================================================
        private static void T03_33_34_LinkageOnly(List<string> failures)
        {
            const string scenario = "T03-33-34-LinkageOnly";
            WorkspaceController workspace = null;
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");

                // 33/34 有线但不构成自锁（34 不回到 km.A1，而是接到 power.N）
                factory.Connect(stop, "12", km, "33");
                factory.Connect(km, "34", power, "N");

                // 13/14 不接线
                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasSelfHold = GetFactsBool(facts, "HasSelfHold");

                Debug.Log($"[{scenario}] HasSelfHold={hasSelfHold}");

                if (hasSelfHold)
                    failures.Add($"{scenario}: HasSelfHold 应为 false (33/34 仅联动，不是自锁)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T04: 13/14 自锁 + 33/34 联动
        // =========================================================================
        private static void T04_13_14_SelfHold_33_34_Linkage(List<string> failures)
        {
            const string scenario = "T04-13-14-SelfHold-33-34-Linkage";
            WorkspaceController workspace = null;
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");

                // 13/14 自锁
                factory.Connect(stop, "12", km, "13");
                factory.Connect(km, "14", km, "A1");

                // 33/34 联动（不回到 A1）
                factory.Connect(stop, "12", km, "33");
                factory.Connect(km, "34", power, "N");

                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasSelfHold = GetFactsBool(facts, "HasSelfHold");

                Debug.Log($"[{scenario}] HasSelfHold={hasSelfHold}");

                if (!hasSelfHold)
                    failures.Add($"{scenario}: HasSelfHold 应为 true (13/14 自锁 + 33/34 联动)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T05: 33/34 自锁 + 13/14 联动
        // =========================================================================
        private static void T05_33_34_SelfHold_13_14_Linkage(List<string> failures)
        {
            const string scenario = "T05-33-34-SelfHold-13-14-Linkage";
            WorkspaceController workspace = null;
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");

                // 33/34 自锁
                factory.Connect(stop, "12", km, "33");
                factory.Connect(km, "34", km, "A1");

                // 13/14 联动（不回到 A1）
                factory.Connect(stop, "12", km, "13");
                factory.Connect(km, "14", power, "N");

                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasSelfHold = GetFactsBool(facts, "HasSelfHold");

                Debug.Log($"[{scenario}] HasSelfHold={hasSelfHold}");

                if (!hasSelfHold)
                    failures.Add($"{scenario}: HasSelfHold 应为 true (33/34 自锁 + 13/14 联动)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T06: 标准双 KM 21/22 互锁
        // =========================================================================
        private static void T06_StandardDualKMInterlock(List<string> failures)
        {
            const string scenario = "T06-StandardDualKMInterlock";
            WorkspaceController workspace = null;
            try
            {
                CircuitComponent stop, fwd, rev, kmF, kmR;
                var factory = CreateStandardInterlockCircuit(out stop, out fwd, out rev, out kmF, out kmR);
                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasMutualInterlock = GetFactsBool(facts, "HasMutualInterlock");

                Debug.Log($"[{scenario}] HasMutualInterlock={hasMutualInterlock}");

                if (!hasMutualInterlock)
                    failures.Add($"{scenario}: HasMutualInterlock 应为 true (标准双 KM 21/22 互锁)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T07: dangling NC (RED: 旧代码误报)
        // =========================================================================
        private static void T07_DanglingNC(List<string> failures)
        {
            const string scenario = "T07-DanglingNC";
            WorkspaceController workspace = null;
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var fwd = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var rev = factory.CreateComponent("sb3", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var kmF = factory.CreateComponent("km_f", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var kmR = factory.CreateComponent("km_r", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(fwd, false);
                factory.SetClosed(rev, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", fwd, "23");
                factory.Connect(fwd, "24", kmF, "A1");
                factory.Connect(kmF, "A2", power, "N");

                factory.Connect(stop, "12", rev, "23");
                factory.Connect(rev, "24", kmR, "A1");
                factory.Connect(kmR, "A2", power, "N");

                // dangling NC: km_f.21 悬空, km_f.22 → km_r.A1
                // km_r.21 悬空, km_r.22 → km_f.A1
                // 但 km_f.A1 已经连到 fwd.24，km_r.A1 已经连到 rev.24
                // 所以这里 22 不连到 A1（因为 A1 已被占用）
                // 改为：km_f.22 和 km_r.22 各自悬空，但 21 有线
                factory.Connect(stop, "12", kmF, "21"); // km_f.21 有线但不构成互锁
                factory.Connect(stop, "12", kmR, "21"); // km_r.21 有线但不构成互锁
                // km_f.22 和 km_r.22 悬空

                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasMutualInterlock = GetFactsBool(facts, "HasMutualInterlock");

                Debug.Log($"[{scenario}] HasMutualInterlock={hasMutualInterlock}");

                if (hasMutualInterlock)
                    failures.Add($"{scenario}: HasMutualInterlock 应为 false (dangling NC，22 悬空不构成互锁)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T08: 灯/孤立支路
        // =========================================================================
        private static void T08_LampIsolatedBranch(List<string> failures)
        {
            const string scenario = "T08-LampIsolatedBranch";
            WorkspaceController workspace = null;
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var fwd = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var rev = factory.CreateComponent("sb3", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var kmF = factory.CreateComponent("km_f", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var kmR = factory.CreateComponent("km_r", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                // 灯（非 KM 负载）
                var lamp = factory.CreateComponent("lamp1", "Lamp_220V", ComponentKind.Lamp, "A1", "A2");

                factory.SetClosed(stop, true);
                factory.SetClosed(fwd, false);
                factory.SetClosed(rev, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", fwd, "23");
                factory.Connect(fwd, "24", kmF, "A1");
                factory.Connect(kmF, "A2", power, "N");

                factory.Connect(stop, "12", rev, "23");
                factory.Connect(rev, "24", kmR, "A1");
                factory.Connect(kmR, "A2", power, "N");

                // km_f NC 21/22 接到灯而非 KM 线圈
                factory.Connect(kmF, "21", lamp, "A1");
                factory.Connect(kmF, "22", lamp, "A2");
                factory.Connect(kmR, "21", lamp, "A1");
                factory.Connect(kmR, "22", lamp, "A2");

                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasMutualInterlock = GetFactsBool(facts, "HasMutualInterlock");

                Debug.Log($"[{scenario}] HasMutualInterlock={hasMutualInterlock}");

                if (hasMutualInterlock)
                    failures.Add($"{scenario}: HasMutualInterlock 应为 false (NC 接灯，不构成 KM 互锁)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // T09: Start.23 悬空 (RED: 旧代码误报)
        // =========================================================================
        private static void T09_DanglingStart23(List<string> failures)
        {
            const string scenario = "T09-DanglingStart23";
            WorkspaceController workspace = null;
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var fwd = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var rev = factory.CreateComponent("sb3", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var kmF = factory.CreateComponent("km_f", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var kmR = factory.CreateComponent("km_r", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(fwd, false);
                factory.SetClosed(rev, false);

                // power → stop
                factory.Connect(power, "L1", stop, "11");
                // stop.12 不连到 fwd.23 (Start.23 悬空)
                // stop.12 不连到 rev.23 (Start.23 悬空)

                // fwd.24 → km_r.21 (NC candidate 一侧)
                factory.Connect(fwd, "24", kmR, "21");
                // km_r.22 → km_f.A1 (candidate 另一侧到达另一 KM 线圈)
                factory.Connect(kmR, "22", kmF, "A1");
                factory.Connect(kmF, "A2", power, "N");

                // rev.24 → km_f.21 (NC candidate 一侧)
                factory.Connect(rev, "24", kmF, "21");
                // km_f.22 → km_r.A1 (candidate 另一侧到达另一 KM 线圈)
                factory.Connect(kmF, "22", kmR, "A1");
                factory.Connect(kmR, "A2", power, "N");

                workspace = CreateTestWorkspace(factory);

                var facts = InvokeBuildFacts(workspace);
                var hasMutualInterlock = GetFactsBool(facts, "HasMutualInterlock");

                Debug.Log($"[{scenario}] HasMutualInterlock={hasMutualInterlock}");

                if (hasMutualInterlock)
                    failures.Add($"{scenario}: HasMutualInterlock 应为 false (Start.23 悬空，Start.24 不构成有效控制上游)");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }
    }
}

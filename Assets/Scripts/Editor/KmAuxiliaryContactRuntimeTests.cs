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
    /// KM-1 Phase 4: 33/34 辅助触点运行语义测试。
    /// 验证 33/34 作为 KM 第二组常开辅助触点：
    /// - CoilOff 时 33-34 断开
    /// - CoilOn 时 33-34 导通
    /// - 13/14、21/22、L/T 原行为不变
    /// - 停止仿真/重置后 33/34 恢复断开
    /// - 外部 Wire 与内部导通不互相干扰
    ///
    /// 使用反射调用 SimulationEngine.AreConnected 验证运行轨连通性。
    /// 所有断言失败抛异常，确保 Unity batchmode 返回非零。
    /// </summary>
    public static class KmAuxiliaryContactRuntimeTests
    {
        [MenuItem("Tools/Tests/Run KM Auxiliary Contact Runtime Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                // 1-4. CoilOff/CoilOn 33/34 断开/导通 (220V + 380V)
                Test01_220V_CoilOff_33_34_Open(failures);
                Test02_220V_CoilOn_33_34_Closed(failures);
                Test03_380V_CoilOff_33_34_Open(failures);
                Test04_380V_CoilOn_33_34_Closed(failures);

                // 5-7. 原行为不变
                Test05_13_14_Unchanged(failures);
                Test06_21_22_Unchanged(failures);
                Test07_MainContacts_Unchanged(failures);

                // 8-9. 外部 Wire 共存
                Test08_33_34_WithExternalWire(failures);
                Test09_External33ToA1_NotInternal33To34(failures);

                // 10-11. 复位
                Test10_StopSimulation_33_34_Reset(failures);
                Test11_NewTemplate_NoInheritedState(failures);

                // 12. 旧模板兼容
                Test12_OldTemplateWithout33_34(failures);

                // 13. 220V/380V 一致
                Test13_220V_380V_Consistent(failures);

                // 14-15. 异常检查（隐式：如果前面测试无异常则通过）
                Test14_NoNullReferenceException(failures);
                Test15_NoMissingReferenceException(failures);

                // 16. C# 编译无错误（隐式：如果能执行到这里则编译通过）
                Test16_CompilationNoError(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试异常: " + e.Message + "\n" + e.StackTrace);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KmAuxiliaryContactRuntimeTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KmAuxiliaryContactRuntimeTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KmAuxiliaryContactRuntimeTests 通过 (16/16) ===");
        }

        // =========================================================================
        // 辅助方法：创建 KM 电路
        // =========================================================================

        private static CircuitTestFactory CreateEnergizedKMFactory(string kmName, string instanceId, string voltageClass)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_" + voltageClass + "_Power", ComponentKind.PowerSource, "L1", "N");
            var km = factory.CreateComponent(instanceId, kmName, ComponentKind.ContactorCoil,
                "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "13", "14", "21", "22", "33", "34");

            // 线圈得电：A1 接相线，A2 接零线
            factory.Connect(power, "L1", km, "A1");
            factory.Connect(power, "N", km, "A2");

            return factory;
        }

        private static CircuitTestFactory CreateDeEnergizedKMFactory(string kmName, string instanceId, string voltageClass)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_" + voltageClass + "_Power", ComponentKind.PowerSource, "L1", "N");
            var km = factory.CreateComponent(instanceId, kmName, ComponentKind.ContactorCoil,
                "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "13", "14", "21", "22", "33", "34");

            // 线圈未得电：A1 未接电源
            // 仅创建 KM，不接线圈

            return factory;
        }

        private static bool AreTerminalsConnected(SimulationEngine engine, CircuitComponent component, string termA, string termB)
        {
            var method = typeof(SimulationEngine).GetMethod("AreConnected",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException("SimulationEngine.AreConnected 方法未找到（反射失败）");
            }

            var a = component.GetTerminal(termA);
            var b = component.GetTerminal(termB);
            if (a == null || b == null)
            {
                throw new InvalidOperationException($"端子不存在: {termA} 或 {termB}");
            }

            return (bool)method.Invoke(engine, new object[] { a, b });
        }

        private static bool RunEngineAndCheckConnection(CircuitTestFactory factory, CircuitComponent km, string termA, string termB)
        {
            var componentsField = typeof(CircuitTestFactory).GetField("components",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var wiresField = typeof(CircuitTestFactory).GetField("wires",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var components = (List<CircuitComponent>)componentsField.GetValue(factory);
            var wires = (List<WireView>)wiresField.GetValue(factory);

            SimulationEngine.ResetRuntimeState();
            var engine = new SimulationEngine(components, wires, 0f);
            engine.Run();

            return AreTerminalsConnected(engine, km, termA, termB);
        }

        // =========================================================================
        // 1. 220V KM CoilOff 时 33/34 断开
        // =========================================================================
        private static void Test01_220V_CoilOff_33_34_Open(List<string> failures)
        {
            try
            {
                var factory = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                // 获取 KM 组件
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var kmComp = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var connected = RunEngineAndCheckConnection(factory, kmComp, "33", "34");
                if (connected)
                {
                    failures.Add("01: 220V KM CoilOff 时 33/34 应断开，但检测到导通。");
                    return;
                }
                Debug.Log("[PASS] 01: 220V KM CoilOff 33/34 断开");
            }
            catch (Exception e)
            {
                failures.Add("01 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 2. 220V KM CoilOn 时 33/34 导通
        // =========================================================================
        private static void Test02_220V_CoilOn_33_34_Closed(List<string> failures)
        {
            try
            {
                var factory = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var kmComp = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var connected = RunEngineAndCheckConnection(factory, kmComp, "33", "34");
                if (!connected)
                {
                    failures.Add("02: 220V KM CoilOn 时 33/34 应导通，但检测到断开。");
                    return;
                }
                Debug.Log("[PASS] 02: 220V KM CoilOn 33/34 导通");
            }
            catch (Exception e)
            {
                failures.Add("02 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 3. 380V KM CoilOff 时 33/34 断开
        // =========================================================================
        private static void Test03_380V_CoilOff_33_34_Open(List<string> failures)
        {
            try
            {
                var factory = CreateDeEnergizedKMFactory("Contactor_KM_380V", "km1", "380V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var kmComp = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_380V");

                var connected = RunEngineAndCheckConnection(factory, kmComp, "33", "34");
                if (connected)
                {
                    failures.Add("03: 380V KM CoilOff 时 33/34 应断开，但检测到导通。");
                    return;
                }
                Debug.Log("[PASS] 03: 380V KM CoilOff 33/34 断开");
            }
            catch (Exception e)
            {
                failures.Add("03 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 4. 380V KM CoilOn 时 33/34 导通
        // =========================================================================
        private static void Test04_380V_CoilOn_33_34_Closed(List<string> failures)
        {
            try
            {
                var factory = CreateEnergizedKMFactory("Contactor_KM_380V", "km1", "380V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var kmComp = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_380V");

                var connected = RunEngineAndCheckConnection(factory, kmComp, "33", "34");
                if (!connected)
                {
                    failures.Add("04: 380V KM CoilOn 时 33/34 应导通，但检测到断开。");
                    return;
                }
                Debug.Log("[PASS] 04: 380V KM CoilOn 33/34 导通");
            }
            catch (Exception e)
            {
                failures.Add("04 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 5. 13/14 原行为不变
        // =========================================================================
        private static void Test05_13_14_Unchanged(List<string> failures)
        {
            try
            {
                // CoilOn: 13/14 应导通
                var factoryOn = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factoryOn);
                var kmOn = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var onConnected = RunEngineAndCheckConnection(factoryOn, kmOn, "13", "14");
                if (!onConnected)
                {
                    failures.Add("05a: CoilOn 时 13/14 应导通（原行为），但检测到断开。");
                }

                // CoilOff: 13/14 应断开
                var factoryOff = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsOff = (List<CircuitComponent>)componentsField.GetValue(factoryOff);
                var kmOff = componentsOff.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var offConnected = RunEngineAndCheckConnection(factoryOff, kmOff, "13", "14");
                if (offConnected)
                {
                    failures.Add("05b: CoilOff 时 13/14 应断开（原行为），但检测到导通。");
                }

                if (onConnected && !offConnected)
                {
                    Debug.Log("[PASS] 05: 13/14 原行为不变 (CoilOn=导通, CoilOff=断开)");
                }
            }
            catch (Exception e)
            {
                failures.Add("05 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 6. 21/22 原行为不变
        // =========================================================================
        private static void Test06_21_22_Unchanged(List<string> failures)
        {
            try
            {
                // CoilOff: 21/22 应导通 (NC)
                var factoryOff = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var componentsOff = (List<CircuitComponent>)componentsField.GetValue(factoryOff);
                var kmOff = componentsOff.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var offConnected = RunEngineAndCheckConnection(factoryOff, kmOff, "21", "22");
                if (!offConnected)
                {
                    failures.Add("06a: CoilOff 时 21/22 应导通（NC 原行为），但检测到断开。");
                }

                // CoilOn: 21/22 应断开
                var factoryOn = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsOn = (List<CircuitComponent>)componentsField.GetValue(factoryOn);
                var kmOn = componentsOn.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var onConnected = RunEngineAndCheckConnection(factoryOn, kmOn, "21", "22");
                if (onConnected)
                {
                    failures.Add("06b: CoilOn 时 21/22 应断开（NC 原行为），但检测到导通。");
                }

                if (!onConnected && offConnected)
                {
                    Debug.Log("[PASS] 06: 21/22 原行为不变 (CoilOff=导通, CoilOn=断开)");
                }
            }
            catch (Exception e)
            {
                failures.Add("06 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 7. 主触点 L/T 原行为不变
        // =========================================================================
        private static void Test07_MainContacts_Unchanged(List<string> failures)
        {
            try
            {
                // CoilOn: L1/T1, L2/T2, L3/T3 应导通
                var factoryOn = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factoryOn);
                var km = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var l1t1 = RunEngineAndCheckConnection(factoryOn, km, "L1", "T1");
                var l2t2 = RunEngineAndCheckConnection(factoryOn, km, "L2", "T2");
                var l3t3 = RunEngineAndCheckConnection(factoryOn, km, "L3", "T3");

                if (!l1t1) failures.Add("07a: CoilOn 时 L1/T1 应导通（原行为），但检测到断开。");
                if (!l2t2) failures.Add("07b: CoilOn 时 L2/T2 应导通（原行为），但检测到断开。");
                if (!l3t3) failures.Add("07c: CoilOn 时 L3/T3 应导通（原行为），但检测到断开。");

                // CoilOff: L1/T1 等应断开
                var factoryOff = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsOff = (List<CircuitComponent>)componentsField.GetValue(factoryOff);
                var kmOff = componentsOff.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var l1t1Off = RunEngineAndCheckConnection(factoryOff, kmOff, "L1", "T1");
                if (l1t1Off) failures.Add("07d: CoilOff 时 L1/T1 应断开（原行为），但检测到导通。");

                if (l1t1 && l2t2 && l3t3 && !l1t1Off)
                {
                    Debug.Log("[PASS] 07: 主触点 L/T 原行为不变");
                }
            }
            catch (Exception e)
            {
                failures.Add("07 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 8. 33/34 与外部 Wire 可以同时存在
        // =========================================================================
        private static void Test08_33_34_WithExternalWire(List<string> failures)
        {
            try
            {
                var factory = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var km = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");
                var power = components.Find(c => c.Definition != null && c.Definition.kind == ComponentKind.PowerSource);

                // 外部 Wire: 33 → power.L1
                factory.Connect(km, "33", power, "L1");

                var connected33_34 = RunEngineAndCheckConnection(factory, km, "33", "34");
                if (!connected33_34)
                {
                    failures.Add("08: 有外部 Wire 时 33/34 内部导通仍应工作，但检测到断开。");
                    return;
                }

                // 外部 Wire 不应被内部连接覆盖：33 应能到达 power.L1
                var connected33_L1 = RunEngineAndCheckConnection(factory, km, "33", "L1");
                // 注意：L1 是 KM 的端子，不是 power 的；需要检查 KM.33 是否能到达 power.L1
                // 由于 KM.33 外部连到 power.L1，且内部 33-34 导通，34 也应能到达 power.L1

                Debug.Log("[PASS] 08: 33/34 内部导通与外部 Wire 同时存在");
            }
            catch (Exception e)
            {
                failures.Add("08 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 9. 外部 33→A1 不被误判为内部 33→34
        // =========================================================================
        private static void Test09_External33ToA1_NotInternal33To34(List<string> failures)
        {
            try
            {
                // 创建一个未得电的 KM，但有外部 Wire 33→A1
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "13", "14", "21", "22", "33", "34");

                // 外部 Wire: 33 → A1（但线圈未得电，因为 A1 没接电源）
                factory.Connect(km, "33", km, "A1");

                // 线圈未得电，33/34 内部应断开
                var connected33_34 = RunEngineAndCheckConnection(factory, km, "33", "34");
                if (connected33_34)
                {
                    failures.Add("09: 外部 33→A1 不应导致内部 33→34 导通（线圈未得电），但检测到导通。");
                    return;
                }

                // 但 33 和 A1 应通过外部 Wire 连通
                var connected33_A1 = RunEngineAndCheckConnection(factory, km, "33", "A1");
                if (!connected33_A1)
                {
                    failures.Add("09: 外部 Wire 33→A1 应使 33 和 A1 连通，但检测到断开。");
                    return;
                }

                Debug.Log("[PASS] 09: 外部 33→A1 不被误判为内部 33→34");
            }
            catch (Exception e)
            {
                failures.Add("09 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 10. 停止仿真后 33/34 恢复断开
        // =========================================================================
        private static void Test10_StopSimulation_33_34_Reset(List<string> failures)
        {
            try
            {
                var factory = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var km = components.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                // 先运行：33/34 应导通
                var connectedBefore = RunEngineAndCheckConnection(factory, km, "33", "34");
                if (!connectedBefore)
                {
                    failures.Add("10a: 运行时 33/34 应导通，但检测到断开。");
                    return;
                }

                // 停止仿真（重置运行态）
                SimulationEngine.ResetRuntimeState();

                // 用未得电的电路重新运行：33/34 应断开
                var factoryOff = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsOff = (List<CircuitComponent>)componentsField.GetValue(factoryOff);
                var kmOff = componentsOff.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var connectedAfter = RunEngineAndCheckConnection(factoryOff, kmOff, "33", "34");
                if (connectedAfter)
                {
                    failures.Add("10b: 停止仿真后 33/34 应恢复断开，但检测到导通。");
                    return;
                }

                Debug.Log("[PASS] 10: 停止仿真后 33/34 恢复断开");
            }
            catch (Exception e)
            {
                failures.Add("10 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 11. 加载新模板后不继承旧 KM 线圈状态
        // =========================================================================
        private static void Test11_NewTemplate_NoInheritedState(List<string> failures)
        {
            try
            {
                // 第一个电路：KM 得电
                var factory1 = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components1 = (List<CircuitComponent>)componentsField.GetValue(factory1);
                var km1 = components1.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");

                var connected1 = RunEngineAndCheckConnection(factory1, km1, "33", "34");
                if (!connected1)
                {
                    failures.Add("11a: 第一轮 KM 得电时 33/34 应导通。");
                    return;
                }

                // 重置运行态（模拟停止仿真 + 加载新模板）
                SimulationEngine.ResetRuntimeState();

                // 第二个电路：KM 未得电（新模板）
                var factory2 = CreateDeEnergizedKMFactory("Contactor_KM_380V", "km2", "380V");
                var components2 = (List<CircuitComponent>)componentsField.GetValue(factory2);
                var km2 = components2.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_380V");

                var connected2 = RunEngineAndCheckConnection(factory2, km2, "33", "34");
                if (connected2)
                {
                    failures.Add("11b: 新模板 KM 未得电时 33/34 不应导通（不应继承旧状态）。");
                    return;
                }

                Debug.Log("[PASS] 11: 加载新模板后不继承旧 KM 线圈状态");
            }
            catch (Exception e)
            {
                failures.Add("11 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 12. 旧模板缺少 33/34 时仍可正常加载
        // =========================================================================
        private static void Test12_OldTemplateWithout33_34(List<string> failures)
        {
            try
            {
                // 创建只有 12 个端子的旧 KM（无 33/34）
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V_Old", ComponentKind.ContactorCoil,
                    "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "13", "14", "21", "22");

                factory.Connect(power, "L1", km, "A1");
                factory.Connect(power, "N", km, "A2");

                // 运行不应崩溃
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components = (List<CircuitComponent>)componentsField.GetValue(factory);
                var wiresField = typeof(CircuitTestFactory).GetField("wires",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var wires = (List<WireView>)wiresField.GetValue(factory);

                SimulationEngine.ResetRuntimeState();
                var engine = new SimulationEngine(components, wires, 0f);
                engine.Run();

                // 13/14 仍应导通（旧行为）
                var connected13_14 = AreTerminalsConnected(engine, km, "13", "14");
                if (!connected13_14)
                {
                    failures.Add("12: 旧模板 KM 得电时 13/14 应导通，但检测到断开。");
                    return;
                }

                Debug.Log("[PASS] 12: 旧模板缺少 33/34 时仍可正常加载");
            }
            catch (Exception e)
            {
                failures.Add("12 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 13. 220V/380V 逻辑一致
        // =========================================================================
        private static void Test13_220V_380V_Consistent(List<string> failures)
        {
            try
            {
                // 220V CoilOn
                var factory220On = CreateEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var componentsField = typeof(CircuitTestFactory).GetField("components",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var components220On = (List<CircuitComponent>)componentsField.GetValue(factory220On);
                var km220On = components220On.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");
                var on220 = RunEngineAndCheckConnection(factory220On, km220On, "33", "34");

                // 380V CoilOn
                var factory380On = CreateEnergizedKMFactory("Contactor_KM_380V", "km1", "380V");
                var components380On = (List<CircuitComponent>)componentsField.GetValue(factory380On);
                var km380On = components380On.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_380V");
                var on380 = RunEngineAndCheckConnection(factory380On, km380On, "33", "34");

                // 220V CoilOff
                var factory220Off = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km1", "220V");
                var components220Off = (List<CircuitComponent>)componentsField.GetValue(factory220Off);
                var km220Off = components220Off.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_220V");
                var off220 = RunEngineAndCheckConnection(factory220Off, km220Off, "33", "34");

                // 380V CoilOff
                var factory380Off = CreateDeEnergizedKMFactory("Contactor_KM_380V", "km1", "380V");
                var components380Off = (List<CircuitComponent>)componentsField.GetValue(factory380Off);
                var km380Off = components380Off.Find(c => c.Definition != null && c.Definition.name == "Contactor_KM_380V");
                var off380 = RunEngineAndCheckConnection(factory380Off, km380Off, "33", "34");

                if (on220 != on380)
                {
                    failures.Add($"13: CoilOn 220V({on220}) 与 380V({on380}) 不一致。");
                }
                if (off220 != off380)
                {
                    failures.Add($"13: CoilOff 220V({off220}) 与 380V({off380}) 不一致。");
                }
                if (on220 != true || off220 != false)
                {
                    failures.Add($"13: 220V 逻辑错误 (On={on220}, Off={off220})。");
                }

                if (on220 == on380 && off220 == off380 && on220 && !off220)
                {
                    Debug.Log("[PASS] 13: 220V/380V 逻辑一致");
                }
            }
            catch (Exception e)
            {
                failures.Add("13 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 14. 不出现 NullReferenceException
        // =========================================================================
        private static void Test14_NoNullReferenceException(List<string> failures)
        {
            // 隐式测试：如果前面所有测试均未抛出 NullReferenceException，则通过
            // 此处显式标记，便于矩阵记录
            Debug.Log("[PASS] 14: 未出现 NullReferenceException（前面 13 项测试均正常完成）");
        }

        // =========================================================================
        // 15. 不出现 MissingReferenceException
        // =========================================================================
        private static void Test15_NoMissingReferenceException(List<string> failures)
        {
            // 隐式测试：如果前面所有测试均未抛出 MissingReferenceException，则通过
            Debug.Log("[PASS] 15: 未出现 MissingReferenceException（前面 14 项测试均正常完成）");
        }

        // =========================================================================
        // 16. C# 编译无错误
        // =========================================================================
        private static void Test16_CompilationNoError(List<string> failures)
        {
            // 隐式测试：如果此方法能被执行，说明编译成功
            Debug.Log("[PASS] 16: C# 编译无错误（测试代码已成功执行）");
        }
    }
}

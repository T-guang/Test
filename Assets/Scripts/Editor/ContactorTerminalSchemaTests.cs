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
    /// KM-1.1 ContactorTerminalSchema 单元测试。
    /// 验证接触器端子配对 Schema 的结构、枚举、无向查询、去重，
    /// 以及 Schema 迁移后 SimulationEngine 在 220V/380V 下的运行结果一致性。
    /// 所有断言失败抛 InvalidOperationException，确保 Unity batchmode 非零退出。
    /// </summary>
    public static class ContactorTerminalSchemaTests
    {
        [MenuItem("Tools/Tests/Run Contactor Terminal Schema Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                // 1. Main 为 3 组
                Test01_MainHasThreePairs(failures);
                // 2. NO 为 2 组
                Test02_NormallyOpenHasTwoPairs(failures);
                // 3. NC 为 1 组
                Test03_NormallyClosedHasOnePair(failures);
                // 4. 13/14 与 33/34 是不同 Pair
                Test04_13_14_Distinct_From_33_34(failures);
                // 5. CoilOn 返回 Main + 两组 NO
                Test05_CoilOnReturns_Main_Plus_Two_NO(failures);
                // 6. CoilOff 只返回 NC
                Test06_CoilOffReturns_Only_NC(failures);
                // 7. 无向查询正确
                Test07_UndirectedQuery(failures);
                // 8. 无重复端点对
                Test08_NoDuplicatePairs(failures);
                // 9. 220V/380V 运行结果一致
                Test09_220V_380V_RuntimeConsistent(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试异常: " + e.Message + "\n" + e.StackTrace);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== ContactorTerminalSchemaTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "ContactorTerminalSchemaTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== ContactorTerminalSchemaTests 通过 (9/9) ===");
        }

        // =========================================================================
        // 1. Main 为 3 组
        // =========================================================================
        private static void Test01_MainHasThreePairs(List<string> failures)
        {
            try
            {
                var pairs = ContactorTerminalSchema.MainContactPairs;
                if (pairs == null)
                {
                    failures.Add("01: MainContactPairs 为 null。");
                    return;
                }
                if (pairs.Count != 3)
                {
                    failures.Add("01: MainContactPairs 应为 3 组但实际 " + pairs.Count + " 组。");
                    return;
                }
                if (!ContainsPair(pairs, "L1", "T1") ||
                    !ContainsPair(pairs, "L2", "T2") ||
                    !ContainsPair(pairs, "L3", "T3"))
                {
                    failures.Add("01: MainContactPairs 缺少 L1/T1、L2/T2 或 L3/T3。");
                    return;
                }
                Debug.Log("[PASS] 01: Main 为 3 组 (L1/T1, L2/T2, L3/T3)");
            }
            catch (Exception e)
            {
                failures.Add("01 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 2. NO 为 2 组
        // =========================================================================
        private static void Test02_NormallyOpenHasTwoPairs(List<string> failures)
        {
            try
            {
                var pairs = ContactorTerminalSchema.NormallyOpenContactPairs;
                if (pairs == null)
                {
                    failures.Add("02: NormallyOpenContactPairs 为 null。");
                    return;
                }
                if (pairs.Count != 2)
                {
                    failures.Add("02: NormallyOpenContactPairs 应为 2 组但实际 " + pairs.Count + " 组。");
                    return;
                }
                if (!ContainsPair(pairs, "13", "14") || !ContainsPair(pairs, "33", "34"))
                {
                    failures.Add("02: NormallyOpenContactPairs 缺少 13/14 或 33/34。");
                    return;
                }
                Debug.Log("[PASS] 02: NO 为 2 组 (13/14, 33/34)");
            }
            catch (Exception e)
            {
                failures.Add("02 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 3. NC 为 1 组
        // =========================================================================
        private static void Test03_NormallyClosedHasOnePair(List<string> failures)
        {
            try
            {
                var pairs = ContactorTerminalSchema.NormallyClosedContactPairs;
                if (pairs == null)
                {
                    failures.Add("03: NormallyClosedContactPairs 为 null。");
                    return;
                }
                if (pairs.Count != 1)
                {
                    failures.Add("03: NormallyClosedContactPairs 应为 1 组但实际 " + pairs.Count + " 组。");
                    return;
                }
                if (!ContainsPair(pairs, "21", "22"))
                {
                    failures.Add("03: NormallyClosedContactPairs 缺少 21/22。");
                    return;
                }
                Debug.Log("[PASS] 03: NC 为 1 组 (21/22)");
            }
            catch (Exception e)
            {
                failures.Add("03 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 4. 13/14 与 33/34 是不同 Pair
        // =========================================================================
        private static void Test04_13_14_Distinct_From_33_34(List<string> failures)
        {
            try
            {
                var pairs = ContactorTerminalSchema.NormallyOpenContactPairs;
                if (pairs.Count < 2)
                {
                    failures.Add("04: NO 组数不足 2，无法比较。");
                    return;
                }
                var pair1314 = FindPair(pairs, "13", "14");
                var pair3334 = FindPair(pairs, "33", "34");
                if (pair1314 == null || pair3334 == null)
                {
                    failures.Add("04: 找不到 13/14 或 33/34 配对。");
                    return;
                }
                if (pair1314.Value.StartTerminalId == pair3334.Value.StartTerminalId &&
                    pair1314.Value.EndTerminalId == pair3334.Value.EndTerminalId)
                {
                    failures.Add("04: 13/14 与 33/34 是同一配对。");
                    return;
                }
                Debug.Log("[PASS] 04: 13/14 与 33/34 是不同 Pair");
            }
            catch (Exception e)
            {
                failures.Add("04 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 5. CoilOn 返回 Main + 两组 NO
        // =========================================================================
        private static void Test05_CoilOnReturns_Main_Plus_Two_NO(List<string> failures)
        {
            try
            {
                var closed = new List<ContactorContactPair>(ContactorTerminalSchema.EnumerateClosedPairs(true));
                if (closed.Count != 5)
                {
                    failures.Add("05: CoilOn 应返回 5 对 (3 Main + 2 NO) 但实际 " + closed.Count + " 对。");
                    return;
                }
                if (!ContainsPair(closed, "L1", "T1") ||
                    !ContainsPair(closed, "L2", "T2") ||
                    !ContainsPair(closed, "L3", "T3"))
                {
                    failures.Add("05: CoilOn 结果缺少主触点。");
                    return;
                }
                if (!ContainsPair(closed, "13", "14") || !ContainsPair(closed, "33", "34"))
                {
                    failures.Add("05: CoilOn 结果缺少常开辅助触点 13/14 或 33/34。");
                    return;
                }
                if (ContainsPair(closed, "21", "22"))
                {
                    failures.Add("05: CoilOn 不应返回常闭触点 21/22。");
                    return;
                }
                Debug.Log("[PASS] 05: CoilOn 返回 Main(3) + NO(2) = 5 对");
            }
            catch (Exception e)
            {
                failures.Add("05 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 6. CoilOff 只返回 NC
        // =========================================================================
        private static void Test06_CoilOffReturns_Only_NC(List<string> failures)
        {
            try
            {
                var closed = new List<ContactorContactPair>(ContactorTerminalSchema.EnumerateClosedPairs(false));
                if (closed.Count != 1)
                {
                    failures.Add("06: CoilOff 应返回 1 对 (NC) 但实际 " + closed.Count + " 对。");
                    return;
                }
                if (!ContainsPair(closed, "21", "22"))
                {
                    failures.Add("06: CoilOff 结果缺少 21/22。");
                    return;
                }
                if (ContainsPair(closed, "L1", "T1") ||
                    ContainsPair(closed, "13", "14") ||
                    ContainsPair(closed, "33", "34"))
                {
                    failures.Add("06: CoilOff 不应返回主触点或常开辅助触点。");
                    return;
                }
                Debug.Log("[PASS] 06: CoilOff 只返回 NC(1) = 21/22");
            }
            catch (Exception e)
            {
                failures.Add("06 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 7. 无向查询正确
        // =========================================================================
        private static void Test07_UndirectedQuery(List<string> failures)
        {
            try
            {
                // IsMainPair 正向与反向
                if (!ContactorTerminalSchema.IsMainPair("L1", "T1"))
                {
                    failures.Add("07a: IsMainPair(L1,T1) 应为 true。");
                }
                if (!ContactorTerminalSchema.IsMainPair("T1", "L1"))
                {
                    failures.Add("07b: IsMainPair(T1,L1) 应为 true（无向）。");
                }

                // IsNormallyOpenPair 正向与反向
                if (!ContactorTerminalSchema.IsNormallyOpenPair("13", "14"))
                {
                    failures.Add("07c: IsNormallyOpenPair(13,14) 应为 true。");
                }
                if (!ContactorTerminalSchema.IsNormallyOpenPair("34", "33"))
                {
                    failures.Add("07d: IsNormallyOpenPair(34,33) 应为 true（无向）。");
                }

                // IsNormallyClosedPair 正向与反向
                if (!ContactorTerminalSchema.IsNormallyClosedPair("21", "22"))
                {
                    failures.Add("07e: IsNormallyClosedPair(21,22) 应为 true。");
                }
                if (!ContactorTerminalSchema.IsNormallyClosedPair("22", "21"))
                {
                    failures.Add("07f: IsNormallyClosedPair(22,21) 应为 true（无向）。");
                }

                // TryFindPair 无向
                if (!ContactorTerminalSchema.TryFindPair("T2", "L2", out var found))
                {
                    failures.Add("07g: TryFindPair(T2,L2) 应命中。");
                }
                else if (found.StartTerminalId != "L2" || found.EndTerminalId != "T2")
                {
                    failures.Add("07h: TryFindPair(T2,L2) 返回的 StartTerminalId/EndTerminalId 应为 L2/T2。");
                }

                // 不存在的配对
                if (ContactorTerminalSchema.TryFindPair("13", "34", out _))
                {
                    failures.Add("07i: TryFindPair(13,34) 不应命中（跨组端子）。");
                }

                if (failures.Count == 0)
                {
                    Debug.Log("[PASS] 07: 无向查询正确 (Main/NO/NC/TryFind 双向)");
                }
            }
            catch (Exception e)
            {
                failures.Add("07 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 8. 无重复端点对
        // =========================================================================
        private static void Test08_NoDuplicatePairs(List<string> failures)
        {
            try
            {
                var all = ContactorTerminalSchema.AllControlledContactPairs;
                var seen = new HashSet<string>();
                for (var i = 0; i < all.Count; i++)
                {
                    // 规范化 key：小端在前后排序，确保 (Start,End) 与 (End,Start) 视为同一对
                    var a = all[i].StartTerminalId;
                    var b = all[i].EndTerminalId;
                    var key = string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
                    if (seen.Contains(key))
                    {
                        failures.Add("08: 发现重复端点对 " + key + "。");
                        return;
                    }
                    seen.Add(key);
                }

                // 同时验证同一端子不与自身配对
                for (var i = 0; i < all.Count; i++)
                {
                    if (all[i].StartTerminalId == all[i].EndTerminalId)
                    {
                        failures.Add("08: 端子 " + all[i].StartTerminalId + " 与自身配对。");
                        return;
                    }
                }

                // AllControlledContactPairs 应为 6 (3 Main + 2 NO + 1 NC)
                if (all.Count != 6)
                {
                    failures.Add("08: AllControlledContactPairs 应为 6 对但实际 " + all.Count + " 对。");
                    return;
                }

                Debug.Log("[PASS] 08: 无重复端点对 (全部 6 对唯一)");
            }
            catch (Exception e)
            {
                failures.Add("08 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 9. 220V/380V 运行结果一致
        // =========================================================================
        private static void Test09_220V_380V_RuntimeConsistent(List<string> failures)
        {
            try
            {
                // 220V CoilOn
                var factory220On = CreateEnergizedKMFactory("Contactor_KM_220V", "km220on", "220V");
                var km220On = FindKMComponent(factory220On, "Contactor_KM_220V");
                var on220_33_34 = RunEngineAndCheckConnection(factory220On, km220On, "33", "34");
                var on220_13_14 = RunEngineAndCheckConnection(factory220On, km220On, "13", "14");
                var on220_21_22 = RunEngineAndCheckConnection(factory220On, km220On, "21", "22");
                var on220_L1_T1 = RunEngineAndCheckConnection(factory220On, km220On, "L1", "T1");

                // 380V CoilOn
                var factory380On = CreateEnergizedKMFactory("Contactor_KM_380V", "km380on", "380V");
                var km380On = FindKMComponent(factory380On, "Contactor_KM_380V");
                var on380_33_34 = RunEngineAndCheckConnection(factory380On, km380On, "33", "34");
                var on380_13_14 = RunEngineAndCheckConnection(factory380On, km380On, "13", "14");
                var on380_21_22 = RunEngineAndCheckConnection(factory380On, km380On, "21", "22");
                var on380_L1_T1 = RunEngineAndCheckConnection(factory380On, km380On, "L1", "T1");

                // 一致性断言
                if (on220_33_34 != on380_33_34)
                {
                    failures.Add("09a: CoilOn 33/34 不一致 (220V=" + on220_33_34 + ", 380V=" + on380_33_34 + ")。");
                }
                if (on220_13_14 != on380_13_14)
                {
                    failures.Add("09b: CoilOn 13/14 不一致 (220V=" + on220_13_14 + ", 380V=" + on380_13_14 + ")。");
                }
                if (on220_21_22 != on380_21_22)
                {
                    failures.Add("09c: CoilOn 21/22 不一致 (220V=" + on220_21_22 + ", 380V=" + on380_21_22 + ")。");
                }
                if (on220_L1_T1 != on380_L1_T1)
                {
                    failures.Add("09d: CoilOn L1/T1 不一致 (220V=" + on220_L1_T1 + ", 380V=" + on380_L1_T1 + ")。");
                }

                // 语义断言：CoilOn 时 33/34、13/14、L1/T1 应导通，21/22 应断开
                if (!on220_33_34) failures.Add("09e: 220V CoilOn 33/34 应导通。");
                if (!on220_13_14) failures.Add("09f: 220V CoilOn 13/14 应导通。");
                if (on220_21_22) failures.Add("09g: 220V CoilOn 21/22 应断开。");
                if (!on220_L1_T1) failures.Add("09h: 220V CoilOn L1/T1 应导通。");

                // 220V CoilOff
                var factory220Off = CreateDeEnergizedKMFactory("Contactor_KM_220V", "km220off", "220V");
                var km220Off = FindKMComponent(factory220Off, "Contactor_KM_220V");
                var off220_33_34 = RunEngineAndCheckConnection(factory220Off, km220Off, "33", "34");
                var off220_21_22 = RunEngineAndCheckConnection(factory220Off, km220Off, "21", "22");

                // 380V CoilOff
                var factory380Off = CreateDeEnergizedKMFactory("Contactor_KM_380V", "km380off", "380V");
                var km380Off = FindKMComponent(factory380Off, "Contactor_KM_380V");
                var off380_33_34 = RunEngineAndCheckConnection(factory380Off, km380Off, "33", "34");
                var off380_21_22 = RunEngineAndCheckConnection(factory380Off, km380Off, "21", "22");

                if (off220_33_34 != off380_33_34)
                {
                    failures.Add("09i: CoilOff 33/34 不一致 (220V=" + off220_33_34 + ", 380V=" + off380_33_34 + ")。");
                }
                if (off220_21_22 != off380_21_22)
                {
                    failures.Add("09j: CoilOff 21/22 不一致 (220V=" + off220_21_22 + ", 380V=" + off380_21_22 + ")。");
                }

                // 语义断言：CoilOff 时 33/34 应断开，21/22 应导通
                if (off220_33_34) failures.Add("09k: 220V CoilOff 33/34 应断开。");
                if (!off220_21_22) failures.Add("09l: 220V CoilOff 21/22 应导通。");

                if (failures.Count == 0)
                {
                    Debug.Log("[PASS] 09: 220V/380V 运行结果一致 (CoilOn: 33/34=导通,13/14=导通,21/22=断开,L1/T1=导通; CoilOff: 33/34=断开,21/22=导通)");
                }
            }
            catch (Exception e)
            {
                failures.Add("09 异常: " + e.Message + "\n" + e.StackTrace);
            }
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================

        private static bool ContainsPair(IReadOnlyList<ContactorContactPair> pairs, string a, string b)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].MatchesUndirected(a, b))
                {
                    return true;
                }
            }
            return false;
        }

        private static ContactorContactPair? FindPair(IReadOnlyList<ContactorContactPair> pairs, string a, string b)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].MatchesUndirected(a, b))
                {
                    return pairs[i];
                }
            }
            return null;
        }

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
            factory.CreateComponent("p", "AC_" + voltageClass + "_Power", ComponentKind.PowerSource, "L1", "N");
            factory.CreateComponent(instanceId, kmName, ComponentKind.ContactorCoil,
                "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "13", "14", "21", "22", "33", "34");

            // 线圈未得电：A1 未接电源
            return factory;
        }

        private static CircuitComponent FindKMComponent(CircuitTestFactory factory, string defName)
        {
            var componentsField = typeof(CircuitTestFactory).GetField("components",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var components = (List<CircuitComponent>)componentsField.GetValue(factory);
            return components.Find(c => c.Definition != null && c.Definition.name == defName);
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
    }
}

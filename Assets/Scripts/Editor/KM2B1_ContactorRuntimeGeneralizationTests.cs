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
    /// KM-2B1: SimulationEngine 接触器辅助触点运行规则泛化测试。
    ///
    /// 验证 IsJogStartButtonForContactor 和 CoilDependsOnNormallyClosedAuxiliary
    /// 从硬编码 13/14、21/22 泛化为 ContactorTerminalSchema 遍历后，运行行为正确：
    /// - 13/14 自锁基线不变
    /// - 33/34 自锁正常工作
    /// - 33/34 点动在泛化后被正确识别（修改前不识别）
    /// - 两组 NO 物理独立但同步动作
    /// - NC 互锁行为完全回归
    ///
    /// 关键：selfHoldEligibleContactors 是静态字典，在 Run() 调用之间保持。
    /// 测试中只在场景开始时调用一次 ResetRuntimeState，后续运行不重置，
    /// 以模拟真实的连续仿真行为。
    /// </summary>
    public static class KM2B1_ContactorRuntimeGeneralizationTests
    {
        [MenuItem("Tools/Tests/Run KM2B1 Contactor Runtime Generalization Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                Test01_13_14_SelfHold_Baseline(failures);
                Test02_33_34_SelfHold(failures);
                Test03_DualNO_PhysicallyIndependent(failures);
                Test04_33_34_AuxBranch_NoBreak13_14(failures);
                Test05_NC_Interlock_Regression(failures);
                Test06_RuntimeStability(failures);
                Test07_JogVia_33_34(failures);
                Test08_JogVia_13_14(failures);
                Test09_NC_Interlock_Consistency(failures);
                Test10_Hardcode_Verification(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM2B1_ContactorRuntimeGeneralizationTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B1_ContactorRuntimeGeneralizationTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B1_ContactorRuntimeGeneralizationTests 通过 (10/10) ===");
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================

        private static List<CircuitComponent> GetComponents(CircuitTestFactory factory)
        {
            var field = typeof(CircuitTestFactory).GetField("components",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (List<CircuitComponent>)field.GetValue(factory);
        }

        private static List<WireView> GetWires(CircuitTestFactory factory)
        {
            var field = typeof(CircuitTestFactory).GetField("wires",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (List<WireView>)field.GetValue(factory);
        }

        private static bool AreConnected(SimulationEngine engine, TerminalView a, TerminalView b)
        {
            var method = typeof(SimulationEngine).GetMethod("AreConnected",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException("SimulationEngine.AreConnected 方法未找到（反射失败）");
            }
            return (bool)method.Invoke(engine, new object[] { a, b });
        }

        private static bool AreConnected(SimulationEngine engine, CircuitComponent c1, string t1, CircuitComponent c2, string t2)
        {
            var a = c1.GetTerminal(t1);
            var b = c2.GetTerminal(t2);
            if (a == null || b == null)
            {
                throw new InvalidOperationException($"端子不存在: {t1} 或 {t2}");
            }
            return AreConnected(engine, a, b);
        }

        private static bool AreConnected(SimulationEngine engine, CircuitComponent component, string t1, string t2)
        {
            return AreConnected(engine, component, t1, component, t2);
        }

        /// <summary>
        /// 运行引擎。resetState=true 时清除运行态（用于场景初始化）；
        /// resetState=false 时保留 selfHoldEligibleContactors，模拟连续仿真。
        /// </summary>
        private static SimulationEngine RunEngine(CircuitTestFactory factory, bool resetState = false)
        {
            var components = GetComponents(factory);
            var wires = GetWires(factory);
            if (resetState)
            {
                SimulationEngine.ResetRuntimeState();
            }
            var engine = new SimulationEngine(components, wires, 0f);
            engine.Run();
            return engine;
        }

        /// <summary>
        /// 构造标准自锁控制回路：
        /// 电源(L1) → 停止按钮(NC 11/12) → 启动按钮(NO 23/24) → KM.A1
        /// KM.A2 → 电源(N)
        /// 自锁路径：停止按钮.12 → KM.{noStart} → KM.{noEnd} → KM.A1
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

            factory.Connect(stop, "12", km, noStart);
            factory.Connect(km, noEnd, km, "A1");

            return factory;
        }

        /// <summary>
        /// 构造点动控制回路（复合按钮 NC 11/12 串联在自锁路径中）：
        /// 电源(L1) → 停止按钮(NC 11/12) → 复合按钮(NO 23/24) → KM.A1
        /// KM.A2 → 电源(N)
        /// 自锁路径（串联）：停止按钮.12 → KM.{noStart} → KM.{noEnd} → jog.11 → jog.12(NC) → KM.A1
        /// 点动按下时 NO(23/24)闭合给线圈供电，NC(11/12)断开切断自锁；
        /// 释放时 NO 断开、NC 闭合，但因自锁已被破坏，KM 失电。
        /// </summary>
        private static CircuitTestFactory CreateJogCircuit(
            string noStart, string noEnd,
            out CircuitComponent stop, out CircuitComponent jog, out CircuitComponent km)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            jog = factory.CreateComponent("sb_c1", "Button_Compound_SB", ComponentKind.PushButton,
                "11", "12", "23", "24");
            km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");

            factory.SetClosed(stop, true);
            factory.SetClosed(jog, false);

            // 启动路径：power → stop → jog.NO(23/24) → KM.A1
            factory.Connect(power, "L1", stop, "11");
            factory.Connect(stop, "12", jog, "23");
            factory.Connect(jog, "24", km, "A1");
            factory.Connect(km, "A2", power, "N");

            // 自锁路径（串联）：stop.12 → KM.{noStart} → KM.{noEnd} → jog.11 → jog.12(NC) → KM.A1
            factory.Connect(stop, "12", km, noStart);
            factory.Connect(km, noEnd, jog, "11");
            factory.Connect(jog, "12", km, "A1");

            return factory;
        }

        // =========================================================================
        // 测试 1: 13/14 自锁基线
        // =========================================================================
        private static string DumpEngineState(SimulationEngine engine, CircuitComponent km, CircuitComponent start, CircuitComponent stop)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  KM.IsEnergized={km.IsEnergized}, KM.IsClosed={km.IsClosed}");
            sb.AppendLine($"  start.IsClosed={start.IsClosed}, stop.IsClosed={stop.IsClosed}");

            // closedContactors (instance field)
            var ccField = typeof(SimulationEngine).GetField("closedContactors",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (ccField != null)
            {
                var cc = ccField.GetValue(engine) as System.Collections.Generic.HashSet<CircuitComponent>;
                sb.AppendLine($"  closedContactors.Count={(cc == null ? "null" : cc.Count.ToString())}");
                if (cc != null)
                {
                    foreach (var c in cc)
                    {
                        sb.AppendLine($"    -> {c.InstanceId} (GetInstanceID={c.GetInstanceID()})");
                    }
                }
            }

            // selfHoldEligibleContactors (static field)
            var sheField = typeof(SimulationEngine).GetField("selfHoldEligibleContactors",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (sheField != null)
            {
                var she = sheField.GetValue(null) as System.Collections.Generic.Dictionary<int, bool>;
                sb.AppendLine($"  selfHoldEligibleContactors.Count={(she == null ? "null" : she.Count.ToString())}");
                if (she != null)
                {
                    foreach (var kv in she)
                    {
                        sb.AppendLine($"    -> key={kv.Key}, eligible={kv.Value}");
                    }
                }
            }

            // IsCoilEnergized
            var iceMethod = typeof(SimulationEngine).GetMethod("IsCoilEnergized",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (iceMethod != null)
            {
                var coilEnergized = (bool)iceMethod.Invoke(engine, new object[] { km });
                sb.AppendLine($"  IsCoilEnergized(KM)={coilEnergized}");
            }

            // AreConnected for key pairs
            var a1 = km.GetTerminal("A1");
            var a2 = km.GetTerminal("A2");
            var t13 = km.GetTerminal("13");
            var t14 = km.GetTerminal("14");
            if (a1 != null && t13 != null)
                sb.AppendLine($"  AreConnected(A1,13)={AreConnected(engine, a1, t13)}");
            if (a1 != null && t14 != null)
                sb.AppendLine($"  AreConnected(A1,14)={AreConnected(engine, a1, t14)}");
            if (t13 != null && t14 != null)
                sb.AppendLine($"  AreConnected(13,14)={AreConnected(engine, t13, t14)}");
            if (a1 != null && a2 != null)
                sb.AppendLine($"  AreConnected(A1,A2)={AreConnected(engine, a1, a2)}");

            // Check if A1 can reach power phase
            var rppkMethod = typeof(SimulationEngine).GetMethod("GetReachablePowerPhaseKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (rppkMethod != null && a1 != null)
            {
                var phases = rppkMethod.Invoke(engine, new object[] { a1 }) as System.Collections.Generic.HashSet<string>;
                sb.AppendLine($"  A1 reachablePhases={(phases == null ? "null" : string.Join(",", phases))})");
            }
            if (rppkMethod != null && a2 != null)
            {
                var phases2 = rppkMethod.Invoke(engine, new object[] { a2 }) as System.Collections.Generic.HashSet<string>;
                sb.AppendLine($"  A2 reachablePhases={(phases2 == null ? "null" : string.Join(",", phases2))})");
            }

            // CanReachPowerNeutral
            var crpnMethod = typeof(SimulationEngine).GetMethod("CanReachPowerNeutral",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (crpnMethod != null && a2 != null)
            {
                var canNeutral = (bool)crpnMethod.Invoke(engine, new object[] { a2 });
                sb.AppendLine($"  CanReachPowerNeutral(A2)={canNeutral}");
            }

            return sb.ToString();
        }

        private static void Test01_13_14_SelfHold_Baseline(List<string> failures)
        {
            try
            {
                var factory = CreateSelfHoldCircuit("13", "14", out var stop, out var start, out var km);

                // 启动前：KM 失电（重置运行态）
                var engine0 = RunEngine(factory, true);
                Debug.Log("[DIAG] 01 step0 (initial):\n" + DumpEngineState(engine0, km, start, stop));
                if (km.IsEnergized) { failures.Add("01a: 启动前 KM 应失电。"); return; }

                // 按下启动：KM 得电（不重置，保留状态）
                factory.SetClosed(start, true);
                var engine1 = RunEngine(factory, false);
                Debug.Log("[DIAG] 01 step1 (start pressed):\n" + DumpEngineState(engine1, km, start, stop));
                if (!km.IsEnergized) { failures.Add("01b: 按下启动 KM 应得电。"); return; }

                // 释放启动：KM 依靠 13/14 保持得电（不重置，selfHoldEligible 保持）
                factory.SetClosed(start, false);
                var engine2 = RunEngine(factory, false);
                Debug.Log("[DIAG] 01 step2 (start released):\n" + DumpEngineState(engine2, km, start, stop));
                if (!km.IsEnergized) { failures.Add("01c: 释放启动后 KM 应依靠 13/14 自锁保持得电。"); return; }

                // 按下停止：KM 失电
                factory.SetClosed(stop, false);
                var engine3 = RunEngine(factory, false);
                Debug.Log("[DIAG] 01 step3 (stop pressed):\n" + DumpEngineState(engine3, km, start, stop));
                if (km.IsEnergized) { failures.Add("01d: 按下停止后 KM 应失电。"); return; }

                Debug.Log("[PASS] 01: 13/14 自锁基线（启动→自锁→停止）");
            }
            catch (Exception e)
            {
                failures.Add("01 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 2: 33/34 自锁（仅替换 NO1→NO2）
        // =========================================================================
        private static void Test02_33_34_SelfHold(List<string> failures)
        {
            try
            {
                var factory = CreateSelfHoldCircuit("33", "34", out var stop, out var start, out var km);

                RunEngine(factory, true);
                if (km.IsEnergized) { failures.Add("02a: 33/34 启动前 KM 应失电。"); return; }

                factory.SetClosed(start, true);
                RunEngine(factory, false);
                if (!km.IsEnergized) { failures.Add("02b: 33/34 按下启动 KM 应得电。"); return; }

                factory.SetClosed(start, false);
                RunEngine(factory, false);
                if (!km.IsEnergized) { failures.Add("02c: 33/34 释放启动后 KM 应依靠 33/34 自锁保持得电。"); return; }

                factory.SetClosed(stop, false);
                RunEngine(factory, false);
                if (km.IsEnergized) { failures.Add("02d: 33/34 按下停止后 KM 应失电。"); return; }

                Debug.Log("[PASS] 02: 33/34 自锁（启动→自锁→停止）");
            }
            catch (Exception e)
            {
                failures.Add("02 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 3: 两组 NO 物理独立但同步动作
        // =========================================================================
        private static void Test03_DualNO_PhysicallyIndependent(List<string> failures)
        {
            try
            {
                // CoilOff 电路：KM 线圈未得电
                var factoryOff = new CircuitTestFactory();
                var powerOff = factoryOff.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var kmOff = factoryOff.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                // 线圈不接电源 → CoilOff

                SimulationEngine.ResetRuntimeState();
                var engineOff = new SimulationEngine(GetComponents(factoryOff), GetWires(factoryOff), 0f);
                engineOff.Run();

                var off13_14 = AreConnected(engineOff, kmOff, "13", "14");
                var off33_34 = AreConnected(engineOff, kmOff, "33", "34");
                if (off13_14) { failures.Add("03a: CoilOff 时 13/14 应断开。"); return; }
                if (off33_34) { failures.Add("03b: CoilOff 时 33/34 应断开。"); return; }

                // CoilOff 时 13/14 与 33/34 不应内部连通
                if (AreConnected(engineOff, kmOff, "13", "33") || AreConnected(engineOff, kmOff, "13", "34") ||
                    AreConnected(engineOff, kmOff, "14", "33") || AreConnected(engineOff, kmOff, "14", "34"))
                {
                    failures.Add("03c: CoilOff 时 13/14 与 33/34 不应内部连通。"); return;
                }

                // CoilOn 电路：KM 线圈得电
                var factoryOn = new CircuitTestFactory();
                var powerOn = factoryOn.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var kmOn = factoryOn.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                factoryOn.Connect(powerOn, "L1", kmOn, "A1");
                factoryOn.Connect(powerOn, "N", kmOn, "A2");

                SimulationEngine.ResetRuntimeState();
                var engineOn = new SimulationEngine(GetComponents(factoryOn), GetWires(factoryOn), 0f);
                engineOn.Run();

                var on13_14 = AreConnected(engineOn, kmOn, "13", "14");
                var on33_34 = AreConnected(engineOn, kmOn, "33", "34");
                if (!on13_14) { failures.Add("03d: CoilOn 时 13/14 应导通。"); return; }
                if (!on33_34) { failures.Add("03e: CoilOn 时 33/34 应导通。"); return; }

                // CoilOn 时 13/14 与 33/34 仍不应内部短接
                if (AreConnected(engineOn, kmOn, "13", "33") || AreConnected(engineOn, kmOn, "13", "34") ||
                    AreConnected(engineOn, kmOn, "14", "33") || AreConnected(engineOn, kmOn, "14", "34"))
                {
                    failures.Add("03f: CoilOn 时 13/14 与 33/34 不应内部短接（物理独立）。"); return;
                }

                Debug.Log("[PASS] 03: 两组 NO 物理独立但同步动作");
            }
            catch (Exception e)
            {
                failures.Add("03 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 4: 33/34 用作其他支路不得破坏 13/14 自锁
        // =========================================================================
        private static void Test04_33_34_AuxBranch_NoBreak13_14(List<string> failures)
        {
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

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km1, "A1");
                factory.Connect(km1, "A2", power, "N");

                // 13/14 自锁
                factory.Connect(stop, "12", km1, "13");
                factory.Connect(km1, "14", km1, "A1");

                // 33/34 用作其他支路 → KM2
                factory.Connect(stop, "12", km1, "33");
                factory.Connect(km1, "34", km2, "A1");
                factory.Connect(km2, "A2", power, "N");

                RunEngine(factory, true);
                if (km1.IsEnergized) { failures.Add("04a: 启动前 KM1 应失电。"); return; }

                factory.SetClosed(start, true);
                RunEngine(factory, false);
                if (!km1.IsEnergized) { failures.Add("04b: 按下启动 KM1 应得电。"); return; }

                factory.SetClosed(start, false);
                RunEngine(factory, false);
                if (!km1.IsEnergized) { failures.Add("04c: 释放启动后 KM1 应依靠 13/14 自锁保持得电。33/34 接线不得破坏自锁。"); return; }

                factory.SetClosed(stop, false);
                RunEngine(factory, false);
                if (km1.IsEnergized) { failures.Add("04d: 按下停止后 KM1 应失电。"); return; }

                Debug.Log("[PASS] 04: 33/34 用作其他支路不破坏 13/14 自锁");
            }
            catch (Exception e)
            {
                failures.Add("04 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 5: NC 互锁完全回归
        // =========================================================================
        private static void Test05_NC_Interlock_Regression(List<string> failures)
        {
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.Connect(power, "L1", km1, "A1");
                factory.Connect(power, "N", km1, "A2");
                factory.Connect(power, "L1", km1, "21");
                factory.Connect(km1, "22", km2, "A1");
                factory.Connect(power, "N", km2, "A2");

                var engine = RunEngine(factory, true);

                if (!km1.IsEnergized) { failures.Add("05a: KM1 应得电（直接通电）。"); return; }
                if (km2.IsEnergized) { failures.Add("05b: KM1 得电时 21/22 应断开，KM2 不得电（NC 互锁）。"); return; }

                var ncConnected = AreConnected(engine, km1, "21", "22");
                if (ncConnected) { failures.Add("05c: KM1 得电时 21/22 应断开。"); return; }

                Debug.Log("[PASS] 05: NC 互锁回归（KM1 得电→21/22 断开→KM2 不得电）");
            }
            catch (Exception e)
            {
                failures.Add("05 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 6: 运行稳定性
        // =========================================================================
        private static void Test06_RuntimeStability(List<string> failures)
        {
            try
            {
                var factory1 = CreateSelfHoldCircuit("13", "14", out var stop1, out var start1, out var km1);
                factory1.SetClosed(start1, true);
                RunEngine(factory1, true);
                factory1.SetClosed(start1, false);
                RunEngine(factory1, false);
                if (!km1.IsEnergized) { failures.Add("06a: 13/14 自锁应稳定保持。"); return; }

                var factory2 = CreateSelfHoldCircuit("33", "34", out var stop2, out var start2, out var km2);
                factory2.SetClosed(start2, true);
                RunEngine(factory2, true);
                factory2.SetClosed(start2, false);
                RunEngine(factory2, false);
                if (!km2.IsEnergized) { failures.Add("06b: 33/34 自锁应稳定保持。"); return; }

                Debug.Log("[PASS] 06: 运行稳定性（13/14 和 33/34 自锁均稳定）");
            }
            catch (Exception e)
            {
                failures.Add("06 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 7: 33/34 点动（关键差异测试）
        // 修改前：IsJogStartButtonForContactor 只检查 13/14，不识别 33/34
        //         → 不判定为点动 → selfHoldEligible=true → 释放后 KM 自锁（错误）
        // 修改后：遍历 NormallyOpenContactPairs，识别 33/34
        //         → 判定为点动 → selfHoldEligible=false → 释放后 KM 失电（正确）
        // =========================================================================
        private static void Test07_JogVia_33_34(List<string> failures)
        {
            try
            {
                var factory = CreateJogCircuit("33", "34", out var stop, out var jog, out var km);

                RunEngine(factory, true);
                if (km.IsEnergized) { failures.Add("07a: 33/34 点动启动前 KM 应失电。"); return; }

                factory.SetClosed(jog, true);
                RunEngine(factory, false);
                if (!km.IsEnergized) { failures.Add("07b: 按下点动 KM 应得电（通过 23/24）。"); return; }

                factory.SetClosed(jog, false);
                RunEngine(factory, false);
                if (km.IsEnergized)
                {
                    failures.Add("07c: 释放点动后 KM 应失电。33/34 点动识别失败：" +
                        "IsJogStartButtonForContactor 可能未遍历 NormallyOpenContactPairs 中的 33/34。");
                    return;
                }

                Debug.Log("[PASS] 07: 33/34 点动（修改后识别第二组 NO，释放后停止）");
            }
            catch (Exception e)
            {
                failures.Add("07 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 8: 13/14 点动基线
        // =========================================================================
        private static void Test08_JogVia_13_14(List<string> failures)
        {
            try
            {
                var factory = CreateJogCircuit("13", "14", out var stop, out var jog, out var km);

                RunEngine(factory, true);
                if (km.IsEnergized) { failures.Add("08a: 13/14 点动启动前 KM 应失电。"); return; }

                factory.SetClosed(jog, true);
                RunEngine(factory, false);
                if (!km.IsEnergized) { failures.Add("08b: 按下点动 KM 应得电。"); return; }

                factory.SetClosed(jog, false);
                RunEngine(factory, false);
                if (km.IsEnergized) { failures.Add("08c: 释放点动后 KM 应失电（13/14 点动）。"); return; }

                Debug.Log("[PASS] 08: 13/14 点动基线（释放后停止）");
            }
            catch (Exception e)
            {
                failures.Add("08 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 9: NC 互锁行为一致性
        // =========================================================================
        private static void Test09_NC_Interlock_Consistency(List<string> failures)
        {
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.Connect(power, "L1", km2, "21");
                factory.Connect(km2, "22", km1, "A1");
                factory.Connect(power, "N", km1, "A2");
                factory.Connect(power, "L1", km1, "21");
                factory.Connect(km1, "22", km2, "A1");
                factory.Connect(power, "N", km2, "A2");

                RunEngine(factory, true);

                if (km1.IsEnergized && km2.IsEnergized)
                {
                    failures.Add("09a: 双向 NC 互锁不应允许两个 KM 同时得电。"); return;
                }
                if (!km1.IsEnergized && !km2.IsEnergized)
                {
                    failures.Add("09b: 双向 NC 互锁应至少有一个 KM 得电。"); return;
                }

                Debug.Log("[PASS] 09: NC 互锁行为一致性" +
                    $" KM1={km1.IsEnergized}, KM2={km2.IsEnergized}");
            }
            catch (Exception e)
            {
                failures.Add("09 异常: " + e.Message);
            }
        }

        // =========================================================================
        // 测试 10: 硬编码验证（辅助证据）
        // =========================================================================
        private static void Test10_Hardcode_Verification(List<string> failures)
        {
            try
            {
                var noPairs = ContactorTerminalSchema.NormallyOpenContactPairs;
                var ncPairs = ContactorTerminalSchema.NormallyClosedContactPairs;

                if (noPairs.Count < 2) { failures.Add("10a: NO 对应至少 2 组。"); return; }
                if (ncPairs.Count < 1) { failures.Add("10b: NC 对应至少 1 组。"); return; }

                bool has13_14 = false, has33_34 = false;
                foreach (var pair in noPairs)
                {
                    if (pair.MatchesUndirected("13", "14")) has13_14 = true;
                    if (pair.MatchesUndirected("33", "34")) has33_34 = true;
                }
                if (!has13_14) { failures.Add("10c: NO 对中缺少 13/14。"); return; }
                if (!has33_34) { failures.Add("10d: NO 对中缺少 33/34。"); return; }

                bool has21_22 = false;
                foreach (var pair in ncPairs)
                {
                    if (pair.MatchesUndirected("21", "22")) has21_22 = true;
                }
                if (!has21_22) { failures.Add("10e: NC 对中缺少 21/22。"); return; }

                Debug.Log("[PASS] 10: Schema 验证（NO: 13/14+33/34, NC: 21/22 均存在）");
            }
            catch (Exception e)
            {
                failures.Add("10 异常: " + e.Message);
            }
        }
    }
}

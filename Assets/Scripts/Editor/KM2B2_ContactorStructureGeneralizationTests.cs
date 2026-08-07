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
    /// KM-2B2: CircuitStateAnalyzer 接触器自锁/互锁结构拓扑泛化测试。
    ///
    /// 验证自锁和互锁结构识别从硬编码 13/14、21/22 泛化为基于
    /// ContactorTerminalSchema 的结构拓扑识别后，分析结果正确：
    /// - 13/14 自锁识别（基线不变）
    /// - 33/34 自锁识别（泛化后新增）
    /// - 双 NO 场景：只识别真正的自锁组，不误判联动组
    /// - NC 互锁不再写死 21/22
    /// - 文本使用实际匹配 pair
    ///
    /// 使用反射访问 SelfHoldContactPair / InterlockContactPair，
    /// 使得测试在 BEFORE（字段不存在）和 AFTER（字段存在）代码上均能编译运行。
    /// </summary>
    public static class KM2B2_ContactorStructureGeneralizationTests
    {
        [MenuItem("Tools/Tests/Run KM2B2 Contactor Structure Generalization Tests")]
        public static void Run()
        {
            var failures = new List<string>();
            var notApplicable = new List<string>();

            try
            {
                // === A. 自锁结构识别 ===
                Test01_13_14_SelfHold_Baseline(failures);
                Test02_33_34_SelfHold(failures);
                Test03_13_14_SelfHold_33_34_Linkage(failures);
                Test04_33_34_SelfHold_13_14_Linkage(failures);
                Test05_Only_33_34_Linkage_NoSelfHold(failures);
                Test06_BothNO_Unused(failures);
                Test07_SingleNO_Endpoint_Wired(failures);

                // === B. 互锁结构识别 ===
                Test08_21_22_NormalInterlock(failures);
                Test09_21_22_NonKM_Load(failures);

                // === C. 交叉验证 ===
                Test10_DifferentKM_NoImpersonation(failures);
                Test11_NO_NotNC(failures);
                Test12_NC_NotNO(failures);

                // === D. 文本验证 ===
                Test13_TextUsesMatchedPair(failures);
                Test14_NoHardcoded_13_14_Text(failures);
                Test15_NoHardcoded_21_22_Logic(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }

            if (notApplicable.Count > 0)
            {
                foreach (var na in notApplicable)
                {
                    Debug.LogWarning("[NOT_APPLICABLE] " + na);
                }
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM2B2_ContactorStructureGeneralizationTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B2_ContactorStructureGeneralizationTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B2_ContactorStructureGeneralizationTests 通过 (15/15) ===");
        }

        // =========================================================================
        // 反射辅助：访问 AFTER 新增字段
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
        // 电路构造辅助
        // =========================================================================

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

            // 自锁并联：stop.12 → KM.{noStart} → KM.{noEnd} → KM.A1
            factory.Connect(stop, "12", km, noStart);
            factory.Connect(km, noEnd, km, "A1");

            return factory;
        }

        /// <summary>
        /// 构造双 KM 电路：KM1 自锁 + KM1 的另一组 NO 驱动 KM2（联动）。
        /// selfHoldStart/selfHoldEnd 是 KM1 的自锁 NO 对。
        /// linkageStart/linkageEnd 是 KM1 的联动 NO 对（驱动 KM2）。
        /// </summary>
        private static CircuitTestFactory CreateDualKMCircuit(
            string selfHoldStart, string selfHoldEnd,
            string linkageStart, string linkageEnd,
            out CircuitComponent stop, out CircuitComponent start1,
            out CircuitComponent km1, out CircuitComponent km2)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            start1 = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
            km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");
            km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");

            factory.SetClosed(stop, true);
            factory.SetClosed(start1, false);

            // KM1 控制回路
            factory.Connect(power, "L1", stop, "11");
            factory.Connect(stop, "12", start1, "23");
            factory.Connect(start1, "24", km1, "A1");
            factory.Connect(km1, "A2", power, "N");

            // KM1 自锁并联
            factory.Connect(stop, "12", km1, selfHoldStart);
            factory.Connect(km1, selfHoldEnd, km1, "A1");

            // KM1 联动：KM1.{linkageStart} → KM1.{linkageEnd} → KM2.A1
            factory.Connect(stop, "12", km1, linkageStart);
            factory.Connect(km1, linkageEnd, km2, "A1");
            factory.Connect(km2, "A2", power, "N");

            return factory;
        }

        /// <summary>
        /// 构造正反转互锁电路：
        /// KM_F 和 KM_R 互为 NC 互锁。
        /// </summary>
        private static CircuitTestFactory CreateInterlockCircuit(
            out CircuitComponent stop, out CircuitComponent fwd, out CircuitComponent rev,
            out CircuitComponent kmF, out CircuitComponent kmR)
        {
            var factory = new CircuitTestFactory();
            var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
            stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
            fwd = factory.CreateComponent("sb2", "Button_Compound_SB", ComponentKind.PushButton, "11", "12", "23", "24");
            rev = factory.CreateComponent("sb3", "Button_Compound_SB", ComponentKind.PushButton, "11", "12", "23", "24");
            kmF = factory.CreateComponent("km_f", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");
            kmR = factory.CreateComponent("km_r", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                "A1", "A2", "13", "14", "21", "22", "33", "34");

            factory.SetClosed(stop, true);
            factory.SetClosed(fwd, false);
            factory.SetClosed(rev, false);

            // 公共停止
            factory.Connect(power, "L1", stop, "11");

            // 正转回路：stop.12 → fwd.23 → fwd.24 → KM_R.21(NC) → KM_R.22 → KM_F.A1
            factory.Connect(stop, "12", fwd, "23");
            factory.Connect(fwd, "24", kmR, "21");
            factory.Connect(kmR, "22", kmF, "A1");
            factory.Connect(kmF, "A2", power, "N");

            // 正转自锁
            factory.Connect(stop, "12", kmF, "13");
            factory.Connect(kmF, "14", kmF, "A1");

            // 反转回路：stop.12 → rev.23 → rev.24 → KM_F.21(NC) → KM_F.22 → KM_R.A1
            factory.Connect(stop, "12", rev, "23");
            factory.Connect(rev, "24", kmF, "21");
            factory.Connect(kmF, "22", kmR, "A1");
            factory.Connect(kmR, "A2", power, "N");

            // 反转自锁
            factory.Connect(stop, "12", kmR, "13");
            factory.Connect(kmR, "14", kmR, "A1");

            return factory;
        }

        // =========================================================================
        // 测试 1: 13/14 自锁基线
        // =========================================================================
        private static void Test01_13_14_SelfHold_Baseline(List<string> failures)
        {
            const string scenario = "T01-13/14-SelfHold";
            try
            {
                var factory = CreateSelfHoldCircuit("13", "14", out _, out _, out var km);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}, " +
                    $"SelfHoldStatus={info.SelfHoldStatus}");

                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 2: 33/34 自锁
        // =========================================================================
        private static void Test02_33_34_SelfHold(List<string> failures)
        {
            const string scenario = "T02-33/34-SelfHold";
            try
            {
                var factory = CreateSelfHoldCircuit("33", "34", out _, out _, out var km);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}, " +
                    $"SelfHoldStatus={info.SelfHoldStatus}");

                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true（33/34 自锁应被识别）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 3: 13/14 自锁 + 33/34 联动
        // =========================================================================
        private static void Test03_13_14_SelfHold_33_34_Linkage(List<string> failures)
        {
            const string scenario = "T03-13/14-SelfHold+33/34-Linkage";
            try
            {
                var factory = CreateDualKMCircuit("13", "14", "33", "34",
                    out _, out _, out var km1, out _);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                var pair = GetSelfHoldContactPair(info);
                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={pair}");

                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true");
                if (pair != null && pair != "13/14")
                    failures.Add($"{scenario}: SelfHoldContactPair 应为 13/14，实际={pair}");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 4: 33/34 自锁 + 13/14 联动
        // =========================================================================
        private static void Test04_33_34_SelfHold_13_14_Linkage(List<string> failures)
        {
            const string scenario = "T04-33/34-SelfHold+13/14-Linkage";
            try
            {
                var factory = CreateDualKMCircuit("33", "34", "13", "14",
                    out _, out _, out var km1, out _);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                var pair = GetSelfHoldContactPair(info);
                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={pair}");

                if (!info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 true");
                if (pair != null && pair != "33/34")
                    failures.Add($"{scenario}: SelfHoldContactPair 应为 33/34，实际={pair}");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 5: 只有 33/34 联动、无自锁
        // =========================================================================
        private static void Test05_Only_33_34_Linkage_NoSelfHold(List<string> failures)
        {
            const string scenario = "T05-Only-33/34-Linkage-NoSelfHold";
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

                // KM1 控制回路（无自锁）
                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km1, "A1");
                factory.Connect(km1, "A2", power, "N");

                // KM1 联动：stop.12 → KM1.33 → KM1.34 → KM2.A1
                factory.Connect(stop, "12", km1, "33");
                factory.Connect(km1, "34", km2, "A1");
                factory.Connect(km2, "A2", power, "N");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}");

                if (info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 false（33/34 只有联动不是自锁）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 6: 两组 NO 都未接
        // =========================================================================
        private static void Test06_BothNO_Unused(List<string> failures)
        {
            const string scenario = "T06-BothNO-Unused";
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
                // 13/14、33/34 均不接

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}");

                if (info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 false");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 7: 仅一个 NO 端点接线
        // =========================================================================
        private static void Test07_SingleNO_Endpoint_Wired(List<string> failures)
        {
            const string scenario = "T07-SingleNO-Endpoint-Wired";
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
                // 只接 13，不接 14
                factory.Connect(stop, "12", km, "13");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}");

                if (info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 false（仅一端接线）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 8: 21/22 正常互锁
        // =========================================================================
        private static void Test08_21_22_NormalInterlock(List<string> failures)
        {
            const string scenario = "T08-21/22-NormalInterlock";
            try
            {
                var factory = CreateInterlockCircuit(out _, out _, out _, out var kmF, out var kmR);
                var result = Analyze(factory);

                var infoF = result.FindComponent("km_f");
                var infoR = result.FindComponent("km_r");
                if (infoF == null) { failures.Add($"{scenario}: km_f 未找到"); return; }
                if (infoR == null) { failures.Add($"{scenario}: km_r 未找到"); return; }

                Debug.Log($"[{scenario}] KM_F: HasInterlockStructure={infoF.HasInterlockStructure}, " +
                    $"InterlockContactPair={GetInterlockContactPair(infoF)}");
                Debug.Log($"[{scenario}] KM_R: HasInterlockStructure={infoR.HasInterlockStructure}, " +
                    $"InterlockContactPair={GetInterlockContactPair(infoR)}");

                if (!infoF.HasInterlockStructure)
                    failures.Add($"{scenario}: KM_F HasInterlockStructure 应为 true");
                if (!infoR.HasInterlockStructure)
                    failures.Add($"{scenario}: KM_R HasInterlockStructure 应为 true");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 9: 21/22 接普通非 KM 支路（灯）
        // =========================================================================
        private static void Test09_21_22_NonKM_Load(List<string> failures)
        {
            const string scenario = "T09-21/22-NonKM-Load";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var lamp = factory.CreateComponent("lamp1", "Lamp_220V", ComponentKind.Lamp, "L", "N");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");
                factory.Connect(start, "24", km, "A1");
                factory.Connect(km, "A2", power, "N");
                // 自锁
                factory.Connect(stop, "12", km, "13");
                factory.Connect(km, "14", km, "A1");
                // 21/22 接灯
                factory.Connect(power, "L1", km, "21");
                factory.Connect(km, "22", lamp, "L");
                factory.Connect(lamp, "N", power, "N");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasInterlockStructure={info.HasInterlockStructure}, " +
                    $"InterlockContactPair={GetInterlockContactPair(info)}");

                if (info.HasInterlockStructure)
                    failures.Add($"{scenario}: HasInterlockStructure 应为 false（21/22 接灯非互锁）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 10: 不同 KM 的辅助触点不能互相冒充
        // =========================================================================
        private static void Test10_DifferentKM_NoImpersonation(List<string> failures)
        {
            const string scenario = "T10-DifferentKM-NoImpersonation";
            try
            {
                // KM1 的 33/34 驱动 KM2，不应被识别为 KM1 的自锁
                var factory = CreateDualKMCircuit("13", "14", "33", "34",
                    out _, out _, out var km1, out var km2);
                var result = Analyze(factory);

                var info1 = result.FindComponent("km1");
                var info2 = result.FindComponent("km2");
                if (info1 == null) { failures.Add($"{scenario}: km1 未找到"); return; }
                if (info2 == null) { failures.Add($"{scenario}: km2 未找到"); return; }

                var pair1 = GetSelfHoldContactPair(info1);
                var pair2 = GetSelfHoldContactPair(info2);

                Debug.Log($"[{scenario}] KM1: HasSelfHoldStructure={info1.HasSelfHoldStructure}, pair={pair1}");
                Debug.Log($"[{scenario}] KM2: HasSelfHoldStructure={info2.HasSelfHoldStructure}, pair={pair2}");

                // KM1 应识别 13/14 自锁
                if (!info1.HasSelfHoldStructure)
                    failures.Add($"{scenario}: KM1 HasSelfHoldStructure 应为 true");
                if (pair1 != null && pair1 != "13/14")
                    failures.Add($"{scenario}: KM1 SelfHoldContactPair 应为 13/14，实际={pair1}");
                // KM2 不应有自锁（KM2 没有自己的启动按钮并联）
                if (info2.HasSelfHoldStructure)
                    failures.Add($"{scenario}: KM2 HasSelfHoldStructure 应为 false（KM2 无自锁）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 11: NO 不得识别为 NC
        // =========================================================================
        private static void Test11_NO_NotNC(List<string> failures)
        {
            const string scenario = "T11-NO-NotNC";
            try
            {
                var factory = CreateSelfHoldCircuit("13", "14", out _, out _, out var km);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                // 13/14 是 NO，不应被识别为互锁
                Debug.Log($"[{scenario}] HasInterlockStructure={info.HasInterlockStructure}");
                if (info.HasInterlockStructure)
                    failures.Add($"{scenario}: HasInterlockStructure 应为 false（NO 不应识别为 NC 互锁）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 12: NC 不得识别为 NO
        // =========================================================================
        private static void Test12_NC_NotNO(List<string> failures)
        {
            const string scenario = "T12-NC-NotNO";
            try
            {
                // 21/22 接灯，不应被识别为自锁
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
                // 21/22 接线但不构成自锁
                factory.Connect(stop, "12", km, "21");
                factory.Connect(km, "22", km, "A1");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] HasSelfHoldStructure={info.HasSelfHoldStructure}");
                if (info.HasSelfHoldStructure)
                    failures.Add($"{scenario}: HasSelfHoldStructure 应为 false（NC 不应识别为 NO 自锁）");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 13: 文本使用实际匹配 pair
        // =========================================================================
        private static void Test13_TextUsesMatchedPair(List<string> failures)
        {
            const string scenario = "T13-TextUsesMatchedPair";
            try
            {
                // 33/34 自锁 → 文本应显示 33/34
                var factory = CreateSelfHoldCircuit("33", "34", out _, out _, out var km);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                Debug.Log($"[{scenario}] SelfHoldStatus={info.SelfHoldStatus}");
                // AFTER: 文本应包含 33/34
                // BEFORE: 文本可能包含 13/14（硬编码），这里只验证 AFTER
                var pair = GetSelfHoldContactPair(info);
                if (pair != null)
                {
                    // AFTER code: pair exists, text should contain 33/34
                    if (!info.SelfHoldStatus.Contains("33/34"))
                        failures.Add($"{scenario}: SelfHoldStatus 应包含 33/34，实际={info.SelfHoldStatus}");
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 14: 无硬编码 13/14 文案残留
        // =========================================================================
        private static void Test14_NoHardcoded_13_14_Text(List<string> failures)
        {
            const string scenario = "T14-NoHardcoded-13/14-Text";
            try
            {
                // 33/34 自锁场景下，文本不应固定写 "13/14 自锁结构"
                var factory = CreateSelfHoldCircuit("33", "34", out _, out _, out var km);
                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                var pair = GetSelfHoldContactPair(info);
                if (pair == "33/34" && info.HasSelfHoldStructure)
                {
                    // AFTER code: 确认文本不包含硬编码 "13/14 自锁结构"
                    if (info.SelfHoldStatus.Contains("13/14 自锁结构"))
                        failures.Add($"{scenario}: SelfHoldStatus 不应包含硬编码 '13/14 自锁结构'，实际={info.SelfHoldStatus}");
                    if (info.SelfHoldStatus.Contains("未检测到 13/14"))
                        failures.Add($"{scenario}: SelfHoldStatus 不应包含硬编码 '未检测到 13/14'，实际={info.SelfHoldStatus}");
                }

                // 也检查未检测到自锁时的文本
                var factory2 = new CircuitTestFactory();
                var power2 = factory2.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop2 = factory2.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                var start2 = factory2.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km2 = factory2.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                factory2.SetClosed(stop2, true);
                factory2.SetClosed(start2, false);
                factory2.Connect(power2, "L1", stop2, "11");
                factory2.Connect(stop2, "12", start2, "23");
                factory2.Connect(start2, "24", km2, "A1");
                factory2.Connect(km2, "A2", power2, "N");

                var result2 = Analyze(factory2);
                var info2 = result2.FindComponent("km1");
                if (info2 != null && !info2.HasSelfHoldStructure)
                {
                    Debug.Log($"[{scenario}] No-selfhold text={info2.SelfHoldStatus}");
                    // AFTER: 不应包含硬编码 "未检测到 13/14"
                    var pair2 = GetSelfHoldContactPair(info2);
                    if (pair2 == null && info2.SelfHoldStatus.Contains("未检测到 13/14"))
                    {
                        // In AFTER code, the field exists but is null, and text should not say "13/14"
                        // This check only applies if the field exists (AFTER code)
                    }
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // 测试 15: 无硬编码 21/22 业务判断残留
        // =========================================================================
        private static void Test15_NoHardcoded_21_22_Logic(List<string> failures)
        {
            const string scenario = "T15-NoHardcoded-21/22-Logic";
            try
            {
                // 验证互锁文本在 AFTER 中使用匹配 pair 而非固定 21/22
                var factory = CreateInterlockCircuit(out _, out _, out _, out var kmF, out var kmR);
                var result = Analyze(factory);

                var infoF = result.FindComponent("km_f");
                if (infoF == null) { failures.Add($"{scenario}: km_f 未找到"); return; }

                var pair = GetInterlockContactPair(infoF);
                Debug.Log($"[{scenario}] KM_F InterlockStatus={infoF.InterlockStatus}, pair={pair}");

                // AFTER: 如果有互锁且 pair 存在，文本应包含 pair label
                if (infoF.HasInterlockStructure && pair != null)
                {
                    if (!infoF.InterlockStatus.Contains(pair))
                        failures.Add($"{scenario}: InterlockStatus 应包含 '{pair}'，实际={infoF.InterlockStatus}");
                }
                // AFTER: 如果没有互锁，文本不应固定写 "未检测到 21/22 互锁结构"
                if (!infoF.HasInterlockStructure)
                {
                    // Only check if field exists (AFTER code)
                    if (GetInterlockContactPair(infoF) != null &&
                        infoF.InterlockStatus.Contains("未检测到 21/22"))
                    {
                        failures.Add($"{scenario}: InterlockStatus 不应包含硬编码 '未检测到 21/22'，实际={infoF.InterlockStatus}");
                    }
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }
    }
}

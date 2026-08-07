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
    /// KM-2B2.2: Start.24 互锁上游有效性收口测试。
    ///
    /// 验证 B2.2 修复：
    /// Start.24 只有在同一个启动按钮的 Start.23 能够通过 candidate-excluded
    /// 静态控制拓扑证明连接到有效控制上游时，Start.24 才能作为 valid upstream。
    ///
    /// T21（RED→GREEN）：Start.23 悬空 + Start.24 → KM1.NC → KM2.A1
    ///     旧代码（e00e9b9）将 Start.24 直接当作 valid upstream → 误报 true
    ///     修复后：Start.23 悬空 → Start.24 不构成 valid upstream → false
    ///
    /// T22（GREEN 保持）：标准 Start.24 互锁
    ///     Power/Stop → Start.23 → Start.24 → KM1.NC → KM2.A1
    ///     Start.23 有真实上游 → Start.24 作为 valid upstream → true, pair=21/22
    /// </summary>
    public static class KM2B2_2_InterlockUpstreamValidationTests
    {
        [MenuItem("Tools/Tests/Run KM2B2.2 Interlock Upstream Validation Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                Test21_DanglingStart23_Start24_As_Upstream(failures);
                Test22_StandardStart24_Interlock_WithValidUpstream(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM2B2_2_InterlockUpstreamValidationTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B2_2_InterlockUpstreamValidationTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B2_2_InterlockUpstreamValidationTests 通过 (2/2) ===");
        }

        // =========================================================================
        // 反射辅助
        // =========================================================================

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
        // T21: Start.23 悬空 + Start.24 → KM1.NC → KM2.A1（RED 场景）
        //
        // 电路：
        //   Power.L1 → Stop.11 → Stop.12 → KM1.13(自锁) → KM1.14 → KM1.A1 → KM1.A2 → Power.N
        //   Start.23 = 完全悬空（不连任何端子）
        //   Start.24 → KM1.21 (NC candidate 一侧)
        //   KM1.22 → KM2.A1 (candidate 另一侧到达另一 KM 线圈)
        //   KM2.A2 → Power.N (返回侧)
        //
        // 关键条件：
        //   - KM1.21、KM1.22 两端均有真实外部 Wire
        //   - Start.24 与 candidate 一侧同 root
        //   - Start.23 没有连接 Stop/Power/其他有效控制上游
        //   - candidate 另一侧到达 KM2.A1
        //
        // 旧代码（e00e9b9）：Start.24 直接作为 valid upstream → 误报 true
        // 修复后：Start.23 悬空 → Start.24 不构成 valid upstream → false
        //
        // 预期：HasInterlockStructure = false, InterlockContactPair = null/empty
        // =========================================================================
        private static void Test21_DanglingStart23_Start24_As_Upstream(List<string> failures)
        {
            const string scenario = "T21-DanglingStart23-Start24-As-Upstream";
            try
            {
                var factory = new CircuitTestFactory();
                var power = factory.CreateComponent("p", "AC_220V_Power", ComponentKind.PowerSource, "L1", "N");
                var stop = factory.CreateComponent("sb1", "Button_Stop_NC", ComponentKind.PushButton, "11", "12");
                // 启动按钮：23/24，23 悬空
                var start = factory.CreateComponent("sb2", "Button_Start_NO", ComponentKind.PushButton, "23", "24");
                var km1 = factory.CreateComponent("km1", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");
                var km2 = factory.CreateComponent("km2", "Contactor_KM_220V", ComponentKind.ContactorCoil,
                    "A1", "A2", "13", "14", "21", "22", "33", "34");

                factory.SetClosed(stop, true);
                factory.SetClosed(start, false);

                // KM1 自锁控制回路（不经 Start，直接 Stop.12 → KM1.13 自锁）
                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", km1, "13");
                factory.Connect(km1, "14", km1, "A1");
                factory.Connect(km1, "A2", power, "N");

                // Start.23 完全悬空（不连接任何端子）
                // Start.24 → KM1.21 (NC candidate 一侧)
                factory.Connect(start, "24", km1, "21");
                // KM1.22 → KM2.A1 (candidate 另一侧到达另一 KM 线圈)
                factory.Connect(km1, "22", km2, "A1");
                // KM2.A2 → Power.N (返回侧，避免场景本身过于畸形)
                factory.Connect(km2, "A2", power, "N");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                var pair = GetInterlockContactPair(info);
                Debug.Log($"[{scenario}] KM1: HasInterlockStructure={info.HasInterlockStructure}, " +
                    $"InterlockContactPair={pair}");

                if (info.HasInterlockStructure)
                    failures.Add($"{scenario}: KM1 HasInterlockStructure 应为 false（Start.23 悬空，Start.24 不构成有效控制上游）");
                if (!string.IsNullOrEmpty(pair))
                    failures.Add($"{scenario}: InterlockContactPair 应为 null/empty，实际={pair}");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }

        // =========================================================================
        // T22: 标准 Start.24 互锁（Start.23 有真实上游）
        //
        // 电路：
        //   Power.L1 → Stop.11 → Stop.12 → Start.23 (Start.23 有有效控制上游)
        //   Start.24 → KM1.21 (NC candidate 一侧)
        //   KM1.22 → KM2.A1 (candidate 另一侧到达另一 KM 线圈)
        //   KM2.A2 → Power.N (返回侧)
        //   KM1 自锁：Stop.12 → KM1.13 → KM1.14 → KM1.A1 → KM1.A2 → Power.N
        //
        // 预期：HasInterlockStructure = true, InterlockContactPair = 21/22
        //
        // 该测试证明：不是简单禁用 Start.24，而是要求 Start.23 有真实上游支撑。
        // =========================================================================
        private static void Test22_StandardStart24_Interlock_WithValidUpstream(List<string> failures)
        {
            const string scenario = "T22-StandardStart24-Interlock-WithValidUpstream";
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

                // 控制上游：Power.L1 → Stop.11 → Stop.12 → Start.23
                factory.Connect(power, "L1", stop, "11");
                factory.Connect(stop, "12", start, "23");

                // 互锁路径：Start.24 → KM1.21(NC) → KM1.22 → KM2.A1
                factory.Connect(start, "24", km1, "21");
                factory.Connect(km1, "22", km2, "A1");
                factory.Connect(km2, "A2", power, "N");

                // KM1 自锁回路：Stop.12 → KM1.13 → KM1.14 → KM1.A1 → KM1.A2 → Power.N
                factory.Connect(stop, "12", km1, "13");
                factory.Connect(km1, "14", km1, "A1");
                factory.Connect(km1, "A2", power, "N");

                var result = Analyze(factory);
                var info = result.FindComponent("km1");
                if (info == null) { failures.Add($"{scenario}: km1 未找到"); return; }

                var pair = GetInterlockContactPair(info);
                Debug.Log($"[{scenario}] KM1: HasInterlockStructure={info.HasInterlockStructure}, " +
                    $"InterlockContactPair={pair}");

                if (!info.HasInterlockStructure)
                    failures.Add($"{scenario}: KM1 HasInterlockStructure 应为 true（Start.23 有有效上游，Start.24 构成有效互锁）");
                if (pair != "21/22")
                    failures.Add($"{scenario}: InterlockContactPair 应为 21/22，实际={pair}");
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
        }
    }
}

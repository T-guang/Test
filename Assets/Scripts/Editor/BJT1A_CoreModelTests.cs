using System;
using ElectricalSim.Spice.Core;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// BJT1A — Core 模型回归测试。
    /// 验证 GenericNpnBjt / GenericPnpBjt 的枚举、端子、接线规则以及既有器件行为不变。
    /// 所有断言均调用真实生产 API，不读取源码字符串。
    /// </summary>
    public static class BJT1A_CoreModelTests
    {
        private static int _passed;
        private static int _failed;

        [MenuItem("Tools/Tests/Run BJT1A Core Model Tests")]
        public static void RunAllTests()
        {
            _passed = 0;
            _failed = 0;

            // 1. NPN/PNP 枚举存在
            Run("GenericNpnBjt_EnumValueExists", () =>
            {
                CheckTrue(Enum.IsDefined(typeof(SpiceComponentKind), SpiceComponentKind.GenericNpnBjt),
                    "SpiceComponentKind.GenericNpnBjt must exist in the enum.");
            });
            Run("GenericPnpBjt_EnumValueExists", () =>
            {
                CheckTrue(Enum.IsDefined(typeof(SpiceComponentKind), SpiceComponentKind.GenericPnpBjt),
                    "SpiceComponentKind.GenericPnpBjt must exist in the enum.");
            });

            // 2. 既有枚举整数值未变化
            Run("ExistingEnumIntegerValues_AreUnchanged", () =>
            {
                CheckEqual(0, (int)SpiceComponentKind.DcVoltageSource, "DcVoltageSource must be 0.");
                CheckEqual(1, (int)SpiceComponentKind.DcCurrentSource, "DcCurrentSource must be 1.");
                CheckEqual(2, (int)SpiceComponentKind.IdealSwitch, "IdealSwitch must be 2.");
                CheckEqual(3, (int)SpiceComponentKind.SiliconDiode, "SiliconDiode must be 3.");
                CheckEqual(4, (int)SpiceComponentKind.Resistor, "Resistor must be 4.");
                CheckEqual(5, (int)SpiceComponentKind.Capacitor, "Capacitor must be 5.");
                CheckEqual(6, (int)SpiceComponentKind.Inductor, "Inductor must be 6.");
                CheckEqual(7, (int)SpiceComponentKind.Ground, "Ground must be 7.");
                CheckEqual(8, (int)SpiceComponentKind.VoltageProbe, "VoltageProbe must be 8.");
                CheckEqual(9, (int)SpiceComponentKind.CurrentProbe, "CurrentProbe must be 9.");
                CheckEqual(10, (int)SpiceComponentKind.AcVoltageSource, "AcVoltageSource must be 10.");
                CheckEqual(11, (int)SpiceComponentKind.IdealOperationalAmplifier, "IdealOperationalAmplifier must be 11.");
            });

            // 3. NPN/PNP 都有 collector/base/emitter
            Run("Npn_HasAllThreeTerminals", () =>
            {
                var npn = SpiceComponentModel.GenericNpnBjt("Q1");
                CheckTrue(npn.HasTerminal(SpiceComponentModel.CollectorTerminalId), "NPN must have collector.");
                CheckTrue(npn.HasTerminal(SpiceComponentModel.BaseTerminalId), "NPN must have base.");
                CheckTrue(npn.HasTerminal(SpiceComponentModel.EmitterTerminalId), "NPN must have emitter.");
            });
            Run("Pnp_HasAllThreeTerminals", () =>
            {
                var pnp = SpiceComponentModel.GenericPnpBjt("Q1");
                CheckTrue(pnp.HasTerminal(SpiceComponentModel.CollectorTerminalId), "PNP must have collector.");
                CheckTrue(pnp.HasTerminal(SpiceComponentModel.BaseTerminalId), "PNP must have base.");
                CheckTrue(pnp.HasTerminal(SpiceComponentModel.EmitterTerminalId), "PNP must have emitter.");
            });

            // 4. 端子顺序稳定
            Run("Npn_TerminalOrder_IsCollectorBaseEmitter", () =>
            {
                var terminals = SpiceComponentModel.TerminalIdsFor(SpiceComponentKind.GenericNpnBjt);
                CheckEqual(3, terminals.Count, "NPN must have exactly 3 terminals.");
                CheckEqual(SpiceComponentModel.CollectorTerminalId, terminals[0], "Terminal[0] must be collector.");
                CheckEqual(SpiceComponentModel.BaseTerminalId, terminals[1], "Terminal[1] must be base.");
                CheckEqual(SpiceComponentModel.EmitterTerminalId, terminals[2], "Terminal[2] must be emitter.");
            });
            Run("Pnp_TerminalOrder_IsCollectorBaseEmitter", () =>
            {
                var terminals = SpiceComponentModel.TerminalIdsFor(SpiceComponentKind.GenericPnpBjt);
                CheckEqual(3, terminals.Count, "PNP must have exactly 3 terminals.");
                CheckEqual(SpiceComponentModel.CollectorTerminalId, terminals[0], "Terminal[0] must be collector.");
                CheckEqual(SpiceComponentModel.BaseTerminalId, terminals[1], "Terminal[1] must be base.");
                CheckEqual(SpiceComponentModel.EmitterTerminalId, terminals[2], "Terminal[2] must be emitter.");
            });

            // 5. C-B、B-E、C-E 同元件接线允许
            Run("Npn_SameComponent_DifferentTerminals_AllAllowed", () =>
            {
                var kind = SpiceComponentKind.GenericNpnBjt;
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "base"),
                    "NPN collector↔base must be allowed.");
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "base", kind, "Q1", "emitter"),
                    "NPN base↔emitter must be allowed.");
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "emitter"),
                    "NPN collector↔emitter must be allowed.");
            });
            Run("Pnp_SameComponent_DifferentTerminals_AllAllowed", () =>
            {
                var kind = SpiceComponentKind.GenericPnpBjt;
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "base"),
                    "PNP collector↔base must be allowed.");
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "base", kind, "Q1", "emitter"),
                    "PNP base↔emitter must be allowed.");
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "emitter"),
                    "PNP collector↔emitter must be allowed.");
            });

            // 6. 同端子自连拒绝
            Run("Npn_SameTerminal_SelfConnection_Rejected", () =>
            {
                var kind = SpiceComponentKind.GenericNpnBjt;
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "collector"),
                    "NPN collector self-connection must be rejected.");
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "base", kind, "Q1", "base"),
                    "NPN base self-connection must be rejected.");
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "emitter", kind, "Q1", "emitter"),
                    "NPN emitter self-connection must be rejected.");
            });
            Run("Pnp_SameTerminal_SelfConnection_Rejected", () =>
            {
                var kind = SpiceComponentKind.GenericPnpBjt;
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "collector"),
                    "PNP collector self-connection must be rejected.");
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "base", kind, "Q1", "base"),
                    "PNP base self-connection must be rejected.");
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "emitter", kind, "Q1", "emitter"),
                    "PNP emitter self-connection must be rejected.");
            });

            // 7. 未知端子拒绝
            Run("Npn_UnknownTerminal_Rejected", () =>
            {
                var kind = SpiceComponentKind.GenericNpnBjt;
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "unknown"),
                    "NPN collector↔unknown must be rejected.");
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "base", kind, "Q1", ""),
                    "NPN base↔empty must be rejected.");
            });
            Run("Pnp_UnknownTerminal_Rejected", () =>
            {
                var kind = SpiceComponentKind.GenericPnpBjt;
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "collector", kind, "Q1", "unknown"),
                    "PNP collector↔unknown must be rejected.");
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(kind, "Q1", "base", kind, "Q1", ""),
                    "PNP base↔empty must be rejected.");
            });

            // 8. startKind/endKind 不一致时拒绝
            Run("Npn_Diode_KindMismatch_Rejected", () =>
            {
                // 同 componentId 但 startKind=NPN, endKind=Diode — 不应允许
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(
                    SpiceComponentKind.GenericNpnBjt, "Q1", "collector",
                    SpiceComponentKind.SiliconDiode, "Q1", "positive"),
                    "NPN↔Diode kind mismatch must be rejected.");
            });
            Run("Pnp_Resistor_KindMismatch_Rejected", () =>
            {
                // 同 componentId 但 startKind=PNP, endKind=Resistor — 不应允许
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(
                    SpiceComponentKind.GenericPnpBjt, "Q1", "base",
                    SpiceComponentKind.Resistor, "Q1", "positive"),
                    "PNP↔Resistor kind mismatch must be rejected.");
            });
            Run("Diode_Npn_KindMismatch_Rejected", () =>
            {
                // 反向：startKind=Diode, endKind=NPN — 不应允许
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(
                    SpiceComponentKind.SiliconDiode, "Q1", "positive",
                    SpiceComponentKind.GenericNpnBjt, "Q1", "emitter"),
                    "Diode↔NPN kind mismatch must be rejected.");
            });

            // 9. 普通双端器件行为不变
            Run("SiliconDiode_SameComponentConnection_Rejected", () =>
            {
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(SpiceComponentKind.SiliconDiode, "D1", "positive", SpiceComponentKind.SiliconDiode, "D1", "negative"),
                    "Silicon diode same-component connection must still be rejected.");
            });
            Run("Resistor_SameComponentConnection_Rejected", () =>
            {
                CheckFalse(SpiceConnectionRules.IsConnectionAllowed(SpiceComponentKind.Resistor, "R1", "positive", SpiceComponentKind.Resistor, "R1", "negative"),
                    "Resistor same-component connection must still be rejected.");
            });
            Run("DifferentComponents_ConnectionAllowed", () =>
            {
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(SpiceComponentKind.Resistor, "R1", "positive", SpiceComponentKind.SiliconDiode, "D1", "positive"),
                    "Different components connection must still be allowed.");
            });

            var total = _passed + _failed;
            Debug.Log($"BJT1A Core Model Tests: {_passed}/{total} passed.");
            if (_failed > 0)
            {
                throw new InvalidOperationException($"BJT1A Core Model Tests: {_failed} of {total} FAILED.");
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Debug.Log($"[BJT1A] PASS: {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Debug.LogError($"[BJT1A] FAIL: {name} — {ex.Message}");
            }
        }

        private static void CheckTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckFalse(bool condition, string message)
        {
            if (condition) throw new InvalidOperationException(message);
        }

        private static void CheckEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}

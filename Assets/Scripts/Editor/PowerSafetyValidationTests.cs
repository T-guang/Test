using System;
using UnityEditor;
using UnityEngine;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 仅在 Editor 菜单运行的通用电源安全 Validation 回归夹具。
    /// 测试通过 CircuitTestFactory 在内存中构造元件和导线，再调用当前 CircuitValidationService；
    /// 不加载或改写标准模板、不写快照、不保存场景，也不属于 Windows Player 功能。
    ///
    /// Console 中逐项 PASS/FAIL 是本组规则夹具的结果，不能替代 18 张真实模板基线。RuleId、Severity、
    /// 候选拓扑和断言条件是兼容边界；修改规则后应先审查本测试与模板基线的差异。
    /// </summary>
    public static class PowerSafetyValidationTests
    {
        [MenuItem("Tools/Tests/运行通用电源安全规则测试")]
        public static void RunTests()
        {
            // 每个用例独立构造内存拓扑，避免测试之间共享 Workspace 或导线状态。
            Debug.Log("==== 开始执行通用电源安全规则测试 ====");
            var passed = 0;
            var total = 20;

            Action<string, Action> run = (name, test) =>
            {
                try
                {
                    test();
                    passed++;
                    Debug.Log("[PASS] " + name);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[FAIL] " + name + ": " + ex.Message);
                }
            };

            run("POWER_POTENTIAL_CONFLICT_220V_L_N", TestPowerConflictSinglePhaseLn);
            run("POWER_POTENTIAL_CONFLICT_L1_L2", () => TestPowerConflictThreePhasePair("L1", "L2"));
            run("POWER_POTENTIAL_CONFLICT_L2_L3", () => TestPowerConflictThreePhasePair("L2", "L3"));
            run("POWER_POTENTIAL_CONFLICT_L1_L3", () => TestPowerConflictThreePhasePair("L1", "L3"));
            run("POWER_POTENTIAL_NO_FALSE_Lamp", TestNoPowerConflictNormalLamp);
            run("POWER_POTENTIAL_NO_FALSE_Motor", TestNoPowerConflictNormalMotor);

            run("LIVE_TO_PE_FAULT_L_PE", () => TestLiveToPe("L"));
            run("LIVE_TO_PE_FAULT_L1_PE", () => TestLiveToPe("L1"));
            run("LIVE_TO_PE_FAULT_L2_PE", () => TestLiveToPe("L2"));
            run("LIVE_TO_PE_FAULT_L3_PE", () => TestLiveToPe("L3"));
            run("LIVE_TO_PE_NO_FALSE_NormalPE", TestNoLiveToPeNormalPe);

            run("NEUTRAL_PE_MISUSE_N_PE", TestNeutralPeMisuse);
            run("NEUTRAL_PE_NO_FALSE_NormalN", TestNoNeutralPeNormalN);
            run("NEUTRAL_PE_NO_FALSE_NormalPE", TestNoNeutralPeNormalPe);

            run("COIL_VOLTAGE_MISMATCH_KM220_380", () => TestCoilMismatch("Contactor_KM_220V", "L1", "L2", true));
            run("COIL_VOLTAGE_MISMATCH_KM380_220", () => TestCoilMismatch("Contactor_KM_380V", "L", "N", true));
            run("COIL_VOLTAGE_MISMATCH_KT220_380", () => TestCoilMismatch("Timer_OnDelay_220V", "L1", "L2", true));
            run("COIL_VOLTAGE_MISMATCH_KT380_220", () => TestCoilMismatch("Timer_OnDelay_380V", "L", "N", true));
            run("COIL_VOLTAGE_NO_FALSE_KM220_220", () => TestCoilMismatch("Contactor_KM_220V", "L", "N", false));
            run("COIL_VOLTAGE_NO_FALSE_KM380_380", () => TestCoilMismatch("Contactor_KM_380V", "L1", "L2", false));

            Debug.Log($"==== 通用电源安全规则测试完成: {passed}/{total} 成功 ====");
        }

        private static void TestPowerConflictSinglePhaseLn()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            factory.Connect(power, "L", power, "N");
            AssertHasRuleId(factory.Validate(), "POWER_POTENTIAL_CONFLICT");
        }

        private static void TestPowerConflictThreePhasePair(string first, string second)
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            factory.Connect(power, first, power, second);
            AssertHasRuleId(factory.Validate(), "POWER_POTENTIAL_CONFLICT");
        }

        private static void TestNoPowerConflictNormalLamp()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var lamp = factory.CreateComponent("lamp", "Lamp", ComponentKind.Lamp, "L", "N");
            factory.Connect(power, "L", lamp, "L");
            factory.Connect(power, "N", lamp, "N");
            AssertDoesNotHaveRuleId(factory.Validate(), "POWER_POTENTIAL_CONFLICT");
        }

        private static void TestNoPowerConflictNormalMotor()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            factory.Connect(power, "L1", motor, "U");
            factory.Connect(power, "L2", motor, "V");
            factory.Connect(power, "L3", motor, "W");
            AssertDoesNotHaveRuleId(factory.Validate(), "POWER_POTENTIAL_CONFLICT");
        }

        private static void TestLiveToPe(string liveTerminal)
        {
            var factory = new CircuitTestFactory();
            var power = liveTerminal == "L" ? SinglePhasePower(factory) : ThreePhasePower(factory);
            factory.Connect(power, liveTerminal, power, "PE");
            AssertHasRuleId(factory.Validate(), "LIVE_TO_PE_FAULT");
        }

        private static void TestNoLiveToPeNormalPe()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W", "PE");
            factory.Connect(power, "PE", motor, "PE");
            AssertDoesNotHaveRuleId(factory.Validate(), "LIVE_TO_PE_FAULT");
        }

        private static void TestNeutralPeMisuse()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            factory.Connect(power, "N", power, "PE");
            var report = factory.Validate();
            AssertHasRuleId(report, "NEUTRAL_PE_MISUSE");
            AssertSeverity(report, "NEUTRAL_PE_MISUSE", CircuitValidationSeverity.Warning);
        }

        private static void TestNoNeutralPeNormalN()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var lamp = factory.CreateComponent("lamp", "Lamp", ComponentKind.Lamp, "L", "N");
            factory.Connect(power, "N", lamp, "N");
            AssertDoesNotHaveRuleId(factory.Validate(), "NEUTRAL_PE_MISUSE");
        }

        private static void TestNoNeutralPeNormalPe()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W", "PE");
            factory.Connect(power, "PE", motor, "PE");
            AssertDoesNotHaveRuleId(factory.Validate(), "NEUTRAL_PE_MISUSE");
        }

        private static void TestCoilMismatch(string definitionName, string firstPowerTerminal, string secondPowerTerminal, bool shouldReport)
        {
            var factory = new CircuitTestFactory();
            var power = firstPowerTerminal == "L" || secondPowerTerminal == "N"
                ? SinglePhasePower(factory)
                : ThreePhasePower(factory);
            var coil = factory.CreateComponent("coil", definitionName, ComponentKind.ContactorCoil, "A1", "A2");
            factory.Connect(power, firstPowerTerminal, coil, "A1");
            factory.Connect(power, secondPowerTerminal, coil, "A2");
            var report = factory.Validate();
            if (shouldReport)
            {
                AssertHasRuleId(report, "COIL_VOLTAGE_MISMATCH");
            }
            else
            {
                AssertDoesNotHaveRuleId(report, "COIL_VOLTAGE_MISMATCH");
            }
        }

        private static CircuitComponent SinglePhasePower(CircuitTestFactory factory)
        {
            return factory.CreateComponent("power", "AC_220V_Power", ComponentKind.PowerSource, "L", "N", "PE");
        }

        private static CircuitComponent ThreePhasePower(CircuitTestFactory factory)
        {
            return factory.CreateComponent("power", "AC_ThreePhase_380V", ComponentKind.PowerSource, "L1", "L2", "L3", "N", "PE");
        }

        private static void AssertHasRuleId(CircuitValidationReport report, string ruleId)
        {
            if (report == null)
            {
                throw new Exception("Report is null.");
            }

            foreach (var issue in report.Issues)
            {
                if (issue != null && string.Equals(issue.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            throw new Exception("Expected RuleId not found: " + ruleId);
        }

        private static void AssertDoesNotHaveRuleId(CircuitValidationReport report, string ruleId)
        {
            if (report == null)
            {
                return;
            }

            foreach (var issue in report.Issues)
            {
                if (issue != null && string.Equals(issue.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Unexpected RuleId found: " + ruleId);
                }
            }
        }

        private static void AssertSeverity(CircuitValidationReport report, string ruleId, CircuitValidationSeverity severity)
        {
            if (report == null)
            {
                throw new Exception("Report is null.");
            }

            foreach (var issue in report.Issues)
            {
                if (issue != null && string.Equals(issue.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
                {
                    if (issue.Severity != severity)
                    {
                        throw new Exception(ruleId + " severity expected " + severity + " but got " + issue.Severity);
                    }

                    return;
                }
            }

            throw new Exception("Expected RuleId not found: " + ruleId);
        }
    }
}

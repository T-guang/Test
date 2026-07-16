using System;
using UnityEditor;
using UnityEngine;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 仅在 Editor 菜单运行的热继主回路与时间继电器控制旁路 Validation 回归夹具。
    /// 当前实现以 CircuitTestFactory 的内存组件和导线构造目标拓扑，再交给 CircuitValidationService；
    /// 不依赖已打开场景或 18 张标准模板，也不会写入模板、快照、报告或 Player 资源。
    ///
    /// 用例的 RuleId 触发/不触发预期和拓扑边界用于防止规则漂移。当前夹具不验证 Severity；
    /// 严重等级仍需结合 Validation 规则快照和真实模板基线复核。失败仅写入 Console。
    /// </summary>
    public static class ThermalTimerBypassValidationTests
    {
        [MenuItem("Tools/Tests/运行热继主回路与时间继电器旁路测试")]
        public static void RunTests()
        {
            // 每个用例自行创建输入，避免计时或保护状态在用例之间残留。
            Debug.Log("==== 开始执行热继主回路与时间继电器旁路测试 ====");
            var passed = 0;
            var total = 14;

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

            run("THERMAL_MAIN_NO_FALSE_NormalMainCircuit", TestNoFalseNormalThermalMainCircuit);
            run("THERMAL_MAIN_BYPASSED_KmDirectToMotor", TestThermalMainCircuitBypassed);
            run("THERMAL_MAIN_DISTINCT_ControlBypassOnly", TestThermalControlBypassOnly);
            run("THERMAL_MAIN_NO_FALSE_MainOkControlOpen", TestNoFalseMainOkControlOpen);
            run("THERMAL_MAIN_NO_FALSE_TwoMotorSequence", TestNoFalseTwoMotorSequenceThermal);
            run("THERMAL_MAIN_NO_FALSE_StarDelta", TestNoFalseStarDeltaThermal);

            run("TIMER_CONTROL_NO_FALSE_SequenceTiming", TestNoFalseSequenceTiming);
            run("TIMER_CONTROL_BYPASSED_SequenceEarlyKm2", TestTimerBypassedSequenceEarlyKm2);
            run("TIMER_CONTROL_NO_FALSE_SequenceElapsed", TestNoFalseSequenceElapsed);
            run("TIMER_CONTROL_NO_FALSE_StarPhase", TestNoFalseStarDeltaStarPhase);
            run("TIMER_CONTROL_BYPASSED_StarDeltaEarlyDelta", TestTimerBypassedStarDeltaEarlyDelta);
            run("TIMER_CONTROL_NO_FALSE_Continuous", TestNoFalseContinuousRun);
            run("TIMER_CONTROL_NO_FALSE_Reversing", TestNoFalseReversing);
            run("TIMER_CONTROL_NO_FALSE_AutoReciprocating", TestNoFalseAutoReciprocating);

            Debug.Log($"==== 热继主回路与时间继电器旁路测试完成: {passed}/{total} 成功 ====");
        }

        private static void TestNoFalseNormalThermalMainCircuit()
        {
            var factory = new CircuitTestFactory();
            var circuit = BuildThermalCircuit(factory, bypassMainCircuit: false);
            ConnectControlCoil(factory, circuit.Power, circuit.Km);
            AssertDoesNotHaveRuleId(factory.Validate(), "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED");
        }

        private static void TestThermalMainCircuitBypassed()
        {
            var factory = new CircuitTestFactory();
            var circuit = BuildThermalCircuit(factory, bypassMainCircuit: true);
            ConnectControlCoil(factory, circuit.Power, circuit.Km);
            AssertHasRuleId(factory.Validate(), "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED");
        }

        private static void TestThermalControlBypassOnly()
        {
            var factory = new CircuitTestFactory();
            var circuit = BuildThermalCircuit(factory, bypassMainCircuit: false);
            factory.SetClosed(circuit.Fr, false);
            factory.Connect(circuit.Power, "L1", circuit.Km, "A1");
            factory.Connect(circuit.Km, "A2", circuit.Fr, "95");
            factory.Connect(circuit.Fr, "96", circuit.Power, "N");
            factory.Connect(circuit.Km, "A2", circuit.Power, "N");

            var report = factory.Validate();
            AssertHasRuleId(report, "THERMAL_RELAY_CONTROL_BYPASSED");
            AssertDoesNotHaveRuleId(report, "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED");
        }

        private static void TestNoFalseMainOkControlOpen()
        {
            var factory = new CircuitTestFactory();
            var circuit = BuildThermalCircuit(factory, bypassMainCircuit: false);
            factory.SetClosed(circuit.Fr, false);
            factory.Connect(circuit.Power, "L1", circuit.Km, "A1");
            factory.Connect(circuit.Km, "A2", circuit.Fr, "95");
            factory.Connect(circuit.Fr, "96", circuit.Power, "N");

            AssertDoesNotHaveRuleId(factory.Validate(), "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED");
        }

        private static void TestNoFalseTwoMotorSequenceThermal()
        {
            var factory = new CircuitTestFactory();
            var first = BuildThermalCircuit(factory, "1", false);
            var second = BuildThermalCircuit(factory, "2", false);
            ConnectControlCoil(factory, first.Power, first.Km);
            ConnectControlCoil(factory, second.Power, second.Km);
            AssertDoesNotHaveRuleId(factory.Validate(), "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED");
        }

        private static void TestNoFalseStarDeltaThermal()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var fr = factory.CreateComponent("fr", "ThermalRelay_FR_380V", ComponentKind.Switch, "L1", "L2", "L3", "T1", "T2", "T3", "95", "96", "97", "98");
            var km = factory.CreateComponent("km_main", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");
            var motor = factory.CreateComponent("m", "Motor_StarDelta_380V", ComponentKind.Motor, "U1", "V1", "W1", "U2", "V2", "W2");

            factory.Connect(power, "L1", km, "L1");
            factory.Connect(power, "L2", km, "L2");
            factory.Connect(power, "L3", km, "L3");
            factory.Connect(km, "T1", fr, "L1");
            factory.Connect(km, "T2", fr, "L2");
            factory.Connect(km, "T3", fr, "L3");
            factory.Connect(fr, "T1", motor, "U1");
            factory.Connect(fr, "T2", motor, "V1");
            factory.Connect(fr, "T3", motor, "W1");

            AssertDoesNotHaveRuleId(factory.Validate(), "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED");
        }

        private static void TestNoFalseSequenceTiming()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var timer = CreateTimer(factory, elapsed: false);
            var km2 = CreateKm220(factory, "km2");
            EnergizeTimer(factory, power, timer);
            ConnectTimerNoToContactor(factory, power, timer, km2);
            factory.Connect(km2, "A2", power, "N");

            AssertDoesNotHaveRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestTimerBypassedSequenceEarlyKm2()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var timer = CreateTimer(factory, elapsed: false);
            var km2 = CreateKm220(factory, "km2");
            EnergizeTimer(factory, power, timer);
            ConnectTimerNoToContactor(factory, power, timer, km2);
            factory.Connect(km2, "A2", power, "N");
            factory.Connect(power, "L", km2, "A1");

            AssertHasRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestNoFalseSequenceElapsed()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var timer = CreateTimer(factory, elapsed: true);
            var km2 = CreateKm220(factory, "km2");
            EnergizeTimer(factory, power, timer);
            ConnectTimerNoToContactor(factory, power, timer, km2);
            factory.Connect(km2, "A2", power, "N");

            AssertDoesNotHaveRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestNoFalseStarDeltaStarPhase()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var timer = CreateTimer(factory, elapsed: false);
            var kmd = CreateKm220(factory, "km_delta");
            EnergizeTimer(factory, power, timer);
            ConnectTimerNoToContactor(factory, power, timer, kmd);
            factory.Connect(kmd, "A2", power, "N");
            AssertDoesNotHaveRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestTimerBypassedStarDeltaEarlyDelta()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var timer = CreateTimer(factory, elapsed: false);
            var kmd = CreateKm220(factory, "km_delta");
            EnergizeTimer(factory, power, timer);
            ConnectTimerNoToContactor(factory, power, timer, kmd);
            factory.Connect(kmd, "A2", power, "N");
            factory.Connect(power, "L", kmd, "A1");
            AssertHasRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestNoFalseContinuousRun()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var km = CreateKm220(factory, "km");
            ConnectControlCoil(factory, power, km);
            AssertDoesNotHaveRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestNoFalseReversing()
        {
            var factory = new CircuitTestFactory();
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var forward = CreateKm380(factory, "km_forward");
            var reverse = CreateKm380(factory, "km_reverse");
            factory.Connect(forward, "T1", motor, "U");
            factory.Connect(forward, "T2", motor, "V");
            factory.Connect(forward, "T3", motor, "W");
            factory.Connect(reverse, "T1", motor, "W");
            factory.Connect(reverse, "T2", motor, "V");
            factory.Connect(reverse, "T3", motor, "U");
            AssertDoesNotHaveRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static void TestNoFalseAutoReciprocating()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var km = CreateKm220(factory, "km");
            var sq = factory.CreateComponent("sq", "LimitSwitch_Compound", ComponentKind.PushButton, "11", "12", "23", "24");
            factory.Connect(power, "L", sq, "23");
            factory.Connect(sq, "24", km, "A1");
            factory.Connect(km, "A2", power, "N");
            AssertDoesNotHaveRuleId(factory.Validate(), "TIMER_CONTROL_BYPASSED");
        }

        private static ThermalCircuit BuildThermalCircuit(CircuitTestFactory factory, bool bypassMainCircuit)
        {
            return BuildThermalCircuit(factory, string.Empty, bypassMainCircuit);
        }

        private static ThermalCircuit BuildThermalCircuit(CircuitTestFactory factory, string suffix, bool bypassMainCircuit)
        {
            var idSuffix = string.IsNullOrWhiteSpace(suffix) ? string.Empty : "_" + suffix;
            var power = ThreePhasePower(factory, "power" + idSuffix);
            var km = CreateKm380(factory, "km" + idSuffix);
            var fr = factory.CreateComponent("fr" + idSuffix, "ThermalRelay_FR_380V", ComponentKind.Switch, "L1", "L2", "L3", "T1", "T2", "T3", "95", "96", "97", "98");
            var motor = factory.CreateComponent("m" + idSuffix, "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");

            factory.Connect(power, "L1", km, "L1");
            factory.Connect(power, "L2", km, "L2");
            factory.Connect(power, "L3", km, "L3");
            factory.Connect(km, "T1", fr, "L1");
            factory.Connect(km, "T2", fr, "L2");
            factory.Connect(km, "T3", fr, "L3");

            if (bypassMainCircuit)
            {
                factory.Connect(km, "T1", motor, "U");
                factory.Connect(km, "T2", motor, "V");
                factory.Connect(km, "T3", motor, "W");
            }
            else
            {
                factory.Connect(fr, "T1", motor, "U");
                factory.Connect(fr, "T2", motor, "V");
                factory.Connect(fr, "T3", motor, "W");
            }

            return new ThermalCircuit(power, km, fr, motor);
        }

        private static void ConnectControlCoil(CircuitTestFactory factory, CircuitComponent power, CircuitComponent km)
        {
            factory.Connect(power, power.GetTerminal("L") != null ? "L" : "L1", km, "A1");
            factory.Connect(km, "A2", power, power.GetTerminal("N") != null ? "N" : "L2");
        }

        private static CircuitComponent CreateTimer(CircuitTestFactory factory, bool elapsed)
        {
            var timer = factory.CreateComponent("kt", "Timer_OnDelay_220V", ComponentKind.ContactorCoil, "A1", "A2", "15", "16", "18");
            factory.SetClosed(timer, elapsed);
            return timer;
        }

        private static CircuitComponent CreateKm220(CircuitTestFactory factory, string id)
        {
            return factory.CreateComponent(id, "Contactor_KM_220V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");
        }

        private static CircuitComponent CreateKm380(CircuitTestFactory factory, string id)
        {
            return factory.CreateComponent(id, "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");
        }

        private static void EnergizeTimer(CircuitTestFactory factory, CircuitComponent power, CircuitComponent timer)
        {
            factory.Connect(power, "L", timer, "A1");
            factory.Connect(timer, "A2", power, "N");
        }

        private static void ConnectTimerNoToContactor(CircuitTestFactory factory, CircuitComponent power, CircuitComponent timer, CircuitComponent contactor)
        {
            factory.Connect(power, "L", timer, "15");
            factory.Connect(timer, "18", contactor, "A1");
        }

        private static CircuitComponent SinglePhasePower(CircuitTestFactory factory)
        {
            return SinglePhasePower(factory, "power");
        }

        private static CircuitComponent SinglePhasePower(CircuitTestFactory factory, string id)
        {
            return factory.CreateComponent(id, "AC_220V_Power", ComponentKind.PowerSource, "L", "N", "PE");
        }

        private static CircuitComponent ThreePhasePower(CircuitTestFactory factory)
        {
            return ThreePhasePower(factory, "power");
        }

        private static CircuitComponent ThreePhasePower(CircuitTestFactory factory, string id)
        {
            return factory.CreateComponent(id, "AC_ThreePhase_380V", ComponentKind.PowerSource, "L1", "L2", "L3", "N", "PE");
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

        private sealed class ThermalCircuit
        {
            public ThermalCircuit(CircuitComponent power, CircuitComponent km, CircuitComponent fr, CircuitComponent motor)
            {
                Power = power;
                Km = km;
                Fr = fr;
                Motor = motor;
            }

            public CircuitComponent Power { get; }
            public CircuitComponent Km { get; }
            public CircuitComponent Fr { get; }
            public CircuitComponent Motor { get; }
        }
    }
}

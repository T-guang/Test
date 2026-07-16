using System;
using UnityEditor;
using UnityEngine;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 仅在 Editor 菜单运行的保护旁路、接触器旁路和正反转互锁缺失 Validation 回归夹具。
    /// 用例使用 CircuitTestFactory 的内存电路调用 CircuitValidationService，不读取 18 张标准模板，
    /// 不写入资产、快照或报告，也不修改当前场景。
    ///
    /// 本工具用于保护相关 RuleId 的稳定触发和误报边界验证；它不能代替真实模板回归。
    /// 规则标题、Severity、测试端点与断言条件均应保持稳定，失败通过 Console 输出。
    /// </summary>
    public static class ProtectionBypassValidationTests
    {
        [MenuItem("Tools/Tests/运行保护旁路与正反转互锁缺失测试")]
        public static void RunTests()
        {
            // 测试夹具彼此隔离；不要把这里构造的实例或端点写回模板数据。
            Debug.Log("==== 开始执行保护旁路与正反转互锁缺失测试 ====");
            var passed = 0;
            var total = 12;

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

            run("BREAKER_OR_FUSE_BYPASSED_BreakerOffOutputLive", TestBreakerOffOutputStillLive);
            run("BREAKER_OR_FUSE_BYPASSED_FuseBypassed", TestFuseBypassed);
            run("BREAKER_OR_FUSE_NO_FALSE_NormalBreaker", TestNoFalseNormalBreaker);

            run("MOTOR_CONTACTOR_BYPASSED_DirectMotorSupply", TestMotorBypassedDirectSupply);
            run("MOTOR_CONTACTOR_NO_FALSE_NormalKmMainPath", TestNoFalseMotorThroughContactor);
            run("MOTOR_CONTACTOR_NO_FALSE_ThermalRelayTemplateShape", TestNoFalseThermalRelayPath);
            run("MOTOR_CONTACTOR_NO_FALSE_StarDelta", TestNoFalseStarDeltaMotor);

            run("REVERSING_INTERLOCK_MISSING_NoInterlock", TestReversingInterlockMissing);
            run("REVERSING_INTERLOCK_NO_FALSE_ElectricalInterlock", TestNoFalseElectricalInterlock);
            run("REVERSING_INTERLOCK_NO_FALSE_DoubleInterlock", TestNoFalseDoubleInterlock);
            run("REVERSING_INTERLOCK_NO_FALSE_TwoMotorSequence", TestNoFalseTwoMotorSequence);
            run("REVERSING_INTERLOCK_NO_FALSE_StarDelta", TestNoFalseStarDeltaReversingRule);

            Debug.Log($"==== 保护旁路与正反转互锁缺失测试完成: {passed}/{total} 成功 ====");
        }

        private static void TestBreakerOffOutputStillLive()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var breaker = factory.CreateComponent("qf", "Breaker_3P", ComponentKind.Breaker, "L1", "L2", "L3", "T1", "T2", "T3");
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            factory.SetTogglable(breaker, true);
            factory.SetClosed(breaker, false);

            factory.Connect(power, "L1", breaker, "L1");
            factory.Connect(power, "L2", breaker, "L2");
            factory.Connect(power, "L3", breaker, "L3");
            factory.Connect(breaker, "T1", motor, "U");
            factory.Connect(breaker, "T2", motor, "V");
            factory.Connect(breaker, "T3", motor, "W");
            factory.Connect(power, "L1", breaker, "T1");

            AssertHasRuleId(factory.Validate(), "BREAKER_OR_FUSE_BYPASSED");
        }

        private static void TestFuseBypassed()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var fuse = factory.CreateComponent("fu", "Fuse_3P", ComponentKind.Fuse, "L1", "L2", "L3", "T1", "T2", "T3");
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            factory.SetTogglable(fuse, true);
            factory.SetClosed(fuse, false);

            factory.Connect(power, "L1", fuse, "L1");
            factory.Connect(power, "L2", fuse, "L2");
            factory.Connect(power, "L3", fuse, "L3");
            factory.Connect(fuse, "T1", motor, "U");
            factory.Connect(fuse, "T2", motor, "V");
            factory.Connect(fuse, "T3", motor, "W");
            factory.Connect(power, "L2", fuse, "T2");

            AssertHasRuleId(factory.Validate(), "BREAKER_OR_FUSE_BYPASSED");
        }

        private static void TestNoFalseNormalBreaker()
        {
            var factory = new CircuitTestFactory();
            var power = SinglePhasePower(factory);
            var breaker = factory.CreateComponent("qf", "Breaker_1P", ComponentKind.Breaker, "L1", "T1");
            var lamp = factory.CreateComponent("lamp", "Lamp", ComponentKind.Lamp, "L", "N");
            factory.SetTogglable(breaker, true);
            factory.SetClosed(breaker, true);

            factory.Connect(power, "L", breaker, "L1");
            factory.Connect(breaker, "T1", lamp, "L");
            factory.Connect(power, "N", lamp, "N");

            AssertDoesNotHaveRuleId(factory.Validate(), "BREAKER_OR_FUSE_BYPASSED");
        }

        private static void TestMotorBypassedDirectSupply()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            factory.CreateComponent("km", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");

            factory.Connect(power, "L1", motor, "U");
            factory.Connect(power, "L2", motor, "V");
            factory.Connect(power, "L3", motor, "W");

            AssertHasRuleId(factory.Validate(), "MOTOR_CONTACTOR_BYPASSED");
        }

        private static void TestNoFalseMotorThroughContactor()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var km = factory.CreateComponent("km", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");

            factory.Connect(power, "L1", km, "L1");
            factory.Connect(power, "L2", km, "L2");
            factory.Connect(power, "L3", km, "L3");
            factory.Connect(km, "T1", motor, "U");
            factory.Connect(km, "T2", motor, "V");
            factory.Connect(km, "T3", motor, "W");

            AssertDoesNotHaveRuleId(factory.Validate(), "MOTOR_CONTACTOR_BYPASSED");
        }

        private static void TestNoFalseThermalRelayPath()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var km = factory.CreateComponent("km", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");
            var fr = factory.CreateComponent("fr", "ThermalRelay_FR_380V", ComponentKind.Switch, "L1", "L2", "L3", "T1", "T2", "T3", "95", "96");

            factory.Connect(power, "L1", km, "L1");
            factory.Connect(power, "L2", km, "L2");
            factory.Connect(power, "L3", km, "L3");
            factory.Connect(km, "T1", fr, "L1");
            factory.Connect(km, "T2", fr, "L2");
            factory.Connect(km, "T3", fr, "L3");
            factory.Connect(fr, "T1", motor, "U");
            factory.Connect(fr, "T2", motor, "V");
            factory.Connect(fr, "T3", motor, "W");

            AssertDoesNotHaveRuleId(factory.Validate(), "MOTOR_CONTACTOR_BYPASSED");
        }

        private static void TestNoFalseStarDeltaMotor()
        {
            var factory = new CircuitTestFactory();
            var power = ThreePhasePower(factory);
            var motor = factory.CreateComponent("m", "Motor_StarDelta_380V", ComponentKind.Motor, "U1", "V1", "W1", "U2", "V2", "W2");
            factory.CreateComponent("km", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2");

            factory.Connect(power, "L1", motor, "U1");
            factory.Connect(power, "L2", motor, "V1");
            factory.Connect(power, "L3", motor, "W1");

            AssertDoesNotHaveRuleId(factory.Validate(), "MOTOR_CONTACTOR_BYPASSED");
        }

        private static void TestReversingInterlockMissing()
        {
            var factory = new CircuitTestFactory();
            BuildReversingPair(factory, false, false);
            var report = factory.Validate();
            AssertHasRuleId(report, "REVERSING_INTERLOCK_MISSING");
            AssertSeverity(report, "REVERSING_INTERLOCK_MISSING", CircuitValidationSeverity.Warning);
        }

        private static void TestNoFalseElectricalInterlock()
        {
            var factory = new CircuitTestFactory();
            BuildReversingPair(factory, true, false);
            AssertDoesNotHaveRuleId(factory.Validate(), "REVERSING_INTERLOCK_MISSING");
        }

        private static void TestNoFalseDoubleInterlock()
        {
            var factory = new CircuitTestFactory();
            BuildReversingPair(factory, true, true);
            AssertDoesNotHaveRuleId(factory.Validate(), "REVERSING_INTERLOCK_MISSING");
        }

        private static void TestNoFalseTwoMotorSequence()
        {
            var factory = new CircuitTestFactory();
            var motor1 = factory.CreateComponent("m1", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var motor2 = factory.CreateComponent("m2", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var km1 = factory.CreateComponent("km1", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "21", "22");
            var km2 = factory.CreateComponent("km2", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "21", "22");

            factory.Connect(km1, "T1", motor1, "U");
            factory.Connect(km1, "T2", motor1, "V");
            factory.Connect(km1, "T3", motor1, "W");
            factory.Connect(km2, "T1", motor2, "U");
            factory.Connect(km2, "T2", motor2, "V");
            factory.Connect(km2, "T3", motor2, "W");

            AssertDoesNotHaveRuleId(factory.Validate(), "REVERSING_INTERLOCK_MISSING");
        }

        private static void TestNoFalseStarDeltaReversingRule()
        {
            var factory = new CircuitTestFactory();
            var motor = factory.CreateComponent("m", "Motor_StarDelta_380V", ComponentKind.Motor, "U1", "V1", "W1", "U2", "V2", "W2");
            var km = factory.CreateComponent("km_main", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "21", "22");
            factory.Connect(km, "T1", motor, "U1");
            factory.Connect(km, "T2", motor, "V1");
            factory.Connect(km, "T3", motor, "W1");
            AssertDoesNotHaveRuleId(factory.Validate(), "REVERSING_INTERLOCK_MISSING");
        }

        private static void BuildReversingPair(CircuitTestFactory factory, bool electricalInterlock, bool buttonInterlock)
        {
            var motor = factory.CreateComponent("m", "Motor_ThreePhase_380V", ComponentKind.Motor, "U", "V", "W");
            var kmForward = factory.CreateComponent("km_forward", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "21", "22");
            var kmReverse = factory.CreateComponent("km_reverse", "Contactor_KM_380V", ComponentKind.ContactorCoil, "L1", "L2", "L3", "T1", "T2", "T3", "A1", "A2", "21", "22");

            factory.Connect(kmForward, "T1", motor, "U");
            factory.Connect(kmForward, "T2", motor, "V");
            factory.Connect(kmForward, "T3", motor, "W");
            factory.Connect(kmReverse, "T1", motor, "W");
            factory.Connect(kmReverse, "T2", motor, "V");
            factory.Connect(kmReverse, "T3", motor, "U");

            if (electricalInterlock)
            {
                factory.Connect(kmReverse, "21", kmForward, "A1");
                factory.Connect(kmForward, "21", kmReverse, "A1");
            }

            if (buttonInterlock)
            {
                var forwardButton = factory.CreateComponent("sb_forward", "Button_Compound_SB", ComponentKind.PushButton, "11", "12", "23", "24");
                var reverseButton = factory.CreateComponent("sb_reverse", "Button_Compound_SB", ComponentKind.PushButton, "11", "12", "23", "24");
                factory.Connect(forwardButton, "11", kmReverse, "A1");
                factory.Connect(reverseButton, "11", kmForward, "A1");
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

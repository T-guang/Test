using System;
using UnityEditor;
using UnityEngine;
using ElectricalSim.Core;

namespace ElectricalSim.Editor
{
    public static class MotorPhaseSequenceEvaluatorTests
    {
        [MenuItem("Tools/Tests/Run Motor Phase Sequence Evaluator Tests")]
        public static void RunTests()
        {
            var passed = 0;
            var total = 10;

            Run("Forward L1/L2/L3", MotorDirectionState.Forward, new[] { "L1" }, new[] { "L2" }, new[] { "L3" }, ref passed);
            Run("Forward L2/L3/L1", MotorDirectionState.Forward, new[] { "L2" }, new[] { "L3" }, new[] { "L1" }, ref passed);
            Run("Forward L3/L1/L2", MotorDirectionState.Forward, new[] { "L3" }, new[] { "L1" }, new[] { "L2" }, ref passed);
            Run("Reverse L1/L3/L2", MotorDirectionState.Reverse, new[] { "L1" }, new[] { "L3" }, new[] { "L2" }, ref passed);
            Run("Reverse L3/L2/L1", MotorDirectionState.Reverse, new[] { "L3" }, new[] { "L2" }, new[] { "L1" }, ref passed);
            Run("Reverse L2/L1/L3", MotorDirectionState.Reverse, new[] { "L2" }, new[] { "L1" }, new[] { "L3" }, ref passed);
            Run("Missing phase", MotorDirectionState.Stopped, new[] { "L1" }, new[] { "L2" }, Array.Empty<string>(), ref passed);
            Run("Duplicate phase", MotorDirectionState.Invalid, new[] { "L1" }, new[] { "L1" }, new[] { "L3" }, ref passed);
            Run("Multiple source", MotorDirectionState.Invalid, new[] { "L1", "L2" }, new[] { "L2" }, new[] { "L3" }, ref passed);
            Run("Unknown source", MotorDirectionState.Unknown, new[] { "L" }, new[] { "L2" }, new[] { "L3" }, ref passed);

            Debug.Log($"Motor phase sequence evaluator tests: {passed}/{total} passed.");
            if (passed != total)
            {
                throw new InvalidOperationException("Motor phase sequence evaluator tests failed.");
            }
        }

        private static void Run(
            string name,
            MotorDirectionState expected,
            string[] u,
            string[] v,
            string[] w,
            ref int passed)
        {
            var result = MotorPhaseSequenceEvaluator.Evaluate(u, v, w);
            if (result.Direction != expected)
            {
                throw new InvalidOperationException($"{name}: expected {expected}, got {result.Direction}.");
            }

            passed++;
        }
    }
}

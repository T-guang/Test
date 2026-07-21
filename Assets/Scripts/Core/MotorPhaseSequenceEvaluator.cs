using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim.Core
{
    public enum MotorDirectionState
    {
        Stopped,
        Forward,
        Reverse,
        Invalid,
        Unknown
    }

    public sealed class MotorDirectionResult
    {
        public MotorDirectionState Direction;
        public string PhaseAtU;
        public string PhaseAtV;
        public string PhaseAtW;
        public string Reason;

        public bool IsRunning => Direction == MotorDirectionState.Forward || Direction == MotorDirectionState.Reverse;
    }

    /// <summary>
    /// Evaluates the phase sequence at an ordinary three-phase motor's U/V/W inputs.
    /// The caller supplies phase sources reachable through the current dynamic topology.
    /// </summary>
    public static class MotorPhaseSequenceEvaluator
    {
        public static MotorDirectionResult Evaluate(
            IEnumerable<string> phasesAtU,
            IEnumerable<string> phasesAtV,
            IEnumerable<string> phasesAtW)
        {
            var u = Normalize(phasesAtU);
            var v = Normalize(phasesAtV);
            var w = Normalize(phasesAtW);

            var result = new MotorDirectionResult
            {
                PhaseAtU = Display(u),
                PhaseAtV = Display(v),
                PhaseAtW = Display(w)
            };

            if (u.Count == 0 || v.Count == 0 || w.Count == 0)
            {
                result.Direction = MotorDirectionState.Stopped;
                result.Reason = "电机未获得完整三相供电。";
                return result;
            }

            if (u.Count > 1 || v.Count > 1 || w.Count > 1)
            {
                result.Direction = MotorDirectionState.Invalid;
                result.Reason = "电机输入端同时可达多个相源。";
                return result;
            }

            var phaseU = u[0];
            var phaseV = v[0];
            var phaseW = w[0];
            if (!IsKnownThreePhase(phaseU) || !IsKnownThreePhase(phaseV) || !IsKnownThreePhase(phaseW))
            {
                result.Direction = MotorDirectionState.Unknown;
                result.Reason = "电机输入端未获得可识别的 L1/L2/L3 相源。";
                return result;
            }

            if (phaseU == phaseV || phaseV == phaseW || phaseU == phaseW)
            {
                result.Direction = MotorDirectionState.Invalid;
                result.Reason = "电机三相输入存在重复相。";
                return result;
            }

            if ((phaseU == TerminalConstants.L1 && phaseV == TerminalConstants.L2 && phaseW == TerminalConstants.L3) ||
                (phaseU == TerminalConstants.L2 && phaseV == TerminalConstants.L3 && phaseW == TerminalConstants.L1) ||
                (phaseU == TerminalConstants.L3 && phaseV == TerminalConstants.L1 && phaseW == TerminalConstants.L2))
            {
                result.Direction = MotorDirectionState.Forward;
                result.Reason = "电机获得正相序三相供电。";
                return result;
            }

            if ((phaseU == TerminalConstants.L1 && phaseV == TerminalConstants.L3 && phaseW == TerminalConstants.L2) ||
                (phaseU == TerminalConstants.L3 && phaseV == TerminalConstants.L2 && phaseW == TerminalConstants.L1) ||
                (phaseU == TerminalConstants.L2 && phaseV == TerminalConstants.L1 && phaseW == TerminalConstants.L3))
            {
                result.Direction = MotorDirectionState.Reverse;
                result.Reason = "电机获得反相序三相供电。";
                return result;
            }

            result.Direction = MotorDirectionState.Unknown;
            result.Reason = "当前相序无法识别。";
            return result;
        }

        private static List<string> Normalize(IEnumerable<string> phases)
        {
            return phases == null
                ? new List<string>()
                : phases.Where(phase => !string.IsNullOrWhiteSpace(phase))
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .OrderBy(phase => phase, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        private static string Display(IReadOnlyList<string> phases)
        {
            return phases == null || phases.Count == 0 ? string.Empty : string.Join("/", phases);
        }

        private static bool IsKnownThreePhase(string phase)
        {
            return phase == TerminalConstants.L1 || phase == TerminalConstants.L2 || phase == TerminalConstants.L3;
        }
    }
}

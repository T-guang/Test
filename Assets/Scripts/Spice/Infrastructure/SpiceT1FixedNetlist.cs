using System;

namespace ElectricalSim.Spice.Infrastructure
{
    public static class SpiceT1FixedNetlist
    {
        public const double ExpectedInputVoltage = 10d;
        public const double ExpectedV1Current = -0.01d;
        public const double VoltageTolerance = 1e-6d;
        public const double CurrentTolerance = 1e-8d;

        public const string Content =
            "* ElectricalSimulation2D SPICE T1\n" +
            "V1 input 0 DC 10\n" +
            "R1 input 0 1000\n\n" +
            ".control\n" +
            "set noaskquit\n" +
            "op\n" +
            "echo __SPICE_T1_BEGIN__\n" +
            "print v(input)\n" +
            "print i(v1)\n" +
            "echo __SPICE_T1_END__\n" +
            "quit\n" +
            ".endc\n\n" +
            ".end\n";

        public static bool TryValidate(NgspiceRunResult result, out string failure)
        {
            failure = null;
            if (result == null)
            {
                failure = "ngspice did not return a result.";
                return false;
            }

            if (!result.Success)
            {
                failure = result.FailureCode + ": " + result.FailureMessage;
                return false;
            }

            if (!result.NodeVoltages.TryGetValue("input", out var inputVoltage))
            {
                failure = "Missing v(input).";
                return false;
            }

            if (!result.BranchCurrents.TryGetValue("V1", out var v1Current))
            {
                failure = "Missing i(v1).";
                return false;
            }

            if (Math.Abs(inputVoltage - ExpectedInputVoltage) > VoltageTolerance)
            {
                failure = "v(input) expected " + ExpectedInputVoltage + ", actual " + inputVoltage + ".";
                return false;
            }

            if (Math.Abs(v1Current - ExpectedV1Current) > CurrentTolerance)
            {
                failure = "i(v1) expected " + ExpectedV1Current + ", actual " + v1Current + ".";
                return false;
            }

            return true;
        }
    }
}
